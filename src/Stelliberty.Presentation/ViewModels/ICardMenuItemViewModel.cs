namespace Stelliberty.Presentation.ViewModels;

public interface ICardMenuItemViewModel
{
    string DisplayName { get; }

    string IconType { get; }

    string AutomationId { get; }

    bool IsDanger { get; }
}
