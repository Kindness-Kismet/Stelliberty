#!/usr/bin/env python3

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

from build_support.beta_workflow import (
    flatten_pull_pages,
    requires_beta_build,
    select_beta_promotion,
)
from build_support.versioning import beta_release_metadata


def main() -> None:
    parser = argparse.ArgumentParser(description="Compute and validate beta workflow metadata")
    subparsers = parser.add_subparsers(dest="command", required=True)

    metadata = subparsers.add_parser("metadata")
    metadata.add_argument("--props-version", required=True)
    metadata.add_argument("--release-tags", type=Path, required=True)
    metadata.add_argument("--repository-tags", type=Path, required=True)

    changes = subparsers.add_parser("should-build")
    changes.add_argument("--paths", type=Path, required=True)

    promotion = subparsers.add_parser("select-promotion")
    promotion.add_argument("--pulls", type=Path, required=True)
    promotion.add_argument("--repository", required=True)
    promotion.add_argument("--stable-sha", required=True)

    args = parser.parse_args()
    if args.command == "metadata":
        result = beta_release_metadata(
            read_lines(args.release_tags),
            args.props_version,
            read_lines(args.repository_tags),
        )
        print(f"version={result.version}")
        print(f"tag={result.tag}")
        print(f"previous_tag={result.previous_tag}")
        return

    if args.command == "should-build":
        value = requires_beta_build(read_lines(args.paths))
        print(f"should_build={str(value).lower()}")
        return

    pulls = flatten_pull_pages(json.loads(args.pulls.read_text(encoding="utf-8")))
    result = select_beta_promotion(
        pulls,
        args.repository,
        lambda commit: is_ancestor(commit, args.stable_sha),
    )
    print(f"pull_number={result.pull_number}")
    print(f"source_sha={result.source_sha}")


def read_lines(path: Path) -> list[str]:
    return [line.strip() for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def is_ancestor(commit: str, stable_sha: str) -> bool:
    result = subprocess.run(
        ["git", "merge-base", "--is-ancestor", commit, stable_sha],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    if result.returncode not in (0, 1):
        raise RuntimeError(f"Failed to inspect stable ancestry for {commit}")
    return result.returncode == 0


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError, json.JSONDecodeError) as exception:
        print(str(exception), file=sys.stderr)
        raise SystemExit(1) from exception
