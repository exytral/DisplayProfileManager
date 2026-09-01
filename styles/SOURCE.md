# Source Style

Canonical source-code style reference for the project. These rules describe recurring source conventions established by the reviewed codebase; incidental variation remains acceptable unless a rule below establishes a clear reason to change it.

## General Source Structure

- Prefer source organization that makes the type or file easy to scan.
- Keep simple types and small files compact.
- Group related members or declarations when the relationship is meaningful and recurring.
- Keep declarations close to the members that implement or expose the same responsibility.
- Treat technical declaration groups, responsibility groups, and access-oriented groups as distinct structural purposes.
- Structural markers should reduce navigation cost rather than add ceremony.
- Existing variation is evidence rather than an automatic defect. Mechanical normalization belongs to the final conformance pass.
- Generated source is a separate ownership category and is not evidence for hand-maintained source style.
- Supporting formats should use their native organizational mechanisms rather than being forced into C# conventions.

## Members and Ordering

- Type-level constants, static state, and instance state commonly appear before constructors and operational members.
- Constructors generally precede operational methods when an explicit constructor is needed.
- Public properties, events, and API members may appear before implementation details when that improves discoverability.
- Related declarations should remain together when they form a meaningful domain group.
- Methods with materially different responsibilities should be grouped by responsibility rather than alphabetically or by access modifier.
- Private helpers may remain adjacent to the responsibility they support or in a final implementation group.
- Nested types may be placed with foundational declarations, technical interop declarations, a file-level type region, or remain unsectioned when the structure is already clear.
- Do not infer a universal access-modifier-first or member-kind-first sequence from a single file.
- WPF code-behind favors lifecycle and UI responsibility order.
- Test fixtures favor behavioral progression.
- Test builders use conceptual property order and place `Build()` last.
- Native source uses responsibility-oriented declaration and implementation order.
- Python tooling uses semantic module organization.

### Authoritative ordering within a stated structure

When a table or ordered list intentionally represents a specific structure:

- Explicit model-structure tables should follow authoritative model declaration order.
- Persisted structures should follow serialized member order only when serialization order itself is being documented.
- User-facing UI descriptions should follow actual presentation order.
- Execution and pipeline descriptions should follow actual causal execution order.

This does not establish universal source-member ordering. Logical capability lists, examples, inventories, and domain groupings may use an order chosen for clarity.

## Control Flow and Braces

- `try`, `catch`, and `finally` always use braces.
- Conditional and loop bodies use braces when they contain meaningful work or a meaningful branch result.
- A single-line `return`, `break`, or `continue` may remain unbraced only when it functions as a trivial early escape from the current control flow.
- Other single-statement conditional and loop bodies use braces.
- Meaningful returns use a braced conditional branch when the return represents the result of that branch rather than merely terminating the method early.
- Early-escape returns may remain on the same line as their controlling condition when that compact form is consistent with the surrounding method.
- Returns that conclude meaningful work or establish the method's resulting value remain visually separated from preceding work according to the local method structure.
- The distinction between an early escape and a meaningful return is semantic and follows the established style of the surrounding file or type rather than the returned value itself.

## C# Identifier Naming
- Methods, properties, events, types, and constants use **PascalCase**.
- Local variables and parameters use **camelCase**.
- Private non-constant fields use the underscore-prefixed camelCase convention.
- Existing identifiers should only be changed when they actually violate the applicable convention.

## Properties and Model Organization

- Model classes may group properties by domain concept.
- Persisted properties should remain in a coherent domain order when that order reflects the model structure.
- Runtime-only state may be grouped separately when the distinction improves navigation.
- Use short structural comments for logical property categories only when they materially improve navigation.
- Do not require category comments or a fixed property sequence for every model.
- Serialization attributes belong directly with the property they configure.

## Regions and Structural Grouping

- Regions are optional.
- Small or straightforward types should generally remain unsectioned.
- Larger classes may use regions for clearly separable responsibilities, lifecycle or API groupings, technical declarations, or other boundaries that materially improve navigation.
- Region names should describe the actual structural purpose.
- Do not introduce a region solely because another class uses the same name.
- Do not create empty, one-member, or purely cosmetic regions.
- A private helper may remain outside an access-oriented region when adjacency is clearer.
- Technical interop declarations may be grouped separately from runtime operations when that materially reduces navigation cost.
- Technical interop groups may be organized by mechanism or by coherent native subsystem.
- When a technical declaration block uses separate mechanism-oriented groups, the general ordering tendency is **P/Invoke → Enums → Structures → Constants**.
- That ordering is a group-local tendency, not a universal file template and not a requirement that every interop-heavy file use all four groups.
- A coherent subsystem-oriented interop region may contain both declarations and the methods that directly implement or support that subsystem.
- Multiple substantial types in one file may use file-level type regions when the grouping materially improves navigation.
- Test fixtures generally remain unregioned unless a genuinely separable behavioral group benefits from a region.
- Native source files do not need region directives when declaration and implementation order already provides navigation.
- Supporting formats use their native structural mechanisms.

