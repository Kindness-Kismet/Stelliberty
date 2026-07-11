using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Stelliberty.Application.Diagnostics;

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
            AppLogger.Info($"File picker preparing to open: TopLevel={topLevel.GetType().Name}, Provider={provider.GetType().FullName}, CanOpen={provider.CanOpen}");
            if (!provider.CanOpen)
            {
                AppLogger.Warning("File picker is unavailable");
                return null;
            }

            AppLogger.Info($"File picker call started: Title={title}, Filter={filterName}");
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

            AppLogger.Info($"File picker call completed: Count={files.Count}");
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "File picker open failed");
            return null;
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
            AppLogger.Info($"Save file picker preparing to open: TopLevel={topLevel.GetType().Name}, Provider={provider.GetType().FullName}, CanSave={provider.CanSave}");
            if (!provider.CanSave)
            {
                AppLogger.Warning("Save file picker is unavailable");
                return null;
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
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Save file picker open failed");
            return null;
        }
    }
}
