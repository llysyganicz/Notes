---
project: Notes
researched_at: 2026-05-19
context_type: mvp
recommended_strategy: GitHub Releases — Windows ZIP + Linux AppImage
runner_up: GitHub Releases — portable archives only (tar.gz / zip, no AppImage tooling)
tech_stack:
  language: C#
  framework: Avalonia UI 12
  runtime: .NET 10
packaging:
  windows: zip (self-contained single-file)
  linux: AppImage (DotnetPackaging.Tool)
distribution: GitHub Releases
ci: GitHub Actions (single ubuntu-latest runner)
signing: deferred (free options identified)
---

## Recommendation

**Distribute via GitHub Releases with two artifact types: ZIP (Windows) and AppImage (Linux).**

Both artifacts are built from a single `ubuntu-latest` GitHub Actions runner using `dotnet publish` cross-compilation. No platform-specific toolchain, no installer complexity, no sandbox. The AppImage is a single portable executable that runs on most Linux distros without installation. The ZIP is the direct Windows equivalent — extract and run.

## Packaging Strategy

### Windows — self-contained ZIP

`dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` produces a single `.exe` bundling the .NET 10 runtime. This is zipped and uploaded to GitHub Releases.

`PublishSingleFile=true` collapses the publish folder into one executable. Avalonia extracts native libraries (SkiaSharp, etc.) to a temp directory on first run — expected behavior. Test startup on a clean Windows machine without .NET installed before the first release.

### Linux — AppImage

**DotnetPackaging.Tool** (NuGet: `DotnetPackaging.Tool`, MIT license, v10.1.3, Jan 2026) builds an AppImage directly from the `dotnet publish` output directory. Pure .NET, no native dependencies, no `appimagetool` binary required.

```bash
dotnet tool install --global DotnetPackaging.Tool

dotnetpackager appimage \
  --directory ./publish/linux-x64 \
  --output ./artifacts/Notes-<version>-x86_64.AppImage \
  --application-name "Notes" \
  --summary "Markdown note-taking app" \
  --homepage https://github.com/<owner>/Notes
```

The resulting `.AppImage` is a single executable. Users run `chmod +x Notes.AppImage && ./Notes.AppImage` — no installation, no package manager, no sandbox. Works on Arch, Ubuntu, Fedora, and any distro with FUSE support (see Risk Register for FUSE 2 caveat).

### Single CI runner — cross-compilation

Both artifacts build from one `ubuntu-latest` runner. .NET 10 supports cross-compiling Windows binaries from Linux:

```
ubuntu-latest runner:
  1. dotnet publish -r linux-x64  →  AppImage via dotnetpackager
  2. dotnet publish -r win-x64    →  ZIP
```

This eliminates the need for a `windows-latest` runner entirely.

## Code Signing

### Windows — SmartScreen reality

Since 2024, no certificate type (OV, EV, or Azure Trusted Signing) bypasses Windows SmartScreen for new files. SmartScreen reputation is per file hash and builds organically through download volume. Every new build starts with zero reputation.

Signing still matters: it shows a verified publisher name (weaker "unrecognized app" warning) instead of "unknown publisher" (stronger block). Enterprise admins can whitelist the certificate.

### Free signing options for open source

Two viable free options when the project is ready (both require a public repo with meaningful history):

1. **SignPath Foundation** (signpath.org) — HSM-backed, CI-integrated. Publisher displays as "SignPath Foundation". Requires privacy policy, development history. Approval: ~6-11 weeks.

2. **Necessary Code Signing** (sign.necessary.nu) — simpler process (form → API token). Publisher displays as "Necessary Innovations AB, Sweden". Uses `osslsigncode` + HTTP API. Easy GitHub Actions integration.

### Decision: defer signing to post-MVP

For MVP, ship unsigned — the SmartScreen warning is unavoidable regardless of signing for new files. Document the "More info → Run anyway" workaround in the README. Apply for SignPath once the project has public history and a download base.

### Linux — no signing needed

AppImage files don't require signing for distribution. Users verify by downloading from the official GitHub Release page.

## CI/CD — GitHub Actions

### Release workflow

Triggered on version tag push (`v*`). Single `ubuntu-latest` runner builds both artifacts:

