<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: File-safety & data-loss guardrails

- **Plan**: context/changes/file-safety/plan.md
- **Scope**: Phases 1–3 of 3 (full plan)
- **Date**: 2026-06-08
- **Verdict**: NEEDS ATTENTION → all findings resolved
- **Findings**: 0 critical  2 warnings  2 observations
- **Prior**: Phase 2 report consulted (F1 FIXED, F2–F4 SKIPPED)

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Read paths not guarded by PathGuard

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/NoteFileService.cs:19-36 (Read and ReadAsync)
- **Detail**: The plan guarded Save, Create, Delete, DeleteFolder but left Read unguarded. Current callers all derive paths safely via Path.Combine(_workspacePath, relative). No immediate exploitation vector, but no structural enforcement stops a future caller from passing an out-of-workspace path to Read/ReadAsync.
- **Fix A ⭐ Recommended**: Add EnsureWithinWorkspace to Read and ReadAsync
  - Strength: Completes the symmetry — every disk operation on NoteFileService is confined.
  - Tradeoff: Future cross-workspace reads would need a separate read path.
  - Confidence: HIGH — all current callers pass workspace-rooted paths.
  - Blind spot: Should verify NoteSearchIndex's ReadAsync always passes a workspace-rooted path.
- **Fix B**: Document the deliberate exclusion with a comment on the interface
  - Strength: Preserves flexibility for future cross-workspace reads.
  - Tradeoff: Gap remains.
  - Confidence: MEDIUM
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A — added EnsureWithinWorkspace to Read and ReadAsync; added 4 containment tests (Read_WhenPathOutsideWorkspace_ThrowsPathContainmentException, Read_WhenPathInsideWorkspace_ReturnsContent, ReadAsync equivalents). 276/276 tests green.

### F2 — OrphanedTempCleaner swallows per-file errors silently

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality / Pattern Consistency
- **Location**: Notes/Services/OrphanedTempCleaner.cs:29
- **Detail**: Empty catch { } swallowed per-file delete failures with no trace. The rest of the codebase logs before swallowing (NoteSearchIndex.cs:197, NoteEditorViewModel.cs:169-173).
- **Fix**: Add Trace.WriteLine inside catch, mirroring the codebase pattern.
- **Decision**: FIXED — added `catch (Exception ex) { Trace.WriteLine($"OrphanedTempCleaner: could not delete '{tmpFile}': {ex.Message}"); }`.

### F3 — IOrphanedTempCleaner is an empty marker interface

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes/Services/IOrphanedTempCleaner.cs
- **Detail**: Interface declared no members. Existed solely for DI eager-resolve. Every other service interface exposes at least one method.
- **Fix**: Remove the interface; register OrphanedTempCleaner as the concrete type directly.
- **Decision**: FIXED — interface deleted, Program.cs updated to `services.AddSingleton<OrphanedTempCleaner>()`, App.axaml.cs updated to resolve `OrphanedTempCleaner` directly.

### F4 — NoteDeleterTests uses real PathGuard; NoteFileServiceTests uses stub

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes.Tests/NoteDeleterTests.cs:15-18
- **Detail**: NoteDeleterTests instantiates new PathGuard(settings) directly (integration-style), while NoteFileServiceTests uses Substitute.For<IPathGuard>(). Both are defensible — the deleter tests double as integration coverage for the guard-deleter pair. Confirmed intentional after inspection.
- **Fix**: No change.
- **Decision**: SKIPPED — intentional integration-style test for the guard-deleter pair.
