---
date: 2026-07-05T15:43:29+02:00
researcher: pi-research
git_commit: 096e30d806743f3586081c9f59a52d5ad0e5c6bf
branch: main
repository: Notes
topic: "VM↔view signal mechanism for AvaloniaEdit caret/replace insert (template-into-note)"
tags: [research, codebase, note-editor, avaloniaedit, mvvm, messaging, templates-insert-into-note]
status: complete
last_updated: 2026-07-05
last_updated_by: pi-research
---

# Research: VM→view insert-signal mechanism for AvaloniaEdit caret/replace

**Date**: 2026-07-05T15:43:29+02:00 (CEST)
**Researcher**: pi-research
**Git Commit**: `096e30d806743f3586081c9f59a52d5ad0e5c6bf` (local working commit; not on `origin/main`)
**Branch**: `main`
**Repository**: Notes (no GitHub permalink — commit not pushed)

## Research Question

How should `NoteEditorViewModel` signal `NoteEditorView` (AvaloniaEdit) to insert a
rendered template body at the caret, replacing any selection, while staying inside the
project's MVVM + DI conventions — and what does the reverse view→VM channel need to
carry (caret offset + selection length) back so the VM's command stays testable?

Scope agreed with the user: **Depth = B** (pattern survey plus a recommended mechanism
with concrete code shape) and **Focus = 1 (AvaloniaEdit API surface) + 2 (MVVM boundary
conventions)**. Testability (Focus 4) and the messaging reuse analysis (Focus 3) are
touched only where they intersect with Focus 1/2.

## Summary

1. The codebase has **exactly one** existing view↔VM channel for the editor —
   view code-behind calls a **public method on the VM directly**
   (`NoteEditorView.OnEditorTextChanged` → `NoteEditorViewModel.OnEditorTextChanged(string)`,
   `Notes/Views/NoteEditorView.axaml.cs:46-53`), plus the VM pushes content *into* the
   view by the view subscribing to `PropertyChanged` and writing `Editor.Text`
   (`ApplyLoadedText`, same file, lines 30-44). There is **no** `IView`/`IViewFor`
   abstraction, **no** Avalonia `StyledProperty`/`AttachedProperty` carrying
   caret/selection, **no** `Behavior<T>` custom class, and **no** routed-event
   handler reused for this purpose anywhere in `Notes/` (verified by `rg` over all
   `.cs` excluding obj/, tests; only the AvaloniaEdit XAML element itself matches).
2. `EditorPaneState { Empty, Editing, Previewing }` already gates editor visibility
   and the VM exposes `IsEditing` (`Notes/ViewModels/NoteEditorViewModel.cs:38-46`).
   The new `InsertFromTemplate` command belongs on the editor VM and is gated on
   `IsEditing`, exactly as the frame brief's dimension 4 concluded.
3. The recommended mechanism — consistent with the existing channel and the AGENTS.md
   "code-behind is UI wiring only" rule — is: **the VM exposes a public method
   `ApplyCaretInsert(string body)` (no caret/selection state on the VM) and the view's
   code-behind performs the AvaloniaEdit `Document.Replace(caretOffset, selectionLength,
   body)` using `Editor.CaretOffset` / `Editor.SelectionLength` it already owns.**
   The reverse direction (view→VM announcing the new full text) reuses the existing
   `OnEditorTextChanged(string)` channel — `Editor.Document.Replace` raises
   `TextChanged`, so the view's existing handler automatically forwards the updated
   `Editor.Text` and `_currentEditorText` / autosave stay correct with zero new
   message type. Caret and selection length are **view-only**: they never live on a
   `Notes.Core` model, never cross into `Notes.Core`.
4. For the `what-inserted` (the rendered body), the orchestrator VM must fetch it from
   the new shared `ITemplateInstantiationService` (frame.md dimension 5). The body-only
   render is a *Core* concern; it can be exposed as a new method on
   `ITemplateRenderer` (e.g. `RenderBody(string, FormDefinition, IReadOnlyDictionary<…>)`)
   reusing the splitter already in `TemplateRenderer.Render` (`Notes.Core/Services/TemplateRenderer.cs:28-67`).
   The VM never needs to know about caret/selection to obtain the body — only the view
   needs them at insert time. This keeps the split clean.
