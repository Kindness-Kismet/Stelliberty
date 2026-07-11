using Stelliberty.Application.Rules;
using Stelliberty.Domain.Rules;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Infrastructure.Rules;

public sealed class FileRuntimeRuleConfigSource(string runtimeDirectory, ISubscriptionSelectionStore selectionStore) : IRuleConfigSource
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
