import copy
import importlib.machinery
import importlib.util
import inspect
import os
import pathlib
import re
import sys
import tempfile
import types
import unittest
import xml.etree.ElementTree as ET
from unittest import mock


def _install_headless_tk_stubs():
    """Keep pure Builder tests runnable where Tk is intentionally unavailable."""
    if "tkinter" in sys.modules:
        return

    tkinter = types.ModuleType("tkinter")
    ttk = types.ModuleType("tkinter.ttk")
    filedialog = types.ModuleType("tkinter.filedialog")
    messagebox = types.ModuleType("tkinter.messagebox")

    class _Widget:
        pass

    class _Tk(_Widget):
        pass

    class _Canvas(_Widget):
        pass

    class _Toplevel(_Widget):
        pass

    tkinter.Widget = _Widget
    tkinter.Tk = _Tk
    tkinter.Canvas = _Canvas
    tkinter.Toplevel = _Toplevel
    tkinter.END = "end"
    tkinter.NORMAL = "normal"
    tkinter.DISABLED = "disabled"
    tkinter.SINGLE = "single"
    tkinter.VERTICAL = "vertical"
    tkinter.HORIZONTAL = "horizontal"
    tkinter.LEFT = "left"
    tkinter.StringVar = object
    tkinter.BooleanVar = object
    tkinter.PhotoImage = object
    tkinter.Listbox = object
    tkinter.Text = object
    tkinter.Frame = object
    tkinter.Label = object
    filedialog.asksaveasfilename = lambda **_: ""
    filedialog.askopenfilename = lambda **_: ""
    messagebox.showerror = lambda *_, **__: None

    tkinter.ttk = ttk
    tkinter.filedialog = filedialog
    tkinter.messagebox = messagebox

    sys.modules["tkinter"] = tkinter
    sys.modules["tkinter.ttk"] = ttk
    sys.modules["tkinter.filedialog"] = filedialog
    sys.modules["tkinter.messagebox"] = messagebox


_install_headless_tk_stubs()

MODULE_PATH = pathlib.Path(__file__).resolve().parents[1] / "DPMThemeBuilder.pyw"
LOADER = importlib.machinery.SourceFileLoader("dpm_theme_builder", str(MODULE_PATH))
SPEC = importlib.util.spec_from_loader(LOADER.name, LOADER)
theme_builder = importlib.util.module_from_spec(SPEC)
sys.modules[LOADER.name] = theme_builder
LOADER.exec_module(theme_builder)


BASE16_FIXTURE = """
system: base16
name: Builder Base16
variant: dark
palette:
  base00: "101010"
  base01: "202020"
  base02: "303030"
  base03: "606060"
  base04: "A0A0A0"
  base05: "F0F0F0"
  base06: "F8F8F8"
  base07: "FFFFFF"
  base08: "D04040"
  base09: "D08040"
  base0A: "D0C040"
  base0B: "40B060"
  base0C: "40B0B0"
  base0D: "4080D0"
  base0E: "9050D0"
  base0F: "A06040"
"""

BASE24_FIXTURE = """
system: base24
name: Builder Base24
variant: light
palette:
  base00: "F8F8F8"
  base01: "EEEEEE"
  base02: "DDDDDD"
  base03: "999999"
  base04: "666666"
  base05: "202020"
  base06: "101010"
  base07: "000000"
  base08: "A02020"
  base09: "A06020"
  base0A: "A09020"
  base0B: "208040"
  base0C: "208080"
  base0D: "2050A0"
  base0E: "7040A0"
  base0F: "704020"
  base10: "F0F0F0"
  base11: "E8E8E8"
  base12: "C03030"
  base13: "C09030"
  base14: "30A050"
  base15: "309090"
  base16: "3060B0"
  base17: "8050B0"
"""

EXPECTED_CUSTOM_THEME_KEYS = {
    "BackgroundColor",
    "SurfaceColor",
    "BorderColor",
    "HoverColor",
    "AccentColor",
    "WindowBackgroundBrush",
    "TitleBarBackgroundBrush",
    "AlternateBackgroundBrush",
    "ContentBackgroundBrush",
    "ControlBackgroundBrush",
    "TextBoxBackgroundBrush",
    "ComboBoxBackgroundBrush",
    "ComboBoxDropDownBackgroundBrush",
    "CheckBoxBackgroundBrush",
    "ListItemBackgroundBrush",
    "BorderBrush",
    "SeparatorBrush",
    "ControlBorderBrush",
    "TextBoxBorderBrush",
    "ComboBoxBorderBrush",
    "CheckBoxBorderBrush",
    "WindowControlHoverBrush",
    "ListItemSelectedBackgroundBrush",
    "ListItemSelectedHoverBackgroundBrush",
    "ControlHoverBackgroundBrush",
    "ListItemHoverBackgroundBrush",
    "ComboBoxHoverBackgroundBrush",
    "ControlPressedBackgroundBrush",
    "ButtonBackgroundBrush",
    "ButtonForegroundBrush",
    "ButtonHoverBackgroundBrush",
    "ButtonPressedBackgroundBrush",
    "ButtonBorderBrush",
    "TextBoxFocusBorderBrush",
    "CheckBoxCheckmarkBrush",
    "LinkBrush",
    "SecondaryButtonBackgroundBrush",
    "SecondaryButtonForegroundBrush",
    "SecondaryButtonHoverBackgroundBrush",
    "SecondaryButtonPressedBackgroundBrush",
    "SecondaryButtonBorderBrush",
    "DangerButtonBackgroundBrush",
    "DangerButtonHoverBackgroundBrush",
    "SuccessButtonBackgroundBrush",
    "SuccessButtonHoverBackgroundBrush",
    "TitleBarTextBrush",
    "CloseButtonHoverBrush",
    "PrimaryTextBrush",
    "SecondaryTextBrush",
    "TertiaryTextBrush",
    "TooltipBackgroundBrush",
    "TooltipTextBrush",
    "CardShadow",
    "ButtonShadow",
}


