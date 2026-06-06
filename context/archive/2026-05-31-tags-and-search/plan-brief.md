# Tags and Search — Plan Brief

> Full plan: `context/changes/tags-and-search/plan.md`

## What & Why

Add YAML-frontmatter tag parsing and a Ctrl+F search overlay that filters the workspace by filename, tag, and full-text body content. The parser only extracts tags; bodies are read from disk on demand at search time. This is S-03 from the roadmap — the organization slice that turns the editor-only app from S-02 into a navigable note-taking tool by letting the user actually find anything they wrote down. Without it, a note is only as findable as the user's memory of where they put it.

## Starting Point

S-02 has shipped: `MainWindowViewModel` + `NoteTreeViewModel` + `NoteEditorViewModel` split, `IMessenger`-based communication, `WorkspaceScanner` + `NoteFileService` for IO, autosave + new-note creation flowing through the existing dialog services, Markdig + Markdown.Avalonia for preview. No YAML parser, no metadata model, no search of any kind. `IWorkspaceScanner.ScanMarkdownFiles(...)` already returns every `.md` path in the workspace (including `.templates/` content); `INoteFileService.Read(...)` already returns full UTF-8 text — the IO primitives are in place.

## Desired End State

A user presses Ctrl+F (or File → Search…) and a search overlay appears layered over the tree column. The text input is focused immediately, an "Include templates" checkbox sits below (unchecked by default), and a flat result list sits underneath. Typing filters live with a 150 ms debounce; each whitespace-separated query token must appear case-insensitively in (filename, tags, or body) for a note to match. Clicking a result opens the note in the editor and closes the overlay; Esc closes without opening anything. Tags live in YAML frontmatter as `tags: [project, urgent]` (flow list) or block list form; tags are canonicalized to lowercase + hyphens-allowed at parse time; malformed YAML silently falls back to "no tags" so search never breaks on user-edited files. The index holds only metadata `(relativePath, fileName, tags)` in memory — bodies are NOT cached. On a search query, the index first checks filename + tags in memory; for any query token not yet matched, it lazy-reads the candidate note's body from disk to test the remaining tokens. The index build runs asynchronously (off the UI thread) on workspace change and stays fresh through autosave + delete + new-note creation via a new `NoteSavedMessage`. While the initial build is in progress, the search overlay shows "Indexing notes…" in place of results.

## Key Decisions Made

