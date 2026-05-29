<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Create, Edit, and Preview Markdown Notes

- **Plan**: context/changes/note-editor-and-preview/plan.md
- **Scope**: Phase 1 of 3
- **Date**: 2026-05-29
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical · 2 warnings · 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — NoteFileService propagates I/O exceptions to the UI thread

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality / Pattern Consistency
- **Location**: Notes/Services/NoteFileService.cs:10-23 + Notes/ViewModels/NoteEditorViewModel.cs:114 (DoSave)
- **Detail**: `Read` and `Save` propagate every I/O failure (IOException, UnauthorizedAccessException, file locked by virus scanner / OneDrive / Dropbox sync) to callers. `DoSave` runs on a DispatcherTimer tick with no try/catch — a transient lock crashes the UI thread. Inconsistent with the S-01 pattern in `Notes/Services/SettingsService.cs:29-37`, which catches and falls back to `AppSettings.Empty` on read failures. Plan contract says "other read failures propagate" for Read, but DoSave's timer-driven entry has no recovery story.
- **Fix**: Wrap `DoSave` in try/catch with logging at minimum; longer term, surface failures via a `SaveFailedMessage` so the user knows their note didn't persist. Keep `INoteFileService`'s throwing contract — resilience belongs at the timer call site, not the leaf service.
  - Strength: Localizes the change to one place; keeps the file service's contract clean (it's a thin wrapper, like SettingsService is for JSON).
  - Tradeoff: Silent failures without surfacing to the user are worse than a crash for unsaved-work scenarios — a log line alone isn't great UX.
  - Confidence: MEDIUM — try/catch is mechanical; the right user surface is a design question that may belong in a later phase.
  - Blind spot: Haven't audited whether Avalonia's unhandled-exception hook would currently surface this nicely.
- **Decision**: FIXED (try/catch IOException + UnauthorizedAccessException in NoteEditorViewModel.DoSave, Trace.WriteLine on failure)

### F2 — AutoSaveScheduler.Flush() silently no-ops when not armed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/AutoSaveScheduler.cs:29-38
- **Detail**: `Flush()` only raises `OnSave` when `_timer.IsEnabled` (a Bump is pending). Today's callers follow `OnEditorTextChanged` on the UI thread, so "timer not armed" really does mean "nothing to save". The risk is future: any caller assuming "Flush always persists pending edits" will get silently dropped writes if a future feature mutates `_currentEditorText` without going through the editor control. Implementation matches the plan's contract; the contract itself is the fragile bit.
- **Fix**: Rename to `FlushIfPending` (or add a one-line interface comment documenting the precondition) so the next caller doesn't assume idempotent persist semantics.
- **Decision**: SKIPPED

### F3 — WeakReferenceMessenger in tests could be GC-flaky

- **Severity**: 📝 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes.Tests/NoteEditorViewModelTests.cs:13 · Notes.Tests/NoteTreeViewModelTests.cs:13
- **Detail**: Tests use `new WeakReferenceMessenger()` (good — isolated, not `.Default`). VMs are constructed inside `BuildSut()` and held by a test-method local. Because the messenger holds recipients via weak references, a hypothetical GC between `BuildSut()` and the next `_messenger.Send(...)` could unregister the VM and silently break the test. xUnit runs each test fast enough that this doesn't fire in practice — but it's a known flaky-test trap.
- **Fix**: Switch test-only messenger to `StrongReferenceMessenger`, or keep a strong field reference to each constructed VM.
- **Decision**: FIXED (swapped WeakReferenceMessenger → StrongReferenceMessenger in both test classes)