class FakeStatus:
    def __init__(self):
        self.values = []

    def set(self, value):
        self.values.append(value)


class ThemeBuilderConversionTests(unittest.TestCase):
    def test_build_xaml_contains_current_custom_theme_contract(self):
        scheme = theme_builder.parse_yaml(BASE16_FIXTURE)

        xaml = theme_builder.build_xaml(scheme, seamless=True)
        keys = set(re.findall(r'x:Key="([^"]+)"', xaml))

        self.assertEqual(EXPECTED_CUSTOM_THEME_KEYS, keys)
        self.assertNotIn("SystemAccentBrush", keys)

    def test_selected_hover_has_deliberate_distinct_derivation(self):
        scheme = theme_builder.parse_yaml(BASE16_FIXTURE)

        colors = theme_builder.extract_Colors(scheme, seamless=True)

        self.assertNotEqual(colors["hover"], colors["selected_hover"])

    def test_base16_fixture_converts(self):
        scheme = theme_builder.parse_yaml(BASE16_FIXTURE)

        xaml = theme_builder.build_xaml(scheme, seamless=False)

        self.assertIn("Builder Base16", xaml)
        self.assertIn("#4080D0", xaml)
        self.assertIn('x:Key="ListItemSelectedHoverBackgroundBrush"', xaml)

    def test_base24_fixture_uses_bright_status_slots(self):
        scheme = theme_builder.parse_yaml(BASE24_FIXTURE)

        xaml = theme_builder.build_xaml(scheme, seamless=True)

        self.assertIn("Builder Base24", xaml)
        self.assertIn("#C03030", xaml)
        self.assertIn("#30A050", xaml)



class ThemeBuilderXmlMetadataTests(unittest.TestCase):
    def assert_safe_resources(self, scheme):
        baseline = theme_builder.build_xaml(theme_builder.parse_yaml(BASE16_FIXTURE), seamless=True)
        actual = theme_builder.build_xaml(scheme, seamless=True)
        expected = ET.fromstring(baseline)
        parsed = ET.fromstring(actual)
        key = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
        self.assertEqual(len(expected), len(parsed))
        self.assertEqual([child.get(key) for child in expected], [child.get(key) for child in parsed])
        self.assertEqual([ET.tostring(child) for child in expected], [ET.tostring(child) for child in parsed])
        self.assertNotIn("Injected", actual.split('    <!-- Base Colors -->')[1])
        return actual

    def test_literal_hyphens_are_safe_with_both_yaml_parsers(self):
        text = BASE16_FIXTURE.replace("Builder Base16", "Ocean--Breeze").replace(
            "variant: dark", "author: A---B\nvariant: dark--night")
        for parser in (theme_builder.parse_yaml, theme_builder._fallback_parse):
            with self.subTest(parser=parser.__name__):
                scheme = parser(text)
                scheme["system"] = "base--16"
                actual = self.assert_safe_resources(scheme)
                self.assertNotIn("--", actual.split("<!-- ", 1)[1].split(" -->", 1)[0])

    def test_comment_breakout_cannot_inject_resources(self):
        scheme = theme_builder.parse_yaml(BASE16_FIXTURE)
        scheme["name"] = 'Theme --> <Color x:Key="Injected">#010203</Color> <!--'
        actual = self.assert_safe_resources(scheme)
        self.assertIn('x:Key="Injected"', actual)

    def test_invalid_scalars_and_benign_metadata(self):
        scheme = theme_builder.parse_yaml(BASE16_FIXTURE)
        cases = ["A\u0000B", "A\u0001B", "A\ud800B", "A\ufffeB", "A\uffffB",
                 "A<B & Ω 🎨", "ending-", "", "---", "----"]
        for field in ("name", "author", "system", "variant"):
            for value in cases:
                with self.subTest(field=field, value=ascii(value)):
                    candidate = dict(scheme)
                    candidate[field] = value
                    self.assert_safe_resources(candidate)

        for field in ("name", "author", "system", "variant"):
            candidate = dict(scheme)
            candidate.pop(field, None)
            self.assert_safe_resources(candidate)

    def test_pyyaml_decoded_control_remains_valid_xml(self):
        if __import__("importlib").util.find_spec("yaml") is None:
            self.skipTest("PyYAML not installed")
        scheme = theme_builder.parse_yaml(BASE16_FIXTURE.replace(
            "name: Builder Base16", r'name: "A\u0001B"'))
        self.assertIn("\u0001", scheme["name"])
        self.assert_safe_resources(scheme)

    def test_refresh_and_save_keep_default_filename_and_safe_resource_authority(self):
        scheme = theme_builder.parse_yaml(BASE16_FIXTURE.replace("Builder Base16", "Ocean--Breeze"))
        app = type("AppHarness", (), {})()
        app._seamless = type("Value", (), {"get": lambda self: True})()
        app._status = FakeStatus()
        app._btn_save = mock.Mock()
        app._set_xaml_text = mock.Mock()
        app._redraw_preview = mock.Mock()
        app._apply_to_dpm = mock.Mock()
        theme_builder.App._refresh_output(app, scheme, "Converted")
        self.assertTrue(app._current_scheme_authoritative)
        self.assert_safe_resources(scheme)
        ET.fromstring(app._current_xaml)
        app._btn_save.configure.assert_called_with(state=theme_builder.tk.NORMAL)
        with mock.patch.object(theme_builder.filedialog, "asksaveasfilename",
                               return_value="") as dialog, \
             mock.patch.object(theme_builder.os.path, "isdir", return_value=False):
            theme_builder.App._save(app)
        self.assertEqual("Ocean- -Breeze.xaml", dialog.call_args.kwargs["initialfile"])
        app._apply_to_dpm.assert_not_called()


