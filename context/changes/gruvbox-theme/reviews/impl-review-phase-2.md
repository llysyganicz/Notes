<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Gruvbox Theme, App Icon, and Tree-Row Context Menu

- **Plan**: context/changes/gruvbox-theme/plan.md
- **Scope**: Phase 2 of 4
- **Date**: 2026-08-01
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Verification evidence

- **2.1 `dotnet build`** — PASS (re-run). 0 errors, 0 warnings across all 5 projects.
- **2.2 `dotnet test`** — PASS (re-run). 301 tests green: Notes.Core.Tests 229, Notes.Tests 64, Notes.E2ETests 8. No regressions; same totals as the Phase 1 review (no new tests added in Phase 2, matching the plan — Phase 2 didn't call for new automated tests, only that existing editor/preview-adjacent flows keep passing).
- **Manual 2.3–2.7** — correctly left `[ ]` in Progress; no rubber-stamping. Not evaluated here (GUI-only, pending human confirmation per the plan's own gate).

### Plan-vs-diff cross-check (`ed7f53a`)

| Plan contract | File | Verdict |
|---|---|---|
| #1 Gruvbox AvaloniaEdit control theme (chrome) | `Notes/Themes/Controls/Editor.axaml` | MATCH — landed in Phase 1 per the documented addendum; Phase 2 correctly left it untouched since chrome (background/foreground/selection/caret/line-number color) was already gruvbox. |
| #2 Gruvbox markdown highlighting definition | `Notes/Themes/GruvboxMarkdownHighlighting.xshd`, `Notes/Services/GruvboxHighlightingLoader.cs` | MATCH — `.xshd` ported 1:1 from AvaloniaEdit's built-in `MarkDown-Mode.xshd` with 6 `<Color>` values templated as `__TOKEN__` placeholders; loader substitutes gruvbox hex per `ThemeVariant` and parses via `HighlightingLoader.Load`. Verified the 7 dark/light hex pairs in `GruvboxHighlightingLoader.cs` exactly match `GruvboxPalette.axaml`'s `GruvboxBrightOrange/Yellow/Aqua/Blue/Green`, `GruvboxGray`, and `GruvboxBg1` per variant. |
| #3 Wire the custom definition into the editor | `Notes/Views/NoteEditorView.axaml.cs` | MATCH — `HighlightingManager.Instance.GetDefinition("MarkDown")` replaced with `ApplyGruvboxSyntaxHighlighting()`; `Application.ActualThemeVariantChanged` is hooked (subscribe on attach, unsubscribe on detach — no leak) to reload and reassign `Editor.SyntaxHighlighting` on OS-theme toggle, exactly per the plan's stated requirement. |
| #4 Gruvbox markdown preview styles | `Notes/Themes/GruvboxMarkdownPreview.axaml`, wired via `App.axaml` + `NoteEditorView.axaml.cs` | MATCH — `Styles` block scoped to `.Markdown_Avalonia_MarkdownViewer`, covering headings 1–6, code blocks, inline code, lists, tables, blockquotes, links, and hr; all colors are `DynamicResource`. Assigned via code-behind (`Preview.MarkdownStyle = style`) with an in-file comment correctly explaining why XAML attribute assignment isn't possible (private backing field → cross-assembly `FieldAccessException`). |

No files changed outside the four planned contracts (plus the expected `App.axaml` `StyleInclude` wiring, `Notes.csproj` `.xshd` resource include, and the plan.md Progress checkbox update).

## Findings

### F1 — Stale Phase-1 comment misdescribes where Phase 2 syntax coloring landed

- **Severity**: 🟡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: Notes/Themes/Controls/Editor.axaml:9-11
- **Detail**: The Phase-1-authored header comment reads: "Markdown syntax token colors and the line-number margin foreground are refined in Phase 2 (Notes/Themes/Controls/Editor.axaml continues to be the home for that)." Phase 2 instead implemented syntax token coloring via a separate `.xshd` resource + `GruvboxHighlightingLoader` service (a reasonable design choice, since `.xshd` `<Color>` values are load-time literals with no `DynamicResource` equivalent — documented in the new files' own comments). `Editor.axaml` was not touched by the Phase 2 commit (`ed7f53a`); `LineNumbersForeground` is unchanged from Phase 1. The forward-reference comment is now inaccurate and could send a future maintainer looking in the wrong file for syntax-color logic.
- **Fix**: Update the `Editor.axaml` comment to drop the stale forward reference — e.g. "Markdown syntax token colors are provided separately via `Notes/Themes/GruvboxMarkdownHighlighting.xshd` + `Notes/Services/GruvboxHighlightingLoader.cs` (see Phase 2), since `.xshd` colors are load-time literals and can't use `DynamicResource` like this file's setters."
- **Decision**: FIXED — comment updated in `Notes/Themes/Controls/Editor.axaml` to point at `GruvboxMarkdownHighlighting.xshd` + `GruvboxHighlightingLoader.cs`; `dotnet build` re-verified green.

## Notes

- No security, performance, reliability, or data-safety issues found in the changed files.
- `Notes.Core` untouched — architecture boundary respected.
- No editor behavior changes — confirmed: the only `NoteEditorView.axaml` structural change is wrapping the existing (now-named) `MarkdownScrollViewer` in a `Border` for background theming and adding `x:Name="Preview"` so code-behind can assign `MarkdownStyle`; the `TogglePreviewRequestedMessage` / `EditorPaneState` logic in `NoteEditorViewModel` is unchanged and untested-here because it's unchanged (already covered by `Notes.Tests/NoteEditorViewModelTests.cs`).
- Hardcoded hex duplication between `GruvboxPalette.axaml` and `GruvboxHighlightingLoader.cs`'s token tuples is a deliberate, documented tradeoff (`.xshd` has no `DynamicResource` equivalent) — verified the two are in sync today; not flagged as a finding, but worth remembering as a manual-sync point if the palette's bright-accent hexes ever change.
