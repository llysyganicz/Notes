# App icon assets

- `app-icon.svg` — editable source of truth (256×256, no external refs). A closed notebook
  cover with a spine strip and a big block-letter "N" monogram, filling the square canvas
  edge-to-edge (small consistent margin). Gruvbox palette (cream cover, blue spine, dark-warm
  "N", dark bg0 backdrop), square corners.

  All shapes are original: two rectangles (stems) plus one polygon (the "N"'s diagonal
  stroke) whose corners exactly match the stems' corners, so the union renders as one
  seamless glyph with no visible joint. No third-party icon path data is used — an earlier
  design pass did incorporate Google Material Symbols path data, but the final design
  replaced all of it with original shapes, so no attribution is required.
- `app-icon-{16,32,48,128,256}.png` — raster set rendered from the SVG, embedded via
  `<AvaloniaResource>` for runtime use (`Window.Icon`, `avares://Notes/Assets/...`).
- `app-icon.ico` — multi-size Windows icon (16/32/48/256, PNG-compressed, Vista+ format)
  built from the same PNGs, referenced by `<ApplicationIcon>` in `Notes.csproj`.

## Regenerating the raster set

The PNGs and the `.ico` are derived artifacts — edit `app-icon.svg` and regenerate both from
it, don't hand-edit the rasters.

Rendering was done with a throwaway console app referencing the `Svg.Skia` NuGet package
(SVG → `SKPicture` → `SKBitmap` at each target size) plus `SkiaSharp.NativeAssets.Linux` for
the native Skia runtime. The `.ico` is a hand-assembled Vista+ ICO container (`ICONDIR` +
`ICONDIRENTRY[]` + raw PNG bytes for the 16/32/48/256 frames) built from the same PNGs — no
BMP/DIB encoding involved, since Windows Vista+ readers accept embedded PNG frames directly.

Equivalent one-off regeneration steps:

```sh
mkdir /tmp/icon-gen && cd /tmp/icon-gen
dotnet new console
dotnet add package Svg.Skia --version 1.0.0.19
dotnet add package SkiaSharp.NativeAssets.Linux --version 2.88.9   # or .macOS / .Win32 on other hosts
# Program.cs: load app-icon.svg via SKSvg, draw each size's SKPicture into an SKBitmap,
# encode to PNG; then concatenate the 16/32/48/256 PNGs into a manually-built ICO
# (ICONDIR header + one ICONDIRENTRY per frame + raw PNG bytes).
dotnet run -- /path/to/Notes/Assets/app-icon.svg /path/to/Notes/Assets
```

Any SVG rasterizer that preserves the viewBox and emits RGBA PNGs at the exact target sizes
works equally well (e.g. `rsvg-convert -w N -h N`, Inkscape's CLI export, or a headless
browser). The `.ico` only needs the four PNG byte blobs concatenated behind the header above.
