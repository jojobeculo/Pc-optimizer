using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.BenchmarkManager.Games;
using FrameCore.BenchmarkManager.QuickBench;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

/// <summary>One game in the explorer / showcase, already formatted for display.</summary>
public sealed partial class GameRow : ObservableObject
{
    public GameEstimate Estimate { get; init; } = null!;
    public GameGain? Gain { get; init; }
    public GameInfo Game => Estimate.Game;
    public string Name => Game.Name;
    public string Genre => Loc.T("genre." + Game.Genre);
    public string Engine => Game.Engine;
    public double FpsValue => Estimate.Fps ?? double.NaN;
    public string FpsText => Estimate.Fps is { } f ? f.ToString("0") : "—";
    public string RangeText => Estimate.FpsLow is { } lo && Estimate.FpsHigh is { } hi && lo != hi ? Loc.T("game.range", lo.ToString("0"), hi.ToString("0")) : "";
    public string LimiterText => Loc.T("limiter." + Estimate.Limiter);
    public string ConfidenceKey => Estimate.Fps == null ? "Neutral" : Estimate.Confidence switch { ResultConfidence.High => "Success", ResultConfidence.Medium => "Info", _ => "Warning" };
    public string ConfidenceText => Estimate.Fps == null ? Loc.T("basis.Insufficient") : Loc.T("confidence.level." + Estimate.Confidence);
    public string BasisText => Loc.T("basis." + Estimate.Basis);
    public string SettingsText => Loc.T("game.settings", Estimate.Settings.Width, Estimate.Settings.Height, Loc.T("preset." + Estimate.Settings.Preset));
    public string PresetText => Loc.T("preset." + Estimate.Settings.Preset);
    public string ResolutionText => $"{Estimate.Settings.Width} × {Estimate.Settings.Height}";
    public double BarFraction { get; set; }
    public bool HasGain => Gain?.GainPercent != null;
    public string AfterText => Gain?.AfterFps is { } a ? a.ToString("0") : "—";
    public string GainText => Gain?.GainPercent is { } g ? (g >= 0 ? "+" : "") + g.ToString("0.#") + " %" : Loc.T("game.gain.unknown");
    public string GainAbsText => Gain?.GainAbsolute is { } a ? (a >= 0 ? "+" : "") + a.ToString("0") + " " + Loc.T("unit.fps") : "";
    public string GainRangeText => Gain?.GainPercentLow is { } lo && Gain.GainPercentHigh is { } hi && Math.Abs(hi - lo) > 0.05 ? Loc.T("game.gainRange", lo.ToString("0.#"), hi.ToString("0.#")) : "";
    public string GainReason => Gain == null ? "" : string.Join(" ", Gain.ReasonKeys.Select(k => Loc.T(k)));
    public string Notes => string.Join("\n", Estimate.NoteKeys.Distinct().Select(k => "• " + Loc.T(k)).Concat(Game.Note != null ? new[] { "• " + Game.Note } : Array.Empty<string>()));
    public string CpuGpuText => Estimate.GpuBoundFps is { } g && Estimate.CpuBoundFps is { } c ? Loc.T("game.cpuGpu", g.ToString("0"), c.ToString("0")) : "";
}

public sealed record GenreChoice(string Key, string Label);

/// <summary>"Games &amp; FPS": showcase after optimisation, full library explorer and per-game details.</summary>
public sealed partial class GamesViewModel : ObservableObject, IDiagnosticConsumer, IPageActivated
{
    private readonly ShellViewModel _shell;
    private List<GameRow> _all = new();
    private EstimatorMachine? _machine;

