# General Documentation Style

Shared writing and formatting rules for project documentation. Document-specific rules live in the corresponding style file.

## Prose

- Prefer concise, direct sentences.
- Avoid filler, repetition, vague qualifiers, and unnecessary marketing language.
- Do not make unsupported claims.
- Avoid ambiguous pronouns when more than one referent is possible.
- Say each constraint once unless repeating it serves a different audience or preserves important context.
- Preserve established project terminology and user-facing labels.

## Formatting

- Use headings to improve navigation, not merely to add structure.
- Prefer a bold lead-in followed by an em dash for explanatory bullets where the document type calls for it.
- Use Markdown consistently with the surrounding document.
- Keep examples representative of supported current behavior when documenting current functionality.

## Terminology

- Use `DPM` only for established proper nouns such as DPM Theme Builder, DPM Shortcut Builder, and `DPM_IpcPipe`.
- Use established application terminology consistently across documentation.
- When application terminology differs intentionally from Windows terminology, make the distinction explicit.

## Documentation Boundaries

- `CHANGELOG.md` records technical history.
- `RELEASE-NOTES.md` records user-facing release history.
- User guides document current behavior and usage.
- Source-code comment conventions are defined separately in `styles/SOURCE.md`.