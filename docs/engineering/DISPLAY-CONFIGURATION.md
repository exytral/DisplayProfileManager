# Display Configuration Engineering Reference

This document is the detailed current-state authority for Display Profile Manager display behavior and safety-critical display invariants. Root `AGENTS.md` defines operating rules; [ARCHITECTURE.md](./ARCHITECTURE.md) owns broader application/profile/project architecture.

The profile aggregate and persisted schema are described in ARCHITECTURE.md. This file is authoritative for how display state is interpreted, applied, recovered, edited, and self-healed.

## Display Configuration

The application uses the Windows Display Configuration API (`SetDisplayConfig`) for atomic profile switching.

1. **`ApplyDisplayTopology`** — reads live virtual-mode-aware CCD paths and prepares requested adapter-local clone/extend relationships. Normally it submits topology with `SDC_TOPOLOGY_SUPPLIED`, `SDC_VIRTUAL_MODE_AWARE`, and a null mode array. The virtual-mode-aware query also acts as a capability probe: when any requested path lacks `DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE`, Windows rejects a virtual-mode-aware submission that activates it with `ERROR_INVALID_PARAMETER` (87), so the topology is re-queried with plain `QDC_ALL_PATHS`, prepared with all-invalid mode indices, and submitted without `SDC_VIRTUAL_MODE_AWARE`; the recovery described next then also queries and submits without virtual-mode awareness. Only `ERROR_GEN_FAILURE` (31) permits bounded recovery: a fresh query with the same virtual-mode awareness as the submission obtains full path and mode arrays; recovery validates uniquely resolved adapter-qualified requested targets, source/target/desktop-image mode indices and types, and clone/extend group relationships, failing closed on missing, malformed, unavailable, or ambiguous structures. A successful `SDC_USE_SUPPLIED_DISPLAY_CONFIG` retry uses that validated **complete path+mode array**, followed by explicit fresh active-topology readback that must match the requested topology. `ERROR_INVALID_PARAMETER` (87) never enters this recovery path; there is no same-path/null-mode retry or best-mode fill of a null array.
2. **`ApplyDisplayConfig`** — captures all-paths target presence once, builds a stabilization wait set from enabled displays present in that snapshot, defers that set before the normal layout attempt, and calls `ApplyDisplayLayout` with the full requested configuration. A layout-stage `ERROR_GEN_FAILURE` (31) causes the same wait set to be deferred again and the full layout retried once, after which advanced color state and color profiles are applied.
    - **`DeferDisplayLayoutAsync`** — waits every 250 ms for up to 10 seconds for the supplied displays to become active. `ApplyDisplayConfig` supplies the enabled displays that were present in the all-paths snapshot; displays absent from that snapshot do not enter the wait set. The timeout is a maximum, not a mandatory delay.
    - **`ApplyDisplayLayout`** — issues a fresh `QueryDisplayConfig` because raw IDs from the pre-topology snapshot are stale after topology changes, then applies position, resolution, refresh rate, rotation, and SourceId normalization through `SDC_USE_SUPPLIED_DISPLAY_CONFIG`. `QDC_VIRTUAL_REFRESH_RATE_AWARE` and `SDC_VIRTUAL_REFRESH_RATE_AWARE` are used only on Windows 11 and later; supported Windows 10 systems use the non-VRR-aware path. The call is skipped when all live layout checks already match. Rotation is skipped when `profile.Rotation == 0` (`Not Applied`).
    - **`ApplyAdvancedColorState`** — queries live display configuration again after topology apply to obtain current `RawTargetId` values. On Windows 11 24H2+, HDR capability comes only from `HighDynamicRangeSupported`, WCG capability comes only from `WideColorSupported`, and policy limitation suppresses both configurable capabilities. `activeColorMode` is interpreted as the effective SDR, WCG, or HDR destination. Desired HDR rows resolve to HDR regardless of any compatibility WCG bit: entering HDR issues only the HDR setter, while HDR-to-SDR/WCG disables HDR before explicitly establishing the required SDR-side WCG state when that capability exists. Native HDR/WCG setter return codes are the mutation success/failure authority; the apply path does not add synchronous readback sleeps before later color-profile or DPI stages. Exact effective-mode matches are no-ops. Earlier supported systems retain the legacy generic Advanced Color/ACM provider behavior.
    - **`ApplyColorProfiles`** — for each enabled display with a non-null `ColorProfile`, builds a transient `DisplaySetting` carrying the current adapter-qualified target; `ColorProfileAdapter.ApplyColorProfile` reacquires the fresh live `SourceId` from that target before the native color-profile call.
