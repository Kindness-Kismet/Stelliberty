using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Stelliberty.Desktop.Views.Dialogs;

public sealed partial class AccentColorPickerView : UserControl
{
    public AccentColorPickerView()
    {
        InitializeComponent();
    }

    public Color SelectedColor => PART_ColorView.Color;

    public Color InitialColor
    {
        get => PART_ColorView.Color;
        set => PART_ColorView.Color = value;
    }

    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;

    private void OnConfirmClick(object? sender, RoutedEventArgs args)
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs args)
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
