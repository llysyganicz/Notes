---
date: 2026-06-02T21:26:38+02:00
researcher: Claude (10x-research)
git_commit: 6b636e00a4678a61a5dc9eaae46809816b6bede1
branch: (detached HEAD at 6b636e0)
repository: Notes
topic: "S-04 Templates — create templates and generate notes from them via a typed form"
tags: [research, codebase, templates, yaml-frontmatter, dialogs, mvvm, scanner, editor]
status: complete
last_updated: 2026-06-02
last_updated_by: Claude (10x-research)
---

# Research: S-04 Templates — create templates and generate notes from them via a typed form

**Date**: 2026-06-02T21:26:38+02:00
**Researcher**: Claude (10x-research)
**Git Commit**: 6b636e00a4678a61a5dc9eaae46809816b6bede1
**Branch**: (detached HEAD at 6b636e0)
**Repository**: Notes

## Research Question

Map the existing codebase surface that the **templates** slice (roadmap S-04 — the north star) will build on. S-04 must let a user (a) create a template — a `.md` file in a `.templates/` subfolder whose YAML frontmatter declares typed field definitions (text, date, number, dropdown/select) and whose body carries `{{placeholder}}` tokens — and (b) create a new note from a template by filling a form generated from those fields, with placeholders replaced. **Scope expansion requested by the user beyond PRD FR-009:** also support applying/inserting a template's content into an **existing, already-open note**.

Focus areas (per scope alignment): **YAML & frontmatter handling**, **forms & dialogs**, and **template-into-existing-note insertion**.

## Summary

The codebase is a clean MVVM + Messenger architecture (CommunityToolkit.Mvvm) with DI singletons registered in `Program.cs`. Three findings shape the S-04 plan:

1. **Reading template frontmatter is well-supported; writing populated frontmatter is not.** There is exactly one frontmatter parser (`NoteMetadataParser`, Markdig `UseYamlFrontMatter` + YamlDotNet) but it only extracts `tags`, and **no YAML serializer exists anywhere** in the app. S-04 must (a) extend the parse model to carry typed field definitions and (b) introduce a new component that *emits* YAML frontmatter for the generated note.

2. **The "new note from template" output path is almost entirely reusable.** `NoteTreeViewModel.HandleNewNote()` already prompts for a name, validates, writes the file, broadcasts `NoteSavedMessage` (which auto-updates the search index), reloads the tree, and selects the note. The single seam is the literal `string.Empty` written as the new note's content (`NoteTreeViewModel.cs:110`) — S-04 substitutes rendered template body there.

3. **The "apply template to an existing open note" expansion has real gaps and is the riskiest part.** The editor's text actually lives in the AvaloniaEdit `TextEditor` control, not the ViewModel; there is **no caret/selection/insert API surfaced**, and setting the VM's `LoadedText` does *not* update the live save buffer `_currentEditorText`. Insert-at-cursor requires new code-behind plumbing. This capability is **not in PRD v1** and should be an explicit plan/PRD decision.

There is **no existing template, placeholder-substitution, or dynamic-form logic** of any kind — the template engine and the typed dynamic form are net-new. The `.templates/` convention is already half-wired: the **search index** filters it out at query time, but the **scanner and tree do not exclude it**, so `.templates/` currently shows as a folder in the note tree. S-01 and S-03 both explicitly deferred this exclusion decision to S-04.

## Detailed Findings

### Area 1 — YAML & frontmatter handling

**Libraries** (`Notes/Notes.csproj`):
- `YamlDotNet` `18.*` (`Notes.csproj:30`) — deserialization.
- `Markdig` `1.2.0` (`Notes.csproj:28`) — used for frontmatter *detection/extraction* via `Markdig.Extensions.Yaml`, not deserialization.
- `Notes.Tests` references no YAML package — it tests through the production assembly.