### Region decision standard

Structure should follow responsibility and reduce navigation cost. Legitimate forms include compact unsectioned files, file-level regions for multiple substantial types, responsibility-oriented regions, access or API-oriented regions, technical declaration regions, coherent subsystem interop regions, and narrow interop regions in UI code-behind.

No universal region template is required.

## Comments

- Prefer no comment when names and structure are sufficient.
- Use comments for rationale, invariants, compatibility, ownership, sequencing, or context that cannot be inferred reliably from source.
- Keep comments concise and direct.
- Prefer single-line comments for local annotations.
- Avoid comments that merely restate a name, call, condition, or obvious assignment.
- Structural comments are appropriate when they materially improve navigation through dense declarations, methods, resources, configuration, or generated/template boundaries.
- Short category markers may label dense declaration groups when the category is meaningful and stable.
- A top-level comment may label a contiguous declaration group; it is not permission to place a heading above an individual method merely to name it.
- For complex methods, place rationale near the line or block it explains.
- Do not repeat the same constraint in multiple nearby comments.
- Comments should not address the reader directly.
- Comments that merely duplicate adjacent logging or status text should generally be removed.
- Generated-source ownership notices remain structural metadata.

## Logging

- Declare the logger with other class static state, normally near the top of the type.
- Use `LoggerHelper.GetLogger()`.
- Keep logging close to the operation or decision it describes.
- Prefer concise developer-facing messages with useful context.
- Use the appropriate log level.
- Do not add comments merely to repeat a log message.

## Core Layer

Core files vary in size and responsibility.

### Small Core types

Small models, parsers, value types, and utility classes may remain unsectioned.

### Larger Core managers and services

Large managers and services may use functional or API-oriented regions when those groups materially improve navigation. Exact sequencing remains responsibility-driven.

### Settings and configuration types

Configuration models may group properties by application concern. Settings-management classes may separate public mutations from accessors when the API is large enough to benefit from that division.

## Helpers Layer

### Compact helpers

Small stateless or narrowly scoped helpers may remain unsectioned.

### Technical interop helpers

Interop-heavy helpers may separate technical declarations from runtime operations when the grouping improves navigation. Mechanism-oriented and subsystem-oriented structures are both valid.

### Access-oriented and hybrid helper regions

Helpers may use access-oriented regions such as `Private Methods` and `Public Methods` when the class has substantial technical declaration front matter. Such regions are organizational boundaries, not literal inventories of every member matching the label.

## WPF Code-Behind

WPF windows and controls follow responsibility-oriented source principles while adding UI-specific lifecycle and event-handler structure.

- Keep logger and instance state near the top, followed by the constructor.
- Initialization and setup commonly follow the constructor.
- Group lifecycle methods, event handlers, and feature helpers by UI responsibility when that improves navigation.
- Public methods used by `App` or another window may remain adjacent to the feature they expose.
- Event handlers may remain unregioned in medium-sized windows and controls.
- A technical subsystem such as native window-message handling may use a dedicated region containing its declarations and implementing methods.
- Do not introduce responsibility regions solely because a code-behind file is large.
- Lifecycle overrides may appear near the operations they support or near the end of the type.
- UI helper methods may remain adjacent to the handlers they support.
- Shared UI-opacity constants should be used in C# when an opacity value represents a recurring application-state semantic such as blocked or inactive; control-template, presentation-hierarchy, effect-specific, and one-off visual opacities should remain local to their visual layer.
- A repeated local presentation value may use a local named constant when it represents one coherent visual semantic within that responsibility; it does not need to become a shared application-state abstraction merely because it recurs.
- Lower local opacity may establish a deliberate information hierarchy within a view; that hierarchy remains local rather than becoming a shared application-state opacity.

## View Models and Converters

- Small view models and converters generally remain unregioned.
- View models may place backing fields and the constructor first, followed by model projections, mutable UI state, notification events, and notification helpers.
- Converter classes should remain compact.

## XAML

- Place local resources before the visual tree.
- Group resources by functional UI role rather than alphabetically.
- Organize the visual tree by layout and user-facing responsibility.
- Use short structural comments for substantial visual sections.
- Keep repeated controls and nested templates ordered according to visual or component hierarchy.
- Event-handler attributes remain with the control declaration they serve.
- Preserve intentional local overrides of shared styles.

### Theme resource dictionaries

Packaged color dictionaries use a stable semantic order:

1. Base Colors
2. System Accent
3. Window Backgrounds
4. Content & Control Backgrounds
5. Borders & Separators
6. Interaction States
7. Primary Button (Accent)
8. Secondary Button
9. Status Buttons
10. Title Bar Extras
11. Text Brushes
12. Tooltips
13. Effects

