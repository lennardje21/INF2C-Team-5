"""Shared helpers for the endpoint tests.

Every test class here mirrors a class in C# WMS/tests/CargoHub.Tests/Controllers with
the same test names. The same tests run against v1 or v2 (see targets.py), so both
systems get identical requests from the same client. Tests are written in v1's
snake_case; the target converts names for v2. Where the systems differ only in form,
the helpers adapt to the target:

- v1 does not choose ids, so a v1 client sends the next free id on POST (`with_id`);
- v1 PUT replaces the stored record with the body, so `put` also sends the id from the URL;
- v1 POST returns no body, so `create` reads the stored record back with a GET (for both);
- v1 answers a successful PUT or DELETE with 200 instead of 204; both count as success.

A failed check does not stop a test: every check is recorded, so a run also reports
how many checks pass. The test fails if any check failed.
"""

import atexit
import contextlib
import http.client
import json
import os
import re
import threading
import time
import unittest
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta, timezone
from urllib.parse import urlparse

from targets import TARGETS

# GET /orders and GET /shipments read the whole item file once per record in v1.
TIMEOUT_SECONDS = 600

_api = None
_api_lock = threading.Lock()


def get_api():
    """The target that every test class shares, like the C# "Api" collection."""
    global _api
    with _api_lock:
        if _api is None:
            _api = TARGETS[os.environ.get("CARGOHUB_TARGET", "v1")]()
            _api.start()
            atexit.register(_api.stop)
        return _api


class Response:
    def __init__(self, status, headers, body):
        self.status = status
        self.headers = headers
        self.body = body

    def location_path(self):
        location = self.headers.get("location")
        return urlparse(location).path if location else None


def parse_time(value):
    """Parses a timestamp such as 2026-11-06T10:00:00.1234567Z."""
    return datetime.fromisoformat(value) if value else None


def utc_now():
    return datetime.now(timezone.utc)


def run_concurrently(function, arguments):
    """Calls `function` once per argument at the same time, like Task.WhenAll."""
    arguments = list(arguments)
    with ThreadPoolExecutor(max_workers=len(arguments)) as pool:
        return list(pool.map(function, arguments))


