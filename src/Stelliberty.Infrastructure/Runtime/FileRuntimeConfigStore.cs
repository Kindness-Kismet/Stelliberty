using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Infrastructure.Runtime;

public sealed class FileRuntimeConfigStore(string runtimeDirectory) : ISelectedSubscriptionRuntimeStore
{
    private readonly string _runtimeDirectory = runtimeDirectory;

    public SelectedSubscriptionRuntimePaths Save(Subscription subscription, string originalContent, string runtimeConfigContent)
    {
        var subscriptionRuntimeDirectory = Path.Combine(_runtimeDirectory, subscription.Id);
        Directory.CreateDirectory(subscriptionRuntimeDirectory);

        var originalContentPath = Path.Combine(subscriptionRuntimeDirectory, "original.yaml");
        var runtimeConfigPath = Path.Combine(subscriptionRuntimeDirectory, "runtime.yaml");
        File.WriteAllText(originalContentPath, originalContent);
        File.WriteAllText(runtimeConfigPath, runtimeConfigContent);
        AppLogger.Info($"Runtime config generated: {subscription.Name}");

        return new SelectedSubscriptionRuntimePaths(originalContentPath, runtimeConfigPath);
    }

    public string SaveEmpty(string runtimeConfigContent)
    {
        var emptyRuntimeDirectory = Path.Combine(_runtimeDirectory, "empty");
        Directory.CreateDirectory(emptyRuntimeDirectory);

        var runtimeConfigPath = Path.Combine(emptyRuntimeDirectory, "runtime.yaml");
        File.WriteAllText(runtimeConfigPath, runtimeConfigContent);
        AppLogger.Info("Empty runtime config generated");
        return runtimeConfigPath;
    }

    public string ReadRuntimeConfig(string subscriptionId)
    {
        return File.ReadAllText(Path.Combine(_runtimeDirectory, subscriptionId, "runtime.yaml"));
    }

    public void Delete(string subscriptionId)
    {
        var subscriptionRuntimeDirectory = Path.Combine(_runtimeDirectory, subscriptionId);
        if (Directory.Exists(subscriptionRuntimeDirectory))
        {
            Directory.Delete(subscriptionRuntimeDirectory, true);
        }

        AppLogger.Info($"Runtime config deleted: {subscriptionId}");
    }
}
