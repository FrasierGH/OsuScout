using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace OsuScoutNew
{
    public enum DialogButtons { Ok, YesNo }
    public enum DialogIcon { None, Information, Question, Warning, Error }
    public enum DialogResult { Ok, Yes, No }

    // A message box in the app's own theme, on every platform.
    public partial class MessageDialog : Window
    {
        private DialogResult _result;
        private readonly string _copyText;

        public MessageDialog()   // also used by the XAML loader
        {
            InitializeComponent();
            Opened += (_, _) => Services.SystemInteropService.UseDarkTitleBar(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        }

        private MessageDialog(string message, string title, DialogButtons buttons, DialogIcon icon) : this()
        {
            Title = title;
            MessageText.Text = message;
            _copyText = $"{title}\n\n{message}";
            ShowBadge(icon);

            if (buttons == DialogButtons.YesNo)
            {
                AddButton("Yes", DialogResult.Yes, isDefault: true, isCancel: false);
                AddButton("No", DialogResult.No, isDefault: false, isCancel: true);
                _result = DialogResult.No; // closing the window answers "no"
            }
            else
            {
                AddButton("OK", DialogResult.Ok, isDefault: true, isCancel: true);
                _result = DialogResult.Ok;
            }
        }

        public static async Task<DialogResult> ShowAsync(Window owner, string message, string title = "Scoutsu",
            DialogButtons buttons = DialogButtons.Ok, DialogIcon icon = DialogIcon.None)
        {
            var dialog = new MessageDialog(message, title, buttons, icon);
            if (owner != null && owner.IsVisible)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                // Nothing to centre on (e.g. during start-up).
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                var closed = new TaskCompletionSource();
                dialog.Closed += (_, _) => closed.TrySetResult();
                dialog.Show();
                await closed.Task;
            }
            return dialog._result;
        }

        // Ctrl+C copies the whole message, as it does in a Windows message box (handy for crash
        // reports); selected text copies as usual.
        protected override async void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control) && string.IsNullOrEmpty(MessageText.SelectedText))
            {
                e.Handled = true;
                if (Clipboard != null) await Clipboard.SetTextAsync(_copyText);
            }
        }

        private void AddButton(string text, DialogResult result, bool isDefault, bool isCancel)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 84,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                IsDefault = isDefault,
                IsCancel = isCancel
            };
            if (isDefault) button.Classes.Add("primary");
            button.Click += (_, _) =>
            {
                _result = result;
                Close();
            };
            ButtonRow.Children.Add(button);
            if (isDefault) Opened += (_, _) => button.Focus();
        }

        private void ShowBadge(DialogIcon icon)
        {
            (string text, string fill) = icon switch
            {
                DialogIcon.Error => ("!", "Brush.AccentStrong"),
                DialogIcon.Warning => ("!", "Brush.Field"),
                DialogIcon.Question => ("?", "Brush.Field"),
                DialogIcon.Information => ("i", "Brush.Field"),
                _ => (null, null)
            };
            if (text == null) return;

            BadgeText.Text = text;
            BadgeText.Foreground = icon == DialogIcon.Error ? Brushes.White : Resource("Brush.Accent");
            BadgeCircle.Fill = Resource(fill);
            Badge.IsVisible = true;
        }

        private IBrush Resource(string key) =>
            this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;
    }
}
