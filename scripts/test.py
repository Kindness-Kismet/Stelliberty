#!/usr/bin/env python3

import os
import sys

sys.dont_write_bytecode = True
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")

import argparse
import re
import subprocess
import time
from contextlib import contextmanager
from pathlib import Path

from build_support.console import fail, header, print_summary, timing
from build_support.help import MultilineHelpFormatter

ROOT = Path(__file__).resolve().parents[1]
PRE_BUILD_TESTS: list[tuple[str, Path, str]] = [
    ("monitoring-rules", ROOT / "tests" / "Stelliberty.Monitoring.Tests" / "Stelliberty.Monitoring.Tests.csproj", "Monitoring rules: connection and log parsing and reduction, plus rule parsing and classification"),
    ("settings-rules", ROOT / "tests" / "Stelliberty.Settings.Tests" / "Stelliberty.Settings.Tests.csproj", "Settings rules: TUN permission correction, system proxy requests, and update release selection"),
    ("subscription-rules", ROOT / "tests" / "Stelliberty.Subscription.Tests" / "Stelliberty.Subscription.Tests.csproj", "Subscription rules: update planning, provider parsing, and content normalization"),
    ("proxy-selection-rules", ROOT / "tests" / "Stelliberty.ProxySelection.Tests" / "Stelliberty.ProxySelection.Tests.csproj", "Proxy selection rules: group semantics, normalization, selection, and visibility"),
    ("runtime-config-rules", ROOT / "tests" / "Stelliberty.RuntimeConfig.Tests" / "Stelliberty.RuntimeConfig.Tests.csproj", "Runtime config rules: settings normalization and deterministic YAML generation"),
    ("chain-proxy-rules", ROOT / "tests" / "Stelliberty.ChainProxy.Tests" / "Stelliberty.ChainProxy.Tests.csproj", "Chain proxy rules: analysis and deterministic runtime config transformation"),
]
CSHARP_TEST_ATTRIBUTE_PREFIXES = ("[Fact", "[Theory")
CSHARP_TEST_DISPLAY_NAME_PATTERN = re.compile(r'DisplayName\s*=\s*"([^"]+)"')
CSHARP_TEST_DISPLAY_NAME_MIN_LENGTH = 20

class TestStep:
    def __init__(self) -> None:
        self.passed = False

@contextmanager
def timed_test_step(label: str):
    print(header(label), flush=True)
    started_at = time.perf_counter()
    step = TestStep()
    try:
        yield step
    finally:
        elapsed = time.perf_counter() - started_at
        mark = "✅" if step.passed else "❌"
        print(f"{timing(elapsed)} {mark}", flush=True)
        print()

def main() -> None:
    tests = available_tests()
    parser = argparse.ArgumentParser(
        description="Pre-build test runner for C# business logic",
        formatter_class=MultilineHelpFormatter,
        epilog=format_available_tests(tests),
    )
    parser.add_argument(
        "test",
        nargs="?",
        metavar="TEST",
        help="Pre-build test name.\nAvailable values are listed below.",
    )
    parser.add_argument("--all", action="store_true", help="Run every pre-build test")
    args = parser.parse_args()

    if not args.test and not args.all:
        parser.print_help()
        sys.exit(0)

    if args.all:
        targets = tests
    else:
        targets = [item for item in tests if item[0] == args.test]
        if not targets:
            sys.exit(f"Unknown pre-build test: {args.test}\nRun --help to see available tests.")

    failed: list[str] = []
    for name, project, description in targets:
        with timed_test_step(f"{name}  {description}") as step:
            step.passed = run_dotnet_tests(project)
            if not step.passed:
                failed.append(name)

    passed = len(targets) - len(failed)
    if failed:
        summary = fail(f"Passed {passed}  Failed {len(failed)}  Failed pre-build tests {', '.join(failed)}")
    else:
        summary = f"Passed {passed}  Failed 0"
    print_summary(summary)
    if failed:
        sys.exit(1)

def available_tests() -> list[tuple[str, Path, str]]:
    return [test for test in PRE_BUILD_TESTS if test[1].exists()]

def format_available_tests(tests: list[tuple[str, Path, str]]) -> str:
    return "\n".join(
        [
            "Available Pre-build Tests:",
            "",
            *format_test_lines(tests),
        ]
    )

def format_test_lines(tests: list[tuple[str, Path, str]]) -> list[str]:
    return [f"  {name:<18}  {desc}" for name, _, desc in tests]

def run_dotnet_tests(project: Path) -> bool:
    if not validate_csharp_test_descriptions(project):
        return False

    command = [
        "dotnet",
        "test",
        str(project),
        "-c",
        "Debug",
        "--nologo",
        "--logger",
        "console;verbosity=minimal",
        "--verbosity",
        "quiet",
    ]
    return subprocess.run(command).returncode == 0

def validate_csharp_test_descriptions(project: Path) -> bool:
    failures: list[str] = []
    for source in sorted(project.parent.rglob("*.cs")):
        if any(part in {"bin", "obj"} for part in source.relative_to(project.parent).parts):
            continue

        for line_number, line in enumerate(source.read_text(encoding="utf-8").splitlines(), start=1):
            stripped = line.strip()
            if not stripped.startswith(CSHARP_TEST_ATTRIBUTE_PREFIXES):
                continue

            match = CSHARP_TEST_DISPLAY_NAME_PATTERN.search(stripped)
            if not match:
                failures.append(f"{source.relative_to(ROOT)}:{line_number} is missing DisplayName")
                continue

            display_name = match.group(1).strip()
            if len(display_name) < CSHARP_TEST_DISPLAY_NAME_MIN_LENGTH or not contains_english_text(display_name):
                failures.append(f"{source.relative_to(ROOT)}:{line_number} has an unclear test description: {display_name}")

    if failures:
        print(fail("C# test description check failed:"))
        for failure in failures:
            print(f"  {failure}")
        return False

    return True

def contains_english_text(text: str) -> bool:
    return any(("A" <= char <= "Z") or ("a" <= char <= "z") for char in text)

if __name__ == "__main__":
    main()
