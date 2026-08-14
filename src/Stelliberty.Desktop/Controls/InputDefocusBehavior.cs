using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Stelliberty.Desktop.Controls;

public static class InputDefocusBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(InputDefocusBehavior));

    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    static InputDefocusBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            control.Focusable = true;
            control.IsTabStop = false;
            control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
            control.AddHandler(Control.RequestBringIntoViewEvent, OnRequestBringIntoView);
        }
        else
        {
            control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            control.RemoveHandler(Control.RequestBringIntoViewEvent, OnRequestBringIntoView);
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control
            && e.Source is Visual source
            && !IsInsideInteractiveControl(source))
        {
            control.Focus(NavigationMethod.Pointer);
        }
    }

    internal static bool IsInsideInteractiveControl(Visual source)
    {
        return source.FindAncestorOfType<TextBox>(includeSelf: true) is not null
            || source.FindAncestorOfType<CodeEditor>(includeSelf: true) is not null
            || source.FindAncestorOfType<Button>(includeSelf: true) is not null
            || source.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null
            || source.FindAncestorOfType<ComboBoxItem>(includeSelf: true) is not null
            || source.FindAncestorOfType<ComboBox>(includeSelf: true) is not null;
    }

    private static void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (ReferenceEquals(e.TargetObject, sender))
        {
            e.Handled = true;
        }
    }
}
