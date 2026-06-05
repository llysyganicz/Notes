<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Note-tree Folder Management

- **Plan**: context/changes/note-tree-folder-management/plan.md
- **Scope**: Phase 3 of 4 (Folder Delete)
- **Date**: 2026-06-03
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS (build clean; 116/116 tests green) |

## Findings

### F1 — Folder-delete branch has no unit test

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes.Tests/NoteTreeViewModelTests.cs (new test); exercises NoteTreeViewModel.cs:148-201
- **Detail**: The file-delete path was unit-tested (`DeleteNote_WhenConfirmed_PublishesNoteDeletedMessage` / `_RefreshesTree`) but the new folder branch — `DeleteFolder` call, recursive `DescendantFileNodes` fan-out of one `NoteDeletedMessage` per contained file, and the root-rejection in `CanDeleteNote` — had no equivalent coverage. The plan named the fan-out helper "the only non-obvious piece," exactly the logic most worth pinning. `StubNoteDeleter` already grew a `DeletedFolders` list but nothing asserted against it.
- **Fix**: Added `DeleteNote_WhenFolderConfirmed_DeletesFolderAndSendsMessagePerDescendantFile` (asserts folder path in `DeletedFolders` and one message per descendant file across nested folders) and `DeleteNote_WhenRootFolder_CannotExecute` (root with empty `RelativePath` → `CanExecute` false; non-root folder → true). Mirrors the existing delete tests' structure.
- **Decision**: FIXED (via Fix now) — landed alongside Phase 3; suite now 116/116.

## Deliberate deviations (not findings)

- **`CanDeleteNote` simplified** to `!string.IsNullOrEmpty(node?.RelativePath)` instead of the plan's explicit kind+path check — user-directed during this phase, logically equivalent (files always carry a path; the synthetic root is the only empty-path node).
- **`relative`/`absolutePath` hoisted** above the file/folder branch to avoid duplication — behavior identical to the plan's per-branch computation.

## Commit

- Phase 3 landed in `bac88ca` — `feat(note-tree-folder-management): folder delete (p3)`. The F1 test fix is a follow-on edit in the working copy, to fold into the next commit.
