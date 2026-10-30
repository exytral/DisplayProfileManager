# Contributing

Contributions to Display Profile Manager should be focused, reviewable, and supported by evidence appropriate to the change.

## Before making changes

- Keep each contribution scoped to one coherent feature set, bug-fix set, or related change set. Avoid unrelated formatting, cleanup, or refactoring.
- Read `AGENTS.md` for the current project architecture, build guidance, and working conventions.
- Follow `styles/SOURCE.md` for source changes and `styles/GENERAL.md` for documentation.
- When changing release history or release-facing documentation, also follow `styles/CHANGELOG.md` and `styles/RELEASE-NOTES.md`.
- Preserve established behavior unless the change intentionally corrects or replaces it and the new behavior is clearly explained.

## Tests and validation

Add deterministic regression coverage when practical for corrected or newly introduced behavior.

Run the tests and builds relevant to the changed surface and report what was actually validated in the pull request. Native Windows, display, shell, installer, or hardware-dependent changes may also require manual validation; describe that validation and any important limits rather than implying coverage that was not performed.

For native or hardware-dependent fixes, distinguish clearly between automated or simulated coverage, no-effect validation against the real native boundary, and state-changing/runtime validation on real systems.

Describe the exact failing scenario and environment when relevant, representative before/after behavior or native error/log evidence, what was actually exercised, and useful negative or positive controls when practical. If patched physical/runtime validation has not been performed, say so explicitly rather than letting green CI imply it.

Do not treat an earlier successful build or test run as validation of a later changed tree when the changed surface can affect that evidence.

## Changelog and documentation

Update `CHANGELOG.md` for notable behavior, compatibility, dependency, packaging, or developer-facing changes when practical. Describe the behavioral change first and include implementation detail when it materially explains the correction.

Final `RELEASE-NOTES.md` wording may remain maintainer-owned unless the contribution is explicitly release-scoped. Contributors should not assume they must author final release prose for every notable change.

Current-state documentation should describe the behavior that the current source implements. Historical changes belong in the changelog rather than current architecture documentation.

## Pull requests

Treat the pull-request description as a compact review record:

- explain what changed and why;
- make the behavioral delta clear, using `Previously` / `Now` wording when it helps;
- identify the important source, test, documentation, or packaging surfaces involved;
- report the tests, builds, and manual validation actually performed;
- call out known limitations, deferred work, or follow-up scope when relevant.

For bugs or compatibility fixes, separate **observation** from **diagnosis**: state what was reproduced, measured, or logged independently from the proposed cause, workaround, or implementation. Useful reproduction evidence remains valuable even if the maintainer ultimately accepts a different explanation or correction.

When a maintainer materially reworks an external contribution before integration, the integrating pull request or release discussion should distinguish what evidence or design was retained, what implementation changed, and why. Routine mechanical edits do not need a separate adaptation note.

For larger pull requests, use a changelog-like breakdown by subsystem or responsibility so reviewers can understand the change without reconstructing it from the commit history. The repository pull-request template is recommended because it keeps review evidence easy to scan, but equivalent structure and section names are fine; conventional-title formatting is not required.

Pull-request prose is repository-facing and should stand on its own:

- follow `styles/GENERAL.md` for project terminology and documentation style;
- describe the submitted change, not internal release or coordination mechanics;
- report validation outcomes and meaningful limits rather than the infrastructure or coordination path used to obtain them.

## AI assistance

AI-assisted contributions are accepted. Disclose material AI assistance in the pull request when an AI tool substantially contributed to design, implementation, debugging, review, test work, analysis, documentation, or other relevant project work.

The disclosure should briefly identify:

- the tool or model family when known;
- the substantive role AI played in the contribution;
- what the contributor personally reviewed or validated before submission.

The pull request must stand on its own. Do not request or expect AI session or transcript links, and never make review, acceptance, attribution, or validation depend on them. If a contributor voluntarily includes such a link and it is accessible, treat it only as incidental supplementary context.

The contributor remains responsible for the submitted changes, their accuracy, licensing, and the validation reported in the pull request.

## Third-party material

Do not add code, assets, generated output, or other material whose license is incompatible with the repository. Preserve required attribution and license notices for redistributed dependencies or adapted work.