5. No new `WeakReferenceMessenger` message type is required for the insert path — the
   view's existing `TextChanged` → `OnEditorTextChanged` channel already propagates the
   post-insert full text into `_currentEditorText` and autosave. A new message type would
   only be needed if the trigger were issued from a VM other than the editor VM; the
   frame brief assigns the trigger to the editor VM, so no message is needed.

## Detailed Findings

### A. Existing view↔VM channel on the editor (the precedent to follow)

`NoteEditorView.axaml.cs` is the code-behind that owns the `AvaloniaEdit.TextEditor`
instance (`Editor`, xaml `Notes/Views/NoteEditorView.axaml:15-18`). It currently does
exactly two things, both of which are the established shape for this codebase:

- **view → VM (text changed)**: on `Editor.TextChanged`, the handler calls a **public
  method** on the VM, passing the whole text:
  `Notes/Views/NoteEditorView.axaml.cs:46-53`
  ```csharp
  private void OnEditorTextChanged(object? sender, EventArgs e)
  {
      if (_suppressEvents || _viewModel is null) return;
      _viewModel.OnEditorTextChanged(Editor.Text);
  }
  ```
- **VM → view (loaded text)**: the view subscribes to `_viewModel.PropertyChanged` for
  `LoadedText` and writes the new value into `Editor.Text` under a `_suppressEvents`
  guard so it doesn't echo back through `TextChanged`:
  `Notes/Views/NoteEditorView.axaml.cs:30-44` (`ApplyLoadedText`).

The VM side of the channel: `NoteEditorViewModel.OnEditorTextChanged(string)` just
stores `_currentEditorText = text` and bumps the autosave scheduler
(`Notes/ViewModels/NoteEditorViewModel.cs:121-127`). It intentionally takes *the full
text only* — never caret/selection — and never has before.

`NoteTreeView.axaml.cs` is even thinner — `InitializeComponent()` and nothing else
(`Notes/Views/NoteTreeView.axaml.cs`), which is the project's neutral case when no
AvaloniaEdit plumbing is needed and confirms the project does **not** consider an
`IView` interface a required seam.

### B. No prior art for Behaviors / AttachedProperties / IViewFor in this codebase

`rg "Behavior|AttachedProperty|IView|AvaloniaEdit|CaretOffset|SelectionLength|Document.Replace"`
over all `.cs` (excluding obj/ and the Tests projects) returns **zero** hits for any of
`Behavior`, `AttachedProperty`, `IView`, `CaretOffset`, `SelectionLength`,
`Document.Replace` — the only matches are the AvaloniaEdit `edit:TextEditor` XAML element
in `NoteEditorView.axaml:15` and the `xmlns:edit` import. So:

- The project has never introduced an Avalonia `StyledProperty`/`AttachedProperty` to
  cross the MVVM boundary;
- The project has never used `Avalonia.Xaml.Interactivity` `Behavior<T>` custom classes
  (no `using Avalonia.Xaml.Interactivity` anywhere, no `<i:Interaction.Behaviors>` in
  any `.axaml`);
- The codebase does not implement `IViewFor<TViewModel>` or any view-interface.

The single established shape is therefore **"code-behind reads the Avalonia control's
own properties, calls a public VM method; VM writes via `PropertyChanged` + code-behind
writes back into the control"**. This is the safest precedent to extend for the insert
feature and matches AGENTS.md exactly: code-behind is "UI wiring", the *what to insert*
stays in the VM.

### C. AvaloniaEdit API surface, vs. what the codebase already calls (Focus 1)

The current code touches only `Editor.Text`, `Editor.TextChanged`,
`Editor.SyntaxHighlighting`, and the implicit `Editor.Document` (the
`AvaloniaEdit.TextEditor` exposes `Text` as a thin wrapper over `Document.Text`).
For caret/replace insert, the only additional members the view needs are:

- `Editor.CaretOffset : int` — current caret position as a character offset into
  `Document.Text`. Read-only at insert time (we don't move the caret except by virtue
  of `Replace`).
- `Editor.SelectionLength : int` — 0 when nothing is selected; the length of the
  selection region adjacent to the caret otherwise. Combined with `CaretOffset`,
  the *selected region* is `[CaretOffset - (caret at-selection-start ? 0 : SelectionLength), …]`.
  For doReplace purposes there is a simpler call.
