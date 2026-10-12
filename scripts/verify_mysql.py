"""Read-only field comparison of the project MySQL database against JSON sources."""

from decimal import Decimal
import json
import subprocess

from export_mysql import ROOT, TABLES, normalize


def query(sql):
    result = subprocess.run(
        ["docker", "compose", "exec", "-T", "mysql", "sh", "-c",
         'MYSQL_PWD="$MYSQL_PASSWORD" exec mysql --default-character-set=utf8mb4 --batch --raw --skip-column-names '
         '-u"$MYSQL_USER" "$MYSQL_DATABASE"'],
        cwd=ROOT / "C# WMS", input=sql, text=True, capture_output=True, check=True,
    )
    return [json.loads(line, parse_float=Decimal) for line in result.stdout.splitlines()]


def read_rows(table, columns):
    fields = []
    for column in columns:
        value = f"`{column}`"
        if column.endswith("_at") or column.endswith("_date"):
            value = f"DATE_FORMAT({value}, '%Y-%m-%d %H:%i:%s.%f')"
        fields.extend((f"'{column}'", value))
    return query(f"SELECT JSON_OBJECT({', '.join(fields)}) FROM `{table}`;")


def main():
    endpoints = read_rows("user_endpoint", ("api_key", "resource"))
    permissions = read_rows("user_permission", ("api_key", "resource", "method", "allowed"))
    count = 0
    for table in TABLES:
        source = json.loads((ROOT / "Python WMS/data" / f"{table}.json").read_text(),
                            parse_float=Decimal)
        columns = [column for column in source[0] if column != "endpoint_access"]
        actual = read_rows(table, columns)
        if table == "user":
            for user in actual:
                user["endpoint_access"] = {
                    row["resource"]: {} for row in endpoints if row["api_key"] == user["api_key"]
                }
                for row in permissions:
                    if row["api_key"] == user["api_key"]:
                        user["endpoint_access"][row["resource"]][row["method"]] = bool(row["allowed"])
        expected = [{key: normalize(key, value) for key, value in row.items()} for row in source]
        key = lambda row: (row["item_id"], row["location_id"]) if table == "inventory" else (
            row["api_key"] if table == "user" else row["id"]
        )
        if sorted(actual, key=key) != sorted(expected, key=key):
            raise ValueError(f"MySQL data differs from source: {table}")
        count += len(source)
        print(f"{table}: {len(source)} records verified")
    if len(endpoints) != sum(len(u["endpoint_access"]) for u in source):
        raise ValueError("Unexpected endpoint records")
    if len(permissions) != sum(len(p) for u in source for p in u["endpoint_access"].values()):
        raise ValueError("Unexpected permission records")
    print(f"Verified all fields of {count} source records in live MySQL.")


if __name__ == "__main__":
    main()
