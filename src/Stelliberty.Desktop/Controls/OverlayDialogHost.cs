using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Threading;
using Stelliberty.Presentation.Dialogs;

namespace Stelliberty.Desktop.Controls;

public sealed class OverlayDialogHost : Control
{
    public static readonly StyledProperty<Control?> DialogContentProperty =
        AvaloniaProperty.Register<OverlayDialogHost, Control?>(nameof(DialogContent));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<OverlayDialogHost, bool>(nameof(IsOpen));

    // 禁用虚拟化对话框的卡片缩放，避免变换干扰视口。
    public static readonly StyledProperty<bool> AnimateScaleProperty =
        AvaloniaProperty.Register<OverlayDialogHost, bool>(nameof(AnimateScale), true);

    private readonly Border _scrim;
    private readonly ContentPresenter _presenter;
    private OverlayLayer? _layer;
    private IDisposable? _boundsSubscription;
    private bool _closing;
    private bool _clearContentAfterClose;
    private long _animationRevision;

    private const double DialogMargin = 32;

    private static readonly ITransform ClosedTransform = DialogAnimation.ClosedTransform;
    private static readonly ITransform OpenTransform = DialogAnimation.OpenTransform;
    private static readonly ITransform ExitTransform = DialogAnimation.ExitTransform;

    public OverlayDialogHost()
    {
        _presenter = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = ClosedTransform,
            Opacity = 0,
        };
        _presenter[!ContentPresenter.ContentProperty] = this[!DialogContentProperty];

        _scrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            Child = _presenter,
            Opacity = 0,
        };
        _scrim[!DataContextProperty] = this[!DataContextProperty];
    }

    public Control? DialogContent
    {
        get => GetValue(DialogContentProperty);
        set => SetValue(DialogContentProperty, value);
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public bool AnimateScale
    {
        get => GetValue(AnimateScaleProperty);
        set => SetValue(AnimateScaleProperty, value);
    }

    public void Show(Control content)
    {
        _clearContentAfterClose = false;
        DialogContent = content;
        IsOpen = true;
    }

    public void Close()
    {
        _clearContentAfterClose = true;
        IsOpen = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _layer = OverlayLayer.GetOverlayLayer(this);
        SyncScrim();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _closing = true;
        _animationRevision++;
        RemoveScrim();
        _layer = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsOpenProperty)
        {
            SyncScrim();
        }
        else if (change.Property == AnimateScaleProperty && !change.GetNewValue<bool>())
        {
            _presenter.RenderTransform = OpenTransform;
        }
    }

    private void SyncScrim()
    {
        if (IsOpen)
        {
            AttachScrim();
        }
        else
        {
            DetachScrim();
        }
    }

    private void AttachScrim()
    {
        if (_layer is null)
        {
            return;
        }

        _closing = false;
        var revision = ++_animationRevision;

        if (_scrim.Parent is null)
        {
            _scrim.Transitions = null;
            _presenter.Transitions = null;
            _scrim.Opacity = 0;
            _presenter.Opacity = 0;
            _presenter.RenderTransform = AnimateScale ? ClosedTransform : OpenTransform;

            _boundsSubscription?.Dispose();
            _boundsSubscription = _layer.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(bounds =>
            {
                _scrim.Width = bounds.Width;
                _scrim.Height = bounds.Height;

                _presenter.MaxHeight = Math.Max(0, bounds.Height - DialogMargin * 2);
                _presenter.MaxWidth = Math.Max(0, bounds.Width - DialogMargin * 2);
            }));
            _layer.Children.Add(_scrim);
        }

        ConfigureTransitions(DialogTiming.EnterDuration, DialogAnimation.EnterEasing);
        RequestOpenFrame(revision);
    }

    private void DetachScrim()
    {
        if (_scrim.Parent is null)
        {
            RemoveScrim();
            ClearContentAfterClose();
            return;
        }

        _closing = true;
        var revision = ++_animationRevision;
        ConfigureTransitions(DialogTiming.ExitDuration, DialogAnimation.ExitEasing);
        _scrim.Opacity = 0;
        _presenter.Opacity = 0;
        _presenter.RenderTransform = AnimateScale ? ExitTransform : OpenTransform;

        DispatcherTimer.RunOnce(
            () =>
            {
                if (_closing && revision == _animationRevision)
                {
                    RemoveScrim();
                    ClearContentAfterClose();
                }
            },
            DialogTiming.ExitDuration);
    }

    private void RequestOpenFrame(long revision)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            if (IsOpen && revision == _animationRevision)
            {
                _scrim.Opacity = 1;
                _presenter.Opacity = 1;
                _presenter.RenderTransform = OpenTransform;
            }

            return;
        }

        topLevel.RequestAnimationFrame(
            _ =>
            {
                if (!IsOpen || revision != _animationRevision)
                {
                    return;
                }

                _scrim.Opacity = 1;
                _presenter.Opacity = 1;
                _presenter.RenderTransform = OpenTransform;
            });
    }

    private void ConfigureTransitions(TimeSpan duration, Easing easing)
    {
        var presenterTransitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration,
                Easing = easing,
            },
        };
        if (AnimateScale)
        {
            presenterTransitions.Add(
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = duration,
                    Easing = easing,
                });
        }

        _presenter.Transitions = presenterTransitions;
        _scrim.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = duration,
                Easing = easing,
            },
        };
    }

    private void RemoveScrim()
    {
        _boundsSubscription?.Dispose();
        _boundsSubscription = null;
        if (_scrim.Parent is Panel panel)
        {
            panel.Children.Remove(_scrim);
        }
    }

    private void ClearContentAfterClose()
    {
        if (!_clearContentAfterClose)
        {
            return;
        }

        _clearContentAfterClose = false;
        DialogContent = null;
    }
}
