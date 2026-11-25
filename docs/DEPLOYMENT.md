# Deployment

The API runs on the project server at `http://145.24.237.19/api/v2/...`.

## How it works

The server runs [`C# WMS/deploy/docker-compose.prod.yml`](<../C# WMS/deploy/docker-compose.prod.yml>):
MySQL (not reachable from outside), two containers of the API image that CI publishes
from `main` (`api-a` and `api-b`), and nginx on port 80 in front of them
([`nginx/default.conf`](<../C# WMS/deploy/nginx/default.conf>)). When one API container
is down, nginx sends every request to the other one.

Every 5 minutes a timer runs [`deploy.sh`](<../C# WMS/deploy/deploy.sh>), which updates
to the latest `main`. When there is a new image it restarts `api-a`, waits until it is
healthy, and only then restarts `api-b`, so the API stays up during a deployment. If
`api-a` does not become healthy, the deployment stops there and `api-b` keeps running
the old version.

Before restarting the API, `deploy.sh` applies the database migrations that come with the
new image (see [Database migrations](#database-migrations)).

Not updated automatically:

- **The timer files.** After changing them, repeat step 4 of the setup.
- **`deploy/.env`.** It only exists on the server.

## Getting access

1. Ask the server admin for an account (Hogeschool Rotterdam students and teachers only).
2. Log in once and choose your own password: `ssh your-username@145.24.237.19`
3. Set up an SSH key so you don't need the password every time:
   ```sh
   ssh-keygen -t ed25519
   ssh-copy-id your-username@145.24.237.19
   ```
4. Add this to `~/.ssh/config`, then connect with `ssh cargohub`:
   ```
   Host cargohub
       HostName 145.24.237.19
       User your-username
   ```
   Prefer a terminal over the VS Code **Remote - SSH** extension. The server has only
   1.6 GB of memory, and Remote - SSH runs a VS Code server there that uses around 400 MB,
   which pushes MySQL into swap. If you do use it, close the connection when you are done.

## Server setup (one time)

Already done on the current server. Run everything on the server.

1. Install Docker and Git, and create the user that runs the deployment:
   ```sh
   sudo apt update && sudo apt install -y docker.io docker-compose-v2 git
   sudo useradd --system --create-home --home-dir /var/lib/cargohub --groups docker cargohub
   ```
2. Clone the repository:
   ```sh
   sudo git clone https://github.com/lennardje21/INF2C-Team-5.git /srv/cargohub
   sudo chown -R cargohub:cargohub /srv/cargohub
   ```
3. Create `.env` and replace both `change-me` passwords with ones from `openssl rand -hex 24`:
   ```sh
   cd "/srv/cargohub/C# WMS/deploy"
   sudo -u cargohub cp .env.example .env
   sudo -u cargohub chmod 600 .env
   sudo -u cargohub nano .env
   ```
   Do this before the first start: MySQL only reads the passwords when it creates the database.
4. Install the timers (deploy, nightly backup, watchdog) and start the first deployment
   (the data import takes a few minutes):
   ```sh
   sudo cp cargohub-*.service cargohub-*.timer /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now cargohub-deploy.timer cargohub-backup.timer cargohub-watchdog.timer
   sudo systemctl start cargohub-deploy
   ```
5. Give the accounts their API keys (see below).

## Useful commands

Run these from `/srv/cargohub/C# WMS/deploy`:

| Command | What it does |
|---------|--------------|
| `sudo -u cargohub docker compose -f docker-compose.prod.yml ps` | Container status |
| `sudo -u cargohub docker compose -f docker-compose.prod.yml logs -f api-a api-b` | API logs |
| `sudo -u cargohub docker compose -f docker-compose.prod.yml logs -f nginx` | Requests, and errors reaching the API |
| `sudo -u cargohub docker compose -f docker-compose.prod.yml restart api-a` | Restart one API container (the other keeps serving) |
| `sudo systemctl start cargohub-deploy` | Deploy now |
| `journalctl -u cargohub-deploy -n 50` | Output of recent deployments |

Docker keeps at most 50 MB of logs per container (5 files of 10 MB, compressed) and
removes the oldest lines after that, so the logs only go back a limited time. Set in
`x-logging` in the compose file.

## Monitoring

`http://145.24.237.19/health` returns `200 Healthy`, or `503 Unhealthy` when the API
cannot reach MySQL. It needs no API key. Docker uses it as the healthcheck of both API
containers, so `ps` shows `(healthy)` or `(unhealthy)` next to the uptime.

To track the uptime percentage and get an email when the API goes down, add an HTTP
monitor for that URL in a free service such as [UptimeRobot](https://uptimerobot.com).
It has to run outside the server: a check on the server itself cannot report that the
server is down.

Docker restarts a container that crashes (`restart: unless-stopped`), but not one that
hangs. For that, a timer runs [`watchdog.sh`](<../C# WMS/deploy/watchdog.sh>) every 15
seconds, which restarts every container that is `unhealthy`, one at a time. An API
container counts as unhealthy after two failed checks 5 seconds apart, so a hung
container is restarted within about half a minute. When MySQL does not respond, the API
containers fail their checks too, so then it only restarts MySQL. It skips its turn while
a deployment runs. See what it did with `journalctl -u cargohub-watchdog`.

## Backups

Every night at 03:00 a timer runs [`backup.sh`](<../C# WMS/deploy/backup.sh>), which dumps
the database to `/var/lib/cargohub/backups/cargohub-<date>_<time>.sql.gz` and keeps the
newest 7. The API keeps working during the backup.

| Command | What it does |
|---------|--------------|
| `sudo systemctl start cargohub-backup` | Make a backup now (do this before risky changes) |
| `sudo ls -lh /var/lib/cargohub/backups` | List the backups |
| `journalctl -u cargohub-backup -n 20` | Output of recent backups |

The backups are on the same disk as the database, so they don't survive losing the server.
Copy one to your own computer now and then. On the server, put the newest one in your
home directory:

```sh
latest=$(sudo sh -c 'ls -1t /var/lib/cargohub/backups/*.sql.gz | head -1')
sudo install -m 600 -o "$USER" "$latest" ~/cargohub-backup.sql.gz
```

Then on your own computer download it, and remove the copy from the server:

```sh
scp cargohub:cargohub-backup.sql.gz . && ssh cargohub rm cargohub-backup.sql.gz
```

### Restoring

This replaces every table in the database with the version from the backup; changes made
after that backup are lost. Make a fresh backup first if the current data might still be
needed. From `/srv/cargohub/C# WMS/deploy`:

```sh
sudo gunzip -c /var/lib/cargohub/backups/<file>.sql.gz \
  | sudo -u cargohub docker compose -f docker-compose.prod.yml exec -T mysql \
    sh -c 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysql -u root'
```

The API picks up the restored data right away. API keys come back as they were at the time
of the backup.

## API keys

Every account has one API key; the database only stores its SHA-256 hash, so keys are
never in the repository and cannot be looked up later. A new database has no keys at all.
Give an account a key (this also replaces a lost or leaked one) from `/srv/cargohub/C# WMS`:

```sh
sudo -u cargohub scripts/set-api-key.sh --prod --list   # all accounts
sudo -u cargohub scripts/set-api-key.sh --prod 1        # new key for account 1, the superroot
```

The key is printed once and works right away. Store it in the password manager and
share it privately, never in the repository.

## Database migrations

`01-schema.sql` and `02-seed.sql` only run on an empty database. An existing database is
updated by the files in [`Data/MySql/migrations`](<../C# WMS/src/CargoHub.Api/Data/MySql/migrations>),
named `NNN-description.sql`, where `NNN` is the schema version the file upgrades to.
They are part of the API image, and `deploy.sh` applies every migration above the
database's `schema_version` before it restarts the API, after a backup
(see [Backups](#backups)). So a schema change reaches the server together with the code
that needs it, never earlier. While the migration runs and until `api-a` is replaced,
the old containers use the new schema, so expect errors for about a minute.

If a migration fails, the deployment stops before the API is restarted, so the old version
keeps running; the error is in `journalctl -u cargohub-deploy`. Every 5 minutes the next
deployment tries again, so fix the cause (if it is not temporary) and wait, or run
`sudo systemctl start cargohub-deploy`. A migration must be safe to run again after failing
at any statement: MySQL commits every DDL statement on its own, so write it like `002`
(remove what an earlier attempt created, insert in one transaction, set `schema_version`
as the very last statement).

The backup before a migration is called `cargohub-before-migration-NNN-description.sql.gz`.
It is made before the first attempt only and is never removed automatically, so it still
holds the data from before the migration after any number of retries. Restore it (see
[Restoring](#restoring)) to undo a migration; delete it yourself once it is no longer needed.

Changing the schema:

1. Add a migration `NNN-description.sql` that ends with
   ``UPDATE `schema_version` SET `version` = NNN;``. Make it stop on the wrong version,
   like [`002-accounts.sql`](<../C# WMS/src/CargoHub.Api/Data/MySql/migrations/002-accounts.sql>).
2. Make the same change in `01-schema.sql` (including the version in `schema_version`)
   and, if needed, `02-seed.sql`, `verify.sql` and `manifest.json`, so new databases end
   up the same.
3. Locally, either apply the migration to your database or start over with
   `docker compose down -v` (this deletes your local data). To apply it, from `C# WMS`:
   ```sh
   docker compose exec -T mysql sh -c 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysql -u root "$MYSQL_DATABASE"' \
       < src/CargoHub.Api/Data/MySql/migrations/NNN-description.sql
   ```

### 002: accounts

Migration 002 removes the old users and their API keys, and creates the superroot and a
root, admin and user for every warehouse, all without a key. Until the superroot has a
key, nobody can use the API, so give it one right after the deployment (see
[API keys](#api-keys)), and from there create the other accounts' keys.

### 003: floormanager and soft delete

Migration 003 adds the `floormanager` role and `deleted_at` to `account`, and gives the
account CHECK constraints names. It changes no data; existing keys keep working.
