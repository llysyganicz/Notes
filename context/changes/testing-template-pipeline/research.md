---
date: 2026-06-06T15:19:38+0200
researcher: lysy
git_commit: 59055a5f4b602477a5a89752c393feb1821c3b8e
branch: HEAD (detached)
repository: Notes
topic: "Template pipeline correctness — grounding test-plan risks #1, #2, #6 in code"
tags: [research, codebase, templates, template-parser, template-renderer, note-file-service, test-plan, phase-1]
status: complete
last_updated: 2026-06-06
last_updated_by: lysy
---

# Research: Template pipeline correctness (test-plan rollout Phase 1)

**Date**: 2026-06-06T15:19:38+0200
**Researcher**: lysy
**Git Commit**: 59055a5f4b602477a5a89752c393feb1821c3b8e
**Branch**: HEAD (detached)
**Repository**: Notes

## Research Question

Ground rollout Phase 1 of `context/foundation/test-plan.md` ("Template pipeline
correctness") in current code, so a test author can write the oracle independently
of the implementation. Scope is the three risks the change brief
(`context/changes/testing-template-pipeline/change.md`) assigns to this phase:

- **Risk #1** — Create-from-template produces a wrong/corrupt note: leftover
  `{{placeholder}}` syntax, wrong-slot substitution, or dropped fields.
- **Risk #2** — Malformed/edge-case template frontmatter (missing type, unknown
  field type, empty value) silently fails or renders wrong.
- **Risk #6** — A saved note is no longer valid/portable `.md` (frontmatter
  mangled, encoding or line-ending corruption).

Per test-plan §1 principle #3, this document is the ground truth on *where the
failure lives*; the test-plan stays a risk spec.

## Summary

The template pipeline is **`TemplateParser.Parse` → `FormDefinition` →
`TemplateFormViewModel` (FieldVm per type) → `TemplateRenderer.Render` →
`NoteFileService.Save`**. The dominant cross-cutting fact for all three risks is
that **every failure mode in this pipeline is silent by design** — malformed YAML,
missing/unknown field types, empty values, and undeclared placeholders all collapse
to a fallback (empty form, text field, empty string, or a verbatim literal) and
never throw, log, or warn. This is a *deliberate, locked* contract
(`context/foundation/lessons.md`), not a bug — so the test oracle must assert on
the *resulting shape*, never on an exception or a surfaced warning.

Key per-risk landing:

- **Risk #1 (render):** Substitution is regex `{{(.*?)}}` over the **body only**,
  exact-ordinal key match. Undeclared / mis-cased / frontmatter-placed tokens
  survive **verbatim** to disk (no leftover-detection pass). Declared-but-empty
  values render as empty string (no required-field guard). No wrong-slot bleed is
  possible (full-key dictionary lookup, ordinal). The renderer output is written
  **byte-for-byte** by `Save`, so the oracle = `(template + FormDefinition + values)`
  → expected string → assert equality against file content.
- **Risk #2 (parse):** A broad `catch (Exception) → FormDefinition.Empty` swallows
  every parse failure. The "supported field-type set" is **not** validated at parse
  time — it exists only as string-literal `case` labels in the form-builder switch,
  where the dropdown keyword is `"dropdown"` (not `"select"`) and any
  unknown/missing type silently becomes a **text field** (field kept, not dropped).
- **Risk #6 (round-trip):** **There is no YAML serializer on the save path.** A note
  is persisted as the raw editor string via `WriteAllText(..., UTF8 no-BOM)`. This
  makes round-trip *safer than the risk assumes*: nothing is reordered/re-quoted,
  unknown frontmatter keys and line endings survive verbatim. The two real exposure
  points are (a) an **encoding asymmetry** — sync `Read` omits the explicit encoding
  that write and `ReadAsync` specify — and (b) line-ending/BOM fidelity is only
  proven at the *renderer* layer, never at the *file-service* layer.

