import shutil
from pathlib import Path

from build_support.paths import BUILD_DIR, ROOT


def clean_outputs() -> None:
    remove_path(BUILD_DIR)
    for project_file in (ROOT / "src").rglob("*.csproj"):
        remove_path(project_file.parent / "bin")
        remove_path(project_file.parent / "obj")


def remove_path(path: Path) -> None:
    if not path.exists():
        return

    resolved = path.resolve()
    allowed_roots = [BUILD_DIR.resolve(), (ROOT / "src").resolve()]
    if not any(resolved == root or resolved.is_relative_to(root) for root in allowed_roots):
        raise ValueError(f"Refusing to clean a path outside the project boundary: {resolved}")

    if path.is_dir():
        shutil.rmtree(path)
        print(f"  Removed {path}", flush=True)
        return

    path.unlink()
    print(f"  Removed {path}", flush=True)
