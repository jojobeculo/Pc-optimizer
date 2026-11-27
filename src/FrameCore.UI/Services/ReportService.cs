using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FrameCore.Core;
using FrameCore.Core.Hardware;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;
using FrameCore.BenchmarkManager.QuickBench;

namespace FrameCore.UI.Services;

/// <summary>
/// Local diagnostic report (HTML for humans, JSON for tools). Nothing is sent anywhere: the user decides whom to give
/// the file to. With anonymization (default), serial numbers, computer and user names are not included.
/// </summary>
public static class ReportService
{
    public static string BuildJson(DiagnosticResult r, IChangeJournal journal, bool anonymize)
    {
        var payload = new
        {
            generator = "FrameCore " + AppInfo.Version,
            generatedAt = DateTime.Now,
            anonymized = anonymize,
            machine = anonymize ? null : Environment.MachineName,
            profile = r.Profile,
            findings = r.Findings,
            catalog = r.Evaluations.Select(e => new { e.Tweak.Id, status = e.Status.ToString(), e.Compatible, current = e.CurrentSummary, target = e.TargetSummary }),
            changes = journal.ChangeSets.Select(c => new { c.TweakId, title = c.Title.ToString(), c.Status, c.CreatedUtc, c.RestoredUtc }),
        };
        var node = JsonSerializer.SerializeToNode(payload, Json.Options)!;
        if (anonymize) Scrub(node);
        return node.ToJsonString(Json.Options);
    }

    /// <summary>Network identifiers that are not needed to diagnose performance (addresses, interface IDs, MAC override).</summary>
    private static readonly HashSet<string> PrivateKeys = new(StringComparer.OrdinalIgnoreCase) { "gateway", "dnsServers", "interfaceGuid", "NetworkAddress" };

