#!/usr/bin/env bash
# Dumps the MySQL database to a gzipped SQL file and keeps the newest 7.
# Run daily by cargohub-backup.timer; safe to run by hand as well.
#
# With a label (backup.sh before-migration-002) it writes cargohub-<label>.sql.gz instead:
# only once, so a retry keeps the first one, and never removed by the rotation below.
# deploy.sh uses this before a database migration.
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
compose=(docker compose --file "$repo_dir/C# WMS/deploy/docker-compose.prod.yml")

# Outside the repository checkout, so deployments never touch the backups.
backup_dir="${BACKUP_DIR:-$HOME/backups}"
keep=7

# The dumps contain all data, so only the cargohub user may read them.
umask 077
mkdir -p "$backup_dir"

label="${1:-}"
if [[ -n "$label" ]]; then
    file="$backup_dir/cargohub-$label.sql.gz"
    if [[ -e "$file" ]]; then
        echo "Backup already exists, keeping it: $file"
        exit 0
    fi
else
    file="$backup_dir/cargohub-$(date +%Y-%m-%d_%H%M).sql.gz"
fi
trap 'rm -f "$file.tmp"' EXIT

# --single-transaction gives a consistent dump without locking the tables, so the API
# keeps working. Written to a .tmp file first so a failed dump never looks like a backup.
"${compose[@]}" exec -T mysql sh -c \
    'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysqldump -u root --single-transaction --routines --triggers --databases "$MYSQL_DATABASE"' \
    | gzip > "$file.tmp"
mv "$file.tmp" "$file"
echo "Backup written: $file ($(du -h "$file" | cut -f1))"

# Remove everything but the newest $keep dated backups; labelled ones are kept.
ls -1t "$backup_dir"/cargohub-[0-9]*.sql.gz | tail -n +$((keep + 1)) | xargs -r rm --
