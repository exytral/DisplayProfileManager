# Changelog

All notable changes to this project are documented here.
Technical entries are intended for developers and contributors.
For user-facing release notes, see the [GitHub Releases](https://github.com/Exytral/DisplayProfileManager/releases) page.

---

<a id="2.1.0"></a>
## [2.1.0] - 2026-05-29

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.1.0)_

### feat — color profiles

- **`ColorProfile` field on `DisplaySetting`** — nullable `string`; `null` = not applied; any other value = bare ICC/ICM filename from the system color store. Stored in JSON. `[JsonIgnore]` `AdapterLuid` on `DisplaySetting` is populated at apply time from live config — not stored.
- **`IsAcmEnabled` field on `DisplaySetting`** — bool, default `false`. Independent of `ColorProfile`. ACM is forced on at apply time when `IsHdrEnabled` is true, regardless of this flag.
- **`ColorProfileHelper`** (`Helpers/ColorProfileHelper.cs`) — P/Invoke wrapper for `mscms.dll`. `GetSystemColorDirectory` resolves the system color profile directory. `GetInstalledColorProfilesFiltered(hdrOnly)` enumerates installed `.icc`/`.icm` files; when `hdrOnly = true`, restricts to profiles containing an MHC2 tag or a CICP tag with transfer function 16 (PQ) or 18 (HLG). `GetDisplayDefaultColorProfile` reads the current per-display OS association (user scope first, system scope fallback). `ApplyColorProfile` sets the default via `ColorProfileSetDisplayDefaultAssociation`, enabling per-user scope if not already active.
- **`ApplyColorProfiles`** in `DisplayConfigHelper` — called inside `ApplyDisplayConfig` after `ApplyAdvancedColorState`. Builds a transient `DisplaySetting` from live config to supply the correct `AdapterLuid` and `SourceId` for the P/Invoke call.
- **Color profile combobox** — rightmost column of the `DisplaySettingControl` settings row. Dropdown: Not Applied, then installed profiles (HDR-only set when HDR is active, full set otherwise). Profiles no longer installed on the system appear as `(not found)` placeholders to preserve the stored value.
- **Native resolution marker** — resolution dropdown appends `★` to the native EDID entry. Refresh rate dropdown appends `★` to the peak rate.

### feat — advanced color state

- **`ApplyAdvancedColorState`** replaces `ApplyHdrSettings` in `DisplayConfigHelper`. Handles HDR and ACM in a single pass per display. HDR forces ACM on; ACM is independently toggleable otherwise.
- **`DisplayConfigColorIntent` enum** — `Off`, `Acm`, `Hdr`; used by `SetAdvancedColorState` to route to the correct API path.
- **`SetAdvancedColorState(LUID, uint, DisplayConfigColorIntent)`** — unified toggle using the legacy `DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE` path. For `Acm` intent, resets to `Off` first so Windows selects ACM rather than HDR on dual-capable displays.
- **`SetHdrState`** — on Windows 11 24H2+, uses `DisplayConfigSetHdrState` (type 16 in `DisplayConfigDeviceInfoType`); falls back to `SetAdvancedColorState` on earlier builds.
- **`SetAcmState`** — on 24H2+, delegates to `SetWcgState` (type 17); pre-24H2, ACM is not supported on HDR-capable displays (logged as warning) and uses `SetAdvancedColorState` for SDR-only displays.
- **`IsAcmSupported(uint targetId)`** — returns true when running Windows 11 22H2+ and the target reports HDR capability via `GetAdvancedColorInfo`.
- **ACM checkbox in `DisplaySettingControl`** — hidden when unsupported (`Visibility.Collapsed`), grayed out and force-checked when HDR is active. HDR state change syncs the ACM checkbox.

### feat — script class

- **`Script` model** (`Core/Script.cs`) — `Profile.Scripts` migrated from `List<string>` to `List<Script>`. Each script carries `FileName`, `Arguments`, and `IsEnabled` (presently unused and not currently checked in `ExecuteScript`).
- **`ScriptListConverter`** — custom `JsonConverter` on `Profile.Scripts` handles backward-compatible deserialization: string entries (schema \<3) are parsed via `ScriptHelper.ParseScriptString` and promoted to `Script` objects; object entries deserialize normally.
- **`ScriptListEntry`** — strongly-typed UI class replacing `dynamic`/`ExpandoObject` in `ProfileEditWindow`. Carries `FilePath`, `FileName`, `Arguments`, `IsEnabled`, `IsDeleted`. Eliminates `Items.Refresh()` calls for property changes.

### feat — UI

- **`DisplaySettingControl` settings row** — single row replacing the previous two-row layout.
- **Rotation "Not Applied"** — rotation dropdown now has "Not Applied" at index 0 (`Rotation = 0`). `ApplyDisplayLayout` skips `paths[pIdx].targetInfo.rotation` when `profile.Rotation == 0`. Check `paths[pIdx].targetInfo.rotation != (uint)profile.Rotation` now also guards `profile.Rotation != 0`.
- **Monitor name font size** — increased from 14 to 18 in `DisplaySettingControl` header.
- **Icon scroll bubbling** — `IconScrollViewer_PreviewMouseWheel` in `ProfileEditWindow` bubbles wheel events from the icon grid's inner `ScrollViewer` to the outer container when the inner scroll is exhausted at either end.
- **Script/hotkey controls state** — `UpdateScriptControlsState` grays out `EnableScripts` toggle when no scripts are imported; `UpdateHotkeyEnableState` grays out the hotkey enable checkbox when no key is assigned and clears `IsChecked`. Auto-enables on script import/hotkey capture. Scripts disabled state no longer blocks import and configuration.
- **Profile icon hidden when file missing** — `Image` elements for profile icons (profile list card and details panel) now collapse to zero size when `ProfileIconConverter` returns `null` (i.e. the icon filename is set in the profile but the file is no longer present on disk). Added a third `DataTrigger` on the converter result in `Image.Style` alongside the existing null/empty string guards. `UpdateProfileDetails` in `MainWindow.xaml.cs` already guarded against this with `if (iconSource != null)` — this fix brings the list card XAML into line with that behavior.

### fix — clone groups

