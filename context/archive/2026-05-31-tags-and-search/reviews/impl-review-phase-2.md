<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Tags and Search — Phase 2

- **Plan**: context/changes/tags-and-search/plan.md
- **Scope**: Phase 2 of 3 (Search Index and Message Integration)
- **Date**: 2026-06-01
- **Verdict**: NEEDS ATTENTION → resolved during triage
- **Findings**: 1 critical · 3 warnings · 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | FAIL → resolved |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Lazy body read catches only FileNotFoundException

- **Severity**: ❌ CRITICAL
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/NoteSearchIndex.cs:159
- **Detail**: `File.ReadAllTextAsync` can also throw `DirectoryNotFoundException` when an intermediate folder disappears between index build and lazy read. That escaped the original catch and surfaced as an unhandled exception to the search caller.
- **Fix**: Widened catch to `IOException` (common base of `FileNotFoundException`, `DirectoryNotFoundException`, and other transient FS errors); `continue` semantics unchanged.
- **Decision**: FIXED

### F2 — CancellationTokenSource disposed while in use by old build

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/NoteSearchIndex.cs:57-59
- **Detail**: `Receive(WorkspaceChangedMessage)` called `Cancel`+`Dispose` synchronously while the previous Build task could still be inside an `await ReadAsync(token)`. Any subsequent `token.Register(...)` inside an IO continuation throws `ObjectDisposedException`, escaping `catch (OperationCanceledException)` and becoming an unobserved task exception.
- **Fix A ⭐ Recommended**: Dropped the synchronous `Dispose`; relying on the CTS finalizer for handle cleanup.
  - Strength: One-line change. `Cancel` is what matters; `Dispose` is the part introducing the race. Matches documented .NET guidance when you can't synchronously await the consumer.
  - Tradeoff: Slightly delayed handle cleanup — negligible at one CTS per workspace switch.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Decision**: FIXED via Fix A

### F3 — Build catches only OperationCanceledException

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Notes/Services/NoteSearchIndex.cs:223-226 (pre-fix)
- **Detail**: Build's outer `try/catch` absorbed only `OperationCanceledException`. A `FileNotFoundException` from `ReadAsync` (file deleted between scan and read) or any other `IOException` would kill the whole build — `IsReady` stays `false` forever for this workspace, leaving the user staring at "Indexing notes…". The plan's missing-file-race note explicitly required FNF tolerance.
- **Fix**: Added per-file `catch (IOException ex)` around `ReadAsync` inside the build loop with `Trace.WriteLine` and `continue`. Outer `OperationCanceledException` catch retained for cancellation.
- **Decision**: FIXED

### F4 — Save-during-build does redundant CoW on _entries

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Quality
- **Location**: Notes/Services/NoteSearchIndex.cs:78-83
- **Detail**: Original framing called the CoW upsert "dead work" because Build's swap overwrites `_entries` with `newEntries`. **Revised during triage**: on the *cancelled*-build path the swap doesn't run, so the CoW is the only thing that lands the mutation into `_entries`. The pending buffer handles the *successful*-build path. The two paths are belt-and-suspenders, not duplication.
- **Decision**: SKIPPED — kept current behavior; cost is one extra dictionary copy per save during build, negligible at PRD scale.

### F5 — Lock instead of Dispatcher.UIThread for swap

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Plan Adherence
- **Location**: Notes/Services/NoteSearchIndex.cs:23, 197-220
- **Detail**: Plan called for `await Dispatcher.UIThread.InvokeAsync(...)` to swap on the UI thread. Implementation uses a private `_gate` object and `lock`-based serialization. Functionally equivalent — serializes against message handlers (which run on UI thread in production) — and avoids Avalonia dispatcher dependency in tests.
- **Decision**: SKIPPED — acknowledged substitution.

### F6 — StrongReferenceMessenger used in tests

- **Severity**: 💡 OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Plan Adherence
- **Location**: Notes.Tests/NoteSearchIndexTests.cs:20
- **Detail**: Plan §9 specified `WeakReferenceMessenger`; implementation uses `StrongReferenceMessenger`, matching existing `NoteEditorViewModelTests` and `NoteTreeViewModelTests`. Sidesteps weak-ref GC flakiness in tests.
- **Decision**: SKIPPED — acknowledged substitution.

## Triage Summary

| Outcome | Findings |
|---------|----------|
| Fixed | F1, F2 (Fix A), F3 |
| Skipped | F4, F5, F6 |
| Recorded as rule | — |
| Accepted as risk | — |

Post-fix verification: `dotnet build` clean, `dotnet test` 93/93 pass.
