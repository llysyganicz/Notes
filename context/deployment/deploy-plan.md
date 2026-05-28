# Deployment

## Overview

Releases are published to **GitHub Releases** automatically by a GitHub Actions workflow triggered on every `v*` tag push.

Two artifacts are produced from a single `ubuntu-latest` runner using cross-compilation:

| Artifact | Platform | Format |
|---|---|---|
| `Notes-<tag>-x86_64.AppImage` | Linux x64 | Single portable executable |
| `Notes-<tag>-win-x64.zip` | Windows x64 | Self-contained ZIP (single EXE inside) |

Tags containing `-` (e.g. `v1.0.0-beta.1`) are published as **pre-releases**. Clean tags (e.g. `v1.0.0`) are published as **stable releases**.

## How to release

### Pre-release (smoke test / beta)

```sh
jj tag set v0.0.1-beta.1 -r <commit>
jj git push --tag v0.0.1-beta.1
```

### Stable release

```sh
jj tag set v0.0.1 -r <commit>
jj git push --tag v0.0.1
```

The workflow runs automatically. Monitor it at:
`https://github.com/llysyganicz/Notes/actions`

Or from the terminal:
```sh
gh run watch --repo llysyganicz/Notes
```

## Versioning

- The tag drives the `Version` property passed to `dotnet publish` (e.g. tag `v1.2.3-beta.1` → `Version=1.2.3-beta.1`).
- `FileVersion` is **not** overridden by the tag — it uses the static value in `Notes/Notes.csproj` (`0.0.1.0`). Update it manually in `Notes/Notes.csproj` for major releases. Windows requires `FileVersion` to be numeric (`major.minor.build.revision`).

## Rollback

1. Go to the GitHub Release page and edit the release to **Draft** — this removes the download links without deleting the release.
2. Re-tag the previous good commit with a new patch version and push it.

```sh
jj tag set v0.0.2 -r <good-commit>
jj git push --tag v0.0.2
```

## Moving an existing tag

If you need to retag (e.g. workflow file was not included in the tagged commit):

```sh
jj tag set v0.0.1-beta.1 --allow-move -r <new-commit>
jj git push --tag v0.0.1-beta.1
```

## Workflow file

`.github/workflows/release.yml`

Key details:
- Runner: `ubuntu-latest` (single runner for both platforms)
- .NET version: `10.0.x`
- Packaging tool: `DotnetPackaging.Tool` pinned to `10.1.3`
- Windows publish flags: `PublishSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true` (required to embed SkiaSharp/HarfBuzz native libraries into the single EXE)
- Linux publish: standard self-contained directory (no single-file; `dotnetpackager appimage` consumes the directory)
- No secrets required — uses the automatic `GITHUB_TOKEN`

## Known issues / upcoming maintenance

- **Node.js 20 deprecation (June 2026):** `actions/checkout@v4`, `actions/setup-dotnet@v4`, and `softprops/action-gh-release@v2` will need to be updated to Node.js 24-compatible versions (`@v5` or later) before June 2026.
- **SmartScreen warning on Windows:** The Windows EXE is unsigned. Users will see "Windows protected your PC" and must click "More info → Run anyway". This is expected until code signing is set up via SignPath Foundation or similar.
- **AppImage requires FUSE 2 (`fuse2`):** On Ubuntu 22.04+, Fedora 36+, and similar modern distros, FUSE 3 ships by default. Users on these systems can work around it with `./Notes.AppImage --appimage-extract-and-run`.
