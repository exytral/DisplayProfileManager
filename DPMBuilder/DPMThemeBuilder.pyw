"""
DPM Theme Builder
Converts tinted-theming Base16 / Base24 YAML schemes to the WPF
ResourceDictionary format used by Display Profile Manager.

Requirements: Python 3.10+ with Tkinter (standard on Windows).
  No third-party packages required.
  Optional: pip install pyyaml  — more robust YAML parsing.

Run as .pyw to suppress the console window on Windows.
"""

import base64
import colorsys
import ctypes
from ctypes import wintypes
import hashlib
import json
import os
import re
import subprocess
import threading
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import urllib.request
import winreg

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
    "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAGjElEQVR4nM2Xa2icWRnHf895333u"
    "ubRpm3TT3GmxXkuhSu6CbbNdFWxrFSQVhRVB/SC4oODWDwqTARGp4GXBCy5+UbC67e4Xy7KKYFKK"
    "gqwK26V7aavtttnWbLNtkk5m5n3POY8f3pnJTJM0hQXZ50NO5knO8/8/9zPC2xB9eiKg972y6h/f"
    "PK9y9KR7O/b/L7I6+3VEQQQ0OfGlw2Ehu4Oad05EvOYSh1SJwgBvruQ//d0/FYtIqYSC6D0JTExM"
    "BLOzs+sSGh/vM1NTs/7UY9u3b+nJvEIhC1qnFJYBCyYEn4Ho9i4Z/805VWRyUmVyEhVpJyIt56oM"
    "14zCK98a49zcJbx3XgKMVDh76SGuLo4wuOGm7+u9E82XH3ji71c+duKTH5q7NXx0uLJWBATQQ4cO"
    "HTQm3GMwqIgYA8YEBGLUGACDMRAaQ9ka/+7CrYHvHOz/CgRIKLzx3xpf++OT+LCDQgZyEXTmIRNQ"
    "zkVuNgrlYs4nX/3GD3KvFYtqSiXxACGgBw9+4nGQJ511ePEYMXg1oA6MohiMKF4NTkHVc+2tJfAO"
    "NRFiYq7d6ibxWTZlHEEAoQmIYzAZOt7UYGxznrFKNbtfi8WLU1zOTKrWRERDAJvEj3mPv3DpQqKq"
    "gYikuamfIvVACQRGKNccO3sQMdsDD0DMbLmXREMS6/EqLJQrHCucYFtY0wuyw/67+h5TyxUWpVTy"
    "UKpSSlMQAnhV61VNnMRhYEzQAEzPtL6gTkoN1jmcBzSFx8XMLPThHKCOuWpE79wL7B45Ty3ulr3B"
    "jXCv/oPYFX76xWM//6zLd/w+s3HwFF9/ZN4AqCJpb9WB6+AiqW75c10HqCqoYgCShOuL21AF9Ya5"
    "RWVv7hwiWSrWMG+zcqemItVaT1CJj2Ri/5SbefXXAmkKABStt1PaDoKiKukpgmh6Um8XI4IqOO/w"
    "NeXGUj8CLCUBQeU6D/ddY8kGGLGIN4BQS7wmzsUFJbABmbS0mwy0mecmysrmq4PDQiVGRAk3hqAZ"
    "bpb7CAVuxbBTXmYkW6HiJU2T93ivqKo4b0MgtLnwd80aaOClUaBtPjajgSAK3iv5KODyrZhP/fAM"
    "Oway1CqjlH0/ucixWBU+nH2ZYIN6jwLegHhQ0MiYoBJXlmRo6Pk2AvccQ22kUjIburo5+7rlD6/e"
    "xMaw78EymXwXuWSOBzv+Q9kGCBZtBDm97/NhJlgUPbvhm597Q4tFE7ahrDML23iI0NMRsaGwhcWF"
    "a7z00sPYzAc4snmAgV7HQqJkgkZkQUQR9SqARNGpuql0xqG6Alvr+mXFSnbOK94rhc4tdHTc5q3F"
    "3zKa/JmubA8dJgCvJNbirMU7p+I1nLfVcvzA5ucAnQQfrrC6lufLrrf8bLQpIHk25ft56vrrVP3z"
    "PNI7wq6NW9mUK+CNsOSs74qiYN6Yv2z59hdmnp6YCI6WSi5c1du7pA2y2arStstVPYVMRFkd37v+"
    "Ij+aeZEd2W729mzjI72jfHBjP51hZG2m8xephQngZGrjwIGP/s1Z+9BrFy+4IAiCtsknLfCr/N4a"
    "Da07IYD1jjtxzFJSxVdjv29sp3n/8Nj5Hz/3zPtanQsbN7VuVOqjbtk/WcZq0UvrzGhJR0MiY9gU"
    "ZugV3VQqFVx/L1e6srcxmGkngAqgzlpUtWUB3WW4jdjKSKwUxYghsZbAes05NXJXrzVqwGUzGdna"
    "15dY59Q0l1ETteG3iBA0XV8m5AHfTF1ziTX/z3V0dmZrNklauGtLBOS4qj4zODCYawVuLKNWEmmE"
    "GkZTO8YERkRM6+Ki5a4RCRMbe+fdcYCJiQlz8mT6Ym6+iA4cOLDLqNlj1afDQRGC5pgQY4xbWirv"
    "Xlxcetx770n3iBcR093d/bOurq4XnPNhKOlLhwDAICIamQg17l+nT5/+Z6v3rWLuVqwmQ0Ojnxke"
    "GdOBwaFkYHBYBwaHkuGRUR0YGPr8/dwvFosrcBqDyBeLRTM1NbUqkenpaQtQ6OzYLQrWWUQEVSUM"
    "QoxhT8wMv4K1X9fj4+O+VCr5u/X39b3g0Uc//q58PjCXL1/9ibV2n6q6+tPNIRJEmeivW3s3fzlJ"
    "Ej1z5syF+7G5LoH9+/eH09PT9sjhw09Emdxxax13ymUqlUpbElWVfD5PoVAgn8/hnD/27LMnv9+4"
    "vx6BNXdBX1+fAszfvn3DYX4pQmwkMFEU0igZgwqIqvPcWZhX71xejLnaev8dL+vWwL2KczVZq9je"
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

