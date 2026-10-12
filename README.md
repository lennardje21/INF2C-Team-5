# INF2C-Team-5

## MySQL-data en C#

Alle 80.471 JSON-bronrecords zijn omgezet naar het
[MySQL-schema](<C# WMS/src/CargoHub.Api/Data/MySql/01-schema.sql>) en het
[importbestand](<C# WMS/src/CargoHub.Api/Data/MySql/02-seed.sql>).
Docker Compose importeert deze bestanden bij de eerste start met een leeg datavolume.
De [MySQL-implementatiehandleiding](docs/MYSQL.md) beschrijft herexport, import in
bestaande installaties en de C#-repositories. De Python-API blijft JSON gebruiken.

Het [vergelijkingsrapport Python/JSON versus C#/MySQL](docs/PYTHON_MYSQL_BENCHMARK.md)
bevat succespercentages, gemeten snelheid, P95-latency en reproduceerbare testresultaten.

## Workflow guidelines

To ensure code quality and catch issues early, we stick to the following agreements:

- **Always work in a separate branch.** Never make changes directly on `main`. Use a clear branch name,preferrably create a branch through the github projects/issues system.
- **Open a pull request for your changes.** Only merge into `main` through a pull request, not via a direct push or merge.
- **Have your code reviewed by another team member.** Every pull request must be reviewed and approved by at least one other team member before it is merged. This gives everyone a good overview of what has been done and prevents new bugs from ending up on main.
- **Write clear commit messages** that explain what changed and why.
- **Write code comments in English**, so the codebase stays consistent and understandable for the whole team.
- **Delete your branch** after it has been merged, to keep the repository tidy.

## Repository structure

| Folder | Contents |
|--------|----------|
| `Python WMS/` | The original CargoHub API (Python, JSON files). Kept as reference during the migration. |
| `C# WMS/` | The new CargoHub API (C#, ASP.NET Core, MySQL). All new work happens here. |

The endpoints of the original API are listed in [`Python WMS/ENDPOINTS.md`](Python%20WMS/ENDPOINTS.md). The C# version keeps the same `/api/v1/...` routes.

## Getting started (C# WMS)

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)

### 1. Start the database

From the `C# WMS/` folder:

```sh
test -f .env || cp .env.example .env  # preserve existing credentials
docker compose up -d        # starts MySQL 8.4 and imports the data on an empty volume
```

`.env` contains your local passwords and is ignored by Git. Never commit it.

| Command | What it does |
|---------|--------------|
| `docker compose up -d` | Start MySQL |
| `docker compose down` | Stop MySQL (data is kept) |
| `docker compose down -v` | Stop MySQL and delete all data |

The import runs only on a new, empty volume. For an existing database, follow
[the manual import instructions](docs/MYSQL.md). Do not remove a volume that contains work.

### 2. Run the API

Set the connection string with the same database credentials as `.env`:

```sh
export ConnectionStrings__CargoHub='Server=localhost;Port=3307;Database=cargohub;User ID=cargohub;Password=YOUR_PASSWORD'
dotnet run --project src/CargoHub.Api
```

The repositories are registered for dependency injection. Existing controllers
and services have not been wired to them; implement that integration as described
in [the MySQL guide](docs/MYSQL.md).

The API runs on `http://localhost:3000`, the same port as the Python version, so existing Postman requests keep working. Stop the Python server first if it is running.

### 3. Run the tests

```sh
dotnet test
```

### Project structure

```
C# WMS/
├── docker-compose.yml          MySQL container
├── .env.example                template for your local .env
├── CargoHub.slnx               solution file
├── src/CargoHub.Api/
│   ├── Controllers/            one controller per resource, handles requests and status codes
│   ├── Services/               business rules (e.g. transfer commit, stock checks)
│   ├── Models/                 entities that map to the MySQL tables
│   └── Data/                   database context and migrations
└── tests/CargoHub.Tests/       xUnit tests
    ├── Controllers/
    └── Services/
```
