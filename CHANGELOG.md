# Changelog

All notable changes to vibeRacingOverlays, newest first. Each version is a git tag (`vX.Y.Z`) and a GitHub Release with the installer and the portable zip, so any older version can be downloaded or checked out again.

Versions: the last digit is for fixes only, the middle digit for new features.

## [Unreleased]

### Added
- Relative: new style setting **Cars in the pits**. *Dim row* fades the whole row of a car on pit road (car number, license and class color too, the PIT badge stays bright), so it's clear it isn't racing you. *Dim text* is the old look (only the text in grey). Your existing Relative widgets keep *Dim text*; the Default preset (and so new widgets and fresh installs) uses *Dim row*.
- **System tray icon.** Closing the window keeps the app and your widgets running in the tray; click the icon to open it again. Its menu has show widgets, edit layout, switch layout and Exit. *Keep running when the window is closed* (in that menu) turns this off.
- **Update notice.** The app looks for a new version at start and twice a day. A newer version shows a button at the top of the window and one Windows notification; both open the download page. Nothing is downloaded by itself. *Check for updates* in the tray menu turns it off.
- **Undo / redo** (↶ ↷ at the top, `Ctrl+Z` / `Ctrl+Y`) for every change: widgets and their settings, position and dragging, layouts (new, rename, delete), widget presets and layout presets (save, rename, delete, import) and app settings like the theme. The tooltip says what will be undone; a change in another layout switches to it first. 50 steps, while the app is open.
- **Hotkeys and wheel buttons** (Hotkeys menu): set the keys of every action, or a button on your wheel or button box (read directly, no extra program; also while iRacing is in front). New actions: previous layout, layout 1–4, show/hide widget 1–5, fuel custom per lap ±0.05 L, start the fuel averages again. One field per action, for a key combination or a wheel button; setting it recognises a short press (released quickly) or a hold (pressed for the hold time, 0.6 s, adjustable); a Repeat switch for steps (greyed when the key or button also has a hold); a key or button can carry one short and one hold action; new action "fuel custom = current average"; the Fuel calculator confirms fuel hotkeys in its header for 2 s. The new actions have no keys until you set them.
- **Font per widget** (Text & size > Font): Bahnschrift, Rajdhani, Titillium Web, Chakra Petch, Exo 2, Oxanium, Orbitron, JetBrains Mono, Tomorrow, Bai Jamjuree, IBM Plex Sans Condensed, Sofia Sans Semi Condensed, Lexend and Jura. The thirteen new fonts come with the app (SIL Open Font License, see `FONT-LICENSES.txt`). Column widths follow the font, also in the Fuel calculator. Your existing widgets and the Default presets keep Bahnschrift.
- **Text weight per widget** (Text & size > Text weight): *Regular* or *Semibold*. Windows apps draw semibold heavier than a web browser, so *Regular* looks like the fonts in a browser. Your existing widgets and the Default presets keep *Semibold*.
- Fuel calculator: new layout **Table** (Content > Layout), in the style of the Relative and Standings: header bar, fuel level and laps in race big with the PIT indicator, then column titles and a row per estimate (colour bar, name, L/lap, laps, refuel, stops, at end; negative fuel at the end in red). Your existing Fuel calculators keep *Classic*; the Default preset (new widgets, fresh installs) uses *Table* at value font size 24.
- Buttons below the layout list: **Add layout**, rename, **duplicate** (new: a copy of the layout with all its widgets, also in the Layout menu) and delete.

