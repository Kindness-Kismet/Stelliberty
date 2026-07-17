from __future__ import annotations

import unittest

from scripts.build_support.beta_workflow import (
    flatten_pull_pages,
    requires_beta_build,
    select_beta_promotion,
)
from scripts.build_support.versioning import beta_release_metadata


class BetaWorkflowTests(unittest.TestCase):
    def test_first_beta_uses_stable_baseline_marker(self) -> None:
        metadata = beta_release_metadata(
            ["v2.0.7", "v2.0.5-beta1"],
            "2.0.7",
            ["v2.0.7", "beta-base/v2.0.7"],
        )

        self.assertEqual("2.0.8-beta1", metadata.version)
        self.assertEqual("v2.0.8-beta1", metadata.tag)
        self.assertEqual("beta-base/v2.0.7", metadata.previous_tag)

    def test_next_beta_uses_previous_beta_release(self) -> None:
        metadata = beta_release_metadata(
            ["v2.0.7", "v2.0.8-beta1", "v2.0.8-beta2"],
            "2.0.7",
            ["v2.0.7", "beta-base/v2.0.7", "v2.0.8-beta1", "v2.0.8-beta2"],
        )

        self.assertEqual("2.0.8-beta3", metadata.version)
        self.assertEqual("v2.0.8-beta2", metadata.previous_tag)

    def test_first_beta_falls_back_to_stable_tag(self) -> None:
        metadata = beta_release_metadata(["v2.0.7"], "2.0.7", ["v2.0.7"])

        self.assertEqual("v2.0.7", metadata.previous_tag)

    def test_version_metadata_does_not_hide_product_changes(self) -> None:
        self.assertTrue(requires_beta_build(["Directory.Build.props", "src/App.cs"]))
        self.assertFalse(requires_beta_build(["Directory.Build.props"]))

    def test_project_and_build_script_changes_require_beta_build(self) -> None:
        self.assertTrue(requires_beta_build(["src/App.csproj"]))
        self.assertTrue(requires_beta_build(["Cargo.toml"]))
        self.assertTrue(requires_beta_build(["scripts/build.py"]))

    def test_selects_latest_included_beta_promotion(self) -> None:
        pulls = [
            pull(10, "2026-07-01T00:00:00Z", "merge-old", "beta-old"),
            pull(11, "2026-07-10T00:00:00Z", "merge-new", "beta-new"),
            pull(12, "2026-07-12T00:00:00Z", "merge-future", "beta-future"),
        ]

        result = select_beta_promotion(
            pulls,
            "Kindness-Kismet/stelliberty",
            lambda commit: commit in {"merge-old", "merge-new"},
        )

        self.assertEqual(11, result.pull_number)
        self.assertEqual("beta-new", result.source_sha)

    def test_flattens_paginated_pull_responses(self) -> None:
        pages = [[{"number": 1}], [{"number": 2}]]

        self.assertEqual([{"number": 1}, {"number": 2}], flatten_pull_pages(pages))


def pull(number: int, merged_at: str, merge_sha: str, source_sha: str) -> dict[str, object]:
    return {
        "number": number,
        "merged_at": merged_at,
        "merge_commit_sha": merge_sha,
        "base": {"ref": "stable"},
        "head": {
            "ref": "beta",
            "sha": source_sha,
            "repo": {"full_name": "Kindness-Kismet/stelliberty"},
        },
    }


if __name__ == "__main__":
    unittest.main()
