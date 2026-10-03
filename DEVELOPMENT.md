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

A running DEV app (started from `.\publish`) is asked to exit before the build (it saves first) and started again afterwards. Closing the app's window only hides it to the tray; tools that must stop the app set the named event `Local\vibeRacingOverlays-exit-<process id>` (`App.ExitEventName`, used by the installer and `publish-dev.ps1`). One app runs per settings folder (a second start shows the running app's window), so DEV, release and `--settings-dir` test instances can run side by side.

Command-line options for design and debugging:

- `vibeRacingOverlays.exe --snapshot <folder>`: renders every widget to PNG with the demo race and demo practice (single class and multiclass; practice files end in `_pq`), plus your saved widgets and a narrow-columns test.
- `--snapshot <folder> --live`: the same, with live iRacing data.
- `--settings-dir <folder>`: use another settings folder (with its own `backups` and `errors.log`). UI tests always run like this on a copy of the settings, so they never touch the real `settings.json`.
- `--dump <file>`: writes raw iRacing values (per car) to a text file.
- `--zorder-log <file>`: every 2 seconds one line with the window in front, iRacing's window (position, size, style), its display mode from `Documents\iRacing\rendererDX11Monitor.ini` and whether Windows reports exclusive full screen, and per widget whether a window lies over it and whether it was lifted. Only process names and window classes, no window titles. For checking the widgets with iRacing in full screen, in a window and borderless.
- `--update-feed <url or file>`: test the update notice with a fake release (JSON like GitHub's `{"tag_name": "v9.9.9", "html_url": "https://..."}`). Also turns the check on in DEV builds, which normally skip it (their version number isn't a release number).

## Update notice

`Core/UpdateCheck.cs` asks `https://api.github.com/repos/c4seum/vibeRacingOverlays/releases/latest` (no account, no data sent) at start and every 12 hours, and compares its `tag_name` with the running version. **GitHub only answers this for a public repository**: while the repository is private the check silently finds nothing. To use it with a private source repository, publish the releases in a separate public repository and change `UpdateCheck.Repository`.

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
3. Register both in `Widget.Create`, in `Widget.Catalog`, and with `[JsonDerivedType]` on `WidgetSettings`. A Default preset for the new type is created automatically (from its preset file in `app/defaults/presets`, else the code defaults). Add that preset file to give the type its recommended settings.

## Changing a widget (new options, new look) without changing what users have

Rule: an update never changes how existing widgets, widget presets, layout presets or exported files look or behave. Only fresh installs and the **Default** presets get the new recommended values, plus whatever you deliberately mark for everyone in that release. Users read about it in the release notes.

This works with the existing loading code:

- **A new setting**: a value missing from a saved widget gets the property's initializer. So give the property the value that **keeps the old behaviour** (`public bool ShowX { get; set; } = false;`, or the old fixed value when something becomes adjustable), and put the **recommended** value in the type's Default preset file in `app/defaults/presets/*.vropreset.json`. Fresh installs, "Get started" and new widgets start from the Default preset, and Default presets are refreshed from these files on every start.
- **A new column or header item**: give its `ColumnDef` `DefaultOn = false` (existing lists get it switched off), and add it switched on to the Default preset file if it should be on for new users. New entries are added at the end of existing lists for now; inserting them next to a logical neighbour is still to be built.
- **A renamed setting, or one whose meaning changes**: never let it fall back silently. Keep the old property readable (nullable, `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`) and convert it in `Normalize()` (`INormalizable`), like the legacy properties of `StandingsSettings`.
- **Something every existing user should get anyway** (a real improvement or a fix): write it as a migration in `Normalize()`, with a comment saying why, and list it under *Changed* in the changelog.
- **Drawing changes** (how something is rendered, not a setting) reach everyone automatically; list them under *Changed*.
- **Release notes**: say per widget what's new and whether it's off for existing widgets ("new column X, off in your widgets, on in Default").
- **Before a release**: load settings, presets and layout files from the previous release (keep copies) and check they still look the same. Automated test files for this are still to be built.
