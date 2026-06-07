# Template Pipeline Correctness Tests Implementation Plan

## Overview

This is rollout **Phase 1** of `context/foundation/test-plan.md` ("Template
pipeline correctness"). It writes the tests that protect the three highest /
least-confident risks in the template pipeline:

- **Risk #1** — Create-from-template produces a wrong/corrupt note (leftover
  `{{placeholder}}`, wrong-slot substitution, dropped fields).
- **Risk #2** — Malformed/edge-case template frontmatter (missing type, unknown
  type, empty value) silently fails or renders a half-form.
- **Risk #6** — A saved note is no longer valid/portable `.md` (frontmatter,
  encoding, or line-ending corruption).

All work lands at the **unit** and **integration (MockFileSystem)** layers — no
e2e, per the test-plan's cost × signal layering. The phase also performs one
small production simplification (`NoteFileService` encoding) that the test
work surfaced, and finishes by filling the §6 cookbook entries this rollout
phase owns.

## Current State Analysis

The pipeline is `TemplateParser.Parse` → `FormDefinition` →
`TemplateFormViewModel` (FieldVm per type) → `TemplateRenderer.Render` →
`NoteFileService.Save`. The dominant cross-cutting fact — established by
`context/changes/testing-template-pipeline/research.md` and locked in
`context/foundation/lessons.md` — is that **every failure mode in this pipeline
is silent by design**: malformed YAML → `FormDefinition.Empty`; unknown/missing
field type → `TextFieldVm`; missing value → empty string; undeclared placeholder
→ verbatim literal. Nothing throws, logs, or warns. The test oracle must
therefore assert on the **resulting shape**, never on an exception or a surfaced
warning.

An existing suite of ~198 methods already covers the happy paths and several
hard-won regressions (CRLF preservation, blank-line-in-form, undeclared-token
verbatim, malformed → empty, UTF-8 no-BOM). This phase targets only the
**named gaps** in research §"Open Questions / Coverage Gaps" and must not
duplicate existing coverage.

### Key Discoveries:

- **Keyword/class mismatch trap:** the YAML keyword is `dropdown`, the class is
  `SelectFieldVm` (`Notes/ViewModels/TemplateFormViewModel.cs:61-67`). `type: select`
  silently degrades to a `TextFieldVm` and drops its `entries`. High-value target.
- **Substitution is exact-ordinal, body-only** (`Notes/Services/TemplateRenderer.cs:156-169`):
  undeclared / mis-cased / frontmatter-placed `{{…}}` survive **verbatim** — there
  is no leftover-detection pass. This is the canonical Risk #1 outcome.
- **No YAML serializer on the save path** (`Notes/Services/NoteFileService.cs:39-42`):
  a note is persisted as the raw editor string via `WriteAllText`, byte-for-byte.
  Round-trip is therefore *safer than the risk assumes* — nothing is reordered or
  re-quoted.
- **Encoding is already correct via .NET 10 defaults.** `File.WriteAllText(path, text)`
  defaults to UTF-8 no-BOM; `File.ReadAllText(path)` reads UTF-8 with BOM
  auto-detection. The explicit `Utf8NoBom` argument on write/`ReadAsync`
  (`NoteFileService.cs:10,:36,:41`) is equivalent to the default; sync `Read`'s
  arg-less call (`:26`) behaves identically. The "asymmetry" research flagged is
  cosmetic, not behavioral.
- **Locked decisions (do NOT test against):** malformed `form:` → silent static
  copy (no exception/warning); the broad `catch (Exception)` in `TemplateParser`
  (`lessons.md:5-10` lineage); duplicate-field-names throwing in `Submit` is a
  deferred boundary assumption, not a guaranteed behavior.

## Desired End State

