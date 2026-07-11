import time
from contextlib import contextmanager

from build_support.console import header, timing


@contextmanager
def timed_step(label: str):
    print(header(label), flush=True)
    started_at = time.perf_counter()
    try:
        yield
    finally:
        elapsed = time.perf_counter() - started_at
        print(timing(elapsed), flush=True)
        print()
