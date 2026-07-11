using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class UnsupportedUwpLoopbackService : IUwpLoopbackService
{
    public IReadOnlyList<UwpLoopbackPackage> LoadPackages()
    {
        return [];
    }

    public UwpLoopbackOperationResult SetLoopback(string packageFamilyName, bool isEnabled)
    {
        return new UwpLoopbackOperationResult(false, "UWP loopback configuration is not supported in this environment", null);
    }

    public UwpLoopbackBatchResult SetLoopbackBatch(IReadOnlyCollection<string> enabledPackageFamilyNames)
    {
        return new UwpLoopbackBatchResult(false, "UWP loopback configuration is not supported in this environment", []);
    }
}