3. **DPI** — applies `DpiScalingAdapter.SetDPIScaling` after layout, HDR, and color are committed. The pre-topology strict mapper carries a per-row adapter-qualified target authorization into this stage. DPI reobserves fresh post-topology display state and reacquires only that authorized target key so current `SourceId` and device-name data are fresh; ambiguous, conflicting, insufficient-evidence, absent, or disappeared rows cannot gain a target through stale IDs, path order, or permissive continuity guesses. When the caller already has that fresh `DisplayConfigInfo`, `SetDPIScaling` uses it directly. The method refuses when no scaling information is available and snaps an in-range unsupported value to the nearest ladder step.
4. **Wallpaper** — applies `WallpaperSettings` through the destination-owned transition plan when wallpaper is enabled. Picture assignments and Slideshow sources are preflighted before destructive ownership changes; any unresolved requested Picture assignment prevents full success. Solid/Picture/Slideshow explicitly establish and verify their `BackgroundType` ownership after COM state is applied. Spotlight first returns desktop rendering ownership to Windows, establishes provider/mode state, normalizes Fill presentation, and verifies rendered provider content over the exact current-active monitor domain. If Windows does not repaint promptly, the application may use only an existing verified Client.CBS provider-owned current/landscape image as a repaint bridge before reasserting and verifying provider/mode; Windows remains responsible for Spotlight content delivery and freshness. Hard destination-establishment failures are surfaced through `WallpaperSuccess` and trigger a best-effort prior-owner restore.
5. **Audio** — applies the configured playback and recording devices only when audio is enabled and the corresponding `ApplyPlaybackDevice` or `ApplyCaptureDevice` flag is enabled.
6. **Scripts** — executes a script only when script execution is enabled and that script's own `IsEnabled` flag is enabled.

`QDC_ALL_PATHS` membership determines which enabled displays belong in the stabilization wait set and which targets participate in topology comparison and reconstruction; transient `targetAvailable` state is not used as a presence gate. `QDC_ONLY_ACTIVE_PATHS` is used while polling for active paths. A display present in `QDC_ALL_PATHS` but temporarily absent from `QDC_ONLY_ACTIVE_PATHS` remains in the supplied defer set and can be picked up by a later poll — temporary absence from the active-path query does not by itself establish physical disconnection. No disconnected-display result state is recorded on `ProfileApplyResult`.

### Critical implementation notes

- **Never use `ChangeDisplaySettingsEx` for topology or resolution changes.** Display topology and layout changes go through `DisplayConfigService`; `ApplyDisplayLayout` handles resolution atomically inside `SetDisplayConfig`.
- **SourceId is runtime topology state.** Before topology, the apply mapper synthesizes adapter-local source groups from `CloneGroupId`; independent displays receive distinct groups. `ApplyDisplayTopology` and `ApplyDisplayLayout` normalize those groups through `BuildSourceIdMap`.
- **Apply-time address resolution is separate from profile topology intent.** Before topology, `QDC_ALL_PATHS` supplies adapter-qualified observations for active and inactive-but-present endpoints. Resolution uses a unique numeric `TargetId` when identity is absent or agrees, otherwise follows one unique manufacturer/product EDID match, then permits the same unique numeric target as the generic port/location fallback when EDID does not resolve uniquely. Duplicate adapter-qualified endpoints sharing that numeric target remain ambiguous. `CloneGroupId` expresses clone/extend intent; adapter LUID, raw target ID, source ID, and path index are live execution data. DPI may refresh only the adapter-qualified target authorized before topology.
- **Source IDs are assigned per clone group per adapter, not per path.** The apply mapper translates `CloneGroupId` into transient adapter-local source groups, and topology/layout then assign the Windows source IDs needed for the current session. Identical clone labels on different adapters never imply a cross-adapter shared source.
- **Clone groups must be set in `ApplyDisplayTopology` with `SDC_TOPOLOGY_SUPPLIED`.** Once the mode array is used for layout, changing clone groups would invalidate mode indices.
- **24H2 Advanced Color capability is mode-specific.** `HighDynamicRangeSupported` controls HDR availability and `WideColorSupported` controls WCG availability; `AdvancedColorLimitedByPolicy` suppresses both configurable capabilities. `AdvancedColorSupported` is not a substitute for either dedicated capability bit. The active destination remains exactly SDR, WCG, or HDR. `DisplayConfigSetDeviceInfo` success/failure from the requested HDR/WCG setter is authoritative for mutation; do not add warning-only synchronous readback polling that stalls downstream stages without governing success. Pre-24H2, live HDR capability selects the HDR compatibility path, while SDR-side WCG intent uses ACM only on non-HDR-capable providers.
- **HDR requires a live `RawTargetId`.** Always query live display configuration after topology apply, match by base `TargetId` (lower 16 bits), and use `activeDisplay.RawTargetId` for `DisplayConfigSetDeviceInfo`.
- **Disconnected-display detection must distinguish deep sleep from absence.** `ApplyDisplayConfig` captures `GetAllPathTargetIds` once using `QDC_ALL_PATHS`, then excludes enabled profile displays that are absent from that snapshot from the stabilization wait.

