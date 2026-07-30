# Frame Brief: Insert a rendered template into an existing note

> Framing step before `/10x-plan`. Captures what is actually at issue,
> separated from what was initially assumed.

## Reported Observation

Templates can only be used to spawn a brand-new note (via the "New from
Template" menu). They cannot be applied to a note the user is already
editing — there is no way to drop a rendered template's content into an
existing note.

## Initial Framing (preserved)

- **User's stated cause or approach**: templates should be reusable in an
  existing note.
- **User's proposed direction**: insert the template at the cursor
  position in the open note.
- **Pre-dispatch narrowing**: the user picked two scope positions —
  (Q2) two entry points sharing a common core: the menu creates a new
  note *and then* inserts the rendered content; the editor inserts the
  rendered content into the currently open note. (Q1) frontmatter is
  never changed when the target is an existing non-empty note. (Q3) the
  active selection is replaced. ("Insert at cursor") held up; Q1+A
  scoped it to body-only and Q2+A/Q3+A pinned selection + entry-point
  behavior.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Body-only insert is genuinely all that's wanted** — `TemplateRenderer`
   already substitutes `{{field}}` in the body; inserting that rendered
   body into the editor is the whole feature. ← initial framing.
2. **Frontmatter conflict** — `TemplateRenderer.Render` keeps the
   template's non-`form` frontmatter verbatim and emits a full `---`
   fence + body (`Notes.Core/Services/TemplateRenderer.cs:28-67`; archived
   plan `2026-06-02-templates/plan.md:39,58`). Inserting the *full* render
   mid-note would produce a stray `---` fence = invalid markdown.
3. **Cursor access lives only in the view** — `NoteEditorView.axaml.cs`
   owns the `AvaloniaEdit.TextEditor`; `NoteEditorViewModel.OnEditorTextChanged(string)`
   only sees the full text, never caret/selection. No cursor channel
   crosses that MVVM boundary today.
4. **Enablement / trigger location** — "New from Template" is a
   `MainWindow` menu command gated on `HasTemplates`
   (`MainWindowViewModel.cs:78-82`), handled by `NoteTreeViewModel`.
   Insert-into-open-note is naturally owned by `NoteEditorViewModel`
   (which owns editor text) and must be live only while editing
   (`EditorPaneState.Editing`), not in preview/empty.
5. **Orchestration sharing** — pick → parse → collect → render lives
   entirely inside `NoteTreeViewModel.HandleNewFromTemplate`
   (`NoteTreeViewModel.cs:101-141`). Repeating it in the editor VM
   duplicates logic AGENTS.md says to extract as a shared service.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| 1. Body-only insert is the whole feature | `ITemplateRenderer.Render` already substitutes body tokens; `Render` is pure and has no UI deps (`Notes.Core/Services/ITemplateRenderer.cs`, `TemplateRenderer.cs`). | STRONG but incomplete — there is no public body-only extractor; `Render` emits frontmatter when non-`form` keys survive. |
| 2. Frontmatter conflict breaks naive "insert full render" | `TemplateRenderer.cs:46-67` — when `keptFrontmatter.Count > 0` it re-emits the opening fence, kept keys, closing fence, then body. Inserting that into a note mid-text yields a second `---` fence → invalid markdown. Archived plan explicitly chose verbatim frontmatter, no YAML round-trip. | STRONG |
| 3. Caret/selection has no MVVM channel | `NoteEditorView.axaml.cs` reads `Editor.Text` only; `NoteEditorViewModel.OnEditorTextChanged(string)` takes a single string, never caret/selection. `Editor.CaretOffset`/`Editor.SelectionLength` are AvaloniaEdit APIs on the view, not surfaced. | STRONG |
| 4. Trigger belongs in the editor VM, gated on Editing | `EditorPaneState { Empty, Editing, Previewing }` exists (`Notes.Core/Models/EditorPaneState.cs`); `IsEditing` is already exposed (`NoteEditorViewModel.cs:38`). Today's menu handler is tree-owned because the tree drives the new-note pipeline; insert-into-open-note has nothing to do with the tree. | STRONG |
| 5. Orchestration should be a shared service | AGENTS.md rule: "Share behavior across ViewModels via DI-injected services, not base-class hierarchies." `HandleNewFromTemplate` is the candidate to extract; both VMs would inject it. | STRONG |

## Narrowing Signals

- **Q1 = A** (user): existing-note frontmatter is never modified. This
  rules out any frontmatter-merge dimension; the editor-insert path is
  body-only by user decision, not by renderer limitation. Decisive — it
  kills dimension-2's "merge frontmatter" alternative before planning.
- **Q2 (user, restated)**: two entry points sharing a common core — menu
  → new note + rendered content; editor → rendered content into open
  note. Pins dimension 4 (editor-scoped trigger) AND dimension 5
  (extract shared orchestration). Confirms the new command lives in the
  editor, the menu path keeps today's frontmatter-applied semantics.
- **Q3 = A** (user): replace the active selection. Decisive for dimension
  3 — the view must report caret + selection length, not just caret.

## Cross-System Convention

- **AvaloniaEdit caret/insert**: the standard pattern is for the view
  (code-behind) to read `TextEditor.CaretOffset` / `SelectionLength` and
  perform `Document.Replace(offset, length, text)` (or
  `Editor.Document.Insert`), then surface the result to the VM via the
  existing `OnEditorTextChanged` channel. AGENTS.md permits this —
  code-behind is for "UI wiring" only; the *what* (insert this body)
  stays in the VM, the *where* (caret/selection) is the view's job.
- **MVVM + DI-sharing**: the project already routes all shared logic
  through `Notes/Program.cs` singletons; the new shared service follows
  the same registration.
- **Existing templates decision record**: the archived S-04 plan
  explicitly deferred this feature noting "the AvaloniaEdit caret/insert
  plumbing risk" (`context/archive/2026-06-02-templates/change.md`).
  Dimension 3 is precisely that risk — the plan must own it.

## Reframed (or Confirmed) Problem Statement

> **The actual problem to plan around is**: add a body-only template-insert
> entry point in the editor (caret-position, replace-selection), backed by
> a shared instantiate-template service extracted from the existing
> new-from-template orchestration, while the menu path keeps today's
> frontmatter-applied new-note semantics.

The initial framing ("insert template at cursor") was correct in spirit.
Two refinements the framing added: (a) the editor-insert must be
**body-only** or it produces invalid markdown (a stray `---` fence) — this
is the load-bearing constraint the user's Q1=A locked; (b) the
pick→parse→collect→render pipeline must be **extracted into a shared
service** so the menu and editor paths don't duplicate it. The menu path is
unchanged behavior; the editor path is net-new.

## Confidence

- **HIGH** — strong evidence at every dimension (file:line above), matches
  the project's MVVM+DI convention, and the decisive narrowing signals
  (Q1/Q2/Q3) each killed an open alternative. Also corroborated by the
  archived S-04 decision record naming this exact deferral.

## What Changes for /10x-plan

The plan should: (1) extract a shared `ITemplateInstantiationService`
(working name) into `Notes.Core` covering pick → parse → collect → render,
returning both the full render (for the menu path) and a body-only render
(for the editor path) — likely a new pure method on `ITemplateRenderer`
for body extraction; (2) reroute `NoteTreeViewModel.HandleNewFromTemplate`
through it (no behavior change); (3) add an editor-scoped
`InsertFromTemplate` command to `NoteEditorViewModel`, gated on
`IsEditing`, that calls the shared service for the body-only render and
inserts at the caret replacing any selection via the view (code-behind
reads `Editor.CaretOffset`/`SelectionLength`, performs the replace); (4)
surface caret/selection from `NoteEditorView` to `NoteEditorViewModel`
through a small UI-wiring method on the VM (not a property on the model);
(5) add the menu/editor command enablement and a message type if needed.

## References

- Source files:
  - `Notes.Core/Services/ITemplateRenderer.cs`
  - `Notes.Core/Services/TemplateRenderer.cs:28-67`
  - `Notes/ViewModels/NoteTreeViewModel.cs:101-141` (`HandleNewFromTemplate`)
  - `Notes/ViewModels/NoteEditorViewModel.cs:38, 113-129` (`IsEditing`,
    `OnEditorTextChanged`, `DoSave`)
  - `Notes/Views/NoteEditorView.axaml.cs` (AvaloniaEdit ownership)
  - `Notes/ViewModels/MainWindowViewModel.cs:78-82` (menu enablement)
  - `Notes.Core/Models/EditorPaneState.cs`
- Related decisions: `context/archive/2026-06-02-templates/change.md`
  ("Apply-template-to-existing-note: OUT of MVP scope. Deferred to the
  first post-MVP feature.") and `…/plan.md:39,58` (verbatim frontmatter
  decision)
- PRD update target: `context/foundation/prd.md` — new US-03 + extended
  FR-009 + §Business Logic paragraph