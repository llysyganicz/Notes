# Mutation Baseline — Notes.Core (Phase 2 first run)

> Run record for Phase 2 of `plan.md`. Raw baseline before the three §D gaps are
> closed (Phase 3) and before §F line-range exclusions / `break` threshold
> (Phase 4).

## Run metadata

| Field | Value |
| --- | --- |
| Date | 2026-06-11 |
| Stryker | dotnet-stryker 4.14.2 (global tool) |
| **Run directory** | `Notes.Core.Tests/` |
| **Command** | `dotnet stryker -f ../stryker-config.json` |
| Runner | **MTP** (native, `test-runner: mtp`) — no VSTest fallback needed |
| Project mutated | `Notes.Core` (single-project mode via cwd auto-discovery) |
| Output | `Notes.Core.Tests/StrykerOutput/2026-06-11.17-45-06/` |
| Elapsed | ~30 s |

### Single-project mode confirmed (2.5)

The mutated-file list contains **only `Notes.Core` files** — nothing from the
Avalonia `Notes` project. Running from `Notes.Core.Tests/` (whose only
`ProjectReference` is `Notes.Core`) gave unambiguous single-project mode; the
`.slnx` solution-mode trap was not triggered. The blocker is not reintroduced.

### Mutate scope resolved correctly (2.4)

Mutated (in-scope) files exactly match the allow-list: `NameValidator`,
`NoteDeleter`, `NoteFileService`, `NoteFolderService`, `OrphanedTempCleaner`,
`PathGuard`, `TemplateCatalog`, `TemplateParser`, `TemplateRenderer`,
`ViewModels/Fields/*` (5), `TemplateFormViewModel`. All other moved
`Notes.Core` files (`NoteSearchIndex`, `SettingsService`, `WorkspaceScanner`,
`NoteMetadataParser`, `NoteTreeBuilder`, every `I*.cs`, `PathContainmentException`)
report **Excluded**. No `.axaml.cs`, no `Notes`-project file mutated.

## VM source-generator gate — PASSED (2.6)

The open risk from the plan was whether the CommunityToolkit
`[ObservableProperty]`/`[RelayCommand]` source generators survive Stryker's
in-memory mutated recompile (the same *class* of failure as the Avalonia
`InitializeComponent` blocker). **They do.** The in-scope VMs produced and
recompiled real mutants:

- VM mutant outcomes: **35 Killed**, 8 CompileError, 7 Ignored, 2 NoCoverage, 1 Survived.
- The 8 VM CompileErrors are scattered normal non-compilable mutants (regex /
  type-level mutations in `DateFieldVm` ×3, `NumberFieldVm` ×5), **not** the
  wholesale CS0103 source-generator failure mode (which would make *every* VM
  mutant a CompileError).

**Verdict: no VM-exclusion fallback taken.** The VMs stay in the `mutate`
allow-list.

## Raw mutation score

| Metric | Value |
| --- | --- |
| **Mutation score (incl. NoCoverage)** | **93.12 %** |
| Score (covered code only) | 99.14 % |
| Killed | 228 |
| Timeout | 2 |
| Survived | 2 |
| NoCoverage | 15 |
| CompileError (excluded from score) | 38 |
| Ignored (out-of-scope / excluded) | 57 |

Detected = Killed + Timeout = 230. Denominator (incl. NoCoverage) = 247 →
93.12 %.

## Survivor inventory (file:line → classification)

17 survivors total (2 Survived + 15 NoCoverage). Classified against research §D
(three known gaps) and §F (intentional/equivalent):

