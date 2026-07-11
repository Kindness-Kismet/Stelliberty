import os
import subprocess

from build_support.paths import ROOT


def run(command: list[str], extra_env: dict[str, str] | None = None) -> None:
    env = os.environ.copy()
    env["PYTHONDONTWRITEBYTECODE"] = "1"
    env["MSBUILDDISABLENODEREUSE"] = "1"
    if extra_env is not None:
        env.update(extra_env)
    subprocess.run(command, cwd=ROOT, env=env, check=True)
