---
date: 2026-06-08T20:20:03+02:00
researcher: lysy
git_commit: 906110a24ca3b5270dcaff3c056f203d8aad22ba
branch: (detached / jj)
repository: Notes
topic: "File-safety & data-loss guardrails — collision (#3), durable write (#4), path containment (#5)"
tags: [research, codebase, file-safety, NoteFileService, NameValidator, AutoSaveScheduler, NoteDeleter]
status: complete
last_updated: 2026-06-08
last_updated_by: lysy
---

# Research: File-safety & data-loss guardrails (rollout Phase 2)

**Date**: 2026-06-08T20:20:03+02:00
**Researcher**: lysy
**Git Commit**: 906110a24ca3b5270dcaff3c056f203d8aad22ba
**Branch**: (detached HEAD — repo uses jj)
**Repository**: Notes

## Research Question

Ground where, in the live codebase, the three Phase-2 file-safety risks from
`context/foundation/test-plan.md` actually live, and — per the change brief —
**verify rather than assume** that each guard exists:

- **#3** — create-from-template collides with an existing note of the same name and silently overwrites. *Verify the collision guard actually exists.*
- **#4** — a crash / fast quit mid-save truncates or empties the existing file. *Verify atomicity exists before simulating a crash against it.*
- **#5** — a crafted note/folder name (traversal, absolute, reserved) escapes the workspace at the **service** layer, not just the dialog. *Assert service parity, not only the UI validator.*

## Summary

The three risks land very differently once grounded:

| Risk | Guard status (verified) | Test posture |
|------|-------------------------|--------------|
| **#3 collision** | **Guard EXISTS** — `NameValidator.ValidateNoteName` does a `File.Exists` check, enforced in `NoteTreeViewModel.PromptNameAndSave` before the write. But it is a check-then-write across two services (TOCTOU gap), and the write itself (`WriteAllText`) is an unconditional overwrite. | Test the guard *works* on the happy path (existing name → early return, original untouched) + document that the write layer has no guard of its own. Don't test "a guard exists" — it does. |
| **#4 atomic save** | **Atomicity does NOT exist** — the save is a single in-place `File.WriteAllText` of the live `.md` file. No temp-then-rename, no `File.Replace`, no backup, no flush/fsync anywhere in the repo. | The anti-pattern in `test-plan.md §4` applies directly: **do not simulate a crash against a non-atomic write** — it cannot pass. The honest Phase-2 outcome is either (a) a test that documents the gap as a known limitation, or (b) introduce atomic-write *then* test it. This is a design decision for the plan, not a pure test addition. |
| **#5 path containment** | **Validation is DIALOG-ONLY** — exactly one production caller of `NameValidator` (the ViewModel). All three disk services (`NoteFileService`, `NoteFolderService`, `NoteDeleter`) trust a pre-built absolute path with zero validation and zero root-confinement. The delete flow skips the validator entirely. | Service-parity tests are the whole point here, and they will *fail by default* — the services have no guard. Like #4, closing the gap is an implementation decision the plan must own, not just an assertion. |

**Big picture:** Only #3 has a working guard to pin. #4 and #5 are *missing* guards
— the research confirms the test-plan's "verify, don't assume" instinct was correct.
A test phase that only *asserts* the desired behavior would produce red tests; the
plan must decide, per risk, whether Phase 2 (a) adds the guard then tests it, or
(b) lands a characterization test that documents the current unsafe behavior and
defers the fix. That decision is `/10x-plan`'s, but the evidence below scopes it.

## Detailed Findings

### Risk #3 — Create-from-template collision (guard EXISTS, with a TOCTOU caveat)

**Flow.** `NewFromTemplateRequestedMessage` → `NoteTreeViewModel.Receive`
(`Notes/ViewModels/NoteTreeViewModel.cs:114`) → `HandleNewFromTemplate`
(`:132-177`): lists templates, picks one, reads + parses it, collects field
values via `TemplateFormViewModel`, renders with `TemplateRenderer`, then calls
the **shared** `PromptNameAndSave(rendered)` (`:176`). Plain new-note and
new-from-template converge on this same save path.

**The guard.** In `PromptNameAndSave` (`NoteTreeViewModel.cs:179-219`):

```csharp
// NoteTreeViewModel.cs:200-203
if (_nameValidator.ValidateNoteName(entered, workspace, parentRelative) is not NoteNameResult.Success success)
{
    return;
}
```

`ValidateNoteName` performs the collision check:

```csharp
// Notes/Services/NameValidator.cs:30-34
var absolutePath = ResolveAbsolutePath(workspaceAbsolutePath, parentRelativePath, fileName);
if (_fileSystem.File.Exists(absolutePath))
{
    return new NoteNameResult.Failure("A note with that name already exists");
}
```

The same validator is wired as the dialog's live inline validator
(`NoteTreeViewModel.cs:190-194`), so the user sees the collision message while
typing, and the post-prompt re-validation (`:200`) blocks the save. **On the
normal single-threaded UI path, the silent overwrite cannot happen.**

**The write site has no guard of its own** — `NoteTreeViewModel.cs:209` →

```csharp
// Notes/Services/NoteFileService.cs:36-39
public void Save(string absolutePath, string text)
{
    _fileSystem.File.WriteAllText(absolutePath, text);   // unconditional overwrite (truncate-or-create)
}
```

**TOCTOU caveat (residual risk).** The `File.Exists` check
(`NameValidator.cs:31`) and the `WriteAllText` (`NoteFileService.cs:38`) are two
separate operations with no shared lock and no create-if-not-exists semantics.
The silent-overwrite outcome is reachable only through that check-then-write
window, not the happy path.

**Template note:** the chosen template only determines note *content*; the
filename is derived purely from the user's typed name (`.md` appended if absent,
`NameValidator.cs:25-28`; path built by `ResolveAbsolutePath`, `:80-89`). Two
templates saved under the same typed name target the same path.

### Risk #4 — Mid-save truncation (atomicity does NOT exist)

**Save path.** Debounced auto-save only; there is no separate explicit-save
command. User types → `NoteEditorViewModel.OnEditorTextChanged`
(`Notes/ViewModels/NoteEditorViewModel.cs:144-151`) stores text and calls
`_scheduler.Bump()`. After the debounce, the scheduler fires `DoSave`
(wired at `NoteEditorViewModel.cs:45`), which resolves the absolute path and
calls `_fileService.Save(absolutePath, _currentEditorText)`
(`:153-175`, catching only `IOException`/`UnauthorizedAccessException`).
`Flush()` forces an immediate save on workspace change (`:62`) and note-selection
change (`:81`).

**The write is a single in-place overwrite** — `NoteFileService.Save` →
`_fileSystem.File.WriteAllText` (`NoteFileService.cs:38`), targeting the **live
note file** (not a temp copy). `WriteAllText` opens with truncation, then streams
the bytes.

**No atomicity mechanism anywhere.** A repo-wide grep for `File.Replace`,
`File.Move`, `.tmp`, `GetTempFileName`, `FileStream`, fsync turns up nothing on
the save path — the only `Flush` in the repo is `AutoSaveScheduler.Flush()` (a
*timer* flush, unrelated to disk durability). **A crash after the file is opened
but before the write completes leaves the note truncated or empty, with no
recoverable prior copy.** The Risk #4 scenario is real.

**AutoSaveScheduler** (`Notes/Services/AutoSaveScheduler.cs`) wraps an Avalonia
`DispatcherTimer`, 500 ms (`:19`):
- `Bump()` (`:23-27`) = `Stop(); Start()` → debounce on every keystroke.
- `OnTick` (`:45-49`) stops the timer, then invokes `_onSave`.
- `Flush()` (`:29-38`) fires the save immediately if pending (note/workspace switch).
- `Cancel()` (`:40-43`) stops without saving (on `NoteDeletedMessage`, `NoteEditorViewModel.cs:123`).
- No `CancellationToken` reaches the write; an in-flight `WriteAllText` cannot be cancelled. Saves are serialized by the UI thread (sync write on `DispatcherTimer`), so true overlap can't happen, but the design relies on single-thread dispatch, not an explicit in-flight lock.

**Testability caveat.** `NoteFileService` takes `IFileSystem`
(`NoteFileService.cs:9-14`) so `MockFileSystem` can drive the save path —
**but the mock write is atomic in-memory**, so `MockFileSystem` alone cannot
reproduce a real mid-write truncation. Exercising the failure mode needs an
injected fault (an `IFileSystem`/stream substitute that throws mid-write) or a
redesigned atomic write to assert against.

**Existing tests** (`Notes.Tests/NoteFileServiceTests.cs`,
`NoteEditorViewModelTests.cs`) cover content correctness, overwrite, and
encoding/line-ending round-trips — **none exercise atomicity, crash safety, or
partial writes.**

### Risk #5 — Path containment (validation is DIALOG-ONLY; no service parity)

