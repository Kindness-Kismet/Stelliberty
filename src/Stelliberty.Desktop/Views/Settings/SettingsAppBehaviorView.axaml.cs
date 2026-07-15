using Avalonia.Controls;
using Avalonia.Input;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views.Settings;

public sealed partial class SettingsAppBehaviorView : UserControl
{
    public SettingsAppBehaviorView()
    {
        InitializeComponent();
    }

    private void OnWindowToggleHotkeyKeyDown(object? sender, KeyEventArgs args)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var parts = new List<string>();
        if (args.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (args.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (args.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        parts.Add(ShortcutKeyName(args.Key));

        viewModel.AppBehavior.SetWindowToggleHotkey(string.Join('+', parts));
        args.Handled = true;
    }

    private static string ShortcutKeyName(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        return key switch
        {
            Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            _ => key.ToString(),
        };
    }
}
