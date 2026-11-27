using FrameCore.StartupManager;
using Xunit;

namespace FrameCore.Tests;

public class BootPerformanceTests
{
    private const string Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    private static string Boot(string start, int bootMs, int mainMs, int postMs, bool degraded = false, bool afterInstall = false, int logonWaitMs = 0, int apps = 17) => $"""
        <Event xmlns='{Ns}'>
          <System><Provider Name='Microsoft-Windows-Diagnostics-Performance'/><EventID>100</EventID><TimeCreated SystemTime='{start}'/></System>
          <EventData>
            <Data Name='BootTsVersion'>2</Data>
            <Data Name='BootStartTime'>{start}</Data>
            <Data Name='BootTime'>{bootMs}</Data>
            <Data Name='MainPathBootTime'>{mainMs}</Data>
            <Data Name='BootKernelInitTime'>22</Data>
            <Data Name='BootNumStartupApps'>{apps}</Data>
            <Data Name='BootPostBootTime'>{postMs}</Data>
            <Data Name='BootIsRebootAfterInstall'>{(afterInstall ? "true" : "false")}</Data>
            <Data Name='BootIsDegradation'>{(degraded ? "true" : "false")}</Data>
            <Data Name='UserLogonWaitDuration'>{logonWaitMs}</Data>
          </EventData>
        </Event>
        """;

    private static string Culprit(int id, string time, string name, string? friendly, int totalMs, int degradationMs) => $"""
        <Event xmlns='{Ns}'>
          <System><EventID>{id}</EventID><TimeCreated SystemTime='{time}'/></System>
          <EventData>
            <Data Name='Name'>{name}</Data>
            <Data Name='FriendlyName'>{friendly}</Data>
            <Data Name='Version'>1.0</Data>
            <Data Name='TotalTime'>{totalMs}</Data>
            <Data Name='DegradationTime'>{degradationMs}</Data>
          </EventData>
        </Event>
        """;

    [Fact]
    public void Boot_event_is_read_in_seconds_with_its_flags()
    {
        var b = BootPerformance.ParseBoot(Boot("2026-11-20T07:30:00.000Z", 39836, 13136, 26700, degraded: true, afterInstall: true, logonWaitMs: 3247, apps: 21))!;
        Assert.Equal(new DateTime(2026, 11, 20, 7, 30, 0, DateTimeKind.Utc), b.StartUtc.ToUniversalTime());
        Assert.Equal(39.836, b.TotalSeconds, 3);
        Assert.Equal(13.136, b.MainPathSeconds, 3);
        Assert.Equal(26.7, b.PostBootSeconds, 3);
        Assert.Equal(3.247, b.SignInWaitSeconds!.Value, 3);
        Assert.Equal(21, b.StartupApps);
        Assert.True(b.AfterUpdate);
        Assert.True(b.FlaggedSlow);
    }

    [Fact]
    public void Missing_times_are_not_invented()
    {
        Assert.Null(BootPerformance.ParseBoot($"<Event xmlns='{Ns}'><EventData><Data Name='BootTime'>1000</Data></EventData></Event>"));
        Assert.Null(BootPerformance.ParseBoot("not xml"));
        var noWait = BootPerformance.ParseBoot(Boot("2026-11-20T07:30:00Z", 20000, 15000, 5000))!;
        Assert.Null(noWait.SignInWaitSeconds);
    }

    [Theory]
    [InlineData(101, BootCulpritKind.Application)]
    [InlineData(102, BootCulpritKind.Driver)]
    [InlineData(103, BootCulpritKind.Service)]
    [InlineData(109, BootCulpritKind.Device)]
    [InlineData(106, BootCulpritKind.Other)]
    public void Slowdown_events_are_classified(int id, BootCulpritKind kind) => Assert.Equal(kind, BootPerformance.KindOf(id));

    [Fact]
    public void Slowdown_event_keeps_the_file_name_and_the_delay()
    {
        var c = BootPerformance.ParseCulprit(101, Culprit(101, "2026-11-20T07:31:00Z", @"C:\Program Files\Vendor\tray.exe", "Vendor Tray", 4200, 3100))!;
        Assert.Equal("tray.exe", c.Name);
        Assert.Equal("Vendor Tray", c.Display);
        Assert.Equal(4.2, c.TotalSeconds, 3);
        Assert.Equal(3.1, c.DelaySeconds, 3);
        Assert.Equal(BootCulpritKind.Application, c.Kind);
        Assert.Null(BootPerformance.ParseCulprit(102, Culprit(102, "2026-11-20T07:31:00Z", "", null, 1, 1)));
    }

    [Fact]
    public void History_gives_a_robust_median_a_trend_and_the_worst_culprits()
    {
        var h = new BootHistory();
        // 5 older starts around 20 s, then 5 recent ones around 35 s, with one outlier at 120 s.
        var t0 = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        double[] times = { 19, 21, 20, 22, 18, 34, 36, 120, 35, 33 };
        for (var i = 0; i < times.Length; i++)
            h.Boots.Add(new BootRecord { StartUtc = t0.AddDays(i), MainPathSeconds = times[i], TotalSeconds = times[i] + 10 });
        Assert.Equal(35, h.RecentMedianMainPath()!.Value, 3);   // the 120 s outlier does not drive the result
        Assert.Equal(15, h.TrendSeconds()!.Value, 3);           // 35 (recent median) − 20 (previous median)
        Assert.Equal(33, h.Last!.MainPathSeconds);              // the most recent start

        h.Culprits.Add(new BootCulprit { Kind = BootCulpritKind.Application, Name = "tray.exe", FriendlyName = "Vendor Tray", DelaySeconds = 3, TimeUtc = t0 });
        h.Culprits.Add(new BootCulprit { Kind = BootCulpritKind.Application, Name = "tray.exe", FriendlyName = "Vendor Tray", DelaySeconds = 5, TimeUtc = t0.AddDays(2) });
        h.Culprits.Add(new BootCulprit { Kind = BootCulpritKind.Driver, Name = "slow.sys", DelaySeconds = 6, TimeUtc = t0.AddDays(1) });
        var top = h.TopCulprits();
        Assert.Equal("Vendor Tray", top[0].Name);               // 2 × 4 s on average beats 1 × 6 s
        Assert.Equal(2, top[0].Occurrences);
        Assert.Equal(4, top[0].AverageDelaySeconds, 3);
        Assert.Equal(t0.AddDays(2), top[0].LastSeenUtc);
        Assert.Equal("slow.sys", top[1].Name);
    }

    [Fact]
    public void Too_few_starts_give_no_trend()
    {
        var h = new BootHistory();
        h.Boots.Add(new BootRecord { StartUtc = DateTime.UtcNow, MainPathSeconds = 30 });
        Assert.Null(h.TrendSeconds());
        Assert.Equal(30, h.RecentMedianMainPath());
        Assert.Null(new BootHistory().RecentMedianMainPath());
    }
}
