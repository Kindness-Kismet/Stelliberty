using System.Diagnostics;
using System.Net.Sockets;
using Stelliberty.Application.Tray;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Tray;

public sealed class TrayProcessLauncher
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(8);

    public async Task<UiActivateResult> ActivateUiAsync(CancellationToken cancellationToken)
    {
        var client = await TryConnectAsync(cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            StartTrayProcess();
            client = await ConnectUntilReadyAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (client.ConfigureAwait(false))
        {
            return await client.ActivateUiAsync(Environment.ProcessId, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<TrayIpcClient> ConnectUntilReadyAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        Exception? lastError = null;
        while (Stopwatch.GetElapsedTime(startedAt) < StartupTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var client = await TryConnectAsync(cancellationToken).ConfigureAwait(false);
                if (client is not null)
                {
                    return client;
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException)
            {
                lastError = exception;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Tray did not become ready before the startup timeout.", lastError);
    }

    private static async Task<TrayIpcClient?> TryConnectAsync(CancellationToken cancellationToken)
    {
        var client = new TrayIpcClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await client.HelloAsync(Environment.ProcessId, timeout.Token).ConfigureAwait(false);
            return client;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (IOException)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (SocketException)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void StartTrayProcess()
    {
        var executablePath = ResolveTrayExecutable();
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Tray process could not be started.");
    }

    private static string ResolveTrayExecutable()
    {
        var packagedPath = Path.Combine(AppContext.BaseDirectory, AppRuntimeNames.TrayBinaryName);
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
                "Stelliberty.Tray",
                "bin",
                "Debug",
                "net11.0",
                AppRuntimeNames.TrayBinaryName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }
#endif

        throw new FileNotFoundException("Tray executable was not found.", packagedPath);
    }
}
