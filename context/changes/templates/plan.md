# Templates — Note-from-Template with a Typed Form Implementation Plan

## Overview

Add the S-04 "north star" slice: a user picks a template (a `.md` file in `.templates/` whose YAML frontmatter declares a typed `form:` schema and whose body carries `{{placeholder}}` tokens), fills a form generated from the field definitions, and the app writes a **new** note with the form block stripped from the frontmatter and every `{{field}}` substituted in the body.

Template *creation* is already free: `.templates/` is created via the **New Folder** command landed in `note-tree-folder-management`, and a template is a normal `.md` note created inside it. This plan builds only the **note-from-template** read/render/generate path.

## Current State Analysis

The prerequisite change `note-tree-folder-management` has landed (`status: impl_reviewed`), making the tree directory-aware and `.templates/` reliably creatable/visible. Key surface this plan builds on (verified against current source, post-prerequisite):

- **New-note tail is intact and reusable.** `NoteTreeViewModel.HandleNewNote()` (`Notes/ViewModels/NoteTreeViewModel.cs:101-141`) prompts → validates via `INameValidator.ValidateNoteName` → `_fileService.Save(success.AbsolutePath, string.Empty)` (`:131`) → `_messenger.Send(new NoteSavedMessage(...))` (`:132`) → `LoadTreeCommand` reload → select (`:134-140`). The literal `string.Empty` is the template-content seam.
- **Template discovery is free.** `WorkspaceScanner.ScanMarkdownFiles` (`Notes/Services/WorkspaceScanner.cs:32-42`) already returns `.templates/daily.md` etc. — the dotfile filter (`:35`) matches *filenames* only, not directories. Listing templates = filter scanner output by the `.templates/` prefix at top level.
- **Frontmatter parse pattern exists but reads only `tags`.** `NoteMetadataParser` (`Notes/Services/NoteMetadataParser.cs:15-50`) uses a static Markdig `UseYamlFrontMatter()` pipeline + a YamlDotNet `IDeserializer` (lower-case naming, `IgnoreUnmatchedProperties`), with a deliberate broad `catch (Exception) → Empty` (`:46-49`; see `context/foundation/lessons.md` — do not narrow). The `form` parser/model is net-new but mirrors this exactly.
- **No YAML serializer, no substitution engine, no dynamic form** exist anywhere. All net-new.
- **Dialog idiom is uniform:** code-behind `Window` + private result field + `static Task<T> Show(owner, …)` + a focused `I…DialogService` resolving the owner via `(Application.Current as App)?.MainWindow` (`Notes/Views/NewNoteDialog.axaml.cs:21-34`, `Notes/Services/NewNoteDialogService.cs:10-19`).
- **Coordination is exclusively `IMessenger` records** (`Notes/Messaging/Messages.cs`); menu commands live on `MainWindowViewModel` and send request messages that `NoteTreeViewModel` handles (`MainWindowViewModel.cs:78-88`, `NoteTreeViewModel.cs:73-95`).
- **DI:** every service is `AddSingleton<IInterface, Impl>` in `Notes/Program.cs:37-51`; VMs are singletons; `MainWindow` is the only transient.

### Key Discoveries:

- Template-content seam: `Notes/ViewModels/NoteTreeViewModel.cs:131-132` (substitute rendered text for `string.Empty`).
- Frontmatter parse pattern to mirror: `Notes/Services/NoteMetadataParser.cs:15-50`; broad-catch convention is deliberate (`context/foundation/lessons.md`).
- Dialog precedent (returns data + takes a validation callback): `Notes/Views/NewNoteDialog.axaml.cs`, service `Notes/Services/NewNoteDialogService.cs`.
- Menu/keybinding/message pattern: `Notes/MainWindow.axaml:12-31` (KeyBindings + File menu) → `MainWindowViewModel` `[RelayCommand]` → message → `NoteTreeViewModel` recipient.
- Dynamic-form idiom (confirmed, net-new): `ItemsControl` over per-field-type VMs with one implicit `DataTemplate DataType="…"` each — consistent with `NoteTreeView.axaml:14` / `SearchView.axaml:42` `DataType`-keyed templates; compiled bindings stay intact inside each per-type template.
- `NoteFileService.Save` (`Notes/Services/NoteFileService.cs:39-42`) does **not** create parent directories — irrelevant here because generated notes target an existing selected folder (same constraint New Note already lives under).

