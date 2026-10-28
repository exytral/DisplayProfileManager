# Application Architecture

This document is the detailed current-state engineering authority for Display Profile Manager application structure and non-display subsystem architecture. Root `AGENTS.md` defines contributor/agent operating rules and routes work here. Deep display-domain invariants are authoritative in [DISPLAY-CONFIGURATION.md](./DISPLAY-CONFIGURATION.md).

Do not treat historical plans, changelog entries, release records, or cleanup notes as competing current-state architecture.

## Architecture

### Core Patterns

- **Manual composition**: `App` is the composition root. It constructs application services, capability services, platform adapters, stores, and UI controllers once and supplies collaborators explicitly; DPM does not use a DI container or process-global manager singletons for profile/settings/script authority.
- **Application seams**: `ApplicationCommandRouter`, `SingleInstanceCoordinator`, `ProfileEditorService`, `SettingsCoordinator`, and `HotkeyCoordinator` own cross-capability use-case policy. UI code calls these seams or capability services rather than reaching persistence/native APIs directly.
- **Capability ownership**: persisted models, policy, persistence, and native interop stay with their owning capability. There is no generic top-level Models/Services/Repositories/Interfaces/Platform/Infrastructure bucket.
- **Async/Await**: file I/O and profile-apply operations are asynchronous. Wallpaper COM/settling work runs off the WPF UI thread while remaining inside the owning serialized apply request.
- **P/Invoke/COM**: Windows-specific APIs are isolated in capability-local adapters/interop such as Displays/Interop, Audio/Interop, Wallpapers/Interop, Hotkeys/Interop, Integration, and UI/Interop.
- **MVVM + code-behind**: ViewModels expose binding-friendly state where useful; focused WPF code-behind remains deliberate for interaction-heavy windows and controls.
- **Logging**: NLog provides structured application logging.

### Composition and Application Flow

- **App** (`DisplayProfileManager/App.xaml.cs`) — WPF entry point and composition root. It wires settings, profiles, display/wallpaper/audio/script services, integration adapters, theme/hotkey/tray/UI coordinators, startup behavior, and shutdown.
- **ApplicationCommandRouter** (`DisplayProfileManager/Application/ApplicationCommandRouter.cs`) — interprets CLI/IPC commands and routes application actions.
- **EngineeringOverrides** (`DisplayProfileManager/Application/EngineeringOverrides.cs`) — immutable process-scoped engineering fault-injection snapshot resolved once by `App` from `DPM_ENGINEERING_FORCE_APPLY_FAILURE` and `DPM_ENGINEERING_FORCE_TOPOLOGY_RECOVERY`; malformed values fail inactive with warnings, and downstream services never read these variables directly.
- **SingleInstanceCoordinator** (`DisplayProfileManager/Application/SingleInstanceCoordinator.cs`) — owns normal/development single-instance authority and secondary-instance redirection policy.
- **NamedPipeIpcTransport** (`DisplayProfileManager/Integration/NamedPipeIpcTransport.cs`) — session-scoped command transport; command semantics remain in the application layer.
- **ProfileEditorService** (`DisplayProfileManager/Application/ProfileEditorService.cs`) — cross-capability editor-session orchestration used by the profile editor.
- **SettingsCoordinator** (`DisplayProfileManager/Application/SettingsCoordinator.cs`) — reconciles settings mutations that require OS/integration work.
- **ShellCommandCoordinator** (`DisplayProfileManager/Application/ShellCommandCoordinator.cs`) — coordinates shell registration settings and exit-code semantics through `IShellExtensionRegistrar`.
- **GitHubUpdateChecker** (`DisplayProfileManager/Integration/GitHubUpdateChecker.cs`) — performs the opt-in release metadata check and settling-window policy.
- **ShellExtensionRegistrar** (`DisplayProfileManager/Integration/ShellExtensionRegistrar.cs`) — owns per-user ShellExt COM registration/removal and Explorer refresh behavior.
- **WindowActivator** (`DisplayProfileManager/Integration/WindowActivator.cs`) — Win32 existing-window activation.
- **AutoStartService** (`DisplayProfileManager/Integration/AutoStartService.cs`) — Registry/Task Scheduler auto-start operations.

### Profile and Capability Ownership

