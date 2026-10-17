"""Static build-only contract tests; never invokes PyInstaller or installed applications."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
RECIPE = (ROOT / "DPMBuilder" / "build-release.ps1").read_text(encoding="utf-8")
WORKFLOW = (ROOT / ".github" / "workflows" / "release.yml").read_text(encoding="utf-8")


class ReleaseRecipeTests(unittest.TestCase):
    def test_release_calls_one_shared_clean_builder_recipe(self):
        for name in ("DPMShortcutBuilder", "DPMThemeBuilder"):
            self.assertIn(f"./DPMBuilder/build-release.ps1 -Builder {name}", WORKFLOW)
        for arg in ('"--clean"', '"--noconfirm"', '"--onefile"', '"--windowed"'):
            self.assertIn(arg, RECIPE)
        self.assertIn('Join-Path $PSScriptRoot \'icon.ico\'', RECIPE)
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


if __name__ == "__main__":
    unittest.main()
