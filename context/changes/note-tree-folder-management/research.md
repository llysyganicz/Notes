---
date: 2026-06-02T21:26:38+02:00
researcher: Claude (10x-research)
git_commit: 6b636e00a4678a61a5dc9eaae46809816b6bede1
branch: (detached HEAD at 6b636e0)
repository: Notes
topic: "Note-tree folder management — create folder, delete folder, directory-aware tree"
tags: [research, codebase, note-tree, scanner, delete, context-menu]
status: complete
last_updated: 2026-06-02
last_updated_by: Claude (10x-research)
---

# Research: Note-tree folder management

**Date**: 2026-06-02T21:26:38+02:00
**Researcher**: Claude (10x-research)
**Git Commit**: 6b636e00a4678a61a5dc9eaae46809816b6bede1
**Repository**: Notes

## Research Question

Scope three note-tree management capabilities (prerequisite for `templates`): (1) a **New Folder** context-menu command, (2) **deleting a folder/directory** (currently file-only), and (3) a **directory-aware scanner/tree** so empty folders persist and appear. Decisions are locked: directory-aware scanner = **Option B**; this is a **separate change** that blocks `templates`.

## Summary

The note tree already has a right-click context menu (Delete only) with a clean wiring pattern, so adding commands is cheap. Folder delete is a small, well-bounded change. The load-bearing decision — already made — is **directory-aware scanning (Option B)**: today the scanner emits only `*.md` file paths and the tree builder synthesizes folder nodes solely as ancestors of those files, so an empty directory is invisible. Making empty folders first-class requires changing the `IWorkspaceScanner` contract and `NoteTreeBuilder`, and reconciling the search-index build path that shares the scanner.

## Detailed Findings

### Existing context-menu + command surface
- A `ContextMenu` already exists on the tree item's `TextBlock` with one "Delete" `MenuItem` (`NoteTreeView.axaml:16-22`).
- Wiring pattern for new items: `Command="{Binding $parent[TreeView].((vm:NoteTreeViewModel)DataContext).XxxCommand}" CommandParameter="{Binding}"`. The bound parameter is the **right-clicked** `NoteTreeNode`, not `SelectedNode`.
- Two trigger styles coexist: **New Note** is message-driven from MainWindow (Ctrl+N / File menu → `NewNoteRequestedMessage` → `NoteTreeViewModel.HandleNewNote`, resolves parent from `SelectedNode`, `NoteTreeViewModel.cs:68-120,162-177`); **Delete** is a direct context-menu command (`DeleteNoteCommand(node)`). New Folder should follow the Delete/context-menu style: `[RelayCommand] NewFolder(NoteTreeNode? node)`.

### Current delete flow (to extend) — `NoteTreeViewModel.cs:136-160`
- File-only gates in **two** places: runtime guard `node.Kind != NoteNodeKind.File` (`:139`) and `CanExecute` → `CanDeleteNote` (`:159-160`).
- Flow: `IConfirmDialogService.Confirm(title, message)` (`IConfirmDialogService.cs:7`) → `INoteDeleter.Delete(absolutePath)` = `File.Delete` only (`NoteDeleter.cs:7`) → `NoteDeletedMessage(relativePath)` (single key) → full tree rebuild via `LoadTreeCommand` (`NoteTreeViewModel.cs:122-134,156`).
- Reactions: index removes exactly one key (`NoteSearchIndex.cs:88-100`); editor clears only on exact-path match (`NoteEditorViewModel.cs:116-128`).
- Model: `NoteTreeNode(Name, RelativePath, Kind, Children)`, `NoteNodeKind { Folder, File }` (`Models/NoteTreeNode.cs:5-15`). Synthetic root is a Folder with empty Name/RelativePath (`NoteTreeBuilder.cs:11`) and is not bound (TreeView binds `Root.Children`, `NoteTreeView.axaml:11`).

### Capability 1 — Folder DELETE (small)
1. Relax both file-only gates (`:139`, `:159-160`) to allow `Folder` nodes with **non-empty `RelativePath`**; reject empty-`RelativePath` (root) as a guard rail.
2. Add recursive delete to the deleter: `Directory.Delete(path, recursive: true)` — extend `INoteDeleter`/`NoteDeleter` (e.g. `DeleteFolder`).
3. Index/editor refresh: emit **one `NoteDeletedMessage` per contained `.md` file** (walk `node.Children` recursively — file nodes carry `RelativePath`) so existing single-key index removal and exact-match editor-clear work without a new message type. (Alternative: a `FolderDeletedMessage(prefix)` with prefix handlers — more surface area.)
4. Confirmation wording must signal recursive deletion ("delete this folder and all notes inside it").

