namespace Stelliberty.Presentation.ViewModels;

public sealed record ConnectionDetailGroupViewModel(
    string Title,
    IReadOnlyList<ConnectionDetailRowViewModel> Rows);
