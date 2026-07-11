using Avalonia.Controls;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Desktop.Localization;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views.Settings;

public sealed partial class SettingsAppLogView : UserControl
{
    public SettingsAppLogView()
    {
        InitializeComponent();
    }

    private async void OnExportAppLogsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (sender is not Button button
            || DataContext is not MainWindowViewModel viewModel
            || TopLevel.GetTopLevel(button) is not { } topLevel)
        {
            return;
        }

        try
        {
            var exportPath = await LocalFilePicker.PickSaveFileAsync(
                topLevel,
                Localize("AppLogs.SavePicker.Title"),
                $"{AppRuntimeNames.FileNameToken}-applog-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log",
                Localize("AppLogs.SavePicker.Filter"),
                ["*.log", "*.txt"],
                "log");
            if (string.IsNullOrWhiteSpace(exportPath))
            {
                return;
            }

            await viewModel.AppLog.ExportToFileAsync(exportPath);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "App log save picker failed");
        }
    }

    private static string Localize(string key) => LocalizationManager.Translate(key);
}
