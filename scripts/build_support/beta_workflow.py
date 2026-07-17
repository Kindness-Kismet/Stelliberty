from __future__ import annotations

from collections.abc import Callable, Iterable
from dataclasses import dataclass
from typing import Any


PRODUCT_SUFFIXES = (".cs", ".rs", ".axaml", ".csproj")
PRODUCT_FILES = {"Cargo.toml", "Cargo.lock", "Directory.Packages.props", "Stelliberty.slnx", "global.json"}


@dataclass(frozen=True)
class BetaPromotion:
    pull_number: int
    source_sha: str


def flatten_pull_pages(value: list[object]) -> list[dict[str, object]]:
    if value and all(isinstance(page, list) for page in value):
        return [pull for page in value for pull in page if isinstance(pull, dict)]
    return [pull for pull in value if isinstance(pull, dict)]


def requires_beta_build(paths: Iterable[str]) -> bool:
    for path in paths:
        normalized = path.strip().replace("\\", "/")
        if not normalized or normalized == "Directory.Build.props":
            continue
        if normalized.endswith(PRODUCT_SUFFIXES):
            return True
        if normalized in PRODUCT_FILES or normalized.startswith("scripts/"):
            return True
    return False


def select_beta_promotion(
    pulls: Iterable[dict[str, Any]],
    repository: str,
    is_ancestor: Callable[[str], bool],
) -> BetaPromotion:
    candidates: list[tuple[str, int, str]] = []
    for pull in pulls:
        head = pull.get("head") or {}
        head_repository = head.get("repo") or {}
        base = pull.get("base") or {}
        merge_sha = pull.get("merge_commit_sha")
        merged_at = pull.get("merged_at")
        source_sha = head.get("sha")
        if (
            merged_at
            and merge_sha
            and source_sha
            and base.get("ref") == "stable"
            and head.get("ref") == "beta"
            and head_repository.get("full_name") == repository
            and is_ancestor(merge_sha)
        ):
            candidates.append((merged_at, int(pull["number"]), source_sha))

    if not candidates:
        raise ValueError("No merged beta to stable pull request is included in this release")

    _, pull_number, source_sha = max(candidates)
    return BetaPromotion(pull_number, source_sha)