| Decision                           | Choice                                                                                                | Why                                                                                                                                                              | Source |
| ---------------------------------- | ----------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| Search UI placement                | Ctrl+F overlay (popup-style) over the tree column                                                     | User chose modal-search feel; zero chrome cost when idle; Esc dismisses                                                                                          | Plan   |
| Filter mode while searching        | Replace tree with a flat list of matches inside the overlay                                           | User chose flat list; tree spatial model isn't useful at search time; easier to scan                                                                              | Plan   |
| Tag display                        | Not displayed anywhere — search-only metadata for MVP                                                 | User chose minimal scope; no commitment to tag-rendering style; revisit post-MVP                                                                                  | Plan   |
| Query semantics                    | Whitespace-tokenized AND; each token must appear case-insensitively in (filename, tags, body)         | User chose simple substring; zero learning curve; trivial to implement; matches intuition                                                                         | Plan   |
| Tag canonical form                 | Lowercase, no whitespace, hyphens allowed (`[a-z0-9-]+`); invalid tags silently dropped               | User-stated constraint; aligns with silent-fallback policy on YAML errors                                                                                         | Plan   |
| Malformed YAML handling            | Silent fallback — note has zero tags but stays fully searchable by name + body                        | User chose this; honors PRD "no data loss / never lock the user out" guardrail; never breaks search                                                                | Plan   |
| Index lifecycle                    | Built on `WorkspaceChangedMessage`; refreshed per entry on `NoteSavedMessage` / `NoteDeletedMessage` | User chose this; incremental + message-driven matches existing flow; no FileSystemWatcher complexity; external edits are a known MVP gap                          | Plan   |
| Body storage                       | NOT cached — index holds only `(relativePath, fileName, tags)`; bodies read lazily from disk         | User flagged discomfort with bodies in memory; lazy two-pass search (memory first, disk only for unmatched tokens) keeps FR-006 full-text intact at zero RAM cost | Plan (refined by user feedback) |
| Index build threading              | Async via `Task.Run`; `IsReady` toggles via new `SearchIndexStateChangedMessage`                     | User flagged that mid-build empty results need a visible state; async build + status message replaces a UI-thread block; cancellation handles workspace re-switch | Plan (refined by user feedback) |
| Empty/loading state in overlay     | "Indexing notes…" shown while `IsReady == false`; results only render once index is built            | Honest UX over silent failure; the user knows why results are empty                                                                                              | Plan (refined by user feedback) |
| Frontmatter scope                  | Parse `tags` only; everything else stays as raw body text for full-text search                        | User chose minimum; S-04 will define the rest of the frontmatter shape; locking it in now risks rework                                                            | Plan   |
| `.templates/` filtering            | Excluded from search by default; checkbox in overlay includes them                                    | User chose excluded-with-toggle; templates aren't notes for search purposes, but power users want access                                                          | Plan   |
| Result click behavior              | Open the note in the editor AND close the overlay                                                     | User chose this; matches VS Code Ctrl+P "find and go" mental model                                                                                                | Plan   |
| Result ordering                    | Alphabetical by relative path (same order as the tree)                                                | User chose this; simplest; matches tree intuition                                                                                                                 | Plan   |
| YAML parser library                | YamlDotNet 18                                                                                         | De-facto .NET YAML library; actively maintained; explicit .NET 10 support; non-AOT desktop app doesn't need VYaml's gains                                         | Plan (research) |
| Frontmatter location strategy      | Markdig's `UseYamlFrontMatter()` to find the block + YamlDotNet to parse it                           | Markdig is already a dependency for preview; its extension tokenizes the block but doesn't parse YAML; idiomatic .NET pattern                                     | Plan (research) |
| Search algorithm                   | Naive linear scan with `IndexOf(token, OrdinalIgnoreCase)`                                            | At documented scale (<10k notes, <10MB) completes in single-digit ms; no inverted-index complexity; upgrade path stays open behind the `INoteSearchIndex` interface | Plan (research) |
| Debounce interval                  | 150 ms                                                                                                | Proven sweet spot; search is cheap so small debounce keeps it snappy                                                                                              | Plan   |
| YAML tag list forms accepted       | Flow `[a, b]` and block `- a\n- b`                                                                    | YamlDotNet handles both transparently when deserializing to `List<string>`; anything else falls back to "no tags"                                                  | Plan   |
| State on overlay close             | Query + IncludeTemplates + Results all reset                                                          | Simpler than persistence; "search is a transient mode" intuition                                                                                                  | Plan   |

## Scope

**In scope:**
- YamlDotNet 18 NuGet dependency
- `NoteMetadata` + `NoteSearchResult` models
- `INoteMetadataParser` / `NoteMetadataParser` (pure-logic)
- `INoteSearchIndex` / `NoteSearchIndex` (singleton, message-driven, async build, lazy body reads on search)
- `INoteFileService.ReadAsync(string)` added alongside the existing sync `Read`
- New messages: `NoteSavedMessage(relativePath, content)`, `OpenSearchRequestedMessage`, `SearchIndexStateChangedMessage(bool IsReady)`
- `NoteEditorViewModel` + `NoteTreeViewModel` edits to publish `NoteSavedMessage` after save / new-note creation
- `NoteSearchViewModel` + `SearchView.axaml` UserControl with "Indexing notes…" empty-state hint
- `ViewModelLocator.Search`, `MainWindowViewModel.SearchCommand`, Ctrl+F binding, File → Search… menu item
- DI registrations for parser, index, search VM
- Eager startup resolution of `INoteSearchIndex` so it registers with the messenger before the first `WorkspaceChangedMessage`
- Unit tests for parser (15+ cases), index (build + each message handler + two-pass query semantics + cancellation), and search VM (debounce + lifecycle + result interaction + IsIndexReady transitions)

**Out of scope:**
- Tag chips, tag panel, or any tag display
- Structured parsing of non-tag frontmatter fields
- File-system watcher / external-edit detection
- Query syntax (no `tag:`, no boolean operators)
- Ranked / scored results (no BM25, no TF-IDF)
- Result cap or pagination
- Persistence of search state across overlay open/close
- Surfaced parse errors (no toast / notification system)
- Custom keyboard navigation inside the result list beyond Avalonia's defaults
- Changes to `WorkspaceScanner`, `NoteTreeBuilder`, `NoteFileService`, or S-04 template scope

## Architecture / Approach

