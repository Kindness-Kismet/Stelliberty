namespace Stelliberty.Application.Proxies;

public sealed record TrafficRateSample(
    long UploadSpeed,
    long DownloadSpeed,
    long UploadTotal,
    long DownloadTotal);

// mihomo 只返回总量；速率来自相邻采样差值。
public sealed class TrafficRateTracker
{
    private long _lastUploadTotal;
    private long _lastDownloadTotal;
    private DateTimeOffset? _lastSampleAt;
    private long _baselineUploadTotal;
    private long _baselineDownloadTotal;
    private long _uploadSpeed;
    private long _downloadSpeed;

    public TrafficRateSample Update(long uploadTotal, long downloadTotal, DateTimeOffset sampledAt)
    {
        if (_lastSampleAt is { } last)
        {
            var seconds = (sampledAt - last).TotalSeconds;
            if (seconds > 0)
            {
                _uploadSpeed = Math.Max(0, (long)((uploadTotal - _lastUploadTotal) / seconds));
                _downloadSpeed = Math.Max(0, (long)((downloadTotal - _lastDownloadTotal) / seconds));
            }
        }

        _lastUploadTotal = uploadTotal;
        _lastDownloadTotal = downloadTotal;
        _lastSampleAt = sampledAt;
        return new TrafficRateSample(
            _uploadSpeed,
            _downloadSpeed,
            Math.Max(0, uploadTotal - _baselineUploadTotal),
            Math.Max(0, downloadTotal - _baselineDownloadTotal));
    }

    // 总量不能重置，所以移动基线让本地显示归零。
    public void ResetBaseline()
    {
        _baselineUploadTotal = _lastUploadTotal;
        _baselineDownloadTotal = _lastDownloadTotal;
        _uploadSpeed = 0;
        _downloadSpeed = 0;
    }

    // 核心停止会重置总量，使采样和基线都失效。
    public void Reset()
    {
        _lastUploadTotal = 0;
        _lastDownloadTotal = 0;
        _lastSampleAt = null;
        _baselineUploadTotal = 0;
        _baselineDownloadTotal = 0;
        _uploadSpeed = 0;
        _downloadSpeed = 0;
    }
}