class ThemeBuilderRequestOwnershipTests(unittest.TestCase):
    def make_selection_app(self):
        app = type("SelectionApp", (), {})()
        app._selection_requests = theme_builder.RequestGate()
        app._status = FakeStatus()
        app.refreshed = []
        app._refresh_output = lambda scheme, label: app.refreshed.append((scheme, label))
        return app

    def test_selection_start_preserves_preview_while_invalidating_output_authority(self):
        previous_scheme = {"name": "Previous"}
        next_meta = {
            "name": "Next",
            "system": "base16",
            "slug": "next",
            "path": "base16/next.yaml",
        }
        app = type("SelectionApp", (), {})()
        app._selection_requests = theme_builder.RequestGate()
        app._listbox = mock.Mock()
        app._listbox.curselection.return_value = (0,)
        app._display_keys = ["base16/next"]
        app._schemes = {"base16/next": next_meta}
        app._token = mock.Mock()
        app._token.get.return_value = "token"
        app._current_scheme = previous_scheme
        app._current_scheme_authoritative = True
        app._current_xaml = "<ResourceDictionary/>"
        app._btn_save = mock.Mock()
        app._set_xaml_text = mock.Mock()
        app._canvas = mock.Mock()
        app._status = FakeStatus()
        app._fetch_and_convert = mock.Mock()
        app._redraw_preview = mock.Mock()
        app._refresh_output = mock.Mock()
        worker = mock.Mock()

        with mock.patch.object(
            theme_builder.threading, "Thread", return_value=worker
        ) as thread:
            theme_builder.App._on_select(app)

        request = (1, "base16/next")
        self.assertIs(previous_scheme, app._current_scheme)
        self.assertFalse(app._current_scheme_authoritative)
        self.assertEqual("", app._current_xaml)
        app._btn_save.configure.assert_called_once_with(
            state=theme_builder.tk.DISABLED
        )
        app._set_xaml_text.assert_called_once_with("")
        app._canvas.delete.assert_not_called()
        self.assertEqual(
            ["Fetching base16/next.yaml…"],
            app._status.values,
        )
        self.assertTrue(app._selection_requests.owns(request))
        thread.assert_called_once_with(
            target=app._fetch_and_convert,
            args=(next_meta, "token", request),
            daemon=True,
        )
        worker.start.assert_called_once_with()

        theme_builder.App._on_canvas_resize(app)
        theme_builder.App._on_seamless_change(app)

        self.assertEqual(2, app._redraw_preview.call_count)
        app._refresh_output.assert_not_called()

    def test_stale_selection_completion_cannot_supersede_current_selection(self):
        app = self.make_selection_app()
        request_a = app._selection_requests.begin("base16/a")
        request_b = app._selection_requests.begin("base16/b")

        theme_builder.App._complete_selection(app, request_b, {"name": "B"}, "B")
        theme_builder.App._complete_selection(app, request_a, {"name": "A"}, "A")

        self.assertEqual([({"name": "B"}, "B")], app.refreshed)

    def test_stale_selection_error_cannot_replace_current_status(self):
        app = self.make_selection_app()
        request_a = app._selection_requests.begin("base16/a")
        app._selection_requests.begin("base16/b")

        theme_builder.App._selection_error(app, request_a, "Error: stale A")

        self.assertEqual([], app._status.values)

    def test_stale_reload_cannot_replace_newer_scheme_list(self):
        app = type("ReloadApp", (), {})()
        app._list_requests = theme_builder.RequestGate()
        app._status = FakeStatus()
        app._schemes = {}
        app.filter_count = 0
        app._filter_list = lambda: setattr(app, "filter_count", app.filter_count + 1)
        request_a = app._list_requests.begin()
        request_b = app._list_requests.begin()
        old_schemes = {"base16/a": {"slug": "a"}}
        new_schemes = {"base16/b": {"slug": "b"}}

        theme_builder.App._populate_list(app, request_a, old_schemes, [])
        theme_builder.App._populate_list(app, request_b, new_schemes, [])

        self.assertEqual(new_schemes, app._schemes)
        self.assertEqual(1, app.filter_count)


class ThemeBuilderMinimumLayoutTests(unittest.TestCase):
    def test_minimum_layout_reserves_more_expandable_space_for_preview(self):
        self.assertEqual(8, theme_builder.SCHEME_LIST_VISIBLE_ROWS)
        self.assertGreater(
            theme_builder.PREVIEW_GRID_WEIGHT,
            theme_builder.SCHEME_GRID_WEIGHT,
        )

