# Create, Edit, and Preview Markdown Notes — Plan Brief

> Full plan: `context/changes/note-editor-and-preview/plan.md`

## What & Why

Deliver the second vertical slice of the Notes app: clicking a `.md` file in the tree opens it in an editor with markdown syntax highlighting, edits auto-save back to disk, `File → New Note` creates new files from inside the app, and `View → Preview` toggles a rendered view of the same content. This is the slice that turns the workspace browser from S-01 into an actual note-taking tool — every downstream slice (S-03 search, S-04 templates) assumes a working editor.

Alongside the feature work, split the single `MainWindowViewModel` from S-01 into three focused VMs (`MainWindowViewModel` for menu actions, `NoteTreeViewModel` for the tree, `NoteEditorViewModel` for the editor) communicating via `CommunityToolkit.Mvvm.Messaging.IMessenger`. Required up-front because the editor's new state would otherwise bloat the shell VM, and S-03 / S-04 will each add more.

## Starting Point

S-01 has shipped: `MainWindowViewModel` holds the workspace and tree, `MainWindow.axaml` is a `DockPanel` with a menu and a full-width `TreeView`, and the MVVM/DI/services foundation (settings, scanner, tree builder, deleter, folder picker, confirm dialog) is in place with xUnit tests for the pure-logic services. `CommunityToolkit.Mvvm` is already a dependency and ships `WeakReferenceMessenger` / `IMessenger` — no new package needed for messaging. No editor library, no markdown library, no file-content read/write, no tree selection tracking yet.

## Desired End State

The main window grows a second column. The tree lives in its own `NoteTreeView` UserControl; the editor lives in a new `NoteEditorView` UserControl. Each view's AXAML sets its own `DataContext` via a static `ViewModelLocator` resource declared in `App.axaml` — `DataContext="{Binding Tree, Source={StaticResource Locator}}"` for the tree, and the equivalent for `Main` (window) and `Editor` (editor pane). The locator exposes one property per VM and resolves each from `App.Services` on access. `MainWindowViewModel` does NOT hold references to either child VM — they're fully decoupled siblings communicating only through `IMessenger`. View code-behind is empty (or, for the editor, contains only the AvaloniaEdit-document wiring). Selecting a `.md` file publishes a `NoteSelectedMessage` and the editor loads it; typing auto-saves after a 500 ms debounce; switching notes flushes pending saves. `File → New Note…` (Ctrl+N) publishes a `NewNoteRequestedMessage`; the tree VM consumes it (it owns the selection state), prompts via a name dialog, creates the file in the selected folder (or root), refreshes, and selects the new node — which auto-opens it in the editor via the standard selection-message flow. `View → Preview` (Ctrl+E) publishes a `TogglePreviewRequestedMessage`; the editor VM flips between editor and Markdown.Avalonia rendering. Empty state shows "Select a note to edit".

## Key Decisions Made

