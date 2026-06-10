---
change_id: test-validation
title: Mutation testing to validate template + file-safety test effectiveness
status: implementing
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
