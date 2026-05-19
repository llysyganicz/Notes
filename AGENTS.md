# AGENTS.md

## Project Overview

Notes is a cross-platform desktop markdown note-taking app built with **Avalonia UI 12** on **.NET 10** (C#). Product requirements, domain concepts, and architectural decisions are documented in `context/foundation/` — read `prd.md` and `tech-stack.md` before making design decisions.

## Build & Run

```sh
dotnet build
dotnet run
```

Publish a self-contained binary:
```sh
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r win-x64 --self-contained
```

No test framework is configured yet. When adding tests, use a separate `Notes.Tests` project with `dotnet test`.

## Conventions

- **MVVM pattern:** Keep business logic out of code-behind (.axaml.cs) files. Views bind to ViewModels; code-behind should only contain UI wiring.
- **Compiled bindings** are enabled by default — use `x:DataType` in AXAML and avoid reflection-based bindings.
- **Composition over inheritance** for extracting shared behavior between similar components.

## Version Control

This repo uses **jujutsu (jj)**, not raw git. Use `jj` commands for all VCS operations.
