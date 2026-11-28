using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.BenchmarkManager.Detective;

/// <summary>Static facts about a running process (path, publisher, window, hosted services).</summary>
public sealed class ProcessFacts
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? Company { get; set; }
    public bool HasWindow { get; set; }
    public List<string> Services { get; set; } = new();
}

/// <summary>What else is known about the PC when classifying (start-up entries, game folders, running game).</summary>
public sealed class ClassificationContext
{
    /// <summary>Executable file name (lower case, no extension) → start-up classification and category.</summary>
    public Dictionary<string, (StartupClassification Classification, string? Category)> Startup { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> GameFolders { get; set; } = new();
}

internal sealed class ProcessRule
{
    public string Match { get; set; } = "";
    public ProcessTier Tier { get; set; }
    public string Category { get; set; } = "";
    public bool Protected { get; set; }
    public LocalizedText Note { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public Regex? Rx { get; set; }
}

internal sealed class ServiceRule { public string Name { get; set; } = ""; public ProcessTier Tier { get; set; } }

internal sealed class ProcessRulesFile
{
    public List<ProcessRule> Processes { get; set; } = new();
    public List<ServiceRule> Services { get; set; } = new();
    public List<string> Games { get; set; } = new();
}

/// <summary>Action an optimisation level takes on a process (always listed and confirmed before anything is done).</summary>
public enum ProcessAction { None, LowerPriority, Close }

/// <summary>
/// Sorts every running process into six tiers (harmful → essential). Known programs come from a knowledge base;
/// any other process is classified from what can be observed: hosted Windows services, Windows folder, publisher,
/// open window, start-up entry, suspicious location — and its measured cost (CPU, wake-ups) orders the list.
/// </summary>
public sealed class ProcessClassifier
{
    private readonly List<ProcessRule> _rules;
    private readonly Dictionary<string, ProcessTier> _services;
    private readonly HashSet<string> _games;

    private static readonly HashSet<string> ProtectedCategories = new(StringComparer.OrdinalIgnoreCase)
        { "system", "security", "anticheat", "driver", "audio", "peripheral", "game" };

    private ProcessClassifier(ProcessRulesFile f)
    {
        _rules = f.Processes;
        foreach (var r in _rules) r.Rx = new Regex(r.Match, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        _services = f.Services.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Tier, StringComparer.OrdinalIgnoreCase);
        _games = new HashSet<string>(f.Games, StringComparer.OrdinalIgnoreCase);
    }

    public int RuleCount => _rules.Count;

    public static ProcessClassifier Load(string knowledgeDir)
    {
        var file = System.IO.Path.Combine(knowledgeDir, "process-rules.json");
        try { return new ProcessClassifier(JsonSerializer.Deserialize<ProcessRulesFile>(File.ReadAllText(file), Json.Options) ?? new()); }
        catch (Exception ex) { Log.Error("Processes", "process-rules.json unreadable", ex); return new ProcessClassifier(new ProcessRulesFile()); }
    }

    public static string Normalize(string name)
    {
        var n = name.Trim();
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n;
    }

    public bool IsKnownGame(string name) => _games.Contains(Normalize(name));

