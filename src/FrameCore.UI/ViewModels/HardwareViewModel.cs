using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FrameCore.BiosManager;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.DriverManager;
using FrameCore.MemoryManager;
using FrameCore.NetworkManager;
using FrameCore.UI.Infrastructure;
using FrameCore.UI.Services;

namespace FrameCore.UI.ViewModels;

public sealed class VolumeRow
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public double UsedPercent { get; init; }
}

public sealed class TitledRows
{
    public string Title { get; init; } = "";
    public List<PropertyRow> Rows { get; init; } = new();
    public List<VolumeRow> Volumes { get; init; } = new();
}

public sealed partial class HardwareViewModel : ObservableObject, IDiagnosticConsumer, IPageActivated
{
    private readonly ShellViewModel _shell;
    private readonly ProcessMemoryTracker _tracker = new();
    private DateTime _lastProcessSample = DateTime.MinValue;

    [ObservableProperty] private string _section = "cpu";
    [ObservableProperty] private double[] _cpuHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _gpuHistory = Array.Empty<double>();
    [ObservableProperty] private double[] _ramHistory = Array.Empty<double>();
    [ObservableProperty] private string _cpuLive = "";
    [ObservableProperty] private string _gpuLive = "";
    [ObservableProperty] private string _ramLive = "";
    [ObservableProperty] private double[] _perCore = Array.Empty<double>();
    [ObservableProperty] private string _latencyResult = "";
    [ObservableProperty] private bool _latencyRunning;
    [ObservableProperty] private string _memDiagResult = "";
    [ObservableProperty] private string? _biosUrl;
    [ObservableProperty] private string _biosNote = "";

    public ObservableCollection<PropertyRow> CpuRows { get; } = new();
    public ObservableCollection<TitledRows> GpuBlocks { get; } = new();
    public ObservableCollection<PropertyRow> RamRows { get; } = new();
    public ObservableCollection<PropertyRow> RamModules { get; } = new();
    public ObservableCollection<PropertyRow> BoardRows { get; } = new();
    public ObservableCollection<string> BiosSteps { get; } = new();
    public ObservableCollection<string> BiosRisks { get; } = new();
    public ObservableCollection<TitledRows> StorageBlocks { get; } = new();
    public ObservableCollection<TitledRows> DisplayBlocks { get; } = new();
    public ObservableCollection<TitledRows> NetworkBlocks { get; } = new();
    public ObservableCollection<PropertyRow> WindowsRows { get; } = new();
    public ObservableCollection<PropertyRow> SecurityRows { get; } = new();
    public ObservableCollection<ProcessMemoryGroup> TopProcesses { get; } = new();
    public ObservableCollection<string> MemoryAnomalies { get; } = new();
    public ObservableCollection<Finding> SectionFindings { get; } = new();

    public HardwareViewModel(ShellViewModel shell)
    {
        _shell = shell;
        shell.Monitoring.Updated += (_, s) => ShellViewModel.Ui(() => OnSample(s));
    }

    public void OnActivated() => RefreshFindings();
    partial void OnSectionChanged(string value) => RefreshFindings();

    private void RefreshFindings()
    {
        SectionFindings.Clear();
        var cats = Section switch
        {
            "cpu" => new[] { "Cpu" }, "gpu" => new[] { "Gpu" }, "ram" => new[] { "Ram" }, "board" => new[] { "Bios" }, "storage" => new[] { "Storage" },
            "display" => new[] { "Display" }, "network" => new[] { "Network" }, _ => new[] { "Windows", "Security" },
        };
        foreach (var f in _shell.Diagnostic?.Findings.Where(f => cats.Contains(f.Category)) ?? Enumerable.Empty<Finding>()) SectionFindings.Add(f);
    }

    private static string YesNo(bool? b) => b == null ? Loc.Unknown : Loc.T(b.Value ? "common.yes" : "common.no");
    private static string Tri(TriState t) => Loc.T("tri." + t);

