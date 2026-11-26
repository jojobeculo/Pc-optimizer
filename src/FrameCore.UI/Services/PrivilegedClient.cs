using FrameCore.BackupManager;
using FrameCore.BenchmarkManager;
using FrameCore.Core;
using FrameCore.Core.Elevation;
using FrameCore.Core.Hardware;
using FrameCore.DriverManager;
using FrameCore.GpuManager;
using FrameCore.HardwareDetection;
using FrameCore.StorageManager;

namespace FrameCore.UI.Services;

/// <summary>Typed calls to the elevated worker (or direct calls when the UI itself runs elevated).</summary>
public sealed class PrivilegedClient
{
    private readonly ElevationBroker _broker;
    private readonly AppServices _services;
    private GpuTuningService? _localGpu;
    private SensorService? _localSensors;

    public PrivilegedClient(ElevationBroker broker, AppServices services) { _broker = broker; _services = services; }

    public bool LocalElevated => AppInfo.IsElevated;
    public bool IsConnected => LocalElevated || _broker.IsConnected;

    private T Call<T>(string op, object payload, Func<T> local, TimeSpan? timeout = null) =>
        LocalElevated ? local() : _broker.Send<T>(op, payload, timeout);

    public bool Ensure() => LocalElevated || _broker.EnsureStarted();

    public SensorSnapshot ReadSensors() => Call("sensors.read", new { }, () => (_localSensors ??= new SensorService()).Read());

    public RestorePointResult CreateRestorePoint(string description) =>
        Call("restorepoint.create", description, () => RestorePointService.Create(description), TimeSpan.FromMinutes(3));

    public List<GpuTuningCapabilities> GpuCapabilities() => Call("gpu.caps", new { }, () => Gpu().Capabilities().ToList());
    public GpuTuningState GpuRead(string vendor, int index) => Call("gpu.read", new GpuTuningRequest { Vendor = vendor, State = new GpuTuningState { Index = index } }, () => Gpu().Read(vendor, index));
    public bool GpuApply(string vendor, GpuTuningState state) => Call("gpu.apply", new GpuTuningRequest { Vendor = vendor, State = state }, () => { Gpu().Apply(vendor, state); return true; });
    public bool GpuReset(string vendor, int index) => Call("gpu.reset", new GpuTuningRequest { Vendor = vendor, State = new GpuTuningState { Index = index } }, () => { Gpu().Reset(vendor, index); return true; });
    public bool GpuPreset(string vendor, GpuPresetRequest preset) =>
        Call("gpu.preset", new GpuTuningRequest { Vendor = vendor, Preset = preset }, () => Gpu().RunPreset(vendor, preset), TimeSpan.FromMinutes(12));

    public DriverInstallResult InstallDriverUpdates(List<string> ids) =>
        Call("wu.install", ids, () => WindowsUpdateDrivers.Install(ids), TimeSpan.FromMinutes(45));

    public CaptureResult CaptureEtw(CaptureRequest r) =>
        Call("bench.etw", r, () => EtwPresentCapture.Capture(r), TimeSpan.FromSeconds(r.DurationSeconds + 60));

    public CaptureResult CapturePresentMon(string path, CaptureRequest r) =>
        Call("bench.presentmon", new PresentMonRequest { Path = path, Request = r }, () => PresentMonRunner.Capture(path, r), TimeSpan.FromSeconds(r.DurationSeconds + 90));

    public List<StorageDevice> StorageWithReliability() =>
        Call("storage.scan", new { }, () => new StorageScanner(_services.Registry).Scan());

    private GpuTuningService Gpu() => _localGpu ??= new GpuTuningService();
}

public sealed class PresentMonRequest
{
    public string Path { get; set; } = "";
    public CaptureRequest Request { get; set; } = new();
}
