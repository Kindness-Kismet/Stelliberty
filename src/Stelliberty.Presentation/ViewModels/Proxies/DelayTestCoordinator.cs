namespace Stelliberty.Presentation.ViewModels;

// 批测持单一令牌，单测按节点各持一个令牌；派生量内部维护，外部只读。
// 集合非并发，仅限 UI 线程访问。
internal sealed class DelayTestCoordinator
{
    private readonly Dictionary<string, CancellationTokenSource> _singleTests = new(StringComparer.Ordinal);
    private readonly HashSet<string> _batchTargetNodeNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _batchTestingNodeNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _batchResults = new(StringComparer.Ordinal);
    private readonly HashSet<string> _testingNodeNames = new(StringComparer.Ordinal);
    private CancellationTokenSource? _batchCancellation;

    public IReadOnlyCollection<string> TestingNodeNames => _testingNodeNames;

    // 批测进行中收到的逐节点延迟，用于在整批结束前就渲染已完成的节点。
    public IReadOnlyDictionary<string, int> BatchResults => _batchResults;

    public bool IsBatchTesting => _batchCancellation is not null;

    public bool IsTesting => IsBatchTesting || _singleTests.Count > 0;

    public IReadOnlyList<string> ActiveSingleNodeNames => [.. _singleTests.Keys];

    // 批测覆盖的节点不再接受单测，避免同一节点被两条链路同时测。
    public bool IsBatchTarget(string nodeName) => _batchTargetNodeNames.Contains(nodeName);

    public CancellationTokenSource BeginSingle(string nodeName)
    {
        // 只有同一节点被重复触发才取消它上一次的测试，不同节点互不影响。
        if (_singleTests.Remove(nodeName, out var previous))
        {
            previous.Cancel();
        }

        var cancellation = new CancellationTokenSource();
        _singleTests[nodeName] = cancellation;
        RefreshTestingNodeNames();
        return cancellation;
    }

    // 令牌已被取消或已被同节点的新测试顶替时，对应结果都不能再写回。
    public bool IsSingleCurrent(string nodeName, CancellationTokenSource cancellation)
    {
        return !cancellation.IsCancellationRequested
            && _singleTests.TryGetValue(nodeName, out var current)
            && ReferenceEquals(current, cancellation);
    }

    public void CompleteSingle(string nodeName, CancellationTokenSource cancellation)
    {
        if (!_singleTests.TryGetValue(nodeName, out var current)
            || !ReferenceEquals(current, cancellation))
        {
            return;
        }

        _singleTests.Remove(nodeName);
        RefreshTestingNodeNames();
    }

    public CancellationTokenSource BeginBatch(IReadOnlyList<string> targetNodeNames)
    {
        _batchCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _batchCancellation = cancellation;
        _batchTargetNodeNames.Clear();
        _batchTargetNodeNames.UnionWith(targetNodeNames);
        _batchTestingNodeNames.Clear();
        _batchResults.Clear();
        RefreshTestingNodeNames();
        return cancellation;
    }

    public bool IsBatchCurrent(CancellationTokenSource cancellation)
    {
        return !cancellation.IsCancellationRequested
            && ReferenceEquals(_batchCancellation, cancellation);
    }

    public void CompleteBatch(CancellationTokenSource cancellation)
    {
        if (!ReferenceEquals(_batchCancellation, cancellation))
        {
            return;
        }

        _batchCancellation = null;
        _batchTargetNodeNames.Clear();
        _batchTestingNodeNames.Clear();
        _batchResults.Clear();
        RefreshTestingNodeNames();
    }

    public void MarkBatchNodeTesting(string nodeName)
    {
        _batchTestingNodeNames.Add(nodeName);
        RefreshTestingNodeNames();
    }

    public void MarkBatchNodeCompleted(string nodeName, int delay)
    {
        _batchTestingNodeNames.Remove(nodeName);
        _batchResults[nodeName] = delay;
        RefreshTestingNodeNames();
    }

    public void CancelAll()
    {
        _batchCancellation?.Cancel();
        _batchCancellation = null;
        // 只取消不释放：各发起方在自己的 finally 里释放自己的令牌。
        foreach (var cancellation in _singleTests.Values)
        {
            cancellation.Cancel();
        }

        _singleTests.Clear();
        _batchTargetNodeNames.Clear();
        _batchTestingNodeNames.Clear();
        _batchResults.Clear();
        RefreshTestingNodeNames();
    }

    private void RefreshTestingNodeNames()
    {
        _testingNodeNames.Clear();
        _testingNodeNames.UnionWith(_batchTestingNodeNames);
        _testingNodeNames.UnionWith(_singleTests.Keys);
    }
}