class RecordingCanvas:
    def __init__(self, width=760, height=360):
        self.width = width
        self.height = height
        self.calls = []

    def delete(self, *args):
        self.calls.append(("delete", args, {}))

    def winfo_width(self):
        return self.width

    def winfo_height(self):
        return self.height

    def _record(self, name, *args, **kwargs):
        self.calls.append((name, args, kwargs))
        return len(self.calls)

    def create_rectangle(self, *args, **kwargs):
        return self._record("rectangle", *args, **kwargs)

    def create_oval(self, *args, **kwargs):
        return self._record("oval", *args, **kwargs)

    def create_line(self, *args, **kwargs):
        return self._record("line", *args, **kwargs)

    def create_text(self, *args, **kwargs):
        return self._record("text", *args, **kwargs)

    def create_image(self, *args, **kwargs):
        return self._record("image", *args, **kwargs)

    def scale(self, *args, **kwargs):
        raise AssertionError("draw_preview must not globally scale the live canvas scene")

    def move(self, *args, **kwargs):
        raise AssertionError("draw_preview must not letterbox/move a virtual scene")


class ThemeBuilderPreviewContractTests(unittest.TestCase):
    def test_preview_geometry_matches_current_wpf_authority(self):
        geometry = theme_builder._preview_geometry(760, 360)
        main_xaml = (
            MODULE_PATH.parents[1]
            / "DisplayProfileManager"
            / "UI"
            / "Windows"
            / "MainWindow.xaml"
        ).read_text(encoding="utf-8")

        self.assertEqual(760, geometry["width"])
        self.assertEqual(360, geometry["height"])
        self.assertEqual(16, geometry["margin"])
        self.assertEqual(16, geometry["gap"])
        self.assertEqual(326, geometry["details_w"])
        self.assertEqual(16, geometry["content_x1"])
        self.assertEqual(744, geometry["content_x2"])
        self.assertEqual(
            geometry["details_x1"] - 16,
            geometry["profiles_x2"],
        )
        self.assertEqual(312, geometry["status_y1"])
        self.assertIn('<ColumnDefinition Width="16"/>', main_xaml)
        self.assertIn('<ColumnDefinition Width="326"/>', main_xaml)
        self.assertIn('<ColumnDefinition Width="44"/>', main_xaml)
        self.assertIn('x:Name="ProfileLayoutImage"', main_xaml)
        self.assertIn('Width="32"', main_xaml)
        self.assertIn('Height="32"', main_xaml)

    def test_preview_draws_root_title_and_status_to_live_viewport_extents(self):
        scheme = theme_builder.parse_yaml(BASE16_FIXTURE)
        colors = theme_builder.extract_Colors(scheme, seamless=True)
        canvas = RecordingCanvas(width=760, height=360)

        theme_builder.draw_preview(canvas, colors, None)

        tagged_rectangles = {}
        for name, args, kwargs in canvas.calls:
            if name != "rectangle":
                continue
            for tag in kwargs.get("tags", ()):
                tagged_rectangles[tag] = args[:4]

        self.assertEqual((0, 0, 760, 360), tagged_rectangles["root"])
        self.assertEqual((0, 0, 760, 32), tagged_rectangles["title"])
        self.assertEqual((0, 312, 760, 360), tagged_rectangles["status"])

    def test_desk_saved_layout_is_two_equal_horizontal_1440p_footprints(self):
        fixture = theme_builder.DESK_SAVED_LAYOUT

        self.assertEqual(2, len(fixture))
        first, second = fixture
        self.assertEqual((2560, 1440), (first["width"], first["height"]))
        self.assertEqual((2560, 1440), (second["width"], second["height"]))
        self.assertEqual(first["y"], second["y"])
        self.assertEqual(first["x"] + first["width"], second["x"])

        projected = theme_builder._project_saved_layout(fixture, 294, 168, 12)
        self.assertEqual(2, len(projected))
        self.assertAlmostEqual(projected[0]["width"], projected[1]["width"])
        self.assertAlmostEqual(projected[0]["height"], projected[1]["height"])
        self.assertAlmostEqual(projected[0]["y"], projected[1]["y"])
        self.assertAlmostEqual(
            projected[0]["x"] + projected[0]["width"],
            projected[1]["x"],
        )

    def test_projector_preserves_nontrivial_relative_geometry_and_inset_bounds(self):
        fixture = (
            {"name": "Left", "x": -320, "y": 180, "width": 1920, "height": 1080},
            {"name": "Right", "x": 1760, "y": -120, "width": 1440, "height": 2560},
        )
        projected = theme_builder._project_saved_layout(fixture, 300, 168, 12)

        self.assertEqual(2, len(projected))
        first, second = projected
        self.assertLess(first["x"], second["x"])
        self.assertGreater(first["y"], second["y"])
        self.assertAlmostEqual(
            first["width"] / first["height"],
            fixture[0]["width"] / fixture[0]["height"],
            places=6,
        )
        self.assertAlmostEqual(
            second["width"] / second["height"],
            fixture[1]["width"] / fixture[1]["height"],
            places=6,
        )
        self.assertGreaterEqual(min(item["x"] for item in projected), 12)
        self.assertGreaterEqual(min(item["y"] for item in projected), 12)
        self.assertLessEqual(
            max(item["x"] + item["width"] for item in projected),
            288,
        )
        self.assertLessEqual(
            max(item["y"] + item["height"] for item in projected),
            156,
        )

    def test_tv_saved_layout_is_one_living_room_tv_4k_footprint(self):
        fixture = theme_builder.TV_SAVED_LAYOUT

        self.assertEqual(
            (
                {"name": "Living Room TV", "x": 0, "y": 0,
                 "width": 3840, "height": 2160},
            ),
            fixture,
        )

    def test_thumbnail_and_details_share_desk_projection_semantics(self):
        fixture = theme_builder.DESK_SAVED_LAYOUT
        thumbnail = theme_builder._project_saved_layout(
            fixture, 32, 32, max(1.0, 32 * 0.08)
        )
        details = theme_builder._project_saved_layout(fixture, 293, 168, 12)

        self.assertEqual(
            [item["name"] for item in thumbnail],
            [item["name"] for item in details],
        )
        for projection in (thumbnail, details):
            self.assertEqual(2, len(projection))
            self.assertAlmostEqual(
                projection[0]["width"] / projection[0]["height"],
                2560 / 1440,
                places=6,
            )
            self.assertAlmostEqual(
                projection[1]["width"] / projection[1]["height"],
                2560 / 1440,
                places=6,
            )

    def test_preview_uses_canonical_profile_vocabulary_and_current_row_contract(self):
        source = MODULE_PATH.read_text(encoding="utf-8")
        preview = source[
            source.index("def draw_preview"):
            source.index("# Async request ownership")
        ]

        self.assertIn('"Desk Setup"', preview)
        self.assertIn('"TV Setup"', preview)
        self.assertIn('"Dual-monitor layout for everyday desktop use."', preview)
        self.assertIn('"4K HDR layout for games and media."', preview)
        self.assertIn('"2 displays"', preview)
        self.assertIn('"1 display"', preview)
        self.assertIn('default_badge=True', preview)
        self.assertIn('active_badge=True', preview)
        self.assertIn('selected=True', preview)
        self.assertIn('hotkey_text="Ctrl + Alt + T"', preview)
        self.assertNotIn('hotkey_text="Ctrl+Alt+T"', preview)
        self.assertIn('text="Default"', preview)
        self.assertIn('text="Active"', preview)
        self.assertIn("DESK_SAVED_LAYOUT", preview)
        self.assertIn("TV_SAVED_LAYOUT", preview)
        self.assertNotIn("Work setup", source)
        self.assertNotIn("Presentation", preview)
        self.assertNotIn("Generic Display", preview)
        self.assertNotIn("Primary desk layout and devices", preview)
        self.assertIn("row_pad_x = 12", preview)
        self.assertIn("row_pad_y = 8", preview)
        self.assertIn("row_h = 76", preview)
        self.assertIn("visual_size = 32", preview)
        self.assertIn("visual_column = 44", preview)
        self.assertIn("apply_size = 32", preview)
        self.assertIn("selected_hover", preview)
        self.assertIn("show_apply=True", preview)

        scheme = theme_builder.parse_yaml(BASE16_FIXTURE)
        colors = theme_builder.extract_Colors(scheme, seamless=True)
        canvas = RecordingCanvas(width=1000, height=800)
        theme_builder.draw_preview(canvas, colors, None)
        texts = [
            kwargs.get("text")
            for name, _, kwargs in canvas.calls
            if name == "text" and kwargs.get("text")
        ]
        self.assertEqual(1, texts.count("Default"))
        self.assertEqual(1, texts.count("Active"))
        self.assertEqual(1, texts.count("Dual-monitor layout for everyday desktop use."))
        self.assertEqual(2, texts.count("4K HDR layout for games and media."))
        self.assertIn("Ctrl + Alt + T", texts)

        desk_name = texts.index("Desk Setup")
        desk_description = texts.index("Dual-monitor layout for everyday desktop use.")
        desk_metadata = texts.index("2 displays")
        tv_name = texts.index("TV Setup")
        tv_description = texts.index("4K HDR layout for games and media.")
        tv_metadata = texts.index("1 display")
        self.assertLess(desk_name, desk_description)
        self.assertLess(desk_description, desk_metadata)
        self.assertLess(tv_name, tv_description)
        self.assertLess(tv_description, tv_metadata)

    def test_preview_tracks_current_details_order_and_tv_display_facts(self):
        source = MODULE_PATH.read_text(encoding="utf-8")
        preview = source[
            source.index("def draw_preview"):
            source.index("# Async request ownership")
        ]

        self.assertIn("representative approximation", source)
        self.assertNotIn("canvas.scale(", preview)
        self.assertNotIn("canvas.move(", preview)
        self.assertNotIn("ref_w = 900", preview)
        self.assertNotIn("ref_h = 600", preview)
        self.assertIn("details_inner_left = 16", preview)
        self.assertIn("details_inner_right = 17", preview)
        self.assertIn("saved_layout_y2 = saved_layout_y1 + 168", preview)
        self.assertIn("layout_height,\n        12,", preview)
        self.assertIn("TV_SAVED_LAYOUT", preview)
        self.assertIn('"4K HDR layout for games and media."', preview)
        self.assertIn('"Living Room TV"', preview)
        self.assertIn('"3840x2160 • 120Hz"', preview)
        self.assertIn('"HDR: On"', preview)
        self.assertIn("card_h = 116 if hdr and primary else 112 if primary else 94", preview)
        self.assertIn('"150%"', preview)
        self.assertIn('"Primary Display"', preview)
        self.assertIn('"Output: TV Speakers"', preview)
        self.assertIn('text="Hotkey:"', preview)
        self.assertIn('text="Ctrl + Alt + T"', preview)
        self.assertIn('text="Enabled"', preview)
        self.assertIn('"Created: Oct 1, 2026 at 12:00 AM"', preview)
        self.assertIn('"Last Modified: Oct 1, 2026 at 12:00 AM"', preview)
        self.assertNotIn('"Mode: Picture"', preview)
        self.assertNotIn('"Fitment: Fill"', preview)
        self.assertNotIn('"Output: Headphones"', preview)
        self.assertNotIn('"Input: Microphone"', preview)
        self.assertNotIn("monitor_thumb", preview)
        self.assertNotIn("stand", preview.lower())
        self.assertNotIn("bezel", preview.lower())

        details_name = preview.index('text="TV Setup"')
        details_description = preview.index('text="4K HDR layout for games and media."', details_name)
        displays = preview.index('text="Displays"')
        saved = preview.index("saved_layout_y1 = y")
        display_card = preview.index('"Living Room TV"')
        audio = preview.index('text="Audio"')
        output = preview.index('text="Output: TV Speakers"')
        hotkey = preview.index('text="Hotkey:"')
        key = preview.index('text="Ctrl + Alt + T"', hotkey)
        enabled = preview.index('text="Enabled"')
        created = preview.index('text="Created: Oct 1, 2026 at 12:00 AM"')
        modified = preview.index('text="Last Modified: Oct 1, 2026 at 12:00 AM"')
        self.assertLess(details_name, details_description)
        self.assertLess(details_description, displays)
        self.assertLess(displays, saved)
        self.assertLess(saved, display_card)
        self.assertLess(display_card, audio)
        self.assertLess(audio, output)
        self.assertLess(output, hotkey)
        self.assertLess(hotkey, key)
        self.assertLess(key, enabled)
        self.assertLess(enabled, created)
        self.assertLess(created, modified)

    def test_lower_details_sections_render_as_a_monotonic_prefix(self):
        scheme = theme_builder.parse_yaml(BASE16_FIXTURE)
        colors = theme_builder.extract_Colors(scheme, seamless=True)

        audio_labels = {"Audio", "Output: TV Speakers"}
        hotkey_labels = {"Hotkey:", "Ctrl + Alt + T", "Enabled"}
        meta_labels = {
            "Created: Oct 1, 2026 at 12:00 AM",
            "Last Modified: Oct 1, 2026 at 12:00 AM",
        }

        def rendered_text(height):
            canvas = RecordingCanvas(width=1000, height=height)
            theme_builder.draw_preview(canvas, colors, None)
            return {
                kwargs.get("text")
                for name, _, kwargs in canvas.calls
                if name == "text" and kwargs.get("text")
            }

        previous_stage = 0
        for height in range(1, 1201):
            texts = rendered_text(height)
            hotkey_any = "Hotkey:" in texts or "Enabled" in texts
            meta_any = bool(texts & meta_labels)

            if hotkey_any:
                self.assertTrue(
                    audio_labels <= texts,
                    f"hotkey rendered before complete audio block at height {height}",
                )
            if meta_any:
                self.assertTrue(
                    audio_labels <= texts and hotkey_labels <= texts,
                    f"metadata rendered before complete preceding blocks at height {height}",
                )

            stage = (
                3 if meta_labels <= texts
                else 2 if hotkey_labels <= texts
                else 1 if audio_labels <= texts
                else 0
            )
            self.assertGreaterEqual(
                stage,
                previous_stage,
                f"lower Details prefix regressed at height {height}",
            )
            previous_stage = stage

        self.assertEqual(3, previous_stage)

    def test_preview_preserves_current_actions_and_no_invented_layout_legend(self):
        source = MODULE_PATH.read_text(encoding="utf-8")
        preview = source[
            source.index("def draw_preview"):
            source.index("# Async request ownership")
        ]

        self.assertIn('text="Profiles"', preview)
        self.assertIn('"Duplicate"', preview)
        self.assertIn('"Create"', preview)
        self.assertIn('"Edit"', preview)
        self.assertIn('"Delete"', preview)
        self.assertNotIn('"Import"', preview)
        self.assertIn('details_border = co["comment"]', preview)
        self.assertNotIn("Layout:", preview)
        self.assertNotIn("resolution legend", preview.lower())


