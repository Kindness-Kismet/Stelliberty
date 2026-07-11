using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public interface ILocalSubscriptionFileReader
{
    string ReadAllText(string filePath);
}
