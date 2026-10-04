using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Overlay;

namespace vibeRacingOverlays.App.UI
{
    /// <summary>
    /// Keys and wheel / button box buttons for every action. Click a field and press the keys or the button; ✕ clears.
    /// While this window is open the hotkeys and buttons don't run their actions (so setting Ctrl+Shift+E doesn't also
    /// toggle edit mode).
    /// </summary>
    public sealed class HotkeysWindow : Window
    {
        readonly AppSettings settings;
        readonly OverlayManager overlays;
        readonly StackPanel host = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        readonly TextBlock notice = Ui.Caption("");
        HotkeyBinding capturingKeys;
        DispatcherTimer buttonTimeout;

        HotkeysWindow(AppSettings settings, OverlayManager overlays)
        {
            this.settings = settings;
            this.overlays = overlays;
            Title = "Hotkeys";
            Width = 1130;
            Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "Bg");
            SetResourceReference(ForegroundProperty, "Fg");
            FontFamily = (System.Windows.Media.FontFamily)FindResource("UiFont");
            FontSize = 13;
            Content = new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            notice.TextWrapping = TextWrapping.Wrap;
            notice.SetResourceReference(TextBlock.ForegroundProperty, "Warning");

            PreviewKeyDown += OnKey;
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);
            overlays.SuspendHotkeys();
            Closed += (s, e) => { StopButtonCapture(); overlays.ReloadHotkeys(); };
            Build();
        }

        public static void Show(Window owner, AppSettings settings, OverlayManager overlays)
        {
            new HotkeysWindow(settings, overlays) { Owner = owner }.ShowDialog();
        }

        string LabelOf(HotkeyAction a, string label)
        {
            // layouts and widgets by their place in the list: show which one that is now
            if (a >= HotkeyAction.Layout1 && a <= HotkeyAction.Layout4)
            {
                int i = a - HotkeyAction.Layout1;
                return label + (i < settings.Layouts.Count ? "  (" + settings.Layouts[i].Name + ")" : "");
            }
            if (a >= HotkeyAction.Widget1 && a <= HotkeyAction.Widget5)
            {
                int i = a - HotkeyAction.Widget1;
                return label + (i < settings.Widgets.Count ? "  (" + settings.Widgets[i].Title + ")" : "");
            }
            return label;
        }

        void Build()
        {
            host.Children.Clear();
            var card = Ui.Card(host, Ui.Header("Hotkeys and wheel buttons", null));
            var hint = Ui.Caption("Click a field, then press the keys (for example Ctrl+Shift+F5) or a button on your wheel or button box. "
                + "Keys work everywhere in Windows, so pick combinations other programs don't use. Wheel buttons work while iRacing is in front. "
                + "Short press: right away (on release when the same key or button also has a Hold action). Repeat: again while you hold it. "
                + "Hold: after the hold time. Layouts and widgets go by their place in the lists (widget 1 = the first widget of the active layout).");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(14, 0, 10, 8);
            card.Children.Add(hint);
            notice.Margin = new Thickness(14, 0, 10, 6);
            card.Children.Add(notice);

            var times = new[] { 0.4, 0.5, 0.6, 0.8, 1.0 };
            var hold = new ComboBox { Width = 120, ItemsSource = times.Select(t => t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s").ToList(), HorizontalAlignment = HorizontalAlignment.Left };
            hold.SelectedIndex = Math.Max(0, Array.IndexOf(times, times.OrderBy(t => Math.Abs(t - settings.HoldSeconds)).First()));
            hold.SelectionChanged += (s, e) => { settings.HoldSeconds = times[hold.SelectedIndex]; overlays.ScheduleSave(); };
            Ui.Row(card, "Hold time", hold, "How long a key or button must be held for a Hold action", null, 250);

            foreach (var c in Hotkeys.Catalog)
            {
                var b = Hotkeys.Of(settings, c.Action);
                var row = new StackPanel { Orientation = Orientation.Horizontal };

                bool capturing = capturingKeys == b;
                string keysText = capturing ? "Press the keys... (Esc)" : string.IsNullOrEmpty(b.Keys) ? "Set keys" : Hotkeys.Show(b.Keys);
                var keys = new Button { Content = keysText, Width = 180, HorizontalContentAlignment = HorizontalAlignment.Left };
                if (string.IsNullOrEmpty(b.Keys) && !capturing) keys.SetResourceReference(Control.ForegroundProperty, "Muted");
                if (overlays.KeysInUse.Contains(c.Action))
                {
                    keys.SetResourceReference(Control.ForegroundProperty, "Danger");
                    keys.ToolTip = "Another program already uses this combination: choose another one";
                }
                keys.Click += (s, e) => { StopButtonCapture(); capturingKeys = b; Build(); };
                var clearKeys = Ui.IconButton("", "No keys");
                clearKeys.IsEnabled = !string.IsNullOrEmpty(b.Keys);
                clearKeys.Click += (s, e) => { b.Keys = null; Changed(); };

                string buttonText = buttonCapture == b ? "Press a wheel button... (Esc)" : b.Device == null ? "Set wheel button" : Hotkeys.ShowButton(b);
                var button = new Button { Content = buttonText, Width = 220, Margin = new Thickness(18, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
                if (b.Device == null && buttonCapture != b) button.SetResourceReference(Control.ForegroundProperty, "Muted");
                button.Click += (s, e) => StartButtonCapture(b);
                var clearButton = Ui.IconButton("", "No wheel button");
                clearButton.IsEnabled = b.Device != null;
                clearButton.Click += (s, e) => { b.Device = null; b.DeviceName = null; b.Button = 0; Changed(); };

                row.Children.Add(keys);
                row.Children.Add(ModeBox(b, true));
                row.Children.Add(clearKeys);
                row.Children.Add(button);
                row.Children.Add(ModeBox(b, false));
                row.Children.Add(clearButton);
                Ui.Row(card, LabelOf(c.Action, c.Label), row, null, null, 250);
            }
        }

        // ------------------------------------------------------------ short press / repeat / hold

        static readonly (PressMode Mode, string Label)[] Modes = { (PressMode.Press, "Short press"), (PressMode.PressRepeat, "Short, repeat"), (PressMode.Hold, "Hold") };

        static PressMode ModeOf(HotkeyBinding b, bool keysSide) { return keysSide ? b.KeysMode : b.ButtonMode; }
        static void SetMode(HotkeyBinding b, bool keysSide, PressMode m) { if (keysSide) b.KeysMode = m; else b.ButtonMode = m; }
        static string InputOf(HotkeyBinding b, bool keysSide) { return keysSide ? Hotkeys.KeysInput(b) : Hotkeys.ButtonInput(b); }

        /// <summary>The other bindings on the same key combination or button (and which side of them).</summary>
        IEnumerable<(HotkeyBinding B, bool KeysSide)> Others(HotkeyBinding b, bool keysSide)
        {
            string input = InputOf(b, keysSide);
            if (input == null) yield break;
            foreach (var o in settings.Hotkeys)
            {
                if (o == b) continue;
                if (Hotkeys.KeysInput(o) == input) yield return (o, true);
                if (Hotkeys.ButtonInput(o) == input) yield return (o, false);
            }
        }

        /// <summary>
        /// How this binding goes off. Options that would clash with another action on the same key or button are greyed:
        /// one short press and one hold per key or button, and repeat can't share a key or button with a hold.
        /// </summary>
        ComboBox ModeBox(HotkeyBinding b, bool keysSide)
        {
            var others = Others(b, keysSide).ToList();
            bool otherPress = others.Any(o => ModeOf(o.B, o.KeysSide) != PressMode.Hold);
            bool otherHold = others.Any(o => ModeOf(o.B, o.KeysSide) == PressMode.Hold);
            bool otherRepeat = others.Any(o => ModeOf(o.B, o.KeysSide) == PressMode.PressRepeat);
            string what = keysSide ? "key combination" : "button";
            var box = new ComboBox { Width = 120, Margin = new Thickness(4, 0, 0, 0) };
            ToolTipService.SetShowOnDisabled(box, true);
            foreach (var m in Modes)
            {
                string why = null;
                if (m.Mode == PressMode.PressRepeat && !Hotkeys.CanRepeat(b.Action)) why = "This action doesn't repeat (only steps do: fuel +/- and next / previous layout)";
                else if (m.Mode != PressMode.Hold && otherPress) why = "Another action already has a short press on this " + what;
                else if (m.Mode == PressMode.PressRepeat && otherHold) why = "Repeat can't share a " + what + " with a Hold action";
                else if (m.Mode == PressMode.Hold && otherHold) why = "Another action already holds this " + what;
                else if (m.Mode == PressMode.Hold && otherRepeat) why = "Hold can't share a " + what + " with a repeating action";
                var item = new ComboBoxItem { Content = m.Label, Tag = m.Mode, IsEnabled = why == null, ToolTip = why };
                ToolTipService.SetShowOnDisabled(item, true);
                box.Items.Add(item);
            }
            box.SelectedIndex = Array.FindIndex(Modes, m => m.Mode == ModeOf(b, keysSide));
            box.SelectionChanged += (s, e) =>
            {
                var item = box.SelectedItem as ComboBoxItem;
                if (item == null) return;
                SetMode(b, keysSide, (PressMode)item.Tag);
                Changed();
            };
            return box;
        }

        /// <summary>
        /// After a key combination or button was set: another action with the same kind (short press, or hold) on it loses
        /// it, and repeat and hold can't share it (repeat becomes a plain short press).
        /// </summary>
        void Resolve(HotkeyBinding b, bool keysSide)
        {
            var mode = ModeOf(b, keysSide);
            if (mode == PressMode.PressRepeat && !Hotkeys.CanRepeat(b.Action)) SetMode(b, keysSide, mode = PressMode.Press);
            bool isHold = mode == PressMode.Hold;
            string what = keysSide ? "keys." : "button.";
            foreach (var o in Others(b, keysSide).ToList())
            {
                var om = ModeOf(o.B, o.KeysSide);
                var label = "\"" + Hotkeys.Catalog.First(c => c.Action == o.B.Action).Label + "\"";
                if ((om == PressMode.Hold) == isHold)
                {
                    if (o.KeysSide) o.B.Keys = null; else { o.B.Device = null; o.B.DeviceName = null; o.B.Button = 0; }
                    notice.Text = "Moved from " + label + " to this action.";
                }
                else if (isHold && om == PressMode.PressRepeat)
                {
                    SetMode(o.B, o.KeysSide, PressMode.Press);
                    notice.Text = label + " no longer repeats: it shares the same " + what;
                }
                else if (mode == PressMode.PressRepeat && om == PressMode.Hold)
                {
                    SetMode(b, keysSide, PressMode.Press);
                    notice.Text = "This action no longer repeats: " + label + " holds the same " + what;
                }
            }
        }

        void Changed()
        {
            overlays.ScheduleSave();
            Build();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (capturingKeys == null) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { capturingKeys = null; Build(); return; }
            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LWin || key == Key.RWin) return;   // wait for the actual key
            var mods = Keyboard.Modifiers;
            bool fkey = key >= Key.F1 && key <= Key.F24;
            if (mods == ModifierKeys.None && !fkey)
            {
                notice.Text = "Use Ctrl, Alt or Shift with that key (or an F-key): a single key would be taken from every program, also while typing.";
                return;
            }
            var parts = new List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            string combo = string.Join("+", parts);
            var b = capturingKeys;
            capturingKeys = null;
            notice.Text = "";
            b.Keys = combo;
            Resolve(b, true);
            Changed();
        }

        HotkeyBinding buttonCapture;

        void StartButtonCapture(HotkeyBinding b)
        {
            capturingKeys = null;
            buttonCapture = b;
            notice.Text = "";
            overlays.CaptureButton = (device, name, button) =>
            {
                var target = buttonCapture;
                StopButtonCapture();
                if (target == null) return;
                target.Device = device;
                target.DeviceName = name;
                target.Button = button;
                Resolve(target, false);
                Changed();
            };
            // no wheel connected, or no press: give up after 15 seconds
            buttonTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            buttonTimeout.Tick += (s, e) => { StopButtonCapture(); notice.Text = "No wheel or button box button was pressed. Is it connected?"; Build(); };
            buttonTimeout.Start();
            Build();
        }

        void StopButtonCapture()
        {
            if (buttonTimeout != null) { buttonTimeout.Stop(); buttonTimeout = null; }
            buttonCapture = null;
            overlays.CaptureButton = null;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (buttonCapture != null && e.Key == Key.Escape) { StopButtonCapture(); Build(); e.Handled = true; return; }
            base.OnPreviewKeyDown(e);
        }
    }
}
