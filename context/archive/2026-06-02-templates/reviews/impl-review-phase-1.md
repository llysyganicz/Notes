<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Templates — Note-from-Template (Engine)

- **Plan**: context/changes/templates/plan.md
- **Scope**: Phase 1 of 3
- **Date**: 2026-06-04
- **Verdict**: NEEDS ATTENTION → resolved to APPROVED after triage (all findings fixed or dismissed)
- **Findings**: 0 critical, 2 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING → resolved (F1, F2 fixed) |
| Architecture | PASS |
| Pattern Consistency | PASS (parser deviation later simplified to deserialize-to-object) |
| Success Criteria | PASS (build clean; 159 tests pass) |

## Findings

### F1 — Blank line inside the form block leaks later keys into output

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Reliability)
- **Location**: Notes/Services/TemplateRenderer.cs (StripFormBlock)
- **Detail**: A blank line between indented form children terminated the strip early (`IsIndented("")` is false), leaking subsequent form sub-keys into the output as malformed frontmatter. Parser (YamlDotNet) accepted such templates, so parser and renderer disagreed on the block's extent. Confirmed by tracing.
- **Fix**: Treat whitespace-only lines as continuations in the strip loop (`IsIndented(content) || string.IsNullOrWhiteSpace(content)`). Added regression test `Render_WhenFormBlockContainsBlankLine_StripsEntireBlock` + a `format:`-sibling guard test.
- **Decision**: FIXED

### F2 — CRLF frontmatter silently normalized to LF (mixed-ending output)

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality (Reliability)
- **Location**: Notes/Services/TemplateRenderer.cs (TrySplitFrontmatter / rebuild)
- **Detail**: A CRLF template emerged with LF frontmatter (`TrimEnd('\r')` + hard-coded `\n` on rebuild) but a CRLF body (raw substring) — a mixed-ending file and a soft break of the "verbatim" promise.
- **Fix**: Fix B — rewrote the renderer to carry each line's original terminator (`\n`/`\r\n`) through the whole split/strip/rebuild pipeline via a `Line(Content, Ending)` token. Added `Render_WhenTemplateUsesCrlf_PreservesCrlfEndings` asserting a CRLF template round-trips as CRLF.
- **Decision**: FIXED (Fix B)

### F3 — Edge-case test coverage gaps

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria (Test coverage)
- **Detail**: Missing CRLF round-trip, blank-line-in-form-block, `format:`-sibling guard, and `number`-type parser tests.
- **Fix**: CRLF / blank-line / format-sibling tests added under F1/F2; added `Parse_WhenNumberFieldWithFormat_CapturesTypeAndFormat`.
- **Decision**: FIXED

### F4 — IsFormKeyLine not anchored to column 0

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: Notes/Services/TemplateRenderer.cs (IsFormKeyLine)
- **Detail**: Concern that `StartsWith("form:")` could match an indented `form:`. Re-examined and found unfounded: any indentation puts whitespace before `"form"`, so `"  form:".StartsWith("form:")` is false — the check already requires column 0.
- **Decision**: DISMISSED (unfounded)

## Note — parser mechanism change (out-of-band, user-directed)

During triage the user directed the parser to deserialize the frontmatter into a typed shape object (YamlDotNet `IDeserializer` + `FrontmatterShape { Dictionary<string, FieldShape> Form }`) to obtain the `FormDefinition` directly — explicitly no re-serialization. This replaced the earlier `YamlStream`/representation-model node-walking (review observation #6), restoring the plan's *literal* original instruction ("reuse a YamlDotNet `IDeserializer` as `NoteMetadataParser`"). Field document-order is preserved (deserializer reads the mapping in order; the backing dictionary keeps insertion order) and verified by the existing ordering test. The renderer's textual form-strip is unchanged in purpose (no re-emit) and keeps its line-scan.
