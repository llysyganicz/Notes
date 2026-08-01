# Gruvbox Theme, App Icon, and Tree-Row Context Menu Implementation Plan

## Overview

Ship the post-MVP "Theme & Identity" UX slice. Three deliverables land together: (1) a hand-written gruvbox control-theme library — light + dark variants, system-following — that **replaces** `FluentTheme` entirely and covers every control the app uses plus the AvaloniaEdit markdown editor and the Markdown.Avalonia preview; (2) a single gruvbox notebook-and-pen application icon wired into the build and window chrome; (3) a `NoteTreeView` row hit-area fix so the context menu opens from the whole row, with an automated E2E test. No theme switcher UI; the app keeps following the system setting (`RequestedThemeVariant="Default"`).

## Current State Analysis

The styling surface today is minimal and Fluent-default:

- `Notes/App.axaml` loads `<FluentTheme />` plus `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml`, and sets `RequestedThemeVariant="Default"`. There are **zero** theme resource overrides, no `Styles` blocks, no `Themes/` or `Assets/` directories.
- The full control surface enumerated from every `.axaml` view and dialog: `Window` (chrome/titlebar), `Button`, `TextBox`, `TextBlock` (styled-only), `Menu`/`MenuItem`/`Separator`, `TreeView`/`TreeViewItem`, `ListBox`/`ListBoxItem`, `ContextMenu`, `ScrollViewer`/`ScrollBar` (implicit, used by every scrollable list), `GridSplitter`, `NumericUpDown`, `DatePicker`, `ComboBox`/`ComboBoxItem`, `CheckBox`. ~14 control templates + scrollbar + window chrome.
- Markdown syntax highlighting: `Notes/Views/NoteEditorView.axaml.cs:18` sets `Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("MarkDown")` — the **built-in** AvaloniaEdit markdown definition with Fluent-aligned colors. To gruvbox-ify the editor this assignment must point at a custom `IHighlightingDefinition`.
- Markdown preview: `Notes/Views/NoteEditorView.axaml:24` uses `md:MarkdownScrollViewer Markdown="{Binding PreviewText}"` (Markdown.Avalonia `12.0.0-a3`). It renders to standard Avalonia controls (Border/Grid/TextBlock); styling is via `Styles` / `OverrideRootStyle`, not C#.
- `Notes/Notes.csproj` has **no** `<ApplicationIcon>`, no `<AvaloniaResource>` asset entries, and no `Assets/` folder. `MainWindow.axaml` sets no `Icon`.
- `Notes/Views/NoteTreeView.axaml` (the context-menu defect) attaches `ContextMenu` to the **inner `TextBlock`** inside `TreeDataTemplate`; right-clicking empty row space outside the text glyphs does nothing. The menu's two commands (`NewFolderCommand`, `DeleteNoteCommand`) route via `$parent[TreeView].((vm:NoteTreeViewModel)DataContext).…` and are correct — only the hit area is wrong.
- `Notes.E2ETests/E2ETestBase.cs` already runs the real `MainWindow` headless via `Avalonia.Headless.XUnit` and can drive real controls (`TreeView.SelectedItem`, button clicks, text-entry), so a right-click→menu-open test is automatable here.
- VCS is jujutsu (`jj`); build/test via `dotnet build` / `dotnet test`.

### Key Discoveries:

- `Notes/App.axaml:13-14` (`<FluentTheme />` + AvaloniaEdit Fluent `StyleInclude`) is the single seam to remove; everything gruvbox must be authored in-`Notes`.
- The Fluent theme is packaged entirely inside `Avalonia.Themes.Fluent.dll` (`~/.nuget/packages/avalonia.themes.fluent/12.0.3/lib/...`) — no loose XAML to fork. "Replace FluentTheme" = hand-write gruvbox `ControlTheme`s in `Notes`.
- `NoteEditorView.axaml.cs:18` is the single line that selects editor highlighting; swapping the definition there re-colors the whole editor.
- `Notes/ViewModels/NoteTreeViewModel.cs` already exposes `NewFolderCommand` and `DeleteNoteCommand` accepting a `NoteTreeNode?` parameter — the context-menu fix needs **no ViewModel change**, only an AXAML restructure.
- `Notes.E2ETests/E2ETestBase.cs:SelectTreeItemAsync` already proves `TreeView` two-way binding is drivable headless; the same mechanism + `PointerPressed` right-button event supports the new test.