- **ProfileService** — in-memory profile catalog and CRUD/import/default-profile authority. Persistence is delegated to `IProfileStore`; profile apply is delegated to `ProfileApplyService`.
- **ProfileStore / ProfileSerializer / ProfileMigrationService** — own profile file I/O, schema serialization/recovery, and ordered semantic migration respectively.
- **ProfileApplyService** — serialized multi-capability apply/rollback/finalization orchestration. `ProfileApplyQueue` is the FIFO apply authority.
- **ProfileCaptureService** — captures the live desktop into profile state.
- **ProfileHardwareReconciler** — resolves deferred hardware identity/native-resolution backfill without turning live topology identifiers into persisted identity.
- **DisplayApplyAdapter** — application-facing display apply boundary. `DisplayConfigService` owns CCD topology/layout/query mechanics; `AdvancedColorService`, `ColorProfileAdapter`, `DpiScalingAdapter`, `GdiDisplayAdapter`, and `MonitorBoundsProvider` own their focused platform responsibilities.
- **WallpaperService** — application-facing wallpaper capture/apply boundary. `WallpaperCapture`, `WallpaperApply`, `WallpaperTransitionPlanner`, `WallpaperMonitorMapper`, `SpotlightProvider`, and `WindowsWallpaperPlatform` separate capture, transition policy, monitor identity, Spotlight provider state, and Windows interop.
- **AudioDeviceService** — enumerates/resolves Core Audio endpoints and applies configured playback/recording defaults through capability-local COM interop.
- **ScriptLibrary / ScriptRunner** — separate managed script storage/import from execution.
- **SettingsService / SettingsStore** — separate in-memory settings authority/events from `Settings.json` persistence.
- **HotkeyCoordinator / GlobalHotkeyRegistrar** — separate application hotkey policy/lifecycle from Win32 registration.
- **ThemeService / ThemeStore / SystemThemeProvider** — separate WPF live theme state, user-theme persistence, and Windows theme/accent observation.
- **ProfileIconLibrary / ProfileIconProvider** — separate profile-icon sandbox/import from WPF image loading/caching.

### UI Ownership

- `UI/Windows` contains the main, profile editor, settings, monitor-identify, and confirmation windows.
- `UI/Controls` contains reusable controls including `DisplaySettingControl` and `HotkeyEditorControl`.
- `UI/Tray/TrayIcon` owns native notification-area hosting; `TrayMenuController` owns profile/update menu coordination.
- `UI/Displays/DisplayGroupBuilder` builds display-group projections for presentation.
- `UI/Interop` contains UI-specific native boundaries such as `NativeColorDialog` and `WindowSnapLayout`.
- `UI/Icons`, `UI/Themes`, `UI/Presentation`, `UI/ViewModels`, and `UI/Converters` retain presentation-specific providers, formatting, state, and bindings.

### Settled 2.3 capabilities

**Start Menu profile shortcuts** — `ProfileShortcutPlanner`, `StartMenuProfileShortcutManager`, and `StartMenuProfileShortcutStore` own managed profile shortcuts under Integration. `manageStartMenuProfileShortcuts` controls the feature. Shortcut arguments use stable profile IDs so profile renames do not break them, and Integration owns lifecycle synchronization and removal.

**Quick Switch** — the profile switcher is an opaque WPF overlay with StaticFinal reveal only: pre-show parking, exact final placement/readback, then opacity release. The current implementation has no Acrylic, Windows App SDK runtime dependency, or entrance-motion path. Serialized `quickSwitchAcrylic` remains compatibility-only state with no UI/runtime effect. New or missing settings default to active-monitor targeting by Mouse position; Primary and Window focus remain selectable alternatives, and failure of the selected active source falls only to Primary with no Mouse/Window cross-fallback. Every route shares the selected-monitor DPI, raw work-area, auto-hide AppBar, Explorer-taskbar, and 12-DIP inset resolver. Global-hotkey ownership remains with the existing hotkey coordinator/registration seams.

**Saved layout preview** — Main Details uses `SavedLayoutPreviewControl` to project stored `Profile.DisplaySettings` geometry as a read-only saved-intent layout. Generated saved-layout thumbnails are presentation-only; preview/thumbnails do not query or mutate live display state and do not predict the exact topology Windows/CCD will ultimately realize during apply.

**Smooth scrolling** — `UI/Presentation/SmoothScrollBehavior` provides reusable restrained WPF wheel scrolling on intended surfaces, preserves nested routing, and uses immediate behavior when Windows client-area animation is disabled.

