namespace Stelliberty.Domain.CoreLogs;

public sealed record CoreLogMessage(string Type, string Payload, DateTimeOffset Timestamp)
{
    public CoreLogLevel Level
    {
        get
        {
            var type = Type.ToUpperInvariant();
            if (type.Contains("ERROR", StringComparison.Ordinal) || type.Contains("FATAL", StringComparison.Ordinal))
            {
                return CoreLogLevel.Error;
            }

            if (type.Contains("WARN", StringComparison.Ordinal))
            {
                return CoreLogLevel.Warning;
            }

            if (type.Contains("DEBUG", StringComparison.Ordinal))
            {
                return CoreLogLevel.Debug;
            }

            return CoreLogLevel.Info;
        }
    }
}
