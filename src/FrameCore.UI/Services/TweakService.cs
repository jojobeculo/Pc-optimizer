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
    public string? BatchId { get; set; }
    public FrameCore.BenchmarkManager.Verification.VerifyRepairReport? Verification { get; set; }
    public int NotApplicable { get; set; }
    public int AlreadyInPlace { get; set; }
    /// <summary>Settings not applied because administrator rights were refused or unavailable.</summary>
    public int SkippedNeedRights { get; set; }
}

/// <summary>Stages of an application, reported to the step progress indicator (fraction null = indeterminate).</summary>
public enum ApplyStage { Elevation, RestorePoint, Apply, Verify, Done }

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
    public async Task<BatchResult> ApplyAsync(IReadOnlyList<TweakDefinition> tweaks, LocalizedText title, ProfileLevel? profile, bool createRestorePoint,
                                              IProgress<string>? progress = null, Action<ApplyStage, double?>? stage = null, ChangeBatch? resume = null)
    {
        var result = new BatchResult();
        if (tweaks.Count == 0) return result;
        void Stage(ApplyStage s, double? f) => Application.Current?.Dispatcher.BeginInvoke(() => stage?.Invoke(s, f));
        Stage(ApplyStage.Elevation, null);
        var facts = _diag.Last?.Facts ?? _diag.BuildFacts(_s.CreateScanner().Scan());
        var needsAdmin = NeedsElevation(tweaks) || createRestorePoint;
        if (needsAdmin && !_priv.IsConnected)
        {
            try { await Task.Run(() => _priv.Ensure()); }
            catch (ElevationDeclinedException)
            {
                // Settings that need rights are left out and reported as such (never counted as applied).
                var skipped = tweaks.Where(t => NeedsElevation(new[] { t })).ToList();
                result.SkippedNeedRights = skipped.Count;
                foreach (var t in skipped) result.Lines.Add("⚠ " + t.Name + " — " + Loc.T("elevation.skippedLine"));
                result.Lines.Add(Loc.T("elevation.declinedApplyingUser"));
                tweaks = tweaks.Except(skipped).ToList();
                createRestorePoint = false;
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or UnauthorizedAccessException)
            {
                // The administrator module could not start: same outcome, with the reason.
                var skipped = tweaks.Where(t => NeedsElevation(new[] { t })).ToList();
                result.SkippedNeedRights = skipped.Count;
                result.Lines.Add(Loc.T("elevation.failed", ex.Message));
                tweaks = tweaks.Except(skipped).ToList();
                createRestorePoint = false;
            }
        }
        // The batch is written before anything changes: if the PC stops, it can be resumed without re-applying.
        var batch = resume ?? new ChangeBatch { Title = title, Profile = profile, PlannedTweakIds = tweaks.Select(t => t.Id).ToList() };
        result.BatchId = batch.Id;
        _s.Journal.Save(batch);
        if (createRestorePoint)
        {
            Stage(ApplyStage.RestorePoint, null);
            progress?.Report(Loc.T("restorepoint.creating"));
            var rp = await Task.Run(() => _priv.CreateRestorePoint("FrameCore: " + title.ToString()));
            batch.RestorePointCreated = rp.Created;
            batch.RestorePointNote = result.RestorePointMessage = rp.Message;
        }
        var done = 0;
        foreach (var t in tweaks)
        {
            Stage(ApplyStage.Apply, (double)done++ / tweaks.Count);
            progress?.Report(t.Name.ToString());
            // Resuming: an optimisation already active is never applied a second time.
            if (resume != null && _s.Engine.ActiveChangeSets(t.Id).Any()) { result.Lines.Add("= " + t.Name + " — " + Loc.T("apply.alreadyActive")); continue; }
            var r = await Task.Run(() => _s.Engine.Apply(t, facts, batch.Id));
            if (r.ChangeSet != null) batch.ChangeSetIds.Add(r.ChangeSet.Id);
            _s.Journal.Save(batch);
            if (!r.Success && r.NotApplicable)
            {
                result.NotApplicable++;
                result.Lines.Add("– " + t.Name + " — " + r.Error);
                continue;
            }
            if (r.Success && r.AlreadyInPlace)
            {
                result.AlreadyInPlace++;
                result.Lines.Add("= " + t.Name + " — " + Loc.T("apply.alreadyInPlace"));
                continue;
            }
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
        // Auto verify & repair: every change is read back; values Windows did not keep are rewritten at most twice.
        Stage(ApplyStage.Verify, null);
        progress?.Report(Loc.T("verify.running"));
        var sets = batch.ChangeSetIds.Select(id => _s.Journal.Find(id)).Where(c => c != null).Cast<ChangeSet>().ToList();
        var verifier = new FrameCore.BenchmarkManager.Verification.VerifyRepairService(_s.Engine, _s.Catalog.Find);
        result.Verification = await Task.Run(() => verifier.VerifyAndRepair(sets, batch.Id));
        _s.VerificationReports.Save(result.Verification);
        batch.CompletedUtc = DateTime.UtcNow;
        _s.Journal.Save(batch);
        Stage(ApplyStage.Done, 1);
        return result;
    }

    /// <summary>Batches that were interrupted (PC turned off, crash) and still have planned optimisations not applied.</summary>
    public List<(ChangeBatch Batch, List<TweakDefinition> Remaining)> InterruptedBatches()
    {
        var list = new List<(ChangeBatch, List<TweakDefinition>)>();
        foreach (var b in _s.Journal.Batches.Where(b => b.CompletedUtc == null && b.PlannedTweakIds.Count > 0 && b.CreatedUtc < DateTime.UtcNow.AddMinutes(-2)))
        {
            var remaining = b.PlannedTweakIds.Where(id => !_s.Engine.ActiveChangeSets(id).Any())
                                             .Select(id => _s.Catalog.Find(id)).Where(t => t != null).Cast<TweakDefinition>().ToList();
            if (remaining.Count > 0) list.Add((b, remaining));
        }
        return list;
    }

    /// <summary>Marks an interrupted batch as finished without applying the rest (user choice).</summary>
    public void DismissBatch(ChangeBatch b) { b.CompletedUtc = DateTime.UtcNow; _s.Journal.Save(b); }

    /// <summary>After a reboot, re-reads the changes that needed one and records the result in their history.</summary>
    public List<(ChangeSet Set, bool? Active)> VerifyAfterReboot(DateTime? lastBootUtc)
    {
        var list = new List<(ChangeSet, bool?)>();
        if (lastBootUtc == null) return list;
        foreach (var cs in _s.Journal.ChangeSets.Where(c => c.Status == ChangeSetStatus.Applied && c.Restart == RestartRequirement.Reboot
                                                          && c.CreatedUtc < lastBootUtc && !c.History.Any(h => h.Kind == "postReboot")))
        {
            bool? ok;
            try { ok = _s.Engine.Verify(cs); } catch { ok = null; }
            // Not readable without rights (no UAC prompt at start-up): keep it for a later check instead of closing it.
            if (ok == null && !_priv.IsConnected && cs.Entries.Any(e => _s.Dispatcher.NeedsElevation(e.Action))) continue;
            cs.History.Add(new ChangeEvent { Kind = "postReboot", Detail = Loc.T(ok == true ? "verify.postRebootOk" : ok == false ? "verify.postRebootLost" : "verify.notVerifiable") });
            _s.Journal.Save(cs);
            list.Add((cs, ok));
        }
        return list;
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
        if (r.AlreadyInPlace > 0) s += "\n" + Loc.T("apply.alreadyInPlaceCount", r.AlreadyInPlace);
        if (r.SkippedNeedRights > 0) s += "\n" + Loc.T("apply.skippedRights", r.SkippedNeedRights);
        if (r.RebootRequired) s += "\n" + Loc.T("apply.reboot");
        else if (r.SignOutRequired) s += "\n" + Loc.T("apply.signout");
        if (r.RestorePointMessage != null) s += "\n" + r.RestorePointMessage;
        return s;
    }
}
