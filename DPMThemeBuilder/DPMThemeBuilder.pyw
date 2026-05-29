"""
DPM Theme Builder
Converts tinted-theming Base16 / Base24 YAML schemes to the WPF
ResourceDictionary format used by Display Profile Manager.

Requirements: Python 3.8+ with Tkinter (standard on Windows).
  No third-party packages required.
  Optional: pip install pyyaml  — more robust YAML parsing.

Run as .pyw to suppress the console window on Windows.
"""

import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import threading
import urllib.request
import json
import re
import os
import colorsys
import ctypes
import base64
import datetime

# ---------------------------------------------------------------------------
# Embedded icons (base64 PNG — generated from bundled .ico files)
# ---------------------------------------------------------------------------

# DPM application icon — used inside the preview canvas title bar

DPM_ICON_16_B64 = (
    "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAACsElEQVR4nJVTTWhU"
    "ZxQ99373/WTmjcaojRUjdkwXMYgFrSVSGaEQioJC4oSiKEihXairQjelxEIXXSq4"
    "E7pxYesIwkAXrQq2IAVXdhOz0CGGOGJi4iSTGefNe993XcQpEuLCA2dzOfdy4JxL"
    "AFAsFg3WQKlUsqtnquMCwBH95ACA1lrs4ETpxYgfypl6w7Y46qHZRfz+90kqva0R"
    "ABgdHd0XhtF6o+okDGGCjOGluXRp4fG3SX7/8Od8E4d3/ofmS/rsk8rIABpy58KN"
    "gX8HLwwSHTly9PhyvV5qNpsQT8BsIJ6HZLmG6OBpBEPf2HP+eQx//A+mWrtM3DeI"
    "Dc+e3erd8+swAIiq21FbWkS1+jTx/cAAABtBXJ9H/6dtyiI2G8NFaMvDpdl+9zAe"
    "sH2yY+jcg0N3JbE/CIA2EcHzPPZEGABYPKgIklQRcAsfZat43vQwJb28wdW40SZP"
    "1mULpp1uY1UlKKCq/9PaFNIVYebOVUxe/go5fY57cR9qCJGmbadCqM8tTG1PozID"
    "gEJXInpDqAIsCN0yZicnMPT1HK483op1W7qQBuKQCeBAt747MPZK4DpbayTKBl25"
    "HjypNTB18TY2359G995+3rQ7b3vIvwaA2MHBOpumNrWrAGstkril2ayxXS9bdv76"
    "/eTRj7/p0i9/Vr/vLtwDoALrkIsiieMYIh6oY0RXTDExEZGhbgYzG8OMXCLhxMTE"
    "SpEsbCmbye6OMrnNSk6J2EBd1Gg0CyAiMTKTy2UfAKTEnPqez6lNymNjY+3x8XFe"
    "s8pBEPT3btk6CQBO3R8z00+OvavuBICKxSJXKhWOokijTHS22WqdqteX96oqQNAP"
    "P+i96ofez8aYSqVS4Xw+7zqPRquOaaHwxZe+4e7EaV1ViZnY9/2eIJC/yuVytaN7"
    "l6P3xmvsXi9CnHJAPQAAAABJRU5ErkJggg=="
)

# DPM Theme Builder window icon

THEME_ICON_16_B64 = (
    "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAACtklEQVR4nJWTT2hcVRTGf+fe+968"
    "ZN5MykhMkTSGoHYRuzLgH8SxFFKhYME4AUEXunDjLuJCXEwHuhE3iuDGpQspA4oDgiiVulCh+A/E"
    "2IWM2k4STOM0nXEmM/Pm3uNCIjWkC3/Lw/kOH+d8RwAqlYrlEOr1uj9Yq1bVAaFWkwAghwn30Q9e"
    "fIp44nlG3QFTRuDPC3Lqw/qtPQKwsrKylCTplFUNLkko5qzd6oTx64/5V+9eTJa/uXyC728sY6TX"
    "+rG3+G7kufjGm+e+rlfOiTtz5smnd6636/1+Cxc5jLHEkaX9V8YX0yWePTblP1m/l8+35nlk6o/Z"
    "08e2az+N40eF2jL1Gk41zO92brK5uZHFcc4CRNZwvTMgW1oUyUp2d1BCVDnVvxBObrf8yfjow2tr"
    "71zyRl5zwEhEiKLIRM4ZgMhZbOQJYYSMYq525ygO2hzPb5lOljfie1GSy5XHTmadqgoKqoqqApB5"
    "T3Ei5u1Lv/LRd3u0C3lOR19R1C69zIViHJtO98ZvN+8/3nAAyj9C/Xf9EBnYHeXYurbFUB/ilfuW"
    "SEszaAhhQqwZqf9s7uXVPUfYV/73ogo4A6Vinu7eBud/2eZye4HH75g1D07PeY4U31cQAwEf/Hjsx"
    "/4AeB8YDEca2dRfc8a/tfNz9syVi/rCzrebG2tnvxRQF3ygkKZuOBziXITILRYEjBgREVsyBitisZ"
    "ZemiTr6+sAON/39cnJ/In5ycK0SlARY9GQ9nr9MiLirGsVCvkfQFSMGcdRbMIoa6yuro6q1ao5NMq"
    "5XO6emaN3XQEIGj5uXf397O3iLoBUKhXTbDZNmqaaTqYv9Qd7z3W7vQdUFQSduXPmvSSJzltrm81m"
    "0ywsLIT9R5MDw7RcLj8R2/hIFrKuqhVjxMRxXMrl3KeNRmNzv+92jv43fwNKITCy+ZakAQAAAABJ"
    "RU5ErkJggg=="
)

THEME_ICON_32_B64 = (
    "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"
    "sfI/JnYGXiJS0eQAAAAASUVORK5CYII="
)

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

REPO_OWNER = "tinted-theming"
REPO_NAME  = "schemes"
BRANCH     = "spec-0.11"

RAW_BASE   = f"https://raw.githubusercontent.com/{REPO_OWNER}/{REPO_NAME}/{BRANCH}"
API_BASE   = f"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/contents"
API_BRANCH = f"?ref={BRANCH}"

SYSTEMS = ["base16", "base24"]

DEFAULT_SAVE_DIR = os.path.join(
    os.environ.get("APPDATA", os.path.expanduser("~")),
    "DisplayProfileManager", "Themes"
)

# ---------------------------------------------------------------------------
# Color helpers
# ---------------------------------------------------------------------------

def norm(h) -> str:
    if isinstance(h, int):
        h = str(h).zfill(6)
    h = str(h).strip().lstrip("#").strip()
    if len(h) == 3:
        h = "".join(c * 2 for c in h)
    return "#" + h.upper()