    public ObservableCollection<GameRow> Showcase { get; } = new();
    public ObservableCollection<GameRow> Rows { get; } = new();
    public ObservableCollection<string> Genres { get; } = new();
    public ObservableCollection<string> Resolutions { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _genre = "all";
    [ObservableProperty] private string _sort = "popularity";
    [ObservableProperty] private string _resolution = "";
    [ObservableProperty] private string _preset = "auto";
    [ObservableProperty] private GameRow? _selected;
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private bool _ready;
    [ObservableProperty] private string _machineText = "";
    [ObservableProperty] private string _gainSourceText = "";
    [ObservableProperty] private bool _showExplorer;
    [ObservableProperty] private int _count;
    /// <summary>False when the graphics card cannot be identified (basic display driver, VM): no number is shown.</summary>
    [ObservableProperty] private bool _hasEstimates = true;

    public string Provenance => _shell.Services.Games.Provenance?.ToString() ?? "";
    public string ExcludedText => string.Join(" ", _shell.Services.Games.Excluded.Select(e => $"{e.Name} : {e.Reason}"));

    public GamesViewModel(ShellViewModel shell) => _shell = shell;

    public void OnDiagnostic(Services.DiagnosticResult r) { _machine = null; Ready = false; if (_shell.CurrentKey == "games") _ = LoadAsync(); }

    /// <summary>
    /// Layout check of the automated tour only (CI machines have no identifiable GPU): estimates for a stated
    /// reference machine, so the cards and the explorer are rendered with data. Never reachable from the UI.
    /// </summary>
    internal void UseMachineForLayoutCheck(EstimatorMachine m) { _machine = m; Ready = false; }
    public void OnEvaluationsRefreshed(Services.DiagnosticResult r) => Ready = false;
    public void OnActivated() { if (!Ready && !Loading) _ = LoadAsync(); }

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnGenreChanged(string value) => ApplyFilter();
    partial void OnSortChanged(string value) => ApplyFilter();
    partial void OnResolutionChanged(string value) { if (Ready) _ = LoadAsync(); }
    partial void OnPresetChanged(string value) { if (Ready) _ = LoadAsync(); }

    [RelayCommand] private void ToggleExplorer() => ShowExplorer = !ShowExplorer;
    [RelayCommand] private void SetGenre(string? g) => Genre = g ?? "all";
    [RelayCommand] private void SetResolution(string? r) { if (!string.IsNullOrEmpty(r)) Resolution = r; }
    public string GenreLabel(string g) => g == "all" ? Loc.T("games.allGenres") : Loc.T("genre." + g);
    public ObservableCollection<GenreChoice> GenreChoices { get; } = new();
    [RelayCommand] private void Select(GameRow? row) { Selected = row; ShowExplorer = true; }
    [RelayCommand] private void CloseDetail() => Selected = null;
    [RelayCommand] private void GoBenchmark() => _shell.NavigateCommand.Execute("benchmark");

    private (int w, int h) ParseResolution()
    {
        var parts = (Resolution ?? "").Split('×', 'x');
        return parts.Length == 2 && int.TryParse(parts[0].Trim(), out var w) && int.TryParse(parts[1].Trim(), out var h) ? (w, h) : (1920, 1080);
    }

    public async Task LoadAsync()
    {
        var diag = _shell.Diagnostic;
        if (diag == null) return;
        Loading = true;
        try
        {
            var lib = _shell.Services.Games;
            _machine ??= EstimatorMachine.From(diag.Profile);
            if (Resolutions.Count == 0)
            {
                var d = diag.Profile.Displays.FirstOrDefault(x => x.IsPrimary) ?? diag.Profile.Displays.FirstOrDefault();
                var native = d != null ? $"{d.CurrentWidth} × {d.CurrentHeight}" : "1920 × 1080";
                foreach (var r in new[] { native, "1920 × 1080", "2560 × 1440", "3840 × 2160" }.Distinct()) Resolutions.Add(r);
#pragma warning disable MVVMTK0034 // set without triggering a reload: this is the initial value
                _resolution = native;
#pragma warning restore MVVMTK0034
                OnPropertyChanged(nameof(Resolution));
            }
            var (w, h) = ParseResolution();
            var measurements = _shell.Services.Benchmarks.LoadAll()
                .Where(s => s.GameId != null && s.Width != null && s.Height != null && s.Runs.Count > 0)
                .Select(s => new GameMeasurement(s.GameId!, s.Width!.Value, s.Height!.Value,
                    Enum.TryParse<GraphicsPreset>(s.Preset, out var p) ? p : GraphicsPreset.High, s.Mean(x => x.AverageFps), s.Mean(x => x.Low1PercentFps), s.CreatedUtc))
                .ToList();
            var est = new FpsEstimator(lib, measurements);
            var (cmp, source) = LatestComparison();
            var applied = _shell.Services.Journal.ChangeSets.Where(c => c.Status == ChangeSetStatus.Applied && c.TweakId != null)
                .Select(c => _shell.Services.Catalog.Find(c.TweakId!)).Where(t => t != null).Cast<TweakDefinition>().ToList();
            var machine = _machine;
            var preset = Preset;
            var rows = await Task.Run(() => lib.Games.Select(g =>
            {
                var p = preset == "auto" ? est.SuggestPreset(g, machine, w, h) : Enum.Parse<GraphicsPreset>(preset);
                var e = est.Estimate(g, machine, new EstimateSettings(w, h, p));
                return new GameRow { Estimate = e, Gain = FpsEstimator.ProjectGain(e, cmp, applied) };
            }).ToList());
            var max = rows.Where(r => r.Estimate.Fps != null).Select(r => r.Estimate.Fps!.Value).DefaultIfEmpty(1).Max();
            foreach (var r in rows) r.BarFraction = r.Estimate.Fps is { } f ? Math.Min(1, f / Math.Max(1, max)) : 0;
            _all = rows;
            var picks = await Task.Run(() => est.PickShowcase(machine, w, h));
            Showcase.Clear();
            foreach (var p in picks) Showcase.Add(rows.First(r => r.Game.Id == p.Game.Id) is var row && row.Estimate.Settings.Preset == p.Settings.Preset
                ? row : new GameRow { Estimate = p, Gain = FpsEstimator.ProjectGain(p, cmp, applied) });
            if (GenreChoices.Count == 0)
            {
                GenreChoices.Add(new GenreChoice("all", Loc.T("games.allGenres")));
                foreach (var g in lib.Games.Select(g => g.Genre).Distinct().OrderBy(x => Loc.T("genre." + x))) GenreChoices.Add(new GenreChoice(g, Loc.T("genre." + g)));
            }
            MachineText = Loc.T("games.machine", _machine.CpuName ?? Loc.Unknown, _machine.GpuName ?? Loc.Unknown, Math.Round(_machine.RamGb));
            GainSourceText = source;
            Count = lib.Games.Count;
            HasEstimates = rows.Any(r => r.Estimate.Fps != null);
            ApplyFilter();
            Ready = true;
        }
        catch (Exception ex)
        {
            Core.Log.Error("Games", "Estimation failed", ex);
            _shell.StatusText = Loc.T("games.error", ex.Message);
        }
        finally { Loading = false; }
    }

    /// <summary>The two most recent quick benchmarks taken with a different set of active optimisations.</summary>
    private (QuickBenchComparison?, string) LatestComparison()
    {
        var all = _shell.Services.QuickBenches.LoadAll().Where(r => !r.Cancelled).OrderByDescending(r => r.StartedUtc).ToList();
        if (all.Count < 2) return (null, Loc.T("games.gain.noBench"));
        var after = all[0];
        var before = all.Skip(1).FirstOrDefault(r => !r.ActiveTweakIds.ToHashSet().SetEquals(after.ActiveTweakIds)) ?? all[1];
        return (QuickBenchComparer.Compare(before, after), Loc.T("games.gain.source", before.StartedUtc.ToLocalTime().ToString("g"), after.StartedUtc.ToLocalTime().ToString("g")));
    }

    private void ApplyFilter()
    {
        IEnumerable<GameRow> q = _all;
        if (!string.IsNullOrWhiteSpace(Search)) q = q.Where(r => r.Name.Contains(Search.Trim(), StringComparison.CurrentCultureIgnoreCase) || r.Engine.Contains(Search.Trim(), StringComparison.CurrentCultureIgnoreCase));
        if (Genre != "all") q = q.Where(r => r.Game.Genre == Genre);
        q = Sort switch
        {
            "name" => q.OrderBy(r => r.Name, StringComparer.CurrentCulture),
            "fpsDesc" => q.OrderByDescending(r => r.Estimate.Fps ?? -1),
            "fpsAsc" => q.OrderBy(r => r.Estimate.Fps ?? double.MaxValue),
            "gain" => q.OrderByDescending(r => r.Gain?.GainPercent ?? double.MinValue),
            _ => q.OrderBy(r => r.Game.Popularity).ThenBy(r => r.Name, StringComparer.CurrentCulture),
        };
        Rows.Clear();
        foreach (var r in q) Rows.Add(r);
    }
}
