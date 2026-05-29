# Create, Edit, and Preview Markdown Notes — Implementation Plan

## Overview

Deliver roadmap slice S-02: the user clicks a `.md` file in the tree and it opens in an AvaloniaEdit pane with markdown syntax highlighting; edits auto-save debounced to disk; `File → New Note` prompts for a filename and creates the file in the currently selected folder (or workspace root); `View → Preview` flips the content pane between editor and a Markdown.Avalonia-rendered preview of the same text.

Alongside the feature work, split the single `MainWindowViewModel` into three focused VMs (`MainWindowViewModel` for menu actions, `NoteTreeViewModel` for the tree, `NoteEditorViewModel` for the editor) communicating via `CommunityToolkit.Mvvm.Messaging.IMessenger`. The split is a prerequisite for adding new state to the editor without further bloating the shell VM.

## Current State Analysis

The codebase has shipped S-01:

- `MainWindowViewModel` is a single class holding `WorkspacePath`, `Root` (the `NoteTreeNode` hierarchy), and the commands `ChangeWorkspaceCommand`, `LoadTreeCommand`, `DeleteNoteCommand`, `ExitCommand`. Services for settings, scanner, tree builder, deleter, folder picker, confirm dialog are wired in `Program.cs` as singletons; ViewModels and `MainWindow` are transient.
- `MainWindow.axaml` is a `DockPanel` with a `Menu` (File → Change Notes Folder…, Exit) and a single `TreeView` bound to `Root.Children` filling the rest. No selection tracking; the delete action passes the bound node directly via the context menu's `CommandParameter`.
- `Notes.csproj` references Avalonia 12.0.3, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 9.0.16. `CommunityToolkit.Mvvm` already ships `WeakReferenceMessenger` and `IMessenger`, so introducing a messenger requires no new packages. No editor library, no markdown library yet.
- `Notes.Tests` runs xUnit against `net10.0` with tests for `SettingsService`, `WorkspaceScanner`, `NoteTreeBuilder`.
- `Notes/CLAUDE.md` mandates MVVM with code-behind only for UI wiring, compiled bindings (`x:DataType`), composition over inheritance, no `Async` suffix unless there's a sync sibling, test method naming `Method_WhenScenario_ExpectedBehaviour`. The compose-via-services rule is satisfied because the new VMs collaborate through `IMessenger`, not a base class.

What's missing for S-02: opening a file's content, an editor control, a markdown renderer, auto-save plumbing, new-note creation, preview toggling, tracking which tree node is selected, AND the VM split that keeps the editor's new state out of `MainWindowViewModel`.

## Desired End State

After this slice ships:

1. Selecting a `.md` file in the tree opens its content in the editor pane (right of the tree). Folder nodes don't open anything.
2. The editor highlights markdown syntax (headings, emphasis, code blocks, lists, links).
3. Typing in the editor schedules a debounced save (~500 ms idle); the latest text persists to disk as plain UTF-8. Switching notes flushes any pending save before loading the next file.
4. `File → New Note…` (Ctrl+N) opens a dialog asking for a filename; on confirm, a new empty `.md` file is created in the currently-selected folder (or workspace root if no folder is selected), the tree refreshes, the new file is selected, and the editor opens it ready to type. The dialog rejects empty names, names with path separators, names that resolve to an already-existing file, and silently appends `.md` if the user omits it.
5. `View → Preview` (Ctrl+E) toggles the content area between the editor and a Markdown.Avalonia view rendering the same text. Toggling back returns to the editor with the document text intact. Preview is off by default whenever a new note is opened.
6. When no note is selected, the content area shows a centered "Select a note to edit" hint.
7. The single S-01 `MainWindowViewModel` has been split into three VMs that communicate via `IMessenger`. No direct references between sibling VMs. Each VM is constructable and testable in isolation.
8. `dotnet build` succeeds; `dotnet test` is green; new unit tests cover the note file service (read/write, encoding, missing-file handling), the new-note name validation logic, and the routing of key messages.

### Key Discoveries

