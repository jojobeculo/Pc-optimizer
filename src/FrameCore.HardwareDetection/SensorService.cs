using FrameCore.Core;
using LibreHardwareMonitor.Hardware;

namespace FrameCore.HardwareDetection;

public sealed class SensorReading
{
    public string Hardware { get; set; } = "";
    public string HardwareType { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public float? Value { get; set; }
    public float? Max { get; set; }
}

public sealed class SensorSnapshot
{
    public bool PawnIoAvailable { get; set; }
    public bool Elevated { get; set; }
    public List<SensorReading> Readings { get; set; } = new();
    public string? Error { get; set; }

    public float? Find(string hardwareType, string sensorType, params string[] names)
    {
        foreach (var n in names)
        {
            var r = Readings.FirstOrDefault(x => x.HardwareType == hardwareType && x.Type == sensorType && x.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && x.Value != null);
            if (r != null) return r.Value;
        }
        return null;
    }

    public float? FindContains(string hardwareType, string sensorType, string fragment) =>
        Readings.FirstOrDefault(x => x.HardwareType == hardwareType && x.Type == sensorType && x.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase) && x.Value != null)?.Value;
}

/// <summary>
/// LibreHardwareMonitor 0.9.6 wrapper. CPU/motherboard/SPD sensors need the separately-installed, signed
/// PawnIO driver and administrator rights; FrameCore never installs a kernel driver itself.
/// Hosted in the elevated worker when "advanced sensors" are enabled; GPU/storage sensors also work without it.
/// </summary>
public sealed class SensorService : IDisposable
{
    private Computer? _computer;
    private readonly object _gate = new();

    public static bool PawnIoInstalled =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "drivers", "PawnIO.sys"))
        || new Core.Platform.WindowsRegistryAccess().KeyExists(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");

    public SensorSnapshot Read()
    {
        lock (_gate)
        {
            var snap = new SensorSnapshot { PawnIoAvailable = PawnIoInstalled, Elevated = AppInfo.IsElevated };
            try
            {
                if (_computer == null)
                {
                    _computer = new Computer
                    {
                        IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true,
                        IsStorageEnabled = true, IsControllerEnabled = false, IsNetworkEnabled = false, IsPsuEnabled = false,
                    };
                    _computer.Open();
                }
                foreach (var hw in _computer.Hardware)
                {
                    Collect(hw, snap.Readings);
                    foreach (var sub in hw.SubHardware) Collect(sub, snap.Readings);
                }
            }
            catch (Exception ex)
            {
                snap.Error = ex.Message;
                Log.Error("Sensors", "LibreHardwareMonitor failure", ex);
            }
            return snap;
        }
    }

    private static void Collect(IHardware hw, List<SensorReading> into)
    {
        try { hw.Update(); } catch { return; }
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType is not (SensorType.Temperature or SensorType.Clock or SensorType.Power or SensorType.Voltage
                or SensorType.Load or SensorType.Fan or SensorType.SmallData or SensorType.Data or SensorType.Control)) continue;
            into.Add(new SensorReading
            {
                Hardware = hw.Name, HardwareType = hw.HardwareType.ToString(), Name = s.Name, Type = s.SensorType.ToString(),
                Value = s.Value is { } v && !float.IsNaN(v) ? v : null, Max = s.Max,
            });
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _computer?.Close(); } catch { }
            _computer = null;
        }
    }
}
