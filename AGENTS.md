# AGENTS.md

## Project Overview

Notes is a cross-platform desktop markdown note-taking app built with **Avalonia UI 12** on **.NET 10** (C#). Product requirements, domain concepts, and architectural decisions are documented in `context/foundation/` — read `prd.md` and `tech-stack.md` before making design decisions.

## Project Structure

- `Notes.slnx` — solution file at the repo root (XML format produced by .NET 10's `dotnet new sln`; `dotnet build` / `dotnet test` work without specifying the file).
- `Notes/` — main project (Avalonia app).
  - `Notes/Models/` — domain records (`AppSettings`, `NoteTreeNode`).
  - `Notes/Services/` — interfaces + Avalonia/IO-bound implementations (settings, scanner, tree builder, deleter, folder picker, confirm dialog).
  - `Notes/ViewModels/` — CommunityToolkit.Mvvm view models.
  - `Notes/Views/` — custom dialogs (e.g. `ConfirmDialog`).
  - `Notes/App.axaml(.cs)`, `Notes/Program.cs`, `Notes/MainWindow.*` — application root, DI composition root, shell window.
- `Notes.Tests/` — xUnit test project; pure-logic services only.

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

## Conventions

- **MVVM pattern:** Keep business logic out of code-behind (.axaml.cs) files. Views bind to ViewModels; code-behind should only contain UI wiring.
- **MVVM library — CommunityToolkit.Mvvm:** ViewModels derive from `ObservableObject`; use `[ObservableProperty]` and `[RelayCommand]` source generators rather than hand-rolling `INotifyPropertyChanged` and `ICommand`.
- **Dependency injection — Microsoft.Extensions.DependencyInjection:** services are registered in `Notes/Program.cs` and resolved through the static `App.Services` provider. Services are singletons; ViewModels and Windows are transients.
- **Compiled bindings** are enabled by default — use `x:DataType` in AXAML and avoid reflection-based bindings.
- **Share behavior across ViewModels via DI-injected services, not base-class hierarchies.** When two ViewModels need the same logic, register it as a service in `Notes/Program.cs` and inject it into both rather than introducing a `ViewModelBase` parent.
- **No `Async` suffix without a sync sibling:** prefer `Task LoadTree()` over `Task LoadTreeAsync()` — the `Task` return type already signals async. Keep the suffix only when a synchronous method with the same base name exists, or when the API is framework-owned (e.g. `IStorageProvider.OpenFolderPickerAsync`).

## Version Control

This repo uses **jujutsu (jj)**, not raw git. Use `jj` commands for all VCS operations.