- `Editor.Document.Replace(int offset, int length, string text)` (or
  `Editor.TextArea.Selection.ReplaceSelectionWith(string)`) — the safest, least
  framework-coupled primitive. The recommended form is:
  ```csharp
  var offset  = Editor.CaretOffset;
  var length  = Editor.SelectionLength;          // 0 when nothing selected
  var inserted = body;
  Editor.Document.Replace(offset, length, inserted);
  ```
  which inserts when `length == 0` and replaces when `length > 0`. `Replace` raises the
  `Document.TextChanged` + `Editor.TextChanged` events automatically, so the existing
  `OnEditorTextChanged` channel (A) re-fires and the VM's `_currentEditorText` is
  updated as a side effect — no new VM-side method is required to "report" the new text
  after insert.
- `Editor.SelectionStart` / `Editor.SelectionEnd` exist but are **not** needed for
  the simple "replace whatever is selected around the caret" semantics this feature
  requires (frame.md Q3=A — replace the active selection). `Document.Replace(CaretOffset,
  SelectionLength, …)` already covers the Q3=A case and the no-selection case in one
  call.

Edge cases that the insert must tolerate (the plan should own these):

- **Read-only / empty document**: `Document.Replace` is legal on an empty document
  (offset 0, length 0). No guard needed beyond firing the command only while `IsEditing`
  (`EditorPaneState.Editing`) — which already gates `IsVisible` on the `edit:TextEditor`
  in `NoteEditorView.axaml:17`, so the control is live exactly when the command is
  enabled.
- **Caret at offset 0 with selection spanning the whole note**: `Replace(0, total,
  body)` replaces everything — this is the documented "replace selection" behavior,
  not an error.
- **`Editor.Document` lazily initialized**: `AvaloniaEdit.TextEditor` constructs its
  `Document` on first use; reading `Editor.Document` after `InitializeComponent()` is
  safe (the existing code already sets `Editor.Text` in the constructor, which forces it).
  No new null-check is needed; the existing code reads `Editor.Text` comfortably and the
  view won't be re-created with a dead control.
- **`_suppressEvents` interplay**: `ApplyLoadedText` sets `_suppressEvents = true`
  around `Editor.Text = …` to stop recursion. The insert path **must not set
  `_suppressEvents`** — we *want* the changed-text channel to fire so the VM reflects
  the insertion. (If it did suppress, the VM would save a stale buffer.) This is the
  one asymmetry with the existing `ApplyLoadedText` path and is worth a comment in the
  code-behind.
- **Selection while in preview/empty**: command is gated on `IsEditing`, so the
  `TextEditor` is invisible and cannot hold a selection when the command is disabled;
  the plan doesn't need to defend against `CaretOffset` while previewing. Belts-and-
  braces: the view insert may still be invoked from code-behind in tests and should be a
  no-op if `_viewModel is not { IsEditing: true }`.

### D. MVVM boundary implications of "caret/selection must never live on a Core model" (Focus 2)