The four risks have explicit, oracle-grounded tests at the cheapest layer that
gives signal; the encoding code is simplified to rely on documented defaults with
the existing no-BOM test proving the default still matches intent; and the §6
cookbook tells a future author how to add a unit test, a new-field-type test, and
a view-model test in this project. Verified by: `dotnet test` green with the new
methods present, and `test-plan.md` §6.1/§6.3/§6.4 no longer reading "TBD".

## What We're NOT Doing

- **Not** testing for thrown exceptions or surfaced warnings on malformed
  templates — silence is the locked contract.
- **Not** narrowing or re-flagging the broad `catch` in `TemplateParser`
  (`lessons.md`).
- **Not** asserting `Save` parent-directory behavior — deferred to **Phase 2**
  (file-safety & data-loss guardrails).
- **Not** testing name-collision / overwrite — that is **Phase 2** (Risk #3).
- **Not** testing durable/atomic writes or path containment — **Phase 2**
  (Risks #4, #5).
- **Not** adding e2e / GUI automation, or testing the YAML/markdown library
  internals (test-plan §7).
- **Not** writing the mutation-testing validation — **Phase 3**.

## Implementation Approach

Work proceeds layer-by-layer following the pipeline's natural data flow:
parse → render → save round-trip → end-to-end orchestration → cookbook. Each
test phase extends the **existing** test file for its SUT (research confirms one
test file per SUT already exists) and follows the established conventions:
`Method_WhenScenario_ExpectedBehaviour` naming, xUnit v3 `[Fact]`/`[Theory]`,
NSubstitute for behavior doubles, fresh `StrongReferenceMessenger` per test,
MockFileSystem for any disk-touching service, `[AvaloniaFact]` + `TestApp.cs`
only where the SUT touches Avalonia primitives. Pure-engine SUTs are `new`-ed
directly (no DI in tests).

The oracle for every render/parse assertion is derived **independently** from
the template definition + input — never copied from the renderer/parser output —
to defeat the "it returned a string ⇒ it's correct" anti-pattern called out in
the test-plan's Risk Response Guidance.

## Critical Implementation Details

**Locked-contract assertion shape.** Because the pipeline never throws on bad
input, no test in this phase may use `Assert.Throws` for a malformed-template or
edge-case-field scenario. Assert the *fallback shape* instead
(`FormDefinition.Empty`, a `TextFieldVm`, `string.Empty`, or a verbatim literal).
The one place a throw is real — duplicate field names in `Submit`'s ordinal
`ToDictionary` — is a deferred boundary assumption and must not be asserted as a
guarantee.

**Encoding test fidelity after simplification.** When the explicit `Utf8NoBom`
argument is removed (Phase 3), the *authoritative* proof that the default still
writes UTF-8 no-BOM is the **existing** real-FS no-BOM test in
`NoteFileServiceTests`. New round-trip tests run on `MockFileSystem`; a
byte-level no-BOM assertion on `MockFileSystem` would only prove the mock's
default, so the real-FS test must stay green as the regression guard for the
production default.

## Phase 1: Risk #2 — Parser & form edge-case coverage (unit)

### Overview

Prove that malformed and edge-case template frontmatter resolves to the
defined silent fallback — never a thrown exception, never a half-form — at the
parse → form boundary. Targets the research-named Risk #2 gaps.

### Changes Required:

#### 1. Parser malformed-shape & edge-field cases

**File**: `Notes.Tests/TemplateParserTests.cs`

**Intent**: Cover the malformed-frontmatter shapes and edge fields that are not
yet asserted, proving each resolves to the documented fallback without throwing.

**Contract**: New `[Fact]`/`[Theory]` methods following
`Parse_WhenScenario_Expected` naming, asserting against the returned
`FormDefinition`:
- `form:` as a YAML **sequence** and as a **tab-indented** block (the real FU-2
  trigger) → `FormDefinition.Empty`, no throw.
- Field with **missing `type:`** → `FormField.Type` is empty string (the value
  that later maps to `TextFieldVm`).
- Field with **missing `label:`** → `FormField.Label` empty.
- Dropdown declared with **no `entries:`** → entries null/empty at the model level
  (the value the form builder later coalesces to a zero-choice dropdown).
Reuse the existing `FormDefinition` construction helper pattern from the test
file. Do **not** duplicate the already-covered null/empty/no-frontmatter/
no-`form`/malformed-YAML→empty cases.

#### 2. Field-type resolution at the form boundary

**File**: `Notes.Tests/TemplateFormViewModelTests.cs`

**Intent**: Pin the keyword-mismatch trap and the unknown/missing-type fallback
at the actual decision point (`CreateField`), where a wrong type silently
becomes free text.

**Contract**: New methods asserting the concrete `FieldVm` subtype produced by
`Load(FormDefinition)`:
- `type: select` → resolves to `TextFieldVm` (NOT `SelectFieldVm`) and carries no
  entries — the keyword is `dropdown`. **Highest-value case in this phase.**
- Unknown `type:` (e.g. `colorpicker`) → `TextFieldVm`, field **kept** (not
  dropped).
- Dropdown (`type: dropdown`) with no entries → `SelectFieldVm` with a
  zero-length choice list whose `RenderValue()` is empty.
Follow the existing file's fixture convention (matching `[Fact]`/`[AvaloniaFact]`
usage already present for the VM tests).

#### 3. Unknown frontmatter key in the note metadata reader

**File**: `Notes.Tests/NoteMetadataParserTests.cs`

**Intent**: Assert that an unrecognized frontmatter key is ignored without error
(currently unasserted) — supporting the Risk #6 portability story at the read
layer.

**Contract**: One `[Fact]` parsing frontmatter containing a key the model does
not declare (e.g. `author:`/`date:`) alongside `tags:`; assert tags parse
normally and no exception is raised.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- New parser/form tests pass: `dotnet test`
- No existing TemplateParser/TemplateFormViewModel/NoteMetadataParser test regressed: `dotnet test`

#### Manual Verification:

- The `type: select` test demonstrably fails if `SelectFieldVm` is asserted
  instead of `TextFieldVm` (confirms the trap is really pinned, not tautological).
- No new test uses `Assert.Throws` for a malformed/edge-case scenario.

**Implementation Note**: After this phase and all automated verification passes,
pause for manual confirmation before proceeding.

---

## Phase 2: Risk #1 — Render correctness & leftover-placeholder oracle (unit)

### Overview

Prove a rendered note contains zero leftover declared placeholders and that each
value lands in its declared slot, with the oracle derived independently from
`(template + FormDefinition + values)`. Drive the cases with xUnit theories.

### Changes Required:

#### 1. Leftover-placeholder & slot-fidelity theory

**File**: `Notes.Tests/TemplateRendererTests.cs`

**Intent**: Add a data-driven proof that no **declared** `{{name}}` token survives
rendering while **undeclared / mis-cased / frontmatter-placed** tokens remain
verbatim, and that each declared value lands only in its own slot.

**Contract**: A `[Theory]` with `[InlineData]` (or `[MemberData]` where a
`FormDefinition` is needed) cases covering:
- Every declared ordinal name substituted → expected rendered body contains zero
  of those declared tokens; assert against an expected string built from the
  inputs, not from the renderer's output.
- Mis-cased token (`{{Title}}` vs declared `title`) → literal `{{Title}}` remains.
- Undeclared token → literal remains.
- Frontmatter-placed token → not substituted (body-only rule).
- Two distinct declared values → each appears only in its own slot (no
  wrong-slot bleed), including duplicate-occurrence consistency.
Do **not** duplicate the existing single-case declared/undeclared/missing/
form-fence/CRLF/blank-line tests; this is the consolidated whole-note oracle.

#### 2. (Cut-first) odd-bracing grammar boundaries

**File**: `Notes.Tests/TemplateRendererTests.cs`

**Intent**: Lowest-priority edge cases pinning the placeholder grammar
boundaries. **This sub-section is the explicit drop-first if effort is tight.**

**Contract**: `[Theory]` cases for `{{ name }}` (trim), `{{{x}}}`, unterminated
`{{x` on a line, and adjacent `{{a}}{{b}}` — asserting the documented grammar
behavior. Mark these tests so they are clearly the optional set.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- New render theory passes: `dotnet test`
- No existing TemplateRenderer test regressed: `dotnet test`

#### Manual Verification:

- The leftover-placeholder oracle fails if a single declared token is left
  unsubstituted in the expected string (confirms it actually checks for leftovers).
- Expected strings are constructed from inputs, not pasted from a render run.

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 3: Risk #6 — Round-trip portability + encoding simplification (integration)

### Overview

Simplify `NoteFileService` to rely on .NET 10 encoding defaults, then prove
save→reload round-trips frontmatter + body faithfully (content, encoding, line
endings) on `MockFileSystem`. The existing real-FS no-BOM test remains the guard
that the default still matches intent.

### Changes Required:

#### 1. Encoding simplification

**File**: `Notes/Services/NoteFileService.cs`

**Intent**: Remove the redundant explicit encoding handling now that .NET 10
defaults provide UTF-8 no-BOM on write and BOM-detecting UTF-8 on read, leveling
all three paths (sync `Read`, `ReadAsync`, `Save`) to the same default behavior
and eliminating the cosmetic asymmetry.

**Contract**: Drop the `Utf8NoBom` constant (`:10`) and the explicit encoding
arguments on `WriteAllText` (`:41`) and `ReadAllTextAsync`/`ReadAsync` (`:36`);
sync `Read` (`:26`) already takes no encoding argument and stays as-is. No
behavior change intended — defaults are equivalent. Public method signatures
unchanged.

#### 2. Round-trip portability tests

**File**: `Notes.Tests/NoteFileServiceTests.cs`

**Intent**: Add file-service-layer round-trip tests on `MockFileSystem` proving
content, encoding, and line-ending fidelity through `Save`→`Read`. New tests
prefer `MockFileSystem` over the file's existing real-FS pattern (per CLAUDE.md +
memory).

