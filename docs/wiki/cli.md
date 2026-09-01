# CLI Reference

Display Profile Manager accepts command-line arguments for profile application, theme switching, and other application actions. Commands are forwarded to a running instance through named-pipe IPC when possible; otherwise supported commands run locally in a new process.

> Looking to switch profiles automatically when launching a game or app? **[DPM Shortcut Builder](#dpm-shortcut-builder)** creates augmented shortcuts without manual scripting.

---

Flags accept any number of leading dashes, or none at all:

```text
DisplayProfileManager.exe --profile "Profile"
DisplayProfileManager.exe -profile "Profile"
DisplayProfileManager.exe profile "Profile"
```

---

## Flags

### `--profile` "name/ID"

Apply a profile by name or ID. With no argument, reapplies the current active profile. When a running instance is available, the command is forwarded through IPC; otherwise, the profile is applied locally.

```text
DisplayProfileManager.exe --profile "Profile"
DisplayProfileManager.exe --profile
```

### `--headless` "name/ID"

Apply a profile and exit without opening the main window or tray icon. With a running instance, the command is forwarded through IPC; otherwise the application applies the profile locally and exits. A local headless application returns an exit code to denote success or failure, suitable for automation and launchers.

```text
DisplayProfileManager.exe --headless "Profile"
DisplayProfileManager.exe --headless
```

### `--theme` "name"

Apply a named theme. With no name, refreshes the current theme. When a running instance is unavailable, a named theme is applied locally; a nameless `--theme` has no effect.

```text
DisplayProfileManager.exe --theme "Theme"
DisplayProfileManager.exe --theme
```

### `--refresh`

Refresh profiles and themes from disk. `--reload` and `-r` are accepted aliases. With no running instance, the command exits without effect.

```text
DisplayProfileManager.exe --refresh
```

### `--tray`

Start minimized to the system tray. Exact match only.

```text
DisplayProfileManager.exe --tray
```

### `--exit`

Gracefully exit the running DPM instance. When a running instance is available, the command is forwarded through IPC and the invoking process exits. When no running instance is available, the command exits without effect. Exact match only.

```text
DisplayProfileManager.exe --exit
```

### `--shell`

Register the desktop context-menu shell extension as a per-user COM extension and enable the setting. Returns an exit code to denote success, failure, or no change. Exits immediately. Exact match only.

```text
DisplayProfileManager.exe --shell
```

### `--unshell`

Remove the desktop context-menu shell-extension registration. Used by the uninstaller. Returns an exit code to denote success, failure, or no change. Exits immediately. Exact match only.

```text
DisplayProfileManager.exe --unshell
```

### `--dev`

Bypass the single-instance check, used for development. Exact match only.

```text
DisplayProfileManager.exe --dev
```
---

## Prefix matching

All flags except `--tray`, `--exit`, `--shell`, `--unshell`, and `--dev` support unambiguous prefix matching after leading dashes are stripped:

```text
--profile "Profile"  == --prof "Profile" == --p "Profile"
--headless "Profile" == --head "Profile" == --h "Profile"
--theme "Theme"      == --t "Theme"
--refresh            == --ref            == -r
```

The exact-only flags require their complete names.

---

## Precedence when flags are combined

When several flags are combined:

1. `--shell` and `--unshell` are handled first and terminate further command processing.
2. `--exit` is handled next when neither shell action is present and terminates further command processing.
3. `--refresh`, `--theme`, `--profile`, and `--headless` can be supplied together. Refresh and theme commands run in the order given, while one profile target is handled last. When multiple `--profile` or `--headless` targets are supplied, the last supplied target replaces the earlier one.
4. `--dev` and `--tray` are startup modifiers and can be combined with the other flags.

---

## IPC behavior

The application first attempts to forward commands to a running instance through `DPM_IpcPipe.{sessionId}`. When no running instance is available, supported commands fall back to local execution.

- `--profile`, `--headless` — forward profile application to a running instance when available; otherwise, apply locally.
- `--headless` never creates the main window or tray icon.
- `--theme "Theme"` — forward the named theme to a running instance when available; otherwise, apply it locally.
- `--theme` with no name — refreshes the current theme when a running instance is available; otherwise, it does nothing.
- `--refresh` — requires a running instance and does nothing when none is available.
- `--exit` — requires a running instance; when none is available, the invoking process exits without starting the application.

See [Precedence when flags are combined](#precedence-when-flags-are-combined) above for what happens when multiple flags are given at once.

```text
DisplayProfileManager.exe --theme "Theme" --headless "Profile"
```

`--profile` and `--headless` share one profile value. An explicit profile argument is last-write-wins; a bare profile-affecting flag does not clear an existing value. `--headless` is cumulative once present.

---

## Automation with `--headless`

`--headless` is intended for scheduled tasks, scripts, shortcuts, and launcher integrations that should apply a profile without leaving the application's UI running in the foreground.

### Apply a profile from Task Scheduler

- **Program:** `C:\Path\To\DisplayProfileManager.exe`
- **Arguments:** `--headless "Profile"`
- **Trigger:** At log on or on a schedule

### Apply a profile from PowerShell

```powershell
$dpm = "C:\Path\To\DisplayProfileManager.exe"
& $dpm --headless "Profile"
```

### Desktop shortcut for one-click switching

Set the shortcut target to:

```text
"C:\Path\To\DisplayProfileManager.exe" --headless "Profile"
```

### Chain a theme and profile switch

```text
DisplayProfileManager.exe --theme "Theme" --headless "Profile"
```

---

## Steam Big Picture Mode watcher

`BigPictureMode.ps1` is a separate watcher script that monitors Steam's `webhelper.txt` log and calls the application with `--headless` when Big Picture Mode opens or closes.

```powershell
$logFile = "${env:ProgramFiles(x86)}\Steam\logs\webhelper.txt"
$appPath = "C:\Path\To\DisplayProfileManager.exe"
$global:BPM = $false

Get-Content $logFile -Tail 0 -Wait -ErrorAction SilentlyContinue | ForEach-Object {
    $line = $_

    if ($line -match "SP BPM" -and $line -match "CreatingPopup" -and -not $global:BPM) {
        Start-Process $appPath -ArgumentList '--headless "Profile 1"'
        $global:BPM = $true
    }
    elseif ($line -match "SP Desktop" -and $line -match "CreatingPopup" -and $global:BPM) {
        Start-Process $appPath -ArgumentList '--headless "Profile 2"'
        $global:BPM = $false
    }
}
```

Run it from a shortcut in `shell:startup` when automatic Big Picture Mode switching is desired. Update the application path and profile names for the local installation.

> For a script that launches Big Picture Mode when a profile is applied, see [Scripts — Launch Steam Big Picture Mode on profile apply](./scripts.md#launch-steam-big-picture-mode-on-profile-apply).

---

## DPM Shortcut Builder

DPM Shortcut Builder (`DPMShortcutBuilder.pyw`) is a standalone Python tool for creating launch shortcuts that switch a display profile before starting an application and restore a selected profile on exit.

**Requirements:** Python 3.8+ with Tkinter. `pywin32` is required for `.lnk` generation when using the Python version.

> The standalone `DPMShortcutBuilder.exe` bundles its Python dependencies.

![Shortcut Builder](../img/shortcut-builder.png)

**Workflow:**

1. Click **"New"** and choose a target application or supported script. The working directory is populated from the target when applicable.
2. Select the **"Display profile"** to apply before launch.
3. Select the **"Restore profile on exit"**. It defaults to the profile active when the shortcut starts, or a saved profile can be chosen explicitly.
4. Add optional **Pre-start applications** to run after the profile switch and before the target launches. Each supports arguments, **"Kill on exit"**, and a delay up to 10.0 seconds.
5. Give the shortcut a name and click **"Save"**. Use **"Export .lnk…"** to copy the generated shortcut elsewhere.

The **Launcher integration** panel provides launch strings for Steam, Epic Games, GOG Galaxy, Heroic, Playnite, and Generic / Desktop shortcuts.

> A target application and display profile are required before a shortcut can be saved.