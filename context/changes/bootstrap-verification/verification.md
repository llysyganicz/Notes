---
bootstrapped_at: 2026-05-18T19:10:30Z
starter_id: dotnet
starter_name: ".NET (ASP.NET Core webapi)"
project_name: Notes
language_family: dotnet
package_manager: dotnet
cwd_strategy: subdir-then-move
bootstrapper_confidence: best-effort
phase_3_status: ok
audit_command: "dotnet list package --vulnerable --include-transitive"
---

## Hand-off

```yaml
starter_id: dotnet
package_manager: dotnet
project_name: Notes
hints:
  language_family: dotnet
  team_size: solo
  deployment_target: github-releases
  ci_provider: github-actions
  ci_default_flow: auto-deploy-on-merge
  bootstrapper_confidence: best-effort
  path_taken: custom
  quality_override: false
  self_check_answers:
    typed: true
    from_official_starter: true
    conventions: true
    docs_current: true
    can_judge_agent: true
  has_auth: false
  has_payments: false
  has_realtime: false
  has_ai: false
  has_background_jobs: false
```

### Why this stack

A solo developer building a cross-platform markdown note-taking desktop app (Linux + Windows, macOS later) needs a native-rendered UI that avoids web wrappers and stays in the .NET/C# ecosystem. Avalonia UI is the only .NET framework that satisfies all three constraints simultaneously — MAUI has no official Linux support, WPF and WinForms are Windows-only, and Uno Platform's web-centric approach adds unnecessary complexity for a desktop-first app. The app is single-user and local-only with no auth, payments, realtime, or background-job requirements, keeping the stack minimal. Distribution via GitHub Releases with GitHub Actions CI on auto-deploy-on-merge aligns with a solo, after-hours workflow. Avalonia's `dotnet new` templates provide the scaffold entry point; the `best-effort` bootstrapper confidence reflects that Avalonia is not yet wired end-to-end in the bootstrapper — the `dotnet` registry key identifies the toolchain, but manual Avalonia-specific setup steps will be required.

## Pre-scaffold verification

| Signal | Value | Severity | Notes |
| --- | --- | --- | --- |
| npm package | not run | n/a | dotnet starter; no npm CLI involved |
| GitHub repo | not run | n/a | card docs_url (learn.microsoft.com) is not a GitHub URL |

## Scaffold log

**Resolved invocation**: `dotnet new avalonia.app -n .bootstrap-scaffold -o /home/lysy/Projects/Notes/.bootstrap-scaffold` (overridden from registry's `dotnet new webapi -n {name} --no-restore` to use Avalonia template per user request)
**Strategy**: subdir-then-move
**Exit code**: 0
**Files moved**: 7 (App.axaml, App.axaml.cs, app.manifest, Notes.csproj, MainWindow.axaml, MainWindow.axaml.cs, Program.cs)
**Conflicts (.scaffold siblings)**: none
**.gitignore handling**: absent in scaffold
**.bootstrap-scaffold cleanup**: deleted
**Post-move fixups**: renamed .bootstrap-scaffold.csproj → Notes.csproj; replaced `_bootstrap_scaffold` namespace with `Notes` in all .cs and .axaml files; skipped obj/ (build artifact, regenerated on restore)

## Post-scaffold audit

**Tool**: `dotnet list package --vulnerable --include-transitive`
**Summary**: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
**Direct vs transitive**: not distinguished by this tool (no findings to classify)

## Hints recorded but not acted on

| Hint | Value |
| --- | --- |
| bootstrapper_confidence | best-effort |
| quality_override | false |
| path_taken | custom |
| self_check_answers | typed: true, from_official_starter: true, conventions: true, docs_current: true, can_judge_agent: true |
| team_size | solo |
| deployment_target | github-releases |
| ci_provider | github-actions |
| ci_default_flow | auto-deploy-on-merge |
| has_auth | false |
| has_payments | false |
| has_realtime | false |
| has_ai | false |
| has_background_jobs | false |

## Next steps

Next: a future skill will set up agent context (CLAUDE.md, AGENTS.md). For now, your project is scaffolded and verified — happy hacking.

Useful manual steps in the meantime:
- Add `CommunityToolkit.Mvvm` via `dotnet add package CommunityToolkit.Mvvm` for your MVVM architecture.
- Review the scaffolded files and set up your ViewModels directory structure.
- Address audit findings per your project's risk tolerance — the full breakdown is in this log.
