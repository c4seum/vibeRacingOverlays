# Development

Technical notes for working on vibeRacingOverlays. For what the app does, see [README.md](README.md).

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git (and optionally the GitHub CLI `gh`)

## Local test version (DEV build)

```powershell
.\publish-dev.ps1          # -> .\publish, Start menu: "vibeRacingOverlays DEV"
```

Local builds are **DEV builds**. They are called "vibeRacingOverlays DEV" (orange title) and use their own settings folder, `%APPDATA%\vibeRacingOverlays-dev`. On first start that folder gets a copy of the release settings, so testing never changes the layouts of an installed release. Release builds pass `-p:ReleaseBuild=true`.

Command-line options for design and debugging:

- `vibeRacingOverlays.exe --snapshot <folder>`: renders every widget to PNG with the demo race (single class and multiclass), plus your saved widgets and a narrow-columns test.
- `--snapshot <folder> --live`: the same, with live iRacing data.
- `--dump <file>`: writes raw iRacing values (per car) to a text file.

## Releasing a new version

```powershell
git add -A
git commit -m "Describe the change"
git push                   # "Build" workflow checks that it still compiles, no release
git tag v1.0.1
git push origin v1.0.1     # "Release" workflow builds the installer + zip and publishes a GitHub Release
```

- Only a pushed tag in the form `vX.Y.Z` creates a release; a normal `git push` never does.
- The version number comes from the tag (`v1.0.1` -> 1.0.1), in the app, the installer and the file names.
- Last digit = fixes only, middle digit = new features.

To build the release files locally: `.\build-release.ps1 -Version 1.0.0` writes the installer and the portable zip to `dist\`.

The installer is per-user, needs no admin rights and runs as-is on Windows 10/11 (it's built on .NET Framework 4.8). The app itself is self-contained, with .NET 8 included.

- Unattended install: `--silent [--dir <folder>] [--no-desktop] [--launch]`.
- Uninstall: `uninstall.exe --uninstall [--quiet]`.

## Why it's light on FPS

- A background thread reads telemetry at 60 Hz. It publishes an immutable `RaceSnapshot` 20 times per second.
- Each widget draws into a `DisplayList`, a plain list of rectangles and texts.
- The overlay window only redraws when that list differs from the previous frame, and at most at the widget's own refresh rate (default 10 Hz, 4 Hz for fuel).
- The windows are click-through, don't take focus and have no taskbar entry.

## Structure

```
src/vibeRacingOverlays.Data      no UI
  IRSdk/                  shared-memory reader, lenient session-YAML parser, SessionInfo model
  Telemetry/              TelemetryState (one frame), IRacingSource, DemoSource
  Engine/                 RaceEngine: positions, live gaps (100 checkpoints/lap), relative,
                          pit/tow/stint tracking, iRating estimate, SOF, fuel, class naming
                          TelemetryService: background thread + source switching
  Model/                  RaceSnapshot, CarInfo, FuelInfo
src/vibeRacingOverlays.App       WPF
  Widgets/                Standings, Relative, Fuel (settings class + Draw)
  Rendering/              DisplayList, WpfRenderer, preview renderer, formatting
  Overlay/                OverlayWindow (transparent/topmost/click-through), OverlayManager (hotkeys, layouts, saving)
  UI/                     settings panel generated from [Setting] attributes (reset buttons, presets), preview, themes
src/vibeRacingOverlays.Setup     per-user installer (.NET Framework 4.8), app embedded as a resource
```

## Adding a widget

1. Create `XxxSettings : WidgetSettings` with `[Setting]` properties. The editor UI, the reset buttons and presets are generated from them.
2. Create `XxxWidget : Widget` and implement `Draw(DisplayList, RaceSnapshot)`.
3. Register both in `Widget.Create`, in `Widget.Catalog`, and with `[JsonDerivedType]` on `WidgetSettings`.
