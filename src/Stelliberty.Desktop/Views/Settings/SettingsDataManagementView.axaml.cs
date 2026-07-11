using Avalonia.Controls;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Desktop.Localization;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views.Settings;

public sealed partial class SettingsDataManagementView : UserControl
{
    public SettingsDataManagementView()
    {
        InitializeComponent();
    }

    private async void OnCreateBackupClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            if (!TryGetViewModelAndTopLevel(sender, out var viewModel, out var topLevel))
            {
                return;
            }

            var backupPath = await LocalFilePicker.PickSaveFileAsync(
                topLevel,
                Localize("Settings.Data.SavePicker.Title"),
                $"{AppRuntimeNames.FileNameToken}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.{AppRuntimeNames.FileNameToken}",
                Localize("Settings.Data.FilePicker.Filter"),
                [$"*.{AppRuntimeNames.FileNameToken}"],
                AppRuntimeNames.FileNameToken);
            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                viewModel.DataManagement.CreateBackupToFile(backupPath);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Backup save picker failed");
        }
    }

    private async void OnRestoreFromFileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            if (!TryGetViewModelAndTopLevel(sender, out var viewModel, out var topLevel))
            {
                return;
            }

            var filePath = await LocalFilePicker.PickFileAsync(
                topLevel,
                Localize("Settings.Data.FilePicker.Title"),
                Localize("Settings.Data.FilePicker.Filter"),
                [$"*.{AppRuntimeNames.FileNameToken}", "*.zip"]);
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                viewModel.DataManagement.BeginRestoreFromFile(filePath);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Backup file picker failed");
        }
    }

    private bool TryGetViewModelAndTopLevel(object? sender, out MainWindowViewModel viewModel, out TopLevel topLevel)
    {
        viewModel = null!;
        topLevel = null!;
        if (sender is not Button button)
        {
            return false;
        }

        viewModel = DataContext as MainWindowViewModel ?? null!;
        if (viewModel is null || TopLevel.GetTopLevel(button) is not { } resolvedTopLevel)
        {
            return false;
        }

        topLevel = resolvedTopLevel;
        return true;
    }

    private static string Localize(string key) => LocalizationManager.Translate(key);
}
