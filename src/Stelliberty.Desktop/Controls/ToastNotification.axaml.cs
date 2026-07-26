using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Reactive;
using Avalonia.Threading;
using IconPacks.Avalonia.MingCuteIcons;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Controls;

public sealed partial class ToastNotification : UserControl
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<ToastNotification, string>(nameof(Message), string.Empty);

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ToastNotification, bool>(nameof(IsOpen));

    public static readonly StyledProperty<ToastType> TypeProperty =
        AvaloniaProperty.Register<ToastNotification, ToastType>(nameof(Type));

    private readonly Panel _host;
    private readonly Border _toast;
    private readonly PackIconMingCuteIcons _icon;
    private readonly TextBlock _messageText;
    private OverlayLayer? _layer;
    private IDisposable? _boundsSubscription;
    private int _animationRevision;

    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(500);
    private static readonly ITransform ClosedTransform = TransformOperations.Parse("translate(0px,16px) scale(0.98)");
    private static readonly ITransform OpenTransform = TransformOperations.Parse("translate(0px,0px) scale(1)");

    public ToastNotification()
    {
        InitializeComponent();

        _icon = new PackIconMingCuteIcons
        {
            Classes = { "toast-icon" }
        };
        _messageText = new TextBlock
        {
            Classes = { "toast-message" }
        };
        var content = new Grid
        {
            Classes = { "toast-content" },
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children = { _icon, _messageText }
        };
        Grid.SetColumn(_messageText, 1);

        _toast = new Border
        {
            Classes = { "toast" },
            Child = content,
            Opacity = 0,
            RenderTransform = ClosedTransform,
            RenderTransformOrigin = RelativePoint.Center,
        };
        AutomationProperties.SetAutomationId(_toast, "Main.Toast");

        _host = new Panel
        {
            IsHitTestVisible = false,
            IsVisible = false,
            Children = { _toast }
        };
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public ToastType Type
    {
        get => GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _layer = OverlayLayer.GetOverlayLayer(this);
        AttachHost();
        ApplyState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _boundsSubscription?.Dispose();
        _boundsSubscription = null;
        if (_host.Parent is Panel panel)
        {
            panel.Children.Remove(_host);
        }
        _layer = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MessageProperty || change.Property == IsOpenProperty || change.Property == TypeProperty)
        {
            ApplyState(change.Property);
        }
    }

    private void AttachHost()
    {
        if (_layer is null || _host.Parent is not null)
        {
            return;
        }

        _boundsSubscription?.Dispose();
        _boundsSubscription = _layer.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(bounds =>
        {
            _host.Width = bounds.Width;
            _host.Height = bounds.Height;
        }));
        _layer.Children.Add(_host);
    }

    private void ApplyState(AvaloniaProperty? changedProperty = null)
    {
        _messageText.Text = Message;
        ApplyType();
        if (changedProperty is null || changedProperty == IsOpenProperty)
        {
            SyncOpenState(IsOpen);
            return;
        }

        if (IsOpen)
        {
            BringToFront();
        }
    }

    private void SyncOpenState(bool isOpen)
    {
        var revision = ++_animationRevision;
        if (isOpen)
        {
            _host.IsVisible = true;
            _toast.Transitions = null;
            _toast.Opacity = 0;
            _toast.RenderTransform = ClosedTransform;
            BringToFront();

            Dispatcher.UIThread.Post(
                () =>
                {
                    if (revision != _animationRevision || !IsOpen)
                    {
                        return;
                    }

                    _toast.Transitions = CreateOpenTransitions();
                    _toast.Opacity = 1;
                    _toast.RenderTransform = OpenTransform;
                },
                DispatcherPriority.Render);
            return;
        }

        if (!_host.IsVisible)
        {
            _toast.Opacity = 0;
            _toast.RenderTransform = ClosedTransform;
            return;
        }

        _toast.Transitions = CreateCloseTransitions();
        _toast.Opacity = 0;
        _toast.RenderTransform = ClosedTransform;

        DispatcherTimer.RunOnce(
            () =>
            {
                if (revision == _animationRevision && !IsOpen)
                {
                    _host.IsVisible = false;
                }
            },
            CloseDuration);
    }

    private void BringToFront()
    {
        if (_host.Parent is not Panel panel)
        {
            return;
        }

        panel.Children.Remove(_host);
        panel.Children.Add(_host);
    }

    private void ApplyType()
    {
        _toast.Classes.Set("info", Type == ToastType.Info);
        _toast.Classes.Set("success", Type == ToastType.Success);
        _toast.Classes.Set("warning", Type == ToastType.Warning);
        _toast.Classes.Set("error", Type == ToastType.Error);
        _icon.Kind = Type switch
        {
            ToastType.Success => PackIconMingCuteIconsKind.CheckCircleLine,
            ToastType.Warning => PackIconMingCuteIconsKind.AlertDiamondLine,
            ToastType.Error => PackIconMingCuteIconsKind.CloseCircleLine,
            _ => PackIconMingCuteIconsKind.InformationLine
        };
    }

    private static Transitions CreateOpenTransitions()
    {
        return new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = OpenDuration,
                Easing = new CubicEaseOut(),
            },
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = OpenDuration,
                Easing = new CubicEaseOut(),
            },
        };
    }

    private static Transitions CreateCloseTransitions()
    {
        return new Transitions
        {
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = CloseDuration,
                Easing = new CubicEaseIn(),
            },
            new TransformOperationsTransition
            {
                Property = RenderTransformProperty,
                Duration = CloseDuration,
                Easing = new CubicEaseIn(),
            },
        };
    }
}
