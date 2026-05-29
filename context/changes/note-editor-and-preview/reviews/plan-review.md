<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Create, Edit, and Preview Markdown Notes

- **Plan**: `context/changes/note-editor-and-preview/plan.md`
- **Mode**: Deep
- **Date**: 2026-05-28
- **Verdict**: REVISE
- **Findings**: 1 critical, 1 warning, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | WARNING |
| Plan Completeness | FAIL |

## Grounding

7/7 paths exist ✓, 3/3 symbols verified against current code ✓, brief↔plan consistent ✓. No `lessons.md`, no `contract-surfaces.md`.

## Findings

### F1 — Tree context-menu binding pattern claim is wrong

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — fix is obvious; one AXAML line
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §10 (Tree UserControl extraction) Contract
- **Detail**: Plan claims the UserControl's DataContext lets `{Binding DeleteNoteCommand}` work directly inside the `TreeDataTemplate`. Inside a templated item, the local DataContext is the bound `NoteTreeNode`, not the UserControl — binding silently fails, Delete menu does nothing. S-01's `MainWindow.axaml` confirms the escape pattern `$parent[TreeView].((vm:MainWindowViewModel)DataContext).DeleteNoteCommand` is required precisely because of this scoping.
- **Fix**: Keep the escape; update only the cast type to `NoteTreeViewModel`:
  `Command="{Binding $parent[TreeView].((vm:NoteTreeViewModel)DataContext).DeleteNoteCommand}"`
- **Decision**: FIXED

### F2 — async-void Receive handler has no exception safety

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 §4 (Tree VM handles NewNoteRequestedMessage)
- **Detail**: `IRecipient<TMessage>.Receive` returns void, forcing `async void Receive(...)`. `HandleNewNote()` does dialog + `File.WriteAllText` + workspace rescan; any throw escalates to the UI sync context and crashes the app. Plan doesn't specify exception handling.
- **Fix**: Wrap `HandleNewNote()` body in try-catch. Log on failure; either swallow (matching S-01's `SettingsService.Load` tolerance) or display a brief error via the confirm dialog.
  - Strength: Matches existing fault-tolerance posture; prevents one bad write from crashing the app.
  - Tradeoff: Silent failures can confuse users; adding an error dialog grows the UX surface slightly.
  - Confidence: HIGH — well-known .NET behavior.
  - Blind spot: Avalonia 12 may have improved async-void handler integration; if so the wrapper is belt-and-suspenders.
- **Decision**: SKIPPED

### F3 — Phase 2 step 8 bypasses INoteFileService

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 §4 step 8
- **Detail**: The new-note flow calls `File.WriteAllText(...)` directly instead of going through `INoteFileService.Save`, the abstraction Phase 1 introduces for all writes.
- **Fix**: Inject `INoteFileService` into `NoteTreeViewModel`; replace the direct call with `_fileService.Save(absolutePath, "")`.
- **Decision**: FIXED

### F4 — `Roundtrip_WhenCalled_PreservesContent` violates naming convention

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §14 NoteFileServiceTests
- **Detail**: Convention is `Method_WhenScenario_ExpectedBehaviour`. `Roundtrip` isn't a method on `INoteFileService`.
- **Fix**: Rename to `Save_WhenFollowedByRead_RoundtripsContent`.
- **Decision**: FIXED

### F5 — Bundled new-note variant tests obscure failure signal

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 §7 NoteTreeViewModelTests extension
- **Detail**: "(folder-selected, file-selected, no-selection variants)" collapses three branches of `HandleNewNote` into one test name.
- **Fix**: Split into three named tests: `Receive_WhenNewNoteRequestedMessageWithFolderSelected_CreatesInThatFolder`, `Receive_WhenNewNoteRequestedMessageWithFileSelected_CreatesInFileParentFolder`, `Receive_WhenNewNoteRequestedMessageWithNoSelection_CreatesAtWorkspaceRoot`.
- **Decision**: FIXED (via xUnit `[Theory]` + `[InlineData]` per user direction — same test method, three distinct cases reported to the runner).

### F6 — Missing negative test for NoteDeletedMessage routing

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 1 §14 NoteEditorViewModelTests
- **Detail**: Positive case `Receive_WhenNoteDeletedMessageMatchesCurrent_ClearsState` is covered; negative case (delete an unrelated note while editing a different one) isn't.
- **Fix**: Add `Receive_WhenNoteDeletedMessageDoesNotMatchCurrent_LeavesStateUnchanged`.
- **Decision**: FIXED

### F7 — Performance Considerations understates new-note handler work

- **Severity**: 💬 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Performance Considerations section
- **Detail**: Says "Message handlers do at most one file read or a property assignment." The `NewNoteRequestedMessage` handler does: modal dialog + file write + full workspace rescan + tree-walk lookup. Acceptable at PRD `data_volume: small` but doc doesn't match reality.
- **Fix**: Note that the new-note handler is the heaviest message handler (dialog + write + rescan) but stays fast at small data volumes.
- **Decision**: SKIPPED
