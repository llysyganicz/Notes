# File-safety & data-loss guardrails Implementation Plan

## Overview

Rollout **Phase 2** of `context/foundation/test-plan.md`: prove that creating from a
template, saving a note, and naming a note/folder can **never destroy data**. The
research (`context/changes/file-safety/research.md`) verified — rather than assumed —
the state of each guard, and they land very differently:

- **#3 collision** — the guard already exists; this phase *pins* it with a test and
  documents the residual TOCTOU window as accepted.
- **#4 atomic save** — atomicity does **not** exist; this phase *introduces* a
  temp-then-rename durable write, then tests it under an injected fault.
- **#5 path containment** — validation is dialog-only and the delete path bypasses it
  entirely; this phase *introduces* a shared service-layer `PathGuard` and a workspace-root
  source, then proves service parity.

Two of the three risks therefore require a small, localized production change before there
is anything green to assert against — confirmed in planning as the intended scope.

## Current State Analysis

| Risk | Verified state (from research) | Key file:line |
|------|--------------------------------|---------------|
| #3 | Guard EXISTS. `NameValidator.ValidateNoteName` does a `File.Exists` collision check, enforced in `PromptNameAndSave` before the write. Check-then-write TOCTOU window; the write (`WriteAllText`) is an unconditional overwrite. | `NameValidator.cs:31`, `NoteTreeViewModel.cs:200`, `NoteFileService.cs:38` |
| #4 | Atomicity ABSENT. `NoteFileService.Save` is a single in-place `File.WriteAllText` of the live `.md`. No temp-then-rename / `File.Replace` / flush anywhere. Auto-save only, 500 ms debounce. | `NoteFileService.cs:36-39`, `AutoSaveScheduler.cs:19` |
| #5 | Validation DIALOG-ONLY. Only `NoteTreeViewModel` calls `NameValidator`; the three disk services trust a pre-built absolute path with zero validation/confinement. Delete path skips the validator. Validator also misses bare `..` and reserved device names; no post-resolution `StartsWith(root)` check. | `NoteFileService.cs:38`, `NoteFolderService.cs:14`, `NoteDeleter.cs:14,16`, `NoteTreeViewModel.cs:295-296` |

Supporting facts established during planning:

- **The workspace root** is persisted by `SettingsService` (`AppSettings { WorkspacePath }`,
  `SettingsService.cs:21-49`) and re-broadcast at runtime via `WorkspaceChangedMessage`
  whenever the user switches workspace. `MainWindowViewModel.ChangeWorkspace` always calls
  `_settingsService.Save(...)` **immediately before** `_messenger.Send(new WorkspaceChangedMessage(...))`
  (`MainWindowViewModel.cs:87-89`; same shape at startup `:65,:74`), so a value cached inside
  `SettingsService` on `Save`/`Load` stays in lockstep with the live root without SettingsService
  having to subscribe to the messenger.
- **`IFileSystem` (System.IO.Abstractions) is pervasive** — every disk service is
  constructor-injected with it, so `MockFileSystem` drives the integration tests for #3 and #5.
  For #4, `MockFileSystem`'s in-memory write is atomic, so reproducing a mid-write truncation
  needs an **injected fault** (a throwing `IFileSystem`/wrapper), not `MockFileSystem` alone.
- **Test harness already exists** from Phase 1 (`testing-template-pipeline`): xUnit, `MockFileSystem`,
  NSubstitute, fresh `StrongReferenceMessenger` per VM test, `InMemoryNoteFileService` fake.
  Cookbook §6.1/§6.3/§6.4 are filled; **§6.2 (integration recipe) is TBD and this phase fills it.**

## Desired End State

After this plan:

1. An integration test proves the create-from-template collision guard refuses an existing
   name and leaves the original file byte-for-byte intact (**#3**), with the TOCTOU window
   documented as an accepted residual risk.
2. `NoteFileService.Save` writes durably (temp file → atomic rename); an injected fault
   *before* the rename provably leaves the existing note untouched (**#4**).
3. A shared `PathGuard`, fed the current workspace root from `SettingsService`, rejects
   traversal/absolute/reserved names and confines every write **and delete** to the
   workspace root at the **service** layer — proven by service-parity tests including the
   previously-unguarded delete path (**#5**).
4. Cookbook §6.2 documents the MockFileSystem integration recipe; the test-plan Phase 2 row
   moves toward `complete`.

**Verification:** `dotnet build` and `dotnet test` green; new tests fail if any guard is
reverted (the atomic-write test fails against a plain `WriteAllText`; the containment tests
fail if a service is called without the guard).

### Key Discoveries

- #3's guard is real and on the happy path (`NoteTreeViewModel.cs:200`) — do **not** re-add it; pin it.
- The write site has no guard of its own (`NoteFileService.cs:38`) — both #4's atomicity and
  #5's confinement attach here, so the two phases compose at this one method.
- The **delete path is the sharp edge of #5**: `NoteTreeViewModel.cs:295-296` builds the path
  straight from a tree node and never touches `NameValidator`. A service-layer guard is the
  only enforcement point that covers it.
- `SettingsService` is the single source of truth for the root; reuse it (per planning
  decision) by caching `CurrentWorkspacePath` in-memory rather than introducing a parallel store.

## What We're NOT Doing

- **Not** writing E2E / GUI tests (test-plan §7; Lesson 4 territory).
- **Not** changing the risk strategy or quality-gate definitions (Lesson 1 / `/10x-test-plan`).
- **Not** closing the #3 TOCTOU window with atomic create-if-not-exists — accepted as residual
  for a single-user desktop app (planning decision).
- **Not** adding a `.bak`/backup file or read-back-verify to the durable write — plain
  temp-then-rename only (planning decision; backups clutter the portable notes folder).
- **Not** wiring CI gates or post-edit hooks (test-plan §3 Phase 4).
- **Not** authoring mutation tests (test-plan §3 Phase 3).
- **Not** touching the search index, template pipeline, or auto-save *scheduling* — only the
  write primitive and the name/path boundary.

## Implementation Approach

Three phases, ordered by increasing blast radius so each layer is verified before the next:
**test-only (#3) → single-service production change (#4) → cross-service boundary (#5)**.
The #5 guard is added last so it composes onto an already-atomic `Save`. All disk-touching
tests use `MockFileSystem`; the #4 fault case uses a thin throwing `IFileSystem` decorator
(NSubstitute or hand-rolled) injected into the real `NoteFileService`.

## Critical Implementation Details

- **#4 first-save case.** `File.Replace` throws when the target does not yet exist, so the
  durable write must branch: if the target exists, atomically replace it; if not (first save),
  move the temp into place. `File.Move(temp, target, overwrite: true)` covers both on modern
  .NET and is supported by `MockFileSystem` — prefer it over `File.Replace` to avoid a backup
  file. Keep the temp file a *sibling* of the target (same directory/volume) so the rename is
  a true atomic same-volume move.
- **#4 temp cleanup on failure.** Wrap the write+move in a single `try`; on **any** exception
  (a faulted `WriteAllText` *or* a faulted `Move`/rename) the `catch` deletes the temp and
  rethrows, so `NoteEditorViewModel.DoSave`'s existing `IOException`/`UnauthorizedAccessException`
  catch still handles it as today. The cleanup `Delete` is itself wrapped so a failed cleanup
  cannot mask the original write error. On success, `Move` consumed the temp — nothing to clean.
  The original target is never touched in any failure path.
- **#4 crash-orphan handling.** A process crash *between* write and rename leaves the temp orphaned
  (no `catch` runs). Two layers reclaim it: (a) a **fixed sibling name** (`<file>.md.tmp`, not a
  GUID) so the next save of that *same* note overwrites it via `WriteAllText` mid-session; and (b) a
  **startup/workspace-load sweep** (Phase 2, change #4) that deletes orphaned `*.md.tmp` files under
  the root for the note-never-resaved case. The temp's `.tmp` extension keeps it out of the tree and
  search (the scanner globs `*.md` only — `WorkspaceScanner.cs:32`), so an orphan is silent on disk,
  not user-visible, until the sweep removes it.
- **#5 fail-closed root.** If `SettingsService.CurrentWorkspacePath` is null/empty (no workspace
  selected yet), `PathGuard` must **reject** — services are not expected to be called before a
  workspace exists, and failing closed is safer than confining against an empty root.
- **#5 confinement check.** Compare `Path.GetFullPath(target)` against the canonicalized root
  with a trailing-separator-aware `StartsWith` (so `/work` does not match `/workspace-evil`),
  using the platform's path comparison. This catches `..`, absolute paths, and drive-relative
  forms uniformly *after* resolution — the character/token checks in `NameValidator` are
  defense-in-depth UX, not the authoritative boundary.

---

## Phase 1: #3 — Pin the collision guard (test-only)

### Overview

Prove the existing create-from-template collision guard actually prevents an overwrite, and
document the TOCTOU window as accepted. No production code changes. Establishes the
MockFileSystem integration harness this change reuses and seeds cookbook §6.2.

### Changes Required

#### 1. Collision integration test

**File**: `Notes.Tests/NoteTreeViewModelTests.cs` (extend; do not add a parallel file)

**Intent**: Drive the real save path with a pre-existing note of the same typed name and prove
the guard short-circuits before the write, leaving the original content untouched. This pins
#3's working guard rather than asserting a guard that needs adding.

**Contract**: A message-driven `[Fact]` following the §6.4 shape — fresh `StrongReferenceMessenger`,
NSubstitute dialog doubles returning the colliding name, `MockFileSystem` (or `InMemoryNoteFileService`)
pre-seeded with the existing note at the target path. Send the create-from-template (and/or plain
new-note) request; assert the stored file content is **byte-for-byte the original** (independent
oracle — not re-derived from the renderer output) and that no save message/write for that path
occurred. Test name per convention, e.g.
`Receive_WhenNewNoteNameCollidesWithExisting_DoesNotOverwriteOriginal`.

#### 2. TOCTOU note + cookbook §6.2 seed

**File**: `context/foundation/test-plan.md` (§6.2) — *appended by `/10x-implement` on phase close, per §6.6 convention*

**Intent**: Record that #3's guard is pinned on the happy path and the check-then-write TOCTOU
window is an accepted residual, and write the first §6.2 integration-recipe entry (MockFileSystem,
pre-seeded files, independent oracle).

**Contract**: Prose only — a §6.2 sub-section replacing its "TBD" placeholder. No code.

### Success Criteria

#### Automated Verification

- Build passes: `dotnet build`
- New collision test passes: `dotnet test`
- The collision test fails if the guard at `NoteTreeViewModel.cs:200` is removed (verify by
  local spike, then restore)

#### Manual Verification

- The test reads as pinning real behavior, not re-asserting renderer output (oracle is the
  pre-seeded original content, independent of inputs)
- §6.2 cookbook entry is accurate and the TOCTOU acceptance is stated

**Implementation Note**: After automated verification passes, pause for human confirmation of the
manual items before starting Phase 2.

---

## Phase 2: #4 — Atomic durable write

### Overview

Replace the in-place `File.WriteAllText` with a temp-then-rename durable write so an interrupted
save can never truncate the live note, then prove it with an injected mid-write fault. Also add a
workspace-load sweep that deletes orphaned temp files left by a crash, completing the durable-write
story.

### Changes Required

#### 1. Durable write in NoteFileService

**File**: `Notes/Services/NoteFileService.cs`

**Intent**: Make `Save` write the full content to a sibling temp file and then atomically move it
onto the target, so the live note is never the file being truncated. On a write failure, the temp
is cleaned up and the original is left intact.

**Contract**: `Save(string absolutePath, string text)` signature unchanged (interface
`INoteFileService` untouched). New internal behavior: write to a **fixed-name** temp sibling in the
same directory → `File.Move(temp, target, overwrite: true)` → on any fault, delete the temp and
rethrow (see "Critical Implementation Details — #4 temp cleanup on failure"). Use
`_fileSystem.File`/`_fileSystem.Path` throughout (no direct `System.IO`).

```csharp
// Shape (not final code): same-directory temp, atomic move, cleanup-on-fault, rethrow
var dir = _fileSystem.Path.GetDirectoryName(absolutePath);
var temp = _fileSystem.Path.Combine(dir, _fileSystem.Path.GetFileName(absolutePath) + ".tmp");
try
{
    _fileSystem.File.WriteAllText(temp, text);
    _fileSystem.File.Move(temp, absolutePath, overwrite: true);
}
catch
{
    try { _fileSystem.File.Delete(temp); } catch { /* swallow: don't mask the real write error */ }
    throw;
}
```

#### 2. Throwing-IFileSystem fault double

**File**: `Notes.Tests/Fakes/` (new fake, e.g. `ThrowingFileSystem.cs` or an NSubstitute setup helper)

**Intent**: Provide an `IFileSystem` that performs the temp write normally but throws on the
operation that would correspond to a crash *before* the rename completes, so the "original survives"
invariant is testable. `MockFileSystem` alone cannot reproduce this (its writes are atomic).

**Contract**: An `IFileSystem` decorator wrapping `MockFileSystem` that throws (e.g. `IOException`)
on the temp `WriteAllText` or on `Move`, configurable per test. Reuses the existing Fakes pattern.

#### 3. Durable-write tests

**File**: `Notes.Tests/NoteFileServiceTests.cs` (extend)

**Intent**: Assert both the mechanism and the safety invariant.

**Contract**: Two `[Fact]`s following §6.2:
- `Save_WhenCalled_WritesViaTempThenRenamesOntoTarget` — on `MockFileSystem`, assert the live
  target is never opened for truncation directly and the final content matches; assert the
  temp→move sequence occurs.
- `Save_WhenWriteFaultsBeforeRename_LeavesOriginalIntact` — pre-seed the target with known content,
  inject the fault double so the temp write/move throws, assert the original target content is
  **byte-for-byte unchanged** (independent oracle), the temp sibling is **cleaned up** (no `.tmp`
  left behind), and the exception surfaces as today (`IOException`/`UnauthorizedAccessException`
  still caught by `NoteEditorViewModel.DoSave`). Cover both fault points — a faulted `WriteAllText`
  and a faulted `Move`/rename — since cleanup must hold for either.

#### 4. Orphaned-temp sweep on workspace load

**File**: `Notes/Services/OrphanedTempCleaner.cs` + `Notes/Services/IOrphanedTempCleaner.cs` (new), DI reg in `Notes/Program.cs`, eager-resolution in `Notes/App.axaml.cs`

**Intent**: Reclaim temp files orphaned by a crash *between* write and rename (the one case the
inline `catch` can't reach) for notes that are never resaved. Runs on every workspace load so both
app start and runtime workspace switches are covered.

**Contract**: A service that, on `WorkspaceChangedMessage`, enumerates the temp pattern
(`*.md.tmp`, the suffix produced by `NoteFileService.Save` — share the suffix as a constant rather
than duplicating the literal) **under the new workspace root** and deletes each, best-effort
(per-file errors swallowed so one locked file doesn't abort the sweep). Inherently confined — it
only enumerates children of the handed-in root and never resolves user input, so it does **not**
depend on `PathGuard`. Register as a singleton; subscribe to the messenger like the other
message-driven services. Silent delete — crash-recovery of unsaved edits is an explicit non-goal.
**Eager-resolution is required:** the cleaner is a leaf singleton (nothing depends on it), and
M.E.DI constructs singletons lazily, so it never subscribes unless forced. Add
`_ = Services.GetRequiredService<IOrphanedTempCleaner>();` in `App.axaml.cs` **before**
`StartAsync` (which runs `InitializeAsync` → the first `WorkspaceChangedMessage`), mirroring the
existing `INoteSearchIndex` eager-resolve at `App.axaml.cs:29`. Without this line the startup sweep
— its primary crash-recovery scenario — silently never fires.

#### 5. Sweep test

**File**: `Notes.Tests/OrphanedTempCleanerTests.cs` (new)

**Intent**: Prove orphans are removed and real notes are not.

**Contract**: A message-driven `[Fact]`/`[Theory]` on `MockFileSystem` pre-seeded with a mix of
`.md` notes and `.md.tmp` orphans under a root (including a nested subdirectory). Send
`WorkspaceChangedMessage`; assert every `*.md.tmp` under the root is gone and every `*.md` note is
untouched (independent oracle). Include a case where a `.md.tmp` sits outside the root to confirm
the sweep stays within it.

### Success Criteria

#### Automated Verification

- Build passes: `dotnet build`
- New durable-write + sweep tests pass: `dotnet test`
- The fault test fails if `Save` is reverted to a plain `WriteAllText` (verify by local spike, then restore)
- The sweep test confirms `*.md` notes are never deleted and the sweep stays under the root
- Existing `NoteFileServiceTests` / `NoteEditorViewModelTests` round-trip + overwrite tests still pass

#### Manual Verification

- App still saves and reloads notes correctly end-to-end (`dotnet run --project Notes`): edit a note,
  switch notes, reopen — content persists; no stray `.tmp` files left in the workspace
- Drop a hand-made `orphan.md.tmp` in the workspace, restart the app — it's gone, real notes intact
- Saving a large note shows no perceptible regression from the extra temp write

**Implementation Note**: After automated verification passes, pause for human confirmation of the
manual items before starting Phase 3.

---

## Phase 3: #5 — Service-layer path containment

### Overview

Establish a service-layer trust boundary: a shared `PathGuard` fed the current workspace root from
`SettingsService`, invoked by every disk operation (save, create, **delete**, delete-folder), plus
defense-in-depth hardening of `NameValidator`. Proven by service-parity tests, including the
previously-unguarded delete path.

### Changes Required

#### 1. Workspace-root cache in SettingsService

**File**: `Notes/Services/SettingsService.cs`, `Notes/Services/ISettingsService.cs`

**Intent**: Expose the current workspace root as an in-memory value updated on `Load`/`Save`, so
`PathGuard` can read the live root without a per-write JSON disk read. SettingsService remains the
single source of truth (planning decision: cache inside SettingsService itself).

**Contract**: Add `string? CurrentWorkspacePath { get; }` to `ISettingsService`. In the
implementation, set the backing field whenever `Load()` returns settings and whenever `Save(settings)`
is called, from `settings.WorkspacePath`. No messenger subscription — the existing
Save-before-broadcast ordering in `MainWindowViewModel.cs:87-89` keeps it current.

#### 2. Shared PathGuard

**File**: `Notes/Services/PathGuard.cs` + `Notes/Services/IPathGuard.cs` (new), DI reg in `Notes/Program.cs`

**Intent**: One reusable confinement check the disk services call before touching disk: reject names
that would escape the workspace, and verify the resolved absolute path stays under the current root.
Fails closed when the root is unknown.

**Contract**: e.g. `void EnsureWithinWorkspace(string absolutePath)` that throws a dedicated
exception `PathContainmentException` when the root is null/empty or when
`Path.GetFullPath(absolutePath)` is not under the canonicalized root (trailing-separator-aware
`StartsWith`, platform path comparison). **`PathContainmentException` must derive from `IOException`**
so the existing `NoteEditorViewModel.DoSave` catch (`IOException`/`UnauthorizedAccessException`,
`NoteEditorViewModel.cs:162-174`) absorbs a guard rejection as a logged auto-save failure rather than
letting it escape uncaught from the `DispatcherTimer` callback and crash the app — this matters for
the fail-closed-on-null-root case (e.g. a lagged `CurrentWorkspacePath` while the editor's own
`_workspacePath` is still set). Depends on `ISettingsService` for the root. See "Critical
Implementation Details — #5" for the fail-closed and comparison rules. Register as a singleton in
`Program.cs` alongside the other services.

#### 3. Wire PathGuard into the disk services

**File**: `Notes/Services/NoteFileService.cs`, `Notes/Services/NoteFolderService.cs`, `Notes/Services/NoteDeleter.cs`, `Notes/Program.cs`

**Intent**: Make the service layer the enforcement point. Each disk operation calls
`PathGuard.EnsureWithinWorkspace(absolutePath)` before its `IFileSystem` call, so the
validator-bypassing delete path is covered automatically.

**Contract**: Inject `IPathGuard` into `NoteFileService`, `NoteFolderService`, `NoteDeleter`; call
the guard at the top of `Save`, `Create`, `Delete`, `DeleteFolder`. Public method signatures and
interfaces unchanged. Update DI registrations to pass the new dependency. The guard call sits
*above* Phase 2's temp-then-rename in `Save`.

#### 4. Harden NameValidator (defense-in-depth / dialog UX)

**File**: `Notes/Services/NameValidator.cs`

**Intent**: Give the create dialog inline feedback for the cases the character check misses today —
a bare `..` token and reserved Windows device names — so users see the error while typing. This is
UX parity, not the authoritative boundary (the PathGuard is).

**Contract**: Extend `ValidateCharacters` (or `ValidateNoteName`/`ValidateFolderName`) to reject a
trimmed input equal to `..` (and `.`) and the reserved device-name set (`CON`, `PRN`, `AUX`, `NUL`,
`COM1`–`COM9`, `LPT1`–`LPT9`, case-insensitive, with or without extension). Existing collision and
separator checks unchanged.

#### 5. Service-parity + containment tests

**File**: `Notes.Tests/NoteFileServiceTests.cs`, `Notes.Tests/NoteDeleterTests.cs` (new), `Notes.Tests/NameValidatorTests.cs`, optionally a focused `Notes.Tests/PathGuardTests.cs` (new)

**Intent**: Prove rejection happens at the **service** layer (not only the dialog), that the delete
path is now guarded, and that the validator surfaces the new cases.

**Contract**: `[Theory]`-driven tests with crafted inputs (`../escape`, `/etc/passwd`, `C:\x`, bare
`..`, `CON`) on `MockFileSystem` with a known workspace root set via `SettingsService`:
- Each disk service (`Save`, `Create`, `Delete`, `DeleteFolder`) throws `PathContainmentException`
  for an out-of-root absolute path and **does not** write/delete (assert the FS is unchanged) —
  independent oracle.
- An in-root path passes the guard and performs the op (no false positives).
- The **delete** parity test is explicit: a crafted out-of-root path reaches `NoteDeleter` and is
  rejected (closing the validator-bypass gap).
- `NameValidator` rejects bare `..` and reserved names; existing valid names still pass.

#### 6. Cookbook §6.2 completion + per-phase note

**File**: `context/foundation/test-plan.md` (§6.2, §6.6) — *appended by `/10x-implement` on phase close*

**Intent**: Finalize the integration recipe (service + MockFileSystem + root via SettingsService +
fault double for durability) and add the §6.6 Phase-2 note (e.g. the delete-path bypass and the
fail-closed root rule).

**Contract**: Prose only.

### Success Criteria

#### Automated Verification

- Build passes: `dotnet build`
- All new containment + parity tests pass: `dotnet test`
- Removing the guard call from any one disk service makes its parity test fail (verify by local
  spike, then restore)
- Full suite green, no regressions in existing service/VM tests

#### Manual Verification

- App end-to-end (`dotnet run --project Notes`): create note, create folder, delete note, delete
  folder, switch workspace — all normal in-workspace operations still work; no false rejections
- A crafted name in the new-note dialog shows the inline validation error (`..`, reserved name)
- Switching workspaces and then saving/deleting confines against the **new** root (the SettingsService
  cache tracked the change)

**Implementation Note**: After automated verification passes, pause for human confirmation before
closing the change.

---

## Testing Strategy

### Unit / integration tests

- **#3:** collision short-circuit leaves original intact (VM-level, MockFileSystem/fake).
- **#4:** temp-then-rename sequence; fault-before-rename preserves original (service-level,
  throwing `IFileSystem` decorator).
- **#5:** per-service rejection of traversal/absolute/reserved out-of-root paths incl. delete;
  in-root happy path passes; `NameValidator` rejects `..`/reserved.

### Key edge cases

- First save (target does not exist yet) still works under temp-then-rename.
- Null/empty workspace root → PathGuard fails closed.
- Root boundary string-prefix trap (`/work` vs `/workspace-evil`) handled by separator-aware compare.
- Reserved names with extensions (`CON.md`) and case-insensitivity.

### Manual testing steps

1. Edit, switch, reopen a note — content persists; no `.tmp` residue.
2. Create/delete notes and folders normally — no false rejections.
3. Type `..` / `CON` in the new-note dialog — inline error shown.
4. Switch workspace, then save and delete — confinement tracks the new root.

## Performance Considerations

Temp-then-rename adds one extra file write + a rename per debounced save. For single-note,
human-sized markdown this is negligible; the planning decision explicitly rejected the
read-back-verify variant to avoid a full extra read per keystroke-debounced save.

## Migration Notes

No data migration. No persisted-format change. `ISettingsService` and `INoteFileService` keep their
existing method signatures (only `ISettingsService` gains a read-only property), so DI wiring changes
are additive.

## References

- Research: `context/changes/file-safety/research.md`
- Test plan: `context/foundation/test-plan.md` §2 (risks #3/#4/#5), §4 (anti-patterns), §6.2 (recipe this fills)
- Lessons: `context/foundation/lessons.md` — "a missing safeguard may be a deliberate choice; verify intent" (applied: #3 not re-added)
- Collision guard: `Notes/ViewModels/NoteTreeViewModel.cs:200`, `Notes/Services/NameValidator.cs:31`
- Write site: `Notes/Services/NoteFileService.cs:36-39`
- Delete bypass: `Notes/ViewModels/NoteTreeViewModel.cs:295-296`
- Root wiring: `Notes/Services/SettingsService.cs:21-49`, `Notes/ViewModels/MainWindowViewModel.cs:65,74,87-89`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: #3 — Pin the collision guard (test-only)

#### Automated

- [x] 1.1 Build passes: `dotnet build` — 975fc75c
- [x] 1.2 New collision test passes: `dotnet test` — 975fc75c
- [x] 1.3 Collision test fails if the guard at `NoteTreeViewModel.cs:200` is removed (spike + restore) — 975fc75c

#### Manual

- [x] 1.4 Test pins real behavior with an independent oracle (pre-seeded original content)
- [x] 1.5 §6.2 cookbook entry accurate; TOCTOU acceptance stated

### Phase 2: #4 — Atomic durable write

#### Automated

- [x] 2.1 Build passes: `dotnet build`
- [x] 2.2 New durable-write + sweep tests pass: `dotnet test`
- [x] 2.3 Fault test fails if `Save` reverted to plain `WriteAllText` (spike + restore)
- [x] 2.4 Sweep test confirms `*.md` notes never deleted and sweep stays under the root
- [x] 2.5 Existing round-trip + overwrite tests still pass

#### Manual

- [x] 2.6 App saves/reloads correctly end-to-end; no stray `.tmp` files
- [x] 2.7 Hand-made `orphan.md.tmp` removed on next app start; real notes intact
- [x] 2.8 No perceptible regression saving a large note

### Phase 3: #5 — Service-layer path containment

#### Automated

- [ ] 3.1 Build passes: `dotnet build`
- [ ] 3.2 All new containment + parity tests pass: `dotnet test`
- [ ] 3.3 Removing the guard call from any disk service fails its parity test (spike + restore)
- [ ] 3.4 Full suite green, no regressions

#### Manual

- [ ] 3.5 Normal in-workspace create/delete/switch operations work; no false rejections
- [ ] 3.6 Crafted name (`..`, reserved) shows inline dialog validation error
- [ ] 3.7 After workspace switch, save/delete confine against the new root
