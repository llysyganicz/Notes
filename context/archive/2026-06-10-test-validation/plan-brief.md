# Test-Effectiveness Validation (Mutation Testing) — Plan Brief

> Full plan: `context/changes/test-validation/plan.md`
> Research: `context/changes/test-validation/research.md`
> Blocker record: `context/changes/test-validation/stryker-avalonia-blocker.md`

## What & Why

Phase 3 of the test-plan proves the Phase 1–2 tests actually kill regressions —
"are these tests correct?" — by running **mutation testing (Stryker.NET) scoped to
the template + file-safety logic**. The first attempt hit a hard wall: Stryker's
mutated recompile must compile the *whole* `Notes` project, and Avalonia's
`InitializeComponent` source-generator output doesn't survive that recompile
(CS0103), independent of `mutate` scope. **This re-plan unblocks mutation by
extracting the Avalonia-free logic into a new `Notes.Core` library** — a mutable
compile target with no UI source generators — then runs the mutation testing the
original plan intended.

## Starting Point

Both prior phases ship with unusually independent oracles. The MTP runner already
works (273 tests found via `test-runner: mtp`). The blocker is structural: the
single-project Avalonia layout has no Avalonia-free compile target. The in-scope
services are all Avalonia-free; only `AutoSaveScheduler` (a UI-thread
`DispatcherTimer`) is genuinely UI-coupled. The in-scope ViewModels are
Avalonia-free but use CommunityToolkit source generators — an unproven recompile
risk.

## Desired End State

A new `Notes.Core` library holds all Avalonia-free logic (re-namespaced
`Notes.Core.*`); `Notes` references it and still builds, tests, and launches. A
new Avalonia-free `Notes.Core.Tests` project holds the moved logic tests. A
committed `stryker-config.json` scopes mutation to the template + file-safety
files inside `Notes.Core`, run from `Notes.Core.Tests/`, and exits 0 against a
`break` floor set just under the clean score. The three known gaps are closed
(proven by a score increase), §F equivalents are excluded at exact line ranges,
and cookbook §6.5 documents the procedure. No CI gate yet (test-plan Phase 4).

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Unblock strategy | Extract Avalonia-free logic into a `Notes.Core` class library | Stryker needs a compile target without Avalonia's UI source generator | Blocker |
| Extraction breadth | **All** Avalonia-free services (not just mutation-scope ones) | A clean one-way logic/UI boundary; `Notes.Core` never references `Notes` | Plan |
| `AutoSaveScheduler` | Leave in `Notes` | Its `DispatcherTimer` is UI-thread-bound; swapping it is behavior-sensitive and it isn't in mutation scope | Plan |
| ViewModels (`Fields/*`, `TemplateFormViewModel`) | Include in scope, **gated** on a generator-survival smoke check | Aim for full §C coverage without betting the plan on an unproven CommunityToolkit-generator assumption | Plan |
| Namespaces | Re-namespace to `Notes.Core.*` | Names match the assembly; accept the mechanical churn (incl. AXAML `clr-namespace`) | Plan |
| Test layout | New `Notes.Core.Tests` (split from `Notes.Tests`) | Leaner, Avalonia-free Stryker test set; makes the run unambiguously single-project | Plan |
| Sequencing | Smoke-run-first — extract (P1) then prove mutation (P2) before the rest | Isolates the two risks (re-namespace churn vs generator survival) to fast, separate checkpoints | Plan |
| Runner | Native MTP runner (`test-runner: mtp`), VSTest fallback only if it breaks | Already proven to drive the suite; default VSTest can't | Research |
| Threshold | Set `break` after measuring, exclude equivalents first | Floor reflects what the suite actually achieves, not a guess | Plan |

## Scope

**In scope:**
- New `Notes.Core` library + `Notes.Core.Tests` project; move all Avalonia-free
  logic + its tests, re-namespaced `Notes.Core.*`; re-wire references and AXAML
- Re-pointed scoped `stryker-config.json`; raw baseline + survivor inventory; post-fix re-run + delta
- Close 3 gaps: `NoteFolderService` coverage, `NoteFileService` BOM oracle, `TemplateCatalog` prefix-trap
- Line-range exclusions for §F intentional survivors + locked `break` threshold
- Cookbook §6.5 + test-plan status/doc sync

**Out of scope:**
- CI / GitHub Actions gate wiring (test-plan Phase 4)
- Moving `AutoSaveScheduler`, dialog services, shell VMs, or Views
- Widening mutation scope beyond the template + file-safety files
- Switching to the VSTest runner (contingency only); chasing §F survivors

## Architecture / Approach

Two halves. **Half A (Phase 1):** a pure structural refactor — extract the
Avalonia-free logic into `Notes.Core` + `Notes.Core.Tests`, re-namespaced, with
its own build/test/launch gate and no Stryker. **Half B (Phases 2–4):** the
mutation work pointed at `Notes.Core` — first run (smoke + raw baseline + the VM
source-generator gate), then close the three gaps and re-run for the proof delta,
then exclude equivalents + lock the threshold + document. Reference graph ends up
one-way: `Notes` → `Notes.Core`; `Notes.Core.Tests` → `Notes.Core`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Extract `Notes.Core` + `Notes.Core.Tests` | Re-namespaced library + test project; build/test green; app launches | Re-namespace ripples into AXAML `clr-namespace` (compiled bindings) — missed refs fail at runtime |
| 2. Re-point Stryker + first run | Scoped config + raw score + VM-generator verdict + survivor inventory | CommunityToolkit generators may not survive recompile → VM-exclusion fallback; `.slnx` solution-mode trap |
| 3. Close gaps + re-run | 3 gaps closed, measurable score increase | A targeted survivor doesn't die → oracle still weak |
| 4. Exclude + threshold + docs | Line-range exclusions, locked `break`, cookbook §6.5, doc sync | Over-excluding a killable mutant; mis-set threshold margin |

**Prerequisites:** Stryker global tool installed (done); Phase 1–2 tests present
(done); `dotnet build` green.
**Estimated effort:** ~3–4 sessions; the long poles are the re-namespace
extraction (Phase 1) and each Stryker run + report inspection.

## Open Risks & Assumptions

- **VM source-generator survival is unproven** — Phase 2's run is the empirical
  gate; if CommunityToolkit generators drop (the Avalonia failure mode from a
  different generator), the VMs leave the `mutate` scope (documented fallback).
- **Run location is load-bearing** — must run from `Notes.Core.Tests/` for
  single-project mode; the repo-root `.slnx` path re-includes the Avalonia project
  and reproduces the blocker.
- The MTP runner is preview and net10 mutation isn't doc-certified; VSTest fallback
  (two NuGet packages on `Notes.Core.Tests`) is the escape hatch.
- The `break` number is unknown until measured; the plan commits to the method.

## Success Criteria (Summary)

- `dotnet stryker` (from `Notes.Core.Tests/`) exits 0 with the score at/above a
  `break` floor set under the measured clean number, mutating only `Notes.Core`
  files — never the Avalonia project.
- The post-fix score is strictly higher than the raw baseline — the three gaps are
  demonstrably closed.
- The app still launches with the template-form dialog rendering, proving the
  extraction + re-namespace are non-breaking; cookbook §6.5 lets the next
  contributor repeat the procedure.