**Contract**: New methods on a `MockFileSystem`-injected `NoteFileService`:
- `Save`→`Read` of frontmatter + non-ASCII / emoji body → content returned
  unchanged.
- Line-ending fidelity: write LF-only content and write CRLF content → bytes
  survive `Save`→`Read` unchanged (the file-service-layer counterpart to the
  existing renderer-layer CRLF test). Assert on raw stored bytes via the mock's
  file contents, not on a normalized string compare.
- BOM-prefixed external file seeded directly into the mock → read through sync
  `Read` returns the expected content (auto-detection behavior pinned).
Keep the **existing** real-FS UTF-8-no-BOM test untouched — it is the
authoritative guard that the simplified default still writes no BOM.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- New round-trip tests pass: `dotnet test`
- Existing real-FS no-BOM / non-ASCII / overwrite / emoji tests still pass after
  the encoding simplification: `dotnet test`

#### Manual Verification:

- Removing the explicit encoding produced no behavior change (existing no-BOM
  test green confirms the default is UTF-8 no-BOM).
- Line-ending test asserts on raw bytes, so a future LF/CRLF normalization would
  fail it.

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 4: Risk #1 end-to-end — create-from-template reaches disk intact (integration)

### Overview

Prove the silent-fallback contract survives all the way to the file: a malformed
template produces a static-copy note on disk, and a blank/cancelled form produces
a note with empty substitutions saved intact. Exercises the
`NoteTreeViewModel` create orchestration with `MockFileSystem` and doubled
dialogs.

