// Runs Kuma's own calculator from the official Docker image, without a database
// or a running monitor. TEST_BACKEND is Kuma's supported backend test switch.
const fs = require("node:fs");
const crypto = require("node:crypto");
const { createRequire } = require("node:module");
const requireKuma = createRequire("/app/package.json");
const dayjs = requireKuma("dayjs");
dayjs.extend(requireKuma("dayjs/plugin/utc"));
const { UptimeCalculator } = requireKuma("./server/uptime-calculator");
const version = requireKuma("./package.json").version;
const fixtures = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));

(async () => {
    const scenarios = [];
    for (const fixture of fixtures) {
        UptimeCalculator.currentDate = dayjs.utc(fixture.history.retrievedAt);
        const calculator = new UptimeCalculator();
        for (const beat of fixture.history.heartbeats) {
            await calculator.update(beat.status, 0, dayjs.utc(beat.time));
        }
        const result = calculator.getData(168, "hour");
        const buckets = calculator.getDataArray(168, "hour");
        scenarios.push({
            name: fixture.name,
            uptimePercentage: result.uptime * 100,
            upChecks: buckets.reduce((total, bucket) => total + (bucket.up || 0), 0),
            downChecks: buckets.reduce((total, bucket) => total + (bucket.down || 0), 0)
        });
    }
    const calculatorSha256 = crypto.createHash("sha256")
        .update(fs.readFileSync("/app/server/uptime-calculator.js")).digest("hex");
    process.stdout.write("@@UPTIME_RESULT@@" + JSON.stringify({ version, calculatorSha256, scenarios }));
})().catch(error => {
    console.error(error);
    process.exitCode = 1;
});
