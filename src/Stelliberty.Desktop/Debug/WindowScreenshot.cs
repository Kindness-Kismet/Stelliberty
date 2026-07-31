#if DEBUG
using Avalonia;
using Avalonia.Media.Imaging;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Debug;

internal static class WindowScreenshot
{
    public static void Save(MainWindow window)
    {
        var pixelSize = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            return;
        }

        var screenshotDirectory = Path.Combine(GetProjectRoot(), "build", "screenshots");
        Directory.CreateDirectory(screenshotDirectory);
        var screenshotPath = Path.Combine(screenshotDirectory, $"{AppRuntimeNames.FileNameToken}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(screenshotPath, PngBitmapEncoderOptions.Default);
        AppLogger.Info($"Screenshot saved: {screenshotPath}");
    }

    private static string GetProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
#endif
