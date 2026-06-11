---
change_id: test-validation
title: Mutation testing to validate template + file-safety test effectiveness
status: implemented
created: 2026-06-10
updated: 2026-06-11
archived_at: null
---

## Notes

The 3rd phase from the @context/foundation/test-plan.md

## 2026-06-11 — Phase 1 halted, re-plan required

Implementation of Phase 1 hit a structural blocker: **Stryker.NET cannot
mutation-test the `Notes` project** because Avalonia's `InitializeComponent`
source generator output does not survive Stryker's in-memory mutated recompile
(CS0103 on every View's code-behind), independent of `mutate` scope. The MTP
runner itself works (273 tests found); the wall is the single-project Avalonia
layout. No supported config-only fix exists on either the Stryker or Avalonia
side (Avalonia #11050 closed as not-planned). Decision: **stop and re-plan**
around extracting the pure logic into a non-Avalonia class library
(`Notes.Core`). Full empirical record + sources in
`stryker-avalonia-blocker.md`.

## 2026-06-11 — Re-plan complete

`plan.md` + `plan-brief.md` rewritten around the `Notes.Core` extraction. Now four
phases: **(1)** extract all Avalonia-free logic into `Notes.Core` + a new
`Notes.Core.Tests` project, re-namespaced `Notes.Core.*` (refactor only — build +
test + app-launch gate, no Stryker); **(2)** re-point Stryker at `Notes.Core`,
first run = smoke + raw baseline + a CommunityToolkit source-generator survival
gate (the open risk); **(3)** close the three §D gaps + re-run for the score
delta; **(4)** exclude §F equivalents, lock `break`, document (cookbook §6.5 +
doc sync). Key decisions: extraction breadth = all Avalonia-free services
(`AutoSaveScheduler` stays — UI-thread `DispatcherTimer`); in-scope VMs included
but gated; run from `Notes.Core.Tests/` to avoid the `.slnx` solution-mode trap.

## 2026-06-11 — Implemented (all 4 phases)

All phases landed. Highlights: P1 `Notes.Core`/`Notes.Core.Tests` extraction
(131f3cc), P2 scoped Stryker + raw baseline 93.12%, VM generator gate passed
(c0ec940), P3 three §D gaps closed → 94.33% (c7abb6f), P4 six real coverage gaps
killed → **96.76%**, eight true equivalents accepted + documented, `break: 95`,
cookbook §6.5 (33d8130). Two plan deviations, both recorded in `baseline.md`:
(a) `InMemoryNoteFileService` stayed in `Notes.Tests` (only its VM-test consumers
remain there); (b) equivalents were **accepted + catalogued, not config-excluded**
— Stryker's `mutate` spans are character-offset based (brittle) and `// Stryker
disable` source comments were declined. Reporters narrowed to `["markdown","json"]`.
Run record + survivor inventory: `baseline.md`.
