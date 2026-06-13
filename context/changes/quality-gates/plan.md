# Quality-Gates Wiring Implementation Plan

## Overview

This is **Phase 4 of the test plan** (`context/foundation/test-plan.md` §3) — "Quality-gates wiring". The first three rollout phases produced the tests (template pipeline, file-safety) and proved they kill regressions (mutation testing). This phase **locks the floor**: it maps format / build / test to automated CI steps so the guarantees hold on every change, not just by convention, and repairs the recommended-local post-edit hook that broke during the Phase 3 `Notes.Core` split.

Per `change.md` and test-plan §5, after this phase:
- `dotnet format --verify-no-changes` (format) — **required**, local + CI.
- `dotnet build` and `dotnet test` — wired into CI alongside format (already required from earlier phases).
- post-edit hook (run affected tests) — **recommended-local**, repaired to cover `Notes.Core.Tests`.
- mutation-score threshold — stays **local-only / optional** (§5), not in CI.

## Current State Analysis

What exists today (verified during research):

- **No PR/CI workflow.** The only workflow is `.github/workflows/release.yml`, triggered solely on `v*` tags (publish + package AppImage/ZIP). Nothing runs build/format/test on push or PR. This phase adds the first CI workflow from scratch.
- **The full format gate fails today.** Measured locally:
  - `dotnet format whitespace --verify-no-changes` → **exit 0 (clean)**.
  - `dotnet format analyzers --verify-no-changes` → **exit 2**: 23 `xUnit1051` warnings (calls that should pass `TestContext.Current.CancellationToken`) across `Notes.Core.Tests/NoteSearchIndexTests.cs` and `Notes.Core.Tests/NoteFileServiceTests.cs`.
  - So `dotnet format --verify-no-changes` (the gate named in `change.md`, which includes the analyzers pass) is **red** until those 23 occurrences are fixed.
- **No `.editorconfig`.** `dotnet format` runs on SDK defaults — the gate's exact ruleset is implicit and can drift with the SDK. There is no `Directory.Build.props` either.
- **The post-edit hook is stale.** `.claude/hooks/run-related-tests.sh` (PostToolUse `Write|Edit`, wired in `.claude/settings.json`) only resolves test classes under `Notes.Tests/`. After the Phase 3 split, the logic tests live in `Notes.Core.Tests/` (e.g. `TemplateRendererTests.cs`, `NameValidatorTests.cs`). Editing any `Notes.Core/**` source now falls through to "no related test class → skip", so the entire core logic layer runs **unguarded** at edit time.
- **Stryker** (`stryker-config.json`, `test-runner: mtp`, `project: Notes.Core.csproj`, `thresholds.break: 95`) is run locally from `Notes.Core.Tests/` (single-project mode). The mutation gate is "optional after Phase 3" per §5.
- **Branch protection — already applied during planning.** A GitHub ruleset **"main protection"** (id `17644374`, enforcement `active`) now blocks direct pushes to `main` (requires PR, 0 approvals), plus `deletion` and `non_fast_forward`. It does **not** yet require a status check — that binding is deferred to this plan because the CI check did not exist when the ruleset was created.
- **Toolchain:** .NET 10 (`setup-dotnet` uses `10.0.x` in release.yml). `dotnet format` is a built-in SDK command — no tool install needed. `gh` is authenticated as repo admin on `llysyganicz/Notes`.

## Desired End State

When this plan is complete:

- `dotnet format --verify-no-changes` exits 0 on a clean tree, governed by a committed `.editorconfig`.
- Opening a PR against `main` triggers `.github/workflows/ci.yml`, which runs build, format-verify, and test on `ubuntu-latest`; a failure shows a red required check and blocks the merge.
- The "main protection" ruleset requires that CI check to pass before merge (in addition to the already-active PR requirement and direct-push block).
- Editing a `Notes.Core/**` source file in an agent loop runs its `Notes.Core.Tests` class via the post-edit hook; a failure is fed back into the agent's context (exit 2).
- `test-plan.md` §5 reflects the now-enforced gates, and `change.md` status is advanced.

**How to verify:** `dotnet format --verify-no-changes` returns 0; a deliberately mis-formatted PR shows a red CI check and cannot be merged; a direct `jj git push` to `main` is rejected; editing `Notes.Core/Services/TemplateRenderer.cs` triggers the hook to run `~TemplateRendererTests`.