```yaml
on:
  push:
    tags: ['v*']

jobs:
  release:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install DotnetPackaging tool
        run: dotnet tool install --global DotnetPackaging.Tool

      - name: Publish Linux (x64)
        run: |
          dotnet publish Notes/Notes.csproj -c Release -r linux-x64 \
            --self-contained -o ./publish/linux-x64

      - name: Build AppImage
        run: |
          dotnetpackager appimage \
            --directory ./publish/linux-x64 \
            --output ./artifacts/Notes-${{ github.ref_name }}-x86_64.AppImage \
            --application-name "Notes" \
            --summary "Markdown note-taking app"
          chmod +x ./artifacts/Notes-${{ github.ref_name }}-x86_64.AppImage

      - name: Publish Windows (x64)
        run: |
          dotnet publish Notes/Notes.csproj -c Release -r win-x64 \
            --self-contained -p:PublishSingleFile=true \
            -o ./publish/win-x64

      - name: Build Windows ZIP
        run: |
          cd ./publish/win-x64
          zip -r ../../artifacts/Notes-${{ github.ref_name }}-win-x64.zip .

      - uses: softprops/action-gh-release@v2
        with:
          files: |
            artifacts/Notes-${{ github.ref_name }}-x86_64.AppImage
            artifacts/Notes-${{ github.ref_name }}-win-x64.zip
```

### Release artifacts per version

- `Notes-<version>-x86_64.AppImage` — Linux portable executable
- `Notes-<version>-win-x64.zip` — Windows portable archive

## Anti-Bias Cross-Check

### Devil's Advocate — Weaknesses

1. **SmartScreen hard-blocks the unsigned EXE.** Users see "Windows protected your PC" with no publisher name and must click "More info" → "Run anyway." Many won't — and will assume the app is malicious. This is a real adoption barrier, not minor friction.
2. **No update mechanism in either format.** Users who download v1.0 never know v1.1 exists. The PRD's core differentiator over Notable is that Notes is *maintained* — but without update signals, that advantage is invisible after first install.
3. **AppImage has no desktop integration by default.** No launcher icon appears in GNOME/KDE menus, no file association. Users wanting desktop integration must set it up manually.
4. **`PublishSingleFile=true` extracts native DLLs to `%TEMP%` on every Windows run.** Antivirus software (including Windows Defender) may flag or quarantine the extracted SkiaSharp/HarfBuzz libraries. Known issue with self-contained .NET single-file apps.
5. **`DotnetPackaging.Tool` is a low-star project (124 stars, single maintainer).** Bus-factor risk if abandoned. Less tested than `appimagetool`-based approaches.

### Pre-Mortem — How This Could Fail

The developer ships v1.0. On Windows, the unsigned single-file EXE triggers a hard SmartScreen block. Technical users click through; everyone else stops. A forum thread asks "is Notes malware?" — it gets more Google impressions than the project README. SignPath approval is at week 8.

The Linux AppImage works on Arch and Ubuntu but fails silently on Fedora 40 — FUSE 2 is not installed by default there. The developer (on Arch, where `fuse2` is available) doesn't catch this. Meanwhile, no users report updating to v1.1 because there is no update signal. Three months after release, 80% of installs are still on v1.0 with a known bug. The developer posts a GitHub release note, but nobody sees it.

### Unknown Unknowns

- **AppImage type 2 requires FUSE 2 (`fuse2`), not FUSE 3.** Ubuntu 22.04+, Fedora 36+, and others ship FUSE 3 by default. Users without `fuse2` see "fuse: device not found" on launch. Mitigation: document `--appimage-extract-and-run` flag in README as a fallback.
- **Windows Defender may quarantine extracted SkiaSharp/HarfBuzz DLLs** from the single-file EXE. Test on a fresh Windows VM with Defender enabled before the first release. If triggered, fall back to `PublishSingleFile=false` (produces a folder ZIP instead of a single EXE).
- **Cross-compiled Windows EXE has no Windows version resource by default.** Without `<AssemblyTitle>`, `<Product>`, `<FileVersion>` in the `.csproj`, Windows Explorer → Properties shows blank fields. Looks unprofessional — set these before the first release.
- **`DotnetPackaging.Tool` targets .NET 8 internally** but installs and runs fine on a machine with .NET 10 SDK. No conflict with the app's target framework.

## Operational Story

