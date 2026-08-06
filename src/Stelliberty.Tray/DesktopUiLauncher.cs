using System.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Tray;

internal interface IDesktopUiLauncher
{
    Task LaunchAsync(string sessionToken, CancellationToken cancellationToken);
}

internal sealed class DesktopUiLauncher : IDesktopUiLauncher
{
    public Task LaunchAsync(string sessionToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var executablePath = ResolveDesktopExecutable();
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        startInfo.ArgumentList.Add("--ui");
        startInfo.ArgumentList.Add("--tray-session");
        startInfo.ArgumentList.Add(sessionToken);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Desktop UI process could not be started.");
        return Task.CompletedTask;
    }

    private static string ResolveDesktopExecutable()
    {
        var packagedPath = Path.Combine(AppContext.BaseDirectory, AppRuntimeNames.UiBinaryName);
        if (File.Exists(packagedPath))
        {
            return packagedPath;
        }

#if DEBUG
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Stelliberty.Desktop",
                "bin",
                "Debug",
                "net11.0",
                AppRuntimeNames.UiBinaryName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }
#endif

        throw new FileNotFoundException("Desktop executable was not found.", packagedPath);
    }
}