### Key Discoveries:

- Whitespace is already clean; **only the analyzers pass (23 `xUnit1051`) blocks the full format gate** — the cleanup is bounded and mechanical (`NoteSearchIndexTests.cs`, `NoteFileServiceTests.cs`).
- The hook's mapping logic lives at `.claude/hooks/run-related-tests.sh:40-52`; the gap is that the `[ -f "$REPO_ROOT/Notes.Tests/${base}Tests.cs" ]` probe never looks in `Notes.Core.Tests/`.
- CI must run format-verify **after** build/restore but the order of format vs test is free; format is the cheapest and should fail fast.
- The ruleset already exists (id `17644374`); the required-check step is a **PATCH that adds a `required_status_checks` rule**, not a new ruleset — appending to the existing `rules` array.
- The CI check's "context" name that the ruleset must reference is the **job name** GitHub reports — fix it deliberately in the workflow (see Critical Implementation Details) so the ruleset binding matches.

## What We're NOT Doing

- **Not** adding Stryker / mutation testing to CI (stays local-only per §5; it is the slowest gate and the `break` floor was only just set).
- **Not** adding a Windows (or macOS) CI job — `Notes.Core` logic is platform-agnostic and tested via `MockFileSystem`; the `release.yml` already exercises the `win-x64` publish. (Re-evaluate if a Windows-only path/line-ending regression ever ships.)
- **Not** running CI on `push` to arbitrary branches or on docs-only changes — `on: pull_request` to `main` only.
- **Not** changing the `release.yml` workflow.
- **Not** adding a `.config/dotnet-tools.json` manifest — `dotnet format` is an SDK built-in; Stryker stays a local/global concern.
- **Not** requiring PR approvals (solo repo — 0 approvals; the PR + CI requirement is the gate).
- **Not** authoring a sprawling "aspirational" `.editorconfig` — it is deliberately conservative (see Phase 1) to avoid surfacing a violation backlog beyond the known 23.

## Implementation Approach

Three phases, ordered by dependency:

1. **Make the gate green locally first** (`.editorconfig` + fix the 23 violations) — CI must not be introduced red, or the first PR (the workflow PR itself) would be blocked by its own new gate.
2. **Introduce the CI workflow** so the check exists and reports on PRs.
3. **Wire enforcement** — bind the now-existing check to the ruleset, repair the local hook, and sync the docs/status.

This ordering avoids the chicken-and-egg deadlock: the workflow PR runs `ci.yml` on itself (because `on: pull_request`), so the check reports green on the very PR that introduces it — but only **after** Phase 1 has made the tree format-clean. The required-status-check binding (Phase 3) is applied only once the check name is confirmed to report, so no PR is ever blocked by a check that cannot run.

## Critical Implementation Details

