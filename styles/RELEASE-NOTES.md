# Release Notes Style

`RELEASE-NOTES.md` is the user-facing historical record of project releases. It explains meaningful changes for users without reproducing the technical history in `CHANGELOG.md`.

## Purpose

Prioritize changes that materially affect normal use.

Include major features, important bug fixes, meaningful compatibility changes, and substantial user-visible improvements.

Usually omit minor cleanup, small UI adjustments, internal refactors, routine dependency maintenance, and implementation details that do not affect normal use.

A minor change may still be included when it fixes a significant user-visible bug or is important enough to define the release.

## Release Structure

`RELEASE-NOTES.md` contains a current cumulative overview followed by version-specific release sections in descending version order.

The current cumulative overview is part of the current GitHub release. It is not merely repository documentation.

A release section uses a second-level heading whose title begins with its literal version:

```markdown
## 2.2.0 — Wallpaper, desktop context menu, HDR fixes
```

Do not force every release into the same hierarchy. Small maintenance releases may use one section; larger releases may use several product-surface sections.

### Current Release

The current release contains three source blocks, in this order:

1. The cumulative overview at the top of the file.
2. The version-specific section for the current tag.
3. The `Current Release Assets` block at the bottom of the file.

The release workflow combines those three blocks when publishing the current GitHub release.

When a new version is released, add its version-specific section above the previous release and update the cumulative overview at the top. Do not move the asset block; it remains pinned at the bottom.

### Historical Releases

Historical release sections contain their version-specific user-facing changes.

They normally do not contain cumulative-overview text or repeated download tables and requirements.

When a newer version is published, the release workflow downgrades the immediately previous published GitHub release to its version-specific section, preserving that release's own already-published assets.

## Release Titles

Release titles should identify the release's meaningful user-facing changes.

For the current GitHub release, the published release title uses the tag version followed by the current cumulative-overview heading. The version-specific section title is reserved for the historical release section.

The version-specific heading still identifies the historical section and is used to build that release's title when the release workflow downgrades it to historical form.

Prefer concrete subjects over generic labels such as "Misc features and bug fixes" when the release has identifiable themes.

Commit messages are historical evidence, not mandatory release titles. A commit message may provide the basis for a better user-facing title, but implementation-heavy commit wording should be rewritten for readers.

## Sections

Use headings to group related changes by product surface or user-facing area.

Useful section subjects include Display, Scripts, CLI, Themes, UI, Wallpaper, Audio, Integration, Reliability, Dependencies, and Build.

Do not create a section solely to hold one insignificant bullet when the release is otherwise small.

A section can have an emoji identifying its product surface.

## Emoji Conventions

Use emojis as section or major-surface markers, not as decoration on every individual bullet.

Examples:

- `🖥️` — display and monitor behavior
- `🔊` — audio
- `📜` — scripts
- `💻` — CLI
- `🎨` — themes
- `✨` — UI
- `🖼️` — wallpaper
- `🖱️` — desktop or shell integration
- `🛡️` — reliability or apply failure handling
- `📦` — dependencies or packaging
- `🧪` — tests
- `🧰` — build or project tooling
- `🎮` — DPM Shortcut Builder
- `🛠️` — DPM Theme Builder

Do not create a separate emoji for every bug-fix or commit category. Emojis identify the subject area, not whether a change is a `feat`, `fix`, or `refactor`.

## Bullet Style

Prefer:

`- **What changed** — what the user gains or what behavior changed.`

Use a short bolded lead-in followed by an em dash and the explanation.

The lead-in should identify the feature or change rather than merely repeat the section heading.

Avoid padded phrases that restate the same behavior in another form.

Do not create a separate bullet merely to describe implementation structure behind another user-visible change. Combine supporting detail into the primary change when it does not represent a distinct user-visible behavior.

## Ordering

Order changes roughly from largest and most user-visible to smaller supporting changes within a release or section.

Use logical grouping when closely related changes belong together.

Do not force every release to use the same subsystem ordering.

UI documentation should generally follow rendered control order when describing a UI surface sequentially. Release notes should prioritize user significance rather than mirror control layout.

## Technical Depth

Translate technical changes into their user-visible effect.

