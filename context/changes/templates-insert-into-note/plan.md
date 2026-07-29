# Insert a rendered template body into an existing note at the cursor

## Overview

Add an editor-side "Insert from Template" entry point that renders a template's
body and inserts it at the current caret position, replacing any active selection.
The existing menu "New from Template" path is preserved. The shared
pick → parse → collect → render pipeline is extracted into a `TemplateService`
in the Notes layer, and the body-only render is added to `ITemplateRenderer` in
Notes.Core.

## Current State Analysis

- `ITemplateRenderer.Render` returns the full generated note (frontmatter + body).
  There is no body-only render path.
- `NoteTreeViewModel.HandleNewFromTemplate` contains the entire template
  orchestration: list templates, show picker, read file, parse, show form, render.
- `NoteEditorViewModel` has no template-related logic and no caret/selection channel.
- `NoteEditorView.axaml.cs` owns the AvaloniaEdit `TextEditor` and already
  communicates with the VM via `OnEditorTextChanged(string)` and the `LoadedText`
  property-changed channel.
- `MainWindow.axaml` binds commands through `Locator.*` and exposes
  "New from Template" under the File menu.

## Desired End State

- `ITemplateRenderer` exposes `RenderBody` that returns only the substituted body,
  dropping the template's frontmatter entirely.
- `Notes/Services/TemplateService` encapsulates the template orchestration and
  exposes two methods:
  - `RenderForNewNote(string workspacePath)` for the menu new-note path.
  - `RenderForInsert(string workspacePath)` for the editor insert path.
- `NoteTreeViewModel` delegates its template flow to `TemplateService`; behavior
  is unchanged.
- `NoteEditorViewModel` has an `InsertFromTemplateCommand` gated on `IsEditing`
  that calls `TemplateService` to get the body and asks the view to insert it.
- `NoteEditorView.axaml.cs` performs `Editor.Document.Replace` at the caret,
  replacing any selection.
- The File menu has an "Insert from Template" item with a shortcut, bound to
  `Locator.Editor.InsertFromTemplateCommand`.

## What We're NOT Doing

- No custom wrapper control around AvaloniaEdit.
- No automated tests for the view-side `Document.Replace` behavior.
- No change to the "New from Template" menu path behavior or UX order.
- No frontmatter merging into existing notes.
- No new `WeakReferenceMessenger` message type for the insert path.
- No cancellation tokens on the dialog-driven `TemplateService` methods — the
  existing dialog services do not accept them.

## Implementation Approach

1. Extend the pure Core renderer with a body-only method.
2. Extract the shared orchestration into `TemplateService` in the Notes layer.
3. Reroute the menu path through `TemplateService` (no behavior change).
4. Add the editor command and the view-side insert seam.
5. Add the menu item and shortcut.
6. Verify the menu path still works and the editor insert behaves correctly.

## Critical Implementation Details

- The view-side insert must **not** set `_suppressEvents`. `Document.Replace`
  raises `TextChanged`, and the existing `OnEditorTextChanged` channel must
  propagate the updated text so autosave works.
- Caret offset and selection length are **view-only**; they never live on a Core
  model or VM property.
- The command is enabled only when `IsEditing` is true. `IsEditing` is already
  recomputed on `PaneState` change, so
  `[RelayCommand(CanExecute = nameof(IsEditing))]` is sufficient.
- `TemplateService` lives in `Notes` (not `Notes.Core`) because it depends on
  `ITemplatePickerDialogService` and `ITemplateFormDialogService`, which are
  UI-dialog abstractions in the Notes layer.
- The `InsertAtCaretRequested` event is `internal` so headless VM tests do not
  see it; calling `ApplyCaretInsert` with no view attached is a no-op.

## Phase 1: Add body-only render to Core template renderer

### Overview

Add `RenderBody` to `ITemplateRenderer` and implement it in `TemplateRenderer`,
reusing the existing frontmatter/body splitter and body substitution.

### Changes Required:

#### 1. Core renderer interface

**File**: `Notes.Core/Services/ITemplateRenderer.cs`

**Intent**: Expose a body-only render contract so the editor-insert path can get
substituted body text without the template's frontmatter.

**Contract**: Add method
`string RenderBody(string templateText, FormDefinition definition, IReadOnlyDictionary<string, string> values);`.

#### 2. Core renderer implementation

**File**: `Notes.Core/Services/TemplateRenderer.cs`

**Intent**: Implement `RenderBody` by reusing the existing `SplitLines` and
`SubstituteBody` helpers, returning only the body region after the closing
frontmatter fence.

**Contract**:
- Signature:
  `string RenderBody(string templateText, FormDefinition definition, IReadOnlyDictionary<string, string> values)`.
- Locate the frontmatter fences the same way `Render` does: split into lines,
  treat the whole text as body when the first line is not `---` or when no
  closing `---` fence is found; otherwise take the lines after the closing
  fence as the body.
- Null `templateText` is treated as empty.
- The resulting body is passed through the existing `SubstituteBody` helper,
  so token substitution behaves identically to `Render`.

#### 3. Core renderer tests

**File**: `Notes.Core.Tests/TemplateRendererTests.cs`

**Intent**: Pin `RenderBody` behavior for templates with frontmatter, without
frontmatter, and with body-only content.

**Contract**: Add test cases covering:
- Template with frontmatter and body returns only the substituted body.
- Template without frontmatter returns the full substituted text.
- Template with only frontmatter returns an empty string.
- Undeclared tokens in the body remain verbatim.

### Success Criteria:

#### Automated Verification:

- `dotnet test --filter Notes.Core.Tests` passes.
- New `RenderBody` tests pass.

#### Manual Verification:

- N/A

---

## Phase 2: Extract shared template orchestration into TemplateService

### Overview

Move the pick → parse → collect → render pipeline from `NoteTreeViewModel` into
a new `TemplateService` in the Notes layer, then reroute the menu path through it.

### Changes Required:

#### 1. Template service interface

**File**: `Notes/Services/ITemplateService.cs`

**Intent**: Define the shared orchestration contract that both ViewModels will
consume.

**Contract**: Two async methods that take the workspace path and return the
rendered text, or `null` if the user cancels any dialog step.

```csharp
public interface ITemplateService
{
    Task<string?> RenderForNewNote(string workspacePath);
    Task<string?> RenderForInsert(string workspacePath);
}
```

#### 2. Template service implementation

**File**: `Notes/Services/TemplateService.cs`

**Intent**: Encapsulate the orchestration currently in
`NoteTreeViewModel.HandleNewFromTemplate`.

**Contract**: Constructor takes `ITemplateCatalog`,
`ITemplatePickerDialogService`, `INoteFileService`, `ITemplateParser`,
`ITemplateFormDialogService`, and `ITemplateRenderer`.

- `RenderForNewNote` calls `_templateRenderer.Render(...)` and returns the full
  rendered text.
- `RenderForInsert` calls `_templateRenderer.RenderBody(...)` and returns only
  the body.
- Both methods return `null` if the picker is cancelled or the form is cancelled.
- A template with zero fields skips the form dialog.

#### 3. DI registration

**File**: `Notes/Program.cs`

**Intent**: Register `TemplateService` as a singleton.

**Contract**: Add `services.AddSingleton<ITemplateService, TemplateService>();`.

#### 4. NoteTreeViewModel refactor

**File**: `Notes/ViewModels/NoteTreeViewModel.cs`

**Intent**: Delegate the template orchestration to `TemplateService`.

**Contract**:
- Replace `ITemplateCatalog`, `ITemplatePickerDialogService`, `ITemplateParser`,
  `ITemplateFormDialogService`, and `ITemplateRenderer` constructor parameters
  with `ITemplateService`.
- Update `HandleNewFromTemplate` to call
  `_templateService.RenderForNewNote(_workspacePath)` and pass the result to
  `PromptNameAndSave` when non-null.

