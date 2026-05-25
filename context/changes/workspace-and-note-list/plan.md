# Workspace Selection, Note List, and Delete — Implementation Plan

## Overview

Deliver roadmap slice S-01: the user picks a notes folder on first launch, sees every `.md` file in that folder (recursively) in a hierarchical tree grouped by subdirectory, and can delete a note with a confirmation dialog. Along the way, introduce the MVVM/DI/services foundation that S-02–S-04 inherit.

## Current State Analysis

The codebase is an empty Avalonia 12 / .NET 10 scaffold from `dotnet new avalonia.app`:

- `Notes.csproj` references `Avalonia` 12.0.3 + `Avalonia.Themes.Fluent` + `Avalonia.Fonts.Inter` + `Avalonia.Desktop` + `AvaloniaUI.DiagnosticsSupport` (Debug-only). Nullable enabled. `AvaloniaUseCompiledBindingsByDefault=true`.
- `Program.cs` runs `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` with the standard `WithInterFont().LogToTrace()` setup.
- `App.axaml.cs` instantiates `MainWindow` directly in `OnFrameworkInitializationCompleted`. No DI container.
- `MainWindow.axaml` shows the placeholder text `Welcome to Avalonia!`. No MVVM, no ViewModel, no bindings.
- No `Notes.sln`, no test project, no `Services/`, `Models/`, or `ViewModels/` folders.
- `AGENTS.md` mandates: MVVM with code-behind only for UI wiring, compiled bindings (`x:DataType`), composition over inheritance, jujutsu (`jj`) for VCS.
- `context/foundation/tech-stack.md` flags `bootstrapper_confidence: best-effort` — Avalonia-specific MVVM setup was intentionally deferred to the first slice that needs it (this one).

## Desired End State

After this slice ships, on Linux and Windows:

1. Running the app for the first time pops a folder-picker; selecting a folder writes the choice to `$XDG_CONFIG_HOME/Notes/settings.json` (Linux) or `%APPDATA%\Notes\settings.json` (Windows).
2. The main window opens to a `TreeView` showing every `.md` file in that folder, recursively, with subdirectories as expandable nodes and files as leaves.
3. `File → Change Notes Folder…` reopens the picker and reloads the tree.
4. Right-clicking a note leaf opens a context menu with "Delete"; clicking it shows a confirmation dialog; confirming deletes the file from disk and removes the leaf from the tree.
5. Restarting the app skips the picker and opens directly into the saved workspace.
6. `dotnet build` succeeds; `dotnet test` runs the new `Notes.Tests` project green.

### Key Discoveries

- Avalonia 12 ships `IStorageProvider` (replacement for the deprecated `OpenFolderDialog`) accessed via `TopLevel.GetTopLevel(control).StorageProvider`. This is the API for the folder picker.
- Avalonia has no built-in `MessageBox` — confirmation dialogs are custom `Window` subclasses opened with `ShowDialog(owner)`. Adding a package for one dialog is overkill at this stage.
- `CommunityToolkit.Mvvm` 8.x ships source generators for `[ObservableProperty]` and `[RelayCommand]` that work cleanly with Avalonia compiled bindings — see `x:DataType` requirement in `AGENTS.md`.
- `Microsoft.Extensions.DependencyInjection` is the conventional DI container in the .NET ecosystem and pairs naturally with `CommunityToolkit.Mvvm`.
- On Linux, `Environment.GetFolderPath(SpecialFolder.ApplicationData)` returns `~/.config`, matching XDG; on Windows it returns `%APPDATA%`. One call covers both OSes for the settings dir.

## What We're NOT Doing

- **No file-system watcher** — external edits to the notes folder don't refresh the tree in this slice. Reload happens on workspace change. Defer to a later slice if needed.
- **No `.templates/` filtering** — the user explicitly chose to show every `.md` recursively, including those under `.templates/`. S-04 may revisit.
- **No trash/recycle-bin integration** — delete is `File.Delete` after confirmation, by user choice. No undo.
- **No editor, no preview, no search, no tags, no templates** — those are S-02/S-03/S-04.
- **No empty-state welcome screen** — if the user cancels the first-launch picker, the app exits.
- **No macOS-specific work** — out of scope per `infrastructure.md` §Out of Scope.
- **No drag/drop, rename, or move** in the tree — delete is the only mutation in this slice.
- **No theming/UI polish beyond Fluent defaults.**

