using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.BackupManager;
using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.StartupManager;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;
using Microsoft.Win32;

namespace FrameCore.UI.ViewModels;

public sealed class ProCategory
{
    public string Key { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Label => Loc.T("pro.cat." + Key);
    public System.Windows.Media.Geometry? Glyph => System.Windows.Application.Current?.TryFindResource("Icon." + Icon) as System.Windows.Media.Geometry;
}

/// <summary>A popular tweak FrameCore refuses to apply, with the reason (catalog/knowledge/rejected-tweaks.json).</summary>
public sealed class RejectedRow
{
    public string Name { get; init; } = "";
    public string Claim { get; init; } = "";
    public string Verdict { get; init; } = "";
    public string Kind { get; init; } = "";
    public string KindLabel => Loc.T("rejected.kind." + Kind);
    public string LevelKey => Kind switch { "Harmful" => "Danger", "SecurityTradeoff" => "Warning", _ => "Neutral" };
    public string Sources { get; init; } = "";
}

public sealed partial class StartupRowViewModel : ObservableObject
{
    public StartupEntry Entry { get; }
    [ObservableProperty] private bool _enabled;
    private readonly Func<StartupRowViewModel, bool, Task> _toggle;
    public StartupRowViewModel(StartupEntry e, Func<StartupRowViewModel, bool, Task> toggle) { Entry = e; _enabled = e.Enabled; _toggle = toggle; }
    public string Name => Entry.DisplayName ?? Entry.Name;
    public string KindLabel => Loc.T("startup.kind." + Entry.Kind);
    public string Publisher => Entry.Publisher ?? Entry.Signer ?? Loc.Unknown;
    public string Command => Entry.Command ?? "";
    public string Location => Entry.Location;
    public StartupClassification Classification => Entry.Classification;
    public string ClassificationLabel => Loc.T("class." + Entry.Classification);
    public string Reason => Entry.Reason ?? "";
    public bool CanToggle => Entry.CanToggle && !Entry.Protected;
    public string StartType => Entry.StartType != null ? Loc.T("startType." + Entry.StartType) : "";
    public bool IsRunning => Entry.IsRunning;
    [RelayCommand] private Task Disable() => _toggle(this, false);
    [RelayCommand] private Task Enable() => _toggle(this, true);
}

public sealed partial class ChangeSetRow : ObservableObject
{
    public ChangeSet Set { get; }
    private readonly Func<ChangeSetRow, Task> _restore;
    public ChangeSetRow(ChangeSet s, Func<ChangeSetRow, Task> restore) { Set = s; _restore = restore; }
    public string Title => Set.Title.ToString();
    public string Date => Set.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public string Category => Set.Category;
    public ChangeSetStatus Status => Set.Status;
    public string StatusLabel => Loc.T("changeStatus." + Set.Status);
    public string Details => string.Join("\n", Set.Entries.Select(e =>
        $"{e.Action}\n    {Loc.T("backup.before")} {e.Before?.Describe()}" + (e.ObservedAfterApply != null ? $"\n    {Loc.T("backup.readBack")} {e.ObservedAfterApply}" : "")))
        + (Set.History.Count == 0 ? "" : "\n\n" + string.Join("\n", Set.History.Select(h => $"{h.Utc.ToLocalTime():g} — {h.Detail}")));
    /// <summary>Verified (read back), NotEffective, NotVerifiable or empty for restored/failed sets.</summary>
    public string VerificationKey => Set.Status != ChangeSetStatus.Applied ? "" :
        Set.Entries.Any(e => e.ApplyVerified == false) ? "NotEffective" : Set.Entries.All(e => e.ApplyVerified == true) ? "Verified" : "NotVerifiable";
    public string VerificationLabel => VerificationKey == "" ? "" : Loc.T("verifyState." + VerificationKey);
    public bool HasVerification => VerificationKey != "";
    public bool CanRestore => Set.Reversible && Set.Status is ChangeSetStatus.Applied or ChangeSetStatus.PartiallyRestored or ChangeSetStatus.Failed;
    [RelayCommand] private Task Restore() => _restore(this);
}

public sealed class CustomProfile
{
    public string Name { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public List<string> TweakIds { get; set; } = new();
}

/// <summary>Pro mode: every setting individually, startup/services management, backups and live monitoring.</summary>
public sealed partial class ProModeViewModel : ObservableObject, IDiagnosticConsumer, IPageActivated
{
    private readonly ShellViewModel _shell;
    private List<TweakRowViewModel> _allRows = new();

