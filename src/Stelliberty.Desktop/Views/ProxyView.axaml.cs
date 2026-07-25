using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stelliberty.Desktop.Controls;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

public sealed partial class ProxyView : UserControl, IPageContentLifecycle
{
    // 单格滚轮约移动一个分组标签，触控板增量仍按比例生效。
    private const double GroupTabsWheelStep = 72;
    private static readonly TimeSpan GroupExpandDuration = TimeSpan.FromMilliseconds(220);
    private readonly Dictionary<Border, GroupContentAnimationState> _groupContentAnimations = [];
    private CancellationTokenSource _groupContentSequenceCancellation = new();
    private int _groupContentSyncVersion;
    private ProxyPageViewModel? _attachedViewModel;
    private bool _isPageContentActive;
    private int _handledLocateNodeRequestId;
    private int _handledScrollToTopRequestId;
    private double _savedNodeScrollOffset;

    public ProxyView()
    {
        InitializeComponent();
        GroupTabsScroll.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnGroupTabsPointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        ProxyPageRoot.DataContextChanged += OnDataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _savedNodeScrollOffset = NodeListScroll.Offset.Y;
        DeactivatePageContent();
        DetachViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_isPageContentActive)
        {
            return;
        }

        AttachViewModel();
        RestoreNodeScrollOffset();
    }

    private void RestoreNodeScrollOffset()
    {
        if (_savedNodeScrollOffset <= 0)
        {
            return;
        }

        var offset = _savedNodeScrollOffset;
        Dispatcher.UIThread.Post(
            () => NodeListScroll.Offset = NodeListScroll.Offset.WithY(offset),
            DispatcherPriority.Background);
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_isPageContentActive)
        {
            AttachViewModel();
        }
        else
        {
            DetachViewModel();
        }
    }

    private void AttachViewModel()
    {
        DetachViewModel();

        _attachedViewModel = ProxyPageRoot.DataContext as ProxyPageViewModel;
        _handledLocateNodeRequestId = _attachedViewModel?.LocateNodeRequestId ?? 0;
        _handledScrollToTopRequestId = 0;
        if (_attachedViewModel is not null)
        {
            _attachedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (_isPageContentActive)
            {
                _attachedViewModel.ActivatePresentation();
            }
        }
    }

    private void DetachViewModel()
    {
        if (_attachedViewModel is null)
        {
            return;
        }

        _attachedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _attachedViewModel = null;
    }

    void IPageContentLifecycle.ActivatePageContent()
    {
        _isPageContentActive = true;
        AttachViewModel();
        RestoreNodeScrollOffset();
    }

    void IPageContentLifecycle.DeactivatePageContent()
        => DeactivatePageContent();

    private void DeactivatePageContent()
    {
        _isPageContentActive = false;
        _attachedViewModel?.DeactivatePresentation();
        DetachViewModel();
    }

    void IPageContentLifecycle.ReleasePageContent()
    {
        var viewModel = _attachedViewModel ?? ProxyPageRoot.DataContext as ProxyPageViewModel;
        DeactivatePageContent();
        viewModel?.ReleasePresentationCache();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not ProxyPageViewModel viewModel)
        {
            return;
        }

        if (args.PropertyName == nameof(ProxyPageViewModel.LocateNodeRequestId)
            && viewModel.LocateNodeRequestId != _handledLocateNodeRequestId)
        {
            _handledLocateNodeRequestId = viewModel.LocateNodeRequestId;
            if (viewModel.LocatedNodeName is not null)
            {
                ScrollToNode(viewModel.LocatedNodeName);
            }
        }

        if (args.PropertyName == nameof(ProxyPageViewModel.ScrollToTopRequestId)
            && viewModel.ScrollToTopRequestId != _handledScrollToTopRequestId)
        {
            _handledScrollToTopRequestId = viewModel.ScrollToTopRequestId;
            ScrollToTop();
        }
    }

    private void ScrollToNode(string nodeName)
    {
        if (_attachedViewModel is null)
        {
            return;
        }

        var index = _attachedViewModel.IndexOfNode(nodeName);
        if (index < 0)
        {
            return;
        }

        NodeList.GetVisualDescendants().OfType<VirtualizingWrapPanel>().FirstOrDefault()?.BringIndexIntoView(index);
    }

    private void ScrollToTop()
    {
        NodeList.UpdateLayout();
        NodeListScroll.Offset = NodeListScroll.Offset.WithY(0);
    }

    private void OnGroupScrollLeft(object? sender, RoutedEventArgs args)
    {
        var step = GroupTabsScroll.Viewport.Width * 0.6;
        GroupTabsScroll.Offset = GroupTabsScroll.Offset.WithX(Math.Max(0, GroupTabsScroll.Offset.X - step));
    }

    private void OnGroupScrollRight(object? sender, RoutedEventArgs args)
    {
        var step = GroupTabsScroll.Viewport.Width * 0.6;
        var maxX = Math.Max(0, GroupTabsScroll.Extent.Width - GroupTabsScroll.Viewport.Width);
        GroupTabsScroll.Offset = GroupTabsScroll.Offset.WithX(Math.Min(maxX, GroupTabsScroll.Offset.X + step));
    }

    private void OnGroupTabsPointerWheelChanged(object? sender, PointerWheelEventArgs args)
    {
        var delta = Math.Abs(args.Delta.X) > Math.Abs(args.Delta.Y) ? args.Delta.X : args.Delta.Y;
        var maxX = Math.Max(0, GroupTabsScroll.Extent.Width - GroupTabsScroll.Viewport.Width);
        var nextX = Math.Clamp(GroupTabsScroll.Offset.X - delta * GroupTabsWheelStep, 0, maxX);
        if (Math.Abs(nextX - GroupTabsScroll.Offset.X) < 0.5)
        {
            return;
        }

        GroupTabsScroll.Offset = GroupTabsScroll.Offset.WithX(nextX);
        args.Handled = true;
    }

    private void OnGroupContentAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is not Border content || content.DataContext is not ProxyGroupCardViewModel card)
        {
            return;
        }

        RemoveGroupContentAnimation(content);
        PropertyChangedEventHandler handler = (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(ProxyGroupCardViewModel.IsExpanded))
            {
                QueueGroupContentSynchronization();
            }
        };
        _groupContentAnimations[content] = new GroupContentAnimationState(card, handler);
        card.PropertyChanged += handler;
        SetGroupContentState(content, card.IsExpanded);
    }

    private void OnGroupContentDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Border content)
        {
            RemoveGroupContentAnimation(content);
        }
    }

    private void QueueGroupContentSynchronization()
    {
        var syncVersion = ++_groupContentSyncVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (syncVersion == _groupContentSyncVersion)
            {
                _ = SynchronizeGroupContentAsync();
            }
        }, DispatcherPriority.Background);
    }

    private async Task SynchronizeGroupContentAsync()
    {
        _groupContentSequenceCancellation.Cancel();
        _groupContentSequenceCancellation.Dispose();
        _groupContentSequenceCancellation = new CancellationTokenSource();
        var cancellationToken = _groupContentSequenceCancellation.Token;
        var targetContent = _groupContentAnimations
            .Where(item => item.Value.Card.IsExpanded)
            .Select(item => item.Key)
            .FirstOrDefault();

        foreach (var content in _groupContentAnimations.Keys.Where(item => item != targetContent && item.IsVisible).ToList())
        {
            if (!await AnimateGroupContentAsync(content, false, cancellationToken))
            {
                return;
            }
        }

        if (targetContent is not null
            && _groupContentAnimations.TryGetValue(targetContent, out var state)
            && state.Card.IsExpanded
            && !targetContent.IsVisible)
        {
            await AnimateGroupContentAsync(targetContent, true, cancellationToken);
        }
    }

    private static async Task<bool> AnimateGroupContentAsync(
        Border content,
        bool isExpanded,
        CancellationToken cancellationToken)
    {
        var startHeight = isExpanded ? 0 : content.Bounds.Height;
        var endHeight = isExpanded ? MeasureExpandedHeight(content) : 0;
        content.IsVisible = true;
        content.MaxHeight = startHeight;
        content.Opacity = isExpanded ? 0 : 1;

        var animation = new Animation
        {
            Duration = GroupExpandDuration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(MaxHeightProperty, startHeight),
                        new Setter(OpacityProperty, isExpanded ? 0d : 1d),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(MaxHeightProperty, endHeight),
                        new Setter(OpacityProperty, isExpanded ? 1d : 0d),
                    },
                },
            },
        };

        try
        {
            await animation.RunAsync(content, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (isExpanded)
        {
            content.MaxHeight = double.PositiveInfinity;
            content.Opacity = 1;
        }
        else
        {
            SetGroupContentState(content, false);
        }

        return true;
    }

    private static double MeasureExpandedHeight(Border content)
    {
        content.IsVisible = true;
        content.MaxHeight = double.PositiveInfinity;
        var availableWidth = (content.Parent as Control)?.Bounds.Width ?? content.Bounds.Width;
        content.Measure(new Size(availableWidth, double.PositiveInfinity));
        return content.DesiredSize.Height;
    }

    private static void SetGroupContentState(Border content, bool isExpanded)
    {
        content.IsVisible = isExpanded;
        content.MaxHeight = isExpanded ? double.PositiveInfinity : 0;
        content.Opacity = isExpanded ? 1 : 0;
    }

    private void RemoveGroupContentAnimation(Border content)
    {
        if (!_groupContentAnimations.Remove(content, out var state))
        {
            return;
        }

        state.Card.PropertyChanged -= state.Handler;
    }

    private sealed class GroupContentAnimationState(
        ProxyGroupCardViewModel card,
        PropertyChangedEventHandler handler)
    {
        public ProxyGroupCardViewModel Card { get; } = card;

        public PropertyChangedEventHandler Handler { get; } = handler;

    }
}
