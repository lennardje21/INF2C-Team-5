#!/usr/bin/env bash
# Gives an account a new random API key and prints it once. Only the SHA-256 hash is
# stored, so a lost key cannot be looked up: run this again to replace it. Any previous
# key of the account stops working.
#
# Usage, from anywhere:
#   scripts/set-api-key.sh <account-id>          local MySQL (docker-compose.yml)
#   scripts/set-api-key.sh --prod <account-id>   server MySQL (deploy/docker-compose.prod.yml)
#
# Account 1 is the superroot. List the accounts with --list:
#   scripts/set-api-key.sh [--prod] --list
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
compose=(docker compose)
if [[ "${1:-}" == "--prod" ]]; then
    compose+=(--file deploy/docker-compose.prod.yml)
    shift
fi

# MYSQL_PWD keeps the password off the command line and avoids mysql's warning about it.
mysql_in_container() {
    "${compose[@]}" exec -T mysql sh -c 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -u root -N "$MYSQL_DATABASE"'
}

if [[ "${1:-}" == "--list" ]]; then
    echo "SELECT id, role, IFNULL(warehouse_id, '-'), app, email, IF(api_key_hash IS NULL, 'no key', 'has key')
          FROM account ORDER BY id;" | mysql_in_container | column -t -s $'\t'
    exit 0
fi

if [[ ! "${1:-}" =~ ^[1-9][0-9]*$ ]]; then
    echo "Usage: $0 [--prod] <account-id> | [--prod] --list" >&2
    exit 2
fi
account_id=$1

key="$(openssl rand -hex 32)"

# The key goes in over stdin, so it never appears in the process list.
account="$(mysql_in_container <<SQL
UPDATE account SET api_key_hash = SHA2('$key', 256), updated_at = UTC_TIMESTAMP(6)
WHERE id = $account_id;
SELECT CONCAT(role, ' ', name, ' (', app, ')') FROM account WHERE id = $account_id;
SQL
)"

if [[ -z "$account" ]]; then
    echo "Account $account_id does not exist." >&2
    exit 1
fi

echo "New API key for account $account_id, $account:"
echo
echo "    $key"
echo
echo "It is shown only this once and works right away. Share it privately, never in the repository."
