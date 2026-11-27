using FrameCore.BackupManager;
using FrameCore.Core.Hardware;
using FrameCore.BenchmarkManager;
using FrameCore.Core;
using FrameCore.Core.Catalog;
using FrameCore.Core.Elevation;
using FrameCore.Core.Engine;
using FrameCore.Core.Platform;
using FrameCore.DisplayManager;
using FrameCore.GpuManager;
using FrameCore.HardwareDetection;
using FrameCore.MemoryManager;
using FrameCore.NetworkManager;
using FrameCore.StartupManager;
using FrameCore.StorageManager;
using FrameCore.WindowsOptimization.Executors;
using FrameCore.WindowsOptimization.Platform;

namespace FrameCore.UI.Services;

/// <summary>Composition root shared by the UI process and the elevated worker.</summary>
public sealed class AppServices
{
    public AppSettings Settings { get; }
    public IRegistryAccess Registry { get; } = new WindowsRegistryAccess();
    public IServiceConfigurator ServiceConfig { get; } = new WindowsServiceConfigurator();
    public ITaskSchedulerAccess Tasks { get; } = new WindowsTaskSchedulerAccess();
    public IPowerAccess Power { get; } = new WindowsPowerAccess();
    public TweakCatalog Catalog { get; }
    public ExecutorRegistry Executors { get; }
    public JsonChangeJournal Journal { get; }
    public ElevationBroker Broker { get; } = new();
    public IActionDispatcher Dispatcher { get; }
    public TweakEngine Engine { get; }
    public StartupKnowledgeBase StartupKb { get; }
    public BenchmarkStore Benchmarks { get; } = new();
    public FrameCore.BenchmarkManager.QuickBench.JsonFolderStore<FrameCore.BenchmarkManager.QuickBench.QuickBenchResult> QuickBenches { get; } = FrameCore.BenchmarkManager.QuickBench.Stores.QuickBench();
    public FrameCore.BenchmarkManager.QuickBench.JsonFolderStore<FrameCore.BenchmarkManager.Verification.VerifyRepairReport> VerificationReports { get; } = FrameCore.BenchmarkManager.QuickBench.Stores.Verification();
    private FrameCore.BenchmarkManager.Games.GameLibrary? _games;
    /// <summary>Game library and hardware reference tables, loaded on first use (keeps start-up fast).</summary>
    private FrameCore.CpuManager.CpuOcGuide? _cpuOcGuide;
    public FrameCore.CpuManager.CpuOcGuide CpuOcGuide => _cpuOcGuide ??= FrameCore.CpuManager.CpuOcGuide.Load(Path.Combine(AppPaths.CatalogDir, "knowledge"));
    public FrameCore.BenchmarkManager.Games.GameLibrary Games => _games ??= FrameCore.BenchmarkManager.Games.GameLibrary.Load(Path.Combine(AppPaths.CatalogDir, "knowledge"));

    public AppServices(AppSettings settings, bool forWorker = false)
    {
        Settings = settings;
        Catalog = TweakCatalog.LoadFromDirectory(AppPaths.CatalogDir);
        foreach (var e in Catalog.LoadErrors) Log.Warn("Catalog", e);
        Executors = CreateExecutors(Registry, ServiceConfig, Tasks, Power);
        Journal = new JsonChangeJournal();
        Dispatcher = forWorker || AppInfo.IsElevated
            ? new LocalDispatcher(Executors)
            : new RoutingDispatcher(Executors, () => Broker, AppInfo.IsElevated);
        Engine = new TweakEngine(Dispatcher, Journal);
        StartupKb = StartupKnowledgeBase.Load();
    }

    private static readonly IDefenderPreferences Defender = new WmiDefenderPreferences();

    public static ExecutorRegistry CreateExecutors(IRegistryAccess reg, IServiceConfigurator svc, ITaskSchedulerAccess tasks, IPowerAccess power)
    {
        var handlers = new IActionHandler[]
        {
            new TempCleanupHandler(system: false), new TempCleanupHandler(system: true), new RetrimHandler(), new PagefileHandler(),
            new HibernateHandler(reg), new MaxRefreshHandler(), new AutotuningHandler(),
            // Extreme level only (consent-gated): scoped Microsoft Defender preferences through its official interface.
            new DefenderScanCpuHandler(Defender), new DefenderGameFolderExclusionsHandler(Defender, () => GameLibraryFolders.Detect(reg)),
        };
        return new ExecutorRegistry(new IActionExecutor[]
        {
            new RegistryExecutor(reg), new DxSettingExecutor(reg), new ServiceExecutor(svc), new TaskExecutor(tasks),
            new PowerSettingExecutor(power), new PowerSchemeExecutor(power), new PowerOverlayExecutor(power), new BcdRemoveExecutor(),
            new SpiExecutor(), new OptionalFeatureExecutor(), new NetAdapterPropertyExecutor(reg), new HandlerExecutor(handlers),
        });
    }

    public HardwareScanner CreateScanner() => new(Registry, new IHardwareEnricher[]
    {
        new GpuVendorEnricher(), new MemoryConfigAnalyzer(), new StorageScanner(Registry), new DisplayScanner(Registry), new NetworkScanner(Registry),
    });

    public StartupScanner CreateStartupScanner() => new(Registry, Tasks, StartupKb);
}
