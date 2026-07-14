using System;
using Avalonia;
using Avalonia.Controls;

namespace Stelliberty.Desktop.Controls;

public sealed class EvenWrapPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<EvenWrapPanel, double>(nameof(MinItemWidth), 168d);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<EvenWrapPanel, double>(nameof(ItemHeight), 56d);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<EvenWrapPanel, double>(nameof(RowSpacing), 3d);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<EvenWrapPanel, double>(nameof(ColumnSpacing), 3d);

    public static readonly StyledProperty<double> EdgePaddingProperty =
        AvaloniaProperty.Register<EvenWrapPanel, double>(nameof(EdgePadding), 4d);

    static EvenWrapPanel()
    {
        AffectsMeasure<EvenWrapPanel>(MinItemWidthProperty, ItemHeightProperty,
            RowSpacingProperty, ColumnSpacingProperty, EdgePaddingProperty);
        AffectsArrange<EvenWrapPanel>(MinItemWidthProperty, ItemHeightProperty,
            RowSpacingProperty, ColumnSpacingProperty, EdgePaddingProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double EdgePadding
    {
        get => GetValue(EdgePaddingProperty);
        set => SetValue(EdgePaddingProperty, value);
    }

    private (int cols, double itemW) ComputeLayout(double availW)
    {
        var inner = Math.Max(0, availW - 2 * EdgePadding);
        var colSp = ColumnSpacing;
        var minW = MinItemWidth;
        var step = minW + colSp;
        var cols = step > 0
            ? Math.Max(1, (int)Math.Floor((inner + colSp) / step))
            : 1;
        var itemW = (inner - (cols - 1) * colSp) / cols;
        return (cols, Math.Max(0, itemW));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var itemH = ItemHeight;
        var rowGap = RowSpacing;

        var availableWidth = double.IsInfinity(availableSize.Width)
            ? MinItemWidth + 2 * EdgePadding
            : availableSize.Width;
        var (cols, itemW) = ComputeLayout(availableWidth);
        var childMeasure = new Size(itemW, itemH);

        foreach (var child in Children)
        {
            child.Measure(childMeasure);
        }

        var rows = (Children.Count + cols - 1) / cols;
        var totalHeight = rows * itemH + Math.Max(0, rows - 1) * rowGap;
        return new Size(availableWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemH = ItemHeight;
        var rowGap = RowSpacing;
        var edge = EdgePadding;
        var colSp = ColumnSpacing;

        var (cols, itemW) = ComputeLayout(finalSize.Width);
        var x = edge;
        var y = 0d;
        var nextY = 0d;

        for (var i = 0; i < Children.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var idealRight = edge + (col + 1) * itemW + col * colSp;
            var idealBottom = (row + 1) * itemH + row * rowGap;

            // 复用已取整边界，避免末列因独立取整越过面板边界。
            Children[i].Arrange(new Rect(new Point(x, y), new Point(idealRight, idealBottom)));
            x = Children[i].Bounds.Right + colSp;
            nextY = Math.Max(nextY, Children[i].Bounds.Bottom);

            if (col == cols - 1)
            {
                x = edge;
                y = nextY + rowGap;
                nextY = 0d;
            }
        }

        return finalSize;
    }
}
