"""
DPM Shortcut Builder
Generates PowerShell-based shortcuts for launching applications with
automatic DPM display profile switching.

Standalone — profile enumeration, shortcut CRUD, and the PS1 runtime template
are all embedded directly; no sidecar files are required at runtime.

Requirements: Python 3.8+ with Tkinter (standard on Windows).
  No third-party packages required.
  pywin32 is used for .lnk generation if available; omitted otherwise.

Run as .pyw to suppress the console window on Windows.
"""

# ---------------------------------------------------------------------------
# Embedded icons (base64 PNG — generated from bundled .ico files)
# ---------------------------------------------------------------------------

# DPM Shortcut Builder window icon (16x16)
BUILDER_ICON_16_B64 = (
    "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAACtklEQVR4nJWTT2hcVRTG"
    "f+fe+968ZN5MykhMkTSGoHYRuzLgH8SxFFKhYME4AUEXunDjLuJCXEwHuhE3iuDGpQsp"
    "A4oDgiiVulCh+A/E2IWM2k4STOM0nXEmM/Pm3uNCIjWkC3/Lw/kOH+d8RwAqlYrlEOr1"
    "uj9Yq1bVAaFWkwAghwn30Q9efIp44nlG3QFTRuDPC3Lqw/qtPQKwsrKylCTplFUNLkko"
    "5qzd6oTx64/5V+9eTJa/uXyC728sY6TX+rG3+G7kufjGm+e+rlfOiTtz5smnd6636/1+"
    "Cxc5jLHEkaX9V8YX0yWePTblP1m/l8+35nlk6o/Z08e2az+N40eF2jL1Gk41zO92brK5"
    "uZHFcc4CRNZwvTMgW1oUyUp2d1BCVDnVvxBObrf8yfjow2tr71zyRl5zwEhEiKLIRM4Z"
    "gMhZbOQJYYSMYq525ygO2hzPb5lOljfie1GSy5XHTmadqgoKqoqqApB5T3Ei5u1Lv/LR"
    "d3u0C3lOR19R1C69zIViHJtO98ZvN+8/3nAAyj9C/Xf9EBnYHeXYurbFUB/ilfuWSEsz"
    "aAhhQqwZqf9s7uXVPUfYV/73ogo4A6Vinu7eBud/2eZye4HH75g1D07PeY4U31cQAwEf"
    "/Hjsx/4AeB8YDEca2dRfc8a/tfNz9syVi/rCzrebG2tnvxRQF3ygkKZuOBziXITILRYE"
    "jBgREVsyBitisZZemiTr6+sAON/39cnJ/In5ycK0SlARY9GQ9nr9MiLirGsVCvkfQFSM"
    "GcdRbMIoa6yuro6q1ao5NMq5XO6emaN3XQEIGj5uXf397O3iLoBUKhXTbDZNmqaaTqYv"
    "9Qd7z3W7vQdUFQSduXPmvSSJzltrm81m0ywsLIT9R5MDw7RcLj8R2/hIFrKuqhVjxMRx"
    "XMrl3KeNRmNzv+92jv43fwNKITCy+ZakAQAAAABJRU5ErkJggg=="
)

# DPM Shortcut Builder window icon (32x32)
BUILDER_ICON_32_B64 = (
    "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAGjElEQVR4nM2Xa2icWRnH"
    "f895333uubRpm3TT3GmxXkuhSu6CbbNdFWxrFSQVhRVB/SC4oODWDwqTARGp4GXBCy5+"
    "UbC67e4Xy7KKYFKKgqwK26V7aavtttnWbLNtkk5m5n3POY8f3pnJTJM0hQXZ50NO5knO"
    "8/8/9zPC2xB9eiKg972y6h/fPK9y9KR7O/b/L7I6+3VEQQQ0OfGlw2Ehu4Oad05EvOYS"
    "h1SJwgBvruQ//d0/FYtIqYSC6D0JTExMBLOzs+sSGh/vM1NTs/7UY9u3b+nJvEIhC1qn"
    "FJYBCyYEn4Ho9i4Z/805VWRyUmVyEhVpJyIt56oM14zCK98a49zcJbx3XgKMVDh76SGu"
    "Lo4wuOGm7+u9E82XH3ji71c+duKTH5q7NXx0uLJWBATQQ4cOHTQm3GMwqIgYA8YEBGLU"
    "GACDMRAaQ9ka/+7CrYHvHOz/CgRIKLzx3xpf++OT+LCDQgZyEXTmIRNQzkVuNgrlYs4n"
    "X/3GD3KvFYtqSiXxACGgBw9+4nGQJ511ePEYMXg1oA6MohiMKF4NTkHVc+2tJfAONRFi"
    "Yq7d6ibxWTZlHEEAoQmIYzAZOt7UYGxznrFKNbtfi8WLU1zOTKrWRERDAJvEj3mPv3Dp"
    "QqKqgYikuamfIvVACQRGKNccO3sQMdsDD0DMbLmXREMS6/EqLJQrHCucYFtY0wuyw/67"
    "+h5TyxUWpVTyUKpSSlMQAnhV61VNnMRhYEzQAEzPtL6gTkoN1jmcBzSFx8XMLPThHKCO"
    "uWpE79wL7B45Ty3ulr3BjXCv/oPYFX76xWM//6zLd/w+s3HwFF9/ZN4AqCJpb9WB6+Ai"
    "qW75c10HqCqoYgCShOuL21AF9Ya5RWVv7hwiWSrWMG+zcqemItVaT1CJj2Ri/5SbefXX"
    "AmkKABStt1PaDoKiKukpgmh6Um8XI4IqOO/wNeXGUj8CLCUBQeU6D/ddY8kGGLGIN4BQ"
    "S7wmzsUFJbABmbS0mwy0mecmysrmq4PDQiVGRAk3hqAZbpb7CAVuxbBTXmYkW6HiJU2T"
    "93ivqKo4b0MgtLnwd80aaOClUaBtPjajgSAK3iv5KODyrZhP/fAMOway1CqjlH0/ucix"
    "WBU+nH2ZYIN6jwLegHhQ0MiYoBJXlmRo6Pk2AvccQ22kUjIburo5+7rlD6/exMaw78Ey"
    "mXwXuWSOBzv+Q9kGCBZtBDm97/NhJlgUPbvhm597Q4tFE7ahrDML23iI0NMRsaGwhcWF"
    "a7z00sPYzAc4snmAgV7HQqJkgkZkQUQR9SqARNGpuql0xqG6Alvr+mXFSnbOK94rhc4t"
    "dHTc5q3F3zKa/JmubA8dJgCvJNbirMU7p+I1nLfVcvzA5ucAnQQfrrC6lufLrrf8bLQp"
    "IHk25ft56vrrVP3zPNI7wq6NW9mUK+CNsOSs74qiYN6Yv2z59hdmnp6YCI6WSi5c1du7"
    "pA2y2arStstVPYVMRFkd37v+Ij+aeZEd2W729mzjI72jfHBjP51hZG2m8xephQngZGrj"
    "wIGP/s1Z+9BrFy+4IAiCtsknLfCr/N4aDa07IYD1jjtxzFJSxVdjv29sp3n/8Nj5Hz/3"
    "zPtanQsbN7VuVOqjbtk/WcZq0UvrzGhJR0MiY9gUZugV3VQqFVx/L1e6srcxmGkngAqg"
    "zlpUtWUB3WW4jdjKSKwUxYghsZbAes05NXJXrzVqwGUzGdna15dY59Q0l1ETteG3iBA0"
    "XV8m5AHfTF1ziTX/z3V0dmZrNklauGtLBOS4qj4zODCYawVuLKNWEmmEGkZTO8YERkRM"
    "6+Ki5a4RCRMbe+fdcYCJiQlz8mT6Ym6+iA4cOLDLqNlj1afDQRGC5pgQY4xbWirvXlxc"
    "etx770n3iBcR093d/bOurq4XnPNhKOlLhwDAICIamQg17l+nT5/+Z6v3rWLuVqwmQ0Oj"
    "nxkeGdOBwaFkYHBYBwaHkuGRUR0YGPr8/dwvFosrcBqDyBeLRTM1NbUqkenpaQtQ6OzY"
    "LQrWWUQEVSUMQoxhT8wMv4K1X9fj4+O+VCr5u/X39b3g0Uc//q58PjCXL1/9ibV2n6q6"
    "+tPNIRJEmeivW3s3fzlJEj1z5syF+7G5LoH9+/eH09PT9sjhw09Emdxxax13ymUqlUpb"
    "ElWVfD5PoVAgn8/hnD/27LMnv9+4vx6BNXdBX1+fAszfvn3DYX4pQmwkMFEU0igZgwqI"
    "qvPcWZhX71xejLnaev8dL+vWwL2KczVZq9jesfI/JnYGXiJS0eQAAAAASUVORK5CYII="
)