| file:line | status | mutation | class | note |
| --- | --- | --- | --- | --- |
| `Services/NoteFolderService.cs:11` | NoCoverage | block removal of `Create` body | **§D #1** | `NoteFolderService` has zero tests → Phase 3.1 |
| `Services/NoteFolderService.cs:18` | NoCoverage | drop `EnsureWithinWorkspace` guard call | **§D #1** | same — guard-call-drop, untested |
| `Services/NoteFolderService.cs:19` | NoCoverage | drop `CreateDirectory` | **§D #1** | same |
| `Services/OrphanedTempCleaner.cs:31` | NoCoverage | remove `Trace.WriteLine` body / string | **§F** | log-only catch — equivalent (impl-review F2) |
| `Services/OrphanedTempCleaner.cs:26` | NoCoverage | `var root = message.WorkspacePath;` → `;` | **investigate (2.8)** | tests don't exercise the `Receive(WorkspaceChangedMessage)` entry path |
| `Services/TemplateRenderer.cs:85` | NoCoverage | last-line-without-newline branch (stmt + string) | **investigate (2.8)** | no test feeds a template whose final line lacks a terminator |
| `Services/TemplateRenderer.cs:86` | NoCoverage | `break;` in same no-final-newline branch | **investigate (2.8)** | same branch as :85 |
| `Services/TemplateRenderer.cs:48` | NoCoverage | block removal of `closing < 0` early return | **investigate (2.8)** | no-closing-fence path coverage |
| `Services/TemplateRenderer.cs:166` | **Survived** | `&&` → `\|\|` in `TryGetValue && value is not null` | **borderline equivalent (2.8)** | both arms yield `string.Empty` on the differing input; near-equivalent |
| `ViewModels/TemplateFormViewModel.cs:37` | **Survived** | `Result = null;` → `;` | **investigate (2.8)** | no test asserts `Result` is reset on a second `Load` |
| `Services/NameValidator.cs:65` | NoCoverage | `string.Empty` → literal in `rawInput ?? string.Empty` | benign null-default | tests never pass null `rawInput` |
| `Services/NoteFileService.cs:34` | NoCoverage | `string.Empty` on `ReadAsync` missing-file return | benign null-default | missing-file *async* read path untested |
| `Services/TemplateRenderer.cs:26` | NoCoverage | `string.Empty` in `templateText ??= string.Empty` | benign null-default | tests never pass null template |
| `ViewModels/Fields/TextFieldVm.cs:16` | NoCoverage | `string.Empty` in `Value ?? string.Empty` | benign null-default | tests always set `Value` |
| `ViewModels/TemplateFormViewModel.cs:61` | NoCoverage | `string.Empty` in `(field.Type ?? string.Empty)` | benign null-default | tests always set `Type` |

(The `OrphanedTempCleaner.cs:31` row covers both its mutants — statement + string.)

## Cross-check vs research §D / §F predictions

The suite killed **more** than predicted — several §D/§F candidates did **not**
survive:

- **§D #3 — `TemplateCatalog` `.templatesX/` prefix gap:** `TemplateCatalog`
  scored **100 %**; the predicted `StartsWith`-only survivor did **not** appear.
  Phase 3.3 still adds the negative test defensively, but it will not move the
  score (no survivor to kill).
- **§D #2 — `NoteFileService` BOM oracle:** the BOM *write* path is killed; the
  only `NoteFileService` survivor is the *async read* null-default (`:34`),
  unrelated to the BOM oracle. The §D concern is oracle *weakness*, not a current
  survivor — Phase 3.2 still tightens the literal-byte oracle per
  `feedback-independent-test-oracle`, but expect ~0 score delta from it.
- **§F — `TemplateParser` broad catch:** `TemplateParser` scored **100 %** — no
  catch-narrowing survivor appeared.
- **§F — `PathGuard` OS-conditional (`:27-29`):** `PathGuard` scored **100 %** —
  the platform-equivalent mutant did not survive on this Linux run.
- **§F — `NameValidator` TOCTOU `File.Exists`:** no survivor at the collision
  check; the only `NameValidator` survivor is the benign `?? string.Empty`
  null-default (`:65`).
- **§F — `OrphanedTempCleaner` Trace-log catch:** **confirmed** survivor
  (`:31`) — the one §F prediction that held.

### Implication for Phase 3 / Phase 4

- **Phase 3 score delta will be small.** The only §D gap that produces real
  survivors is `NoteFolderService` (3 NoCoverage mutants at `:11/:18/:19`).
  Closing it (Phase 3.1) should move the score; the BOM (3.2) and TemplateCatalog
  (3.3) fixes are defensive and expected to be ~score-neutral. This will be
  called out honestly in cookbook §6.5.
- **Phase 4 §F exclusions** should target the **confirmed** survivors only (do
  not pre-exclude on prediction): `OrphanedTempCleaner.cs:31` is the clear §F
  line. The `:26` Receive-path and the TemplateRenderer no-final-newline /
  no-closing-fence branches are real coverage gaps — decide in Phase 3/4 whether
  to add a cheap test or accept+exclude. `TemplateRenderer.cs:166` (`&&`→`||`)
  is borderline-equivalent; the `?? string.Empty` null-defaults are benign and
  may be left as NoCoverage or excluded if the floor needs it.
