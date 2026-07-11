using System.Text.Json;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Storage;

namespace Stelliberty.Infrastructure.Subscriptions;

public sealed class FileSubscriptionSelectionStore(string rootDirectory) : ISubscriptionSelectionStore
{
    private readonly string _statePath = Path.Combine(rootDirectory, "subscriptions", "selection_state.json");

    public string? GetCurrentSubscriptionId()
    {
        return JsonFileRecovery.ReadOrRecover<SelectionState>(_statePath)?.CurrentSubscriptionId;
    }

    public void SetCurrentSubscriptionId(string? subscriptionId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var json = JsonSerializer.Serialize(new SelectionState(subscriptionId), new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_statePath, json);
    }

    private sealed record SelectionState(string? CurrentSubscriptionId);
}
