namespace Stelliberty.Presentation.ViewModels;

public interface ISelectionOption
{
    string DisplayName { get; }
}

public sealed record SelectionOption<T>(T Value, string DisplayName) : ISelectionOption;
