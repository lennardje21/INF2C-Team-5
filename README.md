# CargoHub – INF2C Team 5

CargoHub is a warehouse management API. We are migrating it from Python with JSON
files to C# (ASP.NET Core) with MySQL. The C# API exposes `/api/v2/...` routes.

## Repository structure

| Folder | Contents |
|--------|----------|
| `C# WMS/` | The new C# API. All new work happens here. |
| `Python WMS/` | The original Python API, kept as reference. Endpoints are listed in [`ENDPOINTS.md`](Python%20WMS/ENDPOINTS.md). |
| `docs/` | Guides for MySQL and deployment, and the benchmark report. |
| `postman/` | Postman collections for testing the API. |
| `benchmarks/`, `scripts/`, `tests/` | Benchmarks, data export scripts and their tests. |

## Workflow guidelines

- **Always work in a separate branch**, preferably created from a GitHub issue. Never change `main` directly.
- **Merge into `main` only through a pull request**, reviewed and approved by at least one other team member.
- **Write clear commit messages** that explain what changed and why.
- **Write code comments in English.**
- **Delete your branch** after it has been merged.

## Getting started

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) and
[Docker Desktop](https://www.docker.com/products/docker-desktop).

From the `C# WMS/` folder:

```sh
test -f .env || cp .env.example .env   # local passwords, never commit this file
docker compose up -d                   # start MySQL and import the data

export ConnectionStrings__CargoHub='Server=localhost;Port=3307;Database=cargohub;User ID=cargohub;Password=YOUR_PASSWORD'
dotnet run --project src/CargoHub.Api  # API on http://localhost:3000

dotnet test                            # run the tests
```

Use the password from your `.env`. `docker compose down` stops MySQL and keeps the
data; `docker compose down -v` also deletes it. More about the database is in
[docs/MYSQL.md](docs/MYSQL.md).

## Authentication

Every request needs an `API_KEY` header. Each key belongs to an account; the database
only stores a hash of it, so keys are never in the repository. Give a local account a key
(account 1 is the superroot, which may use every endpoint):

```sh
cd "C# WMS"
scripts/set-api-key.sh --list   # all accounts
scripts/set-api-key.sh 1        # prints a new key for account 1, once
```

From there, accounts are managed through `/api/v2/accounts`. Each role manages the role
directly below it, in its own warehouse: the superroot manages roots (in every warehouse),
a root manages admins, and an admin manages floormanagers and users. Roots and admins also
see the accounts further down, but cannot change them. Nobody changes or deletes their own
account, or grants access they do not have themselves; that gives `403`, as does an account
in another warehouse. A duplicate email gives `409`.

Creating an account (`POST /api/v2/accounts`) returns its API key once;
`POST /api/v2/accounts/{id}/api_key` replaces a key, also your own. `DELETE` is a soft
delete: the account stays in the database without name, email or key, so its key stops
working at once and the account no longer appears in the API.

```sh
curl -H "API_KEY: your-key" http://localhost:3000/api/v2/warehouses/1
```

A missing or unknown key returns `401`; a key without permission for that action
returns `403`. Use Postman, curl or the endpoint overview below: a browser address
bar cannot send this header.

## Error responses

Errors use `application/problem+json` with `status`, `title`, `detail`, `instance`
(the request path) and `traceId`. Validation errors also include an `errors`
object with the affected fields. Existing validation and conflict messages are
preserved in `detail`.

Unknown paths return `404` with a description of the unmatched route, even without
an API key. Unsupported HTTP methods return `405` with an `Allow` header. Valid
API routes still enforce API-key authentication and permissions.

Unexpected errors return `500` with a safe message and a `traceId` for finding the
exception in the server logs. Internal exception messages are not returned.

## Endpoint overview

All endpoints are listed at [`http://localhost:3000/docs`](http://localhost:3000/docs),
with their parameters and responses. Click **Authentication**, enter your API key, and
use **Test Request** to call an endpoint from the browser. The raw OpenAPI document
is at `/openapi/v2.json`. Neither page needs a key.

## Uptime monitoring

The public `/metrics/uptime?SLA=99.99` endpoint reports weekly availability from
Uptime Kuma. Configuration, the calculation and monitoring setup are described in
[docs/UPTIME.md](docs/UPTIME.md).

## CI/CD

The [pipeline](.github/workflows/ci-cd.yml) runs on every pull request and on `main`.
It builds the C# solution and runs all tests. On `main` it also publishes a Docker
image to `ghcr.io/lennardje21/inf2c-team-5`.

## Deployment

The API runs on our project server:

```
http://145.24.237.19/api/v2/...
```

The endpoint overview is at [`http://145.24.237.19/docs`](http://145.24.237.19/docs).

The server checks for a new image every 5 minutes, so a merged pull request is live
a few minutes after the pipeline passes. Server access, setup and maintenance are
described in [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).
