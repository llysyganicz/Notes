<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: File-safety & data-loss guardrails

- **Plan**: context/changes/file-safety/plan.md
- **Scope**: Phase 2 of 3
- **Date**: 2026-06-08
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical  2 warnings  2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Test name packs two assertions into the expected-behaviour segment

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes.Tests/OrphanedTempCleanerTests.cs:12
- **Detail**: `Receive_WhenWorkspaceContainsOrphanedTemps_DeletesTempFilesAndPreservesNotes` packs two outcomes into the expected-behaviour segment. Convention requires a single leading verb on that segment.
- **Fix**: Rename to `Receive_WhenWorkspaceContainsOrphanedTemps_DeletesTempFiles`.
- **Decision**: FIXED — renamed test method

### F2 — ThrowingFileSystem write-then-throw semantic is undocumented

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (test infrastructure)
- **Location**: Notes.Tests/Fakes/ThrowingFileSystem.cs:25-31
- **Detail**: When `throwOnWriteAllText = true`, the `Do` callback writes the temp file to `inner.MockFileSystem` before throwing — intentional, but undocumented. A future author misreading the semantics could silently break the cleanup assertion.
- **Fix**: Add a one-line comment at line 28 explaining the write-then-throw is intentional.
- **Decision**: SKIPPED

### F3 — Null-forgiving on GetDirectoryName with no precondition guard

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/NoteFileService.cs:40
- **Detail**: `_fileSystem.Path.GetDirectoryName(absolutePath)!` suppresses the nullable warning. On .NET, `GetDirectoryName` returns `""` not `null` for relative paths, and both production callers always pass rooted paths (workspace root + relative via `Path.Combine`). No real risk; Phase 3 PathGuard adds the proper precondition above this code anyway.
- **Fix**: No action — dismissed after reviewing all callers.
- **Decision**: SKIPPED

### F4 — BOM test asserts platform behaviour, not service code

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes.Tests/NoteFileServiceTests.cs:143
- **Detail**: `Read_WhenFileHasBomPrefix_ReturnsBomStrippedContent` verifies `StreamReader`/`MockFileSystem` BOM-stripping — not anything `NoteFileService` does.
- **Fix**: Add a one-line comment clarifying this tests platform behaviour, not service logic.
- **Decision**: SKIPPED
