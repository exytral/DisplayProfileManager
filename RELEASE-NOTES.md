# Display Profile Manager

## Display Engine Rewrite, Automation, and More

### 🖥️ Display Profiles

Manage complex multi-monitor desktop configurations from preset profiles. Configure per-monitor enable/disable state, primary display, resolution, refresh rate, rotation, DPI, HDR/ACM, and color profiles; extend or clone displays in any combination; and reliably restore configurations when displays change, wake from deep sleep, or an apply fails. Capture and restore wallpaper, switch default playback and recording devices, and attach helper scripts to run after display configuration has finished.

See [Creating and Managing Profiles](./docs/wiki/profiles.md) for profiles, displays, wallpaper, audio, scripts, and hotkeys.

### 💻 CLI

Control the application through scripts and external tools, with commands that work against the running application or independently when it is not running.

See [CLI & DPM Shortcut Builder](./docs/wiki/cli.md) for available commands, usage examples, and shortcut creation.

**🎮 DPM Shortcut Builder** — included standalone Python tool for creating game/app launch shortcuts that switch display profiles before launch and restore a selected profile on exit, with guided launcher integration for Steam, Epic Games, GOG Galaxy, Heroic, and Playnite.

### 🎨 UI

Customize the application with a cleaner, more consistent visual system built around shared control styles, three packaged themes, and imported `.xaml` themes. Packaged themes integrate with the Windows accent color, while custom themes retain their own colors and accents.

See [Themes & DPM Theme Builder](./docs/wiki/themes.md) for built-in themes, custom themes, and theme generation.

