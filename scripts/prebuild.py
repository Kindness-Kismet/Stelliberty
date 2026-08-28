#!/usr/bin/env python3
import os
import sys

sys.dont_write_bytecode = True
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
# 进度输出含 · 与 ✅，cp1252/cp936 控制台会抛 UnicodeEncodeError，故自行接管标准流编码。
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

import argparse
import gzip
import io
import json
import shutil
import subprocess
import urllib.request
import zipfile
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

from build_support.clean import clean_outputs
from build_support.console import header, print_summary
from build_support.fonts import ensure_app_fonts
from build_support.help import MultilineHelpFormatter, format_choice_help
from build_support.layout import service_binary_name
from build_support.metadata import read_app_metadata
from build_support.paths import PRE_ASSETS_DIR, ROOT, RUST_WORKSPACE
from build_support.platforms import PLATFORMS, default_platform, split_rid
from build_support.timer import timed_step

PREBUILD_PLATFORM_HELP = format_choice_help(
    "Target platform.",
    [
        ("current", "Current host platform"),
        ("win-x64", "Windows x64"),
        ("win-arm64", "Windows ARM64"),
        ("linux-x64", "Linux x64"),
        ("linux-arm64", "Linux ARM64"),
        ("macos-x64", "macOS x64"),
        ("macos-arm64", "macOS ARM64"),
    ],
)

GEO_FILES: dict[str, str] = {
    "country.mmdb": "country.mmdb",
    "GeoLite2-ASN.mmdb": "asn.mmdb",
    "geoip.dat": "geoip.dat",
    "geoip.metadb": "geoip.metadb",
    "geosite.dat": "geosite.dat",
}

GEO_RELEASE = "https://github.com/MetaCubeX/meta-rules-dat/releases/download/latest"
MIHOMO_LATEST = "https://api.github.com/repos/MetaCubeX/mihomo/releases/latest"

MIHOMO_ASSET_KEY: dict[str, str] = {
    "win-x64": "windows-amd64",
    "win-arm64": "windows-arm64",
    "linux-x64": "linux-amd64",
    "linux-arm64": "linux-arm64",
    "macos-x64": "darwin-amd64",
    "macos-arm64": "darwin-arm64",
}

def main() -> None:
    args = parse_args()
    rid = resolve_rid(args.platform)
    target_dir = PRE_ASSETS_DIR / rid
    platform_name, arch = split_rid(rid)

    print(header("Current environment"))
    print(f"  Platform       {platform_name}")
    print(f"  Architecture   {arch}")
    print(f"  Configuration  {', '.join(c.capitalize() for c in args.configurations)}")
    print(f"  Proxy          {describe_proxy()}")
    print()

    if args.clean:
        with timed_step("Clean workspace"):
            clean_outputs()

    target_dir.mkdir(parents=True, exist_ok=True)

    with timed_step("Fetch mihomo core"):
        core_path = fetch_mihomo_core(rid, target_dir)

    with timed_step("Fetch GeoIP data"):
        geo_paths = fetch_geo_files(target_dir)

    with timed_step("Fetch font assets"):
        font_paths = ensure_app_fonts()
        for path in font_paths:
            print(f"  {path.name:<14}  {format_bytes(path.stat().st_size)}", flush=True)

    with timed_step("Build service mode"):
        service_paths = build_service_binaries(rid, target_dir, args.configurations)

    total_bytes = core_path.stat().st_size + sum(p.stat().st_size for p in geo_paths) + sum(p.stat().st_size for p in font_paths) + sum(p.stat().st_size for p in service_paths)
    print_summary(
        f"Location {target_dir}",
        f"Files {len(geo_paths) + 1 + len(font_paths) + len(service_paths)}",
        f"Total {format_bytes(total_bytes)}",
    )

def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Fetch the mihomo core and GeoIP data into build/pre_assets",
        formatter_class=MultilineHelpFormatter,
    )
    configuration = parser.add_mutually_exclusive_group()
    configuration.add_argument("--dev", action="store_true", help="Build Debug service-mode binaries")
    configuration.add_argument("--all", action="store_true", help="Build both Debug and Release service-mode binaries")
    parser.add_argument("--platform", metavar="TARGET", default="current", help=PREBUILD_PLATFORM_HELP)
    parser.add_argument("--clean", action="store_true", help="Clean build and project bin/obj directories before fetching")
    args = parser.parse_args()
    args.configurations = resolve_configurations(args)
    return args

def resolve_configurations(args: argparse.Namespace) -> list[str]:
    if args.all:
        return ["dev", "release"]

    if args.dev:
        return ["dev"]

    return ["release"]

def resolve_rid(platform: str) -> str:
    rid = default_platform() if platform == "current" else platform
    if rid not in MIHOMO_ASSET_KEY:
        allowed = ", ".join(sorted(MIHOMO_ASSET_KEY.keys()))
        raise SystemExit(f"Unsupported platform '{platform}'. Available: current, {allowed}")
    return rid