## Desired End State

- Running `dotnet run --project Notes` launches the app rendered entirely in gruvbox (original palette). Light and dark variants follow the OS theme automatically; toggling the OS theme swaps all chrome, controls, editor syntax colors, and the markdown preview cohesively.
- Every control the app uses — menus, dialogs, tree, search pane, editor, preview, scrollbars, window title bar — looks unambiguously gruvbox on both light and dark; no Fluent/default-colored control leaks through.
- The OS shows the notebook-and-pen gruvbox icon for the `Notes` window (title bar + taskbar on Linux/Windows) and for the published `Notes` binary (`.exe`/app bundle). The icon reads legibly on light and dark desktop backgrounds.
- Right-clicking anywhere on a tree-row — not only on the file/folder name text — opens the row's context menu with the existing New Folder / Delete actions; an automated E2E test asserts this.

### Verification

- `dotnet build` and `dotnet test` pass (Notes.Core.Tests + Notes.Tests headless).
- `Notes.E2ETests` green, including the new right-click context-menu test.
- Manual: on Linux (light + dark) and Windows (light + dark), the chrome, all controls, editor syntax, and markdown preview are cohesive gruvbox; the icon is legible; the context menu opens from the whole row.

## What We're NOT Doing

- **No theme switcher UI** — the app keeps `RequestedThemeVariant="Default"` (system). Switcher / manual override deferred.
- **No editor behavior changes** — no editing UX, autosave, or rendering changes. Theming the editor's syntax colors is in scope; the editor control's behavior is untouched.
- **No `.desktop` / Linux packaging**, installers, or per-OS icon bundling beyond what `<ApplicationIcon>` and `Window.Icon` already provide cross-platform. Full packaging is its own future slice.
- **No Markdown.Avalonia replacement** — the preview control stays; only its styling changes.
- **No `Notes.Core` change** — theming is entirely an Avalonia-layer concern; `Notes.Core` stays platform-agnostic and untouched.

## Implementation Approach

Author gruvbox as a set of `ControlTheme`s and palette resources in `Notes`, loaded as `Application.Styles`/`Application.Resources` after removing `FluentTheme`.

**Pre-flight (before any authoring):** search NuGet for an existing, maintained Avalonia 12 gruvbox theme package (control themes + AvaloniaEdit highlighting, light and dark variants). Candidates to evaluate include gruvbox-themed Avalonia theme libraries and gruvbox syntax-highlighting definitions for AvaloniaEdit. If a package covers the full control surface this slice needs and supports `ThemeVariant.Light`/`Dark` via `ThemeDictionaries` (or an equivalent auto-switching mechanism), adopt it as a `StyleInclude`/`AvaloniaResource` reference and skip the hand-written `ControlTheme` authoring below — record the chosen package and version in `change.md`. Only if no suitable package exists (or the closest one forces Fluent, misses variants, or covers too few controls) proceed with the hand-written library described here. Use Avalonia 12's `ThemeDictionaries` keyed to `ThemeVariant.Light` / `ThemeVariant.Dark` so the system setting auto-selects, matching the existing `RequestedThemeVariant="Default"`. Each control gets one `ControlTheme` whose setters reference `DynamicResource` gruvbox brushes — so flipping the OS theme re-resolves without restart.

The AvaloniaEdit markdown definition is replaced with a custom `IHighlightingDefinition` (built from an embedded `.xshd` resource or constructed in code) whose rule colors are gruvbox token classes; `NoteEditorView.axaml.cs:18` is updated to load it instead of the built-in. The AvaloniaEdit control chrome itself (line-number margin, selection, caret) is themed alongside other controls via the new `ControlTheme`s for `TextEditor` / its templates, dropping the `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml` include.

The markdown preview is re-styled via a `Styles` block on `MarkdownScrollViewer`'s descendant elements (headings, code borders, links, blockquotes, tables), keyed to the same gruvbox brushes and `ThemeVariant`.