# Palette slots consumed by extract_Colors
PALETTE_SLOTS = (
    "base00", "base01", "base02", "base03", "base04", "base05",
    "base08", "base0B", "base0C", "base0D", "base0E", "base12", "base14",
)

SCHEME_LIST_VISIBLE_ROWS = 8
SCHEME_GRID_WEIGHT = 1
PREVIEW_GRID_WEIGHT = 4

DEFAULT_SAVE_DIR = os.path.join(
    os.environ.get("APPDATA", os.path.expanduser("~")),
    "DisplayProfileManager", "Themes"
)

# ---------------------------------------------------------------------------
# Color helpers
# ---------------------------------------------------------------------------

_HEX_RGB_RE = re.compile(r"[0-9A-Fa-f]{6}")
_XAML_COLOR_RE = re.compile(r"#[0-9A-F]{6}")

def norm(h) -> str:
    # Palette values become XAML attribute text, so anything other than 6-digit RGB hex is rejected
    if isinstance(h, bool):
        raise ValueError("Palette color must be 6-digit hex RGB.")
    if isinstance(h, int):
        h = str(h).zfill(6)
    h = str(h).strip().lstrip("#").strip()
    if not _HEX_RGB_RE.fullmatch(h):
        raise ValueError("Palette color must be 6-digit hex RGB.")
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

    for slot in PALETTE_SLOTS:
        value = palette.get(slot)
        if value is None:
            continue
        try:
            norm(value)
        except ValueError:
            raise ValueError(f"Invalid {slot} color: expected 6-digit hex RGB.") from None
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
            # Keep slot values verbatim; parse_yaml validates consumed slots for both parsers
            if pending_key:
                key, pending_key = pending_key, None
                if not re.match(r"^[\w-]+\s*:", stripped):
                    palette[key] = stripped.strip('"\'').lstrip("#").strip()
                    continue
            m = re.match(r"^\s+(base[0-9A-Fa-f]{2})\s*:\s*(.*?)\s*$", line)
            if m:
                value = m.group(2).strip('"\'').lstrip("#").strip()
                if value:
                    palette[m.group(1)] = value
                else:
                    pending_key = m.group(1)
                continue
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
    selected_hover = lighten(hover, 0.06) if is_dark else darken(hover, 0.06)

    return dict(
        bg=bg, surf=surf, border=border, comment=comment,
        txt1=txt1, txt2=txt2, accent=accent, sec_acc=sec_acc,
        link=link, danger=danger, success=success,
        bright_danger=bright_danger, bright_success=bright_success,
        titlebar=titlebar, alt_bg=alt_bg, hover=hover, pressed=pressed,
        shadow=shadow, btn_fg=btn_fg, btn_pressed=btn_pressed,
        danger_hover=danger_hover, success_hover=success_hover,
        sec_btn_bg=sec_btn_bg, sec_btn_fg=txt1,
        selected_hover=selected_hover,
        default_tag=blend(accent, bg, 0.4),
        active_tag=blend(success, bg, 0.4),
        is_dark=is_dark,
    )

# ---------------------------------------------------------------------------
# XAML generation
# ---------------------------------------------------------------------------

def build_xaml(scheme: dict, seamless: bool) -> str:
    co = extract_Colors(scheme, seamless)
    # Every interpolated attribute value must remain a validated color literal
    if any(key != "is_dark" and not _XAML_COLOR_RE.fullmatch(value) for key, value in co.items()):
        raise ValueError("Theme colors must be 6-digit hex RGB.")

    name    = scheme.get("name",    "Unknown")
    author  = scheme.get("author",  "Unknown")
    system  = scheme.get("system",  "base16").upper()
    variant = scheme.get("variant", "dark")

    def comment_text(value):
        value = "".join(ch if (ord(ch) in (9, 10, 13) or 0x20 <= ord(ch) <= 0xD7FF or 0xE000 <= ord(ch) <= 0xFFFD or 0x10000 <= ord(ch) <= 0x10FFFF) else " " for ch in str(value))
        return re.sub(r"(?<=-)(?=-)", " ", value)

    name, author, system, variant = (comment_text(value) for value in (name, author, system, variant))

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
        f'    <SolidColorBrush x:Key="ListItemSelectedHoverBackgroundBrush" Color="{co["selected_hover"]}"/>',
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

