# Project guide

This file provides project guidance for human contributors and AI assistants working in this codebase.

## Project Overview

Display Profile Manager is a Windows desktop application for managing display profiles (resolution, refresh rate, rotation, DPI, HDR, color profile, wallpaper, audio devices, and scripts), with control through the main window, an overlay, the system tray, the desktop context menu, global hotkeys, and the command line. Built with C# (.NET 10) and WPF.

This is a fork by [exytral](https://github.com/exytral) based on [zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager).

## Maintaining This Guide

- **No hard wrapping** — keep each paragraph on one line. Fixed-column wrapping adds visual noise for human readers and causes unrelated lines to reflow when a paragraph is edited, making diffs harder to review.
- **Current and durable only** — keep this root guide limited to stable project-wide guidance and routing. Describe current behavior or structure only when it materially helps navigation; source remains authoritative for implementation details, while historical changes belong in `CHANGELOG.md`.
- **Avoid duplicated policy** — when a dedicated authority exists, link to it instead of restating its rules here.

## Build and Run Commands

```bash
# Build
cmd.exe //c "msbuild DisplayProfileManager.sln /p:Configuration=Debug"
cmd.exe //c "msbuild DisplayProfileManager.sln /p:Configuration=Release"

# Clean and Rebuild
cmd.exe //c "msbuild DisplayProfileManager.sln /t:Rebuild /p:Configuration=Debug"

# Run
cmd.exe //c "start DisplayProfileManager\bin\Debug\DisplayProfileManager.exe"

# Build for Specific Platforms (x86, x64, ARM64 supported)
cmd.exe //c "msbuild DisplayProfileManager.sln /p:Configuration=Release /p:Platform=x64"

# Dev Script (auto-discovers Visual Studio and, when -Platform is omitted, the current OS architecture)
powershell -File dev-build.ps1 [-Configuration Debug|Release] [-Platform x86|x64|ARM64]
```

## Development Guidelines

### Source Style

Source-code structure, comments, logging, tests, XAML, and supporting-format conventions are maintained in `styles/SOURCE.md`. Read that authority before changing source or source-adjacent comments.

### Changelog Relationship

`CHANGELOG.md` is the technical historical record for developers and contributors. It may contain implementation details, historical reasoning, API names, error codes, and lifecycle information that do not belong in this current-state guide or in user-facing release notes.

Detailed changelog-writing rules are maintained in `styles/CHANGELOG.md`.

## Architecture Quick Map

- `DisplayProfileManager/App.xaml.cs` is the manual composition root; the application does not use a DI container or process-global profile/settings/script manager singletons.
- `DisplayProfileManager/Application/` owns cross-capability command, single-instance, editor, settings, and shell use-case coordination.
- `DisplayProfileManager/Profiles/`, `Displays/`, `Wallpapers/`, `Audio/`, `Scripts/`, `Settings/`, and `Hotkeys/` own their capability models, policies, services, persistence, and native boundaries as appropriate.
- `DisplayProfileManager/Integration/` owns auto-start, update, IPC, shell registration, and window activation.
- `DisplayProfileManager/Common/` contains only genuinely cross-cutting utilities.
- `DisplayProfileManager/UI/` owns WPF windows, controls, view models, presentation helpers, themes, tray behavior, icons, and UI-specific interop.
- `DisplayProfileManager.ShellExt/` is the native Explorer COM extension; `DisplayProfileManager.ShellExt.Tests/` is its separate native test executable.
- `DisplayProfileManager.Tests/` contains managed MSTest coverage.
- `DPMBuilder/` is a separate Python tooling boundary.
- Capability-local folders are deliberate; avoid generic top-level `Models/`, `Services/`, `Repositories/`, `Interfaces/`, `Platform/`, and `Infrastructure/` buckets.
- The technical C# root namespace is `DisplayProfileManager`; capability-aligned namespaces follow the current source structure.

## Compatibility and Release Guardrails

Preserve compatibility identities unless the active task explicitly authorizes changing them: `DisplayProfileManager` and `DisplayProfileManager.Tests` assembly/root namespaces, `DisplayProfileManager.App` XAML identity, `InternalsVisibleTo("DisplayProfileManager.Tests")`, executable/DLL/product names, AppData locations and persisted schemas, mutex/show-event/pipe names, CLI flags/exit codes, registry/task/shell/COM identities, ShellExt identity, installer AppIds/architecture naming, theme resource identities, and update/release semantics.

Managed output layout is load-bearing: explicit project output paths keep binaries under `bin\<Platform>\<Configuration>\`, and ShellExt, installer, candidate packaging, and release automation depend on that arrangement. Do not redesign it casually.
