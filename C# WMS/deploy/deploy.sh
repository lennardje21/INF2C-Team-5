#!/usr/bin/env bash
# Moves the server checkout to the latest main and restarts the containers whose
# image changed. Run by cargohub-deploy.timer; safe to run by hand as well.
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
compose=(docker compose --file "$repo_dir/C# WMS/deploy/docker-compose.prod.yml")

# Keeps watchdog.sh from restarting containers while this script replaces them.
exec 9< "$repo_dir/C# WMS/deploy"
flock 9

# The server checkout is only used for deploying, so local changes are discarded.
# Untracked files such as deploy/.env are kept.
git -C "$repo_dir" fetch --quiet origin main
git -C "$repo_dir" checkout --quiet --force --detach origin/main

"${compose[@]}" pull --quiet api-a api-b nginx

"${compose[@]}" up --detach --wait --wait-timeout 300 mysql

# Database migrations come from the new image, so the schema changes together with the
# code that needs it: never before CI has published that code. Every migration numbered
# above schema_version is applied in order, after a backup. The running api containers
# use the old schema until they are replaced below, which takes about a minute.
migrations_dir=/app/Data/MySql/migrations
# Over TCP: while a new database imports the seed, MySQL already reports healthy but runs a
# temporary server that only accepts socket connections, so this waits for the real one.
mysql_root() {
    "${compose[@]}" exec -T mysql sh -c \
        'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysql -h 127.0.0.1 -u root -N "$MYSQL_DATABASE"'
}
in_new_image() {
    "${compose[@]}" run --rm --no-deps -T --entrypoint "$1" api-a "${@:2}"
}
mysql_ready_by=$((SECONDS + 900))
until mysql_root <<< 'SELECT 1;' > /dev/null 2>&1; do
    if (( SECONDS > mysql_ready_by )); then
        echo "MySQL did not accept connections within 15 minutes." >&2
        exit 1
    fi
    sleep 5
done
schema_version="$(mysql_root <<< 'SELECT MAX(`version`) FROM `schema_version`;')"
pending=()
# Older images have no migrations directory; that simply means there is nothing to apply.
for name in $(in_new_image ls "$migrations_dir" 2> /dev/null || true); do
    if [[ "$name" =~ ^([0-9]{3})-.+\.sql$ ]] && (( 10#${BASH_REMATCH[1]} > schema_version )); then
        pending+=("$name")
    fi
done
if (( ${#pending[@]} > 0 )); then
    "$repo_dir/C# WMS/deploy/backup.sh"
    for name in "${pending[@]}"; do
        echo "Applying database migration $name"
        in_new_image cat "$migrations_dir/$name" | mysql_root
    done
fi

# One api container at a time, and only continue once it is healthy, so the other
# one keeps serving requests. --wait fails the deployment (visible in journalctl)
# when a container does not become healthy; the other one is then left untouched.
# The sleep gives nginx time to find the new container's IP (see nginx/default.conf)
# before the other one goes down.
"${compose[@]}" up --detach --no-deps --wait --wait-timeout 300 api-a
sleep 5
"${compose[@]}" up --detach --no-deps --wait --wait-timeout 300 api-b
"${compose[@]}" up --detach --remove-orphans nginx
# Picks up changes to nginx/default.conf without dropping connections. A broken config
# fails the deployment and nginx keeps running with the old one.
"${compose[@]}" exec -T nginx sh -c 'nginx -t -q && nginx -s reload 2> /dev/null'

# Keep a week of old images for rollbacks, remove the rest.
docker image prune --force --filter "until=168h" > /dev/null
