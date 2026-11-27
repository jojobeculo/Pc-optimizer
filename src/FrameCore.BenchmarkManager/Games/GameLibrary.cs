using System.Text.Json;
using System.Text.RegularExpressions;
using FrameCore.Core;
using FrameCore.Core.Models;

namespace FrameCore.BenchmarkManager.Games;

public sealed class GameInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Engine { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Api { get; set; } = "";
    /// <summary>Average FPS of the reference machine (RTX 4070, 1440p, High), GPU-bound.</summary>
    public double RefFps { get; set; }
    /// <summary>FPS when the reference CPU (Ryzen 7 7800X3D) is the limit (1080p, fast GPU).</summary>
    public double CpuCeilingFps { get; set; }
    /// <summary>How strongly FPS falls with pixel count (0.4 light engines … 0.85 heavy AAA).</summary>
    public double ResolutionExponent { get; set; } = 0.75;
    public double VramGb1440High { get; set; }
    public double RamGb { get; set; } = 16;
    public double? FpsCap { get; set; }
    public double CpuSensitivity { get; set; }
    public double MemorySensitivity { get; set; }
    /// <summary>0 = very variable (sandbox, mods, servers), 1 = typical, 2 = reproducible (built-in benchmark).</summary>
    public int DataQuality { get; set; } = 1;
    public int Popularity { get; set; } = 2;
    public bool RequiresRayTracing { get; set; }
    public LocalizedText? Note { get; set; }
    /// <summary>In-game settings that raise FPS or lower latency in this game (competitive titles).</summary>
    public List<LocalizedText> Tips { get; set; } = new();
}

public sealed class GpuPerf { public string Name { get; set; } = ""; public string Vendor { get; set; } = ""; public double Index { get; set; } public double VramGb { get; set; } }
public sealed class CpuPerf { public string Name { get; set; } = ""; public double Index { get; set; } }

public sealed class ExcludedGame { public string Name { get; set; } = ""; public LocalizedText Reason { get; set; } = new(); }

internal sealed class GamesFile { public List<GameInfo> Games { get; set; } = new(); public List<ExcludedGame> Excluded { get; set; } = new(); public LocalizedText? Provenance { get; set; } }
internal sealed class HardwareFile { public List<GpuPerf> Gpus { get; set; } = new(); public List<CpuPerf> Cpus { get; set; } = new(); public LocalizedText? Note { get; set; } }

public enum MatchQuality { Exact, Approximate, None }

/// <summary>Game library and hardware reference tables (catalog/knowledge/games.json, hardware-performance.json).</summary>
public sealed class GameLibrary
{
    public const double ReferenceGpuIndex = 50;   // RTX 4070
    public const double ReferenceCpuIndex = 89;   // Ryzen 7 7800X3D

    public IReadOnlyList<GameInfo> Games { get; }
    public IReadOnlyList<ExcludedGame> Excluded { get; }
    public IReadOnlyList<GpuPerf> Gpus { get; }
    public IReadOnlyList<CpuPerf> Cpus { get; }
    public LocalizedText? Provenance { get; }
    public LocalizedText? HardwareNote { get; }

    public GameLibrary(IReadOnlyList<GameInfo> games, IReadOnlyList<GpuPerf> gpus, IReadOnlyList<CpuPerf> cpus,
                       IReadOnlyList<ExcludedGame>? excluded = null, LocalizedText? provenance = null, LocalizedText? hardwareNote = null)
    {
        Games = games; Gpus = gpus; Cpus = cpus; Excluded = excluded ?? Array.Empty<ExcludedGame>();
        Provenance = provenance; HardwareNote = hardwareNote;
    }

    public static GameLibrary Load(string knowledgeDir)
    {
        var g = JsonSerializer.Deserialize<GamesFile>(File.ReadAllText(Path.Combine(knowledgeDir, "games.json")), Json.Options) ?? new();
        var h = JsonSerializer.Deserialize<HardwareFile>(File.ReadAllText(Path.Combine(knowledgeDir, "hardware-performance.json")), Json.Options) ?? new();
        return new GameLibrary(g.Games, h.Gpus, h.Cpus, g.Excluded, g.Provenance, h.Note);
    }

