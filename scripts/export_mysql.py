"""Export all legacy JSON data as a validated MySQL 8.4 seed file."""

import argparse
from datetime import datetime, timezone
from decimal import Decimal
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "C# WMS/src/CargoHub.Api/Data/MySql"
TABLES = (
    "warehouse", "client", "supplier", "item_line", "item_group", "item_type",
    "location", "item", "inventory", "order", "order_item", "shipment",
    "shipment_item", "transfer", "transfer_item", "user",
)


def literal(value):
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return str(int(value))
    if isinstance(value, (int, Decimal)):
        return str(value)
    if isinstance(value, str):
        # Hex literals preserve quotes, backslashes, Unicode and SQL-mode independence.
        return "CONVERT(X'" + value.encode("utf-8").hex() + "' USING utf8mb4)"
    raise ValueError(f"Unsupported value type: {type(value).__name__}")


def normalize(column, value):
    if value is not None and (column.endswith("_at") or column.endswith("_date")):
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            raise ValueError(f"Timestamp without timezone: {column}")
        return parsed.astimezone(timezone.utc).strftime("%Y-%m-%d %H:%M:%S.%f")
    return value


def export(source=ROOT / "Python WMS/data", output=OUTPUT):
    source, output = Path(source), Path(output)
    schema = (output / "01-schema.sql").read_text()
    columns = {
        table: re.findall(r"^    `([^`]+)` ", body, re.M)
        for table, body in re.findall(r"CREATE TABLE `([^`]+)` \((.*?)\n\)", schema, re.S)
    }
    rows, hashes = {}, {}
    for table in TABLES:
        raw = (source / f"{table}.json").read_bytes()
        hashes[table] = hashlib.sha256(raw).hexdigest()
        records = json.loads(raw, parse_float=Decimal)
        if not isinstance(records, list):
            raise ValueError(f"Expected list: {table}")
        for record in records:
            expected = set(columns[table]) | ({"endpoint_access"} if table == "user" else set())
            if set(record) != expected:
                raise ValueError(f"Unexpected or missing fields: {table}")
        rows[table] = records
    rows["user_endpoint"], rows["user_permission"] = [], []
    for user in rows["user"]:
        for resource, permissions in user["endpoint_access"].items():
            rows["user_endpoint"].append({"api_key": user["api_key"], "resource": resource})
            for method, allowed in permissions.items():
                if not isinstance(allowed, bool):
                    raise ValueError("Permissions must be booleans")
                rows["user_permission"].append(dict(
                    api_key=user["api_key"], resource=resource, method=method, allowed=allowed
                ))
    ids = {table: {record["id"] for record in records}
           for table, records in rows.items() if table not in
           ("inventory", "user", "user_endpoint", "user_permission")}
    for table, records in rows.items():
        keys = ("item_id", "location_id") if table == "inventory" else (
            ("api_key", "resource", "method") if table == "user_permission" else
            ("api_key", "resource") if table == "user_endpoint" else
            ("api_key",) if table == "user" else ("id",)
        )
        if len({tuple(r[k] for k in keys) for r in records}) != len(records):
            raise ValueError(f"Duplicate key: {table}")
        for record in records:
            for column in columns[table]:
                value = record[column]
                if value is None and re.search(rf"`{column}` [^\n,]*NOT NULL", schema):
                    raise ValueError(f"Null required value: {table}.{column}")
                if column.endswith("_id"):
                    target = {"from_location_id": "location", "to_location_id": "location",
                              "ship_to_client_id": "client", "bill_to_client_id": "client"}.get(column, column[:-3])
                    if target in ids and value not in ids[target]:
                        raise ValueError(f"Invalid reference: {table}.{column}")
                if isinstance(value, int) and not isinstance(value, bool) and not -(2**31) <= value < 2**31:
                    raise ValueError(f"Value outside C# int range: {table}.{column}")
                if column in ("unit_price", "unit_weight"):
                    scale = 4 if column == "unit_price" else 6
                    if not isinstance(value, (int, Decimal)) or not Decimal(value).is_finite():
                        raise ValueError(f"Invalid decimal: {table}.{column}")
                    if Decimal(value).quantize(Decimal(10) ** -scale) != value or abs(value) >= 10 ** (18 - scale):
                        raise ValueError(f"Decimal cannot be stored losslessly: {table}.{column}")
                normalize(column, value)
    sql = ["-- Generated by scripts/export_mysql.py. Import into empty tables only.",
           "SET NAMES utf8mb4;", "SET time_zone = '+00:00';", "START TRANSACTION;"]
    for table, records in rows.items():
        fields = columns[table]
        for offset in range(0, len(records), 250):
            values = ["(" + ", ".join(literal(normalize(k, r[k])) for k in fields) + ")"
                      for r in records[offset:offset + 250]]
            sql.append(f"INSERT INTO `{table}` (" + ", ".join(f"`{k}`" for k in fields)
                       + ") VALUES\n" + ",\n".join(values) + ";")
    sql.append("COMMIT;")
    seed = "\n".join(sql) + "\n"
    report = {"source_records": sum(len(rows[t]) for t in TABLES),
              "table_counts": {t: len(r) for t, r in rows.items()},
              "source_sha256": hashes, "seed_sha256": hashlib.sha256(seed.encode()).hexdigest()}
    (output / "02-seed.sql").write_text(seed, encoding="utf-8")
    (output / "manifest.json").write_text(json.dumps(report, indent=2) + "\n")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / "Python WMS/data")
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()
    report = export(args.source, args.output)
    print(f"Exported {report['source_records']} source records to {args.output}")
