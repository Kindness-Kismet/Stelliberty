using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Stelliberty.Desktop.Controls;

public sealed class VirtualizingWrapPanel : VirtualizingPanel
{
    // 视口外保留一行缓冲，不展开整页元素。
    private const int CacheRows = 1;
    // ScrollViewer 暴露视口前，只预热首屏。
    private const double FallbackViewportHeight = 560d;

    private static readonly AttachedProperty<object?> RecycleKeyProperty =
        AvaloniaProperty.RegisterAttached<VirtualizingWrapPanel, Control, object?>("RecycleKey");

    private readonly Dictionary<int, Control> _realized = new();
    private readonly Dictionary<object, Stack<Control>> _recyclePool = new();
    private Rect _viewport;
    private bool _hasViewport;
    private int _columns = 1;

    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(MinItemWidth), 168d);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ItemHeight), 56d);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(RowSpacing), 3d);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(ColumnSpacing), 3d);

    public static readonly StyledProperty<double> EdgePaddingProperty =
        AvaloniaProperty.Register<VirtualizingWrapPanel, double>(nameof(EdgePadding), 4d);

    static VirtualizingWrapPanel()
    {
        AffectsMeasure<VirtualizingWrapPanel>(MinItemWidthProperty, ItemHeightProperty,
            RowSpacingProperty, ColumnSpacingProperty, EdgePaddingProperty);
    }

    public VirtualizingWrapPanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public double RowSpacing { get => GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }
    public double EdgePadding { get => GetValue(EdgePaddingProperty); set => SetValue(EdgePaddingProperty, value); }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        _hasViewport = true;
        var viewport = e.EffectiveViewport.Intersect(new Rect(Bounds.Size));
        if (this.FindAncestorOfType<ScrollViewer>() is { Viewport.Height: > 0 } scrollViewer
            && viewport.Height > scrollViewer.Viewport.Height)
        {
            viewport = new Rect(viewport.X, viewport.Y, viewport.Width, scrollViewer.Viewport.Height);
        }

        if (viewport != _viewport)
        {
            _viewport = viewport;
            InvalidateMeasure();
        }
    }

    // 列数算法与 UniformWrapPanel 保持一致
    private (int Columns, double ItemWidth) ComputeLayout(double availableWidth)
    {
        var inner = Math.Max(0, availableWidth - 2 * EdgePadding);
        var columnSpacing = ColumnSpacing;
        var step = MinItemWidth + columnSpacing;
        var columns = step > 0 ? Math.Max(1, (int)Math.Floor((inner + columnSpacing) / step)) : 1;
        var itemWidth = (inner - (columns - 1) * columnSpacing) / columns;
        return (columns, Math.Max(0, itemWidth));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items;
        var count = items.Count;
        if (count == 0 || ItemContainerGenerator is null)
        {
            RecycleAllRealized();
            return default;
        }

        var availableWidth = double.IsInfinity(availableSize.Width)
            ? MinItemWidth + 2 * EdgePadding
            : availableSize.Width;
        var (columns, itemWidth) = ComputeLayout(availableWidth);
        _columns = columns;
        var rowPitch = ItemHeight + RowSpacing;

        double viewportTop, viewportBottom;
        if (_hasViewport && _viewport.Height > 0)
        {
            viewportTop = _viewport.Top;
            viewportBottom = _viewport.Bottom;
        }
        else
        {
            viewportTop = 0;
            viewportBottom = FallbackViewportHeight;
        }

        var firstRow = Math.Max(0, (int)Math.Floor(viewportTop / rowPitch) - CacheRows);
        var lastRow = (int)Math.Floor((viewportBottom - 0.0001) / rowPitch) + CacheRows;
        var firstIndex = Math.Clamp(firstRow * columns, 0, count - 1);
        var lastIndex = Math.Clamp((lastRow + 1) * columns - 1, firstIndex, count - 1);

        RecycleOutside(firstIndex, lastIndex);

        var childConstraint = new Size(itemWidth, ItemHeight);
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            var container = GetOrCreateElement(items, index);
            container.Measure(childConstraint);
        }

        var totalRows = (count + columns - 1) / columns;
        var extentHeight = totalRows > 0 ? totalRows * ItemHeight + (totalRows - 1) * RowSpacing : 0;
        return new Size(availableWidth, extentHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = ComputeLayout(finalSize.Width);
        var rowPitch = ItemHeight + RowSpacing;
        var edge = EdgePadding;
        var columnSpacing = ColumnSpacing;
        var layoutScale = LayoutHelper.GetLayoutScale(this);

        foreach (var (index, container) in _realized)
        {
            var column = index % columns;
            var row = index / columns;
            var left = edge + column * (itemWidth + columnSpacing);
            var right = edge + (column + 1) * itemWidth + column * columnSpacing;
            var top = row * rowPitch;
            var bottom = top + ItemHeight;

            if (UseLayoutRounding)
            {
                left = LayoutHelper.RoundLayoutValue(left, layoutScale);
                right = LayoutHelper.RoundLayoutValue(right, layoutScale);
                top = LayoutHelper.RoundLayoutValue(top, layoutScale);
                bottom = LayoutHelper.RoundLayoutValue(bottom, layoutScale);
            }

            // 虚拟化可能跳过前项，直接对齐每个单元格的物理像素边界。
            container.Arrange(new Rect(new Point(left, top), new Point(right, bottom)));
        }

        return finalSize;
    }

    private Control GetOrCreateElement(IReadOnlyList<object?> items, int index)
    {
        if (_realized.TryGetValue(index, out var existing))
            return existing;

        var item = items[index];
        var generator = ItemContainerGenerator!;
        Control container;
        if (generator.NeedsContainer(item, index, out var recycleKey))
        {
            container = TakeFromPool(recycleKey, item, index) ?? CreateContainerCore(item, index, recycleKey);
        }
        else
        {
            container = (Control)item!;
            container.SetValue(RecycleKeyProperty, null);
            AddInternalChild(container);
            generator.PrepareItemContainer(container, item, index);
            generator.ItemContainerPrepared(container, item, index);
        }

        _realized[index] = container;
        return container;
    }

    private Control CreateContainerCore(object? item, int index, object? recycleKey)
    {
        var generator = ItemContainerGenerator!;
        var container = generator.CreateContainer(item, index, recycleKey);
        container.SetValue(RecycleKeyProperty, recycleKey);
        AddInternalChild(container);
        generator.PrepareItemContainer(container, item, index);
        generator.ItemContainerPrepared(container, item, index);
        return container;
    }

    private Control? TakeFromPool(object? recycleKey, object? item, int index)
    {
        if (recycleKey is null || !_recyclePool.TryGetValue(recycleKey, out var pool) || pool.Count == 0)
            return null;

        var recycled = pool.Pop();
        recycled.IsVisible = true;
        var generator = ItemContainerGenerator!;
        generator.PrepareItemContainer(recycled, item, index);
        generator.ItemContainerPrepared(recycled, item, index);
        return recycled;
    }

    private void RecycleOutside(int firstIndex, int lastIndex)
    {
        if (_realized.Count == 0)
            return;

        List<int>? toRecycle = null;
        foreach (var index in _realized.Keys)
        {
            if (index < firstIndex || index > lastIndex)
                (toRecycle ??= new List<int>()).Add(index);
        }

        if (toRecycle is null)
            return;

        foreach (var index in toRecycle)
            RecycleAt(index);
    }

    private void RecycleAllRealized()
    {
        if (_realized.Count == 0)
            return;

        foreach (var index in _realized.Keys.ToList())
            RecycleAt(index);
    }

    private void RecycleAt(int index)
    {
        if (!_realized.Remove(index, out var container))
            return;

        var recycleKey = container.GetValue(RecycleKeyProperty);
        if (recycleKey is null)
        {
            RemoveInternalChild(container);
            return;
        }

        ItemContainerGenerator!.ClearItemContainer(container);
        container.IsVisible = false;
        if (!_recyclePool.TryGetValue(recycleKey, out var pool))
        {
            pool = new Stack<Control>();
            _recyclePool[recycleKey] = pool;
        }

        pool.Push(container);
    }

    // 集合变更会整体移动索引，全量回收比局部修补更安全。
    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        RecycleAllRealized();
        InvalidateMeasure();
    }

    protected override void OnItemsControlChanged(ItemsControl? oldValue)
    {
        foreach (var index in _realized.Keys.ToList())
        {
            if (_realized.Remove(index, out var container))
                RemoveInternalChild(container);
        }

        foreach (var pool in _recyclePool.Values)
        {
            while (pool.Count > 0)
                RemoveInternalChild(pool.Pop());
        }

        _recyclePool.Clear();
    }

    protected override Control? ContainerFromIndex(int index)
        => _realized.TryGetValue(index, out var container) ? container : null;

    protected override int IndexFromContainer(Control container)
    {
        foreach (var (index, candidate) in _realized)
        {
            if (ReferenceEquals(candidate, container))
                return index;
        }

        return -1;
    }

    protected override IEnumerable<Control>? GetRealizedContainers()
        => _realized.Values.ToList();

    protected override Control? ScrollIntoView(int index)
    {
        BringIndexIntoView(index);
        return _realized.TryGetValue(index, out var container) ? container : null;
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
        => null;

    // 行号由当前列数推导
    public void BringIndexIntoView(int index)
    {
        if (index < 0 || index >= Items.Count)
            return;

        var scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (scrollViewer is null)
            return;

        var row = index / Math.Max(1, _columns);
        var targetY = row * (ItemHeight + RowSpacing);
        var maxY = Math.Max(0, Bounds.Height - scrollViewer.Viewport.Height);
        targetY = Math.Clamp(targetY, 0, maxY);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetY);
    }
}
