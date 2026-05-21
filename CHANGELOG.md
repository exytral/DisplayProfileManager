# Changelog

All notable changes to this project are documented here.
Technical entries are intended for developers and contributors.
For user-facing release notes, see the [GitHub Releases](https://github.com/Exytral/DisplayProfileManager/releases) page.

<a id="2.0.2"></a>
## [2.0.2] - 2026-05-21

### feat — scripts

- **`.vbs`, `.js`, `.ahk` script support** — VBScript and JScript run via `cscript.exe /nologo`; AutoHotkey runs via `autohotkey.exe`. File picker updated to include all new types.

### feat — profile schema

- **`SchemaVersion` field on profiles** — defaults to `0` on deserialization so existing profiles without the field automatically trigger migration on first load. New profiles write `schemaVersion: 1`.
- **Automatic profile migration** — `LoadProfilesAsync` migrates outdated profiles on startup without changing `LastModifiedDate`. Version 0 → 1 backfills `NativeWidth`/`NativeHeight` and corrects `ReadableDeviceName` from live display data by `TargetId`. Disconnected displays are skipped and backfilled on next load when reconnected.
- **`NativeWidth`/`NativeHeight` on `DisplaySetting`** — stores the EDID preferred timing resolution from `targetVideoSignalInfo.activeSize`, representing the panel's physical pixel grid. Populated during `GetCurrentDisplaySettingsAsync` and used by `BreakClone` to restore the correct resolution rather than defaulting to the highest supported (which may be a wider DCI resolution).
- **`DisplaySetting` property reorder** — fields now follow identity → state → layout → active configuration → native → capabilities. Purely cosmetic for `.dpm` files; no functional change.

### feat — display

- **Disconnected display detection** — `ApplyProfileAsync` checks enabled profile displays against live configs before topology apply. Missing displays are recorded in `ProfileApplyResult.DisconnectedDisplays`, logged as warnings immediately, and excluded from the defer wait. The remaining displays still apply. Previously, a disconnected display would cause the full 10s defer timeout before any error surfaced.
- **`DeferDisplayLayoutAsync` skips disconnected displays** — only waits for displays confirmed present during disconnected display detection.

### fix — display

- **`BreakClone` uses native resolution** — non-representative clone members now restore to `NativeWidth`/`NativeHeight` instead of `AvailableResolutions[0]`, which could be a DCI resolution wider than the panel's actual pixel grid (e.g. 4096x2160 on a 3840x2160 panel).

### fix — profile management

- **Friendly monitor name** — `ReadableDeviceName` now uses the CCD friendly name from `DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME` instead of the raw WMI `Win32_PnPEntity` string. Applied on new profile captures and backfilled during migration.

### fix — script import

- **`.lnk` files already in sandbox no longer duplicated** — the early-return sandbox check now uses `DereferenceLinks = false` on the file picker so `.lnk` paths are not resolved to their targets before the directory comparison.

### refactor — tests

- **Test files reorganised** — `SourceIdNormalizationTests.cs` split into `DisplayConfigNormalizationTests.cs` (SourceId normalization and `BuildSourceIdMap`) and `ProfileTests.cs` (`ApplyProfileScriptLogicTests` moved here as it tests `Profile` model behavior). `ProfileManagerInMemoryTests.cs` renamed to `ProfileManagerTests.cs`. `ScriptHelperTests.cs` added as a new file.
- **`DisplaySettingTests.cs` updated** — new default value coverage for `NativeWidth`, `NativeHeight`, and `SchemaVersion`.

---

<a id="2.0.1"></a>
## [2.0.1] - 2026-05-09

### fix — script import

- **File picker extended** — filter now explicitly includes `.py` and `.exe` alongside previously supported types
- **Sandbox import and shortcut virtualization** — `.exe` files now correctly copy to the scripts sandbox and are automatically converted to `.lnk` shortcuts via late-bound `WScript.Shell`, fixing failures in the import pipeline
- **Filename tokenization with spaces** — script paths containing spaces no longer split incorrectly during import or configuration serialization

<a id="2.0.0"></a>
## [2.0.0] - 2026-05-08

_[Fork](https://github.com/exytral/DisplayProfileManager) by [exytral](https://github.com/Exytral) — incorporating [PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23) by [rvahilario](https://github.com/rvahilario) and [PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14) by [jonathanasdf](https://github.com/jonathanasdf)_

### fix — display engine

- **Complete display engine rewrite** — `ApplyDisplayTopology`+`ApplyDisplayLayout`+`ApplyDisplayConfig`+`DeferDisplayLayoutAsync` replace existing logic from [1.3.0](#1.3.0)/[1.3.5](#1.3.5)/[1.4.0](#1.4.0) with true, robust atomic application. Topology (enable/disable/clone grouping via `SDC_TOPOLOGY_SUPPLIED`) is now cleanly separated from layout (resolution, position, rotation, refresh via `SDC_USE_SUPPLIED_DISPLAY_CONFIG`). Both are applied atomically within their respective phases rather than through multiple post-call corrections (see **Removed erroneous `SetDisplayConfig` and `ChangeDisplaySettingsEx` calls** below).

- **`DeferDisplayLayoutAsync` replaces staged application mode** — polls every expected display for live path and valid dimensions (or until 10s timeout). The staged application design from [1.3.0](#1.3.0) applied settings in two phases: first configure currently-active displays, apply an arbitrary delay, then configure everything. The delay sat between the active and inactive display configuration steps — failing to instead wait for inactive displays to wake from deep sleep (making the approach ineffective). The implementation also used `Thread.Sleep` (blocking) throughout, not `Task.Delay`, so the delay was synchronous regardless. `DeferDisplayLayoutAsync` polls the actual live display state and proceeds only when displays confirm ready.

- **SourceId normalization** — when a saved profile contains monitors that are disabled (or simply not in the current session), the remaining active displays may have non-contiguous `SourceId` values (e.g. 0, 2, 4). `SetDisplayConfig` rejects gaps, causing apply to sometimes fail. Active displays now receive contiguous IDs (0, 1, 2...) via `BuildSourceIdMap` before submission. Previously, single-monitor configs worked by coincidence because it happened to always be assigned to `SourceId 0` regardless. In most test cases, multi-monitor configs with disabled displays sometimes succeeded and sometimes failed depending on which monitor was saved as primary and how Windows had assigned IDs.

- **`ApplyHdrSettings` uses live `RawTargetId`** — `TargetId` values from a saved profile are the lower-16-bit base IDs, which are stable across sessions but are not what `DisplayConfigSetDeviceInfo` expects. After a `SetDisplayConfig` call, Windows assigns session-scoped raw target IDs that include upper bits. Passing the stored base ID produced `ERROR_INVALID_PARAMETER` (error 87). `ApplyHdrSettings` now runs a fresh `GetDisplayConfigs` query after topology is applied, matches displays by base `TargetId`, and uses `activeDisplay.RawTargetId` for the API call. `ApplyDisplayLayout` performs the same fresh query for the same reason — the raw IDs from the pre-topology snapshot are stale after `SetDisplayConfig` has reconfigured the adapter. Base `TargetId` (lower 16 bits) is stored in profiles for stable cross-session identification; `RawTargetId` is kept separately for live API calls only.

- **Added topology, layout, and HDR redundancy checks** — compare current live state before applying. Topology and/or layout skips the `SetDisplayConfig` call entirely if all checks (active state, SourceId, resolution, refresh rate, position, rotation) already match. HDR only toggles if the live state differs from the profile.

- **Removed erroneous `SetDisplayConfig` and `ChangeDisplaySettingsEx` calls** — `SetPrimary`, `ApplyDisplayPosition`, `ChangeResolution`
  - **`SetPrimary` and `ApplyDisplayPosition`** — called before `ApplyDisplayTopology`. `SetPrimary` calculated coordinate offsets to shift all displays so the intended primary landed at (0,0), then called `ApplyDisplayPosition` to issue another `SetDisplayConfig` call.
    If intended primary monitor is not currently active, `SetPrimary` will fail to correctly set new positions (leaving Windows to recover the layout).
  - **`ChangeResolution`** — called legacy `ChangeDisplaySettings` API to set resolution after `ApplyDisplayTopology`.
  - 2.0.0 introduces true atomic application for resolution and position, including moving primary monitor to (0,0) with offset logic handled inline during layout construction. Rewritten `ApplyDisplayTopology` calls `SetDisplayConfig` once to set topology (enabling the intended primary monitor), `ApplyDisplayTopology` calls `SetDisplayConfig` once to set layout.

- **Staged application mode removed** — see **`DeferDisplayLayoutAsync`** above.

- **`SDC_TOPOLOGY_SUPPLIED` correctly re-added to `SetDisplayConfigFlags` enum** — required for proper clone group topology application, this flag was present in [1.3.5](#1.3.5) ([PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14)), removed in [1.4.0](#1.4.0) ([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23)), and is restored in 2.0.0.

- **`VerifyDisplayConfiguration`** — in [1.4.0](#1.4.0) ([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23)), `VerifyDisplayConfiguration` was called in `ProfileManager` after the apply sequence as a non-blocking diagnostic (preceded by a `Thread.Sleep(500)`). It has been moved into `ApplyDisplayLayout` where it gates the success return: if `SetDisplayConfig` returns non-zero, the result is cross-checked against a live query. If the configuration matches anyway (Windows sometimes returns non-fatal codes on valid configs), the call succeeds. Note: `VerifyDisplayConfiguration` checks enabled/disabled state and clone group SourceId sharing — it does not check HDR state, so HDR failures are reported separately.

### fix — clone

- **Clone creation for non-primary displays** — `ApplyDisplayTopology`/`ApplyDisplayLayout` now correctly assigns a shared `SourceId` to all members of a clone group. In [1.4.0](#1.4.0), `EnableDisplays` reassigned `SourceId`s sequentially per display without checking whether two displays belonged to the same clone group. Displays that should have shared a `SourceId` received distinct ones, so the clone relationship was never established. Only the primary display (`SourceId 0`) could be cloned by coincidence, since it was always assigned `SourceId 0` regardless.

- **`BreakClone` preserves per-member settings** — when a clone group was broken, non-first members retained the representative's Resolution and Frequency instead of recovering to their own stored values. Fixed by pre-seeding per-member parameters in `BreakClone` and updating `GetDisplaySettings` to use stored per-member values when `CloneGroupId` is cleared, preventing the shared UI values from being stamped back onto all members during profile rebuild.

### fix — profile management

- **`ImportProfileAsync` validates deserialized content** — now checks that the deserialized object has a non-null `Name` and non-null `DisplaySettings` before saving. Previously any structurally valid JSON file was accepted as a profile.

- **Profile list sorted alphabetically** — was previously sorted by internal ID (GUID).

### feat — display engine

- **Clone group detection by `SourceId` only** — previously grouped by `DeviceName + SourceId`, which failed for some multi-monitor clone scenarios. Grouping by `SourceId` alone correctly identifies all clone configurations regardless of device name.

- **`DisplayGroupingHelper` extracted to `DisplayGroupHelper.cs`** ([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23)) — was an inner class inside `ProfileEditWindow`. Now a standalone file in the Helpers directory.

### feat — CLI (full rewrite)

The original CLI from [1.0.0](#1.0.0) supported only `--tray` (start minimized). [PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23) by [jonathanasdf](https://github.com/jonathanasdf) introduced `--dev` mode so that an external build script could launch a second instance alongside a running one for development. The CLI has been fully rewritten around a command queue architecture.

- **Command queue** — multiple commands can be issued in a single invocation and are executed in order.
- **Fuzzy flag matching** — `--profile`, `--p`, `-p`, `pro` etc. all resolve to the same command. Flags are matched by prefix against their full name.
- **`--tray`** — start minimized to tray (carried from [1.0.0](#1.0.0).
- **`--dev`** — bypass single-instance enforcement; allows a second instance to run alongside a running one (carried from [PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23)).
- **`--refresh`/`--reload`/`-r`** — rescans the profiles and themes folder and reapplies the current theme, equivalent to pressing the Refresh button in the UI. Does not re-apply the active display profile. Designed to support external tools (such as DPM Theme Builder) that modify theme files and need to signal the running instance to pick up changes.
- **`--theme`  /` -t` + "name"** — apply a named theme. With no argument, resolves and refreshes the currently active theme from settings.
- **`--profile`/`-p` + "name/ID"** — apply a named profile. With no argument, resolves and reapplies the currently active profile from settings.
- **`--headless`/`-h` + "name/ID"** — apply a profile and exit without showing any UI. With no argument, reapplies the current active profile headlessly.
- **IPC via named pipe (`DPM_ProfilePipe`)** — all commands are attempted against a running instance first via pipe. Falls back to local execution only if IPC fails. Refresh- and theme-only commands with no name exit if no running instance is found rather than launching a UI window.
- **IPC message protocol extended** — pipe carries typed commands (`CMD:REFRESH`, `THEME:<name>`, `PROFILE:<name>`) instead of raw profile names. A running instance handles all three command types.
- **Theme persistence without UI** — `--theme <name>` with no running instance the writes theme to `Settings.json` and exits cleanly.

### feat — custom themes

The initial WPF ResourceDictionary-based theme engine system was introduced in [1.0.0](#1.0.0) with Light, Dark, and System options. 2.0.0 significantly expands it.

- **Theming engine rebuilt** — control styles (TextBox, ComboBox, ScrollBar, ComboBoxItem, etc.) now live in a shared `Base.xaml`; individual theme files contain only brush and color definitions. Base `Color` keys (`BackgroundColor`, `SurfaceColor`, `BorderColor`, `HoverColor`, `AccentColor`) are defined per theme; most brushes derive from these, reducing per-theme boilerplate while allowing granular brush-level overrides.
- **Live theme list** — `ThemeHelper.AvailableThemes` is dynamically built from both built-in themes and the user themes folder. The settings dropdown populates from this list at runtime rather than from a hardcoded enum.
- **`ThemeHelper.RefreshThemes`** (public) — rescans the themes folder, reregisters all available themes, and reapplies the current theme unconditionally. Covers live edits, additions, and deletions without restart.
- **Custom theme import** — the Import button in the main window now accepts `.xaml` theme files in addition to `.dpm` profile files (branches on extension). `ImportThemeAsync` validates required brush keys before copying to the themes folder, then applies and persists the theme immediately.
- **Theme persists after import** — `SetThemeAsync` called after `ImportThemeAsync` to ensure the newly imported theme is immediately active and saved.
- **Refresh button** (`MainWindow`) — rescans the theme folder and reapplies the current theme in addition to reloading profiles.
- **User themes folder** — drop a `.xaml` color file into `%AppData%\DisplayProfileManager\Themes\`; it appears in the theme dropdown after the next refresh. Built-in theme names (Light, Dark, Black) can be overridden by a user file of the same name. `System` is reserved and protected.
- **Theme fallback** — `InitializeTheme` detects a missing saved theme key (e.g. after a user theme is deleted) and falls back to System, persisting the fallback to settings.
- **Added Black theme** — new built-in theme using a Google Material dark mode palette (`#8AB4F8` primary blue, `#F28B82` danger red, `#81C995` success green), targeting OLED displays.
- **DPM Theme Builder** — included with 2.0.0 release. A standalone Python tool that generates DPM-compatible `.xaml` theme files from the [tinted-themes](https://github.com/tinted-theming/tinted-themes) database.
  - A 0.5s polling loop watches the themes folder and signals DPM automatically when a file is saved.

### feat — scripts (new in 2.0.0)

- **`ScriptManager` singleton** — sandboxed scripts folder at `%AppData%\DisplayProfileManager\Scripts\`. Exposes `ExecuteScript`, `AddScript`, `RemoveScript`, `SortScripts`, `ImportScriptAsync`.
- **`.exe` imports** — create `.lnk` shortcuts via late-bound Windows Script Host (`WScript.Shell`) to avoid a COM reference requirement.
- **Script runners** — `.ps1` files via `powershell.exe -ExecutionPolicy Bypass`, `.bat` via `cmd.exe`, `.py` via `python.exe`, `.lnk` via shell execute.
- **Per-profile script enable/disable** — `EnableScripts` flag on the profile. When disabled, scripts still remain stored in the profile but skip execution.
- **Scripts panel in profile editor** — lists all scripts with file-exists validation; missing scripts flagged in orange. Add and edit custom launch arguments.
- **`DuplicateProfileAsync`** — includes `EnableScripts` flag and deep-copies the `Scripts` list.

### feat — UI

Refreshed the UI to a more minimal, interface — removing redundant labels, hiding context-irrelevant controls, and reducing visual clutter.

- **Double-click profile item** — applies the profile if it is not currently active; opens the editor if it is.
- **Inline Apply button** — 32×32, MDL2 `E751/E73E` icon on profile list items. Appears on hover, collapses on mouse leave. Reads the profile directly from `DataContext`, so it works without requiring the item to be selected first.
- **Edit/~~Export~~/Delete moved** to the Details panel header (right-aligned), hidden when no profile is selected.
- **Export deprecated** — EDID-based monitor identification makes profile portability impractical. Button remains in code but is collapsed.
- **Duplicate moved** to the Profiles panel header alongside Import/Create, hidden when no profile is selected.
- **Import button** accepts both `.dpm` profile files and `.xaml` theme files — branches on extension and validates both types.
- **Description capped at 3 display lines** with `CharacterEllipsis` — was uncapped, causing profile list items to expand to arbitrary width.
- **Custom scrollbar style** — thin overlay-style thumb (8px wide), click-to-jump via `PART_PageUp`/`PART_PageDown` repeat buttons, separate vertical/horizontal templates. Arrow buttons removed.
- **Horizontal scroll removed from profile list** — replaced with text wrapping (`ScrollViewer.HorizontalScrollBarVisibility="Disabled"`).
- ~~**Shift+scroll horizontal scrolling** registered globally on all `ScrollViewer`s via `EventManager.RegisterClassHandler`~~ Added but no longer relevant.
- **Inner `ScrollViewer` scroll bubbling** — `InnerScrollViewer_PreviewMouseWheel` bubbles scroll events from nested `ScrollViewer`s (hotkey list, about section) to the outer container; divides delta by 3 for a smoother feel.
- **Profile apply success popup removed** — only failures produce a `MessageBox`. Successful applies are silent.

### tests

- **Test suite expanded** — added regression coverage for hotkey configuration, profile model, display settings, LUID parsing, SourceId normalization, and in-memory profile manager operations. `BuildSourceIdMap` extracted from `ApplyDisplayTopology`/`ApplyDisplayLayout` as a public static method, making the normalization logic directly testable without hardware.
- **Existing tests updated for the rewritten display engine** — test classes covering clone group topology, clone group validation, and `DISPLAYCONFIG_PATH_SOURCE_INFO` bit encoding were updated to reflect the 2.0.0 API: `EnableDisplays` consolidation into `ApplyDisplayTopology`, `ValidateCloneGroups` move from `ProfileManager` to `DisplayConfigHelper`, and removal of the `SourceModeInfoIdx` and `CloneGroupId` properties from the P/Invoke struct.

### misc
- **Reset Settings button removed** — existing function only disabled auto-start. Not enough settings to justify dedicated button — deleting `Settings.json` achieves a full reset/regeneration if desired.
- **Open folder uses shell execute** — the "open scripts folder" action uses `UseShellExecute = true` so custom file explorers and shell extension mods are respected (rather than hardcoding `explorer.exe`).
- **`dev-build.ps1`** — uses `vswhere` for dynamic Visual Studio discovery; accepts `-Configuration` and `-Platform` parameters.
- **General cleanup** — comment density reduced, log messages revised for clarity, misc code and XAML attributes cleaned up.

---

<a id="1.4.0"></a>
## [1.4.0] - 2026-03-15 (Beta)

_[Fork](https://github.com/rvahilario/DisplayProfileManager/tree/fix/clone-display-bugs) by [rvahilario](https://github.com/rvahilario) — incorporating [PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14) by [jonathanasdf](https://github.com/jonathanasdf)_

### fix

- **`SourceModeInfoIdx` setter** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — `set => modeInfoIdx = value` overwrote the entire 32-bit field including the lower 16 bits used for `CloneGroupId`. Fixed to store the plain index in the upper 16 bits only. Phase 2 sets `modeInfoIdx` directly (Bug #2)
- **Source mode iteration per `SourceId` group** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — clone group members now correctly share one source mode entry. Previously, `EnableDisplays` consumed a mode entry per display rather than per unique `SourceId`, so the second display in a clone group attempted to consume a non-existent mode entry (Bug #1)
- **Clone display disable loop removed** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — a redundant loop in topology application was disabling displays twice (Bug #3)
- **`CloneGroupId` getter** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — simplified from `(modeInfoIdx << 16) >> 16` to `modeInfoIdx & 0xFFFF` (Bug #6, refactor only)
- **Clone group member positions synced in `ExecuteClone`** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — secondary displays joining a clone group from a non-zero extended position were given mismatched coordinates, causing `SetDisplayConfig` to reject the configuration
- **`SetDisplayConfig` non-zero return treated as success when `VerifyDisplayConfiguration` confirms apply** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — Windows returns non-fatal codes on some valid configurations

_Note: clone creation for non-primary displays remained broken — `EnableDisplays` reassigned SourceIds sequentially without respecting clone groups. HDR broken — wrong `TargetId` (stripped base ID) passed to `DisplayConfigSetDeviceInfo`. Both resolved in [2.0.0](#2.0.0)._

### feat

- **`ValidateCloneGroups`** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — validates clone group members share resolution, refresh rate, SourceId, and position before apply; warns on DPI mismatch
- **`VerifyDisplayConfiguration`** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — re-added with a cleaner implementation; verifies enabled/disabled state and clone group SourceId sharing post-apply. Note: does not verify HDR state
- **`GetLUIDFromString`** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — reconstructs LUID from 16-char hex string for adapter ID mapping
- **`DuplicateProfile`/`DuplicateProfileAsync`** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — deep copies display settings and audio settings; clears hotkey to avoid conflicts
- **`DisplayGroupingHelper` inner class** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — groups displays in the editor UI (extracted to its own file in [2.0.0](#2.0.0))
- **HDR enable/disable per display** — via `DisplayConfigSetDeviceInfo`; broken due to incorrect `TargetId` — see note below
- **~~Staged application mode~~** — applied settings in two phases with a configurable blocking delay as a workaround for displays not receiving settings during deep sleep. The delay was a fixed `Thread.Sleep` and was non-deterministic in practice. Removed in [2.0.0](#2.0.0)
- **Initial `SetDisplayConfig`-based apply** — an attempt was made to move toward atomic display configuration via `SetDisplayConfig`. Separate post-calls for resolution and primary display were still required due to malformed path/mode construction. Fully resolved in [2.0.0](#2.0.0)

### tests

- **MSTest project established** _([PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23))_ — initial test infrastructure with builder helpers (`DisplayConfigInfoBuilder`, `DisplaySettingBuilder`) and regression coverage for the clone group bugs fixed in this release: `DISPLAYCONFIG_PATH_SOURCE_INFO` bit encoding, clone group topology, and clone group validation.

---

<a id="1.3.5"></a>
## [1.3.5] - 2025-11-21 (Alpha)

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager) — [PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14) by [jonathanasdf](https://github.com/jonathanasdf)_

### feat

- Initial clone/mirror display support
- `CloneGroupId` encoded in lower 16 bits of `modeInfoIdx`; `SourceModeInfoIdx` in upper 16 bits
- `ResetModeAndSetCloneGroup()` — invalidates source mode index while setting clone group; required for `SDC_TOPOLOGY_SUPPLIED`
- `SDC_TOPOLOGY_SUPPLIED` flag added to `SetDisplayConfigFlags` enum (was missing)
- `DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID` constant added
- Clone group detection in `GetCurrentDisplaySettings` — groups displays by `DeviceName + SourceId`, assigns `CloneGroupId` strings
- Phase 1/Phase 2 apply pattern — topology first (`SDC_TOPOLOGY_SUPPLIED`, null modes), then full config (`SDC_USE_SUPPLIED_DISPLAY_CONFIG` with modes)
- Clone group UI in `ProfileEditWindow` — Clone dropdown button, Break Clone button, member name stacking, link icon

_Note: clone creation only worked when the primary display was part of the group — source mode consumption iterated per-display instead of per-SourceId. `SourceModeInfoIdx` setter overwrote entire `modeInfoIdx`. HDR used wrong `TargetId`. Resolved in [1.4.0](#1.4.0), but `TargetId` remains stripped_

---

<a id="1.3.0"></a>
## [1.3.0] - 2025-10-14

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.3.0) — [PR #8](https://github.com/zac15987/DisplayProfileManager/pull/8) by [jarandal](https://github.com/jarandal)_

### feat

- ~~HDR support~~: enable/disable per display in profiles via `DisplayConfigSetDeviceInfo` — Failed to apply due to stripped `TargetId`. Resolved in [2.0.0](#2.0.0)
- Screen rotation per display (0°, 90°, 180°, 270°)
- ~~Staged application mode~~ — applied settings in two phases with a configurable delay as a workaround for displays not receiving settings if woken up from deep sleep. Fixed delay was non-deterministic. Removed in [2.0.0](#2.0.0)
- ~~Atomic `SetDisplayConfig`~~ — initial attempt at using `SetDisplayConfig` for display configuration. Separate post-calls for resolution and primary display were still required due to malformed path/mode construction. Resolved in [2.0.0](#2.0.0)

_Note: HDR broken — `DisplayConfigSetDeviceInfo` was passed the stripped base `TargetId` from the profile instead of the live raw `TargetId`. Resulted in `ERROR_INVALID_PARAMETER` (error 87). Resolved in [2.0.0](#2.0.0)._

---

<a id="1.2.0"></a>
## [1.2.0] - 2025-10-09

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.2.0)_

### feat

- EDID-based monitor identification (ManufacturerName, ProductCodeID, SerialNumberID via `WmiMonitorID`) — profiles now correctly identify monitors even when Windows reassigns device names after hardware changes
- Display position (X, Y) stored and restored per profile
- Monitor enable/disable per profile — unincluded monitors explicitly disabled on apply; ~~undefined monitors repositioned to rightmost position prevent overlap~~ — changed to disabling undefined monitors in [1.3.5](#1.3.5)/[2.0.0](#2.0.0)
- ~~_Automatic rollback on topology failure — captures state before apply, restores on `SetDisplayConfig` failure with user notification_~~ — retired, `ApplyDisplayTopology` was rewritten and augmented to become much more robust in [1.3.5](#1.3.5)/[1.4.0](#1.4.0) + [2.0.0](#2.0.0)
- Monitor identification overlay — numbered overlays on each display for 3 seconds, triggered from profile editor
- Profile duplication support in UI
- Dual auto-start modes: Registry (no admin) and Task Scheduler (faster, requires admin for initial setup only). App no longer requires admin by default
- NLog 6.0.4 integration with daily rotation and 30-day retention, replacing all `Debug.WriteLine` calls
- Monitor capabilities (resolutions, DPI, refresh rates) stored in profiles for offline editing
- Per-device audio apply flags (`ApplyPlaybackDevice`, `ApplyCaptureDevice`)
- `AudioController` re-initialization for device refresh
- Comprehensive third-party library attribution in Settings

### fix

- EDID matching skips monitors with serial "0" to prevent false positives
- Undefined monitors skip inactive entries during positioning
- Refresh rate dropdown populated with current rate when `GetAvailableRefreshRates` returns empty
- `SetWindowPos` used for monitor identification overlay positioning — fixes WPF coordinate errors on secondary monitors with different DPI

### refactor

- `QueryDisplayConfig` replaces legacy `ChangeDisplaySettings` API — correctly reports all displays including clones. [1.0.0](#1.0.0)–[1.1.0](#1.1.0) used the legacy API, which could not reliably detect clone topology ~~(legacy `ChangeDisplaySettings` API still used to set resolution)~~ — Removed in [1.3.5](#1.3.5)
- DPI scaling simplified — uses stored adapter IDs directly
- Extensive cleanup: removed WMI correlation code, Levenshtein matching, registry fallbacks, unused P/Invoke declarations
- ~~Print Screen hotkey and low-level keyboard hook~~ — Removed in [1.3.0](#1.3.0)
- Audio system initialized at app startup via explicit `InitializeAudio()`

---

<a id="1.1.0"></a>
## [1.1.0] - 2025-09-10

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.1.0)_

### feat

- Global hotkeys for profile switching — `HotkeyConfig` per profile, `HotkeyEditorControl` for capture/editing, conflict detection, tray menu integration showing shortcuts, toast notifications on hotkey-triggered switch
- Hotkeys disabled automatically when `ProfileEditWindow` is open, re-enabled when all edit windows closed
- Hotkey visualization in main window profile list — green when enabled, gray when disabled
- Audio device switching per profile — playback and capture device selection via AudioSwitcher, Bluetooth device support
- Per-profile audio apply flags
- `AudioController` re-initialization for device refresh
- `AboutHelper` — centralized version/settings path management, community acknowledgments in Settings
- Semantic versioning with beta tag support via `AssemblyInformationalVersion`
- Inno Setup installer (x64, x86, ARM64)
- Window resizing enabled across all application windows
- Settings accessible from tray icon

### fix

- Bluetooth device naming: fixed invalid WMI queries and cross-device name contamination via stricter filtering, GUID/MAC-based validation, and dual-layer caching
- Hotkey conflict detection uses `Key != None` for accurate validation
- Single instance now reliably restores foreground window using thread input attachment and dual activation strategy

### refactor

- ~~Global hotkey toggle~~ removed — each profile controls its own hotkey enable/disable
- ~~Automatic update checking~~ removed
- Version read from assembly instead of settings

---

<a id="1.0.0"></a>
## [1.0.0] - 2025

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.0.0)_

### feat

- Multi-monitor display profile management — resolution, refresh rate, DPI per display
- System tray integration with dynamic context menu for quick profile switching
- Profile storage as individual `.dpm` files in `%APPDATA%\DisplayProfileManager\Profiles\`
- Profile import/export
- Light/Dark/System themes via WPF ResourceDictionary with dynamic switching and Windows theme detection. Framework extended significantly in [2.0.0](#2.0.0)
- Monitor-specific resolution and refresh rate detection — dropdown shows only supported values
- Readable monitor names via WMI
- Primary display management
- Auto-start with Windows (Registry-based)
- `--tray` CLI flag — start minimized to system tray
- Close confirmation dialog with "Remember my choice"
- Windows 11 Snap Layouts support via `WM_NCHITTEST`
- Custom native-style window chrome across all windows
- Single instance enforcement via named mutex
- ~~Print Screen detection for profile switching~~ — Removed in [1.2.0](#1.2.0)
- Per-monitor DPI awareness (V2) via manifest
- Note: used legacy `ChangeDisplaySettings` API — replaced by `QueryDisplayConfig` in [1.2.0](#1.2.0)