The app icon is a single editable `Assets/app-icon.svg` source; a raster set (PNG 16/32/48/128/256 + a multi-size `app-icon.ico`) is rendered into `Assets/` and embedded via `<AvaloniaResource>`; `<ApplicationIcon>` points at the `.ico`, and `MainWindow` sets `Icon` from a runtime-loaded `Bitmap` asset.

The tree-row context-menu fix restructures `NoteTreeView.axaml`'s `TreeDataTemplate` so the `ContextMenu` lives on a horizontally-stretched container wrapping the `TextBlock`, covering the full row hit area. No ViewModel change. An E2E test right-clicks a row and asserts the `ContextMenu` is open.

## Critical Implementation Details

- **`ThemeDictionaries` + `DynamicResource` is load-bearing.** Every control theme setter must `DynamicResource` the gruvbox brushes (declared in `ThemeDictionaries` per `ThemeVariant`), not hard-code colors, or the light/dark swap-on-OS-toggle won't re-resolve without an app restart. `StaticResource` would freeze the variant picked at load.
- **Avalonia 12 uses `ControlTheme`, not `<Style>` selectors, for templated controls.** Each themed control needs an explicit `ControlTheme` with `x:Key="{x:Type Button}"` (etc.) so it's applied by type. Styled-only controls (`TextBlock`) can use `Style` selectors against the gruvbox brushes.
- **Replace the AvaloniaEdit Fluent include, don't keep it.** `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml` ships Fluent-colored templates; leaving it in would leak Fluent chrome behind the gruvbox `TextEditor` theme. Remove the `StyleInclude` and provide a gruvbox `TextEditor` control theme (or a minimal `Styles` override covering the editor chrome).
- **Icon must be raster `.ico`/PNG for cross-platform runtime use.** `Window.Icon` loads a `Bitmap` from an `AvaloniaResource` PNG; `<ApplicationIcon>` (csproj) wants a `.ico` for the Windows binary/taskbar. Keep `Assets/app-icon.svg` as the editable source of truth; regenerate the raster set into `Assets/` and embed both. Don't ship the SVG to the runtime icon path — Avalonia's `Bitmap` doesn't render SVG for `Window.Icon`.
- **Context-menu event flow under headless test.** `Avalonia.Headless.XUnit` dispatches `PointerPressed`/`PointerReleased` with `RightButton` on a target control. The test must aim at the stretched row container (or the row's `TreeViewItem`), not the inner `TextBlock`, and assert the `ContextMenu` becomes the open popup — mirroring how `E2ETestBase.FindControl` walks the visual tree in existing tests.

## Phase 1: Gruvbox Palette + Control-Theme Library (Replace FluentTheme)

### Overview

Define the original gruvbox color/brush palette in `ThemeDictionaries` for light and dark, author gruvbox `ControlTheme`s for every control the app uses, and swap them in by removing `<FluentTheme />` and the AvaloniaEdit Fluent include from `App.axaml`.

### Changes Required:

#### 1. Gruvbox palette resource dictionary

**File**: `Notes/Themes/GruvboxPalette.axaml`

**Intent**: Single source of truth for the original gruvbox colors, declared as `ThemeDictionaries` keyed to `ThemeVariant.Light` and `ThemeVariant.Dark` so the active variant auto-selects from `RequestedThemeVariant="Default"`.

**Contract**: A `ResourceDictionary` exposing named `IBrush`/`Color` resources for the original gruvbox neutrals (dark bg `#282828`/fg `#ebdbb2`, light bg `#fbf1c7`/fg `#3c3836`, plus `bg1`/`bg2`/`fg1`/`fg2`/`gray` ramps) and all accent roles (`red #cc241d`, `green #98971a`, `yellow #d79921`, `blue #458588`, `purple #b16286`, `aqua #689d6a`, `orange #d65d0e`, plus brighter variants `#fb4934`/`#b8bb26`/`#fabd2f`/`#83a598`/`#d3869b`/`#8ec07c`/`#fe8019` for dark-mode text). Each variant-pair key resolved at runtime via `DynamicResource`. Also expose unprefixed semantic aliases (`Accent`, `Foreground`, `Background`, `ControlBackground`, `ControlBorder`, `LowContrast`, etc.) that the control themes reference — these map to the raw gruvbox names differently per variant so light/dark flip together. The aliases carry no `Gruvbox` prefix: they live scoped inside `GruvboxPalette.axaml` and are consumed only by this theme's control themes, so the dictionary context already disambiguates them and the style name need not be repeated in every key.

#### 2. Control themes for chrome & buttons

**File**: `Notes/Themes/GruvboxControls.axaml` (split into per-control files if it grows large)

**Intent**: Hand-written `ControlTheme`s replacing Fluent templates for the chrome and action controls, using the palette via `DynamicResource`.

**Contract**: `ControlTheme`s with `x:Key="{x:Type …}"` for at minimum: `Window` (ExtendClientAreaToDecorationsHint titlebar / transparency handled by gruvbox background), `Button` (default + `:pointerover`/`:pressed` states), `RepeatButton` (scrollbar arrows reuse), `TextBlock` via `Style` selectors for default foreground. Setters reference `ControlBackground`, `ControlBorder`, `Accent` etc. from the phase palette. No hard-coded hex.

#### 3. Control themes for input controls

**File**: `Notes/Themes/GruvboxControls.axaml` (continued)

**Intent**: Gruvbox themes for text-entry and selection controls so dialogs and the search pane read gruvbox.

**Contract**: `ControlTheme`s for `TextBox` (caret, selection highlight, `:focused` border), `NumericUpDown`, `DatePicker`, `ComboBox`/`ComboBoxItem`, `CheckBox` (check glyph + border states). Each `:focused`/`:pointerover`/`:pressed`/`:checked` setter `DynamicResource`s the palette so the variant flip propagates.

#### 4. Control themes for lists & menus

**File**: `Notes/Themes/GruvboxControls.axaml` (continued)

**Intent**: Gruvbox themes for the tree, search results, menus, and the context menu so the primary navigation surfaces are gruvbox.

**Contract**: `ControlTheme`s for `TreeView`/`TreeViewItem` (selection, hover, expansion arrow), `ListBox`/`ListBoxItem` (selection + hover), `Menu`/`MenuItem`/`Separator` (popup chrome + highlight), `ContextMenu` (matches `MenuItem`), and `ScrollViewer`/`ScrollBar` (thumbs, track, arrows) since every scrollable list depends on them. Setters reference palette semantic aliases.

#### 5. Control themes for layout chrome

**File**: `Notes/Themes/GruvboxControls.axaml` (continued)

**Intent**: Gruvbox treatment for the structural chrome (`GridSplitter`, dialog `Window` backgrounds) so no Fluent-default surface remains.

**Contract**: `ControlTheme` for `GridSplitter` (gruvbox neutral thumb). Dialog windows (`ConfirmDialog`, `NewNoteDialog`, `TemplatePickerDialog`, `TemplateFormDialog`) inherit the `Window` theme; no per-dialog override needed unless a Fluent leak is found during manual verification.

#### 6. Swap themes in App.axaml

**File**: `Notes/App.axaml`

**Intent**: Remove Fluent and wire gruvbox as the active styling, keeping `RequestedThemeVariant="Default"`.

**Contract**: Remove `<FluentTheme />` and `<StyleInclude Source="avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml" />`. The AvaloniaEdit Fluent include reappears in Phase 2 as a gruvbox replacement. Add `<StyleInclude Source="avares://Notes/Themes/GruvboxPalette.axaml" />` and `<StyleInclude Source="avares://Notes/Themes/GruvboxControls.axaml" />` (or merge palette into `Application.Resources` `MergedDictionaries`). `RequestedThemeVariant="Default"` unchanged. **Addendum (Phase 1 review 2026-08-01):** a minimal gruvbox `TextEditor`/`TextArea` chrome theme (`Notes/Themes/Controls/Editor.axaml`) landed in this phase so the editor renders once the Fluent include is removed; the full AvaloniaEdit syntax-color theming remains a Phase 2 deliverable.

#### 7. csproj asset includes

**File**: `Notes/Notes.csproj`

**Intent**: Ensure the new `Themes/` `.axaml` files are compiled assets available via `avares://Notes/...`.

**Contract**: Add `<AvaloniaResource Include="Themes\GruvboxPalette.axaml" />` and `<AvaloniaResource Include="Themes\GruvboxControls.axaml" />` resource entries (or rely on the default glob — verify the default `AvaloniaResource` glob picks up `Themes/**`). No new package references; control themes use only built-in Avalonia 12 APIs.

### Success Criteria:

#### Automated Verification:

- `dotnet build` passes for all four projects.
- `dotnet test` passes (Notes.Core.Tests + Notes.Tests); no existing test regresses (the suite is headless; it does not assert colors, so a fully-restyled chrome must still render and not throw).
- `dotnet run --project Notes` launches without an XAML/asset-load exception and shows the main window.

#### Manual Verification:

- On Linux (light + dark) and Windows (light + dark): main window chrome, menu bar, menu popups, search pane, split bar, and the (still Fluent-default editor) render in gruvbox neutrals + accents with no stray Fluent-colored control.
- Toggling the OS theme while the app is running re-resolves chrome to the other variant without restart.
- All dialogs (New Note, Confirm, Template Picker, Template Form) render gruvbox.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Editor & Preview Theming

### Overview

Make the two content surfaces — the AvaloniaEdit markdown editor and the Markdown.Avalonia preview — gruvbox. Covers syntax-highlighting colors (editor) and rendered-markdown styles (preview), both variant-aware.

### Changes Required:

#### 1. Gruvbox AvaloniaEdit control theme

**File**: `Notes/Themes/GruvboxControls.axaml` (or a dedicated `Notes/Themes/GruvboxAvaloniaEdit.axaml`)

**Intent**: Give the `TextEditor` chrome (line-number margin, selection, caret, background) gruvbox colors, replacing what the removed `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml` provided.

**Contract**: A `ControlTheme` (or `Styles` overrides) for `edit:TextEditor` / its template parts setting `Background`→`Background`, `Foreground`→`Foreground`, line-number foreground, selection background (a translucent `Accent`), caret color, all via `DynamicResource`. Loaded from `App.axaml` instead of the Fluent `AvaloniaEdit.xaml`. Before authoring this by hand, check NuGet for an existing gruvbox AvaloniaEdit highlighting/theme package (see the Phase 1 pre-flight step); if a maintained one covering both light and dark variants exists, adopt it and skip the hand-written definition.

#### 2. Gruvbox markdown highlighting definition

**File**: `Notes/Themes/GruvboxMarkdownHighlighting.xshd` (embedded `AvaloniaResource`) **and** `Notes/Services/GruvboxHighlightingLoader.cs`

**Intent**: Provide an `IHighlightingDefinition` whose markdown token colors are gruvbox, so `Editor.SyntaxHighlighting` is gruvbox instead of the built-in Fluent-aligned "MarkDown" definition.

**Contract**: A `.xshd` (AvaloniaEdit syntax-highlighting XML) resource modeled on the built-in MarkDown definition's rule sets (headings, bold/italic, inline code, fenced code spans, links, lists, block quotes, frontmatter) but with `<Color>` elements pointing at gruvbox accent roles. `GruvboxHighlightingLoader` loads it via `HighlightingLoader.Load(...)` from the embedded `AvaloniaResource` stream and registers/returns the definition. Colors are resolved at load time against the active `ThemeVariant` — re-load on variant change if a running-app toggle is required (verify during testing whether a static definition suffices for the dark/light flip).

#### 3. Wire the custom definition into the editor

**File**: `Notes/Views/NoteEditorView.axaml.cs`

**Intent**: Stop using the built-in "MarkDown" definition and use the gruvbox one.

**Contract**: Replace `Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("MarkDown");` (`Notes/Views/NoteEditorView.axaml.cs:18`) with a load of the gruvbox definition (DI-injected `GruvboxHighlightingLoader` or a static accessor). If variant-dependent reloading is needed, hook `Application.ActualThemeVariant` change to re-apply.

#### 4. Gruvbox markdown preview styles

**File**: `Notes/Themes/GruvboxMarkdownPreview.axaml` (loaded via `App.axaml` `StyleInclude`) **or** a `Styles` block on `NoteEditorView.axaml`

**Intent**: Theme the rendered markdown preview (headings, paragraphs, code blocks, inline code, links, blockquotes, horizontal rules, tables, lists) in gruvbox, variant-aware.

**Contract**: `Style` selectors targeting `MarkdownScrollViewer` descendant elements (`md|MarkdownScrollViewer TextBlock[class^=H]`, `Border` for code blocks, `TextBlock` for inline code, `Hyperlink`/`Button` for links, `Blockquote` border brushes). Setters `DynamicResource` the palette. Loaded alongside the other theme dictionaries in `App.axaml`.

### Success Criteria:

#### Automated Verification:

- `dotnet build` passes.
- `dotnet test` and `Notes.E2ETests` pass; the editor/preview E2E flows (open note, type, toggle preview) still work with the new highlighting and preview styles.

#### Manual Verification:

- On Linux (light + dark) and Windows (light + dark): markdown syntax tokens in the editor (headings, bold, inline code, fenced code, links, lists, blockquotes, frontmatter) use gruvbox accent colors against the gruvbox background; the caret, selection, and line numbers are gruvbox.
- The rendered preview renderers headings, code blocks, inline code, links, blockquotes, tables, and lists in gruvbox, cohesive with the editor.
- Toggling the OS theme re-resolves editor syntax + preview colors (with app running) or on relaunch (if static definition) — confirm which during testing.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: App Icon

### Overview

Add a gruvbox notebook-and-pen application icon readable on light and dark backgrounds, wire it into the build (`<ApplicationIcon>`) and the `MainWindow` runtime chrome (`Icon`).

### Changes Required:

#### 1. Editable SVG source

**File**: `Notes/Assets/app-icon.svg`

**Intent**: Single editable design source for the icon (notebook + pen nib, gruvbox yellow/orange on a rounded dark-neutral panel), the artifact a maintainer edits to change the icon.

**Contract**: A self-contained SVG (no external refs), drawn at 256×256 with the gruvbox-original palette. Readable at 16px (test by downscaling). Not shipped to the runtime `Window.Icon` path — Avalonia's `Bitmap` doesn't render SVG — it's the source for regenerating the raster set.

#### 2. Raster asset set

**Files**: `Notes/Assets/app-icon-{16,32,48,128,256}.png` and `Notes/Assets/app-icon.ico`

**Intent**: Cross-platform runtime + build-surface icons derived from the SVG.

**Contract**: PNGs at the named sizes (taskbar 16/32, file-manager 48/128, large 256) plus a multi-size Windows `.ico` containing 16/32/48/256. Embedded in the assembly as `AvaloniaResource`. Add a one-time render note (tool used) in the change folder so the set is reproducible.

#### 3. csproj ApplicationIcon + asset includes

**File**: `Notes/Notes.csproj`

**Intent**: Set the Windows binary/taskbar icon and make the PNGs embeddable.

**Contract**: Add `<ApplicationIcon>Assets/app-icon.ico</ApplicationIcon>` to the main `<PropertyGroup>` (Windows exe + taskbar). Add `<AvaloniaResource Include="Assets\app-icon-256.png" />` (or rely on default `AvaloniaResource` glob for `Assets/**`) so the runtime PNG is loadable via `AssetLoader`.

#### 4. MainWindow runtime icon

**File**: `Notes/MainWindow.axaml` (or `MainWindow.axaml.cs` if a code-setter is cleaner)

**Intent**: Give the running window its gruvbox icon in the title bar and taskbar at runtime.

**Contract**: Set `Icon` to a `Bitmap` loaded from `avares://Notes/Assets/app-icon-256.png` (via `AssetLoader`/`Bitmap`), e.g. `Icon="avares://Notes/Assets/app-icon-256.png"` or a code-behind `new WindowIcon(AssetLoader.Open(...))`.

### Success Criteria:

#### Automated Verification:

- `dotnet build` passes; the `.ico`/PNGs compile into the output without resource errors.
- `dotnet test` and `Notes.E2ETests` still pass (icon-loading must not throw during window construction, which is exercised by `E2ETestBase.InitializeAsync`).

#### Manual Verification:

- On Linux: the taskbar/dock and the window title bar show the gruvbox notebook-and-pen icon.
- On Windows: the `.exe` in Explorer, the taskbar, and the title bar show the icon.
- The icon is legible on a light desktop background and a dark one.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Tree-Row Context Menu Fix

### Overview

Make the tree view's context menu open on a right-click anywhere on the row, not only on the file/folder name text. Add an automated E2E test.

### Changes Required:

#### 1. Strengthen the row hit area

**File**: `Notes/Views/NoteTreeView.axaml`

**Intent**: Move the `ContextMenu` from the inner `TextBlock` to a stretched container so the whole row opens it.

**Contract**: In the `TreeDataTemplate`, wrap the existing `TextBlock Text="{Binding Name}"` in a container (e.g. a `Border` or `Panel`) with `HorizontalAlignment="Stretch"` carrying the `ContextMenu`. The two `MenuItem`s and their `$parent[TreeView].((vm:NoteTreeViewModel)DataContext).…` command bindings are unchanged — only the owner element changes. Ensure the `TreeViewItem` row template fills width so the stretch container covers the empty area (verify against the Phase 1 `TreeViewItem` theme; add `HorizontalAlignment="Stretch"` to the row as needed).

#### 2. E2E test: right-click opens the menu

**File**: `Notes.E2ETests/TreeViewContextMenuTests.cs` (new)

**Intent**: Lock in the row-hit-area behavior so a regression back to the inner-`TextBlock` attachment is caught.

**Contract**: A new `[AvaloniaFact]` test following `E2ETestBase`. Set up a workspace with at least one note (reuse the harness pattern from `CreateNewNoteTests`/`PlaceholderTests`). Find the `TreeView` and a row `TreeViewItem` via the visual-tree walk helpers; raise a `PointerPressed`/`PointerReleased` with `RightButton` aimed at the row's container (not the `TextBlock`). Assert a `ContextMenu` is now open (e.g. the topmost active popup is a `ContextMenu`, or the menu's `MenuItem` count matches). Use `Dispatcher.UIThread.RunJobs()` / `WaitForConditionAsync` as existing tests do.