- **Preview / pre-release builds:** Push a tag like `v1.0.0-beta.1`. The workflow auto-detects the `-` suffix and marks the release as pre-release (`prerelease: ${{ contains(github.ref_name, '-') }}`). Pre-release builds appear on the Releases page but not as "Latest release."
- **Secrets:** Only `GITHUB_TOKEN` is needed now (auto-provided by GitHub Actions — no setup required). When signing is added, the token (SignPath API key or Necessary Code Signing token) goes in `Settings → Secrets → Actions` as `SIGNING_TOKEN`. Reference as `${{ secrets.SIGNING_TOKEN }}` in YAML — never echoed in logs. Rotate by generating a new token from the signing service and updating the secret.
- **Rollback:** Edit the GitHub Release to mark it as draft (removes download links from the Releases page without deleting the release). Re-tag the previous good commit with a patch version (`v1.0.2`) to trigger a new build. Portable formats make rollback trivial — no installer database to corrupt.
- **Approval:** Releasing requires a human to push a version tag (`jj tag create v1.0.0`). CI builds and publishes automatically after that — no further human gate. All steps run with the default `GITHUB_TOKEN`; no elevated permissions needed.
- **Logs:** GitHub Actions workflow logs at `https://github.com/<owner>/Notes/actions`. `gh run view <run-id> --log-failed` reads only failed steps from a terminal. AppImage failures appear in the `dotnetpackager appimage` step; publish failures in the MSBuild output of the `dotnet publish` step.

## Risk Register

| Risk | Source | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| SmartScreen hard-blocks unsigned EXE; users assume malware | Devil's advocate | High | High | Apply for SignPath Foundation early (6-11 week lead time); document "More info → Run anyway" in README |
| No update signal — users stay on old versions | Devil's advocate | High | Medium | Plan GitHub Releases API version check (`GET /repos/<owner>/Notes/releases/latest`) for post-MVP |
| AppImage FUSE 2 missing on modern distros | Unknown unknowns | Medium | High | Document `--appimage-extract-and-run` fallback in README; test on Ubuntu 22.04+ and Fedora in CI |
| Windows Defender quarantines extracted SkiaSharp DLLs | Unknown unknowns | Medium | High | Test on fresh Windows VM with Defender before first release; fall back to folder ZIP if triggered |
| AppImage has no desktop integration by default | Devil's advocate | High | Low | Acceptable for technical user persona; document manual `.desktop` integration in README |
| Missing Windows EXE version metadata | Unknown unknowns | High | Low | Set `<AssemblyTitle>`, `<Product>`, `<FileVersion>`, `<Copyright>` in `.csproj` before first release |
| DotnetPackaging.Tool single-maintainer bus factor | Research finding | Low | Medium | Pin to a specific version in CI; MIT license allows forking if abandoned |

## Getting Started

1. Add version metadata to `Notes/Notes.csproj` before first release:
   ```xml
   <AssemblyTitle>Notes</AssemblyTitle>
   <Product>Notes</Product>
   <FileVersion>1.0.0</FileVersion>
   <Copyright>Copyright © 2026</Copyright>
   ```
2. Install packaging tool: `dotnet tool install --global DotnetPackaging.Tool`
3. Test Linux publish: `dotnet publish -c Release -r linux-x64 --self-contained -o ./publish/linux-x64`
4. Test AppImage creation: `dotnetpackager appimage --directory ./publish/linux-x64 --output ./Notes.AppImage --application-name "Notes" --summary "Markdown note-taking app"`
5. Run it: `chmod +x ./Notes.AppImage && ./Notes.AppImage`
6. Add `.github/workflows/release.yml` with the workflow from the CI/CD section above
7. Push a test tag to verify both artifacts appear on the GitHub Release

## Out of Scope

The following were not evaluated in this research:
- MSI installer packaging (dropped in favour of portable ZIP after anti-bias cross-check)
- Flatpak packaging (dropped in favour of AppImage)
- Auto-update mechanism (Velopack, GitHub Releases API polling — post-MVP)
- macOS packaging (DMG via Avalonia Parcel free tier — future consideration)
- Code signing pipeline configuration (deferred; options documented in Code Signing section)
- AUR packaging for Arch Linux (community-maintained PKGBUILD — future consideration)
- Windows Store / MSIX distribution
- Flathub submission
