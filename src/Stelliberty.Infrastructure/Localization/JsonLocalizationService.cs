using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;

namespace Stelliberty.Infrastructure.Localization;

public sealed class JsonLocalizationService : ILocalizationService
{
    private const string AssetNamespace = "Stelliberty.Infrastructure.Localization.Assets";

    private readonly IReadOnlyDictionary<AppLanguage, IReadOnlyDictionary<string, string>> _catalogs;
    private AppLanguage _currentLanguage;

    public JsonLocalizationService(AppLanguage initialLanguage)
    {
        _catalogs = LoadCatalogs();
        _currentLanguage = initialLanguage;
    }

    public AppLanguage CurrentLanguage => _currentLanguage;

    public AppLanguage EffectiveLanguage => _currentLanguage switch
    {
        AppLanguage.ZhHans => AppLanguage.ZhHans,
        AppLanguage.ZhHant => AppLanguage.ZhHant,
        AppLanguage.En => AppLanguage.En,
        _ => CultureInfo.CurrentUICulture.Name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
            || CultureInfo.CurrentUICulture.Name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
            || CultureInfo.CurrentUICulture.Name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
            || CultureInfo.CurrentUICulture.Name.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.ZhHant
            : CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.ZhHans
                : AppLanguage.En
    };

    public event EventHandler? LanguageChanged;

    public void SetLanguage(AppLanguage language)
    {
        if (_currentLanguage == language)
        {
            return;
        }

        _currentLanguage = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetString(string key)
    {
        if (_catalogs.TryGetValue(EffectiveLanguage, out var catalog) && catalog.TryGetValue(key, out var value))
        {
            return value;
        }

        if (EffectiveLanguage != AppLanguage.En
            && _catalogs.TryGetValue(AppLanguage.En, out var fallback)
            && fallback.TryGetValue(key, out var fallbackValue))
        {
            AppLogger.Debug($"i18n missing key ({EffectiveLanguage}): {key}");
            return fallbackValue;
        }

        AppLogger.Debug($"i18n missing key: {key}");
        return key;
    }

    private static IReadOnlyDictionary<AppLanguage, IReadOnlyDictionary<string, string>> LoadCatalogs()
    {
        return new Dictionary<AppLanguage, IReadOnlyDictionary<string, string>>
        {
            [AppLanguage.ZhHans] = LoadCatalog("zh-Hans.json"),
            [AppLanguage.ZhHant] = LoadCatalog("zh-Hant.json"),
            [AppLanguage.En] = LoadCatalog("en.json")
        };
    }

    private static IReadOnlyDictionary<string, string> LoadCatalog(string fileName)
    {
        var assembly = typeof(JsonLocalizationService).Assembly;
        var resourceName = $"{AssetNamespace}.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource is missing: {resourceName}", fileName);
        var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Language pack deserialization failed: {fileName}");
        return dictionary;
    }
}
