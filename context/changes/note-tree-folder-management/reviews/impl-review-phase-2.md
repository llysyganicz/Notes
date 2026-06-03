<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Note-tree Folder Management

- **Plan**: context/changes/note-tree-folder-management/plan.md
- **Scope**: Phase 2 of 4 (Directory-aware Tree)
- **Date**: 2026-06-03
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS (1 documented out-of-scope fix, user-directed) |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS (build clean; 114/114 tests green) |

## Findings

### F1 — Workspace reload walks the tree twice

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; no action needed this phase
- **Dimension**: Performance
- **Location**: Notes/Services/NoteTreeBuilder.cs:24-47 (caller NoteTreeViewModel.cs:131-132)
- **Detail**: `LoadTree` does one recursive `EnumerateFiles` walk (scanner) and then `NoteTreeBuilder.Build` does a second recursive `EnumerateDirectories` walk of the same tree — two independent stat-walks per reload. Negligible at note-vault scale; the plan's "Performance Considerations" section already documents and accepts this. Only matters on very large/deep trees or networked filesystems.
- **Fix**: None required. If reload latency ever regresses, fold file + directory enumeration into a single pass. Documented trade-off.
- **Decision**: SKIPPED (accepted documented trade-off)

## Deliberate deviations (not findings)

- **NoteSearchIndex `cts.Dispose()` removal** (Notes/Services/NoteSearchIndex.cs): exactly the remedy prescribed by `context/foundation/lessons.md` ("Don't dispose a CancellationTokenSource shared with an in-flight task", option a — rely on the finalizer). Fixes the `ObjectDisposedException`-on-workspace-change crash. Out of the plan's stated Phase 2 scope ("search-index build path untouched") but user-directed during manual verification and correctly attributed in commit 2b34711. Treat as the intended state, not a leak.
- **Dot-directories shown while dot-files skipped**: intentional asymmetry required by the dependent `templates` change; matches manual check 2.4.

## Commit

- Phase 2 landed in `2b34711` — `feat(note-tree-folder-management): directory-aware tree (p2)`.