The existing suite (198 methods) already covers the happy paths and several
hard-won regressions; this phase should target the **named gaps in §"Open
Questions / Coverage Gaps"** and respect the locked decisions in §"Historical
Context".

## Detailed Findings

### Risk #2 — Template parse → form pipeline

**Entry point.** `Notes/Services/TemplateParser.cs:33` —
`public FormDefinition Parse(string? templateText)` (interface
`Notes/Services/ITemplateParser.cs:12`). Never returns null; on any problem returns
the singleton `FormDefinition.Empty`. Mechanism: Markdig `UseYamlFrontMatter()`
extracts the YAML block (`TemplateParser.cs:24-26,40-46`), then a YamlDotNet
`IDeserializer` with `LowerCaseNamingConvention` + `IgnoreUnmatchedProperties()`
(`:28-31`) deserializes into `FrontmatterShape { Dictionary<string,FieldShape?> Form }`
(`:74-85`). Dictionary insertion order preserves template field order.

`TemplateCatalog` (`Notes/Services/TemplateCatalog.cs`) does **not** parse content —
it only discovers `.templates/*.md` files (`Load`/`List`/`HasAny`). A template with
broken frontmatter still appears in the picker; failure only manifests later in
`Parse`.

**Supported field-type set is unvalidated and lives only in the form builder.**
`Notes/ViewModels/TemplateFormViewModel.cs:61-67`:
- `"date"` → `DateFieldVm`
- `"number"` → `NumberFieldVm`
- `"dropdown"` → `SelectFieldVm` — **the YAML keyword is `dropdown`, not `select`**
- `_` (default — includes `text`, empty, and any unknown) → `TextFieldVm`

Matching is case-insensitive: `(field.Type ?? string.Empty).ToLowerInvariant()`
(`:61`). The parser copies the raw `type:` string verbatim into `FormField.Type`
(`TemplateParser.cs:60`) with no validation. The decision point is `CreateField`
(`TemplateFormViewModel.cs:58-68`), called by `Load(FormDefinition)` (`:34-42`).

**Failure mode on bad YAML — SILENT.** `TemplateParser.cs:48-71`:
```csharp
var shape = YamlDeserializer.Deserialize<FrontmatterShape?>(yamlBlock.Lines.ToString()); // :50
...
catch (Exception) { return FormDefinition.Empty; }   // :68-71
```
Any `YamlException` (or anything else) → empty form. No rethrow, no log. This breadth
is intentional and documented at `TemplateParser.cs:18-20` and
`context/foundation/lessons.md:15` (a prior narrowing to `catch (YamlException)` was
rolled back in tags-and-search phase F2 — **do not narrow, do not flag**). Two
non-exception early-outs also return `FormDefinition.Empty`: null/empty input
(`:35-38`) and no YAML frontmatter present (`:43-46`).

**Unknown field type — SILENT default to text, field KEPT (not dropped).** Parser
preserves the unknown `type:` verbatim (`TemplateParser.cs:60`); the form builder's
`_ =>` arm maps it to `TextFieldVm` (`TemplateFormViewModel.cs:66`). So `type: colorpicker`
or a typo renders as a plain text input. **Trap:** `type: select` also lands here
(keyword is `dropdown`), silently degrading to free text and losing its `entries`.

**Missing `type:` / empty value — SILENT.** Missing `type:` coalesces to empty string
(`TemplateParser.cs:60`) → `TextFieldVm`. Null field value: null-conditionals
(`:60-63`) default Type/Label to empty, Entries/Format to null. Empty/absent `form:`
map → `FormDefinition.Empty` (`:51`). Dropdown with no `entries:` → coalesced to
`Array.Empty<string>()` (`TemplateFormViewModel.cs:65`), i.e. a zero-choice dropdown.
Empty field value at fill time → every `FieldVm.RenderValue()` returns `string.Empty`
(`TextFieldVm.cs:16`, `DateFieldVm.cs:31-32`, `NumberFieldVm.cs:43-44`,
`SelectFieldVm.cs:23`).

