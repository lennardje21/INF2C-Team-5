using CargoHub.Api.Monitoring;

namespace CargoHub.Tests.Unit;

public sealed class UptimeServiceTests
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    internal static UptimeHistory FullWeek(decimal downtime = 0)
    {
        var start = Now.AddDays(-7);
        var beats = new List<UptimeHeartbeat>();
        if (downtime > 0)
            beats.Add(new UptimeHeartbeat(start.AddSeconds((double)downtime), 0, downtime));
        var previous = start.AddSeconds((double)downtime);
        for (var day = 1; day <= 7; day++)
        {
            var time = start.AddDays(day);
            beats.Add(new UptimeHeartbeat(time, 1, (decimal)(time - previous).TotalSeconds));
            previous = time;
        }
        return new UptimeHistory(4, 86400, Now, beats);
    }

    [Theory]
    [InlineData("0", "100", true)]
    [InlineData("60", "99.990079", true)]
    [InlineData("60.48", "99.99", true)]
    [InlineData("61", "99.989914", false)]
    public void CalculatesDurationWeightedWeeklyUptime(string downtime, string percentage, bool met)
    {
        var report = UptimeService.Calculate(FullWeek(decimal.Parse(downtime,
            System.Globalization.CultureInfo.InvariantCulture)), 99.99m);

        Assert.Equal(decimal.Parse(percentage, System.Globalization.CultureInfo.InvariantCulture), report.UptimePercentage);
        Assert.Equal(met, report.SlaMet);
        Assert.Equal(100m, report.CoveragePercentage);
        Assert.Equal(0, report.UnknownSeconds);
        Assert.Equal(60.48m, report.AllowedDowntimeSeconds);
    }

    [Fact]
    public void ComparesSlaBeforeRoundingDisplayedPercentage()
    {
        var report = UptimeService.Calculate(FullWeek(60.48001m), 99.99m);
        Assert.Equal(99.99m, report.UptimePercentage);
        Assert.False(report.SlaMet);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(100, false)]
    public void AcceptsBothSlaBoundaries(int sla, bool met)
    {
        Assert.Equal(met, UptimeService.Calculate(FullWeek(60), sla).SlaMet);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void RejectsSlaOutsidePercentageRange(int sla)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UptimeService.Calculate(FullWeek(), sla));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void PendingMaintenanceAndDownDoNotConfirmAvailability(int status)
    {
        var history = FullWeek();
        var beats = history.Heartbeats.ToArray();
        beats[0] = beats[0] with { Status = status };
        var report = UptimeService.Calculate(history with { Heartbeats = beats }, 99.99m);
        Assert.Equal(86400m, report.DownSeconds);
        Assert.False(report.SlaMet);
    }

    [Fact]
    public void ClipsAnIntervalAtTheStartOfTheWeekAndSortsBeats()
    {
        var history = FullWeek();
        var beats = history.Heartbeats.ToList();
        beats.Insert(0, new UptimeHeartbeat(Now.AddDays(-8), 0, 86400));
        beats.Reverse();
        var report = UptimeService.Calculate(history with { Heartbeats = beats }, 100);
        Assert.Equal(100, report.UptimePercentage);
        Assert.True(report.SlaMet);
    }

    [Fact]
    public void IncompleteHistoryDoesNotClaimAFullWeekOfAvailability()
    {
        var history = new UptimeHistory(4, 60, Now,
            [new UptimeHeartbeat(Now, 1, 60)]);
        var report = UptimeService.Calculate(history, 99.99m);
        Assert.Equal(60, report.UpSeconds);
        Assert.Equal(604740, report.UnknownSeconds);
        Assert.Null(report.SlaMet);
    }

    [Fact]
    public void MonitoringGapIsUnknownRatherThanContinuousUptime()
    {
        var history = new UptimeHistory(4, 60, Now,
        [
            new UptimeHeartbeat(Now.AddHours(-1), 1, 60),
            new UptimeHeartbeat(Now, 1, 3600)
        ]);
        var report = UptimeService.Calculate(history, 99.99m);
        Assert.Equal(180, report.UpSeconds);
        Assert.Null(report.SlaMet);
    }

    [Fact]
    public void StaleHeartbeatDoesNotExtendUptimeToThePresent()
    {
        var history = new UptimeHistory(4, 60, Now,
            [new UptimeHeartbeat(Now.AddMinutes(-10), 1, 60)]);
        Assert.Equal(60, UptimeService.Calculate(history, 99.99m).UpSeconds);
    }

    [Fact]
    public void RecentHeartbeatCoversTimeUntilTheNextExpectedCheck()
    {
        var history = new UptimeHistory(4, 60, Now,
            [new UptimeHeartbeat(Now.AddSeconds(-30), 1, 60)]);
        Assert.Equal(90, UptimeService.Calculate(history, 99.99m).UpSeconds);
    }

    [Fact]
    public void EmptyHistoryIsUnavailable()
    {
        Assert.Throws<UptimeUnavailableException>(() => UptimeService.Calculate(
            new UptimeHistory(4, 60, Now, []), 99.99m));
    }

    [Fact]
    public void DuplicateTimestampsAreRejectedInsteadOfDoubleCounted()
    {
        var history = FullWeek();
        Assert.Throws<UptimeUnavailableException>(() => UptimeService.Calculate(
            history with { Heartbeats = [.. history.Heartbeats, history.Heartbeats[0]] }, 99.99m));
    }
}
