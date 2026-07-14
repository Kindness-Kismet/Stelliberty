namespace Stelliberty.Application.Platform;

public static class PortableDataDirectoryResolver
{
    public static string ResolveMacOS(string baseDirectory)
    {
        var installDataDirectory = InstallDataDirectory(baseDirectory);
        var macOSDirectory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(baseDirectory));
        var contentsDirectory = macOSDirectory.Parent;
        var bundleDirectory = contentsDirectory?.Parent;
        if (macOSDirectory.Name != "MacOS"
            || contentsDirectory?.Name != "Contents"
            || bundleDirectory is not { Parent: { } bundleParent }
            || !string.Equals(bundleDirectory.Extension, ".app", StringComparison.OrdinalIgnoreCase))
        {
            return installDataDirectory;
        }

        return Path.Combine(
            bundleParent.FullName,
            $"{Path.GetFileNameWithoutExtension(bundleDirectory.Name)}.data");
    }

    public static string ResolveLinux(string baseDirectory, string? configuredDataDirectory)
    {
        return string.IsNullOrWhiteSpace(configuredDataDirectory)
            ? InstallDataDirectory(baseDirectory)
            : Path.GetFullPath(configuredDataDirectory);
    }

    private static string InstallDataDirectory(string baseDirectory) =>
        Path.Combine(baseDirectory, PathConventions.DataDirectoryName);
}