## Desired End State

A user with at least one template in `.templates/` chooses **File → New from Template…** (or Ctrl+T), picks a template from a flat list, fills a typed form (text/date/number/dropdown), and a new note appears in the currently-selected tree folder (named via the existing name prompt), opened in the editor, with the `form:` block gone and all declared `{{field}}` tokens replaced. With no templates, the menu item is disabled and Ctrl+T does nothing.

Verify: create `.templates/meeting.md` with a `form` schema + body placeholders; New from Template → fill form → confirm the generated note's frontmatter has no `form` key, the body has substituted values, unknown `{{tokens}}` survive, and blank fields render empty.

## What We're NOT Doing

- **Apply/insert a template into an existing open note** — explicitly OUT of MVP (first post-MVP feature); no AvaloniaEdit caret/insert plumbing.
- **No YAML serializer / frontmatter re-emission.** Other frontmatter keys pass through **verbatim** (textual strip of the `form` block only — no deserialize→reserialize round-trip that would reorder/restyle/strip comments).
- **No `.templates/` exclusion** from the tree, scanner, or search index. Templates stay visible (that's how they're created/edited). The existing query-time search filter (`NoteSearchIndex`) and its "Include templates" toggle are left untouched.
- **No `default`/`required`/optional flags** on fields — fields carry `type`/`label` (+ `entries` for dropdown, + optional `format` for date). Every field is shown; blank → empty string.
- **No nested template subfolders** — picker lists `.templates/` top level flat.
- **No first-template bootstrap code** — solved generically by `note-tree-folder-management`.
- **No filename auto-suggestion** — the generated note is named through the existing New Note prompt.

## Implementation Approach

Three layers, built bottom-up so each phase is independently verifiable:

1. **Pure engine** (no UI, no IO): parse the `form` schema into a typed model; render = body-only `{{field}}` substitution over a `name→formatted-string` map + textual strip of the `form` block. Fully unit-tested against the locked semantics.
2. **Dynamic typed form** (UI): per-field-type VMs that own typed→string formatting (ISO/format-override dates, locale numbers), composed into an `ItemsControl`-driven dialog returning collected values.
3. **Catalog + picker + entry point + orchestration**: list templates, drive `HasTemplates` for command enablement, wire the menu/keybinding/message, and orchestrate picker → form → render → reuse the New Note save tail.

Value formatting lives in the **field VMs** (phase 2), so the **engine** (phase 1) stays a pure string transformer over already-formatted string values — keeping it trivially testable and UI-free.

## Critical Implementation Details

- **Form-block strip is textual, not a YAML round-trip.** The product ethos is "users type their own frontmatter" — other keys must survive verbatim (order, style, comments). Remove the `form:` top-level line and its more-indented continuation lines up to the next top-level key (a non-space-prefixed line) or the end of the frontmatter block. If, after removal, the frontmatter contains no remaining keys, drop the whole `---\n…\n---` fence (no empty `---\n---`). This logic is the single non-obvious part of the engine — see the Phase 1 contract for the shape.
- **Substitute declared fields only.** A `{{x}}` token is replaced **only if `x` is a declared field** in the parsed `form`; undeclared tokens are left exactly as written (PRD "no leftover placeholder syntax" applies to *declared* fields). A declared field with a blank/missing value → replaced with empty string.
- **Body-only substitution.** Tokens inside the frontmatter region are never substituted; only the body (everything after the frontmatter fence) is scanned.
- **Field render order = template document order.** `form` is a YAML map; the parser must preserve the template's key order so the form renders fields top-to-bottom as authored. Deserialize into an order-preserving structure (or capture key order explicitly) rather than assuming `Dictionary<,>` ordering.
- **Malformed/absent `form`** → treat as a template with **no fields** (a static copy): no form dialog, straight to name → render (which becomes a plain substitution that changes nothing but still strips a malformed `form` line if present). Follow the broad-catch → empty convention from `NoteMetadataParser` (`lessons.md`).

