namespace Stelliberty.Application.Proxies;

public interface IProxySelectionStore
{
    IReadOnlyDictionary<string, string> GetSelections(string subscriptionId);

    void SetSelection(string subscriptionId, string groupName, string proxyName);

    void RemoveSelection(string subscriptionId, string groupName);
}