class ThemeBuilderSaveTests(unittest.TestCase):
    def test_save_error_does_not_report_success_or_apply(self):
        app = type("SaveApp", (), {})()
        app._current_xaml = "<!-- Example — Author -->\n<ResourceDictionary/>"
        app.applied = []
        app._apply_to_dpm = app.applied.append

        with mock.patch.object(theme_builder.filedialog, "asksaveasfilename",
                               return_value=r"C:\\blocked\\Example.xaml"), \
             mock.patch.object(theme_builder.os.path, "isdir", return_value=False), \
             mock.patch.object(theme_builder, "write_theme_file",
                               side_effect=OSError("access denied")), \
             mock.patch.object(theme_builder.messagebox, "showerror") as showerror:
            theme_builder.App._save(app)

        self.assertEqual([], app.applied)
        showerror.assert_called_once()
        self.assertEqual("Save failed", showerror.call_args.args[0])


CONSUMED_PALETTE_SLOTS = (
    "base00", "base01", "base02", "base03", "base04", "base05",
    "base08", "base0B", "base0C", "base0D", "base0E", "base12", "base14",
)
ATTRIBUTE_BREAKOUT = 'AABBCC" audit-flag="SAFE'
MALFORMED_COLORS = [
    ATTRIBUTE_BREAKOUT, "NOTHEX", "ABC", "ABCD", "ABCDE", "ABCDEF0", "", "#", "<x/>",
    "AA BBCC", "\uff21\uff22\uff23\uff24\uff25\uff26", True, -1, 1234567,
]
INSTALLER_DEFAULT_EXE = r"LocalAppData\Programs\Display Profile Manager\DisplayProfileManager.exe"
LEGACY_PROGRAMS_EXE = r"LocalAppData\Programs\DisplayProfileManager\DisplayProfileManager.exe"
ALL_USERS_EXE = r"ProgramFiles\Display Profile Manager\DisplayProfileManager.exe"


