---
change_id: note-tree-folder-management
title: Note-tree folder management — create folder, delete folder, directory-aware tree
status: implementing
created: 2026-06-02
updated: 2026-06-03
roadmap_ref: S-01 (follow-up)
prd_refs: [FR-007, FR-003]
blocks: [templates]
---

# Note-tree folder management — create folder, delete folder, directory-aware tree

General note-tree management capabilities that extend S-01 (workspace-and-note-list). Surfaced while preparing the `templates` slice: creating the first template requires a way to create the `.templates/` folder. Rather than a templates-special command, the user opted for first-class tree management.

This change is a **prerequisite for `templates`** (it unblocks the first-template bootstrap) but is independently useful for everyday note organization.

## Scope (decided 2026-06-02)

- **New Folder** — a right-click context-menu command that creates a directory in the workspace.
- **Delete Folder** — enable deleting a directory node (currently delete is gated to files only), with recursive-delete confirmation.
- **Directory-aware scanner/tree (Option B)** — the scanner and tree builder must enumerate directories, not just `.md` files, so that **empty folders persist and appear in the tree**. This changes the `IWorkspaceScanner` contract and the `NoteTreeBuilder`, and touches the search-index build path that also calls the scanner.

## Source

- Originated from `/10x-research templates` — see `context/changes/templates/research.md` (Follow-up 2026-06-02 (b)) for the originating analysis.
- PRD refs: FR-007 (folder-based grouping / browse), FR-003 (delete with confirmation — extended to folders).

## Artifacts in this folder

- `research.md` — internal codebase research for the three capabilities.
