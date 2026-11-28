using FrameCore.BackupManager;
using FrameCore.BenchmarkManager;
using FrameCore.Core;
using FrameCore.Core.Elevation;
using FrameCore.Core.Engine;
using FrameCore.Core.Models;
using FrameCore.DriverManager;
using FrameCore.GpuManager;
using FrameCore.HardwareDetection;
using FrameCore.StorageManager;

namespace FrameCore.UI.Services;

/// <summary>
/// Entry point of "FrameCore.exe --worker &lt;pipe&gt; &lt;parentPid&gt;" (elevated). Only the operations registered here exist;
/// catalog actions are re-validated against the catalog shipped in the installation folder.
/// </summary>
public static class WorkerProgram
{
    public static int Run(string pipeName, int parentPid)
    {
        var services = new AppServices(AppSettings.Load(), forWorker: true);
        // The operation checked is the one of the route called, never the one written in the request.
        var validator = new PrivilegedRequestValidator(services.Catalog);
        var dispatcher = new LocalDispatcher(services.Executors);
        var host = new WorkerHost();
        using var sensors = new SensorService();
        GpuTuningService? gpu = null;
        GpuTuningService Gpu() => gpu ??= new GpuTuningService();

        host.Register<object>("ping", _ => true);
        host.Register<PrivilegedActionRequest>("action.read", r => { validator.Validate(r with { Op = "read" }); return dispatcher.Read(r.Action, r.Context); });
        host.Register<PrivilegedActionRequest>("action.capture", r => { validator.Validate(r with { Op = "capture" }); return dispatcher.Capture(r.Action, r.Context); });
        host.Register<PrivilegedActionRequest>("action.apply", r => { validator.Validate(r with { Op = "apply" }); dispatcher.Apply(r.Action, r.Context); return true; });
        host.Register<PrivilegedActionRequest>("action.restore", r =>
        {
            validator.Validate(r with { Op = "restore" });
            dispatcher.Restore(r.Action, r.Snapshot ?? throw new ArgumentException("snapshot"), r.Context);
            return true;
        });
        host.Register<PrivilegedActionRequest>("action.verify", r => { validator.Validate(r with { Op = "verify" }); return dispatcher.Verify(r.Action, r.Snapshot ?? StateSnapshot.Absent(), r.Context); });

        host.Register<object>("sensors.read", _ => sensors.Read());
        host.Register<string>("restorepoint.create", d => RestorePointService.Create(d.Length > 60 ? d[..60] : d));
        host.Register<object>("gpu.caps", _ => Gpu().Capabilities().ToList());
        host.Register<GpuTuningRequest>("gpu.read", r => Gpu().Read(r.Vendor, r.State.Index));
        host.Register<GpuTuningRequest>("gpu.apply", r => { Gpu().Apply(r.Vendor, r.State); return true; });
        host.Register<GpuTuningRequest>("gpu.reset", r => { Gpu().Reset(r.Vendor, r.State.Index); return true; });
        host.Register<GpuTuningRequest>("gpu.preset", r => Gpu().RunPreset(r.Vendor, r.Preset ?? throw new ArgumentException("preset")));
        host.Register<List<string>>("wu.install", ids => WindowsUpdateDrivers.Install(ids));
        host.Register<CaptureRequest>("bench.etw", r => EtwPresentCapture.Capture(r));
        host.Register<LatencyRequest>("detective.latency", r => FrameCore.BenchmarkManager.Detective.LatencyProbe.Capture(Math.Clamp(r.Seconds, 5, 120)));
        host.Register<PresentMonRequest>("bench.presentmon", r => PresentMonRunner.Capture(r.Path, r.Request)); // signature verified inside
        host.Register<object>("storage.scan", _ => new StorageScanner(services.Registry).Scan());
        host.Register<object>("boot.history", _ => StartupManager.BootPerformance.Read()); // read-only

        try { return host.Run(pipeName, parentPid); }
        finally { gpu?.Dispose(); }
    }
}
