namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionOverrideOptionViewModel(string Id, string Name, string FormatText)
{
    public bool IsSelected { get; init; }

    // FormatText 仅允许 YAML 或 JavaScript，由 Mappers.ToOverrideOption 约束。
    public string IconType => FormatText == "JavaScript" ? "CodeLine" : "FileLine";

    public string ToggleAutomationId => $"Subscriptions.OverrideSelector.{Id}.Toggle";

}
