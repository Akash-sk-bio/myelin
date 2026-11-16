# Contributing to Myelin

Thanks for helping! The project is a single .NET 10 WPF + WinForms app in `src/Myelin`.

## Build and test

```bash
dotnet build src/Myelin -c Release
```
```bash
src/Myelin/bin/Release/net10.0-windows/Myelin.exe --selftest selftest
```

The self-test renders the Tinted and Clear glass packs (full and quick), loads every `.cur`/`.ani` through the Win32 loader, and writes proof images (`cursor-sheet-*.png`, `looks.png`, `fx-gallery.png`). It must report `RESULT: all cursor files load through Win32`.

## Where things live

| File | What it does |
|---|---|
| `Neon.cs` | Vector mask → signed distance field → neon, fluid and Liquid Glass shading, plus rounding and shadows |
| `Art.cs` | All 17 pointer glyphs (32×32 design grid), animation frames, the logo glyph |
| `Pack.cs` | `.cur`/`.ani` writers, pack cache, per-user scheme install and restore, export with INF |
| `Effects.cs` | Effects engine (trails, clicks, extras) and the click-through overlay window |
| `MainWindow.xaml(.cs)`, `Theme.xaml`, `Controls.cs` | The one-page app UI, themes (`Look`), live stage |
| `Host.cs` | Entry point, CLI, tray, hotkeys, self-test |

## Guidelines

- Animated cursors may hold at most **4 images per frame**. The Windows ANI loader rejects 5.
- Keep effects cheap: the overlay only redraws the bounding box of live effects and idles otherwise.
- Run the self-test before opening a PR, and attach `fx-gallery.png` if you change effects.
