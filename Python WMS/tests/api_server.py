"""Runs the original v1 API on a temporary copy of the JSON data.

The v1 counterpart of C# WMS/tests/CargoHub.Tests/Fixtures/ApiFixture.cs: the
handler and models are used unchanged, only the data folder is a disposable copy,
so the tests can write without touching Python WMS/data.
"""

import json
import os
import shutil
import socketserver
import sys
import tempfile
import threading

API_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "api"))
SOURCE_DATA_DIR = os.path.abspath(os.path.join(API_DIR, "..", "data"))

# main.py and the models import `providers` and `models` from the api folder.
sys.path.insert(0, API_DIR)

from main import ApiRequestHandler  # noqa: E402
from providers import auth_provider, data_provider  # noqa: E402

METHODS = ("get", "post", "put", "delete")


class QuietRequestHandler(ApiRequestHandler):
    # BaseHTTPRequestHandler logs every request to stderr, which floods test output.
    def log_message(self, format, *args):
        pass


def reset_data(data_dir, api_key):
    """(Re)writes the copy from the original data, plus a user for `api_key` with
    every permission, like the user the C# fixture adds to its test database."""
    shutil.copytree(SOURCE_DATA_DIR, data_dir, dirs_exist_ok=True)

    users_path = os.path.join(data_dir, "user.json")
    with open(users_path) as f:
        users = json.load(f)
    resources = sorted({r for user in users for r in user["endpoint_access"]})
    users.append({
        "api_key": api_key,
        "app": "integration-tests",
        "endpoint_access": {r: {m: True for m in METHODS} for r in resources},
    })
    with open(users_path, "w") as f:
        json.dump(users, f)


def start(api_key):
    """Serves the API on a free localhost port in a daemon thread.
    Returns (server, base_url, data_dir)."""
    data_dir = os.path.join(tempfile.mkdtemp(prefix="cargohub-v1-"), "data")
    reset_data(data_dir, api_key)
    data_provider.ROOT_PATH = data_dir + os.sep
    auth_provider.ROOT_PATH = data_dir + os.sep

    # The same single-threaded TCPServer as main.py.
    server = socketserver.TCPServer(("127.0.0.1", 0), QuietRequestHandler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    host, port = server.server_address
    return server, f"http://{host}:{port}", data_dir


def stop(server, data_dir):
    server.shutdown()
    server.server_close()
    shutil.rmtree(os.path.dirname(data_dir), ignore_errors=True)
