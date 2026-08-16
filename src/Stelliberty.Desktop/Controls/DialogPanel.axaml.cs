using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stelliberty.Presentation.Dialogs;

namespace Stelliberty.Desktop.Controls;

public sealed partial class DialogPanel : UserControl
{
    public static readonly StyledProperty<Control?> DialogContentProperty =
        AvaloniaProperty.Register<DialogPanel, Control?>(nameof(DialogContent));

    public static readonly StyledProperty<double> PanelWidthProperty =
        AvaloniaProperty.Register<DialogPanel, double>(nameof(PanelWidth), 600);

    // 设为 0 让滚动条贴对话框边缘
    public static readonly StyledProperty<Thickness> ContentPaddingProperty =
        AvaloniaProperty.Register<DialogPanel, Thickness>(nameof(ContentPadding), new Thickness(24));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<DialogPanel, bool>(nameof(IsOpen), true);

    private int _visibilityRevision;

    public DialogPanel()
    {
        InitializeComponent();
        Focusable = true;
        IsTabStop = false;
        AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public Control? DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }

    public double PanelWidth
    {
        get => GetValue(PanelWidthProperty);
        set => SetValue(PanelWidthProperty, value);
    }

    public Thickness ContentPadding
    {
        get => GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty)
        {
            SyncOpenState(change.GetNewValue<bool>());
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Visual source)
        {
            return;
        }

        if (InputDefocusBehavior.IsInsideInteractiveControl(source))
        {
            return;
        }

        Focus(NavigationMethod.Pointer);
    }

    private void SyncOpenState(bool isOpen)
    {
        var revision = ++_visibilityRevision;
        if (isOpen)
        {
            IsVisible = true;
            IsHitTestVisible = true;
            Opacity = 1;
            return;
        }

        IsHitTestVisible = false;
        Opacity = 1;
        if (VisualRoot is null)
        {
            IsVisible = false;
            return;
        }

        DispatcherTimer.RunOnce(
            () =>
            {
                if (_visibilityRevision == revision && !IsOpen)
                {
                    IsVisible = false;
                }
            },
            DialogTiming.ExitDuration);
    }
}