    private static void Scrub(System.Text.Json.Nodes.JsonNode? n)
    {
        switch (n)
        {
            case System.Text.Json.Nodes.JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).Where(PrivateKeys.Contains).ToList()) o.Remove(key);
                foreach (var kv in o.ToList()) Scrub(kv.Value);
                break;
            case System.Text.Json.Nodes.JsonArray a:
                foreach (var item in a) Scrub(item);
                break;
        }
    }

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? Loc.Unknown);

    public static string BuildHtml(DiagnosticResult r, IChangeJournal journal, bool anonymize)
    {
        var p = r.Profile;
        var sb = new StringBuilder();
        Head(sb, Loc.T("report.title"));
        sb.Append("<h1>").Append(E(Loc.T("report.title"))).Append("</h1><div class=\"muted\">FrameCore ").Append(E(AppInfo.Version)).Append(" · ")
          .Append(E(DateTime.Now.ToString("f", CultureInfo.CurrentCulture))).Append(" · ").Append(E(p.Signature)).Append("</div>");
        return Body(sb, r, journal, anonymize);
    }

    private static void Head(StringBuilder sb, string title)
    {
        sb.Append("<!doctype html><html lang=\"").Append(Loc.Language).Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append("<title>FrameCore – ").Append(E(title)).Append("</title><style>")
          .Append(":root{--bg:#0b0d14;--card:#151925;--text:#e8eaf2;--muted:#9aa3b8;--border:#252b3b;--accent:#7c5cff;--ok:#34d399;--warn:#fbbf24;--bad:#f87171;--info:#60a5fa}")
          .Append("@media (prefers-color-scheme: light){:root{--bg:#f4f5f9;--card:#fff;--text:#151926;--muted:#56607a;--border:#e1e5ee}}")
          .Append("body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 'Segoe UI',system-ui,sans-serif}main{max-width:1000px;margin:0 auto;padding:24px 16px}")
          .Append("h1{font-size:24px;margin:0 0 4px}h2{font-size:17px;margin:28px 0 10px}.muted{color:var(--muted)}.card{background:var(--card);border:1px solid var(--border);border-radius:12px;padding:16px;margin-bottom:12px}")
          .Append("table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:6px 8px;border-bottom:1px solid var(--border);vertical-align:top}th{color:var(--muted);font-weight:600;width:34%}")
          .Append(".sev{display:inline-block;padding:1px 8px;border-radius:6px;font-size:12px;font-weight:600}.Critical{color:var(--bad)}.Warning{color:var(--warn)}.Suggestion{color:var(--accent)}.Info{color:var(--info)}")
          .Append(".up{color:var(--ok)}.down{color:var(--bad)}.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}.cmp th{width:auto}.cmp td:last-child{min-width:260px}")
          .Append("@media (max-width:600px){th{width:45%}}</style></head><body><main>");
    }

    private static string Body(StringBuilder sb, DiagnosticResult r, IChangeJournal journal, bool anonymize)
    {
        var p = r.Profile;

        void Table(string title, IEnumerable<(string k, string? v)> rows)
        {
            sb.Append("<h2>").Append(E(title)).Append("</h2><div class=\"card\"><table>");
            foreach (var (k, v) in rows) sb.Append("<tr><th>").Append(E(k)).Append("</th><td>").Append(E(v)).Append("</td></tr>");
            sb.Append("</table></div>");
        }

        Table(Loc.T("hw.windows"), new (string, string?)[]
        {
            (Loc.T("hw.os.product"), p.Os.ProductName), (Loc.T("hw.os.version"), $"{p.Os.DisplayVersion} ({p.Os.FullBuild})"), (Loc.T("hw.os.edition"), p.Os.EditionId),
            (Loc.T("hw.os.servicing"), p.Os.ServicingNote), (Loc.T("hw.os.arch"), p.Os.Architecture),
        });
        Table(Loc.T("hw.cpu"), new (string, string?)[]
        {
            (Loc.T("hw.cpu.model"), p.Cpu.Name), (Loc.T("hw.cpu.arch"), p.Cpu.Microarchitecture), (Loc.T("hw.cpu.generation"), p.Cpu.Generation),
            (Loc.T("hw.cpu.cores"), $"{p.Cpu.Cores} / {p.Cpu.Threads}" + (p.Cpu.IsHybrid ? $" (P {p.Cpu.PerformanceCores} + E {p.Cpu.EfficiencyCores})" : "")),
            (Loc.T("hw.cpu.base"), p.Cpu.BaseMhz is { } b ? b + " MHz" : null), (Loc.T("hw.cpu.l3"), p.Cpu.L3Kb is { } l3 ? $"{l3 / 1024} Mo" : null),
        });
        foreach (var g in p.Gpus.Where(g => !g.IsSoftware))
            Table(Loc.T("hw.gpu") + " – " + g.Name, new (string, string?)[]
            {
                (Loc.T("hw.gpu.vram"), g.DedicatedVramBytes is { } v and > 0 ? Infrastructure.BytesConverter.Format(v) : null),
                (Loc.T("hw.gpu.driver"), g.DriverVersion == null ? null : g.DriverVersion + (g.DriverDate is { } dd ? $" ({dd.ToString("d", CultureInfo.CurrentCulture)})" : "")),
                (Loc.T("hw.gpu.arch"), g.Architecture), (Loc.T("hw.gpu.rebar"), Loc.T("rebar." + g.ResizableBar) + (g.ResizableBarDetail != null ? " – " + g.ResizableBarDetail : "")),
                (Loc.T("hw.gpu.hags"), g.HagsEnabled is { } h ? Loc.T(h ? "common.yes" : "common.no") : null), (Loc.T("hw.gpu.vbios"), g.VbiosVersion),
            });
        Table(Loc.T("hw.ram"), new (string, string?)[]
        {
            (Loc.T("hw.ram.total"), Infrastructure.BytesConverter.Format(p.Memory.TotalBytes)), (Loc.T("hw.ram.type"), p.Memory.Type),
            (Loc.T("hw.ram.speed"), p.Memory.ConfiguredSpeedMts is { } s ? $"{s} MT/s ({s / 2} MHz)" : null), (Loc.T("hw.ram.channels"), p.Memory.Channels == ChannelMode.Unknown ? null : Loc.T("channels." + p.Memory.Channels)),
            (Loc.T("hw.ram.xmp"), Loc.T("xmp." + p.Memory.Xmp) + " – " + p.Memory.XmpReasoning),
            (Loc.T("hw.ram.modules"), string.Join(" ; ", p.Memory.Modules.Select(m => $"{m.Slot}: {Infrastructure.BytesConverter.Format(m.CapacityBytes)} {m.Manufacturer} {m.PartNumber}".Trim()))),
        });
        Table(Loc.T("hw.board"), new (string, string?)[]
        {
            (Loc.T("hw.board.model"), $"{p.Board.Manufacturer} {p.Board.Product}"), (Loc.T("hw.board.system"), $"{p.Board.SystemManufacturer} {p.Board.SystemModel}"),
            (Loc.T("hw.board.chipset"), p.Board.Chipset), (Loc.T("hw.bios.version"), $"{p.Firmware.Version} ({p.Firmware.ReleaseDate:d})"),
            (Loc.T("hw.bios.mode"), p.Firmware.Mode == FirmwareMode.Unknown ? null : p.Firmware.Mode.ToString().ToUpperInvariant()), (Loc.T("hw.bios.secureboot"), Loc.T("tri." + p.Firmware.SecureBoot)), (Loc.T("hw.bios.tpm"), p.Firmware.TpmVersion),
        });
        foreach (var d in p.Displays)
            Table(Loc.T("hw.display") + " – " + (d.FriendlyName ?? d.DeviceName), new (string, string?)[]
            {
                (Loc.T("hw.display.mode"), $"{d.CurrentWidth}×{d.CurrentHeight} @ {d.CurrentRefreshHz:0.##} Hz"),
                (Loc.T("hw.display.max"), d.MaxRefreshAtCurrentResolution is { } m ? $"{m:0} Hz" : null), (Loc.T("hw.display.connection"), d.Connection),
                (Loc.T("hw.display.vrr"), d.VrrMinHz != null ? $"{d.VrrMinHz}–{d.VrrMaxHz} Hz" : null), (Loc.T("hw.display.hdr"), d.HdrSupported is { } h ? Loc.T(h ? "common.yes" : "common.no") : null),
            });
        sb.Append("<h2>").Append(E(Loc.T("report.findings"))).Append("</h2>");
        foreach (var f in r.Findings)
            sb.Append("<div class=\"card\"><span class=\"sev ").Append(f.Severity).Append("\">").Append(E(Loc.T("severity." + f.Severity))).Append("</span> <b>")
              .Append(E(f.Title)).Append("</b><div class=\"muted\">").Append(E(f.Detail)).Append("</div>")
              .Append(f.Recommendation != null ? "<div>" + E(f.Recommendation) + "</div>" : "").Append("</div>");
        sb.Append("<h2>").Append(E(Loc.T("report.changes"))).Append("</h2><div class=\"card\"><table>");
        if (!journal.ChangeSets.Any()) sb.Append("<tr><td class=\"muted\">").Append(E(Loc.T("home.noOpt"))).Append("</td></tr>");
        foreach (var c in journal.ChangeSets.Take(200))
            sb.Append("<tr><th>").Append(E(c.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))).Append("</th><td>").Append(E(c.Title.ToString()))
              .Append(" — ").Append(E(Loc.T("changeStatus." + c.Status))).Append("</td></tr>");
        sb.Append("</table></div>");
        if (!anonymize) sb.Append("<p class=\"muted\">").Append(E(Environment.MachineName)).Append("</p>");
        sb.Append("<p class=\"muted\">").Append(E(Loc.T("report.footer"))).Append("</p></main></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Shareable before/after comparison of two quick benchmarks (forum, friend, technician). Contains no machine name,
    /// user name or serial number: only the generic hardware signature, the dates, the labels and the measurements.
    /// </summary>
    public static string BuildComparisonHtml(QuickBenchComparison cmp, string beforeTitle, string afterTitle)
    {
        var sb = new StringBuilder();
        Head(sb, Loc.T("quick.share.title"));
        sb.Append("<h1>").Append(E(Loc.T("quick.share.title"))).Append("</h1><div class=\"muted\">FrameCore ").Append(E(AppInfo.Version))
          .Append(" · ").Append(E(cmp.After.MachineSignature)).Append("</div>");
        sb.Append("<div class=\"card\"><table>")
          .Append("<tr><th>").Append(E(Loc.T("quick.share.before"))).Append("</th><td>").Append(E(beforeTitle)).Append("</td></tr>")
          .Append("<tr><th>").Append(E(Loc.T("quick.share.after"))).Append("</th><td>").Append(E(afterTitle)).Append("</td></tr>")
          .Append("<tr><th>").Append(E(Loc.T("quick.share.index"))).Append("</th><td><b>")
          .Append(E(cmp.IndexAfterRelativeToBefore is { } i ? i.ToString("0.0", CultureInfo.CurrentCulture) : "—")).Append("</b> ").Append(E(Loc.T("quick.share.indexNote"))).Append("</td></tr>");
        if (cmp.Before.GpuDriver != cmp.After.GpuDriver && cmp.Before.GpuDriver != null && cmp.After.GpuDriver != null)
            sb.Append("<tr><th>").Append(E(Loc.T("quick.share.changes"))).Append("</th><td>").Append(E(Loc.T("quick.driverChanged", cmp.Before.GpuDriver, cmp.After.GpuDriver))).Append("</td></tr>");
        if (cmp.Before.OsBuild != cmp.After.OsBuild && cmp.Before.OsBuild != null && cmp.After.OsBuild != null)
            sb.Append("<tr><th>").Append(E(Loc.T("quick.share.changes"))).Append("</th><td>").Append(E(Loc.T("quick.osChanged"))).Append("</td></tr>");
        sb.Append("</table></div>");
        sb.Append("<h2>").Append(E(Loc.T("quick.share.metrics"))).Append("</h2><div class=\"card\"><table class=\"cmp\"><tr><th>").Append(E(Loc.T("quick.col.metric")))
          .Append("</th><th class=\"num\">").Append(E(Loc.T("quick.share.before"))).Append("</th><th class=\"num\">").Append(E(Loc.T("quick.share.after")))
          .Append("</th><th class=\"num\">%</th><th>").Append(E(Loc.T("quick.share.verdict"))).Append("</th></tr>");
        foreach (var m in cmp.Metrics)
        {
            string F(double? v) => v is { } x ? x.ToString("N1", CultureInfo.CurrentCulture) + " " + m.Unit : "—";
            var cls = m.Verdict == ChangeVerdict.Improved ? "up" : m.Verdict == ChangeVerdict.Regressed ? "down" : "muted";
            sb.Append("<tr><td>").Append(E(Loc.T("metric." + m.Key))).Append("</td><td class=\"num\">").Append(E(F(m.Before))).Append("</td><td class=\"num\">").Append(E(F(m.After)))
              .Append("</td><td class=\"num ").Append(cls).Append("\">").Append(E(m.ImprovementPercent is { } pc ? (pc >= 0 ? "+" : "") + pc.ToString("0.0", CultureInfo.CurrentCulture) : "—"))
              .Append("</td><td class=\"").Append(cls).Append("\">").Append(E(Loc.T("verdict." + m.Verdict, m.NoiseFloorPercent.ToString("0.#", CultureInfo.CurrentCulture)))).Append("</td></tr>");
        }
        sb.Append("</table></div><p class=\"muted\">").Append(E(Loc.T("quick.share.method"))).Append("</p></main></body></html>");
        return sb.ToString();
    }
}
