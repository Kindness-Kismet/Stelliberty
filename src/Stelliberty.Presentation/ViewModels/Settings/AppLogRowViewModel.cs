using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;

namespace Stelliberty.Presentation.ViewModels;

public sealed record AppLogRowViewModel(int Index, AppLogEntry Entry, ILocalizationService? Localization = null)
{
    public string RowAutomationId => $"AppLogs.Row.{Index}";

    public string TimeAutomationId => $"AppLogs.Row.{Index}.TimeText";

    public string LevelAutomationId => $"AppLogs.Row.{Index}.LevelText";

    public string MessageAutomationId => $"AppLogs.Row.{Index}.MessageText";

    public string Message => Entry.Message;

    public string FormattedTime => Entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string LevelText => Entry.Level switch
    {
        AppLogLevel.Debug => Localize("AppLogs.Level.Debug"),
        AppLogLevel.Info => Localize("AppLogs.Level.Info"),
        AppLogLevel.Warning => Localize("AppLogs.Level.Warning"),
        AppLogLevel.Error => Localize("AppLogs.Level.Error"),
        _ => Localize("AppLogs.Level.Info")
    };

    public string LevelStyleClass => Entry.Level switch
    {
        AppLogLevel.Debug => "log-level-debug",
        AppLogLevel.Info => "log-level-info",
        AppLogLevel.Warning => "log-level-warning",
        AppLogLevel.Error => "log-level-error",
        _ => "log-level-info"
    };

    private string Localize(string key) => Localization?.GetString(key) ?? key;
}
