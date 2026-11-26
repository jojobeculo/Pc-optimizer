using System.Text.Json;
using System.Text.RegularExpressions;
using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.StartupManager;

public enum StartupKind { RunKey, RunOnce, StartupFolder, ScheduledTask, Service, ShellExtension }

public sealed class StartupEntry
{
    public string Id { get; set; } = "";
    public StartupKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Command { get; set; }
    public string? FilePath { get; set; }
    public string Location { get; set; } = "";
    public string? Publisher { get; set; }
    public string? Signer { get; set; }
    public bool IsMicrosoft { get; set; }
    public bool Enabled { get; set; }
    public bool CanToggle { get; set; } = true;
    public bool RequiresAdmin { get; set; }
    public bool IsRunning { get; set; }
    public bool FileExists { get; set; } = true;
    public StartupClassification Classification { get; set; } = StartupClassification.Unknown;
    public string? Category { get; set; }
    public string? Reason { get; set; }
    public bool Protected { get; set; }
    /// <summary>Service start type (Automatic, AutomaticDelayed, Manual, Disabled) when Kind = Service.</summary>
    public string? StartType { get; set; }
    public string? ServiceState { get; set; }
    /// <summary>Approval key path/value used to disable Run/StartupFolder entries reversibly (same mechanism as Task Manager).</summary>
    public string? ApprovalPath { get; set; }
    public string? Clsid { get; set; }
}

public sealed class StartupRule
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Publisher { get; set; }
    public StartupClassification Classification { get; set; }
    public string Category { get; set; } = "";
    public bool Protected { get; set; }
    public LocalizedText? Note { get; set; }
}

/// <summary>
/// Knowledge base (catalog/knowledge/startup-rules.json) mapping known entries to a classification.
/// Anti-cheats, drivers, audio, input devices and security software are "Essential"/protected and never offered for disabling by default.
/// </summary>
public sealed class StartupKnowledgeBase
{
    public List<StartupRule> Rules { get; set; } = new();

    public static StartupKnowledgeBase Load(string? path = null)
    {
        path ??= System.IO.Path.Combine(AppPaths.CatalogDir, "knowledge", "startup-rules.json");
        try
        {
            if (File.Exists(path)) return new StartupKnowledgeBase { Rules = JsonSerializer.Deserialize<List<StartupRule>>(File.ReadAllText(path), Json.Options) ?? new() };
        }
        catch (Exception ex) { Log.Error("StartupKB", "Unreadable knowledge base", ex); }
        return new StartupKnowledgeBase();
    }

    private static bool M(string? pattern, string? value) =>
        pattern == null || (value != null && Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    public void Classify(StartupEntry e)
    {
        var haystackName = $"{e.Name} {e.DisplayName}".Trim();
        var fileName = System.IO.Path.GetFileName(e.FilePath ?? "");
        var path = e.FilePath ?? e.Command;
        foreach (var r in Rules)
        {
            if (r.Name == null && r.Path == null && r.Publisher == null) continue;
            if ((M(r.Name, haystackName) || (r.Name != null && fileName.Length > 0 && M(r.Name, fileName))) && M(r.Path, path) && M(r.Publisher, e.Publisher ?? e.Signer))
            {
                e.Classification = r.Classification;
                e.Category = r.Category;
                e.Protected = r.Protected;
                e.Reason = r.Note?.ToString() ?? Loc.T("startup.cat." + r.Category);
                return;
            }
        }
        // Heuristics when no rule matched.
        if (!e.FileExists && e.Kind != StartupKind.Service)
        {
            e.Classification = StartupClassification.Optional;
            e.Reason = Loc.T("startup.reason.missingFile");
            return;
        }
        var p = (path ?? "").ToLowerInvariant();
        if ((p.Contains(@"\appdata\local\temp\") || p.Contains(@"\windows\temp\")) && e.Signer == null)
        {
            e.Classification = StartupClassification.Risky;
            e.Reason = Loc.T("startup.reason.tempPath");
            return;
        }
        if (Regex.IsMatch(haystackName, @"update|updater|helper|assistant|launcher|notifier|tray", RegexOptions.IgnoreCase))
        {
            e.Classification = StartupClassification.Optional;
            e.Category = "updater";
            e.Reason = Loc.T("startup.reason.helper");
            return;
        }
        if (e.IsMicrosoft)
        {
            e.Classification = StartupClassification.Recommended;
            e.Reason = Loc.T("startup.reason.microsoft");
            return;
        }
        e.Classification = StartupClassification.Unknown;
        e.Reason = e.Signer != null ? Loc.T("startup.reason.signedUnknown", e.Signer) : Loc.T("startup.reason.unknown");
    }
}