### Changes Required:

#### 1. Malformed-template and blank/cancelled-form orchestration tests

**File**: `Notes.Tests/NoteTreeViewModelTests.cs`

**Intent**: Add end-to-end create-from-template tests for the two silent-fallback
paths, asserting the rendered content actually written to the mock file system —
complementing the existing happy-path render→save glue test (do not duplicate it).

**Contract**: New methods on a `NoteTreeViewModel` wired with `MockFileSystem`,
NSubstitute dialog doubles, and a fresh `StrongReferenceMessenger`:
- Malformed template (e.g. tab-indented `form:`) selected → no form prompt (empty
  definition) → the template's static body is saved to the mock FS unchanged
  (static copy reaches disk).
- Form presented but submitted blank / cancelled → note saved with declared
  placeholders resolved to empty substitutions (per the chosen blank/cancel
  semantics in the create flow), with no leftover declared tokens.
Use `[AvaloniaFact]` + `TestApp.cs` if the VM touches Avalonia primitives,
matching the existing file's convention. Assert against the mock's stored file
content, not the renderer return value.

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- New orchestration tests pass: `dotnet test`
- Existing `NoteTreeViewModel` tests (incl. the render→save glue, picker-cancelled,
  no-templates paths) still pass: `dotnet test`

#### Manual Verification:

