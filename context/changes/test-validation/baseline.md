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

---

## Post-fix re-run (Phase 3)

After closing the three gaps (Phase 3.1–3.3), re-ran with the same command from
`Notes.Core.Tests/`. Output: `StrykerOutput/2026-06-11.18-05-30/`.

| Metric | Raw (P2) | Post-fix (P3) | Δ |
| --- | --- | --- | --- |
| **Mutation score (incl. NoCoverage)** | 93.12 % | **94.33 %** | **+1.21 pp** |
| Killed | 228 | 231 | +3 |
| NoCoverage | 15 | 12 | −3 |
| Survived | 2 | 2 | 0 |
| Timeout | 2 | 2 | 0 |

**The entire delta is `NoteFolderService`** (3.1): its three NoCoverage mutants at
`:11/:18/:19` are now **Killed** — `NoteFolderService` survivors remaining: **0**.

- **3.2 BOM oracle:** the write-path mutant was already killed; the oracle is now a
  fixed literal byte array `{0x68,0x65,0x6C,0x6C,0x6F}` (ASCII `h,e,l,l,o`),
  no longer re-derived from the SUT's `Encoding.UTF8`
  (`feedback-independent-test-oracle`). Score-neutral, as predicted.
- **3.3 TemplateCatalog prefix-trap:** `TemplateCatalog` was already 100 %; the new
  `.templatesX/` / `.templates-backup/` / `.templates` negative case is defensive —
  it locks in the kill of a "drop the trailing `/` from the prefix" mutant.
  Score-neutral, as predicted.

### Remaining survivors after Phase 3 (14)

The three §D-gap survivors are gone. What remains is the Phase-4 input — **not**
purely the §F set (3.8 is only partially literal-true; documented honestly):

- **§F intentional/equivalent:** `OrphanedTempCleaner.cs:31` (Trace-log catch).
- **Coverage gaps to accept+exclude or cheaply cover in Phase 4:**
  `OrphanedTempCleaner.cs:26` (`Receive` entry path untested),
  `TemplateRenderer.cs:48` (no-closing-fence early return),
  `TemplateRenderer.cs:85/86` (last-line-without-newline branch).
- **Borderline-equivalent:** `TemplateRenderer.cs:166` (`&&`→`||`).
- **Minor oracle gap:** `TemplateFormViewModel.cs:37` (`Result` reset on re-`Load`).
- **Benign `?? string.Empty` null-defaults (NoCoverage):** `NameValidator.cs:65`,
  `NoteFileService.cs:34`, `TemplateRenderer.cs:26`, `TextFieldVm.cs:16`,
  `TemplateFormViewModel.cs:61`.

Phase 4 will line-range-exclude the confirmed §F + accepted-equivalent lines, then
set `thresholds.break` just under the resulting score.

### Reading the report — `Ignored` vs the cleartext `# survived` column

A trap worth recording for cookbook §6.5: the **cleartext** reporter's table has no
`# ignored` column, so it folds `Ignored` mutants **into `# survived`**. Running
`dotnet stryker` from the repo root produced a cleartext table showing
`NoteFolderService` "1 survived" and `TemplateCatalog` "7 survived" — yet both rows
read **100 % score**. A 100 % score with real survivors is impossible; the tell is
that those are `Ignored`, not survived.

- `NoteFolderService` "1 survived" = **1 Ignored** block-removal (`Create()` body),
  reason `"Removed by block already covered filter"` — its inner statements (L18/L19)
  are individually mutated and Killed, so Stryker dedups the whole-block removal.
- `TemplateCatalog` "7 survived" = **6 Ignored** block-removals + **1 CompileError**.
- The repo-root run also tallied excluded-file mutants (`NoteSearchIndex` 96, etc.)
  into the same cleartext `# survived` column (309 total) — none are real survivors.

**Fix adopted:** the `markdown` reporter has a dedicated `Ignored` column (and
separate `Compile Errors` / `No Coverage`), so it shows the true `Survived` count
(2). Reporters set to **`["markdown", "json"]`** (dropped `html` + `cleartext`):
`markdown` is the human summary, `json` drives the survivor extraction in this file.
The real undetected set is unchanged: **2 Survived + 12 No Coverage = 14**.

---

## Phase 4 — kill the real gaps, accept equivalents, lock threshold

**Decision (revised from the plan):** the plan called for line-range *config*
exclusions of the §F/equivalent survivors. Stryker's only precise per-mutant
suppressors turned out to be (a) `// Stryker disable` **source comments** and (b)
**character-offset** config spans (`{start..end}` is char indices, not lines —
brittle). Source comments were declined (no tool-coupled comments in source), so the
final approach is: **kill the genuinely-coverable gaps with tests; accept + document
the true equivalents; set `break` under the measured score.** Equivalents are *not*
suppressed — the score stays honest.

### Gaps killed (score 93.12% → 94.33% → 96.76%)

Six undetected mutants were real coverage gaps and were killed — **preferring to
strengthen an existing test over adding a new method** (now an AGENTS.md rule):

| Mutant | How killed |
| --- | --- |
| `TemplateRenderer:48` unclosed-fence early return | **new** `Render_WhenFrontmatterFenceUnclosed_TreatsWholeTextAsBody` |
| `TemplateRenderer:85/86` no-trailing-newline branch | **strengthened** existing `Render_WhenMultipleDeclaredTokens...` (dropped its trailing `\n`) |
| `TemplateFormViewModel:37` `Fields.Clear()` on re-`Load` | **new** `Load_WhenCalledAfterSubmit_ReplacesFieldsAndResetsResult` |
| `OrphanedTempCleaner:26` missing-root early return | **folded** existing empty-workspace test into a `[Theory]` over existing-empty + missing |

(The Phase 3 `TemplateCatalog` prefix-trap Fact was likewise **folded** into
`List_WhenNoTemplatesLoaded_ReturnsEmpty` via near-miss inputs.) Net: only 2 new test
methods. Suite 221 green in `Notes.Core.Tests` (+61 `Notes.Tests` = 282 total).

### Accepted equivalents (the residual 8 undetected mutants)

These survive by design — chasing them would be wrong; they are catalogued, not
suppressed:

| file:line | mutator | why equivalent / accepted |
| --- | --- | --- |
| `OrphanedTempCleaner.cs:31` | Statement, String | best-effort `Trace.WriteLine` log inside a catch (impl-review F2) |
| `TemplateRenderer.cs:166` | Logical (`&&`→`\|\|`) | differs only if a *declared* token maps to an explicit `null` value — the `IReadOnlyDictionary<string,string>` contract forbids it |
| `NameValidator.cs:65`, `NoteFileService.cs:34`, `TemplateRenderer.cs:26`, `TextFieldVm.cs:16`, `TemplateFormViewModel.cs:61` | String | `?? string.Empty` defensive null-guards; the null branch is unreachable from callers |

### Threshold locked

Final score **96.76%** (237 killed + 2 timeout = 239 detected; 8 accepted undetected;
247 valid). `"thresholds": { "high": 100, "low": 95, "break": 95 }` — `break` is
~1.7 pp under the score (≈5 new real survivors would trip it). `dotnet stryker` (from
`Notes.Core.Tests/`) exits **0**. `high`/`low` are cosmetic.
