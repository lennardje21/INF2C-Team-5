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

"${compose[@]}" pull --quiet api
"${compose[@]}" up --detach --remove-orphans

# Keep a week of old images for rollbacks, remove the rest.
docker image prune --force --filter "until=168h" > /dev/null