**What `NameValidator` rejects** (`Notes/Services/NameValidator.cs`, via
`ValidateCharacters` `:56-78`, called by `ValidateNoteName` `:20` and
`ValidateFolderName` `:41`):
- empty/whitespace (`:59-62`);
- path separators `/` and `\` (`:64-67`);
- `Path.GetInvalidFileNameChars()` (`:69-75`) — on Linux just `/` and `\0`; the Windows reserved set (`< > : " | ? *`) only applies on Windows.

**Gaps even within the validator:**
- **`..` traversal** is not checked as a token — only incidentally blocked when accompanied by a separator. A bare `".."` passes `ValidateCharacters`, then `ResolveAbsolutePath` → `Path.Combine(workspace, "..")` resolves to the workspace parent.
- **Absolute paths / drive-relative `C:`** — `/abs`, `C:\x` caught incidentally by the separator check; bare `C:` (no separator) only fails on Windows (`:` is an invalid char there), not Linux.
- **Reserved Windows device names (CON, PRN, AUX, NUL, COM1…, LPT1…)** — not checked at all.
- Even on success, `ResolveAbsolutePath` (`:80-89`) uses raw `Path.Combine` with **no** post-resolution check that the result stays under the workspace root.

**Exactly one production caller** of `NameValidator` (grep `--include=*.cs`):
the ViewModel `NoteTreeViewModel` (field/ctor `:26,50`; used at `:192,200` for
notes and `:233,241` for folders). DI reg at `Notes/Program.cs:50`. **No service
calls it.**

**The three disk services validate nothing and confine nothing** — each takes a
pre-built absolute path and hits `IFileSystem` directly:
- `NoteFileService.Save` → `File.WriteAllText(absolutePath, text)` (`NoteFileService.cs:36-39`)
- `NoteFolderService.Create` → `Directory.CreateDirectory(absolutePath)` (`NoteFolderService.cs:14`)
- `NoteDeleter.Delete` → `File.Delete(absolutePath)` (`NoteDeleter.cs:14`); `DeleteFolder` → `Directory.Delete(absolutePath, recursive: true)` (`:16`)

**The delete flow bypasses the validator entirely** — paths are built straight
from a tree node value:

```csharp
// NoteTreeViewModel.cs:295-296
var relative = node.RelativePath.Replace('/', Path.DirectorySeparatorChar);
var absolutePath = Path.Combine(_workspacePath, relative);
// → _noteDeleter.Delete(absolutePath) (:326) or DeleteFolder(absolutePath) (:308, recursive)
```

Containment of deletes rests entirely on `WorkspaceScanner` only ever emitting
in-tree relative paths. The editor save (`NoteEditorViewModel.cs:93,161`) is the
same validator-free `Path.Combine(_workspacePath, relative)` shape.

**No workspace confinement anywhere.** The root is a plain string passed by
`WorkspaceChangedMessage` (`Messages.cs:5`), set in
`MainWindowViewModel.cs:73-74,88-89`, consumed into `_workspacePath` fields
(`NoteTreeViewModel.cs:85`, `NoteEditorViewModel.cs:63`, `NoteSearchIndex.cs:63`).
A repo-wide grep found **no** `Path.GetFullPath(...).StartsWith(root)` guard; the
only `StartsWith` uses are unrelated (`.templates/` filtering, dot-file skip).

**Conclusion:** if a crafted name reaches the service layer (or a future caller),
the service writes/deletes wherever told. Validation is a single create-dialog
entry point with gaps, absent from delete, and absent from all three disk
services. This is not defense-in-depth.

## Code References

- `Notes/ViewModels/NoteTreeViewModel.cs:114` — `Receive(NewFromTemplateRequestedMessage)` entry
- `Notes/ViewModels/NoteTreeViewModel.cs:132-177` — `HandleNewFromTemplate` (template → render → save)
- `Notes/ViewModels/NoteTreeViewModel.cs:179-219` — `PromptNameAndSave` (shared save path; collision guard at `:200`)
- `Notes/ViewModels/NoteTreeViewModel.cs:209` — note write call site
- `Notes/ViewModels/NoteTreeViewModel.cs:250` — folder create call site
- `Notes/ViewModels/NoteTreeViewModel.cs:295-296,308,326` — **delete path built without the validator**
- `Notes/Services/NameValidator.cs:20-34` — `ValidateNoteName` + `File.Exists` collision check
- `Notes/Services/NameValidator.cs:56-78` — `ValidateCharacters` (separators + invalid chars; no `..`/reserved-name handling)
- `Notes/Services/NameValidator.cs:80-89` — `ResolveAbsolutePath` (raw `Path.Combine`, no confinement)
- `Notes/Services/NoteFileService.cs:36-39` — `Save` → `WriteAllText` (unconditional, non-atomic, no validation)
- `Notes/Services/NoteFolderService.cs:14` — `Create` → `CreateDirectory` (no validation)
- `Notes/Services/NoteDeleter.cs:14,16` — `Delete` / `DeleteFolder` (no validation; recursive)
- `Notes/Services/AutoSaveScheduler.cs:19,23-49` — 500 ms debounce, `Bump`/`Flush`/`Cancel`/`OnTick`
- `Notes/ViewModels/NoteEditorViewModel.cs:45,144-175` — `DoSave` wiring + triggers; `:93,161` validator-free path build
- `Notes/Program.cs:50` — `NameValidator` DI registration
- `Notes/Messaging/Messages.cs:5` — `WorkspaceChangedMessage(string WorkspacePath)`
- Tests: `Notes.Tests/NoteFileServiceTests.cs`, `NoteEditorViewModelTests.cs`, `NameValidatorTests.cs`, `NoteTreeViewModelTests.cs`