| Decision                       | Choice                                                                              | Why                                                                                                                | Source |
| ------------------------------ | ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------ | ------ |
| ViewModel structure            | Split into `MainWindowViewModel` + `NoteTreeViewModel` + `NoteEditorViewModel`      | Single VM was already growing; new state for editor/preview would bloat it further; downstream slices will add more | Plan (refined by user feedback)   |
| VM communication               | `CommunityToolkit.Mvvm.Messaging.IMessenger` (WeakReferenceMessenger), no direct refs| Already in the dependency set; matches the "compose via DI services, not base classes" rule from CLAUDE.md          | Plan (refined by user feedback)   |
| VM-to-View binding             | `ViewModelLocator` registered as a static `Application.Resources` entry in `App.axaml`; each view sets `DataContext="{Binding <Prop>, Source={StaticResource Locator}}"` | Fully decouples siblings — `MainWindowViewModel` doesn't know child VMs exist; no service-locator calls in code-behind; DI registration is the only place that wires view types to VM types | Plan (refined by user feedback)   |
| VM lifetime                    | All three VMs are singletons (deliberate change from S-01's transient VMs)           | Stable `IMessenger` recipient registration for the lifetime of the app; locator property accesses return the same instance every time | Plan (refined by user feedback)   |
| Auto-save scheduler shape      | Event-based (`event Action OnSave`, `Bump`, `Flush`, `Cancel`) — no per-call action  | The save action doesn't change over a note's lifetime; subscribing once at construction beats threading an `Action` through every `Bump` call. Scheduler stays purpose-built; consumer owns "what to save" | Plan (refined by user feedback)   |
| Validator result shape         | Closed discriminated union: `abstract record NoteNameResult` with `Success` / `Failure` sealed nested records and a private base ctor | Compiler structurally guarantees exactly one variant; callers pattern-match instead of null-checking; no runtime invariant to maintain | Plan (refined by user feedback)   |
| Dialog service granularity     | One interface per dialog (`IConfirmDialogService`, `INewNoteDialogService`, …)        | `Confirm` is a general primitive; `PromptForName` is domain-specific. Bundling them widens the surface and violates ISP. New domain dialogs (S-04 template picker, field forms) get their own focused services | Plan (refined by user feedback)   |
| Editor control                 | AvaloniaEdit (with bundled `MarkDown.xshd` highlighter)                              | De facto Avalonia editor; ships markdown syntax highlighting; mature undo/redo and line numbers                     | Plan   |
| Preview rendering              | Markdown.Avalonia (native Avalonia controls; no WebView)                             | Zero browser dependency, theme-aware, lightweight; Markdig under the hood handles CommonMark + popular extensions   | Plan   |
| Preview layout                 | Replace editor with preview (one or the other, never side-by-side)                   | Simplest binding; full pane for whichever mode is active; matches a "hidden by default" preview model               | Plan   |
| Save behavior                  | Auto-save, debounced ~500 ms, flushed on note switch / workspace change              | Aligns with PRD "no data loss" guardrail; eliminates "forgot to save" failure mode                                  | Plan   |
| New-note naming                | Modal dialog prompts for filename upfront; `.md` appended if missing                 | User picks a meaningful name immediately; no `untitled-*.md` clutter                                                | Plan   |
| New-note location              | In currently selected folder (folder node), parent folder (file node), or root       | Matches "create here" mental model from file explorers; respects user organization                                  | Plan   |
| New-note ownership             | `NoteTreeViewModel` handles `NewNoteRequestedMessage` because it owns `SelectedNode` | Single source of truth for parent-folder resolution; the menu VM stays selection-agnostic                           | Plan   |
| Editor pane state              | Enum (`Empty` / `Editing` / `Previewing`) on `NoteEditorViewModel`                   | One state per visual; toggle is just an enum flip; cleanest binding shape                                           | Plan   |
| Tree selection model           | `SelectedNode` on `NoteTreeViewModel`, `TwoWay` to `TreeView.SelectedItem`           | Drives editor opening (via message) AND new-note parent-folder resolution                                           | Plan   |
| Empty state                    | Plain "Select a note to edit" hint when no file is open                              | Cheap, clear; no persisted last-opened complexity for MVP                                                           | Plan   |
| Mermaid / LaTeX / raw HTML     | Not rendered in preview                                                              | Out of Markdown.Avalonia's native scope; user confirmed Mermaid is not important for MVP                            | Plan   |

## Scope

**In scope:**
- VM split with `IMessenger`-based communication
- Five message types (`WorkspaceChanged`, `NoteSelected`, `NoteDeleted`, `NewNoteRequested`, `TogglePreviewRequested`)
- Two new UserControls (`NoteTreeView`, `NoteEditorView`) hosted in `MainWindow.axaml`
- `ViewModelLocator` class + `Application.Resources` entry in `App.axaml`
- AvaloniaEdit + Markdown.Avalonia + Markdig packages added
- `INoteFileService` (read / write `.md` files as UTF-8 without BOM)
- `IAutoSaveScheduler` (DispatcherTimer-based debounce)
- `File → New Note…` flow with name-prompt dialog, validator, and parent-folder resolution
- `View → Preview` toggle (Ctrl+E)
- Empty-state hint
- Unit tests for the three new services (`NoteFileService`, `NewNoteNameValidator`, scheduler exposed via interface so VMs can be tested with a fake), plus message-driven flow tests for `NoteTreeViewModel` and `NoteEditorViewModel`

**Out of scope:**
- Rename or move of existing notes
- File-system watcher / external-edit detection
- Multi-document tabs
- Side-by-side preview
- Custom XSHD tweaks
- Raw HTML / LaTeX / Mermaid in preview
- Persisted last-opened note across launches
- Configurable keyboard shortcuts
- Any "central state" service holding the current workspace — `WorkspacePath` lives in `MainWindowViewModel` and propagates via messages

## Architecture / Approach

```
                                  ┌─────────────────────────────┐
                                  │     IMessenger (singleton)   │
                                  │     WeakReferenceMessenger   │
                                  └──────────────┬──────────────┘
                                                 │ (Send / Receive)
        ┌────────────────────────────────────────┼────────────────────────────────────────┐
        │                                        │                                        │
┌───────▼────────────┐               ┌───────────▼──────────┐                ┌────────────▼───────────┐
│ MainWindowViewModel│               │  NoteTreeViewModel   │                │  NoteEditorViewModel   │
│  (no refs to       │               │  (singleton)         │                │  (singleton)           │
│   siblings)        │               │                      │                │                        │
│                    │               │  • Root              │                │  • LoadedText          │
│  • WorkspacePath   │               │  • SelectedNode      │                │  • PreviewText         │
│  • ChangeWorkspace │               │  • LoadTreeCommand   │                │  • PaneState           │
│  • NewNote         │               │  • DeleteNoteCommand │                │  • OnEditorTextChanged │
│  • TogglePreview   │               │                      │                │                        │
│  • Exit            │               │  Sends:              │                │  Receives:             │
│                    │               │   NoteSelectedMsg    │                │   NoteSelectedMsg      │
│  Sends:            │               │   NoteDeletedMsg     │                │   NoteDeletedMsg       │
│   WorkspaceChanged │               │                      │                │   WorkspaceChangedMsg  │
│   NewNoteRequested │               │  Receives:           │                │   TogglePreviewReqMsg  │
│   TogglePreviewReq │               │   WorkspaceChangedMsg│                │                        │
│                    │               │   NewNoteRequestedMsg│                │  Services:             │
│  Services:         │               │                      │                │   INoteFileService     │
│   ISettingsService │               │  Services:           │                │   IAutoSaveScheduler   │
│   IFolderPicker    │               │   IWorkspaceScanner  │                └────────────────────────┘
└────────────────────┘               │   NoteTreeBuilder    │
                                     │   INoteDeleter       │
                                     │   IConfirmDialogSvc  │
                                     │   INewNoteNameValid. │  (Phase 2)
                                     │   INewNoteDialogSvc  │  (Phase 2)
                                     └──────────────────────┘

App.axaml resources:
  <Application.Resources>
    <vm:ViewModelLocator x:Key="Locator" />
  </Application.Resources>

ViewModelLocator (Notes/ViewModels/ViewModelLocator.cs):
  public MainWindowViewModel? Main   => Resolve<MainWindowViewModel>();
  public NoteTreeViewModel?   Tree   => Resolve<NoteTreeViewModel>();
  public NoteEditorViewModel? Editor => Resolve<NoteEditorViewModel>();
  private static T? Resolve<T>() where T : class =>
      Design.IsDesignMode ? null : App.Services.GetRequiredService<T>();

MainWindow.axaml:
  <Window DataContext="{Binding Main, Source={StaticResource Locator}}" ...>
    <Grid Columns="*,Auto,2*">
      <views:NoteTreeView />
      <GridSplitter />
      <views:NoteEditorView />
    </Grid>
  </Window>

NoteTreeView.axaml:
  <UserControl DataContext="{Binding Tree,   Source={StaticResource Locator}}" ...>

NoteEditorView.axaml:
  <UserControl DataContext="{Binding Editor, Source={StaticResource Locator}}" ...>

Code-behind in all three views: empty beyond InitializeComponent()
  (NoteEditorView additionally wires AvaloniaEdit's TextChanged ↔ LoadedText bridge — no service-locator calls).
```

Sibling VMs hold no references to each other. The messenger is the only communication channel. DI registration is the only place that wires view types to VM types — change `Program.cs` and you change which VM the locator returns, without touching any AXAML or other VM.

## Phases at a Glance

| Phase                                  | What it delivers                                                                          | Key risk                                                                                                                       |
| -------------------------------------- | ----------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| 1. VM split + editor pane + auto-save  | Refactor lands AND user can open + edit + auto-save notes                                 | Re-entrancy loop between `LoadedText` → AvaloniaEdit `Document.Text` → `TextChanged` → save scheduling; guarded by `_suppressEvents` flag |
| 2. New note creation                   | Ctrl+N → publishes message → tree resolves parent folder → name dialog → file → opens     | Parent-folder resolution from `SelectedNode` has three branches; name validation has six rules to enforce in one pass         |
| 3. Preview toggle                      | Ctrl+E publishes message → editor VM flips PaneState → MarkdownScrollViewer renders text  | Markdown.Avalonia version compatibility with Avalonia 12 — verify the resolved package version when adding the dependency      |

**Prerequisites:** S-01 (`workspace-and-note-list`) shipped.
**Estimated effort:** ~3 focused sessions (Phase 1 is larger than its S-01 counterpart because the VM refactor and the editor land together).

## Open Risks & Assumptions

- **Assumes** an Avalonia-12-compatible release of AvaloniaEdit and Markdown.Avalonia exists on NuGet. If only Avalonia-11 packages are available, fall back to the closest 12-compatible fork or pin Avalonia to a version both libraries support.
- **Assumes** `WeakReferenceMessenger.Default` is acceptable as the DI-registered `IMessenger` — there's one VM instance per type in the app, so global vs scoped doesn't matter at runtime. Tests use fresh `WeakReferenceMessenger()` instances per test to avoid cross-test bleed.
- **Assumes** the XAML loader evaluates the locator-driven `DataContext` bindings during `MainWindow`'s `InitializeComponent()` (synchronously, inside the `MainWindow` constructor), so all three VMs are constructed — and registered as messenger recipients — BEFORE `MainWindowViewModel.InitializeAsync()` publishes the initial `WorkspaceChangedMessage`. Verified by the manual smoke test in Phase 1 (tree and editor should react to the first-launch workspace pick).
- **Assumes** the `Design.IsDesignMode` guard inside `ViewModelLocator.Resolve<T>()` correctly disables the DI call inside the Avalonia designer (so the designer renders with null DataContexts instead of crashing on a missing service provider).
- **Assumes** synchronous message delivery (CommunityToolkit's default) is acceptable. Message handlers in this design do at most one file read or a property assignment, both fast on the UI thread.
- **Assumes** the re-entrancy guard in `NoteEditorView.axaml.cs` (set `_suppressEvents = true` before copying `LoadedText` into the editor's document, clear after) reliably prevents an infinite `TextChanged ↔ LoadedText` loop. Verified by manual smoke test in Phase 1.
- **The user opted for** auto-save over explicit Ctrl+S — flagged in case behavior surprises someone used to Notepad's "save explicitly" model; AvaloniaEdit's per-document undo stack is the only safety net for an unwanted edit.
- **Mermaid diagrams** won't render in preview; user confirmed acceptable for MVP. Logged for a possible follow-up (custom AST visitor or out-of-process renderer).

## Success Criteria (Summary)

- User can click a note in the tree and edit it with markdown syntax highlighting; edits persist to disk without manual save
- User can create a new note via Ctrl+N (or File menu), name it in a dialog, and have it land in the selected folder
- User can press Ctrl+E to toggle between the editor and a rendered markdown preview of the same content
- All three VMs are independently testable; sibling VMs hold no references to each other; `MainWindowViewModel` does not know `NoteTreeViewModel` or `NoteEditorViewModel` exist
- `dotnet test` is green for `NoteFileService`, `NewNoteNameValidator`, and the message-driven flow tests on `NoteTreeViewModel` + `NoteEditorViewModel`