**The single parse site — `Notes/Services/NoteMetadataParser.cs`** (`INoteMetadataParser`):
- `NoteMetadata Parse(string? noteText)` (`NoteMetadataParser.cs:26`).
- Two-stage: a static `MarkdownPipeline` built `.UseYamlFrontMatter()` (`:15-17`) locates the leading `---`-delimited block via `Markdown.Parse(...).OfType<YamlFrontMatterBlock>().FirstOrDefault()` (`:33-35`). **No hand-rolled `---` splitting exists anywhere** — Markdig owns delimiter recognition.
- A static YamlDotNet `IDeserializer` with `.WithNamingConvention(LowerCaseNamingConvention.Instance).IgnoreUnmatchedProperties()` (`:19-22`) deserializes the block into a private nested DTO `FrontmatterShape { List<string?>? Tags }` (`:62-65`). **Only `tags` is read.**
- `NormalizeTags` (`:52-60`): trims, lowercases, keeps only `\A[a-z0-9-]+\z` (`:24`), drops blanks, dedupes preserving order.
- Malformed YAML → broad `catch (Exception)` → `NoteMetadata.Empty` (`:41-49`). **This breadth is deliberate** — narrowing to `catch (YamlException)` was proposed and rolled back; see `context/foundation/lessons.md` ("Don't narrow the malformed-YAML catch"). Do not re-flag.

**Serialization — NONE.** YamlDotNet's `ISerializer`/`SerializerBuilder` is never used. Notes are saved as opaque whole-file text:
- `INoteFileService.Save(absolutePath, text)` → `File.WriteAllText(..., Utf8NoBom)` (`Notes/Services/NoteFileService.cs:32-35`).
- Editor autosave writes the entire raw buffer verbatim (`NoteEditorViewModel.cs:164`); new notes write `string.Empty` (`NoteTreeViewModel.cs:110`). Frontmatter is never re-serialized.
- The only structured serialization in the repo is `System.Text.Json` for settings (`SettingsService.cs:32,48`) — unrelated.

**Models** (`Notes/Models/`): `NoteMetadata(IReadOnlyList<string> Tags)` is the *only* parsed-frontmatter model. `FrontmatterShape` is a private nested DTO, not in `Models/`. No typed-field / schema / default-value concept exists.

**S-04 implication:** reading a *template's* frontmatter can reuse the Markdig+YamlDotNet pattern, but S-04 must introduce (1) a richer model than `FrontmatterShape` to carry field definitions `{name, type, default, options}`, and (2) a **YAML emitter** to write populated frontmatter into the generated note. S-03's plan explicitly stated "S-04 (templates) will define the rest of the frontmatter shape" (`context/changes/tags-and-search/plan.md:66`).

### Area 2 — Forms & dialogs (basis for the typed dynamic form)

**Dialog return-value pattern (consistent across the app):** code-behind `Window` subclass + a private result field + a `static Task<T> Show(owner, …)` factory that does `await dialog.ShowDialog(owner)` and returns the field, wrapped in an `I…DialogService` that resolves the owner via `(Application.Current as App)?.MainWindow`. No `TaskCompletionSource`, no `ShowDialog<T>`, no dialog-ViewModels.

- **`ConfirmDialog`** (bool): `ConfirmDialog.axaml.cs:9` result field; `static Task<bool> Show(...)` (`:18-27`); service `IConfirmDialogService`/`ConfirmDialogService.cs:9-18`. No `x:DataType`, controls by `x:Name`.
- **`NewNoteDialog`** (the closest precedent — **returns data + takes a validation callback**): `_result` string (`:11`) and `Func<string,string?>? _validate` (`:10`); `static Task<string?> Show(owner, parentFolderDisplay, validate)` (`:21-34`); live validation `RefreshValidation()` (`:38-54`); service `INewNoteDialogService.PromptForName(...)` (`NewNoteDialogService.cs:10-19`); consumed in `NoteTreeViewModel.cs:91-95`.
- **Folder picker**: `IFolderPicker.PickFolder()` → `AvaloniaFolderPicker.cs:10-25` (`StorageProvider.OpenFolderPickerAsync`).

**ViewModelLocator + DataContext hop:** `<vm:ViewModelLocator x:Key="Locator"/>` (`App.axaml:9`); `ViewModelLocator.cs:6-18` resolves via `App.Services.GetRequiredService<T>()` (null in design mode). Every view-root uses `DataContext="{ReflectionBinding Prop, Source={StaticResource Locator}}"` (e.g. `MainWindow.axaml:9-10`, `SearchView.axaml:9-10`) — the documented `ReflectionBinding` exception — while **all inner bindings are compiled** against the view's `x:DataType`.

**DI composition root** (`Notes/Program.cs`): `BuildServiceProvider()` (`:32-58`). Services are `AddSingleton<IInterface, Impl>()` (`:36-48`, e.g. `INewNoteDialogService` `:48`); ViewModels `AddSingleton` (`:50-53`); only `MainWindow` is `AddTransient` (`:55`). Resolved via `public static App.Services` (`App.axaml.cs:15`).
> **Doc discrepancy to note:** CLAUDE.md says "ViewModels and Windows are transients," but in code ViewModels are singletons. A per-invocation form ViewModel should be `AddTransient` (matching doc intent) or constructed directly by the dialog factory as the existing dialogs do.

