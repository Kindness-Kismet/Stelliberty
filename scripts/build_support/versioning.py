from __future__ import annotations

import re
from dataclasses import dataclass


_VERSION_RE = re.compile(
    r"^v?(?P<major>\d+)\.(?P<minor>\d+)\.(?P<patch>\d+)(?:-(?P<pre>beta(?P<beta>\d+)))?$",
    re.IGNORECASE,
)


@dataclass(frozen=True)
class ParsedVersion:
    major: int
    minor: int
    patch: int
    beta: int | None = None

    @property
    def is_stable(self) -> bool:
        return self.beta is None

    @property
    def core(self) -> tuple[int, int, int]:
        return (self.major, self.minor, self.patch)

    def format(self) -> str:
        if self.beta is None:
            return f"{self.major}.{self.minor}.{self.patch}"
        return f"{self.major}.{self.minor}.{self.patch}-beta{self.beta}"


def parse_version(value: str) -> ParsedVersion | None:
    match = _VERSION_RE.match(value.strip())
    if match is None:
        return None
    beta_text = match.group("beta")
    return ParsedVersion(
        major=int(match.group("major")),
        minor=int(match.group("minor")),
        patch=int(match.group("patch")),
        beta=int(beta_text) if beta_text is not None else None,
    )


def latest_stable_version(tags: list[str]) -> ParsedVersion | None:
    best: ParsedVersion | None = None
    for tag in tags:
        parsed = parse_version(tag)
        if parsed is None or not parsed.is_stable:
            continue
        if best is None or parsed.core > best.core:
            best = parsed
    return best


def next_beta_version(tags: list[str], stable_base: ParsedVersion | None = None) -> str:
    base = stable_base or latest_stable_version(tags)
    if base is None:
        candidate = ParsedVersion(0, 0, 1, beta=1)
        return candidate.format()

    target_core = (base.major, base.minor, base.patch + 1)
    max_beta = 0
    for tag in tags:
        parsed = parse_version(tag)
        if parsed is None or parsed.is_stable:
            continue
        if parsed.core == target_core:
            max_beta = max(max_beta, parsed.beta or 0)

    return ParsedVersion(target_core[0], target_core[1], target_core[2], beta=max_beta + 1).format()


def to_tag(version: str) -> str:
    value = version.strip()
    return value if value.lower().startswith("v") else f"v{value}"
