<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Insert a rendered template body into an existing note at the cursor

- **Plan**: context/changes/templates-insert-into-note/plan.md
- **Scope**: All 4 phases
- **Date**: 2026-07-29
- **Verdict**: APPROVED
- **Findings**: 0 critical 0 warnings 3 observations

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

### F1 — Unplanned InternalsVisibleTo in Notes/Notes.csproj

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: Notes/Notes.csproj
- **Detail**: The diff adds `<InternalsVisibleTo>` entries for `Notes.Tests` and `Notes.E2ETests` so the headless tests can subscribe to the internal `InsertAtCaretRequested` event. This file was not listed in any phase's "Changes Required" section. The change is necessary and benign (tests cannot compile against the internal event without it), but it was an unplanned project-file change.
- **Fix**: Add `Notes/Notes.csproj` to Phase 3 "Changes Required" as a discovered test-visibility dependency.
- **Decision**: FIXED — added item 5 to Phase 3 "Changes Required" in plan.md.

### F2 — View-side insert uses SelectionStart instead of specified CaretOffset

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Notes/Views/NoteEditorView.axaml.cs:OnInsertAtCaretRequested
- **Detail**: Phase 3 specified `Editor.Document.Replace(Editor.CaretOffset, Editor.SelectionLength, body)`. The implementation uses `Editor.SelectionStart` as the offset. Using `CaretOffset` would replace the wrong range for typical left-to-right selections (caret is at the selection end), so the implementation is more correct than the plan's literal contract.
- **Fix**: Update the Phase 3 contract to document the `SelectionStart`-based replacement and the post-insert caret placement.
- **Decision**: FIXED — updated Phase 3 "View code-behind insert handler" contract in plan.md.

### F3 — RenderBody trims leading blank lines beyond plan contract

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Notes.Core/Services/TemplateRenderer.cs:RenderBody / Notes.Core/Services/TemplateRenderer.cs:TrimLeadingEmptyLines
- **Detail**: Phase 1 said `RenderBody` returns "the lines after the closing frontmatter fence as the body." The implementation strips leading empty lines so a template that separates the fence from the body with blank lines does not inject leading whitespace into the open note. The behavior is covered by tests (`RenderBody_WhenBlankLineAfterFence_TrimsLeadingEmptyLines` and variants) and is a sensible UX refinement, but it was not in the original contract.
- **Fix**: Document the leading-empty-line trim in the Phase 1 contract as an intentional UX refinement.
- **Decision**: FIXED — added the trimming behavior to the Phase 1 "Core renderer implementation" contract in plan.md.

## Verification Log

- `dotnet test` → PASS (229 Core + 64 Notes + 8 E2E = 301 total)
- Existing `NoteTreeViewModel` template tests → PASS
- New `RenderBody` tests → PASS
- `NoteEditorViewModelTests` insert-from-template tests → PASS

## Notes

- Per-phase reviews for phases 1 and 2 were consulted; all prior findings were FIXED and the plan was updated accordingly.
- All findings from this full-plan review were plan-documentation updates only; no code changes were required.
