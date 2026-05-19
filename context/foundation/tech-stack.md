---
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
---

## Why this stack

A solo developer building a cross-platform markdown note-taking desktop app (Linux + Windows, macOS later) needs a native-rendered UI that avoids web wrappers and stays in the .NET/C# ecosystem. Avalonia UI is the only .NET framework that satisfies all three constraints simultaneously — MAUI has no official Linux support, WPF and WinForms are Windows-only, and Uno Platform's web-centric approach adds unnecessary complexity for a desktop-first app. The app is single-user and local-only with no auth, payments, realtime, or background-job requirements, keeping the stack minimal. Distribution via GitHub Releases with GitHub Actions CI on auto-deploy-on-merge aligns with a solo, after-hours workflow. Avalonia's `dotnet new` templates provide the scaffold entry point; the `best-effort` bootstrapper confidence reflects that Avalonia is not yet wired end-to-end in the bootstrapper — the `dotnet` registry key identifies the toolchain, but manual Avalonia-specific setup steps will be required.
