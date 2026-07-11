namespace Stelliberty.Domain.Subscriptions;

public sealed record SubscriptionProvider(
    string Name,
    string Type,
    string VehicleType,
    string Path,
    int Count,
    DateTimeOffset? UpdatedAt,
    bool IsUpdating = false)
{
    public bool IsVisible => IsHttp || string.Equals(VehicleType, "File", StringComparison.OrdinalIgnoreCase);

    public bool CanSync => IsHttp && !IsUpdating;

    private bool IsHttp => string.Equals(VehicleType, "HTTP", StringComparison.OrdinalIgnoreCase);
}
