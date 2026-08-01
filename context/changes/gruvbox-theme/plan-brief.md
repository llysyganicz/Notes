# Gruvbox Theme, App Icon, and Tree-Row Context Menu — Plan Brief

> Full plan: `context/changes/gruvbox-theme/plan.md`

## What & Why

Ship the post-MVP "Theme & Identity" UX slice: give Notes a hand-written gruvbox look (original palette, light + dark, system-following), a gruvbox notebook-and-pen app icon, and fix the tree-row context menu so it opens on the whole row. No theme switcher UI — the app keeps following the OS theme. PRD is not extended; this change folder is the sole execution record.

## Starting Point

Today `App.axaml` loads bare `FluentTheme` + the AvaloniaEdit Fluent theme with zero overrides; there is no `Assets/` folder, no `<ApplicationIcon>`, no `Window.Icon`. The tree view's `ContextMenu` is attached to the inner `TextBlock` inside `NoteTreeView.axaml`'s `TreeDataTemplate`, so right-clicking empty row space does nothing. Full E2E scaffolding exists (`Notes.E2ETests`) and drives real Avalonia controls headless, so the context-menu fix is automatable; theming and icon are inherently visual (manual verification).

## Desired End State

Running `dotnet run --project Notes` launches the app rendered entirely in original-palette gruvbox; light and dark follow the OS automatically and swap cohesive across chrome, controls, editor syntax, and the markdown preview. The OS shows the gruvbox notebook-and-pen icon for the window and the published binary on Linux and Windows. Right-clicking anywhere on a tree row opens the context menu (with an automated test proving it).

## Key Decisions Made

| Decision                                         | Choice                                                                        | Why (1 sentence)                                                                                              | Source |
| ------------------------------------------------ | ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------- | ------ |
| Gruvbox palette flavor                           | Original (medium contrast) — `#282828`/`#ebdbb2` dark, `#fbf1c7`/`#3c3836` light | Recognizable canonical gruvbox, most-ported spec                                                              | Plan   |
| Theme delivery technique                         | Replace `<FluentTheme />` entirely — hand-written gruvbox `ControlTheme`s     | User explicitly chose full replacement over per-resource overrides                                           | Plan   |
| Light/dark selection                             | `RequestedThemeVariant="Default"` — system-following, no switcher UI          | Matches existing setup and change.md ("app keeps following the system setting")                             | Change |
| App icon motif                                   | Notebook + pen nib (gruvbox yellow/orange on dark panel)                      | Universally reads "notes/writing"; pen ties to the markdown-writing core                                     | Plan   |
| Icon asset tooling                               | SVG source → PNG {16,32,48,128,256} + multi-size `.ico`; `avares`-embedded     | Single editable source of truth; cross-platform raster for runtime `Window.Icon` + Windows binary icon      | Plan   |
| Context-menu fix technique                       | `ContextMenu` on a stretched container wrapping the `TextBlock`              | Whole-row hit area; keeps proven `$parent[TreeView]` command routing; no ViewModel change                    | Plan   |
| Automated-test boundary                          | Automate context-menu right-click; manual verification for palette + icon     | Deterministic behavior tested; color/icon legibility are inherently visual (no brittle color assertions)     | Plan   |
| Theme variant resolution                         | `ThemeDictionaries` + `DynamicResource` brushes                             | OS-theme toggle re-resolves without restart; `StaticResource` would freeze the loaded variant                | Plan   |

## Scope

**In scope:**
- Gruvbox palette (original colors) in light + dark `ThemeDictionaries`
- Hand-written gruvbox `ControlTheme`s for every control the app uses, replacing `FluentTheme`
- Gruvbox AvaloniaEdit markdown syntax highlighting + `TextEditor` chrome
- Gruvbox Markdown.Avalonia preview styles
- App icon (SVG source + raster set) wired into `Notes.csproj` `<ApplicationIcon>` and `MainWindow` `Icon`
- Tree-row context-menu hit-area fix + automated E2E test