**Models.** `FormField(Type, Label, Entries?, Format?)` `Notes/Models/FormField.cs:11-15`;
`FormFieldEntry(Name, Field)`, `FormDefinition(Fields)` with `.Empty` and an
ordinal `Names` `HashSet` at `Notes/Models/FormDefinition.cs:8,15-21`; `TemplateInfo`
`Notes/Models/TemplateInfo.cs:8`.

### Risk #1 — Render → note creation pipeline

**Placeholder grammar.** `Notes/Services/TemplateRenderer.cs:19`:
```csharp
private static readonly Regex PlaceholderRegex = new(@"\{\{(.*?)\}\}", RegexOptions.Compiled);
```
`{{` … `}}`, **non-greedy** inner capture, inner text `.Trim()`-ed before lookup
(`:159`) — so `{{name}}`, `{{ name }}`, `{{  name  }}` all key to `name`. `.` does not
match newline, so an unterminated `{{` on a line is left untouched. No dotted-subfield
grammar — `{{field.sub}}` is the literal key `field.sub`. Nested/adjacent braces
(`{{ {{a}} }}`, `{{{x}}}`) can capture unexpected spans — worth a test.

**Entry point.** `TemplateRenderer.cs:24` (interface `ITemplateRenderer.cs:13`):
`public string Render(string templateText, FormDefinition definition, IReadOnlyDictionary<string,string> values)`.
Splits frontmatter/body (`:28-53`), **substitutes body only** (`:53`), strips the
`form:` block textually (`StripFormBlock`, `:52`,`:108-134`); if `form` was the only
frontmatter key the whole `---` fence is dropped (`:55-59`). Other frontmatter keys
pass through verbatim — **no YAML reserialize**. Per-line terminators preserved
(`SplitLines`, `:76-102`).

**Slot substitution — no wrong-slot bleed possible.** `TemplateRenderer.cs:156-169`:
```csharp
var name = match.Groups[1].Value.Trim();
if (!definition.Names.Contains(name)) { return match.Value; }   // undeclared → verbatim
return values.TryGetValue(name, out var value) && value is not null ? value : string.Empty;
```
Exact, **case-sensitive ordinal** match (`FormDefinition.cs:19-20` and the values dict
built ordinal at `TemplateFormViewModel.cs:47`). `{{Title}}` does not match field
`title`. No substring/partial match → a value cannot bleed into another slot.
Duplicate `{{name}}` occurrences all get the same value (no per-occurrence drift).

**Missing value — empty string, no error.** A *declared* placeholder with a missing
or null value renders as `string.Empty` (`:166-168`). Empty user input does the same
(every `FieldVm.RenderValue()` → empty). **No required-field concept** — a blank field
silently produces an empty slot.

**Leftover placeholders — survive verbatim, no post-pass.** `TemplateRenderer.cs:160-164`
returns `match.Value` for any **undeclared** token. There is no pass that detects or
strips leftover `{{…}}`. So a body token whose key isn't in the schema (typo, case
mismatch, or any frontmatter-placed placeholder per the entry-point note) lands in the
finished note as literal `{{…}}`, saved with no error. **This is the canonical Risk #1
outcome.**

**Create-from-template flow (form confirm → file on disk):**
1. `Notes/ViewModels/MainWindowViewModel.cs:105-107` — `NewFromTemplate()` only sends
   `NewFromTemplateRequestedMessage` (`Notes/Messaging/Messages.cs:15`); does no work.
2. `Notes/ViewModels/NoteTreeViewModel.cs:114-124` — `Receive(...)` (`async void`,
   bare `catch {}`) → `HandleNewFromTemplate()`.
3. `NoteTreeViewModel.cs:132-177` — list templates (`_templateCatalog.List()`, `:139`),
   pick via dialog (`:145`), build path (`:151-153`), read (`_fileService.Read`, `:154`),
   parse (`_templateParser.Parse`, `:156`), collect values via dialog only if
   `definition.Fields.Count > 0` (`:159-168`), render (`_templateRenderer.Render`, `:174`),
   `PromptNameAndSave(rendered)` (`:176`).
