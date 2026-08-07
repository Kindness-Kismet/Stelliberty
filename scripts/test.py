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

from build_support.builder import generate_bindings
from build_support.console import fail, header, print_summary, timing
from build_support.help import MultilineHelpFormatter

ROOT = Path(__file__).resolve().parents[1]
RUST_WORKSPACE = ROOT / "Cargo.toml"
CSHARP_TESTS: list[tuple[str, Path, str]] = [
    ("infrastructure", ROOT / "tests" / "Stelliberty.Infrastructure.Tests" / "Stelliberty.Infrastructure.Tests.csproj", "Infrastructure integrations: core REST responses, remote downloads, and platform behavior"),
    ("tray-lifetime", ROOT / "tests" / "Stelliberty.Tray.Tests" / "Stelliberty.Tray.Tests.csproj", "Tray lifetime: UI process ownership, graceful exit, forced cleanup, and relaunch replacement"),
    ("proxy-selection", ROOT / "tests" / "Stelliberty.ProxySelection.Tests" / "Stelliberty.ProxySelection.Tests.csproj", "Proxy selection semantics: default nodes, fixed groups, local persistence, external sync, and outbound mode"),
    ("proxy-page", ROOT / "tests" / "Stelliberty.ProxyPage.Tests" / "Stelliberty.ProxyPage.Tests.csproj", "Proxy page interactions: visible groups, node switching, search, sorting, delay tests, and controller sync"),
    ("home-state", ROOT / "tests" / "Stelliberty.Home.Tests" / "Stelliberty.Home.Tests.csproj", "Home state: system proxy, TUN permissions, outbound mode, runtime refresh, and service mode"),
    ("shell-navigation", ROOT / "tests" / "Stelliberty.Shell.Tests" / "Stelliberty.Shell.Tests.csproj", "Window shell: page visibility, settings back navigation, and localization refresh"),
    ("runtime-config", ROOT / "tests" / "Stelliberty.RuntimeConfig.Tests" / "Stelliberty.RuntimeConfig.Tests.csproj", "Runtime config: ports, DNS overrides, TUN, LAN, external controller, and final transforms"),
    ("core-ipc-contract", ROOT / "tests" / "Stelliberty.IpcContract.Tests" / "Stelliberty.IpcContract.Tests.csproj", "IPC contract: C# wrapper methods, parameters, response parsing, and error-code propagation"),
    ("chain-proxy", ROOT / "tests" / "Stelliberty.ChainProxy.Tests" / "Stelliberty.ChainProxy.Tests.csproj", "Chain proxy: built-in detection, disabling, custom chain generation, naming conflicts, and invalid YAML fallback"),
    ("monitoring-pages", ROOT / "tests" / "Stelliberty.Monitoring.Tests" / "Stelliberty.Monitoring.Tests.csproj", "Monitoring pages: connection, log, and rule parsing, pause, filtering, closing, and refresh behavior"),
    ("subscription-page", ROOT / "tests" / "Stelliberty.Subscription.Tests" / "Stelliberty.Subscription.Tests.csproj", "Subscription page behavior: add, edit, override selection, chain proxy, update failures, and scheduling"),
    ("override-page", ROOT / "tests" / "Stelliberty.Override.Tests" / "Stelliberty.Override.Tests.csproj", "Override page behavior: add validation, import, save, reference cleanup, ordering, metadata, and update skips"),
    ("settings-page", ROOT / "tests" / "Stelliberty.Settings.Tests" / "Stelliberty.Settings.Tests.csproj", "Settings page behavior: core config, permission correction, data management, language, updates, system proxy, and theme"),
    ("webdav", ROOT / "tests" / "Stelliberty.WebDav.Tests" / "Stelliberty.WebDav.Tests.csproj", "WebDAV behavior: connection validation, folder creation, upload, list, download, and delete"),
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

RUST_TESTS: list[tuple[str, str, str]] = [
    ("empty-start", "rust:empty_start", "hub IPC starts the core with an empty config"),
    ("hub-ipc-contract", "rust:ipc_contract", "hub IPC contract for methods, fields, error codes, and lifecycle"),
    ("yaml-override", "rust:yaml_override", "YAML override output starts as the bootstrap config"),
    ("js-override", "rust:js_override", "JS override output starts as the bootstrap config"),
    ("combo", "rust:combo", "Combined YAML and JS override output starts as the bootstrap config"),
    ("config-switch", "rust:config_switch", "apply_config switches config while the core is already running"),
]

def main() -> None:
    tests = available_tests()
    parser = argparse.ArgumentParser(
        description="Pre-build test runner for Rust scenarios and C# business checks",
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
    category = parser.add_mutually_exclusive_group()
    category.add_argument("--rust", action="store_true", help="Run only Rust scenario integration tests")
    category.add_argument("--csharp", action="store_true", help="Run only C# business tests")
    args = parser.parse_args()
    category_filter = selected_category(args)

    if not args.test and not args.all and category_filter is None:
        parser.print_help()
        sys.exit(0)

    filtered_tests = filter_tests(tests, category_filter)
    if args.all or (not args.test and category_filter is not None):
        targets = filtered_tests
    else:
        targets = [item for item in filtered_tests if item[0] == args.test]
        if not targets:
            suffix = f" ({category_name(category_filter)})" if category_filter is not None else ""
            sys.exit(f"Unknown pre-build test{suffix}: {args.test}\nRun --help to see available tests.")

    if any(handler.startswith("dotnet:") for _, handler, _ in targets):
        generate_bindings()

    failed: list[str] = []
    for name, handler, description in targets:
        with timed_test_step(f"{name}  {description}") as step:
            step.passed = run_handler(handler)
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

def run_handler(handler: str) -> bool:
    if handler.startswith("rust:"):
        return run_features_test_bin(handler[len("rust:") :])
    if handler.startswith("dotnet:"):
        return run_dotnet_tests(Path(handler[len("dotnet:") :]))
    sys.exit(f"Unknown handler: {handler}")

def available_tests() -> list[tuple[str, str, str]]:
    tests = list(RUST_TESTS)
    for name, project, description in CSHARP_TESTS:
        if project.exists():
            tests.append((name, f"dotnet:{project}", description))
    return tests

def selected_category(args: argparse.Namespace) -> str | None:
    if args.rust:
        return "rust"
    if args.csharp:
        return "dotnet"
    return None

def filter_tests(tests: list[tuple[str, str, str]], category: str | None) -> list[tuple[str, str, str]]:
    if category is None:
        return tests
    return [item for item in tests if item[1].startswith(f"{category}:")]

def category_name(category: str | None) -> str:
    return {
        "rust": "Rust scenario integration tests",
        "dotnet": "C# business tests",
    }.get(category, "all tests")

def format_available_tests(tests: list[tuple[str, str, str]]) -> str:
    rust_tests = filter_tests(tests, "rust")
    csharp_tests = filter_tests(tests, "dotnet")
    return "\n".join(
        [
            "Available pre-build tests:",
            "",
            "Rust scenario pre-build tests:",
            *format_test_lines(rust_tests),
            "",
            "C# business pre-build tests:",
            *format_test_lines(csharp_tests),
        ]
    )

def format_test_lines(tests: list[tuple[str, str, str]]) -> list[str]:
    return [f"  {name:<18}  {desc}" for name, _, desc in tests]

def run_features_test_bin(bin_name: str) -> bool:
    command = [
        "cargo",
        "run",
        "--quiet",
        "--manifest-path",
        str(RUST_WORKSPACE),
        "-p",
        "features_test",
        "--bin",
        bin_name,
    ]
    return subprocess.run(command).returncode == 0

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
