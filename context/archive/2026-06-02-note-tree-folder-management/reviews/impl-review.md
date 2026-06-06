<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Note-tree Folder Management

- **Plan**: context/changes/note-tree-folder-management/plan.md
- **Scope**: Full plan (Phases 1-4); Phase 4 reviewed fresh, Phases 1-3 prior-APPROVED
- **Date**: 2026-06-03
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Unguarded Directory.CreateDirectory on the context-menu path

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: Notes/Services/NoteFolderService.cs:14; Notes/ViewModels/NoteTreeViewModel.cs:97-99
- **Detail**: Directory.CreateDirectory can throw (IOException / UnauthorizedAccess / PathTooLong) on a read-only or permission-restricted workspace. The message path (Receive → HandleNewFolder) is wrapped in the async-void try/catch, but the `[RelayCommand] NewFolder(node)` context-menu path is not — a throw lands in AsyncRelayCommand's ExecutionTask and fails silently. NOT a Phase 4 regression: existing NewNote/DeleteNote relay commands call IO services the same unguarded way (established codebase convention).
- **Fix**: Codebase-wide (separate change) — surface IO failures via a dialog rather than silent swallow. Out of scope for this phase.
- **Decision**: SKIPPED — matches existing NewNote/DeleteNote convention; deferred to a dedicated IO-error-surfacing change.

### F2 — NoteNameResult.Success.FileName carries a folder name

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Pattern Consistency
- **Location**: Notes/Services/INameValidator.cs:13
- **Detail**: The unified validator returns `NoteNameResult.Success(FileName, ...)` for both notes and folders, so for folders FileName holds a directory name. Value is correct; the property name reads note-centric. Renaming to `Name` would ripple to unrelated search-type `.FileName` members, so leaving it is lower-risk.
- **Fix**: None / leave as-is.
- **Decision**: SKIPPED — cosmetic only; value is correct.

### F3 — Indirect assertion in WithoutWorkspace folder test

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Success Criteria (test quality)
- **Location**: Notes.Tests/NoteTreeViewModelTests.cs:274
- **Detail**: `Receive_WhenNewFolderRequestedWithoutWorkspace` asserts `!Directory.Exists(Workspace)`. Passes only because the workspace dir is never created — an indirect proxy for "no folder was created". A direct assertion on the target path would read more clearly.
- **Fix**: Optionally assert the specific intended folder path does not exist.
- **Decision**: FIXED (via Fix now) — now asserts `!Directory.Exists(Path.Combine(Workspace, "ideas"))`.
