# vibeRacingOverlays – notes for Claude

Standalone iRacing overlay app (C# / .NET 8 / WPF), an alternative to Kapps/RaceLab with **low FPS impact** as the main goal.
The user (c4seum) speaks **Dutch**: answer in Dutch. Setup: triple screens (3× 2560x1440, primary in the middle), no VR.

## Structure
- `src/vibeRacingOverlays.Data`: iRacing shared-memory reader (`IRSdk/`), lenient session-YAML parser, `DemoSource` (synthetic race), `RaceEngine` (positions, live gaps via 100 checkpoints/lap, relative, pit/tow/stint, iRating/SOF, fuel), `TelemetryService` (background thread).
- `src/vibeRacingOverlays.App`: WPF editor + overlay windows.
  - `Widgets/`: Standings, Relative, Fuel. Each is a `XxxSettings : WidgetSettings` with `[Setting]` attributes (the editor UI is generated from them) plus `XxxWidget.Draw(DisplayList, RaceSnapshot)`. Register new widgets in `Widget.Create`, `Widget.Catalog` and with `[JsonDerivedType]` on `WidgetSettings`.
  - `Rendering/`: widgets draw into a `DisplayList`. The window only redraws when the list changed (FPS!). `WpfRenderer` fits text inside its column (shrinks numbers, uses an ellipsis for names).
  - `Core/Settings.cs`: `AppSettings` holds Layouts (sets of widgets), Presets (per widget type), theme etc. Stored in `%APPDATA%\vibeRacingOverlays[-dev]\settings.json`.
  - `UI/`: settings panel with reset buttons (per setting / group / widget), live preview (live or demo single class / multiclass), side-by-side layout.
- `src/vibeRacingOverlays.Setup`: .NET Framework 4.8 per-user installer (no admin rights). The app exe is embedded as a resource, plus a small uninstaller.

## Terms (user's wishes)
- In the UI they are called **widgets** (not overlays). The "Toggle overlay" button shows/hides all widgets.
- Single class vs multiclass is **detected automatically**. Class names/colors come from iRacing (`ClassNaming` recognizes GTP/LMP2/GT3... when iRacing leaves the name empty). No manual class editing.
- Renaming a widget only changes its display name; its type stays the same.

## Build / test / release
- Local test version: `.\publish-dev.ps1` produces a **DEV build** ("vibeRacingOverlays DEV", orange title, settings in `%APPDATA%\vibeRacingOverlays-dev`, Start menu "vibeRacingOverlays DEV"). Close the app first.
- Design checks without iRacing: `vibeRacingOverlays.exe --snapshot <folder>` (PNG per widget, demo single and multi, plus the user's saved widgets and a narrow-columns test). `--snapshot <folder> --live` uses live iRacing data. `--dump <file>` writes raw iRacing values.
- Release: `.\build-release.ps1 -Version x.y.z` locally, or push a tag `vX.Y.Z` so GitHub Actions (`release.yml`) builds the installer and portable zip and creates a GitHub Release. **Never push a version tag without the user's explicit consent.**
- Versions: last digit = fixes only, middle digit = new features.

## Working rules
- Before UI tests: back up `settings.json` and restore it afterwards. Close test instances (also leftover Debug instances).
- Never send keystrokes blindly: the user's other windows are often in front. Use UI Automation, the `--silent` installer mode, or PrintWindow for screenshots.
- `winget`-installed tools (dotnet, git, gh) may be missing from PATH in already-open terminals: refresh with
  `$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')`.
- Commits as `c4seum <c4seum@users.noreply.github.com>` (no personal email address).

## Ideas for next versions (as discussed)
- 1.0.1: hide the fuel calculator in replays/spectating, set hotkeys in the UI, tray icon / autostart, FPS measurement in a race.
- 1.1.0: widgets Inputs (throttle/brake/steering), Delta bar, Radar/spotter, Flags, Trackmap, Session info.
- Later: update notification, brand logos, layout export/import (settings don't sync between PCs).
