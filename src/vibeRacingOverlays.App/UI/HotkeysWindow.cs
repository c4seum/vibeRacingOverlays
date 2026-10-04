using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using vibeRacingOverlays.App.Core;
using vibeRacingOverlays.App.Overlay;

namespace vibeRacingOverlays.App.UI
{
    /// <summary>
    /// One input per action (user's design, 2026-10-04): click the field and press a key combination or a wheel / button
    /// box button. Releasing it quickly makes a short press, holding it for the hold time makes a hold. Behind it a
    /// Repeat switch (short presses of step actions; greyed when the input also has a hold action). ✕ clears.
    /// While this window is open the hotkeys and buttons don't run their actions.
    /// </summary>
    public sealed class HotkeysWindow : Window
    {
        readonly AppSettings settings;
        readonly OverlayManager overlays;
        readonly StackPanel host = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        readonly TextBlock notice = Ui.Caption("");

        // the window edits a copy: nothing is saved (or used) until Save, Cancel or closing throws the changes away
        readonly List<HotkeyBinding> work;
        double holdSeconds;
        readonly string original;
        bool saved, discard;
        readonly List<TextBlock> statusTexts = new List<TextBlock>();

        HotkeyBinding capturing;      // the binding waiting for a key or button
        string downKeys;              // the key combination being held while capturing
        string downDevice, downName;  // the button being held while capturing
        int downButton;
        DispatcherTimer holdTimer, timeout;

        HotkeysWindow(AppSettings settings, OverlayManager overlays)
        {
            this.settings = settings;
            this.overlays = overlays;
            work = settings.Hotkeys.Select(Clone).ToList();
            holdSeconds = settings.HoldSeconds;
            original = Signature();
            Title = "Hotkeys";
            Width = 900;
            Height = 780;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "Bg");
            SetResourceReference(ForegroundProperty, "Fg");
            FontFamily = (System.Windows.Media.FontFamily)FindResource("UiFont");
            FontSize = 13;
            // Save / Cancel above and below the list, so it's clear the hotkeys only count once saved
            var root = new DockPanel();
            var top = Bar(false); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            var bottom = Bar(true); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
            root.Children.Add(new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = root;
            notice.TextWrapping = TextWrapping.Wrap;
            notice.SetResourceReference(TextBlock.ForegroundProperty, "Warning");

            PreviewKeyDown += OnKeyDown;
            PreviewKeyUp += OnKeyUp;
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);
            overlays.SuspendHotkeys();
            Closing += OnClosing;
            Closed += (s, e) => { StopCapture(); overlays.ReloadHotkeys(); };
            Build();
            UpdateStatus();
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
            var card = Ui.Card(host, Ui.Header("Hotkeys and wheel buttons", null));   // explanation, messages and the hold time
            var hint = Ui.Caption("Click a field, then press a key combination (for example Ctrl+Shift+F5) or a button on your wheel or button box. "
                + "Let go quickly for a short press, keep it pressed for a hold (the hold time below). A key or button can have one short press and one hold action. "
                + "Repeat: a short press that goes on while you hold it (steps only). Keys work everywhere in Windows, so pick combinations other programs don't use. "
                + "Layouts and widgets go by their place in the lists (widget 1 = the first widget of the active layout).");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(14, 0, 10, 8);
            card.Children.Add(hint);
            notice.Margin = new Thickness(14, 0, 10, 6);
            // the notice survives rebuilds (its text is set before Build): take it out of the previous card first, an
            // element can have only one parent (adding it again crashed the app on every rebuild)
            var oldParent = notice.Parent as Panel;
            if (oldParent != null) oldParent.Children.Remove(notice);
            card.Children.Add(notice);

