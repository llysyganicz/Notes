# File-safety & data-loss guardrails — Plan Brief

> Full plan: `context/changes/file-safety/plan.md`
> Research: `context/changes/file-safety/research.md`

## What & Why

Rollout Phase 2 of the test plan: prove that creating from a template, saving a note, and
naming a note/folder can **never destroy data** (test-plan §2 risks #3/#4/#5; PRD Guardrails
"a crash or unexpected quit must never corrupt or lose a note file"). Research verified — not
assumed — each guard's real state, and two of the three guards are missing today, so this phase
adds small, localized service-layer guardrails *and then* tests them.

## Starting Point

Today: the #3 collision guard exists and works on the happy path, but the note write
(`NoteFileService.Save` → `File.WriteAllText`) is a single in-place overwrite — **no atomicity**
— and name validation is **dialog-only**: the three disk services trust any absolute path they're
handed, and the delete flow bypasses the validator entirely. There is no service-layer trust
boundary and no workspace confinement anywhere.

## Desired End State

The collision guard is pinned by a test (original file provably untouched on a name clash); the
note write is durable (temp file → atomic rename) so an interrupted save can't truncate the live
note; and a shared `PathGuard` confines every write **and delete** to the workspace root at the
service layer. Crafted names (`../`, absolute, reserved) are rejected at the service — not just
the UI — and the cookbook §6.2 integration recipe is filled.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Scope: fix or just test? | Add guardrails + test | Brief intent is "never destroy data"; #4/#5 assertion tests are red without a guard to assert against | Plan |
| #4 durable write | Temp file → atomic rename (`File.Move` overwrite) | Crash mid-write only damages the disposable temp, never the live note; no `.bak` clutter | Plan |
| #4 how to test atomicity | Assert temp→rename sequence + inject a fault before rename | `MockFileSystem` writes are atomic, so prove the invariant via a throwing `IFileSystem` decorator | Plan |
| #5 guard location | Shared `PathGuard`, called by all four disk ops | One service-layer boundary that also covers the validator-bypassing delete path | Plan |
| #5 root source | Cache `CurrentWorkspacePath` inside `SettingsService` | Reuse the existing root owner; keep config-file IO off the keystroke-debounced save path | Plan |
| #3 TOCTOU window | Pin happy path, accept TOCTOU as residual | A single-user desktop app makes the check-then-write race near-impossible; not worth a save-contract change | Plan |

## Scope

**In scope:** durable temp-then-rename write in `NoteFileService`; an orphaned-temp sweep on
workspace load; a shared `PathGuard` + `SettingsService.CurrentWorkspacePath`; wiring the guard
into save/create/delete/delete-folder; `NameValidator` hardening for `..`/reserved names;
integration tests for #3/#4/#5; cookbook §6.2.

**Out of scope:** E2E/GUI tests (Lesson 4); mutation tests (Phase 3); CI/hook gates (Phase 4);
closing the #3 TOCTOU; backup/read-back-verify on the write; changes to risk strategy or
quality-gate definitions.

## Architecture / Approach

`SettingsService` gains an in-memory `CurrentWorkspacePath` (updated on Load/Save, already
ordered before `WorkspaceChangedMessage`). A new `PathGuard` reads that root, rejects out-of-root
absolute paths (fail-closed if root unknown), and is called at the top of `Save`, `Create`,
`Delete`, `DeleteFolder`. The durable write lives inside `Save`, *below* the guard call. Tests use
`MockFileSystem`; the #4 fault case uses a throwing `IFileSystem` decorator.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. #3 collision pin (test-only) | Test proving a name clash leaves the original file intact; §6.2 seeded | Test must use an independent oracle, not renderer output |
| 2. #4 atomic durable write | Temp→rename `Save` + fault-injection tests | First-save (no existing target) path; stray `.tmp` cleanup |
| 3. #5 service-layer containment | `PathGuard` + `SettingsService` root + wiring + parity tests | False rejections on legit in-workspace paths; root-prefix trap |

**Prerequisites:** None beyond the existing Phase-1 test harness (xUnit, MockFileSystem,
NSubstitute, `InMemoryNoteFileService`).
**Estimated effort:** ~2–3 sessions across 3 phases; production changes are small and localized.

## Open Risks & Assumptions

- Assumes `SettingsService.Save` always runs before `WorkspaceChangedMessage` is broadcast (verified
  at `MainWindowViewModel.cs:87-89`); if a future caller broadcasts without saving, the cached root
  would lag.
- Temp-then-rename assumes the temp sits on the same volume as the target (sibling path) for a true
  atomic move.
- A crash between write and rename orphans a `*.md.tmp` temp; reclaimed by the next save of that note
  or the workspace-load sweep. The sweep deletes any `*.md.tmp` under the root, so a user's own file
  named `*.md.tmp` (disposable by `.tmp` convention) would be removed too; recovery of crash-time
  unsaved edits is an explicit non-goal (silent delete).
- The #3 TOCTOU window remains technically reachable by design (accepted residual).

## Success Criteria (Summary)

- A name collision provably never overwrites an existing note (#3).
- An interrupted save provably leaves the prior note content byte-for-byte intact (#4).
- A crafted out-of-root name is rejected at the **service** layer, including on delete (#5).
