# Review Follow-ups

Outstanding fixes deferred from `/10x-impl-review` triage.

## NoteSearchViewModel — CTS Cancel+Dispose race

- **Discovered**: 2026-06-02, during triage of `reviews/impl-review.md` F3.
- **Location**: `Notes/ViewModels/NoteSearchViewModel.cs:92-93` (`TriggerSearchNow`) and `:142-143` (`Close`).
- **Symptom**: Same pattern as the rolled-back phase-2 fix in `NoteSearchIndex`: `_searchCts?.Cancel(); _searchCts?.Dispose();` runs synchronously while the previous `RunSearch` may still be inside `await _index.Search(token)`. Any later `token.Register(...)` from an IO continuation throws `ObjectDisposedException`, which now escapes to the new general `catch (Exception)` and gets traced — visible but suspicious.
- **Rule**: See `context/foundation/lessons.md` → "Don't dispose a CancellationTokenSource shared with an in-flight task".
- **Suggested fix**: Drop the synchronous `Dispose` calls in both `TriggerSearchNow` and `Close`. Rely on the CTS finalizer for handle cleanup (same resolution as `NoteSearchIndex` phase-2 review F2). `Cancel` is what matters; `Dispose` reintroduces the race.
- **Why deferred**: Out of scope for the F3 triage decision (F3 was about the missing general catch, not the dispose race). Should be a tight follow-up commit before the change ships.
