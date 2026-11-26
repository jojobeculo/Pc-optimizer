using FrameCore.Core.Models;
using System.Text.RegularExpressions;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;

namespace FrameCore.MemoryManager;

/// <summary>
/// Decodes the rated data rate (MT/s) of retail kits from their part number. Returns null when the
/// format is not recognized — FrameCore never guesses an XMP/EXPO rating.
/// </summary>
public static class RamPartNumberDecoder
{
    private static readonly int[] KnownRates = { 2133, 2400, 2666, 2800, 2933, 3000, 3200, 3333, 3466, 3600, 3733, 3800, 4000, 4133, 4266, 4400, 4600, 4800, 5200, 5600, 6000, 6200, 6400, 6600, 6800, 7000, 7200, 7600, 8000, 8200, 8400, 8800 };

    private static int? Valid(int v) => KnownRates.Contains(v) ? v : null;

    public static int? Decode(string? partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber)) return null;
        var p = partNumber.Trim().ToUpperInvariant();
        Match m;
        // Corsair: CMK16GX4M2B3200C16, CMH32GX5M2E6000C36
        if ((m = Regex.Match(p, @"^CM[A-Z]{1,3}\d+GX[45]M\d[A-Z](\d{4})C\d")).Success) return Valid(int.Parse(m.Groups[1].Value));
        // G.Skill: F4-3600C16D-16GTZNC, F5-6000J3038F16GX2-TZ5RK
        if ((m = Regex.Match(p, @"^F[45]-(\d{4})[CJUL]")).Success) return Valid(int.Parse(m.Groups[1].Value));
        // Kingston FURY / HyperX: KF432C16BBK2/16, KF560C36BBEK2-32, HX436C17PB3/16
        if ((m = Regex.Match(p, @"^K?F([45])(\d{2})C\d")).Success || (m = Regex.Match(p, @"^HX([45])(\d{2})C\d")).Success)
            return KingstonRate(m.Groups[2].Value);
        // Crucial Ballistix / Crucial Pro OC: BL2K16G36C16U4B, CP2K16G60C36U5B
        if ((m = Regex.Match(p, @"^(?:BL|CP)\d*K?\d+G(\d{2})C\d")).Success) return KingstonRate(m.Groups[1].Value);
        // TeamGroup T-Force: TF3D416G3200HC16FDC01, FF3D532G6000HC38ADC01
        if ((m = Regex.Match(p, @"G(\d{4})HC\d{2}")).Success) return Valid(int.Parse(m.Groups[1].Value));
        // ADATA XPG: AX4U360038G18A-ST80, AX5U6000C3016G-DCLARBK
        if ((m = Regex.Match(p, @"^AX[45]U(\d{4})")).Success) return Valid(int.Parse(m.Groups[1].Value));
        // Patriot Viper: PVS416G320C6K (×10), PVV532G620C40K
        if ((m = Regex.Match(p, @"^PV[A-Z]{1,2}[45]\d+G(\d{3})C")).Success) return Valid(int.Parse(m.Groups[1].Value) * 10 == 2660 ? 2666 : int.Parse(m.Groups[1].Value) * 10);
        return null;
    }

    private static int? KingstonRate(string two) => two switch
    {
        "21" => 2133, "24" => 2400, "26" => 2666, "29" => 2933, "30" => 3000, "32" => 3200, "33" => 3333, "34" => 3466, "36" => 3600,
        "37" => 3733, "40" => 4000, "42" => 4266, "46" => 4600, "48" => 4800, "52" => 5200, "56" => 5600, "60" => 6000, "62" => 6200,
        "64" => 6400, "66" => 6600, "68" => 6800, "72" => 7200, "76" => 7600, "80" => 8000, "82" => 8200, "84" => 8400, "88" => 8800,
        _ => null,
    };
}

/// <summary>Channel population and XMP/EXPO heuristics. Results are labelled "probable" and explained.</summary>
public sealed class MemoryConfigAnalyzer : IHardwareEnricher
{
    public string Name => Loc.T("scan.memoryAnalysis");

    public void Enrich(MachineProfile profile) => Analyze(profile.Memory, profile.Board.IsLaptop, profile.Board.IsVirtualMachine);

