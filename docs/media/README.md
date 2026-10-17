# Main-window showcase media

The source of truth for the showcase animation is `showcase-motion.json`. The standalone, offline `showcase-motion-tuner.html` edits and exports the same versioned configuration; it starts with an empty sequence and offers an explicit **Use bundled PNGs** action. Use its frame-by-frame controls to inspect the result before exporting.

The owner-accepted settings are 60 FPS, a 2-second slot per image, a 0.75-second transition, `cubic-bezier(0.4, 0, 0.2, 1)`, and a bottom-up expanding wipe with its next image centered inside the growing window. The four PNGs under `main-window/` are the explicitly accepted source sequence. The eight-second loop is generated without OBS, browser capture, video conversion, or screen recording.

From the repository root, run:

```powershell
.\docs\media\Generate-MainWindowShowcase.ps1
```

To test without replacing the public documentation image:

```powershell
.\docs\media\Generate-MainWindowShowcase.ps1 -ConfigPath .\docs\media\showcase-motion.json -OutputPath "$env:TEMP\dpm-showcase-test.webp"
```

The generator reads all timing, easing, wipe, source, and incoming/outgoing page-motion settings from the JSON file, and writes `docs/img/main-window.webp` by default. Input source paths in the configuration resolve relative to this directory. Source images must have identical dimensions. Requires PowerShell 7+ on Windows with System.Drawing and ImageMagick (`magick`) on PATH.

`showcase-reference.html` preserves the owner's original CSS wipe example with the accepted 750 ms transition for independent geometry comparison. It is a reference, not the source of truth for the final generator.

**Export note:** The tuner can use file-picker sources immediately. For durable generation, save the PNGs under `docs/media/main-window/` and ensure the exported paths match the actual filenames. The optional bundled-source action is intentionally separate from startup. Some browsers restrict saving a PNG from images loaded through `file://`; choosing the images through the picker avoids that restriction. Appearance settings and reduced-motion UI polish are editor-only and do not change the generated frames.