- `Notes.Core` may not reference Avalonia (`AGENTS.md` — "the `Notes.Core` project must
  not reference Avalonia or any UI framework"). Therefore any *caret offset*,
  *selection length*, or *insert-at* notion placed on a Core model (`EditorPaneState`,
  `EditorPaneState`, etc.) would be a leak even though the numbers themselves are just
  `int`. Practically: even storing `int CaretOffset` on `EditPaneState` would entrench
  a UI concept in the headless model and the headless VM tests would gain a degree of
  freedom they cannot meaningfully exercise. **The conclusion: caret and selection live
  on the view only**, and the VM's `ApplyCaretInsert(string body)` method
  intentionally *carries no offset* — it asks the view to insert wherever the caret is.
- **Compiled-bindings**: `NoteEditorView.axaml` already declares
  `x:DataType="vm:NoteEditorViewModel"` (`Notes/Views/NoteEditorView.axaml:14`), and the
  new command will be `[RelayCommand]`-generated `.InsertFromTemplateCommand` — a normal
  `ICommand` on the compiled-up VM. A hypothetical menu/menuitem binding such as
  `<MenuItem Command="{Binding InsertFromTemplateCommand}" />` compiles cleanly because
  `x:DataType` is `NoteEditorViewModel`. The `DataContext="{ReflectionBinding Editor,
  Source={StaticResource Locator}}"` hop (line 15) is the *only* ReflectionBinding in
  the view and that exception is already sanctioned by AGENTS.md — it remains
  ReflectionBinding; everything inside the view stays compiled. The insert trigger is
  therefore idiomatic: a VM command bound to a `MenuItem`/`Button`, *not* a view event
  handler bound from XAML.
- **`EditorPaneState.Editing` gating**: `IsEditing` is already recomputed on
  `OnPaneStateChanged` (`Notes/ViewModels/NoteEditorViewModel.cs:48-53`), so a
  `[RelayCommand(CanExecute = nameof(IsEditing))] private async Task
  InsertFromTemplate()` will toggle cleanly whenever the pane transitions in/out of
  editing. No extra `OnPropertyChanged` plumbing needed; CommunityToolkit re-evaluates
  `CanExecute` on `IsEditing`'s change. (`MainWindowViewModel.NewFromTemplateCommand`
  uses the same pattern with `[NotifyCanExecuteChangedFor]` at
  `Notes/ViewModels/MainWindowViewModel.cs:30-33` — though for `IsEditing` a plain
  `CanExecute = nameof(IsEditing)` suffices because `IsEditing` is itself
  `OnPropertyChanged`-driven off `PaneState`.)
- **"No `Async` suffix without a sync sibling" (AGENTS.md)**: name the VM method
  `InsertFromTemplate()` (Task-returning) and the view-side "do the replace" as
  `InsertBodyAtCaret(string body)` — neither needs an `Async` suffix.
- **Sharing the orchestration**: per frame.md dimension 5 (and AGENTS.md
  "Share behavior across ViewModels via DI-injected services…"), the pick→parse→
  collect→render pipeline is extracted into a shared `ITemplateInstantiationService`
  in `Notes.Core`. The *body-only* render the editor path needs is best added as a new
  method on the existing `ITemplateRenderer` (`RenderBody`) that reuses the frontmatter/
  body splitter already living in `TemplateRenderer.Render`
  (`Notes.Core/Services/TemplateRenderer.cs:28-67`). The renderer never needs to know
  about caret/selection — body extraction is a pure `string → string` over an already-
  rendered template, so Clean Core/MVVM constituents stay intact.

### E. Candidate mechanisms evaluated (Depth B survey)

| # | Mechanism | Where state lives | Fits existing precedent | Fits AGENTS.md code-behind rule | Verdict |
|---|---|---|---|---|---|
| 1 | **VM public method, view code-behind reads `Editor.CaretOffset`/`SelectionLength` and calls `Document.Replace`** | caret/selection view-only; the inserted body from Core service | ✅ Identical shape to existing `OnEditorTextChanged` channel (A) | ✅ code-behind = UI wiring; *what* in VM, *where* in view | **Recommended** |
| 2 | New `WeakReferenceMessenger` message `InsertBodyAtCaretMessage(string)` | caret/selection view-only; message carrier is Core | ⚠ View must subscribe to the messenger from code-behind — no precedent in `Notes/Views/` (all `IRecipient<…>` are VMs) | ⚠ Messenger-from-code-behind is novel and mixes messaging into a view; the message would have to be in `Notes.Core.Messaging`, but the only consumer would be one view | Rejected — over-engineered, introduces a new cross-VM coupling shape not present elsewhere |
| 3 | `StyledProperty`/`AttachedProperty` on `NoteEditorView` for `InsertText` and `CaretOffset` | caret/selection become bindable → end up swept into the VM via two-way binding | ❌ No `AttachedProperty`/`StyledProperty` for editor concerns exists; introducing one would tempt binding caret into the VM (Focus-2 leak risk) | ❌ Pushes UI state toward composability that the project consistently avoids | Rejected |
| 4 | `Avalonia.Xaml.Interactivity` `Behavior<TextEditor>` reacting to a VM `IsInsertPending` flag | `IsInsertPending : bool` on the VM | ❌ No `Behavior<T>` usage anywhere in `Notes/` | ✅ behaviors are conventionally "UI wiring" | Rejected — absent prior art, and an `IsInsertPending` bool state on the VM is uglier than a method call (mechanism 1) |
| 5 | View subscribes to `PropertyChanged` on a new `PendingInsertBody` VM property (mirrors existing `LoadedText` pattern) | caret/selection view-only; body on VM as a `string` property | ✅ Same shape as the existing `LoadedText` → `ApplyLoadedText` channel | ✅ code-behind writes to the control, just like today | Viable alternative — more verbose than mechanism 1 but consistent with the existing inbound channel; choose if you prefer a property + `PropertyChanged` hop over a method call |

