---
change_id: gruvbox-theme
title: Gruvbox theme, app icon, and tree-row context menu
status: planned
created: 2026-07-30
updated: 2026-07-30
archived_at: null
---

## Notes

Post-MVP "Theme & Identity" UX slice. Three deliverables:

1. **Gruvbox color palette** — ship light + dark variants; the app keeps following the system setting (no theme switcher UI). Covers all chrome, controls, and the markdown editor's syntax-highlighting colors.
2. **Application icon** — a single gruvbox-consistent asset, used as the system app icon. Not theme-aware (one variant that reads well on both light and dark backgrounds).
3. **Tree view row context menu** — the context menu opens on the whole row, not only on the text element.

PRD is intentionally NOT being extended for this slice; this change folder is the sole execution record.

Deferred to their own future change folders (do NOT bundle here):
- Editor UX improvements — revisit after theming lands; scope TBD then. Theming the editor syntax colors is in scope here, but no editor behavior changes.
- Template shortcut assignment — bind a keyboard shortcut to a template that triggers insert-into-existing-note. Own feature, separate slice.