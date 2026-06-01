# Tags and Search Implementation Plan

## Overview

Add YAML-frontmatter tag parsing and a Ctrl+F overlay that filters the workspace by filename, tag, and full-text body content. A new pure-logic metadata parser extracts the tag list from a note's full text — it doesn't care about the body. A new singleton search index holds only metadata `(relativePath, fileName, tags)` in memory, lazy-reads file content from disk when a query token isn't satisfied by filename or tags, builds asynchronously off the UI thread, and stays fresh via the messaging bus. A new search ViewModel + overlay UserControl renders the experience, surfacing an "Indexing notes…" state while the build runs. Result clicks open the note in the existing editor and dismiss the overlay.

This is S-03 from the roadmap — the organization slice that turns the editor-only app from S-02 into a navigable note-taking tool.

## Current State Analysis

The codebase already supplies every primitive this slice needs except YAML parsing:

- **`IWorkspaceScanner.ScanMarkdownFiles(root)`** returns forward-slash-separated relative paths, sorted lexicographically; recurses through `.dot` folders (including `.templates/`); skips dotfiles. (`Notes/Services/WorkspaceScanner.cs`)
- **`INoteFileService.Read(absolutePath)`** returns the note's full UTF-8 text (or empty if missing). (`Notes/Services/NoteFileService.cs`)
- **Messaging:** five message records exist in `Notes/Messaging/Messages.cs`; `WorkspaceChangedMessage`, `NoteSelectedMessage`, and `NoteDeletedMessage` are all consumed today. `IMessenger` is registered as a singleton (`WeakReferenceMessenger.Default`) in `Notes/Program.cs`.
- **ViewModel split:** `MainWindowViewModel` owns menu commands, `NoteTreeViewModel` owns tree state, `NoteEditorViewModel` owns editor/preview state — all singletons resolved through `ViewModelLocator` (`Notes/ViewModels/ViewModelLocator.cs`); siblings hold no references to each other.
- **DI composition:** all services registered in `Notes/Program.cs::BuildServiceProvider()`; new services slot in via `AddSingleton`.
- **Test pattern:** `Notes.Tests/` uses xUnit with a temp-directory `IDisposable` pattern for IO-touching services and inline stubs at the bottom of each ViewModel test class for messaging fakes.
- **Markdig is already a dependency** (v1.2.0) — wired for preview rendering but its `UseYamlFrontMatter()` extension also tokenizes the leading `---…---` block into a `YamlFrontMatterBlock` AST node. We can reuse that tokenizer to locate the frontmatter; YAML parsing itself needs YamlDotNet (not currently a dependency).

What's missing:

- No YAML parser in the dependency set.
- No service that splits a note into metadata vs body.
- No in-memory index of note metadata.
- No `NoteSavedMessage` — the editor calls `_fileService.Save(...)` and the indexer would have no way to learn of the save.
- No search UI of any kind, no `Search` command on `MainWindowViewModel`, no Ctrl+F binding.

## Desired End State

A user can press **Ctrl+F** (or **File → Search…**) to open a search overlay layered over the tree column. The overlay contains a text input that gains focus immediately, an "Include templates" checkbox (unchecked by default), and a flat result list below. Typing in the input filters the workspace live (150 ms debounce); each whitespace-separated token must appear, case-insensitively, somewhere across (filename, normalized tags, body) for a note to be a result. Results are listed in alphabetical relative-path order (same order the tree uses). Clicking a result publishes `NoteSelectedMessage`, opens the note in the editor (existing flow), and closes the overlay. **Esc** also closes the overlay without opening anything.

While the initial index build is in progress, the overlay shows "Indexing notes…" in place of the result list — search input remains enabled but returns no results until the index becomes ready. The build runs off the UI thread.

The index holds only metadata in memory — `(relativePath, fileName, tags)` per note. Body content is never cached. When a query has a token that isn't satisfied by any filename or tag, the index opens the candidate file via `INoteFileService.ReadAsync(...)`, scans the body for the remaining tokens, and closes it. This honors PRD FR-006 (full-text search) without holding all bodies in memory.

Notes are tagged via YAML frontmatter:

```markdown
---
tags: [project, urgent]
---

# Note body
```

Tags accepted in both YAML list forms (flow `[a, b]` and block `- a\n- b`). Each tag value is lowercased and dropped if it still contains whitespace or other non-canonical characters after normalization. Malformed YAML or non-list `tags:` values silently fall back to "no tags" — full-text search still works. Files under `.templates/` are excluded from results by default; the toggle includes them.

Verification:
- `dotnet test` is green for `NoteMetadataParser` (15+ cases), `NoteSearchIndex` (async build + build cancellation + each message handler + two-pass query + lazy body read + IsReady transitions), and `NoteSearchViewModel` (debounce, lifecycle, result interaction, includeTemplates, IsIndexReady mirroring, search cancellation on re-type).
- Manual smoke test: create a workspace with several `.md` files (some with tags, some without, some with malformed YAML, some under `.templates/`), launch the app, press Ctrl+F, type queries, and confirm the result list reflects the rules above; on a larger workspace, observe the "Indexing notes…" state appearing momentarily after a workspace switch.

### Key Discoveries:

- `WorkspaceScanner` recurses into `.templates/` already (`Notes/Services/WorkspaceScanner.cs:14-32`) — template paths are present in the scanner's output today; S-03 must filter them out at the search layer, not change the scanner.
- `NoteFileService.Read` returns `string.Empty` when a file is missing (`Notes/Services/NoteFileService.cs:11-18`) — the indexer must treat a missing file the same as an empty note (zero tags, empty body) to stay race-safe between scan and read.
- `WeakReferenceMessenger.Default` is the singleton `IMessenger` (`Notes/Program.cs:39`) — recipients are weak-referenced; long-lived singletons (the index, the search VM) must keep the messenger alive by holding it in a field (the existing VMs already do this).
- `NoteEditorViewModel` calls `_fileService.Save(absolutePath, _currentEditorText)` inside `DoSave()` (the `IAutoSaveScheduler.OnSave` handler) and on workspace-change flush. Both call sites need to publish `NoteSavedMessage` after the save lands.
- `NoteTreeViewModel.HandleNewNote()` calls `_fileService.Save(success.AbsolutePath, string.Empty)` for new-note creation (`Notes/ViewModels/NoteTreeViewModel.cs:106`) — also needs to publish `NoteSavedMessage` (with empty content) so the indexer registers the new file before the user types anything.
- `MainWindow.axaml` `KeyBindings` is the existing wiring point for global shortcuts (`Notes/MainWindow.axaml:9-12` already binds Ctrl+N and Ctrl+E) — Ctrl+F slots in next to them.
- `ViewModelLocator` already has the `Main`/`Tree`/`Editor` pattern (`Notes/ViewModels/ViewModelLocator.cs:7-13`); adding `Search` follows the same recipe.

## What We're NOT Doing

