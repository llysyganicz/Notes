# AGENTS.md

## Version Control

This repo uses **jujutsu (jj)**, not raw git. Use `jj` commands for all VCS operations.

## Conventions

- **MVVM pattern:** Keep business logic out of code-behind (.axaml.cs) files. Views bind to ViewModels; code-behind should only contain UI wiring.
- **MVVM library — CommunityToolkit.Mvvm:** ViewModels derive from `ObservableObject`; use `[ObservableProperty]` and `[RelayCommand]` source generators.
- **Dependency injection — Microsoft.Extensions.DependencyInjection:** services are registered in `Notes/Program.cs` and resolved through the static `App.Services` provider. Services are singletons; ViewModels and Windows are transients.
- **Compiled bindings** are enabled by default — use `x:DataType` in AXAML and avoid reflection-based bindings. **Exception:** the view-root `DataContext` hop through the `ViewModelLocator` (e.g. `DataContext="{ReflectionBinding Tree, Source={StaticResource Locator}}"`) must use `ReflectionBinding` because a `StaticResource` source has no `x:DataType` known at compile time. All other bindings inside the view use compiled form against the view's own `x:DataType`.
- **Share behavior across ViewModels via DI-injected services, not base-class hierarchies.** When two ViewModels need the same logic, register it as a service in `Notes/Program.cs` and inject it into both rather than introducing a `ViewModelBase` parent.
- **No `Async` suffix without a sync sibling:** prefer `Task LoadTree()` over `Task LoadTreeAsync()` — the `Task` return type already signals async. Keep the suffix only when a synchronous method with the same base name exists, or when the API is framework-owned (e.g. `IStorageProvider.OpenFolderPickerAsync`).
- **File system in tests — `MockFileSystem`:** inject `IFileSystem` (from `System.IO.Abstractions`) into services that touch disk; use `new MockFileSystem()` (from `System.IO.Abstractions.TestingHelpers`) in tests. Never access the real file system from `Notes.Tests/`.
- **Test method naming — `Method_WhenScenario_ExpectedBehaviour`:** three PascalCase segments separated by underscores, e.g. `Load_WhenFileMissing_ReturnsEmpty`, `Save_WhenParentDirectoryMissing_CreatesParentDirectory`. The expected-behaviour segment leads with a verb (`Returns`, `Creates`, `Throws`, `Calls`). Use `WhenCalled` when the test asserts a general property with no specific scenario.
- **Killing a surviving mutant — fix an existing test first:** when mutation testing (Stryker) surfaces a survivor, first look for an existing test that already exercises that code path and strengthen it — tighten a weak assertion, or tweak an input (e.g. drop a trailing newline, add a near-miss case). Only add a new test method when no existing test reaches that branch and none can be naturally extended onto it. Prefer one strengthened test over a new redundant one.

## Project Overview

Notes is a cross-platform desktop markdown note-taking app built with **Avalonia UI 12** on **.NET 10** (C#). Product requirements, domain concepts, and architectural decisions are documented in `context/foundation/` — read `prd.md` and `tech-stack.md` before making design decisions.

## Project Structure

- `Notes.slnx` — solution file at the repo root (XML format produced by .NET 10's `dotnet new sln`; `dotnet build` / `dotnet test` work without specifying the file).
- `Notes/` — main project (Avalonia app).
  - `Notes/Models/` — domain records: `AppSettings`, `NoteTreeNode`, `NoteMetadata`, `NoteSearchResult`, `EditorPaneState`, `FormDefinition`, `FormField`, `TemplateInfo`.
  - `Notes/Services/` — interfaces + implementations grouped by concern:
    - *Workspace & files:* `SettingsService`, `WorkspaceScanner`, `NoteFileService`, `NoteFolderService`, `NoteDeleter`, `NameValidator`
    - *Editor:* `AutoSaveScheduler`
    - *Search:* `NoteSearchIndex`, `NoteMetadataParser`
    - *Templates:* `TemplateCatalog`, `TemplateParser`, `TemplateRenderer`
    - *UI dialogs:* `ConfirmDialogService`, `NewNoteDialogService`, `TemplatePickerDialogService`, `TemplateFormDialogService`, `AvaloniaFolderPicker`
  - `Notes/ViewModels/` — CommunityToolkit.Mvvm view models: `MainWindowViewModel`, `NoteTreeViewModel`, `NoteEditorViewModel`, `NoteSearchViewModel`, `TemplatePickerViewModel`, `TemplateFormViewModel`, `ViewModelLocator`; `Fields/` subdirectory holds per-field-type VMs (`FieldVm`, `TextFieldVm`, `DateFieldVm`, `NumberFieldVm`, `SelectFieldVm`).
  - `Notes/Views/` — views and dialogs: `NoteTreeView`, `NoteEditorView`, `SearchView`, `ConfirmDialog`, `NewNoteDialog`, `TemplatePickerDialog`, `TemplateFormDialog`.
  - `Notes/Messaging/` — `Messages.cs` — WeakReferenceMessenger message types used for cross-VM communication.
  - `Notes/App.axaml(.cs)`, `Notes/Program.cs`, `Notes/MainWindow.*` — application root, DI composition root, shell window.
- `Notes.Tests/` — xUnit test project; pure-logic services and view models only.
  - `Notes.Tests/Fakes/` — `InMemoryNoteFileService` and other in-process fakes.
  - `Notes.Tests/TestApp.cs` — minimal Avalonia app bootstrap required by ViewModel tests that touch Avalonia primitives.

## Build & Run

```sh
dotnet build                   # builds Notes + Notes.Tests
dotnet run --project Notes     # launches the desktop app
dotnet test                    # runs the xUnit suite in Notes.Tests/
```

Publish a self-contained binary:
```sh
dotnet publish Notes/Notes.csproj -c Release -r linux-x64 --self-contained
dotnet publish Notes/Notes.csproj -c Release -r win-x64 --self-contained
```
