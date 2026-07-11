using Stelliberty.Domain.Subscriptions;
using Stelliberty.Application.Diagnostics;

namespace Stelliberty.Application.Subscriptions;

public sealed class LocalSubscriptionFileImporter(
    LocalSubscriptionImporter importer,
    ILocalSubscriptionFileReader fileReader)
{
    public Subscription Import(LocalSubscriptionFileImportRequest request)
    {
        var content = fileReader.ReadAllText(request.FilePath);
        var subscription = importer.Import(
            request.Name,
            content,
            sourceLocation: request.FilePath,
            autoTestDelayIntervalMinutes: request.AutoTestDelayIntervalMinutes);
        AppLogger.Info($"Local subscription file imported: {request.Name}");
        return subscription;
    }
}
