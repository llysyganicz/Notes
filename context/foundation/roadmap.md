---
project: "Notes"
version: 1
status: draft
created: 2026-05-25
updated: 2026-05-25
prd_version: 1
main_goal: low-complexity
top_blocker: capacity
---

# Roadmap: Notes

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline.
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

Existing markdown note-taking apps force a choice between abandoned simplicity (Notable) and maintained complexity (Obsidian). Notes merges plain-text editing with syntax highlighting and a template system driven by YAML frontmatter into one lightweight desktop app — plain markdown files, structured templates, nothing more.

## North star

**S-04: User can create and use templates** — the product's differentiator (the one capability that, if removed, makes the app indistinguishable from a generic markdown editor). Placed as early as its prerequisites allow because everything else only matters if templates work.

> "North star" means the smallest end-to-end slice whose successful delivery proves the core product hypothesis — that combining plain-text editing with syntax highlighting and a YAML-frontmatter-driven template system in one lightweight app fills a gap no existing tool covers. It is placed as early as prerequisites allow because everything else only matters if this works.

## At a glance

| ID | Change ID | Outcome (user can …) | Prerequisites | PRD refs | Status |
|---|---|---|---|---|---|
| S-01 | workspace-and-note-list | select a notes folder, browse notes with folder grouping, and delete a note | — | FR-010, FR-007, FR-003, US-01 | ready |
| S-02 | note-editor-and-preview | create a note, edit with syntax highlighting, and preview as HTML | S-01 | FR-001, FR-002, FR-004, US-01 | proposed |
| S-03 | tags-and-search | tag notes via YAML frontmatter and search/filter by name, tags, content | S-02 | FR-005, FR-006, US-01 | proposed |
| S-04 | templates | create a template and generate a note from it via a typed form | S-02 | FR-008, FR-009, US-02 | proposed |

## Streams

Navigation aid — groups items that share a Prerequisites chain. Canonical ordering still lives in the dependency graph below; this table is the proposed reading order across parallel tracks.

| Stream | Theme | Chain | Note |
|---|---|---|---|
| A | Core + north star | `S-01` → `S-02` → `S-04` | Shortest path to the north star — proves the product hypothesis after two prerequisite slices. |
| B | Organization | `S-03` | Parallel with S-04 after S-02 lands; delivers search/filter for the daily note-taking workflow. |

## Baseline

What's already in place in the codebase as of 2026-05-25 (auto-researched + user-confirmed).
Slices below account for these when building their scope.

- **Frontend:** Partial — Avalonia UI 12 scaffold with FluentTheme, `MainWindow.axaml`, `App.axaml`, compiled bindings enabled. No MVVM structure.
- **Backend / API:** N/A — local desktop app, no server.
- **Data:** Absent — no file I/O services, no YAML parser, no markdown renderer.
- **Auth:** N/A — single user, no auth (per PRD §Access Control).
- **Deploy / infra:** Present — `.github/workflows/release.yml` (Linux AppImage + Windows ZIP on tag push), `infrastructure.md`.
- **Observability:** Absent — only Avalonia's built-in `LogToTrace()`.

## Foundations

No foundations. The MVVM scaffold, file I/O, YAML parsing, and markdown rendering are each delivered within the first vertical slice that needs them — S-01 brings the app shell and file browsing, S-02 brings the editor and markdown renderer, S-03 and S-04 each bring their YAML parsing needs. With `main_goal: low-complexity`, growing the architecture organically with each slice avoids upfront horizontal design.

## Slices

### S-01: Workspace selection, note list, and delete

- **Outcome:** user can select a notes folder on first launch (and switch it later via a menu option), browse existing markdown notes in a list with folder-based grouping, and delete a note with a confirmation dialog
- **Change ID:** workspace-and-note-list
- **PRD refs:** FR-010, FR-007, FR-003, US-01
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Lowest-risk slice — standard file system browsing and Avalonia folder picker. Sequenced first because every subsequent slice needs a selected workspace and visible note list.
- **Status:** ready

### S-02: Create, edit, and preview a note

- **Outcome:** user can create a new blank markdown note, edit it in a plain-text editor with markdown syntax highlighting, save it as a `.md` file, and preview the note rendered as HTML
- **Change ID:** note-editor-and-preview
- **PRD refs:** FR-001, FR-002, FR-004, US-01
- **Prerequisites:** S-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The editor component choice (syntax highlighting quality, performance) directly affects the PRD's NFR ("feel instant"). If the chosen editor control underperforms, it impacts every downstream slice. Sequenced immediately after S-01 because both S-03 and S-04 need a working editor.
- **Status:** proposed

### S-03: Tags and search/filter

- **Outcome:** user can tag notes via YAML frontmatter and search/filter the note list by name, tags, and note content (full-text)
- **Change ID:** tags-and-search
- **PRD refs:** FR-005, FR-006, US-01
- **Prerequisites:** S-02
- **Parallel with:** S-04
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Low risk — YAML frontmatter parsing and in-memory full-text search are well-understood at small data volumes. Sequenced after S-02; parallel with S-04 because neither depends on the other.
- **Status:** proposed

### S-04: Templates — create and use

- **Outcome:** user can create a template (YAML frontmatter field definitions + body placeholders) in `.templates/`, and create a new note from a template by filling a typed form (text, date, number, dropdown/select) that replaces all placeholders
- **Change ID:** templates
- **PRD refs:** FR-008, FR-009, US-02
- **Prerequisites:** S-02
- **Parallel with:** S-03
- **Blockers:** —
- **Unknowns:** —
- **Risk:** The template engine — YAML field parsing, typed form generation, placeholder replacement — is custom domain logic with no off-the-shelf solution. This is the differentiator and the area where the "invest deeply in data/domain logic" decision applies. Sequenced after S-02; parallel with S-03.
- **Status:** proposed

## Backlog Handoff

| Roadmap ID | Change ID | Suggested issue title | Ready for `/10x-plan` | Notes |
|---|---|---|---|---|
| S-01 | workspace-and-note-list | Workspace selection, note list with folder grouping, and note deletion | yes | Run `/10x-plan workspace-and-note-list` |
| S-02 | note-editor-and-preview | Create, edit (syntax highlighting), and preview markdown notes | no | Depends on S-01 |
| S-03 | tags-and-search | YAML frontmatter tags and full-text search/filter | no | Depends on S-02; parallel with S-04 |
| S-04 | templates | Template creation and note-from-template with typed form | no | Depends on S-02; parallel with S-03; north star |

## Open Roadmap Questions

None. All PRD open questions are resolved; no new cross-cutting questions surfaced during roadmap generation.

## Parked

- **Attachments / image embedding** — Why parked: PRD §Non-Goals — "notes are text-only markdown files; attachments add storage, UI, and file-management complexity."
- **macOS packaging** — Why parked: infrastructure.md §Out of Scope — deferred to post-MVP.
- **Auto-update mechanism** — Why parked: infrastructure.md §Out of Scope — GitHub Releases API polling deferred to post-MVP.
- **Code signing** — Why parked: infrastructure.md §Code Signing — deferred to post-MVP; SmartScreen reputation builds organically.

## Done

(Empty on first generation. `/10x-archive` appends entries here when a change is archived.)