**Dynamic UI generation — NONE exists.** No `DataTemplateSelector`, no `ItemsControl`, no runtime control creation. Only homogeneous single-type templated lists: `TreeView` with one `TreeDataTemplate DataType="models:NoteTreeNode"` (`NoteTreeView.axaml:14`) and `ListBox` with one `DataTemplate DataType="models:NoteSearchResult"` (`SearchView.axaml:42`).

**S-04 implication:** the dialog/service/validation scaffolding is reusable (change `NewNoteDialog`'s `Task<string?>` return into a typed values record / `Dictionary<string,object>`). The **typed dynamic form is net-new**. The idiom that best matches the codebase: an `ItemsControl` bound to a collection of per-field-type ViewModels (`TextFieldVm`, `DateFieldVm`, `NumberFieldVm`, `SelectFieldVm`), each with its own implicit `DataTemplate DataType="..."` — consistent with the existing `DataType=`-keyed template convention and compiled bindings.

### Area 3 — Note create/save flow (output path for "new note from template")

**New-note flow (Messenger-driven):**
- `MainWindowViewModel.NewNote()` `[RelayCommand]` sends `NewNoteRequestedMessage` (`MainWindowViewModel.cs:78-82`; message at `Messaging/Messages.cs:11`).
- `NoteTreeViewModel.Receive(NewNoteRequestedMessage)` → `HandleNewNote()` (`NoteTreeViewModel.cs:68-78`, `:80-120`):
  - resolve target folder `ResolveParentRelativePath(SelectedNode)` (`:88`, `:162-177`);
  - prompt `_newNoteDialog.PromptForName(...)` (`:91-95`);
  - validate `_newNoteValidator.Validate(...)` → `NoteNameResult.Success(FileName, AbsolutePath)` (`:101`);
  - **write file** `_fileService.Save(success.AbsolutePath, string.Empty)` (`:110`) ← **the template-content seam**;
  - **index** `_messenger.Send(new NoteSavedMessage(newRelativePath, string.Empty))` (`:111`);
  - reload + select `LoadTreeCommand.ExecuteAsync(null)` then `SelectedNode = match` (`:113-119`), which sends `NoteSelectedMessage` and opens the note in the editor.
- **Filename validation/uniqueness** — `NewNoteNameValidator.cs`: trims/rejects empty (`:13-16`), rejects separators (`:18-21`) and invalid chars (`:23-29`), appends `.md` (`:31-33`), **rejects on collision** `File.Exists` (`:42-45`) — reject, not auto-dedup.
- **Save service**: `NoteFileService.Save` → `File.WriteAllText(..., Utf8NoBom)` (`:32-35`). **No parent-directory creation** — parent must already exist (gap if a template targets a new subfolder).

**Save/autosave:** `IAutoSaveScheduler` (`AutoSaveScheduler.cs`) — `DispatcherTimer` 500 ms (`:19`), `Bump()` restarts on each keystroke (`:23-27`), `Flush()`/`Cancel()` (`:29-43`). Wired `_scheduler.OnSave += DoSave` (`NoteEditorViewModel.cs:45`); `DoSave` saves `_currentEditorText` and sends `NoteSavedMessage` (`:153-175`).

**Visibility after create:** tree refresh is a **manual re-scan** (`LoadTree` → `IWorkspaceScanner.ScanMarkdownFiles` + `NoteTreeBuilder.Build`, `NoteTreeViewModel.cs:122-134`) — **there is no `FileSystemWatcher` anywhere**. Search index updates incrementally via `NoteSearchIndex.Receive(NoteSavedMessage)` (`NoteSearchIndex.cs:72-86`).

**S-04 implication:** "new note from template" reuses `HandleNewNote` almost wholesale — substitute rendered body for `string.Empty` at `:110-111`. Watch the no-directory-creation gap and the reject-on-collision behavior.

### Area 4 — Apply template to an EXISTING open note (scope expansion — gaps)

**Where the open note's text lives** (`Notes/ViewModels/NoteEditorViewModel.cs`):
- `_currentEditorText` — private field, the live buffer / save source of truth (`:25`).
- `[ObservableProperty] LoadedText` (`:27-28`) — **one-way push to the view only**, used to *load* text; **not** the live buffer.
- The actual text owner is the AvaloniaEdit `TextEditor x:Name="Editor"` (`NoteEditorView.axaml:18-23`), wired in code-behind:
  - `LoadedText` change → `ApplyLoadedText` sets `Editor.Text` with `_suppressEvents=true` (`NoteEditorView.axaml.cs:39-63`);
  - user edits → `OnEditorTextChanged` → `_viewModel.OnEditorTextChanged(Editor.Text)` → sets `_currentEditorText` + `_scheduler.Bump()` (`NoteEditorView.axaml.cs:65-73`; `NoteEditorViewModel.cs:144-151`).

**Gaps for insert-into-existing-note:**
- **No caret/selection exposed.** AvaloniaEdit natively supports `Editor.CaretOffset`, `Editor.SelectionStart/Length`, `Editor.Document.Insert(offset, text)`, but none is surfaced or bound. "Insert at cursor" needs new plumbing — cleanest: a new `InsertTextRequestedMessage(string)` handled in the editor code-behind (the only place with control access), doing `Document.Insert(CaretOffset, text)`, then letting `OnEditorTextChanged` propagate so `_currentEditorText` + autosave stay consistent.
- **Whole-text set inconsistency:** setting `LoadedText` alone leaves `_currentEditorText` stale (the change event is suppressed in `ApplyLoadedText`). A "replace/append whole body" path must also update `_currentEditorText` (as `Receive(NoteSelectedMessage)` does at `:111`) or it won't be saved.
- **Reuse once inserted:** after text lands and `OnEditorTextChanged` fires, autosave (500 ms) and index update (`NoteSavedMessage`) work automatically — no new save/index code.

**This capability is not in PRD v1** (FR-009 is "new note from a template"). Recommend an explicit `/10x-plan` decision and a PRD/roadmap amendment if kept.

### Area 5 — Template discovery & `.templates/` exclusion

**Scanner — `Notes/Services/WorkspaceScanner.cs`:** `Directory.EnumerateFiles(root, "*.md", opts)` (`:24`) with `RecurseSubdirectories = true` (`:18`), `AttributesToSkip = 0` (`:20`). The only exclusion is a **filename** dotfile filter `Path.GetFileName(path).StartsWith('.')` (`:26-30`) — it does **not** exclude `.`-prefixed *directories*. So `.templates/note.md` (leaf `note.md`) **is scanned, treed, and indexed today.**

**Tree builder** (`NoteTreeBuilder.cs:9-55`) groups the flat path list with no filtering — `.templates/` appears as a folder node. Called at `NoteTreeViewModel.cs:131-132`.

**Search index** (`NoteSearchIndex.cs`): ingests everything from the scanner (`:182-204`) but **filters `.templates/` at query time only** — `.Where(kv => includeTemplates || !kv.Key.StartsWith(".templates/", ...))` (`:136`), driven by `NoteSearchViewModel.IncludeTemplates` (default off, `NoteSearchViewModel.cs:36,110,156`).

**Recommended insertion points (a plan decision hinges on the search toggle):**
- *Exclude `.templates/` globally* (tree + index + build I/O) → add a path-segment guard in `WorkspaceScanner` next to `:26-30` (`relative.StartsWith(".templates/")`). This makes the query-time filter (`:136`) and the "Include templates in search" toggle redundant — decide whether to remove the toggle.
- *Exclude from tree only, keep searchable via the toggle* → leave the scanner/index alone and filter in `NoteTreeBuilder.Build` (or before `_treeBuilder.Build(paths)` at `NoteTreeViewModel.cs:132`).
- *Discover templates* → add a sibling method `ScanTemplateFiles(root)` on `IWorkspaceScanner` enumerating `.templates/*.md` (likely `TopDirectoryOnly`), mirroring `ScanMarkdownFiles`' shape/sort conventions.

## Code References

- `Notes/Services/NoteMetadataParser.cs:15-65` — the only frontmatter parser (Markdig + YamlDotNet); `FrontmatterShape` reads `tags` only.
- `Notes/Models/NoteMetadata.cs` — only parsed-frontmatter model (`Tags`).
- `Notes/Services/NoteFileService.cs:32-35` — `Save` via `File.WriteAllText` (UTF-8 no BOM); no dir creation.
- `Notes/ViewModels/NoteTreeViewModel.cs:80-120` — `HandleNewNote`; `:110-111` = template-content seam.
- `Notes/Services/NewNoteNameValidator.cs:10-45` — name validation, reject-on-collision.
- `Notes/ViewModels/NoteEditorViewModel.cs:25,27-28,144-175` — `_currentEditorText` buffer, `LoadedText`, `OnEditorTextChanged`, `DoSave`.
- `Notes/Views/NoteEditorView.axaml.cs:39-73` — VM↔AvaloniaEdit text round-trip (insertion plumbing point).
- `Notes/Services/AutoSaveScheduler.cs:19-49` — 500 ms debounced autosave.
- `Notes/Views/ConfirmDialog.axaml.cs:9-39`, `Notes/Views/NewNoteDialog.axaml.cs:10-54` — dialog pattern (result field + `static Show` + service).
- `Notes/Services/INewNoteDialogService.cs` / `NewNoteDialogService.cs:10-19` — dialog service + owner resolution.
- `Notes/ViewModels/ViewModelLocator.cs:6-18`, `Notes/App.axaml:9` — locator + DataContext hop.
- `Notes/Program.cs:32-58` — DI registration pattern.
- `Notes/Services/WorkspaceScanner.cs:24-36` — scanner enumeration + dotfile filter (no dir exclusion).
- `Notes/Services/NoteTreeBuilder.cs:9-55` — tree grouping (no filtering).
- `Notes/Services/NoteSearchIndex.cs:72-86,136,182-204` — incremental index, query-time `.templates/` filter.
- `Notes/Messaging/Messages.cs` — `NewNoteRequestedMessage`, `NoteSavedMessage`, `NoteSelectedMessage`, `NoteDeletedMessage`, `WorkspaceChangedMessage`, `TogglePreviewRequestedMessage`.

## Architecture Insights

- **Coordination is exclusively via `IMessenger` records** (`WeakReferenceMessenger.Default`, DI singleton `Program.cs:36`) — siblings hold no references. New template VMs/services should follow this.
- **One focused service per concern**, registered as a DI singleton in `Program.cs`; control-specific code (caret/insert) stays in code-behind, business logic in services/VMs (CLAUDE.md).
- **One dialog = one service** (`INewNoteDialogService`, `IConfirmDialogService`). S-02's plan explicitly says "S-04 template picker, field forms get their own focused services" — do not bundle onto existing dialog services.
- **No `FileSystemWatcher`** — externally dropped templates won't appear until workspace switch/restart.
- **Compiled bindings everywhere except the locator DataContext hop** (`ReflectionBinding`). A dynamic form using per-field-type implicit `DataTemplate`s keeps compiled bindings inside each field template.
- **Closed-DU result types** (`NoteNameResult.Success/Failure`) are the established success/failure idiom — mirror for any template parse/validate result.

## Historical Context (from prior changes)

- `context/changes/workspace-and-note-list/plan-brief.md:25,86` (S-01) — `.templates/` deliberately shown in the list: "User explicitly chose simplicity over hiding; revisit if S-04 templates make the list noisy." **S-04 owns the exclusion decision.** Also: custom `Window` confirm dialog, no MessageBox package; settings at `ApplicationData/Notes/settings.json`.
- `context/changes/note-editor-and-preview/plan-brief.md:29` (S-02) — "New domain dialogs (S-04 template picker, field forms) get their own focused services." Confirms the new-note save seam at `NoteTreeViewModel.cs:110`.
- `context/changes/tags-and-search/plan.md:66` (S-03) — "No structured parsing of frontmatter fields beyond `tags` … S-04 (templates) will define the rest of the frontmatter shape." `plan.md:55,74-75` — `.templates/` filtered at the search layer only, scanner/tree unchanged, "the tree still shows them as today."
- `context/changes/tags-and-search/follow-ups/review-fixes.md` — open CTS Cancel+Dispose race in `NoteSearchViewModel.cs:92-93,142-143`; see `context/foundation/lessons.md` "Don't dispose a CTS shared with an in-flight task." Relevant only if S-04 adds template-aware search.
- `context/foundation/lessons.md` — the malformed-YAML broad `catch (Exception)` in `NoteMetadataParser` is a *deliberate* rolled-back-narrowing decision; follow the same convention when parsing template frontmatter.

## Related Research

- No prior `research.md` exists under `context/changes/*/`. This is the first research artifact for the `templates` change.

## Open Questions

1. **Scope: apply-to-existing-note.** Keep the "insert template into an open note" expansion (beyond FR-009)? If yes, amend PRD/roadmap and budget for new editor caret/insert plumbing (`NoteEditorView.axaml.cs`).
2. **`.templates/` exclusion strategy.** Global exclusion in the scanner (kills the "Include templates in search" toggle) vs. tree-only exclusion (preserves the toggle). Which survives?
3. **Template-created note location & directory creation.** If a template can target a non-existent subfolder, `NoteFileService.Save` needs `Directory.CreateDirectory` (currently absent).
4. **Frontmatter emission mechanism.** New YamlDotNet `SerializerBuilder` vs. hand-built string for writing populated frontmatter — and how to merge template-declared `tags`/fields with the existing `NoteMetadata`/`FrontmatterShape` model.
5. **Field types & form contract.** Exact mapping of `text/date/number/dropdown` to Avalonia controls, validation rules per type, defaults, and the dialog's return shape (typed record vs. `Dictionary<string,object>`).
6. **Placeholder syntax & engine.** Confirm `{{field_name}}` token format, escaping, and behavior for unfilled/unknown placeholders (PRD requires "no leftover placeholder syntax").
7. **Do templates appear in the tree at all?** Editing a template = opening a `.templates/*.md` file; if excluded from the tree, how does the user edit/create one?

---

## Follow-up Research 2026-06-02 — scope narrowed by user

User clarifications reshaped S-04. Net effect: the slice shrinks to **template picker → dynamic form → placeholder substitution → save as new note**. Three of the original open questions are now resolved.

### Resolved scope decisions
- **Q1 (apply-to-existing-note):** OUT of MVP scope — first post-MVP feature. Area 4 of this doc is informational only; no editor caret/insert work in S-04.
- **Q2 (`.templates/` exclusion):** Do NOT exclude. `.templates/` stays visible in the tree because that is how templates are created/edited. Existing search query-time filter (`NoteSearchIndex.cs:136`) is left as-is.
- **Q4 (frontmatter emission):** No YAML serializer. Users type frontmatter inside their templates; note-from-template is **pure text placeholder substitution** over the whole template file (frontmatter included), written via `INoteFileService.Save`. The earlier "must build a YAML emitter" gap is dropped.
- **Q5/Q6 (form contract & placeholders):** the form-field definitions come from a **user-supplied structure** (pending — see Open Questions below). Placeholder syntax still to confirm.

### New finding — template-creation path works EXCEPT first-template bootstrap
Verified by tracing the existing New Note flow (agent over `NoteTreeViewModel`, `WorkspaceScanner`, `NoteTreeBuilder`, `NoteFileService`, `NewNoteNameValidator`, `NoteTreeNode`):

- **Editing an existing template:** works identically to any note, zero new code — selecting a leaf node under `.templates/` fires `NoteSelectedMessage` and the editor loads it (`NoteEditorViewModel.cs:70-114`). Caveat: a template file must not be **dot-prefixed** (scanner drops `.`-prefixed *filenames*, `WorkspaceScanner.cs:26-30`; it does NOT drop `.`-prefixed *directories*, so `.templates/daily.md` survives).
- **Creating a template once `.templates/` already exists & shows a folder node:** works — select the `.templates` folder node, New Note writes into it. `ResolveParentRelativePath` returns the selected folder's relative path for a `Folder` node (`NoteTreeViewModel.cs:162-177`); folder nodes are selectable (`NoteTreeView.axaml:11-12`, `SelectedItem` two-way bound with no kind gating); `NoteTreeNode` distinguishes `NoteNodeKind.Folder`/`File` (`Models/NoteTreeNode.cs:5-15`).
- **Creating the FIRST template when `.templates/` does not exist yet: BLOCKED (hard gap).** Two compounding causes:
  1. `NewNoteNameValidator` rejects path separators in the name (`NewNoteNameValidator.cs:18-21`) — the user can't type `.templates/daily.md`, and no `.templates` folder node exists to select.
  2. `NoteFileService.Save` does **not** create parent directories (`NoteFileService.cs:32-35`) — `File.WriteAllText` would throw `DirectoryNotFoundException`, and `HandleNewNote` has no try/catch around the save (`NoteTreeViewModel.cs:110`), so the exception is swallowed by the `async void Receive` catch-all (`:74-77`) — the user sees nothing happen.
  - There is also **no "create folder" action anywhere** in the codebase.

**Minimal fix for bootstrap (recommended for the plan):**
1. `NoteFileService.Save` creates the parent dir first: `Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);` (`NoteFileService.cs:32-35`). Single most load-bearing fix.
2. A dedicated **"New Template"** command on the tree VM that programmatically sets `parentRelative = ".templates"` and reuses the existing validate → `Save` → `LoadTree` → select path (`NoteTreeViewModel.cs:91-119`). Sidesteps the separator-rejection (name stays a bare filename; parent supplied in code, exactly like the folder-selected case). With fix #1, the first save creates `.templates/` and the tree reload surfaces the folder node.

### Dynamic typed-form rendering — confirmed idiom (the one net-new technical risk)
No dynamic-form precedent in the codebase, but the approach is standard Avalonia and matches existing `DataType`-keyed template usage (`NoteTreeView.axaml:14`, `SearchView.axaml:42`):
- An `ItemsControl` bound to an `ObservableCollection` of per-field-type field ViewModels (e.g. `TextFieldVm`, `DateFieldVm`, `NumberFieldVm`, `SelectFieldVm`), each carrying its collected value as a two-way-bound `[ObservableProperty]`.
- One implicit `DataTemplate DataType="vm:TextFieldVm"` etc. per field type in `ItemsControl.DataTemplates`; Avalonia auto-selects by runtime type. Compiled bindings stay intact *inside* each per-type template (each has a known `x:DataType`). A custom `IDataTemplate` selector is the fallback only if templates can't be keyed purely by type.
- The form dialog follows the existing pattern (`NewNoteDialog` precedent): code-behind `Window` + `static Task<T> Show(...)` returning a typed values result instead of `string?`; wrapped in its own focused `I…DialogService` (per S-02's mandate that template dialogs get dedicated services). The per-template form VM should be `AddTransient` or constructed by the dialog factory (it is per-invocation, unlike the app's singleton VMs).

### Revised reusable-surface summary for S-04
- **FR-008 (create template):** ≈ existing New Note flow + (1) `Save` parent-dir creation + (2) a "New Template" entry point for the bootstrap case. Editing templates: free.
- **FR-009 (note from template):** template picker (new, small) → dynamic form (new, the main build) → text substitution engine (new, small — `{{token}}` replace over template text) → reuse `HandleNewNote`'s name/validate/save/index/select tail, substituting rendered text for `string.Empty` at `NoteTreeViewModel.cs:110-111`.
- **Dropped from earlier estimate:** YAML serializer, scanner/tree exclusion, editor caret/insert plumbing.

### Open Questions (revised)
1. **The form-definition structure (BLOCKER for engine design).** User has a structure for declaring form fields — needs to be supplied. Determines: where field defs live (template frontmatter? separate block? sidecar?), how the engine reads them, and how they map to the four field types (text/date/number/dropdown).
2. **Placeholder syntax** — confirm `{{field_name}}` (PRD §Business Logic uses this), escaping, and behavior for unfilled/unknown placeholders (PRD requires "no leftover placeholder syntax").
3. **Generated-note destination & naming** — same folder-selection + name-prompt as New Note? Default folder? Does the template suggest a filename?
4. **Bootstrap UX** — is a "New Template" command acceptable, or should `.templates/` be auto-created on first launch / first use?

**Sources (dynamic-form rendering):**
- [ItemsControl | Avalonia Docs](https://docs.avaloniaui.net/docs/reference/controls/itemscontrol)
- [Data Templates | Avalonia Docs](https://docs.avaloniaui.net/docs/basics/data/data-templates)

---

## Follow-up Research 2026-06-02 (b) — first-template bootstrap → split into a prerequisite change

The first-template bootstrap gap is being solved **generically** via first-class note-tree management (a **New Folder** context-menu command + **folder deletion**), not a templates-special command. Per user decision (2026-06-02), this is a **separate prerequisite change** that **blocks `templates`**:

➡ **`context/changes/note-tree-folder-management/research.md`** — full analysis of the context-menu surface, folder-delete change list, and the directory-aware scanner work.

Locked decisions for that change:
- **Directory-aware scanner (Option B):** the scanner/tree become directory-aware so **empty folders persist and show** (chosen over the minimal "folder + first note" approach).
- It lives in its own change (`note-tree-folder-management`), keeping `templates` focused on the engine.

**Dependency for `templates`:** once `note-tree-folder-management` lands, the first-template bootstrap is solved by the user creating a `.templates/` folder (New Folder) and adding a template note inside it — no templates-specific bootstrap code needed.

### Still outstanding (the actual templates-engine blocker)
- ~~**The form-definition structure** and **placeholder syntax**~~ — RESOLVED, see Follow-up (c) below.

---

## Follow-up Research 2026-06-02 (c) — template form schema LOCKED + engine design

### Locked schema (from user)
- Field definitions live in the template's YAML frontmatter under a single top-level **`form`** key. **One `form` per template.**
- **`form` is itself a map keyed by field name** (no `fields` nesting layer); the YAML key is the **placeholder key**. Each field object has `type` and `label`; `dropdown` additionally requires `entries` (list of plain strings).
- Placeholder syntax: **`{{field_name}}`**.
- On generate: **strip the `form` block** from the output note's frontmatter (other frontmatter keys pass through verbatim; omit the block entirely if `form` was the only key — no empty `---\n---`); substitute `{{...}}` **in the body only** (not in frontmatter).

```yaml
---
form:
  project_name:
    type: text
    label: Project name
  priority:
    type: dropdown
    label: Priority
    entries: [low, medium, high]
---
# {{project_name}}

Priority: {{priority}}
```
→ generated note: `form` removed, body `{{project_name}}`/`{{priority}}` replaced with the user's input.

### Engine design (maps to existing codebase)
1. **List templates** — enumerate `.templates/*.md` (after `note-tree-folder-management` makes `.templates/` reliably present). A small picker dialog (reuse the `NewNoteDialog` `static Task<T> Show(...)` pattern, own focused service per S-02 mandate).
2. **Parse the form schema** — reuse the existing Markdig `UseYamlFrontMatter` + YamlDotNet pattern from `NoteMetadataParser.cs:15-35`, but with a **new parser/model** (the current `FrontmatterShape` reads only `tags`, `:62-65`). Since `form` is the field map directly, deserialize `form` into `Dictionary<string, TemplateField>` where `TemplateField { string Type, string Label, List<string>? Entries }`. Follow the established **broad-catch → empty** convention for malformed YAML (`NoteMetadataParser.cs:41-49`; see `lessons.md`). A template with no `form` block = a plain template (no fields, static copy).
3. **Build the dynamic form** — map each field to a per-type field VM and control:
   - `text` → `TextBox`, `number` → `NumericUpDown`, `date` → `DatePicker`, `dropdown` → `ComboBox` (bound to `entries`).
   - `ItemsControl` over an ordered collection of field VMs, one implicit `DataTemplate DataType="..."` per type (the idiom confirmed earlier). Per-field-type VMs (`TextFieldVm`/`DateFieldVm`/`NumberFieldVm`/`SelectFieldVm`) each expose a two-way-bound value; the form VM is `AddTransient`/factory-built (per-invocation).
   - **Field order:** `form` is a YAML map; deserializing into a `Dictionary<,>` preserves insertion order on .NET, but **verify** YamlDotNet emits keys in document order (or deserialize into an order-preserving structure / capture key order), so the form renders fields in template order.
4. **Collect values & render** — replace each `{{field_name}}` token in the **body** with the field's value (empty string if blank). Build the output = (template frontmatter with the `form` block removed) + (substituted body).
5. **Write the note** — reuse the `HandleNewNote` tail: name prompt/validate → `INoteFileService.Save(absolutePath, renderedText)` (substituting the rendered text for the `string.Empty` at `NoteTreeViewModel.cs:110-111`) → `NoteSavedMessage` → tree reload + select.

### Stripping the `form` block (design point for the plan)
Because substitution is body-only and other frontmatter must pass through **verbatim** (the product ethos is "users type frontmatter themselves" — no reformatting), the cleanest strip is **textual removal of the `form:` top-level block** from the frontmatter (remove the `form:` line and its indented children up to the next top-level key / end of frontmatter), NOT a YAML deserialize→reserialize round-trip (which would reorder/restyle/strip comments). Flag for `/10x-plan`.

### Rules (all confirmed by user 2026-06-02)
- **Field types** = `text`, `date`, `number`, `dropdown` (PRD §FR-009). Dropdown `entries` are plain strings (value == display label).
- **No `default` and no `required`/optional flag in MVP** — fields carry only `type`/`label` (+`entries`). All fields shown; a **blank/missing input → placeholder replaced with empty string** (satisfies PRD "no leftover placeholder syntax").
- **Unknown placeholders** in the body (`{{x}}` where `x` is not a declared field) are **left as-is** (only declared fields are substituted).
- **Degenerate frontmatter:** a template whose only frontmatter key is `form` → **omit the frontmatter entirely** in the output (no empty `---\n---`).
- **Picker scope:** templates are listed **flat** from `.templates/` top level (no nested template subfolders) for MVP.

### Status
Both changes are **ready for `/10x-plan`**. Plan order: **`note-tree-folder-management` first** (prerequisite), then **`templates`**.
