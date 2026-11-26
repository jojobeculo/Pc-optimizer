using System.Text.Json;
using FrameCore.Core;
using FrameCore.Core.Catalog;
using FrameCore.Core.Engine;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.Core.Platform;
using FrameCore.HardwareDetection;
using FrameCore.WindowsOptimization.Executors;
using FrameCore.WindowsOptimization.Platform;

namespace FrameCore.Tests;

public static class Repo
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "catalog", "tweaks"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repository root not found");
        }
    }

    public static string Catalog => Path.Combine(Root, "catalog");

    private static bool _locLoaded;
    public static void LoadLoc()
    {
        if (_locLoaded) return;
        Loc.Instance.LoadDirectory(Path.Combine(Root, "lang"));
        Loc.Instance.SetLanguage("fr");
        WindowsVersions.UseEntries(JsonSerializer.Deserialize<List<WindowsVersionEntry>>(
            File.ReadAllText(Path.Combine(Catalog, "knowledge", "windows-versions.json")), Json.Options)!);
        _locLoaded = true;
    }
}

public sealed class InMemoryJournal : IChangeJournal
{
    private readonly List<ChangeSet> _sets = new();
    private readonly List<ChangeBatch> _batches = new();
    public IReadOnlyList<ChangeSet> ChangeSets => _sets;
    public IReadOnlyList<ChangeBatch> Batches => _batches;
    public int SaveCount { get; private set; }
    public event EventHandler? Changed;
    public void Save(ChangeSet changeSet) { if (!_sets.Contains(changeSet)) _sets.Add(changeSet); SaveCount++; Changed?.Invoke(this, EventArgs.Empty); }
    public void Save(ChangeBatch batch) { if (!_batches.Contains(batch)) _batches.Add(batch); }
    public ChangeSet? Find(string id) => _sets.FirstOrDefault(s => s.Id == id);
}

/// <summary>Fake executor that fails on demand (to exercise transactional rollback).</summary>
public sealed class FailingExecutor : IActionExecutor
{
    public string Type => "fail";
    public bool RequiresElevation(ActionDefinition a) => false;
    public ActionState Read(ActionDefinition a) => new() { Matches = false, Current = "x", Target = "y" };
    public StateSnapshot Capture(ActionDefinition a) => StateSnapshot.Of("x");
    public void Apply(ActionDefinition a) => throw new IOException("simulated failure");
    public void Restore(ActionDefinition a, StateSnapshot s) { }
    public bool Verify(ActionDefinition a, StateSnapshot s) => true;
}

public sealed class Sim
{
    public InMemoryRegistry Registry { get; } = new();
    public InMemoryServiceConfigurator Services { get; } = new();
    public InMemoryTaskScheduler Tasks { get; } = new();
    public InMemoryPowerAccess Power { get; } = new();
    public InMemoryJournal Journal { get; } = new();
    public TweakEngine Engine { get; }
    public ExecutorRegistry Executors { get; }

    public Sim()
    {
        Repo.LoadLoc();
        Executors = new ExecutorRegistry(new IActionExecutor[]
        {
            new RegistryExecutor(Registry), new DxSettingExecutor(Registry), new ServiceExecutor(Services), new TaskExecutor(Tasks),
            new PowerSettingExecutor(Power), new PowerSchemeExecutor(Power), new PowerOverlayExecutor(Power), new FailingExecutor(),
        });
        Engine = new TweakEngine(new LocalDispatcher(Executors), Journal);
    }

    public static MachineProfile DesktopProfile(bool dualX3D = false, CpuVendor vendor = CpuVendor.Intel) => new()
    {
        Os = new OsInfo { Build = 26200, ProductName = "Windows 11 Pro", EditionId = "Professional" },
        Cpu = new CpuInfo { Vendor = vendor, Name = dualX3D ? "AMD Ryzen 9 9950X3D 16-Core Processor" : "Intel Core i7-14700K", Cores = 16, Threads = 32, IsX3D = dualX3D, IsDualCcdX3D = dualX3D, L3Groups = dualX3D ? 2 : 1 },
        Gpus = { new GpuInfo { Name = "NVIDIA GeForce RTX 4070", Vendor = GpuVendor.Nvidia, HagsSupported = true, HagsEnabled = false, DedicatedVramBytes = 12L << 30 } },
        Board = new BoardInfo { IsLaptop = false },
        Memory = new MemoryInfo { TotalBytes = 32L << 30 },
        Storage = { new StorageDevice { Media = MediaKind.Ssd, IsSystemDisk = true }, new StorageDevice { Media = MediaKind.Hdd } },
        Displays = { new DisplayInfo { AdaptiveSyncAdvertised = true, HdrSupported = false } },
        Network = { new NetworkAdapterInfo { IsPhysical = true, Type = "Ethernet", IsUp = true } },
        Security = new SecurityInfo { VirtualMachinePlatformEnabled = TriState.Yes },
    };

    public static MachineFacts Facts(MachineProfile p) =>
        MachineFactsBuilder.Build(p, new[] { PowerGuids.Balanced, PowerGuids.HighPerformance }, physicalPrinters: 0);

    public static JsonElement J(object o) => JsonSerializer.SerializeToElement(o);
}
