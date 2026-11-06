"""Runs the original v1 API on another data folder.

    python3 api_server.py --port 3100 --data-dir /tmp/copy-of-data

The same server as main.py (ApiRequestHandler on a single-threaded TCPServer); only
the data folder differs, so tests can write to a copy instead of Python WMS/data.
The notification timer from main.py is left out: it only prints a message every 30 s.
"""

import argparse
import os
import signal
import socketserver
import sys

API_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "api"))

# main.py and the models import `providers` and `models` from the api folder.
sys.path.insert(0, API_DIR)

from main import ApiRequestHandler  # noqa: E402
from providers import auth_provider, data_provider  # noqa: E402


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--data-dir", required=True)
    args = parser.parse_args()

    data_provider.ROOT_PATH = os.path.abspath(args.data_dir) + os.sep
    auth_provider.ROOT_PATH = os.path.abspath(args.data_dir) + os.sep

    # Stop cleanly when the tests terminate the server.
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(0))
    with socketserver.TCPServer(("127.0.0.1", args.port), ApiRequestHandler) as httpd:
        httpd.serve_forever()


if __name__ == "__main__":
    main()