### Success Criteria:

#### Automated Verification:

- `dotnet build` passes.
- `dotnet test` passes; the new `TreeViewContextMenuTests` test passes and existing `Notes.E2ETests`/`Notes.Tests` pass.
- `dotnet run --project Notes` launches with no regression in tree behavior (left-click select still works; right-click still opens the menu on the whole row).

#### Manual Verification:

- Right-clicking the empty area to the right of a folder/note name in the tree opens the context menu with New Folder + Delete.
- The menu's New Folder and Delete actions still work as before (no behavior change beyond the hit area).

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation that the manual testing was successful before considering the slice complete.

---

## Testing Strategy

### Unit Tests:

- No `Notes.Core` logic changes — `Notes.Core.Tests` is unchanged and continues to pass.

### Integration / E2E Tests:

- `Notes.E2ETests` (headless Avalonia): existing suites (`SmokeTests`, `CreateNewNoteTests`, `EditAndAutoSaveTests`, `PlaceholderTests`) keep passing — proving the restyled/re-iconed app still constructs and operates.
- New `TreeViewContextMenuTests`: right-click a tree row → assert `ContextMenu` opens (Phase 4 deliverable).

### Manual Testing Steps:

1. `dotnet run --project Notes` on Linux under a **light** system theme: verify all chrome, controls, editor syntax, and markdown preview are gruvbox; the icon is legible; right-clicking any tree row opens the context menu.
2. Switch the system to **dark** while the app is running: verify chrome/controls/preview re-resolve to dark gruvbox; verify editor syntax re-resolves or note the relaunch requirement.
3. Relaunch under **dark**: verify everything is dark gruvbox from cold start.
4. Repeat 1–3 on Windows (light + dark).
5. Open each dialog (New Note, Confirm, Template Picker, Template Form) and verify gruvbox styling under both variants.
6. Verify the app icon shows in the window title bar, taskbar/dock, and (Windows) Explorer `.exe`.

