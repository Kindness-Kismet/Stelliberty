using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

public sealed class DesktopClipboardWriter(IClassicDesktopStyleApplicationLifetime desktop) : IClipboardWriter
{
    public void WriteText(string text)
    {
        // 剪贴板占用可能很短；异步写入避免卡住 UI 线程。
        _ = WriteTextCoreAsync(text);
    }

    private async Task WriteTextCoreAsync(string text)
    {
        try
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            await clipboard.SetTextAsync(text);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Clipboard write failed: {exception.Message}");
        }
    }
}
