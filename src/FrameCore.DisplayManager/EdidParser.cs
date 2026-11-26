using System.Text;

namespace FrameCore.DisplayManager;

public sealed class EdidInfo
{
    public string? ManufacturerCode { get; set; }
    public int ProductCode { get; set; }
    public string? MonitorName { get; set; }
    public int Version { get; set; }
    public int Revision { get; set; }
    public int? PreferredWidth { get; set; }
    public int? PreferredHeight { get; set; }
    public double? PreferredRefreshHz { get; set; }
    public int? RangeMinVHz { get; set; }
    public int? RangeMaxVHz { get; set; }
    public bool ContinuousFrequency { get; set; }
    public bool FreeSync { get; set; }
    public int? FreeSyncMinHz { get; set; }
    public int? FreeSyncMaxHz { get; set; }
    public bool HdmiForumVrr { get; set; }
    public int? HdmiVrrMinHz { get; set; }
    public int? HdmiVrrMaxHz { get; set; }
    public bool HdrStaticMetadata { get; set; }
    public bool HdrPq { get; set; }
    public double MaxDetailedRefreshHz { get; set; }
    public bool Valid { get; set; }

    /// <summary>VESA Adaptive-Sync (DisplayPort) advertisement heuristic: continuous frequency + a wide vertical range.</summary>
    public bool AdaptiveSyncLikely => ContinuousFrequency && RangeMinVHz is { } min && RangeMaxVHz is { } max && max - min >= 20 && max >= 60;
    public bool AnyVrr => FreeSync || HdmiForumVrr || AdaptiveSyncLikely;
    public int? VrrMin => FreeSyncMinHz ?? HdmiVrrMinHz ?? (AdaptiveSyncLikely ? RangeMinVHz : null);
    public int? VrrMax => FreeSyncMaxHz ?? HdmiVrrMaxHz ?? (AdaptiveSyncLikely ? RangeMaxVHz : null);
}

/// <summary>EDID 1.3/1.4 + CTA-861 extension parser (only the fields FrameCore needs). Pure, unit-tested.</summary>
public static class EdidParser
{
    private static readonly byte[] Header = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

    public static EdidInfo Parse(byte[] edid)
    {
        var info = new EdidInfo();
        if (edid.Length < 128 || !edid.AsSpan(0, 8).SequenceEqual(Header)) return info;
        info.Valid = true;
        var mfg = (edid[8] << 8) | edid[9];
        info.ManufacturerCode = new string(new[] { (char)('A' - 1 + ((mfg >> 10) & 0x1F)), (char)('A' - 1 + ((mfg >> 5) & 0x1F)), (char)('A' - 1 + (mfg & 0x1F)) });
        info.ProductCode = edid[10] | (edid[11] << 8);
        info.Version = edid[18];
        info.Revision = edid[19];
        info.ContinuousFrequency = (edid[24] & 0x01) != 0;

        for (var d = 0; d < 4; d++)
        {
            var o = 54 + d * 18;
            if (edid[o] != 0 || edid[o + 1] != 0)
            {
                var t = DetailedTiming(edid, o);
                if (t != null)
                {
                    if (d == 0) { info.PreferredWidth = t.Value.w; info.PreferredHeight = t.Value.h; info.PreferredRefreshHz = t.Value.hz; }
                    info.MaxDetailedRefreshHz = Math.Max(info.MaxDetailedRefreshHz, t.Value.hz);
                }
                continue;
            }
            var tag = edid[o + 3];
            if (tag == 0xFC) info.MonitorName = Text(edid, o + 5);
            else if (tag == 0xFD)
            {
                var flags = edid[o + 4];
                info.RangeMinVHz = edid[o + 5] + ((flags & 0x01) != 0 ? 255 : 0);
                info.RangeMaxVHz = edid[o + 6] + ((flags & 0x02) != 0 ? 255 : 0);
            }
        }

        var extensions = edid[126];
        for (var e = 1; e <= extensions && (e + 1) * 128 <= edid.Length; e++)
        {
            var b = e * 128;
            if (edid[b] == 0x02) ParseCta(edid, b, info);
        }
        return info;
    }

    private static (int w, int h, double hz)? DetailedTiming(byte[] e, int o)
    {
        var pixelClock = (e[o] | (e[o + 1] << 8)) * 10_000.0;
        var hActive = e[o + 2] | ((e[o + 4] & 0xF0) << 4);
        var hBlank = e[o + 3] | ((e[o + 4] & 0x0F) << 8);
        var vActive = e[o + 5] | ((e[o + 7] & 0xF0) << 4);
        var vBlank = e[o + 6] | ((e[o + 7] & 0x0F) << 8);
        var total = (double)(hActive + hBlank) * (vActive + vBlank);
        if (total <= 0 || hActive == 0 || vActive == 0) return null;
        return (hActive, vActive, Math.Round(pixelClock / total, 2));
    }

    private static string Text(byte[] e, int o)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 13; i++)
        {
            var c = e[o + i];
            if (c == 0x0A) break;
            sb.Append((char)c);
        }
        return sb.ToString().Trim();
    }

    private static void ParseCta(byte[] e, int b, EdidInfo info)
    {
        var dtdOffset = e[b + 2];
        if (dtdOffset < 4) dtdOffset = 127;
        var i = b + 4;
        var end = b + Math.Min((int)dtdOffset, 127);
        while (i < end)
        {
            var header = e[i];
            var tag = (header >> 5) & 0x07;
            var len = header & 0x1F;
            if (i + len >= e.Length) break;
            if (tag == 3 && len >= 3)
            {
                var oui = e[i + 1] | (e[i + 2] << 8) | (e[i + 3] << 16);
                if (oui == 0x00001A && len >= 8) // AMD vendor-specific block (FreeSync over HDMI)
                {
                    info.FreeSync = (e[i + 6] & 0x01) != 0 || e[i + 8] > 0;
                    if (e[i + 7] > 0) info.FreeSyncMinHz = e[i + 7];
                    if (e[i + 8] > 0) info.FreeSyncMaxHz = e[i + 8];
                }
                else if (oui == 0xC45DD8 && len >= 10) // HDMI Forum VSDB (HDMI 2.1 VRR)
                {
                    var vrrMin = e[i + 9] & 0x3F;
                    var vrrMax = ((e[i + 9] & 0xC0) << 2) | (len >= 10 ? e[i + 10] : 0);
                    if (vrrMax > 0 || vrrMin > 0)
                    {
                        info.HdmiForumVrr = true;
                        info.HdmiVrrMinHz = vrrMin > 0 ? vrrMin : null;
                        info.HdmiVrrMaxHz = vrrMax > 0 ? vrrMax : null;
                    }
                }
            }
            else if (tag == 7 && len >= 2)
            {
                var ext = e[i + 1];
                if (ext == 6) // HDR static metadata data block
                {
                    info.HdrStaticMetadata = true;
                    info.HdrPq = (e[i + 2] & 0x04) != 0;
                }
            }
            i += len + 1;
        }
        // Detailed timings inside the CTA block can advertise higher refresh rates.
        for (var o = b + dtdOffset; o + 18 <= b + 127; o += 18)
        {
            if (e[o] == 0 && e[o + 1] == 0) break;
            var t = DetailedTiming(e, o);
            if (t != null) info.MaxDetailedRefreshHz = Math.Max(info.MaxDetailedRefreshHz, t.Value.hz);
        }
    }
}
