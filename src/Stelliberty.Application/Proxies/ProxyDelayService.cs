using System.Collections.Concurrent;
using System.Diagnostics;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

public sealed class ProxyDelayService(IProxyDelayTester tester)
{
    // 限制并发，避免 mihomo 延迟测试超出核心承载。
    private const int DelayTestConcurrency = 15;

    public async Task<ProxyDelayResult> TestNodeAsync(ProxyConfig config, string proxyName, CancellationToken cancellationToken = default)
    {
        if (!ProxyConfigSelectionNormalizer.HasEntry(config, proxyName))
        {
            return new ProxyDelayResult(config, [], [proxyName], []);
        }

        var stopwatch = Stopwatch.StartNew();
        var delay = await tester.TestDelayAsync(proxyName, cancellationToken);
        if (delay >= 0)
        {
            AppLogger.Info($"Proxy delay test completed: proxy={proxyName} delay={delay}ms elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms");
        }
        else
        {
            AppLogger.Warning($"Proxy delay test failed: proxy={proxyName} elapsed={stopwatch.Elapsed.TotalMilliseconds:0}ms");
        }

        return new ProxyDelayResult(
            config.WithEntryDelay(proxyName, delay),
            [proxyName],
            [],
            delay < 0 ? [proxyName] : []);
    }

    public Task<ProxyDelayResult> TestGroupAsync(ProxyConfig config, string groupName, CancellationToken cancellationToken = default)
        => TestGroupAsync(config, groupName, null, cancellationToken);

    public Task<ProxyDelayResult> TestGroupAsync(ProxyConfig config, string groupName, IProgress<ProxyDelayProgress>? progress, CancellationToken cancellationToken = default)
    {
        var group = config.Groups.FirstOrDefault(item => item.Name == groupName)
            ?? throw new InvalidOperationException($"Proxy group not found: {groupName}");
        return TestNodesAsync(config, group.All, $"group={group.Name}", progress, cancellationToken);
    }

    public Task<ProxyDelayResult> TestAllAsync(ProxyConfig config, CancellationToken cancellationToken = default)
        => TestAllAsync(config, null, cancellationToken);

    public Task<ProxyDelayResult> TestAllAsync(ProxyConfig config, IProgress<ProxyDelayProgress>? progress, CancellationToken cancellationToken = default)
    {
        var proxyNames = config.Groups
            .SelectMany(group => group.All)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return TestNodesAsync(config, proxyNames, "scope=all", progress, cancellationToken);
    }

    // 并发测试结束后再合并，避免工作任务修改快照。
    private async Task<ProxyDelayResult> TestNodesAsync(
        ProxyConfig config,
        IReadOnlyList<string> proxyNames,
        string scope,
        IProgress<ProxyDelayProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var targets = new List<string>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var proxyName in proxyNames)
        {
            if (!seen.Add(proxyName))
            {
                continue;
            }

            if (ProxyConfigSelectionNormalizer.HasEntry(config, proxyName))
            {
                targets.Add(proxyName);
            }
            else
            {
                skipped.Add(proxyName);
            }
        }

        var delays = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        using var semaphore = new SemaphoreSlim(DelayTestConcurrency);
        var tasks = targets.Select(async proxyName =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var delay = await tester.TestDelayAsync(proxyName, cancellationToken);
                delays[proxyName] = delay;
                progress?.Report(new ProxyDelayProgress(proxyName, delay));
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();

        // 批量填充延迟，避免为每个项目重建完整配置。
        var testedDelays = new Dictionary<string, int>(StringComparer.Ordinal);
        var tested = new List<string>();
        var failed = new List<string>();
        foreach (var proxyName in targets)
        {
            var delay = delays[proxyName];
            testedDelays[proxyName] = delay;
            tested.Add(proxyName);
            if (delay < 0)
            {
                failed.Add(proxyName);
            }
        }

        LogBatchResult(scope, targets.Count, tested.Count - failed.Count, failed.Count, skipped.Count, stopwatch.Elapsed);
        return new ProxyDelayResult(config.WithEntryDelays(testedDelays), tested, skipped, failed);
    }

    private static void LogBatchResult(string scope, int total, int succeeded, int failed, int skipped, TimeSpan elapsed)
    {
        var message = $"Proxy delay batch completed: {scope} total={total} succeeded={succeeded} failed={failed} skipped={skipped} elapsed={elapsed.TotalMilliseconds:0}ms";
        if (failed > 0)
        {
            AppLogger.Warning(message);
            return;
        }

        AppLogger.Info(message);
    }
}
