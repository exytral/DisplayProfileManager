import base64
import ctypes
import importlib.machinery
import importlib.util
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest
from unittest import mock


MODULE_PATH = Path(__file__).resolve().parents[1] / "DPMShortcutBuilder.pyw"
loader = importlib.machinery.SourceFileLoader("dpm_shortcut_builder", str(MODULE_PATH))
spec = importlib.util.spec_from_loader(loader.name, loader)
builder = importlib.util.module_from_spec(spec)
loader.exec_module(builder)


class FakeVar:
    def __init__(self, value=""):
        self.value = value

    def get(self):
        return self.value

    def set(self, value):
        self.value = value


class FakeCombo:
    def __init__(self, current=0):
        self.value = current

    def current(self, value=None):
        if value is not None:
            self.value = value
        return self.value


class FakeButton:
    def state(self, _):
        pass


class ShortcutBuilderTests(unittest.TestCase):
    def build_script(self, restore_mode=builder.RESTORE_DYNAMIC):
        return builder.build_ps1(
            shortcut_name="Shortcut",
            profile_id="profile-'id",
            profile_name="Profile O'Brien",
            restore_mode=restore_mode,
            target_path=r"C:\Games\O'Brien Game\game.exe",
            target_args='--mode "wide screen"',
            target_workdir=r"C:\Games\O'Brien Game",
            pre_start=[{
                "path": r"C:\Tools\O'Brien Helper\helper.exe",
                "args": '--label "pre start"',
                "kill_on_exit": True,
                "delay": 1.5,
            }],
        )

    def test_ps1_round_trip_preserves_fields(self):
        script = self.build_script()
        fields = builder._parse_ps1_fields(script)

        self.assertEqual("profile-'id", fields["profile_id"])
        self.assertEqual("dynamic", fields["restore_mode"])
        self.assertEqual("", fields["restore_id"])
        self.assertEqual(r"C:\Games\O'Brien Game\game.exe", fields["target_path"])
        self.assertEqual('--mode "wide screen"', fields["target_args"])
        self.assertEqual(r"C:\Games\O'Brien Game", fields["target_workdir"])
        self.assertEqual(r"C:\Tools\O'Brien Helper\helper.exe", fields["pre_start"][0]["path"])
        self.assertEqual('--label "pre start"', fields["pre_start"][0]["args"])
        self.assertTrue(fields["pre_start"][0]["kill_on_exit"])
        self.assertEqual(1.5, fields["pre_start"][0]["delay"])

    def test_ps1_round_trip_preserves_empty_optional_args_and_explicit_restore(self):
        restore_id = "restore-'profile"
        script = builder.build_ps1(
            shortcut_name="Shortcut",
            profile_id="profile-id",
            profile_name="Profile",
            restore_mode=restore_id,
            target_path=r"C:\Games\game.exe",
            target_args="",
            target_workdir="",
            pre_start=[{
                "path": r"C:\Tools\helper.exe",
                "args": "",
                "kill_on_exit": False,
                "delay": 0,
            }],
        )

        fields = builder._parse_ps1_fields(script)

        self.assertEqual(restore_id, fields["restore_mode"])
        self.assertEqual(restore_id, fields["restore_id"])
        self.assertEqual("", fields["target_args"])
        self.assertEqual("", fields["target_workdir"])
        self.assertEqual("", fields["pre_start"][0]["args"])

    def test_kill_on_exit_tracks_owned_process_without_path_wide_sweep(self):
        script = self.build_script()

        self.assertIn("$ownedPreStart = @()", script)
        self.assertIn("$pArgs.PassThru = $true", script)
        self.assertIn("$ownedPreStart += $preStartProc", script)
        self.assertIn("Stop-Process -Id $ownedProc.Id", script)
        self.assertNotIn("Get-Process | Where-Object", script)

    def test_duplicate_regenerates_wrappers_for_duplicate_only(self):
        with tempfile.TemporaryDirectory() as temp:
            source = builder.create_shortcut_folder("Source", temp)
            target = os.path.join(temp, "target.exe")
            Path(target).write_bytes(b"exe")
            Path(source.ps1_path).write_text(
                builder.build_ps1(
                    shortcut_name="Source",
                    profile_id="profile-id",
                    profile_name="Profile",
                    restore_mode=builder.RESTORE_NONE,
                    target_path=target,
                    target_args="",
                    target_workdir="",
                    pre_start=[],
                ),
                encoding="utf-8",
            )
            Path(os.path.join(source.folder, "Source.vbs")).write_text("source-vbs", encoding="utf-8")
            Path(source.lnk_path).write_text("source-lnk", encoding="utf-8")

            def fake_create_lnk(lnk_path, ps1_path, icon_source=None):
                Path(lnk_path).write_text(
                    f"ps1={ps1_path}\nicon={icon_source}", encoding="utf-8"
                )

            with mock.patch.object(builder, "create_lnk", side_effect=fake_create_lnk):
                duplicate = builder.duplicate_shortcut("Source", "Duplicate", temp)

            duplicate_vbs = Path(duplicate.folder, "Duplicate.vbs").read_text(encoding="utf-8")
            duplicate_lnk = Path(duplicate.lnk_path).read_text(encoding="utf-8")
            self.assertIn(duplicate.ps1_path, duplicate_vbs)
            self.assertIn(duplicate.ps1_path, duplicate_lnk)
            self.assertNotIn(os.path.join(source.folder, "Source.ps1"), duplicate_vbs)
            self.assertNotIn(os.path.join(source.folder, "Source.ps1"), duplicate_lnk)
            self.assertIn(f"icon={target}", duplicate_lnk)
            self.assertFalse(Path(duplicate.folder, "Source.vbs").exists())
            self.assertFalse(Path(duplicate.folder, "Source.lnk").exists())

            before = (
                Path(duplicate.ps1_path).read_text(encoding="utf-8"),
                duplicate_vbs,
                duplicate_lnk,
            )
            Path(source.ps1_path).write_text("modified", encoding="utf-8")
            shutil.rmtree(source.folder)
            after = (
                Path(duplicate.ps1_path).read_text(encoding="utf-8"),
                Path(duplicate.folder, "Duplicate.vbs").read_text(encoding="utf-8"),
                Path(duplicate.lnk_path).read_text(encoding="utf-8"),
            )
            self.assertEqual(before, after)

    def test_broken_entry_load_clears_stale_fields(self):
        panel = object.__new__(builder.ConfigPanel)
        panel._name_var = FakeVar("Old")
        panel._target_path = FakeVar("old-target")
        panel._target_args = FakeVar("old-args")
        panel._target_wd = FakeVar("old-workdir")
        panel._profile_cb = FakeCombo(1)
        panel._restore_cb = FakeCombo(1)
        panel._pre_start_rows = []
        panel._profiles = [builder.DpmProfile("profile-id", "Profile", "profile.dpm")]
        panel._save_btn = FakeButton()
        panel._export_btn = FakeButton()
        panel._dirty = True
        panel._update_launcher_strings = lambda: None

        with tempfile.TemporaryDirectory() as temp:
            broken = builder._entry_for("Broken", os.path.join(temp, "Broken"))
            panel.load_entry(broken)

        self.assertEqual("", panel._target_path.get())
        self.assertEqual("", panel._target_args.get())
        self.assertEqual("", panel._target_wd.get())
        self.assertEqual(0, panel._profile_cb.current())
        self.assertEqual(0, panel._restore_cb.current())
        self.assertFalse(panel._dirty)

    def test_empty_target_is_rejected(self):
        panel = object.__new__(builder.ConfigPanel)
        panel._name_var = FakeVar("Shortcut")
        panel._profile_cb = FakeCombo(1)
        panel._profiles = [builder.DpmProfile("profile-id", "Profile", "profile.dpm")]
        panel._restore_cb = FakeCombo(0)
        panel._target_path = FakeVar("")
        panel._target_args = FakeVar("")
        panel._target_wd = FakeVar("")
        panel._pre_start_rows = []

        with self.assertRaisesRegex(ValueError, "target application"):
            panel._collect_fields()

    def test_new_entry_vbs_write_failure_leaves_no_shortcut_folder(self):
        panel = object.__new__(builder.ConfigPanel)
        profile = builder.DpmProfile("profile-id", "Profile", "profile.dpm")
        original_write = builder._write_utf8

        def fail_vbs(path, content):
            if path.endswith(".vbs"):
                raise OSError("vbs failed")
            original_write(path, content)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(builder, "SHORTCUTS_DIR", temp), \
                    mock.patch.object(builder, "_write_utf8", side_effect=fail_vbs):
                with self.assertRaisesRegex(ValueError, "Failed to write \\.vbs"):
                    panel._write_shortcut(
                        "Shortcut", profile, builder.RESTORE_NONE,
                        r"C:\\Games\\game.exe", "", "", [],
                    )

            self.assertFalse(Path(temp, "Shortcut").exists())
            self.assertEqual([], list(Path(temp).iterdir()))

    def test_new_entry_lnk_failure_leaves_no_shortcut_folder(self):
        panel = object.__new__(builder.ConfigPanel)
        profile = builder.DpmProfile("profile-id", "Profile", "profile.dpm")

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(builder, "SHORTCUTS_DIR", temp), \
                    mock.patch.object(builder, "create_lnk", side_effect=RuntimeError("lnk failed")):
                with self.assertRaisesRegex(ValueError, "lnk failed"):
                    panel._write_shortcut(
                        "Shortcut", profile, builder.RESTORE_NONE,
                        r"C:\\Games\\game.exe", "", "", [],
                    )

            self.assertFalse(Path(temp, "Shortcut").exists())
            self.assertEqual([], list(Path(temp).iterdir()))

    def test_overwrite_publish_failure_restores_prior_artifact_bytes(self):
        panel = object.__new__(builder.ConfigPanel)
        profile = builder.DpmProfile("profile-id", "Profile", "profile.dpm")

        with tempfile.TemporaryDirectory() as temp:
            folder = Path(os.path.realpath(temp), "Shortcut")
            folder.mkdir()
            prior = {
                "Shortcut.ps1": b"old-ps1",
                "Shortcut.vbs": b"old-vbs",
                "Shortcut.lnk": b"old-lnk",
            }
            for filename, content in prior.items():
                Path(folder, filename).write_bytes(content)

            def fake_create_lnk(lnk_path, ps1_path, icon_source=None):
                Path(lnk_path).write_text(f"ps1={ps1_path}", encoding="utf-8")

            def fail_staged_publish(source, destination):
                if destination == str(folder) and ".staging-" in source and not source.endswith(".backup"):
                    raise OSError("publish failed")
                os.replace(source, destination)

            with mock.patch.object(builder, "SHORTCUTS_DIR", temp), \
                    mock.patch.object(builder.messagebox, "askyesno", return_value=True), \
                    mock.patch.object(builder, "create_lnk", side_effect=fake_create_lnk), \
                    mock.patch.object(builder, "_replace_shortcut_path", side_effect=fail_staged_publish):
                with self.assertRaisesRegex(ValueError, "publish failed"):
                    panel._write_shortcut(
                        "Shortcut", profile, builder.RESTORE_NONE,
                        r"C:\\Games\\game.exe", "", "", [],
                    )

            for filename, content in prior.items():
                self.assertEqual(content, Path(folder, filename).read_bytes())
            self.assertEqual(["Shortcut"], sorted(path.name for path in Path(temp).iterdir()))

    def test_successful_overwrite_publishes_complete_set_without_residue(self):
        panel = object.__new__(builder.ConfigPanel)
        profile = builder.DpmProfile("profile-id", "Profile", "profile.dpm")

        with tempfile.TemporaryDirectory() as temp:
            folder = Path(os.path.realpath(temp), "Shortcut")
            folder.mkdir()
            Path(folder, "Shortcut.ps1").write_bytes(b"old-ps1")
            Path(folder, "Shortcut.vbs").write_bytes(b"old-vbs")
            Path(folder, "Shortcut.lnk").write_bytes(b"old-lnk")
            Path(folder, "sentinel.dat").write_bytes(b"sentinel")
            nested = Path(folder, "assets")
            nested.mkdir()
            Path(nested, "nested-sentinel.dat").write_bytes(b"nested-sentinel")
            final_ps1 = os.path.join(os.path.realpath(temp), "Shortcut", "Shortcut.ps1")

            def fake_create_lnk(lnk_path, ps1_path, icon_source=None):
                Path(lnk_path).write_text(f"ps1={ps1_path}", encoding="utf-8")

            with mock.patch.object(builder, "SHORTCUTS_DIR", temp), \
                    mock.patch.object(builder.messagebox, "askyesno", return_value=True), \
                    mock.patch.object(builder, "create_lnk", side_effect=fake_create_lnk):
                entry = panel._write_shortcut(
                    "Shortcut", profile, builder.RESTORE_NONE,
                    r"C:\\Games\\game.exe", "", "", [],
                )

            self.assertEqual(final_ps1, entry.ps1_path)
            self.assertNotEqual(b"old-ps1", Path(folder, "Shortcut.ps1").read_bytes())
            self.assertIn(final_ps1, Path(folder, "Shortcut.vbs").read_text(encoding="utf-8"))
            self.assertEqual(f"ps1={final_ps1}", Path(folder, "Shortcut.lnk").read_text(encoding="utf-8"))
            self.assertEqual(b"sentinel", Path(folder, "sentinel.dat").read_bytes())
            self.assertEqual(b"nested-sentinel", Path(folder, "assets", "nested-sentinel.dat").read_bytes())
            self.assertEqual(["Shortcut"], sorted(path.name for path in Path(temp).iterdir()))

    def test_close_uses_unsaved_guard(self):
        app = object.__new__(builder.App)
        destroyed = []
        app._check_unsaved = lambda: False
        app.destroy = lambda: destroyed.append(True)
        app._on_close()
        self.assertEqual([], destroyed)

        app._check_unsaved = lambda: True
        app._on_close()
        self.assertEqual([True], destroyed)




