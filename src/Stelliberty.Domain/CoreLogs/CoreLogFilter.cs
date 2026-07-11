namespace Stelliberty.Domain.CoreLogs;

public sealed class CoreLogFilter
{
    public IReadOnlyList<CoreLogMessage> Apply(
        IReadOnlyList<CoreLogMessage> logs,
        CoreLogLevel? level,
        string searchKeyword)
    {
        IEnumerable<CoreLogMessage> filtered = logs;
        if (level is not null)
        {
            filtered = filtered.Where(log => log.Level == level);
        }

        var normalizedKeyword = searchKeyword.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            filtered = filtered.Where(log => Contains(log.Payload, normalizedKeyword) || Contains(log.Type, normalizedKeyword));
        }

        return filtered.ToList();
    }

    private static bool Contains(string value, string keyword)
    {
        return value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
