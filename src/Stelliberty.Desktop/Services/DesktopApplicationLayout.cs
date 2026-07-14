using Stelliberty.Application.Platform;

namespace Stelliberty.Desktop.Services;

internal static class DesktopApplicationLayout
{
    private static string BaseDirectory => AppContext.BaseDirectory;

    private static string InstallDataDirectory => Path.Combine(BaseDirectory, PathConventions.DataDirectoryName);

    // 安装资源随版本替换，用户数据固定在安装载体之外。
    public static string AppDataDirectory => OperatingSystem.IsMacOS()
        ? PortableDataDirectoryResolver.ResolveMacOS(BaseDirectory)
        : OperatingSystem.IsLinux()
            ? PortableDataDirectoryResolver.ResolveLinux(
                BaseDirectory,
                Environment.GetEnvironmentVariable(PathConventions.PortableDataDirectoryEnvironmentVariable))
            : InstallDataDirectory;

    public static string DepsDirectory => Path.Combine(InstallDataDirectory, PathConventions.DepsSubdirectory);

    public static string CoreDirectory => Path.Combine(InstallDataDirectory, PathConventions.CoreSubdirectory);

    public static string CoreBinaryPath => Path.Combine(CoreDirectory, CoreBinaryName);

    public static string ServiceDirectory => Path.Combine(AppDataDirectory, PathConventions.ServiceSubdirectory);

    public static string ServiceUpdateDirectory => Path.Combine(InstallDataDirectory, PathConventions.ServiceSubdirectory, PathConventions.ServiceUpdateSubdirectory);

    public static string ServiceCommandBinaryPath => Path.Combine(ServiceUpdateDirectory, ServiceBinaryName);

    public static string ServiceInstalledBinaryPath => Path.Combine(ServiceDirectory, ServiceInstalledBinaryName);

    public static string RuntimeDirectory => Path.Combine(AppDataDirectory, PathConventions.RuntimeSubdirectory);

    public static string AppLogsDirectory => Path.Combine(AppDataDirectory, PathConventions.AppLogsSubdirectory);

    public static string RunningLogFilePath => Path.Combine(AppLogsDirectory, PathConventions.RunningLogFileName);

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, PathConventions.SettingsFileName);

    private static string CoreBinaryName => OperatingSystem.IsWindows() ? "clash-mihomo-core.exe" : "clash-mihomo-core";

    private static string ServiceBinaryName => AppRuntimeNames.ServiceBinaryName;

    private static string ServiceInstalledBinaryName => OperatingSystem.IsWindows()
        ? $"{PathConventions.ServiceInstalledBinaryStem}.exe"
        : PathConventions.ServiceInstalledBinaryStem;

}