    public ObservableCollection<ProCategory> Categories { get; } = new()
    {
        new() { Key = "Windows", Icon = "Layers" }, new() { Key = "Services", Icon = "Wrench" }, new() { Key = "Startup", Icon = "Rocket" },
        new() { Key = "Cpu", Icon = "Chip" }, new() { Key = "Gpu", Icon = "Gpu" }, new() { Key = "Ram", Icon = "Ram" },
        new() { Key = "Bios", Icon = "Board" }, new() { Key = "Drivers", Icon = "Drivers" }, new() { Key = "Storage", Icon = "Disk" },
        new() { Key = "Network", Icon = "Network" }, new() { Key = "Display", Icon = "Monitor" }, new() { Key = "Input", Icon = "Target" },
        new() { Key = "Privacy", Icon = "Eye" }, new() { Key = "Security", Icon = "Shield" }, new() { Key = "Benchmark", Icon = "Bench" },
        new() { Key = "Profiles", Icon = "Save" }, new() { Key = "Monitoring", Icon = "Activity" }, new() { Key = "Rejected", Icon = "Ban" },
    };

    private List<RejectedRow>? _rejected;
    public IReadOnlyList<RejectedRow> Rejected => _rejected ??= LoadRejected();

    private static List<RejectedRow> LoadRejected()
    {
        try
        {
            var path = Path.Combine(Core.AppPaths.CatalogDir, "knowledge", "rejected-tweaks.json");
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            string L(System.Text.Json.JsonElement e) => e.GetProperty(Loc.Instance.CurrentLanguage).GetString() ?? "";
            return doc.RootElement.GetProperty("entries").EnumerateArray().Select(e => new RejectedRow
            {
                Name = L(e.GetProperty("name")), Claim = L(e.GetProperty("claim")), Verdict = L(e.GetProperty("verdict")),
                Kind = e.GetProperty("kind").GetString() ?? "", Sources = string.Join("\n", e.GetProperty("sources").EnumerateArray().Select(x => x.GetString())),
            }).ToList();
        }
        catch (Exception ex) { Core.Log.Warn("Pro", "rejected tweaks: " + ex.Message); return new(); }
    }

    [ObservableProperty] private string _selectedCategory = "Windows";
    [ObservableProperty] private string _startupFilter = "All";
    [ObservableProperty] private bool _scanningStartup;
    [ObservableProperty] private string _backupStatus = "";
    [ObservableProperty] private string _profileName = "";
    [ObservableProperty] private double[] _perCore = Array.Empty<double>();
    [ObservableProperty] private double[] _cpuHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _ramHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _cpuTempHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuTempHistory = Array.Empty<double>();
    [ObservableProperty] private string _monitorText = "";

    public ObservableCollection<TweakRowViewModel> Rows { get; } = new();
    public ObservableCollection<StartupRowViewModel> StartupRows { get; } = new();
    public ObservableCollection<StartupRowViewModel> ServiceRows { get; } = new();
    public ObservableCollection<ChangeSetRow> ChangeSets { get; } = new();
    public ObservableCollection<ChangeBatch> Batches { get; } = new();
    public ObservableCollection<CustomProfile> CustomProfiles { get; } = new();
    private List<StartupEntry> _startup = new();

    public bool IsCatalogCategory => SelectedCategory is not ("Startup" or "Benchmark" or "Profiles" or "Backups" or "Monitoring" or "Drivers" or "Rejected");