# ---------------------------------------------------------------------------
# Standard library imports
# ---------------------------------------------------------------------------

import base64
import ctypes
import json
import os
import re
import shutil
import warnings
import winreg
from typing import Dict, List, NamedTuple, Optional
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

# ---------------------------------------------------------------------------
# DPM profile enumeration
# ---------------------------------------------------------------------------

PROFILES_DIR = os.path.join(
    os.environ.get("APPDATA", os.path.expanduser("~")),
    "DisplayProfileManager", "Profiles",
)


class DpmProfile(NamedTuple):
    id:   str   # GUID string — authoritative identity
    name: str   # display name
    path: str   # absolute path to the .dpm file


def get_profiles_dir() -> str:
    """Return the canonical Profiles directory path (not guaranteed to exist)."""
    return PROFILES_DIR


def load_profiles(profiles_dir: Optional[str] = None) -> List[DpmProfile]:
    """
    Enumerate *.dpm files in profiles_dir (defaults to PROFILES_DIR), parse
    each as JSON, and return a list of DpmProfile named tuples sorted by name.

    Files that are missing, unreadable, or malformed are skipped with a
    warning and never raise. Returns an empty list if the directory does not exist.
    """
    directory = profiles_dir or PROFILES_DIR

    if not os.path.isdir(directory):
        return []

    profiles: List[DpmProfile] = []

    for filename in os.listdir(directory):
        if not filename.lower().endswith(".dpm"):
            continue

        filepath = os.path.join(directory, filename)

        try:
            with open(filepath, "r", encoding="utf-8") as fh:
                data = json.load(fh)
        except (OSError, json.JSONDecodeError) as exc:
            warnings.warn(f"DPM Shortcut Builder: skipping {filename!r}: {exc}")
            continue

        if not isinstance(data, dict):
            warnings.warn(f"DPM Shortcut Builder: skipping {filename!r}: root is not an object")
            continue

        profile_id   = data.get("id")
        profile_name = data.get("name")

        if not profile_id or not profile_name:
            warnings.warn(
                f"DPM Shortcut Builder: skipping {filename!r}: "
                f"missing id ({profile_id!r}) or name ({profile_name!r})"
            )
            continue

        profiles.append(DpmProfile(
            id=str(profile_id).strip(),
            name=str(profile_name).strip(),
            path=os.path.normpath(filepath),
        ))

    return sorted(profiles, key=lambda p: p.name.lower())


# ---------------------------------------------------------------------------
# Shortcut folder management
# ---------------------------------------------------------------------------

SHORTCUTS_DIR = os.path.join(
    os.environ.get("APPDATA", os.path.expanduser("~")),
    "DisplayProfileManager", "Shortcuts",
)

# Characters illegal in Windows folder/file names
_ILLEGAL_RE = re.compile(r'[\\/:*?"<>|]')


class ShortcutEntry(NamedTuple):
    name:     str   # subfolder stem == shortcut display name
    folder:   str   # absolute path to the subfolder
    ps1_path: str   # absolute path to the .ps1 (may not exist yet)
    lnk_path: str   # absolute path to the .lnk (may not exist yet)


def get_shortcuts_dir() -> str:
    """Return the canonical Shortcuts directory path (not guaranteed to exist)."""
    return SHORTCUTS_DIR


def ensure_shortcuts_dir(shortcuts_dir: Optional[str] = None) -> str:
    """Create the Shortcuts directory if absent. Returns the path."""
    directory = shortcuts_dir or SHORTCUTS_DIR
    os.makedirs(directory, exist_ok=True)
    return directory


def load_shortcuts(shortcuts_dir: Optional[str] = None) -> List[ShortcutEntry]:
    """
    Enumerate shortcut subfolders and return a list of ShortcutEntry sorted
    by name (case-insensitive). Subfolders that contain no matching .ps1
    are included — the builder may show them as broken entries.
    Non-directory entries are ignored.
    """
    directory = shortcuts_dir or SHORTCUTS_DIR

    if not os.path.isdir(directory):
        return []

    entries: List[ShortcutEntry] = []

    for item in os.listdir(directory):
        subfolder = os.path.join(directory, item)
        if not os.path.isdir(subfolder):
            continue
        entries.append(_entry_for(item, subfolder))

    return sorted(entries, key=lambda e: e.name.lower())


def _entry_for(name: str, folder: str) -> ShortcutEntry:
    return ShortcutEntry(
        name=name,
        folder=os.path.normpath(folder),
        ps1_path=os.path.normpath(os.path.join(folder, name + ".ps1")),
        lnk_path=os.path.normpath(os.path.join(folder, name + ".lnk")),
    )


def validate_name(name: str) -> Optional[str]:
    """
    Return an error string if name is not a valid shortcut name, else None.
    A valid name is non-empty, contains no filesystem-illegal characters,
    and is not a Windows reserved device name.
    """
    name = name.strip()
    if not name:
        return "Shortcut name cannot be empty."
    if _ILLEGAL_RE.search(name):
        return r'Name contains illegal characters: \ / : * ? " < > |'
    reserved = {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    }
    if name.upper() in reserved:
        return f"'{name}' is a reserved Windows device name."
    return None


def create_shortcut_folder(
    name: str,
    shortcuts_dir: Optional[str] = None,
) -> ShortcutEntry:
    """
    Create (if absent) the subfolder for a new shortcut.
    Raises ValueError if name is invalid.
    Raises FileExistsError if a shortcut with that name already exists.
    """
    err = validate_name(name)
    if err:
        raise ValueError(err)

    directory = ensure_shortcuts_dir(shortcuts_dir)
    folder    = os.path.join(directory, name)

    if os.path.exists(folder):
        raise FileExistsError(f"A shortcut named '{name}' already exists.")

    os.makedirs(folder)
    return _entry_for(name, folder)


def delete_shortcut(
    name: str,
    shortcuts_dir: Optional[str] = None,
) -> None:
    """
    Remove the subfolder and all its contents for the named shortcut.
    Raises FileNotFoundError if the subfolder does not exist.
    """
    directory = shortcuts_dir or SHORTCUTS_DIR
    folder    = os.path.join(directory, name)

    if not os.path.isdir(folder):
        raise FileNotFoundError(f"No shortcut folder found for '{name}'.")

    shutil.rmtree(folder)


def rename_shortcut(
    old_name: str,
    new_name: str,
    shortcuts_dir: Optional[str] = None,
) -> ShortcutEntry:
    """
    Rename the subfolder and the .ps1 / .lnk files inside it.
    Returns the updated ShortcutEntry.
    Raises ValueError if new_name is invalid.
    Raises FileNotFoundError if old_name does not exist.
    Raises FileExistsError if new_name already exists.
    """
    err = validate_name(new_name)
    if err:
        raise ValueError(err)

    directory  = shortcuts_dir or SHORTCUTS_DIR
    old_folder = os.path.join(directory, old_name)
    new_folder = os.path.join(directory, new_name)

    if not os.path.isdir(old_folder):
        raise FileNotFoundError(f"No shortcut folder found for '{old_name}'.")
    if os.path.exists(new_folder):
        raise FileExistsError(f"A shortcut named '{new_name}' already exists.")

    for ext in (".ps1", ".lnk"):
        old_file = os.path.join(old_folder, old_name + ext)
        new_file = os.path.join(old_folder, new_name + ext)
        if os.path.isfile(old_file):
            os.rename(old_file, new_file)

    os.rename(old_folder, new_folder)
    return _entry_for(new_name, new_folder)