def _preview_geometry(width: int, height: int) -> dict:
    """Return direct-canvas geometry for the representative DPM preview."""
    title_h = 32
    margin = 16
    status_h = 48
    gap = 16
    details_w = 326

    content_x1 = margin
    content_y1 = title_h + margin
    content_x2 = width - margin
    status_y1 = height - status_h
    content_y2 = status_y1
    details_x2 = content_x2
    details_x1 = details_x2 - details_w
    profiles_x1 = content_x1
    profiles_x2 = details_x1 - gap

    return dict(
        width=width,
        height=height,
        title_h=title_h,
        margin=margin,
        status_h=status_h,
        status_y1=status_y1,
        gap=gap,
        details_w=details_w,
        content_x1=content_x1,
        content_y1=content_y1,
        content_x2=content_x2,
        content_y2=content_y2,
        details_x1=details_x1,
        details_x2=details_x2,
        profiles_x1=profiles_x1,
        profiles_x2=profiles_x2,
    )


DESK_SAVED_LAYOUT = (
    {"name": "Display 1", "x": 0, "y": 0, "width": 2560, "height": 1440},
    {"name": "Display 2", "x": 2560, "y": 0, "width": 2560, "height": 1440},
)

TV_SAVED_LAYOUT = (
    {"name": "Living Room TV", "x": 0, "y": 0, "width": 3840, "height": 2160},
)

def _project_saved_layout(footprints, viewport_width: float,
                          viewport_height: float, inset: float):
    """Uniformly fit logical display rectangles into a preview viewport."""
    if not footprints or viewport_width <= 0 or viewport_height <= 0:
        return []

    min_x = min(item["x"] for item in footprints)
    min_y = min(item["y"] for item in footprints)
    max_x = max(item["x"] + item["width"] for item in footprints)
    max_y = max(item["y"] + item["height"] for item in footprints)
    logical_width = max_x - min_x
    logical_height = max_y - min_y
    safe_inset = max(0.0, inset)
    usable_width = max(0.0, viewport_width - (2 * safe_inset))
    usable_height = max(0.0, viewport_height - (2 * safe_inset))
    if logical_width <= 0 or logical_height <= 0 or usable_width <= 0 or usable_height <= 0:
        return []

    scale = min(usable_width / logical_width, usable_height / logical_height)
    origin_x = safe_inset + ((usable_width - logical_width * scale) / 2)
    origin_y = safe_inset + ((usable_height - logical_height * scale) / 2)

    return [
        dict(
            name=item["name"],
            x=origin_x + ((item["x"] - min_x) * scale),
            y=origin_y + ((item["y"] - min_y) * scale),
            width=item["width"] * scale,
            height=item["height"] * scale,
        )
        for item in footprints
    ]


