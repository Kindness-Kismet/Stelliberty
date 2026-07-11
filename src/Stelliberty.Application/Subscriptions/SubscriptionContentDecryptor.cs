namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionContentDecryptor : ISubscriptionContentDecryptor
{
    private const string AgeArmorHeader = "-----BEGIN AGE ENCRYPTED FILE-----";

    public string DecryptIfNeeded(string content, string ageSecretKey)
    {
        if (string.IsNullOrWhiteSpace(ageSecretKey) || !IsAgeArmor(content))
        {
            return content;
        }

        throw new InvalidOperationException("Age encrypted subscription content is not supported in this build");
    }

    public static bool IsAgeArmor(string content)
    {
        return content.TrimStart('﻿', '\r', '\n', '\t', ' ').StartsWith(AgeArmorHeader, StringComparison.Ordinal);
    }
}
