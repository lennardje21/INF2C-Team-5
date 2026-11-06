"""Shared helpers for the v1 endpoint tests.

Every test class here mirrors a class in C# WMS/tests/CargoHub.Tests/Controllers with
the same test names, so both suites can be compared test by test. The tests check the
behaviour the C# tests check; only the form is adapted to v1:

- field names are snake_case instead of camelCase;
- v1 does not choose ids, so `create` sends the next free id, as a v1 client must;
- v1 PUT replaces the stored record with the body, so `put` also sends the id from the URL;
- v1 POST returns no body, so `create` reads the stored record back with a GET;
- v1 answers a successful PUT or DELETE with 200 instead of 204; both count as success.

Tests that v1 fails show behaviour that v1 does not have, such as validation or 404s.
"""

import atexit
import http.client
import json
import os
import re
import threading
import unittest
import uuid
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from urllib.parse import urlparse

import api_server

# GET /orders and GET /shipments read the whole item file once per record in v1.
TIMEOUT_SECONDS = 600

_api = None
_api_lock = threading.Lock()


class Api:
    """The v1 server that every test class shares, like the C# "Api" collection."""

    def __init__(self):
        self.api_key = uuid.uuid4().hex
        self.server, self.base_url, self.data_dir = api_server.start(self.api_key)
        parsed = urlparse(self.base_url)
        self.host, self.port = parsed.hostname, parsed.port

    def close(self):
        api_server.stop(self.server, self.data_dir)


def get_api():
    global _api
    with _api_lock:
        if _api is None:
            _api = Api()
            atexit.register(_api.close)
        return _api


class Response:
    def __init__(self, status, headers, body):
        self.status = status
        self.headers = headers
        self.body = body

    def json(self):
        return json.loads(self.body) if self.body else None

    def location_path(self):
        location = self.headers.get("location")
        return urlparse(location).path if location else None


def parse_time(value):
    """Parses a v1 timestamp such as 2026-11-06T10:00:00.123456Z."""
    return datetime.fromisoformat(value) if value else None


def utc_now():
    return datetime.now(timezone.utc)


def run_concurrently(function, arguments):
    """Calls `function` once per argument at the same time, like Task.WhenAll."""
    arguments = list(arguments)
    with ThreadPoolExecutor(max_workers=len(arguments)) as pool:
        return list(pool.map(function, arguments))


def raw_numbers(body):
    """Parses JSON but keeps every number as its literal text, e.g. "21.34" or "5.0"."""
    return json.loads(body, parse_float=str, parse_int=str)


class ApiTestCase(unittest.TestCase):
    url = ""

    @classmethod
    def setUpClass(cls):
        cls.api = get_api()

    def setUp(self):
        # v1 lacks the checks that keep the C# tests from changing shared data (it
        # deletes referenced records and stores duplicate ids), so every test
        # starts from the original data to keep one failure from causing others.
        api_server.reset_data(self.api.data_dir, self.api.api_key)

    # HTTP

    def request(self, method, path, body=None, api_key=..., raw_body=None):
        if api_key is ...:
            api_key = self.api.api_key
        headers = {}
        if api_key is not None:
            headers["API_KEY"] = api_key
        data = raw_body
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        connection = http.client.HTTPConnection(self.api.host, self.api.port, timeout=TIMEOUT_SECONDS)
        try:
            connection.request(method, path, body=data, headers=headers)
            response = connection.getresponse()
            return Response(response.status, {k.lower(): v for k, v in response.getheaders()}, response.read())
        finally:
            connection.close()

    def get(self, path, **kwargs):
        return self.request("GET", path, **kwargs)

    def post(self, path, body, **kwargs):
        return self.request("POST", path, body, **kwargs)

    def put(self, path, body, **kwargs):
        match = re.fullmatch(rf"{re.escape(self.url)}/(\d+)", path)
        if match and isinstance(body, dict) and "id" not in body:
            body = {**body, "id": int(match.group(1))}
        return self.request("PUT", path, body, **kwargs)

    def delete(self, path, **kwargs):
        return self.request("DELETE", path, **kwargs)

    def get_json(self, path):
        """Like HttpClient.GetFromJsonAsync: fails unless the status is 2xx."""
        response = self.get(path)
        self.assertTrue(200 <= response.status < 300, f"GET {path} returned {response.status}")
        return response.json()

    # Data

    def records(self, name):
        """Reads a data file directly, as the C# tests query MySQL directly."""
        with open(os.path.join(self.api.data_dir, name + ".json")) as f:
            return json.load(f)

    def write_records(self, name, rows):
        """Replaces a data file, as the C# tests change MySQL directly."""
        with open(os.path.join(self.api.data_dir, name + ".json"), "w") as f:
            json.dump(rows, f)

    def next_id(self, name):
        return max((r["id"] for r in self.records(name)), default=0) + 1

    def inventory_field(self, item_id, field):
        """`field` of the inventory row with the most stock for an item, the row that
        orders and shipments update (ORDER BY quantity_on_hand DESC, location_id)."""
        rows = [r for r in self.records("inventory") if r["item_id"] == item_id]
        return min(rows, key=lambda r: (-r["quantity_on_hand"], r["location_id"]))[field]

    # Assertions

    def assert_no_content(self, response):
        """Where the C# tests expect 204 No Content."""
        self.assertIn(response.status, (200, 204))

    def created_body(self, response):
        """The record in a 201 response, as the C# tests read it."""
        self.assertEqual(201, response.status)
        created = response.json()
        self.assertIsNotNone(created, "POST does not return the created record")
        return created

    def create(self, body, file, url=None):
        """Like CreateAsync in the C# tests: creates a record and returns it as stored."""
        url = url or self.url
        body = dict(body)
        body.setdefault("id", self.next_id(file))
        response = self.post(url, body)
        self.assertEqual(201, response.status)
        stored = self.get_json(f"{url}/{body['id']}")
        self.assertIsNotNone(stored, f"{url}/{body['id']} was not stored")
        return stored
