# Test Plan

> Phased test rollout for this project. Strategy is frozen at the top
> (§1–§5); cookbook patterns at the bottom (§6) fill in as phases ship.
> Read before writing any new test.
>
> Refresh: re-run `/10x-test-plan --refresh` when stale (see §8).
>
> Last updated: 2026-06-07 (Phase 1 cookbook §6.1/§6.3/§6.4 filled)

## 1. Strategy

Tests follow three non-negotiable principles for this project:

1. **Cost × signal.** The cheapest test that gives a real signal for the
   risk wins. Do not promote to e2e because e2e "feels safer." Do not put a
   vision model on top of a deterministic check that already catches the
   regression.
2. **User concerns are first-class evidence.** Risks anchored in "the team
   is worried about X, and the failure would surface somewhere in <area>"
   carry the same weight as PRD lines or hot-spot data.
3. **Risks are scenarios, not code locations.** This plan documents *what
   could fail* and *why we believe it's likely* — drawn from documents,
   interview, and codebase *signal* (churn, structure, test base). It does
   NOT claim to know which line owns the failure. That knowledge is
   produced by `/10x-research` during each rollout phase. If the plan and
   research disagree about where the failure lives, research is the
   ground truth.

Hot-spot scope used for likelihood weighting: `Notes/Services`, `Notes/ViewModels`.

## 2. Risk Map

