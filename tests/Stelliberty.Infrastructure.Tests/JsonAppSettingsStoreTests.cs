using Stelliberty.Application.Platform;
using Stelliberty.Infrastructure.Settings;
using Xunit;

namespace Stelliberty.Infrastructure.Tests;

public sealed class JsonAppSettingsStoreTests
{
    [Fact(DisplayName = "Settings stores preserve changes made from independent snapshots")]
    public void SettingsStoresPreserveChangesMadeFromIndependentSnapshots()
    {
        var root = Path.Combine(Path.GetTempPath(), $"stelliberty-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");

        try
        {
            var directories = new FakePlatformDirectories(root, settingsPath);
            var trayStore = new JsonAppSettingsStore(directories);
            var uiStore = new JsonAppSettingsStore(directories);
            var traySettings = trayStore.Load();
            var uiSettings = uiStore.Load();

            traySettings.IsTunEnabled = true;
            traySettings.OutboundMode = "Global";
            trayStore.Save(traySettings);
            uiSettings.WindowWidth = 1280;
            uiSettings.Theme = "Dark";
            uiStore.Save(uiSettings);

            var saved = trayStore.Load();
            Assert.True(saved.IsTunEnabled);
            Assert.Equal("Global", saved.OutboundMode);
            Assert.Equal(1280, saved.WindowWidth);
            Assert.Equal("Dark", saved.Theme);
            Assert.True(uiSettings.IsTunEnabled);
            Assert.Equal("Global", uiSettings.OutboundMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact(DisplayName = "Concurrent settings stores merge unrelated property updates")]
    public async Task ConcurrentSettingsStoresMergeUnrelatedPropertyUpdates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"stelliberty-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settingsPath = Path.Combine(root, "settings.json");

        try
        {
            var directories = new FakePlatformDirectories(root, settingsPath);
            var firstStore = new JsonAppSettingsStore(directories);
            var secondStore = new JsonAppSettingsStore(directories);
            var first = firstStore.Load();
            var second = secondStore.Load();
            first.TunToggleHotkey = "Ctrl+F8";
            second.WindowHeight = 860;
            using var ready = new CountdownEvent(2);
            using var start = new ManualResetEventSlim();

            var firstSave = Task.Run(() =>
            {
                ready.Signal();
                start.Wait();
                firstStore.Save(first);
            });
            var secondSave = Task.Run(() =>
            {
                ready.Signal();
                start.Wait();
                secondStore.Save(second);
            });
            ready.Wait();
            start.Set();
            await Task.WhenAll(firstSave, secondSave);

            var saved = firstStore.Load();
            Assert.Equal("Ctrl+F8", saved.TunToggleHotkey);
            Assert.Equal(860, saved.WindowHeight);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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
