# Note-tree Folder Management Implementation Plan

## Overview

Add first-class folder management to the note tree — a **New Folder** command and **recursive folder delete** — and make **empty folders persist and appear** in the tree via a directory-aware builder. The work sits on a foundation of `System.IO.Abstractions`: the services that perform filesystem IO are migrated to an injected `IFileSystem` so the new directory-enumerating builder, the folder validator, and the folder creator are all unit-testable without touching disk.

This change is a prerequisite for the `templates` change — creating the first template requires being able to create and see the `.templates/` folder.

## Current State Analysis

The note tree is fed by a single chokepoint: `WorkspaceScanner.ScanMarkdownFiles` enumerates `*.md` files only (`Notes/Services/WorkspaceScanner.cs:24`), and `NoteTreeBuilder.Build` synthesizes folder nodes purely as ancestors of those file paths (`Notes/Services/NoteTreeBuilder.cs:14-55`). Consequently an empty directory is invisible — there is no file path to derive it from. The same scanner is consumed by `NoteSearchIndex.Build` (`Notes/Services/NoteSearchIndex.cs:182`), which must keep ingesting **files only** (directories have no content to index).

Delete is gated to files in two places — the runtime guard `node.Kind != NoteNodeKind.File` (`NoteTreeViewModel.cs:139`) and `CanDeleteNote` (`:159-160`) — and `NoteDeleter.Delete` is `File.Delete` only (`Notes/Services/NoteDeleter.cs:7`). On delete the VM sends one `NoteDeletedMessage(relativePath)`; the search index removes exactly that key (`NoteSearchIndex.cs:88-100`) and the editor clears only on an exact-path match (`NoteEditorViewModel.cs:117-128`).

New Note establishes the patterns this change mirrors: a message-driven entry point (`Ctrl+N` → `MainWindowViewModel.NewNote` → `NewNoteRequestedMessage` → `NoteTreeViewModel.Receive`, resolving the parent from `SelectedNode` via `ResolveParentRelativePath`, `NoteTreeViewModel.cs:80-120,162-177`), a name validator returning a closed DU (`NewNoteNameValidator` + `NoteNameResult`, `Notes/Services/NewNoteNameValidator.cs`, `INewNoteNameValidator.cs:8-15`), and a reusable prompt (`INewNoteDialogService.PromptForName(parent, validate)`). Delete establishes the direct context-menu command pattern (`DeleteNoteCommand(node)` bound to the right-clicked node, `NoteTreeView.axaml:16-22`).

Five services perform raw filesystem IO and will move to `IFileSystem`: `NoteFileService`, `NoteDeleter`, `NewNoteNameValidator`, `SettingsService`, `WorkspaceScanner`. The pure `Path.Combine`/`Path.GetFileName` string operations in the ViewModels and in `NoteSearchIndex` are **not** migrated — they are deterministic string math with no disk access, so abstracting them adds churn without testability benefit.

## Desired End State

- Right-clicking a folder (or empty tree space) offers **New Folder**; `Ctrl+Shift+N` and a **File → New Folder…** menu item also create a folder. The new folder appears immediately and is selected.
- An empty folder created this way (or pre-existing on disk) **persists in the tree** across reloads — it does not vanish for lack of contained files.
- Right-clicking a folder offers **Delete**, which — after a confirmation that names recursive deletion — removes the directory and everything inside it. The search index drops every contained note and the editor closes if an open note was inside.
- `.templates/` and any other dot-prefixed directory are visible and creatable.
- The filesystem-touching services are backed by `IFileSystem`; `NoteTreeBuilder`, the new folder validator, and the folder creator have unit tests using `MockFileSystem`. The existing test suite stays green.

### Key Discoveries:

- Scanner is the single source feeding both tree and index — the index must keep receiving files only (`NoteSearchIndex.cs:182`). Decision: file enumeration stays in the scanner; the **builder** gains directory enumeration, so the index contract is never touched.
- Template `.md` files already render under `.templates/` today because the scanner's dot-skip applies to **filenames**, not directories (`WorkspaceScanner.cs:26-30`). Showing dot-directories as first-class folders is therefore consistent, not new behavior.
- Recursive delete maps onto the existing per-file `NoteDeletedMessage` with no new message type: walking `node.Children` and emitting one message per contained file node reuses the single-key index removal and exact-match editor-clear, and correctly closes an open note inside the deleted folder (`NoteSearchIndex.cs:88-100`, `NoteEditorViewModel.cs:117-128`).
- DI registers services as singletons in `Program.cs:36-53` (the file is the source of truth over AGENTS.md's "transient" note for VMs); new validators/services slot beside `INewNoteNameValidator` (`:47`) and `INoteDeleter` (`:40`).

## What We're NOT Doing

- **Folder rename** — out of scope; a separate change. Users delete + recreate until then.
- **Migrating pure `Path` string operations** to `IFileSystem.Path` in ViewModels and `NoteSearchIndex` — only services that call `File`/`Directory` move.
- **A `FolderDeletedMessage` / prefix-based message type** — superseded by the per-file fan-out.
- **Changing the `IWorkspaceScanner` contract** (`ScanMarkdownFiles` signature/return) — it stays exactly as-is so the index is undisturbed.
- **Drag-and-drop move, multi-select delete, or folder-level metadata** — not requested.

## Implementation Approach

Build bottom-up. Phase 1 lays the `IFileSystem` foundation with zero behavior change, so every later phase can do testable IO. Phase 2 makes empty folders appear (the load-bearing Option B work) by giving the builder directory awareness while leaving the scanner — and therefore the index — alone. Phase 3 (delete) and Phase 4 (create) are the two user-facing commands, each mirroring an existing pattern (Delete-note for delete, New-note for create). Phases 3 and 4 both depend on the directory-aware tree from Phase 2 to render their results correctly.

## Critical Implementation Details

- **Builder ordering & parity with current output** — the builder currently lists folders before files, each `OrdinalIgnoreCase`-sorted (`NoteTreeBuilder.cs:16-17,42-52`). Directory enumeration must merge into the **same** folder set so a folder that both contains files and exists on disk produces exactly one node, not a duplicate — merge directory names into the existing `folderGroups` keying before materializing children.
- **Dot-directories are shown, dot-files are still skipped** — the builder/scanner must not extend the filename dot-skip to directories, or `.templates/` disappears and blocks the dependent `templates` change.
- **Root is never deletable** — a `Folder` node with empty `RelativePath` is the synthetic root (`NoteTreeBuilder.cs:11`); both the runtime guard and `CanDeleteNote` must reject it even after folders become deletable.

## Phase 1: Filesystem Abstraction Foundation

### Overview

Introduce `System.IO.Abstractions`, register a single `IFileSystem`, and migrate the five IO-performing services to use it. Pure behavior-preserving refactor — no feature change, no message change, no UI change.

### Changes Required:

#### 1. Package references

**File**: `Notes/Notes.csproj`, `Notes.Tests/Notes.Tests.csproj`

**Intent**: Add the filesystem abstraction to the app and the mock helpers to the test project.

**Contract**: `Notes.csproj` gains `<PackageReference Include="System.IO.Abstractions" />` (current stable from the TestableIO org; this transitively pulls `TestableIO.System.IO.Abstractions.Wrappers`). `Notes.Tests.csproj` gains `<PackageReference Include="System.IO.Abstractions.TestingHelpers" />`. Pin explicit versions consistent with the repo's existing pinning style.

#### 2. DI registration

**File**: `Notes/Program.cs`

**Intent**: Provide one real `IFileSystem` for the whole app.

**Contract**: Register `services.AddSingleton<IFileSystem, FileSystem>()` (from `System.IO.Abstractions`), near the top of the service registrations so it precedes the services that now depend on it.

#### 3. Migrate IO services to `IFileSystem`

**File**: `Notes/Services/NoteFileService.cs`, `Notes/Services/NoteDeleter.cs`, `Notes/Services/NewNoteNameValidator.cs`, `Notes/Services/SettingsService.cs`, `Notes/Services/WorkspaceScanner.cs`

**Intent**: Replace static `File.*` / `Directory.*` calls with calls through an injected `IFileSystem`, so these services can be unit-tested against `MockFileSystem`.

**Contract**: Each service takes `IFileSystem` as a constructor dependency and routes IO through `_fileSystem.File` / `_fileSystem.Directory` / `_fileSystem.Path`. Public method signatures are unchanged. Notes:
- `WorkspaceScanner.ScanMarkdownFiles` keeps its exact signature and return contract (`IReadOnlyList<string>`, ordinal-sorted relative paths, dot-filename skip) — only the enumeration source swaps to `_fileSystem.Directory.EnumerateFiles` with the same `EnumerationOptions`.
- `SettingsService` currently has a parameterless constructor used as the default DI path and a second `(string configFilePath)` constructor used by tests (`SettingsService.cs:11-19`). Preserve a test seam: keep a path-accepting construction route while adding the `IFileSystem` dependency (e.g. `(IFileSystem, string?)` with the default path computed when null). Verify existing `SettingsService` tests still compile and pass.
- `NewNoteNameValidator` swaps `File.Exists` → `_fileSystem.File.Exists`; `Path.Combine`/`Path.GetInvalidFileNameChars` may stay static (pure) or move to `_fileSystem.Path` for consistency — implementer's discretion, no behavior change either way.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- Full suite passes unchanged: `dotnet test`

#### Manual Verification:

- App launches, loads a workspace, opens/saves/deletes a note, and changes the workspace folder — all behave exactly as before (smoke test of every migrated service path).

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 2: Directory-aware Tree

### Overview

Give `NoteTreeBuilder` directory awareness so empty folders persist in the tree, using the `IFileSystem` foundation from Phase 1. The scanner and the search-index build path are untouched.

### Changes Required:

#### 1. Builder enumerates directories

**File**: `Notes/Services/NoteTreeBuilder.cs`

**Intent**: Materialize folder nodes from the actual directories on disk — even when they contain no `.md` files — while still building the file tree from the scanned file paths.

**Contract**: Inject `IFileSystem`. Change `Build` to `NoteTreeNode Build(string rootDirectory, IReadOnlyList<string> relativePaths)`. The existing path-splitting logic still groups files into folders; additionally, enumerate sub-directories of `rootDirectory` recursively (relative paths, `'/'`-normalized, ordinal-sorted, consistent with the scanner's normalization) and merge each directory into the folder set so that:
- a directory with no files still yields a `Folder` node,
- a directory that also contains files yields exactly **one** node (no duplicate),
- dot-prefixed directories are included (do **not** skip them),
- folder-before-file ordering and `OrdinalIgnoreCase` sorting are preserved.

Empty workspace (`rootDirectory` missing) returns the synthetic empty root as today.

#### 2. ViewModel passes the workspace path

**File**: `Notes/ViewModels/NoteTreeViewModel.cs`

**Intent**: Feed the builder the workspace root alongside the scanned files.

**Contract**: In `LoadTree` (`:122-134`), call `_treeBuilder.Build(_workspacePath, paths)`. No other change; `_scanner.ScanMarkdownFiles` still supplies `paths` and still independently feeds the index.

#### 3. Builder unit tests

**File**: `Notes.Tests/` (new test file for `NoteTreeBuilder`)

**Intent**: Lock the empty-folder and no-duplicate behavior with a mocked filesystem.

**Contract**: Using `MockFileSystem`, assert: an empty directory produces a `Folder` node; a directory with files produces one node (not two); a dot-prefixed directory (e.g. `.templates`) is present; nesting and ordering match the previous builder output for the file-only cases. Test names follow `Method_WhenScenario_ExpectedBehaviour`.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- New builder tests pass and full suite is green: `dotnet test`

#### Manual Verification:

- Create an empty folder on disk inside the workspace, reload/reopen the workspace → the empty folder appears in the tree.
- `.templates/` (if present) appears in the tree.
- An existing folder that contains notes still shows once, with its notes nested correctly and in the same order as before.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 3: Folder Delete

### Overview

Allow deleting a folder node and everything inside it, reusing the per-file `NoteDeletedMessage` fan-out so the index and editor react correctly with no new message type.

### Changes Required:

#### 1. Recursive deleter method

**File**: `Notes/Services/INoteDeleter.cs`, `Notes/Services/NoteDeleter.cs`

**Intent**: Add directory deletion alongside the existing file deletion.

**Contract**: Add `void DeleteFolder(string absolutePath)` to `INoteDeleter`, implemented as `_fileSystem.Directory.Delete(absolutePath, recursive: true)`. Existing `Delete(absolutePath)` (file) is unchanged.

#### 2. Relax delete gates and fan out messages

**File**: `Notes/ViewModels/NoteTreeViewModel.cs`

**Intent**: Permit deleting non-root folder nodes, confirm with recursive wording, delete the directory, and notify the index/editor once per contained note.

**Contract**:
- Runtime guard (`:139`) and `CanDeleteNote` (`:159-160`) accept a `Folder` node **only when** `RelativePath` is non-empty; the synthetic root (empty `RelativePath`) and `null` remain rejected.
- For a folder: confirmation copy must signal recursion (e.g. "Delete this folder and all notes inside it?"); on confirm, call `_noteDeleter.DeleteFolder(absolutePath)`, then for **every descendant file node** (recursive walk of `node.Children`, file nodes carry `RelativePath`) send a `NoteDeletedMessage(file.RelativePath)`; then `await LoadTreeCommand.ExecuteAsync(null)`.
- For a file: behavior is exactly as today.

A small private recursive helper that yields the descendant file nodes is the only non-obvious piece; everything else follows the existing `DeleteNote` flow.

#### 3. Folder delete context-menu wiring

**File**: `Notes/Views/NoteTreeView.axaml`

**Intent**: Ensure the existing Delete menu item also fires for folder nodes.

**Contract**: The existing `Delete` `MenuItem` already binds `DeleteNoteCommand` with the right-clicked node as parameter (`:18-20`); once `CanDeleteNote` accepts folders, no markup change is strictly required. Confirm the command re-evaluates `CanExecute` for folder nodes (it is parameter-driven, so it does). Optionally relabel for clarity, but a single "Delete" item covering both is acceptable.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- Suite passes: `dotnet test`

#### Manual Verification:

- Right-click a folder containing notes → Delete → confirmation names recursive deletion → folder and all contained notes disappear from the tree.
- If one of the deleted notes was open in the editor, the editor returns to the empty state.
- A subsequent search no longer returns any of the deleted notes.
- Attempting to delete produces no action on the synthetic root (not reachable in UI, but guarded).
- Deleting an empty folder works and confirms.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Phase 4: New Folder

### Overview

Add a New Folder command — reachable from the tree context menu, the File menu, and `Ctrl+Shift+N` — that validates the name, creates the directory, refreshes the tree, and selects the new folder.

### Changes Required:

#### 1. Folder name validator

**File**: `Notes/Services/INewFolderNameValidator.cs` + `Notes/Services/NewFolderNameValidator.cs` (new)

**Intent**: Validate a folder name with the same character rules as notes but without the `.md` append and with a directory-collision check.

**Contract**: Mirror `NewNoteNameValidator` (`NewNoteNameValidator.cs:10-48`): same empty/invalid-character rules, returning the existing `NoteNameResult` DU. Differences: do **not** append `.md`; collision check uses `_fileSystem.Directory.Exists` (and should also reject when a **file** of that name exists). `Success.FileName` carries the folder name; `Success.AbsolutePath` the target directory path. Inject `IFileSystem`. Register in `Program.cs` beside `INewNoteNameValidator`.

#### 2. Folder creation service

**File**: `Notes/Services/INoteFolderService.cs` + `Notes/Services/NoteFolderService.cs` (new)

**Intent**: Create a directory on disk behind a service, symmetric with `INoteDeleter`.

**Contract**: `void Create(string absolutePath)` → `_fileSystem.Directory.CreateDirectory(absolutePath)`. Inject `IFileSystem`. Register in `Program.cs`.

#### 3. New Folder command on the tree ViewModel

**File**: `Notes/ViewModels/NoteTreeViewModel.cs`

**Intent**: Drive the prompt → validate → create → reload → select flow, resolving the parent the same way New Note does.

**Contract**: Add `[RelayCommand] Task NewFolder(NoteTreeNode? node)` (context-menu path) and handle a new `NewFolderRequestedMessage` (menu/keyboard path) via a shared private `HandleNewFolder` that mirrors `HandleNewNote` (`:80-120`): resolve parent (`ResolveParentRelativePath` for the message path using `SelectedNode`; the passed `node` for the context-menu path), call `_newFolderDialog`/reuse `INewNoteDialogService.PromptForName(display, validate)` with `_newFolderValidator`, on success call `_noteFolderService.Create(success.AbsolutePath)`, `await LoadTreeCommand`, then select the new folder via `FindNode`. Inject the new validator and folder service; register `IRecipient<NewFolderRequestedMessage>` on the class and in `RegisterAll`. No `NoteSavedMessage` is sent (a folder has no content to index).

#### 4. Message + MainWindow entry points

**File**: `Notes/Messaging/Messages.cs`, `Notes/ViewModels/MainWindowViewModel.cs`, `Notes/MainWindow.axaml`

**Intent**: Provide always-available New Folder access symmetric with New Note.

**Contract**: Add `public sealed record NewFolderRequestedMessage;`. Add `[RelayCommand] void NewFolder()` on `MainWindowViewModel` that sends it (mirror `NewNote`, `MainWindowViewModel.cs:78-82`). In `MainWindow.axaml` add a `File → New Folder…` `MenuItem` bound to `NewFolderCommand` with `InputGesture="Ctrl+Shift+N"` and a matching `KeyBinding Gesture="Ctrl+Shift+N"` (mirror `:13,20`).

#### 5. Context-menu item

**File**: `Notes/Views/NoteTreeView.axaml`

**Intent**: Offer New Folder on right-click.

**Contract**: Add a `New Folder` `MenuItem` binding `NewFolderCommand` with `CommandParameter="{Binding}"`, mirroring the existing Delete item (`:18-20`).

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- New validator tests (valid name, invalid characters, directory collision, file-name collision) pass and suite is green: `dotnet test`

#### Manual Verification:

- `Ctrl+Shift+N` and File → New Folder… both prompt for a name and create a folder at the workspace root when nothing is selected, or inside the selected folder otherwise.
- Right-click a folder → New Folder creates a child folder; the new folder appears and is selected.
- The created folder persists on reload (validates Phase 2 integration) and is immediately usable as a New Note target.
- Invalid names (empty, containing `/` or `\`, OS-invalid characters) and names colliding with an existing folder are rejected with a message; no directory is created.
- Creating `.templates` via New Folder works and the folder is visible (unblocks `templates`).

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before concluding the change. Phase blocks use plain bullets — the corresponding `- [ ]` checkboxes for these items live in the `## Progress` section at the bottom of the plan.

---

## Testing Strategy

### Unit Tests:

- `NoteTreeBuilder` (Phase 2): empty directory yields a folder node; directory-with-files yields one node not two; dot-directory present; ordering/nesting parity with prior output. Mocked via `MockFileSystem`.
- `NewFolderNameValidator` (Phase 4): valid name → `Success`; `/`, `\`, and `Path.GetInvalidFileNameChars` → `Failure`; existing directory → collision `Failure`; existing file of same name → `Failure`.
- Migrated services (Phase 1): existing tests must keep passing against the `IFileSystem`-backed implementations; add focused `WorkspaceScanner` tests with `MockFileSystem` now that the seam exists (dot-filename skip, ordinal sort, recursion).

### Integration Tests:

- Not adding new headless UI tests; the message/command flows are covered by manual verification and the existing Avalonia.Headless harness conventions.

### Manual Testing Steps:

1. Phase 1 smoke: load workspace, open/save/delete a note, change workspace folder — unchanged behavior.
2. Create an empty folder on disk → reload → it appears (Phase 2).
3. Right-click folder → Delete → recursive confirmation → folder + notes gone, open note closed, search clean (Phase 3).
4. `Ctrl+Shift+N` / File menu / right-click → New Folder at root and nested; new folder selected and persists; invalid + colliding names rejected (Phase 4).
5. End-to-end: create `.templates`, add a note inside, confirm it shows and is searchable.

## Performance Considerations

The builder now performs a recursive directory enumeration in addition to the scanner's recursive file enumeration — two passes over the workspace per tree load instead of one. At single-user note-tree scale this is negligible. Both use `EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }` for consistency. No change to the search-index build cost.

## Migration Notes

No data migration. The `System.IO.Abstractions` adoption is internal refactoring with identical runtime behavior (the real `FileSystem` delegates to the same `System.IO` calls). Existing settings files, workspaces, and notes are unaffected.

## References

- Related research: `context/changes/note-tree-folder-management/research.md`
- Change identity: `context/changes/note-tree-folder-management/change.md`
- Originating research: `context/changes/templates/research.md`
- Patterns mirrored: New Note flow `Notes/ViewModels/NoteTreeViewModel.cs:80-120,162-177`; Delete flow `:136-160`; validator `Notes/Services/NewNoteNameValidator.cs`; DI `Notes/Program.cs:36-53`; menu/keybindings `Notes/MainWindow.axaml:13,19-22`.

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Filesystem Abstraction Foundation

#### Automated

- [x] 1.1 Build succeeds: `dotnet build` — d3e32a5
- [x] 1.2 Full suite passes unchanged: `dotnet test` — d3e32a5

#### Manual

- [x] 1.3 App launches; open/save/delete a note and change workspace folder behave exactly as before — d3e32a5

### Phase 2: Directory-aware Tree

#### Automated

- [x] 2.1 Build succeeds: `dotnet build` — 2b34711
- [x] 2.2 New builder tests pass and full suite is green: `dotnet test` — 2b34711

#### Manual

- [x] 2.3 Empty folder on disk appears in the tree after reload — 2b34711
- [x] 2.4 `.templates/` (if present) appears in the tree — 2b34711
- [x] 2.5 Folder with notes shows once, nested correctly, same order as before — 2b34711

### Phase 3: Folder Delete

#### Automated

- [x] 3.1 Build succeeds: `dotnet build`
- [x] 3.2 Suite passes: `dotnet test`

#### Manual

- [x] 3.3 Delete a folder with notes → recursive confirmation → folder and notes disappear
- [x] 3.4 Editor returns to empty state if a deleted note was open
- [x] 3.5 Search no longer returns the deleted notes
- [x] 3.6 Synthetic root is guarded (no delete); empty folder deletes with confirmation

### Phase 4: New Folder

#### Automated

- [ ] 4.1 Build succeeds: `dotnet build`
- [ ] 4.2 Validator tests (valid, invalid chars, dir collision, file collision) pass and suite is green: `dotnet test`

#### Manual

- [ ] 4.3 `Ctrl+Shift+N` and File → New Folder… create at root (no selection) or inside selected folder
- [ ] 4.4 Right-click folder → New Folder creates a selected child folder
- [ ] 4.5 Created folder persists on reload and works as a New Note target
- [ ] 4.6 Invalid and colliding names rejected with a message; no directory created
- [ ] 4.7 Creating `.templates` works and the folder is visible
