using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Subscriptions;
using Xunit;

namespace Stelliberty.Infrastructure.Tests;

public sealed class FileSubscriptionProviderUploaderTests
{
    [Fact(DisplayName = "File provider upload saves relative path under configured core directory")]
    public async Task FileProviderUploadSavesRelativePathUnderConfiguredCoreDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"stelliberty-provider-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(root, "source.yaml");
        var targetPath = Path.Combine(root, "providers", "demo.yaml");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(sourcePath, "proxies: []\n");
        try
        {
            var provider = new SubscriptionProvider("Demo", "proxy", "File", "./providers/demo.yaml", 0, null);
            var uploader = new FileSubscriptionProviderUploader(root);

            var result = await uploader.UploadAsync(provider, sourcePath);

            Assert.True(result.IsUploaded);
            await using var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.None, 4096, useAsync: true);
            using var reader = new StreamReader(stream);
            Assert.Equal("proxies: []\n", await reader.ReadToEndAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
