using System.Globalization;
using FrameCore.Core;
using FrameCore.Core.Engine;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;

namespace FrameCore.DisplayManager;

/// <summary>Active displays: current/maximum refresh rate, connection, HDR, VRR (from EDID).</summary>
public sealed class DisplayScanner : IHardwareEnricher
{
    private readonly IRegistryAccess _reg;
    public DisplayScanner(IRegistryAccess reg) => _reg = reg;
    public string Name => Loc.T("scan.displays");

    public void Enrich(MachineProfile profile) => profile.Displays = Scan();

    public List<DisplayInfo> Scan()
    {
        var result = new List<DisplayInfo>();
        if (!OperatingSystem.IsWindows()) return result;
        var paths = DisplayNative.ActivePaths();
        var adapter = DisplayNative.DISPLAY_DEVICE.Create();
        for (uint i = 0; DisplayNative.EnumDisplayDevicesW(null, i, ref adapter, 0); i++, adapter = DisplayNative.DISPLAY_DEVICE.Create())
        {
            if ((adapter.StateFlags & DisplayNative.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
            var cur = DisplayNative.Current(adapter.DeviceName);
            if (cur == null) continue;
            var modes = DisplayNative.Modes(adapter.DeviceName).ToList();
            var path = paths.FirstOrDefault(p => p.GdiName.Equals(adapter.DeviceName, StringComparison.OrdinalIgnoreCase));
            var d = new DisplayInfo
            {
                DeviceName = adapter.DeviceName,
                AdapterName = adapter.DeviceString,
                IsPrimary = (adapter.StateFlags & DisplayNative.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                CurrentWidth = cur.Width,
                CurrentHeight = cur.Height,
                CurrentRefreshHz = path?.RefreshHz > 0 ? path.RefreshHz : cur.RefreshHz,
                MaxRefreshAtCurrentResolution = modes.Where(m => m.Width == cur.Width && m.Height == cur.Height).Select(m => (double?)m.RefreshHz).Max(),
                FriendlyName = path?.FriendlyName,
                Connection = path != null ? DisplayNative.Technology(path.OutputTechnology) : null,
                IsInternal = path != null && (path.OutputTechnology is 6 or 11 || path.OutputTechnology == unchecked((int)0x80000000)),
                HdrSupported = path?.AdvancedColorSupported,
                HdrEnabled = path?.AdvancedColorEnabled,
                BitsPerColor = path?.BitsPerColor is > 0 ? path.BitsPerColor : null,
            };

            var edid = ReadEdid(adapter.DeviceName, path?.MonitorDevicePath);
            if (edid is { Valid: true })
            {
                d.FriendlyName ??= edid.MonitorName;
                d.ManufacturerCode = edid.ManufacturerCode;
                d.NativeWidth = edid.PreferredWidth;
                d.NativeHeight = edid.PreferredHeight;
                d.EdidMaxRefreshHz = (int?)Math.Round(Math.Max(edid.MaxDetailedRefreshHz, edid.RangeMaxVHz ?? 0));
                d.AdaptiveSyncAdvertised = edid.AdaptiveSyncLikely;
                d.FreeSyncAdvertised = edid.FreeSync;
                d.HdmiVrrAdvertised = edid.HdmiForumVrr;
                d.VrrMinHz = edid.VrrMin;
                d.VrrMaxHz = edid.VrrMax;
                if (d.HdrSupported == null && edid.HdrStaticMetadata) d.HdrSupported = edid.HdrPq;
            }
            if (d.NativeWidth is { } nw && d.NativeHeight is { } nh)
                d.MaxRefreshAtNativeResolution = modes.Where(m => m.Width == nw && m.Height == nh).Select(m => (double?)m.RefreshHz).Max();

            // Link limitation: the monitor advertises more than Windows offers at native resolution.
            if (d.EdidMaxRefreshHz is { } edidMax && d.MaxRefreshAtNativeResolution is { } offered && edidMax > offered + 1 && edid?.RangeMaxVHz >= edidMax)
                d.LinkLimitNote = Loc.T("display.linkLimit", edidMax, offered, d.Connection ?? "?");
            result.Add(d);
        }
        return result;
    }

    /// <summary>EDID from HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\&lt;id&gt;\&lt;instance&gt;\Device Parameters\EDID.</summary>
    private EdidInfo? ReadEdid(string adapterDevice, string? monitorDevicePath)
    {
        try
        {
            var interfaceName = monitorDevicePath;
            if (string.IsNullOrEmpty(interfaceName))
            {
                var mon = DisplayNative.DISPLAY_DEVICE.Create();
                if (DisplayNative.EnumDisplayDevicesW(adapterDevice, 0, ref mon, 1)) interfaceName = mon.DeviceID;
            }
            if (string.IsNullOrEmpty(interfaceName)) return null;
            // \\?\DISPLAY#GSM5B7F#5&1a2b3c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}
            var parts = interfaceName.Replace(@"\\?\", "").Split('#');
            if (parts.Length < 3) return null;
            var key = $@"HKLM\SYSTEM\CurrentControlSet\Enum\{parts[0]}\{parts[1]}\{parts[2]}\Device Parameters";
            var v = _reg.GetValue(key, "EDID");
            return v?.Data is byte[] bytes ? EdidParser.Parse(bytes) : null;
        }
        catch (Exception ex) { Log.Warn("EDID", ex.Message); return null; }
    }
}

/// <summary>
/// Handler "display.maxRefresh": sets every active display to the highest refresh rate Windows offers at its
/// current resolution (tested with CDS_TEST first). Restore re-applies the exact previous mode.
/// </summary>
public sealed class MaxRefreshHandler : IActionHandler
{
    public string Name => "display.maxRefresh";
    public bool RequiresElevation(ActionDefinition a) => false;

    private static IEnumerable<(string device, DisplayMode current, DisplayMode best)> Candidates()
    {
        var adapter = DisplayNative.DISPLAY_DEVICE.Create();
        for (uint i = 0; DisplayNative.EnumDisplayDevicesW(null, i, ref adapter, 0); i++, adapter = DisplayNative.DISPLAY_DEVICE.Create())
        {
            if ((adapter.StateFlags & DisplayNative.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;
            var cur = DisplayNative.Current(adapter.DeviceName);
            if (cur == null) continue;
            var best = DisplayNative.Modes(adapter.DeviceName)
                .Where(m => m.Width == cur.Width && m.Height == cur.Height && m.BitsPerPixel == cur.BitsPerPixel)
                .OrderByDescending(m => m.RefreshHz).FirstOrDefault() ?? cur;
            yield return (adapter.DeviceName, cur, best);
        }
    }

    public ActionState Read(ActionDefinition a)
    {
        var list = Candidates().ToList();
        return new ActionState
        {
            Matches = list.All(c => c.current.RefreshHz >= c.best.RefreshHz),
            Current = string.Join(", ", list.Select(c => $"{c.device.Replace(@"\\.\", "")} {c.current.RefreshHz} Hz")),
            Target = string.Join(", ", list.Select(c => $"{c.device.Replace(@"\\.\", "")} {c.best.RefreshHz} Hz")),
        };
    }

    public StateSnapshot Capture(ActionDefinition a)
    {
        var s = StateSnapshot.Of(null);
        s.Extra = Candidates().ToDictionary(c => c.device, c => $"{c.current.Width},{c.current.Height},{c.current.RefreshHz},{c.current.BitsPerPixel}");
        return s;
    }

    public void Apply(ActionDefinition a)
    {
        foreach (var (device, current, best) in Candidates().Where(c => c.best.RefreshHz > c.current.RefreshHz).ToList())
        {
            if (DisplayNative.SetMode(device, best, test: true) != DisplayNative.DISP_CHANGE_SUCCESSFUL)
                throw new InvalidOperationException(Loc.T("display.modeRejected", device, best.RefreshHz));
            var rc = DisplayNative.SetMode(device, best, test: false);
            if (rc is not (DisplayNative.DISP_CHANGE_SUCCESSFUL or DisplayNative.DISP_CHANGE_RESTART))
                throw new InvalidOperationException(Loc.T("display.modeFailed", device, rc));
            Log.Info("Display", $"{device}: {current.RefreshHz} Hz → {best.RefreshHz} Hz");
        }
    }

    public void Restore(ActionDefinition a, StateSnapshot s)
    {
        foreach (var (device, mode) in s.Extra ?? new())
        {
            var p = mode.Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            DisplayNative.SetMode(device, new DisplayMode(p[0], p[1], p[2], p[3]), test: false);
        }
    }

    public bool Verify(ActionDefinition a, StateSnapshot s) =>
        (s.Extra ?? new()).All(kv => DisplayNative.Current(kv.Key) is { } cur && $"{cur.Width},{cur.Height},{cur.RefreshHz},{cur.BitsPerPixel}" == kv.Value);
}
