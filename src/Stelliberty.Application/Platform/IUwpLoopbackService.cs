namespace Stelliberty.Application.Platform;

public interface IUwpLoopbackService
{
    IReadOnlyList<UwpLoopbackPackage> LoadPackages();

    UwpLoopbackOperationResult SetLoopback(string packageFamilyName, bool isEnabled);

    // 批量提交会写入完整启用包集合，并保留未知 SID。
    UwpLoopbackBatchResult SetLoopbackBatch(IReadOnlyCollection<string> enabledPackageFamilyNames);
}