    public static void Analyze(MemoryInfo mem, bool laptop, bool virtualMachine = false)
    {
        foreach (var m in mem.Modules) m.PartNumberSpeedMts = RamPartNumberDecoder.Decode(m.PartNumber);
        if (virtualMachine)
        {
            (mem.Channels, mem.ChannelReasoning, mem.ChannelsConfidence) = (ChannelMode.Unknown, Loc.T("ram.channels.vm"), DataConfidence.Unavailable);
            (mem.Xmp, mem.XmpReasoning) = (XmpState.NotApplicable, Loc.T("ram.channels.vm"));
            return;
        }
        (mem.Channels, mem.ChannelReasoning) = Channels(mem.Modules);
        mem.ChannelsConfidence = mem.Channels == ChannelMode.Unknown ? DataConfidence.Unavailable
            : mem.ChannelReasoning == Loc.T("ram.channels.countBased", mem.Modules.Count) ? DataConfidence.Inferred : DataConfidence.Reported;
        (mem.Xmp, mem.XmpReasoning) = Xmp(mem, laptop);
    }

    public static (ChannelMode, string) Channels(IReadOnlyList<MemoryModule> modules)
    {
        if (modules.Count == 0) return (ChannelMode.Unknown, Loc.T("ram.channels.noModules"));
        var letters = new HashSet<string>();
        foreach (var m in modules)
        {
            var text = $"{m.Slot} {m.Bank}".ToUpperInvariant();
            var match = Regex.Match(text, @"CHANNEL\s*([A-H])");
            if (!match.Success) match = Regex.Match(text, @"DIMM[_\s-]?([A-H])\d");
            if (!match.Success) match = Regex.Match(text, @"\bCH([A-H])\b");
            if (!match.Success) match = Regex.Match(text, @"CONTROLLER\d-CHANNEL([A-H])");
            if (!match.Success) match = Regex.Match(text, @"\b([A-H])[12]\b");
            if (match.Success) letters.Add(match.Groups[1].Value);
        }
        if (letters.Count >= 4) return (ChannelMode.Quad, Loc.T("ram.channels.fromSlots", string.Join(", ", letters.OrderBy(l => l))));
        if (letters.Count >= 2) return (ChannelMode.Dual, Loc.T("ram.channels.fromSlots", string.Join(", ", letters.OrderBy(l => l))));
        if (letters.Count == 1 && modules.Count >= 1) return (ChannelMode.Single, Loc.T("ram.channels.sameChannel", letters.First()));
        return modules.Count switch
        {
            1 => (ChannelMode.Single, Loc.T("ram.channels.oneModule")),
            2 or 4 => (ChannelMode.Dual, Loc.T("ram.channels.countBased", modules.Count)),
            _ => (ChannelMode.Unknown, Loc.T("ram.channels.unknown")),
        };
    }

    public static (XmpState, string) Xmp(MemoryInfo mem, bool laptop)
    {
        var configured = mem.ConfiguredSpeedMts;
        if (configured == null) return (XmpState.Unknown, Loc.T("ram.xmp.noSpeed"));
        var type = mem.Type ?? "";
        if (type.StartsWith("LPDDR") || mem.Modules.All(m => m.FormFactor == "SO-DIMM") && laptop)
            return (XmpState.NotApplicable, Loc.T("ram.xmp.laptop"));
        var rated = mem.Modules.Select(m => m.PartNumberSpeedMts).Where(s => s != null).Select(s => s!.Value).DefaultIfEmpty(0).Max();
        if (rated > 0)
        {
            if (rated > configured + 100) return (XmpState.LikelyDisabled, Loc.T("ram.xmp.belowRating", configured, rated));
            return (XmpState.LikelyEnabled, Loc.T("ram.xmp.atRating", configured, rated));
        }
        // No decodable kit rating: only flag the typical JEDEC fall-back speeds on desktops.
        var jedecFallback = (type == "DDR4" && configured <= 2666) || (type == "DDR5" && configured <= 4800);
        if (jedecFallback && !laptop) return (XmpState.Unknown, Loc.T("ram.xmp.jedecPossible", configured));
        return (XmpState.Unknown, Loc.T("ram.xmp.cannotConfirm", configured));
    }
}
