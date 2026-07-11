namespace Stelliberty.Presentation.ViewModels;

public sealed class AccentColorOption : ViewModelBase
{
    private bool _isSelected;

    public AccentColorOption(string hexValue)
    {
        HexValue = hexValue;
    }

    public string HexValue { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
