from decimal import Decimal
import hashlib
import json
from pathlib import Path
import re
import shutil
import tempfile
import unittest

from scripts.export_mysql import ROOT, OUTPUT, TABLES, export, literal, normalize


class MySqlExportTests(unittest.TestCase):
    def test_every_seed_field_matches_json(self):
        seed = (OUTPUT / "02-seed.sql").read_text()
        actual = {table: [] for table in (*TABLES, "user_endpoint", "user_permission")}
        for table, column_text, values in re.findall(
            r"INSERT INTO `([^`]+)` \(([^\n]+)\) VALUES\n(.*?);", seed, re.S
        ):
            columns = re.findall(r"`([^`]+)`", column_text)
            for line in values.splitlines():
                tokens = line.rstrip(",")[1:-1].split(", ")
                decoded = []
                for token in tokens:
                    if token == "NULL":
                        decoded.append(None)
                    elif token.startswith("CONVERT("):
                        decoded.append(bytes.fromhex(re.search(r"X'([^']*)'", token)[1]).decode())
                    else:
                        decoded.append(Decimal(token))
                actual[table].append(dict(zip(columns, decoded, strict=True)))
        count = 0
        for table in TABLES:
            expected = json.loads((ROOT / "Python WMS/data" / f"{table}.json").read_text(),
                                  parse_float=Decimal)
            if table == "user":
                for user in actual[table]:
                    access = {r["resource"]: {} for r in actual["user_endpoint"]
                              if r["api_key"] == user["api_key"]}
                    for row in actual["user_permission"]:
                        if row["api_key"] == user["api_key"]:
                            access[row["resource"]][row["method"]] = bool(row["allowed"])
                    user["endpoint_access"] = access
            expected = [{k: normalize(k, v) for k, v in row.items()} for row in expected]
            self.assertEqual(actual[table], expected, table)
            count += len(expected)
        self.assertEqual(count, 80471)

    def test_manifest_and_deterministic_regeneration(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            shutil.copyfile(OUTPUT / "01-schema.sql", output / "01-schema.sql")
            report = export(output=output)
            self.assertEqual(report["source_records"], 80471)
            self.assertEqual((output / "02-seed.sql").read_bytes(), (OUTPUT / "02-seed.sql").read_bytes())
            self.assertEqual(report["seed_sha256"], hashlib.sha256((OUTPUT / "02-seed.sql").read_bytes()).hexdigest())

    def test_csharp_scalar_properties_match_schema(self):
        schema = (OUTPUT / "01-schema.sql").read_text()
        models = ROOT / "C# WMS/src/CargoHub.Api/Models"
        base = (models / "BaseModel.cs").read_text()
        for path in models.glob("*.cs"):
            if path.stem == "BaseModel":
                continue
            text = path.read_text()
            table = re.sub(r"([a-z])([A-Z])", r"\1_\2", path.stem).lower()
            body = re.search(rf"CREATE TABLE `{table}` \((.*?)\n\)", schema, re.S)[1]
            columns = set(re.findall(r"^    `([^`]+)` ", body, re.M))
            if ": BaseModel" in text:
                text += base
            properties = re.findall(r"public (?:int|decimal|DateTime|string\??) (\w+) \{", text)
            mapped = {"min_purchase_qty" if p == "MinPurchaseQuantity" else
                      re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", p).lower() for p in properties}
            self.assertEqual(columns, mapped, path.stem)

    def test_strings_are_sql_mode_independent(self):
        text = "O'Brien \\ line\n café 🚚"
        encoded = literal(text)
        self.assertEqual(bytes.fromhex(re.search(r"X'([^']*)'", encoded)[1]).decode(), text)

    def test_timestamps_normalize_offsets_to_utc(self):
        self.assertEqual(normalize("created_at", "2026-10-12T12:00:00+02:00"),
                         "2026-10-12 10:00:00.000000")
        with self.assertRaises(ValueError):
            normalize("created_at", "2026-10-12T12:00:00")

    def test_invalid_source_does_not_write_seed(self):
        with tempfile.TemporaryDirectory() as directory:
            directory = Path(directory)
            source = directory / "source"
            source.mkdir()
            for table in TABLES:
                shutil.copyfile(ROOT / "Python WMS/data" / f"{table}.json", source / f"{table}.json")
            path = source / "location.json"
            rows = json.loads(path.read_text())
            rows[0]["warehouse_id"] = -1
            path.write_text(json.dumps(rows))
            shutil.copyfile(OUTPUT / "01-schema.sql", directory / "01-schema.sql")
            with self.assertRaises(ValueError):
                export(source, directory)
            self.assertFalse((directory / "02-seed.sql").exists())


if __name__ == "__main__":
    unittest.main()
