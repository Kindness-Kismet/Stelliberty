namespace Stelliberty.Application.Diagnostics;

public static class AppLogger
{
    private static IAppLogger? logger;

    public static event EventHandler<AppLogEntry>? EntryWritten;

    public static IReadOnlyList<AppLogEntry> Snapshot() => logger?.Snapshot() ?? [];

    public static void Configure(IAppLogger value)
    {
        if (logger is not null)
        {
            logger.EntryWritten -= OnEntryWritten;
        }

        logger = value;
        logger.EntryWritten += OnEntryWritten;
    }

    public static void Debug(string message) => logger?.Debug(AppLogSanitizer.Sanitize(message));

    public static void Info(string message) => logger?.Info(AppLogSanitizer.Sanitize(message));

    public static void Warning(string message) => logger?.Warning(AppLogSanitizer.Sanitize(message));

    public static void Error(string message) => logger?.Error(AppLogSanitizer.Sanitize(message));

    public static void Error(Exception exception, string message)
    {
        logger?.Error(AppLogSanitizer.Sanitize($"{message}: {exception}"));
    }

    private static void OnEntryWritten(object? sender, AppLogEntry entry)
    {
        EntryWritten?.Invoke(sender, entry);
    }
}
