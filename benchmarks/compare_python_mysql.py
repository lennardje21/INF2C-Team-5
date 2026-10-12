"""Compare actual Python model operations with the C# MySQL repositories.

Run against an isolated, freshly seeded benchmark database. Writes never use the
repository's JSON files: a temporary copy is made for the original Python models.
"""

import argparse
from datetime import datetime, timezone
from decimal import Decimal
import hashlib
import http.client
import importlib
import json
import os
from pathlib import Path
import platform
import random
import shutil
import socketserver
import statistics
import subprocess
import sys
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Python WMS/api"))
from providers import data_provider, auth_provider
from models.base import Base
from main import ApiRequestHandler

MODELS = {
    "warehouse": ("warehouses", "Warehouses"), "client": ("clients", "Clients"),
    "supplier": ("suppliers", "Suppliers"), "item_line": ("item_lines", "ItemLines"),
    "item_group": ("item_groups", "ItemGroups"), "item_type": ("item_types", "ItemTypes"),
    "location": ("locations", "Locations"), "item": ("items", "Items"),
    "inventory": ("inventories", "Inventories"), "order": ("orders", "Orders"),
    "order_item": ("order_items", "OrderItems"), "shipment": ("shipments", "Shipments"),
    "shipment_item": ("shipment_items", "ShipmentItems"), "transfer": ("transfers", "Transfers"),
    "transfer_item": ("transfer_items", "TransferItems"),
}
READS = ["item_one", "item_missing", "item_all", "inventory_all", "inventory_by_item",
         "order_items", "shipment_items", "inventory_totals", "order_with_items",
         "shipment_with_items", "transfer_with_items"]
WRITES = ["inventory_update", "item_update", "group_roundtrip"]


def canonical(value, key=""):
    if isinstance(value, dict):
        return {k: canonical(v, k) for k, v in value.items()}
    if isinstance(value, list):
        rows = [canonical(v) for v in value]
        def sort_key(row):
            if isinstance(row, dict):
                if "id" in row:
                    return (row["id"],)
                return (row.get("item_id", 0), row.get("location_id", 0), row.get("amount", 0))
            return (str(row),)
        return sorted(rows, key=sort_key)
    if isinstance(value, str) and (key.endswith("_at") or key.endswith("_date")):
        date = datetime.fromisoformat(value.replace("Z", "+00:00"))
        return date.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")
    if isinstance(value, float):
        return Decimal(str(value))
    return value


def python_operation(name):
    if name.startswith("table_"):
        module, cls = MODELS[name[6:]]
        return getattr(importlib.import_module("models." + module), cls)(data_provider.ROOT_PATH).data
    if name == "item_one": return data_provider.fetch_item_pool().get_item(600)
    if name == "item_missing": return data_provider.fetch_item_pool().get_item(-1)
    if name == "item_all": return data_provider.fetch_item_pool().get_items()
    if name == "inventory_all": return data_provider.fetch_inventory_pool().get_inventories()
    if name == "inventory_by_item": return data_provider.fetch_inventory_pool().get_inventories_for_item(600)
    if name == "inventory_totals": return data_provider.fetch_inventory_pool().get_inventory_totals_for_item(600)
    if name == "order_items": return data_provider.fetch_order_item_pool().get_items_for_order(4854)
    if name == "shipment_items": return data_provider.fetch_shipment_item_pool().get_items_for_shipment(6132)
    if name == "order_with_items": return data_provider.fetch_order_pool().get_order(4854)
    if name == "shipment_with_items": return data_provider.fetch_shipment_pool().get_shipment(6132)
    if name == "transfer_with_items": return data_provider.fetch_transfer_pool().get_transfer(800)
    if name == "inventory_update":
        pool = data_provider.fetch_inventory_pool()
        row = pool.get_inventory(600, 121)
        row["quantity_on_hand"] += 1
        pool.update_inventory(600, 121, row)
        pool.save()
        return row
    if name == "item_update":
        pool = data_provider.fetch_item_pool()
        row = pool.get_item(600)
        row["min_purchase_qty"] += 1
        pool.update_item(600, row)
        pool.save()
        return row
    if name == "group_roundtrip":
        pool = data_provider.fetch_item_group_pool()
        row = {"id": 900001, "name": "Benchmark 🚚", "description": "O'Brien"}
        pool.add_item_group(row)
        pool.save()
        pool = data_provider.fetch_item_group_pool()
        pool.remove_item_group(900001)
        pool.save()
        return row
    raise ValueError(name)


