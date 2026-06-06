<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Templates — Note-from-Template (Phase 2)

- **Plan**: context/changes/templates/plan.md
- **Scope**: Phase 2 of 3
- **Date**: 2026-06-05
- **Verdict**: APPROVED
- **Findings**: 0 critical · 1 warning · 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS (automated; manual 2.5–2.7 pending, deferred to Phase 3) |

Automated criteria: `dotnet build` → 0 errors; `dotnet test` → 182 passed.

## Findings

### F1 — Service reads VM via dialog.DataContext! with implicit timing dependency

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/TemplateFormDialogService.cs:22-23
- **Detail**: The service casts `(TemplateFormViewModel)dialog.DataContext!`. DataContext is wired in AXAML via `{ReflectionBinding Form, Source={StaticResource Locator}}`, which resolves synchronously during InitializeComponent — but the null-forgiving `!` turns any future violation of that assumption into a silent NullReferenceException. Implicit, uncommented view↔service contract.
- **Fix A ⭐ Recommended**: Guard the cast with a typed pattern check that throws InvalidOperationException with an explanatory message if DataContext isn't the expected VM.
  - Strength: Converts a silent NRE into a self-describing failure; documents the seam.
  - Tradeoff: A few extra lines; still depends on the locator binding.
  - Confidence: HIGH — pure defensive guard, no behavior change.
  - Blind spot: None significant.
- **Fix B**: Resolve TemplateFormViewModel from App.Services, assign `dialog.DataContext = vm` in code, drop the locator binding for this dialog.
  - Strength: Removes the timing dependency entirely; explicit VM lifecycle.
  - Tradeoff: Diverges from the locator-DataContext convention requested for this dialog.
  - Confidence: MED — works, but undoes the requested locator wiring.
  - Blind spot: Whether this dialog should keep matching the main views' locator pattern.
- **Decision**: SKIPPED — currently safe (StaticResource binding resolves synchronously during InitializeComponent).

### F2 — Increment="1" set statically on every NumericUpDown

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes/Views/TemplateFormDialog.axaml:30
- **Detail**: The plan tied Increment="1" to the no-decimal (integer-format) case; it's applied unconditionally. Harmless — Increment only sets the spinner step; decimal fields still accept typed fractional input via ParsingNumberStyle=Number.
- **Fix**: Leave as-is (default Increment is already 1), or bind Increment to a VM property if per-field step ever matters. No action needed now.
- **Decision**: SKIPPED — harmless; default Increment is 1.

### F3 — Duplicate field names would throw in Submit's ToDictionary

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/ViewModels/TemplateFormViewModel.cs:47
- **Detail**: Submit() builds the result map via ToDictionary(StringComparer.Ordinal). Two fields with the same name would throw ArgumentException. The Phase 1 parser deserializes a YAML map (unique keys), so this is a boundary assumption, not a live bug — confirm it holds when Phase 3 wires the real parse→form path.
- **Fix**: No change now; verify the Phase 1 parse path guarantees unique field names during Phase 3 orchestration testing.
- **Decision**: DEFERRED — queued as follow-up FU-1 (`follow-ups/review-fixes.md`); add form-definition validation-on-save to the roadmap for future implementation.

### F4 — Test names say "Construct_" but exercise Load()

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes.Tests/TemplateFormViewModelTests.cs:21,38,55,68,78
- **Detail**: After the transient-VM refactor, the SUT is built via Load(definition) (BuildSut), but several tests still use the Construct_When… prefix. The behavior under test is Load(), so Load_When… reads more accurately.
- **Fix**: Optionally rename Construct_* → Load_* for accuracy. Trivial.
- **Decision**: FIXED — renamed the five Construct_* tests to Load_*; 182 tests pass.