## Implementation Approach

Three phases, each ending in a verifiable state:

- **Phase 1** introduces all non-UI plumbing — DI, services, models, the test project — with no behavioral change to the running app. Foundation that S-02+ also use.
- **Phase 2** wires the first-launch flow and main shell. The app now persists a workspace and lets the user change it; the main window is structurally ready but its content area is still placeholder.
- **Phase 3** fills the content area with the `TreeView` and adds the context-menu delete. End-to-end user value lands here.

## Critical Implementation Details

- **DI lifetime for services**: register `ISettingsService`, `IWorkspaceScanner`, `INoteDeleter`, `IFolderPicker` as singletons; ViewModels as transients. ViewModels that observe state should not be singletons — a new instance per window-open is the simpler contract.
- **First-launch flow ordering**: show `MainWindow` first, then `await` the folder picker as a modal owned by that window. If the user cancels with no saved workspace, call `desktop.Shutdown(0)`. The main window is visible behind the picker (briefly empty); the picker cannot end up orphaned because it has a real owner.
- **Tree refresh after delete or workspace change**: rebuild the entire tree from a fresh scan rather than mutating the tree in place. The dataset is small (PRD `data_volume: small`); simplicity beats micro-optimization.
- **`SettingsService` write atomicity**: write to `settings.json.tmp` then `File.Move` to `settings.json` — prevents a half-written settings file on crash (honors the PRD "no data loss" guardrail at the settings layer too).

## Phase 1: Foundation — MVVM, DI, services, and tests

### Overview

Add the packages, scaffold the solution + test project, build the pure-logic services and their unit tests. No visible app change.

### Changes Required

#### 1. Solution file and test project scaffolding

**File**: `Notes.sln` (new)

**Intent**: Create a solution so `Notes.csproj` and the new `Notes.Tests/Notes.Tests.csproj` are built and run together by `dotnet build` / `dotnet test`.

**Contract**: `dotnet sln Notes.sln add Notes.csproj Notes.Tests/Notes.Tests.csproj` registers both projects.

**File**: `Notes.Tests/Notes.Tests.csproj` (new)

**Intent**: xUnit test project targeting `net10.0`, referencing `Notes.csproj`. Where the unit tests for services live.

**Contract**: SDK `Microsoft.NET.Sdk`, `TargetFramework=net10.0`, `Nullable=enable`, package references to `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`. ProjectReference to `../Notes.csproj`.

#### 2. Add MVVM and DI packages

**File**: `Notes.csproj`

**Intent**: Bring in `CommunityToolkit.Mvvm` (source generators for observable properties and relay commands) and `Microsoft.Extensions.DependencyInjection` (DI container).

**Contract**: Add `<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />` and `<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.*" />`. Keep all existing Avalonia references untouched.

#### 3. Application settings model

**File**: `Models/AppSettings.cs` (new)

**Intent**: Strongly-typed record holding the persisted user preferences. Currently just the workspace path; designed to extend.

**Contract**:
```csharp
public sealed record AppSettings(string? WorkspacePath)
{
    public static AppSettings Empty { get; } = new(WorkspacePath: null);
}
```

#### 4. Settings service

**File**: `Services/ISettingsService.cs` + `Services/SettingsService.cs` (new)

**Intent**: Resolve the per-OS config file path, read and write `AppSettings` as JSON with atomic write, and expose the resolved path for tests. Pure logic except for the actual file I/O.

**Contract**:
- `interface ISettingsService { AppSettings Load(); void Save(AppSettings settings); string ConfigFilePath { get; } }`
- `ConfigFilePath` = `Path.Combine(Environment.GetFolderPath(SpecialFolder.ApplicationData), "Notes", "settings.json")`. Tests inject this path via a constructor overload that accepts an explicit path.
- `Load()` returns `AppSettings.Empty` if the file does not exist or fails to parse (no exception leaks to callers).
- `Save()` writes to `<path>.tmp` then `File.Move(tmp, final, overwrite: true)`; creates the parent directory if missing.

#### 5. Workspace scanner

**File**: `Services/IWorkspaceScanner.cs` + `Services/WorkspaceScanner.cs` (new)

