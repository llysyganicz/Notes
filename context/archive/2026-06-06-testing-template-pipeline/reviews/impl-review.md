<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Template Pipeline Correctness Tests

- **Plan**: context/changes/testing-template-pipeline/plan.md
- **Scope**: All 5 phases (complete)
- **Date**: 2026-06-07
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical · 2 warnings · 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Production keyword change (dropdown→select) shipped inside "tests-only" Phase 1

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — code is right; the plan record was stale
- **Dimension**: Scope Discipline / Plan Adherence
- **Location**: Notes/ViewModels/TemplateFormViewModel.cs:65 (commit f647c957)
- **Detail**: Phase 1's Changes Required listed only test files, but the same commit changed production: `CreateField` switch arm `"dropdown" => SelectFieldVm` → `"select" => SelectFieldVm`. Genuine bug fix (parser passes `type:` verbatim; everything uses `select`, so the old `dropdown` arm could never match and select fields silently fell to TextFieldVm). However it inverted the plan's emphatic intent to *pin* the `select→TextFieldVm` trap; only §6.6 recorded the reversal, plan body did not.
- **Fix**: Added an Implementation Addenda entry to plan.md recording the dropdown→select fix superseded the "pin the trap" intent. No code change.
- **Decision**: FIXED (plan addendum added)

### F2 — Real-FS UTF-8-no-BOM regression guard removed against explicit plan instruction

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — two locked constraints collide
- **Dimension**: Plan Adherence / Success Criteria
- **Location**: Notes.Tests/NoteFileServiceTests.cs:37-45 (commit df90aebe)
- **Detail**: Plan stated twice to keep the existing real-FS no-BOM test as the authoritative guard that the simplified default still writes UTF-8 no-BOM (a MockFileSystem assertion "only proves the mock's default"). Phase 3 migrated the whole file to MockFileSystem, making that guard vacuous. .NET 10 no-BOM write / BOM-stripping read empirically verified once on this machine, so today's risk is low; the standing guard is gone. Deviation aligns with the durable "never touch real disk in tests" rule.
- **Fix A ⭐ Recommended**: Keep MockFileSystem-only; document the decision (test comment + plan Phase 3 addendum).
  - Strength: Honors the durable no-real-disk preference; suite stays hermetic; platform behavior verified once.
  - Tradeoff: No automated guard catches a future regression that reintroduces a BOM-emitting write encoding.
  - Confidence: HIGH — preference explicit in CLAUDE.md + memory.
  - Blind spot: A change to the write call itself wouldn't be caught.
- **Fix B**: Restore one sanctioned real-FS no-BOM test (reintroduces real disk I/O).
- **Decision**: FIXED via Fix A (test comment at NoteFileServiceTests.cs:37 + plan Phase 3 addendum)

### F3 — `type: dropdown` still silently degrades to a text field (vocabulary gap)

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW — pre-existing, outside this change's edited files
- **Dimension**: Pattern Consistency / Reliability
- **Location**: Notes/Models/FormField.cs (XML doc) + PRD/roadmap wording
- **Detail**: After F1 the recognized keyword is `select`, but FormField's XML comment says "dropdown fields" and PRD/roadmap call it "dropdown/select". No alias, so `type: dropdown` silently yields a TextFieldVm — the silent-degradation class this rollout guards against. Pre-existing.
- **Fix**: Follow-up — alias dropdown→select in CreateField, or align docs/PRD to `select`.
- **Decision**: SKIPPED (acknowledged; not tracked)

## Notes

- All planned test coverage landed and matches intent; no `Assert.Throws` for malformed/edge scenarios (locked-silence contract honored); Phase 2 render oracle is genuinely input-derived; encoding simplification verified behavior-preserving; naming / MockFileSystem / NSubstitute / fresh-StrongReferenceMessenger conventions all clean.
- The Phase 4 assertion corrected this session (`.Received` → `.DidNotReceive` in `Receive_WhenMalformedTemplate_SkipsFormDialogAndSavesStaticBody`) is confirmed correct against `NoteTreeViewModel.cs:159`.
- Suite: 223 passed, 0 failed.