def yaml_stub(data):
    module = types.ModuleType("yaml")
    module.safe_load = lambda _text: copy.deepcopy(data)
    return module


def with_slot_line(text, slot, value):
    return re.sub(rf"(?m)^(  {slot}: ).*$", lambda match: match.group(1) + value, text)


class ThemeBuilderPaletteValidationTests(unittest.TestCase):
    def valid_scheme(self):
        return theme_builder._fallback_parse(BASE24_FIXTURE)

    def test_norm_accepts_six_digit_rgb_forms(self):
        cases = {"#a1b2c3": "#A1B2C3", "A1B2C3": "#A1B2C3", "  #A1B2C3 ": "#A1B2C3", 101010: "#101010"}
        for value, expected in cases.items():
            with self.subTest(value=ascii(value)):
                self.assertEqual(expected, theme_builder.norm(value))

    def test_norm_rejects_malformed_colors(self):
        for value in MALFORMED_COLORS:
            with self.subTest(value=ascii(value)):
                with self.assertRaises(ValueError):
                    theme_builder.norm(value)

    def test_pyyaml_path_rejects_attribute_breakout_in_every_consumed_slot(self):
        for slot in CONSUMED_PALETTE_SLOTS:
            with self.subTest(slot=slot):
                data = self.valid_scheme()
                data["palette"][slot] = ATTRIBUTE_BREAKOUT

                with mock.patch.dict(sys.modules, {"yaml": yaml_stub(data)}):
                    with self.assertRaisesRegex(ValueError, slot):
                        theme_builder.parse_yaml("stubbed")

    def test_pyyaml_path_rejects_malformed_base05_values(self):
        for value in MALFORMED_COLORS:
            with self.subTest(value=ascii(value)):
                data = self.valid_scheme()
                data["palette"]["base05"] = value

                with mock.patch.dict(sys.modules, {"yaml": yaml_stub(data)}):
                    with self.assertRaisesRegex(ValueError, "base05"):
                        theme_builder.parse_yaml("stubbed")

    def test_fallback_path_rejects_malformed_value_in_every_consumed_slot(self):
        for value in ("'" + ATTRIBUTE_BREAKOUT + "'", '"ABCD"', "NOTHEX", '"ABC"'):
            for slot in CONSUMED_PALETTE_SLOTS:
                with self.subTest(slot=slot, value=value):
                    text = with_slot_line(BASE24_FIXTURE, slot, value)

                    with mock.patch.dict(sys.modules, {"yaml": None}):
                        with self.assertRaisesRegex(ValueError, slot):
                            theme_builder.parse_yaml(text)

    def test_pyyaml_legacy_top_level_palette_rejects_breakout(self):
        data = {"scheme": "Legacy", "base00": "101010", "base05": ATTRIBUTE_BREAKOUT, "base0D": "4080D0"}

        with mock.patch.dict(sys.modules, {"yaml": yaml_stub(data)}):
            with self.assertRaisesRegex(ValueError, "base05"):
                theme_builder.parse_yaml("stubbed")

    def test_build_xaml_rejects_unvalidated_palette_values(self):
        for slot in CONSUMED_PALETTE_SLOTS:
            with self.subTest(slot=slot):
                scheme = self.valid_scheme()
                scheme["palette"][slot] = ATTRIBUTE_BREAKOUT

                with self.assertRaises(ValueError):
                    theme_builder.build_xaml(scheme, seamless=True)

    def test_unconsumed_slot_values_do_not_block_conversion(self):
        data = self.valid_scheme()
        data["palette"]["base06"] = "not-a-color"

        with mock.patch.dict(sys.modules, {"yaml": yaml_stub(data)}):
            scheme = theme_builder.parse_yaml("stubbed")

        self.assertNotIn("not-a-color", theme_builder.build_xaml(scheme, seamless=True))

    def test_valid_schemes_convert_identically_with_both_parsers(self):
        if importlib.util.find_spec("yaml") is None:
            self.skipTest("PyYAML not installed")
        hashed = with_slot_line(BASE16_FIXTURE, "base0D", '"#4080d0"')
        for fixture in (BASE16_FIXTURE, BASE24_FIXTURE, hashed):
            with self.subTest(fixture=fixture.split("name: ")[1].split("\n")[0]):
                with mock.patch.dict(sys.modules, {"yaml": None}):
                    fallback = theme_builder.build_xaml(theme_builder.parse_yaml(fixture), seamless=True)
                pyyaml = theme_builder.build_xaml(theme_builder.parse_yaml(fixture), seamless=True)

                self.assertEqual(fallback, pyyaml)
        self.assertIn("#4080D0", theme_builder.build_xaml(theme_builder.parse_yaml(hashed), seamless=True))

    def test_generated_theme_attributes_are_only_keys_and_color_literals(self):
        xaml = theme_builder.build_xaml(theme_builder.parse_yaml(BASE24_FIXTURE), seamless=False)
        key = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
        allowed = {
            "Color": {key},
            "SolidColorBrush": {key, "Color"},
            "DropShadowEffect": {key, "ShadowDepth", "Direction", "BlurRadius", "Opacity", "Color"},
        }

        for element in ET.fromstring(xaml):
            tag = element.tag.split("}")[1]
            with self.subTest(key=element.get(key)):
                self.assertEqual(allowed[tag] & set(element.attrib), set(element.attrib))
                color = element.text if tag == "Color" else element.get("Color")
                self.assertRegex(color, r"^(#[0-9A-F]{6}|\{StaticResource [A-Za-z]+\})$")