### F. Recommended mechanism (concrete code shape)

**Notes.Core** (no Avalonia):

```csharp
// Notes.Core/Services/ITemplateRenderer.cs — add:
/// Renders only the body, substituting field tokens. Frontmatter (form block and any
/// kept keys) is dropped entirely. Use for inserting a template into an open note
/// whose frontmatter must not be touched.
string RenderBody(string templateText, FormDefinition definition,
                   IReadOnlyDictionary<string, string> values);
```

`TemplateRenderer.RenderBody` reuses `SplitLines` + `SubstituteBody` already private in
`Notes.Core/Services/TemplateRenderer.cs:72-95, 124-133` and simply drops the frontmatter
region (`closing`-fence logic at lines 33-44). Pure, no Avalonia, no caret awareness.

**Notes** (editor VM, gated on `IsEditing`):

```csharp
// Notes/ViewModels/NoteEditorViewModel.cs — add a command + a public view-hook method.
// The command does the Core orchestration (shared service) and asks the view to do the
// canvas-specific work. Caret/selection never touch the VM.

[RelayCommand(CanExecute = nameof(IsEditing))]
private async Task InsertFromTemplate()
{
    var body = await _instantiation.RenderBodyForInsert();  // shared service, Core-only
    if (string.IsNullOrEmpty(body)) return;
    ApplyCaretInsert(body);                                  // view hook
}

/// Called by the view's code-behind to actually insert; the VM forwards it
/// to the view-side hook. Caret/selection are resolved by the *view* at call time.
internal event Action<string>? InsertAtCaretRequested;
internal void ApplyCaretInsert(string body) => InsertAtCaretRequested?.Invoke(body);
```

> The `event Action<string>?` is the cheapest possible "view hook" seam that does not
> require the project to introduce any new base class, interface, or messenger use. It
> is *internal* so the headless VM tests don't see it (a headless test runs the VM with
> no view attached and `InsertAtCaretRequested` stays `null` → `ApplyCaretInsert` is a
> no-op; the test asserts `_currentEditorText` never grew, which is the desired
> observable behavior under a headless harness). This seams the otherwise-untestable
> view-side `Document.Replace` cleanly.

**Notes** (view code-behind):

```csharp
// Notes/Views/NoteEditorView.axaml.cs — add inside OnDataContextChanged (subscribe/
// unsubscribe mirroring the PropertyChanged subscription):
_viewModel.InsertAtCaretRequested = OnInsertAtCaretRequested;   // simple event slot

private void OnInsertAtCaretRequested(string body)
{
    if (_viewModel is not { IsEditing: true }) return;
    var offset = Editor.CaretOffset;
    var length = Editor.SelectionLength;
    // Intentionally NOT setting _suppressEvents — we want TextChanged to fire so the
    // existing OnEditorTextChanged channel updates _currentEditorText and autosave.
    Editor.Document.Replace(offset, length, body);
}
```

- `Editor.Document.Replace` raises `Editor.TextChanged` → existing
  `OnEditorTextChanged` → `_viewModel.OnEditorTextChanged(Editor.Text)` → VM stores
  `_currentEditorText` and bumps autosave. **No new message type, no new
  `WeakReferenceMessenger` message**.
- The `[RelayCommand(CanExecute = nameof(IsEditing))] InsertFromTemplateCommand`
  binds to a `MenuItem`/`Button` in `NoteEditorView.axaml` (compiled binding — same
  `x:DataType="vm:NoteEditorViewModel"` already at `NoteEditorView.axaml:14`). Or, if
  the command belongs at the window-menu level and should be disabled when not editing,
  it can be exposed via the editor VM and the window's XAML binds through the Locator's
  `Editor` accessor (`Notes/ViewModels/ViewModelLocator.cs:11`) — same ReflectionBinding
  exception the rest of that view-root uses.