4. `TemplateFormViewModel.Submit` builds the values dict
   `Fields.ToDictionary(f => f.Name, f => f.RenderValue(), StringComparer.Ordinal)`
   (`TemplateFormViewModel.cs:47`).
5. `NoteTreeViewModel.cs:179-219` — `PromptNameAndSave(content)`: prompt name (`:190`),
   validate (`_nameValidator.ValidateNoteName`, `:200`), compute path (`:205-207`),
   write `_fileService.Save(success.AbsolutePath, content)` (`:209`), send
   `NoteSavedMessage` (`:210`), reload tree (`:212`).
6. `Notes/Services/NoteFileService.cs:39-42` — `Save` → `WriteAllText(path, text, Utf8NoBom)`.

**Filename is from the user prompt, not a template field** — validated by
`Notes/Services/NameValidator.cs:18-37`: rejects empty / `/` / `\` / every
`Path.GetInvalidFileNameChars()` (`:56-78`) → path-escape blocked; returns failure if
`File.Exists` (`:31-34`) → **collision blocked, no silent overwrite** (this pre-empts
Risk #3, owned by Phase 2). `.md` appended if absent (`:26-28`).

**Oracle implication:** the renderer output is written byte-for-byte, so the Risk #1
oracle is purely `(template text + FormDefinition + values)` → expected rendered string,
asserted equal to file content.

### Risk #6 — Save / reload round-trip + serialization

**Architecture finding that frames everything:** there is **no YAML serializer on the
save path**. A note is persisted as the raw editor string, byte-for-byte. Frontmatter
is only *parsed* (read side) and only *constructed at creation time* by the renderer.
This is good for portability — a round-trip cannot reorder/re-quote/drop keys because it
never re-serializes.

**Save path.** `Notes/Services/NoteFileService.cs:39-42`:
`_fileSystem.File.WriteAllText(absolutePath, text, Utf8NoBom)`, where `Utf8NoBom =
new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` (`:10`). Writes the whole
note (frontmatter + body) as one opaque string. Callers: `NoteEditorViewModel.cs:164`
(autosave of raw buffer) and `NoteTreeViewModel.cs:209` (new-note creation).

**Load / parse path.** Raw read: `NoteFileService.cs:19-27` (sync `Read`) and `:29-37`
(`ReadAsync`) — return raw text, do not parse frontmatter. Frontmatter parse:
`Notes/Services/NoteMetadataParser.cs:26-50` — Markdig `UseYamlFrontMatter()` extracts
the `---` block (`:33-35`), YamlDotNet deserializes into `FrontmatterShape` with
`LowerCaseNamingConvention` + `IgnoreUnmatchedProperties` (`:19-22`); failure/no block →
`NoteMetadata.Empty` (`:30,:38,:48`). Read-only projection — never feeds the save path.

**Encoding — the one real finding.** Write is explicit UTF-8 no-BOM (`:41`). `ReadAsync`
is explicit `Utf8NoBom` (`:36`). **But sync `Read` (`:26`) uses
`_fileSystem.File.ReadAllText(absolutePath)` with no encoding argument** — it auto-detects
BOM and otherwise defaults to UTF-8. Not a corruption bug for files this app wrote, but
**asymmetric** with write and `ReadAsync`. Worth a round-trip test with BOM-prefixed /
multi-byte external content through the sync path.

**Line endings — preserved, not normalized.** `Save` does no normalization;
`Environment.NewLine` appears nowhere in `Notes/`. Template creation explicitly
preserves per-line terminators: `TemplateRenderer.cs:76-102` captures each line's `\n`
or `\r\n` (`:90-95`) and re-emits it (`:153-154`). Risk is not the app *changing*
endings but *preserving mixed* endings the editor control may introduce — pin observable
bytes in a test.

**Frontmatter fidelity — unknown keys SAFE on disk, lossy only in the read model.**
`Notes/Models/NoteMetadata.cs:6` models only `Tags`; `FrontmatterShape`
(`NoteMetadataParser.cs:62-65`) declares only `Tags` and `IgnoreUnmatchedProperties`
(`:21`) discards the rest **in memory only**. Because `Save` writes raw text, unknown
keys (`author:`, `date:`, custom) are preserved on disk. Tags are normalized
(lowercase, `[a-z0-9-]`, deduped) at `NoteMetadataParser.cs:52-60` — affects
search/index, not the file.

**IFileSystem — fully MockFileSystem-testable.** `NoteFileService` takes `IFileSystem`
(`:12-17`); every op goes through `_fileSystem.File.*` (`:21,:26,:31,:36,:41`); no direct
`System.IO.File`. `MockFileSystem` honors the encoding argument, so the encoding
round-trip *is* testable in-memory.

## Code References

- `Notes/Services/TemplateParser.cs:33` — `Parse` entry; `:48-71` broad-catch → `Empty`; `:60` raw type passthrough; `:18-20` documented intent
- `Notes/Services/ITemplateParser.cs:12` — parse interface
- `Notes/Services/TemplateCatalog.cs` — `.templates/*.md` discovery only (no content parse)
- `Notes/ViewModels/TemplateFormViewModel.cs:58-68` — field-type → FieldVm switch (the de-facto supported set); `:47` ordinal values dict
- `Notes/ViewModels/Fields/{TextFieldVm.cs:16, DateFieldVm.cs:29-38, NumberFieldVm.cs:41-51, SelectFieldVm.cs:23}` — `RenderValue()` per type; empty → `string.Empty`
- `Notes/Services/TemplateRenderer.cs:19` placeholder regex; `:24` `Render`; `:52,:108-134` form-strip; `:156-169` substitution; `:160-164` undeclared verbatim; `:166-168` missing→empty; `:76-102,:153-154` line-ending preservation
- `Notes/ViewModels/NoteTreeViewModel.cs:114-124` `async void` handler (bare catch); `:132-177` create flow; `:179-219` `PromptNameAndSave`; `:209` save
- `Notes/ViewModels/MainWindowViewModel.cs:105-107` — message dispatch
- `Notes/Messaging/Messages.cs:15` — `NewFromTemplateRequestedMessage`
- `Notes/Services/NameValidator.cs:18-37,:31-34,:56-78` — name validation, collision + path-escape guards
- `Notes/Services/NoteFileService.cs:10,:19-27,:29-37,:39-42` — encoding const, sync `Read` (no encoding), `ReadAsync`, `Save`
- `Notes/Services/NoteMetadataParser.cs:19-22,:26-50,:52-60,:62-65` — read-side YAML projection, tag normalization
- `Notes/Models/{FormField.cs:11-15, FormDefinition.cs:8,15-21, NoteMetadata.cs:6, TemplateInfo.cs:8}` — domain records

## Architecture Insights

- **Silence is the contract, not a defect.** Across parse, render, and the create
  handler, the design swallows failures into well-defined fallbacks (`FormDefinition.Empty`,
  `TextFieldVm`, empty string, verbatim literal). Tests must assert *resulting shape*,
  never an exception or warning. The broad `catch` in `TemplateParser` is explicitly
  protected by `lessons.md` — re-flagging it is a known anti-finding.
- **Render is textual, not structural.** Substitution is regex over the body with
  exact-ordinal keys; frontmatter is line-stripped, never deserialized/reserialized.
  This is *why* round-trip portability holds — and also why undeclared/frontmatter
  placeholders leak through verbatim.
- **The save layer is dumb on purpose.** `WriteAllText(rawText, UTF8 no-BOM)` means the
  renderer (or editor buffer) fully determines on-disk bytes. The correctness boundary
  for Risk #1/#6 is the *renderer output* plus the *one encoding asymmetry* in sync `Read`.
- **Keyword/class-name mismatch hazard:** YAML keyword `dropdown` ↔ class `SelectFieldVm`.
  `type: select` silently degrades to text and drops `entries`. High-value test target.
- **Layer for cost × signal:** every risk in this phase is reachable at the unit layer
  (`TemplateParser`, `TemplateRenderer`, `FieldVm`) plus integration via `MockFileSystem`
  for `NoteFileService` and the `NoteTreeViewModel` orchestration. No e2e needed.

## Historical Context (from prior changes)

From `context/archive/2026-06-02-templates/`:

- **Malformed `form:` → silent static copy is a LOCKED decision** (`change.md:21`,
  `plan.md:62`, `reviews/impl-review.md:57-71`, `follow-ups/review-fixes.md:14-20` FU-2).
  Real trigger was tab-indented sequence YAML. A template designer/validator is deferred
  post-MVP (matches user memory "post-mvp-template-authoring-ux"). **Do not test for a
  thrown exception or a surfaced warning on malformed templates.**
- **No form-definition validation** — duplicate names, unknown type, dropdown missing
  `entries` are unvalidated by design (`follow-ups/review-fixes.md:5-11` FU-1). Duplicate
  field names would make `TemplateFormViewModel.Submit`'s `ToDictionary` throw
  `ArgumentException` (`TemplateFormViewModel.cs:47`) — currently unreachable because YAML
  map keys are unique, but a boundary assumption, swallowed by the `async void` catch.
- **Two reliability bugs found + fixed in Phase 1 review** (both regression-tested):
  blank line inside the form block terminated the strip early
  (`reviews/impl-review-phase-1.md:23-31` F1 → `Render_WhenFormBlockContainsBlankLine_StripsEntireBlock`);
  CRLF was normalized to LF (`:33-41` F2 → `Render_WhenTemplateUsesCrlf_PreservesCrlfEndings`).
- **No YAML re-serialization on save** is the deliberate choice that protects round-trip
  fidelity (`change.md:21`, `plan.md:39,58`).

From `context/archive/2026-05-28-note-editor-and-preview/` (`plan.md:120-122`):
`Save` uses UTF-8 no-BOM and **does not create parent directories**; missing file on
read → `string.Empty` (no throw); notes are saved as opaque whole-file text.

### Existing test base (198 methods; `Notes.Tests/`)

Already covered — **do not duplicate**:
- `TemplateParserTests.cs` (~15): null/empty/no-frontmatter/no-`form` → empty; document
  order; type+label; dropdown entries; date & number format; non-dropdown → null entries;
  **malformed YAML → empty** (`:124-132`); `form`-scalar → empty; coexists with other keys;
  `Names`.
- `TemplateRendererTests.cs` (~13): declared substitution; **undeclared verbatim**;
  missing/blank → empty; `form`-only fence dropped; other keys preserved;
  token-in-frontmatter NOT substituted; malformed → static copy; **blank-line-in-form**;
  **CRLF preserved**; repeated tokens.
- `TemplateFormViewModelTests.cs` (~11): VM per field in order; each type → concrete VM;
  case-insensitive matching; **unknown type → `TextFieldVm`**; dropdown entries; submit map;
  empty/untouched → empty.
- `FieldVmTests.cs` (~13): text/select/number/date formatting incl. invariant-culture under
  de-DE; date ISO default + format.
- `NoteFileServiceTests.cs` (~5): missing→empty; UTF-8 incl. non-ASCII; **UTF-8 no-BOM**;
  overwrite; emoji round-trip. (Uses real `FileSystem()` + temp dir — the exception to the
  MockFileSystem rule; new tests should prefer MockFileSystem.)
- `NoteMetadataParserTests.cs` (~15): tag parsing/normalization/dedup/malformed → zero tags.
- `NoteTreeViewModelTests.cs` (~18): incl. `Receive_WhenNewFromTemplateRequested_RendersTemplateAndSavesNote`
  (`:307-334`, the render→save integration glue), picker-cancelled and no-templates paths.

### Conventions a new test must follow

- Naming `Method_WhenScenario_ExpectedBehaviour` (verb-led expectation), e.g.
  `Parse_WhenFrontmatterMalformed_ReturnsEmpty` (`TemplateParserTests.cs:125`).
- **xUnit v3** `3.2.2`; `[Fact]`/`[Theory]`+`[InlineData]`; private enums for readable
  theory labels (`NoteTreeViewModelTests.cs:150-190`).
- **MockFileSystem** (`System.IO.Abstractions.TestingHelpers` `22.1.1`) injected into the
  SUT's `IFileSystem` param (`NoteTreeViewModelTests.cs:22,40-42`).
- **NSubstitute** `5.3.0` for behavior-only doubles (`Substitute.For<T>()`,
  `.Returns(...)`, `Arg.Any<T>()`); hand-rolled `Stub*` only for stateful fakes.
- **Fresh `StrongReferenceMessenger` per test** (not the weak `Default`) —
  `NoteTreeViewModelTests.cs:23`, `NoteEditorViewModelTests.cs:16`.
- Shared `Fakes/InMemoryNoteFileService.cs` (dictionary-backed `INoteFileService`).
- VM tests touching Avalonia primitives use `[AvaloniaFact]` (`Avalonia.Headless.XUnit`
  `12.0.3`) + `TestApp.cs`; the pure parser/renderer/field/form tests use plain `[Fact]`.
- Pure-engine SUTs are `new`-ed directly (`TemplateParser _parser = new()`); no DI in tests.
- `FormDefinition` helper: `new FormDefinition([new FormFieldEntry(name, new FormField(type, label, entries?, format?))])`
  (`TemplateRendererTests.cs:12-21`).

## Related Research

- `context/archive/2026-06-02-templates/research.md` — original template-pipeline exploration
- `context/foundation/test-plan.md` §2 Risk Map + Risk Response Guidance (the spec this grounds)

## Open Questions / Coverage Gaps (input to `/10x-plan`)

Mapped to the risks, these are the **uncovered** scenarios the Phase 1 plan should target:

**Risk #2 — malformed/edge-case frontmatter:**
- `form:` as a YAML **sequence** / **tab-indented** block (the actual FU-2 real-world
  trigger) → assert `FormDefinition.Empty`, no throw.
- Field with **missing `type:`** and field with **missing `label:`** → text field, empty label.
- **`type: select`** (the keyword-mismatch trap) → degrades to `TextFieldVm`, loses entries.
- **Unknown `type:`** asserted at the parse→form boundary → `TextFieldVm`, field kept.
- **Dropdown missing `entries:`** → zero-choice dropdown (renders empty).
- Unknown frontmatter key in `NoteMetadataParser` → ignored, no error (currently unasserted).

**Risk #1 — render correctness:**
- Whole-note **leftover-placeholder** assertion: zero `{{…}}` tokens remain for any key
  that *is* a declared ordinal name; literal `{{…}}` remains for undeclared/mis-cased
  (`{{Title}}` vs `title`) and frontmatter-placed tokens.
- Odd/whitespace bracing: `{{ name }}`, `{{{x}}}`, unterminated `{{x`, adjacent
  `{{a}}{{b}}` — confirm grammar boundaries.
- End-to-end orchestration of the **malformed-template** path (static copy reaches disk)
  and the **blank/cancelled form** path (note saved with empty substitutions).

**Risk #6 — save round-trip portability (file-service layer):**
- `NoteFileService` **line-ending fidelity** — write LF and CRLF content, assert bytes
  survive `Save`→`Read` unchanged (renderer-layer CRLF test exists; file-service layer
  has none).
- **BOM-prefixed / multi-byte external file** read through the **sync `Read`** path (the
  encoding asymmetry at `NoteFileService.cs:26`).
- `Save` **does not create parent directories** (documented behavior, untested at this layer).

**Decisions to respect (NOT to test against):** malformed `form` → silent static copy
(no exception/warning expected); the broad `catch` in `TemplateParser`; duplicate field
names throwing in `Submit` is a deferred boundary assumption, not a guaranteed behavior.