    public ProModeViewModel(ShellViewModel shell)
    {
        _shell = shell;
        shell.Services.Journal.Changed += (_, _) => ShellViewModel.Ui(LoadBackups);
        shell.Monitoring.Updated += (_, s) => ShellViewModel.Ui(() =>
        {
            if (SelectedCategory != "Monitoring" || shell.CurrentKey != "pro") return;
            PerCore = s.Cpu.PerCore.ToArray();
            CpuHistory = shell.Monitoring.CpuHistory.Snapshot(); GpuHistory = shell.Monitoring.GpuHistory.Snapshot(); RamHistory = shell.Monitoring.RamHistory.Snapshot();
            CpuTempHistory = shell.Monitoring.CpuTempHistory.Snapshot(); GpuTempHistory = shell.Monitoring.GpuTempHistory.Snapshot();
            MonitorText = Loc.T("pro.monitor.text", s.Cpu.TotalPercent?.ToString("0") ?? "–", s.Cpu.MaxCorePercent.ToString("0"), s.Cpu.EffectiveMhz?.ToString("0") ?? "–",
                s.Cpu.PerformanceLimitPercent?.ToString("0") ?? "–", s.CpuTemp?.ToString("0") ?? Loc.Unknown, s.PrimaryGpu?.UtilizationPercent?.ToString("0") ?? "–",
                (s.PrimaryGpu?.TemperatureC ?? s.GpuTemp)?.ToString("0") ?? Loc.Unknown, s.Memory.UsedPercent.ToString("0"));
        });
        LoadBackups();
        LoadCustomProfiles();
    }

    public void OnActivated() { LoadBackups(); }

    partial void OnSelectedCategoryChanged(string value)
    {
        OnPropertyChanged(nameof(IsCatalogCategory));
        FilterRows();
        if (value == "Backups") LoadBackups();
        if (value == "Drivers") _shell.CurrentKey = "drivers";
        if (value == "Benchmark") _shell.CurrentKey = "benchmark";
    }

    partial void OnStartupFilterChanged(string value) => FillStartup();

    public void OnDiagnostic(DiagnosticResult r)
    {
        BuildRows(r);
        _startup = r.Startup;
        FillStartup();
    }

    public void OnEvaluationsRefreshed(DiagnosticResult r) => BuildRows(r);

    private void BuildRows(DiagnosticResult r)
    {
        var active = _shell.Services.Journal.ChangeSets.Where(c => c.Status is ChangeSetStatus.Applied or ChangeSetStatus.PartiallyRestored).Select(c => c.TweakId).ToHashSet();
        _allRows = r.Evaluations.Select(e => new TweakRowViewModel(e.Tweak, e, ApplyRow, RestoreRow) { HasActiveChange = active.Contains(e.Tweak.Id) }).ToList();
        FilterRows();
    }

    private void FilterRows()
    {
        Rows.Clear();
        foreach (var row in _allRows.Where(x => x.Tweak.Category == SelectedCategory || (SelectedCategory == "Services" && x.Tweak.Category == "Services"))
                                    .OrderBy(x => x.Group).ThenBy(x => x.Confidence).ThenBy(x => x.Name))
            Rows.Add(row);
    }

    private async Task ApplyRow(TweakRowViewModel row)
    {
        await _shell.ApplySingleAsync(row.Tweak);
        Refresh(row);
    }

    private async Task RestoreRow(TweakRowViewModel row)
    {
        var sets = _shell.Services.Engine.ActiveChangeSets(row.Tweak.Id).ToList();
        if (sets.Count == 0) return;
        if (!Dialogs.Confirm(row.Name, Loc.T("restore.confirmOne"), Loc.T("common.restore"))) return;
        var r = await _shell.TweakService.RestoreAsync(sets);
        Dialogs.Info(row.Name, r.Success ? Loc.T("restore.done", r.Restored) : Loc.T("restore.partial", r.Restored, r.Failed), r.Errors);
        Refresh(row);
    }

    private async void Refresh(TweakRowViewModel row)
    {
        await _shell.RefreshEvaluationsAsync();
    }

    // ---------------------------------------------------------------- Startup & services
    private void FillStartup()
    {
        StartupRows.Clear(); ServiceRows.Clear();
        foreach (var e in _startup.OrderBy(e => e.Classification).ThenBy(e => e.Name))
        {
            var row = new StartupRowViewModel(e, Toggle);
            if (e.Kind == StartupKind.Service) ServiceRows.Add(row);
            else if (StartupFilter == "All" || StartupFilter == e.Kind.ToString()) StartupRows.Add(row);
        }
    }

    [RelayCommand]
    private async Task RescanStartup()
    {
        ScanningStartup = true;
        try
        {
            _startup = await Task.Run(() => _shell.Services.CreateStartupScanner().ScanAll(includeServices: true, includeShellExtensions: true));
            if (_shell.Diagnostic != null) _shell.Diagnostic.Startup = _startup;
            FillStartup();
        }
        finally { ScanningStartup = false; }
    }