## Performance Considerations

- `DynamicResource`-based theming is the standard Avalonia path; variant flips re-resolve a bounded brush set — no measurable cost expected.
- The gruvbox AvaloniaEdit highlighting definition replaces a built-in one with a comparable rule count; no editor perf regression expected. Verify typing latency is unchanged during manual testing.

## Migration Notes

- No on-disk data migration — theming and icon are pure presentation.
- Users with prior builds installed: the Windows `.exe` icon will change on next install (no in-place migration needed).

## References

- Theme seam: `Notes/App.axaml:5,13-15` (`RequestedThemeVariant`, `<FluentTheme />`, AvaloniaEdit Fluent include)
- Editor highlighting selection: `Notes/Views/NoteEditorView.axaml.cs:18`
- Preview control: `Notes/Views/NoteEditorView.axaml:24`
- Tree context-menu defect: `Notes/Views/NoteTreeView.axaml` (inner `TextBlock.ContextMenu`)
- Tree-row command routing (unchanged): `Notes/ViewModels/NoteTreeViewModel.cs` (`NewFolderCommand`, `DeleteNoteCommand`)
- csproj (no icon/assets today): `Notes/Notes.csproj`
- E2E harness (right-click test pattern): `Notes.E2ETests/E2ETestBase.cs`
- Change identity: `context/changes/gruvbox-theme/change.md`
- Lessons priors: `context/foundation/lessons.md` (CTS dispose rule — not touched here; no async-CTS code in this slice)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Gruvbox Palette + Control-Theme Library (Replace FluentTheme)

