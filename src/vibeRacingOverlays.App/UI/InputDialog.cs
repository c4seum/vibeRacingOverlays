using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace vibeRacingOverlays.App.UI
{
    /// <summary>Small themed text prompt (WPF has none built in).</summary>
    public sealed class InputDialog : Window
    {
        readonly TextBox box;

        InputDialog(string title, string prompt, string initial)
        {
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SetResourceReference(BackgroundProperty, "Panel");
            SetResourceReference(ForegroundProperty, "Fg");

            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
            box = new TextBox { Text = initial ?? "", Margin = new Thickness(0, 0, 0, 14) };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) Accept(); };
            stack.Children.Add(box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 70 };
            ok.Click += (s, e) => Accept();
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 70, Margin = new Thickness(0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            stack.Children.Add(buttons);
            Content = stack;

            Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
        }

        void Accept()
        {
            if (string.IsNullOrWhiteSpace(box.Text)) return;
            DialogResult = true;
        }

        /// <summary>Returns the entered text, or null when cancelled.</summary>
        public static string Ask(Window owner, string title, string prompt, string initial)
        {
            var d = new InputDialog(title, prompt, initial) { Owner = owner };
            return d.ShowDialog() == true ? d.box.Text.Trim() : null;
        }
    }
}