**Intent**: Enumerate every `.md` file under a given root directory, recursively, returning relative paths. Used by `NoteTreeBuilder`.

**Contract**:
- `interface IWorkspaceScanner { IReadOnlyList<string> ScanMarkdownFiles(string rootDirectory); }`
- Returns paths relative to `rootDirectory`, using `/` as separator regardless of OS (the tree builder relies on a single separator).
- Skips files starting with `.` but recurses into all directories including dotfolders (templates live under `.templates/` and the user opted to show them).
- Sorted lexicographically for deterministic test output.

#### 6. Note tree builder

**File**: `Models/NoteTreeNode.cs` + `Services/NoteTreeBuilder.cs` (new)

**Intent**: Pure function converting a flat sorted list of relative paths into a hierarchical tree with folder and file nodes. The trickiest piece of logic in S-01 — tests pin its edge cases (root-level files, empty intermediate folders, deeply nested paths, paths with the same folder name at different depths).

**Contract**:
- `NoteTreeNode` is a sealed record with `string Name`, `string RelativePath` (relative to the workspace root, `/` separator), `NoteNodeKind Kind` (`Folder | File`), `IReadOnlyList<NoteTreeNode> Children`.
- `NoteTreeBuilder.Build(IReadOnlyList<string> relativePaths)` returns a single `NoteTreeNode` representing the workspace root (`Name = ""`, `RelativePath = ""`, `Kind = Folder`), with children produced by grouping paths by their first path segment.
- Folder children sort before file children at each level; both sorted alphabetically (case-insensitive).
- Stateless and side-effect-free — no I/O.

#### 7. Note deleter

**File**: `Services/INoteDeleter.cs` + `Services/NoteDeleter.cs` (new)

**Intent**: Single-method wrapper around `File.Delete`. The wrapper exists so ViewModels depend on an interface (testable) and so future trash/undo behavior can swap in without touching ViewModels.

**Contract**: `interface INoteDeleter { void Delete(string absolutePath); }`. Implementation calls `File.Delete(absolutePath)`. Throws on failure — callers decide whether to surface an error dialog.

#### 8. Folder picker abstraction

**File**: `Services/IFolderPicker.cs` + `Services/AvaloniaFolderPicker.cs` (new)

**Intent**: Wrap Avalonia's `IStorageProvider.OpenFolderPickerAsync` behind a service so DI can supply it and startup code stays free of `TopLevel` lookups.

**Contract**:
- `interface IFolderPicker { Task<string?> PickFolder(); }`
- Implementation reads the active window via `(Application.Current as App)?.MainWindow` (Phase 1 §9 adds the property), opens the picker via that window's `StorageProvider` with `AllowMultiple = false`, and returns `null` if the user cancelled. Matches the self-resolution pattern used by `IConfirmDialogService` (Phase 3 §4).
- Returns the picked folder's local filesystem path via `IStorageFolder.Path.LocalPath` (not the `Uri` string or `.ToString()` / `.AbsolutePath`, which produce URL-encoded paths that break `Directory.EnumerateFiles` on folder names containing spaces or non-ASCII characters).

#### 9. DI registration and app composition root

**File**: `Program.cs`

**Intent**: Build a `ServiceCollection`, register the services and ViewModels, hand the resulting `ServiceProvider` to `App` so `App` can resolve `MainWindow`/`MainWindowViewModel` from it.

**Contract**:
- `Program.Main` builds an `AppBuilder` as today, then attaches a `ServiceProvider` to a static field on `App` (`App.Services`) before calling `StartWithClassicDesktopLifetime`.
- Services registered as singletons: `ISettingsService`, `IWorkspaceScanner`, `NoteTreeBuilder`, `INoteDeleter`, `IFolderPicker`. `MainWindow` registered as transient so Phase 1's smoke test (`App.Services.GetRequiredService<MainWindow>()`) succeeds. ViewModels (Phase 2+) registered as transients.

**File**: `App.axaml.cs`

