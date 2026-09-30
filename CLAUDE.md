# vibeRacingOverlays – notes for Claude

Standalone iRacing overlay app (C# / .NET 8 / WPF), an alternative to Kapps/RaceLab with **low FPS impact** as the main goal.
The user (c4seum) speaks **Dutch**: answer in Dutch. Setup: triple screens (3× 2560x1440, primary in the middle), no VR.

## Structure
- `src/vibeRacingOverlays.Data`: iRacing shared-memory reader (`IRSdk/`), lenient session-YAML parser, `DemoSource` (synthetic race), `RaceEngine` (positions, live gaps via 100 checkpoints/lap, relative, pit/tow/stint, iRating/SOF, fuel), `TelemetryService` (background thread).
- `src/vibeRacingOverlays.App`: WPF editor + overlay windows.
  - `Widgets/`: Standings, Relative, Fuel. Each is a `XxxSettings : WidgetSettings` with `[Setting]` attributes (the editor UI is generated from them) plus `XxxWidget.Draw(DisplayList, RaceSnapshot)`. Register new widgets in `Widget.Create`, `Widget.Catalog` and with `[JsonDerivedType]` on `WidgetSettings`.
  - `Rendering/`: widgets draw into a `DisplayList`. The window only redraws when the list changed (FPS!). `WpfRenderer` fits text inside its column (shrinks numbers, uses an ellipsis for names).
  - `Core/Settings.cs`: `AppSettings` holds Layouts (sets of widgets), Presets (per widget type), theme etc. Stored in `%APPDATA%\vibeRacingOverlays[-dev]\settings.json`.
  - `UI/`: settings panel with reset buttons (per setting / group / widget), live preview (live or demo single class / multiclass), side-by-side layout, `PositionPanel` (anchor/screen/offsets/lock of the selected widget + snapping; right of the settings when stacked, below the preview side by side). **Row heights are fixed**: every row in a card is `Ui.RowHeight` (34 px) with the control vertically centred, and every input control is `Ui.ControlHeight` (26 px: text box, combo, button, segmented, color swatch), so each row has the same 4 px of air above and below. Always create settings rows with `Ui.Row` (label | editor | reset), and rows with their own columns (like the column list) with `Ui.ListRow`. Never build rows as loose Grids, and never give rows or editors their own height or vertical margins. An editor that really can't fit (the 3×3 anchor grid) is marked with `Ui.MarkTall` (a UniformGrid counts as tall automatically). New settings via `[Setting]` go through `Ui.Row` automatically. Keep new UI consistent: build it from `UI/Ui.cs` (Card, Header + Reset, Row with dividers, Switch, Segmented, IconButton) and the styles in `App.xaml` (PrimaryButton for the one main action, GhostButton, IconButton); colors only via theme keys (`Themes/Dark.xaml` "Graphite", `Themes/Light.xaml` "Soft light", same keys in both). Never give a style the same key as a theme color (it overrides the brush and crashes).
  - `Overlay/Placement.cs`: widget position = screen ("Left/Middle/Right", same meaning on every PC) + 9-point anchor + inward offsets, in screen pixels; widgets grow away from their anchor. Snapping while dragging via `WM_MOVING` (snap distance / margin, Shift = free).
- `Data/Engine/RaceDistance.cs`: laps to go per iRacing rules (lap limit, time limit or both; overall leader ends the race, also multiclass; everyone finishes on the next line crossing). Pace = median of the last 5 clean laps per car.
- `src/vibeRacingOverlays.Setup`: .NET Framework 4.8 per-user installer (no admin rights). The app exe is embedded as a resource, plus a small uninstaller.
- `assets/logo.png`: the user's own "vRO" logo (transparent). `tools/make-icon.ps1` turns it into `src/vibeRacingOverlays.App/app.ico` (all sizes); rerun it after changing the logo.
- Docs: `README.md` is a **user guide** (features, widgets, installation, usage, FAQ), with no implementation details. Technical notes (dev build, releasing, structure, adding widgets) go in `DEVELOPMENT.md`. Keep both up to date when features change.

## Terms (user's wishes)
- In the UI they are called **widgets** (not overlays). The "Toggle overlay" button shows/hides all widgets.
- Single class vs multiclass is **detected automatically**. Class names/colors come from iRacing (`ClassNaming` recognizes GTP/LMP2/GT3... when iRacing leaves the name empty). No manual class editing.
- Renaming a widget only changes its display name; its type stays the same.

## Build / test / release
- Local test version: `.\publish-dev.ps1` produces a **DEV build** ("vibeRacingOverlays DEV", orange title, settings in `%APPDATA%\vibeRacingOverlays-dev`, Start menu "vibeRacingOverlays DEV"). Close the app first.
- Design checks without iRacing: `vibeRacingOverlays.exe --snapshot <folder>` (PNG per widget, demo single and multi, plus the user's saved widgets and a narrow-columns test). `--snapshot <folder> --live` uses live iRacing data. `--dump <file>` writes raw iRacing values.
- Release: `.\build-release.ps1 -Version x.y.z` locally, or push a tag `vX.Y.Z` so GitHub Actions (`release.yml`) builds the installer and portable zip and creates a GitHub Release (notes = the version's CHANGELOG section). Before tagging: move Unreleased to the new version in CHANGELOG.md, add the version to the release history below, commit, push, then tag. **Never push a version tag without the user's explicit consent.**
- `gh` for this repo: `--repo c4seum/vibeRacingOverlays` (on the work laptop both accounts are logged in; see memory).
- Versions: last digit = fixes only, middle digit = new features.
- A normal `git push` only runs the "Build" check (`build.yml`); only a pushed `vX.Y.Z` tag creates a release.

## Release history
- **v1.0.0**: first release (Standings, Relative, Fuel; layouts, presets, reset buttons, preview, installer).
- **v1.0.1**: the user's vRO logo as app icon, README rewritten as a user guide.
- **v1.1.0** (2026-09-30, built on the work laptop): position panel (anchoring, offsets, lock, snapping), Relative "Laps in stint" column, Fuel: "Last N avg" and "Stint avg" rows, stops indicator (off / next to refuel / column), laps-to-go fixes (timed race last lap, lap+time limit, clean-lap pace, after checkered, grid). Released before an in-game test: check at home (drag snapping, stint reset after a pit stop, last lap of a timed race) and fix in 1.1.x if needed.

## History and documentation (agreed 2026-09-30)
- **One commit per finished change** (a feature or a fix, not per prompt), made locally as soon as it works and is tested, so every change can be reverted on its own (`git revert <hash>`). The message says what changed and why. Pushing stays the user's call (end of the day); don't push unasked.
- **CHANGELOG.md**: add every user-visible change under `## [Unreleased]` in the same commit (Added / Changed / Fixed). At a release, rename that section to `## [x.y.z] - date` and add a new empty Unreleased section; the release workflow uses that section as the GitHub release notes.
- Code comments explain *why* (iRacing quirks, non-obvious choices), not what the code does.
- Going back: every release is a tag plus a GitHub Release with the installer, so users can install an older version; developers can `git checkout vX.Y.Z` to look at old code.

## Working rules
- Before UI tests: back up `settings.json` and restore it afterwards. Close test instances (also leftover Debug instances), but never the user's own running DEV app (`publish\vibeRacingOverlays.exe`): check with `Get-Process` first, and don't edit `settings.json` while it runs.
- UI Automation: toggle buttons/checkboxes react to Checked/Unchecked (a UIA Toggle doesn't raise Click); find windows by the test process id.
- Never send keystrokes blindly: the user's other windows are often in front. Use UI Automation, the `--silent` installer mode, or PrintWindow for screenshots.
- `winget`-installed tools (dotnet, git, gh) may be missing from PATH in already-open terminals: refresh with
  `$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')`.
- Commits as `c4seum <c4seum@users.noreply.github.com>` (no personal email address).

## Working on several PCs
- The user works on a home PC (with iRacing) and a work laptop (probably without iRacing: develop with the demo race, test in-game at home).
- Start a session with `git pull`, end it with `git push`.
- Layouts/presets/settings are not in git: copy `%APPDATA%\vibeRacingOverlays-dev\settings.json` between PCs if needed.
- New PC setup: `winget install` Git.Git, GitHub.cli and Microsoft.DotNet.SDK.8, restart the Claude app, run `gh auth login` and `gh auth setup-git`, `git clone https://github.com/c4seum/vibeRacingOverlays.git`, then `.\publish-dev.ps1`.

## Ideas for next versions (as discussed)
- Next fix release (1.0.x): hide the fuel calculator in replays/spectating (it shows 0.00 there), set hotkeys in the UI, tray icon / start minimized / autostart with Windows or iRacing, FPS measurement in a race.
- 1.1.0 (suggested first: Inputs + Delta bar): widgets Inputs (throttle/brake/steering trace), Delta bar, Radar/spotter, Flags, Trackmap, Session info.
- Later: update notification (easiest if the repo is public), brand logos instead of text, layout export/import (settings don't sync between PCs), pit history from before the app started.
- iRacing terms (article of 2026-09-30, support.iracing.com 31000179757): no approval/registration exists; SDK/data API users may not display, publish or expose a member's display name or custid "in any context" without consent. The app shows driver names locally (like Kapps/RaceLab), shows no custid and publishes nothing. The user decided (2026-09-30) not to act yet; possible later: a "names" option (full / initials / car number only) or a streamer mode, and keeping the repo private for now.
- Known: the GitHub action `softprops/action-gh-release@v2` still runs on Node 20 (warning only); update when a newer major version exists.
