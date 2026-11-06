#!/usr/bin/env bash
# Moves the server checkout to the latest main and restarts the containers whose
# image changed. Run by cargohub-deploy.timer; safe to run by hand as well.
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
compose=(docker compose --file "$repo_dir/C# WMS/deploy/docker-compose.prod.yml")

# The server checkout is only used for deploying, so local changes are discarded.
# Untracked files such as deploy/.env are kept.
git -C "$repo_dir" fetch --quiet origin main
git -C "$repo_dir" checkout --quiet --force --detach origin/main

"${compose[@]}" pull --quiet api-a api-b nginx

# One api container at a time, and only continue once it is healthy, so the other
# one keeps serving requests. --wait fails the deployment (visible in journalctl)
# when a container does not become healthy; the other one is then left untouched.
# The sleep gives nginx time to find the new container's IP (see nginx/default.conf)
# before the other one goes down.
"${compose[@]}" up --detach --wait --wait-timeout 300 mysql
"${compose[@]}" up --detach --no-deps --wait --wait-timeout 300 api-a
sleep 5
"${compose[@]}" up --detach --no-deps --wait --wait-timeout 300 api-b
"${compose[@]}" up --detach --remove-orphans nginx
# Picks up changes to nginx/default.conf without dropping connections. A broken config
# fails the deployment and nginx keeps running with the old one.
"${compose[@]}" exec -T nginx sh -c 'nginx -t -q && nginx -s reload 2> /dev/null'

# Keep a week of old images for rollbacks, remove the rest.
docker image prune --force --filter "until=168h" > /dev/null
