"""The two systems the endpoint tests can run against.

Both run as their own server process and receive the same HTTP requests from the
same client; only the form of the data differs, and a target converts it:

- v1: the original Python API (main.py's ApiRequestHandler and TCPServer, unchanged)
  on a temporary copy of Python WMS/data;
- v2: the C# API, built in Release, against a new MySQL container with the project
  schema and seed, like the C# ApiFixture.

Each target also gives the tests direct access to the stored data (JSON files or
MySQL), as the C# tests query MySQL directly.
"""

import http.client
import json
import os
import re
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import uuid

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
V1_SERVER = os.path.join(os.path.dirname(os.path.abspath(__file__)), "api_server.py")
V1_DATA_DIR = os.path.join(ROOT, "Python WMS", "data")
API_PROJECT = os.path.join(ROOT, "C# WMS", "src", "CargoHub.Api")
SQL_DIR = os.path.join(API_PROJECT, "Data", "MySql")

METHODS = ("get", "post", "put", "delete")


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def wait_for(check, what, timeout):
    deadline = time.time() + timeout
    while time.time() < deadline:
        try:
            if check():
                return
        except Exception:
            pass
        time.sleep(0.5)
    raise RuntimeError(f"{what} did not become ready within {timeout} s")


def responds(port, api_key):
    connection = http.client.HTTPConnection("127.0.0.1", port, timeout=5)
    try:
        connection.request("GET", "/api/v1/warehouses/1", headers={"API_KEY": api_key})
        return connection.getresponse().status == 200
    finally:
        connection.close()


def snake_to_camel(name):
    head, *rest = name.split("_")
    return head + "".join(part.capitalize() for part in rest)


def camel_to_snake(name):
    return re.sub(r"(?<!^)(?=[A-Z])", "_", name).lower()


def convert_keys(value, convert):
    if isinstance(value, dict):
        return {convert(k): convert_keys(v, convert) for k, v in value.items()}
    if isinstance(value, list):
        return [convert_keys(v, convert) for v in value]
    return value


def matches(row, where):
    return all(row.get(k) == v for k, v in where.items())