- The malformed-template test fails if the form dialog is unexpectedly invoked
  (confirms the empty-definition branch is the one exercised).
- Saved content is read back from the mock FS, proving the note reached disk
  intact rather than only existing as a return value.

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 5: Cookbook update (§6)

### Overview

Fill the §6 cookbook entries this rollout phase owns so a future author can add
the patterns introduced here without re-deriving them. Closing ritual for the
rollout phase.

### Changes Required:

#### 1. Cookbook entries

**File**: `context/foundation/test-plan.md`

**Intent**: Replace the "TBD — see §3 Phase 1" placeholders for the patterns this
phase established, each with location, naming convention, a named reference test,
and the run command.

**Contract**: Update three §6 sub-sections (leave §6.2 and §6.5 as their existing
"see §3 Phase 2/3" placeholders):
- **§6.1 Adding a unit test** → pure-engine SUT `new`-ed directly,
  `Method_WhenScenario_ExpectedBehaviour` naming, oracle built from inputs;
  reference test = the Phase 2 leftover-placeholder theory; run: `dotnet test`.
- **§6.3 Adding a test for a new template field type** → assert at the parse →
  form boundary (`TemplateParser` keeps the raw type; `TemplateFormViewModel`
  resolves the `FieldVm`); reference test = the Phase 1 `type: select` /
  unknown-type cases; call out the `dropdown` keyword.
- **§6.4 Adding a view-model test** → `[AvaloniaFact]` + `TestApp.cs`, NSubstitute
  doubles, fresh `StrongReferenceMessenger`, `MockFileSystem`; reference test =
  the Phase 4 orchestration tests.

### Success Criteria:

#### Automated Verification:

- §6.1, §6.3, §6.4 no longer contain the string "TBD": inspect
  `context/foundation/test-plan.md`.
- Full suite still green: `dotnet test`

#### Manual Verification:

- Each cookbook entry names a real test method that exists in the suite.
- A reader unfamiliar with the project could add a test of each kind from the
  entry alone.

**Implementation Note**: Final phase — after this, re-invoke `/10x-test-plan`
(no args) so the orchestrator marks §3 Phase 1 `complete` and advances.

---

## Testing Strategy

### Unit Tests:

- Parser malformed/edge-field fallbacks (Phase 1); field-type resolution incl.
  the `dropdown`/`select` trap (Phase 1); render leftover-placeholder + slot
  fidelity oracle (Phase 2); odd-bracing grammar boundaries (Phase 2, optional).

### Integration Tests:

- `NoteFileService` save→reload round-trip (content, encoding, line endings) on
  MockFileSystem (Phase 3); `NoteTreeViewModel` create-from-template malformed and
  blank/cancelled paths reaching the mock FS (Phase 4).

### Manual Testing Steps:

