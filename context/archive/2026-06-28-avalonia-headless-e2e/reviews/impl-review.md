<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Avalonia headless UI E2E tests (minimal slice)

- **Plan**: context/changes/avalonia-headless-e2e/plan.md
- **Scope**: All phases (1–5)
- **Date**: 2026-07-04
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical · 1 warning · 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Unplanned production mutation of App.MainWindow

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Plan Adherence
- **Location**: Notes/App.axaml.cs:18-24
- **Detail**: Phase 1 "Changes Required" listed only the new test project, .slnx wiring, and stale-obj cleanup — no production edits. But `App.MainWindow` was changed from a read-only computed property to a `virtual` property with a `_mainWindow` backing field and a public setter. This is the seam `E2ETestBase.InitializeAsync` uses (`app.MainWindow = MainWindow`) so dialog services (`NewNoteDialogService`, etc.) resolve the test window. Production behavior is unchanged (getter falls through to `desktop.MainWindow` when `_mainWindow` is null), so it is a clean seam — but undocumented in the plan and avoidable via the headless desktop lifetime.
- **Fix A ⭐ Recommended (APPLIED)**: Document the seam as a plan addendum and keep it.
  - Strength: One short addendum to Phase 1 closes the plan-vs-diff gap without changing code, and preserves the simplest test wiring (`app.MainWindow = …`).
  - Tradeoff: The test-only virtual stays in production shell; future agents see a test-only seam on a core type unless a comment travels with the code.
  - Confidence: HIGH — the seam is real and used; matches the §6.7 phase-5 note style.
  - Blind spot: Whether reviewers prefer the seam commented in source.
- **Fix B**: Remove the production seam; inject via the desktop lifetime in `E2ETestBase`.
  - Strength: Keeps production `App` untouched; the headless lifetime's `MainWindow` is found through the existing getter fallback.
  - Tradeoff: `E2ETestBase` gains a cast depending on Avalonia headless always provisioning a desktop lifetime; slightly more fragile.
  - Confidence: MED — confident Avalonia.Headless restores a classic desktop lifetime, but the alternative was not run end-to-end.
  - Blind spot: Whether any test's `ApplicationLifetime` is null at `InitializeAsync` time.
- **Decision**: FIXED via Fix A — plan addendum added to Phase 1 documenting the `App.MainWindow` virtual seam (read-only → virtual + settable, getter fallback preserving production behavior).

### F2 — SelectTreeItemAsync sets the VM SelectedNode instead of the real TreeView

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria / test fidelity
- **Location**: Notes.E2ETests/E2ETestBase.cs:80-94 (SelectTreeItemAsync)
- **Detail**: Phase 4's flow is "select an existing note → it loads into the editor." `SelectTreeItemAsync` implemented "select" by assigning `treeViewModel.SelectedNode = match` directly on the VM, not by interacting with the real `TreeView` control. The test verified *editor loads on VM selection change* but did not exercise the `TreeView`↔`ViewModel` `SelectedItem` two-way binding. If that binding broke (a real click no longer propagated to `SelectedNode`), `EditAndAutoSaveTests` would still pass because they skipped the click path.
- **Fix (APPLIED)**: `SelectTreeItemAsync` now finds the real `TreeView` control and sets `tree.SelectedItem = match`, propagating through the two-way binding to `NoteTreeViewModel.SelectedNode`. This exercises the same TreeView → ViewModel binding path a real click takes.
  - Strength: Closes the `TreeView` binding coverage gap the E2E layer is meant to own.
  - Confidence: HIGH — verified: all 8 E2E tests pass with the real-TreeView-driven selection; full suite (290 tests) green.
  - Blind spot: Whether real-tree selection covers every click-path edge (e.g. keyboard navigation); not exercised here.
- **Decision**: FIXED — `SelectTreeItemAsync` now drives `TreeView.SelectedItem` instead of the VM directly.

## Success-criteria verification (all automated)

| Check | Result |
|---|---|
| `dotnet build` (solution) | PASS — 0 warnings, 0 errors |
| `dotnet test --filter …~Notes.E2ETests` | PASS — 8/8 |
| `dotnet test` (all projects) | PASS — 221 + 61 + 8 = 290 |
| `Notes.E2ETests` in `Notes.slnx` | PASS |
| DI mirror of `Notes/Program.cs` | PASS — identical except the 3 fakes (`IFileSystem`, `IFolderPicker`, `ISettingsService`), as planned |
| Control names vs. `NewNoteDialog.axaml` | PASS — `NameInput`, `CreateButton`, `CancelButton`, `ErrorText` all present |

## Manual checks (pending, not rubber-stamped)

The deliberate-break validation steps ("break `NewNoteDialog.OnCreate` and confirm the happy-path test fails"; "break `NoteEditorView.OnEditorTextChanged` wiring and confirm the auto-save test fails") plus an IDE-load confirmation remain unchecked. They are open, not marked complete.
