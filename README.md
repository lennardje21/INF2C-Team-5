# CargoHub – INF2C Team 5

CargoHub is a warehouse management API. We are migrating it from Python with JSON
files to C# (ASP.NET Core) with MySQL. The C# API keeps the same `/api/v1/...` routes.

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

Every request needs an `API_KEY` header. Locally you can use the keys from the seed data.

```sh
curl -H "API_KEY: your-key" http://localhost:3000/api/v1/warehouses/1
```

A missing or unknown key returns `401`; a key without permission for that action
returns `403`. Use Postman, curl or the endpoint overview below: a browser address
bar cannot send this header.

## Endpoint overview

All endpoints are listed at [`http://localhost:3000/docs`](http://localhost:3000/docs),
with their parameters and responses. Click **Authentication**, enter your API key, and
use **Test Request** to call an endpoint from the browser. The raw OpenAPI document
is at `/openapi/v1.json`. Neither page needs a key.

## CI/CD

The [pipeline](.github/workflows/ci-cd.yml) runs on every pull request and on `main`.
It builds the C# solution and runs all tests. On `main` it also publishes a Docker
image to `ghcr.io/lennardje21/inf2c-team-5`.

## Deployment

The API runs on our project server:

```
http://145.24.237.19:8000/api/v1/...
```

The endpoint overview is at [`http://145.24.237.19:8000/docs`](http://145.24.237.19:8000/docs).

The server checks for a new image every 5 minutes, so a merged pull request is live
a few minutes after the pipeline passes. Server access, setup and maintenance are
described in [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).
