import os
import sys

if sys.platform == "win32":
    os.system("")

_USE_COLOR = sys.stdout.isatty() and not os.environ.get("NO_COLOR")

CYAN = "\033[36m"
DIM = "\033[2m"
GREEN = "\033[32m"
YELLOW = "\033[33m"
RED = "\033[31m"
BOLD = "\033[1m"
RESET = "\033[0m"

def paint(text: str, *codes: str) -> str:
    if not _USE_COLOR:
        return text
    return "".join(codes) + text + RESET

def header(text: str) -> str:
    return paint(f"==> {text}", CYAN, BOLD)

def timing(elapsed: float) -> str:
    return paint(f"  · {elapsed:.2f}s", GREEN)

def print_summary(*extras: str) -> None:
    for line in extras:
        print(f"  {line}", flush=True)

def warn(text: str) -> str:
    return paint(text, YELLOW)

def fail(text: str) -> str:
    return paint(text, RED, BOLD)