class ShortcutContainmentTests(unittest.TestCase):
    def test_rejects_ambiguous_windows_components(self):
        bad = ["", ".", "..", " ", "Name ", "Name.", "CON", "con.txt", "NUL.log",
               "COM1.txt", "LPT9.backup", "COM¹.txt", "COM²", "LPT³.ext",
               "a/b", r"a\b", "x:y", "a\x00b", "name\nother", "a*"]
        for value in bad:
            with self.subTest(value=value):
                self.assertIsNotNone(builder.validate_name(value))
                with tempfile.TemporaryDirectory() as root:
                    with self.assertRaises(ValueError):
                        builder._shortcut_child(root, value)

    def test_accepts_unicode_and_legitimate_dots(self):
        with tempfile.TemporaryDirectory() as root:
            for value in ["Résumé.2026", "日本語", "Game.v2", "CÔM1", "Profile_2"]:
                with self.subTest(value=value):
                    self.assertIsNone(builder.validate_name(value))
                    self.assertEqual(os.path.join(os.path.realpath(root), value),
                                     builder._shortcut_child(root, value))

    def test_delete_rejects_root_parent_without_rmtree(self):
        with tempfile.TemporaryDirectory() as root:
            with mock.patch.object(builder.shutil, "rmtree") as remove:
                for value in (".", "..", "CON.txt", r"..\outside"):
                    with self.subTest(value=value), self.assertRaises(ValueError):
                        builder.delete_shortcut(value, root)
                remove.assert_not_called()

    def test_source_and_destination_rejected_before_effect(self):
        with tempfile.TemporaryDirectory() as root:
            with mock.patch.object(builder.shutil, "copytree") as copy, \
                 mock.patch.object(builder.os, "rename") as rename:
                for invalid in ("..", "con.txt", "Folder."):
                    with self.assertRaises(ValueError):
                        builder.duplicate_shortcut(invalid, "Valid", root)
                    with self.assertRaises(ValueError):
                        builder.duplicate_shortcut("Valid", invalid, root)
                    with self.assertRaises(ValueError):
                        builder.rename_shortcut(invalid, "Valid", root)
                    with self.assertRaises(ValueError):
                        builder.rename_shortcut("Valid", invalid, root)
                copy.assert_not_called()
                rename.assert_not_called()

    def test_save_rejects_invalid_name_before_prompt_or_staging(self):
        with tempfile.TemporaryDirectory() as root:
            panel = object.__new__(builder.ConfigPanel)
            profile = type("Profile", (), {"id":"test-id","name":"test"})()
            with mock.patch.object(builder, "SHORTCUTS_DIR", root), \
                 mock.patch.object(builder.messagebox, "askyesno") as prompt, \
                 mock.patch.object(builder.tempfile, "mkdtemp") as staging:
                for invalid in (".", "..", "CON.txt", "a."):
                    with self.subTest(value=invalid), self.assertRaises(ValueError):
                        panel._write_shortcut(invalid, profile, builder.RESTORE_NONE,
                                              r"C:\fake\target.exe", "", "", [])
                prompt.assert_not_called()
                staging.assert_not_called()

    def test_symlink_child_is_rejected_before_delete(self):
        with tempfile.TemporaryDirectory() as root:
            with tempfile.TemporaryDirectory() as other:
                link = os.path.join(root, "Linked")
                try:
                    os.symlink(other, link, target_is_directory=True)
                except (OSError, NotImplementedError):
                    self.skipTest("No nonprivileged symlink support")
                with mock.patch.object(builder.shutil, "rmtree") as remove:
                    with self.assertRaises(ValueError):
                        builder.delete_shortcut("Linked", root)
                    remove.assert_not_called()




