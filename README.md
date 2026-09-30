<p align="center"><img src="assets/logo.png" alt="vibeRacingOverlays logo" width="160"></p>

# vibeRacingOverlays

vibecoding easy to run iRacing overlays.

vibeRacingOverlays shows live race information on top of iRacing: standings, relative and a fuel calculator. Each overlay is a widget that you can place, size and style yourself. The app is built to cost as little FPS as possible. It works directly with iRacing, without SimHub or any other program.

## Features

- **Lightweight:** widgets only redraw when something changes, and you set how often each widget may update. When nothing changes, a widget costs nothing.
- **Widgets you arrange yourself:** drag them anywhere on any screen (triple screens work too), resize them with the mouse wheel, and set colors, font size, rows and columns per widget.
- **Single class and multiclass automatically:** the app recognizes the session type and adjusts the standings and relative on its own, with the official iRacing class colors.
- **Layouts:** save complete sets of widgets, for example "Sprint" and "Endurance", and switch between them, even while driving.
- **Presets:** save the settings of a widget and reuse them in every layout.
- **Live preview:** see the effect of each setting immediately, with live iRacing data or a built-in demo race (single class or multiclass). You can set everything up without iRacing running.
- **Reset buttons:** undo a change per setting, per section, or for a whole widget.
- **Dark and light theme** for the app itself.

## Widgets

### Standings
The classification of your session.
- Top of the field plus a window around your own position, so you always see yourself.
- In multiclass sessions: a block per class with class name, class color, fastest lap, strength of field and number of cars. You choose how many rows your own class and the other classes get.
- Columns you can turn on and off, reorder and resize:
  - position and positions gained or lost since the start;
  - car number, driver name, car brand, license and safety rating, iRating;
  - estimated iRating change;
  - gap to the leader, interval to the car ahead, last lap (purple for fastest in class, green for a personal best), best lap;
  - pit status (in the pits, towed, out lap, number of stops), laps in the current stint, laps completed and tire compound.
- Many columns have a format of their own, for example lap times with 3, 2 or 1 decimals, iRating as 4567 or 4.5k, and license as A3.48, A3.4 or only the letter. Numbers always line up neatly in their column.
- Header bar with the items you choose, in your order: session type, class, laps (or an estimate for timed races), time, track and air temperature (°C and/or °F), humidity, strength of field and number of cars, split into a left and a right group.
- **Separate settings for practice & qualifying and for the race:** rows, columns, header and formats can differ per session type (for example 3 decimals and best laps in qualifying, gaps with 1 decimal in the race). The widget switches automatically; colors and size are shared.

### Relative
The cars directly around you on track, with the time difference to each.
- You choose the number of cars ahead and behind.
- Colors show whether a car is a lap ahead of you or a lap down.
- In multiclass sessions, class color bars and car numbers in the class color.
- Pit and tow status per car.
- Optional columns: last lap and laps in the current stint.

