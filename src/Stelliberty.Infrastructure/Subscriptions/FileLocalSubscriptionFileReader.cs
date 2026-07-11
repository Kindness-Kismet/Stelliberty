using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Infrastructure.Subscriptions;

public sealed class FileLocalSubscriptionFileReader : ILocalSubscriptionFileReader
{
    public string ReadAllText(string filePath)
    {
        return File.ReadAllText(filePath);
    }
}