- **Primary transfer on clone** — when the attached display owns the primary flag, it transfers to the source at clone time. `GetDisplaySettings()` now reads `IsPrimary` from the data model rather than the checkbox so the value survives the `RebuildDisplayControls` cycle.
- **Break Clone restores attached display fully** — original resolution, refresh rate, position, SourceId, DPI scaling, rotation, color profile, HDR state, ACM state, and primary flag are saved at clone time via `[JsonIgnore]` fields (`OriginalPositionX/Y`, `OriginalSourceId`, `OriginalWidth/Height`, `OriginalFrequency`, `OriginalIsPrimary`, `OriginalDpiScaling`, `OriginalRotation`, `OriginalColorProfile`, `OriginalIsHdrEnabled`, `OriginalIsAcmEnabled`) and restored on break. Falls back to native resolution and a position to the right of the source if no saved values are available.
- **Break Clone guarantees a primary** — source display is unconditionally assigned primary on break; checkbox is synced before rebuild fires.
- **Clone params carried through rebuild** — `GetDisplaySettings()` now copies all six `[JsonIgnore]` clone param fields so they survive `RebuildDisplayControls`.


### fix — profile manager

- **`ApplyDisplayConfig` awaited** — `ApplyProfileAsync` now awaits `DisplayConfigHelper.ApplyDisplayConfig`, which is async. Previously the return value was captured synchronously.
- **`EnsureProfilesFolderExists` at I/O sites** — called at the start of `LoadProfilesAsync`, `SaveProfileAsync`, and `ImportProfileAsync` in addition to the constructor, matching the pattern used by `ScriptManager` and `ThemeHelper`.
- **HDR/ACM detection fix** — `GetDisplayConfigs` now sets `IsHdrEnabled = isEnabled && isHdrEncoding` and `IsAcmEnabled = isEnabled && !isHdrEncoding`, where `isHdrEncoding` means `colorEncoding == YCbCr444`. Previously, `IsHdrEnabled` was set to `true` whenever `AdvancedColorEnabled` was set without checking encoding type, conflating HDR and ACM.

### fix — IPC

- **Named pipe listener** (`App.xaml.cs`) — `NamedPipeServerStream` is now created once before the listen loop and reset via `Disconnect()` after each connection rather than disposed and recreated per iteration. Recreating per iteration caused `ERROR_PIPE_BUSY` between connections, filling log files with error spam.

### fix — auto start

- **`IsAutoStartEnabledTaskScheduler`** — switched from `/FO CSV` to `/FO LIST /V`; result now checks `output.Contains("Enabled") && !output.Contains("Disabled")`. Previously, a task that was registered but denied elevation (never actually created) was reported as enabled because the task name appeared in CSV output regardless of state.


### refactor — script manager

- **`ExecuteScript(Script)`** replaces the string-based overload.
- **`AddScript`, `RemoveScript`, `SortScripts` removed** — callers operate directly on `List<Script>`.
- **`FormatCommand` removed** — display formatting delegated to `Script.ToString()`.

### refactor —  profile manager

- **`AtomicWriteAllText` → `AtomicWrite`**
- **`GetAllProfilesWithHotkeys` → `GetProfilesWithActiveHotkeys`** — returns only profiles with an enabled hotkey assigned. `GetProfilesWithHotkeys` retained for profiles with any hotkey assigned (enabled or not).
- **Retired `ValidateCloneGroups`** — no longer required due to display engine rewrite since [2.0.0](#2.0.0).
- **Dead methods removed** — `ExportProfileAsync`, `GetProfileByHotkey`, `HasHotkeyConflict`, `FindConflictingProfile`, `ClearHotkeyAsync`, `GetProfilesFilePath`, `GetProfilesFolder`, `EnsureAppDataFolderExists`.

### refactor — profile

- **Schema version 3** — `CurrentSchemaVersion` bumped from 2 to 3. Migration backfills `ColorProfile` per display from `ColorProfileHelper.GetDisplayDefaultColorProfile` using live config. The `List<string>` → `List<Script>` promotion is handled at deserialization via `ScriptListConverter` rather than as a separate migration step, but the schema bump triggers a re-save.
- **`EnableAudio` field** — `bool`, default `true`, added to `Profile` and copied in `DuplicateProfile`. Presently unused and not currently checked in `ApplyProfileAsync`; `ApplyPlaybackDevice`/`ApplyCaptureDevice` on `AudioSetting` remain the operative flags.
- **`ProfileCollection` class removed** — unused.
- **`AddDisplaySetting` method removed** — unused.

### refactor — P/Invoke

- **All P/Invoke struct, enum, and constant names** in converted from `SCREAMING_SNAKE_CASE` to `PascalCase`. Examples: `DISPLAYCONFIG_PATH_INFO` → `DisplayConfigPathInfo`, `SDC_APPLY` → `Apply`, `ERROR_SUCCESS` → `ErrorSuccess`, `DISPLAYCONFIG_PATH_ACTIVE` → `Active`, `DISPLAYCONFIG_ROTATION` → `DisplayConfigRotation`.

### misc

- **`DisplayGroupHelper.cs` wired up** — `ProfileEditWindow` nested class removed; `DisplayGroupHelper.GroupDisplaysForUI` now called directly from both `ProfileEditWindow.LoadDisplaySettings` and `MainWindow.UpdateProfileDetails`. Details panel now renders clone groups correctly with a "Clone Group" indicator and multi-member device name stacking. Recovered from [v1.4.0](#v1.4.0).
- **`RefreshButton` disabled during reload** — button is disabled before `LoadProfilesAsync` begins and re-enabled in `finally`, preventing duplicate default profile generation on rapid clicks.
- **`dev-build.ps1`** — NuGet package restore runs before build; 5-second wait added on build failure so the error is visible.
- **General improvement** — comment and code cleanup and consistency refined, UI polish. Version string in Settings → About is now a hyperlink to the releases page. `DPMThemeBuilder.pyw` preview window updated.
___

<a id="2.0.5"></a>
## [2.0.5] - 2026-05-24

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.5)_

### fix — CLI

- **`--headless` with no argument falls back to saved profile** — `isHeadless` now participates in the startup profile resolution block alongside `isProfile`. Previously, `-h` with no argument and no running instance would skip `GetCurrentProfileId()` (only `isProfile` was checked) and fall through to full UI initialization instead of resolving and applying the saved profile headlessly.

---

<a id="2.0.4"></a>
## [2.0.4] - 2026-05-23

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.4)_

### feat — custom profile icons

