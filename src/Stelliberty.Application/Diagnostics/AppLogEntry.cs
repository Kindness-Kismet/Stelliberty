namespace Stelliberty.Application.Diagnostics;

public sealed record AppLogEntry(AppLogLevel Level, DateTime Timestamp, string Message)
{
    public string LevelText => $"[{LevelCode}]";

    public string Text => $"{Timestamp:yyyy/M/d HH:mm:ss} {Message}";

    public string Format() => $"{LevelText} {Text}";

    private string LevelCode
    {
        get
        {
            return Level switch
            {
                AppLogLevel.Debug => "D",
                AppLogLevel.Info => "I",
                AppLogLevel.Warning => "W",
                AppLogLevel.Error => "E",
                _ => "?"
            };
        }
    }
}
