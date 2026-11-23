"""Read-only field comparison of the project MySQL database against JSON sources.

Accounts are not compared: they do not come from the JSON sources, and setting an API key
changes them.
"""

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
    count = 0
    for table in TABLES:
        source = json.loads((ROOT / "Python WMS/data" / f"{table}.json").read_text(),
                            parse_float=Decimal)
        actual = read_rows(table, list(source[0]))
        expected = [{key: normalize(key, value) for key, value in row.items()} for row in source]
        key = lambda row: (row["item_id"], row["location_id"]) if table == "inventory" else row["id"]
        if sorted(actual, key=key) != sorted(expected, key=key):
            raise ValueError(f"MySQL data differs from source: {table}")
        count += len(source)
        print(f"{table}: {len(source)} records verified")
    print(f"Verified all fields of {count} source records in live MySQL.")


if __name__ == "__main__":
    main()
