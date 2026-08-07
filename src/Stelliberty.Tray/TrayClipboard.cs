using System.ComponentModel;
using System.Diagnostics;

namespace Stelliberty.Tray;

internal static class TrayClipboard
{
    public static Task WriteTextAsync(string text, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return WriteWindowsAsync(text, cancellationToken);
        }

        if (OperatingSystem.IsMacOS())
        {
            return WriteStandardInputAsync("/usr/bin/pbcopy", [], text, cancellationToken);
        }

        return WriteLinuxAsync(text, cancellationToken);
    }

    private static async Task WriteWindowsAsync(string text, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add("Set-Clipboard -Value $args[0]");
        startInfo.ArgumentList.Add(text);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Clipboard process could not be started.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Clipboard process exited with code {process.ExitCode}.");
        }
    }

    private static async Task WriteLinuxAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await WriteStandardInputAsync("wl-copy", [], text, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            await WriteStandardInputAsync("xclip", ["-selection", "clipboard"], text, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteStandardInputAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string text,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Clipboard process could not be started.");
        await process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Clipboard process exited with code {process.ExitCode}.");
        }
    }
}
