# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

## Project Overview

Display Profile Manager is a Windows desktop application for managing display profiles (resolution, refresh rate, rotation, DPI, color profile, HDR, audio devices, scripts) with system tray control. Built with C# (.NET Framework 4.8) and WPF.

This is a fork by [exytral](https://github.com/exytral) based on [zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager).

## Updating This File

- **Current state only** — describe how things are, not how they changed. No "formerly", "previously", "renamed from", "now exposes", or version-delta language.
- **No internal version references** — do not mention which release introduced or changed something. That belongs in `CHANGELOG.md`.
- **When in doubt, rewrite the affected section from scratch** rather than patching around existing wording.

## Build and Run Commands

```bash
# Build
cmd.exe //c "msbuild DisplayProfileManager.sln /p:Configuration=Debug"
cmd.exe //c "msbuild DisplayProfileManager.sln /p:Configuration=Release"

# Clean and rebuild
cmd.exe //c "msbuild DisplayProfileManager.sln /t:Rebuild /p:Configuration=Debug"

# Run
cmd.exe //c "start bin\Debug\DisplayProfileManager.exe"

# Build for specific platforms (x86, x64, ARM64 supported)
cmd.exe //c "msbuild DisplayProfileManager.sln /p:Configuration=Release /p:Platform=x64"

# Dev script (auto-discovers Visual Studio via vswhere)
powershell -File dev-build.ps1 [-Configuration Debug|Release] [-Platform x86|x64|ARM64]
```

## Architecture

### Core Patterns
- **Singletons**: `ProfileManager`, `SettingsManager`, `ScriptManager` for global state (thread-safe, double-check locking)
- **Async/Await**: All file I/O and display apply operations
- **P/Invoke**: Windows Display Configuration, DPI, and Audio APIs via Helper classes
- **MVVM**: ViewModels for UI state management
- **Logging**: NLog with structured logging

### Key Components

Listed roughly in order of invocation during `ApplyProfileAsync`:

- **ProfileManager** — Thread-safe singleton for profile CRUD and application. Stores individual `.dpm` files in `%AppData%\DisplayProfileManager\Profiles\`. Core method: `ApplyProfileAsync(Profile)` returns `ProfileApplyResult`. Orchestrates the full apply sequence: topology → defer → layout + advanced color + color profiles → DPI → audio → scripts. Also handles schema migration via `MigrateProfileAsync` on load.
- **DisplayConfigHelper** — Primary Windows Display Configuration API wrapper. All topology and layout changes go through here. Implements `ApplyDisplayTopology`, `DeferDisplayLayoutAsync`, `ApplyDisplayLayout`, `ApplyDisplayConfig` (async), `ApplyAdvancedColorState`, `ApplyColorProfiles`, `VerifyDisplayConfiguration`, `ValidateCloneGroups`. `SetHdrState` and `SetAcmState` handle per-display Advanced Color routing with Windows version branching (24H2+ uses dedicated type 16/17 API). See **Display Configuration Engine** below.
- **DpiHelper** — System-wide DPI scaling via P/Invoke, adapted from the windows-DPI-scaling-sample project. Called after layout is committed.
- **AudioHelper** — Direct native COM/WASAPI integration for low-overhead audio profile configuration and endpoint switching.
- **ScriptManager** — Thread-safe singleton for script management. Sandboxed scripts folder at `%AppData%\DisplayProfileManager\Scripts\`. All scripts are copied into this folder on import. Exposes `ExecuteScript(Script)`, `ImportScriptAsync`. `IsEnabled = false` — field exists on `Script`; not currently checked in `ExecuteScript`. Present for future use.
- **ColorProfileHelper** (`Helpers/ColorProfileHelper.cs`) — P/Invoke wrapper for `mscms.dll`. `GetSystemColorDirectory` returns the system color store path. `GetInstalledColorProfilesFiltered(hdrOnly)` enumerates installed `.icc`/`.icm` files, optionally restricted to HDR-capable profiles via ICC tag inspection (MHC2 presence or CICP transfer function 16/18). `GetDisplayDefaultColorProfile` reads the current per-display OS association. `ApplyColorProfile` sets the default via `ColorProfileSetDisplayDefaultAssociation` with per-user scope.
- **IconHelper** — Icons sandbox management (`Helpers/IconHelper.cs`). `GetIconsFolderPath` creates `%AppData%\DisplayProfileManager\Icons\` on demand. `ResolveIconPath` rejects path traversal. `LoadImageSource` returns a frozen `BitmapImage` with an in-process `ConcurrentDictionary` cache keyed by `filename|size|lastWriteUtcTicks` — stale entries evict automatically when files change externally. `ImportIconAsync` copies `.ico` files with conflict-resolution renaming. `GetAvailableIcons` returns a sorted list of bare filenames.
- **SettingsManager** — Thread-safe singleton for app settings. Manages auto-start mode (Registry or Task Scheduler), default profile, current theme, and other persisted preferences.
- **ThemeHelper** — Theme loading, registration, and switching. Manages built-in themes and user themes from `%AppData%\DisplayProfileManager\Themes\`. Exposes `AvailableThemes` (live list) and `RefreshThemes` (public, rescans and reapplies).
- **DisplayGroupHelper** (`Helpers/DisplayGroupHelper.cs`) — Groups display settings for UI rendering. `GroupDisplaysForUI` aggregates clone group members behind a representative setting and returns a flat list of display groups. Called from both `MainWindow.UpdateProfileDetails` and `ProfileEditWindow.LoadDisplaySettings`.
- **GlobalHotkeyHelper** — System-wide hotkey registration using `RegisterHotKey`. Registers per-profile hotkeys; disables automatically when `ProfileEditWindow` is open.
- **AutoStartHelper** — Registry mode (no admin) or Task Scheduler mode (requires admin for setup, faster launch).
- **DisplayHelper** — Legacy API wrapper. Used only for checks (e.g. `IsMonitorConnected`).
- **TrayIcon** — System tray integration with dynamically generated context menu from profiles.
- **AboutHelper** — Version string resolution (`GetVersion`, `GetInformationalVersion`), settings path helper, and static data for the Settings → About panel. `Libraries` nested class holds third-party library metadata. `Contributors` nested class holds contributor names, URLs, link labels, and feature request credits used by `SettingsWindow.LoadContributors` to render the contributors list. Each contributor entry has a `Name`, `Url`, `Desc`, an optional `LinkLabel`/`LinkUrl` pair (renders as a hyperlink in parentheses between the name and description), and an optional `SubText` (italic line below, for community request credits).

### Display Configuration Engine

The application uses the Windows Display Configuration API (`SetDisplayConfig`) for atomic, reliable profile switching. The apply flow in `ProfileManager.ApplyProfileAsync` is:

1. **`ApplyDisplayTopology`** — Performs a fresh `QueryDisplayConfig` to get the current live state, then enables/disables displays and sets clone group topology via `SDC_TOPOLOGY_SUPPLIED` with a null mode array (Windows chooses modes). Clone groups must be set here — once the mode array is used for layout in the next step, clone groups cannot be changed without invalidating mode indices. Skips the `SetDisplayConfig` call entirely if live topology checks already match the profile.

2. **`DeferDisplayLayoutAsync`** — Polls every 250ms (up to 10s timeout) until all expected displays are live and reporting valid dimensions. Only waits for displays confirmed present during disconnected display detection — disconnected displays are excluded so they don't cause a full timeout.

3. **`ApplyDisplayConfig`** (async) — Calls `ApplyDisplayLayout`, then `ApplyAdvancedColorState`, then `ApplyColorProfiles`.
   - **`ApplyDisplayLayout`** — Issues a fresh `QueryDisplayConfig` (raw IDs from the pre-topology snapshot are stale after `SetDisplayConfig` reconfigures the adapter) then applies resolution, position, rotation, refresh rate, and SourceId normalization via `SDC_USE_SUPPLIED_DISPLAY_CONFIG`. Skips the `SetDisplayConfig` call entirely if all live checks already match the profile. Rotation is skipped when `profile.Rotation == 0` ("Not Applied"). On non-zero return, cross-checks with `VerifyDisplayConfiguration` before failing — Windows sometimes returns non-fatal codes on valid configs.
   - **`ApplyAdvancedColorState`** — Issues a fresh `GetDisplayConfigs` query after topology apply to get live `RawTargetId` values. For each enabled display with HDR capability: toggles HDR only if live state differs. ACM is forced on when HDR is on; independently toggled otherwise. `SetHdrState` uses `DisplayConfigSetHdrState` (type 16) on Windows 11 24H2+, falls back to the legacy advanced color path. `SetAcmState` uses `SetWcgState` (type 17) on 24H2+; ACM is not supported on HDR-capable displays before 24H2.
   - **`ApplyColorProfiles`** — For each enabled display with a non-null `ColorProfile`, builds a transient `DisplaySetting` from live config (to supply the correct `AdapterLuid` and `SourceId`) and calls `ColorProfileHelper.ApplyColorProfile`.

4. **DPI** — Applied per device via `DpiHelper.SetDPIScaling` after layout is committed.

5. **Audio** — Switched to configured playback/recording devices if specified in the profile's `AudioSettings`.

6. **Scripts** — Executed via `ScriptManager.ExecuteScript` if `EnableScripts` is set on the profile.

**Critical implementation notes:**

- **Never use `ChangeDisplaySettingsEx` for topology or resolution changes, always use `DisplayConfigHelper` methods.** `ApplyDisplayLayout` handles resolution atomically inside `SetDisplayConfig`.
- **SourceId normalization is handled inside `ApplyDisplayTopology` and `ApplyDisplayLayout` via `BuildSourceIdMap` — do not normalize in `ApplyProfileAsync`.** Profiles store raw `SourceId` values which may have gaps when monitors are disabled (e.g. 0, 2, 3 after disabling monitor 1); `SetDisplayConfig` rejects non-contiguous IDs. Normalization is a submission detail handled at the point of each `SetDisplayConfig` call, not at the orchestration layer — `displayConfigs` must preserve the original saved values so that logging, `VerifyDisplayConfiguration`, and clone group detection remain accurate throughout the apply flow.
- **Clone groups must be set in `ApplyDisplayTopology` with `SDC_TOPOLOGY_SUPPLIED`.** Once the mode array is used for resolution/position, clone groups cannot be changed without invalidating mode indices. `SDC_TOPOLOGY_SUPPLIED` must be present in `SetDisplayConfigFlags` for this to work correctly.
- **HDR and ACM are distinct.** Do not conflate them. `IsHdrEnabled` and `IsAcmEnabled` are separate API flags; HDR does not imply ACM and vice versa. On pre-24H2 systems, the legacy advanced color path uses a shared toggle — `SetAdvancedColorState` with `Acm` intent must reset the state to `Off` first so Windows initializes ACM rather than re-engaging HDR.
- **HDR requires a live `RawTargetId`, not the stored profile `TargetId`.** Always call `GetDisplayConfigs` fresh after topology apply, match by base `TargetId` (lower 16 bits), and use `activeDisplay.RawTargetId` for `DisplayConfigSetDeviceInfo`.
- **Disconnected display detection runs after mapping, before topology.** `ApplyProfileAsync` checks enabled profile displays against live `GetDisplayConfigs()` results by `TargetId`. Missing displays are recorded in `ProfileApplyResult.DisconnectedDisplays`, logged as warnings, and excluded from the defer wait — the rest of the profile still applies. This surfaces a specific error immediately rather than hanging through the full defer timeout.

### Clone Group Implementation

Clone groups enable display mirroring (multiple monitors showing identical content):

- Encoded in `DISPLAYCONFIG_PATH_SOURCE_INFO.modeInfoIdx`: lower 16 bits = clone group ID (`modeInfoIdx & 0xFFFF`), upper 16 bits = source mode index (`modeInfoIdx >> 16`). Write via `ResetModeAndSetCloneGroup()` for Phase 1; direct field assignment for Phase 2. In `ApplyDisplayLayout`: all members of a clone group share one source mode entry, keyed by normalized `SourceId`. **Do not consume a separate mode entry per display** — that prevents cloning of non-primary displays.
- Detection: `GetCurrentDisplaySettingsAsync()` groups displays by `SourceId` only (not `DeviceName + SourceId`).
- **`Clone()` saves all attached-member pre-clone state BEFORE any modifications.** The primary-transfer block runs after the save loop — it clears `IsPrimary` on attached members, so saving after would record `false` instead of the original value. Correct order: save all `Original*` fields → transfer primary → set clone markers.
- **`BreakClone()` restores each attached member's full pre-clone display configuration** from values saved in `Clone()`. Restored fields: position (`DisplayPositionX/Y`), `SourceId`, resolution (`Width/Height`), `Frequency`, `DpiScaling`, `Rotation`, `ColorProfile`, `IsHdrEnabled`, `IsAcmEnabled`, and `IsPrimary`. Falls back to native resolution and a position to the right of the source if no saved values are available (old profiles loaded from disk where `[JsonIgnore]` params were never serialized).
- **`_cloneGroupMembers` is `public`** to allow `RebuildDisplayControls` in `ProfileEditWindow` to read member device names for sort-order capture before a rebuild.
- **`BreakClone()` does NOT clear `IsCloneSource`** before triggering the `RebuildDisplayControls` rebuild. `GetDisplaySettings()` uses `IsCloneSource` to route each member to its correct parameter source (see below). `GetDisplaySettings()` itself produces output with `IsCloneSource = false` for all members when `CloneGroupId` is empty, so the new independent controls are always created correctly.
- **`BreakClone()` is order-independent.** It partitions `_cloneGroupMembers` by `IsCloneSource` rather than assuming index 0 is the source. This is necessary because `DisplayGroupHelper.GroupDisplaysForUI` preserves the settings-list order, which may place the attached display first if it had a lower path index than the source.
- **`RebuildDisplayControls()` captures device order from `_profile.DisplaySettings` before the rebuild.** Using `_cloneGroupMembers` instead would place any interleaved independent display after both clone members, since the clone group control lists source before attached.

**`GetDisplaySettings()` — source vs attached routing:**

```csharp
bool useOwnParams = !originalSetting.IsCloneSource && string.IsNullOrEmpty(originalSetting.CloneGroupId);
```

| Situation | `IsCloneSource` | `CloneGroupId` | `useOwnParams` | Result |
|-----------|-----------------|----------------|----------------|--------|
| Active clone — source | `true` | non-empty | `false` | reads combo |
| Active clone — attached | `false` | non-empty | `false` | reads combo (shares source settings) |
| After BreakClone — source | `true` (retained) | `""` | `false` | reads combo (keeps merged-control value) |
| After BreakClone — attached | `false` | `""` | `true` | reads own restored params |
| Independent display | `false` | `""` | `true` | reads own params (single member; same as combo) |

Fields that respect `useOwnParams`: `Width`, `Height`, `Frequency`, `DpiScaling`, `Rotation`, `IsHdrEnabled`, `IsAcmEnabled`, `ColorProfile`. Fields that do not: identity fields, layout (`DisplayPositionX/Y`), `IsEnabled`, `IsHdrSupported`, native dimensions, capabilities.

`IsPrimary` uses `originalSetting.IsPrimary` directly — not gated on list position. After `BreakClone`, attached members have the restored value (`OriginalIsPrimary`) and the source has `!attachedHadPrimary && !primaryExistsElsewhere`.

`IsCloneSource` in output: `originalSetting.IsCloneSource && !string.IsNullOrEmpty(originalSetting.CloneGroupId)` — always `false` for members with empty `CloneGroupId`, ensuring independent controls never inherit the clone-source flag.

**Clone params** (`[JsonIgnore]`, not serialized; populated in `Clone()`, cleared in `BreakClone()`)

| Property | Description |
|----------|-------------|
| `OriginalPositionX`, `OriginalPositionY` | Virtual desktop position before clone overwrote it. |
| `OriginalSourceId` | Adapter SourceId before clone overwrote it. |
| `OriginalWidth`, `OriginalHeight` | Resolution before the shared-source resolution was applied. |
| `OriginalFrequency` | Refresh rate before the shared-source rate was applied. |
| `OriginalIsPrimary` | Whether this display was primary before it became an attached clone member. |
| `OriginalDpiScaling` | DPI scaling percentage before clone. |
| `OriginalRotation` | Rotation value before clone. |
| `OriginalColorProfile` | Color profile filename (or `null` for "Not Applied") before clone. |
| `OriginalIsHdrEnabled` | HDR enabled state before clone. |
| `OriginalIsAcmEnabled` | ACM enabled state before clone. |

These fields are copied through `GetDisplaySettings()` so they survive the `RebuildDisplayControls` call that runs immediately after `Clone()`. They are `[JsonIgnore]` so they do not persist in saved `.dpm` files; if a profile is saved while in cloned state and reloaded, Break Clone uses the fallback path.

**Clone Group Profile Structure:**
```json
{
  "displaySettings": [
    {
      "deviceName": "\\\\.\\DISPLAY1",
      "sourceId": 0,
      "targetId": 0,
      "width": 1920, "height": 1080, "frequency": 60,
      "displayPositionX": 0, "displayPositionY": 0,
      "cloneGroupId": "clone-group-1"
    },
    {
      "deviceName": "\\\\.\\DISPLAY1",
      "sourceId": 0,
      "targetId": 1,
      "width": 1920, "height": 1080, "frequency": 60,
      "displayPositionX": 0, "displayPositionY": 0,
      "cloneGroupId": "clone-group-1"
    }
  ]
}
```

Profiles can contain both clone groups and independent displays in the same configuration. Old profiles without `CloneGroupId` load normally (defaults to empty string = extended mode).

### Theme System

Themes are split into two layers:

1. **`Base.xaml`** — all control styles (TextBox, ComboBox, ScrollBar, ComboBoxItem, etc.). Shared across all themes. Adding a new theme does not require touching this file.
2. **Color/brush files** (e.g. `LightTheme.xaml`, `DarkTheme.xaml`, `BlackTheme.xaml`) — define only brush and color keys. Base `Color` keys (`BackgroundColor`, `SurfaceColor`, `BorderColor`, `HoverColor`, `AccentColor`) are defined here; most brushes derive from these automatically.

**Built-in themes:** Light, Dark, Black. `System` is reserved (follows Windows theme).

**User themes:** Drop a `.xaml` color file into `%AppData%\DisplayProfileManager\Themes\`. It appears in the dropdown after the next refresh. Built-in theme names can be overridden by a same-named user file; `System` is protected.

**`ThemeHelper` key methods:**
- `AvailableThemes` — live list of all registered themes including user themes.
- `RefreshThemes()` — rescans folder, reregisters user themes (never touches built-ins), fires `ThemeChanged`. Call this after any file-system change to the themes folder (also triggered by the Refresh button and `--refresh` CLI flag).
- `ImportThemeAsync()` — validates minimum required keys (`WindowBackgroundBrush`, `PrimaryTextBrush`, `ContentBackgroundBrush`, `BorderBrush`, `ButtonBackgroundBrush`, `ButtonForegroundBrush`), copies to themes folder, applies and persists. Files dropped directly into the folder bypass this validation.
- `InitializeTheme()` — falls back to System if saved theme key is missing; persists fallback.

**DPM Theme Builder** (`DPMThemeBuilder.pyw`) — standalone Python tool included with the release. Generates `.xaml` theme files from the [tinted-themes](https://github.com/tinted-theming/tinted-themes) database. A 0.5s polling loop watches the themes folder for new or modified `.xaml` files. When a change is detected, DPM is signaled via `--theme <name>` (if the file is in the themes folder) or `--refresh` (otherwise), removing the need for a manual refresh step.

### CLI

All flags accept any number of leading dashes or none at all — `--profile`, `-profile`, `profile` are all equivalent. The argument string is lowercased and all leading dashes stripped before matching.

`--tray` and `--dev` are matched exactly (after stripping dashes). All other flags use prefix matching — any unambiguous prefix resolves to the full flag name.

| Flag | Behavior |
|------|----------|
| `--tray` | Start minimized to tray. Exact match only. |
| `--dev` | Bypass single-instance check (for build scripts). Exact match only. |
| `--refresh`/`--reload`/`-r` | Rescan profiles and themes folder, reapply current theme. Does not re-apply the active display profile. |
| `--theme` [name] | Apply named theme. No name = refresh current theme. |
| `--profile` [name\|ID] | Apply profile by name or ID. No argument = reapply current active profile. |
| `--headless` [name\|ID] | Apply profile and exit without UI. No argument = reapply current active profile headlessly. |

**Argument matching:** Profile name and theme name arguments are matched case-insensitively. Flag names are fuzzy-matched by prefix (e.g. `--pro` resolves to `--profile`).

**IPC:** Commands are sent to a running instance via named pipe (`DPM_ProfilePipe`) first. Falls back to local execution if no instance is found. `--refresh` and `--theme` with no argument exit if no instance is running rather than launching UI. `--headless` forwards to a running instance if one exists, otherwise applies locally and exits — no window or tray icon is created in either case.

### Script System

- Scripts are sandboxed to `%AppData%\DisplayProfileManager\Scripts\`. When a script is imported, it is copied into this folder. Arbitrary paths outside this folder are not supported.
- `Profile.Scripts` is `List<Script>`. Each entry carries `FileName` (bare sandbox filename), `Arguments` (empty string default), and `IsEnabled` (bool, default true). `IsEnabled` is not currently checked in `ScriptManager.ExecuteScript`; all scripts in the list execute regardless of this flag.
- Supported types: `.lnk` (via shell execute), `.ps1` (via `powershell.exe -ExecutionPolicy Bypass`), `.bat`/`.cmd` (via `cmd.exe /c`), `.vbs`/`.js` (via `cscript.exe /nologo`), `.py` (via `python.exe`), `.ahk` (via `autohotkey.exe`). `.exe` files are automatically converted to `.lnk` shortcuts on import to avoid COM reference issues.
- `EnableScripts` is a per-profile section-level flag. When false, no scripts in that profile execute on apply. Scripts remain stored when `EnableScripts` is false.
- Arguments can be passed to any script type. They are appended after the script path in the constructed command.

### Data Storage

| Path | Contents |
|------|----------|
| `%AppData%\DisplayProfileManager\Icons\*.ico` | User icons |
| `%AppData%\DisplayProfileManager\Logs\*.log` | NLog daily rotation, 30-day retention |
| `%AppData%\DisplayProfileManager\Profiles\*.dpm` | User profiles (JSON) |
| `%AppData%\DisplayProfileManager\Scripts\` | User scripts |
| `%AppData%\DisplayProfileManager\Themes\*.xaml` | User themes |
| `%AppData%\DisplayProfileManager\Settings.json` | App settings |

### Profile Structure

A `Profile` object contains the following top-level properties:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Id` | `string` (GUID) | new GUID | Unique identifier |
| `Name` | `string` | `""` | Display name |
| `Description` | `string` | `""` | Optional description |
| `Icon` | `string` | `null` | Bare filename of a custom tray/list icon relative to `%AppData%\DisplayProfileManager\Icons\`, or `null` for no custom icon |
| `IsDefault` | `bool` | `false` | Applied on startup if set |
| `CreatedDate` | `DateTime` | now | Creation timestamp |
| `LastModifiedDate` | `DateTime` | now | Last save timestamp |
| `SchemaVersion` | `int` | `0` | Schema version for migration. Defaults to `0` so old profiles without this field trigger migration on first load. Current version is `3`. |
| `DisplaySettings` | `List<DisplaySetting>` | `[]` | Per-monitor settings (see below) |
| `AudioSettings` | `AudioSetting` | default | Playback/recording device config |
| `EnableAudio` | `bool` | `true` | Field present and copied in `DuplicateProfile`. Not currently checked in `ApplyProfileAsync`; `ApplyPlaybackDevice`/`ApplyCaptureDevice` on `AudioSetting` are the operative flags. |
| `EnableScripts` | `bool` | `true` | Whether scripts run on apply |
| `Scripts` | `List<Script>` | `[]` | Script objects; each carries `FileName`, `Arguments`, and `IsEnabled` |
| `HotkeyConfig` | `HotkeyConfig` | default | Global hotkey for this profile |

Each `DisplaySetting` entry (one per physical monitor) includes:

**Identity**

| Property | Description |
|----------|-------------|
| `DeviceName`, `DeviceString` | GDI device path (e.g. `\\.\DISPLAY1`) and adapter string |
| `ReadableDeviceName` | CCD friendly name from `DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME` (e.g. "Samsung Odyssey G60SD"). Preferred over WMI name. |
| `ManufacturerName`, `ProductCodeID`, `SerialNumberID` | EDID identifiers — used for hardware-specific profile matching |
| `AdapterId` | GPU adapter LUID as hex string |
| `AdapterLuid` | `[JsonIgnore]` — GPU adapter LUID; not stored. Populated at apply time from live config for use in color profile P/Invoke calls. |
| `TargetId` | Base target ID (lower 16 bits), stable across sessions — stored in profile. Used for migration backfill and disconnected display detection. |
| `SourceId` | Adapter source ID; shared by clone group members |
| `CloneGroupId` | Clone group identifier; empty string = extended (independent) mode |
| `PathIndex` | Display path enumeration index |

**State**

| Property | Description |
|----------|-------------|
| `IsEnabled` | Include/exclude this monitor from the profile |
| `IsPrimary` | Primary display flag — the display positioned at (0,0) is primary |

**Layout**

| Property | Description |
|----------|-------------|
| `DisplayPositionX`, `DisplayPositionY` | Position in the virtual desktop |

**Configuration**

| Property | Description |
|----------|-------------|
| `Width`, `Height` | Desired resolution |
| `Frequency` | Refresh rate in Hz |
| `Rotation` | Screen orientation: 0=Not Applied, 1=0°, 2=90°, 3=180°, 4=270° (maps to `DISPLAYCONFIG_ROTATION`). `ApplyDisplayLayout` skips rotation when value is 0. |
| `IsHdrSupported`, `IsHdrEnabled` | HDR hardware capability and desired state |
| `IsAcmEnabled` | ACM desired state. Forced on at apply time when `IsHdrEnabled` is true, regardless of this value. |
| `ColorProfile` | Bare ICC/ICM filename from the system color store. `null` = not applied (Windows untouched). |
| `DpiScaling` | Windows DPI scaling percentage |

**Native**

| Property | Description |
|----------|-------------|
| `NativeWidth`, `NativeHeight` | EDID preferred timing resolution from `targetVideoSignalInfo.activeSize` — the panel's physical pixel grid. Used by `BreakClone` to restore the correct resolution rather than defaulting to the highest supported (which may be a DCI resolution). Populated via `GetDisplayConfigs` during `GetCurrentDisplaySettingsAsync`. `0` on old profiles — backfilled during schema migration when the display is connected. |

**Capabilities**

| Property | Description |
|----------|-------------|
| `AvailableResolutions` | All supported resolutions, sorted by width descending |
| `AvailableRefreshRates` | Per-resolution refresh rate map |
| `AvailableDpiScaling` | Supported DPI scaling values |

### Schema Migration

`ProfileManager.LoadProfilesAsync` checks each profile's `SchemaVersion` against `CurrentSchemaVersion` (currently `3`). Profiles with `SchemaVersion < 3` are passed to `MigrateProfileAsync`.

`SchemaVersion` defaults to `0` in `Profile.cs` so that old profiles without this field deserialize to `0` and trigger migration automatically.

**Version 2 → 3:**
- Backfills `ColorProfile` on each `DisplaySetting` from `ColorProfileHelper.GetDisplayDefaultColorProfile` (user scope first, system fallback) by matching `TargetId` to live config. Disconnected displays are skipped and backfilled on next load when reconnected.
- `List<string>` → `List<Script>` promotion is handled transparently at deserialization via `ScriptListConverter`; no per-field migration step is needed.
- Bumps `SchemaVersion` to `3` and triggers a re-save.

**Version 1 → 2:**
- No data backfill — `Icon` defaults to `null` via `JsonProperty`
- Bumps `SchemaVersion` to `2` and sets `changed = true` to trigger a re-save

**Version 0 → 1:**
- Backfills `NativeWidth`/`NativeHeight` from live `GetDisplayConfigs()` by matching `TargetId`
- Updates `ReadableDeviceName` to use the CCD friendly name (`FriendlyName` from `DisplayConfigInfo`) instead of the WMI name
- If a display is not currently connected, that setting is skipped silently — backfill completes on next load when connected
- `LastModifiedDate` is preserved across the migration re-save

## Dependencies

- **.NET Framework 4.8**: WPF support
- **Newtonsoft.Json 13.0.4**: JSON serialization for profiles and settings
- **NLog 6.1.3**: Logging framework with daily file rotation
- **packages.config**: Traditional NuGet package management (not PackageReference) — legacy `.csproj` format

## Platform Requirements

- **Windows 10 version 1709+ required for core functionality.** Windows 7/8 are unsupported — basic single-display profile switching may incidentally work, but:
  - **HDR** — `DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO` is not available below Windows 10 1709; `DisplayConfigGetDeviceInfo` will fail silently or return garbage.
  - **DPI scaling** — per-monitor V2 awareness (`PROCESS_DPI_AWARENESS_CONTEXT`) requires Windows 8.1 Update 1+; below that, `DpiHelper` P/Invoke calls will return incorrect values.
  - **Clone groups** — `SDC_TOPOLOGY_SUPPLIED` behavior is unreliable on Windows 7/8 drivers; apply will fail non-deterministically.
  - **UI** — MDL2 icon font is not installed on Windows 7/8; icon buttons render as blank squares.
- **Windows 11 22H2+ required for ACM.** `IsAcmSupported` checks for this at runtime. On earlier builds, ACM is silently skipped; the toggle is hidden on unsupported displays.
- **Windows 11 24H2+ required for full HDR and ACM API support.** On earlier builds both fall back gracefully:
  - **HDR** — falls back to the legacy `DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE` path instead of `SetHdrState` (type 16).
  - **ACM** — `SetWcgState` (type 17) is unavailable; ACM is not supported on HDR-capable displays and is silently skipped for those targets.
- **Privileges**: Standard user (`asInvoker`). Admin only required for Task Scheduler auto-start setup.
- **Architectures**: AnyCPU (default), x86, x64, ARM64

## Development Guidelines

### Comment Style

- **Short, concise, single-line.** No `<summary>` tags, no step/phase headers, no multi-line blocks.
- **Verb-oriented wherever appropriate** — comments describe what happens and why, not what exists.
- **Don't explain self-explanatory code.** Skip comments for simple assignments, properties, or obvious one-liners.
  - **Structural Milestone Exception**: For long or complex methods, a single concise line per primary logical block is permitted to provide structural orientation and assist scannability.
  - **XAML.cs Layout Exception**: In code-behind files (`.xaml.cs`), short comments marking UI section boundaries or describing what a logical block of imperative UI construction does are permitted even when the code is self-explanatory. These orient readers who cannot see the corresponding `.xaml` in the same view and aid handoffs where only the `.cs` file is shared.
  - XAML comments `(<!-- ... -->)` follow the same single-line, verb-oriented rule. Use them sparingly for non-obvious layout decisions or binding sources. Do not comment self-explanatory XAML structure.
- **Log strings follow the same conciseness principle as comments** — short, verb-oriented, no filler. They are developer-facing and never shown in UI.
StatusTextBlock.Text strings are user-facing. Use past-tense completion phrasing ("Profile applied", "Profiles refreshed"). No "will be", no "for this profile" padding.

When unsure, look at the existing comment style in the file for guidance.

### Logging
```csharp
private static readonly Logger logger = LoggerHelper.GetLogger();
```
`LoggerHelper` automatically uses the calling class name via `StackFrame` reflection. Log levels: Trace (verbose), Debug (development), Info (normal), Warn, Error, Fatal. Output: `%AppData%\DisplayProfileManager\Logs\DisplayProfileManager-{date}.log`.

### Theme Changes
- Add a new built-in theme by creating a color/brush `.xaml` file in `UI/Themes/` and registering it in `ThemeHelper`.
- User themes require no code changes — drop into the themes folder and call `RefreshThemes`.
- Never hardcode theme names in UI dropdowns; always populate from `ThemeHelper.AvailableThemes`.

### Auto-Start Implementation
- Dual mode: Registry (no admin) vs Task Scheduler (admin required for setup).
- **Critical**: When using Task Scheduler without admin, use `UseShellExecute = true` + `Verb = "runas"` to trigger UAC. Never use `Verb = "runas"` with `UseShellExecute = false` (Verb is ignored by .NET).

### Error Handling
- Return boolean success/failure or strongly-typed result objects (`ProfileApplyResult`).
- Use NLog for all error logging with context (device names, settings attempted).
- Graceful degradation: return empty collections on failure, don't crash.
- P/Invoke errors: check return codes (`ERROR_SUCCESS = 0`).

### WPF UI Guidelines
- Extract reusable UI components as standalone `UserControl` in `UI/Controls/` with separate `.xaml` and `.xaml.cs` files. Do not use inner classes for UI components in code-behind.
- Use converters in `UI/Converters/` for data binding transformations.
- Theme resources: `Base.xaml` for control styles, individual color files in `UI/Themes/` for brush definitions.
- Never hardcode theme names or lists in XAML or code-behind; bind to `ThemeHelper.AvailableThemes`.
- **CheckBox / RadioButton alignment** — `Base.xaml` provides implicit styles with custom `Grid`-based templates that respect `VerticalContentAlignment`. Do not use negative padding (e.g. `Padding="-1"`) to compensate for misalignment — the root cause was WPF's default `BulletDecorator` template ignoring `VerticalContentAlignment`, fixed at the style level. Do not add keyed `CheckBox`/`RadioButton` styles in window resources; rely on the implicit style.

### JSON Serialization Notes
- All profile/settings properties use `[JsonProperty("name")]` attributes for consistent naming.
- New properties must have sensible defaults for loading old `.dpm` files (backward compatibility).
  - `IsHdrEnabled` → `false`, `Rotation` → `1` (0°), `EnableScripts` → `false`, `CloneGroupId` → `""` (extended mode), `NativeWidth`/`NativeHeight` → `0` (backfilled by migration), `SchemaVersion` → `0` (triggers migration on first load), `Icon` → `null` (no custom icon; `null` is the correct JSON absence), `EnableAudio` → `true`, `IsAcmEnabled` → `false`, `ColorProfile` → `null`.
  - `OriginalPositionX/Y`, `OriginalSourceId`, `OriginalWidth/Height`, `OriginalFrequency`, `OriginalIsPrimary`, `OriginalDpiScaling`, `OriginalRotation`, `OriginalColorProfile`, `OriginalIsHdrEnabled`, `OriginalIsAcmEnabled` → `null` (`[JsonIgnore]`, not serialized; ephemeral clone state only).

### Adding a Contributor

Two files need updating — `AboutHelper.cs` and `SettingsWindow.xaml.cs`.

**`AboutHelper.Contributors`** — add a constant group for the new entry:

```csharp
public const string ExampleName      = "@example";
public const string ExampleUrl       = "https://github.com/example";
public const string ExampleDesc      = "Short description of contribution";
public const string ExampleLinkLabel = "PR #99"; // omit if no link
public const string ExampleLinkUrl   = "https://github.com/zac15987/DisplayProfileManager/pull/99";
```

For forks use the repo URL as `LinkUrl` and `"Fork"` as `LinkLabel`. For the original project use the repo URL and `"Original project"`. Community requesters go in `SubText` on an existing entry.

**`SettingsWindow.xaml.cs` — `LoadContributors`** — add a new entry to the array:

```csharp
new
{
    Name        = AboutHelper.Contributors.ExampleName,
    Url         = AboutHelper.Contributors.ExampleUrl,
    LinkLabel   = AboutHelper.Contributors.ExampleLinkLabel,  // or (string)null
    LinkUrl     = AboutHelper.Contributors.ExampleLinkUrl,    // or (string)null
    Description = AboutHelper.Contributors.ExampleDesc,
    SubText     = (string)null
},
```

Order: upstream contributors first (chronological by contribution), then `@exytral`, then remaining contributers last (chronological by contribution).

### File Structure
```
DisplayProfileManager/
├── Core/
│   ├── HotkeyConfig.cs                          Per-profile hotkey definition
│   ├── Profile.cs                               Profile model + DisplaySetting, AudioSetting, HotkeyConfig
│   ├── ProfileManager.cs                        Thread-safe singleton; orchestrates ApplyProfileAsync and MigrateProfileAsync
│   ├── Script.cs                                Script model with FileName, Arguments, IsEnabled, ToString()
│   ├── ScriptManager.cs                         Thread-safe singleton; script CRUD and execution
│   └── SettingsManager.cs                       Thread-safe singleton; persists app settings to Settings.json
├── Helpers/
│   ├── AboutHelper.cs                           Version strings, settings path, Libraries and Contributors nested classes
│   ├── AudioHelper.cs                           Native Windows WASAPI/COM interface mapping for audio switching
│   ├── AutoStartHelper.cs                       Registry and Task Scheduler auto-start modes
│   ├── ColorProfileHelper.cs                    ICC/ICM profile enumeration and application via mscms.dll
│   ├── DisplayConfigHelper.cs                   Display engine — all SetDisplayConfig logic lives here
│   ├── DisplayGroupHelper.cs                    Groups display settings for UI rendering; clone group aggregation via GroupDisplaysForUI
│   ├── DisplayHelper.cs                         Legacy API wrapper; used only for IsMonitorConnected
│   ├── DpiHelper.cs                             System-wide DPI scaling via P/Invoke
│   ├── GlobalHotkeyHelper.cs                    RegisterHotKey / UnregisterHotKey management
│   ├── IconHelper.cs                            Icons sandbox — import, load (cached), and enumerate .ico files
│   ├── KeyConverter.cs                          WPF Key ↔ VirtualKey conversion for hotkeys
│   ├── LoggerHelper.cs                          NLog factory with automatic class-name detection
│   ├── ScriptHelper.cs                          Script execution — process launch for .lnk, .ps1, .bat, .vbs, .js, .py, .ahk
│   └── ThemeHelper.cs                           Theme registration, switching, folder scanning
└── UI/
    ├── Controls/
    │   └── HotkeyEditorControl.xaml(.cs)        Hotkey capture and edit control
    ├── Converters/
    │   ├── BooleanToVisibilityConverter.cs      bool → Visibility with Collapsed fallback
    │   ├── ProfileIconConverter.cs              Bare filename → ImageSource for profile list card binding
    │   └── RectConverter.cs                     Width + Height → Rect(0,0,w,h) for rounded clip geometry
    ├── Themes/
    │   ├── Base.xaml                            Shared control styles (TextBox, ComboBox, ScrollBar, CheckBox, RadioButton, etc.)
    │   ├── Black.xaml                           Black theme — Material dark palette, OLED-optimised
    │   ├── Dark.xaml                            Dark theme
    │   └── Light.xaml                           Light theme
    ├── ViewModels/
    │   └── ProfileViewModel.cs                  Profile display model; exposes Icon, Name, and binding-friendly properties
    ├── Windows/
    │   ├── CloseConfirmationDialog.xaml(.cs)    Minimize vs Exit prompt with Remember my choice
    │   ├── MainWindow.xaml(.cs)                 Profile list, details panel, action buttons; uses DisplayGroupHelper for clone group display
    │   ├── MonitorIdentifyWindow.xaml(.cs)      Numbered overlay for physical monitor identification
    │   ├── ProfileEditWindow.xaml(.cs)          Profile info, per-monitor settings editor, clone UI, audio, scripts panel; uses DisplayGroupHelper for display grouping
    │   └── SettingsWindow.xaml(.cs)             Theme, Auto-start, startup profile, notifications, hotkey overview
    └── TrayIcon.cs                              System tray icon and dynamic context menu
```

## UI Behavior Reference

This section documents non-obvious UI interactions that affect how features should be implemented and described. Computer/display logic takes precedence over UI concerns — resolve conflicts there first.

### Main Window — Profile List

- **Click card body** — selects the profile and populates the Details panel. Does not apply.
- **Click apply button (`←` icon)** — applies the hovered profile immediately. Does not require the profile to be selected first. Icon changes to a checkmark on hover.
- **Double-click unselected card** — selects then applies the profile.
- **Double-click selected card** — opens the profile editor.
- **Applying a profile** — clears the current selection.
- **Refreshing** — clears the current selection, refreshes profiles and themes.
- **Deleting a profile** — clears the current selection.
- Profile cards show an inline hotkey label when one is assigned (in green when enabled).
- Profile name is truncated if too long. Description wraps to a maximum of 3 lines.

### Main Window — Action Buttons

- **Import, Create** — always visible in the toolbar, no selection required.
- **Duplicate** — appear in the toolbar only when a profile is selected.
  - Both **Create** and **Duplicate** open the editor immediately — there is no intermediate step.
- **Edit, Delete** — appear in the Details panel only when a profile is selected.

### Main Window — Details Panel

Visible when a profile is selected. Shows:
- Profile name and description; when a custom icon is set, an 18×18 icon appears inline to the right of the name.
- Per-monitor cards: resolution, refresh rate, rotation, HDR/ACM, DPI, color profile. Disabled monitors shown with an amber badge ("DISABLED MONITOR" or "DISABLED CLONE GROUP" for clone groups). Primary monitor labeled "Primary Display". Clone groups shown with a "Clone Group" indicator.
- **Display:** section: with consistent 16px top spacing shared across all sections.
- **Audio:** section: enabled devices show the device name in secondary text; disabled devices dim "Output: Not Applied" / "Input: Not Applied".
- Scripts section: labeled "Scripts (Disabled):" when the section-level `EnableScripts` toggle is off.
- Hotkey Settings: hotkey combination and Enabled/Disabled status. Status is accent-colored when enabled, default text color when disabled.
- Created and Last Modified timestamps.

### Main Window — Bottom Bar

- Left: status feedback messages (e.g. "Duplicated 'Profile Name'", "Opened data folder").
- Right: **Open Folder** (opens the data folder in Explorer) and **Settings**.

### Profile Editor

- **Load** (Displays) — overwrites all display settings with the current live desktop configuration.
- **Identify** — overlays each physical monitor with its number temporarily.
- **Clone dropdown** — per-monitor control. Select another monitor to create a mirror group. The two monitors merge into a single group panel listing both device names. Resolution and refresh rate are shared across the group.
- **Break Clone** — appears on grouped panels in place of the Clone dropdown. Splits the group back into independent monitors. The source display keeps the merged control's current combo values (resolution, refresh rate, DPI, rotation, color profile, HDR, ACM). The attached display fully restores its pre-clone state: position, source ID, resolution, refresh rate, DPI scaling, rotation, color profile, HDR, ACM, and primary flag. If the attached display was primary before cloning, it takes primary back and the source loses it. Falls back to native resolution and a position to the right of the source if no saved original values exist (profiles saved while in cloned state).
- **Profile name** — `MaxLength="60"` enforced on the TextBox. Names longer than 63 characters cause tray menu truncation issues; the 60-char limit is the enforced UI ceiling with headroom below the known breakpoint.
- **Icon picker** — between the name/hotkey section and Display Settings. Import `.ico` files into the Icons sandbox. Select from previously imported icons via a scrollable toggle grid. Clear removes the icon assignment without deleting the file from the sandbox.
- **Load** (Audio) — overwrites audio settings with current default playback/recording devices.
- The Scripts section has a single **Enable** toggle that controls all scripts for the profile. There is no per-script enable/disable.
- **Clear All Scripts** — danger button in the Scripts section header. Marks all non-deleted scripts for deletion in one click; individual rows can still be restored.
- Hotkeys are disabled system-wide while any profile editor window is open.
- The window opens sized and positioned to match the main window at open time.

### Settings Window

- **Themes dropdown** — populated at runtime from `ThemeHelper.AvailableThemes`. Lists all built-in themes (Light, Dark, Black, System) plus any user themes dropped into `%AppData%\DisplayProfileManager\Themes\`. Selecting a theme applies and persists it immediately. The list updates automatically after a refresh.
- **Start with Windows** — enables auto-start. **Start in system tray when Windows starts** is a sub-option, greyed out and inactive unless Start with Windows is enabled. Unchecking Start with Windows also unchecks the tray sub-option.
- **Auto-Start Method** — Standard (Registry, no admin) or Quick Launch (Task Scheduler, admin required for initial setup only).
- **Startup Profile** — any profile can be selected. Independent of the Default profile badge.
- **Apply startup profile on launch** — checkbox that controls whether the startup profile is actually applied on each launch.
- **Window close behavior** — Minimize to system tray or Exit. Remember my choice suppresses the prompt.
- **Notifications** — Show notifications when profiles are applied.
- **Global Hotkeys** — read-only overview of all configured hotkeys across profiles. Edit hotkeys in the individual profile editor.
- **About** — version number, settings file path, third-party library list, contributor list.

### System Tray

- Left-click or right-click opens the context menu.
- Profile list — all profiles listed. Active profile has a checkmark. Clicking any profile applies it directly.
- The tray icon updates to the active profile's custom icon on each apply, falling back to the default app icon if loading fails. Apply success notification uses `ToolTipIcon.Info`; failure uses `ToolTipIcon.Error`.
- **Open** — opens the main window.
- **Settings** — opens the main window, then the Settings window.
- **Exit** — exits the application.

## Tests

The test project (`DisplayProfileManager.Tests/`) is a separate MSTest v3 project targeting .NET Framework 4.8. It references the main project directly and can be built and run independently.

### Structure

```
DisplayProfileManager.Tests/
├── Helpers/
│   ├── DisplayConfigInfoBuilder.cs
│   └── DisplaySettingBuilder.cs
└── Tests/
    ├── CloneGroupTopologyTests.cs
    ├── DisplayConfigNormalizationTests.cs
    ├── DisplayConfigPathSourceInfoTests.cs
    ├── DisplaySettingTests.cs
    ├── GetLUIDFromStringTests.cs
    ├── HotkeyConfigTests.cs
    ├── ProfileManagerTests.cs
    ├── ProfileTests.cs
    ├── ScriptHelperTests.cs
    └── ScriptTests.cs
```

### Categories

Tests use `[TestCategory("Unit")]` by default. When a bug is discovered, the test documenting it is tagged `[TestCategory("Regression")]` until the bug is fixed, at which point it is converted back to `Unit`.

### Builder Pattern

Test fixtures are constructed via builder classes rather than raw constructors. This keeps test bodies focused on the condition under test and insulates them from model changes.

```csharp
new DisplayConfigInfoBuilder()
    .WithSourceId(0)
    .WithTargetId(1)
    .Build()   // IsEnabled = true by default

new DisplaySettingBuilder()
    .WithSourceId(0)
    .WithCloneGroup("clone-1")
    .WithResolution(1920, 1080)
    .WithFrequency(60)
    .WithPosition(0, 0)
    .WithDpi(100)
    .Build()   // IsEnabled = true by default
```

Always use builders for fixture construction. Direct `new DisplaySetting { ... }` is acceptable only when the test is explicitly about the default constructor behavior.

### Writing Tests

**File placement:** add to the existing file whose subject matches. If a new subject area warrants a new file, add it to `Tests/` and register it in the `.csproj`. Do not create nested folders inside `Tests/`.

**Method naming:** `Subject_Condition_ExpectedResult`. The method name and assertion fail message together must be sufficient to identify the broken invariant without reading the body. If they are not, the name or message needs work — not a comment.

**Method ordering:** group by logical flow. Within a group, simplest/happy-path first, edge cases after, error/invalid cases last.

**Test body:** Arrange / Act / Assert with a blank line between each.

**Scope:** unit tests only — no file I/O, no registry, no P/Invoke, no live hardware. Methods that require hardware are either not tested (noted in a code comment in the source method) or covered by extracting the pure logic into a testable public static helper.

**What to test:** invariants that are non-obvious or have previously been wrong. Do not test the .NET framework or trivial getters.