def duplicate_shortcut(
    source_name: str,
    new_name: str,
    shortcuts_dir: Optional[str] = None,
) -> ShortcutEntry:
    """
    Copy the source shortcut subfolder to a new subfolder named new_name,
    renaming the .ps1 and .lnk inside to match.
    Returns the new ShortcutEntry.
    """
    err = validate_name(new_name)
    if err:
        raise ValueError(err)

    directory  = shortcuts_dir or SHORTCUTS_DIR
    src_folder = os.path.join(directory, source_name)
    dst_folder = os.path.join(directory, new_name)

    if not os.path.isdir(src_folder):
        raise FileNotFoundError(f"No shortcut folder found for '{source_name}'.")
    if os.path.exists(dst_folder):
        raise FileExistsError(f"A shortcut named '{new_name}' already exists.")

    shutil.copytree(src_folder, dst_folder)

    for ext in (".ps1", ".lnk"):
        old_file = os.path.join(dst_folder, source_name + ext)
        new_file = os.path.join(dst_folder, new_name + ext)
        if os.path.isfile(old_file):
            os.rename(old_file, new_file)

    return _entry_for(new_name, dst_folder)


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

RESTORE_DYNAMIC = "__dynamic__"
RESTORE_NONE    = "__none__"

# PS1 runtime template — embedded directly; no sidecar file required at runtime.
PS1_TEMPLATE = r"""\
# DPM Shortcut: {{SHORTCUT_NAME}}
# Generated by DPM Shortcut Builder. Edit via the builder, not by hand.

$profileId      = '{{PROFILE_ID}}'
$profileName    = '{{PROFILE_NAME}}'   # display only; ID is authoritative
$restoreMode    = '{{RESTORE_MODE}}'   # 'dynamic' | 'none' | '<guid>'
$restoreId      = '{{RESTORE_ID}}'     # populated when restoreMode is a guid
$pipeName       = 'DPM_ProfilePipe'
$dpmExe         = ''                   # resolved at runtime if empty

$preStart = @(
{{PRE_START_ENTRIES}}
)

$target = @{
    Path             = '{{TARGET_PATH}}'
    Args             = '{{TARGET_ARGS}}'
    WorkingDirectory = '{{TARGET_WORKDIR}}'
}

# --- runtime follows; not edited by builder ---

function Find-DpmExe {
    if ($dpmExe -and (Test-Path $dpmExe)) { return $dpmExe }
    $candidates = @(
        'C:\Program Files\Display Profile Manager\DisplayProfileManager.exe',
        (Join-Path $PSScriptRoot 'DisplayProfileManager.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\DisplayProfileManager\DisplayProfileManager.exe'),
        (Join-Path $env:LOCALAPPDATA 'DisplayProfileManager\DisplayProfileManager.exe')
    )
    try {
        $regPath = 'HKCU:\Software\DisplayProfileManager'
        if (Test-Path $regPath) {
            $regExe = (Get-ItemProperty $regPath -ErrorAction Stop).InstallPath
            if ($regExe) { $candidates = @($regExe) + $candidates }
        }
    } catch {}
    foreach ($c in $candidates) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

function Get-ActiveProfileId {
    # Try IPC pipe first
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName,
            [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(500)
        $writer = New-Object System.IO.StreamWriter($pipe)
        $reader = New-Object System.IO.StreamReader($pipe)
        $writer.AutoFlush = $true
        $writer.WriteLine('QUERY_ACTIVE')
        $response = $reader.ReadLine()
        $pipe.Dispose()
        if ($response -match '^ACTIVE:(.+)$') { return $Matches[1].Trim() }
    } catch {}
    # Fall back to DPM state file
    try {
        $statePath = Join-Path $env:APPDATA 'DisplayProfileManager\Settings.json'
        if (Test-Path $statePath) {
            $state = Get-Content $statePath -Raw | ConvertFrom-Json
            if ($state.currentProfileId) { return $state.currentProfileId }
        }
    } catch {}
    return $null
}

function Invoke-DpmApply([string]$id) {
    # Try IPC pipe first
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $pipeName,
            [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(500)
        $writer = New-Object System.IO.StreamWriter($pipe)
        $reader = New-Object System.IO.StreamReader($pipe)
        $writer.AutoFlush = $true
        $writer.WriteLine("PROFILE:$id")
        $response = $reader.ReadLine()
        $pipe.Dispose()
        if ($response -eq 'OK') { return $true }
    } catch {}
    # Fall back to --headless CLI
    $exe = Find-DpmExe
    if (-not $exe) {
        Write-Warning "DPM Shortcut: DisplayProfileManager.exe not found. Launching target without profile switch."
        return $false
    }
    $result = Start-Process -FilePath $exe -ArgumentList @('--headless', $id) -Wait -PassThru -WindowStyle Hidden
    return ($result.ExitCode -eq 0)
}

# --- main ---

$dynamicRestoreId = $null
if ($restoreMode -eq 'dynamic') {
    $dynamicRestoreId = Get-ActiveProfileId
}

$applyOk = Invoke-DpmApply $profileId
if (-not $applyOk) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "Failed to apply display profile '$profileName'.`nThe application will not be launched.",
        'DPM Shortcut', 'OK', 'Error') | Out-Null
    exit 1
}

foreach ($app in $preStart) {
    try {
        if ($app.Delay -gt 0) { Start-Sleep -Seconds $app.Delay }
        $pArgs = @{ FilePath = $app.Path; WindowStyle = 'Normal' }
        if ($app.Args) { $pArgs.ArgumentList = $app.Args }
        Start-Process @pArgs
    } catch {
        Write-Warning "DPM Shortcut: pre-start '$($app.Path)' failed: $_"
    }
}

$targetProc = $null
try {
    $tArgs = @{ FilePath = $target.Path; PassThru = $true }
    if ($target.Args)             { $tArgs.ArgumentList    = $target.Args }
    if ($target.WorkingDirectory) { $tArgs.WorkingDirectory = $target.WorkingDirectory }
    $targetProc = Start-Process @tArgs
} catch {
    Write-Warning "DPM Shortcut: failed to launch target '$($target.Path)': $_"
}

if ($targetProc) {
    $targetProc.WaitForExit()
}

foreach ($app in $preStart) {
    if ($app.KillOnExit) {
        Get-Process | Where-Object { $_.Path -eq $app.Path } | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

if ($restoreMode -ne 'none') {
    $restoreTarget = if ($restoreMode -eq 'dynamic') { $dynamicRestoreId } else { $restoreId }
    if ($restoreTarget) {
        Invoke-DpmApply $restoreTarget | Out-Null
    }
}
"""

# Full path to powershell.exe — required for .lnk TargetPath, icon fallback,
# and the VBS command string. WScript.Shell.Run needs a fully-qualified path
# for bWaitOnReturn exit tracking to work correctly; a bare "powershell.exe"
# breaks exit detection in launchers such as Steam and GOG Galaxy.
POWERSHELL_EXE = os.path.join(
    os.environ.get("SystemRoot", r"C:\Windows"),
    r"System32\WindowsPowerShell\v1.0\powershell.exe",
)

# File type filters — native Windows executables first, then scripting runtimes.
TARGET_WILDCARD = (
    "Executables & scripts\0*.exe;*.ps1;*.bat;*.cmd;*.vbs;*.js;*.py;*.ahk\0"
    "Executable\0*.exe\0"
    "PowerShell script\0*.ps1\0"
    "Batch file\0*.bat;*.cmd\0"
    "VBScript\0*.vbs\0"
    "JScript\0*.js\0"
    "Python script\0*.py\0"
    "AutoHotkey script\0*.ahk\0"
    "All files\0*.*\0"
)

PRESTART_WILDCARD = (
    "Executables & scripts\0*.exe;*.ps1;*.bat;*.cmd;*.vbs;*.js;*.py;*.ahk\0"
    "All files\0*.*\0"
)

# tkinter filetypes tuples (used with filedialog)
TARGET_FILETYPES = [
    ("Executables & scripts", "*.exe;*.ps1;*.bat;*.cmd;*.vbs;*.js;*.py;*.ahk"),
    ("Executable", "*.exe"),
    ("PowerShell script", "*.ps1"),
    ("Batch file", "*.bat;*.cmd"),
    ("VBScript", "*.vbs"),
    ("JScript", "*.js"),
    ("Python script", "*.py"),
    ("AutoHotkey script", "*.ahk"),
    ("All files", "*.*"),
]

