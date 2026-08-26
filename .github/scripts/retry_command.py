#!/usr/bin/env python3
import argparse
import subprocess
import sys
import time


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Retry a command after transient failures")
    parser.add_argument("--attempts", type=int, default=3)
    parser.add_argument("--delay-seconds", type=float, default=5)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if args.attempts < 1 or not command:
        raise SystemExit("A command and at least one attempt are required")

    for attempt in range(1, args.attempts + 1):
        print(f"Attempt {attempt}/{args.attempts}", flush=True)
        exit_code = subprocess.run(command, check=False).returncode
        if exit_code == 0:
            return 0
        if attempt < args.attempts:
            print(f"Command failed with exit code {exit_code}; retrying...", flush=True)
            time.sleep(args.delay_seconds)

    return exit_code


if __name__ == "__main__":
    sys.exit(main())
