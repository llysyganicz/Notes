<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Gruvbox Theme, App Icon, and Tree-Row Context Menu

- **Plan**: context/changes/gruvbox-theme/plan.md
- **Scope**: Phases 1–4 of 4 (full plan)
- **Date**: 2026-08-01
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 1 observation

## Prior per-phase reviews consulted

Per `context/foundation/lessons.md` ("Full-plan reviews must consult per-phase
review fix decisions"), the saved per-phase reviews were read before drift
detection so their FIXED deviations are treated as effective plan amendments:

- `impl-review-phase-1.md` — F1 (minimal `TextEditor` chrome theme landed in
  Phase 1 ahead of Phase 2): FIXED via plan addendum to Phase 1 contract #6.
  F2 (`Avalonia.Themes.Fluent` PackageReference left after `FluentTheme`
  removal): FIXED — PackageReference removed.
- `impl-review-phase-2.md` — F1 (stale forward-reference comment in
  `Editor.axaml`): FIXED — comment rewritten to point at the `.xshd` +
  loader. The token-hex duplication between `GruvboxPalette.axaml` and
  `GruvboxHighlightingLoader.cs` was explicitly reviewed and accepted as a
  deliberate, documented tradeoff (`.xshd` has no `DynamicResource`
  equivalent) — **not re-flagged** here.

None of these resurface as new findings.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | WARNING |

## Verification evidence

- **Build** — PASS. `dotnet build`: 0 errors, 0 warnings across all 5 projects
  (Notes.Core, Notes.Core.Tests, Notes, Notes.Tests, Notes.E2ETests).
- **`dotnet test`** — PASS. 302 tests green: Notes.Core.Tests 229, Notes.Tests
  64, Notes.E2ETests 9 (E2E went 8→9 — the new `TreeViewContextMenuTests`
  added in Phase 4). No regressions vs. the per-phase reviews (301→302,
  accounting for the one new test).
- **`dotnet run --project Notes`** — not executed (GUI launch). Corroborated
  by the headless E2E suite, which constructs the real `MainWindow` (icon
  load, theme-include resolution, `TextEditor` template application) without
  exceptions on every `[AvaloniaFact]`.
- **Plan-vs-diff cross-check** (baseline `a827155` → `@`):
  - Every planned file is present in the diff; every diffed source file maps
    to a plan contract. No `Notes.Core` / `Notes.Core.Tests` file touched —
    the "No `Notes.Core` change" guardrail holds.
  - The 8-file theme split (`Notes/Themes/Controls/{Base,Buttons,Input,
    DatePicker,Lists,Menus,Scrolling,Editor}.axaml` aggregated by
    `GruvboxControls.axaml`) is the explicit "split into per-control files if
    it grows large" allowance in Phase 1 contract #2 — sanctioned, not scope
    creep.
  - `Notes/Assets/README.md` is the plan's required reproducibility "render
    note," placed beside the assets rather than in the change folder — spirit
    preserved, benign location choice.
- **Architecture** — `GruvboxHighlightingLoader` lives in `Notes/Services/`
  (platform layer, uses `Avalonia.Platform.AssetLoader` + `ThemeVariant`),
  not `Notes.Core`. Dependency direction `Notes → Notes.Core` unchanged.
- **Safety/quality scan** — pure-presentation change (XAML theming, embedded
  `.xshd` string-substitution, icon assets, one headless E2E test). No network,
  DB, auth, or disk-write surfaces. `GruvboxHighlightingLoader.Load` disposes
  its `AssetLoader` stream and both readers; token hexes are compile-time
  literals (no injection class). `NoteEditorView` subscribes
  `Application.ActualThemeVariantChanged` on attach and unsubscribes on detach —
  no handler leak (per phase-2 review, still true). No N+1/unbounded
  iteration; theme-toggle reload re-parses one small `.xshd` (rare event).

## Findings

### F1 — Phase 3 manual icon checks (3.3/3.4) marked complete but not manually verified

- **Severity**: 🟡 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/gruvbox-theme/plan.md (Progress 3.3, 3.4); context/changes/gruvbox-theme/change.md (Phase 3 note)
- **Detail**: The Phase 3 Progress checkboxes 3.3 ("Linux taskbar/dock + window title bar show the gruvbox icon") and 3.4 ("Windows `.exe` in Explorer, taskbar, and title bar show the icon") are marked `- [x]`, but `change.md` openly documents that neither could be manually verified during implementation — Linux release packaging is AppImage (not built in this repo) and no Windows machine with this source was available. The plan's own per-phase gate ("pause for manual confirmation before proceeding") was therefore satisfied by a documented user decision (mark complete, verify against the release AppImage / a Windows machine post-release, ship a patch if either surfaces) rather than by actual observation. This is open, not hidden rubber-stamping — the concern is purely *traceability*: once this change is archived, that deferred-verification commitment lives only in a prose `change.md` note and could be lost. The `<ApplicationIcon>` (Windows exe/taskbar) and `Window.Icon` (title bar, X11 desktops) wiring itself is correct per plan; only the cross-platform manual sighting is pending.
- **Fix**: Carry the deferred-verification commitment out of the `change.md` prose and into a tracked follow-up by creating `context/changes/gruvbox-theme/follow-ups/verify-app-icon-cross-platform.md` restating the two pending checks (Linux AppImage taskbar/dock + `WM_CLASS`/`_NET_WM_ICON`; Windows `.exe` Explorer/taskbar/title bar) and the trigger (first release AppImage build / Windows machine), so `/10x-archive` and `/10x-status` surface it as outstanding work rather than letting it vanish with the archived change.
- **Decision**: SKIPPED