### Fuel calculator
How much fuel you use and need.
- Fuel level, laps left in the race and the time of day.
- Consumption per lap based on your average (last 10 laps), your last lap, the average of your last N laps (you choose N, 2 to 50), the average since your last pit stop, or a value you set yourself.
- For each: how many laps your fuel lasts, how much you need to refuel to finish (with an adjustable safety margin), how many pit stops that takes with your tank size, and how much will be left at the finish.
- *Laps in Race* follows the iRacing rules for lap races, timed races and races with both a lap and a time limit (whichever comes first). The race ends when the overall leader finishes, also in multiclass; you finish the next time you cross the line after that. Lap times are based on your recent clean laps and those of the leader (pit, out and caution laps don't count).
- A PIT indicator that warns when you need to stop.
- Laps with a pit stop, refuel or caution are left out of the average.

## Installation

1. Download the latest version from the [Releases](../../releases) page:
   - **vibeRacingOverlays-Setup-x.y.z.exe**: installer (recommended). It installs for your Windows user only, so no administrator rights are needed. It adds a Start menu shortcut and, optionally, a desktop shortcut.
   - **vibeRacingOverlays-x.y.z-portable.zip**: a single exe that runs without installing.
2. Start vibeRacingOverlays.
3. Set iRacing to **windowed** or **borderless** mode, so the widgets can be drawn on top of it.

Requirements: Windows 10 or 11 (64-bit). Nothing else needs to be installed.

Windows SmartScreen may show "Windows protected your PC" the first time, because the app isn't code-signed. Click **More info** and then **Run anyway**.

What changed in each version is in the [changelog](CHANGELOG.md). Older versions stay available on the [Releases](../../releases) page, so you can always go back to a previous one.

## Getting started

1. **Choose a data source** at the top: *Auto* uses iRacing when it's running and otherwise the demo race. *iRacing* and *Demo* force one source.
2. **Add widgets** with **+ Add** (Standings, Relative or Fuel calculator). Tick a widget in the list to show it, untick it to hide it.
3. **Click a widget** in the list to open its settings. The preview shows the result right away. Use **Side by side** to put the preview next to the settings.
4. **Place your widgets:** press **Edit layout** (or `Ctrl+Shift+E`), drag the widgets to where you want them and use the mouse wheel to resize them. Press it again when you're done. Outside edit mode, clicks go straight through the widgets to iRacing.
   - The **Position** panel (next to the widget settings, or below the preview in *Side by side*) sets the position of the selected widget exactly. In edit layout mode, clicking a widget on screen selects it.
   - **Anchoring & positioning:** pick the **screen** and one of the 9 **anchors** (corners, edges, centre). The widget moves into that corner or edge, and the **X/Y offsets** are measured from there towards the middle of the screen. Use the ‹ › buttons or the arrow keys (1 px, with Shift 10 px). An anchored widget stays put when it gets bigger: a widget in the bottom right corner grows up and to the left. Dragging a widget keeps its anchor and updates the offsets.
   - **Lock** a widget so it can't be dragged or resized by accident. Its edit frame turns grey and shows *LOCKED*. **Lock all** / **Unlock all** do the whole layout at once.
   - **Snapping:** a dragged widget snaps to the edges and corners of other widgets and of the screen. **Snap distance** is how close you have to get, **snap margin** the space kept between widgets and from the screen edge. Hold **Shift** while dragging to place a widget freely.
   - Screens are remembered as *Left*, *Middle* and *Right*, so a layout lands on the same screen on another PC with the same setup. If that screen isn't there, the widget shows on the main screen.
5. **Save layouts** with the buttons under *Layout*: **New**, **Save as**, **Rename** and **Delete**. Everything you change is saved automatically.

### Hotkeys

These also work while iRacing has focus.

| Keys | Action |
|---|---|
| `Ctrl+Shift+E` | Edit layout on/off (move, resize and lock widgets) |
| `Ctrl+Shift+H` | Show/hide all widgets |
| `Ctrl+Shift+L` | Switch to the next layout |

### Per widget

- **Display name:** give a widget its own name, for example "Standings endurance".
- **Preset:** **Save** stores the widget's settings under a name; **Load** applies a saved preset. Deleting the last preset of a widget type resets that widget to the defaults.
- **Show:** always, only when you're in the car, or only in races.
- **Refresh rate:** how often the widget may update. Lower means even less FPS impact.
- **Reset:** ↺ next to a setting, **Reset** next to a section, or **Reset widget** for everything. The widget's name and position are kept.

## Frequently asked questions

**The widgets don't appear over iRacing.**
Set iRacing to windowed or borderless mode. Exclusive fullscreen doesn't allow windows on top.

**Where are my layouts and settings stored?**
In `%APPDATA%\vibeRacingOverlays`. Uninstalling asks whether you want to keep them.

**Does it work in replays?**
Yes. In a replay iRacing provides less data (for example no fuel data), so some values stay empty.

**How do I uninstall?**
Through Windows Settings, under *Apps* > *Installed apps* > *vibeRacingOverlays* > *Uninstall*.

---

Want to work on the app itself? See [DEVELOPMENT.md](DEVELOPMENT.md).
