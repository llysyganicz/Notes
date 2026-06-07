# Template Pipeline Correctness Tests — Plan Brief

> Full plan: `context/changes/testing-template-pipeline/plan.md`
> Research: `context/changes/testing-template-pipeline/research.md`

## What & Why

This is rollout **Phase 1** of the project's test plan: write the tests that
protect the template pipeline — the product's differentiator and (per the
test-plan interview) the least-confident area. It closes the named coverage gaps
for the three top risks: a create-from-template note that's wrong/corrupt (#1),
malformed template frontmatter that silently renders a half-form (#2), and a
saved note that's no longer portable `.md` (#6).

## Starting Point

The pipeline (`TemplateParser` → `FormDefinition` → `TemplateFormViewModel` →
`TemplateRenderer` → `NoteFileService`) works and has ~198 existing test methods
covering happy paths and prior regressions. The defining trait — locked in
`lessons.md` — is that **every failure mode is silent by design** (malformed YAML
→ empty form, unknown type → text field, missing value → empty string, undeclared
token → verbatim). Tests must assert resulting shape, never exceptions.

## Desired End State

The four risks have oracle-grounded tests at the cheapest layer (unit +
MockFileSystem integration, no e2e); the `NoteFileService` encoding code is
simplified to rely on .NET 10 defaults; and the §6 cookbook tells a future author
how to add unit, new-field-type, and view-model tests in this project.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Test layers | Unit + MockFileSystem integration, no e2e | Every risk is reachable cheaply; e2e adds no signal | Research / Plan |
| Encoding finding | Simplify code to defaults **+** add round-trip test | .NET 10 defaults already give UTF-8 no-BOM, so explicit args are redundant; test guards future regressions | Plan |
| Integration breadth | File-service round-trip **+** two VM end-to-end paths | Proves the silent-fallback contract survives all the way to disk | Plan |
| Leftover-placeholder oracle | xUnit `[Theory]`/`[InlineData]` data-driven cases | Oracle built from inputs, defeats "returned a string ⇒ correct" | Plan |
| Parent-dir behavior | Defer to Phase 2 | File-safety concern, not portability; keeps phase boundaries clean | Plan |
| Cut line if tight | Odd-bracing grammar edge cases drop first | Lowest-likelihood; preserves the high-impact #1/#2/#6 coverage | Plan |

## Scope

**In scope:** Risk #2 parser/form edge cases (incl. the `dropdown`/`select`
keyword trap); Risk #1 leftover-placeholder + slot-fidelity oracle; Risk #6
save→reload round-trip (content/encoding/line-ending) + a small encoding-code
simplification; end-to-end malformed/blank-form create paths reaching disk; §6
cookbook entries §6.1/§6.3/§6.4.

**Out of scope:** collision/overwrite, durable/atomic writes, path containment,
parent-dir creation (all Phase 2); mutation testing (Phase 3); CI gate wiring
(Phase 4 of rollout); e2e/GUI; YAML/markdown library internals; any test that
expects an exception/warning on malformed input.

## Architecture / Approach

Tests are added layer-by-layer along the pipeline's data flow (parse → render →
save → orchestration → cookbook), each extending the existing test file for its
SUT and following established conventions (`Method_WhenScenario_ExpectedBehaviour`,
xUnit v3, NSubstitute, fresh `StrongReferenceMessenger`, MockFileSystem,
`[AvaloniaFact]` only where Avalonia primitives are touched). Every oracle is
derived independently from the template definition + input, never from the
component's own output.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Parser & form edge cases | Risk #2 malformed/edge fallbacks + `dropdown`/`select` trap | Asserting an exception instead of the silent fallback |
| 2. Render oracle | Risk #1 leftover-placeholder + slot-fidelity theory | Oracle copied from renderer output (tautology) |
| 3. Round-trip + encoding tidy | Risk #6 round-trip tests + encoding simplification | Mock no-BOM assertion only proves the mock, not real default |
| 4. End-to-end create paths | Risk #1 malformed/blank-form notes reaching disk intact | Duplicating the existing happy-path glue test |
| 5. Cookbook | §6.1/§6.3/§6.4 filled with reference tests | Entries that don't name a real existing test |

**Prerequisites:** Research complete (done); existing suite green.
**Estimated effort:** ~1–2 sessions across 5 short phases (mostly test authoring;
one small production edit in Phase 3).

## Open Risks & Assumptions

- MockFileSystem's default `WriteAllText` is assumed to mirror real .NET UTF-8
  no-BOM; the existing real-FS no-BOM test is retained as the authoritative guard.
- The blank/cancelled-form save semantics (Phase 4) follow the create flow's
  current behavior — the test pins what the flow does, not a new requirement.

## Success Criteria (Summary)

- New tests prove: no leftover declared placeholder survives; the `dropdown`/`select`
  trap and unknown/missing types resolve to the documented fallback; a saved note
  round-trips faithfully.
- `dotnet test` green with the encoding code simplified and the existing no-BOM
  test still passing.
- §6.1/§6.3/§6.4 cookbook entries each name a real test a future author can copy.
