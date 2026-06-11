# Test-Effectiveness Validation (Mutation Testing) Implementation Plan

> **Re-planned 2026-06-11.** The first attempt died on a structural blocker:
> Stryker's mutated recompile must compile the *whole* `Notes` project, and
> Avalonia's `InitializeComponent` source-generator output does not survive that
> recompile (CS0103 on every `.axaml.cs`), independent of `mutate` scope. Full
> record in `stryker-avalonia-blocker.md`. This plan unblocks mutation by
> extracting the Avalonia-free logic into a new `Notes.Core` class library — a
> mutable compile target with no UI source generators — then runs the scoped
> mutation testing the original plan intended.

## Overview

Phase 3 of `context/foundation/test-plan.md`: prove the Phase 1–2 tests actually
kill regressions by running **mutation testing (Stryker.NET) scoped to the
template + file-safety logic**. The blocker forces a structural prerequisite —
extract that logic into a non-Avalonia `Notes.Core` library — so the plan now
has two halves: **(A) extract** `Notes.Core` + `Notes.Core.Tests` (re-namespaced
`Notes.Core.*`), then **(B) mutate** that library and close the known gaps. The
deliverable remains a committed, runnable, scoped mutation command + a measured
baseline score + cookbook §6.5 — **not** a CI gate (Phase 4 of the test-plan,
per §3/§5).

## Current State Analysis

- **The blocker is structural, not config.** `mutate` scoping controls *what is
  mutated*, not *what must compile*. The entire `Notes` project — including every
  Avalonia `.axaml.cs` whose `InitializeComponent` is source-generated — must
  recompile for any mutant, and that generator output is lost on Stryker's
  in-memory recompile (`stryker-avalonia-blocker.md`; Avalonia #11050 closed
  *not planned*). The only supported path is to give Stryker an Avalonia-free
  compile target.