#### Automated

- [x] 1.0 Pre-flight: NuGet search for an existing gruvbox Avalonia theme/AvaloniaEdit package recorded in `change.md` (adopt or justify hand-writing) — 995eb8c
- [x] 1.1 `dotnet build` passes for all four projects — 995eb8c
- [x] 1.2 `dotnet test` passes (Notes.Core.Tests + Notes.Tests) — 995eb8c
- [x] 1.3 `dotnet run --project Notes` launches with no XAML/asset-load exception — 995eb8c

#### Manual

- [x] 1.4 Chrome, menu, search pane, split bar render gruvbox on Linux light + dark — 995eb8c
- [x] 1.5 Same controls render gruvbox on Windows light + dark — 995eb8c
- [x] 1.6 Toggling OS theme re-resolves chrome to the other variant without restart — 995eb8c
- [x] 1.7 All dialogs (New Note, Confirm, Template Picker, Template Form) render gruvbox — 995eb8c

### Phase 2: Editor & Preview Theming

#### Automated

- [x] 2.1 `dotnet build` passes — ed7f53a
- [x] 2.2 `dotnet test` + `Notes.E2ETests` pass (editor/preview flows intact) — ed7f53a

#### Manual

- [x] 2.3 Editor markdown syntax tokens use gruvbox accents on Linux light + dark
- [x] 2.4 Editor syntax tokens use gruvbox accents on Windows light + dark
- [x] 2.5 Caret, selection, line numbers are gruvbox
- [x] 2.6 Rendered preview (headings, code, links, blockquotes, tables, lists) is gruvbox
- [x] 2.7 OS-theme toggle re-resolves editor + preview colors (confirm running-app vs relaunch)

### Phase 3: App Icon

#### Automated

- [x] 3.1 `dotnet build` passes (`.ico`/PNGs compile without resource errors) — 5270397
- [x] 3.2 `dotnet test` + `Notes.E2ETests` pass (icon load does not throw during window construction) — 5270397

#### Manual

- [x] 3.3 Linux taskbar/dock + window title bar show the gruvbox icon — 5270397
- [x] 3.4 Windows `.exe` in Explorer, taskbar, and title bar show the icon — 5270397
- [x] 3.5 Icon is legible on a light desktop background and a dark one — 5270397

### Phase 4: Tree-Row Context Menu Fix

#### Automated

- [x] 4.1 `dotnet build` passes
- [x] 4.2 `dotnet test` passes (new `TreeViewContextMenuTests` + existing suites green)
- [x] 4.3 `dotnet run --project Notes` launches with no tree regression

#### Manual

- [x] 4.4 Right-clicking the empty area to the right of a folder/note name opens the context menu
- [x] 4.5 New Folder and Delete actions from the menu still behave as before