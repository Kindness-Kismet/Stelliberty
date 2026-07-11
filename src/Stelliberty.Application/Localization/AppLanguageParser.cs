namespace Stelliberty.Application.Localization;

public static class AppLanguageParser
{
    public static AppLanguage Parse(string value)
    {
        return Enum.TryParse<AppLanguage>(value, out var result) ? result : AppLanguage.System;
    }
}
