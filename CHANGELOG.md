# Changelog

All notable changes to vibeRacingOverlays, newest first. Each version is a git tag (`vX.Y.Z`) and a GitHub Release with the installer and the portable zip, so any older version can be downloaded or checked out again.

Versions: the last digit is for fixes only, the middle digit for new features.

## [Unreleased]

### Changed
- New look for the app (not the widgets): "Graphite" dark theme and "Soft light" light theme; compact, filled controls without outlines and 6px corners; settings as inset lists in cards with small caps titles (the widget's name and preset in a card of their own); segmented buttons for short choices; slimmer sliders and scroll bars; icons in the widget list; a status label in the top bar and a title bar that follows the theme.
- Setting choices use readable names ("In car" instead of "InCar").
- Side by side: the preview and the position panel share the height, so the preview stays large.
- The settings and position blocks have a fixed width and are centred in their area (stacked: each gets half of the width); the preview is centred too.
- The window can't be made smaller than its content any more (limited to the screen size).

### Fixed
- The preview was cut off at the bottom when the window was small (side by side).
- Rows in the settings and position blocks are always the same height, whatever they hold (check box, slider, segmented buttons, color, column list); only the anchor grid is taller. Rows are more compact (34 px) and every input control (text box, drop-down, button, segmented buttons, color swatch) is the same height, with its text centred, so everything sits visually in the middle of its row.

## [1.1.0] - 2026-09-30

### Added
- **Position panel** in the main window (next to the widget settings, or below the preview in *Side by side*): pick the screen (Left / Middle / Right) and one of 9 anchors (corners, edges, centre), with X/Y offsets measured from the anchor towards the middle of the screen. Anchored widgets grow away from their anchor.
- **Lock** per widget (can't be dragged or resized by accident), plus Lock all / Unlock all.
- **Snapping** while dragging: to the edges and corners of other widgets and of the screen, with adjustable snap distance and snap margin. Hold Shift to drag freely.
- In edit layout mode, clicking a widget on screen selects it in the app.
- Relative: optional **Laps in stint** column.
- Fuel calculator: optional **Last N avg** row (N = 2 to 50) and **Stint avg** row (average since the last pit stop).
- Fuel calculator: **pit stops needed** to finish, based on the tank size: off, next to the refuel value, or as its own column.

### Changed
- Widget positions are stored as screen + anchor + offsets. Screens are remembered as Left / Middle / Right, so a layout lands on the same screen on another PC with the same setup. Existing layouts are converted automatically.
- The fuel tracker keeps the last 50 valid laps (the plain average still uses the last 10).

### Fixed
- *Laps in Race* and *Refuel* showed "-" on the last lap of a timed race, after the clock hit 0:00.
- Races with both a lap and a time limit only used the lap limit; now whichever comes first.
- Lap time estimates used a single last lap, so a pit, out or caution lap made the refuel amount jump. Now the median of the last 5 clean laps per car.
- After the checkered flag the remaining laps stayed near 1 after you had crossed the line.
- Before the start, the distance to the start line wasn't included in the fuel needed.

## [1.0.1] - 2026-09-30

### Changed
- The user's own "vRO" logo as the app icon and in the README.
- README rewritten as a user guide; technical notes moved to DEVELOPMENT.md.

## [1.0.0] - 2026-09-30

### Added
- First release: standalone iRacing overlays that read iRacing's shared memory directly (no SimHub), built for low FPS impact.
- Widgets: **Standings**, **Relative** and **Fuel calculator**, with automatic single class / multiclass detection and iRacing class colors.
- Layouts (sets of widgets), presets per widget type, reset buttons per setting / group / widget, live preview (live or demo race).
- Edit layout mode (drag to move, mouse wheel to resize), global hotkeys, dark and light theme.
- Per-user installer (no admin rights) and a portable zip.

[Unreleased]: https://github.com/c4seum/vibeRacingOverlays/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/c4seum/vibeRacingOverlays/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/c4seum/vibeRacingOverlays/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/c4seum/vibeRacingOverlays/releases/tag/v1.0.0