def hex_to_rgb(h: str):
    h = norm(h).lstrip("#")
    return tuple(int(h[i:i+2], 16) / 255.0 for i in (0, 2, 4))

def luminance(h: str) -> float:
    r, g, b = hex_to_rgb(h)
    def lin(c): return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b)

def darken(h: str, amount: float) -> str:
    r, g, b = hex_to_rgb(h)
    hh, s, v = colorsys.rgb_to_hsv(r, g, b)
    v = max(0.0, v - amount)
    r2, g2, b2 = colorsys.hsv_to_rgb(hh, s, v)
    return "#{:02X}{:02X}{:02X}".format(int(r2*255), int(g2*255), int(b2*255))

def lighten(h: str, amount: float) -> str:
    r, g, b = hex_to_rgb(h)
    hh, s, v = colorsys.rgb_to_hsv(r, g, b)
    v = min(1.0, v + amount)
    r2, g2, b2 = colorsys.hsv_to_rgb(hh, s, v)
    return "#{:02X}{:02X}{:02X}".format(int(r2*255), int(g2*255), int(b2*255))

def readable_on(bg: str, candidate: str) -> str:
    lbg = luminance(bg)
    lfg = luminance(candidate)
    ratio = (max(lbg, lfg) + 0.05) / (min(lbg, lfg) + 0.05)
    return candidate if ratio >= 3.0 else ("#000000" if lbg > 0.5 else "#FFFFFF")

def blend(c1: str, c2: str, t: float) -> str:
    r1, g1, b1 = hex_to_rgb(c1)
    r2, g2, b2 = hex_to_rgb(c2)
    return "#{:02X}{:02X}{:02X}".format(
        int((r1 + (r2-r1)*t)*255),
        int((g1 + (g2-g1)*t)*255),
        int((b1 + (b2-b1)*t)*255),
    )

# ---------------------------------------------------------------------------
# YAML parsing
# ---------------------------------------------------------------------------

def parse_yaml(text: str) -> dict:
    try:
        import yaml
        data = yaml.safe_load(text)
    except ImportError:
        data = _fallback_parse(text)

    if not isinstance(data, dict):
        raise ValueError("YAML did not parse to a mapping.")

    # Normalise legacy format
    if "palette" not in data and any(
        re.match(r"^base[0-9A-Fa-f]{2}$", k) for k in data
    ):
        palette = {k: v for k, v in data.items()
                   if re.match(r"^base[0-9A-Fa-f]{2}$", k)}
        clean   = {k: v for k, v in data.items()
                   if not re.match(r"^base[0-9A-Fa-f]{2}$", k)}
        if "scheme" in clean and "name" not in clean:
            clean["name"] = clean.pop("scheme")
        clean["palette"] = palette
        data = clean

    # Validate enough slots exist to produce a useful theme
    palette = data.get("palette", {})
    if not isinstance(palette, dict) or not any(
        re.match(r"^base[0-9A-Fa-f]{2}$", k) for k in palette
    ):
        raise ValueError(
            "No Base16/Base24 palette slots found.\n\n"
            "This file does not appear to be a tinted-theming scheme. "
            "Expected keys like base00–base0F under a 'palette' block."
        )
    return data


def _fallback_parse(text: str) -> dict:
    result:      dict       = {}
    palette:     dict       = {}
    in_palette              = False
    pending_key: str | None = None

    for raw in text.splitlines():
        line     = re.sub(r"\s+#.*$", "", raw)
        stripped = line.strip()
        if not stripped:
            continue
        if re.match(r"^palette\s*:", stripped):
            in_palette = True; pending_key = None; continue
        if in_palette:
            if pending_key:
                val = stripped.strip('"\'').lstrip("#").strip()
                if re.match(r"^[0-9A-Fa-f]{3,6}$", val):
                    palette[pending_key] = val
                pending_key = None
                continue
            m = re.match(r"^\s+(base[0-9A-Fa-f]{2})\s*:\s*[\"']?([0-9A-Fa-f]{3,6})[\"']?\s*$", line)
            if m: palette[m.group(1)] = m.group(2); continue
            m2 = re.match(r"^\s+(base[0-9A-Fa-f]{2})\s*:\s*[\"']?#([0-9A-Fa-f]{3,6})[\"']?\s*$", line)
            if m2: palette[m2.group(1)] = m2.group(2); continue
            m3 = re.match(r"^\s+(base[0-9A-Fa-f]{2})\s*:\s*[\"']?\s*$", line)
            if m3: pending_key = m3.group(1); continue
            if re.match(r"^\S", line) and ":" in line:
                in_palette = False; pending_key = None
        if not in_palette:
            m = re.match(r'^([\w-]+)\s*:\s*["\']?(.+?)["\']?\s*$', stripped)
            if m:
                result[m.group(1)] = m.group(2)

    result["palette"] = palette
    return result

# ---------------------------------------------------------------------------
# Color extraction
# ---------------------------------------------------------------------------

def extract_Colors(scheme: dict, seamless: bool) -> dict:
    p = scheme.get("palette", {})

    def c(key: str, fallback: str = "#888888") -> str:
        raw = p.get(key)
        return norm(raw) if raw is not None else fallback

    is_dark = scheme.get("variant", "dark") != "light"

    bg      = c("base00")
    surf    = c("base01")
    border  = c("base02")
    comment = c("base03")
    txt2    = c("base04")
    txt1    = c("base05")
    accent  = c("base0D")
    sec_acc = c("base0E")
    link    = c("base0C")
    danger  = c("base08")
    success = c("base0B")

    bright_danger  = c("base12", danger)
    bright_success = c("base14", success)

    titlebar_distinct = darken(bg, 0.07) if is_dark else darken(bg, 0.05)
    titlebar  = bg if seamless else titlebar_distinct
    alt_bg    = darken(bg, 0.04) if is_dark else darken(bg, 0.02)
    hover     = border
    pressed   = darken(bg, 0.06) if is_dark else darken(bg, 0.08)
    shadow    = darken(bg, 0.10)
    btn_fg    = readable_on(accent, bg)
    btn_pressed = darken(accent, 0.12) if is_dark else lighten(accent, 0.08)
    danger_hover  = lighten(bright_danger,  0.08) if is_dark else darken(bright_danger,  0.08)
    success_hover = lighten(bright_success, 0.08) if is_dark else darken(bright_success, 0.08)
    sec_btn_bg = blend(surf, bg, 0.3)

    return dict(
        bg=bg, surf=surf, border=border, comment=comment,
        txt1=txt1, txt2=txt2, accent=accent, sec_acc=sec_acc,
        link=link, danger=danger, success=success,
        bright_danger=bright_danger, bright_success=bright_success,
        titlebar=titlebar, alt_bg=alt_bg, hover=hover, pressed=pressed,
        shadow=shadow, btn_fg=btn_fg, btn_pressed=btn_pressed,
        danger_hover=danger_hover, success_hover=success_hover,
        sec_btn_bg=sec_btn_bg, sec_btn_fg=txt1,
        default_tag=blend(accent, bg, 0.4),
        active_tag=blend(success, bg, 0.4),
        is_dark=is_dark,
    )