## Architecture Insights

- **Path construction lives in the ViewModel/validator, not the services.** All three disk services are deliberately thin "given an absolute path, do the IO" primitives. This is clean MVVM separation but means there is **no service-layer trust boundary** — the security/containment burden sits entirely on the one UI caller, and any path not routed through `NameValidator` (notably delete and editor-save) is unguarded.
- **`NameValidator` conflates three concerns** in one method: character validity, uniqueness (collision), and `.md` normalization. The collision guard for #3 is a side-effect of the uniqueness check, not a dedicated atomic create.
- **`IFileSystem` (System.IO.Abstractions) is pervasive** — every disk service is constructor-injected with it, so `MockFileSystem` can drive integration tests for #3 and #5. For #4, the mock's in-memory write is atomic, so reproducing truncation needs an injected fault rather than `MockFileSystem` alone.
- **The "verify, don't assume" instruction paid off:** #3's guard exists (so don't re-add it), while #4 and #5 guards are absent (so naive assertion tests would be red). The shape of Phase 2 differs per risk.

## Historical Context (from prior changes)

- `context/foundation/test-plan.md` §2 Risk Map (#3/#4/#5) and §2 Risk Response Guidance — the source rows; each "Must challenge" column matches what this research verified (collision guard, atomicity, dialog-only validation).
- `context/foundation/test-plan.md` §6.1/§6.4 and `context/changes/testing-template-pipeline/` (Phase 1, archived/merged) — established the cookbook for unit + VM tests (independent oracle, `MockFileSystem`, NSubstitute, `InMemoryNoteFileService`). §6.2 (integration test recipe) is still **TBD — this Phase 2 is what fills it.**
- `context/foundation/lessons.md` — the two recorded lessons concern CTS disposal and full-plan-review consulting per-phase decisions; neither bears directly on file-safety, but both reinforce "a missing safeguard may be a deliberate choice — verify intent before adding."

## Related Research

- `context/changes/testing-template-pipeline/research.md` (Phase 1, prior) — template parse/form/render pipeline; the create-from-template content path feeding into #3's save.

## Open Questions

These are decisions for `/10x-plan`, surfaced by the evidence:

1. **#4 — fix-then-test or characterize-then-defer?** Phase 2's risk intent says "prove durable writes." But the write is not atomic, so a straight assertion test fails. Does Phase 2 introduce an atomic temp-then-rename in `NoteFileService.Save` (a small, well-scoped change) and then test it, or land a characterization test documenting the non-atomic behavior as a known limitation and defer the fix? The test-plan anti-pattern ("simulating a crash against a non-atomic write that cannot pass") argues against a naive crash test.
2. **#5 — where does the service-parity guard go?** A confinement check (`Path.GetFullPath(target).StartsWith(workspaceRoot)`) could live in each disk service, or in a shared helper the services call. And the **delete path** (`NoteTreeViewModel.cs:295`) bypasses `NameValidator` entirely — should service-layer confinement be the single enforcement point so delete is covered too?
3. **#3 — is the TOCTOU window worth closing?** For a single-user desktop app the check-then-write race is low-likelihood. Phase 2 could pin the existing happy-path guard and explicitly note the TOCTOU gap as accepted, rather than introducing atomic create-if-not-exists.
4. **Scope boundary:** all three "fix" options above edit production code, not just tests. Confirm whether Phase 2 is allowed to add guardrails (the change brief's "prove … never destroy data" implies yes for #4/#5) or is strictly test-only — this materially changes the plan.