class ApiTestCase(unittest.TestCase):
    url = ""
    file = ""

    @classmethod
    def setUpClass(cls):
        cls.api = get_api()

    def setUp(self):
        self.checks = []
        self.requests = []
        self._failed = []
        started = time.perf_counter()
        self.api.reset()
        self.setup_seconds = time.perf_counter() - started
        self.addCleanup(self._raise_failed_checks, 0)

    # Checks

    def _raise_failed_checks(self, start):
        failed = self._failed[start:]
        del self._failed[start:]
        if failed:
            raise self.failureException("; ".join(failed))

    @contextlib.contextmanager
    def subTest(self, msg=None, **params):
        context = super().subTest(msg, **params) if msg is not None else super().subTest(**params)
        with context:
            start = len(self._failed)
            yield
            self._raise_failed_checks(start)

    def _current_case(self):
        return self._subtest._subDescription() if self._subtest is not None else None

    # HTTP

    def request(self, method, path, body=None, api_key=..., raw_body=None):
        if api_key is ...:
            api_key = self.api.api_key
        headers = {}
        if api_key is not None:
            headers["API_KEY"] = api_key
        data = raw_body
        if body is not None:
            data = json.dumps(self.api.to_wire(body)).encode("utf-8")
            headers["Content-Type"] = "application/json"
        connection = http.client.HTTPConnection("127.0.0.1", self.api.port, timeout=TIMEOUT_SECONDS)
        started = time.perf_counter()
        try:
            connection.request(method, path, body=data, headers=headers)
            response = connection.getresponse()
            result = Response(response.status, {k.lower(): v for k, v in response.getheaders()}, response.read())
        except (OSError, http.client.HTTPException) as e:
            self.requests.append({"method": method, "path": path, "status": type(e).__name__,
                                  "ms": (time.perf_counter() - started) * 1000})
            raise
        finally:
            connection.close()
        self.requests.append({"method": method, "path": path, "status": result.status,
                              "ms": (time.perf_counter() - started) * 1000})
        return result

    def get(self, path, **kwargs):
        return self.request("GET", path, **kwargs)

    def post(self, path, body, **kwargs):
        return self.request("POST", path, body, **kwargs)

    def put(self, path, body, **kwargs):
        match = re.fullmatch(rf"{re.escape(self.url)}/(\d+)", path)
        if self.api.client_sets_ids and match and isinstance(body, dict) and "id" not in body:
            body = {**body, "id": int(match.group(1))}
        return self.request("PUT", path, body, **kwargs)

    def delete(self, path, **kwargs):
        return self.request("DELETE", path, **kwargs)

    def json(self, response, keep_numbers=False):
        """The response body in snake_case. With `keep_numbers`, numbers stay the text
        they were sent as, e.g. "21.34" or "5.0"."""
        if not response.body:
            return None
        if keep_numbers:
            value = json.loads(response.body, parse_float=str, parse_int=str)
        else:
            value = json.loads(response.body)
        return self.api.from_wire(value)

    def get_json(self, path):
        """Like HttpClient.GetFromJsonAsync: checks that the status is 2xx."""
        response = self.get(path)
        self.assertTrue(200 <= response.status < 300, f"GET {path} returned {response.status}")
        return self.json(response)

    # Data, as the C# tests query MySQL directly

    def rows(self, table, limit=None, **where):
        return self.api.rows(table, limit=limit, **where)

    def count(self, table, **where):
        return self.api.count(table, **where)

    def insert(self, table, row):
        self.api.insert(table, row)

    def delete_rows(self, table, **where):
        self.api.delete(table, **where)

    def inventory_field(self, item_id, field):
        """`field` of the inventory row with the most stock for an item, the row that
        orders and shipments update (ORDER BY quantity_on_hand DESC, location_id)."""
        rows = self.rows("inventory", item_id=item_id)
        return min(rows, key=lambda r: (-r["quantity_on_hand"], r["location_id"]))[field]

    # Creating records

    def with_id(self, body, table=None):
        """Adds the next free id where the client must choose it (v1)."""
        if self.api.client_sets_ids:
            body["id"] = self.api.next_id(table or self.file)
        return body

    def created_id(self, body, response):
        if self.api.client_sets_ids:
            return body["id"]
        created = self.json(response) if response.status == 201 else None
        return created["id"] if created else None

    def created_body(self, response):
        """The record in a 201 response, as the C# tests read it."""
        self.assertEqual(201, response.status)
        created = self.json(response)
        self.assertIsNotNone(created, "POST does not return the created record")
        return created

    def create(self, body, url=None):
        """Like CreateAsync in the C# tests: creates a record and returns it as stored."""
        url = url or self.url
        body = self.with_id(dict(body))
        response = self.post(url, body)
        self.assertEqual(201, response.status)
        id = self.created_id(body, response)
        stored = self.get_json(f"{url}/{id}")
        self.assertIsNotNone(stored, f"{url}/{id} was not stored")
        return stored

    # Extra assertions

    def assert_no_content(self, response):
        """Where the C# tests expect 204 No Content."""
        self.assertIn(response.status, (200, 204))

    def assert_same_time(self, expected, actual):
        """Equal within 1 µs, as the C# tests allow 10 ticks: the API returns 7 decimals
        and MySQL stores 6."""
        same = bool(expected and actual) and abs(parse_time(expected) - parse_time(actual)) <= timedelta(microseconds=1)
        self.assertTrue(same, f"{expected} != {actual}")


def _recorded(name):
    """Turns an assert method into a check that is recorded instead of stopping the test."""
    hard = getattr(unittest.TestCase, name)

    def check(self, *args, **kwargs):
        case = self._current_case()
        try:
            hard(self, *args, **kwargs)
        except self.failureException as e:
            message = str(e).splitlines()[0][:300]
            self.checks.append({"case": case, "passed": False, "message": message})
            self._failed.append(f"{case}: {message}" if case else message)
        else:
            self.checks.append({"case": case, "passed": True})

    check.__name__ = name
    return check


for _name in ("assertEqual", "assertNotEqual", "assertTrue", "assertIsNone", "assertIsNotNone",
              "assertIn", "assertGreater", "assertGreaterEqual", "assertRegex"):
    setattr(ApiTestCase, _name, _recorded(_name))
