using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.CpuManager;

namespace FrameCore.UI.ViewModels;

public sealed class BiosTab { public string Name { get; init; } = ""; public bool Active { get; init; } }
public sealed class BiosScreenRow { public string Label { get; init; } = ""; public string Value { get; init; } = ""; public bool Highlight { get; init; } public bool Filler { get; init; } }

/// <summary>Schematic BIOS screen of one brand with the option to change highlighted (drawn by FrameCore, not a screenshot).</summary>
public sealed class BiosScreen
{
    public string Title { get; init; } = "";
    public Brush Accent { get; init; } = Brushes.DodgerBlue;
    public Brush Bar { get; init; } = Brushes.Black;
    public IReadOnlyList<BiosTab> Tabs { get; init; } = Array.Empty<BiosTab>();
    public string Breadcrumb { get; init; } = "";
    public IReadOnlyList<BiosScreenRow> Rows { get; init; } = Array.Empty<BiosScreenRow>();
    public string Footer { get; init; } = "";
}

public sealed class WindowsScreen
{
    public string Title { get; init; } = "";
    public string Window { get; init; } = "";
    public string Breadcrumb { get; init; } = "";
    public IReadOnlyList<BiosScreenRow> Rows { get; init; } = Array.Empty<BiosScreenRow>();
    public string Why { get; init; } = "";
}

public sealed partial class CpuGuideStep : ObservableObject
{
    /// <summary>"1.", "2."… (order to follow in the BIOS).</summary>
    public string Number { get; init; } = "";
    public string SettingId { get; init; } = "";
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
    public string Why { get; init; } = "";
    public string Path { get; init; } = "";
    [ObservableProperty] private bool _isSelected;
}

/// <summary>Overclocking page, processor part: levels with concrete BIOS values, per-brand BIOS illustrations, expert settings.</summary>
public sealed partial class CpuGuideViewModel : ObservableObject
{
    private readonly CpuOcGuide _guide;
    private CpuOcFamily? _family;

    public ObservableCollection<BiosBrand> Brands { get; } = new();
    public ObservableCollection<CpuGuideStep> Steps { get; } = new();
    public ObservableCollection<ExpertRow> Expert { get; } = new();
    public ObservableCollection<WindowsScreen> WindowsScreens { get; } = new();
    public ObservableCollection<string> Notes { get; } = new();

    [ObservableProperty] private string _level = "Light";
    [ObservableProperty] private BiosBrand? _brand;
    [ObservableProperty] private CpuGuideStep? _selectedStep;
    [ObservableProperty] private BiosScreen? _screen;
    [ObservableProperty] private bool _available;
    [ObservableProperty] private string _familyTitle = "";
    [ObservableProperty] private string _gainText = "";
    [ObservableProperty] private string _brandHelp = "";
    [ObservableProperty] private string _unavailableText = "";
    public string Disclaimer => _guide.Disclaimer.ToString();

    public sealed class ExpertRow { public string Name { get; init; } = ""; public string Value { get; init; } = ""; public string Why { get; init; } = ""; }

    public CpuGuideViewModel(CpuOcGuide guide)
    {
        _guide = guide;
        foreach (var b in guide.Brands) Brands.Add(b);
        foreach (var w in guide.Windows)
            WindowsScreens.Add(new WindowsScreen
            {
                Title = w.Title.ToString(), Window = w.Window.ToString(), Breadcrumb = string.Join("  ›  ", w.Path.Select(p => p.ToString())), Why = w.Why.ToString(),
                Rows = w.Rows.Select((r, i) => new BiosScreenRow { Label = r.Label.ToString(), Value = r.Value.ToString(), Highlight = i == w.Highlight }).ToList(),
            });
    }