**🛠️ DPM Theme Builder** — included standalone Python tool for generating compatible `.xaml` themes from the [tinted-theming/schemes](https://github.com/tinted-theming/schemes) database.

### ⚙️ Automation & integration

Switch profiles with global hotkeys, the system tray, or the desktop classic right-click menu, and use the CLI for external automation. Optional update checking notifies about newer releases without the application directly downloading or installing them.

---

## 2.2.0 — Wallpaper, desktop context menu, HDR, display recovery & more

### 🖼️ Wallpaper

- **Wallpaper capture and apply** — profiles can capture and restore Solid Color, per-monitor Pictures, Slideshow settings, and Windows Spotlight, with configurable settings.

### 🖱️ Desktop context menu

- **Desktop profile switcher** — apply profiles directly from the desktop's classic right-click menu through a new **Display Profiles** submenu.
- **Shell integration** — the context-menu extension is installed per user and can apply profiles without requiring the app to be already running.

### 🖥️ Display

- **EDID-based display matching** — profiles can follow the same physical monitor when it moves to another connector, using stored panel identity when available.
- **Dynamic refresh-rate support** — supported displays preserve virtual refresh-rate information during profile queries and application.
- **Display wake handling** — displays that are unavailable are kept out of the stabilization wait, while displays that are temporarily missing from the active path during wake-up remain eligible for later polling.
- **HDR and ACM detection** — HDR and Auto Color Management state are read from Windows directly, improving detection on systems where the previous detection could conflate the two states.
- **Display recovery** — certain Windows display-configuration failures can now recover through the supplied display configuration instead of failing unconditionally.

### 🛡️ Reliability

- **Configurable rollback** — when enabled, a failed display application can stop the pipeline and restore either the previous profile or the desktop state captured before the apply.
- **Clone-aware recovery** — pre-apply display state preserves cloned topology so rollback can reconstruct mirror configurations.
- **Safer saved state** — profile and settings saves are hardened against transient or interrupted file-system operations.

### 🎨 Themes and UI

- **Neutral Dark theme** — the packaged Dark theme now uses neutral Windows-style values while retaining Windows accent integration.
- **Theme accent behavior** — packaged themes use the Windows accent color while custom themes retain their configured accents.
- **Overlay scrollbars** — shared overlay scrollbars reduce the layout space consumed by scrolling controls.
- **Per-script enable state** — each script row now has its own enable checkbox, allowing individual scripts to be skipped without removing them from the profile.

### ⚙️ Settings and updates

- **Optional update checking** — update checks are disabled by default. When enabled, the application checks GitHub immediately and at startup. Newer releases are shown in the status bar, About section, and Windows notifications after the seven-day age threshold.

### 🧰 Project modernization

- **.NET 10** — the application and test projects move from .NET Framework 4.8 to the SDK-style .NET 10 Windows target.
- **Updated build and testing** — the build, packaging, and test infrastructure are updated from MSTest 3.6.3 to 4.3.3.

### 🔧 General refinement

- **General refinement** — various code cleanup, bug fixes, reliability improvements, refactors, UI refinements, and optimizations.

---

## 2.1.2 — DPM Shortcut Builder fix

### 🎮 DPM Shortcut Builder

- **Apply completion** — generated shortcuts now wait for a headless profile apply to finish before continuing, preventing launches or restore actions from starting before the display change has completed.

---

## 2.1.1 — Profile editor, CLI, virtual monitor fixes

### 🖥️ Display

- **Profile editor save** — resolution, refresh rate, rotation, DPI, HDR, ACM, and color-profile changes made in the editor are now saved correctly.
- **Disconnected monitor handling** — deep-sleep monitors are no longer incorrectly treated as physically disconnected during profile application.
- **IDD virtual monitor crash** — software virtual monitors no longer cause an unhandled exception during profile switches.

### 💻 CLI

- **`--headless` exit code** — `--headless` now returns exit code `1` on apply failure or profile not found instead of always returning `0`, allowing external tools to determine whether the application succeeded.

### 🎮 DPM Shortcut Builder

- **Shortcut creation** — create game and app shortcuts that automatically switch to a chosen display profile before launch and restore the previous or chosen profile on exit.
- **Pre-start applications** — queue scripts or executables to run after the profile switch but before the target launches, with optional kill-on-exit and a configurable delay up to 10 seconds.
- **Launcher integration** — built-in guides for Steam, Epic Games, GOG Galaxy, Heroic, Playnite, and generic desktop shortcuts.
- **Save and export** — generated shortcuts are stored under `%AppData%\DisplayProfileManager\Shortcuts\`, while the `.lnk` can be exported elsewhere.

---

## 2.1.0 — Color profiles, ACM, Clone fixes, Polish

### 🎨 Color profiles

- **Per-display color profiles** — assign a ICC/ICM color profile per monitor. HDR-capable profiles are shown when HDR is active, while all installed profiles are shown otherwise. `Not Applied` leaves Windows' existing assignment untouched.

### 🖥️ Display

- **ACM support** — Auto Color Management can be configured per display when supported and is forced on while HDR is active. Dedicated Windows 11 24H2+ support is used where available.
- **Clone group fixes** — any display can be explicitly selected as the clone source, primary ownership transfers correctly, and "Break Clone" restores the attached display's saved pre-clone state.
- **Clone group details** — clone groups are rendered correctly in the Details panel.
- **Rotation "Not Applied"** — rotation can be left unchanged when applying a profile.
- **Native resolution markers** — native resolution and peak refresh rate are marked with `★` (star).

### 🛡️ Reliability

- **Auto-start detection** — Task Scheduler auto-start is no longer reported as enabled when registration was denied elevation.
- **IPC pipe reliability** — the named pipe no longer fills logs with `ERROR_PIPE_BUSY` between connections.

### 🖼️ Profile icons

- **Icon state fixes** — profile icons collapse correctly when files are deleted and are reflected in the tray and other profile surfaces outside an explicit apply.

### 🔧 Other changes

- **General cleanup** — various dead-code cleanup, refactors, UI refinements, and optimizations.

---

## 2.0.5 — CLI headless fallback fix

### 💻 CLI

- **`--headless` without a profile argument** — now correctly reapplies the saved profile. Previously, running `-h` with no argument and no existing instance could launch the full UI instead of applying the current profile and exiting.

---

## 2.0.4 — Custom icons and UI fixes

### 🖼️ Profile icons

- **Profile icon picker** — import `.ico` files, choose from saved icons in a scrollable grid, or clear the assignment without deleting the file.
- **Profile icon display** — custom icons appear in the profile list, Details panel, and system tray.

### 📦 Dependencies

- **Dependency updates** — NLog updated to 6.1.3 and Newtonsoft.Json updated to 13.0.4.

### 🔧 General refinement

- **General refinement** — various code cleanup and UI refinements.

---

## 2.0.3 — Audio rewrite and fixes

### 🔊 Audio

- **Audio API rewrite** — AudioSwitcher is replaced with a direct Windows audio API implementation.
- **Background audio loading** — the profile editor loads the audio device list asynchronously so device discovery does not block the initial profile editor window launch.

---

## 2.0.2 — Schema migration, script types, disconnected display handling

### 📜 Scripts

- **Script support** — `.vbs`, `.js`, and `.ahk` scripts are now supported.
- **`.lnk` import** — `.lnk` files already in the scripts sandbox are no longer duplicated during import.

### 🖥️ Display

- **Disconnected display handling** — disconnected displays are detected before apply and skipped instead of waiting through full timeout.
- **Clone restoration** — broken clone members restore to their native panel resolution instead of the highest available resolution.
- **Monitor names** — display names now use the friendly monitor name instead of the raw WMI string.

### 🗂️ Profiles

- **Profile schema migration** — profiles now carry a schema version and are migrated automatically when the format changes.

### 🛡️ Reliability

- **Reliability fixes** — adapted fixes from [@xtrilla](https://github.com/xtrilla) reduce idle audio resource usage and make profile/settings saves and application exit safer against interrupted writes.

---

## 2.0.1 — Script file bug fixes

### 📜 Scripts

- **Script file picker** — `.py` and `.exe` files are now included in the script import filter.
- **`.exe` script imports** — `.exe` files are now converted to `.lnk` shortcuts correctly during import.
- **Script filenames with spaces** — filenames containing spaces are no longer split incorrectly during import or save.

---

## 2.0.0 — Display Engine Rewrite, CLI, Scripts, Custom Themes, and More

#### 🎉 A complete overhaul of the display engine, a full CLI rewrite, script execution, custom themes, and more; based on the original project by @zac15987, @jarandal, @jonathanasdf, @rvahilario, and other contributors.

### 🖥️ Display engine rewrite

Display configuration is now handled through a more reliable, atomic application pipeline.

- **Topology and layout separated** — enable/disable and clone grouping are applied separately from position, resolution, refresh rate, and rotation changes.
- **Mirror display support** — profiles support pure mirror, pure extended, and mixed configurations.
- **HDR fixed** — HDR enable/disable now uses the live display identity expected by Windows, correcting past target-ID failures.
- **Reliable display wake** — the application polls live display state instead of relying on a fixed delay when monitors are waking from deep sleep.
- **Redundancy checks** — topology, layout, HDR, and DPI changes are skipped when the live state already matches the profile.

### 📜 Scripts

- **Script execution** — profiles can run PowerShell, batch, Python, and executable-based scripts with custom arguments.
- **Per-profile enable/disable** — scripts remain stored when the script section is disabled.

### 💻 CLI

- **Command-line profile and theme control** — apply profiles, switch themes, and refresh state whether or not the application is already running.
- **Prefix-matched flags** — common command flags accept unambiguous shortened forms.
- **Named-pipe forwarding** — commands are sent to the existing application instance when one is running; otherwise supported commands fall back to local execution.

### 🎨 Custom themes

- **Rebuilt theme engine** — shared control styles live in a common base resource dictionary while individual themes provide their colors and brushes.
- **User themes** — custom `.xaml` files can be added to the themes folder and loaded without restarting the application.
- **Black theme** — new built-in dark theme.

**🛠️ DPM Theme Builder** — included standalone Python tool for generating DPM-compatible `.xaml` themes from the [tinted-theming](https://github.com/tinted-theming) database.

### ✨ UI

- **Inline Apply button** — apply profiles directly from their cards without selecting them first.
- **Double-click behavior** — double-click an unselected profile to apply it; double-click the selected profile to open the editor.

### 🔧 Other changes

- **General refinement** — various code cleanup, bug fixes, reliability improvements, refactors, UI refinements, and optimizations.

For a full technical breakdown, see [CHANGELOG.md](CHANGELOG.md).

---

# Current Release Assets

### 📥 Downloads

| File                                                   | Description                   |
| ------------------------------------------------------ | ----------------------------- |
| `DisplayProfileManager-{{VERSION}}-arm64-Portable.zip` | Portable — arm64              |
| `DisplayProfileManager-{{VERSION}}-Setup-arm64.exe`    | Installer — arm64             |
| `DisplayProfileManager-{{VERSION}}-x64-Portable.zip`   | Portable — 64-bit             |
| `DisplayProfileManager-{{VERSION}}-Setup-x64.exe`      | Installer — x64               |
| `DisplayProfileManager-{{VERSION}}-x86-Portable.zip`   | Portable — x86                |
| `DisplayProfileManager-{{VERSION}}-Setup-x86.exe`      | Installer — x86               |
| `DPMShortcutBuilder.exe`                               | Shortcut Builder (standalone) |
| `DPMShortcutBuilder.pyw`                               | Shortcut Builder (Python)     |
| `DPMThemeBuilder.exe`                                  | Theme Builder (standalone)    |
| `DPMThemeBuilder.pyw`                                  | Theme Builder (Python)        |

**Requirements:** Windows 10 version 1709+ · [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

> ACM support on supported displays requires Windows 11 22H2+. Dedicated HDR/ACM API support requires Windows 11 24H2+.