- **`icon` field on `Profile`** — bare filename relative to `%AppData%\DisplayProfileManager\Icons\`, or `null` for no custom icon.
- **Tray icon reflects active profile** — `TrayIcon` resolves `profile.Icon` via `IconHelper` and replaces `_notifyIcon.Icon` on each apply. Falls back to the default app icon (cached at init) if loading fails.
- **Profile list inline icon** — `16×16` icon appears right of the profile name, left of Default/Active badges. *Collapses to zero width when `Icon` is null via `DataTrigger`. Bound via `ProfileIconConverter`. Did not include cases where `Icon` was set but file could not be found. Resolved in [2.1.0](#2.1.0).*
- **Details panel inline icon** — `18×18` icon appears right of the profile name in the details panel when a custom icon is set.
- **Profile editor icon picker** — full-width row between name/hotkey and Display Settings. Label, `32×32` preview, filename. **Import** opens file picker filtered to `.ico`; copies into sandbox via `IconHelper.ImportIconAsync`. Scrollable `41×41` toggle grid below shows all icons in the sandbox; clicking selects, clicking again deselects. **Clear** removes the icon without deleting the file.
- **`IconHelper`** — new helper (`Helpers/IconHelper.cs`) owning the Icons sandbox: `GetIconsFolderPath`, `ResolveIconPath` (path traversal rejection), `LoadIcon`, `LoadImageSource` (with `size` parameter and in-process `ConcurrentDictionary` cache keyed by `filename|size|lastWriteUtcTicks`), `ImportIconAsync`, `GetAvailableIcons`. Cache entries auto-evict when the source file is modified externally.
- **`ProfileIconConverter`** — converter (`UI/Converters/ProfileIconConverter.cs`) mapping bare filename → `ImageSource` for list card binding.
- **`ProfileViewModel.Icon`** — pass-through to `Profile.Icon`; required for list card DataTemplate binding.

### feat — profile schema

- **Schema version 2** — `CurrentSchemaVersion` bumped from 1 to 2. Version 1→2 migration is a no-op (`icon` defaults to `null`); version is bumped and re-saved. `DuplicateProfile` copies `Icon` from the source.

### fix — UI alignment

- **Checkbox and RadioButton template** — `Base.xaml` now provides implicit `CheckBox` and `RadioButton` styles with custom `Grid`-based templates. WPF's default `BulletDecorator` template ignores `VerticalContentAlignment`; the new template binds the bullet's `VerticalAlignment` directly to `VerticalContentAlignment`, fixing misalignment across all windows (MainWindow inline checkboxes, ProfileEditWindow, SettingsWindow, DisplaySettingControl).

### fix — details panel

- **Consistent section spacing** — all section headers in the details panel now share the same `16px` combined margin.
- **Display section renamed** — "Display Settings:" → "Display:", "Audio Settings:" → "Audio:" for brevity, matching the profile editor header style.
- **Disabled audio devices** — "Output: Not Applied" / "Input: Not Applied" rendered in `TertiaryTextBrush` (grayed) with no device name shown; enabled devices show name in `SecondaryTextBrush`.

### fix — profile editor

- **Detect Current** buttons — simplified label to **Load**
- **Checkboxes inline with monitor name** — Enable/Primary/HDR checkboxes now sit in the same header row as the monitor name for both single-display controls and clone group controls, removing the separate checkbox row below the name.
- **HDR field order** — HDR now appears above DPI Scaling in both the details panel and profile editor.
- **DPI Scaling label** — shortened to "DPI" in the details panel.
- **Clear All Scripts button** — added alongside Import in the Scripts section header. Marks all scripts `IsDeleted = true` in one click; individual restore still works per-row via the toggle delete button.
- **ProfileEditWindow spawns over MainWindow** — `Window_Loaded` now sets size and position to match the owner window at open time.

### misc

- **Profile list item gap removed** — `ListBoxItem` margin reduced from `0,1` to `0`, eliminating the 2px gap that caused jitter when the Apply button appeared and description text reflowed.
- **Profile name length limit** — `MaxLength` on the name `TextBox` increased from `50` to `60`. Names above 63 characters cause tray menu truncation; 60 is the enforced UI ceiling with headroom below the known breakpoint.
- **Refresh removed from tray**
- **Contributor links** — contributor entries in Settings → About now include a linked `(PR #N)`, `(Fork)`, or `(Original project)` label between the name and description. `AboutHelper.Contributors` updated with link label and URL constants for each entry.
- **Dependency updates** — NLog updated to 6.1.3; Newtonsoft.Json updated to 13.0.4.
- **General improvements** — comment density reduced, UI consistency refined. `DPMThemeBuilder.pyw` preview window updated.

_Note: Icon support in 2.0.4 — profile list cards collapse to zero width rather than hiding the `Image` element when the icon file is missing from disk. Icon assignment is also not reflected in the tray menu, on boot, or on profile updates outside of an explicit apply. Resolved in [2.1.0](#2.1.0)._

---

<a id="2.0.3"></a>
## [2.0.3] - 2026-05-22

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.3)_

### fix — audio

- **`AudioHelper` rewritten as a direct COM wrapper** — `AudioSwitcher.AudioApi` and `AudioSwitcher.AudioApi.CoreAudio` are replaced with a thin P/Invoke layer targeting Windows `IMMDeviceEnumerator` and `IPolicyConfig` directly. `CoreAudioController` construction took multiple seconds and was required on every profile apply and editor open (device enumeration for the dropdown). The new implementation constructs and releases a bare COM object per operation in double-digit milliseconds. No persistent subscription, no background threads, no third-party library overhead.
- **`AudioSwitcher` dependencies removed** — `AudioSwitcher.AudioApi.dll` and `AudioSwitcher.AudioApi.CoreAudio.dll` stripped from project compilation, installer, and portable archives.
- **Startup audio initialization removed** — `InitializeAudio()` call removed from app startup; `Dispose()` and `ReInitializeAudioController()` stubs removed. Audio operations are self-contained per call with no global lifecycle.

### fix — profile editor

- ~~**Monitor name text box styled consistently with script names** — `_deviceTextBox` now uses `TextBoxBackgroundBrush` and `TertiaryTextBrush` resource references, matching the read-only display style applied to script file names.~~ Removed in [2.1.0](#2.1.0)
- **Audio device loading no longer blocks editor** — `LoadAudioDevices` is now fire-and-forget at editor startup; the window opens immediately and the device dropdown populates asynchronously.

### misc

- ~~**Dependency updates** — NLog updated to 6.1.3; Newtonsoft.Json updated to 13.0.4.~~ Only updated in `packages.config`, resolved in [2.0.4](#2.0.4)

---

<a id="2.0.2"></a>
## [2.0.2] - 2026-05-21

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.2) — incorporating [fixes](https://github.com/xtrilla/DisplayProfileManager) by [xtrilla](https://github.com/xtrilla)_

### feat — scripts

- **`.vbs`, `.js`, `.ahk` script support** — VBScript and JScript run via `cscript.exe /nologo`; AutoHotkey runs via `autohotkey.exe`. File picker updated to include all new types.

### feat — profile schema

- **`SchemaVersion` field on profiles** — defaults to `0` on deserialization so existing profiles without the field automatically trigger migration on first load. New profiles write `schemaVersion: 1`.
- **Automatic profile migration** — `LoadProfilesAsync` migrates outdated profiles on startup without changing `LastModifiedDate`. Version 0 → 1 backfills `NativeWidth`/`NativeHeight` and corrects `ReadableDeviceName` from live display data by `TargetId`. Disconnected displays are skipped and backfilled on next load when reconnected.
- **`NativeWidth`/`NativeHeight` on `DisplaySetting`** — stores the EDID preferred timing resolution from `targetVideoSignalInfo.activeSize`, representing the panel's physical pixel grid. Populated during `GetCurrentDisplaySettingsAsync` and used by `BreakClone` to restore the correct resolution rather than defaulting to the highest supported (which may be a wider DCI resolution).
- **`DisplaySetting` property reorder** — fields now follow identity → state → layout → active configuration → native → capabilities. Purely cosmetic for `.dpm` files; no functional change.

### feat — display

- **Disconnected display detection** — `ApplyProfileAsync` checks enabled profile displays against live configs before topology apply. Missing displays are recorded in `ProfileApplyResult.DisconnectedDisplays`, logged as warnings immediately, and excluded from the defer wait. The remaining displays still apply. Previously, a disconnected display would cause the full 10s defer timeout before any error surfaced.

### fix — audio

- ~~**`AudioHelper` transient controller** — `CoreAudioController` is now constructed per audio operation and disposed immediately after, eliminating the persistent WASAPI `IMMNotificationClient` subscription that drove \~210 RPC calls/sec and \~21 kernel security token allocations/sec at idle. See [issue #10](https://github.com/zac15987/DisplayProfileManager/issues/10)~~ — Rewritten in [2.0.3](#2.0.3)

### fix — reliability

- **Atomic profile saves** — `SaveProfileAsync` now writes to a `.tmp` sibling then replaces atomically via `File.Replace` (NTFS-atomic), closing the zero-byte corruption path that `File.WriteAllText`'s truncate-then-write left open.
- **Atomic settings save** — same pattern applied to `SettingsManager.SaveSettingsAsync`.
- **Synchronous settings save on exit** — `OnExit` now uses `.GetAwaiter().GetResult()` instead of `Task.Run(...).Wait(2s)`, closing the silent data-loss path where a slow disk on logout could exceed the timeout and abandon the save.
- **Hotkey counter clamp** — `_profileEditWindowCount` now uses `Math.Max(0, count - 1)` and checks `== 0` instead of `<= 0`, preventing permanent hotkey deactivation if a `ProfileEditWindow` constructor fails after `Window_Loaded` fires.
- **Async void hardening** — `ShowNotification`/`ShowBalloonTip` calls in `async void` handlers (`ApplyProfileViaHotkey`, `OnProfileMenuItemClick`, `OnRefreshClick`) are now wrapped in a nested `try/catch` to prevent process crashes if the tray icon is disposed during shutdown.
- **Audio load cancelled on editor close** — `LoadAudioDevices` now uses a `CancellationTokenSource` cancelled in `OnClosed`, preventing orphaned `Task.Run` continuations from running after the window is disposed.

### fix — display

- ~~**`BreakClone` uses native resolution** — non-representative clone members now restore to `NativeWidth`/`NativeHeight` instead of `AvailableResolutions[0]`, which could be a DCI resolution wider than the panel's actual pixel grid (e.g. 4096×2160 on a 3840×2160 panel).~~ Expanded in [2.0.0](#2.0.0) to restore original settings

### fix — profile management

- **Friendly monitor name** — `ReadableDeviceName` now uses the CCD friendly name from `DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME` instead of the raw WMI `Win32_PnPEntity` string. Applied on new profile captures and backfilled during migration.

### fix — script import

- **`.lnk` files already in sandbox no longer duplicated** — the early-return sandbox check now uses `DereferenceLinks = false` on the file picker so `.lnk` paths are not resolved to their targets before the directory comparison.

### fix — logs

- **Log retention fixed** — `NLog.config` now uses `maxArchiveDays="30"` instead of `maxArchiveFiles="30"`. The previous setting only capped the archive subfolder; daily log files in the root accumulated indefinitely.

### refactor — profile editor

- **WMI and display config queries reduced from 2N to 1 per open** — `LoadDisplaySettings` now fetches `GetMonitorIDsFromWmiMonitorID` once and passes the result to each `DisplaySettingControl`. For profiles without a populated `NativeWidth`/`NativeHeight`, one additional `GetDisplayConfigs` call is made — still constant regardless of display count.
- **Audio device loading moved off UI thread** — `LoadAudioDevices` now runs device discovery via `Task.Run`, preventing WASAPI controller construction from blocking the window on open.

### refactor — tests

- **Test files reorganised** — `SourceIdNormalizationTests.cs` split into `DisplayConfigNormalizationTests.cs` (SourceId normalization and `BuildSourceIdMap`) and `ProfileTests.cs` (`ApplyProfileScriptLogicTests` moved here as it tests `Profile` model behavior). `ProfileManagerInMemoryTests.cs` renamed to `ProfileManagerTests.cs`. `ScriptHelperTests.cs` added as a new file.
- **`DisplaySettingTests.cs` updated** — new default value coverage for `NativeWidth`, `NativeHeight`, and `SchemaVersion`.

---

<a id="2.0.1"></a>
## [2.0.1] - 2026-05-09

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.1)_

### fix — script import

- **File picker extended** — filter now explicitly includes `.py` and `.exe` alongside previously supported types
- **Sandbox import and shortcut virtualization** — `.exe` files now correctly copy to the scripts sandbox and are automatically converted to `.lnk` shortcuts via late-bound `WScript.Shell`, fixing failures in the import pipeline
- **Filename tokenization with spaces** — `.exe` paths containing spaces no longer split incorrectly during import or configuration serialization

---

<a id="2.0.0"></a>
## [2.0.0] - 2026-05-08

_[exytral/DisplayProfileManager](https://github.com/exytral/DisplayProfileManager/releases/tag/2.0.0) — incorporating [PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23) by [rvahilario](https://github.com/rvahilario) and [PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14) by [jonathanasdf](https://github.com/jonathanasdf)_

### feat — display engine

- **Clone group detection by `SourceId` only** — previously grouped by `DeviceName + SourceId`, which failed for some multi-monitor clone scenarios. Grouping by `SourceId` alone correctly identifies all clone configurations regardless of device name.
- ~~**`DisplayGroupingHelper` extracted to `DisplayGroupHelper.cs`** — was an inner class inside `ProfileEditWindow`. Now a standalone file in the Helpers directory.~~ Transition was incomplete: `MainWindow` never called the helper, and `ProfileEditWindow` retained its own nested copy. Resolved in [2.1.0](#2.1.0)

### feat — CLI

- **Command queue** — multiple commands can be issued in a single invocation and are executed in order.
- **Fuzzy flag matching** — `--profile`, `--p`, `-p`, `pro` etc. all resolve to the same command. Flags are matched by prefix against their full name.
- **`--tray`** — start minimized to tray (carried from [v1.0.0](#v1.0.0)).
- **`--dev`** — bypass single-instance enforcement; allows a second instance to run alongside a running one (carried from [PR #23](https://github.com/zac15987/DisplayProfileManager/pull/23)).
- **`--refresh`/`--reload`/`-r`** — rescans the profiles and themes folder and reapplies the current theme, equivalent to pressing the Refresh button in the UI. Does not re-apply the active display profile. Designed to support external tools (such as DPM Theme Builder) that modify theme files and need to signal the running instance to pick up changes.
- **`--theme`  /` -t` + "name"** — apply a named theme. With no argument, resolves and refreshes the currently active theme from settings.
- **`--profile`/`-p` + "name/ID"** — apply a named profile. With no argument, resolves and reapplies the currently active profile from settings.
- **`--headless`/`-h` + "name/ID"** — apply a profile and exit without showing any UI. With no argument, reapplies the current active profile headlessly.
- **IPC via named pipe (`DPM_ProfilePipe`)** — all commands are attempted against a running instance first via pipe. Falls back to local execution only if IPC fails.
- **IPC message protocol extended** — pipe carries typed commands (`CMD:REFRESH`, `THEME:<name>`, `PROFILE:<name>`) instead of raw profile names. A running instance handles all three command types.
- **Theme persistence without UI** — `--theme <name>` with no running instance the writes theme to `Settings.json` and exits cleanly.

### feat — custom themes

- **Theming engine rebuilt** — control styles (TextBox, ComboBox, ScrollBar, ComboBoxItem, etc.) now live in a shared `Base.xaml`; individual theme files contain only brush and color definitions. Base `Color` keys (`BackgroundColor`, `SurfaceColor`, `BorderColor`, `HoverColor`, `AccentColor`) are defined per theme; most brushes derive from these, reducing per-theme boilerplate while allowing granular brush-level overrides.
- **Live theme list** — `ThemeHelper.AvailableThemes` is dynamically built from both built-in themes and the user themes folder. The settings dropdown populates from this list at runtime rather than from a hardcoded enum.
- **`ThemeHelper.RefreshThemes`** — rescans the themes folder, reregisters all available themes, and reapplies the current theme unconditionally. Covers live edits, additions, and deletions without restart.
- **Custom theme import** — the Import button in the main window now accepts `.xaml` theme files in addition to `.dpm` profile files (branches on extension). `ImportThemeAsync` validates required brush keys before copying to the themes folder, then applies and persists the theme immediately.
- **Theme persists after import** — `SetThemeAsync` called after `ImportThemeAsync` to ensure the newly imported theme is immediately active and saved.
- **Refresh button** (`MainWindow`) — rescans the theme folder and reapplies the current theme in addition to reloading profiles.
- **User themes folder** — drop a `.xaml` color file into `%AppData%\DisplayProfileManager\Themes\`; it appears in the theme dropdown after the next refresh. Built-in theme names (Light, Dark, Black) can be overridden by a user file of the same name. `System` is reserved and protected.
- **Theme fallback** — `InitializeTheme` detects a missing saved theme key (e.g. after a user theme is deleted) and falls back to System, persisting the fallback to settings.
- **Added Black theme** — new built-in theme using a Google Material dark mode palette (`#8AB4F8` primary blue, `#F28B82` danger red, `#81C995` success green).
- **DPM Theme Builder** — included with 2.0.0 release. A standalone Python tool that generates DPM-compatible `.xaml` theme files from the [tinted-themes](https://github.com/tinted-theming/base24) database.
  - 0.5s polling loop watches the themes folder and signals DPM automatically when a file is saved.

### feat — scripts

- **`ScriptManager` singleton** — sandboxed scripts folder at `%AppData%\DisplayProfileManager\Scripts\`. Exposes `ExecuteScript`, `ImportScriptAsync`, (~~`AddScript`, `RemoveScript`, `SortScripts`~~ Retired in [2.1.0](#2.1.0))
- ~~**`.exe` imports** — create `.lnk` shortcuts via late-bound Windows Script Host (`WScript.Shell`) to avoid a COM reference requirement.~~ Imports broken, resolved in [2.0.1](#2.0.1)
- **Script runners** — `.ps1` files via `powershell.exe -ExecutionPolicy Bypass`, `.bat` via `cmd.exe`, `.py` via `python.exe`, `.lnk` via shell execute.
- **Per-profile script enable/disable** — `EnableScripts` flag on the profile. When disabled, scripts still remain stored in the profile but skip execution.
- **Scripts panel in profile editor** — lists all scripts with file-exists validation; missing scripts flagged in orange. Add and edit custom launch arguments.
- **`DuplicateProfileAsync`** — includes `EnableScripts` flag and deep-copies the `Scripts` list.

### feat — UI

Refreshed the UI to a more minimal, interface — removing redundant labels, hiding context-irrelevant controls, and reducing visual clutter.

- **Double-click profile item** — applies the profile if it is not currently active; opens the editor if it is.
- **Inline Apply button** — 32×32, MDL2 `E751/E73E` icon on profile list items. Appears on hover, collapses on mouse leave. Reads the profile directly from `DataContext`, so it works without requiring the item to be selected first.
- **Edit/~~Export~~/Delete moved** to the Details panel header (right-aligned), hidden when no profile is selected.
  - ~~**Export deprecated** — EDID-based monitor identification makes profile portability impractical. Button remains in code but is collapsed.~~ Fully removed in [2.1.0](#2.1.0)
- **Duplicate moved** to the Profiles panel header alongside Import/Create, hidden when no profile is selected.
- **Import button** accepts both `.dpm` profile files and `.xaml` theme files — branches on extension and validates both types.
- **Description capped at 3 display lines** with `CharacterEllipsis` — was uncapped, causing profile list items to expand to arbitrary width.
- **Custom scrollbar style** — thin overlay-style thumb (8px wide), click-to-jump via `PART_PageUp`/`PART_PageDown` repeat buttons, separate vertical/horizontal templates. Arrow buttons removed.
- **Horizontal scroll removed from profile list** — replaced with text wrapping (`ScrollViewer.HorizontalScrollBarVisibility="Disabled"`).
  - ~~**Shift+scroll horizontal scrolling** registered globally on all `ScrollViewer`s via `EventManager.RegisterClassHandler`~~ Added but no longer relevant.
- **Inner `ScrollViewer` scroll bubbling** — `InnerScrollViewer_PreviewMouseWheel` bubbles scroll events from nested `ScrollViewer`s (hotkey list, about section) to the outer container; divides delta by 3 for a smoother feel.
- **Profile apply success popup removed** — only failures produce a `MessageBox`. Successful applies are silent.

### fix — display engine

- **Complete display engine rewrite** — `ApplyDisplayTopology`+`DeferDisplayLayoutAsync`+`ApplyDisplayLayout`+`ApplyDisplayConfig` replace old logic from with true, robust atomic application. Topology (enable/disable/clone grouping via `SDC_TOPOLOGY_SUPPLIED`) is now cleanly separated from layout (resolution, position, rotation, refresh via `SDC_USE_SUPPLIED_DISPLAY_CONFIG`). Both are applied atomically within their respective phases rather than through multiple post-call corrections (see **Removed erroneous `SetDisplayConfig` and `ChangeDisplaySettingsEx` calls** below).
- **`DeferDisplayLayoutAsync` replaces staged application mode** — polls every expected display for live path and valid dimensions (or until 10s timeout). Staged application mode applied display configs in two phases: first configure currently-active displays, apply an arbitrary delay, then configure everything. The delay sat between the active and inactive display configuration steps — failing to wait for inactive displays to wake from deep sleep (making the approach ineffective). The implementation also used `Thread.Sleep` (blocking) throughout, not `Task.Delay`, so the delay was synchronous regardless. `DeferDisplayLayoutAsync` polls the actual live display state and proceeds only when displays confirm ready.
- **SourceId normalization** — when a saved profile contains monitors that are disabled (or simply not in the current session), the remaining active displays may have non-contiguous `SourceId` values (e.g. 0, 2, 4). `SetDisplayConfig` rejects gaps, causing apply to sometimes fail. Active displays now receive contiguous IDs (0, 1, 2...) via `BuildSourceIdMap` before submission. Previously, single-monitor configs worked by coincidence because it happened to always be assigned to `SourceId 0` regardless. In most test cases, multi-monitor configs with disabled displays sometimes succeeded and sometimes failed depending on which monitor was saved as primary and how Windows had assigned IDs.
- **`ApplyHdrSettings` uses live `RawTargetId`** — `TargetId` values from a saved profile are the lower-16-bit base IDs, which are stable across sessions but are not what `DisplayConfigSetDeviceInfo` expects. After a `SetDisplayConfig` call, Windows assigns session-scoped raw target IDs that include upper bits. Passing the stored base ID produced `ERROR_INVALID_PARAMETER` (error 87). `ApplyHdrSettings` now runs a fresh `GetDisplayConfigs` query after topology is applied, matches displays by base `TargetId`, and uses `activeDisplay.RawTargetId` for the API call. `ApplyDisplayLayout` performs the same fresh query for the same reason — the raw IDs from the pre-topology snapshot are stale after `SetDisplayConfig` has reconfigured the adapter. Base `TargetId` (lower 16 bits) is stored in profiles for stable cross-session identification; `RawTargetId` is kept separately for live API calls only.
- **Added topology, layout, and HDR redundancy checks** — compare current live state before applying. Topology and/or layout skips the `SetDisplayConfig` call entirely if all checks (active state, SourceId, resolution, refresh rate, position, rotation) already match. HDR only toggles if the live state differs from the profile.
- **Removed erroneous `SetDisplayConfig` and `ChangeDisplaySettingsEx` calls** — `SetPrimary`, `ApplyDisplayPosition`, `ChangeResolution`
  - **`SetPrimary` and `ApplyDisplayPosition`** — called before `ApplyDisplayTopology`. `SetPrimary` calculated coordinate offsets to shift all displays so the intended primary landed at (0,0), then called `ApplyDisplayPosition` to issue another `SetDisplayConfig` call.
    If intended primary monitor is not currently active, `SetPrimary` will fail to correctly set new positions (leaving Windows to recover the layout).
  - **`ChangeResolution`** — called legacy `ChangeDisplaySettings` API to set resolution after `ApplyDisplayTopology`.
  - Profile application now truely atomic for resolution and position, including moving primary monitor to (0,0) with offset logic handled inline during layout construction. Rewritten `ApplyDisplayTopology` calls `SetDisplayConfig` once to set topology (enabling the intended primary monitor), `ApplyDisplayTopology` calls `SetDisplayConfig` once to set layout.
- **`SDC_TOPOLOGY_SUPPLIED` correctly re-added to `SetDisplayConfigFlags` enum** — required for proper clone group topology application.
- **`VerifyDisplayConfiguration`** — moved into `ApplyDisplayLayout` where it gates the success return: if `SetDisplayConfig` returns non-zero, the result is cross-checked against a live query. If the configuration matches anyway (Windows sometimes returns non-fatal codes on valid configs), the call succeeds. Note: `VerifyDisplayConfiguration` checks enabled/disabled state and clone group SourceId sharing — it does not check HDR state, so HDR failures are reported separately.

### fix — clone

- **Clone creation for non-primary displays** — `ApplyDisplayTopology`/`ApplyDisplayLayout` now correctly assigns a shared `SourceId` to all members of a clone group. Previously, `EnableDisplays` reassigned `SourceId`s sequentially per display without checking whether two displays belonged to the same clone group. Displays that should have shared a `SourceId` received distinct ones, so the clone relationship was never established. Only the primary display (`SourceId 0`) could be cloned by coincidence, since it was always assigned `SourceId 0` regardless.
- ~~**`BreakClone` preserves per-member settings** — when a clone group was broken, non-first members retained the representative's Resolution and Frequency instead of recovering to their own stored values. Fixed by pre-seeding per-member parameters in `BreakClone` and updating `GetDisplaySettings` to use stored per-member values when `CloneGroupId` is cleared, preventing the shared UI values from being stamped back onto all members during profile rebuild.~~ Fully implemented in [2.1.0](#2.1.0)

### fix — profile management

- **`ImportProfileAsync` validates deserialized content** — now checks that the deserialized object has a non-null `Name` and non-null `DisplaySettings` before saving. Previously, any structurally valid JSON file was accepted as a profile.
- **Profile list sorted alphabetically** — was previously sorted by internal ID (GUID).

### tests

- **Test suite expanded** — added regression coverage for hotkey configuration, profile model, display settings, LUID parsing, SourceId normalization, and in-memory profile manager operations.
- **Existing tests updated for the rewritten display engine** — test classes covering clone group topology, clone group validation, and `DISPLAYCONFIG_PATH_SOURCE_INFO` bit encoding were updated to reflect the 2.0.0 API: `EnableDisplays` consolidation into `ApplyDisplayTopology`, `ValidateCloneGroups` move from `ProfileManager` to `DisplayConfigHelper`, and removal of the `SourceModeInfoIdx` and `CloneGroupId` properties from the P/Invoke struct.

### misc

- **Reset Settings button removed** — existing function only disabled auto-start. Deleting `Settings.json` achieves a full reset/regeneration if desired.
- **Open folder** — uses `UseShellExecute = true` so custom file explorers and shell extension mods are respected (rather than hardcoding `explorer.exe`).
- **`dev-build.ps1`** — uses `vswhere` for dynamic Visual Studio discovery; accepts `-Configuration` and `-Platform` parameters.
- **General cleanup** — comment density reduced, log messages revised for clarity, misc code and XAML attributes cleaned up.

---

<a id="v1.4.0"></a>
## [v1.4.0] - 2026-03-15 (Alpha)

_[Fork](https://github.com/rvahilario/DisplayProfileManager/tree/fix/clone-display-bugs) by [rvahilario](https://github.com/rvahilario) — incorporating [PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14) by [jonathanasdf](https://github.com/jonathanasdf)_

### feat — display engine

- ~~**`ValidateCloneGroups`** — validates clone group members share resolution, refresh rate, SourceId, and position before apply; warns on DPI mismatch.~~ Retired in [2.1.0](#2.1.0)
- **`VerifyDisplayConfiguration`** — rewritten to a cleaner implementation; verifies enabled/disabled state and clone group SourceId sharing post-apply (still does not verify HDR state).
- **`GetLUIDFromString`** — reconstructs LUID from 16-char hex string for adapter ID mapping
- ~~**`DisplayGroupingHelper` inner class** — groups displays in the editor UI.~~ Extracted to its own file in [2.0.0](#2.0.0)
- **Initial `SetDisplayConfig`-based apply** — further attempt to move toward atomic display configuration via `SetDisplayConfig`. Separate post-calls for resolution and primary display were still required due to malformed path/mode construction. Resolved in [2.0.0](#2.0.0)

### feat — CLI

- **New CLI flag** — introduced `--dev` mode so that an external build script could launch a second instance alongside a running one for development.

### fix — clone groups

- **`SourceModeInfoIdx` setter** — `set => modeInfoIdx = value` overwrote the entire 32-bit field including the lower 16 bits used for `CloneGroupId`. Fixed to store the plain index in the upper 16 bits only. Phase 2 sets `modeInfoIdx` directly (Bug #2)
- **Source mode iteration per `SourceId` group** — clone group members now correctly share one source mode entry. Previously, `EnableDisplays` consumed a mode entry per display rather than per unique `SourceId`, so the second display in a clone group attempted to consume a non-existent mode entry (Bug #1)
- **Clone display disable loop removed** — a redundant loop in topology application was disabling displays twice (Bug #3)
- **`CloneGroupId` getter** — simplified from `(modeInfoIdx << 16) >> 16` to `modeInfoIdx & 0xFFFF` (Bug #6, refactor only)
- **Clone group member positions synced in `ExecuteClone`** — secondary displays joining a clone group from a non-zero extended position were given mismatched coordinates, causing `SetDisplayConfig` to reject the configuration
- **`SetDisplayConfig` non-zero return treated as success when `VerifyDisplayConfiguration` confirms apply** — Windows returns non-fatal codes on some valid configurations

### tests

- **MSTest project established** — initial test infrastructure with builder helpers (`DisplayConfigInfoBuilder`, `DisplaySettingBuilder`) and regression coverage for the clone group bugs fixed in this release: `DISPLAYCONFIG_PATH_SOURCE_INFO` bit encoding, clone group topology, and clone group validation.

_Note: `ApplyDisplayPosition` removed but not replaced; desktop layout no longer applied. Clone creation for non-primary displays remained broken — `EnableDisplays` reassigned SourceIds sequentially without respecting clone groups. `SDC_TOPOLOGY_SUPPLIED` erroneously removed from `SetDisplayConfigFlags` enum — required for proper clone group topology application. All three fully resolved in [2.0.0](#2.0.0)._

---

<a id="v1.3.5"></a>
## [v1.3.5] - 2025-11-21 (Alpha)

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager) — [PR #14](https://github.com/zac15987/DisplayProfileManager/pull/14) by [jonathanasdf](https://github.com/jonathanasdf)_

### feat — clone groups

- Initial clone/mirror display support
- `CloneGroupId` encoded in lower 16 bits of `modeInfoIdx`; `SourceModeInfoIdx` in upper 16 bits
- `ResetModeAndSetCloneGroup()` — invalidates source mode index while setting clone group; required for `SDC_TOPOLOGY_SUPPLIED`
- `SDC_TOPOLOGY_SUPPLIED` flag added to `SetDisplayConfigFlags` enum (was missing)
- `DISPLAYCONFIG_PATH_SOURCE_MODE_IDX_INVALID` constant added
- Clone group detection in `GetCurrentDisplaySettings` — groups displays by `DeviceName + SourceId`, assigns `CloneGroupId` strings
- Phase 1/Phase 2 apply pattern — topology first (`SDC_TOPOLOGY_SUPPLIED`, null modes), then full config (`SDC_USE_SUPPLIED_DISPLAY_CONFIG` with modes)
- Clone group UI in `ProfileEditWindow` — Clone dropdown button, Break Clone button, member name stacking, link icon

_Note: clone creation only worked when the primary display was part of the group — source mode consumption iterated per-display instead of per-SourceId. `SourceModeInfoIdx` setter overwrote entire `modeInfoIdx`. HDR used wrong `TargetId`. Resolved in [v1.4.0](#v1.4.0), but `TargetId` remains stripped._

---

<a id="v1.3.0"></a>
## [v1.3.0] - 2025-10-14

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.3.0) — [PR #8](https://github.com/zac15987/DisplayProfileManager/pull/8) by [jarandal](https://github.com/jarandal)_

### feat — display

- ~~HDR support~~: enable/disable per display in profiles via `DisplayConfigSetDeviceInfo` — Failed to apply due to stripped `TargetId`. Resolved in [2.0.0](#2.0.0)
- Screen rotation per display (0°, 90°, 180°, 270°)
- ~~Staged application mode~~ — applied settings in two phases with a configurable delay as a workaround for displays not receiving settings if woken up from deep sleep. Fixed delay was non-deterministic. Removed in [2.0.0](#2.0.0)
- ~~Atomic `SetDisplayConfig`~~ — initial attempt at using `SetDisplayConfig` for display configuration. Separate post-calls for resolution and primary display were still required due to malformed path/mode construction. Resolved in [2.0.0](#2.0.0)

_Note: HDR broken — `DisplayConfigSetDeviceInfo` was passed the stripped base `TargetId` from the profile instead of the live raw `TargetId`. Resulted in `ERROR_INVALID_PARAMETER` (error 87). Resolved in [2.0.0](#2.0.0)._

---

<a id="v1.2.0"></a>
## [v1.2.0] - 2025-10-09

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.2.0)_

### feat — monitor identification

- EDID-based monitor identification (ManufacturerName, ProductCodeID, SerialNumberID via `WmiMonitorID`) — profiles now correctly identify monitors even when Windows reassigns device names after hardware changes
- Display position (X, Y) stored and restored per profile
- Monitor enable/disable per profile — unincluded monitors explicitly disabled on apply; ~~undefined monitors repositioned to rightmost position prevent overlap.~~ Changed to disabling undefined monitors in [v1.3.5](#v1.3.5)
- ~~_Automatic rollback on topology failure — captures state before apply, restores on `SetDisplayConfig` failure with user notification_~~ — Retired, `ApplyDisplayTopology` was rewritten to become much more robust in [v1.3.5](#v1.3.5) + [2.0.0](#2.0.0)
- Monitor identification overlay — numbered overlays on each display for 3 seconds, triggered from profile editor
- Profile duplication support in UI
- Dual auto-start modes: Registry (no admin) and Task Scheduler (faster, requires admin for initial setup only). App no longer requires admin by default
- NLog 6.0.4 integration with daily rotation and 30-day retention, replacing all `Debug.WriteLine` calls
- Monitor capabilities (resolutions, DPI, refresh rates) stored in profiles for offline editing
- Per-device audio apply flags (`ApplyPlaybackDevice`, `ApplyCaptureDevice`)
- Comprehensive third-party library attribution in Settings

### fix — display

- EDID matching skips monitors with serial "0" to prevent false positives
- Undefined monitors skip inactive entries during positioning
- Refresh rate dropdown populated with current rate when `GetAvailableRefreshRates` returns empty
- `SetWindowPos` used for monitor identification overlay positioning — fixes WPF coordinate errors on secondary monitors with different DPI

### refactor — display API

- `QueryDisplayConfig` replaces legacy `ChangeDisplaySettings` API — correctly reports all displays including clones. [v1.0.0](#v1.0.0)–[v1.1.0](#v1.1.0) used the legacy API, which could not reliably detect clone topology ~~(legacy `ChangeDisplaySettings` API still used to set resolution).~~ Removed in [v1.3.5](#v1.3.5)
- DPI scaling simplified — uses stored adapter IDs directly
- Extensive cleanup: removed WMI correlation code, Levenshtein matching, registry fallbacks, unused P/Invoke declarations
- ~~`InitializeAudio()` called at app startup~~ — created a long-lived `CoreAudioController` that subscribed to the WASAPI `IMMNotificationClient` firehose for the session lifetime. Resolved in [2.0.2](#2.0.2) — see [issue #3](https://github.com/exytral/DisplayProfileManager/issues/3).

---

<a id="v1.1.0"></a>
## [v1.1.0] - 2025-09-10

_[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager/releases/tag/v1.1.0)_

### feat — hotkeys

- Global hotkeys for profile switching — `HotkeyConfig` per profile, `HotkeyEditorControl` for capture/editing, conflict detection, tray menu integration showing shortcuts, toast notifications on hotkey-triggered switch
- Hotkeys disabled automatically when `ProfileEditWindow` is open, re-enabled when all edit windows closed
- Hotkey visualization in main window profile list — green when enabled, gray when disabled

### feat — audio

- Initial support for audio device switching per profile — playback and capture device selection via AudioSwitcher, with support for Bluetooth devices
- `AudioController` re-initialization for device refresh

### feat — misc

- `AboutHelper` — centralized version/settings path management, community acknowledgments in Settings
- Semantic versioning with beta tag support via `AssemblyInformationalVersion`
- Inno Setup installer (x64, x86, ARM64)
- Window resizing enabled across all application windows
- Settings accessible from tray icon

### fix — audio

- Bluetooth device naming: fixed invalid WMI queries and cross-device name contamination via stricter filtering, GUID/MAC-based validation, and dual-layer caching

### fix — misc

- Hotkey conflict detection uses `Key != None` for accurate validation
- Single instance now reliably restores foreground window using thread input attachment and dual activation strategy

### refactor

- ~~Global hotkey toggle~~ removed — each profile controls its own hotkey enable/disable
- Automatic update checking removed
- Version read from assembly instead of settings

---

<a id="v1.0.0"></a>
## [v1.0.0] - 2025

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
- ~~Print Screen detection for profile switching.~~ Removed in [v1.1.0](#v1.1.0)
- Per-monitor DPI awareness (V2) via manifest

_Note: used legacy `ChangeDisplaySettings` API. Replaced by `QueryDisplayConfig` in [v1.2.0](#v1.2.0)_