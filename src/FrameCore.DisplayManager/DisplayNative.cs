using System.Runtime.InteropServices;

namespace FrameCore.DisplayManager;

public sealed record DisplayMode(int Width, int Height, int RefreshHz, int BitsPerPixel);

internal static class DisplayNative
{
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    public const uint CDS_UPDATEREGISTRY = 0x01, CDS_TEST = 0x02;
    public const int DISP_CHANGE_SUCCESSFUL = 0, DISP_CHANGE_RESTART = 1;
    public const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1, DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        public static DISPLAY_DEVICE Create() => new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        public static DEVMODE Create() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>(), dmDeviceName = "", dmFormName = "" };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevicesW(string? device, uint index, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettingsExW(string device, int mode, ref DEVMODE dm, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ChangeDisplaySettingsExW(string device, ref DEVMODE dm, IntPtr hwnd, uint flags, IntPtr lParam);

    // QueryDisplayConfig (byte-offset based, see DISPLAYCONFIG_PATH_INFO layout: 72 bytes).
    public const uint QDC_ONLY_ACTIVE_PATHS = 2;
    [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, byte[] paths, ref uint numModes, byte[] modes, IntPtr topology);
    [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(byte[] packet);

    public static IEnumerable<DisplayMode> Modes(string device)
    {
        var dm = DEVMODE.Create();
        for (var i = 0; EnumDisplaySettingsExW(device, i, ref dm, 0); i++)
        {
            yield return new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency, dm.dmBitsPerPel);
            dm = DEVMODE.Create();
        }
    }

    public static DisplayMode? Current(string device)
    {
        var dm = DEVMODE.Create();
        return EnumDisplaySettingsExW(device, ENUM_CURRENT_SETTINGS, ref dm, 0)
            ? new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency, dm.dmBitsPerPel) : null;
    }

    public static int SetMode(string device, DisplayMode mode, bool test)
    {
        var dm = DEVMODE.Create();
        if (!EnumDisplaySettingsExW(device, ENUM_CURRENT_SETTINGS, ref dm, 0)) return -1;
        dm.dmPelsWidth = mode.Width;
        dm.dmPelsHeight = mode.Height;
        dm.dmDisplayFrequency = mode.RefreshHz;
        dm.dmBitsPerPel = mode.BitsPerPixel;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL;
        return ChangeDisplaySettingsExW(device, ref dm, IntPtr.Zero, test ? CDS_TEST : CDS_UPDATEREGISTRY, IntPtr.Zero);
    }

    public sealed record PathInfo(string GdiName, string? FriendlyName, int OutputTechnology, double RefreshHz,
        bool? AdvancedColorSupported, bool? AdvancedColorEnabled, int? BitsPerColor, string? MonitorDevicePath);

    public static List<PathInfo> ActivePaths()
    {
        var list = new List<PathInfo>();
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var np, out var nm) != 0) return list;
        var paths = new byte[np * 72];
        var modes = new byte[nm * 64];
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return list;
        for (var i = 0; i < np; i++)
        {
            var o = i * 72;
            var srcLuidLow = BitConverter.ToUInt32(paths, o); var srcLuidHigh = BitConverter.ToInt32(paths, o + 4); var srcId = BitConverter.ToUInt32(paths, o + 8);
            var tgtLuidLow = BitConverter.ToUInt32(paths, o + 20); var tgtLuidHigh = BitConverter.ToInt32(paths, o + 24); var tgtId = BitConverter.ToUInt32(paths, o + 28);
            var tech = BitConverter.ToInt32(paths, o + 36);
            var num = BitConverter.ToUInt32(paths, o + 48); var den = BitConverter.ToUInt32(paths, o + 52);

            // DISPLAYCONFIG_SOURCE_DEVICE_NAME (type 1, 84 bytes)
            var src = Packet(1, 84, srcLuidLow, srcLuidHigh, srcId);
            var gdi = DisplayConfigGetDeviceInfo(src) == 0 ? ReadString(src, 20, 32) : "";
            // DISPLAYCONFIG_TARGET_DEVICE_NAME (type 2, 420 bytes)
            var tgt = Packet(2, 420, tgtLuidLow, tgtLuidHigh, tgtId);
            string? friendly = null, devicePath = null;
            if (DisplayConfigGetDeviceInfo(tgt) == 0) { friendly = ReadString(tgt, 36, 64); devicePath = ReadString(tgt, 164, 128); }
            // DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO (type 9, 32 bytes)
            var color = Packet(9, 32, tgtLuidLow, tgtLuidHigh, tgtId);
            bool? acs = null, ace = null; int? bpc = null;
            if (DisplayConfigGetDeviceInfo(color) == 0)
            {
                var v = BitConverter.ToUInt32(color, 20);
                acs = (v & 1) != 0; ace = (v & 2) != 0; bpc = (int)BitConverter.ToUInt32(color, 28);
            }
            list.Add(new PathInfo(gdi, string.IsNullOrWhiteSpace(friendly) ? null : friendly, tech, den == 0 ? 0 : Math.Round((double)num / den, 3), acs, ace, bpc, devicePath));
        }
        return list;
    }

    private static byte[] Packet(int type, int size, uint luidLow, int luidHigh, uint id)
    {
        var p = new byte[size];
        BitConverter.GetBytes(type).CopyTo(p, 0);
        BitConverter.GetBytes(size).CopyTo(p, 4);
        BitConverter.GetBytes(luidLow).CopyTo(p, 8);
        BitConverter.GetBytes(luidHigh).CopyTo(p, 12);
        BitConverter.GetBytes(id).CopyTo(p, 16);
        return p;
    }

    private static string ReadString(byte[] buf, int offset, int chars)
    {
        var s = System.Text.Encoding.Unicode.GetString(buf, offset, chars * 2);
        var z = s.IndexOf('\0');
        return z >= 0 ? s[..z] : s;
    }

    public static string Technology(int t) => t switch
    {
        0 => "VGA", 4 => "DVI", 5 => "HDMI", 6 => "LVDS", 10 => "DisplayPort", 11 => "eDP (interne)", 15 => "Miracast",
        16 or 17 => "Indirect", 18 => "DisplayPort (USB-C)", unchecked((int)0x80000000) => "Interne", _ => $"Autre ({t})",
    };
}