## Phase 1: Template Schema + Render Engine

### Overview

Net-new pure models, a schema parser (mirroring `NoteMetadataParser`), and a render service that performs body-only substitution and textual `form`-block strip. No UI, no IO — fully unit-tested.

### Changes Required:

#### 1. Template models

**File**: `Notes/Models/FormField.cs`, `Notes/Models/FormDefinition.cs`

**Intent**: Carry the parsed `form` schema in a typed, order-preserving shape the form VM and engine consume.

**Contract**:
- `FormField` — `string Type`, `string Label`, `IReadOnlyList<string>? Entries` (dropdown only), `string? Format` (optional .NET format string interpreted **per type** — a date format string for `date`, a numeric format string for `number`; ignored for `text`/`dropdown`). Field `Type` is one of `text`/`date`/`number`/`dropdown` (string-compared, case-insensitive).
- `FormDefinition` — an **ordered** collection of `(string Name, FormField Field)` preserving template document order, plus a convenience set of declared field names. Mirror the closed-record style used by `NoteMetadata`/`NoteTreeNode`.

#### 2. Template schema parser

**File**: `Notes/Services/ITemplateParser.cs`, `Notes/Services/TemplateParser.cs`

**Intent**: Extract and deserialize the `form:` map from a template's frontmatter into a `FormDefinition`, reusing the existing Markdig + YamlDotNet pattern and the deliberate broad-catch convention.

**Contract**: `FormDefinition Parse(string? templateText)`. Reuse a static Markdig `UseYamlFrontMatter()` pipeline + YamlDotNet `IDeserializer` (as `NoteMetadataParser.cs:15-22`). Deserialize the `form` key directly into an order-preserving map of field-name → `{type, label, entries?, format?}`. No `form` block, no frontmatter, or malformed YAML → an **empty** `FormDefinition` (broad `catch (Exception)`, per `lessons.md`). Field order must follow template document order.

#### 3. Template render engine

**File**: `Notes/Services/ITemplateRenderer.cs`, `Notes/Services/TemplateRenderer.cs`

**Intent**: Produce the generated note text from a template plus collected values — the pure heart of the feature.

**Contract**: `string Render(string templateText, FormDefinition definition, IReadOnlyDictionary<string, string> values)`. Steps: (a) split frontmatter region from body; (b) textually strip the `form:` block from the frontmatter, dropping the whole fence if no keys remain; (c) in the **body only**, replace each `{{name}}` where `name` is a declared field with `values[name]` (empty string if absent/blank), leaving undeclared tokens verbatim; (d) recombine stripped-frontmatter + substituted-body.

The `form`-strip is non-obvious; the intended shape:

```
// Within the frontmatter lines only:
// drop the line matching ^form:\s*$ (or 'form:' with inline value)
// then drop every following line that is indented (starts with space/tab)
// stop at the next line that starts at column 0 (next top-level key) or fence end.
// If zero top-level keys remain after removal, emit body with NO frontmatter fence.
```

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- Unit tests pass: `dotnet test`
- Parser: well-formed `form` → ordered `FormDefinition`; missing/malformed/absent-frontmatter → empty definition; field order matches document order; dropdown `entries` and date `format` captured.
- Renderer: declared `{{field}}` substituted in body; undeclared token left verbatim; blank value → empty string; `form` block removed from frontmatter; other frontmatter keys preserved verbatim; `form`-only frontmatter → output has no `---` fence; tokens in frontmatter never substituted.

