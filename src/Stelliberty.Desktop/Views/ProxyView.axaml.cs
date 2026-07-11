using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Stelliberty.Desktop.Controls;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Views;

public sealed partial class ProxyView : UserControl
{
    private ProxyPageViewModel? _attachedViewModel;
    private int _handledScrollToTopRequestId;
    private double _savedNodeScrollOffset;

    public ProxyView()
    {
        InitializeComponent();
        ProxyPageRoot.DataContextChanged += OnDataContextChanged;
        AttachViewModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _savedNodeScrollOffset = NodeListScroll.Offset.Y;
        DetachViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachViewModel();
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
        AttachViewModel();
    }

    private void AttachViewModel()
    {
        DetachViewModel();

        _attachedViewModel = ProxyPageRoot.DataContext as ProxyPageViewModel;
        _handledScrollToTopRequestId = 0;
        if (_attachedViewModel is not null)
        {
            _attachedViewModel.PropertyChanged += OnViewModelPropertyChanged;
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not ProxyPageViewModel viewModel)
        {
            return;
        }

        if (args.PropertyName == nameof(ProxyPageViewModel.LocatedNodeName) && viewModel.LocatedNodeName is not null)
        {
            ScrollToNode(viewModel.LocatedNodeName);
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

        var rows = _attachedViewModel.VisibleNodeRows;
        var index = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].Name, nodeName, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

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
}