```
                    ┌──────────────────────────────────┐
                    │   IMessenger (singleton)         │
                    │   WeakReferenceMessenger.Default │
                    └────────────┬─────────────────────┘
                                 │ (Send / Receive)
        ┌────────────────────────┼────────────────────────────────────┐
        │                        │                                    │
┌───────▼────────────┐  ┌────────▼─────────────┐  ┌──────────────────▼──────────────────┐
│ MainWindowViewModel│  │  NoteTreeViewModel   │  │  NoteEditorViewModel                 │
│                    │  │                      │  │                                      │
│  + SearchCommand   │  │  Publishes:          │  │  Publishes:                          │
│    (Ctrl+F)        │  │    NoteSelectedMsg   │  │    NoteSavedMessage (new, after save)│
│                    │  │    NoteDeletedMsg    │  │                                      │
│  Sends:            │  │    NoteSavedMsg (new,│  │                                      │
│   OpenSearchReqMsg │  │      after new-note  │  │                                      │
│   (also Workspace, │  │      creation)       │  │                                      │
│    NewNote, Toggle │  │                      │  │                                      │
│    from S-02)      │  │                      │  │                                      │
└────────────────────┘  └──────────────────────┘  └──────────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│  NoteSearchIndex (singleton — NEW)                                                       │
│    Dictionary<RelativePath, MetadataEntry { FileName, Tags }>   ← NO bodies in memory   │
│    bool IsReady; CancellationTokenSource _buildCts                                      │
│                                                                                          │
│    Receives:                                                                             │
│       WorkspaceChangedMessage → cancel any in-flight build; IsReady = false;            │
│                                  publish SearchIndexStateChangedMessage(false);         │
│                                  Task.Run { scan + read each file + parse → metadata }; │
│                                  on completion swap dictionary, IsReady = true,         │
│                                  publish SearchIndexStateChangedMessage(true)           │
│       NoteSavedMessage        → parse content, upsert single metadata entry             │
│       NoteDeletedMessage      → remove entry                                            │
│                                                                                          │
│    Exposes: Task<IReadOnlyList<NoteSearchResult>> Search(                                │
│               query, includeTemplates, CancellationToken)                               │
│             Two-pass:                                                                    │
│               (1) tokenize query; for each metadata entry, identify tokens NOT          │
│                   matched by filename or tags                                            │
│               (2) for entries with unmatched tokens, read body via                       │
│                   INoteFileService.ReadAsync and test remaining tokens                  │
│             Skip `.templates/` unless includeTemplates; sort by RelativePath            │
└──────────────────────────────────────────────────────────────────────────────────────────┘
                                 ▲
                                 │ (Search calls)
┌────────────────────────────────┴────────────────────────────────────────────────────────┐
│  NoteSearchViewModel (singleton — NEW)                                                  │
│    [ObservableProperty] Query, IsOpen, IncludeTemplates, Results,                       │
│                         SelectedResult, IsIndexReady                                    │
│    OnQueryChanged → 150 ms debounce → cancel previous Search task →                     │
│                     await index.Search(...) → assign Results                            │
│    OpenResultCommand → publishes NoteSelectedMessage + Close()                          │
│    CloseCommand → cancel in-flight search; reset state                                  │
│    Receives:                                                                             │
│       OpenSearchRequestedMessage    → IsOpen = true                                     │
│       WorkspaceChangedMessage       → Close() (stale results on workspace switch)       │
│       SearchIndexStateChangedMessage → mirror IsReady → IsIndexReady; re-run search     │
│                                         once index becomes ready and Query non-empty    │
└──────────────────────────────────────────────────────────────────────────────────────────┘
                                 ▲
                                 │ (DataContext via ViewModelLocator)
┌────────────────────────────────┴────────────────────────────────────────────────────────┐
│  MainWindow.axaml:                                                                       │
│    <Grid ColumnDefinitions="*,Auto,2*">                                                  │
│      <NoteTreeView Grid.Column="0" />                                                    │
│      <SearchView   Grid.Column="0" />   ← layered ON TOP of tree, IsVisible bound to    │
│                                            Search.IsOpen via locator                    │
│      <GridSplitter Grid.Column="1" />                                                    │
│      <NoteEditorView Grid.Column="2" />                                                  │
│    KeyBindings: Ctrl+F → SearchCommand                                                   │
│    Menu: File → _Search… (Ctrl+F)                                                        │
│                                                                                          │
│  SearchView.axaml:                                                                       │
│    DataContext via Locator → Search                                                      │
│    <Grid RowDefinitions="Auto,Auto,*">                                                   │
│      Row 0: TextBox (query, focused on open, Esc → CloseCommand)                        │
│      Row 1: CheckBox "Include templates"                                                 │
│      Row 2: ListBox (Results, double-click / Enter → OpenResultCommand)                  │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```