#### 5. NoteTreeViewModel tests update

**File**: `Notes.Tests/NoteTreeViewModelTests.cs`

**Intent**: Update the test setup to provide an `ITemplateService` mock instead
of the individual template dependencies.

**Contract**: Add `ITemplateService _templateService = Substitute.For<ITemplateService>();`
and adjust the constructor call and test stubs. The existing test assertions on
saved file content remain unchanged.

#### 6. E2E test harness registration

**File**: `Notes.E2ETests/E2ETestBase.cs`

**Intent**: Register `ITemplateService` in the E2E test harness's service
provider so that `NoteTreeViewModel` (and later `NoteEditorViewModel`) can
resolve it.

**Contract**: Add `services.AddSingleton<ITemplateService, TemplateService>();`
alongside the existing template-service registrations.

### Success Criteria:

#### Automated Verification:

- `dotnet test` passes.
- Existing `NoteTreeViewModel` template tests still pass.

#### Manual Verification:

- N/A

---

## Phase 3: Add editor "Insert from Template" command and view seam

### Overview

Add the command to `NoteEditorViewModel`, the event seam to `NoteEditorView`,
and the menu item in `MainWindow`.

### Changes Required:

#### 1. Editor VM command

**File**: `Notes/ViewModels/NoteEditorViewModel.cs`

**Intent**: Provide a command that runs the template service for body-only render
and asks the view to insert at the caret.

**Contract**:
- Add an `ITemplateService _templateService` field and constructor parameter.
- Add `[RelayCommand(CanExecute = nameof(IsEditing))] private async Task InsertFromTemplate()`.
- The command is a no-op when `_workspacePath` is null/empty; otherwise it
  awaits `_templateService.RenderForInsert(_workspacePath)` and, when a
  non-empty body is returned, calls `ApplyCaretInsert(body)`.
- Add `internal event Action<string>? InsertAtCaretRequested;` and
  `internal void ApplyCaretInsert(string body)` that invokes the event with the
  body (no-op when no handler is attached).

#### 2. View code-behind insert handler

**File**: `Notes/Views/NoteEditorView.axaml.cs`

**Intent**: Perform the actual AvaloniaEdit replace when the VM requests
insertion.

**Contract**:
- In `OnDataContextChanged`, unsubscribe `InsertAtCaretRequested` (and the
  existing `PropertyChanged` handler) from the previous VM and subscribe on
  the new VM, mirroring how `PropertyChanged` is already (un)wired there.
- The `InsertAtCaretRequested` handler is a no-op unless `_viewModel` is
  non-null and `IsEditing` is true.
- When active, it calls `Editor.Document.Replace(Editor.CaretOffset,
  Editor.SelectionLength, body)` — replacing the active selection (zero-length
  at the caret inserts without removing anything).
- Do **not** set `_suppressEvents` for this replace; it must propagate through
  the existing `OnEditorTextChanged` channel so autosave sees the change.

#### 3. MainWindow menu item

**File**: `Notes/MainWindow.axaml`

**Intent**: Surface the editor command in the File menu with a keyboard shortcut.

**Contract**: Add the "Insert from Template…" entry point consistently with the
existing menu items — i.e. a `KeyBinding` next to the other `Window.KeyBindings`
and a `MenuItem` under the `_File` menu next to the existing template/menu
entries, both bound to `Editor.InsertFromTemplateCommand` through the
`ViewModelLocator` `StaticResource`. Use the same `ReflectionBinding`-via-Locator
pattern the other editor-routed entries already follow (see comment about
DataContext hop in AGENTS.md). Suggested shortcut: `Ctrl+Shift+T`, input gesture
mirrored in the `MenuItem`'s `InputGesture`.

#### 4. NoteEditorViewModel tests update

**File**: `Notes.Tests/NoteEditorViewModelTests.cs`

**Intent**: Update the test setup to provide an `ITemplateService` stub and add
headless coverage of the command behavior.

