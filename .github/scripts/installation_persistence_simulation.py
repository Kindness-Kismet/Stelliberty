#!/usr/bin/env python3
import argparse
import shutil
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main() -> None:
    parser = argparse.ArgumentParser(description="Simulate installer replacement while preserving portable data")
    parser.add_argument("--os-family", required=True, choices=["linux", "macos"])
    args = parser.parse_args()

    with tempfile.TemporaryDirectory(prefix="stelliberty-installation-") as temporary_directory:
        root = Path(temporary_directory)
        if args.os_family == "macos":
            verify_macos_layout_policy()
            simulate_macos_replacement(root)
        else:
            verify_linux_installer_policy()
            simulate_linux_package_replacement(root)
            simulate_appimage_replacement(root)


def verify_macos_layout_policy() -> None:
    resolver = read_repository_file("src/Stelliberty.Application/Platform/PortableDataDirectoryResolver.cs")
    require_fragments(resolver, ["ResolveMacOS", '".app"', '}.data'])


def verify_linux_installer_policy() -> None:
    launcher = read_repository_file("scripts/installer/linux/launcher.in")
    appimage = read_repository_file("scripts/installer/linux/appimage.AppRun.in")
    require_fragments(launcher, ["STELLIBERTY_APP_DATA_DIR", "/opt/@APP_PACKAGE@.data"])
    require_fragments(appimage, ["STELLIBERTY_APP_DATA_DIR", "APPIMAGE_DIR", "@APP_PACKAGE@.data"])

    for relative_path in [
        "scripts/installer/linux/postinst.in",
        "scripts/installer/linux/arch.INSTALL.in",
        "scripts/installer/linux/rpm.spec.in",
    ]:
        require_fragments(read_repository_file(relative_path), ["mkdir -p /opt/@APP_PACKAGE@.data"])


def simulate_macos_replacement(root: Path) -> None:
    applications_directory = root / "Applications"
    app_directory = applications_directory / "Stelliberty.app"
    data_directory = applications_directory / "Stelliberty.data"

    install_version(app_directory / "Contents" / "MacOS", "1")
    save_user_state(data_directory)
    replace_installation(app_directory, app_directory / "Contents" / "MacOS", "2")
    require_preserved(data_directory, app_directory / "Contents" / "MacOS")


def simulate_linux_package_replacement(root: Path) -> None:
    install_directory = root / "opt" / "stelliberty"
    data_directory = root / "opt" / "stelliberty.data"

    install_version(install_directory, "1")
    save_user_state(data_directory)
    replace_installation(install_directory, install_directory, "2")
    require_preserved(data_directory, install_directory)


def simulate_appimage_replacement(root: Path) -> None:
    extraction_root = root / "home" / "runner" / ".local" / "share" / "stelliberty" / "appimage"
    first_installation = extraction_root / "1"
    second_installation = extraction_root / "2"
    data_directory = root / "downloads" / "stelliberty.data"

    install_version(first_installation, "1")
    save_user_state(data_directory)
    shutil.rmtree(first_installation)
    install_version(second_installation, "2")
    require_preserved(data_directory, second_installation)


def replace_installation(installation_root: Path, payload_directory: Path, version: str) -> None:
    shutil.rmtree(installation_root)
    install_version(payload_directory, version)


def install_version(install_directory: Path, version: str) -> None:
    install_directory.mkdir(parents=True, exist_ok=True)
    (install_directory / "version").write_text(version, encoding="utf-8")


def save_user_state(data_directory: Path) -> None:
    data_directory.mkdir(parents=True, exist_ok=True)
    (data_directory / "settings.json").write_text("saved", encoding="utf-8")


def require_preserved(data_directory: Path, install_directory: Path) -> None:
    settings = (data_directory / "settings.json").read_text(encoding="utf-8")
    version = (install_directory / "version").read_text(encoding="utf-8")
    if settings != "saved" or version != "2":
        raise RuntimeError("Installer replacement did not preserve portable data")


def read_repository_file(relative_path: str) -> str:
    return (ROOT / relative_path).read_text(encoding="utf-8")


def require_fragments(content: str, fragments: list[str]) -> None:
    missing = [fragment for fragment in fragments if fragment not in content]
    if missing:
        raise RuntimeError(f"Installation persistence policy is incomplete: {missing}")


if __name__ == "__main__":
    main()