- **The MTP runner already works** (don't re-litigate). Stryker's native MTP
  runner (`test-runner: mtp`) attaches to the xUnit v3 + MTP suite — 273 tests
  found, no VSTest fallback needed (`stryker-avalonia-blocker.md` §"What worked").
- **The logic is cleanly extractable** (research §C + coupling scan
  2026-06-11). All in-scope services are Avalonia-free; `Notes/Models/*`,
  `Notes/Messaging/Messages.cs`, and the service interfaces are plain
  POCOs/contracts. Of the broader service set, **only `AutoSaveScheduler` is
  Avalonia-coupled** (`Avalonia.Threading.DispatcherTimer`, UI-thread affinity) —
  it stays in `Notes`. `NoteSearchIndex`, `SettingsService`, `WorkspaceScanner`,
  `NoteMetadataParser` are Avalonia-free and move.
- **One unresolved risk drives the smoke gate.** The in-scope ViewModels
  (`TemplateFormViewModel`, `Fields/*`) use the `CommunityToolkit.Mvvm`
  `[ObservableProperty]`/`[RelayCommand]` **source generators** — the same *class*
  of thing (a source generator) that broke Stryker on Avalonia. Whether
  CommunityToolkit's generators survive Stryker's recompile is **unproven** and is
  the explicit gate in Phase 2.
- **Three known test gaps** will show as survivors until fixed (research §D):
  `NoteFolderService` has zero tests; `NoteFileServiceTests` BOM oracle
  re-derives bytes from the same `Encoding.UTF8` the SUT uses
  (`feedback-independent-test-oracle`); `TemplateCatalogTests` lacks a
  `.templatesX/` prefix-without-separator case.
- **A documented set of intentional/equivalent survivors** (research §F) *should*
  survive; they are pre-classified and excluded so the score floor isn't computed
  against noise.

## Desired End State

A new `Notes.Core` class library holds all Avalonia-free logic (re-namespaced
`Notes.Core.*`), `Notes` references it, and the app still builds, tests green, and
launches unchanged. A `Notes.Core.Tests` project (xUnit v3 + MTP, Avalonia-free)
holds the moved logic tests. A committed `stryker-config.json` scopes mutation to
the template + file-safety files **inside `Notes.Core`** and runs green via
`dotnet stryker` pointed at `Notes.Core.csproj`. The three gaps are closed
(verified by a score increase between raw baseline and post-fix re-run). The §F
intentional survivors are excluded via line-range negations. `thresholds.break`
is set just below the clean score. Cookbook §6.5 documents the procedure
(including the extraction rationale + safe run location); the test-plan status
table and `change.md` reflect reality.

**Verification:** `dotnet stryker` (run from `Notes.Core.Tests/`) exits 0 and
prints a score ≥ `break`; the HTML report shows only the scoped `Notes.Core`
files mutated and only the pre-classified intentional survivors outside the
killed set; `dotnet run --project Notes` launches and the template-form dialog
renders (proving the re-namespace didn't break compiled bindings).

### Key Discoveries:

- **`Notes.Core` has no reference back to `Notes`** — the moving types depend only
  on BCL, `System.IO.Abstractions`, Markdig, YamlDotNet, `CommunityToolkit.Mvvm`,
  and each other. One-way graph: `Notes` → `Notes.Core`. (Coupling scan
  2026-06-11.)
- **The test split makes the Stryker run safe.** `Notes.Core.Tests` references
  exactly one source project (`Notes.Core`), so running `dotnet stryker` from that
  directory gives unambiguous **single-project mode** — sidestepping the `.slnx`
  solution-mode trap that re-includes the Avalonia `Notes` project and
  reintroduces the blocker (`stryker-avalonia-blocker.md` §".slnx auto-discovery").
- **Re-namespacing ripples into AXAML, not just `.cs` usings.** Compiled bindings
  are on (`x:DataType`); any AXAML `clr-namespace:Notes.ViewModels;assembly=Notes`
  reference to a moved VM (the `TemplateFormDialog` view + its field
  `DataTemplate`s keyed on `Fields/*` VMs) must become
  `clr-namespace:Notes.Core.ViewModels;assembly=Notes.Core`. This is the riskiest
  part of the extraction and is gated by a "app launches + dialog renders" manual
  check.
- Stryker's `mutate` array supports **line-range negation**
  (`"!Notes.Core/Services/PathGuard.cs{27-29}"`) — each §F survivor is suppressed
  at its precise range (research §E.5).
- The MTP runner does not yet support per-test coverage filtering, so
  `coverage-analysis: perTest` is omitted; it still auto-skips zero-coverage
  mutants (research §E.1).
- `ThrowingFileSystem` (`Notes.Tests/Fakes/ThrowingFileSystem.cs`) and
  `InMemoryNoteFileService` already exist and move to `Notes.Core.Tests` with the
  tests that use them.

## What We're NOT Doing

- **No CI gate / GitHub Actions wiring** — that is the test-plan's Phase 4 (§5).
- **No moving `AutoSaveScheduler`** — its `DispatcherTimer` is UI-thread-bound;
  swapping the timer changes threading semantics and it isn't in mutation scope.
  It stays in `Notes`; abstract the timer only if a later change needs it there.
- **No moving the dialog services, shell VMs, or Views** (`ConfirmDialogService`,
  `NewNoteDialogService`, `TemplatePickerDialogService`,
  `TemplateFormDialogService`, `AvaloniaFolderPicker`, `MainWindowViewModel`,
  `NoteTreeViewModel`, `NoteEditorViewModel`, `NoteSearchViewModel`) — all UI-coupled.
- **No widening the mutation scope** beyond the template + file-safety files
  (research §C). Other moved services (`NoteSearchIndex`, `SettingsService`, etc.)
  live in `Notes.Core` for a clean boundary but are **not** in the `mutate`
  allow-list.
- **No switch to the VSTest runner** unless the preview MTP runner empirically
  fails — documented contingency only.
- **No chasing the §F intentional survivors** (research §F) — excluded by design.
- **No tool-manifest** — Stryker is already a global tool.

## Implementation Approach

Two halves across four phases. **Half A (Phase 1)** is a pure structural refactor
with its own build/test/launch gate and *no Stryker* — extract the Avalonia-free
logic into `Notes.Core` + `Notes.Core.Tests`, re-namespaced `Notes.Core.*`.
**Half B (Phases 2–4)** is the mutation work the original plan intended, now
pointed at `Notes.Core`:

2. Re-point `stryker-config.json` and take the **first run** = smoke proof + raw
   baseline + the **VM source-generator survival gate** (the open risk) + survivor
   inventory.
3. Close the three gaps and **re-run**; the score delta is the proof and the
   cookbook teaching example.
4. Exclude the confirmed §F survivors at line-range precision, **set the break
   floor** under the clean number, and document.

Splitting extraction (Phase 1) from the first Stryker run (Phase 2) isolates the
two distinct risks — re-namespace churn vs generator survival — to separate, fast
checkpoints, so a failure costs one thin phase rather than the whole plan.

## Critical Implementation Details

- **Build must be green before mutation.** Stryker's BuildAnalyzer aborts on
  compile errors (research §E.2, GH #3397). Run `dotnet build` first.
- **Run location is load-bearing.** Run `dotnet stryker` from `Notes.Core.Tests/`
  so Stryker auto-discovers the single referenced source project (`Notes.Core`) in
  single-project mode. **Never run from the repo root** — that triggers `.slnx`
  solution mode, which mutates the whole solution (including the Avalonia `Notes`
  project) and reproduces the original blocker. Set `project` /`test-projects`
  explicitly in the config as a belt-and-suspenders guard.
- **Re-namespace reaches AXAML.** With compiled bindings on, AXAML
  `clr-namespace`/`assembly` references to moved VMs (`TemplateFormDialog.axaml`
  and any field `DataTemplate`s keyed on `Fields/*` VMs) must be repointed to
  `Notes.Core`. A missed reference fails at runtime, not compile — hence the
  "app launches + dialog renders" manual gate in Phase 1.
- **VM source-generator gate (the open risk).** On the first Stryker run, confirm
  the in-scope VMs (`TemplateFormViewModel`, `Fields/*`) actually produced mutants
  and recompiled. If CS0103 on CommunityToolkit-generated members appears (the
  Avalonia failure mode, now from a different generator), the documented fallback
  is to drop `Notes.Core/ViewModels/**` from the `mutate` allow-list (the VMs keep
  their tests; they just aren't mutated) and record it in `baseline.md`.
- **Preview-runner contingency.** If `dotnet stryker --test-runner mtp` fails to
  execute the suite on net10, the fallback is to add `xunit.runner.visualstudio` +
  `Microsoft.NET.Test.Sdk` to `Notes.Core.Tests.csproj` and drop `test-runner`
  back to default VSTest (research §E.1). Last resort — record the reason.

## Phase 1: Extract Notes.Core + Notes.Core.Tests

### Overview

Create the `Notes.Core` class library and `Notes.Core.Tests` project, move all
Avalonia-free logic + its tests into them, re-namespace to `Notes.Core.*`, and
re-wire references — with **no Stryker** this phase. Gate: build green, both test
suites green, app launches and the template-form dialog renders.

### Changes Required:

#### 1. Notes.Core class library

**File**: `Notes.Core/Notes.Core.csproj` (new), added to `Notes.slnx`

**Intent**: A net10.0 class library that compiles the Avalonia-free logic with no
UI source generators — the mutable target Stryker needs.

**Contract**: `net10.0`, `Nullable=enable`, `OutputType` library (default). Package
references (versions matched to `Notes.csproj`): `System.IO.Abstractions` 22.1.1,
`YamlDotNet` 18.*, `Markdig` 1.2.0, `CommunityToolkit.Mvvm` 8.4.2. **No Avalonia
packages, no `Microsoft.Extensions.DependencyInjection`** (DI composition stays in
`Notes/Program.cs` per CLAUDE.md). No `ProjectReference`.

#### 2. Move Avalonia-free logic into Notes.Core (re-namespaced)

**Files**: move from `Notes/` → `Notes.Core/`, namespace `Notes.*` → `Notes.Core.*`:
- `Models/*` (all — plain records)
- `Messaging/Messages.cs`
- `Services/` Avalonia-free services + their interfaces: `TemplateRenderer`,
  `TemplateParser`, `TemplateCatalog`, `NameValidator`, `PathGuard`,
  `NoteFileService`, `NoteDeleter`, `NoteFolderService`, `OrphanedTempCleaner`,
  `PathContainmentException`, `NoteSearchIndex`, `SettingsService`,
  `WorkspaceScanner`, `NoteMetadataParser`, `NoteTreeBuilder`, and their `I*`
  interfaces
- `ViewModels/TemplateFormViewModel.cs` and `ViewModels/Fields/*`

**Intent**: Relocate the logic that can compile without Avalonia, preserving
directory structure under `Notes.Core/`.

**Contract**: Each moved type's namespace changes `Notes.X` → `Notes.Core.X`
(e.g. `Notes.Services` → `Notes.Core.Services`). `AutoSaveScheduler` + its
interface, all dialog services, shell VMs, `ViewModelLocator`, and Views **stay in
`Notes`**. After the move, `Notes.Core` must not reference any `Notes.*` (verify
no back-reference — it would be a circular dependency).

#### 3. Re-wire Notes to consume Notes.Core

**Files**: `Notes/Notes.csproj`, `Notes/Program.cs`, every `Notes/**/*.cs`
consuming a moved type, and AXAML referencing moved VMs.

**Intent**: Point the app at the extracted library and fix all references the
re-namespace touched.

**Contract**: Add `<ProjectReference Include="../Notes.Core/Notes.Core.csproj" />`
to `Notes.csproj`. Update `using` directives across `Notes` to the `Notes.Core.*`
namespaces (`Program.cs` DI registrations unchanged in shape — same
interface→impl pairs, now resolved from `Notes.Core`). Update AXAML
`clr-namespace`/`assembly` for moved VMs (`TemplateFormDialog.axaml` + field
`DataTemplate`s) to `assembly=Notes.Core`. Drop the now-duplicate
`CommunityToolkit.Mvvm` / `System.IO.Abstractions` etc. from `Notes.csproj` only
if they're no longer used directly by remaining `Notes` code (otherwise leave —
transitive via `Notes.Core` is also fine).

#### 4. Notes.Core.Tests project

**File**: `Notes.Core.Tests/Notes.Core.Tests.csproj` (new), added to `Notes.slnx`

**Intent**: An Avalonia-free xUnit v3 + MTP test project for the moved logic — the
project Stryker reruns.

**Contract**: Mirror `Notes.Tests.csproj`'s runner setup: `net10.0`,
`OutputType=Exe`, `IsTestProject=true`, `UseMicrosoftTestingPlatformRunner=true`,
`TestingPlatformDotnetTestSupport=true`, `IsPackable=false`. Packages: `xunit.v3`
3.2.2, `NSubstitute` 5.3.0, `System.IO.Abstractions.TestingHelpers` 22.1.1. **No
`Avalonia.Headless.XUnit`, no `TestApp`.** `ProjectReference` → `Notes.Core` only.

#### 5. Move the logic tests into Notes.Core.Tests (re-namespaced)

**Files**: move from `Notes.Tests/` → `Notes.Core.Tests/`: the test files whose
SUT moved to `Notes.Core` — `TemplateRendererTests`, `TemplateParserTests`,
`TemplateCatalogTests`, `NameValidatorTests`, `PathGuardTests`,
`NoteFileServiceTests`, `NoteDeleterTests`, `OrphanedTempCleanerTests`,
`FieldVmTests`, `TemplateFormViewModelTests`, plus `Fakes/ThrowingFileSystem.cs`
and `Fakes/InMemoryNoteFileService.cs`. (Tests for other moved SUTs such as
`NoteSearchIndex`/`SettingsService` move too **if** they don't require
`Avalonia.Headless`; any that do signal residual coupling — flag, don't force.)

**Intent**: Co-locate each moved SUT's tests with the SUT; keep VM/headless tests
in `Notes.Tests`.

**Contract**: Update test `using`/namespace to `Notes.Core.*`. The
`NoteTreeViewModel` collision tests (which exercise the real Parser/Renderer but
test a *shell* VM that stays in `Notes`) remain in `Notes.Tests`; add a
`ProjectReference` from `Notes.Tests` → `Notes.Core` so they still see
`Notes.Core.Services`. `TestApp.cs` stays in `Notes.Tests`. Naming/oracle
conventions unchanged.

### Success Criteria:

#### Automated Verification:

- `dotnet build` is green for the whole solution (Notes, Notes.Core, Notes.Tests, Notes.Core.Tests)
- `dotnet test` passes — every moved test runs from `Notes.Core.Tests`, the rest from `Notes.Tests`, with no lost tests (total count ≈ prior total)
- `Notes.Core` has no compile-time reference to `Notes` (one-way graph holds)

#### Manual Verification:

- `dotnet run --project Notes` launches; create-from-template flow opens and the **template-form dialog renders** (proves AXAML `clr-namespace` repoints are correct under compiled bindings)
- Spot-check that no logic was left behind in `Notes/Services` that should have moved, and `AutoSaveScheduler` + dialog services + shell VMs correctly remain

**Implementation Note**: After automated verification passes, pause for manual
confirmation that the app launches and the template-form dialog renders before
proceeding to Phase 2.

---

## Phase 2: Re-point Stryker + first run (smoke + raw baseline + VM gate)

### Overview

Point `stryker-config.json` at `Notes.Core`, run the first scoped mutation from
`Notes.Core.Tests/`, prove the MTP runner + net10 + the VM source generators all
survive the recompile, and capture a raw baseline + classified survivor inventory.

### Changes Required:

#### 1. Re-point the Stryker configuration

**File**: `stryker-config.json` (repo root — re-point existing)

**Intent**: Scope mutation to the template + file-safety files **inside
`Notes.Core`**, MTP runner, reporters. **No `thresholds.break` and no §F
exclusions yet** so the raw baseline shows every survivor.

**Contract**: `stryker-config` object with `test-runner: "mtp"`, `project:
"Notes.Core/Notes.Core.csproj"`, `test-projects:
["Notes.Core.Tests/Notes.Core.Tests.csproj"]`, `reporters:
["html","json","cleartext"]`, `concurrency: 4`, and a `mutate` allow-list of the
scoped files at their new `Notes.Core/` paths (template engine + `NameValidator`,
`PathGuard`, `NoteFileService`, `NoteDeleter`, `NoteFolderService`,
`OrphanedTempCleaner`, and the in-scope VMs `TemplateFormViewModel` +
`Fields/*` per research §C), with `"!**/*.axaml.cs"` as a guard. Exclude
`PathContainmentException.cs`. `coverage-analysis: perTest` omitted (MTP
limitation).

#### 2. First run + survivor inventory + VM-generator verdict

**File**: `context/changes/test-validation/baseline.md` (new — run record)

**Intent**: Run `dotnet stryker` **from `Notes.Core.Tests/`**, record the raw
score, render the VM-generator verdict, and classify every survivor into (a) the
three real gaps (§D), (b) the §F intentional survivors, (c) any unexpected
survivor.

**Contract**: A markdown record — exact command + run directory, runner used (MTP
or VSTest fallback + why), whether the in-scope VMs mutated/compiled (the gate; if
not, the `mutate` VM-exclusion fallback taken + why), raw mutation score, and the
survivor table keyed by `file:line` → classification. Cross-check §F predictions
against actual survivors.

### Success Criteria:

#### Automated Verification:

- `dotnet build` is green
- `dotnet stryker` (from `Notes.Core.Tests/`) completes without aborting and writes `StrykerOutput/` (html + json)
- The cleartext report lists a numeric mutation score
- The `mutate` globs resolved to exactly the intended `Notes.Core` files (no `.axaml.cs`, no out-of-scope `.cs`, no files from the `Notes` project)

#### Manual Verification:

- The HTML report's mutated-file list matches the scoped files — nothing from the Avalonia `Notes` project appears (confirms single-project mode, blocker not reintroduced)
- **VM gate:** the in-scope VMs (`TemplateFormViewModel`, `Fields/*`) either produced mutants and recompiled, OR the documented VM-exclusion fallback was taken and recorded in `baseline.md`
- Every §F intentional survivor appears as predicted; the three known §D gaps appear as survivors
- Any survivor NOT in §D or §F is noted for investigation in `baseline.md`

**Implementation Note**: After automated verification passes, pause for manual
confirmation that the run is in single-project mode, the VM gate verdict is
recorded, and the survivor inventory is classified before proceeding.

---

## Phase 3: Close the three gaps + re-run

### Overview

Add the missing tests / tighten the weak oracle (now in `Notes.Core.Tests`), then
re-run mutation. The before/after score delta is the proof and the cookbook §6.5
teaching example.

### Changes Required:

#### 1. NoteFolderService coverage

**File**: `Notes.Core.Tests/NoteFolderServiceTests.cs` (new)

**Intent**: Cover the untested `EnsureWithinWorkspace` guard so its no-coverage
survivor is killed. Mirror the out-of-root containment theory from
`NoteDeleterTests`.

**Contract**: Tests over `NoteFolderService` driven through a real `PathGuard`
(fed a stubbed `ISettingsService` per cookbook §6.2) + `MockFileSystem`. At least:
an out-of-root path throws `PathContainmentException` and leaves the FS unchanged,
and an in-root happy path creates the directory. Naming per CLAUDE.md
(`Method_WhenScenario_ExpectedBehaviour`).

#### 2. NoteFileService BOM oracle tightening

**File**: `Notes.Core.Tests/NoteFileServiceTests.cs` (the former `:45-53` case)

**Intent**: Replace the encoder-derived expectation
(`Encoding.UTF8.GetBytes("hello")`) with a fully independent literal-byte oracle
(`feedback-independent-test-oracle`).

**Contract**: Assert the written bytes against a fixed literal byte array (ASCII
`h,e,l,l,o`) plus the existing literal BOM-absence check (`0xEF,0xBB,0xBF` not at
head). No value derived from the same `Encoding.UTF8` the SUT uses.

#### 3. TemplateCatalog prefix-trap test

**File**: `Notes.Core.Tests/TemplateCatalogTests.cs`

**Intent**: Add the missing `.templatesX/`-style case so a `StartsWith`-only mutant
(prefix matched without the separator boundary) is killed.

**Contract**: A path that begins with the `.templates` prefix but is not a real
child (`.templatesX/...` or `.templates` with no trailing separator) is **not**
treated as a template entry. Reference the existing `TemplateCatalogTests` shape.

#### 4. Re-run + delta record

**File**: `context/changes/test-validation/baseline.md` (append)

**Intent**: Re-run `dotnet stryker` and record the post-fix score next to the raw
baseline; confirm the three targeted survivors are now killed.

**Contract**: Append a "post-fix" score line + the killed-survivor confirmation.
The delta (raw → post-fix) is referenced by cookbook §6.5.

### Success Criteria:

#### Automated Verification:

- `dotnet build` is green
- `dotnet test` passes (the three new/changed tests included)
- `dotnet stryker` completes and writes an updated report
- Post-fix mutation score is **strictly higher** than the raw baseline

#### Manual Verification:

- The `NoteFolderService` guard mutant is now killed (no longer a no-coverage survivor)
- The BOM-path mutant is killed and the test no longer derives expected bytes from `Encoding.UTF8`
- The `TemplateCatalog` `StartsWith` mutant is killed
- Remaining survivors are now only the §F intentional set (plus any documented-for-investigation residue)

**Implementation Note**: Pause for manual confirmation that the three targeted
survivors are killed and only §F survivors remain before proceeding.

---

## Phase 4: Exclude equivalents, lock threshold, document

### Overview

Suppress the confirmed §F intentional survivors at line-range precision, set the
break floor under the clean score, and document the procedure + sync the test-plan.

### Changes Required:

#### 1. Line-range exclusions for §F survivors

**File**: `stryker-config.json`

**Intent**: Exclude each empirically-confirmed §F intentional/equivalent survivor
at its exact line range so the score reflects only meaningful mutants.

**Contract**: Append `"!<Notes.Core path>{<line-range>}"` negations to `mutate`
for the §F lines confirmed in Phase 2's inventory — `TemplateParser` broad catch,
`OrphanedTempCleaner` Trace-log catch, the `NameValidator` TOCTOU `File.Exists`,
the `PathGuard` OS-conditional branch (Linux-unreachable), and any confirmed
guard-call-drop lines. JSON has no comments — keep the reason ledger in
`baseline.md` keyed by line range. Only exclude lines that actually survived in
Phase 2/3 and are classified §F — do not pre-exclude on prediction.

#### 2. Lock the break threshold

**File**: `stryker-config.json`

**Intent**: Set `thresholds.break` just under the post-exclusion score so the suite
passes today and a future test-quality regression trips a non-zero exit.

**Contract**: `thresholds: { "high": <h>, "low": <l>, "break": <b> }` where `break`
is a small margin below the observed post-exclusion score (recorded in
`baseline.md`). `high`/`low` are cosmetic. The chosen number + derivation recorded
in `baseline.md`.

#### 3. Cookbook §6.5

**File**: `context/foundation/test-plan.md` (§6.5, currently "TBD")

**Intent**: Replace the §6.5 placeholder with the concrete procedure for validating
that tests catch regressions, using this phase's run as the worked example.

**Contract**: Prose covering: **why `Notes.Core` exists** (the Avalonia
source-generator blocker + the run-from-`Notes.Core.Tests/` single-project-mode
rule), the scoped `dotnet stryker` command + `test-runner: mtp` rationale, how to
read the HTML report, the raw→post-fix delta as the proof pattern, the documented
intentional-survivor exclusion list, and the "set break after measuring, exclude
equivalents first" rule. Mirror the style of §6.1–§6.4.

#### 4. Doc/status sync

**Files**: `context/foundation/test-plan.md` (§3 status table, §8 freshness ledger),
`change.md`

**Intent**: Correct the doc drift research §A flagged and stamp this change's status.

**Contract**: In test-plan.md §3, update Phase 2 status from "not started" to its
true shipped state and Phase 3 to complete with this change folder; update the §8
freshness date. In `change.md`, advance `status` and set `updated:` to today. (Per
the jj convention, no git commands here — `/10x-implement` handles VCS.)

### Success Criteria:

#### Automated Verification:

- `dotnet build` is green and `dotnet test` passes
- `dotnet stryker` (from `Notes.Core.Tests/`) exits **0** with the score at or above `thresholds.break`
- The report shows the §F survivors excluded from the scored set
- `stryker-config.json` is valid JSON and committed at the repo root

#### Manual Verification:

- The scored survivor set contains no §F intentional mutants (all excluded at correct line ranges)
- `break` is below the observed score with a sensible margin (documented in `baseline.md`)
- Cookbook §6.5 reads as a usable procedure and matches the actual committed config + command (including the run location)
- test-plan §3 status table and `change.md` reflect reality

**Implementation Note**: Pause for manual confirmation that the threshold margin
and §6.5 wording are acceptable before closing the phase.

---

## Testing Strategy

### Unit Tests:

- `NoteFolderServiceTests` (new, `Notes.Core.Tests`) — out-of-root containment + in-root happy path
- `TemplateCatalogTests` — `.templatesX/` prefix-without-separator negative case
- `NoteFileServiceTests` BOM path — literal-byte oracle replacing the encoder-derived one
- All moved logic tests must keep passing from `Notes.Core.Tests` (no oracle changes beyond the BOM tightening)

### Integration Tests:

- None new — file-safety paths already covered via `MockFileSystem` /
  `ThrowingFileSystem`; this change measures + restructures, it doesn't add
  integration surface.

### Manual Testing Steps:

1. `dotnet run --project Notes` — app launches; create-from-template dialog renders (Phase 1 gate).
2. Run `dotnet stryker` from `Notes.Core.Tests/`; open `StrykerOutput/**/mutation-report.html`.
3. Confirm only the scoped `Notes.Core` files are mutated and nothing from `Notes`.
4. Compare raw baseline vs post-fix score in `baseline.md` — confirm the increase.
5. Confirm the scored survivor set excludes the §F intentional mutants.
6. Confirm `dotnet stryker` exits 0 against the locked `break` threshold.

## Performance Considerations

- The MTP runner is persistent (no per-mutant process startup) — faster than
  VSTest. With the scoped files and `concurrency: 4` the matrix is small.
- `Notes.Core.Tests` pulls **no Avalonia.Headless** — the pure-logic SUTs avoid the
  `TestApp` startup cost entirely (it stays with the VM/headless tests in
  `Notes.Tests`).

## Migration Notes

- The structural change is the extraction itself: a new `Notes.Core` library +
  `Notes.Core.Tests` project, re-namespaced `Notes.Core.*`. No data/schema
  migration. The reference graph becomes `Notes` → `Notes.Core`,
  `Notes.Tests` → {`Notes`, `Notes.Core`}, `Notes.Core.Tests` → `Notes.Core`.
- If the preview MTP runner fails on net10, the VSTest fallback adds two NuGet
  packages to `Notes.Core.Tests.csproj` — reversible; document the reason.

## References

- Research: `context/changes/test-validation/research.md` (§C scope, §D oracle
  quality, §E Stryker grounding, §F intentional survivors)
- Blocker record: `context/changes/test-validation/stryker-avalonia-blocker.md`
- Test plan: `context/foundation/test-plan.md` (§3 Phase 3, §5 gates, §6.5 cookbook)
- Cookbook patterns: `context/foundation/test-plan.md` §6.2 (MockFileSystem +
  PathGuard shape), §6.3 (field-type boundaries)
- Reference oracle: `TemplateRendererTests.cs:30-32,225-235` (now in `Notes.Core.Tests`)
- Containment reference: `NoteDeleterTests.cs`, `PathGuardTests.cs:64-71`
- Memory: `feedback-independent-test-oracle`, `prefer-mockfilesystem-in-unit-tests`,
  `keep-viewmodel-dependencies-minimal`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Extract Notes.Core + Notes.Core.Tests

#### Automated

- [x] 1.1 `dotnet build` is green for the whole solution (4 projects) — 131f3cc
- [x] 1.2 `dotnet test` passes — all moved + remaining tests run, no lost tests — 131f3cc
- [x] 1.3 `Notes.Core` has no compile-time reference to `Notes` (one-way graph) — 131f3cc

#### Manual

- [x] 1.4 `dotnet run --project Notes` launches and the template-form dialog renders — 131f3cc
- [x] 1.5 No mis-placed logic — `AutoSaveScheduler`/dialog services/shell VMs correctly remain in `Notes` — 131f3cc

### Phase 2: Re-point Stryker + first run (smoke + raw baseline + VM gate)

#### Automated

- [x] 2.1 `dotnet build` is green
- [x] 2.2 `dotnet stryker` (from `Notes.Core.Tests/`) completes and writes `StrykerOutput/` (html + json)
- [x] 2.3 Cleartext report lists a numeric mutation score
- [x] 2.4 `mutate` globs resolved to exactly the scoped `Notes.Core` files (no `.axaml.cs`, no `Notes`-project files)

#### Manual

- [x] 2.5 HTML report shows nothing from the Avalonia `Notes` project (single-project mode confirmed)
- [x] 2.6 VM gate verdict recorded — VMs mutated/compiled, or the VM-exclusion fallback taken and documented
- [x] 2.7 Every §F intentional survivor and the three §D gaps appear as survivors
- [x] 2.8 Any survivor outside §D/§F noted for investigation in `baseline.md`

### Phase 3: Close the three gaps + re-run

#### Automated

- [ ] 3.1 `dotnet build` is green
- [ ] 3.2 `dotnet test` passes (three new/changed tests included)
- [ ] 3.3 `dotnet stryker` completes and writes an updated report
- [ ] 3.4 Post-fix mutation score is strictly higher than the raw baseline

#### Manual

- [ ] 3.5 `NoteFolderService` guard mutant now killed
- [ ] 3.6 BOM-path mutant killed and oracle no longer encoder-derived
- [ ] 3.7 `TemplateCatalog` `StartsWith` mutant killed
- [ ] 3.8 Remaining survivors are only the §F intentional set

### Phase 4: Exclude equivalents, lock threshold, document

#### Automated

- [ ] 4.1 `dotnet build` green and `dotnet test` passes
- [ ] 4.2 `dotnet stryker` (from `Notes.Core.Tests/`) exits 0 with score ≥ `thresholds.break`
- [ ] 4.3 Report shows §F survivors excluded from the scored set
- [ ] 4.4 `stryker-config.json` is valid JSON committed at repo root

#### Manual

- [ ] 4.5 Scored survivor set contains no §F intentional mutants
- [ ] 4.6 `break` is below the observed score with a documented margin
- [ ] 4.7 Cookbook §6.5 matches the committed config + command (incl. run location)
- [ ] 4.8 test-plan §3 status table and `change.md` reflect reality
