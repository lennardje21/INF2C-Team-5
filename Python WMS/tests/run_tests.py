"""Runs the v1 endpoint tests and writes the outcome and duration of every test to JSON.

    python3 "Python WMS/tests/run_tests.py" --output v1-tests.json [pattern]

A test passes only when all its cases (subTests) pass, as an xUnit theory passes only
when all its InlineData rows pass, so the results can be compared with the C# TRX.
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
            "cases": [],
            "messages": [],
        })

    def _fail(self, test, err, case=None):
        record = self._record(test)
        record["outcome"] = "Failed"
        message = self._exc_info_to_string(err, test).strip().splitlines()[-1]
        record["messages"].append(f"{case}: {message}" if case else message)

    def startTest(self, test):
        self._started[test.id()] = time.perf_counter()
        self._record(test)
        super().startTest(test)

    def stopTest(self, test):
        self._record(test)["duration_s"] = time.perf_counter() - self._started[test.id()]
        super().stopTest(test)

    def addFailure(self, test, err):
        self._fail(test, err)
        super().addFailure(test, err)

    def addError(self, test, err):
        self._fail(test, err)
        super().addError(test, err)

    def addSubTest(self, test, subtest, err):
        case = subtest._subDescription()
        self._record(test)["cases"].append({"case": case, "outcome": "Failed" if err else "Passed"})
        if err is not None:
            self._fail(test, err, case)
        super().addSubTest(test, subtest, err)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--output", required=True)
    parser.add_argument("pattern", nargs="?", default="test_*.py")
    args = parser.parse_args()

    # datetime.utcnow() in the v1 models is deprecated since Python 3.12.
    warnings.simplefilter("ignore", DeprecationWarning)
    sys.path.insert(0, TESTS_DIR)
    suite = unittest.defaultTestLoader.discover(TESTS_DIR, pattern=args.pattern)

    started = datetime.now(timezone.utc)
    result = unittest.TextTestRunner(resultclass=RecordingResult, verbosity=2).run(suite)

    tests = sorted(result.records.values(), key=lambda r: (r["class"], r["method"]))
    output = {
        "system": "v1 (Python)",
        "started_at": started.isoformat(),
        "python": platform.python_version(),
        "platform": platform.platform(),
        "total_duration_s": (datetime.now(timezone.utc) - started).total_seconds(),
        "passed": sum(t["outcome"] == "Passed" for t in tests),
        "total": len(tests),
        "tests": tests,
    }
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    with open(args.output, "w") as f:
        json.dump(output, f, indent=2)
    print(f"{output['passed']}/{output['total']} tests passed; results in {args.output}")


if __name__ == "__main__":
    main()
