<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Tags and Search Implementation Plan

- **Plan**: `context/changes/tags-and-search/plan.md`
- **Mode**: Deep
- **Date**: 2026-05-31
- **Verdict**: REVISE → SOUND (after triage)
- **Findings**: 0 critical · 5 warnings · 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING (resolved) |
| Blind Spots | WARNING (1 resolved, 1 noted) |
| Plan Completeness | WARNING (1 resolved, 1 skipped) |

## Grounding

7/7 paths ✓, 6/6 symbols ✓, brief↔plan ✓

## Findings

### F1 — BuildAsync threading: Phase 2 step omits the Task.Run wrapper that the rest of the plan assumes

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 #5 (line 256) vs Critical Impl Details (L95) and Performance (L556)
- **Detail**: Three sections of the plan said BuildAsync runs off the UI thread via `Task.Run`, but the Phase 2 step-by-step said `_ = BuildAsync(workspacePath, cts.Token)` with `await Task.Yield()` inside — which keeps the work on the UI thread (Task.Yield posts back to the captured SyncContext). Following the step verbatim would run `ScanMarkdownFiles` and every `ReadAsync` continuation on the UI thread.
- **Fix**: Replace L256 with `_ = Task.Run(() => BuildAsync(workspacePath, cts.Token), cts.Token);` and drop the `await Task.Yield()` on L257.
- **Decision**: FIXED (plan edited; Task.Run wrapper added, Task.Yield dropped, comment clarifies why).

### F2 — Saves/deletes during in-flight build are clobbered by the dictionary swap

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM
- **Dimension**: Blind Spots
- **Location**: Phase 2 #5 — `Receive(NoteSavedMessage)` / `Receive(NoteDeletedMessage)` vs BuildAsync swap
- **Detail**: User-visible scenario: fresh workspace; BuildAsync still running; user hits Ctrl+N. `HandleNewNote` saves the file and publishes `NoteSavedMessage`. SearchIndex upserts into the old `_entries`. BuildAsync's scan (taken before the new file existed) doesn't include the new file. Swap runs → upsert lost → Ctrl+F can't find the just-created note until the next save or workspace switch.
- **Fix A ⭐ (applied)**: Buffer `NoteSaved` / `NoteDeleted` arrivals while `!_isReady` into a UI-thread-only `_pendingDuringBuild` list; drain into the new dict immediately after the swap; clear the buffer on workspace change (re-entrant build).
- **Fix B**: Document as a known MVP limitation in §What We're NOT Doing.
- **Decision**: FIXED via Fix A (added `_pendingDuringBuild` field, updated `Receive(WorkspaceChangedMessage)` to clear it, updated `Receive(NoteSavedMessage)` and `Receive(NoteDeletedMessage)` to append when `!_isReady`, updated the swap step to drain before assigning; added three Phase 2 test cases covering the race).

### F3 — Search-result click leaves the tree's `SelectedNode` stale

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM
- **Dimension**: Plan Completeness
- **Location**: Phase 3 #2 — OpenResult command (line 396)
- **Detail**: `OpenResult` synthesizes a fresh `NoteTreeNode` and publishes `NoteSelectedMessage`. The editor opens the file, but `NoteTreeViewModel` only publishes selection messages — it doesn't receive them. The tree's visual highlight stays on whatever was previously selected, so after the overlay closes the tree shows a different note as "selected" than what's open in the editor.
- **Fix A ⭐**: Inject a small `INoteTreeSelector { SelectByPath(string) }` into the Search VM; route through tree's `SelectedNode`. Reuses the existing publish chain; bends the "siblings hold no references to each other" pattern.
- **Fix B**: Tree subscribes to `NoteSelectedMessage` with a "suppress next publish" guard. Preserves sibling isolation; introduces fragile guard discipline.
- **Decision**: SKIPPED (user decided the UX gap isn't worth addressing now; revisit if it surfaces in actual use).

### F4 — `DoSave`'s publish-after-save ordering vs the existing exception swallow is ambiguous

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW
- **Dimension**: Plan Completeness
- **Location**: Phase 2 #6 (line 288) — `NoteEditorViewModel.DoSave` edit
- **Detail**: `DoSave` already wraps `_fileService.Save(...)` in `try/catch` (IOException, UnauthorizedAccessException) — currently swallowed (Trace log only). The plan said "publish AFTER the file write to honor the file-on-disk-reflects-the-message invariant" but didn't say "skip publish when Save throws." An implementer placing the `Send` after the catch block would broadcast `NoteSavedMessage` even on failed saves, desyncing the index from disk.
- **Fix**: State explicitly that `_messenger.Send(...)` goes INSIDE the try block, on the line immediately after `_fileService.Save(...)`, so the publish is skipped when the existing catches fire.
- **Decision**: FIXED (plan edited; contract reworded to specify position and rationale).

### F5 — Test-only `internal` hooks (`CurrentBuild`, `CurrentSearch`) bake test coupling into production code

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 #9 (line 314), Phase 3 #8 (line 405)
- **Detail**: The plan asked for `internal Task? CurrentBuild` on `NoteSearchIndex` and `internal Task? CurrentSearch` on `NoteSearchViewModel`, plumbed via `InternalsVisibleTo`, so tests could await async completion. That's a test hook bolted into production code — `internal` + `InternalsVisibleTo` is the same coupling as `public`, just quieter. Tests should drive through the contracts the production code already publishes: `SearchIndexStateChangedMessage(true)` for the index, `PropertyChanged` on `Results` for the VM.
- **Fix**: Rewrite both test-strategy notes to use a one-shot `IRecipient<SearchIndexStateChangedMessage>` for the index and the existing `PropertyChanged` event for the VM. Drop both `internal` helpers and the `InternalsVisibleTo` mention.
- **Decision**: FIXED (plan edited; both test-strategy notes rewritten with inline TCS helpers; matching test-case references updated; Testing Strategy summary updated; no `InternalsVisibleTo` change to `Notes.csproj` needed).

### F6 — Ctrl+F vs AvaloniaEdit not verified

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Blind Spots
- **Location**: Phase 3 #6 — Ctrl+F KeyBinding on MainWindow
- **Detail**: The editor uses AvaloniaEdit's `TextEditor`. AvaloniaEdit ships an opt-in `SearchPanel.Install(editor)` — not currently called, so the window's Ctrl+F should win. But the manual verification list didn't include "Ctrl+F with focus inside the editor" — easy regression to miss if AvaloniaEdit ever installs its search panel by default.
- **Fix**: Add a manual-verification step in Phase 3.
- **Decision**: FIXED (added Manual Verification step + Progress entry 3.18).