## Display Recovery

The display recovery settings control what happens after a display stage fails.

- `abortOnApplyFailure` stops the pipeline when topology or layout fails instead of continuing through DPI, wallpaper, audio, and scripts.
- `rollbackAfterApplyFailure` enables rollback after an aborted apply.
- `rollbackToPreviousProfile` selects the recovery target. When enabled and a previous profile exists, the application reapplies that profile. When no previous profile exists, recovery falls back to the pre-apply display snapshot. When disabled, the snapshot is used directly.
- All three recovery settings default to `true`.
- Both topology and layout failures reach the same recovery gate. A topology failure skips defer and layout, records `DisplayConfigApplied` as false, and still reaches recovery.
- Snapshot recovery captures the pre-apply display state before any mutation and preserves clone topology so the existing display pipeline can restore it.
- A snapshot rollback clears the active-profile marker because the restored state may not correspond to any profile.
- Previous-profile rollback applies the entire profile pipeline, while snapshot rollback restores display state only.
- Rollback reuses `ApplyProfileAsync` with a `_rollingBack` guard so a failed rollback reports the failure instead of recursively entering the abort/recovery path.

Advanced Color, Color Profile, DPI, Wallpaper, and Audio are non-blocking secondary stages. `ProfileApplyResult.Success` remains controlled by the display/layout stage. Secondary failures are represented separately and, on otherwise successful applies, summarized in application order for status and normal successful-apply notification presentation. They do not independently make the profile apply fail, trigger rollback, or create blocking error UI.

### Engineering Overrides

Display-apply failure injection is process-scoped engineering state resolved once by `App` through `EngineeringOverrides`. There is no Settings UI or persisted settings object for these controls.

| Environment variable | Value | Effect |
| --- | --- | --- |
| `DPM_ENGINEERING_FORCE_APPLY_FAILURE` | absent / empty / `0` | inactive |
| `DPM_ENGINEERING_FORCE_APPLY_FAILURE` | `1` | inject a Topology-stage failure |
| `DPM_ENGINEERING_FORCE_APPLY_FAILURE` | `2` | inject a DisplayConfiguration-stage failure |
| `DPM_ENGINEERING_FORCE_TOPOLOGY_RECOVERY` | absent / empty / `0` | inactive |
| `DPM_ENGINEERING_FORCE_TOPOLOGY_RECOVERY` | `1` | force the Error-31 topology-recovery path |

Malformed values fail inactive and emit a warning. `SkipSpotlightRepaint` and `CenterIconGrid` no longer exist.

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
- `IDesktopWallpaper` uses monitor device paths. `WallpaperMonitorMapper.BuildMonitorMap()` derives each active GDI source name and exact target `monitorDevicePath` from CCD, intersects that identity with currently attached `IDesktopWallpaper` monitor IDs, and does not use target-number or `EnumDisplayDevices` heuristics.
- Profiles do not persist adapter LUIDs. CCD source/target operations use the live adapter LUID; a unique manufacturer/product EDID may follow a moved panel, while numeric `TargetId` is a generic port/location fallback only when it identifies exactly one canonical endpoint.
- `DisplayConfigInfo.TargetId` is the normalized lower-16-bit target identity used by profile matching; `RawTargetId` preserves the exact native target ID required by device-info setter calls.
- `QDC_ALL_PATHS` can contain inactive alternate routes for the same target. Lookups by target ID must prefer the active path, and callers that clear `Active` must preserve the live route they were using before doing so.


### Clone Groups

Clone groups enable display mirroring with multiple monitors showing identical content.

- **Clone-group IDs** are encoded in `DISPLAYCONFIG_PATH_SOURCE_INFO.modeInfoIdx`: lower 16 bits are the clone group ID (`modeInfoIdx & 0xFFFF`), and upper 16 bits are the source-mode index (`modeInfoIdx >> 16`). `ResetModeAndSetCloneGroup()` is used for Phase 1; Phase 2 assigns the field directly. In `ApplyDisplayLayout`, all members of a durable `CloneGroupId` relationship share one transient adapter-qualified source-mode entry. Do not consume a separate mode entry per display.
- `GetCurrentDisplaySettingsAsync()` detects clone relationships from the live adapter-qualified CCD `SourceId`, then stores only `CloneGroupId` / `IsCloneSource` semantics in the profile model.
- **`CreateCloneGroup()`** saves all attached-member pre-clone state before making changes. The primary-transfer block runs after the save loop because it clears `IsPrimary` on attached members.
- **`BreakCloneGroup()`** restores each attached member's complete saved display state. Restored fields include primary state, position, resolution, refresh rate, rotation, DPI scaling, HDR, WCG, and color profile. If no saved clone settings exist, it falls back to native resolution and a position to the right of the source.
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