class ShortcutRecursiveContainmentTests(unittest.TestCase):
    def _save(self, panel, profile):
        return panel._write_shortcut("Safe", profile, builder.RESTORE_NONE,
                                      r"C:\Synthetic\game.exe", "", "", [])

    def test_nested_junction_never_copied_by_save_or_duplicate(self):
        import subprocess
        with tempfile.TemporaryDirectory() as sandbox:
            root = Path(sandbox, "Shortcuts")
            root.mkdir()
            source = root / "Safe"
            source.mkdir()
            Path(source, "Safe.ps1").write_text("synthetic", encoding="utf-8")
            external = Path(sandbox, "Outside")
            external.mkdir()
            Path(external, "EXTERNAL-SENTINEL.txt").write_bytes(b"outside")
            junction = source / "linked"
            if os.name == "nt":
                created = subprocess.run(["cmd", "/c", "mklink", "/J",
                                          str(junction), str(external)],
                                         capture_output=True, text=True)
                if created.returncode:
                    self.skipTest("Windows directory junction unavailable")
            else:
                try:
                    os.symlink(external, junction, target_is_directory=True)
                except OSError:
                    self.skipTest("Symlink fixture unavailable")
            try:
                with self.assertRaisesRegex(ValueError, "reparse|link"):
                    builder._reject_recursive_copy_links(str(source))
                with mock.patch.object(builder.shutil, "copytree") as copied:
                    with self.assertRaises(ValueError):
                        builder.duplicate_shortcut("Safe", "Duplicate", str(root))
                    copied.assert_not_called()
                panel = object.__new__(builder.ConfigPanel)
                profile = builder.DpmProfile("id", "Profile", "profile.dpm")
                with mock.patch.object(builder, "SHORTCUTS_DIR", str(root)), \
                     mock.patch.object(builder.messagebox, "askyesno", return_value=True), \
                     mock.patch.object(builder.shutil, "copytree") as copied:
                    with self.assertRaises(ValueError):
                        self._save(panel, profile)
                    copied.assert_not_called()
                self.assertEqual(b"outside", Path(external, "EXTERNAL-SENTINEL.txt").read_bytes())
                self.assertEqual(["Safe"], sorted(p.name for p in root.iterdir()))
            finally:
                if junction.exists() or junction.is_symlink():
                    if os.name == "nt":
                        os.rmdir(junction)
                    else:
                        junction.unlink()

    def test_post_staging_guard_failure_cleans_staging(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp, "Safe")
            folder.mkdir()
            (folder / "Safe.ps1").write_bytes(b"old")
            panel = object.__new__(builder.ConfigPanel)
            profile = builder.DpmProfile("id", "Profile", "profile.dpm")
            original = builder._shortcut_child
            calls = 0
            def guard(root, name):
                nonlocal calls
                calls += 1
                if calls == 2:
                    raise ValueError("synthetic containment denial")
                return original(root, name)
            with mock.patch.object(builder, "SHORTCUTS_DIR", temp), \
                 mock.patch.object(builder.messagebox, "askyesno", return_value=True), \
                 mock.patch.object(builder, "_shortcut_child", side_effect=guard), \
                 mock.patch.object(builder.shutil, "copytree") as copied:
                with self.assertRaises(ValueError):
                    self._save(panel, profile)
                copied.assert_not_called()
            self.assertEqual(b"old", (folder / "Safe.ps1").read_bytes())
            self.assertEqual(["Safe"], sorted(p.name for p in Path(temp).iterdir()))

    def test_post_backup_guard_failure_rolls_back_previous_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp, "Safe")
            folder.mkdir()
            prior = {"Safe.ps1": b"old-ps1", "Safe.vbs": b"old-vbs",
                     "Safe.lnk": b"old-lnk"}
            for name, content in prior.items():
                (folder / name).write_bytes(content)
            panel = object.__new__(builder.ConfigPanel)
            profile = builder.DpmProfile("id", "Profile", "profile.dpm")
            original = builder._shortcut_child
            calls = 0
            def guard(root, name):
                nonlocal calls
                calls += 1
                if calls == 3:
                    raise ValueError("synthetic post-backup denial")
                return original(root, name)
            def fake_lnk(lnk_path, ps1_path, icon_source=None):
                Path(lnk_path).write_text("synthetic", encoding="utf-8")
            with mock.patch.object(builder, "SHORTCUTS_DIR", temp), \
                 mock.patch.object(builder.messagebox, "askyesno", return_value=True), \
                 mock.patch.object(builder, "_shortcut_child", side_effect=guard), \
                 mock.patch.object(builder, "create_lnk", side_effect=fake_lnk):
                with self.assertRaisesRegex(ValueError, "synthetic post-backup denial"):
                    self._save(panel, profile)
            for name, content in prior.items():
                self.assertEqual(content, (folder / name).read_bytes())
            self.assertEqual(["Safe"], sorted(p.name for p in Path(temp).iterdir()))


