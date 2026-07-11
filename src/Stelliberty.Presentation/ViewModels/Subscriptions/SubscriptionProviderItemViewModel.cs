using Stelliberty.Application.Localization;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionProviderItemViewModel(
    string Name,
    string DisplayName,
    string Type,
    string VehicleType,
    int Count,
    string UpdatedAt,
    bool HasRuntimeState = false,
    bool IsSyncing = false,
    bool IsSynced = false,
    bool IsUploaded = false,
    ILocalizationService? Localization = null)
{
    public bool CanSync => string.Equals(VehicleType, "HTTP", StringComparison.OrdinalIgnoreCase);

    public bool CanUpload => string.Equals(VehicleType, "File", StringComparison.OrdinalIgnoreCase);

    public string SyncAutomationId => $"Subscriptions.ProviderSelector.{Name}.SyncButton";

    public string UploadAutomationId => $"Subscriptions.ProviderSelector.{Name}.UploadButton";

    public string NameAutomationId => $"Subscriptions.ProviderSelector.{Name}.NameText";

    public string VehiclePillTag => CanSync ? "info" : "warning";

    public string VehicleIconType => CanUpload ? "FileLine" : "CloudLine";

    // 文件 Provider 使用本地灰色徽标；HTTP Provider 使用默认强调色。
    public string VehicleBadgeTag => CanUpload ? "local" : "remote";

    // 缺失运行时状态表示订阅未激活，不是零 providers。
    public string StatText => HasRuntimeState ? $"{CountText} · {UpdatedAt}" : UpdatedAt;

    public string CountText => string.Format(
        Localize(IsRule ? "Subscriptions.Provider.RuleCount" : "Subscriptions.Provider.ProxyCount"),
        Count);

    private bool IsRule => string.Equals(Type, "rule", StringComparison.OrdinalIgnoreCase);

    private string Localize(string key) => Localization?.GetString(key) ?? key;
}