# ---------------------------------------------------------------------------
# XAML generation
# ---------------------------------------------------------------------------

def build_xaml(scheme: dict, seamless: bool) -> str:
    co = extract_Colors(scheme, seamless)

    name    = scheme.get("name",    "Unknown")
    author  = scheme.get("author",  "Unknown")
    system  = scheme.get("system",  "base16").upper()
    variant = scheme.get("variant", "dark")

    def sr(key): return "{StaticResource " + key + "}"

    return "\n".join([
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        '',
        f'    <!-- {name} — {author} ({system} / {variant}) -->',
        '',
        '    <!-- Base Colors -->',
        f'    <Color x:Key="BackgroundColor">{co["bg"]}</Color>',
        f'    <Color x:Key="SurfaceColor">{co["surf"]}</Color>',
        f'    <Color x:Key="BorderColor">{co["border"]}</Color>',
        f'    <Color x:Key="HoverColor">{co["hover"]}</Color>',
        f'    <Color x:Key="AccentColor">{co["accent"]}</Color>',
        '',
        '    <!-- Window Backgrounds -->',
        f'    <SolidColorBrush x:Key="WindowBackgroundBrush" Color="{sr("BackgroundColor")}"/>',
        f'    <SolidColorBrush x:Key="TitleBarBackgroundBrush" Color="{co["titlebar"]}"/>',
        f'    <SolidColorBrush x:Key="AlternateBackgroundBrush" Color="{co["alt_bg"]}"/>',
        '',
        '    <!-- Content & Control Backgrounds -->',
        f'    <SolidColorBrush x:Key="ContentBackgroundBrush" Color="{sr("SurfaceColor")}"/>',
        f'    <SolidColorBrush x:Key="ControlBackgroundBrush" Color="{sr("SurfaceColor")}"/>',
        f'    <SolidColorBrush x:Key="TextBoxBackgroundBrush" Color="{sr("SurfaceColor")}"/>',
        f'    <SolidColorBrush x:Key="ComboBoxBackgroundBrush" Color="{sr("SurfaceColor")}"/>',
        f'    <SolidColorBrush x:Key="ComboBoxDropDownBackgroundBrush" Color="{sr("SurfaceColor")}"/>',
        f'    <SolidColorBrush x:Key="CheckBoxBackgroundBrush" Color="{sr("SurfaceColor")}"/>',
        f'    <SolidColorBrush x:Key="ListItemBackgroundBrush" Color="{sr("SurfaceColor")}"/>',
        '',
        '    <!-- Borders & Separators -->',
        f'    <SolidColorBrush x:Key="BorderBrush" Color="{sr("BorderColor")}"/>',
        f'    <SolidColorBrush x:Key="SeparatorBrush" Color="{sr("BorderColor")}"/>',
        f'    <SolidColorBrush x:Key="ControlBorderBrush" Color="{sr("BorderColor")}"/>',
        f'    <SolidColorBrush x:Key="TextBoxBorderBrush" Color="{sr("BorderColor")}"/>',
        f'    <SolidColorBrush x:Key="ComboBoxBorderBrush" Color="{sr("BorderColor")}"/>',
        f'    <SolidColorBrush x:Key="CheckBoxBorderBrush" Color="{sr("BorderColor")}"/>',
        f'    <SolidColorBrush x:Key="WindowControlHoverBrush" Color="{sr("BorderColor")}"/>',
        f'    <SolidColorBrush x:Key="ListItemSelectedBackgroundBrush" Color="{sr("HoverColor")}"/>',
        '',
        '    <!-- Interaction States -->',
        f'    <SolidColorBrush x:Key="ControlHoverBackgroundBrush" Color="{sr("HoverColor")}"/>',
        f'    <SolidColorBrush x:Key="ListItemHoverBackgroundBrush" Color="{sr("HoverColor")}"/>',
        f'    <SolidColorBrush x:Key="ComboBoxHoverBackgroundBrush" Color="{sr("HoverColor")}"/>',
        f'    <SolidColorBrush x:Key="ControlPressedBackgroundBrush" Color="{co["pressed"]}"/>',
        '',
        '    <!-- Primary Button -->',
        f'    <SolidColorBrush x:Key="ButtonBackgroundBrush" Color="{sr("AccentColor")}"/>',
        f'    <SolidColorBrush x:Key="ButtonForegroundBrush" Color="{co["btn_fg"]}"/>',
        f'    <SolidColorBrush x:Key="ButtonHoverBackgroundBrush" Color="{co["sec_acc"]}"/>',
        f'    <SolidColorBrush x:Key="ButtonPressedBackgroundBrush" Color="{co["btn_pressed"]}"/>',
        f'    <SolidColorBrush x:Key="ButtonBorderBrush" Color="{sr("AccentColor")}"/>',
        f'    <SolidColorBrush x:Key="TextBoxFocusBorderBrush" Color="{sr("AccentColor")}"/>',
        f'    <SolidColorBrush x:Key="CheckBoxCheckmarkBrush" Color="{sr("AccentColor")}"/>',
        f'    <SolidColorBrush x:Key="LinkBrush" Color="{co["link"]}"/>',
        '',
        '    <!-- Secondary Button -->',
        f'    <SolidColorBrush x:Key="SecondaryButtonBackgroundBrush" Color="{co["sec_btn_bg"]}"/>',
        f'    <SolidColorBrush x:Key="SecondaryButtonForegroundBrush" Color="{co["sec_btn_fg"]}"/>',
        f'    <SolidColorBrush x:Key="SecondaryButtonHoverBackgroundBrush" Color="{sr("HoverColor")}"/>',
        f'    <SolidColorBrush x:Key="SecondaryButtonPressedBackgroundBrush" Color="{co["pressed"]}"/>',
        f'    <SolidColorBrush x:Key="SecondaryButtonBorderBrush" Color="{sr("BorderColor")}"/>',
        '',
        '    <!-- Status Buttons -->',
        f'    <SolidColorBrush x:Key="DangerButtonBackgroundBrush" Color="{co["bright_danger"]}"/>',
        f'    <SolidColorBrush x:Key="DangerButtonHoverBackgroundBrush" Color="{co["danger_hover"]}"/>',
        f'    <SolidColorBrush x:Key="SuccessButtonBackgroundBrush" Color="{co["bright_success"]}"/>',
        f'    <SolidColorBrush x:Key="SuccessButtonHoverBackgroundBrush" Color="{co["success_hover"]}"/>',
        '',
        '    <!-- Title Bar Extras -->',
        f'    <SolidColorBrush x:Key="TitleBarTextBrush" Color="{co["txt2"]}"/>',
        f'    <SolidColorBrush x:Key="CloseButtonHoverBrush" Color="{co["danger"]}"/>',
        '',
        '    <!-- Text Brushes -->',
        f'    <SolidColorBrush x:Key="PrimaryTextBrush" Color="{co["txt1"]}"/>',
        f'    <SolidColorBrush x:Key="SecondaryTextBrush" Color="{co["txt2"]}"/>',
        f'    <SolidColorBrush x:Key="TertiaryTextBrush" Color="{co["comment"]}"/>',
        '',
        '    <!-- Tooltips -->',
        f'    <SolidColorBrush x:Key="TooltipBackgroundBrush" Color="{co["surf"]}"/>',
        f'    <SolidColorBrush x:Key="TooltipTextBrush" Color="{co["txt1"]}"/>',
        '',
        '    <!-- Effects -->',
        f'    <DropShadowEffect x:Key="CardShadow" ShadowDepth="2" Direction="270"'
        f' BlurRadius="8" Opacity="0.45" Color="{co["shadow"]}"/>',
        f'    <DropShadowEffect x:Key="ButtonShadow" ShadowDepth="1" Direction="270"'
        f' BlurRadius="4" Opacity="0.35" Color="{co["shadow"]}"/>',
        '',
        '</ResourceDictionary>',
    ])

