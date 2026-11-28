using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using FrameCore.BenchmarkManager.Detective;
using FrameCore.Core;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.UI.ViewModels;

namespace FrameCore.UI.Services;

/// <summary>
/// Automatic game profile, while FrameCore is open (it can stay minimised): when a game starts, the background
/// programs the chosen level considers dispensable step back (lowest CPU priority) and the fastest power plan is used;
/// when the game closes, everything is put back exactly as it was. Nothing is closed, nothing is written to disk.
/// </summary>
public sealed partial class GameProfileService : ObservableObject, IDisposable
{
    private static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private readonly ShellViewModel _shell;
    private readonly Timer _timer;
    private readonly Dictionary<int, ProcessPriorityClass> _lowered = new();
    private readonly object _gate = new();
    private Guid? _previousScheme;
    private int _gamePid;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _activeGame = "";
    [ObservableProperty] private string _statusText = "";

    public GameProfileService(ShellViewModel shell)
    {
        _shell = shell;
        _enabled = shell.Services.Settings.GameProfileEnabled;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(4));
    }

    partial void OnEnabledChanged(bool value)
    {
        _shell.Services.Settings.GameProfileEnabled = value;
        _shell.Services.Settings.Save();
        if (!value) End();
        StatusText = value ? Loc.T("gameprofile.waiting") : Loc.T("gameprofile.off");
    }

    private void Tick()
    {
        if (!Enabled) return;
        try
        {
            if (_gamePid != 0)
            {
                var alive = false;
                try { using var p = Process.GetProcessById(_gamePid); alive = !p.HasExited; } catch { }
                if (!alive) End();
                return;
            }
            var classifier = _shell.Detective.Classifier;
            var folders = GameLibraryFolders();
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    var isGame = classifier.IsKnownGame(p.ProcessName);
                    if (!isGame && folders.Count > 0)
                    {
                        bool hasWindow; try { hasWindow = p.MainWindowHandle != IntPtr.Zero; } catch { hasWindow = false; }
                        if (hasWindow && ProcessClassifier.ImagePath(p.Id) is { } path) isGame = folders.Any(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase));
                    }
                    if (isGame) { Begin(p.Id, p.ProcessName); return; }
                }
        }
        catch (Exception ex) { Log.Warn("GameProfile", ex.Message); }
    }

    private List<string>? _folders;
    private DateTime _foldersAt;
    private List<string> GameLibraryFolders()
    {
        if (_folders == null || DateTime.UtcNow - _foldersAt > TimeSpan.FromMinutes(10))
        {
            _folders = WindowsOptimization.Executors.GameLibraryFolders.Detect(_shell.Services.Registry).ToList();
            _foldersAt = DateTime.UtcNow;
        }
        return _folders;
    }

    private void Begin(int pid, string name)
    {
        _gamePid = pid;
        var level = _shell.Services.Settings.GameProfileLevel;
        // Background programs: classified without measuring (fast), then stepped back according to the level.
        var ctx = _shell.Detective.Context();
        var verdicts = ProcessClassifier.CollectFacts().Where(f => f.Pid != Environment.ProcessId && f.Pid != pid)
            .Select(f => _shell.Detective.Classifier.Classify(f, null, ctx)).ToList();
        var lowered = 0;
        foreach (var v in verdicts.Where(v => !v.Protected && !v.HasWindow && v.Tier <= ProcessTier.LowValue
                                              || ProcessClassifier.ActionFor(v, level) != ProcessAction.None))
            foreach (var p in v.Pids) if (LowerPriority(p)) lowered++;
        // Fastest power plan available, put back afterwards.
        try
        {
            var power = _shell.Services.Power;
            var target = power.SchemeExists(Ultimate) ? Ultimate : HighPerformance;
            var current = power.GetActiveScheme();
            if (current != target && power.SchemeExists(target)) { _previousScheme = current; power.SetActiveScheme(target); }
        }
        catch (Exception ex) { Log.Warn("GameProfile", "Power plan: " + ex.Message); }
        ShellViewModel.Ui(() =>
        {
            ActiveGame = name;
            StatusText = Loc.T("gameprofile.active", name, lowered);
            _shell.StatusText = StatusText;
        });
        Log.Info("GameProfile", $"{name} started: {lowered} background processes stepped back");
    }

    /// <summary>Lowest CPU priority for a background process (remembered and restored later).</summary>
    public bool LowerPriority(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            lock (_gate)
            {
                if (!_lowered.ContainsKey(pid)) _lowered[pid] = p.PriorityClass;
            }
            p.PriorityClass = ProcessPriorityClass.Idle;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Everything back as it was (priorities of the programs still running, power plan).</summary>
    public void End()
    {
        Dictionary<int, ProcessPriorityClass> copy;
        lock (_gate) { copy = new(_lowered); _lowered.Clear(); }
        foreach (var (pid, prio) in copy)
            try { using var p = Process.GetProcessById(pid); p.PriorityClass = prio; } catch { }
        if (_previousScheme is { } prev)
        {
            try { _shell.Services.Power.SetActiveScheme(prev); } catch { }
            _previousScheme = null;
        }
        var game = ActiveGame;
        _gamePid = 0;
        ShellViewModel.Ui(() =>
        {
            ActiveGame = "";
            StatusText = Enabled ? Loc.T("gameprofile.ended", game) : Loc.T("gameprofile.off");
        });
    }

    public void Dispose()
    {
        _timer.Dispose();
        End();
    }
}