1. Run `dotnet test`; confirm new methods appear and pass.
2. Temporarily assert `SelectFieldVm` in the `type: select` test → confirm it
   fails (trap is real).
3. Temporarily leave one declared token in a Phase 2 expected string → confirm
   the oracle fails (leftover detection is real).

## Performance Considerations

None — all tests are in-memory (MockFileSystem) or pure-engine; no real disk,
network, or GUI.

## Migration Notes

The only production change is the `NoteFileService` encoding simplification
(Phase 3), which is behavior-preserving on .NET 10 (defaults equal the removed
explicit values). The existing real-FS no-BOM test is the guard; if it ever goes
red, revert the simplification.

## References

- Research: `context/changes/testing-template-pipeline/research.md`
- Strategy: `context/foundation/test-plan.md` §2 Risk Map + Risk Response Guidance
- Locked decisions: `context/foundation/lessons.md`; archive
  `context/archive/2026-06-02-templates/`
- Keyword trap: `Notes/ViewModels/TemplateFormViewModel.cs:61-67`
- Substitution: `Notes/Services/TemplateRenderer.cs:156-169`
- Save path: `Notes/Services/NoteFileService.cs:10,:26,:36,:39-42`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Risk #2 — Parser & form edge-case coverage

#### Automated

- [x] 1.1 Build passes: `dotnet build` — f647c957
- [x] 1.2 New parser/form tests pass: `dotnet test` — f647c957
- [x] 1.3 No existing TemplateParser/TemplateFormViewModel/NoteMetadataParser test regressed — f647c957

#### Manual

- [x] 1.4 `type: select` test fails if `SelectFieldVm` is asserted instead of `TextFieldVm` — f647c957
- [x] 1.5 No new test uses `Assert.Throws` for a malformed/edge-case scenario — f647c957

### Phase 2: Risk #1 — Render correctness & leftover-placeholder oracle

#### Automated

- [x] 2.1 Build passes: `dotnet build` — c6267ee7
- [x] 2.2 New render theory passes: `dotnet test` — c6267ee7
- [x] 2.3 No existing TemplateRenderer test regressed — c6267ee7

#### Manual

- [x] 2.4 Oracle fails if a declared token is left unsubstituted in the expected string — c6267ee7
- [x] 2.5 Expected strings are constructed from inputs, not pasted from a render run — c6267ee7

### Phase 3: Risk #6 — Round-trip portability + encoding simplification

#### Automated

- [x] 3.1 Build passes: `dotnet build` — df90aebe
- [x] 3.2 New round-trip tests pass: `dotnet test` — df90aebe
- [x] 3.3 Existing real-FS no-BOM / non-ASCII / overwrite / emoji tests still pass after simplification — df90aebe

#### Manual

- [x] 3.4 Removing explicit encoding produced no behavior change (no-BOM test green) — df90aebe
- [x] 3.5 Line-ending test asserts on raw bytes (would fail on LF/CRLF normalization) — df90aebe

### Phase 4: Risk #1 end-to-end — create-from-template reaches disk intact

#### Automated

- [x] 4.1 Build passes: `dotnet build` — b5209f90
- [x] 4.2 New orchestration tests pass: `dotnet test` — b5209f90
- [x] 4.3 Existing NoteTreeViewModel tests still pass — b5209f90

#### Manual

- [x] 4.4 Malformed-template test fails if the form dialog is unexpectedly invoked — b5209f90
- [x] 4.5 Saved content read back from the mock FS (proves note reached disk) — b5209f90

### Phase 5: Cookbook update

#### Automated

- [x] 5.1 §6.1, §6.3, §6.4 no longer contain "TBD" — 8afba184
- [x] 5.2 Full suite still green: `dotnet test` — 8afba184

#### Manual

- [x] 5.3 Each cookbook entry names a real test method that exists in the suite — 8afba184
- [x] 5.4 A reader could add a test of each kind from the entry alone — 8afba184
