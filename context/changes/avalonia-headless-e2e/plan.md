# Plan: Avalonia headless UI E2E tests (minimal slice)

## Goal

Add Avalonia headless UI tests for the two primary user flows that can only break at the assembled UI boundary:
1. Create a new note end-to-end (menu → dialog → tree selection → editor).
2. Select a note, edit it, and have auto-save persist the change.

This reverses the prior test-plan assumption that GUI/E2E is out of scope.

## Background

- `context/changes/avalonia-headless-e2e/research.md` grounds the real UI surfaces and harness requirements.
- `Notes.E2ETests/` exists as an empty directory with stale `obj/` artifacts; we create the project from scratch.
- The existing `Notes.Tests/` project already uses `Avalonia.Headless.XUnit` and provides the `TestApp` pattern.

## Non-goals

- Do not duplicate coverage already owned by `Notes.Core.Tests` / `Notes.Tests` (template pipeline, file-safety guardrails, name validation logic).
- Do not add full GUI automation for every menu item, dialog, or preview mode.
- Do not test Avalonia framework internals.

## Phase 1: Bootstrap `Notes.E2ETests`

Create the E2E test project and wire it into the solution.

### Changes Required

1. Add `Notes.E2ETests/Notes.E2ETests.csproj` with:
   - `TargetFramework`: `net10.0`
   - `Nullable`: `enable`
   - `IsPackable`: `false`
   - `IsTestProject`: `true`
   - `OutputType`: `Exe`
   - `UseMicrosoftTestingPlatformRunner`: `true`
   - `TestingPlatformDotnetTestSupport`: `true`
   - Package references: `xunit.v3` 3.2.2, `Avalonia.Headless.XUnit` 12.0.3, `System.IO.Abstractions.TestingHelpers` 22.1.1, `NSubstitute` 5.3.0.
   - Project references: `Notes`, `Notes.Core`, `Notes.Tests` (to reuse `InMemoryNoteFileService` if appropriate, or copy it).
2. Add `Notes.E2ETests` to `Notes.slnx`.
3. Clean stale `Notes.E2ETests/obj/` artifacts from the previous empty project attempt.

### Success Criteria

- `dotnet build` succeeds for the solution.
- `dotnet test --filter "FullyQualifiedName~Notes.E2ETests"` runs and reports zero tests (none written yet).

### Automated

- [ ] 1.1 Create `Notes.E2ETests.csproj` matching the existing test project conventions.
- [ ] 1.2 Add `Notes.E2ETests` to `Notes.slnx`.
- [ ] 1.3 Verify `dotnet build` and `dotnet test` work for the new project.

### Manual

- [ ] Confirm the project loads cleanly in the IDE with no stale-obj warnings.

## Phase 2: Build the headless test harness

Create the test app, fake services, and window/control helpers that all E2E tests share.

### Changes Required

1. Add `Notes.E2ETests/TestApp.cs`:
   - A headless `Application` subclass.
   - `[assembly: AvaloniaTestApplication(typeof(TestApp))]`.
   - A static helper to build and assign `App.Services` with test doubles before the main window is shown.
   - Use `AppBuilder.Configure<TestApp>().UseHeadless(...)`.

2. Add fake/test-only services under `Notes.E2ETests/Fakes/`:
   - `FakeFolderPicker : IFolderPicker` — returns a configured workspace path.
   - `FakeSettingsService : ISettingsService` — returns empty settings so `InitializeAsync` picks the fake folder.
   - Reuse or copy `InMemoryNoteFileService` from `Notes.Tests/Fakes/` if referencing `Notes.Tests` is acceptable; otherwise copy it into `Notes.E2ETests/Fakes/`.

