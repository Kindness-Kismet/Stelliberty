using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stelliberty.Desktop.Controls;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

public sealed partial class ProxyView : UserControl, IPageContentLifecycle
{
    // 单格滚轮约移动一个分组标签，触控板增量仍按比例生效。
    private const double GroupTabsWheelStep = 72;
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

    void IPageContentLifecycle.WarmupPageContent()
    {
        if (_isPageContentActive
            || ProxyPageRoot.DataContext is not ProxyPageViewModel viewModel)
        {
            return;
        }

        viewModel.WarmupPresentation();
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
}