- **CommunityToolkit.Mvvm 8.x** ships `WeakReferenceMessenger` and the `IMessenger` abstraction (`CommunityToolkit.Mvvm.Messaging`). Recipients implement `IRecipient<TMessage>` and register via `messenger.RegisterAll(this)` (or per-message `Register<TMessage>`). Messages are arbitrary records. We register `IMessenger` in DI as a singleton resolving to `WeakReferenceMessenger.Default` (or a fresh instance — see Critical Implementation Details below for the choice and rationale).
- **AvaloniaEdit** (NuGet `Avalonia.AvaloniaEdit` 11.x line; check the 12-compatible package id at install time) is the standard text editor control for Avalonia. Bundles markdown syntax via `Markdown.xshd` accessible from `HighlightingManager.Instance.GetDefinition("MarkDown")`. Exposes `TextEditor.Document` (an `AvaloniaEdit.Document.TextDocument`) — bindable but not via the source-generator pattern because `Document.Text` is not a `BindingBase`. The UserControl's code-behind handles the document/text bridge.
- **Markdig** (NuGet `Markdig`) is the de facto .NET markdown parser; `Markdown.Avalonia` (NuGet `Markdown.Avalonia`) wraps Markdig and renders to native Avalonia controls via its `MarkdownScrollViewer` control. The `Markdown` (text) property is a regular Avalonia direct property — fine for one-way binding from the editor VM.
- `TreeView` in Avalonia 12 supports `SelectedItem` (object) binding — `TwoWay` works if we expose `SelectedNode` on the tree VM. The S-01 layout puts the entire tree in the content area; for S-02 we extract the tree into its own UserControl and re-host it inside a `Grid` with a `GridSplitter` next to the editor UserControl.
- `DispatcherTimer` (Avalonia) is the simplest way to schedule the debounced auto-save on the UI thread without async/cancellation gymnastics. The "real" save call (`File.WriteAllText`) runs synchronously on the UI thread — file is small (single note) and PRD `data_volume: small`, so blocking ms-scale I/O is acceptable.
- `Path.GetInvalidFileNameChars()` plus an explicit `/` / `\` rejection covers the new-note-name validation portably.
- The S-01 `ConfirmDialog` is the model for a custom modal `Window` opened with `ShowDialog(owner)` returning a `Task<bool>` — `NewNoteDialog` follows the same pattern but returns `Task<string?>` (the entered name) instead.

## What We're NOT Doing

- **No rename or move of existing notes** — S-01 explicitly excluded these. Out of scope for S-02.
- **No file-system watcher** — external edits to the workspace don't refresh the open editor or tree. Save-conflict detection is deferred.
- **No multi-document tabs** — one editor pane, one open note at a time.
- **No live preview side-by-side** — preview toggles in place (one or the other), per user choice.
- **No syntax-highlighting customization** — the bundled AvaloniaEdit `MarkDown.xshd` is used as-is.
- **No raw HTML, LaTeX, Mermaid in preview** — Markdown.Avalonia renders CommonMark + Markdig extensions only. Mermaid parked (user confirmed not important for MVP).
- **No undo across save boundaries** — AvaloniaEdit's built-in undo stack is per-document and resets when the document changes.
- **No persisted last-opened note across launches** — empty hint state on every launch.
- **No empty-folder pruning when the new note's parent doesn't exist** — the dialog only lists names; the parent is always the selected folder (which exists by definition) or workspace root.
- **No keyboard shortcut configuration** — Ctrl+N and Ctrl+E are hard-coded in AXAML.
- **No "central state" service** — `WorkspacePath` lives in `MainWindowViewModel` and propagates to siblings via `WorkspaceChangedMessage`. Sibling VMs cache the path locally.

## Implementation Approach

Three phases, each ending in a verifiable state:

- **Phase 1** restructures the existing `MainWindowViewModel` into three VMs (`MainWindowViewModel`, `NoteTreeViewModel`, `NoteEditorViewModel`), introduces the messenger, extracts the tree into a UserControl, and adds the new editor UserControl with the open/edit/auto-save behavior. End state: S-01 features still work and the user can open + edit + auto-save notes. The architecture is the foundation for Phases 2–3.
- **Phase 2** adds the New Note flow on top of the new architecture: `MainWindowViewModel` publishes a `NewNoteRequestedMessage`; `NoteTreeViewModel` consumes it and runs the create flow (it owns `SelectedNode` and so knows the target folder).
- **Phase 3** adds the preview toggle: `MainWindowViewModel` publishes a `TogglePreviewRequestedMessage`; `NoteEditorViewModel` consumes it and flips its `PaneState`. The Markdown.Avalonia control already sits in the editor UserControl (added in Phase 1 as a hidden child).

## Critical Implementation Details

- **Messenger lifetime and instance.** Register `IMessenger` in DI as a singleton resolving to `WeakReferenceMessenger.Default`. Using `Default` (CommunityToolkit's static singleton) is acceptable here because the app runs a single instance of each VM; tests inject either `Default` or a fresh `WeakReferenceMessenger()` per test to avoid cross-test bleed. The `WeakReference` flavor (rather than the strong-reference `Messenger`) avoids retention pitfalls when VMs are recreated.
- **Recipient registration.** Each VM implements `IRecipient<TMessage>` for every message it consumes and calls `_messenger.RegisterAll(this)` in its constructor. CommunityToolkit picks up all `IRecipient<T>` implementations and registers them in one shot.
- **Initial workspace propagation.** On startup, `App.axaml.cs` calls `MainWindowViewModel.InitializeAsync()` (a new method replacing the inline `Start` orchestration). That method loads settings, handles the first-launch picker, persists, and then publishes a single `WorkspaceChangedMessage` once a valid workspace is known. Both child VMs are constructed before this happens — they're already registered as recipients and will react to the message. No special "startup" message; the same `WorkspaceChangedMessage` covers cold start AND user-initiated workspace change.
- **Tree selection drives the editor via the messenger, not a direct property.** `NoteTreeViewModel.OnSelectedNodeChanged` calls `_messenger.Send(new NoteSelectedMessage(value))`. `NoteEditorViewModel.Receive(NoteSelectedMessage m)` handles it: flush any pending auto-save, then load the new file (or clear if folder/null). The editor never sees the tree.
- **Auto-save debounce timing & wiring.** `DispatcherTimer` with `Interval = 500ms`; `Bump()` does `Stop()` then `Start()`. On `Tick`, stops the timer and raises `OnSave`. The editor VM subscribes to `OnSave` once at construction so the save handler is fixed for the scheduler's lifetime; the "what to save" (current path + text) is read from the VM's own state inside the handler. On note switch or app close, the VM calls `_scheduler.Flush()` which fires `OnSave` immediately if pending. Picked 500 ms because it's short enough that "what's on disk" is fresh and long enough to coalesce burst typing.
- **AvaloniaEdit binding pattern (UserControl code-behind).** `TextEditor.Document.Text` is not directly bindable. The cleanest path: `NoteEditorView.axaml.cs` (allowed per CLAUDE.md as UI wiring) subscribes to `Editor.TextChanged` and forwards to `ViewModel.OnEditorTextChanged(string)`; when `ViewModel.LoadedText` changes (via `INotifyPropertyChanged`), the code-behind copies it into `Editor.Document.Text` in a `PropertyChanged` handler. Both sides use a re-entrancy guard (`_suppressEvents` flag) so the document mutation triggered by loading doesn't cycle back through the auto-save scheduler.
- **Preview rendering uses the live editor text, not the on-disk text.** When the editor VM receives `TogglePreviewRequestedMessage` while in `Editing` state, it grabs the current `_currentEditorText` (the in-memory copy the debounce hasn't flushed yet) and sets `PreviewText = _currentEditorText`. The `MarkdownScrollViewer.Markdown` property binds one-way to `PreviewText`. Toggling back to editor doesn't re-read disk — the editor's `Document` already holds the same text.
- **Empty / folder / file states.** A single enum (`EditorPaneState`: `Empty | Editing | Previewing`) drives template selection in the editor UserControl. The control hosts three children with `IsVisible` bound to computed `IsEmpty` / `IsEditing` / `IsPreviewing` boolean properties. Switching states is just an enum assignment.
- **Dialog service granularity — one interface per dialog.** `IConfirmDialogService` (S-01) and `INewNoteDialogService` (Phase 2) each expose a single, domain-specific method rather than being merged into a shared `IDialogService`. `Confirm(title, message): Task<bool>` is a true general primitive (reusable for any yes/no question); `PromptForName(parent, validate): Task<string?>` is highly domain-specific (knows parent folders, name validation, `.md` extension). Bundling them would widen the interface, violate ISP, and make test fakes pay for unused methods. Future dialogs (e.g. S-04's template picker and field forms) get their own focused services the same way. A true generic text-prompt primitive can be introduced separately (`ITextPromptDialogService`) without disturbing existing services if a use case appears.
- **"Currently selected folder" resolution for new notes.** Lives in `NoteTreeViewModel`. If `SelectedNode?.Kind == Folder`, use its `RelativePath`. If `SelectedNode?.Kind == File`, walk up to the parent folder by trimming the last segment from `RelativePath` (or `""` if no separator). If `SelectedNode == null`, use `""` (workspace root).
- **DataContext composition via `ViewModelLocator` — sibling VMs are fully decoupled.** `MainWindowViewModel` does NOT hold references to `NoteTreeViewModel` or `NoteEditorViewModel`. A `ViewModelLocator` class exposes the three VMs as properties (`Main`, `Tree`, `Editor`), each resolved from `App.Services` on access. The locator is registered as an `Application.Resources` entry in `App.axaml` (`<vm:ViewModelLocator x:Key="Locator" />`). Each view (`MainWindow.axaml`, `NoteTreeView.axaml`, `NoteEditorView.axaml`) sets its own `DataContext` via XAML: `DataContext="{Binding <PropName>, Source={StaticResource Locator}}"`. Code-behind has no service-locator calls — constructors only run `InitializeComponent()` (plus, for the editor, the AvaloniaEdit-document wiring). Each view declares its own `x:DataType` for compiled bindings. The locator returns `null` when `Design.IsDesignMode` is true so the AXAML designer renders without exceptions. **DI is still the only place that wires view types to VM types** — change the registration in `Program.cs` and the locator returns a different VM instance, without touching any view or sibling VM.
- **VM lifetime: all three singletons.** `MainWindowViewModel`, `NoteTreeViewModel`, and `NoteEditorViewModel` are registered as singletons (deliberate departure from S-01's transient VM convention) so (a) the `IMessenger` recipient registration done in each constructor is stable for the app's lifetime, and (b) the `ViewModelLocator` properties return the same instance on every access (no per-binding-evaluation re-construction).
- **Startup ordering for messenger registration.** Sequence: (1) `App.OnFrameworkInitializationCompleted` resolves `MainWindow` from DI; (2) the `MainWindow` constructor runs `InitializeComponent()`, which loads the XAML; (3) the window-root `DataContext` binding evaluates the locator's `Main` property, which resolves `MainWindowViewModel` from DI (constructed and registered as a messenger recipient); (4) the XAML loader instantiates the two child UserControls, each of which has its own `DataContext` locator binding that resolves and constructs its child VM (also registered as recipients); (5) only then does `App` call `await viewModel.InitializeAsync()`, which publishes the initial `WorkspaceChangedMessage`. All three VMs are already registered as recipients by step 4, so the initial broadcast reaches both children.

## Phase 1: VM split + editor pane with auto-save

### Overview

Three concerns landing in one phase because the editor VM is new code anyway, and pure-refactor phases are hard to verify (the only manual test is "S-01 didn't break"). After this phase: the existing S-01 behavior is preserved with the new VM/messenger structure, AND the user can open / edit / auto-save notes.

### Changes Required

#### 1. Add editor and markdown packages

**File**: `Notes/Notes.csproj`

**Intent**: Pull in AvaloniaEdit, Markdig, and Markdown.Avalonia. Markdown.Avalonia is added in Phase 1 because the editor UserControl already accommodates the preview slot; Phase 3 just makes the control visible.

**Contract**: Add three `<PackageReference>` entries — `Avalonia.AvaloniaEdit` (latest 12-compatible release), `Markdig` (latest stable), `Markdown.Avalonia` (latest 12-compatible release). Keep all existing references untouched.

#### 2. Message records

**File**: `Notes/Messaging/Messages.cs` (new, single file containing all message records)

**Intent**: Define the five messages used across the VM boundary. Records, no behavior, immutable.

**Contract**:
- `public sealed record WorkspaceChangedMessage(string WorkspacePath);`
- `public sealed record NoteSelectedMessage(NoteTreeNode? Node);`
- `public sealed record NoteDeletedMessage(string RelativePath);`
- `public sealed record NewNoteRequestedMessage;` (Phase 2 introduces consumers; the record can be declared in Phase 1 alongside the others to keep all messages in one place, or deferred — author's choice. Recommended: declare all five up front for a single PR-friendly diff.)
- `public sealed record TogglePreviewRequestedMessage;` (same — Phase 3 consumer.)

Namespace: `Notes.Messaging`.

#### 3. Note file service

**File**: `Notes/Services/INoteFileService.cs` + `Notes/Services/NoteFileService.cs` (new)

**Intent**: Read and write a note's text content from/to an absolute path. Centralizes UTF-8 (no BOM) and keeps file I/O out of the ViewModel.

**Contract**:
- `interface INoteFileService { string Read(string absolutePath); void Save(string absolutePath, string text); }`
- `Read` returns UTF-8 file contents. If the file does not exist, returns `string.Empty` (no throw). Other read failures propagate.
- `Save` writes via `File.WriteAllText(absolutePath, text, new UTF8Encoding(false))`. Throws on failure. Does not create missing parent directories.

#### 4. Auto-save debouncer

**File**: `Notes/Services/IAutoSaveScheduler.cs` + `Notes/Services/AutoSaveScheduler.cs` (new)

**Intent**: Raise an `OnSave` event ~500 ms after the last `Bump()` call; `Flush()` fires the event immediately if pending; `Cancel()` drops a pending tick without firing.

**Contract**:
- `interface IAutoSaveScheduler { event Action OnSave; void Bump(); void Flush(); void Cancel(); }`
- Implementation uses `DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }`. `Bump()` calls `Stop()` then `Start()` (restart the debounce). On `Tick`, calls `Stop()` then raises `OnSave`. `Flush()`: if `IsEnabled`, stop the timer and raise `OnSave` immediately. `Cancel()` stops the timer without raising.
- The save callback is fixed for the scheduler's lifetime: the editor VM subscribes once at construction (`scheduler.OnSave += DoSave;`). The scheduler is purpose-built for "tell me when the debounce period elapses" and intentionally knows nothing about which file is being saved — the consumer reads its own state when the event fires.
- Singleton (the app has one editor pane).

#### 5. New `NoteTreeViewModel`

**File**: `Notes/ViewModels/NoteTreeViewModel.cs` (new)

**Intent**: Own the tree's state (Root, SelectedNode), the load-tree action, the delete-note command. Consume `WorkspaceChangedMessage` (reload). Publish `NoteSelectedMessage` (on selection change) and `NoteDeletedMessage` (after a successful delete).

**Contract**:
- Inherits `ObservableObject`, implements `IRecipient<WorkspaceChangedMessage>` and (Phase 2 adds) `IRecipient<NewNoteRequestedMessage>`.
- Constructor: `(IMessenger messenger, IWorkspaceScanner scanner, NoteTreeBuilder treeBuilder, INoteDeleter deleter, IConfirmDialogService confirmDialog)`. Calls `_messenger.RegisterAll(this)` at the end.
- Internal state: `private string? _workspacePath` cached from the latest `WorkspaceChangedMessage`.
- `[ObservableProperty] NoteTreeNode? root` — bound to `TreeView.ItemsSource` via `Root.Children`.
- `[ObservableProperty] NoteTreeNode? selectedNode` — bound `TwoWay` to `TreeView.SelectedItem`. `partial void OnSelectedNodeChanged(NoteTreeNode? value)` sends `_messenger.Send(new NoteSelectedMessage(value))`.
- `Receive(WorkspaceChangedMessage m)` sets `_workspacePath = m.WorkspacePath`, clears `SelectedNode`, then triggers `LoadTreeCommand`.
- `[RelayCommand] Task LoadTree()` — rebuilds `Root` from `_workspacePath` (returns synchronously if null/empty; sets `Root = null`).
- `[RelayCommand(CanExecute = nameof(CanDeleteNote))] async Task DeleteNote(NoteTreeNode? node)` — same shape as today, plus: after the file is deleted, publish `_messenger.Send(new NoteDeletedMessage(node.RelativePath))`, then refresh the tree. `CanDeleteNote(node) => node?.Kind == NoteNodeKind.File`.

#### 6. New `NoteEditorViewModel`

**File**: `Notes/ViewModels/NoteEditorViewModel.cs` (new)

**Intent**: Own the editor's state (LoadedText, PaneState, PreviewText, CurrentNote), the auto-save flow, and the (Phase 3) preview-toggle handling. Consume `NoteSelectedMessage`, `NoteDeletedMessage`, `WorkspaceChangedMessage`, `TogglePreviewRequestedMessage`. No commands — all behavior is message-driven, so the menu doesn't bind directly to this VM.

**Contract**:
- Inherits `ObservableObject`. Implements `IRecipient<NoteSelectedMessage>`, `IRecipient<NoteDeletedMessage>`, `IRecipient<WorkspaceChangedMessage>`, and (Phase 3) `IRecipient<TogglePreviewRequestedMessage>`.
- Constructor: `(IMessenger messenger, INoteFileService fileService, IAutoSaveScheduler scheduler)`. Calls `_messenger.RegisterAll(this)` at the end.
- Internal state: `private string? _workspacePath`, `private NoteTreeNode? _currentNote`, `private string _currentEditorText = ""`.
- `[ObservableProperty] string loadedText` — copied into the AvaloniaEdit document by the UserControl's code-behind on change. Default `""`.
- `[ObservableProperty] EditorPaneState paneState` — `Empty | Editing | Previewing`. Default `Empty`. Computed `IsEmpty` / `IsEditing` / `IsPreviewing` raised via `OnPaneStateChanged`.
- `[ObservableProperty] string previewText` — bound to `MarkdownScrollViewer.Markdown` (Phase 3 makes the control visible). Default `""`.
- `Receive(WorkspaceChangedMessage m)` — flush any pending save, set `_workspacePath = m.WorkspacePath`, `_currentNote = null`, `LoadedText = ""`, `_currentEditorText = ""`, `PaneState = EditorPaneState.Empty`.
- `Receive(NoteSelectedMessage m)` — flush pending save; if `m.Node == null` or `m.Node.Kind == Folder`, set state to Empty (clear text + current note); if `m.Node.Kind == File` and `_workspacePath` is set, read content via `_fileService.Read(absolutePath)`, set `LoadedText = content`, `_currentEditorText = content`, `_currentNote = m.Node`, `PaneState = EditorPaneState.Editing`.
- `Receive(NoteDeletedMessage m)` — if `_currentNote?.RelativePath == m.RelativePath`, cancel any pending save, clear state (Empty).
- `Receive(TogglePreviewRequestedMessage m)` — Phase 3 implementation: if `PaneState == Editing`, `PreviewText = _currentEditorText; PaneState = Previewing`; if `PaneState == Previewing`, `PaneState = Editing`; if `Empty`, no-op.
- `void OnEditorTextChanged(string text)` (called from the UserControl code-behind) — set `_currentEditorText = text`; if `_currentNote != null`, call `_scheduler.Bump()`. The constructor subscribes once: `scheduler.OnSave += DoSave;`. Private `DoSave()` no-ops if `_currentNote == null` or `_workspacePath == null`; otherwise resolves the absolute path from `_workspacePath` + `_currentNote.RelativePath` and calls `_fileService.Save(absolutePath, _currentEditorText)`.

**File**: `Notes/Models/EditorPaneState.cs` (new)

**Intent**: Enum that drives the editor pane's visible child.

**Contract**: `public enum EditorPaneState { Empty, Editing, Previewing }`.

#### 7. Slimmer `MainWindowViewModel`

**File**: `Notes/ViewModels/MainWindowViewModel.cs`

**Intent**: Reduce to: menu commands (`ChangeWorkspaceCommand`, `NewNoteCommand`, `TogglePreviewCommand`, `ExitCommand`) and the startup `InitializeAsync` orchestration that publishes the initial `WorkspaceChangedMessage`. Remove `Root`, `SelectedNode`, `LoadTreeCommand`, `DeleteNoteCommand` — moved to `NoteTreeViewModel`. **No references to sibling VMs** — `MainWindowViewModel` does not know `NoteTreeViewModel` or `NoteEditorViewModel` exist. Each UserControl gets its DataContext via the `ViewModelLocator` (see §9, §10, §11).

**Contract**:
- Inherits `ObservableObject`.
- Constructor: `(IMessenger messenger, ISettingsService settings, IFolderPicker folderPicker)`. No child-VM parameters.
- `[ObservableProperty] string? workspacePath` — kept here for window title binding; updated in `InitializeAsync` and `ChangeWorkspaceCommand` after a successful pick.
- `[RelayCommand] async Task ChangeWorkspace()` — calls `_folderPicker.PickFolder()`; on success: `_settings.Save(new AppSettings(picked))`, `WorkspacePath = picked`, `_messenger.Send(new WorkspaceChangedMessage(picked))`.
- `[RelayCommand] void NewNote()` — publishes `_messenger.Send(new NewNoteRequestedMessage())`. (Phase 2 wires the consumer; in Phase 1 nothing receives it, which is fine for a no-op.)
- `[RelayCommand] void TogglePreview()` — publishes `_messenger.Send(new TogglePreviewRequestedMessage())`. (Phase 3 wires the consumer.)
- `[RelayCommand] void Exit()` — unchanged from S-01.
- `public async Task InitializeAsync()` — replaces the `Start` method that lives in `App.axaml.cs` today. Loads settings; if `WorkspacePath` is set but the folder is gone, clear it; if no workspace, prompt the picker (return `false` on cancel so `App` can shut down); otherwise persist and broadcast `WorkspaceChangedMessage`.

#### 8. `App.axaml.cs` orchestration

**File**: `Notes/App.axaml.cs`

**Intent**: Simpler `OnFrameworkInitializationCompleted` — resolve `MainWindow` from DI, show it, then call `InitializeAsync` on the VM. DataContext is set by the locator binding inside `MainWindow.axaml`, not in code-behind.

**Contract**:
- Resolve `MainWindow` from DI (DataContext is established by the XAML locator binding during `InitializeComponent()`); show the window. Then resolve `MainWindowViewModel` from DI (returns the same singleton the locator already produced) and `_ = viewModel.InitializeAsync().ContinueWith(t => { if (t.Result == false) desktop.Shutdown(0); }, TaskScheduler.FromCurrentSynchronizationContext());` (or equivalent async-void wrapper). `InitializeAsync` returns `bool` (`true` = workspace ready; `false` = user cancelled first-launch picker).
- **Remove** the explicit `window.DataContext = viewModel;` line from S-01 — the locator binding now owns this.
- Keep the `MainWindow` accessor property used by dialog services (unchanged).

#### 9. ViewModelLocator and App.axaml resource registration

**File**: `Notes/ViewModels/ViewModelLocator.cs` (new)

**Intent**: Centralized resolver that exposes each VM as a property for XAML to consume via `{StaticResource Locator}`. Replaces the constructor-side service-locator pattern. Lives in the `Notes.ViewModels` namespace alongside the VMs.

**Contract**:
- `public sealed class ViewModelLocator` with three public read-only properties:
  - `MainWindowViewModel? Main => Resolve<MainWindowViewModel>();`
  - `NoteTreeViewModel? Tree => Resolve<NoteTreeViewModel>();`
  - `NoteEditorViewModel? Editor => Resolve<NoteEditorViewModel>();`
- Private static helper: `private static T? Resolve<T>() where T : class => Design.IsDesignMode ? null : App.Services.GetRequiredService<T>();` (uses `Avalonia.Controls.Design.IsDesignMode`). Returns null at design time so the Avalonia previewer renders bindings as no-ops rather than crashing on a missing service provider.
- Properties are computed on each access; because the VMs are DI singletons (§13), repeated accesses return the same instance.
- Not registered in DI. The locator is instantiated by the XAML loader when `App.axaml` resolves the `Application.Resources` entry.

**File**: `Notes/App.axaml`

**Intent**: Register the locator as a static application resource so every view can reference it via `{StaticResource Locator}`.

**Contract**: Add a namespace alias for the locator (`xmlns:vm="using:Notes.ViewModels"`) and an `<Application.Resources>` block at the root containing `<vm:ViewModelLocator x:Key="Locator" />`. No other change to `App.axaml`.

#### 10. Tree UserControl extraction

**File**: `Notes/Views/NoteTreeView.axaml` + `Notes/Views/NoteTreeView.axaml.cs` (new)

**Intent**: Move the `TreeView` markup out of `MainWindow.axaml` into a self-contained UserControl. The UserControl's DataContext is resolved by AXAML through the `ViewModelLocator`.

**Contract**:
- Root element: `<UserControl ... x:DataType="vm:NoteTreeViewModel" DataContext="{Binding Tree, Source={StaticResource Locator}}">`.
- Body: the existing `TreeView` markup, with `SelectedItem="{Binding SelectedNode, Mode=TwoWay}"` added. The context menu's command binding still needs the `$parent[TreeView]` escape because inside a `TreeDataTemplate` the local DataContext is the bound `NoteTreeNode`, not the UserControl. Only the cast type changes from `MainWindowViewModel` to `NoteTreeViewModel`:
  ```xml
  Command="{Binding $parent[TreeView].((vm:NoteTreeViewModel)DataContext).DeleteNoteCommand}"
  CommandParameter="{Binding}"
  ```
  The bound node is still passed via `CommandParameter="{Binding}"` from the template's data context.
- Code-behind: empty beyond `InitializeComponent()`. No service-locator calls.

#### 11. Editor UserControl

**File**: `Notes/Views/NoteEditorView.axaml` + `Notes/Views/NoteEditorView.axaml.cs` (new)

**Intent**: Host the empty hint, the AvaloniaEdit instance, and the (Phase 3) `MarkdownScrollViewer`, switching visibility via `IsEmpty` / `IsEditing` / `IsPreviewing`. Bridge the AvaloniaEdit document to `ViewModel.LoadedText` / `OnEditorTextChanged`. The UserControl's DataContext is resolved by AXAML through the `ViewModelLocator`.

**Contract**:
- Root element: `<UserControl ... x:DataType="vm:NoteEditorViewModel" xmlns:edit="https://github.com/avaloniaui/avaloniaedit" xmlns:md="https://github.com/whistyun/Markdown.Avalonia" DataContext="{Binding Editor, Source={StaticResource Locator}}">`.
- Body: a `Panel` (or `Grid`) hosting three children with `IsVisible` bindings:
  - `TextBlock` (centered, "Select a note to edit") visible when `IsEmpty`.
  - `edit:TextEditor x:Name="Editor" ShowLineNumbers="True" WordWrap="True"` visible when `IsEditing`.
  - `md:MarkdownScrollViewer Markdown="{Binding PreviewText}"` visible when `IsPreviewing`. Added in Phase 1 (always invisible until Phase 3 sets `PaneState = Previewing`) so Phase 3 only adds menu wiring and the `Receive` handler.
- Code-behind: parameterless constructor calls `InitializeComponent()`. Sets `Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("MarkDown")` after `InitializeComponent`. Overrides `OnDataContextChanged(EventArgs e)` — when DataContext becomes a non-null `NoteEditorViewModel`, subscribes to its `PropertyChanged`. Subscribes to `Editor.TextChanged`: if `!_suppressEvents`, call `((NoteEditorViewModel)DataContext!).OnEditorTextChanged(Editor.Document.Text)`. In the VM `PropertyChanged` handler: when `LoadedText` changes, set `_suppressEvents = true`, copy `ViewModel.LoadedText` into `Editor.Document.Text`, `_suppressEvents = false`. Re-entrancy guard prevents the load→change→schedule-save loop. **No service-locator calls** — DataContext arrives via the XAML locator binding.

#### 12. `MainWindow.axaml` layout update

**File**: `Notes/MainWindow.axaml`

**Intent**: Two-column body: tree UserControl on the left, editor UserControl on the right, `GridSplitter` between. The window's own DataContext is resolved through the `ViewModelLocator`. Menu structure unchanged in Phase 1 (Phase 2 adds New Note, Phase 3 adds View menu).

**Contract**:
- Root element gains `DataContext="{Binding Main, Source={StaticResource Locator}}"` (and keeps `x:DataType="vm:MainWindowViewModel"`). The previous `App.axaml.cs` line that set `window.DataContext = viewModel` is removed (see §8).
- Replace the existing `TreeView` (full-width child of `DockPanel`) with a `Grid` having `ColumnDefinitions="*,Auto,2*"`: column 0 = `<views:NoteTreeView />`, column 1 = `<GridSplitter Width="4" />`, column 2 = `<views:NoteEditorView />`. **No `DataContext` attribute on either UserControl** — each control's AXAML sets its own via the locator binding.
- Window-level menu items bind to `MainWindowViewModel` commands (default DataContext is `MainWindowViewModel` via the locator).
- Window `Title` binding still `{Binding WorkspacePath, ...}` against `MainWindowViewModel`.

**File**: `Notes/MainWindow.axaml.cs`

**Intent**: Unchanged from S-01 (empty beyond `InitializeComponent()`).

**Contract**: No change.

#### 13. DI registration updates

**File**: `Notes/Program.cs`

**Intent**: Register the messenger, the two new services, and switch all three VMs to **singleton** lifetime (deliberate departure from S-01's transient VM convention). The single instances guarantee (a) stable `IMessenger` recipient registration and (b) idempotent `ViewModelLocator` property access. Neither the `ViewModelLocator` nor the UserControls are registered in DI — the locator is a static AXAML resource that calls into `App.Services` on demand; UserControls are instantiated by the XAML loader.

**Contract**:
- `services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);`
- `services.AddSingleton<INoteFileService, NoteFileService>();`
- `services.AddSingleton<IAutoSaveScheduler, AutoSaveScheduler>();`
- `services.AddSingleton<NoteTreeViewModel>();`
- `services.AddSingleton<NoteEditorViewModel>();`
- Change `services.AddTransient<MainWindowViewModel>();` → `services.AddSingleton<MainWindowViewModel>();` (also picks up the constructor signature change automatically — no longer takes child VMs).
- `MainWindow` registration unchanged (still transient).

#### 14. Unit tests

**File**: `Notes.Tests/NoteFileServiceTests.cs` (new)

**Intent**: Cover (a) `Read` of missing file → `string.Empty`, (b) `Read` of existing UTF-8 file → exact content, (c) `Save` writes UTF-8 without BOM, (d) round-trip, (e) `Save` overwrites existing content.

**Contract**: Per-test temp directory (same pattern as `SettingsServiceTests`). Method names follow `Method_WhenScenario_ExpectedBehaviour`: `Read_WhenFileMissing_ReturnsEmpty`, `Read_WhenFileExists_ReturnsContent`, `Save_WhenCalled_WritesUtf8WithoutBom`, `Save_WhenCalledTwice_OverwritesContent`, `Save_WhenFollowedByRead_RoundtripsContent`.

**File**: `Notes.Tests/NoteTreeViewModelTests.cs` (new)

**Intent**: Cover the message-driven flow at the tree boundary: (a) on `WorkspaceChangedMessage` the tree loads from the new path, (b) on `SelectedNode` change a `NoteSelectedMessage` is published, (c) on `DeleteNote` a `NoteDeletedMessage` is published and the tree refreshes.

**Contract**: Use a real `WeakReferenceMessenger` instance per test (no globals). Fake `IWorkspaceScanner`, `INoteDeleter`, `IConfirmDialogService`. Method names follow the convention: `Receive_WhenWorkspaceChangedMessage_LoadsTree`, `OnSelectedNodeChanged_WhenSet_PublishesNoteSelectedMessage`, `DeleteNote_WhenConfirmed_PublishesNoteDeletedMessage`, `DeleteNote_WhenConfirmed_RefreshesTree`.

**File**: `Notes.Tests/NoteEditorViewModelTests.cs` (new)

**Intent**: Cover the message-driven flow at the editor boundary: (a) on `WorkspaceChangedMessage` state is reset, (b) on `NoteSelectedMessage` with a file the content is loaded and `PaneState = Editing`, (c) on `NoteSelectedMessage` with a folder/null `PaneState = Empty`, (d) on `NoteDeletedMessage` matching the current note state is cleared, (e) `OnEditorTextChanged` calls `Bump()` on the scheduler, (f) `OnSave` firing persists the current editor text via the file service.

**Contract**: Fresh `WeakReferenceMessenger` per test. Fake `INoteFileService` and `IAutoSaveScheduler` (the scheduler fake records `Bump`/`Flush`/`Cancel` calls and exposes a method to raise `OnSave` deterministically — no real timer). Method names: `Receive_WhenNoteSelectedMessageHasFile_LoadsContentAndSetsEditing`, `Receive_WhenNoteSelectedMessageHasFolder_ClearsState`, `Receive_WhenNoteDeletedMessageMatchesCurrent_ClearsState`, `Receive_WhenNoteDeletedMessageDoesNotMatchCurrent_LeavesStateUnchanged`, `Receive_WhenWorkspaceChangedMessage_ResetsState`, `OnEditorTextChanged_WhenCurrentNoteSet_BumpsScheduler`, `OnSave_WhenRaised_PersistsCurrentEditorTextToFile`, `OnSave_WhenRaisedWithNoCurrentNote_DoesNothing`.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with no new warnings beyond baseline
- `dotnet test` is green; new `NoteFileServiceTests`, `NoteTreeViewModelTests`, and `NoteEditorViewModelTests` cases pass alongside the existing S-01 suite
- The app starts and resolves the new singletons + child VMs without DI errors

#### Manual Verification

- S-01 regressions: workspace picker still appears on first launch / stale-path recovery; tree still renders nested `.md` files; `File → Change Notes Folder…` still reloads the tree; right-click → Delete still works through the confirmation dialog
- Selecting a `.md` file in the tree opens its content in the editor pane with markdown syntax highlighting visible (headings colored differently from body text, code blocks visually distinct, emphasis marks subtle)
- Selecting a folder node clears the editor and shows the empty hint
- Typing in the editor pauses for ~500 ms after the last keystroke; the file on disk reflects the new content (verify with `cat` / Windows equivalent)
- Switching to another note immediately persists any pending edits (no debounce wait); the previous note's file matches the editor's last state
- Deleting the currently-open note via the tree context menu closes the editor (empty hint visible) without errors
- Changing the workspace flushes pending saves, refreshes the tree, and resets the editor to empty

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation that the refactor preserved S-01 behavior AND the new editor flow works before proceeding to Phase 2.

---

## Phase 2: New note creation

### Overview

Add a `File → New Note…` menu item (and Ctrl+N) that publishes a `NewNoteRequestedMessage`; `NoteTreeViewModel` consumes it, prompts for a name, creates the file in the resolved parent folder, refreshes the tree, and selects the new node (which publishes `NoteSelectedMessage`, opening it in the editor automatically).

### Changes Required

#### 1. New-note name validator

**File**: `Notes/Services/INewNoteNameValidator.cs` + `Notes/Services/NewNoteNameValidator.cs` (new)

**Intent**: Pure logic that takes a raw user input string and a workspace-relative parent folder, and returns either a normalized filename (with `.md` appended if missing) or a validation error.

**Contract**:
- `interface INewNoteNameValidator { NoteNameResult Validate(string rawInput, string workspaceAbsolutePath, string parentRelativePath); }`
- `NoteNameResult` is a closed discriminated union via record inheritance with a private base constructor — the compiler structurally guarantees exactly one variant:
  ```csharp
  public abstract record NoteNameResult
  {
      private NoteNameResult() { }
      public sealed record Success(string FileName) : NoteNameResult;
      public sealed record Failure(string Error)    : NoteNameResult;
  }
  ```
  Callers pattern-match: `result switch { NoteNameResult.Success s => ..., NoteNameResult.Failure f => ... }`. External code cannot add new variants (private base ctor closes the hierarchy to the two nested records).
- Rules: trim whitespace; reject empty after trim ("Name cannot be empty"); reject `Path.GetInvalidFileNameChars()` plus explicit `/` and `\` ("Name contains an invalid character"); append `.md` if input doesn't already end in `.md` (case-insensitive); resolve absolute path and reject if `File.Exists` ("A note with that name already exists"). On success, return `new NoteNameResult.Success(normalizedFileName)` (no path, just the leaf).
- `parentRelativePath` uses `/` separator; the validator translates to OS separators internally via `Path.Combine`.

#### 2. New-note dialog

**File**: `Notes/Views/NewNoteDialog.axaml` + `Notes/Views/NewNoteDialog.axaml.cs` (new)

**Intent**: Minimal modal `Window` with a single `TextBox` for the filename, an error label, and Create / Cancel buttons. Same shape as the S-01 `ConfirmDialog` — pure UI wiring, no ViewModel.

**Contract**:
- `NewNoteDialog : Window` with a static `Task<string?> Show(Window owner, string parentFolderDisplay, Func<string, string?> validate)` helper.
- `parentFolderDisplay` is shown in the dialog as "Creating in: <relative-path or 'workspace root'>". `validate` is called on each text change and on Create-click; returns null on success or an error message to display.
- Returns the normalized filename on Create + valid input; `null` on Cancel / window-close. Enter = Create when valid; Esc = Cancel. Create button disabled while the input is invalid.

#### 3. Dialog service

**File**: `Notes/Services/INewNoteDialogService.cs` + `Notes/Services/NewNoteDialogService.cs` (new)

**Intent**: ViewModel-side abstraction (parallel to `IConfirmDialogService`) so `NoteTreeViewModel` can be unit-tested with a stub.

**Contract**:
- `interface INewNoteDialogService { Task<string?> PromptForName(string parentFolderDisplay, Func<string, string?> validate); }`
- Implementation reads the active window via `(Application.Current as App)?.MainWindow` and delegates to `NewNoteDialog.Show`.

#### 4. Tree VM handles `NewNoteRequestedMessage`

**File**: `Notes/ViewModels/NoteTreeViewModel.cs`

**Intent**: Add `IRecipient<NewNoteRequestedMessage>` to the tree VM. The handler resolves the parent folder from `SelectedNode`, prompts for a name, creates the empty file, refreshes the tree, and assigns the matching node to `SelectedNode`.

**Contract**:
- Constructor gains `INewNoteNameValidator validator`, `INewNoteDialogService newNoteDialog`, and `INoteFileService fileService` (the latter so the create-empty-file step uses the same write abstraction as Phase 1's editor flow).
- `async void Receive(NewNoteRequestedMessage m)` — calls a private `async Task HandleNewNote()`:
  1. If `_workspacePath` is null/empty, return.
  2. Resolve parent relative path: `SelectedNode == null` → `""`; `SelectedNode.Kind == Folder` → `SelectedNode.RelativePath`; `SelectedNode.Kind == File` → strip last segment from `SelectedNode.RelativePath` (`""` if no `/`).
  3. Compute display: `""` → "workspace root", else the relative path.
  4. Call `_newNoteDialog.PromptForName(display, raw => _validator.Validate(raw, _workspacePath, parentRelative) is NoteNameResult.Failure f ? f.Error : null)`.
  5. If null returned, exit.
  6. Re-validate defensively: `if (_validator.Validate(returnedFileName, _workspacePath, parentRelative) is not NoteNameResult.Success success) return;`.
  7. Resolve absolute path: `Path.Combine(_workspacePath, parentRelative.Replace('/', Path.DirectorySeparatorChar), success.FileName)`.
  8. Create empty file: `_fileService.Save(absolutePath, "")` — routes through the same `INoteFileService` abstraction the editor uses, keeping the write semantics (UTF-8 without BOM) centralised.
  9. `await LoadTreeCommand.ExecuteAsync(null)`.
  10. Walk the new `Root` to find the node whose `RelativePath` matches `(parentRelative + "/" + success.FileName).TrimStart('/')`; assign to `SelectedNode` (which auto-publishes `NoteSelectedMessage`, opening it in the editor).

#### 5. Menu wiring

**File**: `Notes/MainWindow.axaml`

**Intent**: Add `File → New Note…` and the Ctrl+N key binding.

**Contract**:
- Insert `<MenuItem Header="_New Note…" Command="{Binding NewNoteCommand}" InputGesture="Ctrl+N" />` as the first child of the `File` menu (before "Change Notes Folder…").
- Add `<Window.KeyBindings><KeyBinding Gesture="Ctrl+N" Command="{Binding NewNoteCommand}"/></Window.KeyBindings>` (or extend an existing `KeyBindings` block).

#### 6. DI registration

**File**: `Notes/Program.cs`

**Intent**: Register the new validator and dialog service as singletons.

**Contract**: `services.AddSingleton<INewNoteNameValidator, NewNoteNameValidator>();` and `services.AddSingleton<INewNoteDialogService, NewNoteDialogService>();`.

#### 7. Unit tests

**File**: `Notes.Tests/NewNoteNameValidatorTests.cs` (new)

**Intent**: Cover the validation matrix.

**Contract**: Method names follow `Method_WhenScenario_ExpectedBehaviour`:
- `Validate_WhenInputIsEmpty_ReturnsError`
- `Validate_WhenInputIsWhitespace_ReturnsError`
- `Validate_WhenInputContainsForwardSlash_ReturnsError`
- `Validate_WhenInputContainsBackslash_ReturnsError`
- `Validate_WhenInputContainsInvalidFileNameChar_ReturnsError` (use a cross-platform char from `Path.GetInvalidFileNameChars()`, e.g. `\0`)
- `Validate_WhenInputLacksExtension_AppendsMdSuffix`
- `Validate_WhenInputHasMdSuffix_PreservesItExactly`
- `Validate_WhenInputHasUppercaseMdSuffix_DoesNotDoubleAppend`
- `Validate_WhenFileAlreadyExists_ReturnsError`
- `Validate_WhenInputIsValidAndUnique_ReturnsNormalizedFileName`
- `Validate_WhenParentSubfolderUsed_ResolvesPathThroughParent`

Per-test temp directory for the "file exists" cases.

**File**: `Notes.Tests/NoteTreeViewModelTests.cs`

**Intent**: Extend with `Receive_WhenNewNoteRequestedMessage_CreatesFileAtResolvedParent` as an xUnit `[Theory]` driven by `[InlineData]` over the three selection states, so each branch reports as a separate test case while sharing the setup.

**Contract**: Use a temp workspace fixture and a fake `INewNoteDialogService` that returns a canned filename. Theory shape:
```csharp
[Theory]
[InlineData(NewNoteSelection.NoSelection,    "",         "untitled.md")]
[InlineData(NewNoteSelection.FolderSelected, "sub",      "sub/untitled.md")]
[InlineData(NewNoteSelection.FileSelected,   "sub/x.md", "sub/untitled.md")]
public void Receive_WhenNewNoteRequestedMessage_CreatesFileAtResolvedParent(
    NewNoteSelection selection, string selectedRelativePath, string expectedRelativePath) { ... }
