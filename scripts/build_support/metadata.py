from xml.etree import ElementTree

from build_support.models import AppMetadata
from build_support.paths import BUILD_PROPS


def read_app_metadata() -> AppMetadata:
    root = ElementTree.parse(BUILD_PROPS).getroot()
    app_name = require_property(root, "AppName")
    return AppMetadata(
        app_name=app_name,
        display_name=optional_property(root, "AppDisplayName") or app_name,
        version=require_property(root, "AppVersion"),
    )


def require_property(root: ElementTree.Element, name: str) -> str:
    value = root.findtext(f".//{name}")
    if value is None or value.strip() == "":
        raise RuntimeError(f"Directory.Build.props is missing {name}")
    return value.strip()


def optional_property(root: ElementTree.Element, name: str) -> str | None:
    value = root.findtext(f".//{name}")
    if value is None or value.strip() == "":
        return None
    return value.strip()


def display_version(version: str) -> str:
    return version if version.lower().startswith("v") else f"v{version}"


def normalize_version(version: str) -> str:
    value = version.strip()
    if value.lower().startswith("v"):
        value = value[1:]
    return value


def resolve_metadata_version(metadata: AppMetadata, version_override: str | None) -> AppMetadata:
    if version_override is None or version_override.strip() == "":
        return metadata
    return metadata.with_version(normalize_version(version_override))
