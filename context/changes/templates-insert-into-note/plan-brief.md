# Insert a rendered template body into an existing note at the cursor — Plan Brief

> Full plan: `context/changes/templates-insert-into-note/plan.md`
> Frame brief: `context/changes/templates-insert-into-note/frame.md`
> Research: `context/changes/templates-insert-into-note/research.md`

## What & Why

Add an editor-side "Insert from Template" command that renders a template's
substituted body and drops it at the current caret, replacing any active
selection. The existing "New from Template" menu path stays unchanged. This is
the post-MVP follow-up deferred from the S-04 templates slice.

## Starting Point

Templates today can only spawn a brand-new note via the menu. The orchestration
(pick → parse → collect values → render) lives entirely inside
`NoteTreeViewModel.HandleNewFromTemplate`, and `ITemplateRenderer.Render` always
returns frontmatter + body. The editor has no caret/selection channel across the
MVVM boundary.

## Desired End State

The user can press `Ctrl+Shift+T` (or use the File menu) while editing a note to
pick a template, fill its form, and have the rendered body inserted at the caret.
The open note's frontmatter is never modified. The menu path still creates a new
note with full frontmatter + body.

## Key Decisions Made

| Decision                          | Choice                                    | Why (1 sentence)                                                                 | Source     |
| --------------------------------- | ----------------------------------------- | -------------------------------------------------------------------------------- | ---------- |
| Trigger location                  | MainWindow File menu + `Ctrl+Shift+T`     | Reuses existing menu structure and Locator binding; discoverable and consistent. | Plan       |
| VM↔view seam                      | VM event + view code-behind `Document.Replace` | Keeps caret/selection view-only and extends the existing editor channel.        | Research   |
| Custom AvaloniaEdit wrapper       | No wrapper                                | Overkill for a single insert operation and no prior custom-control convention.   | Plan       |
| Body-only vs full-render insert   | Body-only for editor; full for new note   | Inserting frontmatter mid-document yields invalid markdown.                      | Frame      |
| Shared orchestration location     | `TemplateService` in `Notes`              | Needs UI-dialog abstractions that live in the Notes layer.                       | Plan       |
| Automated tests for view replace  | None                                      | Treats the AvaloniaEdit replace as UI wiring; headless VM logic stays testable.  | Plan       |

## Scope

**In scope:**
- `ITemplateRenderer.RenderBody` in Notes.Core.
- `TemplateService` extraction in Notes.
- `InsertFromTemplateCommand` on `NoteEditorViewModel`.
- View-side caret/selection replace in `NoteEditorView.axaml.cs`.
- File menu item and `Ctrl+Shift+T` shortcut in `MainWindow.axaml`.
- Updating existing tests for constructor changes.

**Out of scope:**
- Custom editor control.
- Frontmatter merging into existing notes.
- Automated tests for the AvaloniaEdit `Document.Replace` itself.
- Changing the "New from Template" UX order or behavior.

## Architecture / Approach

`TemplateService` becomes the single orchestrator used by both ViewModels. It
calls the existing `TemplateCatalog`, picker dialog, file service, parser, form
dialog, and renderer. The menu path uses `ITemplateRenderer.Render` (full note);
the editor path uses `ITemplateRenderer.RenderBody` (body only). The editor VM
fires an `InsertAtCaretRequested` event; the view code-behind resolves the caret
offset and selection length at view-time and calls `Document.Replace`.

## Phases at a Glance

| Phase | What it delivers                                    | Key risk                              |
| ----- | --------------------------------------------------- | ------------------------------------- |
| 1. Core body-only render | `ITemplateRenderer.RenderBody` + tests     | None — pure string manipulation.      |
| 2. Shared service        | `TemplateService` + menu path rerouted     | Existing template tests need update.  |
| 3. Editor command        | Insert command, view seam, menu shortcut   | AvaloniaEdit binding/shortcut syntax. |
| 4. Regression check      | Confirm menu path unchanged                | Manual only.                          |

**Prerequisites:** None beyond the existing codebase.
**Estimated effort:** One focused implementation session across 4 small phases.

## Open Risks & Assumptions

- `Ctrl+Shift+T` does not conflict with future shortcuts.
- The `Locator.Editor.InsertFromTemplateCommand` binding syntax in Avalonia
  `KeyBinding`/`MenuItem` works as expected; minor XAML adjustment may be needed.
- `Document.Replace` behavior with `SelectionLength` matches the
  "replace selection if any" semantics on all target platforms.

## Success Criteria (Summary)

- A template body can be inserted into an open note at the caret, replacing any
  selection, without touching the note's frontmatter.
- The command is disabled unless a note is open in editing mode.
- "New from Template" continues to create a full note with frontmatter preserved.