The top failure scenarios this project must protect against, ordered by
risk = impact × likelihood. Risks are failure scenarios in user / business
terms, not test names. The Source column cites the *evidence that surfaced
this risk* — never a specific file as "where the failure lives" (that is
research's job, see §1 principle #3).

| # | Risk (failure scenario) | Impact | Likelihood | Source (evidence — not anchor) |
|---|--------------------------|--------|------------|---------------------------------|
| 1 | Create-from-template produces a wrong/corrupt note — leftover `{{placeholder}}` syntax, values substituted into the wrong slot, or fields dropped — so the user's note silently loses or mangles data | High | High | PRD FR-009 / US-02 AC ("no leftover placeholder syntax"); interview Q1; hot-spot dir `Notes/Services` |
| 2 | Malformed/edge-case template frontmatter (missing type, unknown field type, empty value) makes a template silently fail or render wrong — the generated form is incomplete or the note is malformed | High | High | PRD FR-008; interview Q3 (parser = least-confident area); hot-spot dir `Notes/Services` |
| 3 | Creating a note from a template collides with an existing note of the same name and silently overwrites prior content | High | Medium | interview Q1; PRD Guardrails ("no data loss"); FR-009 |
| 4 | A crash or fast quit mid-save leaves a note file truncated or empty on disk — data loss | High | Medium | PRD Guardrails ("a crash or unexpected quit must never corrupt or lose a note file"); hot-spot dir `Notes/Services` |
| 5 | A user-supplied note/folder name escapes the workspace (path traversal, absolute path, reserved chars) and writes or deletes outside the notes folder | High | Medium | untrusted-input / abuse lens; PRD FR-010, FR-003; hot-spot dir `Notes/Services` |
| 6 | A note saved by Notes is no longer valid/portable `.md` readable by other tools (frontmatter mangled, encoding or line-ending corruption) — breaks the no-lock-in promise | High | Medium | PRD Guardrails ("no lock-in"); Secondary success ("instantly portable"); FR-005 |

**Impact × Likelihood rubric.**

| Rating | Impact | Likelihood |
|--------|--------|------------|
| High   | user loses access, data, or money; failure is publicly visible | area changes weekly, or we have already been burned here |
| Medium | feature degrades, a workaround exists, only some users affected | touched occasionally, has been a source of bugs |
| Low    | cosmetic, easily reverted, no data effect | stable code, rarely touched |

R1 and R2 are High × High (the template pipeline is the differentiator, the
least-confident area per interview Q3, and the churn center per the
hot-spot scan) and are protected first. R3–R6 are High-impact but
Medium-likelihood (data-safety guardrails on paths the user exercises
occasionally). No High-impact × Low-likelihood scenario is padded into the
map; search-index concurrency (already a deliberate design choice per
`lessons.md`) is intentionally left out of the top N.

**Abuse / security lens.** The product has no auth, payments, or network
surface, but it does accept user input (template-form values, hand-written
YAML/markdown files, and note/folder names) and writes to the file system.
R5 is the untrusted-input/resource-abuse row: server-side-parity for name
validation so a crafted name cannot escape the workspace.

### Risk Response Guidance

| Risk | What would prove protection | Must challenge | Context `/10x-research` must ground | Likely cheapest layer | Anti-pattern to avoid |
|------|-----------------------------|----------------|--------------------------------------|-----------------------|-----------------------|
| #1 | A rendered note contains zero `{{…}}` tokens and each field value lands in its declared slot — oracle taken from the template definition + input, not from the renderer | "render returned a string ⇒ it's correct" | placeholder grammar, render entry point, behaviour when a value is missing | unit / integration (MockFileSystem) | assertion copied from renderer output (oracle problem) |
| #2 | Unknown/missing field type and empty/odd YAML are surfaced (error or a defined fallback), never a silent half-form | "parse succeeded ⇒ the form is complete" | parser entry point, the supported field-type set, failure mode on bad YAML | unit | happy-path-only; over-mocking the YAML library |
| #3 | Create-from-template refuses or safely disambiguates a name collision instead of overwriting | "a unique-name guard exists" — **verify, do not assume** | where the write happens, the collision check, the role of the name validator | integration (MockFileSystem) | testing a safeguard that may not exist yet |
| #4 | An interrupted/partial save never truncates the existing file (atomic temp-then-rename or equivalent) | "save is a single write" — **verify atomicity exists first** | the save path, whether the write is atomic, how auto-save is scheduled | integration (MockFileSystem) | simulating a crash against a non-atomic write that cannot pass |
| #5 | The service layer (not only the dialog) rejects traversal/absolute/reserved names | "dialog-level validation is enough" | the validator boundary, who calls it, which characters/forms are rejected | unit / integration | testing only the UI validator and skipping service parity |
| #6 | Save→reload round-trips frontmatter + body faithfully; output parses as valid markdown in other tools | "we wrote it, so it's fine" | the serializer, encoding handling, line-ending handling | unit / integration | snapshot-without-meaning |

## 3. Phased Rollout

Each row is a discrete rollout phase that will open its own change folder
via `/10x-new`. Status moves left-to-right through the values below; the
orchestrator updates Status as artifacts appear on disk.

| # | Phase name | Goal (one line) | Risks covered | Test types | Status | Change folder |
|---|------------|------------------|----------------|------------|--------|----------------|
| 1 | Template pipeline correctness | Prove parse → form → render produces faithful notes (the differentiator + least-confident area) | #1, #2, #6 | unit + integration | change opened | context/changes/testing-template-pipeline/ |
| 2 | File-safety & data-loss guardrails | Prove collisions, durable writes, and path containment never destroy data | #3, #4, #5 | integration | not started | — |
| 3 | Test-effectiveness validation | Prove the Phase 1–2 tests actually kill regressions (mutation testing scoped to template + file-safety logic), answering "are these tests correct?" | cross-cutting (#1–#6) | mutation testing (AI-native/tooling) | not started | — |
| 4 | Quality-gates wiring | Lock the floor: format/build/test mapped to CI steps; post-edit hook recommended-local | cross-cutting | gates | not started | — |

**Status vocabulary** (fixed — parser literals):

| Value | Meaning |
|-------|---------|
| `not started` | No change folder for this rollout phase yet. |
| `change opened` | `context/changes/<id>/` exists with `change.md`; research not done. |
| `researched` | `research.md` exists in the change folder. |
| `planned` | `plan.md` exists with a `## Progress` section. |
| `implementing` | Progress section has at least one `[x]` and at least one `[ ]`. |
| `complete` | Progress section is fully `[x]`. |

## 4. Stack

The classic test base for this project. AI-native tools carry a `checked:`
date so future readers can see which lines need re-verification.

| Layer | Tool | Version | Notes |
|-------|------|---------|-------|
| unit + integration | xUnit | (per `Notes.Tests.csproj`) | 17 test files spread across services + view models; `meaningful` base |
| file-system fake | `System.IO.Abstractions` + `MockFileSystem` | (per csproj) | mandatory for any disk-touching service; never hit the real FS (per CLAUDE.md + memory) |
| mocking | NSubstitute | (per csproj) | behavior-only doubles; preferred over Moq/hand-rolled stubs (memory) |
| view-model tests | xUnit + `TestApp.cs` bootstrap | — | minimal Avalonia app shell for VM tests that touch Avalonia primitives |
| e2e / GUI | none — deliberately excluded | n/a | see §7 (GUI is out of scope by interview Q5) |
| (optional) AI-native | mutation testing tool, e.g. Stryker.NET — checked: 2026-06-06 | none yet — see §3 Phase 3 | **When NOT to use:** never on GUI/AXAML or the whole repo; scope to the domain logic the suite claims to protect. Version + current usage to be grounded via Context7 / Microsoft Learn when Phase 3 opens. |

**Stack grounding tools (current session):**
- Docs: Context7 — available; will ground the mutation-testing tool's current API/setup when §3 Phase 3 opens; checked: 2026-06-06
- Docs: Microsoft Learn — available; will ground .NET 10 / xUnit / `System.IO.Abstractions` guidance per phase; checked: 2026-06-06
- Search: none — not available in current session; checked: 2026-06-06
- Runtime/browser: Playwright/browser MCP — not available in current session (consistent with GUI being out of scope); checked: 2026-06-06
- Provider/platform: GitHub — release workflow (`.github/workflows/release.yml`) already exists; relevant to §3 Phase 4 gate wiring; checked: 2026-06-06

## 5. Quality Gates

"Required after §3 Phase <N>" means the gate is enforced once that rollout
phase lands; before that, the gate is `planned`.

| Gate | Where | Required? | Catches |
|------|-------|-----------|---------|
| build (`dotnet build`) | local + CI | required | compile / type drift |
| format (`dotnet format --verify-no-changes`) | local + CI | required after §3 Phase 4 | style / convention drift |
| unit + integration (`dotnet test`) | local + CI | required after §3 Phase 1 | logic regressions in template + file-safety paths |
| mutation-score threshold | CI on PR | optional after §3 Phase 3 | tests that pass without actually catching regressions |
| post-edit hook (run affected tests) | local (agent loop) | recommended after §3 Phase 4 | regressions at edit time |
| GUI / e2e | — | not planned | (excluded per §7) |

## 6. Cookbook Patterns

How to add new tests in this project. Each sub-section is filled in once
the relevant rollout phase ships; before that, the sub-section reads
"TBD — see §3 Phase <N>."

### 6.1 Adding a unit test

- **Location:** one test file per SUT under `Notes.Tests/`, named `<Sut>Tests.cs`
  (e.g. `TemplateRendererTests.cs`, `TemplateParserTests.cs`). Extend the
  existing file for the SUT — do not add a parallel file.
- **Construction:** pure-engine SUTs (`TemplateParser`, `TemplateRenderer`,
  `NameValidator`) are `new`-ed directly in the test — there is no DI container
  in tests.
- **Naming:** `Method_WhenScenario_ExpectedBehaviour` — three PascalCase
  segments, the expected-behaviour segment leading with a verb (`Returns`,
  `Substitutes`, `Throws`). Use `WhenCalled` when asserting a general property.
- **Oracle rule:** build the expected value *independently* from the inputs
  (template + definition + values); never paste the SUT's own output back as the
  assertion ("it returned a string ⇒ it's correct" is the anti-pattern). Drive
  multi-case checks with `[Theory]` + `[InlineData]`/`[MemberData]`.
- **Reference test:**
  `TemplateRendererTests.Render_WhenBodyContainsMixedTokens_OnlyDeclaredNamesAreSubstituted`
  — a `[MemberData]` theory whose `Expected` string is constructed from the
  inputs, plus a `DoesNotContain` loop over every declared token proving zero
  leftover `{{name}}` survivors.
- **Run:** `dotnet test`.

### 6.2 Adding an integration test (service touching disk)

**Pattern:** Drive a real service (or the VM → service path) through a `MockFileSystem` pre-seeded
with known state, assert the stored FS content as the oracle — never re-derive the expected value
from the SUT's own output.

**Shape (collision guard / file-safety tests):**

1. Declare expected content as a fixed constant before the test body — not derived from renderer
   output, not read back from the SUT.
2. Pre-seed `MockFileSystem` with the existing file:
   `_fileSystem.AddFile(path, new MockFileData(expectedContent))` — so real services (e.g.
   `NameValidator`) see the file exists via `IFileSystem.File.Exists`.
3. Where an `InMemoryNoteFileService` or other fake tracks written content separately, mirror the
   pre-seeded value there too; it is the post-assertion source of truth for the "was it overwritten?"
   check.
4. Drive through the real entry point (message or `RelayCommand`) — never call the service directly.
5. Assert the stored content equals the pre-seeded constant (unchanged) or the expected new value.

**Accepted residual — #3 TOCTOU window:** The collision guard (`NameValidator.ValidateNoteName` →
`File.Exists` at `NameValidator.cs:31`, enforced at `NoteTreeViewModel.cs:200`) is a
check-then-write. A concurrent rename between the check and the `Save` call could still overwrite an
existing note. This window is accepted as residual for a single-user desktop app: closing it would
require an atomic create-if-not-exists OS primitive not available through `IFileSystem`, and the
product has no concurrent writers in normal usage. The test pins the guard on the happy path; the
TOCTOU gap is documented here, not fixed.

**Reference test:**
`NoteTreeViewModelTests.Receive_WhenNewNoteNameCollidesWithExisting_DoesNotOverwriteOriginal`

**Shape (durability / fault injection):**

When `MockFileSystem` cannot reproduce the failure mode (e.g. its in-memory `WriteAllText` is atomic),
use `ThrowingFileSystem` (`Notes.Tests/Fakes/ThrowingFileSystem.cs`): a thin NSubstitute-backed
`IFileSystem` decorator that wraps `MockFileSystem` and throws `IOException` on a configurable
operation (`WriteAllText` or `Move`). The wrapped inner `MockFileSystem` reflects all writes that
succeeded before the fault, so post-fault assertions read real FS state rather than mocked returns.

Reference: `NoteFileServiceTests.Save_WhenWriteFaultsBeforeRename_LeavesOriginalIntact`

**Shape (service-layer path containment):**

Containment tests use a real `PathGuard` fed via a stubbed `ISettingsService`:

```csharp
var settings = Substitute.For<ISettingsService>();
settings.CurrentWorkspacePath.Returns("/workspace");
var guard = new PathGuard(settings);
var svc = new NoteFileService(mockFs, guard);
```

Assert that crafted out-of-root paths (`/etc/passwd`, `/workspace-evil/…`, traversal `/../…`) throw
`PathContainmentException` and leave the filesystem unchanged (independent oracle: re-read the path).
Always include an in-root happy-path case to catch false positives.

Reference: `NoteFileServiceTests.Save_WhenPathOutsideWorkspace_ThrowsPathContainmentException`,
`NoteDeleterTests.Delete_WhenPathOutsideWorkspace_ThrowsAndLeavesFilesUntouched`

### 6.3 Adding a test for a new template field type

The field-type decision spans two boundaries; test both.

- **Parse boundary (`TemplateParser`):** the parser keeps the raw `type:` string
  and `entries:` verbatim on `FormField` and does **not** validate the type — a
  missing type stays an empty string, an unknown type passes through. Reference:
  `TemplateParserTests.Parse_WhenFieldMissingType_HasEmptyType` and
  `Parse_WhenSelectFieldHasNoEntries_EntriesIsNullOrEmpty`.
- **Form boundary (`TemplateFormViewModel.CreateField`,
  `Notes/ViewModels/TemplateFormViewModel.cs:58-68`):** the lower-cased `type:`
  string is `switch`-mapped to a `FieldVm` subtype. The recognized keywords are
  `date`, `number`, and **`select`**; everything else — including an empty or
  missing type — falls through to `TextFieldVm`. Adding a field type means a new
  `switch` arm here plus a `FieldVm` subclass. Reference:
  `TemplateFormViewModelTests.Load_WhenSelect_PassesEntriesThrough` (recognized
  keyword → `SelectFieldVm`, entries passed through),
  `Load_WhenTypeUnknown_FallsBackToTextField` (unknown keyword → `TextFieldVm`),
  and `Load_WhenSelectHasNoEntries_CreatesSelectVmWithEmptyChoicesAndEmptyRenderValue`.
- **Always pin both** the recognized-keyword happy path *and* the unknown/missing
  fallback, so a typo'd keyword cannot silently degrade to free text unnoticed.
- **Run:** `dotnet test`.

### 6.4 Adding a view-model test

- **Default shape:** `new` the VM directly under a plain `[Fact]`. Wire
  collaborators as: a **fresh `StrongReferenceMessenger` per test** (never the
  static `WeakReferenceMessenger.Default`), **NSubstitute** doubles for
  dialog/service interfaces (`Substitute.For<I…>()`), and **`MockFileSystem`** (or
  an in-process fake such as `InMemoryNoteFileService`) for anything touching
  disk — never the real file system.
- **Drive through the real entry point:** send a message or execute a
  `RelayCommand`, then assert on observable output (a published message, the mock
  FS's stored file content) — not on internal VM state.
- **Avalonia escalation:** use `[AvaloniaFact]` + the `TestApp.cs` bootstrap
  **only** when the VM (or a child VM) touches Avalonia primitives; reference for
  that path is `NoteSearchViewModelTests.cs`. The template-pipeline VMs do not, so
  they stay on plain `[Fact]`.
- **Reference tests:**
  `NoteTreeViewModelTests.Receive_WhenMalformedTemplate_SkipsFormDialogAndSavesStaticBody`
  and `Receive_WhenFormSubmittedBlank_SavesNoteWithNoLeftoverDeclaredTokens` —
  message-driven, NSubstitute dialog doubles, asserting the content actually
  written to the mock file service.
- **Run:** `dotnet test`.

### 6.5 Validating that a test actually catches regressions

- TBD — see §3 Phase 3 (mutation testing scoped to template + file-safety logic).

### 6.6 Per-rollout-phase notes

(Optional. After each phase lands, `/10x-implement` appends a 2–3 line note
here capturing anything surprising the rollout phase taught.)

- **Phase 1 (template pipeline correctness):** the field-type keyword recognized
  by `TemplateFormViewModel.CreateField` is **`select`**, not `dropdown` — an
  early plan note had it backwards. Every other `type:` (including missing/empty)
  silently resolves to `TextFieldVm`, so each new field type needs both a
  recognized-keyword test and an unknown-type fallback test (see §6.3).
- **Phase 2 (file-safety guardrails):** the delete path (`NoteDeleter`) is the
  sharpest edge of #5 — it bypasses `NameValidator` entirely and was completely
  unguarded before `PathGuard`. The `PathContainmentException` must derive from
  `IOException` so the existing `NoteEditorViewModel.DoSave` catch absorbs a
  guard rejection rather than letting it escape the `DispatcherTimer` callback.
  When adding guard tests, always include the delete-path case explicitly — it is
  the most likely spot where a future refactor re-opens the gap.

## 7. What We Deliberately Don't Test

Exclusions agreed during the rollout (Phase 2 interview, Q5). Future
contributors should respect these unless the underlying assumption changes.

- **GUI / AXAML layout and visual styling** — low value, breaks constantly; the MVVM split keeps logic in testable view models. Re-evaluate if visual regressions start shipping. (Source: Phase 2 interview Q5.)
- **Avalonia framework behavior** (bindings, the folder-picker dialog internals) — trust the framework; test our usage of it, not the framework itself. (Source: Phase 2 interview Q5.)
- **Full end-to-end GUI automation** — too heavy for a solo desktop app; the data-loss and correctness guarantees are reachable at unit/integration layers. Re-evaluate if a critical flow can only fail through the assembled UI. (Source: Phase 2 interview Q5.)
- **YAML / markdown library internals** — test our parsing/rendering usage and edge handling, not the third-party parser's own correctness. (Source: Phase 1 cost × signal.)

## 8. Freshness Ledger

- Strategy (§1–§5) last reviewed: 2026-06-06
- Stack versions last verified: 2026-06-06
- AI-native tool references last verified: 2026-06-06

Refresh (`/10x-test-plan --refresh`) when:

- a new top-3 risk surfaces from the roadmap or archive,
- a recommended tool's `checked:` date is older than three months,
- the project's tech stack changes (new framework, new test runner),
- §7 negative-space no longer matches what the team believes.
