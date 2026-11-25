namespace CargoHub.Api.Monitoring;

public sealed class UptimeKumaOptions
{
    public string Url { get; set; } = "";
    public int MonitorId { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Token { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 10;
}