```
`NewNoteSelection` is a private enum in the test file (`NoSelection | FolderSelected | FileSelected`) so each case appears in the test runner with a readable label. The expected relative path encodes the parent-folder resolution branch.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with no new warnings
- `dotnet test` green; new validator tests and extended tree VM tests pass

#### Manual Verification

- `File → New Note…` with no selection shows "Creating in: workspace root"
- With a folder selected, the dialog shows the folder's relative path as the target
- With a file selected, the dialog shows the parent folder (not the file's own path)
- Empty / whitespace input disables Create and shows "Name cannot be empty"
- Name with `/` or `\` shows the invalid-character error
- Existing note's name shows the duplicate error
- Valid name creates the file in the resolved location, refreshes the tree, opens the new (empty) note in the editor
- Ctrl+N triggers the dialog without the menu being open
- Cancelling leaves disk, tree, and editor untouched
- Names entered without `.md` get the suffix on disk

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before Phase 3.

---

## Phase 3: Preview toggle

### Overview

Add `View → Preview` (Ctrl+E) that publishes a `TogglePreviewRequestedMessage`; `NoteEditorViewModel` consumes it and flips `PaneState` between `Editing` and `Previewing`. The `MarkdownScrollViewer` already lives in the editor UserControl (added in Phase 1); only the menu wiring and the editor VM's `Receive` handler are new behavior.

### Changes Required

#### 1. `NoteEditorViewModel` handles the toggle message

**File**: `Notes/ViewModels/NoteEditorViewModel.cs`

**Intent**: Implement the toggle logic the Phase 1 stub left in place.

**Contract**:
- Add `IRecipient<TogglePreviewRequestedMessage>` to the recipient interfaces (if not declared in Phase 1 as a no-op).
- `void Receive(TogglePreviewRequestedMessage m)`:
  - `Empty` → no-op.
  - `Editing` → `PreviewText = _currentEditorText; PaneState = Previewing;`.
  - `Previewing` → `PaneState = Editing;` (no need to touch `LoadedText` — the document is intact).

#### 2. View menu

**File**: `Notes/MainWindow.axaml`

**Intent**: Add a `View` menu with a single `Preview` item, plus the Ctrl+E key binding.

**Contract**:
- Add `<MenuItem Header="_View"><MenuItem Header="_Preview" Command="{Binding TogglePreviewCommand}" InputGesture="Ctrl+E"/></MenuItem>` after the `File` menu.
- Add `<KeyBinding Gesture="Ctrl+E" Command="{Binding TogglePreviewCommand}"/>` to `Window.KeyBindings`.

#### 3. Unit tests

**File**: `Notes.Tests/NoteEditorViewModelTests.cs`

**Intent**: Extend with toggle cases.

**Contract**:
- `Receive_WhenTogglePreviewMessageInEditingState_CopiesTextToPreviewAndSwitches`
- `Receive_WhenTogglePreviewMessageInPreviewingState_SwitchesBackToEditing`
- `Receive_WhenTogglePreviewMessageInEmptyState_RemainsEmpty`

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with no new warnings
- `dotnet test` remains green; new editor VM tests pass

#### Manual Verification

- With a note open in the editor, `View → Preview` (or Ctrl+E) replaces the editor with a rendered preview showing the current edits (not just the on-disk content)
- Toggling back returns to the editor with document text intact
- Switching to another note while in preview mode loads the new note into the editor in editing mode (preview state resets via the `NoteSelectedMessage` path)
- Empty state: with no note selected, Ctrl+E (and the menu) do nothing (no error)
- Mixed markdown (headings, lists, fenced code, links, image URL, table) renders with appropriate styling
- Editing while in preview is not possible (no editor visible); user must toggle back to edit further

**Implementation Note**: After completing this phase and all automated verification passes, run the full end-to-end manual test pass below on Linux and Windows before considering the slice complete.

---

## Testing Strategy

### Unit Tests

- `NoteFileServiceTests` (Phase 1): missing file → empty, round-trip, UTF-8 encoding, overwrite.
- `NoteTreeViewModelTests` (Phase 1, extended in Phase 2): workspace-change reload, selection publishes message, delete publishes message + refreshes, new-note message creates at resolved parent.
- `NoteEditorViewModelTests` (Phase 1, extended in Phase 3): note-selected loads / clears, note-deleted clears matching, workspace-changed resets, text-change schedules save, toggle-preview transitions across the three states.
- `NewNoteNameValidatorTests` (Phase 2): the full validation matrix.

### Integration Tests

None. The UI is exercised manually per phase.

### Manual Testing Steps

After Phase 3, run end-to-end on both Linux and Windows:

1. Launch the app with an existing workspace; select a `.md` file → editor opens with syntax highlighting.
2. Edit the file → wait > 500 ms → close the app → reopen the file in another editor (or `cat`) → changes are persisted.
3. Type some text → immediately click a different note → first note's changes are persisted (debounce flushed).
4. Select a folder node → editor clears, hint visible.
5. With nothing selected, `File → New Note…` → enter `test` → file `test.md` created at workspace root, opens in editor.
6. With a subfolder selected, `File → New Note…` → enter `nested` → file lands inside the subfolder.
7. With a file selected, `File → New Note…` → dialog shows the file's parent folder as the target.
8. Try invalid names: empty, `foo/bar`, an existing note's name → each is rejected with a clear error; Create button disabled until the input is valid.
9. Open a note with mixed markdown (headings, lists, fenced code, links, an image URL, a table) → toggle preview → all elements render with appropriate styling.
10. While in preview, switch to another note → new note opens in edit mode (preview state reset).
11. Empty workspace (delete all `.md` files) → empty tree → New Note still works → creates the first note.
12. Workspace contains `.templates/` from S-01 → templates still appear in the tree as regular files; opening one shows its content in the editor.
13. Delete the currently-open note via right-click in the tree → editor closes to the empty hint without errors.
14. Change the workspace via the menu while a note is open → editor closes; pending edits to the previous note's file persisted; tree loads the new workspace.

## Performance Considerations

PRD NFR ("feel instant"): the auto-save debounce at 500 ms is well within the perceived-lag budget; AvaloniaEdit's rendering is already optimized for code-editor workloads. Markdown.Avalonia rendering is on-demand (only when the preview toggle fires), so it cannot affect typing latency. File I/O is synchronous but trivial. `WeakReferenceMessenger` delivers messages synchronously on the publishing thread — message handlers must avoid heavy work; in this design they perform either a single property assignment or a file read (small files), both fast.

## Migration Notes

No data migration. Existing `.md` files in the workspace open in the editor as-is. The settings file from S-01 is unchanged. The code refactor (VM split) is internal — no on-disk or user-visible change beyond the new editor pane.

## References

- Roadmap: `context/foundation/roadmap.md` §S-02
- PRD: `context/foundation/prd.md` (FR-001, FR-002, FR-004, US-01)
- Prior slice: `context/changes/workspace-and-note-list/plan.md` (the MVVM/DI/services foundation this builds on)
- AvaloniaEdit (NuGet `Avalonia.AvaloniaEdit`): https://github.com/AvaloniaUI/AvaloniaEdit
- Markdown.Avalonia (NuGet `Markdown.Avalonia`): https://github.com/whistyun/Markdown.Avalonia
- CommunityToolkit.Mvvm Messaging: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/messenger
- Project conventions: `Notes/CLAUDE.md` (MVVM, compiled bindings, `jj` for VCS, test method naming `Method_WhenScenario_ExpectedBehaviour`)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: VM split + editor pane with auto-save

#### Automated

- [x] 1.1 `dotnet build` succeeds with no new warnings beyond baseline — 842ffe9
- [x] 1.2 `dotnet test` is green; new `NoteFileServiceTests`, `NoteTreeViewModelTests`, `NoteEditorViewModelTests` cases pass alongside the existing S-01 suite — 842ffe9
- [x] 1.3 The app starts and resolves the new singletons + child VMs without DI errors — 842ffe9

#### Manual

- [x] 1.4 S-01 regressions: first-launch picker, stale-path recovery, Change Notes Folder, right-click → Delete with confirmation all behave as before — 842ffe9
- [x] 1.5 Selecting a `.md` file in the tree opens its content in the editor pane with markdown syntax highlighting visible — 842ffe9
- [x] 1.6 Selecting a folder node clears the editor and shows the empty hint — 842ffe9
- [x] 1.7 Typing pauses ~500 ms after the last keystroke and the file on disk reflects the new content — 842ffe9
- [x] 1.8 Switching notes immediately persists pending edits (no debounce wait); previous file matches the editor's last state — 842ffe9
- [x] 1.9 Deleting the currently-open note via the tree closes the editor to the empty hint without errors — 842ffe9
- [x] 1.10 Changing the workspace flushes pending saves, refreshes the tree, and resets the editor to empty — 842ffe9

### Phase 2: New note creation

#### Automated

- [x] 2.1 `dotnet build` succeeds with no new warnings — bcc2b62
- [x] 2.2 `dotnet test` green; new `NewNoteNameValidatorTests` and extended tree VM tests pass — bcc2b62

#### Manual

- [x] 2.3 `File → New Note…` with no selection shows "Creating in: workspace root" — bcc2b62
- [x] 2.4 With a folder selected, the dialog shows the folder's relative path as the target — bcc2b62
- [x] 2.5 With a file selected, the dialog shows the parent folder (not the file's own path) — bcc2b62
- [x] 2.6 Empty / whitespace input disables Create and shows "Name cannot be empty" — bcc2b62
- [x] 2.7 Name with `/` or `\` shows the invalid-character error — bcc2b62
- [x] 2.8 Duplicate name shows the "already exists" error — bcc2b62
- [x] 2.9 Valid name creates the file in the resolved location, refreshes tree, opens the empty note in editor — bcc2b62
- [x] 2.10 Ctrl+N triggers the dialog without the menu being open — bcc2b62
- [x] 2.11 Cancelling leaves disk, tree, and editor untouched — bcc2b62
- [x] 2.12 Name entered without `.md` gets the suffix on disk — bcc2b62

### Phase 3: Preview toggle

#### Automated

- [ ] 3.1 `dotnet build` succeeds with no new warnings
- [ ] 3.2 `dotnet test` remains green; new editor VM tests pass

#### Manual

- [ ] 3.3 `View → Preview` (or Ctrl+E) on an open note replaces the editor with a rendered preview reflecting current edits
- [ ] 3.4 Toggling back returns to the editor with document text intact
- [ ] 3.5 Switching notes while in preview opens the new note in edit mode (preview state resets)
- [ ] 3.6 With no note selected, View → Preview and Ctrl+E are no-ops (no error, no visible change)
- [ ] 3.7 Mixed markdown (headings, lists, fenced code, links, image, table) renders with appropriate styling
- [ ] 3.8 Editing is not possible while preview is showing (no editor visible)
