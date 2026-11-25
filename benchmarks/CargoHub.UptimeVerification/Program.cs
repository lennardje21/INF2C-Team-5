using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CargoHub.Api.Monitoring;
using CargoHub.UptimeVerification;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
if (args.Length == 2 && args[0] == "export")
{
    await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(TransitionScenarios.All(), jsonOptions));
    return;
}
if (args.Length != 4 || args[0] != "compare")
    throw new ArgumentException("Use export <file>, or compare <fixtures> <kuma-output> <results-directory>.");

var fixtureText = await File.ReadAllTextAsync(args[1]);
var scenarios = JsonSerializer.Deserialize<TransitionScenario[]>(fixtureText, jsonOptions)!;
var referenceText = await File.ReadAllTextAsync(args[2]);
const string marker = "@@UPTIME_RESULT@@";
using var referenceJson = JsonDocument.Parse(referenceText[(referenceText.LastIndexOf(marker, StringComparison.Ordinal) + marker.Length)..]);
var reference = referenceJson.RootElement;
var results = new List<object>();
Directory.CreateDirectory(args[3]);
var csv = new StringBuilder("scenario,time_utc,status,duration_seconds\n");

foreach (var scenario in scenarios)
{
    var report = UptimeService.Calculate(scenario.History, 99.99m);
    var expectedPercentage = Math.Round(scenario.ExpectedUpSeconds * 100 / 604800m, 6);
    var kuma = reference.GetProperty("scenarios").EnumerateArray()
        .Single(r => r.GetProperty("name").GetString() == scenario.Name);
    var kumaPercentage = kuma.GetProperty("uptimePercentage").GetDecimal();
    var upChecks = scenario.History.Heartbeats.Count(b => b.Status == 1);
    var downChecks = scenario.History.Heartbeats.Count(b => b.Status == 0);
    var expectedKumaPercentage = 100m * upChecks / (upChecks + downChecks);
    var agrees = Math.Abs(report.UptimePercentage - kumaPercentage) <= 0.000001m;
    var passed = report.UpSeconds == scenario.ExpectedUpSeconds &&
                 report.DownSeconds == scenario.ExpectedDownSeconds &&
                 report.UptimePercentage == expectedPercentage && report.SlaMet == scenario.ExpectedSlaMet &&
                 Math.Abs(kumaPercentage - expectedKumaPercentage) < 0.000000001m &&
                 agrees == scenario.ExpectKumaAgreement;
    if (!passed) throw new InvalidOperationException("Comparison failed for " + scenario.Name);

    var firstDown = scenario.History.Heartbeats.First(b => b.Status == 0).Time;
    var lastDown = scenario.History.Heartbeats.Last(b => b.Status == 0).Time;
    var excerpt = scenario.History.Heartbeats
        .Where(b => b.Time >= firstDown.AddMinutes(-2) && b.Time <= lastDown.AddMinutes(2)).ToArray();
    foreach (var beat in excerpt)
        csv.AppendLine(string.Join(',', scenario.Name, beat.Time.ToString("O"), beat.Status,
            beat.Duration.ToString(CultureInfo.InvariantCulture)));

    results.Add(new
    {
        scenario.Name, passed, upChecks, downChecks, cargoHub = report,
        kumaPercentage, kumaUpChecks = kuma.GetProperty("upChecks").GetInt32(),
        kumaDownChecks = kuma.GetProperty("downChecks").GetInt32(),
        agreementExpected = scenario.ExpectKumaAgreement, agrees,
        differencePercentagePoints = report.UptimePercentage - kumaPercentage,
        transitionHeartbeats = excerpt
    });
    Console.WriteLine($"PASS {scenario.Name}: CargoHub={report.UptimePercentage:F6}% Kuma={kumaPercentage:F6}% UP={report.UpSeconds}s DOWN={report.DownSeconds}s");
}

var evidence = new
{
    verifiedAt = DateTimeOffset.UtcNow,
    fixtureKind = "Deterministic test data; no production outage was induced",
    inputSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fixtureText))).ToLowerInvariant(),
    kumaVersion = reference.GetProperty("version").GetString(),
    kumaCalculatorSha256 = reference.GetProperty("calculatorSha256").GetString(),
    referenceMethod = "Unmodified UptimeCalculator.update() and getData(168, hour) from the official Kuma image in TEST_BACKEND mode",
    scenarios = results
};
await File.WriteAllTextAsync(Path.Combine(args[3], "transition-comparison.json"), JsonSerializer.Serialize(evidence, jsonOptions));
await File.WriteAllTextAsync(Path.Combine(args[3], "transition-heartbeats.csv"), csv.ToString());
