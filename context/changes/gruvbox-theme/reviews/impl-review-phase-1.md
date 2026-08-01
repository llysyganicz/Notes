<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Gruvbox Theme, App Icon, and Tree-Row Context Menu

- **Plan**: context/changes/gruvbox-theme/plan.md
- **Scope**: Phase 1 of 4
- **Date**: 2026-08-01
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Verification evidence

- **1.1 `dotnet build`** — PASS. 0 errors, 0 warnings across all 5 projects (Notes.Core, Notes.Core.Tests, Notes, Notes.Tests, Notes.E2ETests).
- **1.2 `dotnet test`** — PASS. 301 tests green: Notes.Core.Tests 229, Notes.Tests 64, Notes.E2ETests 8. No regressions.
- **1.3 `dotnet run --project Notes`** — previously confirmed at commit 995eb8c; not re-run (GUI launch). E2E tests construct the real MainWindow headless without XAML/asset-load exceptions, corroborating.
- **Manual 1.4–1.7** — marked `[x]` at 995eb8c. No observable evidence in the diff contradicts these (theming is present for all named surfaces); accepted as previously verified.

## Findings

### F1 — Minimal AvaloniaEdit chrome theme landed in Phase 1 (a Phase 2 deliverable)

- **Severity**: 🟡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: Notes/Themes/Controls/Editor.axaml
- **Detail**: Phase 1 plan contract #6 states the AvaloniaEdit Fluent `StyleInclude` is removed in Phase 1 and "reappears in Phase 2 as a gruvbox replacement"; Phase 2 change #1 owns the "Gruvbox AvaloniaEdit control theme." The implementation added a minimal `TextEditor`/`TextArea` chrome theme (background, foreground, line numbers, selection, caret, template) in Phase 1, with syntax token colors explicitly deferred to Phase 2 (per in-file comment). This is a benign scope advance: once the Fluent include is gone, the editor needs *some* template to render, and `dotnet run`/E2E tests exercise the editor. The deviation is reasoned and documented in-file, but it is work the plan assigned to Phase 2.
- **Fix**: Add a one-line addendum to the plan's Phase 1 contract #6 noting a minimal editor chrome theme landed here to keep the editor functional post-Fluent-removal, with full syntax theming still due in Phase 2.
- **Decision**: FIXED — plan addendum added to Phase 1 contract #6.

### F2 — `Avalonia.Themes.Fluent` NuGet package still referenced after `FluentTheme` removal

- **Severity**: 🟡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: Notes/Notes.csproj:18
- **Detail**: `<FluentTheme />` was removed from App.axaml and grep confirms no code/AXAML references Fluent theme types (only comments mention "Fluent"). The `<PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.3" />` line is now an unused dependency. Harmless functionally, but it bloats restore and leaves a trap for a future dev to re-introduce Fluent. All controls the app uses have gruvbox ControlThemes, so no Fluent fallback is needed.
- **Fix**: Remove the `Avalonia.Themes.Fluent` PackageReference line and re-run `dotnet build` + `dotnet test` to confirm nothing relied on it transitively.
- **Decision**: FIXED — PackageReference removed; build + 301 tests re-run green.
