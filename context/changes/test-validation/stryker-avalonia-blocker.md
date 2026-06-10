# Blocker: Stryker.NET cannot mutate the single-project Avalonia layout

**Date**: 2026-06-11
**Status**: Phase 1 halted — re-plan required (decision: extract pure logic to a non-Avalonia class library, e.g. `Notes.Core`)
**Stryker**: dotnet-stryker 4.14.2 · **Avalonia** 12.0.3 · **net10.0**

## What was attempted

Phase 1 of `plan.md`: stand up `stryker-config.json` scoped to the 8–11 template +
file-safety logic files and take a raw baseline. The committed config used:
`test-runner: mtp`, `**/`-anchored `mutate` allow-list (Services + `ViewModels/Fields/*`
+ `TemplateFormViewModel`), `reporters: [html,json,cleartext]`, `concurrency: 4`.

## What worked (don't re-litigate)

- **The MTP runner works.** The plan's headline risk is fully resolved: Stryker's
  native MTP runner (`test-runner: mtp`) attaches to the xUnit v3 + MTP suite and
  reports `Number of tests found: 273 ... Initial test run started.` No VSTest
  fallback needed.
- **The baseline (unmutated) compile succeeds** — Stryker reuses `dotnet build`
  output, which includes Avalonia's generated `InitializeComponent`.
- **`.slnx` auto-discovery is real (research §E.4 confirmed).** Running `dotnet
  stryker` from the **repo root** makes Stryker pick up `Notes.slnx` in solution
  mode ("Stryker will mutate solution Notes"), which **ignores the `project` /
  `test-projects` / `mutate` config** and mutates the whole solution. Running from
  **`Notes.Tests/`** (`dotnet stryker --config-file ../stryker-config.json`) drops
  to single-project mode ("Analyzing 1 test project(s)") — the correct run location.

## The blocker (why Phase 1 cannot complete as planned)

On the **first mutated recompile**, Stryker aborts:

```
[WRN] An unidentified mutation in .../Notes/MainWindow.axaml.cs resulted in a
      compile error (at 7:5) with id: CS0103, message: 'InitializeComponent'
      does not exist in the current context
[INF] Safe Mode! Stryker will remove all mutations in MainWindow ...
[FTL] Stryker.NET could not compile the project after mutation.
```

**Root cause:** Avalonia 11/12 generates `InitializeComponent` (and `x:Name` field
refs) via the built-in **Avalonia.NameGenerator** Roslyn source generator
(`build_property.AvaloniaNameGeneratorBehavior = InitializeComponent`, confirmed in
`Notes/obj/.../Notes.GeneratedMSBuildEditorConfig.editorconfig`). Stryker's
**in-memory mutated recompile does not re-run / preserve that generator's output**,
so every `.axaml.cs` code-behind that calls `InitializeComponent()` fails to compile
(`MainWindow`, and every View).

**This is independent of `mutate` scope.** Narrowing the allow-list to pure logic
files does not help: the *entire* `Notes` project must compile for *any* mutant, and
the UI code-behind is part of that project. The "unidentified mutation in
MainWindow.axaml.cs" message is collateral from the missing generated code, not a
real mutant there.

## Research — no clean config-only fix on either side

- **Stryker tracker:** zero Avalonia issues — unsupported scenario.
- **Avalonia [#11050](https://github.com/AvaloniaUI/Avalonia/issues/11050)** ("save
  source generator output to source control") — **closed as *not planned***. No
  supported way to persist the generated `InitializeComponent` as a real file.
- **`EmitCompilerGeneratedFiles=true`** emits `.g.cs` but does not add it to the
  `Compile` set; re-including it duplicates the still-running generator's output.
- **[Avalonia.NameGenerator README](https://github.com/AvaloniaUI/Avalonia.NameGenerator/blob/main/README.md):**
  the only "bring your own `InitializeComponent`" path is
  `AvaloniaNameGeneratorBehavior=OnlyProperties` + hand-writing `InitializeComponent`
  (`AvaloniaXamlLoader.Load(this)`) in **every** view — an invasive, fragile change
  to production UI code-behind, rejected.

## Decision

**Stop and re-plan** around extracting the pure logic under test into a **non-Avalonia
class library** (the standard pattern — UI projects aren't mutation-tested; the
research already notes the favourable pure/IO split). Candidate moves: the pure
services (`TemplateRenderer`, `TemplateParser`, `TemplateCatalog`, `NameValidator`,
`PathGuard`, `NoteFileService`, `NoteDeleter`, `NoteFolderService`,
`OrphanedTempCleaner`). The in-scope ViewModel files (`TemplateFormViewModel`,
`Fields/*`) need a feasibility check — they may carry Avalonia/CommunityToolkit
coupling that complicates the move, and may have to drop out of mutation scope.

## Reusable groundwork left on disk

- `stryker-config.json` (repo root) — runner + reporters + `**/`-glob scope are
  reusable; **`mutate` paths and run location must be re-pointed** at the new project
  once the extraction lands. Not currently runnable to completion.
- `.gitignore` — added `StrykerOutput/`.
