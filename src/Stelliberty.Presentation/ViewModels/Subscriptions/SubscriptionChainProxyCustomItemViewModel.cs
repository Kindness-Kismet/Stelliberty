namespace Stelliberty.Presentation.ViewModels;

// 自定义链展示项；HasMissingNodes 表示覆写输出丢失了引用节点。
public sealed record SubscriptionChainProxyCustomItemViewModel(
    string Id,
    string DisplayName,
    string ProxyGroupName,
    string PathText,
    bool HasMissingReferences,
    string MissingText)
{
    public string ProxyGroupText => ProxyGroupName;

    public string EditAutomationId => $"Subscriptions.ChainProxy.Custom.{Id}.EditButton";

    public string DeleteAutomationId => $"Subscriptions.ChainProxy.Custom.{Id}.DeleteButton";
}
