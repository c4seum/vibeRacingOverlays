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
            Width = 860;
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
                + "Layouts and widgets go by their place in the lists (widget 1 = the first widget of the active layout).");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(14, 0, 10, 8);
            card.Children.Add(hint);
            notice.Margin = new Thickness(14, 0, 10, 6);
            card.Children.Add(notice);

            foreach (var c in Hotkeys.Catalog)
            {
                var b = Hotkeys.Of(settings, c.Action);
                var row = new StackPanel { Orientation = Orientation.Horizontal };

                bool capturing = capturingKeys == b;
                string keysText = capturing ? "Press the keys... (Esc: cancel)" : string.IsNullOrEmpty(b.Keys) ? "Set keys" : Hotkeys.Show(b.Keys);
                var keys = new Button { Content = keysText, Width = 230, HorizontalContentAlignment = HorizontalAlignment.Left };
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

                string buttonText = buttonCapture == b ? "Press a wheel button... (Esc: cancel)" : b.Device == null ? "Set wheel button" : Hotkeys.ShowButton(b);
                var button = new Button { Content = buttonText, Width = 260, Margin = new Thickness(14, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left };
                if (b.Device == null && buttonCapture != b) button.SetResourceReference(Control.ForegroundProperty, "Muted");
                button.Click += (s, e) => StartButtonCapture(b);
                var clearButton = Ui.IconButton("", "No wheel button");
                clearButton.IsEnabled = b.Device != null;
                clearButton.Click += (s, e) => { b.Device = null; b.DeviceName = null; b.Button = 0; Changed(); };

                row.Children.Add(keys);
                row.Children.Add(clearKeys);
                row.Children.Add(button);
                row.Children.Add(clearButton);
                Ui.Row(card, LabelOf(c.Action, c.Label), row, null, null, 250);
            }
        }

        void Changed()
        {
            overlays.ScheduleSave();
            Build();
        }

        /// <summary>A binding used twice would run two actions: the other action loses it.</summary>
        void TakeOver(HotkeyBinding b, Func<HotkeyBinding, bool> same, Action<HotkeyBinding> clear)
        {
            foreach (var other in settings.Hotkeys.Where(x => x != b && same(x)).ToList())
            {
                clear(other);
                var label = Hotkeys.Catalog.First(c => c.Action == other.Action).Label;
                notice.Text = "Moved from \"" + label + "\" to this action.";
            }
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
            TakeOver(b, x => x.Keys == combo, x => x.Keys = null);
            b.Keys = combo;
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
                TakeOver(target, x => x.Device == device && x.Button == button, x => { x.Device = null; x.DeviceName = null; x.Button = 0; });
                target.Device = device;
                target.DeviceName = name;
                target.Button = button;
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