3. Add `Notes.E2ETests/E2ETestBase.cs`:
   - Per-test setup: create a unique workspace path (e.g., `/test-workspace-<guid>`), fresh `MockFileSystem`, fresh `StrongReferenceMessenger`, build service provider, assign `App.Services`, show `MainWindow`, wait for `InitializeAsync` to complete.
   - Per-test teardown: close the window, cancel any pending auto-save.
   - Helper methods:
     - `Window MainWindow { get; }`
     - `T FindControl<T>(string name)` — uses `NameScope` or visual-tree search.
     - `Task ClickButtonAsync(string name)`
     - `Task SetTextBoxTextAsync(string name, string text)`
     - `Task SelectTreeItemAsync(string headerText)`
     - `string GetEditorText()` — reads `AvaloniaEdit.TextEditor.Text`.
     - `void FlushAutoSave()` — accesses the `IAutoSaveScheduler` and calls `Flush()`.

4. Register services in the test provider:
   - Use `MockFileSystem` for `IFileSystem`.
   - Use `FakeFolderPicker` for `IFolderPicker`.
   - Use `FakeSettingsService` for `ISettingsService`.
   - Use `StrongReferenceMessenger` for `IMessenger`.
   - Use real `WorkspaceScanner`, `NoteTreeBuilder`, `NoteFileService`, `NameValidator`, `NoteFolderService`, `AutoSaveScheduler`, `NoteDeleter`, etc. — the E2E test exercises the real service stack.
   - Keep real `NewNoteDialogService` so the actual dialog window opens; do not stub dialog services in these tests.

### Success Criteria

- A sample `[AvaloniaFact]` that only opens the main window and asserts the tree is visible passes.
- Each test gets an isolated workspace; no file or messenger state leaks between tests.

### Automated

- [ ] 2.1 Add `TestApp.cs` with headless app builder and test service provider setup.
- [ ] 2.2 Add fake `IFolderPicker` and `ISettingsService`.
- [ ] 2.3 Add `E2ETestBase` with per-test isolation and control helpers.
- [ ] 2.4 Add a smoke test proving the harness can show `MainWindow` and load the tree.

### Manual

- [ ] Run the smoke test in the IDE debugger and confirm no real folder picker appears.

## Phase 3: Create new note end-to-end

Add the first E2E test: File → New Note → enter name → note appears in tree and editor.

### Changes Required

1. Add `Notes.E2ETests/CreateNewNoteTests.cs`.
2. Test `CreateNewNote_WhenNameProvided_SelectsNoteInTreeAndEditor`:
   - Arrange: pre-seed `MockFileSystem` with empty workspace `/workspace`.
   - Act: click the `_File` menu → `_New Note…` (or use `Ctrl+N` key gesture) → set dialog `NameInput` to `ideas` → click `CreateButton` → wait for tree reload.
   - Assert:
     - `MockFileSystem` contains `/workspace/ideas.md`.
     - The tree has a selected item with text `ideas.md`.
     - The editor is visible and empty (new blank note).
     - `NoteEditorViewModel.PaneState` is `Editing`.

3. Test `CreateNewNote_WhenDialogCancelled_CreatesNoFile`:
   - Act: open New Note dialog → click `CancelButton`.
   - Assert: workspace remains empty; tree has no selected note; editor is in `Empty` state.

4. Test `CreateNewNote_WhenNameInvalid_CreateButtonDisabled`:
   - Act: open dialog → type `bad/name`.
   - Assert: `CreateButton.IsEnabled` is `false`; `ErrorText.IsVisible` is `true`.

### Success Criteria

- All three tests pass against the real UI.
- Tests use only role/name-based control access (no fragile pixel or coordinate queries).
- Tests are independent and any-order safe.

### Automated

- [ ] 3.1 Implement happy-path create-new-note test.
- [ ] 3.2 Implement cancelled-dialog test.
- [ ] 3.3 Implement validation-disabled-button test.

### Manual

- [ ] Deliberately break `NewNoteDialog.OnCreate` (e.g., always set `_result = null`) and confirm the happy-path test fails.

## Phase 4: Select note → edit → auto-save

Add the second E2E test: select an existing note, type in the editor, wait/flush auto-save, verify file content.