### File Structure

```text
DisplayProfileManager/
  App.xaml, App.xaml.cs              WPF entry point and composition root
  Application/                       Cross-capability use-case coordination
  Profiles/                          Profile models, persistence, capture and apply
  Displays/                          CCD topology, layout, identity, color and interop
  Wallpapers/                        Capture, apply, transitions and Windows interop
  Audio/                             Core Audio service and interop
  Scripts/                           Script library and execution
  Settings/                          Settings models, service and store
  Hotkeys/                           Hotkey policy and registration
  Integration/                       Auto-start, updates, IPC, shell and activation
  Common/                            Cross-cutting utilities
  UI/                                WPF views, controls, tray, themes and presentation
  Properties/AssemblyInfo.cs
  DisplayProfileManager.csproj

DisplayProfileManager.Tests/
  Application/, Audio/, Common/, Displays/, Helpers/
  Hotkeys/, Integration/, Profiles/, Scripts/, Settings/, UI/, Wallpapers/

DisplayProfileManager.ShellExt/           Native Explorer COM extension
DisplayProfileManager.ShellExt.Tests/     Native JsonReader test executable
DPMBuilder/                               Separate Python builder tools
```
## Dependencies and Platform

### Dependencies

- **.NET 10 (`net10.0-windows`)** — WPF through `UseWPF`; focused native Win32 interop is used where Windows APIs have no managed WPF equivalent.
- **Newtonsoft.Json 13.0.4** — JSON serialization for profiles and settings.
- **NLog 6.2.1** — application logging with daily file rotation.
- **System.Management 10.0.12** — Windows system-management APIs used by the application.
- **MSTest 4.5.1** — test framework metapackage for the unit-test project.
- **PackageReference** — SDK-style project dependency management.

### Load-bearing project settings