# ---------------------------------------------------------------------------
# Preview canvas
# ---------------------------------------------------------------------------

def draw_preview(canvas: tk.Canvas, co: dict, dpm_img):
    """
    Draw an accurate replica of DPM MainWindow (900x600 reference).

    Fixed elements: title bar 32px, status bar 64px, right panel 346px,
    all buttons, icon, window controls.
    Flexible: left panel fills remaining width.

    Layout from MainWindow.xaml:
      TitleBar 32px | StatusBar Padding=16 + btn 32px = 64px
      Content Margin=16,16,16,0 | Panel gap=16px | Right panel=346px
      Profile header MinHeight=56 Padding=16 | Item Padding=12,8
      Buttons: Duplicate auto, Import 60px, Create 60px, Edit 60px, Delete 60px, Apply 32x
      Status: RefreshBtn 32x32, OpenFolder 100px, Settings 70px
      Panel CornerRadius=4 | Item CornerRadius=4 | Card CornerRadius=4
    """
    canvas.delete("all")
    W = canvas.winfo_width()
    H = canvas.winfo_height()
    if W < 20 or H < 20:
        return

    bg       = co["bg"]
    surf     = co["surf"]
    border   = co["border"]
    txt1     = co["txt1"]
    txt2     = co["txt2"]
    comment  = co["comment"]
    accent   = co["accent"]
    danger   = co["bright_danger"]
    success  = co["bright_success"]
    sec_bg   = co["sec_btn_bg"]
    btn_fg   = co["btn_fg"]
    titlebar = co["titlebar"]

    import datetime
    now  = datetime.datetime.now()
    today = now.strftime(f"%B {now.day}, %Y")
    time_str = now.strftime(f"%B {now.day}, %Y at %I:%M %p").replace(" 0", " ")

    # ── Fixed pixel constants from XAML ───────────────────────────────
    TITLE_H  = 32
    M        = 16
    STATUS_H = 64
    GAP      = 16
    RIGHT_W  = 346
    HDR_H    = 56
    HDR_PAD  = 16
    BTN_H    = 34
    BTN_R    = 4
    ITEM_PX  = 12
    ITEM_PY  = 8
    ITEM_MT  = 2
    LIST_PAD = 8
    SB_BTN_H = 32
    CARD_R   = 4
    CARD_PAD = 10

    # ── Derived layout ─────────────────────────────────────────────────
    content_x1 = M
    content_y1 = TITLE_H + M
    content_x2 = W - M
    content_y2 = H - STATUS_H
    right_x2   = content_x2
    right_x1   = right_x2 - RIGHT_W
    left_x1    = content_x1
    left_x2    = right_x1 - GAP

    NAME_H  = 16
    DESC_MT = 2
    DESC_H  = 15
    INFO_MT = 4
    INFO_H  = 14
    ITEM_H  = ITEM_PY + NAME_H + DESC_MT + DESC_H + INFO_MT + INFO_H + ITEM_PY

    # ── Helpers ────────────────────────────────────────────────────────
    def rrect(x1, y1, x2, y2, r, fill, outline=""):
        if r <= 0 or x2-x1 < 2*r or y2-y1 < 2*r:
            canvas.create_rectangle(x1, y1, x2, y2, fill=fill,
                                    outline=outline if outline else fill)
            return
        canvas.create_rectangle(x1+r, y1,   x2-r, y2,   fill=fill, outline="")
        canvas.create_rectangle(x1,   y1+r, x2,   y2-r, fill=fill, outline="")
        canvas.create_oval(x1,     y1,     x1+2*r, y1+2*r, fill=fill, outline="")
        canvas.create_oval(x2-2*r, y1,     x2,     y1+2*r, fill=fill, outline="")
        canvas.create_oval(x1,     y2-2*r, x1+2*r, y2,     fill=fill, outline="")
        canvas.create_oval(x2-2*r, y2-2*r, x2,     y2,     fill=fill, outline="")
        if outline:
            canvas.create_rectangle(x1+r, y1,   x2-r, y1+1, fill=outline, outline="")
            canvas.create_rectangle(x1+r, y2-1, x2-r, y2,   fill=outline, outline="")
            canvas.create_rectangle(x1,   y1+r, x1+1, y2-r, fill=outline, outline="")
            canvas.create_rectangle(x2-1, y1+r, x2,   y2-r, fill=outline, outline="")

    def btn(x1, y1, x2, y2, text, bg_c, fg_c, fsize=10, font_family="Segoe UI"):
        rrect(x1, y1, x2, y2, BTN_R, bg_c)
        canvas.create_text((x1+x2)//2, (y1+y2)//2,
                           text=text, fill=fg_c,
                           font=(font_family, fsize, "bold"))

    canvas.create_rectangle(0, 0, W, H, fill=bg, outline="")
    canvas.create_rectangle(0, 0, W, TITLE_H, fill=titlebar, outline="")
    tx = 12
    if dpm_img:
        canvas.create_image(tx, TITLE_H//2, image=dpm_img, anchor="w")
        tx += 24
    canvas.create_text(tx, TITLE_H//2, text="Display Profile Manager",
                       fill=txt2, font=("Segoe UI", 11), anchor="w")
    for i, sym in enumerate(["−", "□", "✕"]):
        bx1 = W - (3-i) * 46
        canvas.create_rectangle(bx1, 0, bx1+45, TITLE_H, fill=titlebar, outline="")
        canvas.create_text(bx1+22, TITLE_H//2, text=sym,
                           fill=txt2, font=("Segoe UI", 11))

    # ── Left panel ────────────────────────────────────────────────────
    if left_x2 > left_x1 + 10:
        rrect(left_x1, content_y1, left_x2, content_y2, BTN_R, surf, border)

        prof_hdr_y2 = content_y1 + HDR_H
        canvas.create_text(left_x1 + HDR_PAD, content_y1 + HDR_H//2,
                           text="Profiles", fill=txt1,
                           font=("Segoe UI", 16, "bold"), anchor="w")

        b_y1      = content_y1 + (HDR_H - BTN_H) // 2
        b_y2      = b_y1 + BTN_H
        create_x2 = left_x2 - HDR_PAD
        btn(create_x2 - 60, b_y1, create_x2, b_y2, "Create", accent, btn_fg)
        btn(create_x2 - 128, b_y1, create_x2 - 68, b_y2, "Import", sec_bg, txt1)
        btn(create_x2 - 212, b_y1, create_x2 - 136, b_y2, "Duplicate", sec_bg, txt1)

        ix1 = left_x1 + LIST_PAD
        ix2 = left_x2 - LIST_PAD
        iy1 = prof_hdr_y2 + LIST_PAD
        iy2 = iy1 + ITEM_H

        if iy2 <= content_y2 - LIST_PAD:
            rrect(ix1, iy1, ix2, iy2, CARD_R, sec_bg, comment)

            itx = ix1 + ITEM_PX
            ity = iy1 + ITEM_PY

            canvas.create_text(itx, ity, text="Default", fill=txt1,
                               font=("Segoe UI", 11, "bold"), anchor="nw")
            tag1_x = itx + (len("Default") * 7) + 10
            canvas.create_text(tag1_x, ity + 1, text="Default", fill=accent,
                               font=("Segoe UI", 10, "bold"), anchor="nw")
            tag2_x = tag1_x + (len("Default") * 7) + 4
            canvas.create_text(tag2_x, ity + 1, text="Active", fill=success,
                               font=("Segoe UI", 10, "bold"), anchor="nw")

            # Apply button (MDL2 Asset icon)
            apply_btn_size = 32
            apply_x2 = ix2 - ITEM_PX
            apply_x1 = apply_x2 - apply_btn_size
            apply_y1 = iy1 + (ITEM_H - apply_btn_size) // 2
            apply_y2 = apply_y1 + apply_btn_size
            btn(apply_x1, apply_y1, apply_x2, apply_y2, "\ue751", accent, btn_fg, fsize=13, font_family="Segoe MDL2 Assets")

            ity += NAME_H + DESC_MT
            canvas.create_text(itx, ity,
                               text="Default system profile created automatically",
                               fill=txt2, font=("Segoe UI", 10), anchor="nw")

            ity += DESC_H + INFO_MT
            canvas.create_text(itx, ity,
                               text=f"1 display",
                               fill=comment, font=("Segoe UI", 10), anchor="nw")

    # ── Right panel (Details) ─────────────────────────────────────────
    if right_x1 < right_x2 - 10:
        rrect(right_x1, content_y1, right_x2, content_y2, BTN_R, surf, border)

        det_hdr_y2 = content_y1 + HDR_H
        canvas.create_text(right_x1 + HDR_PAD, content_y1 + HDR_H//2,
                           text="Details", fill=txt1,
                           font=("Segoe UI", 16, "bold"), anchor="w")

        db_y1   = content_y1 + (HDR_H - BTN_H) // 2
        db_y2   = db_y1 + BTN_H
        del_x2  = right_x2 - HDR_PAD
        btn(del_x2 - 60,  db_y1, del_x2,  db_y2, "Delete", danger, btn_fg)
        btn(del_x2 - 128, db_y1, del_x2 - 68, db_y2, "Edit", sec_bg, txt1)

        dcx  = right_x1 + HDR_PAD
        dcy  = det_hdr_y2 + 12
        dcx2 = right_x2 - HDR_PAD

        canvas.create_text(dcx, dcy, text="Default", fill=txt1,
                           font=("Segoe UI", 13, "bold"), anchor="nw")
        dcy += 20
        canvas.create_text(dcx, dcy, text="Default system profile created automatically",
                           fill=txt2, font=("Segoe UI", 10), anchor="nw")
        dcy += 30
        canvas.create_text(dcx, dcy, text="Displays",
                           fill=txt1, font=("Segoe UI", 11, "bold"), anchor="nw")
        dcy += 20

        # Monitor card
        mon_card_x1, mon_card_x2 = dcx, dcx2
        mon_card_y1, mon_card_h = dcy, 105
        mon_card_y2 = mon_card_y1 + mon_card_h

        rrect(mon_card_x1, mon_card_y1, mon_card_x2, mon_card_y2, CARD_R, surf, border)

        mcy, mcx = mon_card_y1 + CARD_PAD, mon_card_x1 + CARD_PAD
        canvas.create_text(mcx, mcy, text="Generic Display",
                           fill=txt1, font=("Segoe UI", 11, "bold"), anchor="nw")
        mcy += 17
        canvas.create_text(mcx, mcy, text="Resolution: 1920x1080 @ 60Hz",
                           fill=txt2, font=("Segoe UI", 10), anchor="nw")
        mcy += 17
        canvas.create_text(mcx, mcy, text="Rotation: 0°",
                           fill=txt2, font=("Segoe UI", 10), anchor="nw")
        mcy += 16
        canvas.create_text(mcx, mcy, text="DPI: 100%",
                           fill=txt2, font=("Segoe UI", 10), anchor="nw")
        mcy += 15
        canvas.create_text(mcx, mcy, text="Primary Display",
                           fill=accent, font=("Segoe UI", 10), anchor="nw")

        dcy = mon_card_y2 + 16
        canvas.create_text(dcx, dcy, text="Audio",
                           fill=txt1, font=("Segoe UI", 11, "bold"), anchor="nw")
        dcy += 17
        canvas.create_text(dcx, dcy, text="Output: Default Device (Not Applied)",
                           fill=txt2, font=("Segoe UI", 10), anchor="nw")
        dcy += 15
        canvas.create_text(dcx, dcy, text="Input: Default Device (Not Applied)",
                           fill=txt2, font=("Segoe UI", 10), anchor="nw")
        dcy += 30
        canvas.create_text(dcx, dcy, text=f"Created: {time_str}",
                           fill=comment, font=("Segoe UI", 9), anchor="nw")
        dcy += 14
        canvas.create_text(dcx, dcy, text=f"Last Modified: {time_str}",
                           fill=comment, font=("Segoe UI", 9), anchor="nw")

    # ── Status bar ────────────────────────────────────────────────────
    sb_y1 = H - STATUS_H
    canvas.create_line(0, sb_y1, W, sb_y1, fill=border)
    canvas.create_rectangle(0, sb_y1, W, H, fill=bg, outline="")
    sb_mid = sb_y1 + STATUS_H // 2

    ref_x1, ref_x2 = 16, 16 + SB_BTN_H
    rrect(ref_x1, sb_mid - SB_BTN_H//2, ref_x2, sb_mid + SB_BTN_H//2, BTN_R, sec_bg)
    canvas.create_text((ref_x1+ref_x2)//2, sb_mid, text="\ue72c", fill=txt1, font=("Segoe MDL2 Assets", 12))
    canvas.create_text(ref_x2 + 8, sb_mid, text="Ready", fill=comment, font=("Segoe UI", 11), anchor="w")

    settings_x2 = W - 16
    btn(settings_x2 - 70, sb_mid - SB_BTN_H//2, settings_x2, sb_mid + SB_BTN_H//2, "Settings", accent, btn_fg)
    btn(settings_x2 - 178, sb_mid - SB_BTN_H//2, settings_x2 - 78, sb_mid + SB_BTN_H//2, "Open Folder", sec_bg, txt1)


# ---------------------------------------------------------------------------
# Network
# ---------------------------------------------------------------------------

def github_api_get(path: str, token: str = "") -> list:
    url = f"{API_BASE}/{path}{API_BRANCH}"
    req = urllib.request.Request(url)
    req.add_header("Accept", "application/vnd.github+json")
    req.add_header("User-Agent", "DPMThemeBuilder/1.6")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req, timeout=15) as resp:
        return json.loads(resp.read().decode())


def fetch_raw(path: str, token: str = "") -> str:
    url = f"{RAW_BASE}/{path}"
    req = urllib.request.Request(url)
    req.add_header("User-Agent", "DPMThemeBuilder/1.6")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(req, timeout=15) as resp:
        return resp.read().decode("utf-8")

# ---------------------------------------------------------------------------
# Tooltip
# ---------------------------------------------------------------------------

class Tooltip:
    def __init__(self, widget: tk.Widget, text: str, delay: int = 500):
        self._w = widget; self._text = text; self._delay = delay
        self._job: str | None = None; self._tip: tk.Toplevel | None = None
        widget.bind("<Enter>",       self._schedule, add="+")
        widget.bind("<Leave>",       self._cancel,   add="+")
        widget.bind("<ButtonPress>", self._cancel,   add="+")

    def _schedule(self, _=None):
        self._cancel()
        self._job = self._w.after(self._delay, self._show)

    def _cancel(self, _=None):
        if self._job: self._w.after_cancel(self._job); self._job = None
        if self._tip: self._tip.destroy(); self._tip = None

    def _show(self):
        x = self._w.winfo_rootx() + 16
        y = self._w.winfo_rooty() + self._w.winfo_height() + 6
        self._tip = tk.Toplevel(self._w)
        self._tip.wm_overrideredirect(True)
        self._tip.wm_geometry(f"+{x}+{y}")
        tk.Label(self._tip, text=self._text, justify=tk.LEFT,
                 background="#ffffcc", relief=tk.SOLID, borderwidth=1,
                 font=("Segoe UI", 9), wraplength=340, padx=8, pady=5).pack()

# ---------------------------------------------------------------------------
# Tooltip strings
# ---------------------------------------------------------------------------

TOKEN_TOOLTIP = (
    "The GitHub API limits unauthenticated requests to 60 per hour. "
    "Additional fetches will fail until the hour resets.\n\n"
    "A Personal Access Token raises this limit to 5,000/hr:\n"
    "  GitHub → Settings → Developer settings\n"
    "  → Personal access tokens → Fine-grained\n"
    "  → Public repositories (read-only) is sufficient.\n\n"
    "This token is held in memory only and is never written or stored."
)

LOCAL_TOOLTIP = (
    "Load a local scheme YAML instead of fetching from GitHub.\n"
    "Supports Base16, Base24, and legacy Base16 formats."
)

SEAMLESS_TOOLTIP = (
    "When enabled, the title bar shares the window's "
    "background color for a unified, edge-to-edge look."
)

# ---------------------------------------------------------------------------
# Application
# ---------------------------------------------------------------------------

class App(tk.Tk):
    def __init__(self):
        self._set_taskbar_icon()
        super().__init__()
        self.title("DPM Theme Builder")
        self.resizable(True, True)
        self.minsize(760, 620)

        self._schemes:       dict[str, dict] = {}
        self._display_keys:  list[str]       = []
        self._current_xaml:  str             = ""
        self._current_scheme: dict           = {}
        self._token    = tk.StringVar()
        self._seamless = tk.BooleanVar(value=True)

        self._theme32   = None
        self._theme16   = None
        self._dpm_photo = None
        
        self._load_icons()
        self._build_ui()
        self._load_scheme_list()

    def _set_taskbar_icon(self):
        try:
            myappid = "DisplayProfileManager.ThemeBuilder"
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(myappid)
        except Exception:
            pass

    def _load_icons(self):
        try:
            data16 = base64.b64decode(THEME_ICON_16_B64)
            self._theme16 = tk.PhotoImage(data=base64.b64encode(data16))
            self.iconphoto(False, self._theme16)
        except Exception:
            pass

        try:
            data32 = base64.b64decode(THEME_ICON_32_B64)
            self._theme32 = tk.PhotoImage(data=base64.b64encode(data32))
            self.iconphoto(False, self._theme32)
        except Exception:
            pass

        try:
            raw = base64.b64decode(DPM_ICON_16_B64)
            self._dpm_photo = tk.PhotoImage(data=base64.b64encode(raw))
        except Exception:
            self._dpm_photo = None

    # ── UI ────────────────────────────────────────────────────────────────

    def _build_ui(self):
        self.columnconfigure(0, weight=1)
        self.rowconfigure(2, weight=1)
        self.rowconfigure(3, weight=3)

        # ── Row 0: GitHub token ──────────────────────────
        tok = ttk.Frame(self, padding=(8, 8, 8, 2))
        tok.grid(row=0, column=0, sticky="ew")
        tok.columnconfigure(1, weight=1)

        token_lbl = ttk.Label(tok, text="GitHub token (optional):")
        token_lbl.grid(row=0, column=0, sticky="w")
        Tooltip(token_lbl, TOKEN_TOOLTIP)

        token_entry = ttk.Entry(tok, textvariable=self._token, show="*")
        token_entry.grid(row=0, column=1, sticky="ew", padx=(8, 0))
        Tooltip(token_entry, TOKEN_TOOLTIP)

        # ── Row 1: System | Search ──────────────────────────
        filt = ttk.Frame(self, padding=(8, 2, 8, 4))
        filt.grid(row=1, column=0, sticky="ew")
        filt.columnconfigure(4, weight=1)

        ttk.Label(filt, text="System:").grid(row=0, column=1, sticky="w")
        self._sys_var = tk.StringVar(value="All")
        sys_cb = ttk.Combobox(filt, textvariable=self._sys_var, width=9,
                               state="readonly", values=["All"] + SYSTEMS)
        sys_cb.grid(row=0, column=2, sticky="w", padx=(4, 12))
        sys_cb.bind("<<ComboboxSelected>>", lambda _: self._filter_list())

        ttk.Label(filt, text="Search:").grid(row=0, column=3, sticky="w")
        self._search_var = tk.StringVar()
        self._search_var.trace_add("write", lambda *_: self._filter_list())
        ttk.Entry(filt, textvariable=self._search_var).grid(
            row=0, column=4, sticky="ew", padx=(4, 0))

        # ── Row 2: Scheme list ────────────────────────────────────────
        list_frame = ttk.LabelFrame(self, text="Schemes", padding=4)
        list_frame.grid(row=2, column=0, sticky="nsew", padx=8, pady=4)
        list_frame.columnconfigure(0, weight=1)
        list_frame.rowconfigure(0, weight=1)

        self._listbox = tk.Listbox(
            list_frame, height=15, activestyle="dotbox",
            selectmode=tk.SINGLE, font=("Consolas", 9))
        lsb = ttk.Scrollbar(list_frame, orient=tk.VERTICAL,
                             command=self._listbox.yview)
        self._listbox.configure(yscrollcommand=lsb.set)
        self._listbox.grid(row=0, column=0, sticky="nsew")
        lsb.grid(row=0, column=1, sticky="ns")
        self._listbox.bind("<<ListboxSelect>>", self._on_select)

        # ── Row 3: Notebook (Preview / Generated XAML) ────────────────
        self._notebook = ttk.Notebook(self)
        self._notebook.grid(row=3, column=0, sticky="nsew", padx=8, pady=(0, 4))

        # Preview tab
        pf = ttk.Frame(self._notebook)
        self._notebook.add(pf, text="Preview")
        pf.rowconfigure(0, weight=1)
        pf.columnconfigure(0, weight=1)
        self._canvas = tk.Canvas(pf, bg="#1e1e1e", highlightthickness=0)
        self._canvas.grid(row=0, column=0, sticky="nsew")
        self._canvas.bind("<Configure>", self._on_canvas_resize)

        # XAML tab
        xf = ttk.Frame(self._notebook)
        self._notebook.add(xf, text="Generated XAML")
        xf.rowconfigure(0, weight=1)
        xf.columnconfigure(0, weight=1)
        self._text = tk.Text(xf, font=("Consolas", 8),
                             wrap=tk.NONE, state=tk.DISABLED)
        xsb = ttk.Scrollbar(xf, orient=tk.HORIZONTAL, command=self._text.xview)
        ysb = ttk.Scrollbar(xf, orient=tk.VERTICAL,   command=self._text.yview)
        self._text.configure(xscrollcommand=xsb.set, yscrollcommand=ysb.set)
        self._text.grid(row=0, column=0, sticky="nsew")
        ysb.grid(row=0, column=1, sticky="ns")
        xsb.grid(row=1, column=0, sticky="ew")

        # ── Row 4: Status bar ─────────────────────────────────────────
        bot = ttk.Frame(self, padding=(8, 0, 8, 8))
        bot.grid(row=4, column=0, sticky="ew")
        bot.columnconfigure(0, weight=1)

        self._status = tk.StringVar(value="Loading scheme list…")
        ttk.Label(bot, textvariable=self._status, anchor="w").grid(
            row=0, column=0, sticky="ew")

        seamless_cb = ttk.Checkbutton(
            bot, text="Seamless title bar",
            variable=self._seamless,
            command=self._on_seamless_change,
        )
        seamless_cb.grid(row=0, column=1, padx=(8, 4))
        Tooltip(seamless_cb, SEAMLESS_TOOLTIP)

        self._btn_save = ttk.Button(bot, text="Save theme…",
                                    command=self._save, state=tk.DISABLED)
        self._btn_save.grid(row=0, column=2, padx=(0, 4))

        local_btn = ttk.Button(bot, text="Load local YAML…",
                               command=self._load_local)
        local_btn.grid(row=0, column=3)
        Tooltip(local_btn, LOCAL_TOOLTIP)

    # ── Seamless / preview ────────────────────────────────────────────────

    def _on_seamless_change(self):
        if self._current_scheme:
            self._refresh_output(self._current_scheme)

    def _on_canvas_resize(self, _=None):
        self._redraw_preview()

    def _redraw_preview(self):
        if not self._current_scheme:
            return
        try:
            co = extract_Colors(self._current_scheme, self._seamless.get())
            draw_preview(self._canvas, co, self._dpm_photo)
        except Exception:
            pass   # never let a preview error block the rest of the app

    # ── Scheme list ───────────────────────────────────────────────────────

    def _load_scheme_list(self):
        self._status.set("Fetching scheme list from GitHub…")
        self._listbox.delete(0, tk.END)
        self._schemes.clear()
        self._current_xaml    = ""
        self._current_scheme  = {}
        self._btn_save.configure(state=tk.DISABLED)
        self._set_xaml_text("")
        threading.Thread(target=self._fetch_list_thread, daemon=True).start()

    def _fetch_list_thread(self):
        schemes: dict[str, dict] = {}
        errors:  list[str]       = []
        for system in SYSTEMS:
            try:
                entries = github_api_get(system, self._token.get())
                for entry in entries:
                    if entry.get("type") == "file" and entry["name"].endswith(".yaml"):
                        slug = entry["name"][:-5]
                        key  = f"{system}/{slug}"
                        schemes[key] = {
                            "system": system,
                            "slug":   slug,
                            "name":   slug.replace("-", " ").title(),
                            "path":   f"{system}/{entry['name']}",
                        }
            except Exception as exc:
                # One system failing should not prevent the other from loading
                errors.append(f"{system}: {exc}")
        self._schemes = schemes
        self.after(0, self._populate_list, errors)

    def _populate_list(self, errors: list[str]):
        if errors and not self._schemes:
            self._status.set("Failed to load — " + "; ".join(errors))
        elif errors:
            self._status.set(f"Loaded {len(self._schemes)} schemes (partial: {'; '.join(errors)})")
        else:
            self._status.set(f"Loaded {len(self._schemes)} schemes.")
        self._filter_list()

    def _filter_list(self):
        query = self._search_var.get().lower()
        sys_f = self._sys_var.get()
        self._listbox.delete(0, tk.END)
        self._display_keys = []
        for key, meta in sorted(self._schemes.items()):
            if sys_f != "All" and meta["system"] != sys_f:
                continue
            if query and query not in key.lower():
                continue
            self._listbox.insert(tk.END, f"[{meta['system']:6}]  {meta['slug']}")
            self._display_keys.append(key)

    # ── Selection → convert ───────────────────────────────────────────────

    def _on_select(self, _=None):
        sel = self._listbox.curselection()
        if not sel:
            return
        meta = self._schemes[self._display_keys[sel[0]]]
        self._status.set(f"Fetching {meta['path']}…")
        threading.Thread(target=self._fetch_and_convert,
                         args=(meta,), daemon=True).start()

    def _fetch_and_convert(self, meta: dict):
        try:
            raw    = fetch_raw(meta["path"], self._token.get())
            scheme = parse_yaml(raw)
            scheme.setdefault("name",   meta["name"])
            scheme.setdefault("system", meta["system"])
            label = (f"Converted: {scheme.get('name', meta['slug'])}"
                     f" ({scheme.get('system', meta['system'])})")
            self.after(0, self._refresh_output, scheme, label)
        except Exception as exc:
            self.after(0, self._status.set, f"Error: {exc}")

    def _refresh_output(self, scheme: dict, status: str = ""):
        """Update XAML text, preview canvas, and enable save — in that order."""
        self._current_scheme = scheme
        seamless = self._seamless.get()

        # Generate XAML first — this must always succeed for Save to be available
        try:
            xaml = build_xaml(scheme, seamless)
            self._current_xaml = xaml
            self._set_xaml_text(xaml)
            self._btn_save.configure(state=tk.NORMAL)
        except Exception as exc:
            self._status.set(f"XAML generation error: {exc}")
            return

        # Preview is best-effort — a failure here never blocks save
        self._redraw_preview()

        if status:
            self._status.set(status)

    def _set_xaml_text(self, content: str):
        self._text.configure(state=tk.NORMAL)
        self._text.delete("1.0", tk.END)
        self._text.insert(tk.END, content)
        self._text.configure(state=tk.DISABLED)

    # ── Save / load local ─────────────────────────────────────────────────

    def _init_monitor(self):
        self._monitor_thread = threading.Thread(target=self._folder_monitor_loop, daemon=True)
        self._monitor_thread.start()

    def _folder_monitor_loop(self):
        import time
        
        if not os.path.exists(DEFAULT_SAVE_DIR):
            try:
                os.makedirs(DEFAULT_SAVE_DIR, exist_ok=True)
            except Exception:
                return

        try:
            last_mtime = os.stat(DEFAULT_SAVE_DIR).st_mtime
        except Exception:
            last_mtime = 0

        while True:
            time.sleep(0.5)
            try:
                current_mtime = os.stat(DEFAULT_SAVE_DIR).st_mtime
                
                if current_mtime > last_mtime:
                    last_mtime = current_mtime
                    
                    files = [
                        os.path.join(DEFAULT_SAVE_DIR, f) 
                        for f in os.listdir(DEFAULT_SAVE_DIR)
                        if f.lower().endswith(".xaml")
                    ]
                    
                    if files:
                        newest_file = max(files, key=os.path.getmtime)
                        self.after(0, self._apply_to_dpm, newest_file)
            
            except Exception:
                continue

    def _apply_to_dpm(self, saved_path: str):
        """Signals DPM to apply the theme or refresh the list."""
        import subprocess

        dpm_exe = self._find_dpm_exe()
        if not dpm_exe:
            self._status.set(f"Detected \u2192 {os.path.basename(saved_path)} (DPM not found)")
            return

        theme_name = os.path.splitext(os.path.basename(saved_path))[0]
        saved_dir  = os.path.normcase(os.path.abspath(os.path.dirname(saved_path)))
        themes_dir = os.path.normcase(os.path.abspath(DEFAULT_SAVE_DIR))

        cmd = ([dpm_exe, "--theme", theme_name]
               if saved_dir == themes_dir
               else [dpm_exe, "--refresh"])

        try:
            subprocess.Popen(
                cmd,
                creationflags=0x08000000 | 0x00000008,  # CREATE_NO_WINDOW | DETACHED_PROCESS
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            
            status_text = f"Applied '{theme_name}'" if saved_dir == themes_dir else "Refreshed themes"
            self._status.set(f"Synced \u2192 {os.path.basename(saved_path)} ({status_text})")
        except Exception:
            self._status.set(f"Synced \u2192 {os.path.basename(saved_path)}")

    def _find_dpm_exe(self) -> "str | None":
        """Locates the DPM binary with priority on installed versions."""
        prog_files = os.environ.get("ProgramFiles", r"C:\Program Files")
        local_app  = os.environ.get("LOCALAPPDATA", "")
        exe_name   = "DisplayProfileManager.exe"
        script_dir = os.path.dirname(os.path.abspath(__file__))

        candidates = [
            os.path.join(prog_files, "Display Profile Manager", exe_name),
            os.path.join(local_app, "DisplayProfileManager", exe_name),
            os.path.join(script_dir, exe_name),
            os.path.join(script_dir, "..", exe_name),
        ]

        for p in candidates:
            if os.path.isfile(p):
                return os.path.normpath(p)
        return None

    def _save(self):
        if not self._current_xaml:
            return
        m = re.search(r"<!--\s*(.+?)\s*—", self._current_xaml)
        default = (m.group(1) if m else "Theme") + ".xaml"

        kwargs: dict = dict(
            defaultextension=".xaml",
            initialfile=default,
            filetypes=[("XAML files", "*.xaml"), ("All files", "*.*")],
        )
        if os.path.isdir(DEFAULT_SAVE_DIR):
            kwargs["initialdir"] = DEFAULT_SAVE_DIR

        path = filedialog.asksaveasfilename(**kwargs)
        if not path:
            return

        with open(path, "w", encoding="utf-8") as f:
            f.write(self._current_xaml)

        self._apply_to_dpm(path)

    def _load_local(self):
        path = filedialog.askopenfilename(
            filetypes=[("YAML files", "*.yaml *.yml"), ("All files", "*.*")]
        )
        if not path:
            return
        try:
            with open(path, "r", encoding="utf-8") as f:
                raw = f.read()
            scheme = parse_yaml(raw)   # raises ValueError if not a valid scheme
            scheme.setdefault(
                "name",
                os.path.splitext(os.path.basename(path))[0]
                .replace("-", " ").title()
            )
            self._refresh_output(scheme, f"Converted: {os.path.basename(path)}")
        except ValueError as exc:
            messagebox.showerror("Unsupported file", str(exc))
        except Exception as exc:
            messagebox.showerror("Error", str(exc))


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    App().mainloop()
