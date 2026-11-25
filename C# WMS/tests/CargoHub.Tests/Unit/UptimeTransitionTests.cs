using CargoHub.Api.Monitoring;
using CargoHub.UptimeVerification;

namespace CargoHub.Tests.Unit;

public sealed class UptimeTransitionTests
{
    public static IEnumerable<object[]> Scenarios() =>
        TransitionScenarios.All().Select(scenario => new object[] { scenario });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void UpDownUpMatchesTheTimeRepresentedByTheHeartbeats(TransitionScenario scenario)
    {
        var report = UptimeService.Calculate(scenario.History, 99.99m);

        Assert.Equal(scenario.ExpectedUpSeconds, report.UpSeconds);
        Assert.Equal(scenario.ExpectedDownSeconds, report.DownSeconds);
        Assert.Equal(604800 - scenario.ExpectedUpSeconds - scenario.ExpectedDownSeconds, report.UnknownSeconds);
        Assert.Equal(Math.Round(scenario.ExpectedUpSeconds * 100 / 604800m, 6), report.UptimePercentage);
        Assert.Equal(scenario.ExpectedSlaMet, report.SlaMet);
        var statuses = scenario.History.Heartbeats.Select(b => b.Status).ToArray();
        var firstDown = Array.IndexOf(statuses, 0);
        var lastDown = Array.LastIndexOf(statuses, 0);
        Assert.True(firstDown > 0);
        Assert.Equal(1, statuses[firstDown - 1]);
        Assert.Equal(1, statuses[lastDown + 1]);
    }

    [Fact]
    public void RecoveryDoesNotAddTheFirstUpIntervalToDowntime()
    {
        var scenario = TransitionScenarios.All().First();
        var report = UptimeService.Calculate(scenario.History, 99.99m);
        Assert.Equal(60, report.DownSeconds);
        Assert.True(report.SlaMet);
    }
}