    private async Task Toggle(StartupRowViewModel row, bool enable)
    {
        var e = row.Entry;
        if (!enable && e.Classification is StartupClassification.Essential or StartupClassification.Recommended or StartupClassification.Unknown
            && !Dialogs.Confirm(row.Name, Loc.T("startup.confirmSensitive", row.ClassificationLabel, row.Reason), Loc.T("common.disable"), true)) return;
        string? startType = null;
        if (e.Kind == StartupKind.Service && !enable) startType = "Manual";
        if (e.Kind == StartupKind.Service && enable) startType = "Automatic";
        var controller = new StartupController(_shell.Services.Engine);
        var r = await Task.Run(() => controller.SetEnabled(e, enable, startType));
        if (!r.Success) Dialogs.Error(r.Error ?? "");
        row.Enabled = e.Enabled;
    }

    [RelayCommand] private void OpenAppsSettings() => Dialogs.OpenUri("ms-settings:appsfeatures");
    [RelayCommand] private void OpenUri(string uri) => Dialogs.OpenUri(uri);

    // ---------------------------------------------------------------- Backups (shown in the restore centre)
    public void OnActivatedForRestore() => LoadBackups();
    public void ReloadChangeSets() => LoadBackups();

    private void LoadBackups()
    {
        ChangeSets.Clear();
        foreach (var c in _shell.Services.Journal.ChangeSets.Take(500)) ChangeSets.Add(new ChangeSetRow(c, RestoreSet));
        Batches.Clear();
        foreach (var b in _shell.Services.Journal.Batches.Take(50)) Batches.Add(b);
    }

    private async Task RestoreSet(ChangeSetRow row)
    {
        if (!Dialogs.Confirm(row.Title, Loc.T("restore.confirmOne"), Loc.T("common.restore"))) return;
        var r = await _shell.TweakService.RestoreAsync(new[] { row.Set });
        BackupStatus = r.Success ? Loc.T("restore.done", r.Restored) : Loc.T("restore.partial", r.Restored, r.Failed) + " " + string.Join(" ; ", r.Errors);
        await _shell.RefreshEvaluationsAsync();
        LoadBackups();
    }

    [RelayCommand]
    private async Task RestoreBatch(ChangeBatch? b)
    {
        if (b == null) return;
        var sets = b.ChangeSetIds.Select(id => _shell.Services.Journal.Find(id)).Where(s => s != null).Cast<ChangeSet>().ToList();
        if (!Dialogs.Confirm(b.Title.ToString(), Loc.T("restore.confirmBatch", sets.Count), Loc.T("common.restore"))) return;
        var r = await _shell.TweakService.RestoreAsync(sets);
        BackupStatus = r.Success ? Loc.T("restore.done", r.Restored) : Loc.T("restore.partial", r.Restored, r.Failed);
        await _shell.RefreshEvaluationsAsync();
        LoadBackups();
    }

    [RelayCommand]
    private async Task RestoreCategory(string category)
    {
        var sets = _shell.Services.Journal.ChangeSets.Where(c => c.Category == category).ToList();
        if (sets.Count == 0) return;
        if (!Dialogs.Confirm(Loc.T("pro.cat." + category), Loc.T("restore.confirmBatch", sets.Count), Loc.T("common.restore"))) return;
        var r = await _shell.TweakService.RestoreAsync(sets);
        BackupStatus = r.Success ? Loc.T("restore.done", r.Restored) : Loc.T("restore.partial", r.Restored, r.Failed);
        await _shell.RefreshEvaluationsAsync();
        LoadBackups();
    }

    [RelayCommand]
    private async Task RestoreAll()
    {
        var sets = _shell.Services.Journal.ChangeSets.ToList();
        if (!Dialogs.Confirm(Loc.T("restore.all.title"), Loc.T("restore.all.confirm", sets.Count(s => s.Status == ChangeSetStatus.Applied)), Loc.T("common.restore"), true)) return;
        var r = await _shell.TweakService.RestoreAsync(sets);
        BackupStatus = r.Success ? Loc.T("restore.done", r.Restored) : Loc.T("restore.partial", r.Restored, r.Failed);
        await _shell.RefreshEvaluationsAsync();
        LoadBackups();
    }

