using Stelliberty.Application.Diagnostics;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

public sealed class ProxySelectionRestorer(
    IProxyCoreClient coreClient,
    IProxyConfigProvider configProvider,
    StoredProxySelectionConfigProvider selectionProvider,
    ProxySelectionSyncState syncState)
{
    private static readonly TimeSpan LoadRetryDelay = TimeSpan.FromMilliseconds(250);
    private const int LoadMaxAttempts = 20;
    private const int StableSnapshotCount = 3;

    public async Task RestoreCurrentSubscriptionAsync(CancellationToken cancellationToken = default)
    {
        syncState.DisableCoreSelectionImport();
        var canImportCoreSelections = false;
        try
        {
            var config = await LoadRuntimeConfigAsync(cancellationToken);
            config = selectionProvider.ApplyStoredSelections(config);
            var restoredCount = 0;
            var clearedCount = 0;
            var failedCount = 0;
            foreach (var group in config.Groups)
            {
                var selection = group.UserSelectionName;
                if (!group.IsManualSelectable)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(selection))
                {
                    if (!group.UsesFixedSelection)
                    {
                        continue;
                    }

                    var cleared = await coreClient.ClearProxySelectionAsync(group.Name, cancellationToken);
                    if (cleared)
                    {
                        clearedCount++;
                        AppLogger.Info($"Pinned proxy selection cleared: group={group.Name}");
                    }
                    else
                    {
                        failedCount++;
                        AppLogger.Warning($"Pinned proxy selection clear failed: group={group.Name}");
                    }

                    continue;
                }

                var restored = await coreClient.ChangeProxyAsync(new ProxyChangeRequest(group.Name, selection), cancellationToken);
                if (restored)
                {
                    restoredCount++;
                    AppLogger.Info($"Proxy selection restored: group={group.Name} proxy={selection}");
                }
                else
                {
                    failedCount++;
                    AppLogger.Warning($"Proxy selection restore failed: group={group.Name} proxy={selection}");
                }
            }

            AppLogger.Info($"Proxy selection restore completed: restored={restoredCount} cleared={clearedCount} failed={failedCount}");
            canImportCoreSelections = failedCount == 0;
            if (!canImportCoreSelections)
            {
                throw new InvalidOperationException($"Proxy selection restore incomplete: failed={failedCount}");
            }

            selectionProvider.PruneInvalidStoredSelections(config);
        }
        finally
        {
            if (canImportCoreSelections)
            {
                syncState.EnableCoreSelectionImport();
            }
            else
            {
                // 还原成功前，不允许核心状态覆盖本地状态。
                syncState.DisableCoreSelectionImport();
            }
        }
    }

    private async Task<ProxyConfig> LoadRuntimeConfigAsync(CancellationToken cancellationToken)
    {
        ProxyConfig? previous = null;
        var stableCount = 0;
        for (var attempt = 1; attempt <= LoadMaxAttempts; attempt++)
        {
            var config = await configProvider.LoadAsync(cancellationToken);

            if (IsReady(config) && IsSameGroupSnapshot(previous, config))
            {
                stableCount++;
                if (stableCount >= StableSnapshotCount)
                {
                    return config;
                }
            }
            else
            {
                stableCount = 0;
            }

            previous = config;

            if (attempt == LoadMaxAttempts)
            {
                break;
            }

            await Task.Delay(LoadRetryDelay, cancellationToken);
        }

        throw new InvalidOperationException("Core proxy groups did not become stable");
    }

    private static bool IsSameGroupSnapshot(ProxyConfig? previous, ProxyConfig current)
    {
        if (previous is null || previous.Groups.Count != current.Groups.Count)
        {
            return false;
        }

        return previous.Groups.Zip(current.Groups).All(pair =>
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)
            && string.Equals(pair.First.Type, pair.Second.Type, StringComparison.Ordinal)
            && pair.First.All.SequenceEqual(pair.Second.All, StringComparer.Ordinal));
    }

    private static bool IsReady(ProxyConfig config)
    {
        if (config.Groups.Count == 0)
        {
            return false;
        }

        var entryNames = new HashSet<string>(config.Nodes.Keys, StringComparer.Ordinal);
        foreach (var group in config.Groups)
        {
            entryNames.Add(group.Name);
        }

        return config.Groups
            .Where(group => group.IsManualSelectable)
            .All(group => group.All.Count > 0 && group.All.All(entryNames.Contains));
    }
}