Sibling VMs continue to hold no direct references to each other. The messenger remains the only communication channel. The index is invisible to UI VMs until Phase 3 — at which point `NoteSearchViewModel` calls it directly through its DI-injected interface (a service-call relationship, not a messaging one, because search is request/response).

## Phases at a Glance

| Phase                                       | What it delivers                                                                                                                | Key risk                                                                                                                                              |
| ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1. Metadata parser (pure logic)             | YamlDotNet added; `NoteMetadataParser` ships with 15+ edge-case tests; nothing wired yet                                | YAML parser edge cases (malformed, missing tags, non-string values) — mitigated by exhaustive tests                                                   |
| 2. Search index + message integration       | Metadata-only in-memory index built async; lazy two-pass search; `IsReady` state via new `SearchIndexStateChangedMessage`       | Async build cancellation on re-entrant workspace change; lazy body read I/O on cold disk; eager DI resolve so the index registers before first message |
| 3. Search overlay UI (with templates toggle)| User-visible Ctrl+F overlay; "Indexing notes…" state; result-click opens note; Include templates toggle; cancellation on retype | Overlay focus management; Grid-layered visibility; debounced async search calls correctly cancelling in-flight tasks when the user keeps typing       |

**Prerequisites:** S-01 (`workspace-and-note-list`) and S-02 (`note-editor-and-preview`) shipped.
**Estimated effort:** ~3 focused sessions (one per phase; Phase 3 is the largest because of view-layer work).

## Open Risks & Assumptions

- **Assumes** YamlDotNet 18 is on NuGet with `.NET 10` support — research confirmed late-May-2026; pin to `Version="18.*"` to absorb minor fixes.
- **Assumes** `WeakReferenceMessenger.Default` keeps the singleton index and search VM alive as long as their DI-held references exist. Same pattern as the three existing VMs from S-02 — pattern validated.
- **Assumes** eager resolution of `INoteSearchIndex` at startup (added to `App.OnFrameworkInitializationCompleted`) runs before `MainWindowViewModel.InitializeAsync` publishes the first `WorkspaceChangedMessage`. Tested via Phase 2 manual smoke (no exception on launch + index populated).
- **Lazy body read on cold disk cache** pays I/O proportional to the candidate set whenever a query token isn't matched by any filename or tag. At PRD's "small" scale (~1k notes / ~10 MB) the warm-cache cost stays in the sub-50 ms range; first-search-after-launch may take longer. Acceptable per the NFR.
- **Async index build cancellation:** if a second `WorkspaceChangedMessage` arrives while a build is in flight, the first must be cancelled cleanly (CancellationTokenSource per build). Tests cover the re-entrant case; the implementation must not leak partial state from the cancelled build into the new dictionary.
- **Async search cancellation:** if the user keeps typing while a Search is in flight (because the previous query forced disk reads), the in-flight Search must be cancelled before the new one starts. Without this, results arrive out-of-order and can clobber newer queries. The Search VM owns one CancellationTokenSource for the active search.
- **External edits are not picked up** until app restart or workspace switch — documented limitation, consistent with the rest of the MVP (no FileSystemWatcher in scope).
- **Notes inside `.templates/` are visible in the tree** (S-01 behavior unchanged) but hidden from search by default. Two filtering rules for `.templates/` (tree shows, search hides) is a deliberate split; the user explicitly accepted it.
- **Body extraction on malformed YAML** treats the whole file as body (since we can't reliably slice off a broken block). Side effect: literal `---` markers become indexed body text. Acceptable — they almost never match a real query.
- **Stale index entry races with lazy read:** an entry whose file was deleted between the Search start and the lazy body read is silently skipped (caught `FileNotFoundException`). The next `NoteDeletedMessage` will clean the entry; the user just doesn't see a phantom result for the deleted file.

## Success Criteria (Summary)

- User can press Ctrl+F, type a query, and see matching notes filtered live across filename, tags, and body content within ~150 ms
- User can tag notes via YAML frontmatter with `tags: [a, b]` (or block list form); tags are normalized lowercase + hyphens; malformed YAML never breaks search
- Result click opens the note in the existing editor and dismisses the overlay; Esc closes without opening anything
- `dotnet test` is green for the parser (15+ cases), the index (message handlers + query semantics), and the search ViewModel (lifecycle + debounce + result click)
- Notes inside `.templates/` are hidden from search by default; checkbox in overlay includes them