**Out of scope:**
- Theme switcher UI (manual override deferred)
- Any editor behavior change (autosave, editing UX, rendering) — only syntax colors
- `Notes.Core` change (theme stays Avalonia-layer only)
- `.desktop` / Linux packaging, installers, per-OS icon bundling beyond csproj/Window
- Replacing Markdown.Avalonia (preview control stays; only its styling changes)

## Architecture / Approach

A new `Notes/Themes/` folder holds gruvbox styling authored as AXAML: a palette resource dictionary (`ThemeDictionaries` per `ThemeVariant`) plus `ControlTheme`s for each control, all loaded via `App.axaml` after removing `<FluentTheme />` and the AvaloniaEdit Fluent include. Brush setters use `DynamicResource` so the OS theme auto-selects. The AvaloniaEdit markdown highlighting is replaced with a custom `IHighlightingDefinition` (embedded `.xshd` + a loader service) assigned at `NoteEditorView.axaml.cs:18`. The preview is re-styled via `Style` selectors on `MarkdownScrollViewer` descendants. The icon is an editable `Assets/app-icon.svg` plus a raster set embedded as `AvaloniaResource`, referenced by `<ApplicationIcon>` (`.ico`) and `MainWindow.Icon` (PNG `Bitmap`). The tree-row fix restructures `NoteTreeView.axaml`'s `TreeDataTemplate` (a stretched container owns the `ContextMenu`) and adds a headless right-click E2E test.

## Phases at a Glance

| Phase                                                | What it delivers                                                            | Key risk                                                                                       |
| ---------------------------------------------------- | --------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| 1. Gruvbox palette + control-theme library            | Palette `ThemeDictionaries` + gruvbox `ControlTheme`s; `FluentTheme` removed | Missing a control leaves Fluent-default leaks; coverage is ~14 controls + scrollbar + chrome  |
| 2. Editor & preview theming                          | Gruvbox markdown highlighting + `TextEditor` chrome + preview styles        | Dark/light variant flip of syntax colors needs verification (static def vs reload)            |
| 3. App icon                                          | SVG source + raster set; `<ApplicationIcon>` + `Window.Icon` wired           | Avalonia `Window.Icon` doesn't render SVG — must rasterize; cross-platform taskbar legibility  |
| 4. Tree-row context menu fix                         | Stretched container owns `ContextMenu`; E2E right-click test                | Headless `PointerPressed` right-button must target the row container, not the inner text      |

**Prerequisites:** Avalonia 12 + AvaloniaEdit 12.0.0 + Markdown.Avalonia 12.0.0-a3 (already referenced). An SVG-to-PNG/ICO raster tool available locally for one-time icon generation.
**Estimated effort:** ~4-6 sessions across 4 phases; Phase 1 is the bulk (control-theme authoring + per-control Fluent-leak hunting on two platforms × two variants).

## Open Risks & Assumptions

- **AvaloniaEdit gruvbox highlighting vs running-app theme flip**: a static `.xshd` may bake one variant's colors; confirm whether the active `ThemeVariant` switch re-resolves the highlighting or requires re-loading the definition on `ActualThemeVariant` change.
- **All-controls coverage**: the enumerated control surface must be complete or Fluent-colored leaks will appear during manual cross-platform verification — Phase 1 success hinges on the per-control checklist (chrome, buttons, inputs, lists, menus, scrollbar, splitter, dialogs).
- **Cross-platform icon legibility**: a single asset for both backgrounds is assumed; if it doesn't read on one, a two-variant fallback may be needed (currently out of scope per change.md).

## Success Criteria (Summary)

- App launches fully gruvbox (original palette) on Linux and Windows; light + dark follow the OS and swap cohesive across chrome, controls, editor syntax, and the preview.
- The gruvbox notebook-and-pen icon shows in the window title bar, taskbar/dock, and (Windows) Explorer `.exe`, legible on light and dark backgrounds.
- Right-clicking anywhere on a tree row opens the context menu, proven by an automated E2E test.