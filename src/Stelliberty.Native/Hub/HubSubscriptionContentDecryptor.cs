using Stelliberty.Application.Subscriptions;
using Stelliberty.Native.Generated;

namespace Stelliberty.Native.Hub;

public sealed class HubSubscriptionContentDecryptor : ISubscriptionContentDecryptor
{
    private const string ErrorPrefix = "ERR:";

    public string DecryptIfNeeded(string content, string ageSecretKey)
    {
        if (string.IsNullOrWhiteSpace(ageSecretKey) || !SubscriptionContentDecryptor.IsAgeArmor(content))
        {
            return content;
        }

        using var output = Interop.hub_age_text_decrypt(content.Utf8(), ageSecretKey.Utf8());
        var result = output.String;
        if (result.StartsWith(ErrorPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Age decryption failed: {result[ErrorPrefix.Length..]}");
        }

        return result;
    }
}