- **`AppendTargetFrameworkToOutputPath=false`** plus explicit `OutputPath` values keep output in `bin\<Platform>\<Configuration>\`. ShellExt, `Setup.iss`, and release packaging depend on that layout.
- **`[assembly: SupportedOSPlatform("windows10.0.17763.0")]` in `AssemblyInfo.cs`** supplies the platform declaration used by CA1416 because the main project sets `GenerateAssemblyInfo=false`. This assembly attribute does not by itself establish the documented product-support floor.
- **MSTest metapackage** already supplies the test framework, adapter, analyzers, and test SDK. Do not add a separate older `Microsoft.NET.Test.Sdk` reference that would introduce a package downgrade.

### Platform Requirements

- **Base Windows support** — follow Microsoft's current .NET 10 supported-Windows matrix. Windows 10 support is limited to LTSC/Enterprise releases; installer success on another Windows build does not establish product/runtime support.
- **Windows 10 version 1709+** provides the legacy Advanced Color APIs used for HDR support. This is an API availability floor, not a broader base-OS support claim.
- **Windows 11 22H2+** provides Auto Color Management for eligible SDR displays through the legacy Advanced Color provider path.
- **Windows 11 24H2+** provides the dedicated HDR and WCG CCD setters used by `SetHdrState` and `SetWcgState`. The application models the active destination as SDR, WCG, or HDR; earlier supported systems retain the legacy generic Advanced Color/ACM provider path.
- **Privileges** — the application runs as a standard user (`asInvoker`). Administrator rights are required only when configuring Task Scheduler auto-start.
- **Architectures** — AnyCPU is the default project target; x86, x64, and ARM64 builds are supported.


## Profile and Data Model

Display-specific clone semantics and deferred hardware self-healing are intentionally documented only in [DISPLAY-CONFIGURATION.md](./DISPLAY-CONFIGURATION.md).

### Profile Structure

`Profile` top-level properties, in current declaration order:

| Property            | Type                   | Default  | Description                                                                                                                       |
| ------------------- | ---------------------- | -------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `Id`                | `string` (GUID)        | new GUID | Unique identifier                                                                                                                 |
| `Name`              | `string`               | `""`     | Display name                                                                                                                      |
| `Description`       | `string`               | `""`     | Optional description                                                                                                              |
| `Icon`              | `string`               | `null`   | Bare custom icon filename relative to `%AppData%\DisplayProfileManager\Icons\`, or `null` for none                                |
| `CreatedDate`       | `DateTime`             | now      | Creation timestamp                                                                                                                |
| `LastModifiedDate`  | `DateTime`             | now      | Last save timestamp                                                                                                               |
| `SchemaVersion`     | `int`                  | `0`      | Profile schema version. Missing or malformed values recover to `0` and trigger migration. Current version is `6`.                 |
| `DisplaySettings`   | `List<DisplaySetting>` | `[]`     | Per-monitor display configuration                                                                                                 |
| `WallpaperSettings` | `WallpaperSettings`    | `null`   | Stored wallpaper state; its nested `Enabled` value controls whether the wallpaper stage applies                                   |
| `AudioSettings`     | `AudioSetting`         | default  | Playback/recording configuration; nested `Enabled` gates the audio stage and per-endpoint flags select which defaults are changed |
| `ScriptSettings`    | `ScriptSettings`       | default  | Nested script enablement plus the stored `List<Script>` entries                                                                   |
| `HotkeyConfig`      | `HotkeyConfig`         | default  | Global hotkey assigned to the profile                                                                                             |

Each `DisplaySetting` entry, in current declaration order:

**Identity**

| Property                            | Description                                                                            |
| ----------------------------------- | -------------------------------------------------------------------------------------- |
| `DeviceName`, `DeviceString`        | GDI device path and adapter string                                                     |
| `ReadableDeviceName`                | CCD friendly display name, with GDI name fallback for adapters that do not provide one |
| `ManufacturerName`, `ProductCodeID` | EDID-derived panel identity used with `TargetId` when resolving a live display         |
| `AdapterLuid`                       | `[JsonIgnore]` live adapter LUID used by adapter-qualified native calls                |
| `TargetId`                          | Captured lower-16-bit connector/address hint; current target is resolved before apply   |
| `CloneGroupId`                      | Durable clone relationship; empty string represents extended/independent mode            |
| `IsCloneSource`                     | Marks the source display within an active clone group                                  |

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

| Property                       | Description                                                                                                                             |
| ------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------- |
| `Width`, `Height`              | Desired resolution                                                                                                                      |
| `Frequency`                    | Desired refresh rate                                                                                                                    |
| `Rotation`                     | Screen orientation; `0` means `Not Applied`, `1` = 0°, `2` = 90°, `3` = 180°, `4` = 270°                                                |
| `DpiScaling`                   | Desired Windows DPI scaling                                                                                                             |
| `IsHdrSupported`               | `[JsonIgnore]` live HDR capability from the resolved CCD target                                                                         |
| `IsWcgSupported`               | `[JsonIgnore]` live WCG capability from the resolved CCD target                                                                         |
| `LastKnownHdrSupported`      | Persisted nullable HDR capability snapshot (`lastKnownHdrSupported`); distinct from live `IsHdrSupported` |
| `LastKnownWcgSupported`      | Persisted nullable WCG capability snapshot (`lastKnownWcgSupported`); distinct from live `IsWcgSupported` |
| `IsHdrEnabled`, `IsWcgEnabled` | Desired Advanced Color state; together they resolve to one SDR/WCG/HDR destination, with HDR taking precedence if both bits are present |
| `ColorProfile`                 | ICC/ICM filename from the system color store, or `null` for `Not Applied`                                                               |

**Clone** — see [Clone settings](./DISPLAY-CONFIGURATION.md#clone-settings) for the `[JsonIgnore]` `Original*` fields; those fields sit here in declaration order between Configuration and Native.

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

`AudioSetting` properties: `Enabled`, `DefaultPlaybackDeviceId`, `DefaultCaptureDeviceId`, `PlaybackDeviceName`, `CaptureDeviceName`, `ApplyPlaybackDevice`, `ApplyCaptureDevice`. The nested `Enabled` flag gates the audio stage. `ApplyPlaybackDevice` and `ApplyCaptureDevice` select which configured endpoint is changed independently of whether that endpoint is currently available. `ScriptSettings` similarly stores the section-level `Enabled` value together with its `Scripts` collection.

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


### Schema Migration

`ProfileService.LoadProfilesAsync` deserializes through `ProfileSerializer` and sends profiles below `ProfileSerializer.CurrentSchemaVersion` (`6`) through `ProfileMigrationService.MigrateProfileAsync`. `SchemaVersion` defaults to `0` in `Profile.cs`, so profiles without the field are migrated automatically on load, and `LastModifiedDate` is preserved during the migration save.

**Version 5 → 6**

- Stored `isAcmEnabled` input is translated at deserialization into `isWcgEnabled`.
- Rows with `isHdrEnabled == true` canonicalize `isWcgEnabled` to `false`.
- Existing wallpaper state is invalidated and replaced with a fresh disabled `WallpaperSettings`.
- Stored `NativeWidth` / `NativeHeight` values are invalidated, then preferred/native dimensions are backfilled from safely resolved `QDC_ALL_PATHS` endpoints, including inactive-but-present targets. Unresolved targets and preferred-mode query failures remain `0x0` for deferred hardware self-healing.

**Version 4 → 5**

- Wallpaper, audio, and script section enablement is consolidated into the corresponding nested settings objects.
- Legacy root `enableWallpaper`, `enableAudio`, `enableScripts`, and `scripts` members are accepted during deserialization for compatibility and removed before materializing the current model.
- When both legacy root values and current nested values are present, the nested values win.
- New saves emit only the normalized nested shape.

**Version 3 → 4**

- The default-profile association moves from the retired per-profile default flag into `Settings.json` as `defaultProfileId`; migration reads the legacy flag from raw JSON.
- Multiple legacy defaults resolve first-wins and log a warning.
- `ManufacturerName` and `ProductCodeID` are backfilled from live display configuration by `TargetId`.
- Displays unavailable during migration are skipped and their EDID identity can be repaired later by deferred hardware self-healing.

**Version 2 → 3**

- `ColorProfile` is backfilled from the current OS display association by `TargetId`; displays unavailable during migration are skipped.

**Version 1 → 2**

- `Icon` defaults to `null`; no data backfill is required.

**Version 0 → 1**

- `ReadableDeviceName` is updated from live CCD data.
- Displays unavailable during migration are skipped.


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
- `ProfileSerializer.DeserializeProfile` tolerates malformed optional members by removing the offending token before `ToObject<Profile>` so the rest of the profile still loads; malformed individual script entries are dropped without discarding valid siblings. Legacy root subsystem-enable/script members are normalized into the current nested shape before materialization; `displaySettings` and `id` remain strict.
- `[JsonIgnore]` clone-state fields are not persisted and exist only during live clone editing.


## CLI

All flags accept any number of leading dashes, a leading slash, or no prefix — `--profile`, `-profile`, `/profile`, and `profile` are equivalent. The argument string is lowercased and leading `-` or `/` characters are stripped before matching.

`--tray`, `--exit`, `--shell`, `--unshell`, `--dev`, `--shell-dev`, and `--source` are matched exactly. All other flags use prefix matching; an unambiguous prefix resolves to the full flag name.

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

**Engineering/integration controls:** `--shell-dev` and `--source` are exact-match controls for development shell integration and explicit apply-source attribution, respectively; they are not ordinary end-user feature switches.

**Argument matching:** profile names and theme names are case-insensitive. Flag names use unambiguous prefix matching except where noted above.

**Command ordering:** the table above lists flags in typical usage order, not resolution precedence. `--shell` and `--unshell` are handled first and terminate further command processing. `--exit` is handled next when neither shell action is present and terminates further command processing. `--refresh`, `--theme`, `--profile`, and `--headless` can be combined; `--refresh` and `--theme` are queued in command-line order, while `--profile` and `--headless` are resolved after them regardless of their position. Only one profile target is retained, with the last supplied `--profile` or `--headless` target replacing any earlier one. `--dev` and `--tray` are startup modifiers and can be combined with the other flags.

**IPC and instance authority:** Normal non-development startup decides single-instance authority through the session mutex before local profile/theme execution. A non-authoritative invocation forwards queued commands through `DPM_IpcPipe.{sessionId}` and never executes them locally merely because the pipe is not ready. Command-bearing secondaries wait up to the bounded IPC readiness timeout; if the authoritative process owns the mutex but its pipe never becomes ready, the secondary exits with status `1`. The authoritative process starts the pipe listener only after settings, profiles, themes, hotkeys, and profile-event wiring are initialized, so successful connection is the command-readiness boundary. `NamedPipeIpcTransport` owns transport; `ApplicationCommandRouter` owns command interpretation. `--theme` with no name and `--refresh` require an already running authoritative instance, while a named theme or profile can execute locally only in the invocation that owns authority. `--exit` with no running instance exits with status `2`.

**DPM Shortcut Builder** (`DPMShortcutBuilder.pyw`) — standalone Python tool included with the release. Creates game/app launch shortcuts that switch a display profile before launch and restore a selected profile on exit. Pre-start applications can run after profile switching but before the target launches, with a configurable delay up to 10 seconds. Generated shortcut files are stored under `%AppData%\DisplayProfileManager\Shortcuts\<name>\`; each shortcut gets its own folder containing the generated `.ps1`, `.lnk`, and `.vbs`. Launcher integration guides cover Steam, Epic Games, GOG Galaxy, Heroic, Playnite, and Generic/Desktop shortcuts.

## Themes and Visual State

### Theme System

Themes use two layers:

1. **`Base.xaml`** — shared control styles, including TextBox, ComboBox, ScrollBar, ComboBoxItem, CheckBox, and RadioButton.
2. **Color/brush files** (`Light.xaml`, `Dark.xaml`, `Black.xaml`, and user themes) — define theme-specific colors and brushes. Base color keys such as `BackgroundColor`, `SurfaceColor`, `BorderColor`, `HoverColor`, and `AccentColor` are defined here and used to derive shared brushes.

**Built-in themes:** Light, Dark, Black. `System` is reserved and follows the Windows theme.

**User themes:** Drop a `.xaml` color file into `%AppData%\DisplayProfileManager\Themes\`. It appears after the next refresh. A user file may shadow a built-in theme name; deleting the user file restores the packaged theme. `System` is protected.

**`ThemeService`:**

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

- Add a built-in theme by creating a color/brush `.xaml` file in `UI/Themes/` and registering it in `ThemeService`.
- User themes require no code changes; place them in the themes folder and call `RefreshThemes`.
- Never hardcode theme names or theme lists in UI code. Bind to `ThemeService.AvailableThemes`.

## Update Behavior

### Update Check

Update checking is disabled by default through `checkForUpdates` in `Settings.json`.

When enabled:

- **Startup** checks the latest GitHub release tag and compares it with the running informational version.
- A release is advertised only after its three-day settling window.
- A missing or invalid `published_at` value is treated as old enough to advertise.
- Newer releases are shown in the status bar and About panel.
- A newer release also triggers a tray notification containing a release-page link; update notifications retain that link when activated.
- Enabling update checking while the application is already running performs an immediate fresh check rather than waiting for the next startup.
- An explicit re-check does not reuse the previous startup result, so the newer-release notification can appear again.
- There is no standalone user-facing manual "Check for Updates" command.
- The application never downloads, writes, installs, or executes an update; it only checks release metadata.
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
- per-monitor resolution, refresh rate, rotation, HDR/WCG, color profile, and DPI.
- disabled-monitor and disabled-clone-group badges.
- the primary display marker and clone-group indicators with member names.
- **Display → Wallpaper → Audio → Scripts** sections, with enabled/disabled state reflected in their secondary text.
- saved playback/capture endpoints, marked `(Unavailable)` when the configured endpoint is no longer enumerated. Profiles without a configured endpoint ID do not trigger endpoint enumeration.
- `Scripts (Disabled)` when `ScriptSettings.Enabled` is off; otherwise, stored script file names are shown, with `(Not Found)` appended when a script file is missing.
- hotkey combination and enabled/disabled state.
- read-only saved-layout preview projected from the profile's stored display geometry as saved intent, not as a guarantee of the Windows-realized topology.
- created and last-modified timestamps.

### Main Window — Bottom Bar

- Left: status feedback such as `Applied 'Profile Name'` and `Opened data folder`.
- Right: **Open Folder** and **Settings**.

### Profile Editor

- The editor opens sized and positioned to match the main window at open time.
- **Profile and hotkey fields** occupy the left column, with the profile name and hotkey on separate rows. The profile name has the Default checkbox beside its label. The hotkey row has its enable checkbox beside the label and a right-aligned Clear button. Hotkey controls remain inactive until a key combination is assigned; assigning a key automatically enables the hotkey, which can then be disabled explicitly. Global hotkeys are disabled while the profile editor is open.
- **Description** occupies the full right column alongside the Profile and hotkey fields and spans both rows.
- **Icon picker** appears below the profile and hotkey area. It imports `.ico` files into the icon sandbox and immediately selects the imported icon in the editor; the profile assignment is committed only when the profile is saved. Clearing an assignment does not delete the underlying file.
- **Displays** — shows per-monitor resolution, refresh rate, rotation, HDR/WCG, DPI, and color profile controls. **Load** replaces the stored display settings with the current live configuration; new profiles are initialized from the current live desktop when being created, while existing profiles retain their stored display settings until explicitly loaded. **Identify** temporarily overlays each physical monitor with its number. The Clone action selects a display to mirror, with the initiating display becoming the clone source and the selected display becoming the mirror; grouped displays share resolution and refresh controls. Break Clone splits a group back into independent displays while restoring the saved attached-member state.
- **Wallpaper** - includes the section enable toggle and **Load** action, followed by the wallpaper preview and mode-specific controls. Solid Color provides a color picker; Picture provides color and fitment. Slideshow provides color, fitment, interval, shuffle/order, and source-folder controls. Spotlight provides only a preview.
- **Audio** — includes the section enable toggle and playback and recording device dropdown menus. The dropdown menus enumerate current devices when opened and preserve saved endpoints that are no longer available.
- **Scripts** — includes a section-level Enable toggle and per-script rows containing the script checkbox, file name, arguments, and delete button. The section can be enabled only when at least one non-deleted script is individually enabled, and both the section toggle and the individual script's `IsEnabled` flag must be enabled for that script to execute. Missing script files are shown with `(Not Found)`. **Clear All Scripts** marks all non-deleted scripts for removal while allowing individual rows to be restored.

### Settings Window

- **Themes** — the theme dropdown uses `ThemeService.AvailableThemes` and changes the active theme when selected. Theme availability is refreshed when the Settings window opens, when the theme system changes, and when the dropdown is opened. Import and delete actions are available on the theme row.
- **Start with Windows** — controls whether Display Profile Manager starts with Windows and contains the Start in system tray option.
- **Auto-start method** — selects Registry or Task Scheduler auto-start.
- **App Startup** — contains the startup update-check option and startup-profile options.
- **Closing the Application** — controls whether closing the application minimizes it to the system tray or exits the application, with an option to remember the choice.
- **Notifications** — controls profile-apply notifications.
- **Integration** — controls the desktop context-menu integration and managed Start Menu profile shortcuts.
- **Quick Switch** — controls the Quick Switch hotkey and monitor policy: active-monitor Mouse position by default, with Primary and Window focus as alternatives.
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

`ButtonForegroundBrush` is computed from the Windows accent color. `ThemeService.ApplyAccentForeground` uses Rec. 709 luminance to choose black or white text so accent buttons remain readable across the full accent-color range.

### Scrolling

**Pixel scrolling:** `VirtualizingStackPanel.ScrollUnit="Pixel"` provides pixel scrolling while preserving virtualization. Do not use `ScrollViewer.CanContentScroll="False"` for this purpose because it discards virtualization.

**Overlay scrolling:** `OverlayScrollViewerStyle` re-templates the `ScrollViewer` so the bar overlays content, while the implicit `ScrollBar` style controls its appearance. ComboBox dropdown menus use the same overlay scrollbar treatment through the `PrimaryComboBoxStyle` template. The two styles remain independent.

`OverlayScrollViewerStyle` is keyed rather than implicit because an implicit `ScrollViewer` style would affect the internal scroll viewers used by `ComboBox` and `ListBox` templates.

**Smooth wheel scrolling:** `SmoothScrollBehavior` is attached only to intended WPF scroll surfaces. It accumulates/rebases wheel input, preserves nested-scroll handoff, and falls back to immediate scrolling when client-area animation is disabled.

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

The grid fills the available width exactly at the default editor size.

`MaxDropDownHeight` is pinned on `PrimaryComboBoxStyle` so the dropdown height is an item-count multiple instead of relying on the WPF default fraction of the screen.

### WPF UI Guidelines

- Extract reusable WPF controls as standalone `UserControl` files under `UI/Controls/`, with separate `.xaml` and `.xaml.cs`.
- Use converters under `UI/Converters/` for data-binding transformations.
- Keep shared control styles in `DisplayProfileManager/UI/Themes/Base.xaml` and theme-specific colors/brushes in the individual theme files.
- Never hardcode theme names or lists in XAML/code-behind; bind to `ThemeService.AvailableThemes`.
- **CheckBox / RadioButton alignment** — `Base.xaml` supplies implicit templates that honor `VerticalContentAlignment`. Do not use negative padding to compensate for the default WPF `BulletDecorator` behavior, and do not add keyed CheckBox/RadioButton styles to individual windows.
- **Repeated list geometry** — virtualized or repeated custom geometry should use `SnapsToDevicePixels="True"` and/or inherit `UseLayoutRounding="True"` at the repeated-item or window-root level rather than adding those properties mechanically to unrelated static elements.
- **Shared UI-opacity constants** should be used in C# when an opacity value represents a recurring application-state semantic such as blocked or inactive; control-template, presentation-hierarchy, effect-specific, and one-off visual opacities should remain local to their visual layer.
- **A repeated local presentation value** may use a local named constant when it represents one coherent visual semantic within that responsibility; it does not need to become a shared application-state abstraction merely because it recurs.

## ShellExt

`DisplayProfileManager.ShellExt` is a separate native C++ DLL loaded by Explorer as an in-process COM shell extension. It does not depend on the managed application process and does not require the application to be running.

**Build and output:** `ShellExt.dll` is written to `bin\<Platform>\<Configuration>\` rather than an SDK target-framework subdirectory. The native project retains the explicit output-path configuration consumed by `Setup.iss` and release packaging.

**Registration:** the DLL is not self-registering. `ShellExtensionRegistrar` registers the COM class per-user under HKCU and records the DLL path. `--shell` registers and enables the setting; `--unshell` removes the registration and refreshes Explorer when an existing registration is removed. Explorer can therefore load the extension while the application is not running.

**Explorer boundary:** `DllGetClassObject` exposes the `DpmContextMenu` class factory and `DllCanUnloadNow` reports DLL lifetime state. `IShellExtInit::Initialize` accepts only the desktop namespace, so the submenu does not appear in ordinary Explorer folder views. Command execution launches the application with `--headless` rather than applying profiles inside the DLL.

**Data boundary:** `JsonReader` validates a strict bounded native JSON subset and extracts only `currentProfileId` from `Settings.json` plus `id`, `name`, and `icon` from profile files. Malformed, duplicate-key, invalid-UTF-8, over-limit, or unreadable profile files are rejected independently so one bad file does not prevent other profiles from appearing.

**Menu contract:** `ReadProfiles` sorts profiles by name using `StrCmpLogicalW` natural ordering, and that vector order is also the command-index order used by `QueryContextMenu` and `InvokeCommand`. The cascading `Display Profiles` item consumes no command ID; only profile leaf items map to verb offsets.


## Tests

Managed MSTest coverage is organized by capability rather than by a second architecture taxonomy. Helpers remain under `DisplayProfileManager.Tests/Helpers/`; test files live in capability folders directly under `DisplayProfileManager.Tests/`.

### Current Structure

Managed MSTest source is organized directly into capability folders: `Application/`, `Audio/`, `Common/`, `Displays/`, `Helpers/`, `Hotkeys/`, `Integration/`, `Profiles/`, `Scripts/`, `Settings/`, `UI/`, and `Wallpapers/` under `DisplayProfileManager.Tests/`. The native `DisplayProfileManager.ShellExt.Tests/` executable validates its separate JsonReader boundary. Keep tests near capability ownership; inspect the current project and test files for the live inventory rather than relying on a frozen numeric total.
### Builder Pattern

Use `DisplayConfigInfoBuilder` and `DisplaySettingBuilder` for ordinary display fixture construction so test bodies stay focused and model changes remain localized. Direct construction is appropriate when constructor/default behavior itself is under test.

### Writing Tests

- Add tests to the existing subject file when one exists; create a new file only when the subject warrants one, in the appropriate capability folder.
- Name methods `Subject_Condition_ExpectedResult`.
- Order simple/happy-path cases first, then edge cases, then invalid/error cases.
- Use Arrange / Act / Assert with a blank line between phases.
- Prefer deterministic, self-contained unit coverage. Controlled filesystem access is acceptable when inherent to an existing seam, but tests should not depend directly on live registry state, P/Invoke, or display hardware.
- Test non-obvious invariants and regression behavior, not framework behavior or trivial getters.

## Contributor Metadata

Contributor metadata is owned by `DisplayProfileManager/Application/AboutInfoProvider.cs` and rendered by `DisplayProfileManager/UI/Windows/SettingsWindow.xaml.cs`. Keep those two surfaces synchronized when contributor entries change.
