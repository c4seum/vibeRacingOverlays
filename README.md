# vibeRacingOverlays

vibecoding easy to run iRacing overlays.

A lightweight overlay app that reads iRacing's shared memory directly, without SimHub.

## Download

Get the latest installer or portable zip from the [Releases](../../releases) page.

## Development

```powershell
.\publish-dev.ps1          # local test version -> .\publish, Start menu: "vibeRacingOverlays DEV"
```

Local builds are **DEV builds**. They are called "vibeRacingOverlays DEV" (orange title) and use their own settings folder, `%APPDATA%\vibeRacingOverlays-dev`. On first start that folder gets a copy of the release settings, so testing never changes the layouts of the installed release. Release builds (`build-release.ps1` / GitHub Actions) pass `-p:ReleaseBuild=true`.

## Releasing a new version

```powershell
git add -A
git commit -m "Describe the change"
git push                   # "Build" workflow checks that it still compiles
git tag v1.0.1
git push origin v1.0.1     # "Release" workflow builds the installer + zip and publishes a GitHub Release
```

The version number comes from the tag (`v1.0.1` -> 1.0.1), in the app, the installer and the file names.

## Using the app

- **Data:** `Auto` uses iRacing when it's running and otherwise a built-in demo race. `iRacing` and `Demo` force one source.
- **Widgets:** Standings, Relative and Fuel calculator. Each widget you add gets its own on-screen window. Renaming one only changes its display name; its type stays the same.
- **Edit layout** (`Ctrl+Shift+E`): drag a widget to move it, and use the mouse wheel over it to scale it.
- **Show/hide all widgets:** `Ctrl+Shift+H`.
- **Layouts:** a layout is a named set of widgets with their positions and settings. `New` creates a layout with one widget of each type, `Save as` copies the current one under a new name, `Rename` and `Delete` manage them. Changes are saved automatically in the active layout. Next layout: `Ctrl+Shift+L`, which also works in-game.
- **Settings:** stored in `%APPDATA%\vibeRacingOverlays\settings.json` (DEV builds: `%APPDATA%\vibeRacingOverlays-dev`).

To write design previews with demo data to PNG files: `vibeRacingOverlays.exe --snapshot <folder>`.

## Sharing / release

```powershell
.\build-release.ps1 -Version 1.0.0
```

This creates two files in `dist\`:

- **`vibeRacingOverlays-Setup-<version>.exe`**: the installer. It installs for the current Windows user only, so no admin rights are needed. It creates a Start menu shortcut and, optionally, a desktop shortcut, and adds an entry to Windows "Apps & features" so the app can be uninstalled normally. The installer runs on Windows 10/11 as-is (it uses .NET Framework 4.8, which ships with Windows).
  - Unattended install: `--silent [--dir <folder>] [--no-desktop] [--launch]`.
  - Uninstall: from Apps & features, or `uninstall.exe --uninstall [--quiet]`. User settings are kept unless you choose to remove them.
- **`vibeRacingOverlays-<version>-portable.zip`**: a single `vibeRacingOverlays.exe` plus a README. It runs without installing.

The app is self-contained: .NET 8 is included, so users don't need to install anything. It isn't code-signed, so Windows SmartScreen may warn on first start ("More info" > "Run anyway").

## Why it's light on FPS

- A background thread reads telemetry at 60 Hz. It publishes an immutable `RaceSnapshot` 20 times per second.
- Each widget draws into a `DisplayList`, a plain list of rectangles and texts.
- The overlay window only redraws when that list differs from the previous frame, and at most at the widget's own refresh rate (default 10 Hz, 4 Hz for fuel). Static data costs nothing.
- The windows are click-through, don't take focus and have no taskbar entry.

## Structure

```
src/vibeRacingOverlays.Data      no UI
  IRSdk/                  shared-memory reader, lenient session-YAML parser, SessionInfo model
  Telemetry/              TelemetryState (one frame), IRacingSource, DemoSource
  Engine/                 RaceEngine: positions, live gaps (100 checkpoints/lap), relative,
                          pit/tow/stint tracking, iRating estimate, SOF, fuel
                          TelemetryService: background thread + source switching
  Model/                  RaceSnapshot, CarInfo, FuelInfo
src/vibeRacingOverlays.App       WPF
  Widgets/                Standings, Relative, Fuel (settings class + Draw)
  Rendering/              DisplayList, WpfRenderer, formatting
  Overlay/                OverlayWindow (transparent/topmost/click-through), OverlayManager (hotkeys, saving)
  UI/                     settings panel generated from [Setting] attributes, column editor
```

## Adding a widget

1. Create `XxxSettings : WidgetSettings` with `[Setting]` properties. The editor UI is generated from them.
2. Create `XxxWidget : Widget` and implement `Draw(DisplayList, RaceSnapshot)`.
3. Register both in `Widget.Create`, in `Widget.Catalog`, and with `[JsonDerivedType]` on `WidgetSettings`.
