---
date: 2026-06-10T19:58:47+0200
researcher: lysy
git_commit: 40825692a5d89570593e553e2c2fa449586f2b9b
branch: main
repository: Notes
topic: "Test-effectiveness validation via mutation testing (test-plan Phase 3)"
tags: [research, codebase, mutation-testing, stryker, template-pipeline, file-safety]
status: complete
last_updated: 2026-06-10
last_updated_by: lysy
---

# Research: Test-effectiveness validation via mutation testing (test-plan Phase 3)

**Date**: 2026-06-10T19:58:47+0200
**Researcher**: lysy
**Git Commit**: 40825692a5d89570593e553e2c2fa449586f2b9b
**Branch**: main
**Repository**: Notes

## Research Question

Phase 3 of `context/foundation/test-plan.md` — "Test-effectiveness validation":
prove the Phase 1–2 tests actually kill regressions, via **mutation testing
scoped to the template + file-safety logic**, answering *"are these tests
correct?"*. Ground the tooling (Stryker.NET) fully, map the exact code under
mutation, and judge whether the existing suite will yield a meaningful score.

## Summary

**Phase 1 (template pipeline) and Phase 2 (file-safety) are both implemented and
on disk** — the working copy was a stale head off the Phase-1 merge; it has been
rebased onto `main` (commit `4082569`, "feat(file-safety) … (#10)") so all the
file-safety code + tests are present. Both suites exist and are unusually
oracle-disciplined, so mutation testing is the *right* next move: the tests look
strong, and mutation is exactly how we confirm that rather than assume it.

**The single biggest risk is the test runner, not the tests.** `Notes.Tests`
runs on **xUnit v3 + Microsoft Testing Platform (MTP)** — not VSTest:
`Notes.Tests/Notes.Tests.csproj:8-9,15` (`UseMicrosoftTestingPlatformRunner=true`,
`xunit.v3 3.2.2`), with no `Microsoft.NET.Test.Sdk` and no
`xunit.runner.visualstudio`. Stryker.NET historically integrates through VSTest.
**Whether Stryker 4.14.x can drive this MTP/xunit.v3 project is unconfirmed and
must be proven by a smoke run before any plan commits to a mutation-score gate.**
This dominates the plan's risk register.