PRESTART_FILETYPES = [
    ("Executables & scripts", "*.exe;*.ps1;*.bat;*.cmd;*.vbs;*.js;*.py;*.ahk"),
    ("All files", "*.*"),
]

# ---------------------------------------------------------------------------
# .lnk generation
# ---------------------------------------------------------------------------

def create_lnk(lnk_path: str, ps1_path: str, icon_source: Optional[str] = None):
    """
    Create a .lnk that launches PowerShell silently via -WindowStyle Hidden.

    icon_source: path to the file whose icon to use (ideally the target .exe).
                 Falls back to powershell.exe if absent or not a valid file.
    """
    try:
        import win32com.client
        shell    = win32com.client.Dispatch("WScript.Shell")
        shortcut = shell.CreateShortcut(lnk_path)
        # Full path required — bare "powershell.exe" can trigger a script-editor
        # association on systems with Python or VS Code installed.
        shortcut.TargetPath = POWERSHELL_EXE
        shortcut.Arguments  = (
            f'-NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden '
            f'-File "{ps1_path}"'
        )
        shortcut.WorkingDirectory = os.path.dirname(ps1_path)
        shortcut.Description      = "DPM Shortcut"
        # WindowStyle 1 (normal) — PowerShell's own -WindowStyle Hidden handles
        # suppression. Setting 7 (minimized) here causes a brief taskbar flash
        # in some launchers before the Hidden flag takes effect.
        shortcut.WindowStyle = 1
        if icon_source and os.path.isfile(icon_source):
            shortcut.IconLocation = icon_source
        else:
            shortcut.IconLocation = POWERSHELL_EXE
        shortcut.Save()
    except Exception as exc:
        raise RuntimeError(f"Failed to create .lnk: {exc}") from exc


# ---------------------------------------------------------------------------
# .vbs wrapper generation  (Steam / launchers that ignore WindowStyle on .lnk)
# ---------------------------------------------------------------------------

def build_vbs(ps1_path: str) -> str:
    """
    Return a VBScript that launches the .ps1 via PowerShell with window
    style 0 (hidden) and waits for it to finish, preserving exit detection.

    WScript.Shell.Run(cmd, windowStyle, bWaitOnReturn)
      windowStyle   = 0    → hidden
      bWaitOnReturn = True → script blocks until PowerShell exits, so the
                             launcher's "game running" detection stays accurate.

    The full POWERSHELL_EXE path is required here. WScript.Shell.Run does not
    PATH-search when bWaitOnReturn is True — a bare "powershell.exe" breaks
    exit tracking in launchers such as Steam, GOG Galaxy, and Heroic.
    """
    ps1_escaped = ps1_path.replace('"', '""')
    cmd = (
        f'"{POWERSHELL_EXE}" -NonInteractive -ExecutionPolicy Bypass '
        f'-WindowStyle Hidden -File "{ps1_escaped}"'
    )
    return (
        'Set oShell = CreateObject("WScript.Shell")\n'
        f'oShell.Run "{cmd.replace(chr(34), chr(34)+chr(34))}", 0, True\n'
    )


# ---------------------------------------------------------------------------
# .ps1 generation
# ---------------------------------------------------------------------------

def _ps1_escape(s: str) -> str:
    return s.replace("'", "''")


def build_ps1(
    shortcut_name:  str,
    profile_id:     str,
    profile_name:   str,
    restore_mode:   str,
    target_path:    str,
    target_args:    str,
    target_workdir: str,
    pre_start:      List[dict],
) -> str:
    if restore_mode == RESTORE_DYNAMIC:
        restore_mode_str = "dynamic"
        restore_id_str   = ""
    elif restore_mode == RESTORE_NONE:
        restore_mode_str = "none"
        restore_id_str   = ""
    else:
        restore_mode_str = restore_mode
        restore_id_str   = restore_mode

    pre_start_lines = []
    for app in pre_start:
        kill  = "$true" if app.get("kill_on_exit") else "$false"
        delay = app.get("delay", 0.0) or 0.0
        pre_start_lines.append(
            f"    @{{ Path = '{_ps1_escape(app['path'])}'; "
            f"Args = '{_ps1_escape(app.get('args', ''))}'; "
            f"KillOnExit = {kill}; Delay = {delay} }}"
        )
    pre_start_block = "\n".join(pre_start_lines)

    ps1 = PS1_TEMPLATE
    ps1 = ps1.replace("{{SHORTCUT_NAME}}",     shortcut_name)
    ps1 = ps1.replace("{{PROFILE_ID}}",        _ps1_escape(profile_id))
    ps1 = ps1.replace("{{PROFILE_NAME}}",      _ps1_escape(profile_name))
    ps1 = ps1.replace("{{RESTORE_MODE}}",      restore_mode_str)
    ps1 = ps1.replace("{{RESTORE_ID}}",        restore_id_str)
    ps1 = ps1.replace("{{TARGET_PATH}}",       _ps1_escape(target_path))
    ps1 = ps1.replace("{{TARGET_ARGS}}",       _ps1_escape(target_args))
    ps1 = ps1.replace("{{TARGET_WORKDIR}}",    _ps1_escape(target_workdir))
    ps1 = ps1.replace("{{PRE_START_ENTRIES}}", pre_start_block)
    return ps1


# ---------------------------------------------------------------------------
# DPM executable resolution
# ---------------------------------------------------------------------------

