using System.Text.Json;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Storage;

namespace Stelliberty.Infrastructure.Subscriptions;

public sealed class FileSubscriptionSelectionStore(string rootDirectory) : ISubscriptionSelectionStore
{
    private readonly string _statePath = Path.Combine(rootDirectory, "subscriptions", "selection_state.json");
    // 损坏文件会在读取时被改名重建，读写都要串行。
    private readonly object _syncRoot = new();

    public string? GetCurrentSubscriptionId()
    {
        lock (_syncRoot)
        {
            return JsonFileRecovery.ReadOrRecover<SelectionState>(_statePath)?.CurrentSubscriptionId;
        }
    }

    public void SetCurrentSubscriptionId(string? subscriptionId)
    {
        var json = JsonSerializer.Serialize(new SelectionState(subscriptionId), new JsonSerializerOptions
        {
            WriteIndented = true
        });

        lock (_syncRoot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            AtomicFile.WriteAllText(_statePath, json);
        }
    }

    private sealed record SelectionState(string? CurrentSubscriptionId);
}