class ThemeBuilderDpmExeResolutionTests(unittest.TestCase):
    def resolve(self, files=(), registry=None):
        with tempfile.TemporaryDirectory() as temp:
            script_dir = os.path.join(temp, "Builder")
            os.makedirs(script_dir)
            for relative in files:
                path = os.path.join(temp, relative)
                os.makedirs(os.path.dirname(path), exist_ok=True)
                pathlib.Path(path).write_text("placeholder", encoding="utf-8")
            environment = {"LOCALAPPDATA": os.path.join(temp, "LocalAppData"), "PROGRAMFILES": os.path.join(temp, "ProgramFiles")}
            open_key = mock.Mock() if registry else mock.Mock(side_effect=OSError)
            query = mock.Mock(return_value=(os.path.join(temp, registry) if registry else "", 1))
            with mock.patch.dict(os.environ, environment), \
                 mock.patch.object(theme_builder, "__file__", os.path.join(script_dir, "DPMThemeBuilder.pyw")), \
                 mock.patch.object(theme_builder.winreg, "OpenKey", open_key), \
                 mock.patch.object(theme_builder.winreg, "QueryValueEx", query):
                found = theme_builder.App._find_dpm_exe(None)
            return None if found is None else os.path.relpath(found, temp)

    def test_installer_default_spaced_path_resolves(self):
        self.assertEqual(INSTALLER_DEFAULT_EXE, self.resolve([INSTALLER_DEFAULT_EXE]))

    def test_installer_default_is_preferred_over_legacy_unspaced_path(self):
        self.assertEqual(INSTALLER_DEFAULT_EXE, self.resolve([LEGACY_PROGRAMS_EXE, INSTALLER_DEFAULT_EXE]))

    def test_legacy_unspaced_path_still_resolves(self):
        self.assertEqual(LEGACY_PROGRAMS_EXE, self.resolve([LEGACY_PROGRAMS_EXE]))

    def test_all_users_install_still_takes_precedence(self):
        self.assertEqual(ALL_USERS_EXE, self.resolve([INSTALLER_DEFAULT_EXE, ALL_USERS_EXE]))

    def test_sibling_portable_copy_still_resolves(self):
        self.assertEqual(r"Builder\DisplayProfileManager.exe", self.resolve([r"Builder\DisplayProfileManager.exe"]))

    def test_registry_install_path_file_takes_precedence(self):
        custom = r"Custom\DisplayProfileManager.exe"

        self.assertEqual(custom, self.resolve([custom, ALL_USERS_EXE], registry=custom))

    def test_no_candidate_returns_none(self):
        self.assertIsNone(self.resolve())


