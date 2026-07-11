using Avalonia.Controls;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Desktop.Views.Settings;

public sealed partial class SettingsUpdateView : UserControl
{
    public SettingsUpdateView()
    {
        InitializeComponent();
    }

    private void OnOpenExternalLinkClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"External link open failed: {exception.Message}");
        }
    }
}