class V1Target:
    name = "v1"
    # v1 stores the id the client sends and replaces whole records on PUT.
    client_sets_ids = True

    def start(self):
        self.api_key = uuid.uuid4().hex
        self.data_dir = os.path.join(tempfile.mkdtemp(prefix="cargohub-v1-"), "data")
        self.reset()
        self.port = free_port()
        # The original handler logs every request to stderr; that stays in.
        self.process = subprocess.Popen(
            [sys.executable, "-W", "ignore", V1_SERVER, "--port", str(self.port), "--data-dir", self.data_dir],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        wait_for(lambda: responds(self.port, self.api_key), "v1", 30)

    def stop(self):
        self.process.terminate()
        self.process.wait(timeout=10)
        shutil.rmtree(os.path.dirname(self.data_dir), ignore_errors=True)

    def reset(self):
        """Restores the original data, plus a user for the tests with every permission,
        like the user the C# fixture adds. v1 lacks the checks that keep the tests from
        changing shared data, so every test starts from the original data."""
        shutil.copytree(V1_DATA_DIR, self.data_dir, dirs_exist_ok=True)
        users = self._read("user")
        resources = sorted({r for user in users for r in user["endpoint_access"]})
        users.append({
            "api_key": self.api_key,
            "app": "integration-tests",
            "endpoint_access": {r: {m: True for m in METHODS} for r in resources},
        })
        self._write("user", users)

    # Data

    def _path(self, table):
        return os.path.join(self.data_dir, table + ".json")

    def _read(self, table):
        with open(self._path(table)) as f:
            return json.load(f)

    def _write(self, table, rows):
        with open(self._path(table), "w") as f:
            json.dump(rows, f)

    def rows(self, table, limit=None, **where):
        result = [r for r in self._read(table) if matches(r, where)]
        return result[:limit] if limit else result

    def count(self, table, **where):
        return len(self.rows(table, **where))

    def next_id(self, table):
        return max((r["id"] for r in self._read(table)), default=0) + 1

    def insert(self, table, row):
        rows = self._read(table)
        rows.append({"id": self.next_id(table), **row,
                     "created_at": "2026-01-01T00:00:00Z", "updated_at": "2026-01-01T00:00:00Z"})
        self._write(table, rows)

    def delete(self, table, **where):
        self._write(table, [r for r in self._read(table) if not matches(r, where)])

    # Wire format: v1 uses the same snake_case names as the tests.

    def to_wire(self, body):
        return body

    def from_wire(self, value):
        return value


class V2Target:
    name = "v2"
    client_sets_ids = False

    def start(self):
        self.container = f"cargohub-endpoint-tests-{uuid.uuid4().hex[:8]}"
        self.password = uuid.uuid4().hex
        mysql_port = free_port()
        subprocess.run([
            "docker", "run", "-d", "--name", self.container, "-p", f"127.0.0.1:{mysql_port}:3306",
            "-e", f"MYSQL_ROOT_PASSWORD={self.password}", "-e", "MYSQL_DATABASE=cargohub",
            "--mount", f"type=bind,source={SQL_DIR}/01-schema.sql,target=/docker-entrypoint-initdb.d/01-schema.sql,readonly",
            "--mount", f"type=bind,source={SQL_DIR}/02-seed.sql,target=/docker-entrypoint-initdb.d/02-seed.sql,readonly",
            "mysql:8.4"], check=True, capture_output=True)
        self.process = None
        try:
            # The init scripts run on a server without TCP, so the seed is complete
            # once a TCP connection sees the shipments (as in ApiFixture).
            wait_for(lambda: self._scalar("SELECT COUNT(*) FROM `shipment`") == 6132, "MySQL seed", 600)

            self.api_key = uuid.uuid4().hex
            key = self._literal(self.api_key)
            self._sql(f"""
                INSERT INTO `user` (`api_key`, `app`) VALUES ({key}, 'integration-tests');
                INSERT INTO `user_endpoint` (`api_key`, `resource`)
                    SELECT DISTINCT {key}, `resource` FROM `user_endpoint`;
                INSERT INTO `user_permission` (`api_key`, `resource`, `method`, `allowed`)
                    SELECT DISTINCT {key}, `resource`, `method`, 1 FROM `user_permission`;""")

            subprocess.run(["dotnet", "build", API_PROJECT, "-c", "Release", "--nologo", "-v", "quiet"],
                           check=True, capture_output=True)
            self.port = free_port()
            env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Production",
                       ASPNETCORE_URLS=f"http://127.0.0.1:{self.port}",
                       ConnectionStrings__CargoHub=f"Server=127.0.0.1;Port={mysql_port};Database=cargohub;"
                                                   f"User ID=root;Password={self.password}")
            self.process = subprocess.Popen(
                ["dotnet", "CargoHub.Api.dll"], cwd=os.path.join(API_PROJECT, "bin", "Release", "net10.0"),
                env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            wait_for(lambda: responds(self.port, self.api_key), "v2", 60)
        except BaseException:
            self.stop()
            raise

    def stop(self):
        if self.process is not None:
            self.process.terminate()
            self.process.wait(timeout=10)
        subprocess.run(["docker", "rm", "-f", "-v", self.container], capture_output=True)

    def reset(self):
        # The C# tests share one database and are written not to depend on each
        # other; v2 passes them that way, so its data is not restored between tests.
        pass

    # Data

    def _sql(self, sql):
        result = subprocess.run(
            ["docker", "exec", "-i", "-e", f"MYSQL_PWD={self.password}", self.container,
             "mysql", "--protocol=TCP", "-h", "127.0.0.1", "-uroot", "--default-character-set=utf8mb4",
             "--batch", "--raw", "--skip-column-names", "cargohub"],
            input=sql, capture_output=True, text=True)
        if result.returncode != 0:
            raise RuntimeError(f"MySQL query failed: {result.stderr.strip()}")
        return result.stdout.splitlines()

    def _scalar(self, sql):
        return int(self._sql(sql)[0])

    @staticmethod
    def _literal(value):
        if isinstance(value, (int, float)):
            return str(value)
        return "'" + str(value).replace("\\", "\\\\").replace("'", "''") + "'"

    def _where(self, where):
        if not where:
            return ""
        return " WHERE " + " AND ".join(f"`{k}` = {self._literal(v)}" for k, v in where.items())

    def _columns(self, table):
        if not hasattr(self, "_column_cache"):
            self._column_cache = {}
        if table not in self._column_cache:
            self._column_cache[table] = self._sql(
                "SELECT `column_name` FROM `information_schema`.`columns` "
                f"WHERE `table_schema` = 'cargohub' AND `table_name` = {self._literal(table)} "
                "ORDER BY `ordinal_position`")
        return self._column_cache[table]

    def rows(self, table, limit=None, **where):
        fields = ", ".join(f"'{c}', `{c}`" for c in self._columns(table))
        sql = f"SELECT JSON_OBJECT({fields}) FROM `{table}`{self._where(where)}"
        if limit:
            sql += f" LIMIT {int(limit)}"
        return [json.loads(line) for line in self._sql(sql)]

    def count(self, table, **where):
        return self._scalar(f"SELECT COUNT(*) FROM `{table}`{self._where(where)}")

    def insert(self, table, row):
        columns = ", ".join(f"`{c}`" for c in row) + ", `created_at`, `updated_at`"
        values = ", ".join(self._literal(v) for v in row.values()) + ", UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)"
        self._sql(f"INSERT INTO `{table}` ({columns}) VALUES ({values})")

    def delete(self, table, **where):
        self._sql(f"DELETE FROM `{table}`{self._where(where)}")

    # Wire format: v2 uses camelCase names.

    def to_wire(self, body):
        return convert_keys(body, snake_to_camel)

    def from_wire(self, value):
        return convert_keys(value, camel_to_snake)


TARGETS = {"v1": V1Target, "v2": V2Target}
