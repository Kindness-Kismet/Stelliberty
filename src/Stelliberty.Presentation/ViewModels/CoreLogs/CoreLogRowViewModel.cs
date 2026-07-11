using Stelliberty.Application.CoreLogs;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Application.Localization;

namespace Stelliberty.Presentation.ViewModels;

public sealed record CoreLogRowViewModel(int Index, CoreLogMessage Message, ILocalizationService? Localization = null)
{
    public string RowAutomationId => $"CoreLogs.Row.{Index}";

    public string TimeAutomationId => $"CoreLogs.Row.{Index}.TimeText";

    public string LevelAutomationId => $"CoreLogs.Row.{Index}.LevelText";

    public string PayloadAutomationId => $"CoreLogs.Row.{Index}.PayloadText";

    public string Type => Message.Type;

    public string Payload => Message.Payload;

    public CoreLogLevel Level => Message.Level;

    public string LevelText => Level switch
    {
        CoreLogLevel.Debug => Localize("CoreLogs.Level.Debug"),
        CoreLogLevel.Info => Localize("CoreLogs.Level.Info"),
        CoreLogLevel.Warning => Localize("CoreLogs.Level.Warning"),
        CoreLogLevel.Error => Localize("CoreLogs.Level.Error"),
        _ => Localize("CoreLogs.Level.Silent")
    };

    public string LevelStyleClass => Level switch
    {
        CoreLogLevel.Debug => "log-level-debug",
        CoreLogLevel.Info => "log-level-info",
        CoreLogLevel.Warning => "log-level-warning",
        CoreLogLevel.Error => "log-level-error",
        _ => "log-level-silent"
    };

    public string FormattedTime => Message.Timestamp.ToLocalTime().ToString("HH:mm:ss");

    private string Localize(string key) => Localization?.GetString(key) ?? key;
}
