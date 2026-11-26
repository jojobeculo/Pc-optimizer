using System.Windows;
using FrameCore.Core;
using FrameCore.Core.Elevation;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.UI.Infrastructure;

namespace FrameCore.UI.Services;

public sealed class BatchResult
{
    public int Applied { get; set; }
    public int Failed { get; set; }
    public bool RebootRequired { get; set; }
    public bool SignOutRequired { get; set; }
    public List<string> Lines { get; } = new();
    public string? RestorePointMessage { get; set; }
}

/// <summary>UI-level orchestration of applying/restoring optimizations (confirmation, elevation, restore point, display safety).</summary>
public sealed class TweakService
{
    private readonly AppServices _s;
    private readonly PrivilegedClient _priv;
    private readonly DiagnosticService _diag;

    public TweakService(AppServices s, PrivilegedClient priv, DiagnosticService diag) { _s = s; _priv = priv; _diag = diag; }

    public bool NeedsElevation(IEnumerable<TweakDefinition> tweaks) =>
        tweaks.SelectMany(t => t.Actions).Any(a => _s.Dispatcher.NeedsElevation(a));

    /// <summary>Applies tweaks one by one (each one transactional). Failures do not stop the batch; each is reported.</summary>
    public async Task<BatchResult> ApplyAsync(IReadOnlyList<TweakDefinition> tweaks, LocalizedText title, ProfileLevel? profile, bool createRestorePoint, IProgress<string>? progress = null)
    {
        var result = new BatchResult();
        if (tweaks.Count == 0) return result;
        var facts = _diag.Last?.Facts ?? _diag.BuildFacts(_s.CreateScanner().Scan());
        var needsAdmin = NeedsElevation(tweaks) || createRestorePoint;
        if (needsAdmin && !_priv.IsConnected)
        {
            try { await Task.Run(() => _priv.Ensure()); }
            catch (ElevationDeclinedException)
            {
                result.Lines.Add(Loc.T("elevation.declinedApplyingUser"));
                tweaks = tweaks.Where(t => !NeedsElevation(new[] { t })).ToList();
                createRestorePoint = false;
            }
        }
        var batch = new ChangeBatch { Title = title, Profile = profile };
        if (createRestorePoint)
        {
            progress?.Report(Loc.T("restorepoint.creating"));
            var rp = await Task.Run(() => _priv.CreateRestorePoint("FrameCore: " + title.ToString()));
            batch.RestorePointCreated = rp.Created;
            batch.RestorePointNote = result.RestorePointMessage = rp.Message;
        }
        foreach (var t in tweaks)
        {
            progress?.Report(t.Name.ToString());
            var r = await Task.Run(() => _s.Engine.Apply(t, facts, batch.Id));
            if (r.ChangeSet != null) batch.ChangeSetIds.Add(r.ChangeSet.Id);
            if (r.Success)
            {
                result.Applied++;
                result.Lines.Add("✓ " + t.Name);
                if (t.Restart == RestartRequirement.Reboot) result.RebootRequired = true;
                if (t.Restart is RestartRequirement.SignOut or RestartRequirement.ExplorerRestart) result.SignOutRequired = true;
                if (t.Actions.Any(a => a.Type == "handler" && a.Name == "display.maxRefresh") && r.ChangeSet != null)
                    ConfirmDisplay(r.ChangeSet, result);
            }
            else
            {
                result.Failed++;
                result.Lines.Add("✗ " + t.Name + " — " + r.Error);
            }
        }
        _s.Journal.Save(batch);
        return result;
    }

    private void ConfirmDisplay(ChangeSet cs, BatchResult result)
    {
        var keep = Application.Current.Dispatcher.Invoke(() => Dialogs.KeepChanges(Loc.T("display.keep.title"), Loc.T("display.keep.message")));
        if (keep) return;
        _s.Engine.Restore(cs);
        result.Lines.Add("↺ " + Loc.T("display.reverted"));
    }

    public async Task<RestoreResult> RestoreAsync(IEnumerable<ChangeSet> sets)
    {
        var list = sets.ToList();
        var needsAdmin = list.SelectMany(c => c.Entries).Any(e => _s.Dispatcher.NeedsElevation(e.Action));
        if (needsAdmin && !_priv.IsConnected)
        {
            try { await Task.Run(() => _priv.Ensure()); }
            catch (ElevationDeclinedException) { return new RestoreResult { Success = false, Errors = { Loc.T("elevation.declined") } }; }
        }
        return await Task.Run(() => _s.Engine.RestoreMany(list));
    }

    public static string Summary(BatchResult r)
    {
        var s = Loc.T("apply.summary", r.Applied, r.Failed);
        if (r.RebootRequired) s += "\n" + Loc.T("apply.reboot");
        else if (r.SignOutRequired) s += "\n" + Loc.T("apply.signout");
        if (r.RestorePointMessage != null) s += "\n" + r.RestorePointMessage;
        return s;
    }
}