Related dictionaries should preserve this category order where the same resource families exist. Resource keys do not need alphabetical ordering.

`Base.xaml` groups shared control styles by UI control or function. Exact style ordering is functional rather than alphabetical.

## Tests

- Test fixtures use `[TestClass]`.
- Test methods use `[TestMethod]` and `[TestCategory("Unit")]`.
- `Unit` is the only project-wide category currently established.
- There is no project-wide parameterized or data-driven convention.
- Related test classes may share a source file when they form a coherent subject family.
- Test methods are ordered by responsibility and behavioral progression rather than alphabetically.
- The dominant naming pattern is `Subject_Condition_ExpectedResult`.
- Very small tests may use shorter names where the containing class supplies an unambiguous subject.
- Arrange / Act / Assert should remain clear when a test contains distinct phases, without requiring literal comments.
- Reusable fixture builders normally appear before the tests that use them.
- Builders are top-level `internal sealed` types, keep the subject-under-construction field near the top, expose fluent configuration methods, and place `Build()` last.
- Builder method order is conceptual rather than alphabetical.
- Unit tests do not use file I/O, registry access, P/Invoke, or live display hardware.
- Reflection and controlled in-memory singleton manipulation are acceptable when required to isolate pure behavior.
- Test comments should explain regression intent or unusual platform behavior rather than restate assertions.

## ShellExt C++

- Keep small native files compact and unsectioned.
- Group COM interface declarations with the interfaces they implement.
- Keep primary COM lifetime and interface methods together before private helpers.
- Keep secondary COM types together when they form a distinct implementation unit.
- Native source does not need C#-style regions.
- Comments explain security boundaries, compatibility, sequencing, ownership, or native API constraints.
- Native interop declarations may be grouped through declaration order rather than region directives.

## Miscellaneous and Supporting Source

Supporting source formats use their native organization.

### Python tooling

- Keep module structure semantic.
- Keep imports together.
- Group constants and resources semantically.
- Place deterministic parsing, transformation, and validation helpers before UI orchestration where practical.
- Use concise semantic section comments in large modules.
- Use leading underscores for private state and methods.
- Do not introduce artificial C#-style regions.
- Preserve intentional generated templates and embedded data.
- Behavioral anomalies are not automatically style evidence.

### MSBuild

Use semantic declarative grouping and preserve load-bearing output-path, platform, reference, and packaging relationships.

### Visual Studio solution metadata

Preserve projects, solution folders, `SolutionItems`, mappings, nesting, and global metadata as native solution structure.

### Assembly metadata

Keep using directives first, group assembly attributes by semantic purpose where useful, and keep version attributes together.

### Generated C#

Generated files should not be manually normalized during style cleanup. Changes originate from their inputs or generators.

### PowerShell

Use native staged procedural structure with concise comments around meaningful stages and non-obvious Windows or process behavior.

### Inno Setup

Preserve native section order and packaging relationships. The established section order is common constants, version constants/fallback, target architecture, architecture-specific constants, `[Setup]`, `[Languages]`, `[Tasks]`, `[InstallDelete]`, `[Files]`, `[Icons]`, `[Run]`, `[UninstallRun]`, `[Code]`.

### Application manifest

Preserve XML hierarchy and schema requirements. Do not alphabetize semantic XML.

### NLog configuration

Preserve root attributes → variables → targets → rules.

### Repository ignore configuration

`.gitignore` is a repository-policy file organized by functional categories. Keep related patterns together and do not alphabetize patterns merely for appearance.

## Cross-Layer Reconciliation

- Responsibility-driven grouping is the strongest common convention.
- Regions are optional and should reduce navigation cost.
- Access-oriented regions are organizational, not literal partitions.
- Technical interop declarations may be grouped narrowly or by coherent subsystem.
- The general ordering tendency within a mechanism-oriented technical declaration group is P/Invoke → Enums → Structures → Constants, but the group itself may appear wherever the file's responsibility-oriented organization requires.
- Model properties may use semantic category comments without requiring category regions.
- UI code-behind favors runtime responsibility order.
- Tests favor behavioral progression.
- Native ShellExt uses declaration and implementation order.
- XAML follows visual hierarchy and semantic resource ordering.
- Python uses semantic module sections.
- Supporting formats use their native section and group mechanisms.
- Generated source is excluded from hand-authored style inference.
- No universal declaration-before-method, public-before-private, or region-name template is supported across every layer.

## Final Conformance Pass

The final source-style cleanup is a separate pass after the style standard has been established.

It should:

1. apply the finalized rules to the applicable source tree;
2. identify only confirmed violations of applicable rules;
3. patch only changes the rules actually require;
4. preserve intentional layer- and type-specific variation;
5. exclude generated source from manual normalization;
6. keep correctness fixes separate from style-only changes;
7. re-check the resulting tree against the rules after the patch.

The pass should not treat every difference between files as a violation.