### G. Where the trigger command surfaces (single ViewModel owner)

`MainWindowViewModel.NewFromTemplate` (the **menu** path) sends
`NewFromTemplateRequestedMessage`
(`Notes/ViewModels/MainWindowViewModel.cs:78-82`) handled by `NoteTreeViewModel`
(`Notes/ViewModels/NoteTreeViewModel.cs:62-69`, `HandleNewFromTemplate` at lines 101-141).
For the editor-insert path:

- The `InsertFromTemplate` command lives on `NoteEditorViewModel` (frame.md dimension
  4 — editor-owned trigger).
- If the product decision is to keep it as a *menu* entry in `MainWindow`, the editor
  VM's command is bound via `Locator.Editor.InsertFromTemplateCommand` in the
  `MainWindow.axaml` menu definition (Locator accessor: `ViewModelLocator.cs:11`).
  CanExecute mirrors `IsEditing`, so the menu item grey/disables whenever the editor is
  empty or previewing — exactly the gating dimension 4 calls for, without a new
  messenger message.
- No new message type is required *unless* the trigger is later moved to a non-editor
  VM. That path is documented as out of scope by the frame brief, and shouldn't be
  pre-built.

## Code References

- `Notes/Views/NoteEditorView.axaml.cs:46-53` — existing view→VM channel:
  `OnEditorTextChanged` calls `_viewModel.OnEditorTextChanged(Editor.Text)`.
- `Notes/Views/NoteEditorView.axaml.cs:30-44` — `ApplyLoadedText`, the VM→view channel
  with its `_suppressEvents` guard (the one precedent for "VM writes into the control").
- `Notes/Views/NoteEditorView.axaml:14-18` — the `edit:TextEditor` (`x:Name="Editor"`)
  and the `IsVisible="{Binding IsEditing}"` gating.
- `Notes/ViewModels/NoteEditorViewModel.cs:38-53` — `IsEditing` and the
  `OnPaneStateChanged` → `IsEditing` `OnPropertyChanged` driver (gates the new
  `InsertFromTemplate` command's `CanExecute`).
- `Notes/ViewModels/NoteEditorViewModel.cs:121-127` — `OnEditorTextChanged(string)`,
  the only existing post-edit hook the VM has; reused (unchanged) by the insert path.
- `Notes/ViewModels/NoteTreeViewModel.cs:101-141` — `HandleNewFromTemplate`, the
  orchestration to extract into the shared `ITemplateInstantiationService`
  (frame.md dimension 5).
- `Notes/ViewModels/MainWindowViewModel.cs:78-82` — the menu-path command and the
  `[RelayCommand(CanExecute = nameof(HasTemplates))]` gating pattern to mirror for
  `InsertFromTemplate`.
- `Notes.Core/Services/TemplateRenderer.cs:28-67` — `Render` (frontmatter-strip +
  body substitution); `RenderBody` for the editor-insert path reuses `SplitLines`
  (lines 72-95) and `SubstituteBody` (lines 124-133).
- `Notes.Core/Messaging/Messages.cs` — existing message set; **no new message type
  needed** for the insert path (per Focus-2 analysis).
- `Notes/Program.cs:60-67` — DI composition root; the new `ITemplateInstantiationService`
  is a singleton here, the same registration style as the existing template services.
- `Notes/ViewModels/ViewModelLocator.cs:11` — `Editor` accessor, the only place the
  editor VM is reachable from `MainWindow.axaml`'s menu (ReflectionBinding exception
  sanctioned by AGENTS.md).
- `Notes.Tests/NoteEditorViewModelTests.cs:103-109` — existing test that proves the headless
  editor VM never touches the view: `OnEditorTextChanged` is the model of a VM-only seam.
  Under the recommended mechanism, the new `InsertFromTemplate` command runs the Core
  service to get the body, then `ApplyCaretInsert(body)` no-ops (event is `null`) — *no
  real `Document.Replace` runs in tests*, which is exactly the headless-friendliness we
  want.

## Architecture Insights

- The codebase's de-facto convention for AvaloniaEdit coupling is **"code-behind owns
  the control; VM owns the text"**, implemented by direct method call (view→VM) and a
  `PropertyChanged`-driven write (VM→view). The insert feature should extend this shape,
  not introduce a new shape (no `Behavior<T>`, no `AttachedProperty`, no
  messenger-from-view).