PS_SINGLE_QUOTES = "'\u2018\u2019\u201A\u201B"
INSTALLER_DEFAULT_EXE = r"LocalAppData\Programs\Display Profile Manager\DisplayProfileManager.exe"
LEGACY_PROGRAMS_EXE = r"LocalAppData\Programs\DisplayProfileManager\DisplayProfileManager.exe"
LEGACY_LOCAL_EXE = r"LocalAppData\DisplayProfileManager\DisplayProfileManager.exe"

# ParseFile applies each host's script-file decoding; the parsed script is never invoked
PS_AST_COMMAND = r"""
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($env:DPM_AST_INPUT, [ref]$tokens, [ref]$errors)
$literals = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and $node.StringConstantType -eq 'SingleQuoted' }, $true))
'errors=' + @($errors).Count
foreach ($literal in $literals) { 'literal=' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($literal.Value)) }
"""


def read_ps_single_quoted(text, start):
    # PowerShell closes on any delimiter in the class and treats any delimiter pair as one literal delimiter
    if text[start] not in PS_SINGLE_QUOTES:
        raise ValueError("Not a single-quoted literal")
    value = []
    index = start + 1
    while index < len(text):
        char = text[index]
        if char in PS_SINGLE_QUOTES:
            if index + 1 < len(text) and text[index + 1] in PS_SINGLE_QUOTES:
                value.append(text[index + 1])
                index += 2
                continue
            return "".join(value)
        value.append(char)
        index += 1
    raise ValueError("Unterminated single-quoted literal")


