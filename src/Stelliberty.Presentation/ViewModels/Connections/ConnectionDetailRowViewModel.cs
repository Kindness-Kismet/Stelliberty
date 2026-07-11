namespace Stelliberty.Presentation.ViewModels;

// Emphasis: mono 等宽；accent 强调；muted 次要
public sealed record ConnectionDetailRowViewModel(
    string Label,
    string Value,
    string Emphasis = "");
