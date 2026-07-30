<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Insert a rendered template body into an existing note at the cursor

- **Plan**: context/changes/templates-insert-into-note/plan.md
- **Scope**: Phase 2 of 4
- **Date**: 2026-07-30
- **Verdict**: APPROVED
- **Findings**: 0 critical 0 warnings 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Unplanned E2E test service registration

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: Notes.E2ETests/E2ETestBase.cs:257
- **Detail**: The phase 2 plan did not list `Notes.E2ETests/E2ETestBase.cs`, but the diff updates it to register `ITemplateService`. The E2E harness builds its own service provider, so the new `TemplateService` must be registered there for `NoteTreeViewModel` to resolve. The change is necessary and benign, but it was omitted from the plan's "Changes Required" list.
- **Fix**: Add `Notes.E2ETests/E2ETestBase.cs` to the phase 2 "Changes Required" section as a discovered dependency, or accept it as an implicit DI-registration update.
- **Decision**: FIXED — added item 6 to Phase 2 "Changes Required" in plan.md.

### F2 — Obsolete `_templateCatalog.List()` stubs in template tests

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes.Tests/NoteTreeViewModelTests.cs (template-flow tests)
- **Detail**: After `NoteTreeViewModel` delegates the template orchestration to `TemplateService`, the template-flow tests still stub `_templateCatalog.List()` even though the ViewModel no longer calls it directly on the template path. The stubs are harmless but make the tests harder to maintain and could confuse future reviewers about the actual SUT dependencies.
- **Fix**: Remove the `_templateCatalog.List()` stubs from the template-flow tests (e.g., `Receive_WhenNewFromTemplateRequested_RendersTemplateAndSavesNote`, `Receive_WhenTemplateServiceReturnsNull_CreatesNoNote`, `Receive_WhenNoTemplatesAvailable_CreatesNoNote`, `Receive_WhenTemplateServiceReturnsStaticBody_SavesStaticBody`, `Receive_WhenTemplateServiceReturnsBlank_SavesNoteWithNoLeftoverDeclaredTokens`).
- **Decision**: FIXED — removed the obsolete `_templateCatalog.List()` stubs from `Notes.Tests/NoteTreeViewModelTests.cs`; full suite still passes (225 + 61 + 8).

## Verification Log

- `dotnet test` (full suite) → passed: 225 Core + 61 Notes + 8 E2E tests.
- Existing `NoteTreeViewModel` template tests → all passed after refactor to use `ITemplateService`.

## Notes

- Commit `2abb676` covers the planned phase 2 files: `ITemplateService.cs`, `TemplateService.cs`, `Notes/Program.cs`, `Notes/ViewModels/NoteTreeViewModel.cs`, and `Notes.Tests/NoteTreeViewModelTests.cs`.
- `TemplateService` correctly mirrors the original `NoteTreeViewModel.HandleNewFromTemplate` behavior: empty catalog returns `null`, picker/form cancellation returns `null`, zero-field templates skip the form dialog, and the full rendered text is returned for the new-note path.
- `NoteTreeViewModel` still retains `_templateCatalog` because it is needed for `RefreshTemplateCatalog` / `TemplatesChangedMessage`; the constructor signature is therefore still correct.
