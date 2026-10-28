"""Static build-only contract tests; never invokes PyInstaller or installed applications."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
RECIPE = (ROOT / "DPMBuilder" / "build-release.ps1").read_text(encoding="utf-8")
WORKFLOW = (ROOT / ".github" / "workflows" / "release.yml").read_text(encoding="utf-8")
ASSEMBLY_INFO = (ROOT / "DisplayProfileManager" / "Properties" / "AssemblyInfo.cs").read_text(encoding="utf-8")
VERSION_MATCH = re.search(r'\[assembly:\s*AssemblyInformationalVersion\("([^"]+)"\)\]', ASSEMBLY_INFO)
if VERSION_MATCH is None:
    raise RuntimeError("AssemblyInformationalVersion not found")
CURRENT_VERSION = VERSION_MATCH.group(1)


class ReleaseRecipeTests(unittest.TestCase):
    def test_release_calls_one_shared_clean_builder_recipe(self):
        for name in ("DPMShortcutBuilder", "DPMThemeBuilder"):
            self.assertIn(f"./DPMBuilder/build-release.ps1 -Builder {name}", WORKFLOW)
        for arg in ('"--clean"', '"--noconfirm"', '"--onefile"', '"--windowed"'):
            self.assertIn(arg, RECIPE)
        self.assertIn("Join-Path $PSScriptRoot 'icon.ico'", RECIPE)
        self.assertIn('Join-Path $env:TEMP "dpm-2.3-builder-release"', RECIPE)

    def test_python_and_process_path_boundary(self):
        self.assertIn('if ($env:pythonLocation', RECIPE)
        self.assertIn('(Get-Command python.exe -ErrorAction Stop).Source', RECIPE)
        self.assertIn('$version -ne "3.13.15"', RECIPE)
        self.assertIn('$previousPath = $env:PATH', RECIPE)
        self.assertIn('$env:PATH = $buildPath -join', RECIPE)
        self.assertIn('$env:PATH = $previousPath', RECIPE)
        self.assertIn('Join-Path $basePython "DLLs"', RECIPE)
        self.assertNotIn('$env:PATH +=', RECIPE)

    def test_shortcut_only_module_exclusion_and_dependency_gates(self):
        self.assertIn('if ($Builder -eq "DPMShortcutBuilder")', RECIPE)
        for module in ("win32ui", "pythonwin", "pywin", "pywin.dialogs", "pywin.mfc"):
            self.assertIn(f'--exclude-module={module}"', RECIPE)
        for marker in ("win32ui.pyd", "mfc140u.dll", "ImageMagick", "Java_Temurin"):
            self.assertIn(marker, WORKFLOW)
        self.assertIn("DPMShortcutBuilder bundle is missing", WORKFLOW)
        self.assertIn("DPMThemeBuilder package is missing", WORKFLOW)

    def test_generated_work_and_spec_separate_from_source_tree(self):
        for arg in ('"--workpath=$work"', '"--specpath=$spec"', '"--distpath=$dist"'):
            self.assertIn(arg, RECIPE)
        self.assertIn('$packagingDist = Join-Path $PSScriptRoot "dist"', RECIPE)
        for name in ("DPMShortcutBuilder", "DPMThemeBuilder"):
            self.assertIn(f'"work/{name}/Analysis-00.toc"', WORKFLOW)
            self.assertIn(f'"work/{name}/PKG-00.toc"', WORKFLOW)

    def test_release_dispatch_is_generic_and_single_route(self):
        self.assertIn("  workflow_dispatch:", WORKFLOW)
        self.assertIn("      prerelease:", WORKFLOW)
        self.assertNotIn("  push:", WORKFLOW)
        self.assertNotIn("  workflow_call:", WORKFLOW)
        self.assertNotIn("prerelease_231", WORKFLOW)
        self.assertNotIn(CURRENT_VERSION, WORKFLOW)
        self.assertFalse((ROOT / ".github" / "workflows" / "prerelease.yml").exists())
        self.assertEqual(1, WORKFLOW.count("uses: softprops/action-gh-release@v2"))

    def test_release_dispatch_validates_selected_signed_tag_and_absence(self):
        self.assertIn("github.ref_type", WORKFLOW)
        self.assertIn("/git/ref/tags/$tag", WORKFLOW)
        self.assertIn("/git/tags/$($tagRef.object.sha)", WORKFLOW)
        self.assertIn("$tagObject.verification.verified", WORKFLOW)
        self.assertIn("releases/tags/$tag", WORKFLOW)
        self.assertIn("AssemblyInformationalVersion", WORKFLOW)

    def test_prerelease_metadata_never_runs_stable_downgrade(self):
        self.assertIn("prerelease: ${{ inputs.prerelease == true }}", WORKFLOW)
        self.assertIn("make_latest: ${{ inputs.prerelease == true && 'false' || 'true' }}", WORKFLOW)
        self.assertIn("if: ${{ inputs.prerelease != true }}", WORKFLOW)

    def test_previous_release_em_dash_heading_yields_clean_title(self):
        # The version-title parser must consume one separator, including an em dash.
        self.assertIn(r"'\s*(?:[—\-:]\s*)?(.*)$'", WORKFLOW)
        expression = r"^##\s+" + re.escape("9.8.7") + r"\s*(?:[—\-:]\s*)?(.*)$"
        for heading in ("## 9.8.7 — Historical Title", "## 9.8.7 - Historical Title", "## 9.8.7: Historical Title"):
            match = re.match(expression, heading)
            self.assertIsNotNone(match)
            self.assertEqual("Historical Title", match.group(1).strip())
            self.assertEqual("9.8.7: Historical Title", f"9.8.7: {match.group(1).strip()}")

    def test_prerelease_does_not_mutate_previous_stable_release(self):
        self.assertIn("if: ${{ inputs.prerelease != true }}", WORKFLOW)
        self.assertIn("make_latest: ${{ inputs.prerelease == true && 'false' || 'true' }}", WORKFLOW)
        self.assertIn("releases/tags/$tag", WORKFLOW)
        self.assertNotIn("prerelease_231", WORKFLOW)


if __name__ == "__main__":
    unittest.main()
