# Changelog Style

Style rules for `CHANGELOG.md`. These rules preserve the changelog's purpose as a technical historical record and distinguish historical release state from current-state documentation.

## Historical Attribution

- Every change belongs to the release where it actually occurred.
- Do not move an older change into a later release because the later release rebuilt or corrected it.
- Do not infer historical ownership from the current implementation.
- Preserve meaningful historical chains when a feature was introduced, corrected, rebuilt, replaced, retired, or removed.
- Keep intra-cycle churn out of the historical record when it was resolved before release.
- Use forward or backward references selectively when they materially clarify a historical relationship; do not reproduce the full later history inside every earlier entry.

## Depth

- `CHANGELOG.md` is a technical historical record for developers and contributors.
- Preserve implementation details, rationale, constraints, compatibility behavior, and release-specific context when they explain why a change mattered or distinguish one release from another.
- Preserve meaningful historical detail even when a later release supersedes the implementation; use a concise forward or backward node rather than rewriting the older release into the newer behavior.
- Do not shorten older releases merely to make them match newer releases. Density may legitimately differ when a release documents a major fork transition, subsystem rewrite, or other historically significant change.
- Consolidate only incidental cleanup, bookkeeping, and genuinely minor presentation changes.
- State the meaningful behavioral change first, then implementation detail or reason when useful.

## Structure

- Section names describe a subsystem or surface, never a single change.
- Within a release, order sections and entries by impact and logical flow.
- Do not force every release into the same section order.
- `feat` adds capability; `fix` corrects behavior; `refactor` restructures without intended functional change; `build` covers packaging and dependencies; `test` covers the suite; `misc` is the remainder.
- Complexity does not turn a fix into a feature.
- Meaningful UX or behavior changes may warrant their own entries; small visual consistency changes may be consolidated into a broader UI polish or cleanup entry.

## Bullet Style

- Use `-` for bullets.
- Use a short bolded lead-in, an em dash, then the explanation.
- The lead-in ends with an em dash, never a period or colon.
- Do not use em-dash-wrapped mid-sentence asides; use commas, parentheses, or a second sentence.
- Keep one coherent change per top-level bullet.
- Supporting details that qualify the same change belong with that change rather than becoming unrelated top-level entries.
- Keep punctuation consistent within a coherent release section.

## Historical Nodes

Use a historical node when a later release materially changes the fate, behavior, or implementation of an earlier documented change.

A node should:

- preserve the useful relationship between the releases;
- state what happened later when that matters to understanding the earlier entry;
- use a forward or backward reference when helpful;
- avoid reproducing the entire later implementation or history in the earlier release.

Choose wording according to the actual relationship rather than a fixed vocabulary. The terms `rewritten`, `rebuilt`, `resolved`, `retired`, `removed`, and `replaced` are available when they accurately describe the historical relationship, but they are not interchangeable labels or mandatory terminology.

## Technical Detail

- Preserve exact API names, field names, JSON names, error codes, and compatibility details when useful to developers.
- Native/API naming may retain the exact casing used by that API even when project identifiers follow project naming conventions.
- Performance measurements may be included when historically useful, but environment-dependent measurements should not be presented as universal guarantees.
- Do not replace technical history with marketing language.

## Historical Corrections

- Do not rewrite older entries merely because current behavior differs.
- Where an old entry is obsolete, preserve it when it establishes useful history and a later reference can explain the resolution.
- Remove an old bullet only when its historical value is trivial, duplicated, or misleading in a way that cannot be retained cleanly.
- A historical entry may document an earlier implementation's limitation and point to a later repair without implying that the earlier release had the later design.

## Current-State Boundary

- `CHANGELOG.md` records historical release state.
- `AGENTS.md` records current state.
- `SOURCE.md` and this file define documentation and source conventions, not release behavior.
- A current implementation detail belongs in `CHANGELOG.md` only when it is part of the historical record of a release.

## Scope of UI and Cleanup Entries

- Keep individually significant UI changes when they changed how users operate the application or corrected a meaningful usability problem.
- Consolidate small wording, spacing, color, shading, and consistency adjustments when they do not have independent historical significance.
- Dead-method deletion, file renaming, test-file reorganization, and similar bookkeeping generally do not warrant standalone bullets unless they document a meaningful architectural boundary.
- When a release contains many small cleanup changes, a concise cleanup or polish entry is preferable to a catalog.

## Version and Test Counts

- Release test counts must match the final test-suite state of the release being documented.
- Do not carry an intermediate working-tree count into a release entry.
- Do not infer a historical release count from a later suite state.
- Avoid unnecessary current-state test counts outside historical release entries.

## Terminology

- Use technical terms according to their actual meaning in the release being documented.
- Do not treat distinct technical states as interchangeable merely because their UI presentation is similar.
- Historical terminology may remain when it accurately describes the release being documented; later terminology should not be back-projected into earlier entries without evidence.

## Exclusions

- Do not create entries for cosmetic nitpicks or trivial UI changes unless they belong to a larger meaningful change.
- Do not add instructions to read another document as a prerequisite.
- Do not introduce claims unsupported by source history.