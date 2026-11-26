using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameCore.Core;
using FrameCore.Core.Platform;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FrameCore.GpuManager;

/// <summary>
/// Vendor-neutral GPU validation: a deterministic compute shader is run repeatedly and its output compared with
/// the first run (any difference = computation error), plus large VRAM copies verified against a known pattern
/// (bandwidth also measured). Driver resets (TDR, event 4101) during the run are reported as failures.
/// </summary>
public sealed class D3D11StabilityTester : IGpuStabilityTester
{
    private const string Shader = @"
RWStructuredBuffer<uint> Output : register(u0);
[numthreads(256, 1, 1)]
void ComputeMain(uint3 id : SV_DispatchThreadID)
{
    uint x = id.x * 2654435761u + 1u;
    float f = 1.0f + (id.x & 255) / 256.0f;
    uint acc = 2166136261u;
    [loop] for (uint i = 0; i < 4096; i++)
    {
        x ^= x << 13; x ^= x >> 17; x ^= x << 5;
        acc = (acc ^ x) * 16777619u;
        f = mad(f, 1.0000001f, (x & 0xFFFF) * 1e-9f);
        if (f > 1e6f) f -= 1e6f;
    }
    Output[id.x] = acc ^ asuint(f);
}";

    private readonly long? _luid;
    private readonly Func<double?>? _temperature;
    public int ComputeElements { get; set; } = 1 << 20;
    public int VramMegabytes { get; set; } = 256;

    public D3D11StabilityTester(long? adapterLuid, Func<double?>? temperatureProbe = null)
    {
        _luid = adapterLuid;
        _temperature = temperatureProbe;
    }

    public GpuStabilityResult Run(TimeSpan duration, CancellationToken ct)
    {
        var result = new GpuStabilityResult();
        var started = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();
        IDXGIFactory1? factory = null; IDXGIAdapter1? adapter = null;
        ID3D11Device? device = null; ID3D11DeviceContext? ctx = null;
        try
        {
            factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; factory.EnumAdapters1(i, out var a).Success; i++)
            {
                var d = a.Description1;
                var luid = ((long)d.Luid.HighPart << 32) | d.Luid.LowPart;
                if (_luid == null ? (d.Flags & AdapterFlags.Software) == 0 : luid == _luid) { adapter = a; break; }
                a.Dispose();
            }
            if (adapter == null) { result.Error = "GPU adapter not found"; return result; }
            var hr = D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, new[] { FeatureLevel.Level_11_0 }, out device, out ctx);
            if (hr.Failure || device == null || ctx == null) { result.Error = "D3D11CreateDevice failed: " + hr; return result; }

            var bytecode = Compiler.Compile(Shader, "ComputeMain", "framecore_stress.hlsl", "cs_5_0", ShaderFlags.OptimizationLevel3, EffectFlags.None);
            using var cs = device.CreateComputeShader(bytecode.Span);
            var n = (uint)ComputeElements;
            using var output = device.CreateBuffer(new BufferDescription(n * 4, BindFlags.UnorderedAccess, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
            using var uav = device.CreateUnorderedAccessView(output, new UnorderedAccessViewDescription(output, Format.Unknown, 0, n, BufferUnorderedAccessViewFlags.None));
            using var staging = device.CreateBuffer(new BufferDescription(n * 4, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, ResourceOptionFlags.None, 0));

            // VRAM copy test buffers with a known pattern.
            var vramBytes = (uint)VramMegabytes * 1024 * 1024;
            var words = (int)(vramBytes / 4);
            var pattern = new uint[words];
            for (var i = 0; i < words; i++) pattern[i] = (uint)i * 2654435761u ^ 0xA5A5A5A5u;
            using var src = device.CreateBuffer(pattern.AsSpan(), new BufferDescription(vramBytes, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
            using var dst = device.CreateBuffer(new BufferDescription(vramBytes, BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
            using var vramStaging = device.CreateBuffer(new BufferDescription(vramBytes, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read, ResourceOptionFlags.None, 0));

            ulong? reference = null;
            long copies = 0; double copySeconds = 0; double computeSeconds = 0;
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                // Compute pass
                var t0 = Stopwatch.GetTimestamp();
                ctx.CSSetShader(cs);
                ctx.CSSetUnorderedAccessView(0, uav, 0);
                ctx.Dispatch(n / 256, 1, 1);
                ctx.CopyResource(staging, output);
                var checksum = ReadChecksum(ctx, staging, (int)n);
                computeSeconds += Stopwatch.GetElapsedTime(t0).TotalSeconds;
                if (reference == null) reference = checksum;
                else if (checksum != reference) result.ComputeErrors++;
                result.Iterations++;

                // VRAM pass every 4 iterations
                if (result.Iterations % 4 == 0)
                {
                    var t1 = Stopwatch.GetTimestamp();
                    for (var k = 0; k < 8; k++) { ctx.CopyResource(dst, src); copies++; }
                    ctx.CopyResource(vramStaging, dst);
                    var mismatches = VerifyPattern(ctx, vramStaging, words);
                    copySeconds += Stopwatch.GetElapsedTime(t1).TotalSeconds;
                    result.MemoryErrors += mismatches;
                }
                var temp = _temperature?.Invoke();
                if (temp != null) result.MaxTemperatureC = Math.Max(result.MaxTemperatureC ?? 0, temp.Value);
            }
            result.ComputeScore = computeSeconds > 0 ? result.Iterations / computeSeconds : 0;
            result.BandwidthGBs = copySeconds > 0 ? copies * (double)vramBytes / copySeconds / 1e9 : 0;
        }
        catch (SharpGen.Runtime.SharpGenException ex) when ((uint)ex.HResult is 0x887A0005 or 0x887A0006 or 0x887A0007)
        {
            result.DeviceLost = true;
            result.Error = "GPU device removed/reset (0x" + ((uint)ex.HResult).ToString("X") + ")";
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Log.Error("GpuStress", "Stability test failure", ex);
        }
        finally
        {
            ctx?.ClearState();
            ctx?.Dispose(); device?.Dispose(); adapter?.Dispose(); factory?.Dispose();
            result.Duration = sw.Elapsed;
        }
        // Display driver resets (TDR) logged by Windows during the run.
        result.DriverReset = EventLogQuery.Since("System", "Display", started, new[] { 4101 }).Count > 0;
        return result;
    }

    private static unsafe ulong ReadChecksum(ID3D11DeviceContext ctx, ID3D11Buffer staging, int count)
    {
        var map = ctx.Map(staging, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var p = (uint*)map.DataPointer;
            ulong h = 1469598103934665603UL;
            for (var i = 0; i < count; i++) h = (h ^ p[i]) * 1099511628211UL;
            return h;
        }
        finally { ctx.Unmap(staging); }
    }

    private static unsafe long VerifyPattern(ID3D11DeviceContext ctx, ID3D11Buffer staging, int words)
    {
        var map = ctx.Map(staging, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var p = (uint*)map.DataPointer;
            long bad = 0;
            for (var i = 0; i < words; i++) if (p[i] != ((uint)i * 2654435761u ^ 0xA5A5A5A5u)) bad++;
            return bad;
        }
        finally { ctx.Unmap(staging); }
    }
}