### Changed
- Pit column flags drawn as icons instead of text characters, bigger and clearer: the black flag and the furled black flag as a flag on a pole, the meatball (repair) as a big orange disc. Header bar: humidity has a drop icon instead of "RH".
- Widget settings have the same order for every widget: the widget card (name, Show, Refresh rate), then what it shows (Rows; Fuel: Content and Estimates), Columns (with *Column titles row*), Header bar (with *Show header bar*), Text & size, Colors. The old "Layout" and "Style" cards are gone. Relative: new setting *Header background*. Fuel calculator (Table): *Header background*, *Row background* and *Alternate row* like the Relative.
- Fuel calculator: the *Stops* option "Next to refuel" is gone; stops are shown in a column (or off). Widgets that used "Next to refuel" (it was the default) show their stops in a column now.
- Header bar (Relative and Standings): track and air temperature have an icon instead of a word: a road for the track, wind for the air (*Air 21.3°C* is now the wind icon with *21.3°C*).
- Header item "Incidents and limits" without spaces: *5x/17x/25x* and *5x/25x* (shorter, reads as one value).
- Demo race and practice: one car always drives through the pit lane right behind you, so you can see how cars in the pits look (Relative: *Cars in the pits*, the PIT badge) without waiting for a pit stop. In the demo race there is also always a car a lap up right ahead of you and a car a lap down right behind you, to see the relative's "lapping you" and "lapped by you" colours.
- Standings and Relative are one shape now: the header bar sits directly on the rows (no gap), the whole widget has rounded corners, and the space between classes and blocks in the Standings is filled in the header colour.
- Starting the app while it's already running (for example from the tray) opens the running app instead of starting a second one.
- The installer asks a running app to exit properly (it saves first) instead of only closing its window, which now hides the app to the tray.
- After an install or update, the layout **Get started** is added again when you don't have a layout with that name (for example after updating from an earlier version, or after renaming it). It's added next to your layouts; your active layout stays active, and the status bar says it was added. A "Get started" layout you already have is never changed: it may hold your own changes. If you delete Get started on purpose, updates don't bring it back (New layout from preset still can).

