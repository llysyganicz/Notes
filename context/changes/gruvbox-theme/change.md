---
change_id: gruvbox-theme
title: Gruvbox theme, app icon, and tree-row context menu
status: implemented
created: 2026-07-30
updated: 2026-08-01
archived_at: null
---

## Notes

**Phase 1 pre-flight (2026-08-01):** NuGet search for an existing gruvbox Avalonia 12 theme / AvaloniaEdit highlighting package found nothing suitable — `gruvbox` and `gruvbox avalonia` queries return zero Avalonia theme hits, and the full avalonia-theme package listing (Material.Avalonia, SukiUI, Citrus, Classic, Semi.Avalonia, OneDark.Avalonia, Romzetron, Fluid, LuminaUI, …) contains no gruvbox variant and no AvaloniaEdit gruvbox definition. Proceeding with the hand-written `ControlTheme` library per plan.

Post-MVP "Theme & Identity" UX slice. Three deliverables:

1. **Gruvbox color palette** — ship light + dark variants; the app keeps following the system setting (no theme switcher UI). Covers all chrome, controls, and the markdown editor's syntax-highlighting colors.
2. **Application icon** — a single gruvbox-consistent asset, used as the system app icon. Not theme-aware (one variant that reads well on both light and dark backgrounds).
3. **Tree view row context menu** — the context menu opens on the whole row, not only on the text element.

PRD is intentionally NOT being extended for this slice; this change folder is the sole execution record.

**Phase 3 note (app icon, taskbar/dock limitation):** on wlroots-based Wayland shells
(confirmed on niri + noctalia) the app runs via XWayland (no `Avalonia.Wayland` package
referenced; `AppBuilder.UsePlatformDetect()` falls back to the X11 backend). `Window.Icon`
does set `WM_CLASS`/`_NET_WM_ICON` correctly, but these shells resolve taskbar/dock icons via
the Wayland foreign-toplevel-management protocol (app-id only, no icon pixel data) plus a
`.desktop` file + XDG icon-theme lookup — which this slice explicitly excludes ("No `.desktop`
/ Linux packaging" in "What We're NOT Doing"). Decision (user, 2026-08-01): keep the scope as
planned — accept the generic fallback icon on such shells for now; `.desktop`/icon-theme
packaging is deferred to a future packaging slice. `<ApplicationIcon>` (Windows exe/taskbar)
and `Window.Icon` (title bar, X11 desktops) are unaffected and per plan scope.

**Phase 3 note (icon, final design):** the icon changed from the original notebook-and-pen
idea through several design-feedback rounds; the final design is an original closed-notebook
cover with a spine strip and a big "N" monogram (`Notes/Assets/app-icon.svg`). No
third-party icon assets are used, so no attribution is required.

**Phase 3 note (manual icon checks 3.3/3.4, deferred to release):** neither taskbar/dock
icon (Linux) nor Explorer/.exe/taskbar/title-bar icon (Windows) could be manually verified
during implementation — Linux release packaging is AppImage (not yet built here) and no
Windows machine with this source was available. Decision (user, 2026-08-01): mark 3.3/3.4
complete and verify against the release AppImage / a Windows machine post-release; ship a
patch if either surfaces a problem.

Deferred to their own future change folders (do NOT bundle here):
- Editor UX improvements — revisit after theming lands; scope TBD then. Theming the editor syntax colors is in scope here, but no editor behavior changes.
- Template shortcut assignment — bind a keyboard shortcut to a template that triggers insert-into-existing-note. Own feature, separate slice.