If the runner works, the highest-yield targets are the **pure** logic files —
`TemplateRenderer`, `NameValidator`, `PathGuard`, `TemplateParser` — which are
both mutation-dense and backed by independent-oracle tests. Two findings shape
the score in advance: one genuine coverage hole (**`NoteFolderService` has zero
tests**) and one oracle to tighten (**`NoteFileServiceTests` BOM-write equality**
re-derives expected bytes from the same `Encoding.UTF8` the SUT uses — the very
anti-pattern in the team's own memory). Several survived mutants will be
**intentional/equivalent** (broad catches, best-effort logging, the accepted
TOCTOU window) and must be pre-classified so the score isn't misread.

## Detailed Findings

### A. Repository state (resolved during research)

- `main` == `origin/main` == `40825692` ("feat(file-safety): write atomicity,
  path containment, and collision guard (#10)"). No pull was needed — the commit
  was already local.
- The jj working copy (`@`) was a **divergent head off the Phase-1 merge**
  (`c8cf85a`) that never received the file-safety merge; on-disk it lacked
  `PathGuard.cs`, `OrphanedTempCleaner.cs`, their tests, and the archived
  file-safety change folder. Rebased `@` onto `main` (`jj rebase -r @ -d main`),
  which brought 21 file-safety files onto disk while preserving this change
  folder.
- **Doc drift to note:** `test-plan.md` §3 row 2 still reads Phase 2 = "not
  started" and §6.5 (Phase 3 cookbook) is still "TBD", yet §6.2 and the §6.6
  Phase-2 note are filled with the shipped file-safety patterns. The status table
  lags reality; the implementation is done and archived at
  `context/archive/2026-06-08-file-safety/`.

### B. Project / test-project shape (for Stryker config)

- Solution: `Notes.slnx` (new XML `.slnx` format) — **undocumented in Stryker**;
  avoid relying on it (see §E).
- App: `Notes/Notes.csproj` — `net10.0`, `WinExe`, `System.IO.Abstractions 22.1.1`,
  YamlDotNet 18, Markdig, Avalonia 12.0.3.
- Tests: `Notes.Tests/Notes.Tests.csproj` — `net10.0`, **`xunit.v3` 3.2.2**,
  **MTP runner** (`Notes.Tests.csproj:7-9`), `NSubstitute 5.3.0`,
  `System.IO.Abstractions.TestingHelpers 22.1.1`, `Avalonia.Headless.XUnit 12.0.3`.
  No VSTest adapter, no `Microsoft.NET.Test.Sdk`. **This is the linchpin** (§E.1).

### C. Code under mutation — where the mutants live

Scope = "template + file-safety logic". Priority ordered by mutant density × cost
(pure logic is cheap and dense; IO logic needs MockFileSystem):

**Template (all pure — cheapest, highest yield):**
- `Notes/Services/TemplateRenderer.cs` — **densest file in scope.** Fence detection
  `lines[0].Content != "---"` (`:31`), closing-fence scan + offset math
  `closing - 1` / `closing + 1` (`:52-53`), CRLF detection `text[contentEnd-1]=='\r'`
  and `"\r\n"` vs `"\n"` (`SplitLines` `:80-99`), `{{(.*?)}}` delimiter regex
  (`:19`), form-block strip with `i++`/`i--` index dance (`:108-134`), `"form:"`
  literal (`:136`), indent char literals `' '`/`'\t'` (`:139-140`), undeclared-token
  passthrough (`:160`).
- `Notes/Services/NameValidator.cs` — **second densest.** `ValidateCharacters`
  (`:63-96`) is pure: `trimmed.Length==0` (`:66`), `"."`/`".."` traversal (`:71`),
  reserved-name array CON/PRN/AUX/… (`INameValidator`/`:11-16`), `/`+`\` guard
  (`:82`), invalid-char loop (`:87-93`); `.md` append ternary (`:33`); collision
  `File.Exists`/`Directory.Exists || File.Exists` (`:38`,`:55`, needs MockFS);
  `ResolveAbsolutePath` `'/'`→sep replace (`:102`).
- `Notes/Services/PathGuard.cs` — pure-ish (depends on `ISettingsService`):
  empty-root throw (`:18`), trailing-separator append `!EndsWith(sep)` (`:23-24`,
  prevents `/foo` matching `/foobar`), OS-conditional comparison (`:27-29`,
  platform-equivalent on Linux — flag), central `!StartsWith(root)` containment
  (`:31`).
- `Notes/Services/TemplateParser.cs` — pure: `shape?.Form is not { Count: > 0 }`
  (`:51`), guards (`:35`,`:43`), broad `catch (Exception)→Empty` (`:68`,
  **deliberately broad — see §F**).
- `Notes/Services/TemplateCatalog.cs` — pure: `.templates/` prefix literal (`:9`),
  `StartsWith(prefix, Ordinal)` (`:34`), `remainder.Length==0 || Contains('/')`
  (`:41`), `Count > 0` (`:29`).
- `Notes/ViewModels/TemplateFormViewModel.cs` — `CreateField` switch (`:58-68`):
  `"date"`/`"number"`/`"select"` arms + `_`→`TextFieldVm` default; `(Type ?? "")`
  `.ToLowerInvariant()` (`:61`).
- `Notes/ViewModels/Fields/NumberFieldVm.cs` — dense pure: `IsIntegerFormat`
  `IsNullOrEmpty || Contains('.')` (`:60`), `^[A-Za-z](\d+)$` regex (`:17`),
  `!standard.Success || int.Parse(...)==0` precision check (`:66`). `DateFieldVm`
  ISO-vs-custom format branch (`:36`). `TextFieldVm`/`SelectFieldVm` trivial.

**File-safety (IO — needs MockFileSystem / ThrowingFileSystem):**
- `Notes/Services/NoteFileService.cs` — atomic write: `TempSuffix=".tmp"` (`:40`),
  **write-then-`Move(temp, dst, overwrite: true)`** ordering + the `overwrite: true`
  bool (`:49-50`), missing-file early return (`:21`,`:32`), best-effort temp delete
  in catch (`:52-56`).
- `Notes/Services/OrphanedTempCleaner.cs` — `IsNullOrEmpty(root) || !Dir.Exists`
  (`:25`), `GetFiles(root, "*"+TempSuffix, AllDirectories)` glob + recursion flag
  (`:28`), swallowed per-file catch+Trace (`:30-31`, **equivalent mutant — §F**).
- `Notes/Services/NoteDeleter.cs` — thin; only meaningful mutant is
  `Directory.Delete(path, recursive: true)` (`:25`); guard-call-drop mutants may
  survive.
- `Notes/Services/NoteFolderService.cs` — pure pass-through `EnsureWithinWorkspace`
  → `CreateDirectory`; **no conditionals, no test (coverage hole — §D)**.
- `Notes/Services/PathContainmentException.cs` — exception subclass, **exclude**.

### D. Existing test base — oracle quality (the crux of a meaningful score)

The suite is, unusually, **oracle-disciplined** — expected values are built from
inputs or fixed constants, not re-derived from SUT output. Per-SUT verdicts:

| SUT | Test file | Tests | Oracle quality | Mutation outlook |
|-----|-----------|-------|----------------|------------------|
| TemplateRenderer | `TemplateRendererTests.cs` | 14 Fact + 2 Theory | **Strongest** — header comment pins "expected built from template+def+values" (`:30-32`); extra `DoesNotContain("{{name}}")` loop (`:225-235`) | Excellent |
| TemplateParser | `TemplateParserTests.cs` | 19 Fact | Strong/independent literals; negative `Names.Contains("c")==false` (`:165`) | Good |
| TemplateCatalog | `TemplateCatalogTests.cs` | 8 Fact | Strong; **gap:** no `.templatesX/` prefix-without-separator case → a `StartsWith`-only mutant could survive | Good (one gap) |
| TemplateFormVM + Fields | `TemplateFormViewModelTests.cs` (11), `FieldVmTests.cs` (13) | 24 Fact | Strong; de-DE culture test (`:69-85`) + `ParsingNumberStyle` cases discriminate | Good |
| NoteFileService | `NoteFileServiceTests.cs` | 13 Fact + 4 Theory | Mostly strong (atomicity oracle is a fixed constant, `:137-138`); **FLAG** BOM-write equality re-derives bytes via same `Encoding.UTF8` (`:45-53`) | Good except BOM path |
| NameValidator | `NameValidatorTests.cs` | 23 Fact + 3 Theory | Strong; `"console"` passes reserved check (`:293-299`) kills over-broad mutant | Strong |
| NoteDeleter | `NoteDeleterTests.cs` | 2 Fact + 2 Theory | Strong; uses **real `PathGuard`** → doubles as containment coverage; "still exists" post-condition oracle (`:45`,`:71`) | Good |
| PathGuard | `PathGuardTests.cs` | 7 Fact + 1 Theory | Strong; **prefix-trap `/workspace` vs `/workspace-evil`** (`:64-71`) + `IOException` inheritance (`:81-89`) kill the highest-value mutants | Strong (best-covered) |
| OrphanedTempCleaner | `OrphanedTempCleanerTests.cs` | 3 Fact | Strong; "outside root survives" kills broadened-root mutant | Good (Trace-log survivor is intentional) |
| NoteTreeViewModel (collision) | `NoteTreeViewModelTests.cs` | subset `:307-444` | Strong; collision oracle is fixed `OriginalContent` constant (`:395`); uses real Parser/Renderer | Good |
| **NoteFolderService** | **— none —** | **0** | **No coverage** | **Guard mutant will survive** |

**Two pre-mutation actions worth taking** (decide in `/10x-plan`):
1. **Coverage hole — `NoteFolderService` has no test.** Add a containment test
   mirroring `NoteDeleterTests`' out-of-root theory, or Stryker reports its
   `EnsureWithinWorkspace` guard as a survived no-coverage mutant.
2. **Oracle to tighten — `NoteFileServiceTests.Save_WhenCalled_WritesUtf8WithoutBom`
   (`:45-53`)** asserts `Encoding.UTF8.GetBytes("hello")` against SUT output — same
   encoder family (violates `feedback-independent-test-oracle`). Partially saved by
   the literal-hex BOM check (`0xEF,0xBB,0xBF`, `:52`); tighten to full literal
   bytes before trusting the encoding-path score.

### E. Stryker.NET grounding (observed 2026-06-10)

1. **RUNNER COMPATIBILITY — RESOLVED (2026-06-10).** Stryker's **default VSTest
   runner cannot run this suite** — confirmed empirically: the user installed
   `dotnet-stryker` and the runner fails to execute the tests, because the project
   is **MTP + xunit.v3** with no VSTest adapter (`Notes.Tests.csproj:8-9,15`).
   **Fix:** Stryker added a **native MTP runner in 4.13** (still **preview** in
   4.14.x) — enable it with `--test-runner mtp` (CLI) or `"test-runner": "mtp"`
   (config). The test project's existing `UseMicrosoftTestingPlatformRunner=true` +
   `xunit.v3` is exactly what the MTP runner attaches to, so **no test-project
   changes are needed**, and it is faster than VSTest (persistent runner, no
   per-mutant process startup). **Two caveats for the plan:** (a) MTP runner is
   *preview* — validate it holds across a full scoped run, don't assume; (b)
   **per-test coverage analysis is not yet implemented for the MTP runner** — it
   filters out mutants covered by *zero* tests but cannot narrow to the covering
   tests per mutant, so `coverage-analysis: perTest` does not fully apply and each
   covered mutant reruns the (file-scoped) test set. With the surface scoped to 8
   files this is acceptable. **Fallback** (only if the preview MTP runner
   misbehaves): add `xunit.runner.visualstudio` + `Microsoft.NET.Test.Sdk` to the
   test project — per xUnit docs these coexist with MTP and restore the default
   VSTest path; heavier (touches the deliberately MTP-only test project), so prefer
   `--test-runner mtp` first.
   Sources: stryker-mutator.io/blog/stryker-net-mtp-runner (MTP runner, 4.13,
   preview, `--test-runner mtp`, perTest limitation); xunit.net MTP docs
   (VSTest coexistence via the two packages); GH stryker-net #3094, #3421.
2. **Version:** `dotnet-stryker` **4.14.2** (NuGet, 2026-05-18). Tool host targets
   net8.0, compatible to run on net10.0. No explicit doc statement that mutating a
   **.NET 10 target-framework** project is certified — `dotnet build` must be green
   first (BuildAnalyzer compile errors abort mutation, GH #3397).
3. **Install:** local manifest (recommended, checked-in) — `dotnet new
   tool-manifest` (auto-created on first local install under .NET 10),
   `dotnet tool install dotnet-stryker`, `dotnet tool restore` for CI. Run
   `dotnet stryker`.
4. **Solution format:** `.slnx` is **undocumented** in Stryker. Not needed here —
   for dotnet-core projects the solution is optional; run from `Notes.Tests/`
   (single referenced source project auto-discovered) to sidestep `.slnx` entirely.
   If a solution is ever required, keep a `.sln`, not `.slnx`. (`solution` must sit
   at/above the run dir — GH #2678.)
5. **File scoping (the §1 cost×signal requirement):** the **`mutate` glob array**.
   An explicit *include* list makes Stryker mutate **only** matching files — the
   allow-list that guarantees no GUI/AXAML or other `.cs` is touched. AXAML isn't
   C# so it's outside the mutator anyway.
6. **Thresholds & CI:** `thresholds.break` → **non-zero exit** when score drops
   below it (the CI gate); `high`/`low` only color the report. Reporters: use
   `["html","json","cleartext"]`. `coverage-analysis: perTest` is the main perf
   control alongside file scoping and `concurrency`.

Ready-to-adapt `stryker-config.json` (run from repo root; verify glob anchoring
with a dry run):

```json
{
  "stryker-config": {
    "test-runner": "mtp",
    "project": "Notes.csproj",
    "test-projects": ["Notes.Tests/Notes.Tests.csproj"],
    "mutate": [
      "Notes/Services/Template*.cs",
      "Notes/Services/NoteFileService.cs",
      "Notes/Services/NameValidator.cs",
      "Notes/Services/NoteDeleter.cs",
      "Notes/Services/NoteFolderService.cs",
      "Notes/Services/PathGuard.cs",
      "Notes/Services/OrphanedTempCleaner.cs",
      "!**/*.axaml.cs"
    ],
    "thresholds": { "high": 80, "low": 60, "break": 70 },
    "reporters": ["html", "json", "cleartext"],
    "concurrency": 4
  }
}
```

> `test-runner: mtp` is the fix for this project (the default VSTest runner cannot
> run the MTP/xunit.v3 suite). `coverage-analysis: perTest` is intentionally
> **omitted** — the MTP runner does not yet support per-test coverage filtering
> (it still auto-skips zero-coverage mutants). Re-add it only after switching to
> the VSTest fallback.

Doc sources: NuGet `dotnet-stryker`; stryker-mutator.io getting-started /
configuration / reporters; GH #2678 (solution scope), #3397 (compile-error abort);
MS Learn .NET 10 local tool-manifest behavior.

### F. Intentional / equivalent mutants — pre-classify so the score isn't misread

These **should** survive; chasing them wastes effort and a "kill everything"
threshold would be wrong:

- **`TemplateParser` broad `catch (Exception) → FormDefinition.Empty`** (`:68`,
  comment "do not narrow", lessons.md): the silent-by-design pipeline is a **locked
  contract** (`context/archive/2026-06-06-testing-template-pipeline/research.md`).
  Catch-narrowing mutants with no triggering test are intentional.
- **`OrphanedTempCleaner` per-file `catch`+`Trace.WriteLine`** (`:30-31`): logging
  only — removing the log body is an equivalent mutant (impl-review F2).
- **#3 TOCTOU window** (`NameValidator.File.Exists` → write race): **accepted
  residual risk** for a single-user desktop app (test-plan §6.2; file-safety
  research `:96-100`). No test; intentionally so.
- **`PathGuard` OS-conditional comparison** (`:27-29`): the OrdinalIgnoreCase arm
  is unreachable on Linux/CI — **platform-equivalent mutant**.
- **Guard-call-drop mutants** in `NoteDeleter`/`NoteFolderService`/`NoteFileService`:
  survive unless a test asserts `IPathGuard.EnsureWithinWorkspace` was invoked.
- **(If scope ever widens beyond these 8 files) `NoteSearchIndex` CTS non-dispose**
  (lessons.md): a mutant adding `Dispose()` is equivalent — the non-dispose is the
  correct design.
- **CommunityToolkit source-generated members** (`[ObservableProperty]`,
  `[RelayCommand]` `*Command`): not your source; not mutated.

## Code References

- `Notes.Tests/Notes.Tests.csproj:7-9,15` — MTP + xunit.v3, no VSTest (the linchpin)
- `Notes/Services/TemplateRenderer.cs:19,31,52-53,80-99,108-140,160` — densest mutant surface
- `Notes/Services/NameValidator.cs:33,38,55,63-96,102` — pure char/collision logic
- `Notes/Services/PathGuard.cs:18,23-24,27-31` — containment + trailing-separator
- `Notes/Services/NoteFileService.cs:40,49-50` — atomic write-then-rename, `overwrite: true`
- `Notes/Services/OrphanedTempCleaner.cs:25,28,30-31` — glob/recursion + swallowed catch
- `Notes/Services/NoteFolderService.cs:16-20` — **untested** guard+CreateDirectory
- `Notes/ViewModels/TemplateFormViewModel.cs:58-68` — field-type switch
- `Notes/ViewModels/Fields/NumberFieldVm.cs:17,60,66` — integer-format detection
- `Notes.Tests/TemplateRendererTests.cs:30-32,225-235` — reference independent oracle
- `Notes.Tests/PathGuardTests.cs:64-71,81-89` — prefix-trap + IOException kill cases
- `Notes.Tests/NoteFileServiceTests.cs:45-53` — BOM-write oracle to tighten
- `Notes.Tests/Fakes/ThrowingFileSystem.cs` — fault injection for the catch path
- `Notes.Tests/TestApp.cs` — headless Avalonia bootstrap (per-test startup cost)

## Architecture Insights

- **Pure/IO split is favorable for mutation testing.** The template engine and
  `NameValidator.ValidateCharacters`/`PathGuard` are pure, deterministic, and
  already covered by independent-oracle tests — the ideal mutation target. IO is
  isolated behind `IFileSystem` (`MockFileSystem`) with a `ThrowingFileSystem`
  fault injector, so even the atomic-write/cleanup paths are coverable without real
  disk, and re-running thousands of times under Stryker is deterministic (no
  `Thread.Sleep`, no real clock).
- **Perf sink = Avalonia headless tests.** Only the Field/FormVM/TreeVM tests pull
  in `TestApp`; the pure-logic SUTs don't. `coverage-analysis: perTest` + the file
  allow-list keep the matrix small.
- **The suite already embodies the project's anti-oracle rule** (test-plan §6.1,
  memory `feedback-independent-test-oracle`), which is *why* mutation testing is
  worth doing here: it converts "the tests look principled" into a measured number.

## Historical Context (from prior changes)

- `context/archive/2026-06-08-file-safety/research.md` — atomic write design,
  PathGuard containment, accepted #3 TOCTOU residual (`:96-100`).
- `context/archive/2026-06-08-file-safety/reviews/impl-review.md` — F1 read-path
  confinement (fixed), F2 OrphanedTempCleaner log-only catch, F3 marker-interface
  removal — sources for the equivalent-mutant list (§F).
- `context/changes/testing-template-pipeline/research.md` + `plan.md` — Phase 1
  silent-by-design contract, independent-oracle discipline, `dropdown`→`select`
  keyword fix.
- `context/foundation/lessons.md` — CTS non-dispose (equivalent-mutant prior);
  full-plan reviews must consult per-phase review decisions.

## Related Research

- `context/changes/testing-template-pipeline/research.md` — Phase 1 template-pipeline research
- `context/archive/2026-06-08-file-safety/research.md` — Phase 2 file-safety research

## Open Questions

1. **[RESOLVED] Does Stryker.NET 4.14.2 drive an MTP + xunit.v3 test project?**
   The default VSTest runner does **not** (confirmed empirically). The fix is the
   native MTP runner: `--test-runner mtp` / `"test-runner": "mtp"` (Stryker 4.13+,
   preview) — no test-project changes. Remaining sub-question for the plan: does
   the *preview* MTP runner hold up across a full scoped run (it should; validate,
   don't assume), and is the VSTest-package fallback worth keeping as a documented
   escape hatch? See §E.1.
2. **Does Stryker mutate a `net10.0` target cleanly?** Not doc-certified; confirm
   in the same smoke run.
3. **What break threshold?** Given the strong oracles, a high floor (e.g. 80) may
   be realistic for the pure SUTs, but should be set *after* the first real score —
   not guessed. Consider per-area thresholds or excluding the known-equivalent
   survivors (§F) rather than a single repo-wide number.
4. **Fix the two test gaps (§D) before or after the first run?** Running first
   quantifies their impact (NoteFolderService no-coverage, BOM oracle); fixing
   first produces a cleaner baseline. A `/10x-plan` decision.
5. **CI wiring is Phase 4's job** (test-plan §5: "mutation-score threshold —
   optional after Phase 3"). Phase 3 should produce a *runnable, scoped* mutation
   command + baseline score + cookbook §6.5; the CI gate itself belongs to Phase 4.