    public void OnDiagnostic(DiagnosticResult r)
    {
        var p = r.Profile;
        CpuRows.Clear();
        foreach (var row in new[]
        {
            new PropertyRow(Loc.T("hw.cpu.model"), p.Cpu.Name),
            new PropertyRow(Loc.T("hw.cpu.vendor"), p.Cpu.Vendor.ToString()),
            new PropertyRow(Loc.T("hw.cpu.arch"), p.Cpu.Microarchitecture),
            new PropertyRow(Loc.T("hw.cpu.generation"), p.Cpu.Generation),
            new PropertyRow(Loc.T("hw.cpu.signature"), p.Cpu.Family > 0 ? $"Family {p.Cpu.Family:X} · Model {p.Cpu.Model:X} · Stepping {p.Cpu.Stepping}" : null),
            new PropertyRow(Loc.T("hw.cpu.cores"), $"{p.Cpu.Cores} / {p.Cpu.Threads}" + (p.Cpu.IsHybrid ? $" — {p.Cpu.PerformanceCores} P + {p.Cpu.EfficiencyCores} E" : "")),
            new PropertyRow(Loc.T("hw.cpu.base"), p.Cpu.BaseMhz is { } b ? $"{b} MHz" : null),
            new PropertyRow(Loc.T("hw.cpu.cache"), $"L2 {(p.Cpu.L2Kb is { } l2 ? l2 / 1024.0 + " Mo" : "?")} · L3 {(p.Cpu.L3Kb is { } l3 ? l3 / 1024 + " Mo" : "?")}" + (p.Cpu.L3Groups > 1 ? $" ({p.Cpu.L3Groups} × L3)" : "")),
            new PropertyRow(Loc.T("hw.cpu.x3d"), p.Cpu.IsX3D ? (p.Cpu.IsDualCcdX3D ? Loc.T("hw.cpu.x3dDual") : Loc.T("common.yes")) : Loc.T("common.no")),
            new PropertyRow(Loc.T("hw.cpu.unlocked"), Tri(p.Cpu.Unlocked)),
            new PropertyRow(Loc.T("hw.cpu.socket"), p.Cpu.Socket),
            new PropertyRow(Loc.T("hw.cpu.features"), string.Join(", ", p.Cpu.Features)),
        }) CpuRows.Add(row);

        GpuBlocks.Clear();
        foreach (var g in p.Gpus.Where(g => !g.IsSoftware))
            GpuBlocks.Add(new TitledRows
            {
                Title = g.Name + (g.IsIntegrated ? $" ({Loc.T("hw.gpu.integrated")})" : ""),
                Rows = new()
                {
                    new(Loc.T("hw.gpu.vendor"), g.Vendor.ToString()), new(Loc.T("hw.gpu.arch"), g.Architecture),
                    new(Loc.T("hw.gpu.vram"), g.DedicatedVramBytes is > 0 and var v ? BytesConverter.Format(v) : null),
                    new(Loc.T("hw.gpu.driver"), g.DriverVersion is null ? null : $"{g.DriverVersion} ({g.DriverDate:d})"), new(Loc.T("hw.gpu.provider"), g.DriverProvider),
                    new(Loc.T("hw.gpu.wddm"), g.WddmVersion is { } w ? $"WDDM {w / 1000}.{w % 1000 / 100}" : null),
                    new(Loc.T("hw.gpu.hags"), g.HagsSupported == false ? Loc.T("hw.gpu.hagsUnsupported") : YesNo(g.HagsEnabled)),
                    new(Loc.T("hw.gpu.rebar"), Loc.T("rebar." + g.ResizableBar) + (g.ResizableBarDetail != null ? " — " + g.ResizableBarDetail : "")),
                    new(Loc.T("hw.gpu.pcie"), g.PcieCurrentGen != null ? $"Gen {g.PcieCurrentGen} x{g.PcieCurrentWidth} (max Gen {g.PcieMaxGen} x{g.PcieMaxWidth})" : null),
                    new(Loc.T("hw.gpu.vbios"), g.VbiosVersion), new(Loc.T("hw.gpu.pnp"), g.PnpDeviceId),
                },
            });

        var m = p.Memory;
        RamRows.Clear();
        foreach (var row in new[]
        {
            new PropertyRow(Loc.T("hw.ram.total"), BytesConverter.Format(m.TotalBytes)), new PropertyRow(Loc.T("hw.ram.type"), m.Type),
            new PropertyRow(Loc.T("hw.ram.speed"), m.ConfiguredSpeedMts is { } s ? Loc.T("hw.ram.speedValue", s, s / 2) : null),
            new PropertyRow(Loc.T("hw.ram.rated"), m.Modules.Select(x => x.PartNumberSpeedMts).FirstOrDefault(x => x != null) is { } kit ? $"{kit} MT/s" : Loc.Unknown),
            new PropertyRow(Loc.T("hw.ram.jedec"), m.Modules.Select(x => x.RatedSpeedMts).FirstOrDefault(x => x != null) is { } j ? $"{j} MT/s" : null),
            new PropertyRow(Loc.T("hw.ram.channels"), Loc.T("channels." + m.Channels) + " — " + m.ChannelReasoning),
            new PropertyRow(Loc.T("hw.ram.xmp"), Loc.T("xmp." + m.Xmp) + " — " + m.XmpReasoning),
            new PropertyRow(Loc.T("hw.ram.slots"), m.SlotsTotal is { } sl ? $"{m.Modules.Count} / {sl}" : m.Modules.Count.ToString()),
            new PropertyRow(Loc.T("hw.ram.timings"), Loc.T("hw.ram.timingsNote")),
        }) RamRows.Add(row);
        RamModules.Clear();
        foreach (var mod in m.Modules)
            RamModules.Add(new PropertyRow(mod.Slot ?? "?", $"{BytesConverter.Format(mod.CapacityBytes)} · {mod.Manufacturer ?? "?"} {mod.PartNumber ?? ""} · {mod.ConfiguredSpeedMts?.ToString() ?? "?"} MT/s · {mod.FormFactor} {(mod.ConfiguredVoltageMv is { } mv ? $"· {mv / 1000.0:0.00} V" : "")}"));

        BoardRows.Clear();
        foreach (var row in new[]
        {
            new PropertyRow(Loc.T("hw.board.model"), $"{p.Board.Manufacturer} {p.Board.Product}".Trim()), new PropertyRow(Loc.T("hw.board.revision"), p.Board.Version),
            new PropertyRow(Loc.T("hw.board.system"), $"{p.Board.SystemManufacturer} {p.Board.SystemModel}".Trim()), new PropertyRow(Loc.T("hw.board.chipset"), p.Board.Chipset),
            new PropertyRow(Loc.T("hw.board.formfactor"), p.Board.IsLaptop ? Loc.T("hw.laptop") : Loc.T("hw.desktop")),
            new PropertyRow(Loc.T("hw.bios.vendor"), p.Firmware.Vendor), new PropertyRow(Loc.T("hw.bios.version"), p.Firmware.Version),
            new PropertyRow(Loc.T("hw.bios.date"), p.Firmware.ReleaseDate?.ToString("d", CultureInfo.CurrentCulture)),
            new PropertyRow(Loc.T("hw.bios.mode"), p.Firmware.Mode == FirmwareMode.Unknown ? null : p.Firmware.Mode.ToString().ToUpperInvariant()),
            new PropertyRow(Loc.T("hw.bios.secureboot"), Tri(p.Firmware.SecureBoot)), new PropertyRow(Loc.T("hw.bios.tpm"), p.Firmware.TpmPresent == true ? "TPM " + p.Firmware.TpmVersion : p.Firmware.TpmPresent == false ? Loc.T("common.no") : null),
            new PropertyRow(Loc.T("hw.bios.rebar"), string.Join(", ", p.Gpus.Where(g => !g.IsIntegrated && !g.IsSoftware).Select(g => Loc.T("rebar." + g.ResizableBar)))),
            new PropertyRow(Loc.T("hw.bios.modernStandby"), YesNo(p.Firmware.ModernStandby)),
            new PropertyRow(Loc.T("hw.bios.supported"), Loc.T("hw.bios.supportedNote")),
        }) BoardRows.Add(row);
        BiosUrl = BoardSupport.SupportUrl(p.Board);
        var guidance = FirmwareAdvisor.UpdateGuidance(p, BiosUrl, BoardSupport.VendorUpdateTool(p.Board));
        BiosNote = guidance.Note + (guidance.VendorTool != null ? "\n" + Loc.T("bios.vendorTool", guidance.VendorTool) : "");
        BiosSteps.Clear(); foreach (var s in guidance.Steps) BiosSteps.Add(s);
        BiosRisks.Clear(); foreach (var s in guidance.Risks) BiosRisks.Add(s);

        StorageBlocks.Clear();
        foreach (var d in p.Storage)
            StorageBlocks.Add(new TitledRows
            {
                Title = d.FriendlyName,
                Rows = new()
                {
                    new(Loc.T("hw.storage.type"), $"{Loc.T("media." + d.Media)} · {d.Bus}"), new(Loc.T("hw.storage.size"), BytesConverter.Format(d.SizeBytes)),
                    new(Loc.T("hw.storage.health"), d.Health == null ? null : Loc.T("health." + d.Health)), new(Loc.T("hw.storage.temp"), d.TemperatureC is { } t ? $"{t} °C" : Loc.T("hw.storage.needsAdmin")),
                    new(Loc.T("hw.storage.wear"), d.WearPercent is { } w ? $"{w} %" : null), new(Loc.T("hw.storage.hours"), d.PowerOnHours?.ToString()),
                    new(Loc.T("hw.storage.firmware"), d.Firmware),
                },
                Volumes = d.Volumes.Select(v => new VolumeRow
                {
                    Title = $"{v.Letter}: {v.Label}", UsedPercent = 100 - v.FreePercent,
                    Detail = Loc.T("hw.storage.volume", BytesConverter.Format(v.FreeBytes), BytesConverter.Format(v.SizeBytes), v.FileSystem ?? "?"),
                }).ToList(),
            });

        DisplayBlocks.Clear();
        foreach (var d in p.Displays)
            DisplayBlocks.Add(new TitledRows
            {
                Title = (d.FriendlyName ?? d.DeviceName) + (d.IsPrimary ? $" ({Loc.T("hw.display.primary")})" : ""),
                Rows = new()
                {
                    new(Loc.T("hw.display.mode"), $"{d.CurrentWidth} × {d.CurrentHeight} @ {d.CurrentRefreshHz:0.###} Hz"),
                    new(Loc.T("hw.display.max"), d.MaxRefreshAtCurrentResolution is { } mx ? $"{mx:0} Hz" : null),
                    new(Loc.T("hw.display.native"), d.NativeWidth is { } nw ? $"{nw} × {d.NativeHeight}" : null),
                    new(Loc.T("hw.display.edidMax"), d.EdidMaxRefreshHz is { } em ? $"{em} Hz" : null),
                    new(Loc.T("hw.display.connection"), d.Connection), new(Loc.T("hw.display.adapter"), d.AdapterName),
                    new(Loc.T("hw.display.vrr"), d.VrrMinHz != null ? $"{d.VrrMinHz}–{d.VrrMaxHz} Hz" + (d.FreeSyncAdvertised == true ? " · FreeSync" : "") + (d.HdmiVrrAdvertised == true ? " · HDMI VRR" : "") + (d.AdaptiveSyncAdvertised == true ? " · Adaptive-Sync" : "") : (d.AdaptiveSyncAdvertised == false ? Loc.T("hw.display.noVrr") : null)),
                    new(Loc.T("hw.display.gsync"), Loc.T("hw.display.gsyncNote")),
                    new(Loc.T("hw.display.hdr"), d.HdrSupported == null ? null : YesNo(d.HdrSupported) + (d.HdrEnabled == true ? " (" + Loc.T("state.enabled") + ")" : "")),
                    new(Loc.T("hw.display.bpc"), d.BitsPerColor?.ToString()),
                    new(Loc.T("hw.display.link"), d.LinkLimitNote ?? Loc.T("hw.display.linkOk")),
                },
            });

        NetworkBlocks.Clear();
        foreach (var n in p.Network.Where(n => n.IsPhysical))
            NetworkBlocks.Add(new TitledRows
            {
                Title = $"{n.Name} — {n.Description}",
                Rows = new List<PropertyRow>
                {
                    new(Loc.T("hw.net.type"), n.Type), new(Loc.T("hw.net.status"), n.IsUp ? Loc.T("hw.net.up") : Loc.T("hw.net.down")),
                    new(Loc.T("hw.net.speed"), n.SpeedMbps > 0 ? $"{n.SpeedMbps} Mb/s" : null), new(Loc.T("hw.net.gateway"), n.Gateway), new(Loc.T("hw.net.dns"), string.Join(", ", n.DnsServers)),
                    new(Loc.T("hw.net.driver"), n.DriverVersion),
                }.Concat(n.AdvancedProperties.Select(kv => new PropertyRow(kv.Key, kv.Value))).ToList(),
            });

        WindowsRows.Clear();
        foreach (var row in new[]
        {
            new PropertyRow(Loc.T("hw.os.product"), p.Os.ProductName), new PropertyRow(Loc.T("hw.os.edition"), p.Os.EditionId),
            new PropertyRow(Loc.T("hw.os.version"), p.Os.DisplayVersion), new PropertyRow(Loc.T("hw.os.build"), p.Os.FullBuild),
            new PropertyRow(Loc.T("hw.os.kernel"), p.Os.KernelVersion), new PropertyRow(Loc.T("hw.os.arch"), p.Os.Architecture),
            new PropertyRow(Loc.T("hw.os.servicing"), p.Os.ServicingNote), new PropertyRow(Loc.T("hw.os.pendingReboot"), YesNo(p.Os.PendingReboot)),
            new PropertyRow(Loc.T("hw.os.lastBoot"), p.Os.LastBootUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
            new PropertyRow(Loc.T("hw.os.gameMode"), StatusOf(r, "gaming.gamemode")), new PropertyRow(Loc.T("hw.os.windowed"), StatusOf(r, "graphics.windowed")),
            new PropertyRow(Loc.T("hw.os.hags"), StatusOf(r, "graphics.hags")),
        }) WindowsRows.Add(row);
        SecurityRows.Clear();
        foreach (var row in new[]
        {
            new PropertyRow(Loc.T("hw.sec.vbs"), Tri(p.Security.VbsRunning)), new PropertyRow(Loc.T("hw.sec.hvci"), Tri(p.Security.HvciRunning)),
            new PropertyRow(Loc.T("hw.sec.mbec"), Tri(p.Security.MbecAvailable)), new PropertyRow(Loc.T("hw.sec.vmp"), Tri(p.Security.VirtualMachinePlatformEnabled)),
            new PropertyRow(Loc.T("hw.sec.hyperv"), Tri(p.Security.HyperVEnabled)), new PropertyRow(Loc.T("hw.sec.defender"), Tri(p.Security.DefenderRealTime)),
            new PropertyRow(Loc.T("hw.sec.firewall"), Tri(p.Security.FirewallEnabled)), new PropertyRow(Loc.T("hw.bios.secureboot"), Tri(p.Firmware.SecureBoot)),
        }) SecurityRows.Add(row);
        RefreshFindings();
    }

    private static string StatusOf(DiagnosticResult r, string id)
    {
        var e = r.Evaluations.FirstOrDefault(x => x.Tweak.Id == id);
        return e == null ? Loc.Unknown : Loc.T("status." + e.Status) + (e.ActionStates.Count > 0 ? " — " + e.CurrentSummary : "");
    }

    private void OnSample(MonitorSnapshot s)
    {
        CpuHistory = _shell.Monitoring.CpuHistory.Snapshot();
        GpuHistory = _shell.Monitoring.GpuHistory.Snapshot();
        RamHistory = _shell.Monitoring.RamHistory.Snapshot();
        PerCore = s.Cpu.PerCore.ToArray();
        CpuLive = Loc.T("hw.live.cpu", s.Cpu.TotalPercent?.ToString("0") ?? "–", s.Cpu.EffectiveMhz?.ToString("0") ?? "–",
            s.CpuTemp?.ToString("0") ?? Loc.Unknown, s.Cpu.PerformanceLimitPercent?.ToString("0") ?? "–", s.CpuPowerW?.ToString("0") ?? Loc.Unknown);
        var g = s.PrimaryGpu;
        GpuLive = g == null ? "" : Loc.T("hw.live.gpu", g.UtilizationPercent?.ToString("0") ?? "–", (g.TemperatureC ?? s.GpuTemp)?.ToString("0") ?? Loc.Unknown,
            g.CoreClockMhz?.ToString("0") ?? Loc.Unknown, g.PowerW?.ToString("0") ?? Loc.Unknown,
            g.DedicatedUsedBytes is { } u ? BytesConverter.Format(u) : "–", g.LimitReasons.Count > 0 ? string.Join(", ", g.LimitReasons) : "–");
        var m = s.Memory;
        RamLive = Loc.T("hw.live.ram", BytesConverter.Format(m.UsedBytes), BytesConverter.Format(m.TotalBytes), BytesConverter.Format(m.AvailableBytes),
            BytesConverter.Format(m.CommitBytes), BytesConverter.Format(m.CommitLimitBytes), BytesConverter.Format(m.StandbyBytes), BytesConverter.Format(m.ModifiedBytes),
            m.HardFaultsPerSec?.ToString("0") ?? "–");
        if (Section == "ram" && DateTime.Now - _lastProcessSample > TimeSpan.FromSeconds(5))
        {
            _lastProcessSample = DateTime.Now;
            Task.Run(() => _tracker.Sample()).ContinueWith(t =>
            {
                if (t.Result is not { } groups) return;
                ShellViewModel.Ui(() =>
                {
                    TopProcesses.Clear();
                    foreach (var gr in groups.Take(15)) TopProcesses.Add(gr);
                    MemoryAnomalies.Clear();
                    foreach (var a in _tracker.Anomalies()) MemoryAnomalies.Add(Loc.T("ram.anomaly", a.Name, (a.GrowthMbPerMinute).ToString("0"), a.Over.TotalMinutes.ToString("0")));
                });
            });
        }
    }

    [RelayCommand]
    private async Task TestLatency()
    {
        LatencyRunning = true;
        LatencyResult = Loc.T("net.testing");
        try
        {
            var gateway = _shell.Diagnostic?.Profile.Network.FirstOrDefault(n => n.IsUp && n.Gateway != null)?.Gateway;
            var lines = new List<string>();
            foreach (var target in new[] { gateway, "1.1.1.1", "8.8.8.8" }.Where(t => t != null))
            {
                var s = await LatencyTester.PingAsync(target!, 20, 150);
                lines.Add(Loc.T("net.pingResult", target, s.AvgMs?.ToString("0.0") ?? "–", s.MinMs?.ToString("0") ?? "–", s.MaxMs?.ToString("0") ?? "–", s.JitterMs?.ToString("0.0") ?? "–", s.LossPercent.ToString("0")));
            }
            var dns = await LatencyTester.DnsResolveMsAsync("www.microsoft.com");
            lines.Add(Loc.T("net.dnsResult", dns?.ToString("0") ?? "–"));
            lines.Add(Loc.T("net.dnsNote"));
            LatencyResult = string.Join("\n", lines);
        }
        catch (Exception ex) { LatencyResult = ex.Message; }
        finally { LatencyRunning = false; }
    }

    [RelayCommand]
    private void MemoryDiagnosticLaunch()
    {
        if (!Dialogs.Confirm(Loc.T("ram.diag.title"), Loc.T("ram.diag.confirm"))) return;
        try { MemoryDiagnostic.Launch(); } catch (Exception ex) { Dialogs.Error(ex.Message); }
    }

    [RelayCommand]
    private void MemoryDiagnosticResults()
    {
        var results = MemoryDiagnostic.LastResults();
        MemDiagResult = results.Count == 0 ? Loc.T("ram.diag.none")
            : string.Join("\n", results.Select(e => $"{e.Time:g} — {(e.Id is 1101 or 1201 ? Loc.T("ram.diag.ok") : e.Id is 1102 or 1202 ? Loc.T("ram.diag.errors") : e.Id.ToString())}"));
    }

    [RelayCommand] private void OpenBiosPage() { if (BiosUrl != null) Dialogs.OpenUri(BiosUrl); }
    [RelayCommand] private Task ApplyMaxRefresh() => _shell.Services.Catalog.Find("display.maxRefresh") is { } t ? _shell.ApplySingleAsync(t) : Task.CompletedTask;
    [RelayCommand] private void OpenUri(string uri) => Dialogs.OpenUri(uri);
}
