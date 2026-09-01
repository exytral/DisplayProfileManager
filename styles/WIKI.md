# Wiki Style

The wiki documents current DPM behavior for users. It is intentionally lighter than the source and release-document styles: pages should be clear and consistent without forcing every page into a rigid template.

## Purpose and scope

- Describe current, supported behavior and practical usage.
- Prefer information that helps a user understand what DPM does and how to use it.
- Keep implementation details only when they explain an observable behavior, limitation, compatibility boundary, or troubleshooting step.
- Do not use the wiki as the historical record for release changes; use `CHANGELOG.md` for technical history and `RELEASE-NOTES.md` for release communication.

## Prose

- Prefer direct, concise sentences and concrete verbs.
- Use established DPM terminology and user-facing labels consistently.
- Explain prerequisites, limitations, and consequences where they matter to successful use.
- Avoid marketing language, filler, and claims that cannot be supported by the current implementation.
- Write for a technically capable user without assuming familiarity with DPM internals.
- Prefer examples that can be copied or followed directly.

## Structure

- Give each page a descriptive H1.
- Use H2 for the main topics of a page and H3 only when a subsection materially improves navigation.
- Start with a short orientation paragraph when the page covers more than one task.
- Use tables for compact reference material and bullets for independent options or behaviors.
- Use numbered steps for procedures whose order matters.
- Keep closely related behavior on the same page rather than creating tiny pages for individual controls.

## Links and examples

- Link to another wiki page when it contains the detailed explanation rather than duplicating that explanation.
- Link to an exact subsection when a page has a stable, useful anchor.
- Keep command examples, paths, file extensions, setting names, and UI labels in code or bold formatting appropriate to their role.
- Examples must match currently supported syntax and behavior.
- Avoid examples that depend on obsolete commands, retired features, historical UI, or release-specific behavior.

## Images

- Use screenshots when they clarify the current UI or a procedural step.
- Keep screenshot captions or surrounding prose focused on what the reader should notice.
- Replace screenshots when UI changes make them materially misleading.

## Current-state discipline

- Wiki pages describe the current application behavior.
- Treat the current implementation and current UI as the authority for behavior, labels, settings, and supported workflows.
- Do not describe former implementations, migrations, or release-specific changes as current behavior.
- When a compatibility boundary is user-relevant, state the boundary directly without turning the page into a historical changelog entry.
- Do not preserve stale wording merely because it appeared in an older wiki page; rewrite the affected passage when current behavior has changed.