    public void OnProfile(MachineProfile p)
    {
        var id = CpuOcGuide.FamilyId(p.Cpu, p.Board);
        _family = _guide.Family(id);
        Available = _family != null;
        UnavailableText = _family != null ? "" : Loc.T(p.Board.IsLaptop ? "cpuoc.laptop" : p.Board.IsVirtualMachine ? "cpuoc.vm" : "cpuoc.unknown");
        FamilyTitle = _family?.Title.ToString() ?? "";
        var key = CpuTuningAdvisor.BiosVendorKey(p.Board);
        Brand ??= Brands.FirstOrDefault(b => b.Id == key) ?? Brands.FirstOrDefault();
        Notes.Clear();
        foreach (var n in _family?.Notes ?? new()) Notes.Add(n.ToString());
        Expert.Clear();
        foreach (var e in _family?.Advanced ?? new()) Expert.Add(new ExpertRow { Name = e.Name.ToString(), Value = e.Value.ToString(), Why = e.Why.ToString() });
        Build();
    }

    partial void OnLevelChanged(string value) => Build();
    partial void OnBrandChanged(BiosBrand? value) => Build();
    partial void OnSelectedStepChanged(CpuGuideStep? value)
    {
        foreach (var s in Steps) s.IsSelected = ReferenceEquals(s, value);
        Screen = value == null ? null : BuildScreen(value.SettingId);
    }

    [RelayCommand] private void SelectStep(CpuGuideStep? step) { if (step != null) SelectedStep = step; }

    private List<LevelSetting> LevelSettings() => _family != null && _family.Levels.TryGetValue(Level, out var l) ? l : new();

    private void Build()
    {
        var keep = SelectedStep?.SettingId;
        Steps.Clear();
        GainText = _family != null && _family.Gain.TryGetValue(Level, out var g) ? g.ToString() : "";
        BrandHelp = Brand == null ? "" : $"{Brand.Enter} · {Brand.Advanced} · {Brand.Save}";
        if (_family == null || Brand == null) { Screen = null; return; }
        foreach (var ls in LevelSettings())
        {
            if (!_guide.Settings.TryGetValue(ls.Setting, out var s)) continue;
            var path = s.Paths.TryGetValue(Brand.Id, out var bp) ? string.Join("  ›  ", new[] { bp.Tab }.Concat(bp.Path)) : "";
            Steps.Add(new CpuGuideStep { Number = (Steps.Count + 1) + ".", SettingId = ls.Setting, Label = s.Label.ToString(), Value = ls.Value.ToString(), Why = s.Why.ToString(), Path = path });
        }
        SelectedStep = Steps.FirstOrDefault(x => x.SettingId == keep) ?? Steps.FirstOrDefault();
    }

    private static Brush ToBrush(string hex)
    {
        try { var b = (Brush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; } catch { return Brushes.Gray; }
    }

    /// <summary>The brand's screen containing the option: sibling options of this level in the same menu are shown with their values.</summary>
    private BiosScreen? BuildScreen(string settingId)
    {
        if (Brand == null || !_guide.Settings.TryGetValue(settingId, out var s) || !s.Paths.TryGetValue(Brand.Id, out var target)) return null;
        var parent = target.Path.Take(target.Path.Count - 1).ToList();
        var rows = new List<BiosScreenRow>();
        foreach (var ls in LevelSettings())
        {
            if (!_guide.Settings.TryGetValue(ls.Setting, out var other) || !other.Paths.TryGetValue(Brand.Id, out var op)) continue;
            if (op.Tab != target.Tab || !op.Path.Take(op.Path.Count - 1).SequenceEqual(parent)) continue;
            rows.Add(new BiosScreenRow { Label = op.Path.Last(), Value = ls.Value.ToString(), Highlight = ls.Setting == settingId });
        }
        if (rows.Count < 3) { rows.Insert(0, new BiosScreenRow { Label = "…", Filler = true }); rows.Add(new BiosScreenRow { Label = "…", Filler = true }); }
        return new BiosScreen
        {
            Title = Loc.T("cpuoc.screen.title", Brand.Name), Accent = ToBrush(Brand.Accent), Bar = ToBrush(Brand.Bar),
            Tabs = Brand.Tabs.Select(t => new BiosTab { Name = t, Active = t == target.Tab }).ToList(),
            Breadcrumb = string.Join("  \\  ", new[] { target.Tab }.Concat(parent)),
            Rows = rows,
            Footer = $"{Brand.Save} · {Brand.Advanced}",
        };
    }
}