    /// <summary>Classification of one process (pure: facts in, verdict out).</summary>
    public ProcessVerdict Classify(ProcessFacts f, ProcessActivity? a, ClassificationContext ctx)
    {
        var name = Normalize(f.Name);
        var v = new ProcessVerdict
        {
            Name = name, Path = f.Path, Instances = 1, Pids = { f.Pid }, HasWindow = f.HasWindow, Services = f.Services.ToList(),
            CpuPercent = a?.CpuPercent ?? 0, WakeupsPerSec = a?.WakeupsPerSec ?? 0, MemoryMb = a?.MemoryMb ?? 0,
        };
        var path = f.Path ?? "";
        var inGameFolder = path.Length > 0 && ctx.GameFolders.Any(g => path.StartsWith(g, StringComparison.OrdinalIgnoreCase));

        // 1. A game in progress.
        if (_games.Contains(name) || (inGameFolder && (f.HasWindow || v.CpuPercent > 5)))
            return Set(v, ProcessTier.VeryUseful, "game", true, Loc.T("proc.reason.game"));

        // 2. Knowledge base (the last matching rule is the most specific).
        var rule = _rules.LastOrDefault(r => r.Rx!.IsMatch(name));
        if (rule != null)
        {
            Set(v, rule.Tier, rule.Category, rule.Protected || ProtectedCategories.Contains(rule.Category), rule.Note.ToString());
            if (f.HasWindow && v.Tier < ProcessTier.Useful) Set(v, ProcessTier.Useful, v.Category, v.Protected, Loc.T("proc.reason.openWindow", rule.Note.ToString()));
            return ServiceFloor(v, f);
        }

        // 3. Service host or service program: the most important hosted service decides.
        if (f.Services.Count > 0)
        {
            var tiers = f.Services.Select(s => _services.TryGetValue(s, out var t) ? t : IsWindowsPath(path) ? ProcessTier.Useful : ProcessTier.LowValue).ToList();
            var tier = tiers.Max();
            var isWindows = IsWindowsPath(path);
            return Set(v, tier, isWindows ? "system" : "service", tier >= ProcessTier.VeryUseful || isWindows,
                Loc.T(isWindows ? "proc.reason.windowsService" : "proc.reason.thirdPartyService", string.Join(", ", f.Services.Take(4))));
        }

        // 4. Other Windows binaries.
        if (IsWindowsPath(path)) return Set(v, ProcessTier.Useful, "system", true, Loc.T("proc.reason.windows"));

        // 5. An application the user has open.
        if (f.HasWindow) return Set(v, ProcessTier.Useful, "app", false, Loc.T("proc.reason.window"));

        // 6. Suspicious: unknown publisher running from a temporary or download folder.
        var lower = path.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(f.Company) && (lower.Contains(@"\temp\") || lower.Contains(@"\downloads\") || lower.Contains(@"\appdata\local\temp")))
            return Set(v, ProcessTier.Harmful, "suspicious", false, Loc.T("proc.reason.suspicious"));

        // 7. Start-up entry of known classification.
        if (ctx.Startup.TryGetValue(name, out var st))
        {
            var tier = st.Classification switch
            {
                StartupClassification.Essential => ProcessTier.VeryUseful,
                StartupClassification.Recommended => ProcessTier.Useful,
                StartupClassification.Risky => ProcessTier.Harmful,
                _ => st.Category is "updater" ? ProcessTier.LowValue : ProcessTier.LowValue,
            };
            return Set(v, tier, st.Category ?? "startup", st.Classification == StartupClassification.Essential, Loc.T("proc.reason.startup"));
        }

        // 8. Any other third-party program running in the background.
        return Set(v, ProcessTier.LowValue, "background", false, Loc.T("proc.reason.background", f.Company ?? Loc.Unknown));
    }

    private ProcessVerdict ServiceFloor(ProcessVerdict v, ProcessFacts f)
    {
        // A process that hosts an essential Windows service is never below that service.
        var floor = f.Services.Select(s => _services.TryGetValue(s, out var t) ? t : ProcessTier.Harmful).DefaultIfEmpty(ProcessTier.Harmful).Max();
        if (floor > v.Tier) { v.Tier = floor; v.Protected |= floor >= ProcessTier.VeryUseful; }
        return v;
    }

    private static ProcessVerdict Set(ProcessVerdict v, ProcessTier t, string category, bool prot, string reason)
    {
        v.Tier = t; v.Category = category; v.Protected = prot; v.Reason = reason;
        return v;
    }

    public static bool IsWindowsPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !string.IsNullOrEmpty(win) && path.StartsWith(win, StringComparison.OrdinalIgnoreCase) && !path.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Same program, several instances (e.g. 30 browser processes): one line with the summed cost.</summary>
    public static List<ProcessVerdict> Group(IEnumerable<ProcessVerdict> verdicts) =>
        verdicts.GroupBy(v => (v.Name.ToLowerInvariant(), v.Tier))
            .Select(g =>
            {
                var f = g.First();
                return new ProcessVerdict
                {
                    Name = f.Name, Path = f.Path, Tier = f.Tier, Category = f.Category, Reason = f.Reason, Protected = g.Any(x => x.Protected),
                    Instances = g.Count(), Pids = g.SelectMany(x => x.Pids).ToList(), HasWindow = g.Any(x => x.HasWindow), IsGame = f.Category == "game",
                    Services = g.SelectMany(x => x.Services).Distinct().ToList(),
                    CpuPercent = g.Sum(x => x.CpuPercent), WakeupsPerSec = g.Sum(x => x.WakeupsPerSec), MemoryMb = g.Sum(x => x.MemoryMb),
                };
            })
            .OrderBy(v => v.Tier).ThenByDescending(v => v.WakeupsPerSec + v.CpuPercent * 100).ToList();

    /// <summary>
    /// What each optimisation level does with a process. Essential and very useful ones, protected categories and
    /// open windows are never touched; the deeper the level, the further down the tiers it goes.
    /// </summary>
    public static ProcessAction ActionFor(ProcessVerdict v, ProfileLevel level)
    {
        if (v.Protected || v.Tier >= ProcessTier.VeryUseful || v.IsGame) return ProcessAction.None;
        return v.Tier switch
        {
            ProcessTier.Harmful => v.HasWindow ? ProcessAction.LowerPriority : ProcessAction.Close,
            ProcessTier.Useless => level >= ProfileLevel.Performance && !v.HasWindow ? ProcessAction.Close : level >= ProfileLevel.Performance ? ProcessAction.LowerPriority : ProcessAction.None,
            ProcessTier.LowValue => level == ProfileLevel.Extreme && !v.HasWindow ? ProcessAction.Close : level >= ProfileLevel.Maximum ? ProcessAction.LowerPriority : ProcessAction.None,
            ProcessTier.Useful => level == ProfileLevel.Extreme && !v.HasWindow && v.Category is not ("communication" or "media" or "launcher" or "app") ? ProcessAction.LowerPriority : ProcessAction.None,
            _ => ProcessAction.None,
        };
    }

    // ------------------------------------------------------------------ live facts (Windows)
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);

    public static string? ImagePath(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try { var sb = new StringBuilder(1024); var len = sb.Capacity; return QueryFullProcessImageNameW(h, 0, sb, ref len) ? sb.ToString(0, len) : null; }
        finally { CloseHandle(h); }
    }

    /// <summary>Path, publisher, window and hosted services of every running process (no administrator rights needed).</summary>
    public static List<ProcessFacts> CollectFacts()
    {
        var services = new Dictionary<int, List<string>>();
        try
        {
            using var s = new System.Management.ManagementObjectSearcher("SELECT Name, ProcessId FROM Win32_Service WHERE ProcessId <> 0");
            foreach (System.Management.ManagementObject o in s.Get())
                using (o)
                {
                    var pid = Convert.ToInt32(o["ProcessId"]);
                    (services.TryGetValue(pid, out var l) ? l : services[pid] = new()).Add(o["Name"]?.ToString() ?? "");
                }
        }
        catch (Exception ex) { Log.Warn("Processes", "Services unreadable: " + ex.Message); }
        var companies = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ProcessFacts>();
        foreach (var p in Process.GetProcesses())
            using (p)
            {
                var f = new ProcessFacts { Pid = p.Id, Name = p.ProcessName };
                try { f.HasWindow = p.MainWindowHandle != IntPtr.Zero; } catch { }
                f.Path = p.Id > 4 ? ImagePath(p.Id) : null;
                if (f.Path != null)
                {
                    if (!companies.TryGetValue(f.Path, out var c))
                    {
                        try { c = FileVersionInfo.GetVersionInfo(f.Path).CompanyName; } catch { c = null; }
                        companies[f.Path] = c;
                    }
                    f.Company = c;
                }
                if (services.TryGetValue(p.Id, out var sv)) f.Services = sv;
                list.Add(f);
            }
        return list;
    }
}
