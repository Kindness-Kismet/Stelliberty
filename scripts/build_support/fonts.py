import re
import urllib.request
from dataclasses import dataclass
from pathlib import Path

from build_support.paths import ROOT

FONT_DIR = ROOT / "src" / "Stelliberty.Desktop" / "Assets" / "fonts"
GOOGLE_FONT_CSS = "https://fonts.googleapis.com/css2?family=Google+Sans"
GOOGLE_SANS = "GoogleSans-Regular.ttf"
NOTO_SANS_SC = "NotoSansSC-VF.ttf"


@dataclass(frozen=True)
class FontAsset:
    name: str
    url: str
    min_bytes: int


FONT_ASSETS = [
    FontAsset(GOOGLE_SANS, GOOGLE_FONT_CSS, 32 * 1024),
    FontAsset(
        NOTO_SANS_SC,
        "https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/Variable/TTF/Subset/NotoSansSC-VF.ttf",
        1024 * 1024,
    ),
]


def ensure_app_fonts() -> list[Path]:
    paths: list[Path] = []
    FONT_DIR.mkdir(parents=True, exist_ok=True)

    for asset in FONT_ASSETS:
        path = FONT_DIR / asset.name
        if not is_valid_font(path, asset.min_bytes):
            payload = read_font_bytes(asset)
            if len(payload) < asset.min_bytes:
                raise RuntimeError(f"Downloaded font asset is unexpectedly small: {asset.name}")
            path.write_bytes(payload)
        paths.append(path)

    return paths


def is_valid_font(path: Path, min_bytes: int) -> bool:
    return path.exists() and path.stat().st_size >= min_bytes


def read_font_bytes(asset: FontAsset) -> bytes:
    if asset.name == GOOGLE_SANS:
        return read_bytes(read_google_font_url(asset.url))

    return read_bytes(asset.url)


def read_google_font_url(css_url: str) -> str:
    css = read_bytes(css_url).decode("utf-8")
    match = re.search(r"url\((https://fonts\.gstatic\.com/[^)]+\.ttf)\)", css)
    if match is None:
        raise RuntimeError("Unable to resolve the Google Sans font URL")

    return match.group(1)


def read_bytes(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "app-build"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()
