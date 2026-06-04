# Templates — Note-from-Template with a Typed Form — Plan Brief

> Full plan: `context/changes/templates/plan.md`
> Research: `context/changes/templates/research.md`

## What & Why

S-04, the roadmap north star: let a user pick a template, fill a form generated from its typed field definitions, and produce a new note with `{{placeholders}}` substituted. Templates are plain `.md` files in `.templates/` whose YAML frontmatter declares a `form:` schema. This turns repetitive note structures (meeting notes, dailies) into a one-click, typed-input flow.

## Starting Point

The prerequisite `note-tree-folder-management` has landed (`impl_reviewed`), so the tree is directory-aware and `.templates/` is reliably created (New Folder) and visible. Template *creation* is therefore free. The New Note flow (`NoteTreeViewModel.HandleNewNote`) already prompts → validates → saves → indexes → selects, with `string.Empty` as the content seam. The scanner already returns `.templates/*.md`. There is no template engine, no dynamic form, and no YAML serializer anywhere yet.

## Desired End State

A user with at least one template chooses **File → New from Template…** (or Ctrl+T), picks from a flat list, fills a typed form (text/date/number/dropdown), and a new note appears in the selected folder — opened in the editor, `form` block stripped from frontmatter, declared body placeholders replaced. With no templates, the menu item is disabled and Ctrl+T is a no-op.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Form schema | `form:` map keyed by field name; each `{type, label, entries?}` | User-supplied, locked structure; field name = placeholder key | Research |
| Substitution | Body-only `{{field}}`; undeclared tokens verbatim; blank → empty | Matches PRD "no leftover placeholder syntax" for declared fields only | Research |
| `form` block strip | Textual removal (no YAML re-serialize); drop fence if `form` was the only key | Preserve other frontmatter verbatim — users own their frontmatter | Research |
| Apply to existing note | Out of scope | Avoids AvaloniaEdit caret/insert risk; deferred post-MVP | Research |
| `.templates/` visibility | Stays visible; no scanner/tree/index exclusion | That's how templates are created/edited | Research |
| Generated-note destination | Reuse New Note flow — selected folder + name prompt | Reuses the validate/save/select tail verbatim | Plan |
| Value formatting | Date ISO default + optional per-field `format`; numbers invariant culture + same optional `format` | Stable/sortable dates, deterministic/test-friendly numbers; format override for precision | Plan |
| Number field control | One Avalonia `NumericUpDown` (no separate integer control); integer vs decimal = config from `format` (`ParsingNumberStyle`/`FormatString`) | Avalonia consolidated to a single numeric control; a new field type is unnecessary | Plan |
| Entry point | File menu + Ctrl+T → `NewFromTemplateRequestedMessage` | Mirrors the New Note/New Folder menu+message+keybinding pattern | Plan |
| No-templates state | Menu item disabled via `CanExecute = HasTemplates`; Ctrl+T no-op | Clear affordance without an error dialog | Plan |
| Empty field values | Dropdown/date start empty; untouched → empty string | Honors the locked "no default" rule uniformly | Plan |

## Scope

**In scope:** template picker; dynamic typed form (text/date/number/dropdown); schema parser; body-only substitution + `form`-block strip; reuse of the New Note save tail; menu/keybinding entry point with enablement.

**Out of scope:** apply-to-existing-note; YAML serializer / frontmatter re-emission; `.templates/` exclusion; field `default`/`required` flags; nested template subfolders; filename auto-suggestion; bootstrap code.

## Architecture / Approach

Three bottom-up layers. **(1) Pure engine** — `TemplateParser` (Markdig + YamlDotNet, broad-catch → empty per `lessons.md`) → `FormDefinition`; `TemplateRenderer` does body-only substitution over a `name→string` map + textual `form` strip. **(2) Dynamic form** — per-field-type VMs (`Text/Date/Number/Select`) own typed→string formatting and feed an `ItemsControl` with one implicit `DataTemplate` per type; `TemplateFormDialog` + service return the value map. **(3) Catalog + picker + wiring** — `TemplateCatalog` lists `.templates/*.md` (also drives `HasTemplates`); `TemplatePickerDialog` + service; `MainWindowViewModel.NewFromTemplateCommand` (menu/Ctrl+T, `CanExecute`); `NoteTreeViewModel` orchestrates picker → form → render → New Note tail.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Engine | Parser + renderer (pure, fully unit-tested) | `form`-block textual strip edge cases (frontmatter-drop, indentation) |
| 2. Form dialog | Per-type field VMs + dynamic `ItemsControl` form | Dynamic-form rendering — the one net-new Avalonia idiom |
| 3. Catalog/picker/wiring | Listing, picker, menu+Ctrl+T, end-to-end orchestration | `HasTemplates` `CanExecute` staying fresh across workspace/save/delete |

**Prerequisites:** `note-tree-folder-management` (landed). At least one `.templates/*.md` template to exercise the flow.
**Estimated effort:** ~3 sessions, one per phase.

## Open Risks & Assumptions

- Field render order must follow template document order — parser must preserve YAML map key order (not assume `Dictionary<,>` ordering).
- `HasTemplates` enablement requires `MainWindowViewModel` to react to workspace/save/delete messages; assumed acceptable to add message handling there.
- Numbers render with invariant culture (deterministic, testable); a per-field `format` controls precision and, when it has no decimals, drives integer-only entry via `ParsingNumberStyle`.

## Success Criteria (Summary)

- A template with mixed field types generates a correct note: `form` stripped, declared placeholders substituted, undeclared tokens and other frontmatter preserved, blank fields empty.
- Entry point present and correctly enabled/disabled based on template availability.
- Cancelling at any step (picker, form, name prompt) creates no note.
