# Note-tree Folder Management — Plan Brief

> Full plan: `context/changes/note-tree-folder-management/plan.md`
> Research: `context/changes/note-tree-folder-management/research.md`

## What & Why

Add first-class folder management to the note tree — **New Folder**, **recursive folder delete**, and a **directory-aware tree** so empty folders persist and appear. Today an empty directory is invisible because the tree is built purely from `.md` file paths. This change is a prerequisite for the `templates` change, which needs to create and see the `.templates/` folder.

## Starting Point

The scanner (`WorkspaceScanner.ScanMarkdownFiles`) emits `*.md` paths only, and `NoteTreeBuilder` derives folders solely as ancestors of those files — so empty folders never render. The same scanner also feeds `NoteSearchIndex.Build`, which must keep ingesting files only. Delete is gated to file nodes in two places and `NoteDeleter` is `File.Delete` only. New Note already establishes the validator / prompt / message-driven entry-point patterns this change mirrors.

## Desired End State

Users can create folders (context menu, `Ctrl+Shift+N`, or File menu) and delete folders recursively with a confirmation that names the recursion. Empty folders persist across reloads, `.templates/` is visible and creatable, and the index/editor stay consistent when a folder is deleted. The filesystem-touching services run on an injected `IFileSystem`, making the new builder, validator, and creator unit-testable with `MockFileSystem`.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Directory-aware scanning | Option B | Locked upstream — empty folders must be first-class. | Research |
| Where directory enumeration lives | In `NoteTreeBuilder` (not the scanner) | Keeps `IWorkspaceScanner`/`NoteSearchIndex` contract untouched, so the index never ingests directories. | Plan |
| Builder testability | `System.IO.Abstractions` (`IFileSystem` + `MockFileSystem`) | Lets the now-IO-doing builder be unit-tested without disk. | Plan |
| `IFileSystem` adoption scope | Whole codebase (all IO-doing services) | Consistency — one filesystem-testability approach across the app, not a half-mocked seam. | Plan |
| Folder-delete index/editor refresh | Fan out one `NoteDeletedMessage` per contained `.md` | Reuses single-key index removal + exact-match editor-clear; no new message type. | Plan |
| Dot-prefixed directories | Shown, not skipped | `.templates/` must stay visible/creatable for the dependent change. | Plan |
| New Folder entry points | Context menu + File menu + `Ctrl+Shift+N` | Always-available and symmetric with New Note; works on an empty workspace. | Plan |
| Folder rename | Out of scope | Wasn't in the ask; keeps the change tight and unblocks `templates` fastest. | Plan |

## Scope

**In scope:** directory-aware tree builder; recursive folder delete; New Folder (context menu + menu + keyboard); `System.IO.Abstractions` migration of IO services; unit tests for builder + folder validator.

**Out of scope:** folder rename; drag-and-drop move; multi-select delete; migrating pure `Path` string ops; changing the scanner contract; a prefix-based folder-deleted message.

## Architecture / Approach

The scanner stays the single source of `.md` paths (still feeding the index). `NoteTreeBuilder` gains an injected `IFileSystem` and a new `Build(rootDirectory, filePaths)` signature: it builds the file tree as before and *additionally* enumerates sub-directories to materialize empty folders, merging them into the same folder set (no duplicates, dot-dirs included). Folder delete relaxes the two file-only gates (rejecting the root) and fans out per-file `NoteDeletedMessage`s. New Folder mirrors New Note end-to-end (validator → prompt → create → reload → select) with both a context-menu command and a message-driven menu/keyboard entry.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Filesystem abstraction | All IO services on `IFileSystem`; suite still green | Subtle behavior drift during refactor; `SettingsService` test seam |
| 2. Directory-aware tree | Empty folders persist and render | Duplicate nodes for folders that also contain files |
| 3. Folder delete | Recursive delete with index/editor cleanup | Missing a descendant file in the fan-out walk |
| 4. New Folder | Create via menu/keyboard/context menu | Parent resolution + name collision edge cases |

**Prerequisites:** none beyond the current `main`; Phase 1 must land before 2–4.
**Estimated effort:** ~2–3 sessions across 4 phases (Phase 1 + 4 are the largest).

## Open Risks & Assumptions

- The `System.IO.Abstractions` whole-codebase migration touches `SettingsService`, whose test constructor seam must be preserved — guarded by keeping its existing tests green in Phase 1.
- Two recursive enumerations per tree load (scanner + builder); assumed negligible at single-user scale.
- Assumes the existing per-file `NoteDeletedMessage` handlers in index and editor are the only reactors to deletion (verified in research).

## Success Criteria (Summary)

- An empty folder created on disk or via New Folder appears in the tree and survives reload.
- Deleting a folder removes it and all contained notes, closes an open note inside it, and clears them from search.
- New Folder works from context menu, File menu, and `Ctrl+Shift+N`, rejects invalid/colliding names, and `.templates` can be created and seen.
