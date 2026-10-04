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
    /// One action with ONE input (user's wish, 2026-10-04): a key combination (WPF key names joined with "+", e.g.
    /// "Ctrl+Shift+E") or a button on a wheel or button box (Device = "VID_xxxx&amp;PID_yyyy", Button = HID button number).
    /// Hold: goes off when held (recognised while setting it: released quickly = short press, held = hold).
    /// Repeat: a short press that repeats while held (steps only, never on an input that has a hold action).
    /// </summary>
    public sealed class HotkeyBinding
    {
        public HotkeyAction Action { get; set; }
        public string Keys { get; set; }
        public string Device { get; set; }
        public string DeviceName { get; set; }
        public int Button { get; set; }
        public bool Hold { get; set; }
        public bool Repeat { get; set; }

        // DEV builds of 2026-10-04 had a mode per keys and per button; read once (see Hotkeys.Complete)
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public PressMode? KeysMode { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public PressMode? ButtonMode { get; set; }
    }

    public static class Hotkeys
    {
        /// <summary>
        /// The actions in the order of the Hotkeys window, in blocks (Group). None has keys by default: hotkeys work in all
        /// of Windows and would take combinations from other programs; the user picks them (user's wish, 2026-10-04).
        /// </summary>
        public static readonly (HotkeyAction Action, string Group, string Label)[] Catalog =
        {
            (HotkeyAction.EditLayout, "Widgets", "Edit layout on / off"),
            (HotkeyAction.ToggleWidgets, "Widgets", "Show / hide all widgets"),
            (HotkeyAction.Widget1, "Widgets", "Show / hide widget 1"),
            (HotkeyAction.Widget2, "Widgets", "Show / hide widget 2"),
            (HotkeyAction.Widget3, "Widgets", "Show / hide widget 3"),
            (HotkeyAction.Widget4, "Widgets", "Show / hide widget 4"),
            (HotkeyAction.Widget5, "Widgets", "Show / hide widget 5"),
            (HotkeyAction.NextLayout, "Layouts", "Next layout"),
            (HotkeyAction.PreviousLayout, "Layouts", "Previous layout"),
            (HotkeyAction.Layout1, "Layouts", "Layout 1"),
            (HotkeyAction.Layout2, "Layouts", "Layout 2"),
            (HotkeyAction.Layout3, "Layouts", "Layout 3"),
            (HotkeyAction.Layout4, "Layouts", "Layout 4"),
            (HotkeyAction.FuelCustomUp, "Fuel calculator", "Increase custom"),
            (HotkeyAction.FuelCustomDown, "Fuel calculator", "Decrease custom"),
            (HotkeyAction.FuelCustomToAverage, "Fuel calculator", "Set custom"),
            (HotkeyAction.FuelResetAverage, "Fuel calculator", "Reset average"),
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
                    string keys = null;
                    if (saved == null)
                    {
                        if (c.Action == HotkeyAction.EditLayout) keys = s.HotkeyEditMode;
                        else if (c.Action == HotkeyAction.ToggleWidgets) keys = s.HotkeyToggleOverlays;
                        else if (c.Action == HotkeyAction.NextLayout) keys = s.HotkeyNextLayout;
                    }
                    b = new HotkeyBinding { Action = c.Action, Keys = keys };
                }
                // one input per action: a wheel button wins over keys (DEV builds allowed both); the old per-input mode
                if (b.KeysMode != null || b.ButtonMode != null)
                {
                    var m = b.Device != null ? b.ButtonMode : b.KeysMode;
                    b.Hold = m == PressMode.Hold;
                    b.Repeat = m == PressMode.PressRepeat;
                    b.KeysMode = b.ButtonMode = null;
                }
                if (b.Device != null) b.Keys = null;
                list.Add(b);
            }
            // taken over: not written again (and never read again)
            s.HotkeyEditMode = s.HotkeyToggleOverlays = s.HotkeyNextLayout = null;
            return list;
        }

        /// <summary>What an action does, when its short name needs it (tooltip in the Hotkeys window).</summary>
        public static string Tooltip(HotkeyAction a)
        {
            switch (a)
            {
                case HotkeyAction.FuelCustomUp: return "Custom fuel per lap +0.01 L (a fuel saving target). From \"average\" it starts at your current average.";
                case HotkeyAction.FuelCustomDown: return "Custom fuel per lap -0.01 L. From \"average\" it starts at your current average.";
                case HotkeyAction.FuelCustomToAverage: return "Custom fuel per lap = your current average";
                case HotkeyAction.FuelResetAverage: return "Start the fuel averages again (after a caution or an incident skewed them)";
                default: return null;
            }
        }

        /// <summary>Actions that may repeat while held (steps); repeating an on / off action would only flicker.</summary>
        public static bool CanRepeat(HotkeyAction a)
        {
            return a == HotkeyAction.FuelCustomUp || a == HotkeyAction.FuelCustomDown || a == HotkeyAction.NextLayout || a == HotkeyAction.PreviousLayout;
        }

        /// <summary>The physical input of a binding: "K:keys" or "B:device#button" (null = none).</summary>
        public static string Input(HotkeyBinding b)
        {
            if (b.Device != null) return "B:" + b.Device + "#" + b.Button;
            return string.IsNullOrEmpty(b.Keys) ? null : "K:" + b.Keys;
        }

        /// <summary>How the binding goes off (for the engine): hold, short press with repeat, or short press.</summary>
        public static PressMode Mode(HotkeyBinding b) { return b.Hold ? PressMode.Hold : b.Repeat && CanRepeat(b.Action) ? PressMode.PressRepeat : PressMode.Press; }

        /// <summary>Every binding on one physical input (a key combination or a button), with its mode.</summary>
        public static List<(HotkeyAction Action, PressMode Mode)> On(AppSettings s, string input)
        {
            var list = new List<(HotkeyAction, PressMode)>();
            if (input == null) return list;
            foreach (var b in s.Hotkeys) if (Input(b) == input) list.Add((b.Action, Mode(b)));
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

        /// <summary>What a binding is, for the Hotkeys window: "Ctrl+Shift+F9", "Button 5  ·  hold  (Podium Wheel)".</summary>
        public static string Describe(HotkeyBinding b)
        {
            string what = b.Device != null ? "Button " + b.Button : Show(b.Keys);
            if (string.IsNullOrEmpty(what)) return "";
            // the device name last: it is long and may be cut off in the field, "hold" must stay visible
            return what + (b.Hold ? "  ·  hold" : "") + (b.Device != null && !string.IsNullOrEmpty(b.DeviceName) ? "  (" + b.DeviceName + ")" : "");
        }
    }
}
