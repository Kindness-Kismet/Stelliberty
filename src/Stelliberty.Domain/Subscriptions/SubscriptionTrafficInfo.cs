namespace Stelliberty.Domain.Subscriptions;

public sealed record SubscriptionTrafficInfo(long Upload, long Download, long Total, long Expire)
{
    public static SubscriptionTrafficInfo ParseHeader(string header)
    {
        var values = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var keyValue = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (keyValue.Length == 2 && long.TryParse(keyValue[1], out var value))
            {
                values[keyValue[0]] = value;
            }
        }

        return new SubscriptionTrafficInfo(
            values.GetValueOrDefault("upload"),
            values.GetValueOrDefault("download"),
            values.GetValueOrDefault("total"),
            values.GetValueOrDefault("expire"));
    }
}
