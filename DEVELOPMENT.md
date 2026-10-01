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

- `vibeRacingOverlays.exe --snapshot <folder>`: renders every widget to PNG with the demo race and demo practice (single class and multiclass; practice files end in `_pq`), plus your saved widgets and a narrow-columns test.
- `--snapshot <folder> --live`: the same, with live iRacing data.
- `--settings-dir <folder>`: use another settings folder (with its own `backups` and `errors.log`). UI tests always run like this on a copy of the settings, so they never touch the real `settings.json`.
- `--dump <file>`: writes raw iRacing values (per car) to a text file.

## Commits and the changelog

- One commit per finished change (a feature or a fix), with a message that says what changed and why, so any change can be reverted on its own with `git revert <hash>`.
- Every user-visible change also goes into `CHANGELOG.md` under `## [Unreleased]` (Added / Changed / Fixed), in the same commit.

## Releasing a new version

1. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [1.1.1] - <date>`, add a new empty `## [Unreleased]` above it and update the compare links at the bottom.
2. Commit, push and tag:

```powershell
git commit -am "Release 1.1.1"
git push                   # "Build" workflow checks that it still compiles, no release
git tag v1.1.1
git push origin v1.1.1     # "Release" workflow builds the installer + zip and publishes a GitHub Release
```

- The release notes are that version's section of `CHANGELOG.md` (`tools/changelog-section.ps1`), followed by GitHub's generated list of commits.
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
  Overlay/                OverlayWindow (transparent/topmost/click-through), OverlayManager (hotkeys, layouts, saving),
                          Placement (anchor + screen + offsets <-> pixel position, snapping via WM_MOVING; all in screen pixels)
  App.xaml                all control styles (buttons, switch, segments, text box, combo, slider, scroll bars, cards)
  Themes/                 Dark.xaml ("Graphite") and Light.xaml ("Soft light"): only colors, same keys in both
  UI/                     Ui.cs (shared building blocks: card, header, row, switch, segmented, icon button;
                          every card row is Ui.RowHeight high: use Ui.Row / Ui.ListRow, never loose Grids),
                          settings panel generated from [Setting] attributes (reset buttons, presets), preview, themes,
                          PositionPanel (main window: anchor/screen/offsets and lock of the selected widget, snapping)
src/vibeRacingOverlays.Setup     per-user installer (.NET Framework 4.8), app embedded as a resource
```

## Adding a widget

1. Create `XxxSettings : WidgetSettings` with `[Setting]` properties. The editor UI, the reset buttons and presets are generated from them, with the standard row height. Custom editors go through `Ui.Row` (or `Ui.ListRow`) too, so all rows in a block stay the same height.
   - Column lists: implement `ITableSettings` with `ColumnDef`s; `ColumnDef.Fit(sample)` gives a column its fixed width (widest content per format; only the name uses `UserWidth()`), `ColumnDef.WithFormats(default, (key, example)...)` adds a format drop-down per column (the label is an example of the result, like "1:35.764"). Header item lists work the same via `IHeaderItems`.
   - Settings that differ per session type: put them in a profile object and implement `ISessionProfiles` (the editor shows the P&Q / Race switch and edits that profile; the rest of the widget's settings stay shared), plus `INormalizable` for migrations after loading. See `StandingsSettings`.
2. Create `XxxWidget : Widget` and implement `Draw(DisplayList, RaceSnapshot)`. Text is drawn with equal-width digits (`TabularText`), so numbers in a column line up; measure texts for flowing layouts with `DisplayList.Measure`.
3. Register both in `Widget.Create`, in `Widget.Catalog`, and with `[JsonDerivedType]` on `WidgetSettings`.
