namespace Stelliberty.Application.Diagnostics;

public interface IAppLogReader
{
    IReadOnlyList<AppLogEntry> ReadEntries(int maxEntries, CancellationToken cancellationToken = default);
}
