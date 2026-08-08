using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Desktop.Localization;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

internal static class LocalFilePicker
{
    public static async Task<string?> PickFileAsync(
        TopLevel topLevel,
        string title,
        string filterName,
        IReadOnlyList<string> patterns)
    {
        try
        {
            // StorageProvider 必须绑定当前 TopLevel，不能脱离窗口调用。
            var provider = topLevel.StorageProvider;
            AppLogger.Info($"File picker preparing to open: topLevel={topLevel.GetType().Name} provider={provider.GetType().FullName} canOpen={provider.CanOpen}");
            if (!provider.CanOpen)
            {
                throw new InvalidOperationException("File picker is unavailable");
            }

            AppLogger.Info($"File picker call started: title={title} filter={filterName}");
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(filterName) { Patterns = patterns },
                    FilePickerFileTypes.All
                ]
            });

            AppLogger.Info($"File picker call completed: count={files.Count}");
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
        catch (Exception)
        {
            ShowFailure(topLevel);
            throw;
        }
    }

    public static async Task<string?> PickSaveFileAsync(
        TopLevel topLevel,
        string title,
        string suggestedFileName,
        string filterName,
        IReadOnlyList<string> patterns,
        string defaultExtension)
    {
        try
        {
            var provider = topLevel.StorageProvider;
            AppLogger.Info($"Save file picker preparing to open: topLevel={topLevel.GetType().Name} provider={provider.GetType().FullName} canSave={provider.CanSave}");
            if (!provider.CanSave)
            {
                throw new InvalidOperationException("Save file picker is unavailable");
            }

            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = defaultExtension,
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new FilePickerFileType(filterName) { Patterns = patterns },
                    FilePickerFileTypes.All
                ]
            });

            return file?.TryGetLocalPath();
        }
        catch (Exception)
        {
            ShowFailure(topLevel);
            throw;
        }
    }

    private static void ShowFailure(TopLevel topLevel)
    {
        if (topLevel.DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ShowErrorToast(LocalizationManager.Translate("Common.Error.FilePickerFailed"));
        }
    }
}