def describe_proxy() -> str:
    for key in ("HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy"):
        value = os.environ.get(key)
        if value:
            return f"configured ({key})"
    return "none (direct system connection)"

def fetch_mihomo_core(rid: str, target_dir: Path) -> Path:
    asset_key = MIHOMO_ASSET_KEY[rid]
    release = read_json(MIHOMO_LATEST)
    asset = pick_mihomo_asset(release, asset_key)
    print(f"  Asset {asset['name']}", flush=True)

    payload = read_bytes(asset["browser_download_url"])
    core_bytes = extract_core_bytes(asset["name"], payload)

    core_binary_name = "clash-mihomo-core.exe" if rid.startswith("win") else "clash-mihomo-core"
    target = target_dir / core_binary_name
    target.write_bytes(core_bytes)
    if not rid.startswith("win"):
        target.chmod(0o755)
    print(f"  Saved {target.name}  {format_bytes(len(core_bytes))}", flush=True)
    return target

def pick_mihomo_asset(release: dict, asset_key: str) -> dict:

    tag = release.get("tag_name", "")
    preferred = {f"mihomo-{asset_key}-{tag}.zip", f"mihomo-{asset_key}-{tag}.gz"}
    for item in release.get("assets", []):
        if item.get("name") in preferred:
            return item

    candidates = [
        item for item in release.get("assets", [])
        if asset_key in item.get("name", "") and item.get("name", "").endswith((".gz", ".zip"))
    ]
    fallback = [item for item in candidates if "compatible" not in item.get("name", "")]
    if fallback:
        return fallback[0]
    if candidates:
        return candidates[0]
    raise SystemExit(f"No matching mihomo asset found for {asset_key}")

def fetch_geo_files(target_dir: Path) -> list[Path]:
    saved: list[Path] = []
    with ThreadPoolExecutor(max_workers=len(GEO_FILES)) as executor:
        futures = {
            executor.submit(_fetch_one_geo, remote_name, local_name, target_dir): local_name
            for remote_name, local_name in GEO_FILES.items()
        }
        for future in as_completed(futures):
            target, size = future.result()
            print(f"  {target.name:<14}  {format_bytes(size)}", flush=True)
            saved.append(target)
    return saved

def build_service_binaries(rid: str, target_dir: Path, configurations: list[str]) -> list[Path]:
    target = PLATFORMS[rid]
    metadata = read_app_metadata()
    saved: list[Path] = []
    for profile in configurations:
        command = [
            "cargo",
            "build",
            "--manifest-path",
            str(RUST_WORKSPACE),
            "-p",
            "stelliberty_service",
            "--target",
            target.rust_target,
        ]
        cargo_profile = "release" if profile == "release" else "debug"
        if profile == "release":
            command.append("--release")
        env = os.environ.copy()
        env["STELLIBERTY_APP_NAME"] = metadata.app_name
        subprocess.run(command, cwd=ROOT, env=env, check=True)

        source = ROOT / "target" / target.rust_target / cargo_profile / target.service_binary
        destination_dir = target_dir / profile
        destination_dir.mkdir(parents=True, exist_ok=True)
        destination = destination_dir / service_binary_name(metadata, target)
        shutil.copy2(source, destination)
        if not rid.startswith("win"):
            destination.chmod(0o755)
        print(f"  {profile:<7} {destination.relative_to(target_dir)}  {format_bytes(destination.stat().st_size)}", flush=True)
        saved.append(destination)
    return saved

def _fetch_one_geo(remote_name: str, local_name: str, target_dir: Path) -> tuple[Path, int]:
    payload = read_bytes(f"{GEO_RELEASE}/{remote_name}")
    target = target_dir / local_name
    target.write_bytes(payload)
    return target, len(payload)

def read_json(url: str) -> dict:
    return json.loads(read_bytes(url).decode("utf-8"))

def read_bytes(url: str) -> bytes:
    request = urllib.request.Request(url, headers=github_headers())
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()

def github_headers() -> dict[str, str]:
    headers = {"Accept": "application/vnd.github+json"}
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers

def extract_core_bytes(asset_name: str, content: bytes) -> bytes:
    if asset_name.endswith(".gz"):
        return gzip.decompress(content)
    if asset_name.endswith(".zip"):
        with zipfile.ZipFile(io.BytesIO(content)) as archive:
            for name in archive.namelist():
                stem = Path(name).name.lower()
                if stem.startswith(("mihomo", "clash")) and (stem.endswith(".exe") or "." not in stem):
                    return archive.read(name)
        raise SystemExit("No core executable was found in the ZIP archive")
    raise SystemExit(f"Unknown core asset format: {asset_name}")

def format_bytes(num_bytes: int) -> str:
    units = ["B", "KB", "MB", "GB"]
    value = float(num_bytes)
    for unit in units:
        if value < 1024 or unit == units[-1]:
            return f"{int(value)} B" if unit == "B" else f"{value:.1f} {unit}"
        value /= 1024
    return f"{value:.1f} GB"

if __name__ == "__main__":
    main()