#### Manual Verification:

- (none — pure logic, covered by automated tests)

**Implementation Note**: Phase blocks use plain bullets; the `## Progress` section owns the checkboxes. After automated verification passes, proceed to Phase 2 (no manual step here).

---

## Phase 2: Dynamic Typed Form Dialog

### Overview

The one net-new technical risk. Per-field-type VMs that own typed→string value formatting, composed into an `ItemsControl`-driven dialog that returns the collected `name→formatted-string` map (or null on cancel).

### Changes Required:

#### 1. Per-field-type field ViewModels

**File**: `Notes/ViewModels/Fields/TextFieldVm.cs`, `DateFieldVm.cs`, `NumberFieldVm.cs`, `SelectFieldVm.cs` (+ a shared `FieldVm` base or interface)

**Intent**: One VM per field type, each exposing `Name`, `Label`, a two-way-bound input value, and a `RenderValue()` returning the formatted string the engine consumes. Each starts empty (no defaults).

**Contract**:
- Common surface: `string Name`, `string Label`, `string RenderValue()`. Use `ObservableObject` + `[ObservableProperty]` per CLAUDE.md.
- `TextFieldVm` / `SelectFieldVm` → `RenderValue()` returns the entered/selected string verbatim (`SelectFieldVm` exposes `Entries`; unselected → empty).
- `NumberFieldVm` → nullable `decimal?` input starting empty; `RenderValue()` renders with **invariant culture**, applying the field's `Format` (e.g. `"F2"`, `"0.##"`) when present, else a plain invariant round-trip; null/blank → empty.
- `DateFieldVm` → nullable date input starting empty; `RenderValue()` renders ISO `yyyy-MM-dd` by default, or the field's `Format` when present; null → empty.

#### 2. Form ViewModel

**File**: `Notes/ViewModels/TemplateFormViewModel.cs`

**Intent**: Build the ordered field-VM collection from a `FormDefinition` and expose the collected value map on submit.

**Contract**: Constructed per-invocation (`AddTransient` or built by the dialog factory) from a `FormDefinition`. Exposes an ordered `ObservableCollection<FieldVm>` (one per declared field, in document order) and a method/property yielding `IReadOnlyDictionary<string, string>` = each field's `Name → RenderValue()`.

#### 3. Form dialog + service

**File**: `Notes/Views/TemplateFormDialog.axaml(.cs)`, `Notes/Services/ITemplateFormDialogService.cs`, `Notes/Services/TemplateFormDialogService.cs`

