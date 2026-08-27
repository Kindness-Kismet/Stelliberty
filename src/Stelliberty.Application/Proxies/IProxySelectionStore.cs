namespace Stelliberty.Application.Proxies;

public interface IProxySelectionStore
{
    IReadOnlyDictionary<string, string> GetSelections(string subscriptionId);

    void SetSelection(string subscriptionId, string groupName, string proxyName);

    void RemoveSelection(string subscriptionId, string groupName);

    // 订阅删除后其选择永远不会再被读取，必须显式清理，否则永久残留。
    void RemoveSubscription(string subscriptionId);
}