Prefer a description of what the user can now do or what behavior changed over an explanation of the underlying implementation.

Retain technical terms when they are meaningful to users of the feature, such as HDR, ACM, ICC/ICM, CLI, or Windows Spotlight.

Avoid internal class names, method names, registry paths, error codes, struct fields, API types, and implementation flags unless they materially explain a user-visible change.

## Attribution

When a release adapts or incorporates work from another contributor, describe the contribution accurately.

Do not imply that external fixes were copied verbatim when the project adapted or integrated them.

When attributing a contribution, put the attribution in the change or changes the contributor actually affected.

Explain what was adapted or changed rather than giving detached contributor credit without context.

## Historical Accuracy

Release notes preserve what was meaningfully changed in each release.

Do not move a feature into a later release merely because the later release rebuilt or corrected it.

Do not describe a feature as newly introduced when the release only fixed or extended an existing feature.

Historical release notes may retain wording that is no longer current when that wording accurately describes the release at the time.

## What to Omit

Do not turn release notes into a second changelog.

Usually omit:

- internal refactors without a meaningful user-visible effect
- minor cleanup
- trivial UI adjustments
- implementation-only details
- routine maintenance that does not meaningfully affect users
- repeated download tables and requirements from historical release sections

Include a cleanup or maintenance item when it is unusually significant for the release or fixes an important user-visible problem. A release that includes a deliberate broad cleanup or refactor pass may retain a concise cleanup entry even when the individual cleanup changes are not user-facing features.

## Release Boilerplate

Do not repeat the same cumulative introduction on every release.

The cumulative overview belongs at the top of `RELEASE-NOTES.md` and is included in the current GitHub release.

Historical releases do not need repeated cumulative-overview text.

The current release's download table and requirements are kept in one `Current Release Assets` block at the bottom of `RELEASE-NOTES.md` so adding a new release does not require moving historical content.

Historical release sections normally omit repeated download tables and other release-page packaging boilerplate.

Do not repeat a `For a full technical breakdown, see CHANGELOG.md` footer after every historical release. The relationship between the two documents can be stated once outside the individual release entries.

## Current Release Assets

Use a stable bottom-of-file heading:

```markdown
# Current Release Assets
```

The block may contain:

- the current release download table
- current release requirements
- other packaging text that the GitHub release workflow should publish with the current release

Asset filenames may use `{{VERSION}}`. The release workflow replaces that placeholder with the tag version.

Do not hardcode the current release version into asset filenames or requirements when `{{VERSION}}` can be used.

## Downgrading Previous Releases

`RELEASE-NOTES.md` is the source of truth for the current release's published prose. The single `release.yml` workflow both publishes the current release and downgrades the previous one in the same run.

The workflow publishes the current release by combining:

1. the cumulative overview,
2. the matching version-specific section, and
3. the `Current Release Assets` block.

After publishing, the workflow finds the immediately previous published semver release and, if it is still in current-release form, downgrades it to historical form by removing only its cumulative-overview prefix. It does not rebuild that release's body from `RELEASE-NOTES.md`: the previous release's own already-published version-specific section and its own already-published assets are preserved exactly as published, since the current `RELEASE-NOTES.md` asset block may already describe a newer release by the time the downgrade runs.

The downgrade is intentionally historical-only and touches only the immediately previous release, never older releases. It is idempotent: a release already in historical form, with only its version-specific section and no cumulative prefix, is left unchanged on a rerun.

## Language

Use concise, factual, user-facing language.

Avoid marketing exaggeration and self-congratulatory adjectives.

Prefer concrete statements such as "supports", "adds", "fixes", "restores", "allows", "preserves", and "shows" over vague claims such as "massively improves" or "dramatically enhances".

Do not use implementation jargon simply to make a release sound more technical.

## Consistency with Other Documentation

`RELEASE-NOTES.md` is the user-facing historical counterpart to `CHANGELOG.md`.

`CHANGELOG.md` may contain substantially more implementation detail, historical reasoning, error codes, internal terminology, and developer context.

When the two documents describe the same release, they should agree on the underlying facts while serving different audiences.

Shared terminology should remain consistent unless a deliberate user-facing simplification improves comprehension.