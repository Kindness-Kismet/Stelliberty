namespace Stelliberty.Application.Proxies;

public sealed record CoreRuntimeStats(
    long UploadSpeed,
    long DownloadSpeed,
    long UploadTotal,
    long DownloadTotal,
    int ConnectionCount,
    long Memory,
    bool HasTrafficRate);
