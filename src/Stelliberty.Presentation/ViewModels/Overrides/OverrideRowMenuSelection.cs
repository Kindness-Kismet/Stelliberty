namespace Stelliberty.Presentation.ViewModels;

public sealed record OverrideRowMenuSelection(string OverrideId, OverrideRowMenuAction Action, string DisplayName) : ICardMenuItemViewModel
{
    public string IconType => Action switch
    {
        OverrideRowMenuAction.Edit => "EditLine",
        OverrideRowMenuAction.EditFile => "CodeLine",
        OverrideRowMenuAction.OpenExternalEditor => "ExternalLinkLine",
        OverrideRowMenuAction.Delete => "DeleteLine",
        _ => "More2Line"
    };

    public string AutomationId => $"Overrides.Row.{OverrideId}.Menu.{Action}";

    public bool IsDanger => Action == OverrideRowMenuAction.Delete;
}
