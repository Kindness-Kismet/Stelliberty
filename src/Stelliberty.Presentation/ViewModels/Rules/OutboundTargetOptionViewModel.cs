namespace Stelliberty.Presentation.ViewModels;

public sealed record OutboundTargetOptionViewModel(
    string Name,
    string Value,
    bool IsCustom = false);
