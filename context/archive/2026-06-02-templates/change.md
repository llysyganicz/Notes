---
change_id: templates
title: Template creation and note-from-template with a typed form
status: archived
created: 2026-06-02
updated: 2026-06-06
roadmap_ref: S-04
archived_at: 2026-06-06T00:00:00Z
prd_refs: [FR-008, FR-009, US-02]
blocked_by: [note-tree-folder-management]
---

# Template creation and note-from-template with a typed form

Fourth vertical slice from the roadmap — the **north star**. Builds on the editor + autosave from S-02 and the YAML frontmatter parsing from S-03. Templates live as plain `.md` files in a dedicated `.templates/` subfolder of the workspace; their YAML frontmatter declares typed field definitions (text, date, number, dropdown/select) and their body carries `{{placeholder}}` tokens. The user picks a template, fills a form generated from the field definitions, and the app writes a new `.md` note with frontmatter populated and all placeholders replaced.

## Scope decisions (2026-06-02, post-research)

- **Apply-template-to-existing-note: OUT of MVP scope.** Deferred to the first post-MVP feature. Removes the AvaloniaEdit caret/insert plumbing risk.
- **`.templates/` stays VISIBLE in the note tree.** A template is created by making a regular `.md` file in `.templates/` via the normal flow — hiding the folder would make template creation harder. No scanner/tree exclusion work.
- **No app-generated YAML frontmatter.** Users type frontmatter into their templates themselves; note-from-template is pure placeholder substitution over the template text. No YAML *serializer* needed.
- **Form fields come from a user-defined structure** (now supplied — see below) — not auto-derived by the app from arbitrary frontmatter.

## Template form schema (locked 2026-06-02)

- Field definitions live in the template's YAML frontmatter under a single top-level `form` key (one `form` per template).
- **`form` is itself a map keyed by field name** (no `fields` nesting); the field name is the placeholder key.
- Each field has `type` and `label`; `dropdown` additionally requires `entries` (plain strings).
- Placeholders use `{{field_name}}`. Unknown placeholders (not a declared field) are left as-is. Missing value → empty string.
- On generate: **strip the `form` block** from the output note's frontmatter (keep any other frontmatter keys; omit the block entirely if `form` was the only key), and substitute `{{...}}` **in the body only**.

```yaml
---
form:
  project_name:
    type: text
    label: Project name
  priority:
    type: dropdown
    label: Priority
    entries: [low, medium, high]
---
# {{project_name}}

Priority: {{priority}}
```

## Source

- Roadmap entry: `context/foundation/roadmap.md` §S-04 (north star)
- PRD refs: FR-008 (create template), FR-009 (note from template), US-02
- Business Logic: `context/foundation/prd.md` §Business Logic

## Artifacts in this folder

- `research.md` — internal codebase research (this slice's reusable surface + gaps)
