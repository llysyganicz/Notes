# Research: Avalonia headless UI E2E tests (minimal slice)

## Scope

Add Avalonia headless UI tests for the minimal slice:
1. Create new note end-to-end (toolbar → dialog → tree → editor).
2. Select note → edit → auto-save (tree → editor → file system).

## Existing test-plan position

`context/foundation/test-plan.md` §7 excludes "Full end-to-end GUI automation" on the belief that data-loss and correctness guarantees are reachable at unit/integration layers. The user has explicitly reversed this assumption for Avalonia headless UI tests. This change therefore updates §7 and §4 of the test plan as part of its final phase.

## Existing project state

- `Notes.E2ETests/` exists as an empty directory with stale `obj/` artifacts but **no `.csproj` file** and **not referenced in `Notes.slnx`**.
- `Notes.Tests/` already uses `Avalonia.Headless.XUnit` 12.0.3 with `[AvaloniaTestApplication(typeof(TestApp))]` (`Notes.Tests/TestApp.cs`).
- `Notes.Tests/Notes.Tests.csproj` is the reference pattern: xUnit v3 + Microsoft Testing Platform, `Avalonia.Headless.XUnit`, `System.IO.Abstractions.TestingHelpers`, `NSubstitute`.

## Application startup and DI

- `Program.cs` builds an `IServiceProvider` and assigns it to the static `App.Services`.
- `App.axaml.cs` resolves `MainWindow` from `App.Services`, shows it, then calls `MainWindowViewModel.InitializeAsync()`.
- `InitializeAsync()` calls `IFolderPicker.PickFolder()`. For headless tests, this must be faked to return a workspace path without showing a real folder picker.
- `ViewModelLocator` resolves VMs through `App.Services`. The test app must set `App.Services` before the main window is constructed.

## Relevant UI surfaces

### MainWindow

- `MainWindow.axaml` hosts `NoteTreeView`, `SearchView`, `GridSplitter`, `NoteEditorView`.
- Menu items and key bindings drive `MainWindowViewModel` commands (`NewNoteCommand`, etc.).

### NoteTreeView

- `TreeView` bound to `Root.Children`; `SelectedItem` two-way bound to `SelectedNode`.
- Context menu has `New Folder` and `Delete` commands.

### NewNoteDialog

- `Window` with `TextBox NameInput`, `TextBlock ErrorText`, `Button CreateButton`, `Button CancelButton`.
- Validation is synchronous in code-behind; `CreateButton.IsEnabled` reflects validation result.
- `NewNoteDialogService.PromptForName` gets `Application.Current.MainWindow` and calls `NewNoteDialog.Show(...)`.

### NoteEditorView

- Uses `AvaloniaEdit.TextEditor` named `Editor`.
- View code-behind wires `Editor.TextChanged` to `NoteEditorViewModel.OnEditorTextChanged`.
- `DataContextChanged` applies `LoadedText` to the editor.

### Auto-save

- `AutoSaveScheduler` uses a `DispatcherTimer` with 500 ms interval.
- `Bump()` restarts the timer; `Flush()` forces immediate save; `Cancel()` stops it.
- In headless tests, auto-save can be observed either by waiting for the timer tick or by calling `Flush()` indirectly. The test harness can expose a way to flush pending saves.

## Test harness requirements

1. **Test app class**: a headless `Application` subclass that sets `App.Services` to a test-specific provider before Avalonia initializes the main window. Must avoid using the real `App` type because `App` expects `App.Services` to be set by `Program.Main` and calls `AvaloniaXamlLoader.Load(this)` with real resources.
2. **Fake `IFolderPicker`**: returns a fixed path (e.g., `/workspace`).
3. **Fake `ISettingsService`**: returns empty settings so `InitializeAsync` triggers the picker path.
4. **`MockFileSystem`**: pre-seeded with workspace and any existing notes; used by `WorkspaceScanner`, `NoteFileService`, `NameValidator`, etc.
5. **`StrongReferenceMessenger`**: per-test messenger to avoid cross-test message leakage.
6. **Window helpers**: find controls by name, click buttons, set `TextBox` text, select `TreeViewItem`, read `TextEditor` text.
7. **Per-test isolation**: each test gets fresh services, fresh window, fresh file system.

## Risks specific to the headless layer

| Risk | Why unit/VM tests miss it | What a headless test proves |
|------|---------------------------|------------------------------|
| `x:DataType` / compiled binding mismatch | VM tests don't instantiate AXAML | Real control binds to VM property |
| `ViewModelLocator` resolves wrong VM or null | Design-mode fallback hides it | Real view gets real VM |
| `TextEditor.TextChanged` not wired to VM | VM tests call `OnEditorTextChanged` directly | Typing in control propagates |
| `NewNoteDialog` button/command binding broken | VM tests stub `INewNoteDialogService` | Dialog lifecycle works end-to-end |
| `TreeView.SelectedItem` two-way binding broken | VM tests set `SelectedNode` directly | Clicking tree item opens note |
| `AutoSaveScheduler` timer fires in UI thread | VM tests use stub scheduler | Real timer saves after delay |

## What stays in unit/integration tests

- Template parse/render correctness, file-safety guardrails, name validation, path containment remain in `Notes.Core.Tests` / `Notes.Tests`. Headless E2E only covers cross-control flows that can break at the UI boundary.

## References

- `Notes/Program.cs`
- `Notes/App.axaml.cs`
- `Notes/MainWindow.axaml`
- `Notes/Views/NoteTreeView.axaml`
- `Notes/Views/NewNoteDialog.axaml` + `.axaml.cs`
- `Notes/Views/NoteEditorView.axaml` + `.axaml.cs`
- `Notes/Services/AvaloniaFolderPicker.cs`
- `Notes/Services/NewNoteDialogService.cs`
- `Notes/Services/AutoSaveScheduler.cs`
- `Notes/ViewModels/MainWindowViewModel.cs`
- `Notes/ViewModels/NoteTreeViewModel.cs`
- `Notes/ViewModels/NoteEditorViewModel.cs`
- `Notes.Tests/TestApp.cs`
- `Notes.Tests/Notes.Tests.csproj`
- `Notes.Tests/Fakes/InMemoryNoteFileService.cs`
