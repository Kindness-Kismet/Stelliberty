using Stelliberty.Application.Platform;
using Stelliberty.Infrastructure.Settings;
using Xunit;

namespace Stelliberty.Infrastructure.Tests;

public sealed class JsonAppSettingsStoreTests
{
    [Fact(DisplayName = "Settings store backs up a corrupt file before returning defaults")]
    public void SettingsStoreBacksUpCorruptFileBeforeReturningDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), $"stelliberty-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");
        File.WriteAllText(settingsPath, "{ corrupted");

        try
        {
            var store = new JsonAppSettingsStore(new FakePlatformDirectories(root, settingsPath));

            var settings = store.Load();

            Assert.NotNull(settings);
            Assert.False(File.Exists(settingsPath));
            Assert.True(File.Exists(settingsPath + ".corrupt"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakePlatformDirectories(string root, string settingsPath) : IPlatformDirectories
    {
        public string AppDataDirectory => root;
        public string DepsDirectory => root;
        public string CoreDirectory => root;
        public string RuntimeDirectory => root;
        public string SettingsFilePath => settingsPath;
    }
}
