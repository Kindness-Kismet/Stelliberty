using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

internal static class TrayApplicationLayout
{
    public static string DepsDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "deps");

    private static string AppDataDirectory => OperatingSystem.IsMacOS()
        ? PortableDataDirectoryResolver.ResolveMacOS(AppContext.BaseDirectory)
        : OperatingSystem.IsLinux()
            ? PortableDataDirectoryResolver.ResolveLinux(
                AppContext.BaseDirectory,
                Environment.GetEnvironmentVariable(PathConventions.PortableDataDirectoryEnvironmentVariable))
            : Path.Combine(AppContext.BaseDirectory, PathConventions.DataDirectoryName);

    public static string RunningLogFilePath => Path.Combine(
        AppDataDirectory,
        PathConventions.AppLogsSubdirectory,
        "tray-running.logs");
}
