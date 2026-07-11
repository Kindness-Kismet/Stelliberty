namespace Stelliberty.Application.Subscriptions;

public interface ISubscriptionContentDecryptor
{
    string DecryptIfNeeded(string content, string ageSecretKey);
}