    [RelayCommand]
    private void ExportBackups()
    {
        var dlg = new SaveFileDialog { FileName = $"FrameCore-backups-{DateTime.Now:yyyyMMdd-HHmm}.zip", Filter = "Zip|*.zip" };
        if (dlg.ShowDialog() != true) return;
        _shell.Services.Journal.Export(dlg.FileName);
        BackupStatus = Loc.T("backup.exported", dlg.FileName);
    }

    [RelayCommand]
    private void ImportBackups()
    {
        var dlg = new OpenFileDialog { Filter = "Zip|*.zip" };
        if (dlg.ShowDialog() != true) return;
        var n = _shell.Services.Journal.Import(dlg.FileName);
        BackupStatus = Loc.T("backup.imported", n);
        LoadBackups();
    }

    [RelayCommand] private void OpenSystemRestore() => Dialogs.OpenUri(Path.Combine(Environment.SystemDirectory, "rstrui.exe"));

    // ---------------------------------------------------------------- Custom profiles
    private string ProfilesFile => Path.Combine(AppPaths.Profiles, "custom-profiles.json");

    private void LoadCustomProfiles()
    {
        CustomProfiles.Clear();
        try { if (File.Exists(ProfilesFile)) foreach (var p in Json.Read<List<CustomProfile>>(ProfilesFile) ?? new()) CustomProfiles.Add(p); }
        catch (Exception ex) { Log.Warn("Profiles", ex.Message); }
    }

    [RelayCommand]
    private void SaveCurrentAsProfile()
    {
        var ids = _shell.Services.Journal.ChangeSets.Where(c => c.Status == ChangeSetStatus.Applied && c.TweakId != null).Select(c => c.TweakId!).Distinct().ToList();
        if (ids.Count == 0) { Dialogs.Info(Loc.T("pro.profiles.title"), Loc.T("pro.profiles.empty")); return; }
        CustomProfiles.Insert(0, new CustomProfile { Name = string.IsNullOrWhiteSpace(ProfileName) ? Loc.T("pro.profiles.default", DateTime.Now) : ProfileName, TweakIds = ids });
        Json.WriteAtomic(ProfilesFile, CustomProfiles.ToList());
    }

    [RelayCommand]
    private async Task ApplyCustomProfile(CustomProfile? p)
    {
        if (p == null) return;
        var tweaks = p.TweakIds.Select(id => _shell.Services.Catalog.Find(id)).Where(t => t != null).Cast<TweakDefinition>().ToList();
        if (!Dialogs.Confirm(p.Name, Loc.T("pro.profiles.confirm"), Loc.T("common.apply"), false, tweaks.Select(t => t.Name.ToString()))) return;
        _shell.IsBusy = true;
        try
        {
            var r = await _shell.TweakService.ApplyAsync(tweaks, new LocalizedText(p.Name, p.Name), null, _shell.Services.Settings.CreateRestorePoint, new Progress<string>(s => _shell.BusyText = s));
            Dialogs.Info(p.Name, TweakService.Summary(r), r.Lines);
            await _shell.RefreshEvaluationsAsync();
        }
        finally { _shell.IsBusy = false; _shell.BusyText = ""; }
    }

    [RelayCommand]
    private void ExportCustomProfile(CustomProfile? p)
    {
        if (p == null) return;
        var dlg = new SaveFileDialog { FileName = $"{p.Name}.framecore-profile.json", Filter = "JSON|*.json" };
        if (dlg.ShowDialog() == true) Json.WriteAtomic(dlg.FileName, p);
    }

    [RelayCommand]
    private void ImportCustomProfile()
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var p = Json.Read<CustomProfile>(dlg.FileName);
            if (p == null) return;
            // Only identifiers that exist in this installation's catalog are kept (no arbitrary actions can be imported).
            p.TweakIds = p.TweakIds.Where(id => _shell.Services.Catalog.Find(id) != null).ToList();
            CustomProfiles.Insert(0, p);
            Json.WriteAtomic(ProfilesFile, CustomProfiles.ToList());
        }
        catch (Exception ex) { Dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private void DeleteCustomProfile(CustomProfile? p)
    {
        if (p == null) return;
        CustomProfiles.Remove(p);
        Json.WriteAtomic(ProfilesFile, CustomProfiles.ToList());
    }
}
