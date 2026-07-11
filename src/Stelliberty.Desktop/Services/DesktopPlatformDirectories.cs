using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

internal sealed class DesktopPlatformDirectories : IPlatformDirectories
{
    public string AppDataDirectory => DesktopApplicationLayout.AppDataDirectory;

    public string DepsDirectory => DesktopApplicationLayout.DepsDirectory;

    public string CoreDirectory => DesktopApplicationLayout.CoreDirectory;

    public string RuntimeDirectory => DesktopApplicationLayout.RuntimeDirectory;

    public string SettingsFilePath => DesktopApplicationLayout.SettingsFilePath;
}
