using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Infrastructure.Proxies;

public sealed class FileRuntimeProxyConfigSource(string runtimeDirectory, ISubscriptionSelectionStore selectionStore) : IProxyConfigSource
{
    public string ReadRuntimeConfig()
    {
        var subscriptionId = selectionStore.GetCurrentSubscriptionId();
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return "{}";
        }

        var runtimePath = Path.Combine(runtimeDirectory, subscriptionId, "runtime.yaml");
        return File.Exists(runtimePath) ? File.ReadAllText(runtimePath) : "{}";
    }
}