**Contract**: Add `ITemplateService _templateService = Substitute.For<ITemplateService>();`
and pass it to the constructor. Add tests verifying:
- `InsertFromTemplateCommand` cannot execute when `IsEditing` is false.
- When a body is returned, `ApplyCaretInsert` fires the event.
- When the dialog is cancelled, no event is fired.

### Success Criteria:

#### Automated Verification:

- `dotnet test` passes.
- `NoteEditorViewModelTests` compile and pass.

#### Manual Verification:

- With a note open in editing mode, "Insert from Template" inserts the rendered
  body at the caret.
- With text selected, the selection is replaced by the rendered body.
- The existing note's frontmatter is unchanged.
- The command is disabled when no note is open or when previewing.

---

## Phase 4: Regression-check the menu path

### Overview

Verify that "New from Template" still creates a new note with the full rendered
text, including the template's non-form frontmatter.

### Changes Required:

- No code changes; this is verification only.

### Success Criteria:

#### Automated Verification:

- `dotnet test` passes.
- Existing `NoteTreeViewModelTests` for new-from-template pass.

#### Manual Verification:

- Using the menu "New from Template" creates a new note with the template's full
  rendered text.
- The new note retains the template's non-form frontmatter.
- Canceling the picker or form does not create a note.

---

## Testing Strategy

### Unit Tests:

- `RenderBody` behavior in `Notes.Core.Tests`.
- `NoteTreeViewModel` template flow via `TemplateService`.
- `NoteEditorViewModel` command enablement and `ApplyCaretInsert` event firing
  under a headless harness.

### Manual Testing Steps:

1. Open a note and place the caret in the editor.
2. Choose "Insert from Template" (`Ctrl+Shift+T`), select a template with body
   placeholders, fill the form.
3. Verify the rendered body is inserted at the caret.
4. Select text and repeat; verify the selection is replaced.
5. Verify the note's frontmatter is unchanged.
6. Switch to preview/empty state and verify the command is disabled.
7. Use "New from Template" and verify the new note still contains full
   frontmatter + body.

## Performance Considerations

- `RenderBody` is synchronous string manipulation; no performance concerns.
- The template picker/form dialogs are modal and block the UI; this matches
  existing behavior.
- The insert uses `Document.Replace` which is O(n) in document length; acceptable
  for note-sized documents.

## Migration Notes

- No data migration needed.
- No breaking changes to existing template files.

## References

- Frame brief: `context/changes/templates-insert-into-note/frame.md`
- Research: `context/changes/templates-insert-into-note/research.md`
- PRD: `context/foundation/prd.md` (FR-011, US-03)
- Related archived decision: `context/archive/2026-06-02-templates/change.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step
> lands. See `references/progress-format.md`.

### Phase 1: Add body-only render to Core template renderer

#### Automated

- [x] 1.1 `dotnet test --filter Notes.Core.Tests` passes — 2df3ca0
- [x] 1.2 New `RenderBody` tests pass — 2df3ca0

### Phase 2: Extract shared template orchestration into TemplateService

#### Automated

- [x] 2.1 `dotnet test` passes — 2abb6765
- [x] 2.2 Existing `NoteTreeViewModel` template tests pass — 2abb6765

### Phase 3: Add editor "Insert from Template" command and view seam

#### Automated

- [x] 3.1 `dotnet test` passes
- [x] 3.2 `NoteEditorViewModelTests` compile and pass

#### Manual

- [x] 3.3 Insert at caret works — 814d6b5
- [x] 3.4 Selection replace works — 814d6b5
- [x] 3.5 Existing note frontmatter unchanged — 814d6b5
- [x] 3.6 Command disabled when not editing — 814d6b5

### Phase 4: Regression-check the menu path

#### Automated

- [x] 4.1 `dotnet test` passes
- [x] 4.2 Existing new-from-template tests pass

#### Manual

- [x] 4.3 New from Template creates full note with frontmatter
- [x] 4.4 Canceling picker/form creates no note