Fields that respect `useOwnParams` when restored parameters are active are `Width`, `Height`, `Frequency`, `Rotation`, `DpiScaling`, `IsHdrEnabled`, `IsWcgEnabled`, and `ColorProfile`. Identity, enabled state, layout position, HDR capability, native dimensions, and capability collections come from the model directly.

`IsPrimary` is read from the original setting rather than inferred from list position. `IsCloneSource` in output is `originalSetting.IsCloneSource && !string.IsNullOrEmpty(originalSetting.CloneGroupId)`, so independent displays never inherit the clone-source flag.

### Clone settings

The following `DisplaySetting` fields are `[JsonIgnore]` and therefore exist only during the editor session:

| Property                                 | Description                                                                                           |
| ---------------------------------------- | ----------------------------------------------------------------------------------------------------- |
| `OriginalSettings`                       | Marks an attached member whose restored values should be read from the model after a clone is broken. |
| `OriginalPositionX`, `OriginalPositionY` | Virtual-desktop position before cloning.                                                              |
| `OriginalIsPrimary`                      | Primary state before cloning.                                                                         |
| `OriginalWidth`, `OriginalHeight`        | Resolution before cloning.                                                                            |
| `OriginalFrequency`                      | Refresh rate before cloning.                                                                          |
| `OriginalRotation`                       | Rotation before cloning.                                                                              |
| `OriginalDpiScaling`                     | DPI scaling before cloning.                                                                           |
| `OriginalIsHdrEnabled`                   | HDR state before cloning.                                                                             |
| `OriginalIsWcgEnabled`                   | WCG state before cloning.                                                                             |
| `OriginalColorProfile`                   | Color profile filename before cloning.                                                                |

These values are copied through `GetDisplaySettings()` so they survive the control rebuild immediately following `CreateCloneGroup()`. They are not written to `.dpm` files; profiles saved while cloned therefore use the fallback path when the clone is later broken after reload. Saved profile data retains the ordinary per-display values needed for restoration after save and reopen — only the `Original*` state is editor-session-only.


#### Deferred Hardware Self-Healing

Migration is a one-time format upgrade. Native dimensions and EDID identity can remain incomplete when a display is unavailable during migration and are repaired when a later profile apply sees the same `TargetId` live:

- `NativeWidth == 0` / `NativeHeight == 0` — preferred/native display dimensions.
- Empty `ManufacturerName` / `ProductCodeID` — EDID panel identity.

When `ProfileApplyService.ApplyProfileAsync` applies a profile whose live display map includes one of these `TargetId`s, `ProfileHardwareReconciler` backfills the incomplete hardware information on the applied profile and other loaded profiles that reference the same `TargetId`; `ProfileService` persists only profiles that actually changed. The check is `TargetId`-centric because more than one profile can reference the same physical monitor/port. It runs only as part of an actual profile apply (and the existing boot-time migration pass), not on ordinary profile, tray, or editor reloads. No persistent pending-repair queue is used.

`ColorProfile == null` means "not applied" and is never treated as a repairable sentinel by this pass.

This is separate from schema migration: migration still runs once per profile to bump `SchemaVersion`, even when a particular display cannot be backfilled at that time.


#### Display-event engineering instrumentation

Display-event evidence is disabled by default. `DPM_DISPLAY_EVENT_EVIDENCE=apply-scoped` enables evidence only around profile applies; `DPM_DISPLAY_EVENT_EVIDENCE=app-lifetime` enables the application-lifetime display-event evidence stream. `DPM_DISPLAY_EVENT_TRACE=1` independently enables accepted raw-event ordinary DEBUG mirrors plus the existing first-ready and per-poll defer correlation.

CCD polling remains the sole readiness authority. TRACE adds logging only: it adds no CCD query, event wake, readiness input, cadence change, or dynamic membership. Deferred configuration still uses the one-time ALL_PATHS membership set and the same immediate-first probe, 250 ms cadence, and Error-31 retry behavior.

Normal apply-window evidence with correlation:

```powershell
$env:DPM_DISPLAY_EVENT_EVIDENCE = 'apply-scoped'
$env:DPM_DISPLAY_EVENT_TRACE = '1'
.\DisplayProfileManager.exe --dev
```

Full lifetime hardware-evidence mode:

```powershell
$env:DPM_DISPLAY_EVENT_EVIDENCE = 'app-lifetime'
$env:DPM_DISPLAY_EVENT_TRACE = '1'
.\DisplayProfileManager.exe --dev
```
