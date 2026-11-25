# Uptime monitoring

`GET /metrics/uptime?SLA=99.99` returns the CargoHub monitor's availability over
the preceding seven days in UTC. The endpoint is public and is listed in `/docs`.
`SLA` defaults to `99.99` and accepts decimal values from `0` through `100`.
Invalid values return HTTP 400 with problem details.

## Configure Uptime Kuma

Use the provided Uptime Kuma instance to create or select an HTTP monitor for the
externally reachable `https://YOUR_HOST/health` endpoint. `/health` checks the API
and MySQL. The monitor should accept HTTP 200 and follow the same public route
customers use. Choose the correct protocol for your server's TLS configuration.

Supply the monitor's numeric ID and Kuma URL. Socket.IO requires Kuma account
authentication. Use an existing Kuma login JWT with `loginByToken`, or a username
and password with `login`. A Prometheus API key is not a Socket.IO login token.
For an account using two-factor authentication, use an existing login token.

For local development, use .NET user secrets from `C# WMS/src/CargoHub.Api`:

```sh
dotnet user-secrets set 'UptimeKuma:Url' 'https://YOUR_KUMA_HOST'
dotnet user-secrets set 'UptimeKuma:MonitorId' 'YOUR_MONITOR_ID'
dotnet user-secrets set 'UptimeKuma:Token' 'YOUR_LOGIN_TOKEN'
```

Alternatively, set `UptimeKuma:Username` and `UptimeKuma:Password`. In production,
fill the `UPTIME_KUMA_*` variables in `C# WMS/deploy/.env`. The shared Compose
configuration passes these to both API containers. Keep credentials out of Git.
The connection URL must be reachable from the API containers and support the
`/socket.io/` WebSocket endpoint. TLS certificate validation remains enabled.

## Retrieval and calculation

The API connects through Socket.IO, authenticates, calls `getMonitor` to verify
access and obtain the configured check interval, and calls
`getMonitorBeats(monitorId, 168)` for the last 168 hours. Successful history is
cached for 30 seconds per API instance. Concurrent callers share one retrieval.
The returned `periodEnd` identifies the retrieval time of that snapshot. The
default timeout for connect, login and retrieval together is 10 seconds, adjustable
with `UptimeKuma:TimeoutSeconds` from 1 to 60 seconds.

Availability is weighted by elapsed time rather than the number of checks.
Intervals are clipped to the seven-day window. Kuma attributes the elapsed
interval before a heartbeat to its status. Adjacent timestamps preserve
sub-second precision when Kuma rounds `duration` to whole seconds.

Only status `1` (UP) counts as confirmed availability. DOWN, PENDING and
MAINTENANCE count as unavailable for this SLA. This conservative treatment of
maintenance may differ from the uptime displayed in Kuma itself.

An interval longer than twice the configured check interval leaves its earlier
portion unknown. A recent last heartbeat covers time through `periodEnd` only
when it is at most two check intervals old. This makes stopped, paused or stale
monitoring visible in `unknownSeconds` and `coveragePercentage`.

`uptimePercentage` is confirmed UP seconds divided by the full 604,800-second
week. With incomplete coverage it is a lower bound. `slaMet` is `null` until
the entire week is covered, and otherwise compares the unrounded percentage
against the requested SLA. Displayed percentages are rounded to six decimals.
An empty history, failed login, malformed upstream data or timeout returns
HTTP 503 with a safe problem-details message. Monitoring credentials and raw
upstream messages are not returned.

Example of a fully observed week with one minute of downtime:

```json
{
  "monitorId": 4,
  "periodStart": "2026-11-13T12:00:00+00:00",
  "periodEnd": "2026-11-20T12:00:00+00:00",
  "uptimePercentage": 99.990079,
  "coveragePercentage": 100,
  "sla": 99.99,
  "slaMet": true,
  "upSeconds": 604740,
  "downSeconds": 60,
  "unknownSeconds": 0,
  "allowedDowntimeSeconds": 60.48
}
```

99.99% permits 60.48 seconds of downtime over seven days. Check frequency affects
the precision of detected outages, and monitoring history must cover a full week
before weekly compliance can be assessed. The endpoint reports measured SLA
compliance. Actual service availability also depends on the running API, database,
proxy and deployment process.

## Verification

```sh
dotnet test 'C# WMS/CargoHub.slnx' --filter 'FullyQualifiedName~Uptime|FullyQualifiedName~MetricsControllerTests'
curl 'http://localhost:3000/metrics/uptime?SLA=99.99'
```

The new tests cover duration weighting, the 60.48-second SLA boundary, comparison
before rounding, missing history, stale measurements, query validation and HTTP
503 responses. Socket.IO tests run the real client against a local WebSocket peer
to verify login, monitor ID, the 168-hour argument, concurrent caching and failures.

For live acceptance, configure the provided Kuma monitor and compare the response
with its heartbeat history. Check both a complete week and a newly created monitor.
An active external monitor is required to verify the production uptime target.

The required protocol is described in the
[Uptime Kuma API documentation](https://uptime-kuma-api.readthedocs.io/en/latest/api.html#uptime_kuma_api.UptimeKumaApi.get_monitor_beats).
The implementation follows the
[Kuma socket handler](https://github.com/louislam/uptime-kuma/blob/1.23.16/server/server.js)
and the [SocketIOClient library](https://github.com/doghappy/socket.io-client-csharp).
