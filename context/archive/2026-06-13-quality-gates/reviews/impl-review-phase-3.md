<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Quality-Gates Wiring

- **Plan**: context/changes/quality-gates/plan.md
- **Scope**: Phase 3 of 3
- **Date**: 2026-06-13
- **Verdict**: APPROVED
- **Findings**: 0 critical  2 warnings  3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Notes

Phases 1 and 2 reviewed separately (both APPROVED). Known post-review amendments not re-examined: `.gitattributes` (Phase 1 F1 FIXED), `permissions: contents: read` in ci.yml (Phase 2 F1 FIXED). The 4 manual success criteria (3.5–3.8) are properly marked pending — they require a real PR/push environment and were not rubber-stamped.

Automated verification: `dotnet format --verify-no-changes` exit 0, `dotnet build` 0 warnings/errors, 282/282 tests pass.

## Findings

### F1 — Single-star glob misses subdirectory test files

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .claude/hooks/run-related-tests.sh:34–36
- **Detail**: The "directly-edited test file" branch used single-star globs (`Notes.Core.Tests/*Tests.cs`, `Notes.Tests/*Tests.cs`) which match only flat-root files. A test file in a subdirectory would fall through to the file-existence probes, silently using the wrong hook branch. Current layout is fully flat so this was latent.
- **Fix**: Replaced with prefix+suffix pattern matches: `[[ "$rel" == Notes.Core.Tests/* && "$base" == *Tests ]]` and `[[ "$rel" == Notes.Tests/* && "$base" == *Tests ]]`.
- **Decision**: FIXED

### F2 — run-format-check.sh assumes packages are restored

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .claude/hooks/run-format-check.sh:28
- **Detail**: The hook passed `--no-restore` to `dotnet format`. On a fresh checkout or after a package update before the first build, packages are stale and the hook fails with a restore error rather than a format-violation message. Unplanned addition; benign in practice (test hook has same implicit assumption via `--no-build`).
- **Fix**: Added a comment documenting the assumption: `# --no-restore: assumes packages are already restored (run 'dotnet restore' first on a fresh checkout).`
- **Decision**: FIXED

### F3 — Outside-repo path guard absent in both hooks

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: run-format-check.sh:24, run-related-tests.sh:29
- **Detail**: `rel` was derived via `${file#"$REPO_ROOT"/}`. If the edited file is outside `$REPO_ROOT`, no prefix is stripped and `rel` equals the full absolute path; the hook silently becomes a no-op. Theoretical in Claude Code sessions.
- **Fix**: Added `[ "$rel" = "$file" ] && exit 0  # outside repo, skip` after the `rel` computation in both hooks.
- **Decision**: FIXED

### F4 — Unquoted variable in format-hook error hint

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: .claude/hooks/run-format-check.sh:35
- **Detail**: The remediation hint echoes `${rel}` unquoted. Cosmetic only; no functional impact.
- **Decision**: SKIPPED

### F5 — AGENTS.md added but not in plan

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: AGENTS.md (commit 3ddc037)
- **Detail**: AGENTS.md appears in the diff but is not mentioned in any phase. Contents duplicate CLAUDE.md — a parallel entry point for agent toolchains that look for AGENTS.md. No executable changes, no gate-bypassing content. Benign scope creep.
- **Decision**: SKIPPED
