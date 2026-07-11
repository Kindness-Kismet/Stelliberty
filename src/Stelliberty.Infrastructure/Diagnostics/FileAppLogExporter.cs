using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Infrastructure.Diagnostics;

public sealed class FileAppLogExporter(string logFilePath) : IAppLogExporter
{
    // 路径有效性由调用方保证；无效路径让 IO 抛出，由上层转为导出失败
    public async Task ExportAsync(string exportPath, CancellationToken cancellationToken = default)
    {
        await using var source = new FileStream(
            logFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);
        await using var target = new FileStream(
            exportPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await source.CopyToAsync(target, cancellationToken);
    }
}