    public GameInfo? Find(string id) => Games.FirstOrDefault(x => x.Id == id);

    // ------------------------------------------------------------------ hardware matching
    private static string Normalize(string s)
    {
        s = Regex.Replace(s, @"\((R|TM|C)\)|®|™", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(NVIDIA|GeForce|AMD|Intel|Graphics Processor|\d+-Core Processor|Processor|\d+(th|st|nd|rd) Gen|with Radeon Graphics)\b", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim().ToUpperInvariant();
    }

    /// <summary>Finds the GPU entry; laptop chips match laptop entries only.</summary>
    public (GpuPerf? gpu, MatchQuality quality) MatchGpu(string? name, string? cpuName = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return (null, MatchQuality.None);
        var n = Normalize(name);
        var laptop = n.Contains("LAPTOP") || n.Contains("MOBILE") || Regex.IsMatch(n, @"\b\d{4}M\b");
        // Ambiguous integrated Radeon name: identify through the APU model.
        if (Regex.IsMatch(n, @"^RADEON(\s+GRAPHICS)?$") && cpuName != null)
        {
            var c = Normalize(cpuName);
            var igpu = c switch
            {
                _ when Regex.IsMatch(c, @"\b(HX 37\d|AI 9)") => "Radeon 890M",
                _ when Regex.IsMatch(c, @"\b(7840|7940|8840|8845|8945|8700G)") => "Radeon 780M",
                _ when Regex.IsMatch(c, @"\b(7640|8640|8600G|8645)") => "Radeon 760M",
                _ when Regex.IsMatch(c, @"\b(6800H|6900H|6980H|7735|6800U)") => "Radeon 680M",
                _ when Regex.IsMatch(c, @"\b(6600H|7535)") => "Radeon 660M",
                _ when Regex.IsMatch(c, @"\b(5[5-9]00[GHU]|4[5-9]00[GHU])") => "Radeon Vega 8",
                _ when Regex.IsMatch(c, @"\b(7[6-9]00X?3?D?|9[6-9]00X?3?D?|7950X|9950X)\b") => "Radeon Graphics 610M",
                _ => null,
            };
            var e = igpu == null ? null : Gpus.FirstOrDefault(x => x.Name == igpu);
            return e == null ? (null, MatchQuality.None) : (e, MatchQuality.Approximate);
        }
        GpuPerf? best = null;
        foreach (var g in Gpus)
        {
            var gn = g.Name.ToUpperInvariant();
            var isLaptopEntry = gn.EndsWith(" LAPTOP");
            var core = isLaptopEntry ? gn[..^7] : gn;
            if (isLaptopEntry != laptop && !gn.StartsWith("RADEON") && !gn.StartsWith("IRIS") && !gn.StartsWith("UHD") && !gn.StartsWith("ARC 1")) continue;
            if (!Regex.IsMatch(n, @"(^|\s)" + Regex.Escape(core).Replace("\\ ", "\\s*") + @"(\s|$)")) continue;
            if (best == null || g.Name.Length > best.Name.Length) best = g;
        }
        return best == null ? (null, MatchQuality.None) : (best, MatchQuality.Exact);
    }

    public (CpuPerf? cpu, MatchQuality quality) MatchCpu(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (null, MatchQuality.None);
        var n = Normalize(name);
        // Intel "F"/"KF"/"KS" variants share the gaming performance of the base model.
        var model = Regex.Match(n, @"\b(I[3579]-\d{4,5})(K|KF|KS|F|T|H|HX|HK)?\b");
        CpuPerf? best = null;
        foreach (var c in Cpus)
        {
            var cn = Normalize(c.Name);
            var ok = Regex.IsMatch(n, @"(^|\s)" + Regex.Escape(cn) + @"(\s|$)");
            if (!ok && model.Success)
            {
                var suffix = model.Groups[2].Value switch { "KF" or "KS" => "K", "F" => "", var s => s };
                ok = cn.EndsWith(model.Groups[1].Value + suffix);
            }
            if (ok && (best == null || c.Name.Length > best.Name.Length)) best = c;
        }
        return best == null ? (null, MatchQuality.None) : (best, MatchQuality.Exact);
    }
}
