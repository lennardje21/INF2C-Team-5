# Load tests

[k6](https://k6.io) scripts that test the whole deployed stack (nginx → `api-a`/`api-b` →
MySQL) over HTTP. They only send GET requests, so they never change data. All four share
the endpoint mix in [`lib.js`](lib.js).

| Script | What it tests | Duration |
|--------|---------------|----------|
| [`load.js`](load.js) | 20 users, normal busy-day traffic. Should pass. | ~7 min |
| [`stress.js`](stress.js) | Raises the rate in steps up to `MAX_RATE` req/s (default 200) to find the limit. Stops by itself above 20% errors. | ~9 min |
| [`spike.js`](spike.js) | Jumps from 5 to `PEAK` users (default 300) in 10 seconds, then checks recovery. | ~3 min |
| [`soak.js`](soak.js) | `USERS` users (default 15) for `DURATION` (default `30m`), to find slow degradation. | ~33 min |

## Before you start

- **Tell the server admin.** The server is shared, and heavy traffic can look like an attack.
- Don't test while a deployment can happen (after a merge to `main`, the timer deploys within 5 minutes).
- Use an API key of an account that may read every endpoint, such as the superroot. The
  test checks the key first and stops if it does not work.

## Running

Install k6 (`winget install k6`, `brew install k6`, or see the k6 site), then from this folder:

```sh
k6 run -e API_KEY=yourkey load.js
k6 run -e API_KEY=yourkey -e MAX_RATE=300 stress.js
k6 run -e API_KEY=yourkey -e PEAK=500 spike.js
k6 run -e API_KEY=yourkey -e DURATION=1h -e USERS=20 soak.js
```

Add `-e BASE_URL=http://localhost:3000` to test a local API instead. Add
`--summary-export result.json` to save the summary.

Start with `load.js`. Only run the heavier tests once that one passes.

## Watching the server during a test

The k6 numbers tell you *that* it slows down; the server tells you *why*. In separate SSH
sessions on the server:

```sh
docker stats                                  # CPU and memory per container
free -m                                       # is the server swapping?
journalctl -u cargohub-watchdog -f            # did the watchdog restart a container?
cd "/srv/cargohub/C# WMS/deploy" && sudo -u cargohub docker compose -f docker-compose.prod.yml logs -f nginx
```

## Reading the results

- `http_req_duration`: latency. Look at `p(95)`, not the average.
- `http_req_failed`: share of requests that were not 200 or 404. 502/504 come from nginx:
  both containers were down or a request took over 10 seconds.
- `http_reqs`: throughput (requests per second).
- Thresholds marked ✗ failed. k6 then exits with a non-zero code.
- The results per endpoint show which endpoint is slowest (usually `GET /shipments`).

Your own connection can be the bottleneck. If the server's CPU stays low while latency
rises, run the test from another network or machine.

## Saved results

[`results/`](results/) keeps the dashboard reports of earlier runs (open them in a browser):

| Report | Run |
|--------|-----|
| [`2026-11-23-stress-before-pool-fix.html`](results/2026-11-23-stress-before-pool-fix.html) | `stress.js` with `MAX_RATE=120`, before the connection pool limit. Comfortable up to ~40 req/s, at most ~60 req/s. Above that, 500 errors because MySQL refused connections ("Too many connections", 151 max), and 502s because nginx skipped both containers after a single slow request. |

To save a new one, run with `K6_WEB_DASHBOARD=true` and `K6_WEB_DASHBOARD_EXPORT=stress-report.html`,
then rename the file into `results/` as `<date>-<test>-<what changed>.html`.
