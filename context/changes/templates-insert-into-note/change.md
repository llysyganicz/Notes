---
change_id: templates-insert-into-note
title: Insert a rendered template body into an existing note at the cursor
status: preparing
created: 2026-07-05
updated: 2026-07-05
roadmap_ref: post-MVP (deferred from S-04)
prd_refs: [FR-009, US-02, US-03 (new)]
blocked_by: []
---

# Insert a rendered template body into an existing note at the cursor

First post-MVP feature, deferred from the S-04 templates slice. The shipped
template flow only produces a **new** note. This change adds a second entry
point: insert a rendered template's **body** into the note that is currently
open in the editor, at the caret, replacing any active selection. The open
note's frontmatter is never modified by the editor-insert path.

Two entry points share a common core (pick template → parse → collect values
→ render), extracted from `NoteTreeViewModel.HandleNewFromTemplate` into a
shared service so both ViewModels inject it rather than duplicate the
orchestration (AGENTS.md: "Share behavior across ViewModels via
DI-injected services, not base-class hierarchies"):

- **Menu "New from Template"** (existing) — creates a new note with the
  rendered **full** text (frontmatter + body). Behavior unchanged.
- **Editor "Insert from Template"** (new) — renders the template, extracts
  the body only, and inserts it at the caret, replacing any selection. The
  open note's YAML frontmatter is left untouched.

See `frame.md` for the framing that produced this scope.

## Scope decisions (2026-07-05, post-frame)

- **Editor-insert is body-only; the open note's frontmatter is never
  touched.** Templates' non-`form` frontmatter (tags/title defaults) is
  ignored on the editor-insert path. (User Q1=A.)
- **Menu path keeps today's behavior** — the new note retains the
  template's non-`form` frontmatter verbatim plus the substituted body.
  Q1=A applies to the editor-insert path only.
- **Common orchestration extracted into a shared service** (provisionally
  `ITemplateInstantiationService` in `Notes.Core`), injected by both
  `NoteTreeViewModel` (menu path) and `NoteEditorViewModel` (editor path).
- **Editor entry enabled only while a note is open and in editing mode**
  (`EditorPaneState.Editing`), not in preview/empty.
- **Active selection is replaced** by the rendered body; with no
  selection the body is inserted at the caret. (User Q3=A.)

## Source

- Frame brief: `context/changes/templates-insert-into-note/frame.md`
- Deferred-from decision: `context/archive/2026-06-02-templates/change.md`
  ("Apply-template-to-existing-note: OUT of MVP scope")
- PRD refs: FR-009 (extend), US-02, US-03 (new), §Business Logic
- Roadmap: post-MVP (no slice in `roadmap.md` §S-01..S-04)

## Artifacts in this folder

- `frame.md` — framing brief (this change's pre-plan step)