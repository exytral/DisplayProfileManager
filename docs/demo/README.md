# Reusable sample display profiles

These are **synthetic reference examples** for Display Profile Manager, not exports of a user's connected devices. Both examples use fixed demonstration GUIDs.

- **Desk Setup.dpm** — dual 2560 × 1440 displays arranged side-by-side at 240 Hz; everyday desktop example.
- **TV Setup.dpm** — one synthetic 3840 × 2160 HDR display at 120 Hz and 150% scaling; games/media example.

## Load samples through file management

Load these samples through the managed profile folder rather than an Import or Export button. Their synthetic displays are **not** ready-to-apply presets for real hardware.

1. Download [Desk Setup](profiles/00000000-0000-4000-8000-000000000001.dpm) and/or [TV Setup](profiles/00000000-0000-4000-8000-000000000002.dpm), making sure the downloaded filenames remain the exact GUID filenames shown in the links. Some browsers may rename files; check before copying.
2. In the main window, choose **Open Folder** to open the application's data folder, then open its **Profiles** subfolder.
3. **Back up the existing Profiles folder** first. Confirm the destination does not already contain a file with either of those GUID names. **Never overwrite an existing profile.** If a fixed sample ID collides, skip that sample unless you deliberately create a correctly remapped independent copy using a supported workflow; changing a filename alone is insufficient because its internal ID must match.
4. Copy the downloaded .dpm files into Profiles, preserving their original GUID filenames. Use **Refresh** in the main window (or restart Display Profile Manager) to load them.
5. Select the sample and **inspect** its settings and saved-layout preview. Its fictitious DPM-DEMO-* manufacturer/EDID, targets, and monitor ports will not correspond to actual hardware; applying it unchanged can fail, have no effect, or change display settings unpredictably. Do not apply the samples unchanged.

For a useful real configuration, choose **Create/Edit** and adapt settings to your actual detected displays, resolution, refresh rate, scaling and HDR capabilities. Do not manually edit the live profile-store JSON as an ordinary import procedure.

Both files contain an `id` GUID, `name`, `description`, `displaySettings`, and subsystem settings. To keep examples portable and non-effecting by default, **wallpaper, audio routing, scripts and hotkeys are disabled**. No local image paths, icon assets, credentials, audio endpoint IDs, executable scripts, or external dependencies are required. The artwork/screenshots are not embedded assets. No third-party sample content or attribution is required; project repository licensing applies.

These examples are reference starting points rather than universal or auto-configuring display presets. Recreate settings for your own displays, then review the saved-layout preview and editor before using **Apply**.
