using System.IO;
using System.Text;
using System.Text.Json;
using FrameCore.Core;
using FrameCore.Core.Engine;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.UI.Services;
using Xunit;
using Xunit.Abstractions;

namespace FrameCore.WindowsIntegrationTests;

/// <summary>
/// Exercises the real executors against the real Windows registry, services, scheduled tasks, power plans and BCD
/// of a DISPOSABLE virtual machine, then checks that every captured value is restored exactly.
/// </summary>
public class RealWindowsTests
{
    private readonly ITestOutputHelper _out;
    public RealWindowsTests(ITestOutputHelper output) => _out = output;

    private static bool Enabled =>
        OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FRAMECORE_DISPOSABLE_VM") == "1" && AppInfo.IsElevated;

    private static string OutputDir
    {
        get
        {
            var d = Environment.GetEnvironmentVariable("FRAMECORE_TEST_OUTPUT") ?? Path.Combine(Path.GetTempPath(), "framecore-it");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    private static readonly Lazy<AppServices> Services = new(() =>
    {
        AppPaths.OverrideRoot(Path.Combine(Path.GetTempPath(), "framecore-it-data-" + Guid.NewGuid().ToString("N")));
        Loc.Instance.LoadDirectory(AppPaths.LangDir);
        Loc.Instance.SetLanguage("fr");
        return new AppServices(new AppSettings(), forWorker: true);
    });

    [Fact]
    public void Hardware_scan_produces_a_coherent_profile()
    {
        if (!Enabled) return;
        var s = Services.Value;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = s.CreateScanner().Scan(new Progress<string>(m => _out.WriteLine($"{sw.ElapsedMilliseconds,6} ms  {m}")));
        _out.WriteLine($"scan total {sw.ElapsedMilliseconds} ms");
        File.WriteAllText(Path.Combine(OutputDir, "it-profile.json"), JsonSerializer.Serialize(p, Json.Options));
        Assert.False(string.IsNullOrWhiteSpace(p.Cpu.Name));
        Assert.True(p.Cpu.Threads >= p.Cpu.Cores && p.Cpu.Cores > 0);
        Assert.True(p.Os.Build >= 17763);
        Assert.True(p.Memory.TotalBytes > 1L << 30);
        Assert.NotEmpty(p.Storage);
    }

    /// <summary>Every compatible, reversible catalog entry: apply → verify → restore → exact original state.</summary>
    [Fact]
    public void Catalog_round_trip_restores_the_exact_original_state()
    {
        if (!Enabled) return;
        var s = Services.Value;
        var profile = s.CreateScanner().Scan();
        var diag = new DiagnosticService(s);
        var facts = diag.BuildFacts(profile);
        var dispatcher = new LocalDispatcher(s.Executors);
        var report = new StringBuilder("| Réglage | Compatible | Appliqué | Vérifié | Restauré | État d'origine retrouvé | Détail |\n|---|---|---|---|---|---|---|\n");
        var broken = new List<string>();
        var skippedTypes = new HashSet<string> { "optionalFeature" }; // DISM changes need a reboot cycle

        foreach (var t in s.Catalog.Tweaks.Where(t => t.Reversible))
        {
            var eval = s.Engine.Evaluate(t, facts);
            if (!eval.Compatible || t.Actions.Any(a => skippedTypes.Contains(a.Type)) || t.Actions.Any(a => a.Type == "handler" && a.Name == "display.maxRefresh"))
            {
                report.Append($"| {t.Id} | {(eval.Compatible ? "oui" : "non")} | — | — | — | — | ignoré |\n");
                continue;
            }
            var ctx = new ActionContext(t.Id, "integration-test");
            var before = t.Actions.Select(a => Snapshot(dispatcher, a, ctx)).ToList();
            string applied = "non", verified = "—", restored = "—", exact = "—", detail = "";
            try
            {
                var r = s.Engine.Apply(t, facts);
                applied = r.Success ? "oui" : (r.RolledBack ? "échec (annulé)" : "échec");
                detail = r.Error ?? "";
                if (r.ChangeSet != null)
                {
                    verified = r.ChangeSet.Entries.All(e => e.Verified) ? "oui" : "non";
                    var rr = s.Engine.Restore(r.ChangeSet);
                    restored = rr.Success ? "oui" : "non (" + string.Join("; ", rr.Errors) + ")";
                }
            }
            catch (Exception ex) { detail = ex.GetType().Name + ": " + ex.Message; }

            var after = t.Actions.Select(a => Snapshot(dispatcher, a, ctx)).ToList();
            var same = before.SequenceEqual(after);
            exact = same ? "oui" : "**NON**";
            if (!same)
            {
                broken.Add(t.Id);
                for (var i = 0; i < before.Count; i++)
                    if (before[i] != after[i]) detail += $" [#{i}: avant={before[i]} après={after[i]}]";
            }
            report.Append($"| {t.Id} | oui | {applied} | {verified} | {restored} | {exact} | {detail.Replace("|", "/").Replace("\n", " ")} |\n");
        }
        File.WriteAllText(Path.Combine(OutputDir, "roundtrip.md"), report.ToString());
        _out.WriteLine(report.ToString());
        Assert.True(broken.Count == 0, "State not restored exactly for: " + string.Join(", ", broken));
    }

    private static string Snapshot(IActionDispatcher d, ActionDefinition a, ActionContext c)
    {
        try { return JsonSerializer.Serialize(d.Capture(a, c), Json.Canonical); }
        catch (Exception ex) { return "unreadable:" + ex.GetType().Name; }
    }
}
