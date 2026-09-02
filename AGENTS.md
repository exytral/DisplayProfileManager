# Project guide

This file provides project guidance for human contributors and AI assistants working in this codebase.

## Project Overview

Display Profile Manager is a Windows desktop application for managing display profiles (resolution, refresh rate, rotation, DPI, HDR, color profile, wallpaper, audio devices, and scripts), with control through the main window, system tray, global hotkeys, command line, and the desktop context menu. Built with C# (.NET 10) and WPF.

This is a fork by [exytral](https://github.com/exytral) based on [zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager).

## Updating This File

- **No hard wrapping** — keep each paragraph on one line. Fixed-column wrapping adds visual noise for human readers and causes unrelated lines to reflow when a paragraph is edited, making diffs harder to review.
- **Current state only** — describe how the current code works, not how it changed. Do not document former implementations, historical migrations, renames, or release-specific changes here; historical details belong in `CHANGELOG.md`.
- **Implementation is authoritative** — document the behavior and structure that the current source actually implements. Do not preserve an older description merely because it was previously documented.
- **When in doubt, rewrite the affected section from scratch** rather than patching around existing wording.

## Build and Run Commands

```bash
# Build
cmd.exe //c "msbuild DisplayProfileManager.sln /p:Configuration=Debug"
cmd.exe //c "msbuild DisplayProfileManager.sln /p:Configuration=Release"

# Clean and Rebuild
cmd.exe //c "msbuild DisplayProfileManager.sln /t:Rebuild /p:Configuration=Debug"

# Run
cmd.exe //c "start bin\Debug\DisplayProfileManager.exe"

# Build for Specific Platforms (x86, x64, ARM64 supported)
cmd.exe //c "msbuild DisplayProfileManager.sln /p:Configuration=Release /p:Platform=x64"

# Dev Script (auto-discovers Visual Studio via vswhere)
powershell -File dev-build.ps1 [-Configuration Debug|Release] [-Platform x86|x64|ARM64]
```

## Development Guidelines

### Comment Style

- **Short and concise.** Prefer single-line comments; do not use `<summary>` tags, step/phase headers, or multi-line explanatory blocks.
- **Explain rationale or invariants, not names or obvious code.**
- **Verb-oriented where appropriate.**
- **Structural milestone comments** are allowed in long or complex methods when they materially improve scannability.
- **XAML code-behind** may use short comments for UI section boundaries or non-obvious imperative construction.
- **No per-member explanations.** A comment above one method or property should not merely restate its name or signature.
- **Group markers are allowed, but only for declaration groups.** A short Title Case comment can label a related block of members (fields, constants, properties, enums, structs, or a similar declaration block). It does not authorize a one-line heading placed above an individual method, or a small group of methods, merely to name/categorize them. Method-level rationale belongs inside the method at the line or block it explains; a responsibility-oriented region is appropriate only when a method group is genuinely large enough that it materially improves navigation.
- **Do not restate adjacent log or status text.** A comment should add rationale or context, or it should be removed.
- **Third person only.** Do not address a reader as "you", "we", "our", or "us".
- **Say each thing once per file.** Repeated constraints should be consolidated.
- **Logs follow the same concision principle**: short, verb-oriented, developer-facing, and without filler. User-facing status text uses past-tense completion phrasing such as `Profile applied` and `Profiles refreshed`.

Detailed source-code style conventions are maintained separately in `styles/SOURCE.md`.

### Changelog Relationship

`CHANGELOG.md` is the technical historical record for developers and contributors. It may contain implementation details, historical reasoning, API names, error codes, and lifecycle information that do not belong in this current-state guide or in user-facing release notes.

Detailed changelog-writing rules are maintained in `styles/CHANGELOG.md`.

### Logging

```csharp
private static readonly Logger logger = LoggerHelper.GetLogger();
```

`LoggerHelper` derives the calling class name through `StackFrame` reflection.

Log levels are `Trace`, `Debug`, `Info`, `Warn`, `Error`, and `Fatal`.

Logs are written to `%AppData%\DisplayProfileManager\Logs\DisplayProfileManager-{date}.log`.

### Error Handling

- Return boolean success/failure or strongly typed result objects such as `ProfileApplyResult`.
- Use NLog for failures and include useful context such as device names and attempted settings.
- Degrade gracefully where possible: return empty collections instead of crashing when an enumeration fails.
- P/Invoke return codes must be checked; `ERROR_SUCCESS = 0`.

## Architecture

### Core Patterns

- **Singletons**: `ProfileManager`, `SettingsManager`, and `ScriptManager` own global state with thread-safe double-check locking.
- **Async/Await**: File I/O and display apply operations are asynchronous.
- **P/Invoke**: Windows Display Configuration, DPI, and audio APIs are wrapped by dedicated helper classes.
- **MVVM**: ViewModels expose binding-friendly UI state.
- **Logging**: NLog provides structured application logging.

### Key Components

The application has two primary runtime flows: startup and command handling, and profile application. Other components provide persistence, theming, shell integration, and UI infrastructure around those flows.

#### Startup and command flow

- **App** (`App.xaml.cs`) — application entry point and lifecycle coordinator. Owns single-instance enforcement, initialization, command dispatch, startup-profile handling, tray and main-window setup, global-hotkey policy, update checking and notification, and shutdown. CLI parsing, named-pipe transport, and existing-window activation are delegated to focused helpers.
- **CliParser** (`Core/CliParser.cs`) — pure command-line parser. Normalizes flags, resolves exact or unambiguous prefixes, records command options, and reports shell actions without performing application work.
- **IpcServer** (`Core/IpcServer.cs`) — session-scoped named-pipe transport between additional invocations and the running instance. Builds the session-suffixed pipe name, receives one command at a time, and passes completed messages to an application callback; command interpretation remains in `App`.
- **WindowActivationHelper** (`Helpers/WindowActivationHelper.cs`) — stateless Win32 wrapper for locating, restoring, raising, and foregrounding an existing application window.
- **ShellContextMenuHelper** (`Helpers/ShellContextMenuHelper.cs`) — managed registration boundary for the native shell extension. Registers and unregisters `ShellExt.dll` as a per-user COM in-process server under HKCU and refreshes Explorer when an existing registration is removed. This helper does not implement the Explorer menu itself.
- **ShellExt** (`DisplayProfileManager.ShellExt/`) — native C++ COM shell extension loaded by Explorer. Reads only the profile data required for its menu directly from AppData and launches profile application through `--headless` without requiring the main UI to run.
- **UpdateHelper** (`Helpers/UpdateHelper.cs`) — performs the opt-in GitHub release check, parses release versions, applies the seven-day release-age cooldown, and supplies update state to the About panel, status bar, and notification path.

#### Profile application flow

- **ProfileManager** — thread-safe singleton for profile CRUD, loading, migration, and application. `ApplyProfileAsync(Profile, ApplySource)` coordinates the complete profile-application pipeline and carries its source and elapsed apply duration to the profile-applied event.
- **DisplayConfigHelper** — primary Windows Display Configuration API layer. Owns topology, defer/wait, layout, HDR/ACM, color-profile application, and live display identity resolution.
- **DpiHelper** — applies system-wide DPI scaling after display layout and advanced color state are committed, using a live display identity resolved for the current topology.
- **WallpaperHelper** — captures and reapplies Windows desktop wallpaper state, including per-monitor Solid Color, Picture, Slideshow, and Spotlight modes, and correlates current live monitor interfaces for `IDesktopWallpaper` calls.
- **AudioHelper** — owns native playback/recording endpoint enumeration and switches configured defaults through WASAPI/COM integration.
- **ScriptManager** — owns the sandboxed script store, script import and type conversion, and execution of enabled script entries after the display stages and other profile side effects complete.
- **ScriptHelper** (`Helpers/ScriptHelper.cs`) — provides process-launch support for the script types accepted by `ScriptManager`.

### Supporting Components

#### Persistence and application state

- **SettingsManager** — thread-safe singleton for persisted application settings, including themes, startup behavior, default and startup profiles, notifications, integration settings, update checking, and debug flags.
- **ThemeHelper** — registers built-in and user themes, rescans the user theme folder, applies the selected theme, and exposes the live `AvailableThemes` collection to the UI.

#### Display and audio helpers

- **ColorProfileHelper** — enumerates installed ICC/ICM profiles and applies per-display color-profile associations through the Windows color-management API.
- **DisplayGroupHelper** — converts raw display settings into UI display groups, including clone-group aggregation used by the main window and profile editor.
- **DisplayHelper** — provides legacy GDI display enumeration and supported-mode discovery where CCD has no equivalent.
- **NativeMonitorHelper** — provides native Windows monitor enumeration used by the display UI.

#### Shell and Windows integration

- **ApplicationIconHelper** — loads the executable application icon for WPF and tray use.
- **AutoStartHelper** — implements Registry and Task Scheduler auto-start operations and classifies outcomes as `Success`, `Canceled`, and `Failed`.
- **NativeColorDialogHelper** — provides native Windows color-dialog interop used by the profile editor.
- **TrayIcon** — owns native notification-area integration and builds the profile-driven tray menu from current application state.

#### Input, content, and utility helpers

- **AboutHelper** — resolves application version information and supplies the library and contributor metadata rendered by the Settings → About panel.
- **GlobalHotkeyHelper** — owns Windows global-hotkey registration and dispatches profile hotkey callbacks; `App` retains the policy for when hotkeys are registered or disabled.
- **IconHelper** — owns the profile-icon sandbox, path validation, image loading and caching, import handling, and icon enumeration.
- **KeyConverter** — converts between WPF `Key` values and Windows virtual-key codes for profile hotkeys.
- **SharedHelpers** — provides shared file, text, title-bar, natural-sort, pluralization, and UI-opacity utilities.

### File Structure

```text
DisplayProfileManager/
├── App.xaml(.cs)                                  Application entry point and lifecycle coordinator
├── Core/
│   ├── CliParser.cs                               Command-line parsing to CliOptions
│   ├── HotkeyConfig.cs                            Per-profile hotkey definition
│   ├── IpcServer.cs                               Session-scoped named-pipe transport
│   ├── Profile.cs                                 Profile model + DisplaySetting, AudioSetting, HotkeyConfig
│   ├── ProfileManager.cs                          Thread-safe profile CRUD, migration, and application
│   ├── Script.cs                                  Script model with FileName, Arguments, IsEnabled, ToString()
│   ├── ScriptManager.cs                           Thread-safe script storage, import, and execution
│   └── SettingsManager.cs                         Thread-safe settings persistence
├── Helpers/
│   ├── AboutHelper.cs                             Version strings, settings path, Libraries and Contributors metadata
│   ├── ApplicationIconHelper.cs                   Loads the executable application icon for WPF and tray use
│   ├── AudioHelper.cs                             Native Windows WASAPI/COM interface mapping for audio switching
│   ├── AutoStartHelper.cs                         Registry and Task Scheduler auto-start modes
│   ├── ColorProfileHelper.cs                      ICC/ICM profile enumeration and application via mscms.dll
│   ├── DisplayConfigHelper.cs                     Display engine — all SetDisplayConfig logic lives here
│   ├── DisplayGroupHelper.cs                      Groups display settings for UI rendering and clone aggregation
│   ├── DisplayHelper.cs                           Legacy GDI display and supported-mode enumeration
│   ├── DpiHelper.cs                               System-wide DPI scaling via P/Invoke
│   ├── GlobalHotkeyHelper.cs                      RegisterHotKey / UnregisterHotKey management
│   ├── IconHelper.cs                              Profile icon sandbox, loading, caching, and import
│   ├── KeyConverter.cs                            WPF Key ↔ VirtualKey conversion for hotkeys
│   ├── LoggerHelper.cs                            NLog factory with automatic class-name detection
│   ├── NativeColorDialogHelper.cs                 Native Windows color-dialog interop used by the profile editor
│   ├── NativeMonitorHelper.cs                     Native Windows monitor enumeration used by the display UI
│   ├── ScriptHelper.cs                            Process-launch helpers for supported script types
│   ├── SharedHelpers.cs                           File, text, title-bar, natural-sort, pluralization, and UI-opacity utilities
│   ├── ShellContextMenuHelper.cs                  HKCU COM registration for ShellExt.dll
│   ├── ThemeHelper.cs                             Theme registration, switching, and folder scanning
│   ├── UpdateHelper.cs                            GitHub release update check and version comparison
│   ├── WallpaperHelper.cs                         IDesktopWallpaper COM interop and wallpaper capture/apply
│   └── WindowActivationHelper.cs                  Win32 existing-window activation
├── UI/
│   ├── Controls/                                  Reusable WPF controls
│   ├── Converters/                                WPF value converters
│   ├── Themes/                                    Shared styles and built-in color resources
│   ├── ViewModels/                                MVVM view models
│   ├── Windows/                                   Application windows and code-behind
│   └── TrayIcon.cs                                System tray icon and menu
└── DisplayProfileManager.ShellExt/
    ├── ContextMenu.cpp/.h                         IContextMenu + IShellExtInit implementation
    ├── dllmain.cpp                                DLL entry point, COM exports, class factory
    ├── JsonReader.cpp/.h                          Hand-rolled profile/settings reader for Explorer
    ├── resource.h                                 Resource ID definitions
    ├── ShellExt.def                               Exported shell-extension DLL entry points
    ├── ShellExt.rc                                Embedded shell-extension DLL resources
    └── (registration via ShellContextMenuHelper.cs in the C# project)
```

## Dependencies and Platform

### Dependencies

- **.NET 10 (`net10.0-windows`)** — WPF through `UseWPF`; focused native Win32 interop is used where Windows APIs have no managed WPF equivalent.
- **Newtonsoft.Json 13.0.4** — JSON serialization for profiles and settings.
- **NLog 6.2.0** — application logging with daily file rotation.
- **System.Management 10.0.11** — Windows system-management APIs used by the application.
- **MSTest 4.3.3** — test framework metapackage for the unit-test project.
- **PackageReference** — SDK-style project dependency management.

### Load-bearing project settings

- **`AppendTargetFrameworkToOutputPath=false`** plus explicit `OutputPath` values keep output in `bin\<Platform>\<Configuration>\`. ShellExt, `Setup.iss`, and release packaging depend on that layout.
- **`<ApplicationDefinition Include="src\App.xaml" />` plus `<Page Remove="src\App.xaml" />`** is required because `App.xaml` lives under `src\` instead of the project root. Without the explicit item, the WPF SDK treats it as a normal `Page` and does not generate the application entry point.
- **`[assembly: SupportedOSPlatform("windows10.0.17763.0")]` in `AssemblyInfo.cs`** supplies the platform declaration used by CA1416 because the main project sets `GenerateAssemblyInfo=false`. This assembly attribute does not by itself establish the documented product-support floor.
- **MSTest metapackage** already supplies the test framework, adapter, analyzers, and test SDK. Do not add a separate older `Microsoft.NET.Test.Sdk` reference that would introduce a package downgrade.

### Platform Requirements

- **Windows 10 version 1709+** is the documented minimum supported version.
- **Windows 10 version 1709+** provides the Advanced Color APIs used for HDR support.
- **Windows 11 22H2+** is required for ACM on supported displays.
- **Windows 11 24H2+** provides the dedicated HDR and ACM APIs used by `SetHdrState` and `SetWcgState`; earlier supported systems use the legacy Advanced Color path.
- **Privileges** — the application runs as a standard user (`asInvoker`). Administrator rights are required only when configuring Task Scheduler auto-start.
- **Architectures** — AnyCPU is the default project target; x86, x64, and ARM64 builds are supported.

## Display Configuration

The application uses the Windows Display Configuration API (`SetDisplayConfig`) for atomic profile switching.

1. **`ApplyDisplayTopology`** — performs a fresh `QueryDisplayConfig` to get the current live state, then enables/disables displays and sets clone-group topology via `SDC_TOPOLOGY_SUPPLIED` with a null mode array so Windows chooses modes. Clone groups must be set here because once the mode array is used for layout in the next step, clone groups cannot be changed without invalidating mode indices. The call is skipped when live topology already matches the profile. The query uses `QDC_VIRTUAL_MODE_AWARE`: `cloneGroupId` is packed into the source-info mode-index union, and Windows only reads it on paths whose driver advertises `SUPPORT_VIRTUAL_MODE`. On `ERROR_GEN_FAILURE` (31), the same paths are reissued with `SDC_USE_SUPPLIED_DISPLAY_CONFIG` and `SDC_SAVE_TO_DATABASE` replacing `SDC_TOPOLOGY_SUPPLIED`, with `SDC_ALLOW_PATH_ORDER_CHANGES` omitted. This provides a recovery path when the requested topology has no usable entry in Windows' display-configuration database. Best-mode logic fills the null mode array.
2. **`ApplyDisplayConfig`** — captures target availability once, builds a stabilization wait set from enabled displays present in that snapshot, defers that set before the normal layout attempt, and calls `ApplyDisplayLayout` with the full requested configuration. A layout-stage `ERROR_GEN_FAILURE` (31) causes the same wait set to be deferred again and the full layout retried once, after which advanced color state and color profiles are applied.
    - **`DeferDisplayLayoutAsync`** — waits every 250 ms for up to 10 seconds for the supplied displays to become active. `ApplyDisplayConfig` supplies the enabled displays that were present in the availability snapshot; displays excluded by that classification do not enter the wait set. The timeout is a maximum, not a mandatory delay.
    - **`ApplyDisplayLayout`** — issues a fresh `QueryDisplayConfig` because raw IDs from the pre-topology snapshot are stale after topology changes, then applies position, resolution, refresh rate, rotation, and SourceId normalization through `SDC_USE_SUPPLIED_DISPLAY_CONFIG`. The call is skipped when all live layout checks already match. Rotation is skipped when `profile.Rotation == 0` (`Not Applied`).
    - **`ApplyAdvancedColorState`** — queries live display configuration again after topology apply to obtain current `RawTargetId` values. For each enabled HDR-capable display, HDR is changed only when the live state differs. On Windows 11 24H2+, HDR and ACM use separate APIs and HDR forces ACM on while enabled; when HDR is disabled, ACM follows its own configured state. `SetHdrState` uses `DisplayConfigSetHdrState` (type 16) on 24H2+ and falls back to the legacy advanced-color path earlier. `SetAcmState` uses `SetWcgState` (type 17) on 24H2+; before 24H2, ACM is unavailable on HDR-capable displays and uses the legacy advanced-color path only for SDR-only displays.
    - **`ApplyColorProfiles`** — for each enabled display with a non-null `ColorProfile`, builds a transient `DisplaySetting` from live configuration to supply the correct `AdapterLuid` and `SourceId`, then calls `ColorProfileHelper.ApplyColorProfile`.
3. **DPI** — applies `DpiHelper.SetDPIScaling` after layout, HDR, and color are committed. Display identity is resolved fresh at DPI-apply time so the operation does not rely on a stale display identity after topology or display-state changes. When the caller already has a resolved `DisplayConfigInfo`, `SetDPIScaling` uses it directly rather than re-matching by device-name string. The method refuses when no scaling information is available and snaps an in-range unsupported value to the nearest ladder step.
4. **Wallpaper** — applies the profile's `WallpaperSettings` when wallpaper is enabled.
5. **Audio** — applies the configured playback and recording devices only when audio is enabled and the corresponding `ApplyPlaybackDevice` or `ApplyCaptureDevice` flag is enabled.
6. **Scripts** — executes a script only when script execution is enabled and that script's own `IsEnabled` flag is enabled.

`QDC_ALL_PATHS` with `targetAvailable` determines which enabled displays belong in the stabilization wait set; `QDC_ONLY_ACTIVE_PATHS` is used while polling for active paths. A display that is available in `QDC_ALL_PATHS` but temporarily absent from `QDC_ONLY_ACTIVE_PATHS` remains in the supplied defer set and can be picked up by a later poll — temporary absence from the active-path query does not by itself establish physical disconnection. No disconnected-display result state is recorded on `ProfileApplyResult`.

### Critical implementation notes

- **Never use `ChangeDisplaySettingsEx` for topology or resolution changes.** Display topology and layout changes go through `DisplayConfigHelper`; `ApplyDisplayLayout` handles resolution atomically inside `SetDisplayConfig`.
- **SourceId normalization is handled inside `ApplyDisplayTopology` and `ApplyDisplayLayout` via `BuildSourceIdMap`.** Do not normalize saved profile IDs in `ApplyProfileAsync`. Profiles retain their stored SourceId values so logging and clone-group detection continue to use the original data.
- **Source IDs are assigned per clone group per adapter, not per path.** `MutatePathsForTopology` derives the clone group from the profile's SourceId and renumbers `sourceInfo.id` across active paths. Assigning a different source ID to each member of one clone group describes extend rather than clone.
- **Clone groups must be set in `ApplyDisplayTopology` with `SDC_TOPOLOGY_SUPPLIED`.** Once the mode array is used for layout, changing clone groups would invalidate mode indices.
- **HDR and ACM are distinct.** `IsHdrEnabled` and `IsAcmEnabled` represent separate API states. HDR forces ACM on during apply, but ACM remains independently configurable when HDR is off. On pre-24H2 systems, the shared legacy toggle resets to `Off` before applying `Acm` intent so Windows initializes ACM rather than re-engaging HDR.
- **HDR requires a live `RawTargetId`.** Always query live display configuration after topology apply, match by base `TargetId` (lower 16 bits), and use `activeDisplay.RawTargetId` for `DisplayConfigSetDeviceInfo`.
- **Disconnected-display detection must distinguish deep sleep from absence.** `ApplyDisplayConfig` captures `GetPresentTargetIds` once using `QDC_ALL_PATHS`, then excludes enabled profile displays that are absent from that presence snapshot from the stabilization wait.

## Display Recovery

The display recovery settings control what happens after a display stage fails.

- `abortOnApplyFailure` stops the pipeline when topology or layout fails instead of continuing through DPI, wallpaper, audio, and scripts.
- `rollbackAfterApplyFailure` enables rollback after an aborted apply.
- `rollbackToPreviousProfile` selects the recovery target. When enabled and a previous profile exists, DPM reapplies that profile. When no previous profile exists, recovery falls back to the pre-apply display snapshot. When disabled, the snapshot is used directly.
- All three recovery settings default to `true`.
- Both topology and layout failures reach the same recovery gate. A topology failure skips defer and layout, records `DisplayConfigApplied` as false, and still reaches recovery.
- Snapshot recovery captures the pre-apply display state before any mutation and preserves clone topology so the existing display pipeline can restore it.
- A snapshot rollback clears the active-profile marker because the restored state may not correspond to any profile.
- Previous-profile rollback applies the entire profile pipeline, while snapshot rollback restores display state only.
- Rollback reuses `ApplyProfileAsync` with a `_rollingBack` guard so a failed rollback reports the failure instead of recursively entering the abort/recovery path.

A DPI failure is not an overall profile-apply failure. `ProfileApplyResult.Success` reflects the display stages; `DpiChanged` reports the DPI result separately while scripts and the remaining post-display stages can continue.

### Debug Flags

A `debugFlags` object in `Settings.json` exposes controlled failure paths with no UI.

| Flag                    | Forces                                                                         | Otherwise needs                        |
| ----------------------- | ------------------------------------------------------------------------------ | -------------------------------------- |
| `forceTopologyRecovery` | Treats a successful topology apply as `ERROR_GEN_FAILURE` so recovery executes | A wiped display-configuration database |
| `forceApplyFailure`     | Forces the selected display stage to fail — `1` topology, `2` layout, `0` off  | A real apply failure                   |
| `skipSpotlightRepaint`  | Sets Spotlight without scheduling the repaint step                             | Nothing                                |
| `centerIconGrid`        | Centers the profile-editor icon grid                                           | Nothing                                |

Each active flag logs `[debugFlag: name]` where it acts, and enabled flags are warned about during startup so diagnostic settings are visible in logs.

Debug flags are reserved for behavior that depends on specific hardware or failure conditions. A path reachable through ordinary UI use does not belong behind a debug flag.

## Display Identity

The same physical display can be named through four different mechanisms:

| Namespace            | Looks like                | Source                                                    | Stable?                                                   |
| -------------------- | ------------------------- | --------------------------------------------------------- | --------------------------------------------------------- |
| GDI device name      | `\\.\DISPLAY1`            | `EnumDisplayDevices`                                      | **No** — renumbers on topology changes                    |
| CCD adapter + target | LUID + `targetInfo.id`    | `QueryDisplayConfig`                                      | LUID changes across reboots; target ID is stable per port |
| EDID identity        | `MAN` + `A1B2`            | `GetTargetName`, decoded                                  | Stable per panel and follows it between ports             |
| Interface path       | `\\?\DISPLAY#MANA1B2#...` | `EnumDisplayDevices` with `EDD_GET_DEVICE_INTERFACE_NAME` | Stable per physical port                                  |

Rules:

- Profiles store the base target ID, masked to its lower 16 bits.
- Target ID identifies a connector/port, not a physical panel. EDID identity resolves a moved panel when stored target ID and live target disagree.
- Monitor identity does not depend on WMI.
- `IDesktopWallpaper` uses interface paths, so `WallpaperHelper.BuildMonitorMap()` joins current GDI names to current interface paths instead of persisting the mapping.
- Adapter LUIDs are resolved from live configuration on each apply and are not persisted.
- `QDC_ALL_PATHS` can contain inactive alternate routes for the same target. Lookups by target ID must prefer the active path, and callers that clear `Active` must preserve the live route they were using before doing so.

## Profile and Data Model

### Profile Structure

`Profile` top-level properties, in current declaration order:

| Property            | Type                   | Default  | Description                                                                                                                                                    |
| ------------------- | ---------------------- | -------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Id`                | `string` (GUID)        | new GUID | Unique identifier                                                                                                                                              |
| `Name`              | `string`               | `""`     | Display name                                                                                                                                                   |
| `Description`       | `string`               | `""`     | Optional description                                                                                                                                           |
| `Icon`              | `string`               | `null`   | Bare custom icon filename relative to `%AppData%\DisplayProfileManager\Icons\`, or `null` for none                                                             |
| `CreatedDate`       | `DateTime`             | now      | Creation timestamp                                                                                                                                             |
| `LastModifiedDate`  | `DateTime`             | now      | Last save timestamp                                                                                                                                            |
| `SchemaVersion`     | `int`                  | `0`      | Profile schema version. Profiles without this field, or with a malformed value, deserialize to `0` and trigger migration. Current version is `4`.              |
| `DisplaySettings`   | `List<DisplaySetting>` | `[]`     | Per-monitor display configuration                                                                                                                              |
| `EnableWallpaper`   | `bool`                 | `false`  | Whether the profile's stored wallpaper state is applied                                                                                                        |
| `WallpaperSettings` | `WallpaperSettings`    | `null`   | Stored wallpaper state; see `WallpaperHelper.cs` for the mode-specific structures                                                                              |
| `EnableAudio`       | `bool`                 | `false`  | Whether the profile's audio section is applied; `ApplyPlaybackDevice` and `ApplyCaptureDevice` on `AudioSetting` select which configured endpoints are changed |
| `AudioSettings`     | `AudioSetting`         | default  | Playback and recording device configuration                                                                                                                    |
| `EnableScripts`     | `bool`                 | `false`  | Whether the profile's scripts are executed                                                                                                                     |
| `Scripts`           | `List<Script>`         | `[]`     | Script entries with file name, arguments, and per-script enable state                                                                                          |
| `HotkeyConfig`      | `HotkeyConfig`         | default  | Global hotkey assigned to the profile                                                                                                                          |

Each `DisplaySetting` entry, in current declaration order:

**Identity**

| Property                            | Description                                                                            |
| ----------------------------------- | -------------------------------------------------------------------------------------- |
| `DeviceName`, `DeviceString`        | GDI device path and adapter string                                                     |
| `ReadableDeviceName`                | CCD friendly display name, with GDI name fallback for adapters that do not provide one |
| `ManufacturerName`, `ProductCodeID` | EDID-derived panel identity used with `TargetId` when resolving a live display         |
| `AdapterLuid`                       | `[JsonIgnore]` live adapter LUID used for color-profile P/Invoke calls                 |
| `AdapterId`                         | GPU adapter LUID stored as a hexadecimal string                                        |
| `TargetId`                          | Base target ID, stored as the lower 16 bits and used for stable port identity          |
| `SourceId`                          | Adapter source ID; shared by members of a clone group                                  |
| `CloneGroupId`                      | Clone-group identifier; empty string represents extended/independent mode              |
| `IsCloneSource`                     | Marks the source display within an active clone group                                  |
| `PathIndex`                         | Display path enumeration index                                                         |

**State**

| Property    | Description                                       |
| ----------- | ------------------------------------------------- |
| `IsEnabled` | Includes or excludes the display from the profile |
| `IsPrimary` | Primary-display state                             |

**Layout**

| Property                               | Description              |
| -------------------------------------- | ------------------------ |
| `DisplayPositionX`, `DisplayPositionY` | Virtual-desktop position |

**Configuration**

| Property                         | Description                                                                              |
| -------------------------------- | ---------------------------------------------------------------------------------------- |
| `Width`, `Height`                | Desired resolution                                                                       |
| `Frequency`                      | Desired refresh rate                                                                     |
| `Rotation`                       | Screen orientation; `0` means `Not Applied`, `1` = 0°, `2` = 90°, `3` = 180°, `4` = 270° |
| `DpiScaling`                     | Desired Windows DPI scaling                                                              |
| `IsHdrSupported`, `IsHdrEnabled` | HDR capability and desired HDR state                                                     |
| `IsAcmEnabled`                   | Desired ACM state; HDR forces ACM on during apply                                        |
| `ColorProfile`                   | ICC/ICM filename from the system color store, or `null` for `Not Applied`                |

**Clone** — see [Clone settings](https://claude.ai/chat/50f99ece-bfe1-4a0e-89e7-1339054aee4e#clone-settings) for the `[JsonIgnore]` `Original*` fields; those fields sit here in declaration order between Configuration and Native.

**Native**

| Property                      | Description                                                                                                          |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `NativeWidth`, `NativeHeight` | EDID preferred-timing resolution used when restoring clone members; `0` means the value still needs to be backfilled |

**Capabilities**

| Property                | Description                                       |
| ----------------------- | ------------------------------------------------- |
| `AvailableResolutions`  | Supported resolutions, sorted by width descending |
| `AvailableRefreshRates` | Supported refresh rates grouped by resolution     |
| `AvailableDpiScaling`   | Supported DPI scaling values                      |

`AudioSetting` properties: `DefaultPlaybackDeviceId`, `DefaultCaptureDeviceId`, `PlaybackDeviceName`, `CaptureDeviceName`, `ApplyPlaybackDevice`, `ApplyCaptureDevice`. The `ApplyPlaybackDevice` and `ApplyCaptureDevice` flags select which configured endpoint is changed during apply independently of whether the endpoint is currently available.

### Data Storage

| Path                                             | Contents                              |
| ------------------------------------------------ | ------------------------------------- |
| `%AppData%\DisplayProfileManager\Icons\*.ico`    | User icons                            |
| `%AppData%\DisplayProfileManager\Logs\*.log`     | NLog daily rotation, 30-day retention |
| `%AppData%\DisplayProfileManager\Profiles\*.dpm` | User profiles in JSON                 |
| `%AppData%\DisplayProfileManager\Scripts\`       | User scripts                          |
| `%AppData%\DisplayProfileManager\Themes\*.xaml`  | User themes                           |
| `%AppData%\DisplayProfileManager\Settings.json`  | Application settings                  |

### Profile Naming

Two naming rules serve different purposes:

- **`GetUniqueProfileName`** resolves an import collision by appending `(1)`, `(2)`, and so on. The result does not imply a relationship to an existing profile.
- **`GetDuplicateProfileName`** appends `- Copy`, then applies the collision rule if needed. Duplicating `Profile` twice gives `Profile - Copy` and `Profile - Copy (1)`.

Both cap at `MaxProfileNameLength` by trimming the base before adding a generated suffix.

The tray notification title has a smaller hard limit than the tray tooltip. Profile names therefore cap at 60 characters, leaving room below the 63-character `NOTIFYICONDATA.szInfoTitle` limit used by apply notifications.

### Clone Groups

Clone groups enable display mirroring with multiple monitors showing identical content.

- **Clone-group IDs** are encoded in `DISPLAYCONFIG_PATH_SOURCE_INFO.modeInfoIdx`: lower 16 bits are the clone group ID (`modeInfoIdx & 0xFFFF`), and upper 16 bits are the source-mode index (`modeInfoIdx >> 16`). `ResetModeAndSetCloneGroup()` is used for Phase 1; Phase 2 assigns the field directly. In `ApplyDisplayLayout`, all members of a clone group share one source-mode entry keyed by normalized `SourceId`. Do not consume a separate mode entry per display.
- `GetCurrentDisplaySettingsAsync()` detects clone groups by `SourceId`, not by `DeviceName + SourceId`.
- **`CreateCloneGroup()`** saves all attached-member pre-clone state before making changes. The primary-transfer block runs after the save loop because it clears `IsPrimary` on attached members.
- **`BreakCloneGroup()`** restores each attached member's complete saved display state. Restored fields include position, SourceId, resolution, refresh rate, DPI scaling, rotation, color profile, HDR, ACM, and primary state. If no saved clone settings exist, it falls back to native resolution and a position to the right of the source.
- **`BreakCloneGroup()`** is order-independent. It partitions members by `IsCloneSource` rather than assuming the first item is the source. After a clone is broken, saved attached-member primary state is restored when appropriate; the source does not unconditionally become primary when another display already owns primary.
- **`BreakCloneGroup()`** retains `IsCloneSource` until the rebuild completes. `GetDisplaySettings()` uses it to route parameters correctly during the rebuild and emits `IsCloneSource = false` for settings whose `CloneGroupId` is empty.
- **`CloneGroupMembers`** is public so `ProfileEditWindow.RebuildDisplayControls()` can capture member device names for sort-order preservation before rebuilding controls.
- **`RebuildDisplayControls()`** captures device order from `_profile.DisplaySettings` before rebuilding. Reading order directly from `_cloneGroupMembers` could move an interleaved independent display.

`GetDisplaySettings()` uses:

```csharp
bool useOwnParams = !originalSetting.IsCloneSource && string.IsNullOrEmpty(originalSetting.CloneGroupId);
```

| Situation                            | `IsCloneSource` | `CloneGroupId` | `useOwnParams` | Result                            |
| ------------------------------------ | --------------- | -------------- | -------------- | --------------------------------- |
| Active clone — source                | `true`          | non-empty      | `false`        | reads shared control values       |
| Active clone — attached              | `false`         | non-empty      | `false`        | reads shared control values       |
| After `BreakCloneGroup()` — source   | `true`          | `""`           | `false`        | retains the merged-control values |
| After `BreakCloneGroup()` — attached | `false`         | `""`           | `true`         | reads restored model values       |
| Independent display                  | `false`         | `""`           | `true`         | reads its own values              |

Fields that respect `useOwnParams` when restored parameters are active are `Width`, `Height`, `Frequency`, `Rotation`, `DpiScaling`, `IsHdrEnabled`, `IsAcmEnabled`, and `ColorProfile`. Identity, enabled state, layout position, HDR capability, native dimensions, and capability collections come from the model directly.

`IsPrimary` is read from the original setting rather than inferred from list position. `IsCloneSource` in output is `originalSetting.IsCloneSource && !string.IsNullOrEmpty(originalSetting.CloneGroupId)`, so independent displays never inherit the clone-source flag.

### Clone settings

The following `DisplaySetting` fields are `[JsonIgnore]` and therefore exist only during the editor session:

| Property                                 | Description                                                                                           |
| ---------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `OriginalSettings`                       | Marks an attached member whose restored values should be read from the model after a clone is broken. |
| `OriginalPositionX`, `OriginalPositionY` | Virtual-desktop position before cloning.                                                              |
| `OriginalSourceId`                       | Adapter SourceId before cloning.                                                                      |
| `OriginalIsPrimary`                      | Primary state before cloning.                                                                         |
| `OriginalWidth`, `OriginalHeight`        | Resolution before cloning.                                                                            |
| `OriginalFrequency`                      | Refresh rate before cloning.                                                                          |
| `OriginalRotation`                       | Rotation before cloning.                                                                              |
| `OriginalDpiScaling`                     | DPI scaling before cloning.                                                                           |
| `OriginalIsHdrEnabled`                   | HDR state before cloning.                                                                             |
| `OriginalIsAcmEnabled`                   | ACM state before cloning.                                                                             |
| `OriginalColorProfile`                   | Color profile filename before cloning.                                                                |

These values are copied through `GetDisplaySettings()` so they survive the control rebuild immediately following `CreateCloneGroup()`. They are not written to `.dpm` files; profiles saved while cloned therefore use the fallback path when the clone is later broken after reload. Saved profile data retains the ordinary per-display values needed for restoration after save and reopen — only the `Original*` state is editor-session-only.

### Schema Migration

`ProfileManager.LoadProfilesAsync` compares each profile's `SchemaVersion` with `CurrentSchemaVersion` (`4`). Profiles below the current version are passed to `MigrateProfileAsync`. `SchemaVersion` defaults to `0` in `Profile.cs`, so profiles without the field are migrated automatically on load, and `LastModifiedDate` is preserved during the migration save.

**Version 3 → 4**

- The default-profile association moves from the retired per-profile default flag into `Settings.json` as `defaultProfileId`; migration reads the legacy flag from raw JSON.
- Multiple legacy defaults resolve first-wins and log a warning.
- `ManufacturerName` and `ProductCodeID` are backfilled from live display configuration by `TargetId`.
- Displays unavailable during migration are skipped and their EDID identity can be repaired later by deferred hardware self-healing.

**Version 2 → 3**

- `ColorProfile` is backfilled from the current OS display association by `TargetId`.
- Displays unavailable during migration are skipped.

**Version 1 → 2**

- `Icon` defaults to `null`; no data backfill is required.

**Version 0 → 1**

- `NativeWidth` and `NativeHeight` are backfilled from live display configuration.
- `ReadableDeviceName` is updated from live CCD data.
- Displays unavailable during migration are skipped and their native dimensions can be repaired later by deferred hardware self-healing.

#### Deferred Hardware Self-Healing

Migration is a one-time format upgrade. Native dimensions and EDID identity can remain incomplete when a display is unavailable during migration and are repaired when a later profile apply sees the same `TargetId` live:

- `NativeWidth == 0` / `NativeHeight == 0` — native (EDID preferred-timing) dimensions.
- Empty `ManufacturerName` / `ProductCodeID` — EDID panel identity.

When `ApplyProfileAsync` applies a profile whose live display map includes one of these `TargetId`s, `ProfileManager` backfills the incomplete hardware information on the applied profile, then backfills other loaded profiles that reference the same `TargetId`, saving only profiles that actually changed. The check is `TargetId`-centric because more than one profile can reference the same physical monitor/port. It runs only as part of an actual profile apply (and the existing boot-time migration pass), not on ordinary profile, tray, or editor reloads. No persistent pending-repair queue is used.

`ColorProfile == null` means "not applied" and is never treated as a repairable sentinel by this pass.

This is separate from schema migration: migration still runs once per profile to bump `SchemaVersion`, even when a particular display cannot be backfilled at that time.

#### When a bump is required

| Change                                            | Migration logic?                      | Bump?              |
| ------------------------------------------------- | ------------------------------------- | ------------------ |
| Add a persisted field with a default              | No — absent JSON uses the initializer | **Yes, by policy** |
| Add a `[JsonIgnore]` field                        | No — never written to disk            | No                 |
| Reorder properties                                | No — JSON is name-keyed               | No                 |
| Backfill a computed value from hardware or the OS | Yes                                   | Yes                |
| Change a field's type                             | Yes                                   | Yes                |
| Move persisted data out of the profile            | Yes                                   | Yes                |

The first row is project policy rather than a technical requirement: the migration block can be a no-op that exists only to assign a schema version to the on-disk format change.

Migration blocks are described newest-first here but execute in ascending order. A new migration block is appended at the end of the migration code.

`Settings.json` has no schema version. New settings fields with defaults do not require migration.

### JSON Serialization Notes

- Profile and settings properties use `[JsonProperty("name")]` for explicit JSON field names.
- New persisted properties require sensible defaults so older `.dpm` files can deserialize safely.
- `SchemaVersion` → `0`; profiles without the field, or with a malformed value, recover to `0` and trigger migration.
- `CloneGroupId` → `""`; empty means extended/independent mode.
- `NativeWidth` / `NativeHeight` → `0`; migration and the apply-time deferred hardware self-healing pass backfill them when the display is available.
- `ManufacturerName` / `ProductCodeID` → `""`; migration and the apply-time deferred hardware self-healing pass backfill them for display identity matching.
- `ColorProfile` → `null`; `null` means `Not Applied` and is never auto-filled by migration or self-healing.
- `ProfileManager.DeserializeProfile` tolerates malformed optional members (including retired legacy string-form `Scripts` entries, which are dropped rather than converted) by removing the offending token before `ToObject<Profile>` so the rest of the profile still loads; `displaySettings` and `id` are not recovered and remain strict.
- `[JsonIgnore]` clone-state fields are not persisted and exist only during live clone editing.

## CLI

All flags accept any number of leading dashes or none at all — `--profile`, `-profile`, and `profile` are equivalent. The argument string is lowercased and leading dashes are stripped before matching.

`--tray`, `--exit`, `--shell`, `--unshell`, and `--dev` are matched exactly. All other flags use prefix matching; an unambiguous prefix resolves to the full flag name.

| Flag                        | Behavior                                                                                                                                                                                                                              |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `--profile` "name/ID"       | Apply profile by name or ID. No argument = reapply current active profile.                                                                                                                                                            |
| `--headless` "name/ID"      | Apply profile and exit without UI. No argument = reapply current active profile headlessly. Exit code 0 on success, 1 on apply failure or profile not found.                                                                          |
| `--theme` "name"            | Apply named theme. No name = refresh current theme.                                                                                                                                                                                   |
| `--refresh`/`--reload`/`-r` | Rescan profiles and themes folder, reapply current theme. Does not re-apply the active display profile.                                                                                                                               |
| `--tray`                    | Start minimized to tray. Exact match only.                                                                                                                                                                                            |
| `--exit`                    | Sends an exit command to the running instance. Exits with status 0 when the command is delivered and 2 when no running instance is available. Exact match only.                                                                       |
| `--shell`                   | Register `ShellExt.dll` as a per-user COM shell extension and enable the setting. Exits with status `0` when newly registered, `2` when already registered, and `1` on failure. Exact match only.                                     |
| `--unshell`                 | Unregister the per-user COM shell extension and disable the setting. Refreshes Explorer when an existing registration was removed. Exits with status `0` when removed, `2` when not registered, and `1` on failure. Exact match only. |
| `--dev`                     | Run as an independent development instance, bypassing existing-instance command forwarding and single-instance enforcement. Exact match only.                                                                                         |

**Argument matching:** profile names and theme names are case-insensitive. Flag names use unambiguous prefix matching except where noted above.

**Command ordering:** the table above lists flags in typical usage order, not resolution precedence. `--shell` and `--unshell` are handled first and terminate further command processing. `--exit` is handled next when neither shell action is present and terminates further command processing. `--refresh`, `--theme`, `--profile`, and `--headless` can be combined; `--refresh` and `--theme` are queued in command-line order, while `--profile` and `--headless` are resolved after them regardless of their position. Only one profile target is retained, with the last supplied `--profile` or `--headless` target replacing any earlier one. `--dev` and `--tray` are startup modifiers and can be combined with the other flags.

**IPC:** Commands that can target a running instance are sent through `DPM_IpcPipe.{sessionId}` first. `IpcServer` owns the session-specific pipe transport; `App` interprets the command messages. `--profile` and `--headless` forward profile application to a running instance when available, otherwise apply locally; `--headless` exits without creating a window or tray icon. `--theme "Theme"` applies the named theme through the running instance when available, otherwise applies it locally. `--theme` with no name and `--refresh` require a running instance and do nothing when none is available. `--exit` requires a running instance; when none is available, the invoking process exits with status `2`.

**DPM Shortcut Builder** (`DPMShortcutBuilder.pyw`) — standalone Python tool included with the release. Creates game/app launch shortcuts that switch a display profile before launch and restore a selected profile on exit. Pre-start applications can run after profile switching but before the target launches, with a configurable delay up to 10 seconds. Shortcuts are sandboxed to `%AppData%\DisplayProfileManager\Shortcuts\<name>\`; each shortcut gets its own folder containing the generated `.ps1`, `.lnk`, and `.vbs`. Launcher integration guides cover Steam, Epic Games, GOG Galaxy, Heroic, Playnite, and Generic/Desktop shortcuts.

## Themes and Visual State

### Theme System

Themes use two layers:

1. **`Base.xaml`** — shared control styles, including TextBox, ComboBox, ScrollBar, ComboBoxItem, CheckBox, and RadioButton.
2. **Color/brush files** (`Light.xaml`, `Dark.xaml`, `Black.xaml`, and user themes) — define theme-specific colors and brushes. Base color keys such as `BackgroundColor`, `SurfaceColor`, `BorderColor`, `HoverColor`, and `AccentColor` are defined here and used to derive shared brushes.

**Built-in themes:** Light, Dark, Black. `System` is reserved and follows the Windows theme.

**User themes:** Drop a `.xaml` color file into `%AppData%\DisplayProfileManager\Themes\`. It appears after the next refresh. A user file may shadow a built-in theme name; deleting the user file restores the packaged theme. `System` is protected.

**`ThemeHelper`:**

- `AvailableThemes` exposes the live set of built-in and user themes.
- `RefreshThemes()` rescans the folder, re-registers user themes, and fires `ThemeChanged`.
- `ImportThemeAsync()` validates required brush keys before copying the file into the themes folder, then applies and persists the theme. Files dropped directly into the folder bypass import validation.
- `InitializeTheme()` falls back to System when the saved theme is unavailable and persists that fallback.

**DPM Theme Builder** (`DPMThemeBuilder.pyw`) — standalone Python tool that generates `.xaml` themes from the [tinted-theming/schemes](https://github.com/tinted-theming/schemes) database. A Windows directory-change watcher monitors the application's themes folder and signals the application via `--theme <name>` when an `.xaml` theme changes; saves outside that folder trigger `--refresh`.

### Theme Ownership

Themes are `ResourceDictionary` files merged over `Base.xaml`.

Windows accent handling is not theme-owned: packaged themes derive their accent from `SystemColors.AccentColorKey`, while custom themes retain their configured accent. Accent changes are detected through `WM_DWMCOLORIZATIONCOLORCHANGED`, and packaged theme dictionaries are rebuilt so cached brushes do not retain an old accent.

### Interaction and Opacity

- Controls that are merely inactive remain configurable and use inactive visual treatment — dim means _"will not apply"_, not _"cannot interact."_ This lets profiles be prepared before an inactive mode becomes active.
- Controls representing an invalid or impossible operation in the current state may be non-interactable.
- `UiOpacity.Blocked` and `UiOpacity.Inactive` are shared C# application-state semantics.
- Repeated coherent local presentation values may use local named constants rather than shared application-state abstractions.
- XAML resource indirection should not be introduced solely to mirror C# opacity constants.
- Lower local opacity may establish an information hierarchy within a view; such a hierarchy remains local to the visual responsibility.

### Theme Changes

- Add a built-in theme by creating a color/brush `.xaml` file in `UI/Themes/` and registering it in `ThemeHelper`.
- User themes require no code changes; place them in the themes folder and call `RefreshThemes`.
- Never hardcode theme names or theme lists in UI code. Bind to `ThemeHelper.AvailableThemes`.

## Update Behavior

### Update Check

Update checking is disabled by default through `checkForUpdates` in `Settings.json`.

When enabled:

- **Startup** checks the latest GitHub release tag and compares it with the running informational version.
- A release is advertised only after its seven-day settling window.
- A missing or invalid `published_at` value is treated as old enough to advertise.
- Newer releases are shown in the status bar and About panel.
- A newer release also triggers a tray notification containing a release-page link; update notifications retain that link when activated.
- Enabling update checking while the application is already running performs an immediate fresh check rather than waiting for the next startup.
- An explicit re-check does not reuse the previous startup result, so the newer-release notification can appear again.
- There is no standalone user-facing manual "Check for Updates" command.
- DPM never downloads, writes, installs, or executes an update; it only checks release metadata.
- Update requests use a 10-second HTTP timeout. Network, rate-limit, DNS, and other operational failures are caught and logged at Debug without being surfaced to the user.

## UI Behavior Reference

### Main Window — Profile List

- **Click card body** — selects the profile and populates the Details panel. It does not apply the profile.
- **Click Apply** — applies the hovered profile immediately without requiring it to be selected first.
- **Double-click unselected card** — selects and applies the profile.
- **Double-click selected card** — opens the profile editor.
- **Apply preserves selection** — in-window and external apply handlers capture the selected profile ID before `RefreshProfilesList()` and restore it afterward because replacing `ItemsSource` fires `SelectionChanged` synchronously.
- **Refresh** — clears the current selection, reloads profiles, refreshes themes, and reapplies the current theme. It does not reselect a profile.
- **Delete** — clears the current selection after the profile is removed.
- External applies report source and elapsed duration in the status bar; in-window applies use their own timed message while `_isApplying` prevents competing updates. Successful applies also produce a tray notification with the apply source and elapsed duration when notifications are enabled.
- Profile cards show an inline hotkey label when a hotkey is assigned.
- Profile names are truncated as needed and descriptions wrap to three lines.

### Main Window — Action Buttons

- **Toolbar order** — Duplicate → Import → Create. Duplicate opens the selected profile in the editor with its copied settings; Import handles supported profile, theme, and icon files; Create opens a new profile editor immediately.
- **Details actions** — Edit and Delete are visible in the Details panel only when a profile is selected.

### Main Window — Details Panel

When a profile is selected, the panel shows:

- profile name and description, with a custom icon when assigned.
- per-monitor resolution, refresh rate, rotation, HDR/ACM, color profile, and DPI.
- disabled-monitor and disabled-clone-group badges.
- the primary display marker and clone-group indicators with member names.
- **Display → Wallpaper → Audio → Scripts** sections, with enabled/disabled state reflected in their secondary text.
- saved playback/capture endpoints, marked `(Unavailable)` when the configured endpoint is no longer enumerated. Profiles without a configured endpoint ID do not trigger endpoint enumeration.
- `Scripts (Disabled)` when `EnableScripts` is off; otherwise, stored script file names are shown, with `(Not Found)` appended when a script file is missing.
- hotkey combination and enabled/disabled state.
- created and last-modified timestamps.

### Main Window — Bottom Bar

- Left: status feedback such as `Applied 'Profile Name'` and `Opened data folder`.
- Right: **Open Folder** and **Settings**.

### Profile Editor

- The editor opens sized and positioned to match the main window at open time.
- **Profile and hotkey fields** occupy the left column, with the profile name and hotkey on separate rows. The profile name has the Default checkbox beside its label. The hotkey row has its enable checkbox beside the label and a right-aligned Clear button. Hotkey controls remain inactive until a key combination is assigned; assigning a key automatically enables the hotkey, which can then be disabled explicitly. Global hotkeys are disabled while the profile editor is open.
- **Description** occupies the full right column alongside the Profile and hotkey fields and spans both rows.
- **Icon picker** appears below the profile and hotkey area. It imports `.ico` files into the icon sandbox and immediately selects the imported icon in the editor; the profile assignment is committed only when the profile is saved. Clearing an assignment does not delete the underlying file.
- **Displays** — shows per-monitor resolution, refresh rate, rotation, HDR/ACM, DPI, and color profile controls. **Load** replaces the stored display settings with the current live configuration; new profiles are initialized from the current live desktop when being created, while existing profiles retain their stored display settings until explicitly loaded. **Identify** temporarily overlays each physical monitor with its number. The Clone action selects a display to mirror, with the initiating display becoming the clone source and the selected display becoming the mirror; grouped displays share resolution and refresh controls. Break Clone splits a group back into independent displays while restoring the saved attached-member state.
- **Wallpaper** — includes the section enable toggle and **Load** action, followed by the wallpaper preview and mode-specific controls. Solid Color and Picture provide a color picker; Picture also provides fitment. Slideshow provides fitment, interval, shuffle/order, and source-folder controls. Spotlight provides only a preview.
- **Audio** — includes the section enable toggle and playback and recording device dropdown menus. The dropdown menus enumerate current devices when opened and preserve saved endpoints that are no longer available.
- **Scripts** — includes a section-level Enable toggle and per-script rows containing the script checkbox, file name, arguments, and delete button. The section can be enabled only when at least one non-deleted script is individually enabled, and both the section toggle and the individual script's `IsEnabled` flag must be enabled for that script to execute. Missing script files are shown with `(Not Found)`. **Clear All Scripts** marks all non-deleted scripts for removal while allowing individual rows to be restored.

### Settings Window

- **Themes** — the theme dropdown uses `ThemeHelper.AvailableThemes` and changes the active theme when selected. Theme availability is refreshed when the Settings window opens, when the theme system changes, and when the dropdown is opened. Import and delete actions are available on the theme row.
- **Start with Windows** — controls whether DPM starts with Windows and contains the Start in system tray option.
- **Auto-start method** — selects Registry or Task Scheduler auto-start.
- **App Startup** — contains the startup update-check option and startup-profile options.
- **Closing the Application** — controls whether closing the application minimizes it to the system tray or exits the application, with an option to remember the choice.
- **Notifications** — controls profile-apply notifications.
- **Integration** — controls the desktop context-menu integration.
- **Display Recovery** — controls display-configuration failure recovery.
- **Global Hotkeys** — shows configured profile hotkeys.
- **About** — shows version information, settings path, dependencies, and contributors.

#### Auto-Start Implementation

- Registry mode requires no administrator privileges.
- Task Scheduler mode requires administrator approval during setup.
- When launching Task Scheduler setup through UAC, use `UseShellExecute = true` with `Verb = "runas"`. `Verb = "runas"` is ignored when `UseShellExecute = false`.
- Auto-start operations distinguish `Success`, `Canceled`, and `Failed` outcomes; canceled elevated operations restore prior state and show a warning, while other failures remain errors.

### System Tray

- **Left-click or double left-click** opens the main window.
- **Right-click** opens the tray menu.
- All profiles appear in the menu; the active profile has a checkmark.
- Selecting a profile applies it directly.
- The tray icon follows the active profile's custom icon when available and falls back to the default application icon when loading fails.
- Successful applies produce a notification with the apply source and elapsed duration when notifications are enabled; failed applies use the error notification icon.
- **Open** opens the main window.
- **Settings** opens the main window and then Settings.
- **Exit** exits the application.
- Update notifications retain their release-page link when activated.

### Buttons and Borders

`SecondaryButtonStyle` defines `BorderBrush` and `BorderThickness`, while accent, danger, and success buttons do not. Saturated buttons carry their own visual edge; subtle buttons reuse the shared `BorderColor` used by cards and containers.

`ButtonForegroundBrush` is computed from the Windows accent color. `ThemeHelper.ApplyAccentForeground` uses Rec. 709 luminance to choose black or white text so accent buttons remain readable across the full accent-color range.

### Scrolling

**Pixel scrolling:** `VirtualizingStackPanel.ScrollUnit="Pixel"` provides pixel scrolling while preserving virtualization. Do not use `ScrollViewer.CanContentScroll="False"` for this purpose because it discards virtualization.

**Overlay scrolling:** `OverlayScrollViewerStyle` re-templates the `ScrollViewer` so the bar overlays content, while the implicit `ScrollBar` style controls its appearance. ComboBox dropdown menus use the same overlay scrollbar treatment through the `PrimaryComboBoxStyle` template. The two styles remain independent.

`OverlayScrollViewerStyle` is keyed rather than implicit because an implicit `ScrollViewer` style would affect the internal scroll viewers used by `ComboBox` and `ListBox` templates.

#### Using it on a new surface

1. Add `Style="{StaticResource OverlayScrollViewerStyle}"` to the `ScrollViewer`.
2. Do not reserve layout width for the bar. Use content padding instead and keep the right content margin at zero so the bar reaches the container edge.
3. Leave the shared bar margin at `0,2,-4,2`.

A `ScrollViewer` inside a `ListBox` needs special handling because the default `ListBox` template wraps its scroll viewer in a padded border.

### Icon Picker Fitment

The icon grid is sized to divide the default editor width exactly:

```text
900   window width
-32   editor scroller margin
 -4   icon picker padding
=864  usable width
864 / (42 tile + 6 margin) = 18 columns
```

The grid fills the available width exactly at the default editor size. The `centerIconGrid` debug flag centers the grid when additional horizontal space is available.

`MaxDropDownHeight` is pinned on `PrimaryComboBoxStyle` so the dropdown height is an item-count multiple instead of relying on the WPF default fraction of the screen.

### WPF UI Guidelines

- Extract reusable WPF controls as standalone `UserControl` files under `UI/Controls/`, with separate `.xaml` and `.xaml.cs`.
- Use converters under `UI/Converters/` for data-binding transformations.
- Keep shared control styles in `UI/Themes/Base.xaml` and theme-specific colors/brushes in the individual theme files.
- Never hardcode theme names or lists in XAML/code-behind; bind to `ThemeHelper.AvailableThemes`.
- **CheckBox / RadioButton alignment** — `Base.xaml` supplies implicit templates that honor `VerticalContentAlignment`. Do not use negative padding to compensate for the default WPF `BulletDecorator` behavior, and do not add keyed CheckBox/RadioButton styles to individual windows.
- **Repeated list geometry** — virtualized or repeated custom geometry should use `SnapsToDevicePixels="True"` and/or inherit `UseLayoutRounding="True"` at the repeated-item or window-root level rather than adding those properties mechanically to unrelated static elements.
- **Shared UI-opacity constants** should be used in C# when an opacity value represents a recurring application-state semantic such as blocked or inactive; control-template, presentation-hierarchy, effect-specific, and one-off visual opacities should remain local to their visual layer.
- **A repeated local presentation value** may use a local named constant when it represents one coherent visual semantic within that responsibility; it does not need to become a shared application-state abstraction merely because it recurs.

## ShellExt

`DisplayProfileManager.ShellExt` is a separate native C++ DLL loaded by Explorer as an in-process COM shell extension. It does not depend on the managed application process and does not require the application to be running.

**Build and output:** `ShellExt.dll` is written to `bin\<Platform>\<Configuration>\` rather than an SDK target-framework subdirectory. The native project retains the explicit output-path configuration consumed by `Setup.iss` and release packaging.

**Registration:** the DLL is not self-registering. `ShellContextMenuHelper` registers the COM class per-user under HKCU and records the DLL path. `--shell` registers and enables the setting; `--unshell` removes the registration and refreshes Explorer when an existing registration is removed. Explorer can therefore load the extension while DPM is not running.

**Explorer boundary:** `DllGetClassObject` exposes the `DpmContextMenu` class factory and `DllCanUnloadNow` reports DLL lifetime state. `IShellExtInit::Initialize` accepts only the desktop namespace, so the submenu does not appear in ordinary Explorer folder views. Command execution launches DPM with `--headless` rather than applying profiles inside the DLL.

**Data boundary:** `JsonReader` extracts only `currentProfileId` from `Settings.json` and `id`, `name`, and `icon` from profile files. It is a small flat-string reader rather than a general JSON parser; invalid or unreadable profiles are skipped so one bad file does not prevent other profiles from appearing.

**Menu contract:** `ReadProfiles` sorts profiles by name using `StrCmpLogicalW` natural ordering, and that vector order is also the command-index order used by `QueryContextMenu` and `InvokeCommand`. The cascading `Display Profiles` item consumes no command ID; only profile leaf items map to verb offsets.

## Tests

The test project (`DisplayProfileManager.Tests/`) is a separate MSTest v4 project targeting `net10.0-windows`. It references the main project directly and can be built and run independently.

### Structure

```text
DisplayProfileManager.Tests/
├── Helpers/
│   ├── DisplayConfigInfoBuilder.cs             Builder for DisplayConfigHelper.DisplayConfigInfo test fixtures
│   └── DisplaySettingBuilder.cs                Builder for DisplaySetting test fixtures
└── Tests/
    ├── CliParserTests.cs                       Flag normalization, prefix matching, and refresh/exit spelling
    ├── CloneGroupTopologyTests.cs              Source-mode counts and topology building for clone vs. extended configs
    ├── CloneRestorationTests.cs                BreakCloneGroup restoration of saved attached-member state
    ├── DisplayConfigInfoTests.cs               Default-construction invariants for DisplayConfigInfo
    ├── DisplayConfigNormalizationTests.cs      BuildSourceIdMap contiguous-renumbering behavior
    ├── DisplayConfigPathSourceInfoTests.cs     modeInfoIdx clone-group/source-mode-index bit packing
    ├── DisplayGroupHelperTests.cs              Grouping display settings into UI clone/independent groups
    ├── DisplaySettingTests.cs                  CloneGroupId/IsPartOfCloneGroup and other field defaults
    ├── EdidDecodeTests.cs                      EDID manufacturer ID decoding
    ├── GetLUIDFromStringTests.cs               Hex-string LUID parsing
    ├── HotkeyConfigTests.cs                    HotkeyConfig construction, validity, and equality
    ├── KeyConverterTests.cs                    WPF Key <-> virtual-key code conversion and modifier bit mapping
    ├── NaturalStringComparerTests.cs           Natural sort ordering for embedded numbers and copy suffixes
    ├── ProfileDeserializationRecoveryTests.cs  DeserializeProfile tolerance for malformed optional members
    ├── ProfileHardwareSelfHealingTests.cs      Deferred hardware self-healing detection and backfill
    ├── ProfileManagerTests.cs                  Profile CRUD, lookup, and manager-level behavior (largest suite)
    ├── ProfileTests.cs                         Profile model construction and field defaults
    ├── ScriptTests.cs                          Script model defaults and ToString formatting/quoting
    ├── SettingsManagerTests.cs                 Tolerant settings deserialization and save guards
    ├── UpdateHelperTests.cs                    Release-version parsing and comparison
    └── WallpaperSettingTests.cs                Wallpaper position normalization and Slideshow config scope
```

### Categories

Every test carries `[TestCategory("Unit")]`. It is the only category.

### Builder Pattern

Test fixtures use builder classes rather than raw constructors so test bodies stay focused on the condition under test and model changes are isolated to the builders.

```csharp
new DisplayConfigInfoBuilder()
    .WithDeviceName("\\.\DISPLAY1")
    .WithFriendlyName("Test Monitor")
    .Build()

new DisplaySettingBuilder()
    .WithDeviceName("\\.\DISPLAY1")
    .WithTargetId(1)
    .WithSourceId(0)
    .WithCloneGroup("clone-1")
    .Build()
```

Builders default `IsEnabled` to `true` and seed a 1920×1080 @ 60Hz test display at position (0, 0).

Always use builders for fixture construction. Direct `new DisplaySetting { ... }` is appropriate only when the test is explicitly about the default constructor.

### Writing Tests

- **File placement** — add tests to the existing subject file when one exists. Create a new file only when the subject warrants one; keep test files directly under `Tests/`.
- **Method naming** — use `Subject_Condition_ExpectedResult`.
- **Method ordering** — simplest/happy-path cases first, edge cases next, invalid/error cases last.
- **Test body** — use Arrange / Act / Assert with a blank line between phases.
- **Scope** — unit tests only. Do not use file I/O, registry, P/Invoke, or live display hardware. Pure logic can be extracted into testable helpers when necessary.
- **What to test** — non-obvious invariants and behavior with meaningful regression value. Do not test framework behavior or trivial getters.

The current test suite contains **304 tests**.

## Adding a Contributor

Two files need updating when a contributor is added: `AboutHelper.cs` and `SettingsWindow.xaml.cs`.

**`AboutHelper.Contributors`** uses a constant group for each entry:

```csharp
public const string ExampleName      = "@example";
public const string ExampleUrl       = "https://github.com/example";
public const string ExampleDesc      = "Short description of contribution";
public const string ExampleLinkLabel = "PR #1";
public const string ExampleLinkUrl   = "https://github.com/exytral/DisplayProfileManager/pull/1";
```

Use appropriate link labels and supporting subtext to describe contributor relationships and community contributions.

**`SettingsWindow.xaml.cs` — `LoadContributors`** adds a corresponding array entry:

```csharp
new
{
    Name        = AboutHelper.Contributors.ExampleName,
    Url         = AboutHelper.Contributors.ExampleUrl,
    LinkLabel   = AboutHelper.Contributors.ExampleLinkLabel,
    LinkUrl     = AboutHelper.Contributors.ExampleLinkUrl,
    Description = AboutHelper.Contributors.ExampleDesc,
    SubText     = (string)null
},
```

Contributor ordering starts with `@exytral`, followed by contributors to the fork in contribution order, then the upstream project maintainer `@zac15987`, followed by contributors to the upstream project in contribution order.