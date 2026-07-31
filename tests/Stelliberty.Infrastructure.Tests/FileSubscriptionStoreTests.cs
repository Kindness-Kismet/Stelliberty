using System.Text.Json;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Infrastructure.Subscriptions;
using Xunit;

namespace Stelliberty.Infrastructure.Tests;

public sealed class FileSubscriptionStoreTests
{
    [Theory(DisplayName = "Subscription store recovers a missing or null list before saving")]
    [InlineData("{}")]
    [InlineData("{\"Subscriptions\":null}")]
    public void SubscriptionStoreRecoversMissingOrNullListBeforeSaving(string listContent)
    {
        var root = Path.Combine(Path.GetTempPath(), $"stelliberty-subscriptions-{Guid.NewGuid():N}");
        var directory = Path.Combine(root, "subscriptions");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "subscriptions_list.json"), listContent);

        try
        {
            var store = new FileSubscriptionStore(root);
            var subscription = new Subscription(
                "demo",
                "Demo",
                "https://example.test/subscription",
                IsLocalFile: false,
                CreatedAt: DateTimeOffset.UnixEpoch);

            store.Save(subscription, "proxies: []");

            Assert.Equal("demo", Assert.Single(store.LoadSubscriptions()).Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Subscription store writes strictly PascalCase fields")]
    public void SubscriptionStoreWritesStrictlyPascalCaseFields()
    {
        var root = Path.Combine(Path.GetTempPath(), $"stelliberty-subscriptions-{Guid.NewGuid():N}");

        try
        {
            var store = new FileSubscriptionStore(root);
            var subscription = new Subscription(
                "demo",
                "Demo",
                "https://example.test/subscription",
                IsLocalFile: false,
                CreatedAt: DateTimeOffset.UnixEpoch);

            store.Save(subscription, "proxies: []");

            using var document = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(root, "subscriptions", "subscriptions_list.json")));
            var rootElement = document.RootElement;
            var storedSubscription = Assert.Single(rootElement.GetProperty("Subscriptions").EnumerateArray());

            Assert.False(rootElement.TryGetProperty("subscriptions", out _));
            Assert.All(rootElement.EnumerateObject(), property => Assert.True(char.IsUpper(property.Name[0])));
            Assert.All(storedSubscription.EnumerateObject(), property => Assert.True(char.IsUpper(property.Name[0])));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Subscription save preserves the index when the existing file cannot be read")]
    public void SubscriptionSavePreservesIndexWhenExistingFileCannotBeRead()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"stelliberty-subscriptions-{Guid.NewGuid():N}");
        var store = new FileSubscriptionStore(root);
        var original = new Subscription(
            "original",
            "Original",
            "https://example.test/original",
            IsLocalFile: false,
            CreatedAt: DateTimeOffset.UnixEpoch);
        store.Save(original, "proxies: []");
        var listPath = Path.Combine(root, "subscriptions", "subscriptions_list.json");
        var originalList = File.ReadAllText(listPath);

        try
        {
            using (new FileStream(listPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var added = original with { Id = "added", Name = "Added" };
                Assert.Throws<IOException>(() => store.Save(added, "proxies: []"));
            }

            Assert.Equal(originalList, File.ReadAllText(listPath));
            Assert.Equal("original", Assert.Single(store.LoadSubscriptions()).Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Subscription store rejects lowercase persisted fields")]
    public void SubscriptionStoreRejectsLowercasePersistedFields()
    {
        var root = Path.Combine(Path.GetTempPath(), $"stelliberty-subscriptions-{Guid.NewGuid():N}");
        var directory = Path.Combine(root, "subscriptions");
        var listPath = Path.Combine(directory, "subscriptions_list.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(listPath, "{\"subscriptions\":[]}");

        try
        {
            var store = new FileSubscriptionStore(root);

            Assert.Empty(store.LoadSubscriptions());
            Assert.False(File.Exists(listPath));
            Assert.True(File.Exists(listPath + ".corrupt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
