# Changelog

All notable changes to this project are documented here.
Technical entries are intended for developers and contributors.
For user-facing release notes, see the [GitHub Releases](https://github.com/exytral/DisplayProfileManager/releases) page.

---

<a id="2.2.0"></a>
## [2.2.0] - 2026-09-02

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.2.0)_

- Adapting [fixes](https://github.com/vivittel/DisplayProfileManager) by [vivittel](https://github.com/vivittel) — unambiguous HDR and advanced color state detection fixes on Windows 11 24H2 and later.

### feat — wallpaper

- **Native wallpaper capture and apply** — `WallpaperHelper` wraps `IDesktopWallpaper` and captures `Solid Color`, `Picture`, `Slideshow`, and `Spotlight` state, including background color, per-monitor picture paths, fitment, slideshow options/source, and Spotlight state. Monitor joins use `EDD_GET_DEVICE_INTERFACE_NAME` and exclude detached adapters; duplicate interface paths resolve first-claim-wins.
- **Wallpaper editing and previews** — Solid Color, Picture, Slideshow, and Spotlight profiles expose mode-appropriate controls and previews. Slideshow sources use `IShellItemArray`/`SIGDN_FILESYSPATH`; missing sources, images, and disconnected monitors are skipped non-destructively. Previews use `BitmapCacheOption.OnLoad`.
- **Wallpaper application preserves stored state** — slideshow sources are set before options, fitment is applied after the image, and final desktop refresh handles fitment-only changes. Background color is captured and applied for all modes so Picture profiles using Fit or Center preserve their letterbox color.
- **Spotlight support** — detection combines the wallpaper path, `BackgroundType`, provider state, and ContentDeliveryManager state. Apply enables the provider before selecting the mode and triggers repaint through `BackgroundType`/`SPI_SETDESKWALLPAPER`; globally disabled background apps abort the apply. Displayed image is resolved from the active ContentDeliveryManager cache rather than the fixed `DesktopSpotlight\Assets\Images` set.

### feat — desktop context menu

- **Desktop profile switcher** — new native `DisplayProfileManager.ShellExt` implements `IShellExtInit`/`IContextMenu`, registers per-user under `HKCU\Software\Classes`, reads `Settings.json` and `*.dpm` at menu-open time, and exposes profiles through the **Display Profiles** submenu using `--headless`. Custom icons are shown for inactive profiles while the active profile uses a menu check marker.
- **Shell integration** — `ShellContextMenuHelper` manages `ShellExt.dll` registration, while `--shell`/`--unshell` provide explicit CLI registration actions. `--unshell` refreshes Explorer when it removes an existing registration so unloaded extension state takes effect immediately. Shell actions return `0` when they change registration, `1` on failure, and `2` when requested registration state is already satisfied. `DesktopContextMenuEnabled` toggles the feature and settings-load handlers are guarded against firing during initialization.

### feat — display engine

- **EDID-based display identity** — `ManufacturerName` and `ProductCodeID` are decoded from `DISPLAYCONFIG_TARGET_DEVICE_NAME`; `ResolveLiveDisplay` prefers captured `TargetId` when EDID matches and otherwise follows the panel to its current connector. Replacement monitors can inherit captured settings when the live identity resolves to the stored display mapping, while EDID-unavailable displays fall back to `TargetId`.
- **Dynamic refresh preservation** — display queries and applies use `QDC_VIRTUAL_REFRESH_RATE_AWARE` and `SDC_VIRTUAL_REFRESH_RATE_AWARE`, with fallback when unsupported. `DisplayConfigInfo.SupportsDrr` records capability only; Windows' separate Dynamic refresh rate setting remains outside the application's control.

### feat — display recovery

- **Configurable apply recovery** — `abortOnApplyFailure` stops the pipeline at display failure and `rollbackAfterApplyFailure` enables recovery; all three recovery settings default to `true`. `rollbackToPreviousProfile` selects between reapplying the previous profile and restoring the pre-apply display snapshot, with snapshot used when no previous profile exists or previous-profile rollback is disabled.
- **Clone-aware rollback** — display snapshots preserve shared source IDs, allowing snapshot rollback to reconstruct cloned topology. Topology submission assigns one source ID per clone group and adapter, preserving the clone relationship during rollback.
- **Unified rollback path** — topology and layout failures reach the same recovery gate, snapshots are captured before mutation when required, and snapshot rollbacks clear the active-profile marker. Previous-profile rollback reuses the normal profile-apply pipeline with `_rollingBack` guard, while snapshot rollback restores display state through the same topology/layout path and therefore receives the same layout recovery behavior.

### feat — profile schema

- **Schema version 4 and EDID identity migration** — `CurrentSchemaVersion` advances from `3` to `4` and migration backfills `ManufacturerName`/`ProductCodeID` from live display configuration.
- **Default profile storage** — `Profile.isDefault` is replaced by `defaultProfileId` in `Settings.json`; migration resolves duplicate legacy defaults first-wins with a warning, and changing the default profile becomes a single settings write.

### feat — scripts

- **Per-script enable state** — each script row now exposes its own checkbox, distinct from the section-wide script toggle; disabled script entries remain stored and are skipped during apply.

### feat — UI

- **Display capability loading** — display modes are grouped from one enumeration instead of re-enumerating per resolution; scaling values are taken from the known Windows ladder; live display configuration is reused where available.
- **New profile initialization** — new profiles prefill a unique `New Profile` name, current display settings, current wallpaper settings, and current audio devices when the profile editor opens, while existing profiles retain stored settings until explicitly recaptured.
- **Audio section enable state** — profile editor exposes a section-level audio toggle; audio is saved as enabled only when that toggle and at least one playback or capture apply option are selected and enabled.
- **Overlay scrollbars** — shared `OverlayScrollViewerStyle` is applied to profile lists, details, editor, and Settings.

### feat — theming

- **Windows 11 neutral dark theme** — Dark uses neutral WinUI-style values; packaged themes derive their accents from `SystemColors.AccentColorKey`, while custom themes retain their configured accents.

### feat — notifications

- **Profile apply notifications** — successful applies from main window, tray, hotkey, CLI, and startup now produce a tray notification when notifications are enabled. Notifications identify the apply source and report the elapsed apply duration, while failure notifications retain the Windows error icon and update notifications retain the Windows information icon.

### feat — settings

- **Optional update notifications** — startup checks the GitHub API only when `checkForUpdates` is enabled; newer releases are shown after a seven-day release-age threshold and failures remain silent at Debug level.
- **Debug flags** — `forceApplyFailure`, `forceTopologyRecovery`, `skipSpotlightRepaint`, and `centerIconGrid` provide controlled diagnostic paths for display, recovery, Spotlight, and icon-grid behavior.

### fix — display engine

- **DPI scaling validation** — unreadable scaling ranges no longer report false success, unsupported in-range values snap to the nearest supported step, and write results are logged.
- **DPI scaling targeting** — scaling is resolved against the live display immediately before application when live display configuration is available, dropping reliance on stale device-name match.
- **Topology recovery for `ERROR_GEN_FAILURE` (31)** — `SDC_TOPOLOGY_SUPPLIED` failures now retry with `SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE`; `SDC_ALLOW_PATH_ORDER_CHANGES` is omitted from retry while `SDC_VIRTUAL_MODE_AWARE` is retained. Recovers display topologies that Windows has not yet committed to the display configuration database, including cases where changing display identity or EDID causes previously unseen topology to be rejected by the normal database-backed path.
- **Availability-based display handling** — `QDC_ALL_PATHS` and `targetAvailable` classify which enabled profile displays belong in the stabilization wait set, while `QDC_ONLY_ACTIVE_PATHS` is used by the wait itself to observe active displays. Displays absent from availability snapshot are excluded from the wait without creating separate disconnected-display result state; displays that remain available but are temporarily absent from active query remain eligible for later polling.
- **Deep-sleep layout recovery** — `ApplyDisplayConfig` captures target availability once, excludes unavailable displays from the stabilization wait set, defers currently available displays before the normal `ApplyDisplayLayout`, and preserves the full requested configuration as the layout payload. Layout-stage `ERROR_GEN_FAILURE` (31) invokes the same defer set again and retries the full layout once, allowing transient post-topology states to settle before the second submission.
- **`VerifyDisplayConfiguration` retired** — the post-failure verifier checked only the coarse live topology state, confirming the expected display enablement and clone-group SourceId sharing. Did not verify requested position, resolution, refresh rate, or other settings, so it could convert a failed layout `SetDisplayConfig` into apparent success. Verifier was removed so failed layout submission remains a failure and follows the normal error and recovery path.
- **Live-path selection** — `ApplyDisplayLayout` now prefers the active path when `QDC_ALL_PATHS` returns inactive alternates, and mutation path records which entry was live before clearing `Active`. Prevents layout, resolution, rotation, and subsequent live-display lookups from targeting an inactive route.

### fix — UI

- **Tray interaction and profile ordering** — left-click on the tray icon now opens the main window. The tray menu is presented directly rather than through a separate profile flyout, and Tray/ShellExt profile menus use the same natural name ordering as the main profile list.
- **Theme and editor refresh** — hotkey/contributor/version-link resources rebuild after theme changes; unavailable slideshow intervals are logged; disabled scripts and unavailable audio are shown distinctly; apply logs count enabled scripts only.
- **Profile list and apply state** — external applies and profile edits preserve selection and external applies report their source and elapsed duration in status text and notifications.
- **Tray menu icon rendering** — inactive profile icons are rendered at the native small-menu size with preserved transparency, avoiding oversized icons and opaque backgrounds in the native popup menu. Tray icon updates now resolve from the active profile when another profile is edited or added.
- **Presentation and sorting** — empty descriptions collapse, accent-button foregrounds derive from the system accent luminance, CLI/shell applies report success or failure, combo-box dropdown height is constrained, and names use `StrCmpLogicalW`.
- **Settings initialization** — persisted control state initialized before the window is shown, while loaded-time work reconciles live runtime state. Auto-start reconciliation immediately updates dependent tray-start and auto-start-mode controls when live system state differs from persisted state, while preserving their stored values.
- **Auto-start state handling** — auto-start operations distinguish `Success`, `Canceled`, and `Failed`; canceled elevated Task Scheduler operations restore prior logical and UI state and show a warning, while other failures remain errors. Persistence failures restore in-memory settings and attempt external rollback, with rollback failure logged explicitly.
- **Disabled display color-profile state** — selecting a color profile on a disabled display no longer restores full opacity; color-profile control remains visually inactive until the display is enabled.
- **Unavailable audio devices** — saved playback or capture device that is no longer present is preserved as an unavailable selection rather than silently falling back to another device, remains saveable with its stored ID and name intact, and is shown as unavailable in the editor and Details panel.
- **UI opacity semantics** — recurring inactive application-state opacity is centralized through `UiOpacity`; inactive startup-profile selector now uses shared inactive value, and disabled-display Details hierarchy uses local named opacity constant.

### fix — profile management

- **Duplicate naming** — copies use ` - Copy` / ` - Copy (n)` naming with 60-character truncation, while import collisions retain numeric naming.
- **Profile deserialization recovery** — malformed optional profile members now fall back individually instead of discarding the entire profile. `scripts` entries that cannot deserialize, including retired legacy string-form entries, are dropped individually; `audioSettings`, `wallpaperSettings`, `hotkeyConfig`, enable flags, and descriptive metadata recover to their property defaults. `displaySettings` and `id` remain strict. Malformed `schemaVersion` now recovers to `0`, allowing the existing migration path to handle the profile instead of treating the schema value as an independent fatal field.
- **Deferred hardware self-healing** — schema migration still runs once and advances profiles even when displays are unavailable. `NativeWidth`/`NativeHeight` values of `0` and empty EDID identity fields are treated as deferred hardware sentinels; when the applied profile later sees the same `TargetId` live, missing values are repaired on the applied profile and on other loaded profiles sharing that target. No persistent pending-repair queue or ordinary profile/reload scanner is used. `ColorProfile == null` remains an explicit `Not Applied` value and is never treated as missing migration data.
- **Legacy script converter retired** — `ScriptListConverter` no longer part of profile deserialization. Legacy string-form script entries are discarded rather than promoted, while current object-form entries continue to deserialize normally.
- **Atomic profile writes** — `.dpm` writes use unique temporary filenames so concurrent or interrupted writes cannot collide with one another.

### fix — auto-start

- **Task registration and detection** — scheduled-task XML values are escaped, and enabled state is read from `/XML` instead of localized `schtasks /FO LIST /V` output.

### fix — single instance

- **Per-session IPC** — `DPM_IpcPipe` is suffixed with `Process.GetCurrentProcess().SessionId`, preventing cross-session command forwarding on shared machines.

### fix — CLI

- **Headless execution** — `--headless` resolves profiles independently and always exits with status `0` or `1` after applying, without falling through into the normal UI startup on failure.
- **Graceful application shutdown** — `--exit` is exact-match terminal command that forwards `CMD:EXIT` to the running instance and exits without starting DPM when no instance is available. Returns status `0` when command is delivered and `2` when no running instance is found.
- **IPC command ordering** — `PROFILE:{profile}` is constructed once after argument parsing so combined `--headless --profile` invocations cannot queue duplicates.
- **Pipe serialization** — `.Task.Unwrap()` ensures each IPC command finishes before the next connection is accepted.
- **CLI settings fallback** — `--theme` skips saving when `LoadSettingsAsync()` fails instead of persisting blank defaults.

### fix — settings

- **Shell integration validation** — registration failures roll back the setting; enabling the context menu requires `ShellExt.dll`.
- **Partial settings recovery** — unreadable settings members fall back individually and are logged rather than discarding the entire configuration.
- **Durable atomic writes** — `Settings.json` uses unique temporary filenames; writes are flushed before replacement, transient replacement failures are retried, failed temporary files are cleaned up, and `SaveSettingsAsync` refuses to overwrite settings until valid settings have been loaded or initialized.

### fix — packaging

- **Portable package completeness** — `runtimes\` assets are included and `*.exp`/`*.lib` shell-extension byproducts are excluded.
- **Installer metadata and architecture** — `AppVerName` is set explicitly and `TargetArch` now respects `release.yml`'s architecture define.
- **Installer updated for .NET 10** — Inno Setup package now consumes SDK-style Release output, including the application assembly, runtime configuration files, dependencies, native shell extension, and `runtimes\` assets; obsolete `.exe.config` and legacy AudioSwitcher files removed from upgraded installations, while debug symbols are not included in the package.
- **Per-user and per-machine installation** — installer supports installing for current user without elevation or for all users with administrative elevation, while retaining a fixed application-owned installation location instead of exposing an arbitrary destination path.
- **.NET 10 Desktop Runtime prerequisite** — installer detects required Windows Desktop Runtime through installed `dotnet` host, downloads architecture-matched Microsoft Desktop Runtime when missing, runs Microsoft installer, and blocks installation until the required runtime is detected.
- **Shell extension upgrade handling** — upgrades use existing shell-extension CLI actions to remove prior registration before replacing `ShellExt.dll` and restore it only when it was registered before the upgrade; `--unshell` refreshes Explorer when it removes an existing registration.
- **Graceful uninstall shutdown** — uninstaller requests supported application versions to exit before unregistering the shell extension and removing installer-managed auto-start state, waits for termination, and force-terminates older versions that predate the shutdown command when necessary before continuing.

### refactor — application startup

- **Startup decomposition** — `App.xaml.cs` reduced by moving window activation to `Helpers/WindowActivationHelper.cs`, IPC to `Core/IpcServer.cs`, and argument parsing to `Core/CliParser.cs`. Hotkey policy remains in `App`.
- **Pure CLI parsing** — `CliParser` returns `CliOptions` and `ShellAction` without terminating the process, separating parsing from application behavior.

### refactor — application

- **WinForms dependency removed** — managed application no longer enables WinForms. System-tray integration uses native notification-area APIs, and former WinForms cursor, monitor-enumeration, color-dialog, and generated-resource call sites use focused Windows interop or `ApplicationIconHelper` equivalents instead. `TrayIcon` owns native notification-area integration, while shell extension remains a separate native component.

### refactor — shared helpers

- **Shared helper extraction** — duplicated atomic-write, title-bar sizing, and pluralization logic moves into `Helpers/SharedHelpers.cs`.

### build — .NET 10

- **Framework migration** — `DisplayProfileManager` and tests move from .NET Framework 4.8 to `net10.0-windows` SDK-style projects; `packages.config` becomes `PackageReference`, with x86/x64/ARM64 configurations and existing output paths preserved.
- **Windows targeting and project fixes** — `AssemblyInfo.cs` is retained with `GenerateAssemblyInfo=false`, Windows support is declared explicitly, `src\App.xaml` is marked as `ApplicationDefinition`, and `App.config` is removed.
- **CI and packaging** — CI moves to `setup-dotnet`/`dotnet restore`; the installer includes `.exe`, `.dll`, `.json`, and `NLog.config`, removes stale `.exe.config` entries, and checks for .NET 10 Desktop Runtime.

### build — dependencies

- **NLog update** — NLog moves from 6.1.4 to 6.2.0.

### test — MSTest v4

- **Test suite — 304 tests** — expanded from the 173-test suite in [2.1.0](#2.1.0) with coverage for CLI parsing, clone restoration, display grouping, key conversion, natural sorting, duplicate naming, profile deserialization recovery, deferred hardware self-healing, settings recovery, update checking, wallpaper state, EDID identity, configurable rollback, live display resolution, and related profile-model behavior.
- **MSTest upgrade** — `MSTest` 3.6.3 moves to 4.3.3 metapackage; existing assertion set requires no compatibility changes.

### misc — repository maintenance

- **Runtime dependency reporting** — About-window dependency versions are read from loaded assemblies instead of duplicated constants.
- **Developer build script** — `dev-build.ps1` gracefully requests shutdown of a running `--dev` instance before building, falls back to forced termination if it does not exit within bounded wait, always attempts to unregister the native shell extension before building, retries a failed unregister once, and re-registers the extension only when it was previously enabled.
- **General refinement** — various code cleanup, bug fixes, UI refinements, and optimizations.

---

<a id="2.1.2"></a>
## [2.1.2] - 2026-06-03

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.1.2)_

### fix — DPM Shortcut Builder

- **Pipe communications removed from generated shortcut script** — `Invoke-DpmApply` previously attempted IPC pipe apply before falling back to `--headless`. Pipe server is receive-only (`PipeDirection.In`); pipe path returned success on message delivery, not on apply completion, meaning the target could launch before display settling finished. `Get-ActiveProfileId` had the same issue — `QUERY_ACTIVE` branch could never receive a response. Both functions now use `--headless` exclusively, which blocks until apply completes and surfaces a real exit code. `$pipeName` removed from generated header.

---

<a id="2.1.1"></a>
## [2.1.1] - 2026-06-02

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.1.1)_

### feat — DPM Shortcut Builder

- **DPM Shortcut Builder** (`DPMShortcutBuilder.pyw`) — new standalone Python tool for creating game/app launch shortcuts. Select target application, assign display profile to switch to before launch, and profile to restore on exit. Pre-start applications (or any script type application supports) can be queued to run after profile switching and before target launches, each with optional kill-on-exit and configurable delay. Shortcuts are sandboxed to `%AppData%\DisplayProfileManager\Shortcuts\<name>\` as a `.ps1` + `.lnk` + `.vbs` set. Launcher integration panel provides ready-to-paste launch options for Steam, Epic Games, GOG Galaxy, Heroic, Playnite, and Generic / Desktop shortcuts. Export copies `.lnk` to any location while keeping sandbox files in place.
- **DPMBuilder folder** — `DPMThemeBuilder/` renamed to `DPMBuilder/`.

### fix — profile editor

- **Display setting changes discarded on save for all independent displays** — `GetDisplaySettings()` incorrectly treated every independent display as though it should read restored values from the model rather than current editor controls, so resolution, refresh rate, rotation, and DPI changes were silently discarded on save. Ambiguity came from deriving `useOwnParams` from `!IsCloneSource && CloneGroupId == ""`, which is true for independent displays as well as the post-`BreakClone()` case it was intended to identify. Fixed by replacing that derivation with an explicit `[JsonIgnore]` `UseRestoredParams` flag on `DisplaySetting`; `BreakClone()` sets it on attached members after restoring their pre-clone state, while other code paths leave it false, so independent displays read from the controls and restored clone members read from the model.

### fix — display

- ~~**Disconnected display detection removed** — pre-topology detection incorrectly identified deep-sleep monitors as disconnected, excluding them from defer wait and causing immediate layout failure. Reverted to original defer behavior (physically disconnected displays time out after 10s as before).~~ Rebuilt in [2.2.0](#2.2.0), where availability and active-path state are distinguished during stabilization rather than treating every absent active path as a physical disconnect.

### fix — headless exit code

- **`--headless` returned exit code 0 on apply failure** — three compounding issues: private `ApplyProfileAsync(string)` wrapper captured `ProfileApplyResult` but discarded it, headless branch called bare `Shutdown()` and therefore returned exit code 0, and the profile-not-found path fell through the same zero-exit branch. Fixed by changing `ApplyProfileAsync` to return `Task<bool>` and passing the result to `Shutdown(applySucceeded ? 0 : 1)` in the headless branch. Non-headless callers ignore return value. Profile-not-found now also exits 1.

### fix — IDD virtual monitor crash

- **`ApplyAdvancedColorState` unhandled Win32 exception on IDD paths** — software virtual monitors expose CCD paths that appear valid but lack backing kernel viewport objects. Calling `SetHdrState` or `SetAcmState` against an uninitialized IDD handle throws `ERROR_GEN_FAILURE` (31) or `ERROR_INVALID_PARAMETER` (87) immediately, unwinding out of `ApplyDisplayConfig` and aborting color-profile application for all remaining physical monitors. Wrapped the per-display HDR/ACM block in a targeted `try-catch`; IDD failures are logged as non-fatal warnings and the pipeline continues.

---

<a id="2.1.0"></a>
## [2.1.0] - 2026-05-29

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.1.0)_

### feat — color profiles

- **`ColorProfile` field on `DisplaySetting`** — nullable `string`; `null` = not applied; any other value = bare ICC/ICM filename from system color store. `[JsonIgnore]` `AdapterLuid` on `DisplaySetting` is populated at apply time from live config and is not stored.
- **`IsAcmEnabled` field on `DisplaySetting`** — bool, default `false`. Independent of `ColorProfile`. ACM forced on at apply time when `IsHdrEnabled` is true, regardless of this flag.
- **`ColorProfileHelper`** (`Helpers/ColorProfileHelper.cs`) — P/Invoke wrapper for `mscms.dll`. `GetSystemColorDirectory` resolves the system color profile directory. `GetInstalledColorProfilesFiltered(hdrOnly)` enumerates installed `.icc`/`.icm` files; when `hdrOnly = true`, restricts to profiles containing an MHC2 tag or a CICP tag with transfer function 16 (PQ) or 18 (HLG). `GetDisplayDefaultColorProfile` reads the current per-display OS association (user scope first, system scope fallback). `ApplyColorProfile` sets default via `ColorProfileSetDisplayDefaultAssociation`, enabling per-user scope if not already active.
- **`ApplyColorProfiles`** in `DisplayConfigHelper` — called inside `ApplyDisplayConfig` after `ApplyAdvancedColorState`. Builds transient `DisplaySetting` from live config to supply the correct `AdapterLuid` and `SourceId` for the P/Invoke call.
- **Color profile combobox** — rightmost column of `DisplaySettingControl` settings row. Dropdown: Not Applied, then installed profiles (HDR-only set when HDR is active, full set otherwise). Profiles no longer installed on system appear as `(not found)` placeholders to preserve stored value.
- **Native resolution marker** — resolution dropdown appends `★` to native EDID entry. Refresh rate dropdown appends `★` to peak rate.

### feat — advanced color state

- **`ApplyAdvancedColorState`** replaces `ApplyHdrSettings` in `DisplayConfigHelper`. Handles HDR and ACM in single pass per display. HDR forces ACM on; ACM is independently toggleable otherwise.
- **`DisplayConfigColorIntent` enum** — `Off`, `Acm`, `Hdr`; used by `SetAdvancedColorState` to route to the correct API path.
- **`SetAdvancedColorState(LUID, uint, DisplayConfigColorIntent)`** — unified toggle using legacy `DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE` path. For `Acm` intent, resets to `Off` first so Windows selects ACM rather than HDR on dual-capable displays.
- **`SetHdrState`** — on Windows 11 24H2+, uses `DisplayConfigSetHdrState` (type 16 in `DisplayConfigDeviceInfoType`); falls back to `SetAdvancedColorState` on earlier builds.
- **`SetAcmState`** — on 24H2+, delegates to `SetWcgState` (type 17); pre-24H2, ACM is not supported on HDR-capable displays (logged as warning) and uses `SetAdvancedColorState` for SDR-only displays.
- **`IsAcmSupported(uint targetId)`** — returns true when running Windows 11 22H2+ and target reports HDR capability via `GetAdvancedColorInfo`.
- **ACM checkbox in `DisplaySettingControl`** — hidden when unsupported (`Visibility.Collapsed`), grayed out and force-checked when HDR is active. HDR state change syncs ACM checkbox.

### feat — script class

- **`Script` model** (`Core/Script.cs`) — `Profile.Scripts` migrated from `List<string>` to `List<Script>`. Each script carries `FileName`, `Arguments`, and `IsEnabled`.
- ~~**`ScriptListConverter`** — custom `JsonConverter` on `Profile.Scripts` handles backward-compatible deserialization: string entries (schema <3) are parsed via `ScriptHelper.ParseScriptString` and promoted to `Script` objects; object entries deserialize normally.~~ *Retired in [2.2.0](#2.2.0).*
- **`ScriptListEntry`** — strongly typed UI class replacing `dynamic`/`ExpandoObject` in `ProfileEditWindow`. Carries `FilePath`, `FileName`, `Arguments`, `IsEnabled`, `IsDeleted`. Eliminates `Items.Refresh()` calls for property changes.

### feat — UI

- **`DisplaySettingControl` settings row** — single row replacing previous two-row layout.
- **Rotation "Not Applied"** — rotation dropdown now has "Not Applied" at index 0 (`Rotation = 0`). `ApplyDisplayLayout` skips `paths[pIdx].targetInfo.rotation` when `profile.Rotation == 0`; check `paths[pIdx].targetInfo.rotation != (uint)profile.Rotation` also guards `profile.Rotation != 0`.
- **Monitor name font size** — increased from 14 to 18 in `DisplaySettingControl` header.
- **Icon scroll bubbling** — `IconScrollViewer_PreviewMouseWheel` bubbles wheel events from icon grid inner `ScrollViewer` to outer container when inner scroll is exhausted at either end.
- **Script/hotkey controls state** — `UpdateScriptControlsState` grays out `EnableScripts` when no scripts are imported; `UpdateHotkeyEnableState` grays out hotkey enable checkbox when no key is assigned and clears `IsChecked`. Both controls become usable again when their associated content is available.

### fix — clone groups

- **Primary transfer on clone** — when the attached display owns the primary flag, transfer to the source at clone time. `GetDisplaySettings()` now reads `IsPrimary` from the data model rather than the checkbox so the value survives `RebuildDisplayControls` cycle.
- **Break Clone restores attached display fully** — original SourceId, position, primary flag, resolution, refresh rate, rotation, DPI scaling, HDR state, ACM state, and color profile are saved at clone time via `[JsonIgnore]` fields (`OriginalSourceId`, `OriginalIsPrimary`, `OriginalPositionX/Y`, `OriginalWidth/Height`, `OriginalFrequency`, `OriginalRotation`, `OriginalDpiScaling`, `OriginalIsHdrEnabled`, `OriginalIsAcmEnabled`, `OriginalColorProfile`) and restored on break. Falls back to the native resolution and a position to the right of the source if no saved values are available.
- **Break Clone guarantees a primary** — source display is assigned primary on break; checkbox is synchronized before rebuild fires.
- **Clone params carried through rebuild** — `GetDisplaySettings()` now copies all `[JsonIgnore]` clone parameter fields so they survive control rebuild.

### fix — profile manager

- **`ApplyDisplayConfig` awaited** — `ApplyProfileAsync` now awaits `DisplayConfigHelper.ApplyDisplayConfig`, which is async. Previously, return value was captured synchronously.
- **`EnsureProfilesFolderExists` at I/O sites** — called at start of `LoadProfilesAsync`, `SaveProfileAsync`, and `ImportProfileAsync` in addition to constructor, matching pattern used by `ScriptManager` and `ThemeHelper`.
- **HDR/ACM detection fix** — `GetDisplayConfigs` now sets `IsHdrEnabled = isEnabled && isHdrEncoding` and `IsAcmEnabled = isEnabled && !isHdrEncoding`, where `isHdrEncoding` means `colorEncoding == YCbCr444`. Previously, `IsHdrEnabled` was set to `true` whenever `AdvancedColorEnabled` was set without checking encoding type, conflating HDR and ACM. *24H2+ HDR and advanced-color-state detection was fixed in [2.2.0](#2.2.0) to distinguish separate Windows HDR and ACM states.*

### fix — IPC

- **Named pipe listener** (`App.xaml.cs`) — `NamedPipeServerStream` is now created once before the listen loop and reset via `Disconnect()` after each connection rather than disposed and recreated per iteration. Recreating per iteration caused `ERROR_PIPE_BUSY` between connections, filling log files with error spam.

### fix — auto-start

- **`IsAutoStartEnabledTaskScheduler`** — switched from `/FO CSV` to `/FO LIST /V`; result now checks `output.Contains("Enabled") && !output.Contains("Disabled")`. Previously, a task that was registered but denied elevation (never actually created) was reported as enabled because the task name appeared in the CSV output regardless of state.

### refactor — script manager

- **`ExecuteScript(Script)`** replaces string-based overload.
- **`AddScript`, `RemoveScript`, `SortScripts` removed** — callers operate directly on `List<Script>`.
- **`FormatCommand` removed** — display formatting delegated to `Script.ToString()`.

### refactor — profile manager

- **`GetAllProfilesWithHotkeys` → `GetProfilesWithActiveHotkeys`** — returns only profiles with an enabled hotkey assigned. `GetProfilesWithHotkeys` retained for profiles with any hotkey assigned (enabled or not).
- **`ValidateCloneGroups` retired** — no longer required due to display-engine rewrite.
- **Dead methods removed** — `ExportProfileAsync`, `GetProfileByHotkey`, `HasHotkeyConflict`, `FindConflictingProfile`, `ClearHotkeyAsync`, `GetProfilesFilePath`, `GetProfilesFolder`, `EnsureAppDataFolderExists`.

### refactor — profile

- **Schema version 3** — `CurrentSchemaVersion` bumped from 2 to 3. Migration backfills `ColorProfile` per display from `ColorProfileHelper.GetDisplayDefaultColorProfile` using live config. `List<string>` → `List<Script>` promotion is handled at deserialization via `ScriptListConverter` rather than as a separate migration step.
- **`EnableAudio` field** — bool, default `true`. ~~Unused and not currently checked in `ApplyProfileAsync`;~~ `ApplyPlaybackDevice`/`ApplyCaptureDevice` on `AudioSetting` remain the operative flags. Resolved in [2.2.0](#2.2.0), field now used.
- **`ProfileCollection` class removed** — unused.
- **`AddDisplaySetting` method removed** — unused.

### refactor — P/Invoke

- **All P/Invoke struct, enum, and constant names** were converted from `SCREAMING_SNAKE_CASE` to `PascalCase`.

### test — unit tests

- **Test suite — 173 tests** — reduced from the 212-test suite in [2.0.2](#2.0.2) through removal of obsolete clone-validation and dead-method coverage, elimination of duplicate profile-manager tests, and cleanup of one unused `GetHashCode` test, while adding coverage for new `Script` model and color-profile/ACM defaults.

### misc

- **`DisplayGroupHelper.cs` wired up** — `ProfileEditWindow` nested helper is removed and `DisplayGroupHelper.GroupDisplaysForUI` is called directly from both `ProfileEditWindow.LoadDisplaySettings` and `MainWindow.UpdateProfileDetails`. Details panel now renders clone groups correctly with "Clone Group" indicator and multi-member device-name stacking.
- **Refresh button reliability** — button is disabled before `LoadProfilesAsync` begins and re-enabled in `finally`, preventing duplicate default-profile generation on rapid clicks.
- **General refinement** — comment and code cleanup, consistency improvements, UI refinements, and related minor tooling changes. Version string in Settings → About is now a hyperlink to the releases page.

> **Note:** Profile editor does not commit display setting changes on save. Resolution, refresh rate, rotation, and DPI scaling changes made in the editor are discarded; the original profile values are saved instead. Resolved in [2.1.1](#2.1.1).

---

<a id="2.0.5"></a>
## [2.0.5] - 2026-05-24

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.5)_

### fix — CLI

- **`--headless` with no argument falls back to saved profile** — `isHeadless` now participates in startup profile resolution block alongside `isProfile`. Previously, `-h` with no argument and no running instance would skip `GetCurrentProfileId()` and fall through to the full UI initialization instead of resolving and applying the saved profile headlessly.

---

<a id="2.0.4"></a>
## [2.0.4] - 2026-05-23

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.4)_

### feat — custom profile icons

- **`icon` field on `Profile`** — bare filename relative to `%AppData%\DisplayProfileManager\Icons\`, or `null` for no custom icon.
- **Tray icon reflects active profile** — `TrayIcon` resolves `profile.Icon` via `IconHelper` and replaces `_notifyIcon.Icon` on each apply. Falls back to the default app icon, cached at initialization, if loading fails.
- **Profile list inline icon** — `16×16` icon appears right of the profile name, left of **Default**/**Active** badges. Image collapses when no icon is assigned. *Missing-file handling is resolved in [2.1.0](#2.1.0).*
- **Details panel inline icon** — `18×18` icon appears right of the profile name when a custom icon is set.
- **Profile editor icon picker** — full-width row between name/hotkey and Display settings. Label, `32×32` preview, filename, and Import action filtered to `.ico`; imported files are copied into icon sandbox via `IconHelper.ImportIconAsync`. Scrollable `41×41` toggle grid shows all icons in sandbox; clicking selects, clicking again deselects. Clear removes icon assignment without deleting the file.
- **`IconHelper`** — new helper owning Icons sandbox: path resolution and traversal rejection, icon loading and caching, importing, and enumeration. Cache entries auto-evict when source file is modified externally.
- **`ProfileIconConverter`** — converter mapping bare icon filenames to `ImageSource` for list-card binding.
- **`ProfileViewModel.Icon`** — pass-through to `Profile.Icon` required for list-card binding.

### feat — profile schema

- **Schema version 2** — `CurrentSchemaVersion` bumped from 1 to 2. Version 1 → 2 is no-op because `Icon` defaults to `null`.

### fix — UI alignment

- **Checkbox and RadioButton template** — `Base.xaml` now provides implicit `CheckBox` and `RadioButton` styles with custom `Grid`-based templates. WPF's default `BulletDecorator` template ignores `VerticalContentAlignment`; new template binds bullet's `VerticalAlignment` directly to `VerticalContentAlignment`, fixing alignment across application windows.

### fix — details panel

- **Consistent section spacing** — all section headers in Details panel share the same `16px` combined margin.
- **Display section renamed** — "Display Settings:" → "Display:" and "Audio Settings:" → "Audio:" for brevity, matching the profile editor header style.
- **Disabled audio devices** — "Output: Not Applied" / "Input: Not Applied" render in `TertiaryTextBrush` with no device name shown; enabled devices show device name in `SecondaryTextBrush`.

### fix — profile editor

- **Detect Current buttons** — simplified label to **Load**.
- **Checkboxes inline with monitor name** — Enable/Primary/HDR checkboxes now sit in the same header row as the monitor name for both single-display controls and clone-group controls.
- **HDR field order** — HDR now appears above DPI Scaling in both Details and profile editor.
- **DPI Scaling label** — shortened to "DPI" in Details panel.
- **Clear All Scripts button** — added alongside Import in Scripts section header. Marks all scripts `IsDeleted = true` in one click; individual restore still works per-row through toggle delete button.
- **Profile editor placement** — `Window_Loaded` sizes and positions the editor over the main window at open time.

### misc

- **Profile list item gap removed** — `ListBoxItem` margin reduced from `0,1` to `0`, eliminating a 2px gap that caused jitter when the Apply button appeared and the description text reflowed.
- **Profile name length limit** — `MaxLength` increased from `50` to `60`, leaving headroom below known tray notification title limit.
- **Refresh removed from tray**.
- **Contributor links** — contributor entries in Settings → About include descriptive linked labels for contribution or project provenance.
- **Dependency updates** — NLog updated to 6.1.3; Newtonsoft.Json updated to 13.0.4.
- **General refinement** — various code cleanup, bug fixes, UI refinements, and optimizations.

> **Note:** Icon support in 2.0.4 — profile list cards collapse when icon file is missing from disk, and icon changes are not reflected in every UI surface outside an explicit profile apply. Resolved in [2.1.0](#2.1.0).

---

<a id="2.0.3"></a>
## [2.0.3] - 2026-05-22

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.3)_

### fix — audio

- **`AudioHelper` rewritten as a direct COM wrapper** — `AudioSwitcher.AudioApi` and `AudioSwitcher.AudioApi.CoreAudio` replaced with a thin P/Invoke layer targeting Windows `IMMDeviceEnumerator` and `IPolicyConfig` directly. `CoreAudioController` construction took multiple seconds and was required on every profile apply and editor open; replacement constructs and releases a bare COM object per operation and removes the persistent notification-subscription lifecycle.
- **`AudioSwitcher` dependencies removed** — `AudioSwitcher.AudioApi.dll` and `AudioSwitcher.AudioApi.CoreAudio.dll` are stripped from project compilation, installer, and portable archives.
- **Startup audio initialization removed** — `InitializeAudio()` call removed from application startup; `Dispose()` and `ReInitializeAudioController()` lifecycle stubs removed. Audio operations are self-contained per call.

### fix — profile editor

- ~~**Monitor name text box styled consistently with script names** — `_deviceTextBox` uses `TextBoxBackgroundBrush` and `TertiaryTextBrush` resource references.~~ *Removed in [2.1.0](#2.1.0).*
- **Audio device loading no longer blocks editor** — `LoadAudioDevices` is fire-and-forget at editor startup; window opens immediately and device dropdown populates asynchronously.

---

<a id="2.0.2"></a>
## [2.0.2] - 2026-05-21

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.2)_

- Adapting [fixes](https://github.com/xtrilla/DisplayProfileManager) by [xtrilla](https://github.com/xtrilla) — transient audio controller constructed and disposed per operation in place of persistent controller, atomic writes for settings and profiles, and synchronous settings save on exit.

### feat — profile schema

- **`SchemaVersion` field on profiles** — defaults to `0` on deserialization so existing profiles without a schema field automatically trigger migration on first load.
- **Automatic profile migration** — `LoadProfilesAsync` migrates outdated profiles on startup without changing `LastModifiedDate`. Version 0 → 1 backfills `NativeWidth`/`NativeHeight` and corrects `ReadableDeviceName` from live display data by `TargetId`. Displays unavailable during migration are skipped. *Hardware self-healing expanded to profile apply in [2.2.0](#2.2.0).*
- **`NativeWidth`/`NativeHeight` on `DisplaySetting`** — stores EDID preferred timing resolution from `targetVideoSignalInfo.activeSize`, representing physical pixel grid. Populated during `GetCurrentDisplaySettingsAsync` ~~and used by `BreakClone` to restore the correct resolution rather than defaulting to the highest supported mode, which may be a wider DCI resolution~~. *Rewritten to restore original settings in [2.1.0](#2.1.0).*

### feat — scripts

- **`.vbs`, `.js`, `.ahk` script support** — VBScript and JScript run via `cscript.exe /nologo`; AutoHotkey runs via `autohotkey.exe`. File picker updated to include all new types.

### fix — audio

- ~~**`AudioHelper` transient controller** — `CoreAudioController` constructed per audio operation and disposed immediately after, eliminating a persistent WASAPI `IMMNotificationClient` subscription that drove sustained background activity.~~ *Rewritten in [2.0.3](#2.0.3).*

### fix — reliability

- **Atomic saves** — `SaveProfileAsync` and `SaveSettingsAsync` write to temporary sibling, then replace destination atomically via `File.Replace` (NTFS-atomic), closing a zero-byte corruption path left by the truncate-then-write behavior. *Hardened in [2.2.0](#2.2.0) with unique temporary filenames, flush-before-replace, transient replacement retries, and cleanup.*
- **Synchronous settings save on exit** — `OnExit` uses `.GetAwaiter().GetResult()` instead of `Task.Run(...).Wait(2s)`, closing a silent data-loss path where slow disks could exceed the timeout and abandon the save.
- **Hotkey counter clamp** — `_profileEditWindowCount` uses `Math.Max(0, count - 1)` and checks `== 0`, preventing permanent hotkey deactivation if `ProfileEditWindow` constructor fails after `Window_Loaded`.
- **Async void hardening** — `ShowNotification`/`ShowBalloonTip` calls in async-void handlers, wrapped in nested `try/catch` blocks, to prevent process crashes if the tray icon is disposed during shutdown.
- **Audio load canceled on editor close** — `LoadAudioDevices` uses `CancellationTokenSource`, canceled in `OnClosed`, preventing orphaned `Task.Run` continuations from running after the editor is disposed.

### fix — display

- **Disconnected display detection** — `ApplyProfileAsync` checks enabled profile displays against live configs before topology apply. ~~Missing displays are recorded in `ProfileApplyResult.DisconnectedDisplays`, logged as warnings immediately, and excluded from defer wait. Remaining displays still apply. Previously, disconnected displays would timeout the full 10s defer before any error surfaced.~~ *Removed in [2.1.1](#2.1.1) as deep-sleep displays were misread as disconnected.*

### fix — clone

- ~~**`BreakClone` uses native resolution** — non-representative clone members now restore to `NativeWidth`/`NativeHeight` instead of `AvailableResolutions[0]`, which could be a DCI resolution wider than the actual panel pixel grid.~~ *Rewritten in [2.1.0](#2.1.0) to use full pre-clone-state restoration.*

### fix — profile management

- **Friendly monitor name** — `ReadableDeviceName` now uses the CCD friendly name from `DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME` instead of raw WMI `Win32_PnPEntity` string. Applied on new profile captures and backfilled during migration.

### fix — script import

- **`.lnk` files already in sandbox no longer duplicated** — early-return sandbox check now uses `DereferenceLinks = false` on file picker so `.lnk` paths are not resolved to their targets before directory comparison.

### fix — logs

- **Log retention fixed** — `NLog.config` now uses `maxArchiveDays="30"` instead of `maxArchiveFiles="30"`. Previous setting only capped the archive subfolder; daily log files in the root accumulated indefinitely.

### refactor — profile editor

- **Display loading and audio loading paths reduced** — ~~`LoadDisplaySettings` fetches WMI monitor IDs once per editor open instead of once per display control~~, with one additional display-config query when native dimensions need backfilling. Audio discovery moves off UI thread to avoid blocking editor startup. *WMI calls retired in [2.2.0](#2.2.0).*

### refactor — tests

- **Test suite — 212 tests** — expanded from the 200-test suite in [2.0.0](#2.0.0).
- **Test files reorganized** — `ProfileManagerInMemoryTests.cs` becomes `ProfileManagerTests.cs`, `SourceIdNormalizationTests.cs` becomes `DisplayConfigNormalizationTests.cs`, and `ApplyProfileScriptLogicTests` moves into `ProfileTests.cs` because it tests `Profile` model behavior. `ScriptHelperTests.cs` added for script parsing and process-launch helpers.
- **Default-value coverage attribution corrected** — `NativeWidth`, `NativeHeight`, and `SchemaVersion` default-value tests are now in `ProfileTests.cs`, removed from `DisplaySettingTests.cs`.

---

<a id="2.0.1"></a>
## [2.0.1] - 2026-05-09

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.1)_

### fix — script import

- **File picker extended** — filter now explicitly includes `.py` and `.exe` alongside previously supported types.
- **Sandbox import and shortcut virtualization** — `.exe` files now correctly copy to scripts sandbox and are automatically converted to `.lnk` shortcuts via late-bound `WScript.Shell`, fixing failures in the import pipeline.
- **Filename tokenization with spaces** — `.exe` paths containing spaces no longer split incorrectly during import or configuration serialization.

---

<a id="2.0.0"></a>
## [2.0.0] - 2026-05-08

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.0)_

- Adapting [PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23) by [rvahilario](https://github.com/rvahilario) — partial clone fixes, clone group UI, and test infrastructure.
- Adapting [PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14) by [jonathanasdf](https://github.com/jonathanasdf) — initial clone display support.

### feat — CLI

- **Command queue** — multiple commands can be issued in single invocation and executed in order.
- **Fuzzy flag matching** — flags are matched by prefix against full name.
- **`--tray`** — start minimized to tray *(retained from [v1.0.0](#v1.0.0)).*
- **`--refresh`/`--reload`/`-r`** — rescans profiles and themes folder and reapplies current theme, equivalent to pressing the **Refresh** button. Does not re-apply active display profile. Designed to support external tools, such as DPM Theme Builder, that modify theme files and need to signal the running instance to pick up changes.
- **`--theme`/`-t` + "name"** — apply named theme. With no argument, resolves and refreshes the currently active theme. With no running instance, writes the theme to `Settings.json` and exits cleanly.
- **`--profile`/`-p` + "name/ID"** — apply named profile. With no argument, resolves and reapplies the currently active profile.
- **`--headless`/`-h` + "name/ID"** — apply profile and exit without launching main app. With no argument, reapplies the current active profile headlessly.
- **IPC via named pipe (`DPM_ProfilePipe`)** — all commands are attempted against the running instance first via pipe. Falls back to local execution only if IPC fails.
- **IPC message protocol extended** — pipe carries typed commands (`CMD:REFRESH`, `THEME:<name>`, `PROFILE:<name>`) instead of raw profile names.

### feat — custom themes

- **Theming engine rebuilt** — control styles now live in shared `Base.xaml`; individual theme files contain only brush and color definitions. Base `Color` keys (`BackgroundColor`, `SurfaceColor`, `BorderColor`, `HoverColor`, `AccentColor`) are defined per theme, reducing per-theme boilerplate while allowing granular brush-level overrides.
- **Live theme list** — `ThemeHelper.AvailableThemes` dynamically built from both built-in themes and user themes folder. Settings dropdown populates from this list at runtime rather than from a hardcoded enum. *Reworked in [2.2.0](#2.2.0) to also repopulate after opening dropdown.*
- **`ThemeHelper.RefreshThemes`** — rescans themes folder, re-registers all available themes, and reapplies current theme unconditionally. Covers live edits, additions, and deletions without restart.
- **Custom theme import** — **Import** button in main window accepts `.xaml` theme files in addition to `.dpm` profile files, validates required brush keys before copying them to themes folder, then applies and persists theme immediately.
- **Theme persists after import** — `SetThemeAsync` is called after `ImportThemeAsync` to ensure the newly imported theme is active and saved.
- **Refresh button** — rescans theme folder and reloads profiles while reapplying current theme.
- **User themes folder** — `.xaml` color files loaded from `%AppData%\DisplayProfileManager\Themes\`; built-in theme names can be overridden by user files of the same name. `System` is reserved and protected.
- **Theme fallback** — `InitializeTheme` detects missing saved theme key and falls back to `System`, persisting the fallback to settings.
- **Added Black theme** — new built-in theme.
- **DPM Theme Builder** — included with release. Standalone Python tool generates compatible `.xaml` theme files from [tinted-theming/schemes](https://github.com/tinted-theming/schemes) database and signals the running instance automatically when theme is saved.

### feat — scripts

- **`ScriptManager` singleton** — owns sandboxed scripts folder at `%AppData%\DisplayProfileManager\Scripts\`, script import, and execution.
- **`.exe` imports** — create `.lnk` shortcuts via late-bound Windows Script Host (`WScript.Shell`) to avoid COM reference requirement. *Nonfunctional, resolved in [2.0.1](#2.0.1).*
- **Script runners** — `.ps1` files via `powershell.exe -ExecutionPolicy Bypass`, `.bat` via `cmd.exe`, `.py` via `python.exe`, `.lnk` via shell execute.
- **Per-profile script enable/disable** — `EnableScripts` determine whether scripts run; stored scripts remain in the profile when execution is disabled.
- **Scripts panel in profile editor** — lists all scripts with file-exists validation; missing scripts are flagged in orange. Add and edit custom launch arguments.

### feat — UI

- **Double-click profile item** — applies the profile if it is not currently selected; opens the editor if it is.
- **Inline Apply button** — 32×32, MDL2 `E751/E73E` icon on profile list items. Appears on hover, collapses on mouse leave, and reads the profile directly from `DataContext` to function without requiring prior selection.
- **Edit/Delete relocated** — moved to Details panel header, right-aligned and hidden when no profile is selected.
- **Export deprecated** — button remains in code but is collapsed. *Fully removed in [2.1.0](#2.1.0).*
- **Duplicate relocated** — moved to Profiles panel header alongside **Import**/**Create**, hidden when no profile is selected.
- **Import button** — accepts both `.dpm` profile files and `.xaml` theme files, branches on extension, and validates each type.
- **Description capped at 3 display lines** — truncated with `CharacterEllipsis` in Profile list. Uncapped descriptions previously allowed profile list items to expand arbitrarily.
- **Custom scrollbar style** — thin overlay-style thumb, click-to-jump via `PART_PageUp`/`PART_PageDown` repeat buttons, with separate vertical/horizontal templates; arrow buttons removed.
- **Horizontal scroll removed from profile list** — replaced with text wrapping by disabling horizontal scrollbar.
- **Shift+scroll horizontal scrolling** registered globally on all `ScrollViewer`s
- **Inner `ScrollViewer` scroll bubbling** — nested viewers bubble mouse-wheel events to the outer surface; relevant handler divides delta by 3 for smoother scrolling.
- **Profile apply success popup removed** — successful applies are silent; only failures produce a `MessageBox`. *Reworked in [2.2.0](#2.2.0) to always send notification.*

### fix — display engine

- **Complete display engine rewrite** — `ApplyDisplayTopology` + `DeferDisplayLayoutAsync` + `ApplyDisplayLayout` + `ApplyDisplayConfig` replace earlier application path with separate topology and layout stages. Topology and layout are applied atomically within their respective phases, rather than through multiple post-call corrections.
- **`DeferDisplayLayoutAsync` replaces staged application mode** — previous staged mode configured currently active displays, applied an arbitrary delay, then configured all display settings. Delay used `Thread.Sleep` occurred between active and inactive display configuration steps, rather than after inactive display wake. `DeferDisplayLayoutAsync` instead polls actual live display state every 250 ms (up to 10 seconds) and proceeds when applicable displays report ready.
- **SourceId normalization** — saved profiles can contain disabled displays, leaving remaining active displays with non-contiguous `SourceId` values such as `0, 2, 4`; `SetDisplayConfig` rejects such gaps. Active displays now receive contiguous source IDs through `BuildSourceIdMap` before submission. Single-display configurations had previously worked by coincidence because they were always assigned `SourceId 0`.
- **`ApplyHdrSettings` uses live `RawTargetId`** — stored profile `TargetId` values are lower-16-bit base IDs, while `DisplayConfigSetDeviceInfo` requires session-specific raw target ID. Fresh post-topology `GetDisplayConfigs` query matches by base `TargetId` and supplies live `RawTargetId`; `ApplyDisplayLayout` follows the same pattern because pre-topology raw identities are stale after `SetDisplayConfig`.
- **Redundancy checks** — topology, layout, and HDR application compare current live state first and skip corresponding call when no change is needed.
- **Removed erroneous `SetDisplayConfig` and `ChangeDisplaySettingsEx` calls** — `SetPrimary` and `ApplyDisplayPosition` previously issued an additional display-configuration call before topology apply, while `ChangeResolution` used legacy `ChangeDisplaySettings` API after topology. Rewritten path constructs desired layout directly and submits it atomically.
- **`SDC_TOPOLOGY_SUPPLIED` correctly re-added to `SetDisplayConfigFlags`** — required for proper clone-group topology application.
- ~~**`VerifyDisplayConfiguration`** — moved into `ApplyDisplayLayout` so non-zero `SetDisplayConfig` result could be retained as successful when the post-apply live check confirmed the expected display topology, including enabled/disabled state and clone-group SourceId sharing.~~ *Retired in [2.2.0](#2.2.0) because verifier did not establish that requested layout values had been applied and could therefore turn failed layout submission into false success.*

### fix — clone

- **Clone group detection by `SourceId` only** — previously grouped by `DeviceName + SourceId`, which failed under certain multi-monitor clone scenarios.
- **Clone creation for non-primary displays** — `ApplyDisplayTopology` and `ApplyDisplayLayout` assign shared `SourceId` to clone-group members instead of sequentially assigning one per display. Previously, only the primary display could be cloned by coincidence.
- **`BreakClone` preserves per-member settings** — ~~early clone implementation pre-seeded per-member restoration values so broken clones could recover attached-member resolution and refresh settings.~~ *Rewritten in [2.1.0](#2.1.0).*

### fix — profile management

- **`ImportProfileAsync` validates deserialized content** — imported JSON is accepted only when deserialized object has non-null `Name` and non-null `DisplaySettings`.
- **Profile list sorted alphabetically** — profiles are sorted alphabetically rather than by internal ID.

### refactor — profile editor

- **`DisplayGroupingHelper` extracted to `DisplayGroupHelper.cs`** — ~~grouping logic was moved from `ProfileEditWindow` into standalone helper.~~ Transition was incomplete because `MainWindow` did not use the helper and `ProfileEditWindow` retained its own nested copy. *Resolved in [2.1.0](#2.1.0).*

### test — unit tests

- **Test suite — 200 tests** — expanded from the 41-test baseline in [v1.4.0](#v1.4.0) alongside display-engine rewrite, adding regression coverage for hotkey configuration, profile model, display settings, LUID parsing, SourceId normalization, clone topology, and in-memory profile-manager operations.
- **Existing tests updated for rewritten display engine** — clone-group topology, clone-group validation, and `DISPLAYCONFIG_PATH_SOURCE_INFO` bit-encoding coverage were updated to reflect new API boundaries: `EnableDisplays` consolidation into `ApplyDisplayTopology`, `ValidateCloneGroups` moving from `ProfileManager` to `DisplayConfigHelper`, and removal of `SourceModeInfoIdx` and `CloneGroupId` from the P/Invoke struct.

### misc

- **Reset Settings button removed** — existing function only disabled auto-start; deleting `Settings.json` provides a full reset and regeneration when needed.
- **Open folder** — uses `UseShellExecute = true` so custom file explorers and shell extensions are respected, rather than hardcoding `explorer.exe`.
- **`dev-build.ps1`** — uses `vswhere` for dynamic Visual Studio discovery and accepts `-Configuration` and `-Platform` parameters.
- **General cleanup** — comment density reduced, log messages revised for clarity, and miscellaneous code/XAML attributes cleanup.

---

<a id="v1.4.0"></a>
## [v1.4.0] - 2026-03-15 (Alpha)

_[PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23) by [rvahilario](https://github.com/rvahilario)_

- Adapting [PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14) by [jonathanasdf](https://github.com/jonathanasdf) — initial clone display support.

### feat — display engine

- ~~**`ValidateCloneGroups`** — validates that clone-group members share resolution, refresh rate, SourceId, and position before apply; warns on DPI mismatch.~~ *Retired in [2.1.0](#2.1.0).*
- ~~**`VerifyDisplayConfiguration`** — rewritten to verify enabled/disabled state and clone-group SourceId sharing after apply; it did not verify the requested position, resolution, refresh rate, or rotation.~~ *Retired in [2.2.0](#2.2.0) as checks were insufficient to establish that the failed layout submission had actually applied the requested layout.*
- **`GetLUIDFromString`** — reconstructs an adapter LUID from a 16-character hexadecimal string for adapter-ID mapping.
- **`DisplayGroupingHelper` ~~inner class~~** — groups displays in editor UI. *Extracted in [2.0.0](#2.0.0).*
- **Initial `SetDisplayConfig`-based apply** — further attempts to move toward atomic display configuration via `SetDisplayConfig`; separate post-calls for resolution and primary display were still required because of malformed path/mode construction. *Rewritten in [2.0.0](#2.0.0).*

### feat — CLI

- **Development CLI flag** — introduced `--dev` mode so external build scripts could launch a second instance alongside the running one for development.

### fix — clone groups

- **Clone group UI** — `ProfileEditWindow` gains **Clone** and **Break Clone** buttons, member-name stacking, and a link icon.
- **`SourceModeInfoIdx` setter** — `set => modeInfoIdx = value` previously overwrote the entire 32-bit field, including the lower 16 bits used for `CloneGroupId`. Reworked to store a plain index in the upper 16 bits only; Phase 2 sets `modeInfoIdx` directly.
- **Source mode iteration per `SourceId` group** — clone-group members now correctly share one source-mode entry. Previously, `EnableDisplays` consumed a mode entry per display rather than per unique `SourceId`, so the second display in a clone group could consume a non-existent mode entry.
- **Clone display disable loop removed** — redundant loop in topology application was disabling displays twice.
- **`CloneGroupId` getter** — simplified from `(modeInfoIdx << 16) >> 16` to `modeInfoIdx & 0xFFFF` as correctness-preserving refactor.
- **Clone group member positions synced in `ExecuteClone`** — secondary displays joining a clone group from a non-zero extended position were given mismatched coordinates, causing `SetDisplayConfig` to reject the configuration.
- ~~**`SetDisplayConfig` non-zero return treated as success when `VerifyDisplayConfiguration` confirms apply** — non-zero result was accepted when the post-apply verifier judged the resulting display topology to match the requested enabled/disabled and clone-group state.~~ *Retired in [2.2.0](#2.2.0) as the verification pass could not establish that the requested layout values, such as position, resolution, refresh rate, and rotation, had actually been applied.*

### test — unit tests

- **Test suite — 41 tests** — established first recorded test-suite baseline, with builder infrastructure and regression coverage for clone-group topology, `DISPLAYCONFIG_PATH_SOURCE_INFO` bit encoding, and clone-group validation.

> **Note:** `ApplyDisplayPosition` had been removed without restoring the desktop layout, clone creation for non-primary displays remained nonfunctional because `EnableDisplays` reassigned `SourceId`s sequentially without respecting clone groups, and `SDC_TOPOLOGY_SUPPLIED` had been removed from `SetDisplayConfigFlags` despite being required for proper clone-group topology application. Resolved in [2.0.0](#2.0.0).

---

<a id="v1.3.5"></a>
## [v1.3.5] - 2025-11-21 (Alpha)

_[PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14) by [jonathanasdf](https://github.com/jonathanasdf)_

### feat — clone groups

- **Initial clone/mirror display support**.
- **`CloneGroupId` encoding** — stored in lower 16 bits of `modeInfoIdx`, with `SourceModeInfoIdx` in upper 16 bits.
- **`ResetModeAndSetCloneGroup()`** — invalidates source-mode index while setting clone group, as required for `SDC_TOPOLOGY_SUPPLIED`.
- **`DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID` constant added** — required by clone-topology construction.
- **Clone group detection in `GetCurrentDisplaySettingsAsync`** — groups displays by `DeviceName + SourceId` and assigns `CloneGroupId` strings.
- **Phase 1 / Phase 2 apply pattern** — topology is submitted first with `SDC_TOPOLOGY_SUPPLIED` and null modes, followed by the full configuration with `SDC_USE_SUPPLIED_DISPLAY_CONFIG` and modes.

> **Note:** clone creation only succeeded when the primary display was part of a group because source-mode consumption iterated per display instead of per `SourceId`. `SourceModeInfoIdx` could overwrite the entire `modeInfoIdx` field, and HDR used wrong target-ID form. Partially addressed in [v1.4.0](#v1.4.0) and later display-engine rewrites; base `TargetId`/live `RawTargetId` distinction was ultimately resolved in [2.0.0](#2.0.0).

---

<a id="v1.3.0"></a>
## [v1.3.0] - 2025-10-14

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.3.0)_

- Incorporating [PR #8](https://github.com/zac15987/DisplayProfileManager/pull/8) by [jarandal](https://github.com/jarandal) — initial HDR support and screen rotation.

### feat — display

- ~~**HDR support** — enable and disable HDR per display via `DisplayConfigSetDeviceInfo`.~~ *The profile passed a stripped base `TargetId` where the API required a live raw `TargetId`, producing `ERROR_INVALID_PARAMETER` (87). Resolved by live `RawTargetId` handling in [2.0.0](#2.0.0).*
- **Screen rotation per display** — 0°, 90°, 180°, 270°.
- ~~**Staged application mode** — applied settings in two phases with a configurable delay as a workaround for displays rejecting settings when waking from deep sleep.~~ *Fixed delay was non-deterministic and sat between active and inactive configuration steps, rather than after waking inactive displays. Removed in [2.0.0](#2.0.0).*
- ~~**Atomic `SetDisplayConfig`** — initial attempt at using `SetDisplayConfig` for display configuration.~~ *Separate post-calls for resolution and primary display still required due to malformed path and mode construction. Rewritten in [2.0.0](#2.0.0).*

---

<a id="v1.2.0"></a>
## [v1.2.0] - 2025-10-09

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.2.0)_

### feat — monitor identification

- **EDID-based monitor identification** — ~~`ManufacturerName`, `ProductCodeID`, and `SerialNumberID` were read through `WmiMonitorID` and correlated with CCD display targets.~~ *`ManufacturerName` and `ProductCodeID` moved to CCD `DISPLAYCONFIG_TARGET_DEVICE_NAME` data and `WmiMonitorID` / `SerialNumberID` retired in [2.2.0](#2.2.0).*
- **Display position capture** — `(X, Y)` position is stored and restored per profile.
- **Monitor enable and disable per profile** — monitors disabled in a profile are explicitly detached on apply; ~~undefined monitors are repositioned to the rightmost position to prevent overlap.~~ *Reworked in [v1.3.5](#v1.3.5) to explicit display detachment.*
- ~~**Automatic rollback on topology failure** — captures the state before apply and restores it on `SetDisplayConfig` failure, with a user notification.~~ *Retired in [v1.3.5](#v1.3.5).*
- **Monitor identification overlay** — numbered overlays appear on each display for three seconds, triggered from the profile editor.
- **Profile duplication support in UI** — duplicate copies the profile and opens the new profile in the editor.
- **Dual auto-start modes** — Registry requires no administrator privileges; Task Scheduler provides a faster-starting administrative alternative after setup.
- **NLog 6.0.4 integration** — daily rotation and logging replace `Debug.WriteLine` calls. *`NLog.config` used `maxArchiveDays="30"` instead of `maxArchiveFiles="30"`; only capping the archive subfolder and left daily log files in the root accumulating indefinitely. Resolved in [2.0.2](#2.0.2).*
- **Monitor capabilities stored in profiles** — resolutions, refresh rates, and DPI of detached monitors remain editable.
- **Per-device audio apply flags** — `ApplyPlaybackDevice` and `ApplyCaptureDevice` can be toggled independently.
- **Third-party library attribution** — attribution is added to the settings window.

### fix — display

- **EDID matching skips monitors with serial `0`**
- **Undefined monitors skip inactive entries during positioning**.
- **Refresh rate dropdown fallback** — falls back to current rate when `GetAvailableRefreshRates` returns empty.
- **`SetWindowPos` for monitor-identification overlay positioning** — fixes WPF coordinate errors on secondary monitors with different DPI.

### refactor — display API

- **`QueryDisplayConfig` replaces legacy `ChangeDisplaySettings` API for reading display topology and state** — *legacy `ChangeDisplaySettings` API still used to set resolution. Removed in [v1.3.5](#v1.3.5).*
- **GDI retained where CCD has no equivalent** — `EnumDisplaySettings` remains responsible for enumerating supported resolutions and refresh rates because `QueryDisplayConfig` reports only currently used modes. `EnumDisplayDevices` remains responsible for GDI device names and interface paths used by other subsystems. `QueryDisplayConfig` handles display topology and current display state.
- **DPI scaling simplified** — uses stored adapter IDs directly.
- **Extensive cleanup** — obsolete WMI correlation, Levenshtein matching, registry fallbacks, and unused P/Invoke declarations are removed.
- ~~**`InitializeAudio()` called at application startup** — created a long-lived `CoreAudioController` that subscribed to WASAPI `IMMNotificationClient` for the session lifetime, generating sustained cross-process RPC traffic and kernel paged-pool token allocations while idle.~~ *Reworked in [2.0.2](#2.0.2).*

> **Note:** automatic topology-failure rollback was retired for two independent reasons. Topology path was substantially rewritten in [v1.3.5](#v1.3.5), [2.0.0](#2.0.0), and [2.2.0](#2.2.0), making the original failure case substantially less representative. Separately, old rollback covered only the topology-stage `SetDisplayConfig` call and never protected the later layout-stage calls, so layout failures could still surface without restoring the previous display state.

---

<a id="v1.1.0"></a>
## [v1.1.0] - 2025-09-10

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.1.0)_

### feat — hotkeys

- **Global hotkeys for profile switching** — `HotkeyConfig` per profile, `HotkeyEditorControl` for capture and editing, conflict detection, tray menu integration showing shortcuts, and notifications on hotkey-triggered switches.
- **Hotkeys disabled while editor is open** — disabled automatically when `ProfileEditWindow` is open and re-enabled when all edit windows are closed.
- **Hotkey visualization in main window profile list** — green when enabled, gray when disabled.

### feat — audio

- **Initial support for audio device switching per profile** — playback and capture device selection through AudioSwitcher, including Bluetooth devices. *Rewritten as direct COM-based audio handling in [2.0.3](#2.0.3).*
- **`AudioController` re-initialization** — refreshes device list.

### feat — misc

- **`AboutHelper`** — centralizes version and settings-path management and adds community acknowledgments in Settings.
- **Semantic versioning with beta tag support** — via `AssemblyInformationalVersion`.
- **Inno Setup installer** — x64, x86, and ARM64.
- **Window resizing enabled** — available across application windows.
- **Settings accessible from tray icon**.

### fix — audio

- **Bluetooth device naming** — fixes invalid WMI queries and cross-device name contamination through stricter filtering, GUID and MAC-based validation, and dual-layer caching.

### fix — misc

- **Hotkey conflict detection uses `Key != None`** — accurate validation.
- **Single instance reliably restores foreground window** — uses thread input attachment and a dual activation strategy.

### refactor

- ~~**Global hotkey toggle**~~ removed — each profile controls its own hotkey active state.
- ~~**Automatic update checking removed**~~ *rebuilt in [2.2.0](#2.2.0).*
- **Version read from assembly** — no longer stored in settings.

---

<a id="v1.0.0"></a>
## [v1.0.0] - 2025-08-04

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.0.0)_

### feat

- **Multi-monitor display profile management** — resolution, refresh rate, and DPI per display.
- **System tray integration** — dynamic context menu for quick profile switching.
- **Profile storage as individual JSON `.dpm` files** — in `%APPDATA%\DisplayProfileManager\Profiles\`.
- **Profile management** — add, edit, duplicate, delete, and ~~export~~ profiles. *Export was removed in [2.0.0](#2.0.0).*
- **Light, Dark, and System themes** — WPF `ResourceDictionary` based, with dynamic switching and Windows theme detection. *Theme framework was substantially rewritten in [2.0.0](#2.0.0).*
- **Monitor-specific resolution and refresh-rate detection** — editor shows only supported values.
- **Readable monitor names via WMI**.
- **Primary display management**.
- **Auto-start with Windows** — Registry-based.
- **`--tray` CLI flag** — start minimized to system tray.
- **Close confirmation dialog** — with "Remember my choice".
- **Windows 11 Snap Layouts support** — via `WM_NCHITTEST`.
- **Custom native-style window chrome** — across all windows.
- **Single instance enforcement** — via a named mutex.
- ~~**Print Screen detection for profile switching**~~ *removed in [v1.1.0](#v1.1.0).*
- **Per-monitor DPI awareness (V2)** — declared in manifest.

> **Note:** display topology and mode application initially used legacy `ChangeDisplaySettings` API. Display-state reads moved to `QueryDisplayConfig` in [v1.2.0](#v1.2.0), and display mode application was later rebuilt around `SetDisplayConfig`. GDI remained where CCD had no equivalent: `EnumDisplaySettings` enumerates supported resolutions and refresh rates, while `EnumDisplayDevices` supplies device names and interface paths used by other subsystems. Both call types remain in use.