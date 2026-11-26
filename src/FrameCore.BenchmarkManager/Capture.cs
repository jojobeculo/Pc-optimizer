using System.Diagnostics;
using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.Core.Platform;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace FrameCore.BenchmarkManager;

public sealed class CaptureRequest
{
    public int ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public int DurationSeconds { get; set; } = 60;
}

public sealed class CaptureResult
{
    public List<double> FrameTimesMs { get; set; } = new();
    public string? Runtime { get; set; }
    public string? Error { get; set; }
    public int Events { get; set; }
}

/// <summary>
/// Built-in frame capture through ETW (requires administrator rights → runs in the elevated worker).
/// Consumes Present_Start events of the DXGI (Direct3D 10/11/12) and D3D9 runtimes for the target process and
/// computes the interval between presents (equivalent to PresentMon's MsBetweenPresents).
/// Vulkan/OpenGL titles are not covered: use PresentMon for them.
/// </summary>
public static class EtwPresentCapture
{
    public static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    public static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const int DxgiPresentStart = 42, DxgiPresentMpoStart = 55, D3D9PresentStart = 1;

    public static CaptureResult Capture(CaptureRequest request, CancellationToken ct = default)
    {
        var result = new CaptureResult();
        if (!AppInfo.IsElevated) { result.Error = Loc.T("bench.etw.needsAdmin"); return result; }
        var timestamps = new List<double>();
        string? runtime = null;
        var name = "FrameCore-Present-" + Environment.ProcessId;
        try
        {
            using var session = new TraceEventSession(name) { StopOnDispose = true };
            session.EnableProvider(DxgiProvider, TraceEventLevel.Verbose, ulong.MaxValue);
            session.EnableProvider(D3D9Provider, TraceEventLevel.Verbose, ulong.MaxValue);
            session.Source.AllEvents += e =>
            {
                if (e.ProcessID != request.ProcessId) return;
                var id = (int)e.ID;
                if (e.ProviderGuid == DxgiProvider && id is DxgiPresentStart or DxgiPresentMpoStart) { lock (timestamps) timestamps.Add(e.TimeStampRelativeMSec); runtime ??= "DXGI"; }
                else if (e.ProviderGuid == D3D9Provider && id == D3D9PresentStart) { lock (timestamps) timestamps.Add(e.TimeStampRelativeMSec); runtime ??= "D3D9"; }
            };
            var pump = Task.Run(() => session.Source.Process());
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < request.DurationSeconds && !ct.IsCancellationRequested)
            {
                Thread.Sleep(250);
                try { if (Process.GetProcessById(request.ProcessId).HasExited) break; } catch { break; }
            }
            session.Stop();
            pump.Wait(5000);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            Log.Error("ETW", "Present capture failed", ex);
        }
        lock (timestamps)
        {
            timestamps.Sort();
            result.Events = timestamps.Count;
            result.FrameTimesMs = FrameStatistics.FromTimestamps(timestamps);
        }
        result.Runtime = runtime;
        if (result.Error == null && result.Events < 2) result.Error = Loc.T("bench.etw.noFrames");
        return result;
    }
}

/// <summary>
/// Drives the official Intel PresentMon console application when the user has installed it and configured its path.
/// The executable is only started after its Authenticode signature (Intel) has been verified.
/// </summary>
public static class PresentMonRunner
{
    public static (bool ok, string? reason) Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return (false, Loc.T("bench.pm.notFound"));
        var sig = Authenticode.Verify(path);
        if (!sig.Valid) return (false, Loc.T("bench.pm.badSignature", sig.Error));
        if (sig.Subject == null || !sig.Subject.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return (false, Loc.T("bench.pm.notIntel", sig.Subject));
        return (true, null);
    }

    public static CaptureResult Capture(string presentMonPath, CaptureRequest request, CancellationToken ct = default)
    {
        var result = new CaptureResult();
        var (ok, reason) = Validate(presentMonPath);
        if (!ok) { result.Error = reason; return result; }
        var csv = Path.Combine(Path.GetTempPath(), $"framecore-pm-{Guid.NewGuid():N}.csv");
        var psi = new ProcessStartInfo(presentMonPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var a in new[] { "--process_id", request.ProcessId.ToString(), "--output_file", csv, "--timed", request.DurationSeconds.ToString(),
                     "--terminate_after_timed", "--stop_existing_session", "--no_console_stats", "--session_name", "FrameCorePM" })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var deadline = DateTime.UtcNow.AddSeconds(request.DurationSeconds + 30);
            while (!p.HasExited && DateTime.UtcNow < deadline && !ct.IsCancellationRequested) Thread.Sleep(250);
            if (!p.HasExited) { try { p.Kill(); } catch { } }
            if (!File.Exists(csv)) { result.Error = Loc.T("bench.pm.noCsv", p.StandardError.ReadToEnd()); return result; }
            var captures = PresentMonCsvParser.ParseFile(csv);
            var main = captures.FirstOrDefault();
            if (main == null) { result.Error = Loc.T("bench.etw.noFrames"); return result; }
            result.FrameTimesMs = main.FrameTimesMs;
            result.Runtime = main.Runtime;
            result.Events = main.FrameTimesMs.Count;
        }
        catch (Exception ex) { result.Error = ex.Message; }
        finally { try { File.Delete(csv); } catch { } }
        return result;
    }
}
