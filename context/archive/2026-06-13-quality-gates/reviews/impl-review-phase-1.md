<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Quality-Gates Wiring

- **Plan**: context/changes/quality-gates/plan.md
- **Scope**: Phase 1 of 3
- **Date**: 2026-06-13
- **Verdict**: APPROVED
- **Findings**: 0 critical  1 warning  1 observation

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

### F1 — end_of_line = lf set without .gitattributes

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .editorconfig:5
- **Detail**: end_of_line = lf is pinned in [*] but there was no .gitattributes to enforce LF normalization at the git layer. On Windows, git's default core.autocrlf=true converts to CRLF on checkout regardless of .editorconfig, causing spurious dotnet format --verify-no-changes failures. Mitigating context: solo Linux repo, CI is ubuntu-latest only (Windows CI explicitly ruled out).
- **Fix**: Added .gitattributes with `* text=auto eol=lf`, `*.cs text eol=lf`, `*.axaml text eol=lf`.
- **Decision**: FIXED

### F2 — xUnit1051 pinned as warning, not error (soft gate)

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: .editorconfig:17
- **Detail**: dotnet_diagnostic.xUnit1051.severity = warning means future tests that omit TestContext.Current.CancellationToken will accumulate silently. Matches the plan's conservative intent; will harden automatically if TreatWarningsAsErrors is added in a future phase.
- **Fix**: Change severity to error for a hard gate on xUnit1051.
- **Decision**: SKIPPED

## Success Criteria

### Automated (all pass)

- [x] 1.1 .editorconfig exists at repo root
- [x] 1.2 dotnet format --verify-no-changes → exit 0
- [x] 1.3 dotnet build → exit 0 (0 warnings, 0 errors)
- [x] 1.4 dotnet test → 282 passed (221 core + 61 UI), 0 failed

### Manual (confirmed from diff analysis)

- [x] 1.5 .editorconfig only pins rules the current tree satisfies — sole collateral change was MainWindow.axaml.cs trailing newline from insert_final_newline=true
- [x] 1.6 xUnit1051 fixes semantically correct — 21 search tests + 2 file-service tests; the cancellation-specific test that uses its own CTS was correctly left untouched
