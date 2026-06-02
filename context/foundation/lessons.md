# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Don't dispose a CancellationTokenSource shared with an in-flight task

- **Context**: Cancellation hand-offs where a service cancels an older CTS and starts a new one while the previous task may still be running (e.g. `NoteSearchIndex.Receive(WorkspaceChangedMessage)` at `Notes/Services/NoteSearchIndex.cs:54-70`).
- **Problem**: Calling `Dispose` synchronously after `Cancel` looks like good hygiene but races the consumer: if the previous task is still inside `await ReadAsync(token)`, any later `token.Register(...)` (issued by IO continuations) throws `ObjectDisposedException`, which escapes the `OperationCanceledException` catch and becomes an unobserved task exception. Tags-and-search phase-2 review F2 documented this exact rollback; the all-phases sweep then re-proposed the disposed-fix without consulting that decision.
- **Rule**: Don't synchronously `Dispose` a `CancellationTokenSource` while a consumer task may still touch its token. Either (a) `Cancel` and rely on the finalizer for handle cleanup, or (b) join the consumer task (`await previousBuild`) before disposing.
- **Applies to**: Any service that owns a CTS alongside fire-and-forget `Task.Run` / `_ = ...` work — indexers, debouncers, background watchers, hot-reloaders. Code-review and impl-review agents should treat "CTS not disposed" as a possible *deliberate* choice in this codebase, not an automatic finding.

## Full-plan reviews must consult per-phase review fix decisions

- **Context**: When `/10x-impl-review` runs across all phases of a multi-phase plan, the reviewer agent receives the plan and the diff but doesn't automatically read the saved per-phase reviews under `context/changes/<change-id>/reviews/`.
- **Problem**: Phase-by-phase review decisions often *intentionally deviate* from the original plan (rolled-back fixes, broadened catches, removed disposes). A full-plan review that compares diff against plan will keep regenerating findings for those documented deviations because it doesn't know the plan was already amended in flight. In tags-and-search, both F1 (`_buildCts` dispose) and F2 (`catch (YamlException)` narrowing) were rolled-back phase-N decisions that the all-phases sweep re-proposed.
- **Rule**: Before launching plan-drift sub-agents, the reviewer must enumerate `context/changes/<change-id>/reviews/impl-review-phase-*.md`, parse the `Decision:` fields, and pass the FIXED/SKIPPED/ACCEPTED outcomes to the drift sub-agent as effective amendments to the plan. A "deviation from plan" that matches a prior phase review's FIXED decision is not a new finding.
- **Applies to**: Any review that operates across a scope larger than one phase: full-plan impl-reviews, post-implementation reviews, security reviews after a multi-phase rollout. The remedy is in the review skill's setup step (glob `reviews/*.md`, parse decisions, brief the sub-agents).