def timed_python(name, count):
    timings = []
    for _ in range(count):
        before = time.perf_counter_ns()
        result = python_operation(name)
        timings.append((time.perf_counter_ns() - before) / 1_000_000)
    return {"timings_ms": timings, "result": canonical(result)}


class QuietHandler(ApiRequestHandler):
    def log_message(self, *args):
        pass


def http_checks(rpc):
    users = json.loads(Path(auth_provider.ROOT_PATH, "user.json").read_text())
    user = max(users, key=lambda u: sum(p.get("get", False) for p in u["endpoint_access"].values()))
    checks = []
    with socketserver.TCPServer(("127.0.0.1", 0), QuietHandler) as server:
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            for route, name in [
                ("items/600", "item_one"), ("items", "item_all"),
                ("inventories", "inventory_all"), ("orders/4854", "order_with_items"),
                ("shipments/6132", "shipment_with_items"), ("transfers/800", "transfer_with_items")
            ]:
                connection = http.client.HTTPConnection("127.0.0.1", server.server_address[1], timeout=30)
                connection.request("GET", "/api/v1/" + route, headers={"API_KEY": user["api_key"]})
                response = connection.getresponse()
                payload = response.read()
                passed = response.status == 200 and canonical(json.loads(payload)) == rpc(name, 1)["result"]
                checks.append({"route": "/api/v1/" + route, "status": response.status, "passed": passed})
                connection.close()
            connection = http.client.HTTPConnection("127.0.0.1", server.server_address[1], timeout=30)
            connection.request("GET", "/api/v1/items", headers={"API_KEY": "invalid-benchmark-key"})
            response = connection.getresponse()
            response.read()
            checks.append({"route": "invalid API key", "status": response.status, "passed": response.status == 401})
            connection.close()
        finally:
            server.shutdown()
            thread.join()
    return checks


def percentile(values, fraction):
    ordered = sorted(values)
    position = (len(ordered) - 1) * fraction
    lo = int(position)
    hi = min(lo + 1, len(ordered) - 1)
    return ordered[lo] + (ordered[hi] - ordered[lo]) * (position - lo)


