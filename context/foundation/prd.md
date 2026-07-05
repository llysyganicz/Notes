---
project: "Notes"
version: 2
status: draft
created: 2026-05-18
updated: 2026-07-05
context_type: greenfield
product_type: desktop
target_scale:
  users: small
  qps: n/a
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
---

## Vision & Problem Statement

Existing markdown note-taking apps force a choice between abandoned simplicity and maintained complexity. Notable offered a clean, plain-text editor with markdown syntax highlighting and notes stored as plain files — but development stopped, and the app is no longer maintained. Obsidian is actively developed but has grown into a feature-heavy ecosystem (plugins, graph views, sync services) that adds friction for users who just want to write and organize markdown notes.

The insight: no single tool combines a plain-text editor with syntax highlighting (Notable's core strength) with a template system driven by YAML frontmatter fields and body placeholders (achievable in Obsidian only through plugins). The product merges these two proven concepts into one lightweight, maintained desktop application — plain markdown files, structured templates, nothing more.

## User & Persona

### Primary persona
**Developer / technical user** who writes markdown notes daily. Uses markdown as a first-class format for personal notes, meeting logs, project documentation, and reference material. Values plain files on disk (portable, VCS-friendly), keyboard-driven workflows, and minimal UI chrome. Currently stuck choosing between Notable (right UX, dead project) and Obsidian (alive, too much).

## Success Criteria

### Primary
- The full MVP flow works end-to-end: user can create a note, write markdown with syntax highlighting, save it as a plain `.md` file, preview as HTML, search/filter by name and tags, edit existing notes, and create a note from a template (YAML frontmatter fields → form → placeholders replaced).

### Secondary
- Notes are instantly portable — the folder of `.md` files works with any other markdown tool without conversion or export.

### Guardrails
- No data loss — a crash or unexpected quit must never corrupt or lose a note file.
- No lock-in — notes must remain plain `.md` files readable by any text editor.
- Performance floor — app must launch and display the notes list within a few seconds.

## User Stories

### US-01: User creates and organizes a markdown note

- **Given** a user with a selected notes folder
- **When** they create a new note, write markdown with tags in frontmatter, and save
- **Then** the note is persisted as a `.md` file in the notes folder, appears in the list view, and is findable by name and tags

#### Acceptance Criteria
- The saved file is a valid `.md` file readable by any text editor
- Tags in YAML frontmatter are parsed and shown in the list/filter UI
- The note appears in search results by both name and tag immediately after save

### US-02: User creates a note from a template

- **Given** a user with at least one template defined (YAML frontmatter with field definitions + body placeholders)
- **When** they choose "New from Template", select a template, and fill in the form fields
- **Then** a new note is created with the placeholders replaced by the filled values, saved as a `.md` file

#### Acceptance Criteria
- The form displays all fields defined in the template's YAML frontmatter
- Placeholders in the template body are replaced with the user's input
- The resulting note is a valid `.md` file with no leftover placeholder syntax

### US-03: User inserts a template into an existing note

- **Given** a user with a note open in the editor and at least one template defined
- **When** they choose to insert from a template, select a template, and fill in the form fields
- **Then** the rendered template body is inserted into the open note at the caret position, replacing any active selection, and the note's YAML frontmatter is left unchanged

#### Acceptance Criteria
- The command is available only while a note is open and in editing mode (not in preview or empty state)
- Only the template's **body** is inserted; the template's non-`form` frontmatter is never merged into the existing note
- The note's existing YAML frontmatter is not modified by the insert operation
- Placeholders in the inserted body are replaced with the user's input; no leftover `{{placeholder}}` syntax for declared fields
- Any active text selection in the editor is replaced by the inserted body; with no selection, the body is inserted at the caret
- The resulting note remains a valid `.md` file

## Functional Requirements

### Note management
- FR-001: User can create a new blank markdown note. Priority: must-have
  > Socrates: No counter-argument; stands as written.
- FR-002: User can edit an existing note in a plain-text editor with markdown syntax highlighting. Priority: must-have
  > Socrates: No counter-argument; stands as written.
- FR-003: User can delete a note (with confirmation dialog). Priority: must-have
  > Socrates: Counter-argument considered: "permanent delete is dangerous for a notes app." Resolution: kept with confirmation dialog added — user must confirm before a note file is removed.
- FR-004: User can preview a note rendered as HTML. Priority: must-have
  > Socrates: No counter-argument; stands as written.

### Organization
- FR-005: User can tag notes via YAML frontmatter. Priority: must-have
  > Socrates: No counter-argument; stands as written.
- FR-006: User can search/filter notes by name, tags, and note content (full-text). Priority: must-have
  > Socrates: Counter-argument considered: "full-text search across content would be more useful than name+tag alone." Resolution: FR expanded to include content search — name+tag only is too limiting.
- FR-007: User can browse all notes in a list view with folder-based grouping (subdirectories in the notes folder). Priority: must-have
  > Socrates: Counter-argument considered: "a flat list doesn't scale — folder/directory structure would be needed early." Resolution: FR expanded to include folder-based grouping via subdirectories.

### Templates
- FR-008: User can create a template with field definitions in YAML frontmatter and placeholders in the body. Templates live in a dedicated `.templates/` subfolder inside the notes folder; any `.md` file in that subfolder is treated as a template. Priority: must-have
  > Socrates: Counter-argument considered: "template creation is a power-user feature — ship with built-ins and defer." Resolution: kept — templates are the differentiator; without creation, the app is just another markdown editor.
- FR-009: User can create a new note from a template, filling a form generated from the template's field definitions. The new note retains the template's non-`form` frontmatter verbatim and all declared body placeholders are replaced. Supported field types: text, date, number, and dropdown/select. Priority: must-have
  > Socrates: No counter-argument; stands as written.
- FR-011: User can insert a rendered template body into a note that is already open in the editor, at the caret, replacing any active selection. The open note's YAML frontmatter is never modified by this operation. The command is available only while a note is open and in editing mode. Priority: must-have
  > Socrates: Counter-argument considered: "insert the whole template including its frontmatter, merging tags/title defaults." Resolution: rejected — inserting a full `---` frontmatter block mid-document yields invalid markdown, and silently rewriting a note's frontmatter breaks the no-lock-in / no-surprises promise. Frontmatter templating stays on the new-note path (FR-009); the existing-note path is body-only.

### Workspace
- FR-010: User can select a working directory (notes folder) on first launch, and switch it later via a menu option. Priority: must-have
  > Socrates: Counter-argument considered: "could hardcode a default location and skip the picker." Resolution: kept — users have existing notes in specific folders; directory selection is essential.

## Non-Functional Requirements

- Editor typing, note switching, and list navigation must feel instant — no perceptible input lag or UI stutter during normal use.

## Business Logic

The app transforms a template — defined as YAML frontmatter field definitions plus body placeholders — and user-supplied input into a structured markdown note.

The template file is a standard `.md` file stored in a dedicated `.templates/` subfolder inside the notes folder. Its YAML frontmatter declares field names with types (text, date, number, dropdown/select) and optional defaults. Its body contains placeholders (e.g. `{{field_name}}`). When the user creates a note from a template, the app parses the frontmatter to generate a typed form, collects the user's responses, and writes a new `.md` file with frontmatter populated and all placeholders replaced by the corresponding values. The resulting file is a plain markdown note indistinguishable from one created manually. Templates are distinguished from regular notes solely by their location in `.templates/`.

Templates are also reusable inside an existing note. With a note open in the editor, the user picks a template, fills the same typed form, and the app inserts the rendered **body** at the caret, replacing any active selection. The open note's YAML frontmatter is never touched by this operation — only the body of the template is inserted, never its frontmatter — so an existing note's tags, title, and other metadata survive verbatim. (Frontmatter templating remains exclusive to the new-note path.) The pick → parse → collect-values → render pipeline is shared by both the new-note path (FR-009) and the insert-into-existing-note path (FR-011); only the destination differs — a new file versus editor insertion.

## Access Control

Single user; no auth; data lives on-device only. The app opens directly into the notes workspace — no login, no profile selection, no role separation. All notes and templates are accessible to whoever runs the app.

## Non-Goals

- No attachments or image embedding in the MVP — notes are text-only markdown files. Attachments add storage, UI, and file-management complexity that is out of scope.

## Open Questions

All questions resolved:

1. ~~**Where do templates live?**~~ — Resolved: dedicated `.templates/` subfolder inside the notes folder.
2. ~~**How to distinguish templates from notes?**~~ — Resolved: by file location (`.templates/` subfolder).
3. ~~**What field types?**~~ — Resolved: text, date, number, dropdown/select.
4. ~~**Change working directory after first launch?**~~ — Resolved: yes, via a menu option (no full settings UI).