### Capability 2 — New FOLDER + Capability 3 — directory-aware scanner (Option B)
1. No workspace directory-creation code exists (only `SettingsService.cs:45` for config dir). Add `Directory.CreateDirectory` behind a service.
2. Name validation: reuse `NewNoteNameValidator` char rules (`NewNoteNameValidator.cs:13-29`) but it appends `.md` (`:31-33`) and collision-checks `File.Exists` (`:42-45`). Add a sibling `INewFolderNameValidator`/`NewFolderNameValidator` (no `.md`, `Directory.Exists` collision) returning the closed-DU `NoteNameResult` (`INewNoteNameValidator.cs:8-15`). Reuse `INewNoteDialogService.PromptForName(parent, validate)` for the prompt.
3. Add `[RelayCommand] NewFolder(NoteTreeNode? node)` on `NoteTreeViewModel` + a context-menu `MenuItem` in `NoteTreeView.axaml`.
4. **Directory-aware scanning (Option B, decided):** today an empty folder is invisible — `WorkspaceScanner.ScanMarkdownFiles` enumerates `*.md` files only (`WorkspaceScanner.cs:24`), and `NoteTreeBuilder.Build` derives folder nodes purely from file path segments (`NoteTreeBuilder.cs:14-55`). To make empty folders persist:
   - Extend `IWorkspaceScanner` to also surface directories (e.g. a new `ScanDirectories(root)` or a richer result type carrying both files and dirs). Decide recursion + sorting consistent with the existing `EnumerationOptions`/`Ordinal` sort (`WorkspaceScanner.cs:16-36`).
   - Teach `NoteTreeBuilder` to materialize folder nodes from the directory list even when they contain no files.
   - **Reconcile the search-index build path:** `NoteSearchIndex.Build` also calls `_scanner.ScanMarkdownFiles` (`NoteSearchIndex.cs:182`). If the scanner contract changes (e.g. signature or return type), the index must keep ingesting **files only** (directories have no content to index). Prefer an additive method (`ScanDirectories`) over changing `ScanMarkdownFiles`' signature to avoid disturbing the index.
   - The existing `.templates/` search filter (`NoteSearchIndex.cs:136`) and `.`-prefixed *filename* skip (`WorkspaceScanner.cs:26-30`) are unaffected; note the filename skip does NOT skip `.`-prefixed directories, so `.templates/` will enumerate as a directory (desired — it must be visible/creatable).

### Conventions
- DI: `AddSingleton<IInterface, Impl>()` in `Program.cs:36-53`; new validators/services slot beside `INewNoteNameValidator` (`:47`) / `INoteDeleter` (`:40`). VMs are singletons in this file despite AGENTS.md's "transient" note — match the file.
- Messaging: closed `sealed record` messages in `Messaging/Messages.cs`; `IRecipient<T>` + `_messenger.RegisterAll(this)` (`NoteTreeViewModel.cs:53`).
- Dialogs per concern; closed-DU results (`NoteNameResult`).

## Code References
- `Notes/Views/NoteTreeView.axaml:11-22` — TreeView binding + existing context menu.
- `Notes/ViewModels/NoteTreeViewModel.cs:136-160` — delete command + file-only gates; `:122-134` LoadTree; `:162-177` parent resolution.
- `Notes/Services/NoteDeleter.cs:7`, `Notes/Services/INoteDeleter.cs:5` — file-only deleter.
- `Notes/Services/IConfirmDialogService.cs:7`, `ConfirmDialogService.cs:9-18` — confirm dialog.
- `Notes/Services/WorkspaceScanner.cs:16-36`, `IWorkspaceScanner.cs:7` — `.md`-file-only scanner (Option B target).
- `Notes/Services/NoteTreeBuilder.cs:11-55` — path-driven tree builder (Option B target).
- `Notes/Services/NoteSearchIndex.cs:88-100,136,182` — single-key delete removal, `.templates/` filter, build path sharing the scanner.
- `Notes/Services/NewNoteNameValidator.cs:13-45`, `INewNoteNameValidator.cs:8-15` — validator + result DU to mirror for folders.
- `Notes/Models/NoteTreeNode.cs:5-15` — node model + kind enum.

## Architecture Insights
- The scanner is the single chokepoint feeding tree + index; Option B should be **additive** (`ScanDirectories`) to keep the index ingesting files only and avoid a wide blast radius.
- Recursive folder delete maps cleanly onto the existing per-file `NoteDeletedMessage` by fanning out one message per contained file — no new message type, and it correctly closes the editor if an open note was inside.

## Related Research
- `context/changes/templates/research.md` — the originating research; `templates` depends on this change for the first-template bootstrap.
- `context/changes/workspace-and-note-list/` (S-01) — the original tree/scanner/delete implementation this change extends.

## Open Questions
1. Scanner contract shape for Option B — additive `ScanDirectories(root)` (recommended, keeps index untouched) vs a richer combined result type.
2. New Folder placement — only into the right-clicked folder/its parent, or also a root-level "New Folder" entry point when nothing is selected?
3. Folder rename — out of scope for this change? (Not requested; flag if desired.)
