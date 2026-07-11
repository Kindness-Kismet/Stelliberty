using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Infrastructure.Subscriptions;

public sealed class FileSubscriptionProviderUploader(string providerRootDirectory) : ISubscriptionProviderUploader
{
    public async Task<SubscriptionProviderUploadResult> UploadAsync(SubscriptionProvider provider, string sourcePath, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(provider.VehicleType, "File", StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionProviderUploadResult.Skipped("provider is not File type");
        }

        if (!File.Exists(sourcePath))
        {
            return SubscriptionProviderUploadResult.Skipped("source file does not exist");
        }

        var targetPath = ResolveProviderPath(provider.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? providerRootDirectory);
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
        await source.CopyToAsync(target, cancellationToken);
        AppLogger.Info($"File Provider upload completed: {provider.Name}");
        return SubscriptionProviderUploadResult.Uploaded();
    }

    private string ResolveProviderPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var relativePath = path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
        return Path.Combine(providerRootDirectory, relativePath);
    }
}
