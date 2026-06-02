<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Tags and Search

- **Plan**: context/changes/tags-and-search/plan.md
- **Scope**: All phases (3 of 3)
- **Date**: 2026-06-01
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical · 4 warnings · 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — _buildCts is never disposed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/NoteSearchIndex.cs:54-70, 177-239
- **Detail**: Each WorkspaceChangedMessage allocates a new CancellationTokenSource, cancels the previous one, but never disposes it. Workspace switches accumulate undisposed CTS objects. Build() also doesn't dispose its cts in a finally.
- **Fix**: After the gate-release in Receive, capture the old _buildCts and Dispose() it (post-cancel). In Build(), wrap the body in try/finally and Dispose cts at the end iff still the current one.
- **Decision**: FIXED + ACCEPTED-AS-RULE: Don't dispose a CancellationTokenSource shared with an in-flight task — original Receive-side dispose was the rolled-back phase-2 fix that re-introduced the ObjectDisposedException race. Applied a rule-compliant variant instead: `cts.Dispose()` inside Build's own `finally` clause, so dispose runs only after the consumer task's await chain has unwound.

### F2 — NoteMetadataParser catches Exception, not YamlException

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/NoteMetadataParser.cs:43-49
- **Detail**: Plan (line 92) and Critical Implementation Details specify `catch (YamlException)`. Actual implementation catches `Exception`, which swallows OOM, ThreadAbort, etc. Plan reviewers explicitly chose the targeted catch for contract precision.
- **Fix**: Narrow the catch to `YamlDotNet.Core.YamlException`.
- **Decision**: ACCEPTED-AS-RULE: Full-plan reviews must consult per-phase review fix decisions — phase-1 review F1 documented the deliberate broadening to `catch (Exception)` to honor the parser's "never throws" contract against YamlDotNet 18.x patch drift. No code change.

### F3 — RunSearch only catches OperationCanceledException

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Notes/ViewModels/NoteSearchViewModel.cs:99-119
- **Detail**: Any non-cancel exception from the index (lazy IOException, stub bug) becomes an UnobservedTaskException because the discard-assignment `_ = RunSearch(...)` orphans the task. Sibling pattern in NoteEditorViewModel.DoSave catches IOException + UnauthorizedAccess and Trace.WriteLine.
- **Fix**: Add a general `catch (Exception ex)` after the OperationCanceledException catch, with `Trace.WriteLine($"Search failed: {ex.Message}")`, so failures don't escape to the unobserved-exception sink.
- **Decision**: FIXED — added general `catch (Exception ex) { Trace.WriteLine($"Search failed: {ex.Message}"); }` plus `using System.Diagnostics;`. Sidebar dispose-race in the same VM queued as a follow-up in `follow-ups/review-fixes.md`.

### F4 — Search lazy read only catches IOException

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/NoteSearchIndex.cs:159-162
- **Detail**: `catch (IOException)` covers FileNotFoundException but not UnauthorizedAccessException. NoteEditorViewModel handles both explicitly elsewhere. If a permission error sneaks in during a search-time lazy body read, it crashes the search.
- **Fix**: Add `catch (UnauthorizedAccessException) { continue; }` alongside the IOException catch.
- **Decision**: SKIPPED — the new general `catch (Exception)` in RunSearch (F3) traces any UnauthorizedAccessException, so it won't crash the search; per-file skip is a nicety not worth its own catch clause here.

### F5 — Atomic swap uses lock(_gate), not Dispatcher.UIThread.Post

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Notes/Services/NoteSearchIndex.cs:206-228
- **Detail**: Plan §Critical Implementation Details named option (a) Dispatcher swap or (b) volatile reference. Implementation uses option (c) monitor lock — behaviourally correct, actually more robust because it serializes the swap with _isReady and _pendingDuringBuild updates that the plan also expected to happen atomically.
- **Fix**: None — the implementation is sound. Optionally update the plan comment near the gate to acknowledge the lock-based variant.
- **Decision**: SKIPPED — lock-based swap is behaviourally correct and arguably more robust; no action.

### F6 — Tree hidden instead of overlay painted opaque

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Notes/MainWindow.axaml:32, Notes/Views/SearchView.axaml:11
- **Detail**: Plan (line 440) called for an opaque background brush on the overlay. Actual implementation hides NoteTreeView via `IsVisible="{ReflectionBinding !Search.IsOpen, ...}"` and leaves SearchView background unset. The fluent ThemeBackgroundBrush resource didn't exist in Avalonia 12; the hide-tree approach was user-approved as the cleaner alternative. Same visual outcome.
- **Fix**: None — user-approved adaptation. The locator-mediated binding is mild action-at-a-distance; a future refactor could route it through MainWindowViewModel.IsSearchOpen for tighter coupling.
- **Decision**: SKIPPED — user-approved deviation; same visual outcome.

### F7 — Single-click open instead of double-click

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Notes/Views/SearchView.axaml.cs:20
- **Detail**: Plan (line 446) said "Double-click on an item". User explicitly requested single-click during manual testing. Implementation uses Tapped event with FindAncestorOfType<ListBoxItem>.
- **Fix**: None — user-approved during manual verification.
- **Decision**: SKIPPED — user-approved UX change.

### F8 — _suppressChangeHandlers flag in Close()

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Notes/ViewModels/NoteSearchViewModel.cs:26, 63-82, 146-158
- **Detail**: Plan didn't budget for a recursion-suppression flag. Needed because Close() sets Query="" and IncludeTemplates=false, both of which would otherwise restart the debounce timer / kick off an immediate search after the CTS was already cancelled. Try/finally restores the flag even on exception.
- **Fix**: None — addition is justified and minimal.
- **Decision**: SKIPPED — recursion-suppression flag is justified and minimal.
