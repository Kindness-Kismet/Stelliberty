namespace Stelliberty.Application.Diagnostics;

public interface IAppLogExporter
{
    Task ExportAsync(string exportPath, CancellationToken cancellationToken = default);
}
