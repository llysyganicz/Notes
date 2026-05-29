---
change_id: note-editor-and-preview
title: Create, edit (syntax highlighting), and preview markdown notes
status: impl_reviewed
created: 2026-05-28
updated: 2026-05-29
roadmap_ref: S-02
prd_refs: [FR-001, FR-002, FR-004, US-01]
---

# Create, edit (syntax highlighting), and preview markdown notes

Second vertical slice from the roadmap. Builds on the workspace + note tree from S-01: clicking a note opens it in an AvaloniaEdit pane with markdown syntax highlighting, edits auto-save debounced to disk, a New Note dialog creates files in the currently selected folder, and a View toggle flips between editor and Markdown.Avalonia-rendered preview.

## Source

- Roadmap entry: `context/foundation/roadmap.md` §S-02
- PRD refs: FR-001 (create), FR-002 (edit with syntax highlighting), FR-004 (preview as HTML), US-01

## Artifacts in this folder

- `change.md` — this file (identity)
- `plan.md` — implementation contract
- `plan-brief.md` — two-pager hand-off
