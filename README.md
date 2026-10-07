# INF2C-Team-5

## SQLite-data en overstap naar C#

Alle JSON-brondata is geïmporteerd in [`Python WMS/data/cargohub.sqlite`](<Python WMS/data/cargohub.sqlite>).
Het [databaseschema](<Python WMS/data/schema.sql>) en de
[migratie- en C#-implementatiehandleiding](docs/SQLITE.md) beschrijven de tabellen,
relaties, herhaalbare import, verbindingen en transacties.
De bestaande Python-endpoints gebruiken voorlopig nog de ongewijzigde JSON-bestanden.

De database is voorbereid voor de C#-implementatie; er is geen runtime-koppeling toegevoegd.

## Workflow guidelines

To ensure code quality and catch issues early, we stick to the following agreements:

- **Always work in a separate branch.** Never make changes directly on `main`. Use a clear branch name, for example `feature/name-of-the-feature` or `fix/name-of-the-bug`.
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
cp .env.example .env        # then choose your own passwords in .env
docker compose up -d        # starts MySQL 8.4 on localhost:3307
```

`.env` contains your local passwords and is ignored by Git. Never commit it.

| Command | What it does |
|---------|--------------|
| `docker compose up -d` | Start MySQL |
| `docker compose down` | Stop MySQL (data is kept) |
| `docker compose down -v` | Stop MySQL and delete all data |

### 2. Run the API

```sh
dotnet run --project src/CargoHub.Api
```

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