            var times = new[] { 0.4, 0.5, 0.6, 0.8, 1.0 };
            var hold = new ComboBox { Width = 120, ItemsSource = times.Select(t => t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s").ToList(), HorizontalAlignment = HorizontalAlignment.Left };
            hold.SelectedIndex = Math.Max(0, Array.IndexOf(times, times.OrderBy(t => Math.Abs(t - holdSeconds)).First()));
            hold.SelectionChanged += (s, e) => { holdSeconds = times[hold.SelectedIndex]; UpdateStatus(); };
            Ui.Row(card, "Hold time", hold, "How long a key or button must be pressed to count as a hold", null, 260);

            // a block per group: widgets, layouts, fuel calculator
            foreach (var group in Hotkeys.Catalog.GroupBy(x => x.Group))
            {
                var groupCard = Ui.Card(host, Ui.Header(group.Key, null));
                foreach (var c in group)
                {
                    var b = work.First(x => x.Action == c.Action);
                    var row = new StackPanel { Orientation = Orientation.Horizontal };
    
                    string text = capturing == b ? "Press a key or button...  (Esc: cancel)" : Hotkeys.Input(b) == null ? "Set key or button" : Hotkeys.Describe(b);
                    // long device names end in "...": the press kind comes before the name, so it always shows
                    var field = new Button { Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis }, Width = 330, HorizontalContentAlignment = HorizontalAlignment.Left };
                    if (Hotkeys.Input(b) == null && capturing != b) field.SetResourceReference(Control.ForegroundProperty, "Muted");
                    if (overlays.KeysInUse.Contains(c.Action))
                    {
                        field.SetResourceReference(Control.ForegroundProperty, "Danger");
                        field.ToolTip = "Another program already uses this key combination: choose another one";
                    }
                    if (field.ToolTip == null && Hotkeys.Input(b) != null && capturing != b) field.ToolTip = Hotkeys.Describe(b) + "\nClick to set another key or button";
                    if (field.ToolTip == null) field.ToolTip = "Click, then press a key combination or a wheel button: let go quickly for a short press, keep it pressed for a hold";
                field.Click += (s, e) => StartCapture(b);
    
                    var repeat = new CheckBox { Content = "Repeat", Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsChecked = b.Repeat && !b.Hold };
                    string why = RepeatBlocked(b);
                    repeat.IsEnabled = why == null;
                    repeat.ToolTip = why ?? "Goes on while you hold the key or button (again after 0.4 s, then about 14 times a second)";
                    ToolTipService.SetShowOnDisabled(repeat, true);
                    // Checked/Unchecked instead of Click: also fires for keyboard and UI Automation
                    repeat.Checked += (s, e) => { if (!b.Repeat) { b.Repeat = true; Changed(); } };
                    repeat.Unchecked += (s, e) => { if (b.Repeat) { b.Repeat = false; Changed(); } };
    
                    var clear = Ui.IconButton("", "Remove the key or button");
                    clear.Margin = new Thickness(10, 0, 0, 0);
                    clear.IsEnabled = Hotkeys.Input(b) != null;
                    clear.Click += (s, e) => { Clear(b); Changed(); };
    
                    row.Children.Add(field);
                    row.Children.Add(repeat);
                    row.Children.Add(clear);
                    Ui.Row(groupCard, LabelOf(c.Action, c.Label), row, Hotkeys.Tooltip(c.Action), null, 260);
                }
            }
        }

        /// <summary>Why Repeat can't be switched on for this binding (null = it can).</summary>
        string RepeatBlocked(HotkeyBinding b)
        {
            if (!Hotkeys.CanRepeat(b.Action)) return "Only steps repeat (fuel +/- and next / previous layout)";
            if (Hotkeys.Input(b) == null) return "Set a key or button first";
            if (b.Hold) return "A hold doesn't repeat: it goes off once after the hold time";
            var other = work.FirstOrDefault(o => o != b && o.Hold && Hotkeys.Input(o) == Hotkeys.Input(b));
            if (other != null) return "This key or button also holds \"" + LabelOf(other.Action) + "\": a short press on it goes off on release, so it can't repeat";
            return null;
        }

        string LabelOf(HotkeyAction a) { return Hotkeys.Catalog.First(c => c.Action == a).Label; }

        static void Clear(HotkeyBinding b) { b.Keys = null; b.Device = null; b.DeviceName = null; b.Button = 0; b.Hold = false; b.Repeat = false; }

        void Changed()
        {
            Build();
            UpdateStatus();
        }

        static HotkeyBinding Clone(HotkeyBinding b)
        {
            return new HotkeyBinding { Action = b.Action, Keys = b.Keys, Device = b.Device, DeviceName = b.DeviceName, Button = b.Button, Hold = b.Hold, Repeat = b.Repeat };
        }

        string Signature()
        {
            return string.Join(";", work.Select(b => b.Action + "|" + b.Keys + "|" + b.Device + "|" + b.Button + "|" + b.Hold + "|" + b.Repeat)) + ";" + holdSeconds;
        }

        bool Dirty { get { return Signature() != original; } }

        // ------------------------------------------------------------ save / cancel