- Caret and selection are **canvas-local state of the editor control**, not domain
  state — they must never appear on a `Notes.Core` model and should not bind into the
  VM. The VM asks for "insert the body wherever the caret is" by calling down through a
  seam (`InsertAtCaretRequested`); the view resolves the offset/length at view-time.
- The existing `OnEditorTextChanged` channel is *bidirectional-enough* for the insert
  path: `Document.Replace` raises `TextChanged`, the view forwards the updated text, the
  VM stores it and autosaves. There is **no need to add a view→VM "caret changed"
  message** for this feature — the VM does not act on caret/selection at all.
- Gating a per-pane command on `IsEditing` is a one-liner because `IsEditing` is already
  re-derived on `PaneState` change — `CanExecute = nameof(IsEditing)` on the
  `[RelayCommand]` is sufficient.
- The shared orchestration per AGENTS.md ("Share behavior across ViewModels via
  DI-injected services") belongs in a new `ITemplateInstantiationService` Core singleton
  whose body-only return value the editor VM consumes; the view never touches it.

## Historical Context (from prior changes)

- `context/archive/2026-06-02-templates/change.md` — *the* decision that deferred this
  exact feature out of MVP, naming "the AvaloniaEdit caret/insert plumbing risk" as the
  deferral reason. The research above is the planning-stage elaboration of that risk.
- `context/archive/2026-06-02-templates/plan.md:39,58` — the verbatim-frontmatter
  decision that motivates the body-only insert path: inserting the full render mid-note
  would re-emit a `---` fence and produce invalid markdown. This is *why* the
  editor-insert path is body-only and why `RenderBody` is the right Core seam.
- `context/foundation/lessons.md` — "Don't synchronously `Dispose` a `CancellationTokenSource`
  shared with an in-flight task" — applies to `NoteSearchIndex`, not directly to the
  editor insert, but the same care should be taken if the shared service ever cancels
  an in-flight template render. No CTS is foreseen for the synchronous render pipeline
  the editor path needs today.
- `context/changes/templates-insert-into-note/frame.md` — the framing brief whose
  dimension 3 ("Cursor access lives only in the view") this research resolves, and whose
  dimension 5 ("Orchestration sharing") constrains where `RenderBody` lives. Recommended
  mechanism 1 in section E above is the direct implementation of frame.md dimension 3.

## Related Research

- No prior `research.md` exists under `context/changes/**` or `context/archive/**` for
  the editor insert mechanism (this is the first research artifact in this change). The
  nearest related planning artifacts are `context/archive/2026-06-02-templates/plan.md`
  (full-feature plan that deferred this) and
  `context/archive/2026-06-06-testing-template-pipeline/` (testing pipeline).

## Open Questions

1. **Testability floor (out of requested focus).** The recommended mechanism makes the
   headless VM test of `InsertFromTemplate` trivial (assert `_currentEditorText` is
   unchanged and that the shared `RenderBodyForInsert` was called), but does **not**
   exercise `Editor.Document.Replace` itself in `Notes.Tests`. A follow-up is whether
   to add an E2E/headless+xunit test pinning the view code-behind (per
   `context/archive/2026-06-28-avalonia-headless-e2e/`) to assert the document was
   actually replaced. Decide at `/10x-plan` time; not researched here (Focus=1,2 only).
2. **Trigger placement — menu vs editor-toolbar.** If the trigger ends up on a
   `MainWindow` menu item (not an editor toolbar) the binding must go through
   `Locator.Editor.InsertFromTemplateCommand` via the ReflectionBinding exception. The
   recommended mechanism supports either; the plan picks one. Worth confirming the
   product intent at plan time, since it determines whether a menu-shortcut also needs
   gating disclosure.
3. **`InsertAtCaretRequested` vs `PendingInsertBody` property.** Two viable shapes
   (mechanism 1 vs mechanism 5 in section E) both satisfy the conventions; pick the
   property-version if the team prefers symmetry with `LoadedText` (a property + the
   existing `PropertyChanged` subscription) over a method/event-call-down seam. The
   plan should state a choice; this research recommends mechanism 1 for minimal
   surface area.