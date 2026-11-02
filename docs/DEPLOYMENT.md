# Deployment

The API runs on the project server at `http://145.24.237.19:8000/api/v1/...`.

## How it works

The server runs [`C# WMS/deploy/docker-compose.prod.yml`](<../C# WMS/deploy/docker-compose.prod.yml>):
MySQL (not reachable from outside) and the API image that CI publishes from `main`.
Every 5 minutes a timer runs [`deploy.sh`](<../C# WMS/deploy/deploy.sh>), which updates
to the latest `main` and restarts the API when there is a new image.

Not updated automatically:

- **The timer files.** After changing them, repeat step 4 of the setup.
- **The database schema.** The SQL files only run on an empty database; see [MYSQL.md](MYSQL.md).
- **`deploy/.env`.** It only exists on the server.

## Getting access

1. Ask the server admin for an account (Hogeschool Rotterdam students and teachers only).
2. Log in once and choose your own password: `ssh your-username@145.24.237.19`
3. Set up an SSH key so you don't need the password every time:
   ```sh
   ssh-keygen -t ed25519
   ssh-copy-id your-username@145.24.237.19
   ```
4. Add this to `~/.ssh/config`, then connect with `ssh cargohub` or with the VS Code
   **Remote - SSH** extension:
   ```
   Host cargohub
       HostName 145.24.237.19
       User your-username
   ```

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
4. Install the timer and start the first deployment (the data import takes a few minutes):
   ```sh
   sudo cp cargohub-deploy.service cargohub-deploy.timer /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now cargohub-deploy.timer
   sudo systemctl start cargohub-deploy
   ```
5. Replace the public API keys (see below).

## Useful commands

Run these from `/srv/cargohub/C# WMS/deploy`:

| Command | What it does |
|---------|--------------|
| `sudo -u cargohub docker compose -f docker-compose.prod.yml ps` | Container status |
| `sudo -u cargohub docker compose -f docker-compose.prod.yml logs -f api` | API logs |
| `sudo systemctl start cargohub-deploy` | Deploy now |
| `journalctl -u cargohub-deploy -n 50` | Output of recent deployments |

## API keys

The keys in the seed data are public on GitHub, so the server needs its own. Open MySQL
with the root password from `.env`:

```sh
sudo -u cargohub docker compose -f docker-compose.prod.yml exec mysql mysql -u root -p cargohub
```

List the keys with `SELECT api_key, app FROM user;`, then run this for each key with a
new key from `openssl rand -hex 24`:

```sql
SET @old = 'old-key', @new = 'new-key';
INSERT INTO `user`            SELECT @new, app FROM `user` WHERE api_key = @old;
INSERT INTO `user_endpoint`   SELECT @new, resource FROM `user_endpoint` WHERE api_key = @old;
INSERT INTO `user_permission` SELECT @new, resource, method, allowed FROM `user_permission` WHERE api_key = @old;
DELETE FROM `user` WHERE api_key = @old;
```

The new keys work within a minute. Share them privately, never in the repository.