### Changes Required

1. Add `Notes.E2ETests/EditAndAutoSaveTests.cs`.
2. Test `SelectNote_WhenClicked_LoadsContentIntoEditor`:
   - Arrange: pre-seed `/workspace/existing.md` with `# Hello\n\nworld`.
   - Act: wait for tree load, select `existing.md` tree item.
   - Assert: editor text equals `# Hello\n\nworld`; `PaneState` is `Editing`.

3. Test `EditNote_WhenTextChanged_AutoSavesAfterDelay`:
   - Arrange: select `existing.md`.
   - Act: clear editor, type `# Updated`, wait for `AutoSaveScheduler` interval (or flush via helper), then select a different tree item or close to force flush.
   - Assert: `MockFileSystem` file `/workspace/existing.md` contains `# Updated`.

4. Test `EditNote_WhenSwitchedWithoutChange_KeepsOriginalContent`:
   - Arrange: select `existing.md`.
   - Act: select another note (or none) without editing.
   - Assert: file content unchanged.

### Success Criteria

- Auto-save is observable through the real `DispatcherTimer` path.
- Editor text changes propagate through `TextEditor.TextChanged` → `NoteEditorViewModel.OnEditorTextChanged`.

### Automated

- [ ] 4.1 Implement select-note-loads-content test.
- [ ] 4.2 Implement edit-auto-save test.
- [ ] 4.3 Implement no-edit-no-save test.

### Manual

- [ ] Deliberately break `NoteEditorView.OnEditorTextChanged` wiring (e.g., remove the call to `OnEditorTextChanged`) and confirm the auto-save test fails.

## Phase 5: Update test plan and solution wiring

Make the new E2E layer discoverable and documented.

### Changes Required

1. Update `context/foundation/test-plan.md`:
   - §4 Stack: add row for Avalonia headless UI tests with `Avalonia.Headless.XUnit`.
   - §5 Quality Gates: add `Notes.E2ETests` to the local test command (optional CI gate? keep local-only first).
   - §7 What We Deliberately Don't Test: revise the GUI/E2E exclusion to say "Full GUI automation is out; scoped Avalonia headless smoke tests for cross-control flows are in."
   - §6 Cookbook: add "Adding an Avalonia headless UI test" pattern referencing the new project.

2. Verify `dotnet test` from repo root runs all four projects including `Notes.E2ETests`.

### Success Criteria

- `dotnet test` at repo root passes.
- Test plan reflects the new E2E scope.

### Automated

- [ ] 5.1 Update `test-plan.md` §4, §5, §6, §7.
- [ ] 5.2 Verify full `dotnet test` pass.

### Manual

- [ ] Review the cookbook pattern for accuracy against the final harness code.

## Progress

- [x] 1.1 Create `Notes.E2ETests.csproj` matching the existing test project conventions. — 066440ee
- [x] 1.2 Add `Notes.E2ETests` to `Notes.slnx`. — 066440ee
- [x] 1.3 Verify `dotnet build` and `dotnet test` work for the new project. — 066440ee
- [x] 2.1 Add `TestApp.cs` with headless app builder and test service provider setup.
- [x] 2.2 Add fake `IFolderPicker` and `ISettingsService`.
- [x] 2.3 Add `E2ETestBase` with per-test isolation and control helpers.
- [x] 2.4 Add a smoke test proving the harness can show `MainWindow` and load the tree.
- [ ] 3.1 Implement happy-path create-new-note test.
- [ ] 3.2 Implement cancelled-dialog test.
- [ ] 3.3 Implement validation-disabled-button test.
- [ ] 4.1 Implement select-note-loads-content test.
- [ ] 4.2 Implement edit-auto-save test.
- [ ] 4.3 Implement no-edit-no-save test.
- [ ] 5.1 Update `test-plan.md` §4, §5, §6, §7.
- [ ] 5.2 Verify full `dotnet test` pass.