**Intent**: In `OnFrameworkInitializationCompleted`, resolve `MainWindow` (and Phase 2's startup logic) from DI instead of `new MainWindow()`. Also expose a `MainWindow` accessor on `App` so services that need an owner window don't repeat the lifetime cast.

**Contract**:
- Replace `desktop.MainWindow = new MainWindow();` with a DI-driven flow whose details Phase 2 fills in. For Phase 1, resolving `App.Services.GetRequiredService<MainWindow>()` is enough to confirm the container works.
- Add a public `Window? MainWindow => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;` property on `App`. Services that need an owner window read it via `(Application.Current as App)?.MainWindow` — single place to update if the lifetime model ever changes, and removes the long cast chain from `AvaloniaFolderPicker` and `ConfirmDialogService`.

#### 10. Unit tests

**File**: `Notes.Tests/SettingsServiceTests.cs` (new)

**Intent**: Cover (a) missing file → `Empty`, (b) malformed JSON → `Empty` (no throw), (c) round-trip a populated `AppSettings`, (d) parent directory creation, (e) atomic write leaves no `.tmp` on success.

**Contract**: Each test uses a per-test temp directory (`Path.GetTempPath()` + `Guid`) cleaned up in `IDisposable.Dispose`.

**File**: `Notes.Tests/WorkspaceScannerTests.cs` (new)

**Intent**: Cover (a) empty directory → empty list, (b) flat directory of `.md` and non-`.md` files → only `.md` returned, (c) nested directories → recursive enumeration, (d) results use `/` separator on Windows + Linux, (e) lexicographic sort.

**Contract**: Per-test temp directory fixtures with helper for building file trees.

**File**: `Notes.Tests/NoteTreeBuilderTests.cs` (new)

**Intent**: Cover (a) empty input → root with no children, (b) single root-level file, (c) single nested file creates intermediate folder nodes, (d) folders sort before files at the same level, (e) two folders named the same at different depths remain distinct nodes, (f) deterministic alphabetical order.

**Contract**: Pure-function tests, no I/O. Assertions compare against expected `NoteTreeNode` trees.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with no warnings introduced beyond baseline
- `dotnet test` runs and all `Notes.Tests` cases pass
- `Notes.sln` lists both projects and `dotnet build Notes.sln` builds both
- DI container resolves `MainWindow` without throwing on startup

#### Manual Verification

- App still launches and shows the existing `Welcome to Avalonia!` window (Phase 1 makes no UI change)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation that the app still launches before proceeding to Phase 2.

---

## Phase 2: First-launch picker, main shell, and workspace menu

### Overview

Wire the startup flow: load settings, prompt for a folder if none saved, exit on cancel, otherwise show the main window. Add the menu bar with `File → Change Notes Folder…`.

### Changes Required

#### 1. Main window ViewModel

**File**: `ViewModels/MainWindowViewModel.cs` (new)

**Intent**: Hold the current workspace path and (Phase 3) the note tree. Expose a command to change the workspace.

**Contract** (Phase 2 surface only):
- `[ObservableProperty] string? workspacePath` — bound to the title bar / a label. Nullable so "no workspace yet" is modeled honestly without a sentinel.
- `[RelayCommand] async Task ChangeWorkspace()` — calls `IFolderPicker.PickFolder`, on success persists via `ISettingsService.Save` and updates `WorkspacePath`. Phase 3 extends this to also refresh the tree.
- `[RelayCommand] void Exit()` — calls `(Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown()`.
- Constructor takes `ISettingsService`, `IFolderPicker` (Phase 3 adds the scanner + tree builder).

#### 2. Main window view

**File**: `MainWindow.axaml`

**Intent**: Replace the placeholder text with a `DockPanel` containing a top `Menu` (`File → Change Notes Folder…`, `File → Exit`) and a content area placeholder for Phase 3's tree. Set `x:DataType` to `vm:MainWindowViewModel` for compiled bindings.

**Contract**: Window `Title` bound to `WorkspacePath`. `File → Change Notes Folder…` bound to `ChangeWorkspaceCommand`; `File → Exit` bound to `ExitCommand`. Content area = `Grid` or `Border` with placeholder text "Notes will appear here." (Phase 3 replaces).

**File**: `MainWindow.axaml.cs`

**Intent**: Stays empty beyond `InitializeComponent()` — `DataContext` is supplied by DI in `App.axaml.cs`.

**Contract**: No change beyond what compiled bindings need.

#### 3. Startup flow in App

**File**: `App.axaml.cs`

**Intent**: On framework init, resolve `MainWindow` + `MainWindowViewModel` from DI and show the window. Then load settings; if `WorkspacePath` is null, await the folder picker as a modal owned by the (now-visible) main window. If the user cancels, shut down. Otherwise persist the choice and let Phase 3's tree-load take over.

**Contract**:
- `OnFrameworkInitializationCompleted` resolves `MainWindow`, assigns its `DataContext`, calls `desktop.MainWindow = window; window.Show();`, then kicks off `Start(desktop)` (fire-and-forget — Avalonia lifetime handles it).
- `private async void Start(IClassicDesktopStyleApplicationLifetime desktop)` orchestrates: load settings → if `WorkspacePath` is set but `!Directory.Exists(WorkspacePath)`, clear it via `settingsService.Save(AppSettings.Empty)` → if (now) no workspace, `await folderPicker.PickFolder()` → if cancelled, `desktop.Shutdown(0)` → else persist via `ISettingsService.Save` and update the ViewModel's `WorkspacePath`.
- `IFolderPicker` self-resolves the active window, so `Start` doesn't thread the MainWindow reference through. The empty main window flashes briefly behind the picker on first launch — chosen UX.

#### 4. DI updates

**File**: `Program.cs`

**Intent**: Register `MainWindowViewModel` as transient. `MainWindow` was already registered in Phase 1 §9.

**Contract**: `services.AddTransient<MainWindowViewModel>();`.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with no new warnings
- `dotnet test` remains green (no new tests required for this phase; existing Phase 1 tests still pass)

#### Manual Verification

- First launch (delete `settings.json` first) pops a folder picker; selecting a folder writes `settings.json` and opens the main window with the path in the title bar
- Cancelling the first-launch picker exits the app cleanly
- Restarting the app skips the picker and opens straight to the main window
- `File → Change Notes Folder…` reopens the picker; selecting a new folder updates the title bar; cancelling leaves things unchanged
- `File → Exit` closes the app
- Stale workspace path: with the app closed, rename or delete the previously-selected workspace folder; relaunching shows the folder picker (the stored path is cleared and the app behaves as a first launch)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation that the workspace flow behaves as described above before proceeding to Phase 3.

---

## Phase 3: Note tree and delete with confirmation

### Overview

Bind the workspace's note tree to a `TreeView`, add a context-menu Delete action with a custom confirmation dialog, refresh the tree after delete or workspace change.

### Changes Required

#### 1. Extend MainWindowViewModel

**File**: `ViewModels/MainWindowViewModel.cs`

**Intent**: Add an observable `NoteTreeNode? Root`, a method to rebuild it from disk, and a `DeleteNoteCommand` that takes the selected file node, shows the confirmation dialog, calls `INoteDeleter`, and triggers a tree rebuild.

**Contract**:
- `[ObservableProperty] NoteTreeNode? root` — bound to `TreeView.ItemsSource` (wrapped as a single-element collection or via the root's children).
- `[RelayCommand] async Task LoadTree()` — assumes `WorkspacePath` is non-null and points to an existing directory (the validation is `App.Start`'s job); scans, builds the tree, assigns to `Root`. Exposed as `LoadTreeCommand` so `App` can invoke it from startup.
- `[RelayCommand] async Task DeleteNote(NoteTreeNode node)` — early-return if `node.Kind != File`; open the confirmation dialog with the note's relative path; on confirm, call `INoteDeleter.Delete(absolutePath)`, then `await LoadTreeCommand.ExecuteAsync(null)` to refresh.
- `ChangeWorkspaceCommand` (from Phase 2) is extended to `await LoadTreeCommand.ExecuteAsync(null)` after persisting the new path.
- Constructor gains `IWorkspaceScanner`, `NoteTreeBuilder`, `INoteDeleter`, and `IConfirmDialogService` (see §4 below).

**File**: `App.axaml.cs`

**Intent**: Once `Start` has resolved a valid `WorkspacePath` (whether from saved settings or a fresh pick), trigger the initial tree load. Keeps the disk scan and UI refresh explicit at the orchestration point rather than hiding it inside a property setter.

**Contract**: After Phase 2's settings-and-picker orchestration assigns `viewModel.WorkspacePath`, call `await viewModel.LoadTreeCommand.ExecuteAsync(null);` exactly once. Subsequent reloads come from `ChangeWorkspaceCommand` or `DeleteNoteCommand` — `App` is not involved.

#### 2. TreeView in MainWindow

**File**: `MainWindow.axaml`

**Intent**: Replace the Phase 2 placeholder with a `TreeView` bound to `Root.Children`, using a `HierarchicalDataTemplate<NoteTreeNode>` that renders folder nodes with their `Name` and an expand chevron, file nodes with `Name` only. Add a `ContextMenu` on file nodes with a "Delete" item bound to `DeleteNoteCommand` (parameter = the node).

**Contract**:
- `HierarchicalDataTemplate ItemsSource="{Binding Children}"` — leaves with no children render naturally.
- Context menu's `Command` uses `{Binding $parent[TreeView].DataContext.DeleteNoteCommand}` with `CommandParameter="{Binding}"` so the bound node is passed.
- `x:DataType` continues to point at `MainWindowViewModel`; the template's `x:DataType` is `models:NoteTreeNode`.

#### 3. Confirmation dialog

**File**: `Views/ConfirmDialog.axaml` + `Views/ConfirmDialog.axaml.cs` (new)

**Intent**: A minimal modal `Window` with a message label and Yes/No buttons. Reusable for any future confirmation; sized to fit content.

**Contract**:
- `ConfirmDialog : Window` with a static `Task<bool> Show(Window owner, string title, string message)` helper. Returns `true` if user clicked Yes, `false` if No or window-closed.
- Two `Button`s wired in code-behind to set a `_result` field and `Close()`. Modal via `ShowDialog(owner)`.
- No `ViewModel` required — too small to justify one; pure UI wiring is allowed per AGENTS.md ("code-behind should only contain UI wiring").

#### 4. Inject the dialog into the ViewModel

**File**: `Services/IConfirmDialogService.cs` + `Services/ConfirmDialogService.cs` (new)

**Intent**: ViewModel-side abstraction so the ViewModel can be unit-tested with a stub. The Avalonia implementation calls `Views/ConfirmDialog.Show` against the active window.

**Contract**: `interface IConfirmDialogService { Task<bool> Confirm(string title, string message); }` implemented by `ConfirmDialogService`. Implementation reads the application's main window via `(Application.Current as App)?.MainWindow` (Phase 1 §9 adds the property) and delegates to `Views/ConfirmDialog.Show`.

#### 5. Wire it all up

**File**: `Program.cs`

**Intent**: Register `IConfirmDialogService` as a singleton.

**Contract**: `services.AddSingleton<IConfirmDialogService, ConfirmDialogService>();`.

### Success Criteria

#### Automated Verification

- `dotnet build` succeeds with no new warnings
- `dotnet test` remains green

#### Manual Verification

- After selecting a workspace containing nested `.md` files, the TreeView shows the full hierarchy; folders expand/collapse; subfolder named the same as a top-level folder remains distinct
- Changing the workspace via the menu reloads the tree to the new folder's contents
- Right-clicking a file node opens a context menu with "Delete"
- Clicking Delete opens the confirmation dialog showing the note's relative path
- Confirming deletes the file from disk and removes the leaf from the tree
- Cancelling the confirmation dialog leaves the file and tree untouched
- An empty workspace shows an empty `TreeView` (no error)
- A workspace with `.templates/` shows the templates folder and its `.md` files in the tree (per user's chosen no-filter behavior)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation across both Linux and Windows before considering the slice complete.

---

## Testing Strategy

### Unit Tests (Phase 1)

- `SettingsServiceTests`: missing-file, malformed-JSON, round-trip, parent-dir creation, no stray `.tmp` files.
- `WorkspaceScannerTests`: empty dir, mixed extensions, nested recursion, `/` separator on both OSes, lexicographic sort.
- `NoteTreeBuilderTests`: empty input, root-level files, nested paths, folder-before-file ordering, same-named folders at different depths, alphabetical order.

### Integration Tests

None planned. The UI layer is exercised manually per phase. Adding UI automation (Avalonia.Headless) would inflate the slice past its value.

### Manual Testing Steps

Run end-to-end on both Linux and Windows after Phase 3:

1. Delete `settings.json` from the per-OS config dir. Launch app → main window appears with the folder picker modal on top.
2. Cancel the picker → main window and app exit together.
3. Relaunch; select a folder containing a known mix of root-level `.md` files, subfolders with `.md` files, and a `.templates/` subfolder. Verify the tree matches the directory structure.
4. Use `File → Change Notes Folder…` to point at a different folder; verify the tree reloads.
5. Restart the app; verify it opens directly into the most-recently-selected folder.
6. Right-click a file node; verify the context menu appears with "Delete".
7. Click Delete; verify the confirmation dialog shows the relative path; cancel → file remains.
8. Repeat → confirm → file is deleted from disk and the tree updates.
9. Verify deleting the last file in a subfolder leaves the (now-empty) folder node visible (acceptable for this slice; no auto-prune).

## Performance Considerations

PRD `data_volume: small` and the NFR "feel instant" set a soft budget: the tree should render within a few hundred milliseconds for typical workspaces (hundreds of notes). The scanner uses `Directory.EnumerateFiles(..., SearchOption.AllDirectories)`, which is sufficient at this scale; no caching, no async streaming. If a future user reports lag on a large vault, revisit with file-system watcher + incremental updates.

## Migration Notes

Greenfield — no existing data to migrate. The settings file is created on first save; absence is treated as "no workspace yet".

## References

- Roadmap: `context/foundation/roadmap.md` §S-01
- PRD: `context/foundation/prd.md` (FR-003, FR-007, FR-010, US-01)
- Tech-stack: `context/foundation/tech-stack.md` (Avalonia 12 / .NET 10, GitHub Releases)
- Project conventions: `AGENTS.md` (MVVM, compiled bindings, `jj` for VCS)
- Backlog: https://github.com/llysyganicz/Notes/issues/1

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Foundation — MVVM, DI, services, and tests

#### Automated

- [x] 1.1 `dotnet build` succeeds with no warnings introduced beyond baseline — 42f936d2
- [x] 1.2 `dotnet test` runs and all `Notes.Tests` cases pass — 42f936d2
- [x] 1.3 `Notes.sln` lists both projects and `dotnet build Notes.sln` builds both — 42f936d2
- [x] 1.4 DI container resolves `MainWindow` without throwing on startup — 42f936d2

#### Manual

- [x] 1.5 App still launches and shows the existing `Welcome to Avalonia!` window — 42f936d2

### Phase 2: First-launch picker, main shell, and workspace menu

#### Automated

- [x] 2.1 `dotnet build` succeeds with no new warnings
- [x] 2.2 `dotnet test` remains green

#### Manual

- [x] 2.3 First launch (delete `settings.json` first) shows the main window with the folder picker as a modal on top; selecting a folder writes `settings.json` and the main window's title bar reflects the chosen path
- [x] 2.4 Cancelling the first-launch picker exits the app cleanly (main window closes with it)
- [x] 2.5 Restarting the app skips the picker and opens straight to the main window
- [x] 2.6 `File → Change Notes Folder…` reopens the picker; selecting updates title bar; cancelling leaves things unchanged
- [x] 2.7 `File → Exit` closes the app
- [x] 2.8 Stale workspace path: deleting/renaming the saved folder before relaunch causes the app to show the picker (clear-and-reset behavior)

### Phase 3: Note tree and delete with confirmation

#### Automated

- [ ] 3.1 `dotnet build` succeeds with no new warnings
- [ ] 3.2 `dotnet test` remains green

#### Manual

- [ ] 3.3 Workspace with nested `.md` files renders the full hierarchy; folders expand/collapse; same-named folders at different depths remain distinct
- [ ] 3.4 Changing the workspace via the menu reloads the tree
- [ ] 3.5 Right-clicking a file node opens a context menu with "Delete"
- [ ] 3.6 Clicking Delete opens the confirmation dialog showing the note's relative path
- [ ] 3.7 Confirming deletes the file from disk and removes the leaf from the tree
- [ ] 3.8 Cancelling the confirmation dialog leaves the file and tree untouched
- [ ] 3.9 An empty workspace shows an empty `TreeView` with no error
- [ ] 3.10 A workspace with `.templates/` shows the templates folder and its `.md` files (per chosen no-filter behavior)
