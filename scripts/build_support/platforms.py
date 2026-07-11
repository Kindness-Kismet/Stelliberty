import platform as host_platform
import sys

from build_support.models import PlatformTarget

PLATFORMS = {
    "win-x64": PlatformTarget("win-x64", "x86_64-pc-windows-msvc", "hub.dll", "stelliberty_service.exe"),
    "win-arm64": PlatformTarget("win-arm64", "aarch64-pc-windows-msvc", "hub.dll", "stelliberty_service.exe"),
    "linux-x64": PlatformTarget("linux-x64", "x86_64-unknown-linux-gnu", "libhub.so", "stelliberty_service"),
    "linux-arm64": PlatformTarget("linux-arm64", "aarch64-unknown-linux-gnu", "libhub.so", "stelliberty_service"),
    "macos-x64": PlatformTarget("osx-x64", "x86_64-apple-darwin", "libhub.dylib", "stelliberty_service"),
    "macos-arm64": PlatformTarget("osx-arm64", "aarch64-apple-darwin", "libhub.dylib", "stelliberty_service"),
}

PLATFORM_ALIASES = {
    "windows": "win-x64",
    "linux": "linux-x64",
    "macos": "macos-arm64",
    "osx": "macos-arm64",
    "osx-x64": "macos-x64",
    "osx-arm64": "macos-arm64",
}

def resolve_platforms(platform: str) -> list[tuple[str, PlatformTarget]]:
    if platform == "desktop":
        return [(name, PLATFORMS[name]) for name in ("win-x64", "linux-x64", "macos-arm64")]

    resolved = default_platform() if platform == "current" else PLATFORM_ALIASES.get(platform, platform)
    if resolved not in PLATFORMS:
        allowed = ", ".join(["current", "desktop", *PLATFORMS.keys()])
        raise ValueError(f"Unsupported platform '{platform}'. Allowed: {allowed}")

    return [(resolved, PLATFORMS[resolved])]

def default_platform() -> str:
    machine = host_platform.machine().lower()
    arch = "arm64" if machine in ("arm64", "aarch64") else "x64"

    if sys.platform == "win32":
        return f"win-{arch}"

    if sys.platform == "darwin":
        return f"macos-{arch}"

    return f"linux-{arch}"

def split_rid(rid: str) -> tuple[str, str]:
    aliases = {"win": "Windows", "linux": "Linux", "macos": "macOS"}
    base, _, arch = rid.partition("-")
    return aliases.get(base, base), arch