        /// <summary>Status text with Cancel and Save, above (bottom = false) or below the list.</summary>
        Border Bar(bool bottom)
        {
            var cancel = new Button { Content = "Cancel", Style = Ui.Style("GhostButton"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Close without saving: the hotkeys stay as they were" };
            cancel.Click += (s, e) => { discard = true; Close(); };
            var save = new Button { Content = "Save", Style = Ui.Style("PrimaryButton"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Save the hotkeys and close: they work right away" };
            save.Click += (s, e) => { Save(); Close(); };
            DockPanel.SetDock(save, Dock.Right);
            DockPanel.SetDock(cancel, Dock.Right);
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            statusTexts.Add(status);
            var row = new DockPanel { LastChildFill = true };
            row.Children.Add(save);
            row.Children.Add(cancel);
            row.Children.Add(status);
            var bar = new Border { Child = row, Padding = new Thickness(20, 10, 20, 10), BorderThickness = bottom ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1) };
            bar.SetResourceReference(Border.BackgroundProperty, "Panel");
            bar.SetResourceReference(Border.BorderBrushProperty, "Divider");
            return bar;
        }

        void UpdateStatus()
        {
            bool dirty = Dirty;
            foreach (var t in statusTexts)
            {
                t.Text = dirty ? "Changes not saved yet" : "No unsaved changes";
                t.SetResourceReference(TextBlock.ForegroundProperty, dirty ? "Warning" : "Muted");
            }
        }

        void Save()
        {
            StopCapture();
            settings.Hotkeys = work.Select(Clone).ToList();
            settings.HoldSeconds = holdSeconds;
            overlays.ScheduleSave();
            saved = true;
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (saved || discard || !Dirty) return;   // the window's ✕ or Esc with changes: ask
            var r = MessageBox.Show(this, "Save the changed hotkeys?", "Hotkeys", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) Save();
            else if (r == MessageBoxResult.Cancel) e.Cancel = true;
        }

        // ------------------------------------------------------------ capturing a key or button

        void StartCapture(HotkeyBinding b)
        {
            StopCapture();
            capturing = b;
            notice.Text = "";
            overlays.CaptureButton = (device, name, button) =>
            {
                if (capturing == null || downKeys != null || downDevice != null) return;
                downDevice = device; downName = name; downButton = button;
                StartHoldTimer();
            };
            overlays.CaptureButtonUp = (device, button) =>
            {
                if (capturing != null && downDevice == device && downButton == button) Finish(false);
            };
            // nothing pressed: give up after 15 seconds
            timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timeout.Tick += (s, e) => { StopCapture(); notice.Text = "Nothing was pressed. Is the wheel or button box connected?"; Build(); };
            timeout.Start();
            Build();
        }

        void StartHoldTimer()
        {
            holdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.2, holdSeconds)) };
            holdTimer.Tick += (s, e) => Finish(true);   // still pressed after the hold time: a hold (no need to wait for the release)
            holdTimer.Start();
        }

        void StopCapture()
        {
            if (holdTimer != null) { holdTimer.Stop(); holdTimer = null; }
            if (timeout != null) { timeout.Stop(); timeout = null; }
            capturing = null;
            downKeys = null; downDevice = null; downName = null;
            overlays.CaptureButton = null;
            overlays.CaptureButtonUp = null;
        }

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (capturing == null)
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }   // like the window's ✕ (asks when something changed)
                return;
            }
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { StopCapture(); Build(); return; }
            if (downKeys != null || downDevice != null) return;   // key repeat of the held key
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
            downKeys = string.Join("+", parts);
            StartHoldTimer();
        }

        void OnKeyUp(object sender, KeyEventArgs e)
        {
            if (capturing == null || downKeys == null) return;
            e.Handled = true;
            Finish(false);   // released before the hold time: a short press
        }

        /// <summary>
        /// The key or button is set: a short press or a hold. Another action with the same kind on the same input loses
        /// it; a repeat on the same input as a hold is switched off.
        /// </summary>
        void Finish(bool hold)
        {
            var b = capturing;
            string keys = downKeys, device = downDevice, name = downName;
            int button = downButton;
            StopCapture();
            if (b == null || (keys == null && device == null)) return;

            Clear(b);
            if (device != null) { b.Device = device; b.DeviceName = name; b.Button = button; }
            else b.Keys = keys;
            b.Hold = hold;
            notice.Text = "";
            foreach (var o in work.Where(o => o != b && Hotkeys.Input(o) == Hotkeys.Input(b)).ToList())
            {
                if (o.Hold == b.Hold)
                {
                    Clear(o);
                    notice.Text = "Moved from \"" + LabelOf(o.Action) + "\" to this action.";
                }
                else if (o.Repeat && b.Hold)
                {
                    o.Repeat = false;
                    notice.Text = "\"" + LabelOf(o.Action) + "\" no longer repeats: its key or button now also has a hold action.";
                }
            }
            Changed();
        }
    }
}
