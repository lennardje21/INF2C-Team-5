using CargoHub.Api.Monitoring;
using CargoHub.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CargoHub.Tests.Unit;

public sealed class SlowRequestNotifierTests
{
    internal static readonly DateTimeOffset Now = new(2026, 11, 10, 14, 30, 0, TimeSpan.Zero);

    // Keeps the mails instead of sending them.
    internal sealed class RecordingEmailService : IEmailService
    {
        private readonly List<(string To, string Subject, string Body)> sent = [];

        public IReadOnlyList<(string To, string Subject, string Body)> Sent
        {
            get { lock (sent) return [.. sent]; }
        }

        public Task<bool> SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            lock (sent) sent.Add((to, subject, body));
            return Task.FromResult(true);
        }
    }

    // A clock the test moves forward itself.
    internal sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static SlowRequestNotifier CreateNotifier(
        RecordingEmailService email, ManualTime time, PerformanceOptions? options = null) =>
        new(email, Options.Create(options ?? new PerformanceOptions { AlertEmail = "team@example.com" }),
            time, NullLogger<SlowRequestNotifier>.Instance);

    [Fact]
    public void SlowRequest_MailsTheTeamWithTheEndpointAndDuration()
    {
        var email = new RecordingEmailService();
        var notifier = CreateNotifier(email, new ManualTime(Now));

        notifier.Notify("GET api/v2/items/{id}", "/api/v2/items/5", 450);

        var mail = Assert.Single(email.Sent);
        Assert.Equal("team@example.com", mail.To);
        Assert.Contains("GET api/v2/items/{id}", mail.Subject);
        Assert.Contains("450 ms", mail.Subject);
        Assert.Contains("/api/v2/items/5", mail.Body);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(299)]
    [InlineData(300)]
    public void RequestWithinTheThreshold_SendsNoMail(long durationMs)
    {
        var email = new RecordingEmailService();
        var notifier = CreateNotifier(email, new ManualTime(Now));

        Assert.Null(notifier.Notify("GET api/v2/items", "/api/v2/items", durationMs));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public void SecondSlowRequestWithin15Minutes_SendsNoMail()
    {
        var email = new RecordingEmailService();
        var time = new ManualTime(Now);
        var notifier = CreateNotifier(email, time);

        notifier.Notify("GET api/v2/items", "/api/v2/items", 400);
        time.Now = Now.AddMinutes(14).AddSeconds(59);
        notifier.Notify("GET api/v2/items", "/api/v2/items", 900);

        Assert.Single(email.Sent);
    }

    [Fact]
    public void SlowRequestAfter15Minutes_SendsAMailAgain()
    {
        var email = new RecordingEmailService();
        var time = new ManualTime(Now);
        var notifier = CreateNotifier(email, time);

        notifier.Notify("GET api/v2/items", "/api/v2/items", 400);
        time.Now = Now.AddMinutes(15);
        notifier.Notify("GET api/v2/items", "/api/v2/items", 400);

        Assert.Equal(2, email.Sent.Count);
    }

    [Fact]
    public void CooldownIsPerEndpoint()
    {
        var email = new RecordingEmailService();
        var notifier = CreateNotifier(email, new ManualTime(Now));

        notifier.Notify("GET api/v2/items", "/api/v2/items", 400);
        notifier.Notify("GET api/v2/orders", "/api/v2/orders", 400);
        notifier.Notify("POST api/v2/items", "/api/v2/items", 400);

        Assert.Equal(3, email.Sent.Count);
    }

    [Fact]
    public void ThresholdAndCooldownCanBeChanged()
    {
        var email = new RecordingEmailService();
        var time = new ManualTime(Now);
        var notifier = CreateNotifier(email, time, new PerformanceOptions
        {
            AlertEmail = "team@example.com",
            SlowRequestThresholdMs = 1000,
            AlertCooldownMinutes = 1,
        });

        notifier.Notify("GET api/v2/items", "/api/v2/items", 999);
        Assert.Empty(email.Sent);

        notifier.Notify("GET api/v2/items", "/api/v2/items", 1001);
        time.Now = Now.AddMinutes(1);
        notifier.Notify("GET api/v2/items", "/api/v2/items", 1001);
        Assert.Equal(2, email.Sent.Count);
    }

    [Fact]
    public void WithoutAlertEmail_SendsNoMail()
    {
        var email = new RecordingEmailService();
        var notifier = CreateNotifier(email, new ManualTime(Now), new PerformanceOptions { AlertEmail = "" });

        Assert.Null(notifier.Notify("GET api/v2/items", "/api/v2/items", 5000));
        Assert.Empty(email.Sent);
    }
}