def find_dpm_exe() -> Optional[str]:
    exe_name   = "DisplayProfileManager.exe"
    local_app  = os.environ.get("LOCALAPPDATA", "")
    script_dir = os.path.dirname(os.path.abspath(__file__))

    candidates = [
        os.path.join(local_app, "Programs", "DisplayProfileManager", exe_name),
        os.path.join(local_app, "DisplayProfileManager", exe_name),
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
# Pre-start row widget
# ---------------------------------------------------------------------------

class PreStartRow(tk.Frame):
    """A single row in the pre-start applications list."""

    def __init__(self, parent, on_delete, on_move_up, on_move_down):
        super().__init__(parent)
        self._on_delete    = on_delete
        self._on_move_up   = on_move_up
        self._on_move_down = on_move_down

        self._path_var  = tk.StringVar()
        self._args_var  = tk.StringVar()
        self._kill_var  = tk.BooleanVar()
        self._delay_var = tk.StringVar()

        # Single row: ▲▼✕ | path(weight=3) | … | args(weight=1) | Kill | Delay
        self.columnconfigure(3, weight=3)   # path entry
        self.columnconfigure(5, weight=1)   # args entry

        ttk.Button(self, text="▲", width=2,
                   command=lambda: self._on_move_up(self)).grid(
                       row=0, column=0, padx=(0, 2), pady=(0, 3))
        ttk.Button(self, text="▼", width=2,
                   command=lambda: self._on_move_down(self)).grid(
                       row=0, column=1, padx=(0, 2), pady=(0, 3))
        ttk.Button(self, text="✕", width=2,
                   command=lambda: self._on_delete(self)).grid(
                       row=0, column=2, padx=(0, 6), pady=(0, 3))

        ttk.Entry(self, textvariable=self._path_var).grid(
            row=0, column=3, sticky="ew", padx=(0, 2), pady=(0, 3))
        ttk.Button(self, text="…", width=2,
                   command=self._browse).grid(row=0, column=4, padx=(0, 6), pady=(0, 3))

        ttk.Entry(self, textvariable=self._args_var).grid(
            row=0, column=5, sticky="ew", padx=(0, 6), pady=(0, 3))

        ttk.Checkbutton(self, text="Kill on Exit", variable=self._kill_var).grid(
            row=0, column=6, padx=(0, 6), pady=(0, 3))

        ttk.Label(self, text="Delay:").grid(
            row=0, column=7, sticky="w", padx=(0, 4), pady=(0, 3))
        delay_entry = ttk.Entry(self, textvariable=self._delay_var, width=4)
        delay_entry.grid(row=0, column=8, padx=(0, 2), pady=(0, 3))
        delay_entry.bind("<FocusOut>", self._clamp_delay)

    def _clamp_delay(self, _=None):
        raw = self._delay_var.get().strip()
        if not raw:
            return
        try:
            val = float(raw)
            val = max(0.0, min(10.0, round(val, 1)))
            self._delay_var.set("" if val == 0.0 else str(val))
        except ValueError:
            self._delay_var.set("")

    def _browse(self):
        path = filedialog.askopenfilename(
            title="Select application",
            filetypes=PRESTART_FILETYPES,
        )
        if path:
            self._path_var.set(path)

    def get_data(self) -> dict:
        raw = self._delay_var.get().strip()
        try:
            delay = float(raw) if raw else 0.0
            delay = max(0.0, min(10.0, delay))
        except ValueError:
            delay = 0.0
        return {
            "path":         self._path_var.get().strip(),
            "args":         self._args_var.get().strip(),
            "kill_on_exit": self._kill_var.get(),
            "delay":        delay,
        }

    def set_data(self, d: dict):
        self._path_var.set(d.get("path", ""))
        self._args_var.set(d.get("args", ""))
        self._kill_var.set(bool(d.get("kill_on_exit", False)))
        delay = d.get("delay", 0.0)
        self._delay_var.set("" if not delay else str(delay))


# ---------------------------------------------------------------------------
# Scrollable frame helper
# ---------------------------------------------------------------------------

class ScrollableFrame(tk.Frame):
    """
    A vertically scrollable container. Place child widgets in .inner.
    Call update_scroll() after adding or removing children.
    """

    def __init__(self, parent, **kwargs):
        super().__init__(parent, **kwargs)
        self._canvas = tk.Canvas(self, highlightthickness=0)
        vsb = ttk.Scrollbar(self, orient=tk.VERTICAL, command=self._canvas.yview)
        self._canvas.configure(yscrollcommand=vsb.set)

        vsb.pack(side=tk.RIGHT, fill=tk.Y)
        self._canvas.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        self.inner = tk.Frame(self._canvas)
        self._window_id = self._canvas.create_window((0, 0), window=self.inner, anchor="nw")

        self.inner.bind("<Configure>", self._on_inner_configure)
        self._canvas.bind("<Configure>", self._on_canvas_configure)
        self._canvas.bind("<MouseWheel>", self._on_mousewheel)
        self.inner.bind("<MouseWheel>", self._on_mousewheel)

    def _on_inner_configure(self, _=None):
        self._canvas.configure(scrollregion=self._canvas.bbox("all"))

    def _on_canvas_configure(self, event):
        self._canvas.itemconfig(self._window_id, width=event.width)

    def _on_mousewheel(self, event):
        self._canvas.yview_scroll(int(-1 * (event.delta / 120)), "units")

    def update_scroll(self):
        self.inner.update_idletasks()
        self._canvas.configure(scrollregion=self._canvas.bbox("all"))


# ---------------------------------------------------------------------------
# Configuration panel (right side)
# ---------------------------------------------------------------------------

class ConfigPanel(tk.Frame):
    """
    Right-hand panel containing:
      - A scrollable form with all shortcut configuration fields.
      - A sticky footer with Save and Export buttons, always visible.
    """

    def __init__(self, parent, profiles: List[DpmProfile], on_dirty, on_saved):
        super().__init__(parent)
        self._profiles       = profiles
        self._pre_start_rows: List[PreStartRow] = []
        self._dirty          = False
        self._current_entry: Optional[ShortcutEntry] = None
        self._on_dirty       = on_dirty   # callback → MainWindow
        self._on_saved       = on_saved   # callback(name) → MainWindow

        self._build_ui()

    # ── Form construction ─────────────────────────────────────────────────

    def _build_ui(self):
        # ── Scrollable form ───────────────────────────────────────────────
        self._scroll = ScrollableFrame(self)
        self._scroll.pack(fill=tk.BOTH, expand=True)
        sc = self._scroll.inner
        PAD = (0, 6)   # external padding tuple for grid rows

        # ── Target application ─────────────────────────────────────────
        tgt_frame = ttk.LabelFrame(sc, text="Target application", padding=6)
        tgt_frame.pack(fill=tk.X, padx=8, pady=(8, 4))
        tgt_frame.columnconfigure(1, weight=1)

        ttk.Label(tgt_frame, text="Target:").grid(
            row=0, column=0, sticky="w", padx=(0, 6), pady=2)
        self._target_path = tk.StringVar()
        ttk.Entry(tgt_frame, textvariable=self._target_path).grid(
            row=0, column=1, sticky="ew", pady=2)
        ttk.Button(tgt_frame, text="Browse\u2026",
                   command=self._browse_target).grid(row=0, column=2, padx=(4, 0), pady=2)

        ttk.Label(tgt_frame, text="Arguments:").grid(
            row=1, column=0, sticky="w", padx=(0, 6), pady=2)
        self._target_args = tk.StringVar()
        ttk.Entry(tgt_frame, textvariable=self._target_args).grid(
            row=1, column=1, columnspan=2, sticky="ew", pady=2)

        ttk.Label(tgt_frame, text="Working dir:").grid(
            row=2, column=0, sticky="w", padx=(0, 6), pady=2)
        self._target_wd = tk.StringVar()
        ttk.Entry(tgt_frame, textvariable=self._target_wd).grid(
            row=2, column=1, columnspan=2, sticky="ew", pady=2)

        # ── Display profile ────────────────────────────────────────────
        prof_frame = ttk.LabelFrame(sc, text="Display profile", padding=6)
        prof_frame.pack(fill=tk.X, padx=8, pady=4)
        prof_frame.columnconfigure(0, weight=1)

        prof_choices = ["— select a profile —"] + [p.name for p in self._profiles]
        self._profile_var = tk.StringVar(value=prof_choices[0])
        self._profile_cb  = ttk.Combobox(prof_frame, textvariable=self._profile_var,
                                          values=prof_choices, state="readonly")
        self._profile_cb.pack(fill=tk.X)

        # ── Restore profile ────────────────────────────────────────────
        rest_frame = ttk.LabelFrame(sc, text="Restore profile on exit", padding=6)
        rest_frame.pack(fill=tk.X, padx=8, pady=4)
        rest_frame.columnconfigure(0, weight=1)

        rest_choices = ["Current (active at launch)", "None"] + [
            p.name for p in self._profiles
        ]
        self._restore_var = tk.StringVar(value=rest_choices[0])
        self._restore_cb  = ttk.Combobox(rest_frame, textvariable=self._restore_var,
                                          values=rest_choices, state="readonly")
        self._restore_cb.pack(fill=tk.X)

        # ── Pre-start applications ─────────────────────────────────────
        pre_frame = ttk.LabelFrame(sc, text="Pre-start applications", padding=6)
        pre_frame.pack(fill=tk.X, padx=8, pady=4)

        self._pre_rows_frame = tk.Frame(pre_frame)
        # Packed on demand when the first row is added; hidden when last row removed.

        self._add_app_btn = ttk.Button(pre_frame, text="+ Add application",
                                       command=self._add_pre_start)
        self._add_app_btn.pack(anchor="w", pady=(2, 0))

        # ── Shortcut name ──────────────────────────────────────────────
        name_frame = ttk.LabelFrame(sc, text="Shortcut name", padding=6)
        name_frame.pack(fill=tk.X, padx=8, pady=4)
        name_frame.columnconfigure(0, weight=1)

        self._name_var = tk.StringVar()
        ttk.Entry(name_frame, textvariable=self._name_var).pack(fill=tk.X)

        # ── Launcher integration tabs ──────────────────────────────────
        launcher_frame = ttk.LabelFrame(sc, text="Launcher integration", padding=6)
        launcher_frame.pack(fill=tk.X, padx=8, pady=(4, 8))

        self._launcher_nb = ttk.Notebook(launcher_frame)
        self._launcher_nb.pack(fill=tk.X)

        _tabs = [
            (
                "Steam",
                (
                    "In Steam, right-click the game \u2192 Properties \u2192 General \u2192 "
                    "Launch Options. Paste the line below exactly as shown \u2014 "
                    "the quotes and %command% are required. Steam will apply "
                    "your display profile, launch the game, then restore on exit. "
                    "The .vbs wrapper is used here (not the .lnk) because Steam "
                    "does not honor the hidden-window flag on .lnk files. "
                    "Note: Steam may take a few extra seconds before the game "
                    "opens; this is normal. To keep existing launch options, "
                    "add them after %command% (e.g. \"...vbs\" %command% -fullscreen)."
                ),
                "Launch Options:",
                "steam",
            ),
            (
                "Epic Games",
                (
                    "Epic does not support pre-launch wrappers. The recommended "
                    "approach is to launch the .lnk from outside Epic \u2014 place it "
                    "on your Desktop or in a frontend like Playnite. If you need "
                    "to keep Epic as the launcher, add the .lnk as a separate "
                    "tile in Playnite and launch from there."
                ),
                ".lnk path:",
                "lnk",
            ),
            (
                "GOG Galaxy",
                (
                    "Library game \u2192 More (\u22ef) \u2192 Manage Installation "
                    "\u2192 Configure. Under 'Custom executable / arguments', set "
                    "Executable to wscript.exe and Arguments to the value below "
                    "(include the quotes). This runs the .vbs silently and lets "
                    "GOG track the game as running. "
                    "To keep an existing argument, append it after the quoted .vbs path."
                ),
                "Arguments:",
                "gog",
            ),
            (
                "Heroic",
                (
                    "Game page \u2192 Settings \u2192 Advanced \u2192 Alternative "
                    "executable. Set the path to wscript.exe. In the arguments "
                    "field paste the value below. Heroic will run the .vbs instead "
                    "of the game binary and track the session correctly. "
                    "To keep existing arguments, append them after the quoted .vbs path."
                ),
                "Arguments:",
                "heroic",
            ),
            (
                "Playnite",
                (
                    "Edit game \u2192 Play action \u2192 Type: File, Path: the "
                    ".lnk below. For silent launch, set Type: File, Path: "
                    "wscript.exe, Arguments: the .vbs path below (quoted). "
                    "Both methods allow Playnite to track the session."
                ),
                ".lnk path:",
                "lnk",
            ),
            (
                "Generic / Desktop",
                (
                    "Double-clicking the .lnk works silently from Explorer and "
                    "most launchers that call ShellExecute. For any context that "
                    "needs an explicit silent wrapper (e.g. Task Scheduler, "
                    "batch files), call wscript.exe with the .vbs path."
                ),
                ".lnk path:",
                "lnk",
            ),
        ]

        self._launcher_vars: Dict[str, tk.StringVar] = {}
        self._launcher_keys: Dict[str, str]          = {}

        for tab_name, instr_text, path_lbl, path_key in _tabs:
            tab    = tk.Frame(self._launcher_nb)
            tab.columnconfigure(0, weight=1)

            instr = tk.Label(tab, text=instr_text, justify=tk.LEFT,
                             wraplength=560, foreground="#a0a0a0",
                             font=("Segoe UI", 9))
            instr.grid(row=0, column=0, columnspan=2, sticky="w", padx=6, pady=(6, 2))

            ttk.Label(tab, text=path_lbl).grid(
                row=1, column=0, sticky="w", padx=6, pady=(0, 2))

            var = tk.StringVar()
            entry = ttk.Entry(tab, textvariable=var, state="readonly")
            entry.grid(row=2, column=0, sticky="ew", padx=6, pady=(0, 2))

            ttk.Button(tab, text="Copy to clipboard",
                       command=lambda v=var: self._copy_to_clipboard(v.get())).grid(
                row=3, column=0, sticky="w", padx=6, pady=(0, 6))

            self._launcher_nb.add(tab, text=tab_name)
            self._launcher_vars[tab_name] = var
            self._launcher_keys[tab_name] = path_key

        # ── Sticky footer ─────────────────────────────────────────────────
        ttk.Separator(self, orient=tk.HORIZONTAL).pack(fill=tk.X)

        footer = tk.Frame(self)
        footer.pack(fill=tk.X, padx=6, pady=6)

        self._save_btn = ttk.Button(footer, text="Save", command=self._on_save)
        self._save_btn.pack(side=tk.LEFT, padx=(0, 4))
        self._save_btn.state(["disabled"])

        save_lbl = tk.Label(footer, text="Saves .ps1, .lnk, and .vbs to the Shortcuts folder.",
                            foreground="#969696", font=("Segoe UI", 9))
        save_lbl.pack(side=tk.LEFT, padx=(0, 16))

        self._export_btn = ttk.Button(footer, text="Export .lnk\u2026",
                                      command=self._on_export)
        self._export_btn.pack(side=tk.LEFT, padx=(0, 4))
        self._export_btn.state(["disabled"])

        export_lbl = tk.Label(footer, text="Copy the .lnk to another location (e.g. Desktop).",
                              foreground="#969696", font=("Segoe UI", 9))
        export_lbl.pack(side=tk.LEFT)

        # ── Dirty-tracking bindings ────────────────────────────────────────
        for var in (self._target_path, self._target_args, self._target_wd,
                    self._profile_var, self._restore_var, self._name_var):
            var.trace_add("write", self._mark_dirty_trace)
        self._profile_cb.bind("<<ComboboxSelected>>", lambda _: self._mark_dirty())
        self._restore_cb.bind("<<ComboboxSelected>>", lambda _: self._mark_dirty())
        self._target_path.trace_add("write", self._on_target_changed_trace)
        self._name_var.trace_add("write", self._on_name_changed_trace)

    # ── Dirty tracking ────────────────────────────────────────────────────

    def _mark_dirty_trace(self, *_):
        self._mark_dirty()

    def _mark_dirty(self):
        if not self._dirty and not getattr(self, "_clearing", False):
            self._dirty = True
            self._on_dirty()

    def is_dirty(self) -> bool:
        return self._dirty

    # ── Target browse ─────────────────────────────────────────────────────

    def _browse_target(self):
        path = filedialog.askopenfilename(
            title="Select target application",
            filetypes=TARGET_FILETYPES,
        )
        if path:
            self._target_path.set(path)
            self._target_wd.set(os.path.dirname(path))
            self._name_var.set(os.path.splitext(os.path.basename(path))[0])

    def _on_target_changed_trace(self, *_):
        path = self._target_path.get().strip()
        if path and self._profiles:
            self._save_btn.state(["!disabled"])
        else:
            self._save_btn.state(["disabled"])
        self._update_launcher_strings()

    def _on_name_changed_trace(self, *_):
        self._update_launcher_strings()

    # ── Pre-start rows ────────────────────────────────────────────────────

    def _add_pre_start(self, data: Optional[dict] = None):
        if not self._pre_start_rows:
            self._pre_rows_frame.pack(fill=tk.X, before=self._add_app_btn)
        row = PreStartRow(
            self._pre_rows_frame,
            on_delete=self._remove_pre_start,
            on_move_up=self._move_pre_start_up,
            on_move_down=self._move_pre_start_down,
        )
        if data:
            row.set_data(data)
        self._pre_start_rows.append(row)
        row.pack(fill=tk.X, pady=(0, 2))
        self._scroll.update_scroll()
        self._mark_dirty()

    def _remove_pre_start(self, row: PreStartRow):
        self._pre_start_rows.remove(row)
        row.destroy()
        if not self._pre_start_rows:
            self._pre_rows_frame.pack_forget()
        self._scroll.update_scroll()
        self._mark_dirty()

    def _move_pre_start_up(self, row: PreStartRow):
        idx = self._pre_start_rows.index(row)
        if idx == 0:
            return
        self._pre_start_rows[idx], self._pre_start_rows[idx - 1] = (
            self._pre_start_rows[idx - 1], self._pre_start_rows[idx]
        )
        self._rebuild_rows()

    def _move_pre_start_down(self, row: PreStartRow):
        idx = self._pre_start_rows.index(row)
        if idx >= len(self._pre_start_rows) - 1:
            return
        self._pre_start_rows[idx], self._pre_start_rows[idx + 1] = (
            self._pre_start_rows[idx + 1], self._pre_start_rows[idx]
        )
        self._rebuild_rows()

    def _rebuild_rows(self):
        for row in self._pre_start_rows:
            row.pack_forget()
        for row in self._pre_start_rows:
            row.pack(fill=tk.X, pady=(0, 2))
        self._scroll.update_scroll()
        self._mark_dirty()

    # ── Launcher strings ──────────────────────────────────────────────────

    def _update_launcher_strings(self, *_):
        lnk = self._get_lnk_path()
        vbs = self._get_vbs_path()
        if not lnk:
            return

        for tab_name, var in self._launcher_vars.items():
            key = self._launcher_keys[tab_name]
            if key == "steam":
                var.set(f'"{vbs}" %command%' if vbs else "")
            elif key in ("gog", "heroic"):
                var.set(f'"{vbs}"' if vbs else "")
            else:
                var.set(lnk)

    def _get_lnk_path(self) -> Optional[str]:
        name = self._name_var.get().strip()
        if not name:
            return None
        return os.path.join(SHORTCUTS_DIR, name, name + ".lnk")

    def _get_vbs_path(self) -> Optional[str]:
        name = self._name_var.get().strip()
        if not name:
            return None
        return os.path.join(SHORTCUTS_DIR, name, name + ".vbs")

    def _copy_to_clipboard(self, text: str):
        self.clipboard_clear()
        self.clipboard_append(text)

    # ── Save / Export ─────────────────────────────────────────────────────

    def _collect_fields(self):
        """Validate and return all field values, or raise ValueError."""
        name = self._name_var.get().strip()
        err  = validate_name(name)
        if err:
            raise ValueError(err)

        prof_idx = self._profile_cb.current()
        if prof_idx <= 0 or not self._profiles:
            raise ValueError("Please select a display profile.")
        profile = self._profiles[prof_idx - 1]

        restore_idx = self._restore_cb.current()
        if restore_idx == 0:
            restore_mode = RESTORE_DYNAMIC
        elif restore_idx == 1:
            restore_mode = RESTORE_NONE
        else:
            restore_mode = self._profiles[restore_idx - 2].id

        target_path = self._target_path.get().strip()
        target_args = self._target_args.get().strip()
        target_wd   = self._target_wd.get().strip()
        pre_start   = [
            r.get_data() for r in self._pre_start_rows if r.get_data()["path"]
        ]
        return name, profile, restore_mode, target_path, target_args, target_wd, pre_start

    def _write_shortcut(
        self, name, profile, restore_mode,
        target_path, target_args, target_wd, pre_start,
    ) -> ShortcutEntry:
        """Build .ps1, .lnk, and .vbs. Prompts on overwrite. Raises ValueError."""
        try:
            ps1_content = build_ps1(
                shortcut_name=name,
                profile_id=profile.id,
                profile_name=profile.name,
                restore_mode=restore_mode,
                target_path=target_path,
                target_args=target_args,
                target_workdir=target_wd,
                pre_start=pre_start,
            )
        except RuntimeError as exc:
            raise ValueError(str(exc))

        folder = os.path.join(SHORTCUTS_DIR, name)
        try:
            entry = create_shortcut_folder(name)
        except FileExistsError:
            if not messagebox.askyesno(
                "Overwrite?",
                f"A shortcut named \u2018{name}\u2019 already exists. Overwrite?",
            ):
                raise ValueError("Cancelled.")
            entry = _entry_for(name, folder)

        try:
            with open(entry.ps1_path, "w", encoding="utf-8") as fh:
                fh.write(ps1_content)
        except OSError as exc:
            raise ValueError(f"Failed to write .ps1:\n{exc}")

        # .vbs — silent wrapper for launchers that ignore .lnk window style
        vbs_path = os.path.join(entry.folder, name + ".vbs")
        try:
            with open(vbs_path, "w", encoding="utf-8") as fh:
                fh.write(build_vbs(entry.ps1_path))
        except OSError as exc:
            raise ValueError(f"Failed to write .vbs:\n{exc}")

        # Icon: prefer the target .exe; fall back to DPM exe, then PowerShell
        icon_source = None
        if target_path and target_path.lower().endswith(".exe") and os.path.isfile(target_path):
            icon_source = target_path
        if not icon_source:
            icon_source = find_dpm_exe()

        try:
            create_lnk(entry.lnk_path, entry.ps1_path, icon_source)
        except RuntimeError as exc:
            messagebox.showwarning("Shortcut warning", str(exc))

        return entry

    def _on_save(self):
        try:
            fields = self._collect_fields()
        except ValueError as exc:
            messagebox.showerror("Validation error", str(exc))
            return

        try:
            entry = self._write_shortcut(*fields)
        except ValueError as exc:
            if str(exc) != "Cancelled.":
                messagebox.showerror("Save error", str(exc))
            return

        self._dirty = False
        self._current_entry = entry
        self._export_btn.state(["!disabled"])
        self._update_launcher_strings()
        self._on_saved(fields[0])

    def _on_export(self):
        """Copy the .lnk to a user-chosen location; saves first if dirty."""
        if self._dirty or self._current_entry is None:
            try:
                fields = self._collect_fields()
            except ValueError as exc:
                messagebox.showerror("Validation error", str(exc))
                return
            try:
                self._current_entry = self._write_shortcut(*fields)
            except ValueError as exc:
                if str(exc) != "Cancelled.":
                    messagebox.showerror("Save error", str(exc))
                return
            self._dirty = False
            self._on_saved(fields[0])

        name = self._current_entry.name
        dest = filedialog.asksaveasfilename(
            title="Export shortcut copy to\u2026",
            initialdir=os.path.join(os.environ.get("USERPROFILE", ""), "Desktop"),
            initialfile=name + ".lnk",
            filetypes=[("Shortcuts", "*.lnk"), ("All files", "*.*")],
            defaultextension=".lnk",
        )
        if dest:
            try:
                shutil.copy2(self._current_entry.lnk_path, dest)
            except OSError as exc:
                messagebox.showwarning("Copy failed", f"Could not copy shortcut:\n{exc}")

    # ── Public API ────────────────────────────────────────────────────────

    def load_entry(self, entry: ShortcutEntry):
        self._current_entry = entry
        self._name_var.set(entry.name)
        for row in list(self._pre_start_rows):
            self._remove_pre_start(row)
        if os.path.isfile(entry.ps1_path):
            self._parse_ps1_into_fields(entry.ps1_path)
        self._update_launcher_strings()
        self._dirty = False
        self._save_btn.state(["!disabled"])
        self._export_btn.state(["!disabled"])

    def clear(self):
        self._clearing = True
        self._current_entry = None
        self._name_var.set("")
        self._target_path.set("")
        self._target_args.set("")
        self._target_wd.set("")
        self._profile_cb.current(0)
        self._restore_cb.current(0)
        for row in list(self._pre_start_rows):
            self._remove_pre_start(row)
        for var in self._launcher_vars.values(): var.set("")
        self._save_btn.state(["disabled"])
        self._export_btn.state(["disabled"])
        self._dirty = False
        self._clearing = False

    def _parse_ps1_into_fields(self, path: str):
        try:
            with open(path, "r", encoding="utf-8") as fh:
                text = fh.read()
        except OSError:
            return

        def _extract(key):
            m = re.search(
                rf"^\${re.escape(key)}\s*=\s*'([^']*)'", text, re.MULTILINE
            )
            return m.group(1).replace("''", "'") if m else ""

        tm = re.search(
            r"\$target\s*=\s*@\{[^}]*Path\s*=\s*'([^']*)'"
            r"[^}]*Args\s*=\s*'([^']*)'"
            r"[^}]*WorkingDirectory\s*=\s*'([^']*)'",
            text, re.DOTALL,
        )
        if tm:
            self._target_path.set(tm.group(1).replace("''", "'"))
            self._target_args.set(tm.group(2).replace("''", "'"))
            self._target_wd.set(tm.group(3).replace("''", "'"))

        profile_id = _extract("profileId")
        for i, p in enumerate(self._profiles):
            if p.id == profile_id:
                self._profile_cb.current(i + 1)
                break

        restore_mode = _extract("restoreMode")
        restore_id   = _extract("restoreId")
        if restore_mode == "dynamic":
            self._restore_cb.current(0)
        elif restore_mode == "none":
            self._restore_cb.current(1)
        else:
            for i, p in enumerate(self._profiles):
                if p.id == restore_id:
                    self._restore_cb.current(i + 2)
                    break

        for m in re.finditer(
            r"@\{\s*Path\s*=\s*'([^']*)'\s*;\s*Args\s*=\s*'([^']*)'\s*;"
            r"\s*KillOnExit\s*=\s*(\$true|\$false)\s*;\s*Delay\s*=\s*([\d.]+)\s*\}",
            text,
        ):
            self._add_pre_start(data={
                "path":         m.group(1).replace("''", "'"),
                "args":         m.group(2).replace("''", "'"),
                "kill_on_exit": m.group(3) == "$true",
                "delay":        float(m.group(4)),
            })


# ---------------------------------------------------------------------------
# Main window
# ---------------------------------------------------------------------------

class App(tk.Tk):
    def __init__(self):
        self._set_taskbar_icon()
        super().__init__()
        self.title("DPM Shortcut Builder")
        self.minsize(920, 700)
        self.geometry("980x720")

        self._profiles = load_profiles()
        self._entries:  List[ShortcutEntry] = []

        self._load_icons()
        self._build_ui()
        self._load_shortcut_list()

    def _set_taskbar_icon(self):
        # Tell Windows this process has its own identity so the taskbar shows
        # the correct icon rather than grouping it under pythonw.exe.
        try:
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID(
                "DisplayProfileManager.ShortcutBuilder"
            )
        except Exception:
            pass

    def _load_icons(self):
        try:
            data16 = base64.b64decode(BUILDER_ICON_16_B64)
            self._icon16 = tk.PhotoImage(data=base64.b64encode(data16))
            self.iconphoto(False, self._icon16)
        except Exception:
            pass

        try:
            data32 = base64.b64decode(BUILDER_ICON_32_B64)
            self._icon32 = tk.PhotoImage(data=base64.b64encode(data32))
            self.iconphoto(False, self._icon32)
        except Exception:
            pass

    # ── UI ────────────────────────────────────────────────────────────────

    def _build_ui(self):
        # ── Paned window (left sidebar + right config) ────────────────────
        pane = tk.PanedWindow(self, orient=tk.HORIZONTAL, sashwidth=5,
                              sashrelief=tk.FLAT)
        pane.pack(fill=tk.BOTH, expand=True)

        # ── Left panel ────────────────────────────────────────────────────
        left = tk.Frame(pane, width=200)
        left.pack_propagate(False)   # hold minimum width
        pane.add(left, minsize=160)
        pane.bind("<ButtonRelease-1>", lambda e: pane.sash_place(0, min(pane.sash_coord(0)[0], 200), 0))

        lbl = tk.Label(left, text="Shortcuts", font=("Segoe UI", 9, "bold"))
        lbl.pack(anchor="w", padx=8, pady=(8, 2))

        list_frame = tk.Frame(left)
        list_frame.pack(fill=tk.BOTH, expand=True, padx=8)

        self._listbox = tk.Listbox(list_frame, selectmode=tk.SINGLE,
                                   activestyle="dotbox", font=("Segoe UI", 9))
        lsb = ttk.Scrollbar(list_frame, orient=tk.VERTICAL,
                             command=self._listbox.yview)
        self._listbox.configure(yscrollcommand=lsb.set)
        self._listbox.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        lsb.pack(side=tk.RIGHT, fill=tk.Y)

        self._new_btn = ttk.Button(left, text="New",       command=self._on_new)
        self._del_btn = ttk.Button(left, text="Delete",    command=self._on_delete)
        self._dup_btn = ttk.Button(left, text="Duplicate", command=self._on_duplicate)
        for btn in (self._new_btn, self._del_btn, self._dup_btn):
            btn.pack(fill=tk.X, padx=8, pady=(6, 0))

        if not self._profiles:
            warn = tk.Label(left, text="\u26a0 No DPM profiles found.",
                            foreground="#c88c00", font=("Segoe UI", 9))
            warn.pack(anchor="w", padx=8, pady=(8, 0))

        tk.Frame(left).pack(pady=4)   # bottom spacer

        # ── Right panel ───────────────────────────────────────────────────
        self._config = ConfigPanel(
            pane, self._profiles,
            on_dirty=self._on_dirty,
            on_saved=self._on_shortcut_saved,
        )
        pane.add(self._config, minsize=400)

        # ── Bindings ──────────────────────────────────────────────────────
        self._listbox.bind("<<ListboxSelect>>", self._on_list_select)
        self._listbox.bind("<Double-Button-1>", self._on_list_select)

    # ── Shortcut list ─────────────────────────────────────────────────────

    def _load_shortcut_list(self):
        self._entries = load_shortcuts()
        self._listbox.delete(0, tk.END)
        for e in self._entries:
            self._listbox.insert(tk.END, e.name)

    # ── Title bar dirty marker ────────────────────────────────────────────

    def _on_dirty(self):
        self._refresh_title(dirty=True)

    def _refresh_title(self, dirty: bool = False):
        marker = "*" if dirty else ""
        self.title(f"DPM Shortcut Builder{marker}")

    # ── Unsaved-changes guard ─────────────────────────────────────────────

    def _check_unsaved(self) -> bool:
        if not self._config.is_dirty():
            return True
        return messagebox.askyesno(
            "Unsaved changes",
            "You have unsaved changes. Discard them?",
        )

    # ── List interactions ─────────────────────────────────────────────────

    def _on_list_select(self, _=None):
        sel = self._listbox.curselection()
        if not sel:
            return
        if not self._check_unsaved():
            # Restore selection to the previously loaded entry
            if self._config._current_entry:
                for i, e in enumerate(self._entries):
                    if e.name == self._config._current_entry.name:
                        self._listbox.selection_clear(0, tk.END)
                        self._listbox.selection_set(i)
                        break
            return
        self._config.load_entry(self._entries[sel[0]])
        self._refresh_title(dirty=False)

    def _on_new(self):
        if not self._check_unsaved():
            return
        self._listbox.selection_clear(0, tk.END)
        self._config.clear()
        self._refresh_title(dirty=False)

    def _on_delete(self):
        sel = self._listbox.curselection()
        if not sel:
            messagebox.showinfo("Nothing selected", "Select a shortcut to delete.")
            return
        name = self._entries[sel[0]].name
        if not messagebox.askyesno(
            "Confirm delete",
            f"Delete the shortcut \u2018{name}\u2019?",
        ):
            return
        try:
            delete_shortcut(name)
        except Exception as exc:
            messagebox.showerror("Delete failed", str(exc))
            return
        self._load_shortcut_list()
        self._config.clear()
        self._refresh_title(dirty=False)

    def _on_duplicate(self):
        sel = self._listbox.curselection()
        if not sel:
            messagebox.showinfo("Nothing selected", "Select a shortcut to duplicate.")
            return
        source = self._entries[sel[0]].name

        dlg = tk.Toplevel(self)
        dlg.title("Duplicate shortcut")
        dlg.resizable(False, False)
        dlg.grab_set()
        dlg.transient(self)

        tk.Label(dlg, text="Name for the duplicate:").pack(padx=12, pady=(12, 4), anchor="w")
        name_var = tk.StringVar(value=source + " (copy)")
        entry = ttk.Entry(dlg, textvariable=name_var, width=40)
        entry.pack(padx=12, pady=(0, 8))
        entry.focus_set()
        entry.select_range(0, tk.END)

        result = [None]

        def _ok(_=None):
            result[0] = name_var.get().strip()
            dlg.destroy()

        def _cancel(_=None):
            dlg.destroy()

        btn_row = tk.Frame(dlg)
        btn_row.pack(pady=(0, 12))
        ttk.Button(btn_row, text="OK",     command=_ok).pack(side=tk.LEFT, padx=4)
        ttk.Button(btn_row, text="Cancel", command=_cancel).pack(side=tk.LEFT, padx=4)
        entry.bind("<Return>", _ok)
        entry.bind("<Escape>", _cancel)

        self.wait_window(dlg)

        if not result[0]:
            return
        try:
            duplicate_shortcut(source, result[0])
        except (ValueError, FileExistsError, FileNotFoundError) as exc:
            messagebox.showerror("Duplicate failed", str(exc))
            return
        self._load_shortcut_list()

    def _on_shortcut_saved(self, name: str):
        self._load_shortcut_list()
        self._refresh_title(dirty=False)
        for i, e in enumerate(self._entries):
            if e.name == name:
                self._listbox.selection_clear(0, tk.END)
                self._listbox.selection_set(i)
                break


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    App().mainloop()
