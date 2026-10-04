namespace vibeRacingOverlays.App.Core
{
    /// <summary>What a hotkey or a wheel / button box button does.</summary>
    public enum HotkeyAction
    {
        EditLayout, ToggleWidgets, NextLayout, PreviousLayout,
        Layout1, Layout2, Layout3, Layout4,
        Widget1, Widget2, Widget3, Widget4, Widget5,
        FuelCustomUp, FuelCustomDown, FuelResetAverage, FuelCustomToAverage,
    }

    /// <summary>
    /// How a binding goes off. Press: when pressed (when released if the same key or button also has a Hold action, to
    /// tell the two apart). PressRepeat: when pressed and, while held, again after 0.5 s every 0.15 s. Hold: once, after
    /// the hold time while still held.
    /// </summary>
    public enum PressMode { Press, PressRepeat, Hold }

    /// <summary>
    /// One action with its keyboard combination (WPF key names joined with "+", e.g. "Ctrl+Shift+E"; null = none) and/or
    /// a button on a wheel or button box (Device = "VID_xxxx&amp;PID_yyyy", Button = HID button number; null = none).
    /// </summary>
    public sealed class HotkeyBinding
    {
        public HotkeyAction Action { get; set; }
        public string Keys { get; set; }
        public string Device { get; set; }
        public string DeviceName { get; set; }
        public int Button { get; set; }
        public PressMode KeysMode { get; set; }
        public PressMode ButtonMode { get; set; }
    }

    public static class Hotkeys
    {
        /// <summary>The actions in the order of the Hotkeys window, with their label and default keys.</summary>
        public static readonly (HotkeyAction Action, string Label, string DefaultKeys)[] Catalog =
        {
            (HotkeyAction.EditLayout, "Edit layout on / off", "Ctrl+Shift+E"),
            (HotkeyAction.ToggleWidgets, "Show / hide all widgets", "Ctrl+Shift+H"),
            (HotkeyAction.NextLayout, "Next layout", "Ctrl+Shift+L"),
            // the new actions have no keys by default: hotkeys work in all of Windows, and most free combinations are
            // taken by other programs (Ctrl+Shift+F in VS Code, Ctrl+Shift+K in browsers...): the user picks them
            (HotkeyAction.PreviousLayout, "Previous layout", null),
            (HotkeyAction.Layout1, "Layout 1", null),
            (HotkeyAction.Layout2, "Layout 2", null),
            (HotkeyAction.Layout3, "Layout 3", null),
            (HotkeyAction.Layout4, "Layout 4", null),
            (HotkeyAction.Widget1, "Show / hide widget 1", null),
            (HotkeyAction.Widget2, "Show / hide widget 2", null),
            (HotkeyAction.Widget3, "Show / hide widget 3", null),
            (HotkeyAction.Widget4, "Show / hide widget 4", null),
            (HotkeyAction.Widget5, "Show / hide widget 5", null),
            (HotkeyAction.FuelCustomUp, "Fuel: custom per lap +0.05 L", null),
            (HotkeyAction.FuelCustomDown, "Fuel: custom per lap -0.05 L", null),
            (HotkeyAction.FuelCustomToAverage, "Fuel: custom per lap = current average", null),
            (HotkeyAction.FuelResetAverage, "Fuel: start the averages again", null),
        };

        /// <summary>
        /// The bindings of every action, in catalog order. Settings from before 1.4 only had the three keyboard
        /// hotkeys (HotkeyEditMode...): they are taken over.
        /// </summary>
        public static List<HotkeyBinding> Complete(List<HotkeyBinding> saved, AppSettings s)
        {
            var list = new List<HotkeyBinding>();
            foreach (var c in Catalog)
            {
                var b = saved != null ? saved.FirstOrDefault(x => x.Action == c.Action) : null;
                if (b == null)
                {
                    string keys = c.DefaultKeys;
                    if (saved == null)
                    {
                        if (c.Action == HotkeyAction.EditLayout) keys = s.HotkeyEditMode;
                        else if (c.Action == HotkeyAction.ToggleWidgets) keys = s.HotkeyToggleOverlays;
                        else if (c.Action == HotkeyAction.NextLayout) keys = s.HotkeyNextLayout;
                    }
                    b = new HotkeyBinding { Action = c.Action, Keys = keys };
                }
                list.Add(b);
            }
            return list;
        }

        /// <summary>Actions that may repeat while held (steps); repeating an on / off action would only flicker.</summary>
        public static bool CanRepeat(HotkeyAction a)
        {
            return a == HotkeyAction.FuelCustomUp || a == HotkeyAction.FuelCustomDown || a == HotkeyAction.NextLayout || a == HotkeyAction.PreviousLayout;
        }

        /// <summary>The physical input of a binding: "K:keys" or "B:device#button" (null = none).</summary>
        public static string KeysInput(HotkeyBinding b) { return string.IsNullOrEmpty(b.Keys) ? null : "K:" + b.Keys; }
        public static string ButtonInput(HotkeyBinding b) { return b.Device == null ? null : "B:" + b.Device + "#" + b.Button; }

        /// <summary>Every binding on one physical input (a key combination or a button), with its mode.</summary>
        public static List<(HotkeyAction Action, PressMode Mode)> On(AppSettings s, string input)
        {
            var list = new List<(HotkeyAction, PressMode)>();
            if (input == null) return list;
            foreach (var b in s.Hotkeys)
            {
                if (KeysInput(b) == input) list.Add((b.Action, b.KeysMode));
                if (ButtonInput(b) == input) list.Add((b.Action, b.ButtonMode));
            }
            return list;
        }

        public static HotkeyBinding Of(AppSettings s, HotkeyAction a) { return s.Hotkeys.FirstOrDefault(b => b.Action == a); }

        /// <summary>Keys as shown to the user: "Ctrl+Shift+1" instead of WPF's "D1".</summary>
        public static string Show(string keys)
        {
            if (string.IsNullOrEmpty(keys)) return "";
            return string.Join("+", keys.Split('+').Select(p => p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1]) ? p.Substring(1) : p));
        }

        public static string ShowButton(HotkeyBinding b)
        {
            return b == null || b.Device == null ? "" : "Button " + b.Button + (string.IsNullOrEmpty(b.DeviceName) ? "" : " (" + b.DeviceName + ")");
        }
    }
}