- **No tag display in the tree or anywhere else** — tags are pure search metadata for MVP. No chips, no tag-list panel, no autocomplete. (User decision; revisit post-MVP.)
- **No structured parsing of frontmatter fields beyond `tags`** — `title:`, `date:`, custom fields are read as part of the body for full-text matching but never deserialized into a typed model. S-04 (templates) will define the rest of the frontmatter shape.
- **No file-system watcher** — external edits made while Notes is running are not picked up until the next workspace switch or app restart. Documented as a known limitation, consistent with the rest of the MVP.
- **No inverted index, BM25, fuzzy matching, or ranked scoring** — naive substring scan only. Results are sorted by relative path, not by relevance score.
- **No query syntax** — no `tag:foo`, no `name:`, no boolean operators. Just whitespace-separated AND of substrings.
- **No persistence of search state** — when the overlay closes, the query and the IncludeTemplates toggle reset. Reopening starts fresh.
- **No result cap or pagination** — full result list rendered. At the documented scale (small data volumes per PRD) this is fine; if it ever isn't, revisit before adding pagination.
- **No surfaced parse errors** — malformed YAML silently falls back to "no tags." We add no toast/notification system for this MVP.
- **No keyboard-only result navigation beyond Avalonia's default ListBox behavior** — arrow keys + Enter work because of the standard ListBox, but no custom keyboard handling is built.
- **No changes to `WorkspaceScanner`, `NoteTreeBuilder`, or `NoteFileService`** — the existing services already give us everything we need.
- **No changes to S-04 templates** — `.templates/` filtering happens at the search layer; the tree still shows them as today.

## Implementation Approach

Bottom-up, three phases that each leave the codebase shipping-quality even if work stops between phases.

**Phase 1** is a pure-function service (`NoteMetadataParser`) with no integration into the running app — useful only via tests. This pays the YamlDotNet-dependency cost and locks down the parser's edge-case behavior with a thorough test suite before anything else depends on it.

**Phase 2** adds the stateful `NoteSearchIndex` singleton that uses the Phase 1 parser. The indexer subscribes to `WorkspaceChangedMessage` for full rebuilds, `NoteSavedMessage` (new) for single-entry refreshes, and `NoteDeletedMessage` for removals. The two existing VMs that mutate files (`NoteEditorViewModel`, `NoteTreeViewModel`) are edited to publish `NoteSavedMessage` from their save call-sites. At this phase's end the index is correct and current — verifiable through tests — but no UI exposes it.

**Phase 3** adds the user-visible feature: a `NoteSearchViewModel`, a `SearchView.axaml` UserControl that layers over the tree column, a Ctrl+F keybinding + File-menu entry, debounced query handling, and the "Include templates" toggle. Result-click reuses `NoteSelectedMessage` so the editor opens the picked note without any new editor-side code.

Each phase ends in a green `dotnet test` + a manual smoke check appropriate to what's visible at that point. Phase 1 has no manual check (pure logic). Phase 2's manual check is exercised by adding a temporary debug listener or inspecting through tests — but no UI yet. Phase 3 is the full user-visible smoke test.

## Critical Implementation Details

