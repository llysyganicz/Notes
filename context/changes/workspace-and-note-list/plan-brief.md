# Workspace Selection, Note List, and Delete — Plan Brief

> Full plan: `context/changes/workspace-and-note-list/plan.md`

## What & Why

Deliver the first vertical slice of the Notes app: the user picks a notes folder, sees every `.md` file in it as a hierarchical tree, and can delete a note with confirmation. This slice is the foundation every other slice (S-02 editor, S-03 search, S-04 templates) inherits from — the MVVM library, DI container, services pattern, and test project all land here.

## Starting Point

The codebase is an empty Avalonia 12 / .NET 10 scaffold from `dotnet new avalonia.app`: one `MainWindow` showing "Welcome to Avalonia!", no MVVM, no DI, no services, no test project, no solution file. `AGENTS.md` mandates MVVM with compiled bindings and code-behind only for UI wiring.

## Desired End State

The app launches, asks for a notes folder on first run, remembers it across restarts, and shows the folder's `.md` files in a `TreeView` grouped by subdirectory. A `File → Change Notes Folder…` menu re-opens the picker. Right-clicking a note → Delete → confirmation dialog → file gone, tree updates. `dotnet test` runs unit tests for the tree builder, settings service, and workspace scanner.

## Key Decisions Made

| Decision                          | Choice                                                                  | Why                                                                                                | Source |
| --------------------------------- | ----------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------- | ------ |
| MVVM library                      | `CommunityToolkit.Mvvm` (source generators)                             | Minimal boilerplate, no reflection, idiomatic in modern Avalonia samples                           | Plan   |
| DI container                      | `Microsoft.Extensions.DependencyInjection`                              | Conventional .NET choice; pairs naturally with the toolkit                                         | Plan   |
| Settings persistence              | `Environment.SpecialFolder.ApplicationData/Notes/settings.json`         | Single API covers `$XDG_CONFIG_HOME` on Linux and `%APPDATA%` on Windows; survives reinstalls      | Plan   |
| Note list UI                      | Hierarchical `TreeView` with expandable folder nodes                    | Scales to arbitrary depth; matches user mental model from VS Code / file explorers                 | Plan   |
| List scope                        | All `.md` recursively, no filtering — `.templates/` included            | User explicitly chose simplicity over hiding; revisit if S-04 templates make the list noisy        | Plan   |
| Delete behavior                   | `File.Delete` after confirmation dialog (no trash/recycle integration)  | User chose simplicity; confirmation dialog is the only safety net                                  | Plan   |
| First-launch flow                 | Show main window, then modal folder picker owned by it; cancel → app exits | User chose forcing the setup step over an empty-state welcome view; main window visible behind picker | Plan   |
| Test project                      | `Notes.Tests` with xUnit; tests for tree builder, settings, scanner     | Tree builder edge cases are genuinely easy to get wrong; foundation that future slices inherit     | Plan   |
| Confirmation dialog               | Custom minimal `Window` (no MessageBox package)                         | Avalonia has no built-in; adding a package for one dialog is overkill                              | Plan   |

## Scope

**In scope:**
- Per-OS settings persistence (XDG / AppData)
- First-launch modal folder picker; `File → Change Notes Folder…` menu
- Recursive `.md` scan + hierarchical tree
- TreeView with folder/file rendering
- Context-menu delete with custom confirmation dialog
- DI + services + `Notes.Tests` (xUnit) covering pure-logic services

**Out of scope:**
- Editor, preview, search, tags, templates (S-02–S-04)
- File-system watcher / external-edit detection
- Trash / recycle-bin / undo
- Empty-state welcome screen (user chose exit-on-cancel instead)
- macOS-specific work
- Drag/drop, rename, move in the tree
- UI polish beyond Fluent defaults

## Architecture / Approach

```
Program.cs ──builds──> ServiceProvider ──held by──> App
                                                     │
                                                     ▼
                          OnInit: resolve MainWindow + VM → Show → Start: load settings → clear if stale → maybe-pick → maybe-shutdown → LoadTreeCommand
                                                     │
        ┌────────────────────────────────────────────┴────────────────────────────────────────────┐
        ▼                                                                                          ▼
MainWindowViewModel                                                              MainWindow.axaml (TreeView + Menu)
  ├─ ISettingsService     (JSON read/write, atomic, per-OS path)
  ├─ IFolderPicker         (Avalonia IStorageProvider wrapper; self-resolves owner)
  ├─ IWorkspaceScanner     (Directory.EnumerateFiles ".md" recursive)
  ├─ NoteTreeBuilder       (pure: flat path list → NoteTreeNode hierarchy)
  ├─ INoteDeleter          (File.Delete wrapper)
  └─ IConfirmDialogService (modal Yes/No dialog wrapper; self-resolves owner)
```

Services are singletons; ViewModels and Windows are transient. The single piece of meaningful pure logic — `NoteTreeBuilder` — is the most tested.

## Phases at a Glance

| Phase                                          | What it delivers                                                       | Key risk                                                                       |
| ---------------------------------------------- | ---------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| 1. Foundation — MVVM, DI, services, tests     | All non-UI plumbing + unit tests; app behavior unchanged               | Misjudging `Notes.sln` shape / `net10.0` xUnit package versions                |
| 2. First-launch picker + main shell + menu     | Settings persist; folder picker on first run; menu lets user re-pick   | Async startup ordering — show window first, then await picker without racing the lifetime           |
| 3. TreeView + delete with confirmation         | End-to-end user value: see notes, delete with confirmation             | `HierarchicalDataTemplate` + `ContextMenu` binding to a command on parent VM   |

**Prerequisites:** none (roadmap-foundational slice).
**Estimated effort:** ~2–3 focused sessions across the three phases.

## Open Risks & Assumptions

- **Assumes** awaiting `IStorageProvider.OpenFolderPickerAsync` from inside an `async void` startup handler (after `MainWindow.Show()`) cooperates with the Avalonia desktop lifetime. If it doesn't, the fallback is to trigger the picker from `MainWindow.Opened` or `Loaded`.
- **Assumes** `xunit` + `Microsoft.NET.Test.Sdk` for `net10.0` are published and resolve cleanly. If `net10.0` is too new for current xUnit, fall back to `net9.0` for the test project only.
- The user opted to **show `.templates/`** in the regular list — flagged here so S-04 can revisit if it becomes noisy.
- The user opted for **permanent delete** rather than trash — flagged here against the PRD's "no data loss" guardrail; the confirmation dialog is the only safety net.

## Success Criteria (Summary)

- User can pick a notes folder on first launch and switch it later via menu
- All `.md` files under the workspace appear in a recursive folder-grouped tree
- User can delete a note via right-click → confirm → file removed, tree refreshes
- `dotnet test` is green for tree builder, settings service, workspace scanner
