<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Templates — Note-from-Template with a Typed Form

- **Plan**: context/changes/templates/plan.md
- **Scope**: Full plan (Phases 1–3 of 3)
- **Date**: 2026-06-05
- **Verdict**: APPROVED
- **Findings**: 0 critical · 0 warnings · 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Automated: `dotnet build` → 0 errors (21 pre-existing warnings); `dotnet test` → 202 passed.
Manual: Progress rows 2.5–2.7, 3.5–3.8 all `[x]` (user-confirmed). Drift agent: all 7 planned
items MATCH (every divergence maps to an accepted amendment A1–A6). Safety agent: no
CRITICAL/WARNING; cross-phase catalog cache + `TemplatesChangedMessage` refresh verified
ordering-safe (Load-before-Send; recipient registered before first message).

## Accepted amendments (not flagged as drift)

- A1: `ITemplateCatalog` changed from stateless `List(workspacePath)`/`HasAny(workspacePath)` (deriving from `IWorkspaceScanner`) to a cached `Load(IReadOnlyList<string>)` + parameterless `List()`/`HasAny()`, fed by `NoteTreeViewModel.LoadTree()` + new `TemplatesChangedMessage`. Interactive option-2 decision to avoid re-scanning on every autosave.
- A2: `MainWindowViewModel` reacts to `TemplatesChangedMessage` (consequence of A1).
- A3: `TemplatePickerDialog` implemented as MVVM + locator (`TemplatePickerViewModel`) to match `TemplateFormDialog`, rather than a code-behind `static Show`.
- A4: `?? FormDefinition.Empty` defensive guard (review #1) — **removed during this triage (F2)**.
- A5: Prior-phase decisions: Ph1 F4 DISMISSED; Ph2 F1 SKIPPED (DataContext! timing safe — same pattern in `TemplatePickerDialogService`); Ph2 F3 DEFERRED (FU-1).
- A6: Broad `catch (Exception)` in parser is deliberate (lessons.md).

## Findings

### F1 — Catalog became a stateful singleton cache (plan deviation, accepted)

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — informational
- **Dimension**: Architecture
- **Location**: Notes/Services/TemplateCatalog.cs
- **Detail**: Plan specified a stateless `List(workspacePath)`. Actual is a cached `Load(paths)` + parameterless reads, fed by `LoadTree` + `TemplatesChangedMessage` (accepted amendment A1). Singleton lifetime correct for a cache; UI-thread-only, reference-swap — no locking needed. Documented in XML-doc + `RefreshTemplateCatalog` comment.
- **Decision**: ACCEPTED (informational; no action)

### F2 — Dead `?? FormDefinition.Empty` guard

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/ViewModels/NoteTreeViewModel.cs:156
- **Detail**: `ITemplateParser.Parse` is contractually non-null (returns `FormDefinition.Empty` on every failure path), so the `?? FormDefinition.Empty` right-hand side is unreachable. Added at review #1 request.
- **Fix**: Remove the coalesce → `var definition = _templateParser.Parse(templateText);`
- **Decision**: FIXED — coalesce removed; build + 202 tests green.

### F3 — Malformed `form:` fails silently (static copy with placeholders)

- **Severity**: 🔵 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency (UX)
- **Location**: Notes/Services/TemplateParser.cs (broad-catch) → Notes/ViewModels/NoteTreeViewModel.cs HandleNewFromTemplate
- **Detail**: A present-but-unparseable `form:` yields `FormDefinition.Empty` → form skipped → static copy with literal `{{tokens}}`, no signal. Matches the locked design but is a real authoring trap (hit this session with tab-indented sequence YAML).
- **Fix A ⭐ Recommended**: Defer — keep locked behavior; the post-MVP template designer/validator is the proper home.
  - Strength: Preserves the locked contract and the deliberate broad-catch (lessons.md); no scope addition to a closed slice.
  - Tradeoff: The trap persists until the post-MVP work lands.
  - Confidence: HIGH.
- **Fix B**: Add a pre-MVP guard — warn when frontmatter has a `form:` key that parses to zero fields.
  - Tradeoff: Net-new warning path + a "form key present" probe; scope addition to a closed slice.
  - Confidence: MED.
- **Decision**: DEFERRED — Fix A. Tracked as FU-2 in `follow-ups/review-fixes.md`; also covered by project memory `post-mvp-template-authoring-ux`.