- **Markdig's `YamlFrontMatterExtension` returns the raw text of the frontmatter block, not parsed YAML.** Idiomatic pattern: build a Markdig pipeline once with `.UseYamlFrontMatter()`, parse the note text, locate the first `YamlFrontMatterBlock` in the AST, slice the original text using `block.Span.Start` + `block.Span.Length`, strip the leading and trailing `---` lines, then hand the remaining inner text to YamlDotNet's `Deserializer.Deserialize<FrontmatterModel>(yaml)`. Cache the Markdig pipeline as a static readonly field on the parser (constructing it per call is wasteful).
- **YamlDotNet throws on malformed input.** The parser must wrap deserialization in `try { ... } catch (YamlException) { return ZeroTags; }` and propagate nothing — the silent-fallback contract is the entire interface guarantee for malformed input, and tests assert that behavior explicitly.
- **YamlDotNet maps a missing `tags:` key or `tags: null` to a `null` list** when targeting `List<string>?`. The parser must coerce null → empty before normalization.
- **Body text for search is the raw file content from `INoteFileService.ReadAsync(...)`, frontmatter and all.** The parser never returns the body; the search layer reads the file directly when a query token isn't satisfied by filename or tags. Frontmatter text is included in the body scan by design — this means a query for a literal YAML field name (e.g., `tags` or `title`) matches every note with that field. False-positive surface is small and accepted as an MVP simplification; the alternative (stripping frontmatter at search time) adds code for an edge case that doesn't matter for normal queries.
- **Index build cancellation:** the index holds a single `CancellationTokenSource? _buildCts` field. On each `WorkspaceChangedMessage`, cancel and dispose the previous CTS (if any), create a new one, kick off `Task.Run(() => Build(workspacePath, cts.Token))`. The Build task checks `cancellationToken.ThrowIfCancellationRequested()` between file reads. The cancelled task must not assign its partial result to the live dictionary — guard the final swap with a token check. The new dictionary is built locally and assigned in one statement once parsing completes successfully; on cancellation the local dictionary is discarded.
- **Build → search ordering:** the live dictionary that `Search` reads must be replaced atomically. Either (a) build into a local `Dictionary<...>`, then on completion swap a single field reference (`_entries = newEntries`) on the UI thread via `Dispatcher.UIThread.Post`, or (b) hold a `volatile` reference. Option (a) is simpler given everything else in this codebase runs on the UI thread. The field reference swap is atomic in C#; readers see either the old map or the new map, never a torn read.
- **Search cancellation:** the `Search` method takes a `CancellationToken`. The caller (`NoteSearchViewModel`) holds one `CancellationTokenSource? _searchCts` and cancels it before issuing each new Search. The Search method checks the token between iterating entries and between each lazy body read. On `OperationCanceledException`, the VM swallows it (don't surface as an error) — a cancelled search simply means the user has moved on to a new query.

## Phase 1: Metadata Parser

### Overview

Introduce YamlDotNet as a dependency and ship a pure-function `NoteMetadataParser` that extracts the tag list from a note's full text. The body is irrelevant to the parser — it's read from disk by the search layer when needed and scanned as-is. No integration into the running app at this phase — the parser is registered in DI but called only from its own test suite.

### Changes Required:

#### 1. Add YamlDotNet package

**File**: `Notes/Notes.csproj`

**Intent**: Add the YAML parser used by the metadata parser. YamlDotNet is the de-facto .NET YAML library, actively maintained, ships .NET 10 target framework moniker, and matches the existing dependency style (regular `PackageReference` line, no analyzer/source-generator setup needed for a non-AOT desktop app).

**Contract**: New `<PackageReference Include="YamlDotNet" Version="18.*" />` line in the existing `<ItemGroup>` alongside the other libraries. Version pinned to the 18.x major; minor bumps inside that range are safe.

#### 2. Define `NoteMetadata` model

**File**: `Notes/Models/NoteMetadata.cs` (new)

**Intent**: A record carrying the parser's output: just the tag list. Lives in `Models/` next to `NoteTreeNode` and `AppSettings`. Plain DTO — no behavior.

**Contract**:
- `public sealed record NoteMetadata(IReadOnlyList<string> Tags)`
- Static `NoteMetadata Empty { get; }` convenience for the "missing-file / malformed / no-frontmatter" case → `new(Array.Empty<string>())`.

#### 3. Define `INoteMetadataParser`

**File**: `Notes/Services/INoteMetadataParser.cs` (new)

**Intent**: Pure-function contract — given a full note text, return its tag metadata. No file IO, no IO-bound dependencies; trivially testable.

**Contract**:
- `NoteMetadata Parse(string noteText)` — single method, synchronous, total (never throws for any input, including null/empty/garbage). Returns `NoteMetadata.Empty` for null/empty input.

#### 4. Implement `NoteMetadataParser`

**File**: `Notes/Services/NoteMetadataParser.cs` (new)

**Intent**: Implement the parsing pipeline:
1. Find the leading YAML frontmatter block via Markdig's `UseYamlFrontMatter()` pipeline (cached as `private static readonly`).
2. If no block exists → return `NoteMetadata.Empty`.
3. If a block exists: slice the raw YAML text, deserialize to an internal `FrontmatterShape { List<string>? Tags }` via YamlDotNet, catch `YamlException` and fall back to empty tags.
4. Normalize tags: trim, lowercase (invariant culture), drop any tag that is empty OR contains any character outside `[a-z0-9-]` after lowercasing. Distinct (case-insensitive — though after lowercasing, ordinal Distinct suffices). Preserve insertion order.
5. Return `new NoteMetadata(normalizedTags)`.

**Contract**: Implements `INoteMetadataParser`. Private nested record `FrontmatterShape` annotated for YamlDotNet (the default deserializer matches `tags` to `Tags` by name; explicit alias not required, but a small XML comment noting the contract is welcome). Internal helper `static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? raw)` is `private static` so tests can mirror its behavior via the public `Parse` surface.

#### 5. Register parser in DI

**File**: `Notes/Program.cs`

**Intent**: Wire the parser as a singleton (matches every other service in the file). Add the line in the services-registration block; ordering doesn't matter, but group it near `NoteFileService` for readability since both deal with note content.

**Contract**: `services.AddSingleton<INoteMetadataParser, NoteMetadataParser>();`

#### 6. Tests for `NoteMetadataParser`

**File**: `Notes.Tests/NoteMetadataParserTests.cs` (new)

**Intent**: Lock down the parser's behavior across happy path and every edge case the user can produce by hand. No file IO — every test feeds raw strings to `Parse(...)` and asserts on the returned `NoteMetadata`. Follows the existing project naming convention `Method_WhenScenario_ExpectedBehaviour`.

**Contract**: `public sealed class NoteMetadataParserTests` with these tests (minimum):
- `Parse_WhenInputIsNull_ReturnsEmpty`
- `Parse_WhenInputIsEmpty_ReturnsEmpty`
- `Parse_WhenNoFrontmatter_ReturnsZeroTags`
- `Parse_WhenFrontmatterButNoTagsKey_ReturnsZeroTags`
- `Parse_WhenTagsFlowList_ReturnsTagsLowercased`
- `Parse_WhenTagsBlockList_ReturnsTagsLowercased`
- `Parse_WhenTagsContainMixedCase_ReturnsAllLowercased`
- `Parse_WhenTagsContainWhitespaceValue_DropsThatTag`
- `Parse_WhenTagsContainHyphens_KeepsThemAsCanonical`
- `Parse_WhenTagsContainUnderscoreOrPunctuation_DropsThoseTags`
- `Parse_WhenTagsContainEmptyOrNullValue_DropsThoseEntries`
- `Parse_WhenTagsKeyIsNotAList_ReturnsZeroTags`
- `Parse_WhenFrontmatterMalformed_ReturnsZeroTags`
- `Parse_WhenDuplicateTags_DeduplicatesPreservingFirstOccurrence`

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- All existing tests pass: `dotnet test`
- New parser tests pass (every method listed above): `dotnet test --filter FullyQualifiedName~NoteMetadataParserTests`
- YamlDotNet is the only dependency added in this phase: `git diff Notes/Notes.csproj` shows exactly one new `PackageReference` line.

#### Manual Verification:

- Inspect the diff for `Notes/Services/NoteMetadataParser.cs` and confirm the Markdig pipeline is constructed once (static field), not per call.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation that the parser's contract feels right before wiring it into the indexer.

---

## Phase 2: Search Index and Message Integration

### Overview

Add a singleton search index that uses the Phase 1 parser. The index stores only metadata in memory `(relativePath, fileName, tags)` — bodies are NOT cached. It builds asynchronously off the UI thread on workspace change, exposes an `IsReady` state via a new `SearchIndexStateChangedMessage`, and supports a cancellable `Task<...> Search(...)` that scans filename + tags in memory first and only opens candidate files from disk for query tokens that aren't already matched. The two file-mutating VMs (`NoteEditorViewModel`, `NoteTreeViewModel`) are edited to publish a new `NoteSavedMessage` after their save sites. `INoteFileService` gains an `async Task<string> ReadAsync(string absolutePath)` method for the lazy body-read path. At phase-end the index is correct, current, cancellable, and invisible to users.

### Changes Required:

#### 1. Add `NoteSavedMessage` and `SearchIndexStateChangedMessage`

**File**: `Notes/Messaging/Messages.cs`

**Intent**: Two new messages. `NoteSavedMessage` is published by every code path that writes a `.md` file; the index uses the supplied content to re-parse without a disk read. `SearchIndexStateChangedMessage` is published by the index when its `IsReady` state transitions; the Search VM listens to drive the "Indexing notes…" hint.

**Contract**:
- `public sealed record NoteSavedMessage(string RelativePath, string Content);`
- `public sealed record SearchIndexStateChangedMessage(bool IsReady);`

Both appended to the existing records.

#### 2. Define `NoteSearchResult` model

**File**: `Notes/Models/NoteSearchResult.cs` (new)

**Intent**: The shape returned to the search ViewModel for each hit. Carries enough to (a) display the row, (b) reconstruct the existing `NoteTreeNode` for the editor flow.

**Contract**: `public sealed record NoteSearchResult(string RelativePath, string FileName);` — `FileName` is the leaf (no folders) for display; `RelativePath` is the forward-slash path used to publish `NoteSelectedMessage` (constructing a `NoteTreeNode` of `Kind = File`).

#### 3. Extend `INoteFileService` with `ReadAsync`

**File**: `Notes/Services/INoteFileService.cs` and `Notes/Services/NoteFileService.cs`

**Intent**: The lazy body-read path inside `Search` needs an async disk read so the search task doesn't block a thread-pool thread on synchronous IO. The existing sync `Read` stays for the editor and the index's build path (where we're already on a background task and the cost is amortized over many files).

**Contract**:
- Add to interface: `Task<string> ReadAsync(string absolutePath, CancellationToken cancellationToken = default);`
- Implementation: uses `File.ReadAllTextAsync(absolutePath, Utf8NoBom, cancellationToken)`; returns `string.Empty` if the file does not exist (`!File.Exists(...)` short-circuit, matching the sync `Read` behavior). The shared `Utf8NoBom` field is reused.

#### 4. Define `INoteSearchIndex`

**File**: `Notes/Services/INoteSearchIndex.cs` (new)

**Intent**: Contract for the metadata index. Three responsibilities: stay current with disk reality (via messages), expose its build-readiness state, and answer search queries (which may involve disk reads).

**Contract**:
- `bool IsReady { get; }` — true once at least one build for the current workspace has completed; false on construction and during any in-flight build.
- `Task<IReadOnlyList<NoteSearchResult>> Search(string query, bool includeTemplates, CancellationToken cancellationToken = default)` — total, returns empty list for null/whitespace query OR when `IsReady == false`. AND semantics over whitespace-tokenized query; each token must appear case-insensitively in at least one of (filename, normalized tags joined by spaces, body). Results sorted by `RelativePath` ordinal. Cancellation respected between entries and between body reads.
- No public methods for lifecycle — the index manages itself via `IRecipient<...>`.

#### 5. Implement `NoteSearchIndex`

**File**: `Notes/Services/NoteSearchIndex.cs` (new)

**Intent**: Sealed class implementing `INoteSearchIndex`, `IRecipient<WorkspaceChangedMessage>`, `IRecipient<NoteSavedMessage>`, `IRecipient<NoteDeletedMessage>`. Constructor takes `IMessenger`, `IWorkspaceScanner`, `INoteFileService`, `INoteMetadataParser`; calls `messenger.RegisterAll(this)` once at construction.

Internal state:
- `private IReadOnlyDictionary<string, MetadataEntry> _entries = new Dictionary<string, MetadataEntry>();` — keyed by relative path. `MetadataEntry` is a private nested record `(string FileName, IReadOnlyList<string> Tags)` — NO `Body` field.
- `private string? _workspacePath;`
- `private bool _isReady;`
- `private CancellationTokenSource? _buildCts;`
- `private readonly List<PendingMutation> _pendingDuringBuild = new();` — UI-thread-only buffer of save/delete operations that arrived while `_isReady == false`. Private nested record `PendingMutation(string RelativePath, MetadataEntry? Entry)` — `Entry` is the new metadata for an upsert, or `null` to signal a deletion. Drained against the newly-built dictionary during the swap; without this, a new-note creation (or save) that lands while BuildAsync is still scanning is overwritten when the swap assigns `_entries = newEntries`, and stays invisible to search until the next save or workspace switch.

Behavior:
- `Receive(WorkspaceChangedMessage)`: cancel + dispose `_buildCts` if present; clear `_pendingDuringBuild` (a buffered op from the previous workspace must not leak into the new one); create a new CTS; set `_isReady = false`; publish `SearchIndexStateChangedMessage(false)`; capture `_workspacePath`; call `_ = Task.Run(() => BuildAsync(workspacePath, cts.Token), cts.Token)`. Wrapping in `Task.Run` detaches the work from the UI thread (matching the design called out in §Critical Implementation Details and §Performance Considerations) — without it, `await`s inside BuildAsync would resume on the captured UI-thread SynchronizationContext and run `ScanMarkdownFiles` plus every `ReadAsync` continuation on the dispatcher. The discard-assignment keeps the message handler non-blocking; failures inside the build are caught and logged via Avalonia's logging (matching existing convention — see `NoteEditorViewModel.Receive(NewNoteRequestedMessage)` swallow pattern).
- `private async Task BuildAsync(string workspacePath, CancellationToken token)`:
  1. Get paths from `_scanner.ScanMarkdownFiles(workspacePath)`.
  2. Build a local `Dictionary<string, MetadataEntry> newEntries` of expected size.
  3. For each path: `token.ThrowIfCancellationRequested()`; compute absolute path; `var text = await _fileService.ReadAsync(absolutePath, token)`; `var meta = _parser.Parse(text)`; `newEntries[relativePath] = new MetadataEntry(Path.GetFileName(relativePath), meta.Tags)`.
  4. Swap on the UI thread: `await Dispatcher.UIThread.InvokeAsync(() => { foreach (var op in _pendingDuringBuild) { if (op.Entry is null) newEntries.Remove(op.RelativePath); else newEntries[op.RelativePath] = op.Entry; } _pendingDuringBuild.Clear(); _entries = newEntries; _isReady = true; _messenger.Send(new SearchIndexStateChangedMessage(true)); })`. The drain re-applies any save/delete that arrived while the build was running, so a new note created during the build (or a save that landed before BuildAsync's scan reached that file) survives the swap. Avalonia's `Dispatcher.UIThread.InvokeAsync` is the standard cross-thread marshal.
  5. Wrap the whole method body in `try/catch (OperationCanceledException) { /* cancelled — discard newEntries silently */ }`.
- `Receive(NoteSavedMessage)`: parse the supplied `Content` via `_parser.Parse(...)`; build a new `MetadataEntry`. Upsert into the dictionary via CoW: copy `_entries` to a new `Dictionary<>`, mutate, then assign back. (At PRD scale this is ~1k-entry copy on save — sub-millisecond.) If `!_isReady`, also append `new PendingMutation(RelativePath, entry)` to `_pendingDuringBuild` so the build's swap re-applies the upsert. (The CoW upsert still happens for the in-flight-build case too, but its target is the dictionary that's about to be discarded — the buffer is what survives.)
- `Receive(NoteDeletedMessage)`: same CoW pattern — copy, remove key, assign. If `!_isReady`, also append `new PendingMutation(RelativePath, null)` to `_pendingDuringBuild`.
- `Search(query, includeTemplates, token)`:
  1. If `string.IsNullOrWhiteSpace(query)` or `!_isReady` → return `Array.Empty<NoteSearchResult>()`.
  2. Snapshot `var entries = _entries;` (atomic reference read) and `var workspace = _workspacePath;` (may be null only if Search runs before any workspace; the IsReady check covers this).
  3. Tokenize: `var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);` — guards against duplicate spaces.
  4. Filter candidates: `entries.Where(kv => includeTemplates || !kv.Key.StartsWith(".templates/", StringComparison.Ordinal))`.
  5. For each candidate, ordered by `RelativePath` ordinal:
     - `token.ThrowIfCancellationRequested()`.
     - First pass (in-memory): for each query token, mark as matched if filename or any tag contains it (`IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0`).
     - If every token is matched → add `new NoteSearchResult(relativePath, fileName)` to results, continue.
     - If any token remains unmatched: compute `absolutePath`; `try { var body = await _fileService.ReadAsync(absolutePath, token); } catch (FileNotFoundException) { continue; }` — stale entries are skipped silently (the `NoteDeletedMessage` for that file is presumably in flight).
     - For each unmatched token, check `body.IndexOf(token, OrdinalIgnoreCase) >= 0`.
     - If all eventually match → add to results.
  6. Return results as `IReadOnlyList<NoteSearchResult>`.

**Contract**: Implements `INoteSearchIndex` and the three `IRecipient<>` interfaces. Imperative loops (no LINQ heroics) keep debugging straightforward. The two-pass design ensures every filename/tag-only hit pays zero disk I/O; only entries with truly body-only matches incur a read.

#### 6. Publish `NoteSavedMessage` from `NoteEditorViewModel`

**File**: `Notes/ViewModels/NoteEditorViewModel.cs`

**Intent**: After every successful save inside the editor, broadcast `NoteSavedMessage` so the index can refresh that single entry without a disk reread. There is exactly one place in this file that calls `_fileService.Save(...)` (the `DoSave()` private method) plus a guard inside `WorkspaceChangedMessage` / `NoteSelectedMessage` flush flows that also persist via `_scheduler.Flush()` → `OnSave` → `DoSave()`; routing through `DoSave()` means we only need to publish there.

**Contract**: In `DoSave()`, place `_messenger.Send(new NoteSavedMessage(currentNote.RelativePath, _currentEditorText))` INSIDE the existing `try` block, on the line immediately after `_fileService.Save(absolutePath, _currentEditorText)`. The relative path is the same one the editor used to compute `absolutePath`. Position matters: the IOException / UnauthorizedAccessException catches that already wrap the Save call must also short-circuit the publish — placing the Send after the catch block would broadcast a NoteSavedMessage even when Save threw, putting the index out of sync with disk.

#### 7. Publish `NoteSavedMessage` from `NoteTreeViewModel`

**File**: `Notes/ViewModels/NoteTreeViewModel.cs`

**Intent**: New-note creation in `HandleNewNote()` saves an empty file at `success.AbsolutePath` (line 106). The index needs to learn about the new file even though its content is empty (zero tags, empty body, but it appears in searches that match its filename). Publish `NoteSavedMessage` with empty content immediately after the save.

**Contract**: After `_fileService.Save(success.AbsolutePath, string.Empty);`, publish `_messenger.Send(new NoteSavedMessage(newRelativePath, string.Empty))` where `newRelativePath` is the same value already computed later in the method for `FindNode`. Compute `newRelativePath` once up-front and reuse for both the message and the find.

#### 8. Register `NoteSearchIndex` in DI

**File**: `Notes/Program.cs`

**Intent**: Singleton, like every other service. Must be eagerly instantiated at app start so it registers with the messenger before `MainWindowViewModel.InitializeAsync` publishes the first `WorkspaceChangedMessage` (same lifecycle concern documented for the existing VMs in S-02). The DI registration is enough — the index is touched by the `NoteSearchViewModel` constructor in Phase 3, but for Phase 2 we need explicit eager resolution. Simplest pattern: resolve it from `App.Services` inside `App.OnFrameworkInitializationCompleted` right after the service provider is built, before showing the main window.

**Contract**: 
- `services.AddSingleton<INoteSearchIndex, NoteSearchIndex>();` near the parser registration in `BuildServiceProvider()`.
- In `App.axaml.cs` (or equivalent startup), add a single line resolving `App.Services.GetRequiredService<INoteSearchIndex>()` before the window is shown, so the index's constructor runs and it registers as a messenger recipient. Discard the return value.

#### 9. Tests for `NoteSearchIndex`

**File**: `Notes.Tests/NoteSearchIndexTests.cs` (new)

**Intent**: Cover async build (including cancellation), all three message handlers, IsReady state transitions, and the two-pass search semantics (including lazy body read). Uses a real `WeakReferenceMessenger` instance per test (fresh) and stubs for scanner + file service. Use the real `NoteMetadataParser` (already tested in Phase 1) so the index tests cover the integration with the parser.

The async build is observed through the public contract the production code already publishes: `SearchIndexStateChangedMessage(true)`. Each test that needs to await build completion registers a one-shot recipient on its `WeakReferenceMessenger` instance, captures a `TaskCompletionSource`, completes it when `IsReady == true` arrives, and awaits the TCS. A small inline helper at the bottom of the test class — `private static Task AwaitNextReady(IMessenger m) { var tcs = new TaskCompletionSource(); m.Register<SearchIndexStateChangedMessage>(new object(), (_, msg) => { if (msg.IsReady) tcs.TrySetResult(); }); return tcs.Task; }` — keeps each test readable. The index exposes no test-only members; what tests need to observe (build completion, IsReady transitions) is what the production API already broadcasts.

**Contract**: `public sealed class NoteSearchIndexTests` with tests minimum:
- `Construct_WhenCreated_IsReadyIsFalse`
- `Receive_WhenWorkspaceChangedMessage_PublishesNotReadyImmediately`
- `Receive_WhenWorkspaceChangedMessage_BuildsAsynchronouslyThenBecomesReady` (await build helper; assert `IsReady` becomes true and `SearchIndexStateChangedMessage(true)` is published)
- `Receive_WhenWorkspaceChangedMessageDuringBuild_CancelsFirstBuildAndStartsSecond` (kick first build, immediately kick second; assert first's results never appear in the final index, no exception escapes)
- `Receive_WhenNoteSavedMessage_UpsertsEntryWithoutCallingReadAsync` (StubNoteFileService asserts ReadAsync NOT called for the saved path)
- `Receive_WhenNoteSavedMessageForNewPath_AddsNewEntry`
- `Receive_WhenNoteDeletedMessage_RemovesEntry`
- `Receive_WhenNoteSavedMessageArrivesDuringBuild_SurvivesSwapIntoNewDictionary` (deterministic interleaving: stub scanner returns a fixed path list; stub file service blocks the first ReadAsync until the test gates it; while build is paused, publish `NoteSavedMessage` for a path NOT in the scanner's list; release the gate; await build completion; assert the new path is queryable)
- `Receive_WhenNoteDeletedMessageArrivesDuringBuild_AbsentFromNewDictionaryAfterSwap` (same interleaving; publish `NoteDeletedMessage` for a path the scanner DID return; release; assert the path is gone after swap)
- `Receive_WhenWorkspaceChangedMessageDuringBuild_PendingBufferFromPreviousBuildIsDiscarded` (start build A, publish a NoteSavedMessage that buffers, then publish a new WorkspaceChangedMessage before A's swap; assert the buffered op does NOT leak into workspace B's index)
- `Search_WhenQueryIsEmpty_ReturnsEmpty`
- `Search_WhenQueryIsWhitespace_ReturnsEmpty`
- `Search_WhenIndexNotReady_ReturnsEmpty`
- `Search_WhenSingleTermMatchesFilename_ReturnsMatchWithoutReadingBody` (StubNoteFileService asserts ReadAsync NOT called)
- `Search_WhenSingleTermMatchesTag_ReturnsMatchWithoutReadingBody`
- `Search_WhenSingleTermMatchesBodyOnly_ReadsBodyAndReturnsMatch` (StubNoteFileService asserts ReadAsync IS called for the matching note)
- `Search_WhenMultipleTermsOneMatchesFilenameOneMatchesBody_ReadsBodyForUnmatchedToken`
- `Search_WhenMultipleTerms_RequiresAllToMatch`
- `Search_WhenQueryIsMixedCase_MatchesCaseInsensitively`
- `Search_WhenTemplatePathPresent_ExcludesByDefault`
- `Search_WhenIncludeTemplatesTrue_IncludesTemplatePaths`
- `Search_WhenMultipleMatches_ResultsOrderedByRelativePath`
- `Search_WhenFileMissingDuringLazyRead_SkipsEntryWithoutThrowing`
- `Search_WhenCancellationRequestedMidScan_ThrowsOperationCanceled`
- Inline stubs at the bottom of the test class: `StubWorkspaceScanner` (configurable paths), `StubNoteFileService` (Dictionary<string, string> backing store for `Read` AND `ReadAsync`; counts read calls per path to enable the no-disk-read assertions; supports a configurable "missing" set to throw `FileNotFoundException`).

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- All existing tests pass: `dotnet test`
- New index tests pass: `dotnet test --filter FullyQualifiedName~NoteSearchIndexTests`
- The "no-disk-read" assertions in the search tests confirm the two-pass optimization works as designed
- Index is constructed at app startup (verify by temporary log in `NoteSearchIndex` constructor; remove before commit)

#### Manual Verification:

- Launch the app with a workspace containing a few notes; quit. Confirm no exception is thrown — async build completes silently.
- (Optional, dev-only) Add a temporary `[RelayCommand]` on `MainWindowViewModel` that awaits `App.Services.GetRequiredService<INoteSearchIndex>().Search("test", false)` and writes the count to a `MessageBox` or `Console`. Remove before committing.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation before building the UI on top.

---

## Phase 3: Search Overlay UI

### Overview

Add the user-visible search experience: a new `NoteSearchViewModel`, a `SearchView.axaml` UserControl that layers over the tree column, Ctrl+F + File-menu wiring, debounced query handling, the "Include templates" toggle, and result-click → open + close behavior.

### Changes Required:

#### 1. Add `OpenSearchRequestedMessage`

**File**: `Notes/Messaging/Messages.cs`

**Intent**: Marker message published when the user triggers search (Ctrl+F or menu). Mirrors the existing `NewNoteRequestedMessage` / `TogglePreviewRequestedMessage` pattern — the originating VM (`MainWindowViewModel`) doesn't know which VM listens.

**Contract**: `public sealed record OpenSearchRequestedMessage;` — appended to the existing records.

#### 2. Implement `NoteSearchViewModel`

**File**: `Notes/ViewModels/NoteSearchViewModel.cs` (new)

**Intent**: Manages the overlay's state and search interaction. Singleton. Recipient of `OpenSearchRequestedMessage` (open the overlay), `WorkspaceChangedMessage` (close + clear if open during a workspace switch), and `SearchIndexStateChangedMessage` (mirror the index's IsReady state into `IsIndexReady` for the view to bind). Holds the debounce timer AND a CancellationTokenSource for the in-flight Search task.

**Contract**:
- `public sealed partial class NoteSearchViewModel : ObservableObject, IRecipient<OpenSearchRequestedMessage>, IRecipient<WorkspaceChangedMessage>, IRecipient<SearchIndexStateChangedMessage>`
- `[ObservableProperty] string _query = "";`
- `[ObservableProperty] bool _isOpen;`
- `[ObservableProperty] bool _includeTemplates;`
- `[ObservableProperty] bool _isIndexReady;` — bound to the view's "Indexing notes…" hint visibility.
- `[ObservableProperty] IReadOnlyList<NoteSearchResult> _results = Array.Empty<NoteSearchResult>();`
- `[ObservableProperty] NoteSearchResult? _selectedResult;`
- `private CancellationTokenSource? _searchCts;`
- `partial void OnQueryChanged(string value)` → bump 150 ms `DispatcherTimer`; on tick → cancel + dispose `_searchCts`, create new CTS, kick off `_ = RunSearchAsync(cts.Token)`.
- `partial void OnIncludeTemplatesChanged(bool value)` → re-run search immediately (no debounce; checkbox is intentional). Same cancellation pattern as OnQueryChanged.
- `private async Task RunSearchAsync(CancellationToken token)`:
  1. If `string.IsNullOrWhiteSpace(Query)` → set `Results = Array.Empty<...>()` and return (no need to call the index).
  2. `try { var hits = await _index.Search(Query, IncludeTemplates, token); if (!token.IsCancellationRequested) Results = hits; }`
  3. `catch (OperationCanceledException) { /* swallow — superseded by a newer query */ }`
- `[RelayCommand] OpenResult(NoteSearchResult? result)` → if result is null, no-op; else construct a `NoteTreeNode(result.FileName, result.RelativePath, NoteNodeKind.File, Array.Empty<NoteTreeNode>())`, publish `NoteSelectedMessage(node)`, then invoke `Close()`.
- `[RelayCommand] Close()` → cancel + dispose `_searchCts`; set `IsOpen = false`, `Query = ""`, `IncludeTemplates = false`, `Results = Array.Empty<NoteSearchResult>()`, `SelectedResult = null`. Do NOT reset `IsIndexReady` — that's driven by the index, not the overlay's open state.
- `Receive(OpenSearchRequestedMessage)`: set `IsOpen = true`.
- `Receive(WorkspaceChangedMessage)`: invoke `Close()` so a workspace switch doesn't leave the user staring at stale results.
- `Receive(SearchIndexStateChangedMessage)`: set `IsIndexReady = message.IsReady`. If transitioning to true AND the overlay is open AND `Query` is non-empty → re-trigger search (the user typed a query that was held while the index built; now it can produce results).
- Constructor: takes `IMessenger`, `INoteSearchIndex`, AND an optional debounce-interval parameter (default 150 ms) for testability. Uses Avalonia's `DispatcherTimer` for the debounce. Sets `IsIndexReady = _index.IsReady` once on construction so a late-attached VM still reflects the right state.

Notes for the implementer:
- Setting `Query = ""` from `OpenResult` / `Close` triggers `OnQueryChanged("")`. The debounce-tick short-circuits on empty Query — fine.
- For testability of the async Search path, tests pass `TimeSpan.Zero` for the debounce and await the `PropertyChanged` event the `[ObservableProperty]` source generator already raises on `Results`. A small inline helper — `private static Task AwaitNextResultsChange(NoteSearchViewModel vm) { var tcs = new TaskCompletionSource(); PropertyChangedEventHandler? h = null; h = (_, args) => { if (args.PropertyName == nameof(vm.Results)) { vm.PropertyChanged -= h; tcs.TrySetResult(); } }; vm.PropertyChanged += h; return tcs.Task; }` — keeps tests readable without any test-only members on the VM. Matches the message-driven pattern used for `NoteSearchIndex` tests.
- The `Search` call is async, but `OnQueryChanged` is a sync partial method. The discard-assignment `_ = RunSearchAsync(...)` is the standard pattern in this codebase for fire-and-forget background work (mirrors `NoteTreeViewModel.HandleNewNote` use of `async void` recipients).

#### 3. Extend `ViewModelLocator`

**File**: `Notes/ViewModels/ViewModelLocator.cs`

**Intent**: Expose the new VM through the same pattern used for Main/Tree/Editor.

**Contract**: Add `public NoteSearchViewModel? Search => Resolve<NoteSearchViewModel>();` alongside the existing three properties.

#### 4. Add `SearchCommand` to `MainWindowViewModel`

**File**: `Notes/ViewModels/MainWindowViewModel.cs`

**Intent**: New menu/keyboard command that publishes `OpenSearchRequestedMessage`. Mirrors the existing `NewNote` / `TogglePreview` commands exactly — fire-and-forget message send, nothing else.

**Contract**:
```csharp
[RelayCommand]
private void Search() => _messenger.Send(new OpenSearchRequestedMessage());
```
Placed near the existing commands in the file.

#### 5. Create `SearchView.axaml` + code-behind

**File**: `Notes/Views/SearchView.axaml` and `Notes/Views/SearchView.axaml.cs` (new)

**Intent**: The overlay's visual representation. Lays over the tree column when `IsOpen` is true. Contains: TextBox for query (focused on open), checkbox for "Include templates", ListBox for results showing filename + relative path, and a stub "Indexing notes…" hint visible while `IsIndexReady == false`. Esc closes; Enter on a selected result opens it. Code-behind is empty beyond `InitializeComponent()` plus a single focus-on-show wiring — focus handling happens via XAML wherever possible.

**Contract**:
- Root: `<UserControl x:DataType="vm:NoteSearchViewModel" DataContext="{ReflectionBinding Search, Source={StaticResource Locator}}" IsVisible="{Binding IsOpen}">`
- Background: opaque (`{DynamicResource ThemeBackgroundBrush}` or similar) so it fully obscures the tree behind it.
- Layout: `<Grid RowDefinitions="Auto,Auto,*">`:
  - Row 0: `<TextBox Text="{Binding Query, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Watermark="Search…">` with `KeyBindings` for Esc → `CloseCommand`.
  - Row 1: `<CheckBox Content="Include templates" IsChecked="{Binding IncludeTemplates, Mode=TwoWay}" />`
  - Row 2: A `Grid` (no rows/cols needed) holding two stacked children, mutually exclusive via visibility:
    - `<TextBlock Text="Indexing notes…" HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0.6" IsVisible="{Binding !IsIndexReady}" />` — shown only while the index is not ready.
    - `<ListBox ItemsSource="{Binding Results}" SelectedItem="{Binding SelectedResult, Mode=TwoWay}" IsVisible="{Binding IsIndexReady}">` with an `ItemTemplate` showing `FileName` prominently + `RelativePath` faded below. Double-click on an item → `OpenResultCommand` with the item as parameter. Enter key on the ListBox also triggers it.
- For focus: in `SearchView.axaml.cs`, subscribe to `IsVisibleChanged` and, when becoming visible, call `Dispatcher.UIThread.Post(() => textBox.Focus())` so the TextBox gains keyboard focus on open. This is the one piece of unavoidable code-behind for UX correctness; document with a one-line comment.

Note: this view binds directly to `Search` through the locator (per the existing pattern); it does NOT need a parent VM to expose it. The "Indexing notes…" hint shows regardless of whether the user has typed a query — if they typed before the index was ready, the hint replaces what would otherwise be an empty result list and makes the cause transparent.

#### 6. Layer `SearchView` over the tree column in `MainWindow.axaml`

**File**: `Notes/MainWindow.axaml`

**Intent**: Make the overlay live on top of the tree column so the user sees it covering the tree when search opens. Avalonia's `Grid` stacks children naturally — adding the `<views:SearchView />` as a second child in the same `Grid.Column="0"` cell paints it on top of the existing `<views:NoteTreeView />`. The overlay's `IsVisible` binding to `Search.IsOpen` controls when it's actually painted.

**Contract**: Within the existing `<Grid ColumnDefinitions="*,Auto,2*">`, add `<views:SearchView Grid.Column="0" />` AFTER `<views:NoteTreeView Grid.Column="0" />` (Avalonia paints children in document order; later children paint on top). Add `KeyBinding Gesture="Ctrl+F" Command="{Binding SearchCommand}"` to the window's `KeyBindings` next to the existing Ctrl+N / Ctrl+E. Add `<MenuItem Header="_Search…" Command="{Binding SearchCommand}" InputGesture="Ctrl+F" />` to the File menu above the `_Change Notes Folder…` item.

#### 7. Register `NoteSearchViewModel` in DI

**File**: `Notes/Program.cs`

**Intent**: Singleton, alongside the other VMs.

**Contract**: `services.AddSingleton<NoteSearchViewModel>();` next to the other VM registrations.

#### 8. Tests for `NoteSearchViewModel`

**File**: `Notes.Tests/NoteSearchViewModelTests.cs` (new)

**Intent**: Verify lifecycle (open/close), the debounce path, message subscriptions, result-click flow, and the IncludeTemplates propagation. Uses inline stubs at the bottom of the file (matching `NoteTreeViewModelTests` style): `StubNoteSearchIndex` recording `(query, includeTemplates)` of every `Search` call and returning a configurable result list.

**Contract**: `public sealed class NoteSearchViewModelTests` with tests minimum:
- `Construct_WhenIndexAlreadyReady_MirrorsIsIndexReadyTrue`
- `Construct_WhenIndexNotReady_MirrorsIsIndexReadyFalse`
- `Receive_WhenOpenSearchRequestedMessage_SetsIsOpenTrue`
- `Receive_WhenWorkspaceChangedMessageWhileOpen_ClosesAndClearsState`
- `Receive_WhenSearchIndexStateChangedTrueAndOverlayOpenWithQuery_TriggersSearch`
- `Receive_WhenSearchIndexStateChangedFalse_SetsIsIndexReadyFalse`
- `OnQueryChanged_WhenCalled_TriggersSearchAfterDebounce` (test passes `TimeSpan.Zero` debounce + awaits the `Results` `PropertyChanged` event via the inline helper)
- `OnQueryChanged_WhenSetToEmpty_ClearsResultsWithoutCallingIndex`
- `OnQueryChanged_WhenCalledTwiceRapidly_CancelsFirstSearchBeforeStartingSecond` (StubNoteSearchIndex records cancellation tokens; assert first token was cancelled)
- `OnIncludeTemplatesChanged_WhenToggled_TriggersSearchImmediately`
- `OpenResult_WhenCalledWithResult_PublishesNoteSelectedMessageAndClosesOverlay`
- `OpenResult_WhenCalledWithNull_DoesNothing`
- `Close_WhenCalled_ResetsQueryAndIncludeTemplatesAndResultsAndSelectedResult`
- `Close_WhenCalledWithSearchInFlight_CancelsTheSearch`
- `Search_WhenCalledFromTimer_PassesCurrentQueryAndIncludeTemplatesToIndex`
- `Search_WhenIndexThrowsOperationCanceled_DoesNotSurfaceAsError`

`StubNoteSearchIndex` records `(query, includeTemplates, cancellationToken)` for every Search call, exposes a configurable result list, and can be made to delay (await a `TaskCompletionSource`) so the cancellation test can deterministically interleave a second call before the first completes.

### Success Criteria:

#### Automated Verification:

- Build succeeds: `dotnet build`
- All existing tests pass: `dotnet test`
- New ViewModel tests pass: `dotnet test --filter FullyQualifiedName~NoteSearchViewModelTests`
- No `Async` suffix violations introduced (review new files against the project convention)

#### Manual Verification:

- Launch the app; press Ctrl+F: overlay appears over the tree, textbox is focused, the area below shows "Indexing notes…" briefly (or instantly skips to empty if the workspace is tiny).
- Once "Indexing notes…" disappears, the area is empty (no query yet).
- Place keyboard focus inside the editor's `TextEditor`, then press Ctrl+F — confirm the overlay still opens (AvaloniaEdit ships an opt-in `SearchPanel.Install(editor)` we don't call; this step guards against a future version installing it by default and swallowing the gesture).
- Type a query that matches a filename: results appear within ~150 ms.
- Type a query that only matches a tag (in a note's frontmatter): results include that note.
- Type a query that only matches body content: results include that note (lazy body read kicks in).
- Type multiple words: only notes containing ALL words (across any field) appear.
- Type rapidly (faster than the debounce): the result list does not flicker with stale partial results.
- Create a note in `.templates/` containing a unique tag; default search does not return it; check "Include templates"; results now include it.
- Click a result: the note opens in the editor, the overlay closes, query is empty next time Ctrl+F is pressed.
- Press Esc while the overlay is open: overlay closes; tree visible again.
- Edit a note (autosave fires after 500 ms idle), then search for newly-added body content: hit appears (index refreshed via `NoteSavedMessage`).
- Delete a note: search no longer returns it.
- Change workspace via the menu while the overlay is open: overlay closes; new workspace loads; press Ctrl+F again — "Indexing notes…" briefly visible during the new workspace's build; then search works.
- File menu shows the new `_Search…` item with the `Ctrl+F` gesture hint.

**Implementation Note**: After completing this phase and all automated verification passes, walk through every manual verification step above. The slice ships only when each one passes.

---

## Testing Strategy

### Unit Tests:

- **`NoteMetadataParser`** — exhaustive coverage of YAML edge cases (15+ tests). The parser is pure, so the test cost is low and the safety value is high; this is where malformed-YAML-related bugs hide.
- **`NoteSearchIndex`** — covers async build (including cancellation on re-entrant `WorkspaceChangedMessage`), incremental upsert on save (with explicit "no disk read happened" assertion via a stub file service whose `ReadAsync` counts calls per path), removal on delete, two-pass query semantics (filename/tag fast path with no disk read, body-only slow path with disk read), `IsReady` state transitions, cancellation mid-search, missing-file race during lazy body read, and the `IncludeTemplates` toggle. Stubs for `IWorkspaceScanner` + `INoteFileService` (covering both `Read` and `ReadAsync`); real `NoteMetadataParser` for parser integration coverage.
- **`NoteSearchViewModel`** — covers debounce, lifecycle messages, the result-click flow, the no-op cases (null result, empty query), `IsIndexReady` mirroring on `SearchIndexStateChangedMessage`, search re-trigger when index becomes ready with a non-empty query, and cancellation of in-flight searches when the user keeps typing. Uses `TimeSpan.Zero` debounce; awaits the VM's existing `Results` `PropertyChanged` event for async completion. `StubNoteSearchIndex` records cancellation tokens for assertion.

### Integration Tests:

- None. The architecture decomposes cleanly into pure-logic services that unit tests cover, and the manual smoke test in Phase 3 exercises the end-to-end flow under Avalonia. Per project history (S-01, S-02), Avalonia-host integration tests have not been adopted.

### Manual Testing Steps:

1. Create a test workspace with: a note containing valid `tags: [project, urgent]`, a note with malformed YAML, a note without any frontmatter, a note inside `.templates/`, and a few "plain" notes for body-match testing.
2. Press Ctrl+F. Confirm the overlay opens, textbox is focused, empty state shows no results.
3. Type the partial filename of one note. Confirm it appears in results.
4. Type `urgent`. Confirm only the note with that tag matches (assuming no body mention).
5. Type a unique body word. Confirm the matching note appears.
6. Type two words. Confirm AND semantics (only notes with both words).
7. Type a partial match for the malformed-YAML note's filename. Confirm it appears (tags missing but filename/body searchable).
8. Type a query that only matches a `.templates/` note. Confirm no results. Check "Include templates"; confirm now visible.
9. Click a result. Confirm: note opens in editor, overlay closes, next Ctrl+F starts with empty query.
10. Press Esc with the overlay open. Confirm overlay closes.
11. Edit a note: add a new word to the body, wait for autosave (~1 s). Press Ctrl+F, search for the new word. Confirm hit.
12. Delete a note via the tree context menu. Press Ctrl+F, search for that note's filename. Confirm zero results.
13. Switch workspace via File menu while overlay is open. Confirm overlay closes; search the new workspace works.

## Performance Considerations

Memory: the in-memory state is metadata-only. Per-note overhead is roughly `(relativePath + fileName + N tags) × pointer overhead` — well under 1 KB per note even at the high end. A 10k-note workspace stays under ~10 MB of managed memory for the index itself; bodies never enter the heap.

Search latency (typical case — query matched by filename or tag): pure in-memory dictionary walk; expect single-digit milliseconds at PRD scale.

Search latency (worst case — query token only found in bodies, no in-memory match for any note): every candidate's body is read from disk via `ReadAsync`. On a warm OS file cache this is a sequential read at ~GB/s — a few tens of milliseconds for ~10 MB across 1k files. On a cold cache (immediately after launch or a workspace switch on a slow disk) the first such search may take noticeably longer. The two-pass design ensures this cost is paid only for the specific notes where filename/tag isn't enough. The 150 ms debounce keeps it from happening on every keystroke.

Index build (workspace change): runs off the UI thread via `Task.Run`. Reads every file + parses frontmatter. Expect ~100–500 ms on a 1k-note workspace with a warm disk cache; the UI stays responsive throughout because the build doesn't touch the dispatcher thread until the final atomic swap. The "Indexing notes…" hint covers the user-visible portion.

Cancellation: in-flight Search tasks are cancelled when the user keeps typing, so superseded queries don't compete with the new one for thread-pool or disk bandwidth.

Should the workspace grow past ~10k notes, the upgrade path is a contained refactor: `INoteSearchIndex` is the only surface that needs to change. An inverted-index implementation can replace the dictionary scan behind the same interface without touching the parser, the messages, or the ViewModel. Not in scope for MVP.

## Migration Notes

None. Existing settings, notes, and tree state are untouched. Notes without `tags:` frontmatter (the universe of existing notes) are indexed with empty tags — searchable by name and body as before, just with no tag-matching capability.

## References

- Roadmap: `context/foundation/roadmap.md` §S-03
- PRD: `context/foundation/prd.md` (FR-005, FR-006, US-01)
- Tech stack: `context/foundation/tech-stack.md`
- Previous slice plan: `context/changes/note-editor-and-preview/plan.md`
- Previous slice brief: `context/changes/note-editor-and-preview/plan-brief.md`
- Project conventions: `/home/lysy/Projects/Notes/CLAUDE.md`
- YamlDotNet on NuGet: https://www.nuget.org/packages/YamlDotNet (v18, .NET 10-compatible)
- Existing MVVM + DI + messaging composition: `Notes/Program.cs`, `Notes/ViewModels/ViewModelLocator.cs`
- Pattern for layered overlay: standard Avalonia `Grid` with multiple children in the same cell (paint order = document order)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Metadata Parser

#### Automated

- [x] 1.1 Build succeeds: `dotnet build` — 2508520
- [x] 1.2 All existing tests pass: `dotnet test` — 2508520
- [x] 1.3 New parser tests pass: `dotnet test --filter FullyQualifiedName~NoteMetadataParserTests` — 2508520
- [x] 1.4 YamlDotNet is the only dependency added in this phase — 2508520

#### Manual

- [x] 1.5 Markdig pipeline confirmed constructed once (static field), not per call — 2508520

### Phase 2: Search Index and Message Integration

#### Automated

- [x] 2.1 Build succeeds: `dotnet build` — a618507
- [x] 2.2 All existing tests pass: `dotnet test` — a618507
- [x] 2.3 New index tests pass: `dotnet test --filter FullyQualifiedName~NoteSearchIndexTests` — a618507
- [x] 2.4 No-disk-read assertions in search tests pass (two-pass optimization verified) — a618507
- [x] 2.5 Index is constructed at app startup (verified by temporary log) — a618507

#### Manual

- [x] 2.6 App launches with workspace containing notes; no exception thrown — a618507
- [ ] 2.7 (Optional) Dev-only Search probe returns expected counts

### Phase 3: Search Overlay UI

#### Automated

- [x] 3.1 Build succeeds: `dotnet build`
- [x] 3.2 All existing tests pass: `dotnet test`
- [x] 3.3 New ViewModel tests pass: `dotnet test --filter FullyQualifiedName~NoteSearchViewModelTests`
- [x] 3.4 No `Async` suffix violations introduced (matching project convention; `Task ReadAsync` on `INoteFileService` is allowed because its sync sibling `Read` exists)

#### Manual

- [x] 3.5 Ctrl+F opens overlay over the tree; textbox is focused; "Indexing notes…" hint shows briefly (or instantly disappears for tiny workspaces)
- [x] 3.6 Filename query returns matches within ~150 ms
- [x] 3.7 Tag-only query (no body match) returns matches
- [x] 3.8 Body-only query returns matches (lazy body read fires)
- [x] 3.9 Multi-word query enforces AND semantics across fields
- [x] 3.10 Rapid typing does not flicker stale partial results (search cancellation works)
- [x] 3.11 `.templates/` notes excluded by default; included after toggling checkbox
- [x] 3.12 Result click opens note in editor + closes overlay; next Ctrl+F starts with empty query
- [x] 3.13 Esc closes the overlay
- [x] 3.14 Edit a note → autosave → search for new body content returns hit
- [x] 3.15 Delete a note → search no longer returns it
- [x] 3.16 Workspace switch while overlay open closes overlay; next Ctrl+F shows "Indexing notes…" briefly; new workspace search works
- [x] 3.17 File menu shows `_Search…` item with Ctrl+F gesture hint
- [x] 3.18 Ctrl+F with focus inside the editor's `TextEditor` still opens the overlay (AvaloniaEdit doesn't swallow the gesture)
