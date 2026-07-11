using Stelliberty.Domain.Proxies;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Proxies;

// 核心重启后，实时 API 恢复前 /proxies 可能为空。
public sealed class ResilientProxyConfigLoader
{
    private static readonly TimeSpan PrimaryLoadTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PrimaryReloadRetryDelay = TimeSpan.FromMilliseconds(250);
    private const int PrimaryReloadMaxAttempts = 20;

    public async Task<ProxyConfig> LoadAsync(
        IProxyConfigProvider primary,
        IProxyConfigProvider? fallback,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= PrimaryReloadMaxAttempts; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(PrimaryLoadTimeout);
                var config = await primary.LoadAsync(timeout.Token);
                if (HasRuntimeProxyEntries(config) || fallback is null || attempt == PrimaryReloadMaxAttempts)
                {
                    return config;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                AppLogger.Warning("Core proxy list load timed out");
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                AppLogger.Warning($"Core proxy list load failed: {exception.Message}");
                break;
            }

            await Task.Delay(PrimaryReloadRetryDelay, cancellationToken);
        }

        if (fallback is not null)
        {
            try
            {
                return await fallback.LoadAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Runtime config proxy list load failed: {exception.Message}");
            }
        }

        return new ProxyConfig([], new Dictionary<string, ProxyNode>());
    }

    private static bool HasRuntimeProxyEntries(ProxyConfig config)
    {
        return config.Groups.Count > 0 || config.Nodes.Count > 0;
    }
}