- **Status-check context name must be fixed and stable.** The string the ruleset references as a required check is the **job name** GitHub surfaces (for a single-job workflow, the job's `name:` or its key). Pin an explicit, descriptive job name in `ci.yml` (e.g. job key `build-test-format`) and reference that exact string in the Phase 3 ruleset PATCH. A mismatch here means the required check never resolves and **locks the repo** — every PR stuck "expected". Confirm on the workflow's first PR run that the reported check name equals the string bound in the ruleset before/at the moment of binding.
- **Phase ordering is load-bearing, not cosmetic.** Phase 1 (green tree) must merge before Phase 3 binds the required check; Phase 2 (workflow) must exist before Phase 3 can name its check. Binding the check before the workflow exists would block all merges.
- **`.editorconfig` must stay conservative.** After authoring it, re-run `dotnet format --verify-no-changes`; if it surfaces violations beyond the known 23, either fix them within Phase 1 or relax the offending rule — do not merge Phase 1 with a red gate or a large incidental cleanup that obscures the intent. (The user's 2-space preference is deferred to a separate PR — keep 4-space here.)

## Phase 1: Format Gate Green Locally

### Overview

Author a focused `.editorconfig` and fix the 23 `xUnit1051` violations so `dotnet format --verify-no-changes` exits 0 on a clean tree. This is the prerequisite for CI enforcing the gate.

### Changes Required:

#### 1. EditorConfig

**File**: `.editorconfig` (new, repo root)

**Intent**: Make the format gate intentional, documented, and identical on every machine and in CI, rather than relying on shifting SDK defaults. Codify the formatting that the current tree already satisfies plus the analyzer severities the gate enforces — keeping `xUnit1051` active (since Phase 1 fixes those occurrences) so the cancellation-token convention is held going forward.

**Contract**: Root `.editorconfig` (`root = true`) covering `*.cs` (and a basic `*` section for line-ending/charset/trailing-whitespace). Pin the conventions already documented in `AGENTS.md`/CLAUDE.md (4-space indent — matching the current tree, file-scoped namespaces if used, naming) and set analyzer/diagnostic severities so the gate is deterministic. Keep it conservative: the post-authoring `dotnet format --verify-no-changes` must not surface violations beyond the 23 known `xUnit1051` ones (resolve or relax any that appear). No snippet — follow the standard .NET `.editorconfig` shape.

**Deferred:** the user prefers 2-space indent, but that switch is a whole-repo reindent (all 74 `.cs` files) and ships as a **separate PR** after this change — not in `quality-gates`. Use 4-space here to keep this phase's diff to the gate wiring.

#### 2. Fix xUnit1051 CancellationToken violations

**File**: `Notes.Core.Tests/NoteSearchIndexTests.cs`, `Notes.Core.Tests/NoteFileServiceTests.cs`

**Intent**: Resolve the 23 `xUnit1051` analyzer warnings so the analyzers pass of `dotnet format` is clean, making the full gate green.

**Contract**: At each flagged call site (the line:col list from `dotnet format analyzers --verify-no-changes`), pass `TestContext.Current.CancellationToken` to the method that accepts a `CancellationToken`. Behaviour of the tests is unchanged; they must still pass. Prefer letting `dotnet format` apply the fix (`dotnet format analyzers`) then reviewing the diff, rather than hand-editing 23 sites.

### Success Criteria:

#### Automated Verification:

- [ ] `.editorconfig` exists at repo root
- [ ] `dotnet format --verify-no-changes` exits 0
- [ ] `dotnet build` succeeds
- [ ] `dotnet test` passes (both suites)

#### Manual Verification:

- [ ] The `.editorconfig` only pins rules the current tree satisfies — no large incidental reformat in the diff beyond the 23 `xUnit1051` fixes
- [ ] The `xUnit1051` fixes are semantically correct (cancellation token threaded, no test behaviour change)

**Implementation Note**: After automated verification passes, pause for manual confirmation before proceeding to Phase 2.

---

## Phase 2: CI Workflow

### Overview

Add the first PR-triggered CI workflow running the build, format, and test gates on `ubuntu-latest`.

### Changes Required:

#### 1. CI workflow

**File**: `.github/workflows/ci.yml` (new)

**Intent**: Run the quality gates automatically on every PR targeting `main`, so format/build/test regressions are caught before merge. Fail fast and cheaply (format before test).

**Contract**:
- `name: CI`; `on: pull_request:` with `branches: [main]`.
- Single job, explicit stable key/name **`build-test-format`** (this exact string is bound as the required check in Phase 3 — see Critical Implementation Details), `runs-on: ubuntu-latest`.
- Steps: `actions/checkout@v4` → `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'` → `dotnet restore` → `dotnet build --no-restore` → `dotnet format --verify-no-changes --no-restore` → `dotnet test --no-build`.
- No mutation/Stryker step. No matrix.

### Success Criteria:

#### Automated Verification:

- [ ] `.github/workflows/ci.yml` exists and is valid YAML
- [ ] On a test PR, the `build-test-format` check runs and passes against the now-green tree
- [ ] A PR with a deliberate format violation makes the `build-test-format` check fail (red)

#### Manual Verification:

- [ ] The reported check name in the PR's checks UI is exactly `build-test-format` (the string Phase 3 will bind) — record it before binding
- [ ] Workflow run time is acceptable (cheap enough to run on every PR)

**Implementation Note**: After automated verification passes, pause for manual confirmation before proceeding to Phase 3.

---

## Phase 3: Local Hook + Enforcement Wiring

### Overview

Repair the post-edit test hook to cover `Notes.Core.Tests`, add a sibling post-edit format-check hook, bind the CI check as a required status check on the existing ruleset, and sync the test-plan and change status.

### Changes Required:

#### 1. Fix the post-edit hook for the Notes.Core split

**File**: `.claude/hooks/run-related-tests.sh`

**Intent**: Restore the recommended-local gate for the core logic layer. After the Phase 3 split, editing `Notes.Core/**` source resolves no test class and is silently skipped; map it to its sibling in `Notes.Core.Tests/`.

**Contract**: Extend the test-class resolution (around lines 40-52) so a `Notes.Core/**/Foo.cs` edit resolves to `Notes.Core.Tests/FooTests.cs` (run `~FooTests`) when that file exists — mirroring the existing `Notes.Tests/` probe. Keep the existing branches working: a directly-edited `*Tests.cs` (in either test project), the `Notes/ViewModels/Fields/* → FieldVm` mapping, and the `Notes/** → Notes.Tests/*Tests.cs` mapping. Files with no sibling test class still skip (exit 0). The `dotnet test --filter` invocation is unchanged.

#### 2. Add a post-edit format-check hook

**File**: `.claude/hooks/run-format-check.sh` (new), registered in `.claude/settings.json`

**Intent**: Keep the tree format-clean during agent loops so the CI format gate never trips at PR time. Mirror the existing test hook's verify-and-feed-back contract: the agent stays the sole author of file content (no silent mutation), and a format violation is surfaced back into the agent's context to fix.

**Contract**: New PostToolUse hook script following the shape of `run-related-tests.sh` — read the JSON payload from stdin, extract `.tool_input.file_path`, act only on `*.cs` (exit 0 otherwise). Run the **full** `dotnet format --verify-no-changes` (all three passes — whitespace + style + analyzers, the default) scoped to the edited file via `--include "$rel"` against the solution (`Notes.slnx`). On exit 0, exit 0. On non-zero, print the format diagnostics to stderr and **exit 2** so the agent re-formats. Register it in `.claude/settings.json` as a second command under the existing `PostToolUse` `Write|Edit` matcher (run it *before* the test hook — format is the cheaper gate and fails fast). Note: this means a `.cs` edit now triggers both a format check and a test run — accepted latency for edit-time safety.

#### 3. Bind the required status check to the ruleset

**File**: GitHub ruleset "main protection" (id `17644374`) — applied via `gh api`, no in-repo file.

**Intent**: Make the CI check merge-blocking, completing the "changes must pass CI before landing on main" guarantee. The ruleset already requires a PR and blocks direct pushes; this adds the status-check requirement.

**Contract**: PATCH `repos/{owner}/{repo}/rulesets/17644374`, appending a `required_status_checks` rule to the existing `rules` array (preserving `deletion`, `non_fast_forward`, `pull_request`). The required check references context `build-test-format` (the confirmed Phase 2 job name). Apply only after Phase 2's check has reported on a real PR, to avoid locking the repo on a check that cannot resolve.

#### 4. Sync test-plan and change status

**File**: `context/foundation/test-plan.md`, `context/changes/quality-gates/change.md`

**Intent**: Record that the gates are now enforced so future readers see the live state.

**Contract**: In `test-plan.md` §5, update the format row (now required, in CI), and the post-edit-hook row (now live/recommended — covers `Notes.Core.Tests` for tests, plus a sibling format-check hook); update §3 Phase 4 Status and the §8 freshness line as appropriate. In `change.md`, set `status: planned` (and later progress as phases land) and `updated:` to today. Do not alter the frozen strategy sections beyond the gate-state facts.

### Success Criteria:

#### Automated Verification:

- [ ] `gh api repos/{owner}/{repo}/rulesets/17644374` shows a `required_status_checks` rule referencing `build-test-format`
- [ ] Editing `Notes.Core/Services/TemplateRenderer.cs` triggers the test hook to run `~TemplateRendererTests` (visible `[run-related-tests]` line); editing a `Notes.Core` file whose test fails returns exit 2
- [ ] `run-format-check.sh` exists, is registered in `.claude/settings.json`, and is executable; editing a `.cs` file with a deliberate format violation surfaces the diff and returns exit 2; a clean edit passes (exit 0)
- [ ] `test-plan.md` §5 and `change.md` reflect the enforced gates

#### Manual Verification:

- [ ] A PR whose CI check is red cannot be merged (merge button blocked by the required check)
- [ ] A direct `jj git push` to `main` is rejected by the ruleset
- [ ] The test hook fix does not regress the existing `Notes.Tests/` and `Fields/` mappings (spot-check one edit in each)
- [ ] The format hook does not mutate the edited file (verify-only) and adds acceptable latency on a real edit

**Implementation Note**: This is the final phase; after verification, the quality floor is locked.

---

## Testing Strategy

### Unit Tests:

- No new product unit tests — this phase wires tooling, not application logic. The 23 `xUnit1051` edits must leave the existing `Notes.Core.Tests` suite green.

### Integration Tests:

- The CI workflow itself is the integration check: a real PR exercising green (passes) and red (format violation fails) paths.

### Manual Testing Steps:

1. After Phase 1: run `dotnet format --verify-no-changes` locally → expect exit 0.
2. After Phase 2: open a throwaway PR; confirm `build-test-format` runs green; push a stray-whitespace commit and confirm it goes red; note the exact check name.
3. After Phase 3: attempt `jj git push` to `main` → expect rejection; confirm a red-check PR cannot merge; edit a `Notes.Core` source file in an agent session and confirm the hook runs the matching `Notes.Core.Tests` class.

## Performance Considerations

CI cost is deliberately bounded: single ubuntu job, no matrix, no mutation. Format runs before test to fail fast on the cheapest gate. Docs-only churn under `context/**` does not trigger CI (PR-to-`main` trigger; pure-doc PRs still run but carry no code risk — acceptable, not optimized further).

## Migration Notes

The branch-protection ruleset is **already active** (applied during planning). From now on, all changes — including the PRs that implement this very plan — must go through a PR to `main`; direct pushes are rejected. Implementers using `jj` must push to a bookmark/feature branch and open a PR (see memory `jj-gh-pr-merge-detached-head` for the jj + `gh pr merge` interaction).

## References

- Test plan: `context/foundation/test-plan.md` §3 (Phase 4 row), §5 (Quality Gates), §6.5 (mutation/local).
- Change identity: `context/changes/quality-gates/change.md`
- Prior CI art: `.github/workflows/release.yml` (tag-triggered publish; `setup-dotnet` 10.0.x pattern)
- Hook to repair: `.claude/hooks/run-related-tests.sh` (wired in `.claude/settings.json`)
- Stryker config (local-only): `stryker-config.json`
- Active ruleset: GitHub "main protection" id `17644374`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Format Gate Green Locally

#### Automated

- [x] 1.1 `.editorconfig` exists at repo root — d7d45c62
- [x] 1.2 `dotnet format --verify-no-changes` exits 0 — d7d45c62
- [x] 1.3 `dotnet build` succeeds — d7d45c62
- [x] 1.4 `dotnet test` passes (both suites) — d7d45c62

#### Manual

- [x] 1.5 `.editorconfig` pins only rules the current tree satisfies — no large incidental reformat — d7d45c62
- [x] 1.6 `xUnit1051` fixes are semantically correct (no test behaviour change) — d7d45c62

### Phase 2: CI Workflow

#### Automated

- [x] 2.1 `.github/workflows/ci.yml` exists and is valid YAML
- [x] 2.2 On a test PR, the `build-test-format` check runs and passes against the green tree
- [x] 2.3 A PR with a deliberate format violation makes the check fail (red)

#### Manual

- [x] 2.4 Reported check name is exactly `build-test-format` (recorded before binding)
- [x] 2.5 Workflow run time is acceptable

### Phase 3: Local Hook + Enforcement Wiring

#### Automated

- [ ] 3.1 Ruleset `17644374` shows a `required_status_checks` rule referencing `build-test-format`
- [ ] 3.2 Editing a `Notes.Core` source triggers the test hook to run its `Notes.Core.Tests` class; a failing one returns exit 2
- [ ] 3.3 `run-format-check.sh` exists, is registered + executable; a format-violating `.cs` edit surfaces the diff and returns exit 2; a clean edit exits 0
- [ ] 3.4 `test-plan.md` §5 and `change.md` reflect the enforced gates

#### Manual

- [ ] 3.5 A PR whose CI check is red cannot be merged
- [ ] 3.6 A direct `jj git push` to `main` is rejected by the ruleset
- [ ] 3.7 Test hook fix does not regress the existing `Notes.Tests/` and `Fields/` mappings
- [ ] 3.8 The format hook does not mutate the edited file (verify-only) and adds acceptable latency
