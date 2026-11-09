#!/usr/bin/env bash
# Restarts containers whose healthcheck fails. Docker itself only restarts containers
# that exit, not ones that hang. Run every minute by cargohub-watchdog.timer.
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
compose=(docker compose --file "$repo_dir/C# WMS/deploy/docker-compose.prod.yml")

# Skip while deploy.sh runs: it restarts containers on purpose.
exec 9< "$repo_dir/C# WMS/deploy"
flock --nonblock 9 || exit 0

unhealthy=$("${compose[@]}" ps --status running --format '{{.Service}} {{.Health}}' \
    | awk '$2 == "unhealthy" { print $1 }')

# The api containers are unhealthy whenever MySQL is, so then only MySQL is restarted.
# Their healthcheck reacts faster than MySQL's, so ask MySQL directly as well.
mysql_responds() {
    timeout 10 "${compose[@]}" exec -T mysql mysqladmin ping -h localhost --silent > /dev/null 2>&1
}
if grep -qx mysql <<< "$unhealthy" || { [ -n "$unhealthy" ] && ! mysql_responds; }; then
    unhealthy=mysql
fi

# One at a time, so that with both api containers unhealthy the first one is back
# before the second one goes down.
for service in $unhealthy; do
    echo "Restarting $service: healthcheck failed"
    "${compose[@]}" restart "$service"
    "${compose[@]}" up --detach --no-deps --no-recreate --wait --wait-timeout 120 "$service" \
        || echo "$service is still not healthy after the restart" >&2
done
