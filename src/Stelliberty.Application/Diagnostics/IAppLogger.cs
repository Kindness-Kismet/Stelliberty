namespace Stelliberty.Application.Diagnostics;

public interface IAppLogger
{
    event EventHandler<AppLogEntry>? EntryWritten;

    IReadOnlyList<AppLogEntry> Snapshot();

    void Debug(string message);

    void Info(string message);

    void Warning(string message);

    void Error(string message);

    void Error(Exception exception, string message);
}