def literal_after(text, pattern):
    return read_ps_single_quoted(text, re.search(pattern, text, re.MULTILINE).end())


def parse_with_powershell(host, script_path):
    encoded = base64.b64encode(PS_AST_COMMAND.encode("utf-16-le")).decode("ascii")
    result = subprocess.run(
        [host, "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
        capture_output=True, text=True, timeout=120,
        env=dict(os.environ, DPM_AST_INPUT=script_path),
    )
    lines = result.stdout.splitlines()
    errors = next((int(line[7:]) for line in lines if line.startswith("errors=")), None)
    literals = [base64.b64decode(line[8:]).decode("utf-8") for line in lines if line.startswith("literal=")]
    return errors, literals, result


class ShortcutPowerShellQuotingTests(unittest.TestCase):
    def fields(self):
        return dict(
            shortcut_name="Shortcut",
            profile_id="profile-id",
            profile_name="Dad\u2019s TV \u2018Den\u2019 \u201AOld\u201B O'Brien \u00d1",
            restore_mode=builder.RESTORE_NONE,
            target_path="C:\\Games\\\u00d1and\u00fa \u2019Quest\u2019\\game.exe",
            target_args="--name \u2018Player\u2019 --dir C:\\Saves\\",
            target_workdir="C:\\Games\\\u00d1and\u00fa \u2019Quest\u2019\\",
            pre_start=[{
                "path": "C:\\Tools\\\u0451\u2019s Helper\\helper.exe",
                "args": "--tag \u201Bx\u201A",
                "kill_on_exit": False,
                "delay": 0,
            }],
        )

    def write_script(self, folder, fields):
        entry = builder._entry_for("Shortcut", folder)
        os.makedirs(entry.folder, exist_ok=True)
        with mock.patch.object(builder, "create_lnk"), \
             mock.patch.object(builder, "_shortcut_icon_source", return_value=None):
            builder._write_shortcut_artifacts(entry, entry.ps1_path, builder.build_ps1(**fields), fields["target_path"])
        return entry.ps1_path

    def test_ps1_escape_doubles_every_single_quote_delimiter(self):
        for quote in PS_SINGLE_QUOTES:
            with self.subTest(quote=ascii(quote)):
                escaped = builder._ps1_escape(f"a{quote}b")

                self.assertEqual(f"a{quote}{quote}b", escaped)
                self.assertEqual(f"a{quote}b", builder._ps1_unescape(escaped))

    def test_generated_literals_decode_to_original_values_under_powershell_quote_rules(self):
        fields = self.fields()

        script = builder.build_ps1(**fields)

        self.assertEqual(fields["profile_name"], literal_after(script, r"^\$profileName\s+= "))
        self.assertEqual(fields["target_path"], literal_after(script, r"^    Path\s+= "))
        self.assertEqual(fields["target_args"], literal_after(script, r"^    Args\s+= "))
        self.assertEqual(fields["target_workdir"], literal_after(script, r"^    WorkingDirectory\s+= "))
        self.assertEqual(fields["pre_start"][0]["path"], literal_after(script, r"@\{ Path = "))
        self.assertEqual(fields["pre_start"][0]["args"], literal_after(script, r"; Args = "))

    def test_ps1_round_trip_preserves_typographic_quotes_unicode_and_trailing_backslashes(self):
        fields = self.fields()

        parsed = builder._parse_ps1_fields(builder.build_ps1(**fields))

        self.assertEqual(fields["target_path"], parsed["target_path"])
        self.assertEqual(fields["target_args"], parsed["target_args"])
        self.assertEqual(fields["target_workdir"], parsed["target_workdir"])
        self.assertEqual(fields["pre_start"][0]["path"], parsed["pre_start"][0]["path"])
        self.assertEqual(fields["pre_start"][0]["args"], parsed["pre_start"][0]["args"])

    def test_placeholder_text_inside_values_is_not_substituted(self):
        fields = self.fields()
        fields["profile_name"] = "{{TARGET_PATH}}"
        fields["target_path"] = "C:\\Games\\{{PRE_START_ENTRIES}}\\game.exe"

        script = builder.build_ps1(**fields)
        parsed = builder._parse_ps1_fields(script)

        self.assertEqual("{{TARGET_PATH}}", literal_after(script, r"^\$profileName\s+= "))
        self.assertEqual(fields["target_path"], parsed["target_path"])
        self.assertEqual(1, script.count("KillOnExit ="))

    def test_saved_script_is_utf8_with_bom_and_loads_back(self):
        fields = self.fields()
        with tempfile.TemporaryDirectory() as temp:
            path = self.write_script(os.path.join(temp, "Shortcut"), fields)
            data = Path(path).read_bytes()
            parsed = builder._parse_ps1_fields(data.decode("utf-8-sig"))

        self.assertTrue(data.startswith(b"\xef\xbb\xbf"))
        self.assertEqual(fields["target_path"], parsed["target_path"])

    def test_saved_script_parses_with_literal_values_in_powershell_hosts(self):
        hosts = [path for path in (shutil.which("powershell.exe"), shutil.which("pwsh.exe")) if path]
        if not hosts:
            self.skipTest("No PowerShell host available for parser-only validation")
        fields = self.fields()
        expected = [
            fields["profile_name"], fields["target_path"], fields["target_args"], fields["target_workdir"],
            fields["pre_start"][0]["path"], fields["pre_start"][0]["args"],
        ]

        with tempfile.TemporaryDirectory() as temp:
            path = self.write_script(os.path.join(temp, "Shortcut"), fields)
            for host in hosts:
                with self.subTest(host=os.path.basename(host)):
                    errors, literals, result = parse_with_powershell(host, path)

                    self.assertEqual(0, errors, result.stdout + result.stderr)
                    for value in expected:
                        self.assertIn(value, literals)


class ShortcutDpmExeResolutionTests(unittest.TestCase):
    def resolve(self, files=(), directories=(), registry=None):
        with tempfile.TemporaryDirectory() as temp:
            script_dir = os.path.join(temp, "Builder")
            os.makedirs(script_dir)
            for relative in directories:
                os.makedirs(os.path.join(temp, relative))
            for relative in files:
                path = os.path.join(temp, relative)
                os.makedirs(os.path.dirname(path), exist_ok=True)
                Path(path).write_text("placeholder", encoding="utf-8")
            open_key = mock.Mock() if registry else mock.Mock(side_effect=OSError)
            query = mock.Mock(return_value=(os.path.join(temp, registry) if registry else "", 1))
            with mock.patch.dict(os.environ, {"LOCALAPPDATA": os.path.join(temp, "LocalAppData")}), \
                 mock.patch.object(builder, "__file__", os.path.join(script_dir, "DPMShortcutBuilder.pyw")), \
                 mock.patch.object(builder.winreg, "OpenKey", open_key), \
                 mock.patch.object(builder.winreg, "QueryValueEx", query):
                found = builder.find_dpm_exe()
            return None if found is None else os.path.relpath(found, temp)

    def test_installer_default_spaced_path_resolves(self):
        self.assertEqual(INSTALLER_DEFAULT_EXE, self.resolve([INSTALLER_DEFAULT_EXE]))

    def test_installer_default_is_preferred_over_legacy_layouts(self):
        self.assertEqual(INSTALLER_DEFAULT_EXE, self.resolve([LEGACY_PROGRAMS_EXE, LEGACY_LOCAL_EXE, INSTALLER_DEFAULT_EXE]))

    def test_legacy_unspaced_programs_path_still_resolves(self):
        self.assertEqual(LEGACY_PROGRAMS_EXE, self.resolve([LEGACY_PROGRAMS_EXE]))

    def test_legacy_local_app_data_path_still_resolves(self):
        self.assertEqual(LEGACY_LOCAL_EXE, self.resolve([LEGACY_LOCAL_EXE]))

    def test_sibling_portable_copy_still_resolves(self):
        self.assertEqual(r"Builder\DisplayProfileManager.exe", self.resolve([r"Builder\DisplayProfileManager.exe"]))

    def test_registry_install_path_file_takes_precedence(self):
        custom = r"Custom\DisplayProfileManager.exe"

        self.assertEqual(custom, self.resolve([custom, INSTALLER_DEFAULT_EXE], registry=custom))

    def test_registry_install_path_directory_is_skipped(self):
        self.assertEqual(INSTALLER_DEFAULT_EXE, self.resolve([INSTALLER_DEFAULT_EXE], ["Custom"], registry="Custom"))

    def test_no_candidate_returns_none(self):
        self.assertIsNone(self.resolve())

    def test_generated_find_dpm_exe_checks_installer_default_before_legacy_layouts(self):
        template = builder.PS1_TEMPLATE
        spaced = r"(Join-Path $env:LOCALAPPDATA 'Programs\Display Profile Manager\DisplayProfileManager.exe')"
        unspaced = r"(Join-Path $env:LOCALAPPDATA 'Programs\DisplayProfileManager\DisplayProfileManager.exe')"
        bare = r"(Join-Path $env:LOCALAPPDATA 'DisplayProfileManager\DisplayProfileManager.exe')"

        self.assertIn(r"'C:\Program Files\Display Profile Manager\DisplayProfileManager.exe'", template)
        self.assertIn("(Join-Path $PSScriptRoot 'DisplayProfileManager.exe')", template)
        self.assertLess(template.index(spaced), template.index(unspaced))
        self.assertLess(template.index(unspaced), template.index(bare))
        self.assertIn("if (Test-Path -LiteralPath $c -PathType Leaf) { return $c }", template)
        self.assertNotIn("(Test-Path $c)", template)


VBS_RUN_RE = re.compile(r"^oShell\.Run (?P<expression>.+), 0, True$")
VBS_TOKEN_RE = re.compile(r'\s*(?:"(?P<literal>(?:""|[^"])*)"|ChrW\((?P<unit>\d+)\)|(?P<concat>&))')
ASCII_PS1 = "C:\\Games\\Shortcut\\Shortcut.ps1"
ELYSIUM_PS1 = "C:\\Users\\" + chr(0xC9) + "lysium\\Shortcuts\\O'Brien (1) [x]\\Game.ps1"
CJK_PS1 = "C:\\" + chr(0x7528) + chr(0x6237) + "\\" + chr(0x30B2) + chr(0x30FC) + chr(0x30E0) + "\\" + chr(0x8D77) + chr(0x52D5) + ".ps1"
SUPPLEMENTARY_PS1 = "C:\\Games\\" + chr(0x1F3AE) + " Quest\\Start.ps1"


def evaluate_vbs_string(expression):
    # Accepts only the string literal, ChrW and & forms that build_vbs may emit
    units = []
    position = 0
    expect_operand = True
    while position < len(expression):
        match = VBS_TOKEN_RE.match(expression, position)
        if not match:
            raise ValueError(f"Unexpected VBScript text at {position}: {expression[position:position + 20]!r}")
        if match.group("concat"):
            if expect_operand:
                raise ValueError("Misplaced &")
            expect_operand = True
        else:
            if not expect_operand:
                raise ValueError("Missing &")
            if match.group("unit") is not None:
                unit = int(match.group("unit"))
                if unit > 0xFFFF:
                    raise ValueError("ChrW argument out of range")
                units.append(unit)
            else:
                data = match.group("literal").replace('""', '"').encode("utf-16-le", "surrogatepass")
                units.extend(int.from_bytes(data[index:index + 2], "little") for index in range(0, len(data), 2))
            expect_operand = False
        position = match.end()
    if expect_operand:
        raise ValueError("Incomplete expression")
    return b"".join(unit.to_bytes(2, "little") for unit in units).decode("utf-16-le", "surrogatepass")


def run_command_from_wrapper(source):
    lines = source.replace("\r\n", "\n").split("\n")
    if lines[0] != 'Set oShell = CreateObject("WScript.Shell")' or lines[-1] != "":
        raise ValueError("Unexpected wrapper structure")
    if len(lines) == 3:
        match = VBS_RUN_RE.match(lines[1])
        if not match:
            raise ValueError("Unexpected Run statement")
        return evaluate_vbs_string(match.group("expression"))
    if lines[-2] != 'oShell.Run dpmCommand, 0, True':
        raise ValueError("Unexpected Run statement")
    command = ""
    for index, line in enumerate(lines[1:-2]):
        prefix = "dpmCommand = " if index == 0 else "dpmCommand = dpmCommand & "
        if not line.startswith(prefix):
            raise ValueError("Unexpected or unsafe assignment")
        command += evaluate_vbs_string(line[len(prefix):])
    return command


def windows_argv(command_line):
    # CommandLineToArgvW only tokenizes; nothing is launched
    shell32 = ctypes.WinDLL("shell32")
    kernel32 = ctypes.WinDLL("kernel32")
    shell32.CommandLineToArgvW.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_int)]
    shell32.CommandLineToArgvW.restype = ctypes.POINTER(ctypes.c_wchar_p)
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    count = ctypes.c_int()
    argv = shell32.CommandLineToArgvW(command_line, ctypes.byref(count))
    try:
        return [argv[index] for index in range(count.value)]
    finally:
        kernel32.LocalFree(ctypes.cast(argv, ctypes.c_void_p))


