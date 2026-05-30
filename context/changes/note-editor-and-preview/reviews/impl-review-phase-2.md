<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Create, Edit, Preview Markdown Notes — Phase 2

- **Plan**: `context/changes/note-editor-and-preview/plan.md`
- **Scope**: Phase 2 of 3 (new note creation)
- **Date**: 2026-05-29
- **Commit**: `bcc2b62`
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical · 3 warnings · 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — HandleNewNote is public; IRecipient wiring untested

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence / Pattern Consistency
- **Location**: `Notes/ViewModels/NoteTreeViewModel.cs:73`, `Notes.Tests/NoteTreeViewModelTests.cs:143`
- **Detail**: Plan §"Tree VM handles NewNoteRequestedMessage" specifies a private `HandleNewNote()`. Implementation widened to public so the [Theory] test invokes it directly via `sut.HandleNewNote()` instead of sending `NewNoteRequestedMessage` on the messenger. The IRecipient<NewNoteRequestedMessage> wiring (declared interface + `_messenger.RegisterAll(this)` in the ctor) is not exercised by any test. A refactor that broke registration would pass CI. The sibling `DeleteNote` stays private and is tested via `DeleteNoteCommand.ExecuteAsync` — a more analogous pattern is available.
- **Fix A ⭐ Recommended**: Make `HandleNewNote` private; send the message in the test.
  - Strength: Restores documented contract; exercises actual production path (RegisterAll → recipient dispatch → handler).
  - Tradeoff: Need to drain the async-void continuation (await `Task.Yield()`/spin, or refactor Receive to call an internal Task-returning helper that the test awaits via [InternalsVisibleTo]).
  - Confidence: HIGH — synchronous StrongReferenceMessenger is already used; dialog stub returns a ready Task.
  - Blind spot: Hasn't been measured whether async-void completion is fully deterministic without an explicit drain.
- **Fix B**: Keep public, add a separate test that sends the message.
  - Strength: Keeps existing direct-call test; adds explicit coverage of recipient wiring.
  - Tradeoff: Public surface stays widened; two tests for the same flow.
  - Confidence: MEDIUM.
  - Blind spot: Drift risk between the two test entry points.
- **Decision**: FIXED via Fix A — `HandleNewNote` made `private`; the test now sends `NewNoteRequestedMessage` through the messenger, exercising `RegisterAll` + dispatch. All Theory cases still pass synchronously because the dialog and load-tree stubs return already-completed Tasks.

### F2 — TOCTOU + silent truncation on file create

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `Notes/ViewModels/NoteTreeViewModel.cs:94–106`, `Notes/Services/NoteFileService.cs:21`
- **Detail**: Validator's `File.Exists` runs on line 94 (defensive re-validate); `_fileService.Save(absolutePath, "")` on line 106. Between them a file with the same name could appear; `NoteFileService.Save` uses `File.WriteAllText` which silently truncates an existing file. The new-note flow is the riskiest path because the parent folder + name come from the user.
- **Fix**: Add `CreateNew(absolutePath, text)` to `INoteFileService` using `File.Open(path, FileMode.CreateNew, FileAccess.Write)`. Call it from the new-note path; catch `IOException` and surface as a dialog error. Keep `Save` as-is for the editor's overwrite path.
  - Strength: Closes the truncation window at the OS level; validator's File.Exists stays best-effort.
  - Tradeoff: Adds one method to INoteFileService and one new test; small.
  - Confidence: HIGH — standard .NET idiom.
  - Blind spot: None significant.
- **Decision**: SKIPPED — accepted residual risk for single-user MVP.

### F3 — `async void` recipient has no error containment

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `Notes/ViewModels/NoteTreeViewModel.cs:68–71`
- **Detail**: `public async void Receive(NewNoteRequestedMessage)` awaits `HandleNewNote()`. Any exception thrown from the file save (F2) or future I/O will be raised on the SynchronizationContext, crashing the UI thread. Peer `Receive(WorkspaceChangedMessage)` sidesteps this via fire-and-forget `_ = LoadTreeCommand…`.
- **Fix**: Wrap the await in try/catch inside `Receive`; on exception, show via dialog service and return. Tied to F2's resolution.
- **Decision**: FIXED — `Receive(NewNoteRequestedMessage)` now wraps `await HandleNewNote()` in `try/catch { }` so async-void exceptions stop at the recipient boundary instead of crashing the UI thread.

### F4 — Path resolution duplicated between VM and validator

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `Notes/ViewModels/NoteTreeViewModel.cs:99–104`, `Notes/Services/NewNoteNameValidator.cs:34–40`
- **Detail**: The validator computes the absolute path internally to call `File.Exists`, then the VM recomputes it. A change to one side that doesn't update the other will silently diverge.
- **Fix**: Have `NoteNameResult.Success` carry the absolute path (`Success(string FileName, string AbsolutePath)`); VM uses it.
- **Decision**: FIXED — `Success` now carries `AbsolutePath`; the VM consumes it directly instead of recomputing the path.

### F5 — Dialog OnCreate trims; live validation does not

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: MVVM / Pattern
- **Location**: `Notes/Views/NewNoteDialog.axaml.cs:38, 59`
- **Detail**: `OnCreate` validates `NameInput.Text.Trim()`; `RefreshValidation` validates raw `NameInput.Text`. The validator itself trims, so functionally identical, but the call-site asymmetry hides intent.
- **Fix**: Pass `NameInput.Text` un-trimmed in both places (let the validator own trimming).
- **Decision**: FIXED — `OnCreate` no longer trims; both call sites now feed raw text to `_validate`, with the validator owning trimming.

### F6 — Test stub naming drift (`SavedPaths` vs `FilesByPath`)

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `Notes.Tests/NoteTreeViewModelTests.cs:188`, `Notes.Tests/NoteEditorViewModelTests.cs:144`
- **Detail**: Two parallel `INoteFileService` stubs back the dictionary as `SavedPaths` (new) and `FilesByPath` (existing). Same shape, two names. The new stub's `Read` always returns empty, which will mislead any future test that creates then reads.
- **Fix**: Rename to `FilesByPath` and make `Read` look up the dictionary.
- **Decision**: FIXED via shared-helper variant — extracted `Notes.Tests/Fakes/InMemoryNoteFileService.cs` (read-through `FilesByPath`); both `NoteEditorViewModelTests` and `NoteTreeViewModelTests` now share it instead of carrying parallel local stubs.
