# Quality-Gates Wiring — Plan Brief

> Full plan: `context/changes/quality-gates/plan.md`

## What & Why

Phase 4 of the test plan: **lock the quality floor in CI**. The earlier phases wrote the tests and proved they kill regressions; this phase makes the build/format/test gates run automatically on every PR (not just by convention) and repairs the recommended-local post-edit hook that broke during the Phase 3 `Notes.Core` split.

## Starting Point

The repo has only a tag-triggered `release.yml` — **no PR/CI workflow at all**. `dotnet format --verify-no-changes` is **red today** (23 `xUnit1051` cancellation-token warnings in `Notes.Core.Tests`; whitespace is already clean). There is no `.editorconfig`. The post-edit hook (`run-related-tests.sh`) only looks in `Notes.Tests/`, so it silently skips every `Notes.Core/**` edit since the split.

## Desired End State

Opening a PR against `main` runs build + format-verify + test on ubuntu; a failure shows a red **required** check and blocks the merge. Direct pushes to `main` are rejected (PR-only). Editing a `Notes.Core` file in an agent loop runs its `Notes.Core.Tests` class. `dotnet format` is green, governed by a committed `.editorconfig`.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Format gate scope | Full `dotnet format`, fix violations first | Matches the test-plan's literal gate; catches analyzer/convention drift | Plan |
| `.editorconfig` | Add a focused one (4-space, matches tree) | Makes the gate intentional + identical across machines/CI; 2-space preference deferred to a separate PR | Plan |
| OS matrix | ubuntu-only | `Notes.Core` is platform-agnostic (MockFileSystem); release.yml already covers win-x64 | Plan |
| Mutation in CI | Local-only | §5 marks it optional; slowest gate, `break` floor just set | Plan |
| Post-edit hook | Fix to cover `Notes.Core.Tests` | The recommended-local gate is currently broken for all core logic | Plan |
| CI trigger | `pull_request` → `main` only | Direct pushes are blocked at the repo level instead | Plan |
| Block direct push | GitHub ruleset (applied during planning) | User wants repo-level enforcement, not just advisory CI | Plan |

## Scope

**In scope:** `.editorconfig`; fix 23 `xUnit1051`; `ci.yml` (build/format/test on PR); test-hook fix + new format-check hook; bind required check to ruleset; sync test-plan §5 + change.md.

**Out of scope:** Stryker in CI; Windows/macOS jobs; touching `release.yml`; a `dotnet-tools.json` manifest; PR-approval requirements.

## Architecture / Approach

Three dependency-ordered phases: **(1)** make the format gate green locally (editorconfig + fix violations) so CI is never introduced red; **(2)** add the PR-triggered CI workflow so the check exists and reports; **(3)** wire enforcement — bind the check to the ruleset, fix the hook, sync docs. The workflow PR runs `ci.yml` on itself, so the check reports green on the very PR that introduces it — provided Phase 1 already made the tree clean.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Format green locally | `.editorconfig` + 23 `xUnit1051` fixes → `dotnet format` exits 0 | An over-broad editorconfig surfacing a violation backlog |
| 2. CI workflow | `ci.yml`: build/format/test on PR to main | Wrong/unstable check-name string → ruleset can't bind cleanly |
| 3. Hook + enforcement wiring | Test hook covers `Notes.Core.Tests`; new format-check hook; required check bound; docs synced | Binding a check that can't resolve → **locks the repo** (mitigated by ordering) |

**Prerequisites:** .NET 10 SDK; `gh` admin auth (present). The "main protection" ruleset (id `17644374`) is **already active** — implementers must use a PR flow (jj → bookmark → PR), no direct push to `main`.
**Estimated effort:** ~1–2 sessions across 3 phases; Phase 1 is the bulk (cleanup + editorconfig tuning).

## Open Risks & Assumptions

- **Deadlock risk:** requiring a status check that never reports locks all merges. Mitigated by binding the check only after Phase 2's check reports on a real PR, and by confirming the exact check name (`build-test-format`).
- Assumes the conservative `.editorconfig` surfaces no violations beyond the known 23 — any extras are fixed or the rule relaxed within Phase 1.
- Assumes pure-docs PRs running CI is acceptable (cheap; no path filter added).

## Success Criteria (Summary)

- `dotnet format --verify-no-changes` exits 0; a mis-formatted PR goes red and cannot merge.
- A direct `jj git push` to `main` is rejected; changes land only via PR with a passing check.
- Editing a `Notes.Core` source file triggers the hook to run the matching `Notes.Core.Tests` class.