class ThemeBuilderWatcherTests(unittest.TestCase):
    def make_app(self):
        app = type("WatcherApp", (), {})()
        app._builder_theme_writes = {}
        app._status = FakeStatus()
        app._apply_to_dpm = mock.Mock()
        app._current_xaml = theme_builder.build_xaml(theme_builder.parse_yaml(BASE16_FIXTURE), seamless=True)
        return app

    def save(self, app, path):
        with mock.patch.object(theme_builder.filedialog, "asksaveasfilename", return_value=path), \
             mock.patch.object(theme_builder.os.path, "isdir", return_value=False):
            theme_builder.App._save(app)

    def test_externally_dropped_theme_is_reported_without_apply(self):
        app = self.make_app()
        with tempfile.TemporaryDirectory() as temp:
            path = os.path.join(temp, "Theme.xaml")
            pathlib.Path(path).write_text(app._current_xaml, encoding="utf-8")

            theme_builder.App._on_theme_file_changed(app, path)

        app._apply_to_dpm.assert_not_called()
        self.assertIn("not applied", app._status.values[-1])

    def test_builder_saved_theme_change_is_applied(self):
        app = self.make_app()
        with tempfile.TemporaryDirectory() as temp:
            path = os.path.join(temp, "Theme.xaml")
            self.save(app, path)

            theme_builder.App._on_theme_file_changed(app, path)

        self.assertEqual([mock.call(path), mock.call(path)], app._apply_to_dpm.call_args_list)

    def test_builder_saved_theme_replaced_externally_is_not_applied(self):
        app = self.make_app()
        with tempfile.TemporaryDirectory() as temp:
            path = os.path.join(temp, "Theme.xaml")
            self.save(app, path)
            pathlib.Path(path).write_text(app._current_xaml.replace("#4080D0", "#010203"), encoding="utf-8")

            theme_builder.App._on_theme_file_changed(app, path)

        self.assertEqual([mock.call(path)], app._apply_to_dpm.call_args_list)
        self.assertIn("not applied", app._status.values[-1])

    def test_identical_content_at_another_path_is_not_applied(self):
        app = self.make_app()
        with tempfile.TemporaryDirectory() as temp:
            saved = os.path.join(temp, "Theme.xaml")
            other = os.path.join(temp, "Theme A.xaml")
            self.save(app, saved)
            pathlib.Path(other).write_bytes(pathlib.Path(saved).read_bytes())

            theme_builder.App._on_theme_file_changed(app, other)

        self.assertEqual([mock.call(saved)], app._apply_to_dpm.call_args_list)

    def test_folder_monitor_routes_events_through_builder_write_gate(self):
        source = inspect.getsource(theme_builder.App._folder_monitor_loop)

        self.assertIn("self._on_theme_file_changed", source)
        self.assertNotIn("self._apply_to_dpm", source)


if __name__ == "__main__":
    unittest.main()