def stats(values):
    return {"n": len(values), "median_ms": statistics.median(values),
            "mean_ms": statistics.mean(values), "p95_ms": percentile(values, .95),
            "min_ms": min(values), "max_ms": max(values),
            "serial_ops_per_second": 1000 / statistics.mean(values)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dll", required=True, type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / "benchmarks/results/python_mysql.json")
    parser.add_argument("--rounds", type=int, default=10)
    parser.add_argument("--batch", type=int, default=20)
    parser.add_argument("--warmup", type=int, default=20)
    args = parser.parse_args()
    if not os.environ.get("CARGOHUB_BENCH_MYSQL"):
        parser.error("Set CARGOHUB_BENCH_MYSQL to an isolated seeded database")
    if args.rounds < 1 or args.batch < 1 or args.warmup < 1:
        parser.error("Rounds, batch and warmup must be positive")
    started = datetime.now(timezone.utc)
    hashes = {p.name: hashlib.sha256(p.read_bytes()).hexdigest()
              for p in (ROOT / "Python WMS/data").glob("*.json")}
    with tempfile.TemporaryDirectory(prefix="cargohub-python-benchmark-") as directory:
        for name in hashes:
            shutil.copyfile(ROOT / "Python WMS/data" / name, Path(directory) / name)
        data_provider.ROOT_PATH = directory + os.sep
        auth_provider.ROOT_PATH = directory + os.sep
        Base.get_timestamp = lambda self: "2026-10-12T00:00:00Z"
        process = subprocess.Popen(["dotnet", str(args.dll)], stdin=subprocess.PIPE,
                                   stdout=subprocess.PIPE, text=True, env=os.environ.copy())
        def rpc(name, count):
            process.stdin.write(json.dumps({"name": name, "count": count}) + "\n")
            process.stdin.flush()
            line = process.stdout.readline()
            if not line:
                raise RuntimeError(f"C# worker stopped with code {process.poll()}")
            response = json.loads(line, parse_float=Decimal)
            if "error" in response:
                raise RuntimeError(response["error"])
            return {"timings_ms": [float(v) for v in response["timings_ms"]],
                    "result": canonical(response["result"])}
        try:
            table_checks = []
            for table in MODELS:
                name = "table_" + table
                expected = canonical(python_operation(name))
                actual = rpc(name, 1)["result"]
                table_checks.append({"table": table, "records": len(expected), "passed": expected == actual})
                print(f"Full table comparison: {table} ({len(expected)} records)", flush=True)
            api_checks = http_checks(rpc)
            benchmarks = {name: {"python_ms": [], "mysql_ms": [], "equivalence_checks": []}
                          for name in READS + WRITES}
            # Untimed output comparison after each batch; timing excludes IPC/JSON transport.
            for name in READS + WRITES:
                old = timed_python(name, args.warmup)
                new = rpc(name, args.warmup)
                if old["result"] != new["result"]:
                    raise ValueError(f"Warm-up mismatch: {name}")
            rng = random.Random(20261012)
            for round_number in range(args.rounds):
                for group in (READS, WRITES):
                    names = list(group)
                    rng.shuffle(names)
                    for name in names:
                        if rng.randrange(2):
                            old = timed_python(name, args.batch)
                            new = rpc(name, args.batch)
                        else:
                            new = rpc(name, args.batch)
                            old = timed_python(name, args.batch)
                        benchmarks[name]["python_ms"].extend(old["timings_ms"])
                        benchmarks[name]["mysql_ms"].extend(new["timings_ms"])
                        passed = old["result"] == new["result"]
                        benchmarks[name]["equivalence_checks"].append(passed)
                        if not passed:
                            raise ValueError(f"Measured output mismatch: {name}, round {round_number}")
                print(f"Paired benchmark round {round_number + 1}/{args.rounds} done", flush=True)
            # Re-open all models after committed writes to check actual persisted data.
            persistence = []
            for table in ("item", "inventory", "item_group"):
                expected = canonical(python_operation("table_" + table))
                actual = rpc("table_" + table, 1)["result"]
                persistence.append({"table": table, "passed": expected == actual})
            for name, result in benchmarks.items():
                old, new = stats(result["python_ms"]), stats(result["mysql_ms"])
                result["python"], result["mysql"] = old, new
                result["median_speedup"] = old["median_ms"] / new["median_ms"]
                result["median_time_reduction_percent"] = (1 - new["median_ms"] / old["median_ms"]) * 100
                result["category"] = "write" if name in WRITES else "read"
            report = {"started_utc": started.isoformat(), "finished_utc": datetime.now(timezone.utc).isoformat(),
                      "environment": {"python": platform.python_version(), "os": platform.platform(),
                                      "architecture": platform.machine(), "csharp_worker": str(args.dll),
                                      "dotnet_sdk": subprocess.check_output(["dotnet", "--version"], text=True).strip()},
                      "method": {"rounds": args.rounds, "batch": args.batch, "warmup_per_scenario": args.warmup,
                                 "random_seed": 20261012, "concurrency": 1,
                                 "timed_scope": "storage operation, excluding HTTP and result serialization",
                                 "ordering_compared": False, "fixed_write_timestamp": "2026-10-12T00:00:00Z"},
                      "source_sha256": hashes, "table_checks": table_checks,
                      "python_http_checks": api_checks, "persistence_checks": persistence,
                      "benchmarks": benchmarks}
            all_checks = ([row["passed"] for row in table_checks + api_checks + persistence] +
                          [passed for row in benchmarks.values() for passed in row["equivalence_checks"]])
            report["functional_checks"] = {"passed": sum(all_checks), "total": len(all_checks),
                                           "success_rate_percent": 100 * sum(all_checks) / len(all_checks)}
            args.output.parent.mkdir(parents=True, exist_ok=True)
            args.output.write_text(json.dumps(report, indent=2) + "\n")
        finally:
            process.stdin.close()
            process.wait(timeout=10)
    for name, digest in hashes.items():
        if hashlib.sha256((ROOT / "Python WMS/data" / name).read_bytes()).hexdigest() != digest:
            raise ValueError(f"Original source changed: {name}")
    print(f"Saved results to {args.output}")


if __name__ == "__main__":
    main()
