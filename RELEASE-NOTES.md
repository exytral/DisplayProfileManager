# Display Engine Rewrite, CLI, Scripts, Custom Themes & More

## Display Profile Manager 2.1.1

#### 🎉 A complete overhaul of the display engine, a full CLI rewrite, script execution, custom themes, and more.

---

### 🖥️ Display engine rewrite

Profile switching is now truly atomic and reliable across all configurations.

- **Topology and layout cleanly separated** — enable/disable and clone grouping applied in one phase, resolution/position/rotation/refresh in another, both via `SetDisplayConfig`. No more legacy API calls.
- **Mirror display support** — clone one output to two displays. Profiles support pure mirror, pure extended, and mixed configurations.
- **HDR and ACM** — enable or disable HDR and Auto Color Management per display. ACM is independently toggleable; forced on when HDR is active. Requires Windows 11 22H2 or later for ACM; the control is hidden on unsupported displays.
- **Color profile per display** — assign an ICC/ICM color profile to each monitor, applied automatically on profile switch. Shows only HDR-capable profiles when HDR is active; shows all installed profiles otherwise. Profiles no longer installed appear as `(not found)` rather than silently disappearing.
- **Reliable wake from sleep** — polls for live display state instead of waiting an arbitrary fixed delay. Profiles apply correctly even when monitors are waking from deep sleep.
- **Redundancy checks** — topology, layout, HDR, and DPI are skipped if the live state already matches the profile, avoiding unnecessary calls.

---

### 🔊 Audio rewrite

Audio device switching has been rewritten from the ground up.

- **Direct Windows API** — AudioSwitcher replaced with a native `IMMDeviceEnumerator` / `IPolicyConfig` wrapper. No third-party library, no persistent background activity. Device enumeration that previously took multiple seconds on every profile apply and editor open now completes in single-digit milliseconds.

---

### 📜 Scripts

Run scripts automatically when a profile is applied.

- Supports `.exe`, `.ps1`, `.bat`/`.cmd`, `.vbs`/`.js`, `.py`, and `.ahk`
- Custom launch arguments per script
- Per-profile enable/disable toggle — scripts stay stored in the profile when disabled

---

### 💻 CLI

Commands work whether or not the app is already running, forwarded to the running instance via named pipe with local fallback.

| Flag | What it does |
|------|-------------|
| `--profile "name\|ID"` | Apply a profile by name or ID |
| `--headless "name\|ID"` | Apply a profile and exit — no UI shown |
| `--theme "name"` | Switch to a specified theme |
| `--refresh`/`--reload`/`-r` | Refreshes profiles and themes |
| `--tray` | Start minimized to tray |

Flags are fuzzy-matched by prefix — `--pro`, `-p`, `pro` all resolve to `--profile`.

---

### 🎨 Custom themes

- **Rebuilt theme engine** — control styles live in a shared base file; individual theme files contain only colors and brushes. Easy to create and maintain custom themes.
- **User themes folder** — drop a `.xaml` file into `%AppData%\DisplayProfileManager\Themes\` and it appears in the dropdown on next refresh. No restart required.
- **New Black theme** — OLED-friendly built-in theme.
- **DPM Theme Builder** — included standalone Python tool. Generates DPM-compatible `.xaml` themes from the [tinted-themes](https://github.com/tinted-theming/tinted-themes) database. Auto-applies on save.

---

### ✨ UI

Refreshed to a cleaner, more minimal layout.

- Inline apply button on profile cards — hover to reveal, no selection required
- Double-click a profile to apply it; double-click the selected profile to open the editor
- Assign a custom icon per profile; shown in the profile list, details panel, and system tray when active

---

### 🔧 Other changes

- Various cleanup and optimizations

---

## 2.1.1 - Profile editor, CLI, virtual monitor fixes

- **Profile editor now saves changes** — resolution, refresh rate, rotation, DPI, HDR, ACM, and color profile changes made in the editor were silently discarded on save after 2.1.0 clone restore changes.
- **Disconnected monitor check reverted** — pre-topology detection incorrectly identified deep-sleep monitors as disconnected, excluding them from the defer wait and causing immediate layout failure. Reverted to original defer behavior.
- **`--headless` exit code** — `--headless` now correctly returns exit code 1 on apply failure or profile not found (was always 0).
- **IDD virtual monitor crash** — software virtual monitors (SuperDisplay, Spacedesk, etc.) no longer cause an unhandled exception during profile apply.

### 🎮 DPM Shortcut Builder

Create game and app shortcuts that automatically switch to a chosen display profile before launch and restore the previous (or chosen) on exit.

- Pick a target app or game, assign a **Display profile** to switch to on launch, and a **Restore profile** for when it exits.
- **Pre-start applications** — queue scripts or executables to run after the profile switch but before the game opens, each with optional kill-on-exit and a configurable delay up to 10.0s.
- **Launcher integration** — built-in guides for Steam, Epic Games, GOG Galaxy, Heroic, Playnite, and generic desktop shortcuts.
- **Save** stores the shortcut to `%AppData%\DisplayProfileManager\Shortcuts\`; **Export** copies the `.lnk` anywhere you like.

**Requirements:** Python 3.8+ with Tkinter (standard on Windows). `pywin32` is required for `.lnk` generation — `pip install pywin32`.

> The standalone `DPMShortcutBuilder.exe` bundles all dependencies and does not have requirements.

---


### 📥 Downloads

| File | Description |
|------|-------------|
| `DisplayProfileManager-2.1.1-arm64-Portable.zip` | Portable — arm64 |
| `DisplayProfileManager-2.1.1-Setup-arm64.exe` | Installer — arm64 |
| `DisplayProfileManager-2.1.1-x64-Portable.zip` | Portable — 64-bit |
| `DisplayProfileManager-2.1.1-Setup-x64.exe` | Installer — 64-bit |
| `DisplayProfileManager-2.1.1-x86-Portable.zip` | Portable — 32-bit |
| `DisplayProfileManager-2.1.1-Setup-x86.exe` | Installer — 32-bit |
| `DPMShortcutBuilder.exe` | Shortcut Builder (standalone) |
| `DPMShortcutBuilder.pyw` | Shortcut Builder (Python) |
| `DPMThemeBuilder.exe` | Theme Builder (standalone) |
| `DPMThemeBuilder.pyw` | Theme Builder (Python) |

**Requirements:** Windows 10 1709 or later · [.NET Framework 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework)

> **Note:** ACM requires Windows 11 22H2 or later. Full ACM and HDR API support requires Windows 11 24H2 or later.