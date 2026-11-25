#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT
project="$repo_dir/benchmarks/CargoHub.UptimeVerification/CargoHub.UptimeVerification.csproj"
results="$repo_dir/benchmarks/results/uptime"
image='louislam/uptime-kuma:2.5.12@sha256:8e4b6bef96417ec98a35dc103c99cc64fc229a01e584e56d95d9004d2003c02a'

dotnet run --project "$project" -c Release -- export "$work_dir/fixtures.json"
docker run --rm --entrypoint node \
    -e TEST_BACKEND=1 -e UPTIME_KUMA_LOG_LEVEL=ERROR \
    --mount "type=bind,source=$work_dir,target=/proof,readonly" \
    --mount "type=bind,source=$repo_dir/benchmarks/uptime,target=/verification,readonly" \
    "$image" /verification/kuma-reference.cjs /proof/fixtures.json > "$work_dir/kuma-output.txt"
dotnet run --project "$project" -c Release --no-build -- \
    compare "$work_dir/fixtures.json" "$work_dir/kuma-output.txt" "$results"
