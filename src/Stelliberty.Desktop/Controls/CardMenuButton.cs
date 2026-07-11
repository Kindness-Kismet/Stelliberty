using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Data;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using IconPacks.Avalonia.MingCuteIcons;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Controls;

public sealed class CardMenuButton : Button
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<CardMenuButton, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<ICommand?> ItemCommandProperty =
        AvaloniaProperty.Register<CardMenuButton, ICommand?>(nameof(ItemCommand));

    public static readonly StyledProperty<string> MoreItemTextProperty =
        AvaloniaProperty.Register<CardMenuButton, string>(nameof(MoreItemText), "More");

    private const double MenuWidth = 220d;
    private const double WindowMargin = 16d;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(140);
    private static readonly ITransform ClosedTransform = TransformOperations.Parse("scale(0.88) translate(0px,-4px)");
    private static readonly ITransform OpenTransform = TransformOperations.Parse("scale(1) translate(0px,0px)");

    private readonly Border _dismissLayer;
    private readonly Border _menu;
    private readonly StackPanel _itemsPanel;
    private OverlayLayer? _layer;
    private TopLevel? _topLevel;
    private IDisposable? _boundsSubscription;
    private List<object> _items = [];
    private int _pageIndex;
    private bool _isOpen;
    private bool _isClosing;
    private static CardMenuButton? OpenButton;

    public CardMenuButton()
    {
        _itemsPanel = new StackPanel();

        _dismissLayer = new Border
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = true,
        };
        _dismissLayer.PointerPressed += OnDismissLayerPointerPressed;

        _menu = new Border
        {
            Classes = { "card-menu-panel" },
            Width = MenuWidth,
            Child = _itemsPanel,
            RenderTransformOrigin = new RelativePoint(1, 0, RelativeUnit.Relative),
            Transitions = CreateOpenTransitions(),
        };

        Click += OnClick;
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? ItemCommand
    {
        get => GetValue(ItemCommandProperty);
        set => SetValue(ItemCommandProperty, value);
    }

    public string MoreItemText
    {
        get => GetValue(MoreItemTextProperty);
        set => SetValue(MoreItemTextProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _layer = OverlayLayer.GetOverlayLayer(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CloseMenu(immediate: true);
        _layer = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty && _isOpen)
        {
            ReloadItems();
            RenderPage();
            PositionMenu();
        }
    }

    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isOpen)
        {
            CloseMenu();
            return;
        }

        OpenMenu();
    }

    private void OpenMenu()
    {
        if (_layer is null)
        {
            return;
        }

        if (!ReferenceEquals(OpenButton, this))
        {
            OpenButton?.CloseMenu(immediate: true);
            OpenButton = this;
        }

        ReloadItems();
        _pageIndex = 0;
        RenderPage();

        _isOpen = true;
        _isClosing = false;
        _menu.Transitions = CreateOpenTransitions();
        _menu.Opacity = 0;
        _menu.RenderTransform = ClosedTransform;

        AttachMenu();
        ListenOutsidePointer();

        if (_menu.Parent is Panel panel)
        {
            panel.Children.Remove(_menu);
            panel.Children.Add(_menu);
        }

        PositionMenu();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isOpen)
            {
                return;
            }

            _menu.Opacity = 1;
            _menu.RenderTransform = OpenTransform;
        }, DispatcherPriority.Render);
    }

    private void CloseMenu(bool immediate = false)
    {
        if (!_isOpen && !_isClosing)
        {
            return;
        }

        _isOpen = false;
        _isClosing = true;
        if (ReferenceEquals(OpenButton, this))
        {
            OpenButton = null;
        }

        StopListeningOutsidePointer();
        _boundsSubscription?.Dispose();
        _boundsSubscription = null;

        if (immediate)
        {
            RemoveMenu();
            _isClosing = false;
            return;
        }

        _menu.Transitions = CreateCloseTransitions();
        _menu.Opacity = 0;
        _menu.RenderTransform = ClosedTransform;

        DispatcherTimer.RunOnce(() =>
        {
            if (!_isClosing)
            {
                return;
            }

            RemoveMenu();
            _isClosing = false;
            _menu.Transitions = CreateOpenTransitions();
        }, CloseDuration);
    }

    private void AttachMenu()
    {
        if (_layer is null)
        {
            return;
        }

        _boundsSubscription?.Dispose();
        _boundsSubscription = _layer.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(_ =>
        {
            _dismissLayer.Width = _layer.Bounds.Width;
            _dismissLayer.Height = _layer.Bounds.Height;
            Canvas.SetLeft(_dismissLayer, 0);
            Canvas.SetTop(_dismissLayer, 0);

            if (_isOpen)
            {
                PositionMenu();
            }
        }));

        if (_dismissLayer.Parent is null)
        {
            _layer.Children.Add(_dismissLayer);
        }

        if (_menu.Parent is not null)
        {
            return;
        }

        _layer.Children.Add(_menu);
    }

    private void RemoveMenu()
    {
        if (_menu.Parent is Panel panel)
        {
            panel.Children.Remove(_menu);
        }

        if (_dismissLayer.Parent is Panel dismissPanel)
        {
            dismissPanel.Children.Remove(_dismissLayer);
        }
    }

    private void ReloadItems()
    {
        _items = ItemsSource?.Cast<object>().ToList() ?? [];
    }

    private void RenderPage()
    {
        _itemsPanel.Children.Clear();
        var pageItems = GetPageItems();
        foreach (var item in pageItems)
        {
            _itemsPanel.Children.Add(BuildItemButton(item));
        }
    }

    private IEnumerable<object> GetPageItems()
    {
        if (_items.Count <= 6)
        {
            return _items;
        }

        return _pageIndex == 0 ? _items.Take(5).Append(MoreMenuItem.Instance) : _items.Skip(5);
    }

    private Button BuildItemButton(object item)
    {
        var isMore = ReferenceEquals(item, MoreMenuItem.Instance);
        var menuItem = item as ICardMenuItemViewModel;
        var iconType = ParseIconType(isMore ? "ListExpansionLine" : menuItem?.IconType ?? "More2Line");
        var text = isMore ? MoreItemText : menuItem?.DisplayName ?? string.Empty;

        var icon = new PackIconMingCuteIcons
        {
            Classes = { "card-menu-icon" },
            Width = 16,
            Height = 16,
            Kind = iconType,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock
        {
            Classes = { "card-menu-text" },
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("24,*"),
            ColumnSpacing = 14,
            Children =
            {
                icon,
                label,
            },
        };
        Grid.SetColumn(label, 1);

        var button = new Button
        {
            Classes = { "card-menu-row" },
            Content = content,
            Tag = item,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(button, BuildAutomationId(item, isMore));

        if (IsDangerItem(item))
        {
            button.Classes.Add("danger");
        }

        icon.Bind(ForegroundProperty, button.GetObservable(ForegroundProperty), BindingPriority.Style);
        label.Bind(TextBlock.ForegroundProperty, button.GetObservable(ForegroundProperty), BindingPriority.Style);
        button.Click += OnItemClick;
        return button;
    }

    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: { } item })
        {
            return;
        }

        if (ReferenceEquals(item, MoreMenuItem.Instance))
        {
            _pageIndex = 1;
            RenderPage();
            PositionMenu();
            return;
        }

        if (ItemCommand?.CanExecute(item) == true)
        {
            ItemCommand.Execute(item);
        }
        CloseMenu();
    }

    private void PositionMenu()
    {
        if (_layer is null || this.TranslatePoint(new Point(Bounds.Width, Bounds.Height), _layer) is not { } origin)
        {
            return;
        }

        _menu.Measure(new Size(MenuWidth, double.PositiveInfinity));
        var bounds = _layer.Bounds;
        var menuSize = _menu.DesiredSize;
        var left = Math.Clamp(origin.X - menuSize.Width, WindowMargin, Math.Max(WindowMargin, bounds.Width - menuSize.Width - WindowMargin));
        var top = Math.Clamp(origin.Y + 6, WindowMargin, Math.Max(WindowMargin, bounds.Height - menuSize.Height - WindowMargin));

        Canvas.SetLeft(_menu, left);
        Canvas.SetTop(_menu, top);
    }

    private void ListenOutsidePointer()
    {
        StopListeningOutsidePointer();
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(InputElement.PointerPressedEvent, OnTopLevelPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _topLevel?.AddHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        if (_topLevel is Window window)
        {
            window.Deactivated += OnWindowDeactivated;
        }
    }

    private void StopListeningOutsidePointer()
    {
        _topLevel?.RemoveHandler(InputElement.PointerPressedEvent, OnTopLevelPointerPressed);
        _topLevel?.RemoveHandler(InputElement.KeyDownEvent, OnTopLevelKeyDown);
        if (_topLevel is Window window)
        {
            window.Deactivated -= OnWindowDeactivated;
        }

        _topLevel = null;
    }

    private void OnTopLevelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isOpen || e.Source is not Visual source || IsInside(source, _menu) || IsInside(source, this))
        {
            return;
        }

        CloseMenu();
        e.Handled = true;
    }

    private void OnDismissLayerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseMenu();
        e.Handled = true;
    }

    private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
    {
        if (_isOpen && e.Key == Key.Escape)
        {
            CloseMenu();
            e.Handled = true;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        CloseMenu();
    }

    private static bool IsInside(Visual source, Visual target)
    {
        for (Visual? current = source; current is not null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
        }

        return false;
    }

    private static PackIconMingCuteIconsKind ParseIconType(string value)
    {
        return Enum.TryParse<PackIconMingCuteIconsKind>(value, ignoreCase: false, out var type)
            ? type
            : PackIconMingCuteIconsKind.More2Line;
    }

    private static bool IsDangerItem(object item)
    {
        return item is ICardMenuItemViewModel { IsDanger: true };
    }

    private static string BuildAutomationId(object item, bool isMore)
    {
        if (isMore)
        {
            return "CardMenu.More";
        }

        return item is ICardMenuItemViewModel menuItem ? menuItem.AutomationId : "CardMenu.Item";
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
                Easing = new BackEaseOut(),
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

    private sealed class MoreMenuItem
    {
        public static readonly MoreMenuItem Instance = new();
    }
}