### Fixed
- Sharper text in the widgets: letters and digits are now placed on whole screen pixels. Before, digits (centred in their equal-width cells) often fell between two pixels, so some looked thinner or blurrier than others.
- The preview in the editor now draws text exactly like the widgets on screen (grayscale smoothing). It used Windows ClearType, which gave coloured fringes and made fonts look thinner and harsher than they really are on screen.
- Widgets no longer jump in front of everything every 2 seconds. They are only put back on top while **iRacing is the window in front**, and only when another window covers them. So they stay behind the taskbar, menus, tray flyouts and the **Win+Shift+S** screen (where the live widgets on top of the frozen screenshot looked doubled), and overlapping widgets no longer swap places now and then. Two copies of the app (for example a test version next to the normal one) no longer push each other away.
- With iRacing in **full screen** the app no longer pushes widgets over it (they can't show there, and it can disturb the game). The status bar now says that iRacing runs in full screen and how to fix it.

## [1.3.0] - 2026-10-02

### Added
- Export and import (Layout and Widget menus): a layout file holds every widget's own current settings (also changes not saved in a preset); an imported layout becomes a layout preset, and its widgets follow one of your widget presets only when that holds exactly their settings. Widget presets can be exported and imported as preset files (the settings without the position). Your own presets are never replaced.
- Preset library in `Documents\vibeRacingOverlays\presets`: every widget preset is a file there, and preset files you put there (shared ones, or exported widgets) appear in the app, also while it runs. Delete presets in the app; a file deleted outside the app is written again.
- Drag and drop: drop layout, preset or widget files on the app window to import them.
- Widget types a version doesn't know are skipped with a message.
- Layouts and presets, in a new menu bar (**Layout** and **Widget** menus): layouts and their widgets are always the current version (every change is kept automatically); the layout selected in the list is the active one. **Layout presets**: **Save as preset** / **Save to preset** store the active layout, **New layout from preset** makes a new layout from one (a preset never overwrites a layout you work with), plus **Manage presets** and **Export / Import layout**. **Widget presets**: **Load preset** overwrites the selected widget's settings, **Save as preset** / **Save to preset** store them, plus **Manage presets** and **Export / Import preset**. **Get started** and the **Default** widget presets are the app's own and always stay intact; a new installation starts with Get started: Standings top left, Relative bottom right and Fuel calculator bottom left on the main screen. A widget you add starts in the centre of the main screen.
- Saving a widget preset that other widgets use asks whether to update them too.
- The Default presets are built into the app (preset files in the `defaults` folder of the source code).
- Your layouts and presets are better protected: the app keeps backups in `%APPDATA%\vibeRacingOverlays\backups` (one per day for the last 7 days, and one from before the first start of a new version). A settings file that can't be read is no longer silently replaced by the default layout: you get a message and the file is kept in the backups. A widget or preset that this version doesn't know (for example after going back to an older version) is kept and comes back with a version that knows it, instead of making the whole file unreadable.
- If saving the settings fails, the status bar turns red and the reason is written to `errors.log` (before, this went unnoticed and changes were lost when the app closed).
- `--settings-dir <folder>` start option: use another settings folder (for tests; it never takes settings from another folder).
- Relative: header bar items like in the standings (on/off, order, format): widget name, laps, time, track and air temperature, humidity, incidents and clock. The default looks like before: "RELATIVE" on the left, time remaining and incidents on the right.
- Header item "Incidents and limits" (relative and standings): your incidents with the limits of the event, like "5x / 17x / 25x" (next penalty / DQ; after the first penalty the next one, e.g. 25x then 33x), or "5x / 25x", or only "5x". Without limits (practice, many hosted sessions) only your count shows.
- Header item "Clock" (relative and standings): real time or sim time of day.

### Changed
- Fuel calculator: the per-lap rows (Average, Last, Last N, Stint, Custom) with laps remaining, refuel, stops and fuel at end are updated once a lap, when you cross the line, and when you leave the pit lane (so a refuel shows right away). They no longer tick with every drop of fuel. Fuel level, laps in race and the PIT indicator stay live.

## [1.2.0] - 2026-10-01

### Added
- Standings settings per session type: "Practice & qualifying" and "Race" each have their own rows, columns, header items and formats (switch at the top of the settings; "Copy to ..." copies one to the other). Colors, font size and row height stay shared. The preview's demo session follows the switch. Existing standings keep their settings for both.
- Formats per column, like Kapps: driver name (full / short / last name), license (A3.48 / A3.4 / A3 / A / SR only), iRating (4567 / 4.5k / 4k), gap and interval (3, 2 or 1 decimals), last and best lap (1:35.764 / 1:35.76 / 1:35.7). Defaults: 3 decimals in practice and qualifying, 1 in the race.
- New columns: laps completed (on by default in practice and qualifying) and tire compound (the compound letter, only when cars run different compounds, or always).
- Pit status column (standings and relative) shows the flag iRacing gives a driver: black flag (penalty to serve: drive-through or stop-and-go), yellow flag symbol (warning / slowdown, iRacing's furled black flag), meatball (orange dot: repair required) and DQ. `--dump` lists the per-car flags.
- Relative: the pit status column shows "OUT" on an out lap too.
- Relative: the columns it shares with the standings have the same formats: driver name (full / short / last name, replaces the separate "Name style" setting; your choice is kept), license, iRating and last lap.
- Header bar items you can turn on and off, reorder and format: session, class, laps, time (remaining / total, remaining, elapsed / total), track and air temperature (°C, °F or both, with or without a decimal), humidity, strength of field (4567 / 4.5k / 4k) and cars. "Push what follows to the right" splits the bar into a left and a right group.

### Changed
- Standings and relative: every column has a fixed width that fits its widest content at the chosen font size and format (for example "8:88.888" for a lap time with 3 decimals), so nothing gets cut off or shrunk. Only the driver name keeps a width you set. The new style setting "Column spacing" (default 8 px) sets the space between the columns.
- Standings: the P1 row of each class shows "GAP" and "INT" in those columns in practice and qualifying too (was race only).
- Column list in the settings: the move up / down buttons are now in front of each column, and only the driver name has a width box.
- Numbers in all widgets use equal-width digits, so lap times, gaps and ratings line up exactly in their columns ("1:11.1" is as wide as "1:08.8"), whatever the font.

### Fixed
- TOW only shows in a race, while iRacing tows a car back after a reset (the tow timeout); for your own car it uses iRacing's tow timer. In practice and qualifying a reset is instant, so the list no longer fills up with TOW. In practice and qualifying a car in its pit box shows a grey "PIT" (in the pits between runs, not on track); the orange PIT badge is for driving in the pit lane, and in a race for a pit stop. The number of stops (P2) only shows in a race.
- "OUT" stays for the whole out lap. On tracks where the pit exit lies before the start/finish line it disappeared at the line, a few hundred meters after the pit exit.
- Standings in practice and qualifying: drivers with a lap time were missing or in the wrong place, and lap times of most drivers were empty. The standings now follow iRacing's own results list: ranked by fastest lap, with the fastest and last lap iRacing shows, and drivers without a lap time after them in car-number order (as iRacing lists them). In races, iRacing's results fill in positions and lap times the live telemetry doesn't have.

## [1.1.1] - 2026-09-30

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

[Unreleased]: https://github.com/c4seum/vibeRacingOverlays/compare/v1.1.1...HEAD
[1.1.1]: https://github.com/c4seum/vibeRacingOverlays/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/c4seum/vibeRacingOverlays/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/c4seum/vibeRacingOverlays/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/c4seum/vibeRacingOverlays/releases/tag/v1.0.0