**Intent**: Render the dynamic form and return collected values, following the established dialog pattern with its own focused service (per S-02's "template dialogs get dedicated services" mandate).

**Contract**:
- `TemplateFormDialog` — `Window` with an `ItemsControl` bound to the form VM's field collection; `ItemsControl.DataTemplates` holds one implicit `DataTemplate DataType="vm:TextFieldVm"` (etc.) per field type, each with compiled bindings against its own `x:DataType`. `static Task<IReadOnlyDictionary<string,string>?> Show(owner, FormDefinition)` — returns the collected map on confirm, `null` on cancel.
- `ITemplateFormDialogService.CollectValues(FormDefinition)` → `Task<IReadOnlyDictionary<string,string>?>`, resolving the owner via `(Application.Current as App)?.MainWindow` like `NewNoteDialogService`.
- Control mapping inside templates: `text`→`TextBox`, `number`→`NumericUpDown`, `date`→`DatePicker`, `dropdown`→`ComboBox` bound to `Entries`. Avalonia 12 has a **single** `NumericUpDown` (value `decimal?`) — no separate integer control; configure it from the field's `Format`: set `FormatString` to the format, and `ParsingNumberStyle="Integer"` (with `Increment="1"`) when the format has no decimal places (e.g. `"0"`/`"F0"`) so whole-number-only fields reject decimal entry.

#### 4. DI registration

**File**: `Notes/Program.cs`

**Intent**: Register the form dialog service (and form VM if not factory-built).

**Contract**: `AddSingleton<ITemplateFormDialogService, TemplateFormDialogService>()` alongside the existing dialog services (`:51`). Per-invocation form VM is constructed by the dialog factory from the passed `FormDefinition` (not a DI singleton).

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- Unit tests pass: `dotnet test`
- Field VMs: `DateFieldVm.RenderValue()` → ISO by default and the field `Format` when set; null date → empty; `NumberFieldVm` → invariant-culture, applying `Format` when set, blank → empty; `SelectFieldVm` unselected → empty; `TextFieldVm` verbatim.
- `TemplateFormViewModel` builds one field VM per declared field in document order and yields the correct `Name → RenderValue()` map.

#### Manual Verification:

- Form renders the four control types correctly for a mixed-field template, in template order.
- Dropdown lists `entries`; date picker and dropdown start empty; submitting an untouched field yields an empty token in the result.
- Cancel returns no values (no note created downstream).

**Implementation Note**: After automated verification passes, pause for manual confirmation that the form renders and collects correctly before proceeding to Phase 3.

---

## Phase 3: Template Catalog, Picker, Entry Point & Orchestration

### Overview

List templates, drive command enablement, wire the menu/keybinding/message, and orchestrate the full flow reusing the New Note save tail.

### Changes Required:

#### 1. Template catalog service

**File**: `Notes/Services/ITemplateCatalog.cs`, `Notes/Services/TemplateCatalog.cs`

**Intent**: List the templates available in `.templates/` (flat, top level) for both the picker and the `HasTemplates` enablement check.

**Contract**: `IReadOnlyList<TemplateInfo> List(string workspacePath)` where `TemplateInfo` carries the relative path + display name (filename). Derive from `IWorkspaceScanner.ScanMarkdownFiles` output filtered to entries whose relative path has the single `.templates/` prefix (no deeper nesting). `bool HasAny(string workspacePath)` convenience for `CanExecute`.

#### 2. Template picker dialog + service

**File**: `Notes/Views/TemplatePickerDialog.axaml(.cs)`, `Notes/Services/ITemplatePickerDialogService.cs`, `Notes/Services/TemplatePickerDialogService.cs`

**Intent**: Let the user choose one template; follow the dialog pattern with its own focused service.

**Contract**: `static Task<TemplateInfo?> Show(owner, IReadOnlyList<TemplateInfo>)` (a `ListBox` of template display names, OK/Cancel); service `ITemplatePickerDialogService.PickTemplate(IReadOnlyList<TemplateInfo>) → Task<TemplateInfo?>`. Returns `null` on cancel.

#### 3. Request message

**File**: `Notes/Messaging/Messages.cs`

**Intent**: New request message mirroring `NewNoteRequestedMessage`.

**Contract**: `public sealed record NewFromTemplateRequestedMessage;`

#### 4. Menu command + enablement (MainWindowViewModel)

**File**: `Notes/ViewModels/MainWindowViewModel.cs`

**Intent**: Add the `NewFromTemplate` command (sends the request message) gated by `CanExecute = HasTemplates`, and keep `HasTemplates` fresh as the workspace and template set change.

**Contract**: `[RelayCommand(CanExecute = nameof(HasTemplates))] private void NewFromTemplate()` sending `NewFromTemplateRequestedMessage`. Inject `ITemplateCatalog`; track the workspace path (already set in `InitializeAsync`/`ChangeWorkspace`). Make `MainWindowViewModel` an `IRecipient` of `WorkspaceChangedMessage`, `NoteSavedMessage`, and `NoteDeletedMessage` (or recompute on workspace change + after saves/deletes), recomputing `HasTemplates` and calling `NewFromTemplateCommand.NotifyCanExecuteChanged()`. A disabled command makes both the menu item and the Ctrl+T keybinding no-op.

#### 5. Menu item + keybinding

**File**: `Notes/MainWindow.axaml`

**Intent**: Expose the command in the File menu and on Ctrl+T, mirroring New Note/New Folder.

**Contract**: A `<MenuItem Header="New from _Template…" Command="{Binding NewFromTemplateCommand}" InputGesture="Ctrl+T" />` in the File menu (`:21-24`) and a `<KeyBinding Gesture="Ctrl+T" Command="{Binding NewFromTemplateCommand}" />` in `Window.KeyBindings` (`:12-17`).

#### 6. Orchestration (NoteTreeViewModel)

**File**: `Notes/ViewModels/NoteTreeViewModel.cs`

**Intent**: Handle the request end-to-end, reusing the existing name/validate/save/select tail with rendered text instead of `string.Empty`.

**Contract**: Implement `IRecipient<NewFromTemplateRequestedMessage>` (`async void Receive` with the same try/catch guard as `:73-83`). Flow: read selected/available templates via `ITemplateCatalog`; `ITemplatePickerDialogService.PickTemplate(...)` (null → abort); read the template file via `INoteFileService.Read`; `ITemplateParser.Parse(text)`; if the definition has fields, `ITemplateFormDialogService.CollectValues(definition)` (null → abort), else use an empty value map; `ITemplateRenderer.Render(text, definition, values)`; then reuse the `HandleNewNote` tail — name prompt/validate via `INameValidator.ValidateNoteName` against the selected folder, `_fileService.Save(success.AbsolutePath, renderedText)`, `NoteSavedMessage`, `LoadTree`, select. Inject the new services via the constructor (`:37-59`).

#### 7. DI registration

**File**: `Notes/Program.cs`

**Intent**: Register catalog + picker services.

**Contract**: `AddSingleton<ITemplateCatalog, TemplateCatalog>()`, `AddSingleton<ITemplatePickerDialogService, TemplatePickerDialogService>()`, plus the Phase 1 services (`ITemplateParser`, `ITemplateRenderer`) alongside the existing block (`:37-51`).

### Success Criteria:

#### Automated Verification:

- Build passes: `dotnet build`
- Unit tests pass: `dotnet test`
- `TemplateCatalog.List` returns only top-level `.templates/*.md` entries; `HasAny` false when none (tests use `MockFileSystem` / substituted `IWorkspaceScanner`).
- `MainWindowViewModel` exposes `NewFromTemplateCommand` with `CanExecute` reflecting `HasTemplates`, recomputed on workspace/save/delete.

#### Manual Verification:

- With templates present: File → New from Template… and Ctrl+T both open the picker; with none, the menu item is disabled and Ctrl+T does nothing.
- End-to-end: pick template → fill form → name prompt → new note appears in the selected folder, opens in the editor, `form` block gone, body substituted, undeclared tokens intact, blank fields empty.
- Generating from a template whose only frontmatter key is `form` yields a note with no frontmatter fence.
- Cancelling at the picker, the form, or the name prompt creates no note.

**Implementation Note**: After automated verification passes, pause for manual confirmation of the end-to-end flow before closing the change.

---

## Testing Strategy

### Unit Tests:

- **Engine (Phase 1)** — pure, no IO: parser (ordering, dropdown entries, date format, malformed/absent → empty) and renderer (body-only substitution, declared-vs-undeclared tokens, blank → empty, `form`-block strip incl. frontmatter-drop, verbatim passthrough of other keys).
- **Field VMs (Phase 2)** — `RenderValue()` formatting per type (ISO/format-override date, invariant/format-override number, empty-when-untouched); form VM builds ordered field set + correct value map.
- **Catalog (Phase 3)** — `.templates/`-prefix filtering and `HasAny`, using `MockFileSystem` (per project memory) or a substituted `IWorkspaceScanner` (NSubstitute).

Test naming: `Method_WhenScenario_ExpectedBehaviour` (e.g. `Render_WhenFieldBlank_SubstitutesEmptyString`, `Parse_WhenFormBlockMalformed_ReturnsEmptyDefinition`).

### Integration / End-to-End (manual):

1. Author `.templates/meeting.md` with text/date/number/dropdown fields + body placeholders and a non-`form` frontmatter key.
2. New from Template → confirm form order, control types, empty starts.
3. Submit → verify generated note location, name prompt, frontmatter strip, body substitution, undeclared-token survival.
4. Repeat with a `form`-only template (no fence in output) and a no-`form` template (static copy).
5. Empty-state: remove all templates → menu item disabled, Ctrl+T no-op.

### Manual Testing Steps:

1. Create a template via New Folder (`.templates`) + New Note inside it.
2. Run each of the four control types through the form.
3. Cancel at picker / form / name prompt — confirm no file written.

## Performance Considerations

Negligible. Template listing reuses the already-computed scanner output; parsing/rendering operate on single small files. No new IO hotspots; no `FileSystemWatcher`.

## Migration Notes

None — no schema/data migration. Existing notes and `.templates/` content are unaffected; the feature is purely additive.

## References

- Internal research: `context/changes/templates/research.md`
- Change identity & locked schema: `context/changes/templates/change.md`
- Prerequisite (landed): `context/changes/note-tree-folder-management/`
- Save seam: `Notes/ViewModels/NoteTreeViewModel.cs:131-132`
- Parse pattern to mirror: `Notes/Services/NoteMetadataParser.cs:15-50`
- Dialog precedent: `Notes/Views/NewNoteDialog.axaml.cs`, `Notes/Services/NewNoteDialogService.cs`
- Broad-catch convention: `context/foundation/lessons.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Template Schema + Render Engine

#### Automated

- [x] 1.1 Build passes: `dotnet build`
- [x] 1.2 Unit tests pass: `dotnet test`
- [x] 1.3 Parser: ordered definition; missing/malformed/absent → empty; field order = document order; entries + format captured
- [x] 1.4 Renderer: body substitution; undeclared token verbatim; blank → empty; `form` removed; other keys verbatim; `form`-only → no fence; frontmatter tokens untouched

### Phase 2: Dynamic Typed Form Dialog

#### Automated

- [ ] 2.1 Build passes: `dotnet build`
- [ ] 2.2 Unit tests pass: `dotnet test`
- [ ] 2.3 Field VMs: date ISO/format-override + null→empty; number locale + blank→empty; select unselected→empty; text verbatim
- [ ] 2.4 `TemplateFormViewModel` builds one field VM per field in document order; yields correct value map

#### Manual

- [ ] 2.5 Form renders all four control types in template order
- [ ] 2.6 Dropdown lists entries; date/dropdown start empty; untouched field → empty token
- [ ] 2.7 Cancel returns no values

### Phase 3: Template Catalog, Picker, Entry Point & Orchestration

#### Automated

- [ ] 3.1 Build passes: `dotnet build`
- [ ] 3.2 Unit tests pass: `dotnet test`
- [ ] 3.3 `TemplateCatalog.List` returns only top-level `.templates/*.md`; `HasAny` false when none
- [ ] 3.4 `NewFromTemplateCommand` `CanExecute` reflects `HasTemplates`, recomputed on workspace/save/delete

#### Manual

- [ ] 3.5 Menu item + Ctrl+T open picker when templates exist; disabled / no-op when none
- [ ] 3.6 End-to-end: pick → form → name → note in selected folder, opened, `form` stripped, body substituted, undeclared intact, blank empty
- [ ] 3.7 `form`-only template → output has no frontmatter fence
- [ ] 3.8 Cancel at picker / form / name prompt creates no note
