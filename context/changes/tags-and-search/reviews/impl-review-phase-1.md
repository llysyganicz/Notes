<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Tags and Search — Phase 1

- **Plan**: context/changes/tags-and-search/plan.md
- **Scope**: Phase 1 of 3
- **Date**: 2026-06-01
- **Verdict**: APPROVED
- **Findings**: 0 critical · 1 warning · 3 observations
- **Commit reviewed**: 2508520

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

### F1 — Narrow exception catch is fragile against floating "18.*" pin

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/NoteMetadataParser.cs:47
- **Detail**: The parser advertises a "never throws" contract but catches only `YamlException`. YamlDotNet 18.x covers the current test cases (tags-not-a-list, malformed YAML) by wrapping in `YamlException`, but the package version is pinned as `18.*`. If a future 18.x patch changes the wrapping for an edge case (e.g. nested-sequence-in-tags, or a scalar that fails type conversion), an `InvalidCastException` or `FormatException` would escape `Parse` and break callers that rely on the "total function" guarantee.
- **Fix A ⭐ Recommended**: Broaden to `catch (Exception)`
  - Strength: Honors the documented "never throws" contract exactly; future YamlDotNet patches can't surprise us.
  - Tradeoff: Catches more than strictly intended.
  - Confidence: HIGH — matches the "total function" interface contract from the plan verbatim.
  - Blind spot: None significant.
- **Fix B**: Pin YamlDotNet to a tight version (e.g. `18.0.*` or exact `18.0.0`)
  - Strength: Keeps the narrow catch honest.
  - Tradeoff: Misses bug-fix patches; doesn't address the root issue.
  - Confidence: MEDIUM — partial mitigation.
  - Blind spot: Doesn't help if YamlDotNet's exception taxonomy is inconsistent within 18.0.x.
- **Decision**: FIXED via Fix A — broadened to `catch (Exception)` and dropped the unused `YamlDotNet.Core` import.

### F2 — Markdig parses entire note body to find frontmatter

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🔎 MEDIUM — decision matters for Phase 2 scale
- **Dimension**: Safety & Quality (performance, forward-looking)
- **Location**: Notes/Services/NoteMetadataParser.cs:34
- **Detail**: `Markdig.Markdown.Parse(noteText, Pipeline)` parses the full markdown AST just to read the leading frontmatter block. For Phase 1 the parser is only called from tests, so cost is irrelevant. In Phase 2 the indexer will call `Parse` for every `.md` file at startup and on every save — a 100 KB note pays a full-body Markdig parse even though frontmatter ends in the first ~1 KB.
- **Fix**: No change required in Phase 1. Revisit during Phase 2 if `BuildAsync` profiling shows Markdig as a hot path — a leading-substring scan could replace the full Markdig parse there.
- **Decision**: SKIPPED — defer to Phase 2 profiling.

### F3 — `FrontmatterShape` shape deviates from plan

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Notes/Services/NoteMetadataParser.cs:63
- **Detail**: Plan specifies `private nested record FrontmatterShape { List<string>? Tags }`. Implementation uses `private sealed class FrontmatterShape { List<string?>? Tags }`. Class-vs-record is incidental (YamlDotNet target with public setter — record is awkward). The `string?` element type is load-bearing: YamlDotNet maps `tags: [~, a]` to a null element rather than throwing, which the `WhenTagsContainEmptyOrNullValue_DropsThoseEntries` test requires.
- **Fix**: No code change. Optionally add a one-line comment on `FrontmatterShape` noting that `List<string?>?` is required to absorb YAML null entries cleanly.
- **Decision**: SKIPPED — justified deviation, no action.

### F4 — Parse signature claims non-nullable input but accepts null

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes/Services/INoteMetadataParser.cs:7
- **Detail**: Interface declares `NoteMetadata Parse(string noteText)` under `<Nullable>enable</Nullable>`. The test `Parse_WhenInputIsNull_ReturnsEmpty` passes `null!` to bypass the compiler. Two coherent positions: (a) advertise null-tolerance via `string?`, or (b) drop the null-check and let nullability enforce non-null at compile time, then drop the corresponding test.
- **Fix**: Pick one — either change the signature to `Parse(string? noteText)`, or remove the null-input branch from the parser and its test. Status quo is internally inconsistent.
- **Decision**: FIXED via option (a) — signature now `Parse(string? noteText)` on both interface and implementation; test no longer needs `null!`.
