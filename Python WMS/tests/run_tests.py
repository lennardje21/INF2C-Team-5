"""Runs the endpoint tests against v1 or v2 and writes every result to JSON.

    python3 "Python WMS/tests/run_tests.py" --target v1 --output v1-tests.json [pattern]
    python3 "Python WMS/tests/run_tests.py" --target v2 --output v2-tests.json [pattern]

v2 needs Docker and the .NET SDK: the run starts a new MySQL container and the C# API.

Per test the file holds the outcome, every check, the test duration without the data
reset, and every HTTP request with its status and duration. A test passes only when
all its cases (subTests) pass, as an xUnit theory passes only when all its rows pass.
"""

import argparse
import json
import os
import platform
import sys
import time
import unittest
import warnings
from datetime import datetime, timezone

TESTS_DIR = os.path.dirname(os.path.abspath(__file__))


class RecordingResult(unittest.TextTestResult):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.records = {}
        self._started = {}

    def _record(self, test):
        return self.records.setdefault(test.id(), {
            "class": test.__class__.__name__,
            "method": test._testMethodName.removeprefix("test_"),
            "outcome": "Passed",
            "errors": [],
        })

    def _error(self, test, err, case=None):
        record = self._record(test)
        record["outcome"] = "Failed"
        # Failed checks are already in "checks"; keep the exceptions that stopped a test.
        if not issubclass(err[0], test.failureException):
            message = self._exc_info_to_string(err, test).strip().splitlines()[-1]
            record["errors"].append(f"{case}: {message}" if case else message)

    def startTest(self, test):
        self._started[test.id()] = time.perf_counter()
        self._record(test)
        super().startTest(test)

    def stopTest(self, test):
        record = self._record(test)
        elapsed = time.perf_counter() - self._started[test.id()]
        checks = getattr(test, "checks", [])
        requests = getattr(test, "requests", [])
        record.update({
            "duration_s": elapsed - getattr(test, "setup_seconds", 0.0),
            "setup_s": getattr(test, "setup_seconds", 0.0),
            "http_ms": sum(r["ms"] for r in requests),
            "checks_passed": sum(c["passed"] for c in checks),
            "checks_total": len(checks),
            "failed_checks": [c for c in checks if not c["passed"]],
            "requests": requests,
        })
        super().stopTest(test)

    def addFailure(self, test, err):
        self._error(test, err)
        super().addFailure(test, err)

    def addError(self, test, err):
        self._error(test, err)
        super().addError(test, err)

    def addSubTest(self, test, subtest, err):
        if err is not None:
            self._error(test, err, subtest._subDescription())
        super().addSubTest(test, subtest, err)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--target", choices=["v1", "v2"], required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("pattern", nargs="?", default="test_*.py")
    args = parser.parse_args()

    os.environ["CARGOHUB_TARGET"] = args.target
    # datetime.utcnow() in the v1 models is deprecated since Python 3.12.
    warnings.simplefilter("ignore", DeprecationWarning)
    sys.path.insert(0, TESTS_DIR)
    suite = unittest.defaultTestLoader.discover(TESTS_DIR, pattern=args.pattern)

    import support
    print(f"Starting {args.target}...", flush=True)
    started_at = datetime.now(timezone.utc)
    start = time.perf_counter()
    support.get_api()
    startup = time.perf_counter() - start

    result = unittest.TextTestRunner(resultclass=RecordingResult, verbosity=2).run(suite)

    tests = sorted(result.records.values(), key=lambda r: (r["class"], r["method"]))
    output = {
        "target": args.target,
        "started_at": started_at.isoformat(),
        "python": platform.python_version(),
        "platform": platform.platform(),
        "startup_s": startup,
        "total_duration_s": time.perf_counter() - start,
        "tests_passed": sum(t["outcome"] == "Passed" for t in tests),
        "tests_total": len(tests),
        "checks_passed": sum(t["checks_passed"] for t in tests),
        "checks_total": sum(t["checks_total"] for t in tests),
        "tests": tests,
    }
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    with open(args.output, "w") as f:
        json.dump(output, f, indent=2)
    print(f"{args.target}: {output['tests_passed']}/{output['tests_total']} tests and "
          f"{output['checks_passed']}/{output['checks_total']} checks passed; results in {args.output}")


if __name__ == "__main__":
    main()