def draw_preview(canvas: tk.Canvas, co: dict, dpm_img):
    """Draw a representative approximation of the current DPM MainWindow."""
    canvas.delete("all")
    width = canvas.winfo_width()
    height = canvas.winfo_height()
    if width < 20 or height < 20:
        return

    g = _preview_geometry(width, height)
    if g["profiles_x2"] <= g["profiles_x1"] + 160:
        return

    bg = co["bg"]
    surf = co["surf"]
    border = co["border"]
    details_border = co["comment"]
    txt1 = co["txt1"]
    txt2 = co["txt2"]
    comment = co["comment"]
    accent = co["accent"]
    danger = co["bright_danger"]
    success = co["bright_success"]
    sec_bg = co["sec_btn_bg"]
    btn_fg = co["btn_fg"]
    titlebar = co["titlebar"]
    selected_hover = co["selected_hover"]

    header_h = 56
    header_pad = 16
    button_h = 34
    button_r = 4
    list_pad = 8
    card_r = 4
    row_pad_x = 12
    row_pad_y = 8
    visual_size = 32
    visual_column = 44
    apply_size = 32
    details_inner_left = 16
    details_inner_right = 17

    def font(size, weight="normal", family="Segoe UI"):
        return (family, size, weight)

    def rrect(x1, y1, x2, y2, radius, fill, outline="", tags=()):
        if radius <= 0 or x2 - x1 < 2 * radius or y2 - y1 < 2 * radius:
            canvas.create_rectangle(
                x1, y1, x2, y2,
                fill=fill,
                outline=outline if outline else fill,
                tags=tags,
            )
            return
        canvas.create_rectangle(x1 + radius, y1, x2 - radius, y2, fill=fill, outline="", tags=tags)
        canvas.create_rectangle(x1, y1 + radius, x2, y2 - radius, fill=fill, outline="", tags=tags)
        canvas.create_oval(x1, y1, x1 + 2 * radius, y1 + 2 * radius, fill=fill, outline="", tags=tags)
        canvas.create_oval(x2 - 2 * radius, y1, x2, y1 + 2 * radius, fill=fill, outline="", tags=tags)
        canvas.create_oval(x1, y2 - 2 * radius, x1 + 2 * radius, y2, fill=fill, outline="", tags=tags)
        canvas.create_oval(x2 - 2 * radius, y2 - 2 * radius, x2, y2, fill=fill, outline="", tags=tags)
        if outline:
            canvas.create_rectangle(x1 + radius, y1, x2 - radius, y1 + 1, fill=outline, outline="", tags=tags)
            canvas.create_rectangle(x1 + radius, y2 - 1, x2 - radius, y2, fill=outline, outline="", tags=tags)
            canvas.create_rectangle(x1, y1 + radius, x1 + 1, y2 - radius, fill=outline, outline="", tags=tags)
            canvas.create_rectangle(x2 - 1, y1 + radius, x2, y2 - radius, fill=outline, outline="", tags=tags)

    def button(x1, y1, x2, y2, text, bg_color, fg_color, family="Segoe UI"):
        rrect(x1, y1, x2, y2, button_r, bg_color)
        canvas.create_text(
            (x1 + x2) / 2,
            (y1 + y2) / 2,
            text=text,
            fill=fg_color,
            font=font(10, "bold", family),
        )

    def draw_layout(footprints, x1, y1, viewport_width, viewport_height, inset,
                    fill_color, outline_color=None, show_names=False):
        projection = _project_saved_layout(
            footprints, viewport_width, viewport_height, inset
        )
        for footprint in projection:
            fx1 = x1 + footprint["x"]
            fy1 = y1 + footprint["y"]
            fx2 = fx1 + footprint["width"]
            fy2 = fy1 + footprint["height"]
            rrect(
                fx1, fy1, fx2, fy2,
                2 if show_names else 1.5,
                fill_color,
                outline_color or "",
            )
            if show_names and footprint["width"] >= 48 and footprint["height"] >= 24:
                canvas.create_text(
                    (fx1 + fx2) / 2,
                    (fy1 + fy2) / 2,
                    text=footprint["name"],
                    fill=txt1,
                    font=font(8, "bold"),
                )
        return projection

    def draw_profile_row(y1, name, description, display_count, footprints, selected=False,
                         hotkey_text=None, default_badge=False, active_badge=False,
                         show_apply=False):
        x1 = g["profiles_x1"] + list_pad
        x2 = g["profiles_x2"] - list_pad
        row_h = 76
        y2 = y1 + row_h
        fill = selected_hover if selected else surf
        outline = accent if selected else border
        rrect(x1, y1, x2, y2, card_r, fill, outline)

        visual_x1 = x1 + row_pad_x
        visual_y1 = y1 + (row_h - visual_size) / 2
        draw_layout(
            footprints,
            visual_x1,
            visual_y1,
            visual_size,
            visual_size,
            max(1.0, visual_size * 0.08),
            txt1,
        )

        text_x = x1 + row_pad_x + visual_column
        canvas.create_text(
            text_x, y1 + row_pad_y,
            text=name, fill=txt1, font=font(11, "bold"), anchor="nw",
        )

        badge_x = text_x + max(52, len(name) * 7.8) + 10
        if default_badge:
            canvas.create_text(
                badge_x, y1 + row_pad_y + 2,
                text="Default", fill=accent, font=font(8, "bold"), anchor="nw",
            )
            badge_x += 48
        if active_badge:
            canvas.create_text(
                badge_x, y1 + row_pad_y + 2,
                text="Active", fill=success, font=font(8, "bold"), anchor="nw",
            )

        description_y = y1 + row_pad_y + 21
        canvas.create_text(
            text_x, description_y,
            text=description, fill=txt2, font=font(9), anchor="nw",
        )

        metadata_y = y1 + row_pad_y + 43
        canvas.create_text(
            text_x, metadata_y,
            text=display_count,
            fill=comment,
            font=font(8),
            anchor="nw",
        )
        if hotkey_text:
            canvas.create_text(
                text_x + 45, metadata_y,
                text=" • ",
                fill=comment,
                font=font(8),
                anchor="nw",
            )
            canvas.create_text(
                text_x + 56, metadata_y,
                text=hotkey_text,
                fill=success,
                font=font(8, "bold"),
                anchor="nw",
            )

        if show_apply:
            apply_x2 = x2 - row_pad_x
            apply_x1 = apply_x2 - apply_size
            apply_y1 = y1 + (row_h - apply_size) / 2
            button(
                apply_x1, apply_y1, apply_x2, apply_y1 + apply_size,
                "\ue751", accent, btn_fg, family="Segoe MDL2 Assets",
            )
        return y2

    def draw_display_card(y1, name, resolution, dpi, primary=False, hdr=False):
        card_h = 116 if hdr and primary else 112 if primary else 94
        y2 = y1 + card_h
        if y1 >= g["content_y2"] or y2 > g["content_y2"]:
            return y2

        rrect(
            details_left, y1, details_right, y2,
            card_r, surf, details_border,
        )
        line_y = y1 + 8
        canvas.create_text(
            details_left + 8, line_y,
            text=name, fill=txt1, font=font(10, "bold"), anchor="nw",
        )
        line_y += 22
        canvas.create_text(
            details_left + 8, line_y,
            text=f"Resolution: {resolution}",
            fill=txt2, font=font(9), anchor="nw",
        )
        line_y += 20
        if hdr:
            canvas.create_text(
                details_left + 8, line_y,
                text="HDR: On", fill=txt2, font=font(9), anchor="nw",
            )
            line_y += 20
        canvas.create_text(
            details_left + 8, line_y,
            text=f"DPI: {dpi}",
            fill=txt2, font=font(9), anchor="nw",
        )
        line_y += 23
        if primary:
            canvas.create_text(
                details_left + 8, line_y,
                text="Primary Display",
                fill=accent, font=font(8, "bold"), anchor="nw",
            )
        return y2

    canvas.create_rectangle(0, 0, width, height, fill=bg, outline="", tags=("root",))
    canvas.create_rectangle(0, 0, width, g["title_h"], fill=titlebar, outline="", tags=("title",))

    title_x = 12
    if dpm_img:
        canvas.create_image(title_x, g["title_h"] / 2, image=dpm_img, anchor="w")
        title_x += 24
    canvas.create_text(
        title_x,
        g["title_h"] / 2,
        text="Display Profile Manager",
        fill=txt2,
        font=font(9),
        anchor="w",
    )
    for index, symbol in enumerate(["−", "□", "✕"]):
        x1 = width - (3 - index) * 46
        canvas.create_text(x1 + 23, g["title_h"] / 2, text=symbol, fill=txt2, font=font(9))

    rrect(
        g["profiles_x1"], g["content_y1"],
        g["profiles_x2"], g["content_y2"],
        button_r, surf, border,
    )
    profiles_header_y2 = g["content_y1"] + header_h
    canvas.create_text(
        g["profiles_x1"] + header_pad,
        g["content_y1"] + header_h / 2,
        text="Profiles",
        fill=txt1,
        font=font(18, "bold"),
        anchor="w",
    )

    button_y1 = g["content_y1"] + (header_h - button_h) / 2
    button_y2 = button_y1 + button_h
    create_x2 = g["profiles_x2"] - header_pad
    button(create_x2 - 60, button_y1, create_x2, button_y2, "Create", accent, btn_fg)
    button(create_x2 - 144, button_y1, create_x2 - 68, button_y2, "Duplicate", sec_bg, txt1)

    row_y = profiles_header_y2 + list_pad
    row_y = draw_profile_row(
        row_y,
        "Desk Setup",
        "Dual-monitor layout for everyday desktop use.",
        "2 displays",
        DESK_SAVED_LAYOUT,
        default_badge=True,
    )
    draw_profile_row(
        row_y + 6,
        "TV Setup",
        "4K HDR layout for games and media.",
        "1 display",
        TV_SAVED_LAYOUT,
        selected=True,
        hotkey_text="Ctrl + Alt + T",
        active_badge=True,
        show_apply=True,
    )

    rrect(
        g["details_x1"], g["content_y1"],
        g["details_x2"], g["content_y2"],
        button_r, surf, border,
    )
    details_header_y2 = g["content_y1"] + header_h
    canvas.create_text(
        g["details_x1"] + header_pad,
        g["content_y1"] + header_h / 2,
        text="Details",
        fill=txt1,
        font=font(18, "bold"),
        anchor="w",
    )

    delete_x2 = g["details_x2"] - header_pad
    button(delete_x2 - 60, button_y1, delete_x2, button_y2, "Delete", danger, btn_fg)
    button(delete_x2 - 128, button_y1, delete_x2 - 68, button_y2, "Edit", sec_bg, txt1)

    details_left = g["details_x1"] + details_inner_left
    details_right = g["details_x2"] - details_inner_right
    y = details_header_y2 + 8
    canvas.create_text(
        details_left, y,
        text="TV Setup", fill=txt1, font=font(12, "bold"), anchor="nw",
    )

    description_y = y + 26
    canvas.create_text(
        details_left, description_y,
        text="4K HDR layout for games and media.",
        fill=txt2, font=font(9), anchor="nw",
    )

    y = description_y + 38
    canvas.create_text(
        details_left, y,
        text="Displays", fill=txt1, font=font(10, "bold"), anchor="nw",
    )
    y += 23

    saved_layout_y1 = y
    saved_layout_y2 = saved_layout_y1 + 168
    rrect(
        details_left, saved_layout_y1, details_right, saved_layout_y2,
        card_r, surf, details_border,
    )
    layout_width = details_right - details_left
    layout_height = saved_layout_y2 - saved_layout_y1
    draw_layout(
        TV_SAVED_LAYOUT,
        details_left,
        saved_layout_y1,
        layout_width,
        layout_height,
        12,
        surf,
        details_border,
        show_names=True,
    )

    y = saved_layout_y2 + 12
    y = draw_display_card(
        y,
        "Living Room TV",
        "3840x2160 • 120Hz",
        "150%",
        primary=True,
        hdr=True,
    )

    audio_y = y + 16
    audio_rendered = False
    if audio_y + 42 <= g["content_y2"]:
        canvas.create_text(
            details_left, audio_y,
            text="Audio", fill=txt1, font=font(10, "bold"), anchor="nw",
        )
        canvas.create_text(
            details_left, audio_y + 22,
            text="Output: TV Speakers", fill=txt2, font=font(9), anchor="nw",
        )
        y = audio_y + 42
        audio_rendered = True

    hotkey_y = y + 16
    hotkey_rendered = False
    if audio_rendered and hotkey_y + 62 <= g["content_y2"]:
        canvas.create_text(
            details_left, hotkey_y,
            text="Hotkey:", fill=txt1, font=font(10, "bold"), anchor="nw",
        )
        canvas.create_text(
            details_left, hotkey_y + 22,
            text="Ctrl + Alt + T", fill=txt2, font=font(9), anchor="nw",
        )
        canvas.create_text(
            details_left, hotkey_y + 42,
            text="Enabled", fill=success, font=font(9, "bold"), anchor="nw",
        )
        y = hotkey_y + 62
        hotkey_rendered = True

    meta_y = y + 16
    if hotkey_rendered and meta_y + 38 <= g["content_y2"]:
        canvas.create_text(
            details_left, meta_y,
            text="Created: Oct 1, 2026 at 12:00 AM",
            fill=comment, font=font(8), anchor="nw",
        )
        canvas.create_text(
            details_left, meta_y + 18,
            text="Last Modified: Oct 1, 2026 at 12:00 AM",
            fill=comment, font=font(8), anchor="nw",
        )

    canvas.create_rectangle(
        0, g["status_y1"], width, height,
        fill=bg, outline="", tags=("status",),
    )
    status_mid = g["status_y1"] + g["status_h"] / 2
    rrect(16, status_mid - 16, 48, status_mid + 16, button_r, sec_bg)
    canvas.create_text(
        32, status_mid,
        text="\ue72c", fill=txt1, font=font(10, family="Segoe MDL2 Assets"),
    )
    canvas.create_text(
        56, status_mid,
        text="Ready", fill=comment, font=font(9), anchor="w",
    )
    button(
        width - 86, status_mid - 16, width - 16, status_mid + 16,
        "Settings", accent, btn_fg,
    )
    button(
        width - 194, status_mid - 16, width - 94, status_mid + 16,
        "Open Folder", sec_bg, txt1,
    )


