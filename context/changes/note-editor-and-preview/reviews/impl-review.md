<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Create, Edit, and Preview Markdown Notes

- **Plan**: `context/changes/note-editor-and-preview/plan.md`
- **Scope**: Full plan (3 of 3 phases)
- **Date**: 2026-05-29
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical · 3 warnings · 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Automated success criteria: `dotnet build` succeeds with 0 warnings; `dotnet test` is green (52 / 52). Manual rows checked through prior phase reviews and Phase 3 user confirmation.

## Findings

### F1 — Compiled-bindings convention silently dropped at every view root

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Pattern Consistency
- **Location**: Notes/MainWindow.axaml:10, Notes/Views/NoteTreeView.axaml:10, Notes/Views/NoteEditorView.axaml:11
- **Detail**: All three view roots resolve the locator via `DataContext="{ReflectionBinding Tree, Source={StaticResource Locator}}"`. CLAUDE.md mandates compiled bindings, `Notes.csproj:7` sets `AvaloniaUseCompiledBindingsByDefault=true`, and plan §9 explicitly says "Each view declares its own `x:DataType` for compiled bindings". Every other binding inside these files uses compiled form — only the locator hop reverts to reflection. The pattern will be copy-pasted into every future view.
- **Fix A ⭐ Recommended**: Document in CLAUDE.md / plan that locator hops use ReflectionBinding by necessity; keep code as-is.
  - Strength: Acknowledges the mechanical constraint (locator is a StaticResource so the source has no `x:DataType` known at compile time). Zero churn; future views copy the pattern knowingly.
  - Tradeoff: Loses one tiny perf/safety win per hop (one-time root binding — irrelevant in practice).
  - Confidence: HIGH — common Avalonia limitation; documenting matches what ships.
  - Blind spot: Haven't checked whether a typed factory + `{x:Static}` source would actually compile.
- **Fix B**: Restore compiled bindings via typed source (code-behind DataContext, typed factory).
  - Strength: Brings the locator wiring back under the compiled-binding umbrella.
  - Tradeoff: Three views + locator changes; may force code-behind assignments that re-introduce the very wiring the locator was meant to eliminate.
  - Confidence: MEDIUM — unverified that compiled bindings cleanly reach a StaticResource locator property.
  - Blind spot: Designer-time rendering behavior.
- **Decision**: FIXED via Fix A (CLAUDE.md/AGENTS.md updated with locator-hop exception)

### F2 — `Read` crash on UI thread (symmetric to phase-1 F1 fix on `Save`)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/ViewModels/NoteEditorViewModel.cs:86 (calls Notes/Services/NoteFileService.cs:10-18)
- **Detail**: Phase 1 F1 wrapped `_fileService.Save(...)` in `DoSave` with `try/catch (IOException) / (UnauthorizedAccessException)`. The symmetric `_fileService.Read(absolutePath)` call inside `Receive(NoteSelectedMessage)` has no protection. `NoteFileService.Read` special-cases missing files but not locked ones — a OneDrive / Dropbox / AV-sync lock or permission failure propagates through the messenger's synchronous publish path and crashes the app on the UI thread.
- **Fix**: Wrap the `_fileService.Read(absolutePath)` call in `try/catch (IOException) / (UnauthorizedAccessException)`; on failure, treat as empty + `Trace.WriteLine`.
- **Decision**: FIXED (try/catch + Trace logging mirrors the DoSave pattern; failure renders as empty editor)

### F3 — Bare `catch` swallows everything in NewNote handler (no log surface)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/ViewModels/NoteTreeViewModel.cs:68-78
- **Detail**: Phase-2 F3's fix wraps `await HandleNewNote()` in `try { … } catch { }` with no `Trace.WriteLine`, no user surface. `NoteEditorViewModel.DoSave` already established the right pattern. An `IOException` from `_fileService.Save(success.AbsolutePath, "")` (TOCTOU) vanishes — the user clicks Create, nothing happens, nothing logged. Bare `catch` is also broader than needed.
- **Fix**: Narrow to `catch (Exception ex)` (or specific types) and add `Trace.WriteLine($"New-note flow failed: {ex.Message}");` to match the DoSave pattern.
- **Decision**: SKIPPED

### F4 — Encoding asymmetry: `Read` uses `Encoding.UTF8`; `Save` uses `Utf8NoBom`

- **Severity**: ▫ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes/Services/NoteFileService.cs:8, 17, 22
- **Detail**: `Save` writes with cached `UTF8Encoding(false)`. `Read` passes `Encoding.UTF8` (the BOM-emitting singleton). Runtime behavior is correct (auto-detect), but the visual asymmetry invites a future "fix" in the wrong direction.
- **Fix**: Pass `Utf8NoBom` to `File.ReadAllText` (or drop the encoding arg — auto-detect works).
- **Decision**: FIXED via Fix differently (dropped the encoding arg on Read; BOM auto-detection still applies)

### F5 — `Receive(NoteSelectedMessage)` re-reads disk on same-note re-selection

- **Severity**: ▫ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/ViewModels/NoteEditorViewModel.cs:70-92
- **Detail**: Every `NoteSelectedMessage` for a file flushes the scheduler, then re-reads the file. Same-node republish would flush a pending save and immediately re-read it, blowing away cursor + undo stack. Latent today, but a real risk if any future path republishes the same selection.
- **Fix**: Short-circuit if `node?.Kind == NoteNodeKind.File && node?.RelativePath == _currentNote?.RelativePath` — return without re-reading.
- **Decision**: FIXED (early-return short-circuit added at top of Receive)

### F6 — `HandleNewNote` early-return paths have no automated coverage

- **Severity**: ▫ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria / Tests
- **Location**: Notes/ViewModels/NoteTreeViewModel.cs:82-104 vs Notes.Tests/NoteTreeViewModelTests.cs:106-149
- **Detail**: The happy-path theory covers selection branches. Missing: `_workspacePath` null, dialog returns null (Cancel), defensive re-validation fails. Phase 2 §2.11 "Cancelling leaves disk untouched" is a trivially automatable manual gate.
- **Fix**: Add `Receive_WhenNewNoteRequestedMessageWithoutWorkspace_DoesNothing`, `Receive_WhenNewNoteDialogCancelled_DoesNothing`, `Receive_WhenNewNoteDefensiveValidationFails_DoesNothing`.
- **Decision**: FIXED (3 facts added; tests green at 55 / 55)
