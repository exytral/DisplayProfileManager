# Scripts

A profile can run scripts after the display, wallpaper, and audio stages have completed. Scripts can launch applications, invoke automation tools, or perform other changes associated with a change of display context.

---

## Supported file types

| Type          | How it runs                                                                     |
| ------------- | ------------------------------------------------------------------------------- |
| `.exe`        | Converted to a `.lnk` shortcut on import and launched through the Windows Shell |
| `.ps1`        | PowerShell with `-ExecutionPolicy Bypass`                                       |
| `.bat`/`.cmd` | `cmd.exe /c`                                                                    |
| `.vbs`/`.js`  | `cscript.exe /nologo`                                                           |
| `.py`         | `python.exe` from `PATH`                                                        |
| `.ahk`        | `autohotkey.exe` from `PATH`                                                    |

Imported `.exe` files are converted to `.lnk` shortcuts in the scripts sandbox. The shortcut is stored in the profile and used for execution.

Python and AutoHotkey must be installed and available on `PATH`. Launch failures are logged when the required interpreter cannot be started.

---

## Scripts folder

All imported scripts are sandboxed to:

```text
%AppData%\Roaming\DisplayProfileManager\Scripts\
```

Imported files are copied into this folder. References to files outside the sandbox are not supported.

Deleting a file directly from the sandbox does not automatically remove script entries from profiles that reference it.

---

## Adding a script to a profile

See [Creating and Managing Profiles — Scripts](./profiles.md#scripts) for the editor walkthrough.

In brief:

1. Open the profile editor and scroll to **Scripts**.
2. Click **"Import"** and select a supported file.
3. Enter optional arguments for the script.
4. Use the row checkbox to enable or disable that script without removing it.

---

## Enabling and disabling

The section-level **"Enable"** toggle controls whether scripts run for the profile.

Each script row also has its own checkbox. Clearing it keeps the script and its arguments stored but skips it during profile switches.

Deleting a script removes the entry from the profile when the profile is saved. The underlying file remains in the scripts folder.

---

## Arguments

Arguments are appended after the script path according to the script type:

- `.lnk`: launched through the Windows Shell with arguments
- `.ps1`: `powershell.exe -ExecutionPolicy Bypass -File "script.ps1" <args>`
- `.bat` / `.cmd`: `cmd.exe /c "script.bat" <args>`
- `.vbs` / `.js`: `cscript.exe /nologo "script.vbs" <args>`
- `.py`: `python.exe "script.py" <args>`
- `.ahk`: `autohotkey.exe "script.ahk" <args>`

---

## Examples

### Launch an application on profile apply

```bat
@echo off
start "" "C:\Program Files\Folder\App.exe"
```

Save as `launch-app.bat` and add it to a profile.

### Kill a process when switching profiles

```powershell
Stop-Process -Name "App" -ErrorAction SilentlyContinue
```

Save as `kill-app.ps1` and add it to a profile used when that process should stop.

### Launch Steam Big Picture Mode on profile apply

```powershell
Start-Process "steam://open/bigpicture"
```

Save as `launch-bigpicture.ps1` and add it to a profile that should launch Big Picture Mode.

> For the reverse direction — changing profiles when Big Picture Mode opens or closes — see [CLI Reference — Steam Big Picture Mode watcher](./cli.md#steam-big-picture-mode-watcher).

---

## LG TV switching example

`lg-tv-switch.ps1` can call LGTV Companion's `LGTVcli.exe` for explicit profile-triggered power changes.

```powershell
param(
    [Parameter(Position = 0)]
    [ValidateSet("on", "off")]
    [string]$State = "off"
)

$cli = "C:\Program Files\LGTV Companion\LGTVcli.exe"

if (-not (Test-Path $cli)) {
    Write-Error "LGTVcli.exe not found. Install LGTV Companion or update the path in this script."
    exit 1
}

$arg = if ($State -eq "on") { "-poweron" } else { "-poweroff" }

& $cli $arg
if ($LASTEXITCODE -ne 0) {
    Write-Error "LGTVcli.exe returned exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}
```

LGTV Companion is an external tool; configure it separately before using this example.