# ---------------------------------------------------------------------------
# Async request ownership and file output
# ---------------------------------------------------------------------------

class RequestGate:
    def __init__(self):
        self._generation = 0
        self._owner: str | None = None

    def begin(self, owner: str | None = None) -> tuple[int, str | None]:
        self._generation += 1
        self._owner = owner
        return self._generation, owner

    def invalidate(self):
        self.begin()

    def owns(self, request: tuple[int, str | None]) -> bool:
        return request == (self._generation, self._owner)


MAX_THEME_FILE_BYTES = 1024 * 1024


def write_theme_file(path: str, content: str):
    with open(path, "w", encoding="utf-8") as stream:
        stream.write(content)


def _theme_path_key(path: str) -> str:
    return os.path.normcase(os.path.abspath(path))


def remember_builder_theme_write(writes: dict, path: str, content: str):
    # Mirrors write_theme_file's text-mode newline translation
    data = content.replace("\n", os.linesep).encode("utf-8")
    writes[_theme_path_key(path)] = hashlib.sha256(data).hexdigest()


def is_builder_theme_write(writes: dict, path: str) -> bool:
    expected = writes.get(_theme_path_key(path))
    if expected is None:
        return False
    try:
        with open(path, "rb") as stream:
            data = stream.read(MAX_THEME_FILE_BYTES + 1)
    except OSError:
        return False
    return len(data) <= MAX_THEME_FILE_BYTES and hashlib.sha256(data).hexdigest() == expected


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
        self._current_scheme_authoritative   = False
        self._list_requests                  = RequestGate()
        self._selection_requests             = RequestGate()
        self._builder_theme_writes: dict[str, str] = {}
        self._token    = tk.StringVar()
        self._seamless = tk.BooleanVar(value=True)

        self._theme32   = None
        self._theme16   = None
        self._dpm_photo = None
        
        self._load_icons()
        self._build_ui()
        self._load_scheme_list()
        self._init_monitor()

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
        self.rowconfigure(2, weight=SCHEME_GRID_WEIGHT)
        self.rowconfigure(3, weight=PREVIEW_GRID_WEIGHT)

        # ── Row 0: GitHub token ──────────────────────────
        tok = ttk.Frame(self, padding=(8, 8, 8, 2))
        tok.grid(row=0, column=0, sticky="ew")
        tok.columnconfigure(1, weight=1)

        token_lbl = ttk.Label(tok, text="GitHub token (optional):")
        token_lbl.grid(row=0, column=0, sticky="w")
        Tooltip(token_lbl, TOKEN_TOOLTIP)

        token_entry = ttk.Entry(tok, textvariable=self._token, show="*")
        token_entry.grid(row=0, column=1, sticky="ew", padx=(8, 0))
        token_entry.bind("<Return>", lambda _: self._load_scheme_list())
        Tooltip(token_entry, TOKEN_TOOLTIP)

        reload_btn = ttk.Button(tok, text="Reload schemes",
                                command=self._load_scheme_list)
        reload_btn.grid(row=0, column=2, padx=(8, 0))

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

        list_surface = tk.Frame(list_frame, bd=1, relief=tk.SUNKEN)
        list_surface.grid(row=0, column=0, sticky="nsew")
        list_surface.columnconfigure(0, weight=1)
        list_surface.rowconfigure(0, weight=1)

        self._listbox = tk.Listbox(
            list_surface,
            height=SCHEME_LIST_VISIBLE_ROWS,
            activestyle="dotbox",
            selectmode=tk.SINGLE,
            font=("Consolas", 9),
            bd=0,
            highlightthickness=0,
        )

        lsb = ttk.Scrollbar(
            list_surface,
            orient=tk.VERTICAL,
            command=self._listbox.yview,
        )

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
        if not self._current_scheme:
            return
        if not self._current_scheme_authoritative:
            self._redraw_preview()
            return
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
        request = self._list_requests.begin()
        self._selection_requests.invalidate()
        token = self._token.get()
        self._status.set("Fetching scheme list from GitHub…")
        self._listbox.delete(0, tk.END)
        self._schemes.clear()
        self._current_xaml = ""
        self._current_scheme = {}
        self._current_scheme_authoritative = False
        self._btn_save.configure(state=tk.DISABLED)
        self._set_xaml_text("")
        self._canvas.delete("all")
        threading.Thread(target=self._fetch_list_thread,
                         args=(request, token), daemon=True).start()

    def _fetch_list_thread(self, request: tuple[int, str | None], token: str):
        schemes: dict[str, dict] = {}
        errors: list[str] = []
        for system in SYSTEMS:
            try:
                entries = github_api_get(system, token)
                for entry in entries:
                    if entry.get("type") == "file" and entry["name"].endswith(".yaml"):
                        slug = entry["name"][:-5]
                        key = f"{system}/{slug}"
                        schemes[key] = {
                            "system": system,
                            "slug": slug,
                            "name": slug.replace("-", " ").title(),
                            "path": f"{system}/{entry['name']}",
                        }
            except Exception as exc:
                errors.append(f"{system}: {exc}")
        self.after(0, self._populate_list, request, schemes, errors)

    def _populate_list(self, request: tuple[int, str | None],
                       schemes: dict[str, dict], errors: list[str]):
        if not self._list_requests.owns(request):
            return
        self._schemes = schemes
        if errors and not schemes:
            self._status.set("Failed to load — " + "; ".join(errors))
        elif errors:
            self._status.set(f"Loaded {len(schemes)} schemes (partial: {'; '.join(errors)})")
        else:
            self._status.set(f"Loaded {len(schemes)} schemes.")
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
        key = self._display_keys[sel[0]]
        meta = self._schemes[key]
        request = self._selection_requests.begin(key)
        token = self._token.get()
        self._current_xaml = ""
        self._current_scheme_authoritative = False
        self._btn_save.configure(state=tk.DISABLED)
        self._set_xaml_text("")
        self._status.set(f"Fetching {meta['path']}…")
        threading.Thread(target=self._fetch_and_convert,
                         args=(meta, token, request), daemon=True).start()

    def _fetch_and_convert(self, meta: dict, token: str,
                           request: tuple[int, str | None]):
        try:
            raw = fetch_raw(meta["path"], token)
            scheme = parse_yaml(raw)
            scheme.setdefault("name", meta["name"])
            scheme.setdefault("system", meta["system"])
            label = (f"Converted: {scheme.get('name', meta['slug'])}"
                     f" ({scheme.get('system', meta['system'])})")
            self.after(0, self._complete_selection, request, scheme, label)
        except Exception as exc:
            self.after(0, self._selection_error, request, f"Error: {exc}")

    def _complete_selection(self, request: tuple[int, str | None],
                            scheme: dict, label: str):
        if self._selection_requests.owns(request):
            self._refresh_output(scheme, label)

    def _selection_error(self, request: tuple[int, str | None], message: str):
        if self._selection_requests.owns(request):
            self._status.set(message)

    def _refresh_output(self, scheme: dict, status: str = ""):
        """Update XAML text, preview canvas, and Save authority together."""
        seamless = self._seamless.get()

        try:
            xaml = build_xaml(scheme, seamless)
        except Exception as exc:
            self._current_xaml = ""
            self._current_scheme_authoritative = False
            self._btn_save.configure(state=tk.DISABLED)
            self._set_xaml_text("")
            self._status.set(f"XAML generation error: {exc}")
            return

        self._current_scheme = scheme
        self._current_scheme_authoritative = True
        self._current_xaml = xaml
        self._set_xaml_text(xaml)
        self._btn_save.configure(state=tk.NORMAL)

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
        FILE_LIST_DIRECTORY = 0x0001
        FILE_SHARE_READ = 0x00000001
        FILE_SHARE_WRITE = 0x00000002
        FILE_SHARE_DELETE = 0x00000004
        OPEN_EXISTING = 3
        FILE_FLAG_BACKUP_SEMANTICS = 0x02000000
        FILE_NOTIFY_CHANGE_FILE_NAME = 0x00000001
        FILE_NOTIFY_CHANGE_LAST_WRITE = 0x00000010
        FILE_ACTION_ADDED = 0x00000001
        FILE_ACTION_REMOVED = 0x00000002
        FILE_ACTION_MODIFIED = 0x00000003
        FILE_ACTION_RENAMED_OLD_NAME = 0x00000004
        FILE_ACTION_RENAMED_NEW_NAME = 0x00000005

        kernel32 = ctypes.windll.kernel32
        kernel32.CreateFileW.restype = wintypes.HANDLE
        kernel32.ReadDirectoryChangesW.restype = wintypes.BOOL
        kernel32.CloseHandle.restype = wintypes.BOOL

        os.makedirs(DEFAULT_SAVE_DIR, exist_ok=True)

        handle = kernel32.CreateFileW(
            DEFAULT_SAVE_DIR,
            FILE_LIST_DIRECTORY,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            None,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            None,
        )

        if handle == wintypes.HANDLE(-1).value:
            return

        try:
            buffer = ctypes.create_string_buffer(64 * 1024)
            watch_filter = (
                FILE_NOTIFY_CHANGE_FILE_NAME
                | FILE_NOTIFY_CHANGE_LAST_WRITE
            )
            interesting_actions = {
                FILE_ACTION_ADDED,
                FILE_ACTION_MODIFIED,
                FILE_ACTION_RENAMED_NEW_NAME,
            }

            while True:
                bytes_returned = wintypes.DWORD()
                if not kernel32.ReadDirectoryChangesW(
                    handle,
                    buffer,
                    len(buffer),
                    False,
                    watch_filter,
                    ctypes.byref(bytes_returned),
                    None,
                    None,
                ):
                    break

                offset = 0
                total = bytes_returned.value
                while offset < total:
                    next_offset = int.from_bytes(buffer[offset:offset + 4], "little")
                    action = int.from_bytes(buffer[offset + 4:offset + 8], "little")
                    name_length = int.from_bytes(buffer[offset + 8:offset + 12], "little")
                    name = ctypes.wstring_at(
                        ctypes.addressof(buffer) + offset + 12,
                        name_length // 2,
                    )

                    if action in interesting_actions and name.lower().endswith(".xaml"):
                        self.after(0, self._on_theme_file_changed, os.path.join(DEFAULT_SAVE_DIR, name))

                    if next_offset == 0:
                        break
                    offset += next_offset
        finally:
            kernel32.CloseHandle(handle)

    def _on_theme_file_changed(self, path: str):
        # Other processes can drop arbitrary XAML here; only unchanged Builder output is applied
        if is_builder_theme_write(self._builder_theme_writes, path):
            self._apply_to_dpm(path)
        else:
            self._status.set(f"Detected \u2192 {os.path.basename(path)} (not applied: not saved by Theme Builder)")

    def _apply_to_dpm(self, saved_path: str):
        """Signals DPM to apply the theme or refresh the list."""

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
                creationflags=0x08000000 | 0x00000008,   # CREATE_NO_WINDOW | DETACHED_PROCESS
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            
            status_text = f"Applied '{theme_name}'" if saved_dir == themes_dir else "Refreshed themes"
            self._status.set(f"Synced \u2192 {os.path.basename(saved_path)} ({status_text})")
        except Exception:
            self._status.set(f"Synced \u2192 {os.path.basename(saved_path)}")

    def _find_dpm_exe(self) -> "str | None":
        exe_name   = "DisplayProfileManager.exe"
        prog_files = os.environ.get("PROGRAMFILES", "C:\\Program Files")
        local_app  = os.environ.get("LOCALAPPDATA", "")
        script_dir = os.path.dirname(os.path.abspath(__file__))
        candidates = [
            os.path.join(prog_files, "Display Profile Manager", exe_name),
            os.path.join(local_app,  "Programs", "Display Profile Manager", exe_name),
            os.path.join(local_app,  "Programs", "DisplayProfileManager", exe_name),
            os.path.join(local_app,  "DisplayProfileManager", exe_name),
            os.path.join(script_dir, exe_name),
            os.path.join(script_dir, "..", exe_name),
        ]
        try:
            key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\DisplayProfileManager")
            install_path, _ = winreg.QueryValueEx(key, "InstallPath")
            if install_path:
                candidates.insert(0, install_path)
        except Exception:
            pass
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

        try:
            write_theme_file(path, self._current_xaml)
        except OSError as exc:
            messagebox.showerror("Save failed", str(exc))
            return

        remember_builder_theme_write(self._builder_theme_writes, path, self._current_xaml)
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
            self._selection_requests.invalidate()
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