class ShortcutVbsWrapperTests(unittest.TestCase):
    PATHS = (ASCII_PS1, ELYSIUM_PS1, CJK_PS1, SUPPLEMENTARY_PS1)

    def expected_command(self, path):
        return f'"{builder.POWERSHELL_EXE}" -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "{path}"'

    def test_long_unicode_paths_are_bounded_and_preserve_argv(self):
        paths = [
            "C:\\" + ("漢" * 70) + "\\Start.ps1",
            "C:\\" + ("漢" * 100) + "\\Start.ps1",
            "C:\\" + ("A😀漢'" * 70) + "\\Start.ps1",
            "C:\\" + ("測" * 200) + "\\Start.ps1",
        ]
        for path in paths:
            with self.subTest(length=len(path)):
                source = builder.build_vbs(path)
                self.assertTrue(source.isascii())
                self.assertLessEqual(max(map(len, source.splitlines())), 900)
                for cp in ("cp1252", "cp932", "utf-8"):
                    command = run_command_from_wrapper(source.encode("utf-8").decode(cp))
                    self.assertEqual(self.expected_command(path), command)
                    if hasattr(ctypes, "WinDLL"):
                        self.assertEqual(
                            [builder.POWERSHELL_EXE, "-NonInteractive", "-ExecutionPolicy",
                             "Bypass", "-WindowStyle", "Hidden", "-File", path],
                            windows_argv(command),
                        )

    def test_ascii_wrapper_text_is_unchanged(self):
        expected = (
            'Set oShell = CreateObject("WScript.Shell")\n'
            f'oShell.Run """{builder.POWERSHELL_EXE}"" -NonInteractive -ExecutionPolicy Bypass '
            '-WindowStyle Hidden -File ""C:\\Games\\Shortcut\\Shortcut.ps1""", 0, True\n'
        )

        self.assertEqual(expected, builder.build_vbs(ASCII_PS1))

    def test_wrapper_source_is_ascii_for_unicode_paths(self):
        for path in self.PATHS:
            with self.subTest(path=ascii(path)):
                self.assertTrue(builder.build_vbs(path).isascii())

    def test_wrapper_command_round_trips_under_ansi_and_unicode_decoding(self):
        for path in self.PATHS:
            for codepage in ("cp1252", "cp932", "utf-8"):
                with self.subTest(path=ascii(path), codepage=codepage):
                    data = builder.build_vbs(path).encode("utf-8")

                    command = run_command_from_wrapper(data.decode(codepage, errors="replace"))

                    self.assertEqual(self.expected_command(path), command)

    def test_wrapper_command_line_splits_to_expected_arguments(self):
        if not hasattr(ctypes, "WinDLL"):
            self.skipTest("CommandLineToArgvW requires Windows")
        for path in self.PATHS:
            with self.subTest(path=ascii(path)):
                command = run_command_from_wrapper(builder.build_vbs(path))

                self.assertEqual(
                    [builder.POWERSHELL_EXE, "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", path],
                    windows_argv(command),
                )

    def test_lone_surrogate_round_trips_through_chrw(self):
        path = "C:\\Games\\" + chr(0xD800) + "\\Start.ps1"

        source = builder.build_vbs(path)

        self.assertTrue(source.isascii())
        self.assertIn("ChrW(55296)", source)
        self.assertEqual(self.expected_command(path), run_command_from_wrapper(source))

    def test_saved_wrapper_bytes_resolve_unicode_ps1_path_under_ansi_decoding(self):
        with tempfile.TemporaryDirectory() as temp:
            folder = os.path.join(temp, chr(0xC9) + "lysium " + chr(0x8D77) + chr(0x52D5))
            entry = builder._entry_for("Shortcut", folder)
            os.makedirs(entry.folder, exist_ok=True)
            with mock.patch.object(builder, "create_lnk"), \
                 mock.patch.object(builder, "_shortcut_icon_source", return_value=None):
                builder._write_shortcut_artifacts(entry, entry.ps1_path, "# Shortcut\n", "C:\\Games\\game.exe")
            data = Path(entry.folder, "Shortcut.vbs").read_bytes()

        self.assertTrue(data.isascii())
        self.assertEqual(self.expected_command(entry.ps1_path), run_command_from_wrapper(data.decode("cp1252", errors="replace")))


if __name__ == "__main__":
    unittest.main()
