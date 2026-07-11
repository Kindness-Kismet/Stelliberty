using System;
using Avalonia;
using Avalonia.Controls;

namespace Stelliberty.Desktop.Controls;

// 非虚拟化等宽流式面板; 用在手风琴内容里, 避免嵌套虚拟化视口不稳定
public sealed class UniformWrapPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<UniformWrapPanel, double>(nameof(MinItemWidth), 200d);

    public static readonly StyledProperty<double> ItemHeightProperty =
        AvaloniaProperty.Register<UniformWrapPanel, double>(nameof(ItemHeight), 76d);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<UniformWrapPanel, double>(nameof(RowSpacing), 8d);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<UniformWrapPanel, double>(nameof(ColumnSpacing), 8d);

    static UniformWrapPanel()
    {
        AffectsMeasure<UniformWrapPanel>(MinItemWidthProperty, ItemHeightProperty, RowSpacingProperty, ColumnSpacingProperty);
        AffectsArrange<UniformWrapPanel>(MinItemWidthProperty, ItemHeightProperty, RowSpacingProperty, ColumnSpacingProperty);
    }

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public double RowSpacing { get => GetValue(RowSpacingProperty); set => SetValue(RowSpacingProperty, value); }
    public double ColumnSpacing { get => GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }

    // 列数算法与 VirtualizingWrapPanel 保持一致
    private (int Columns, double ItemWidth) ComputeLayout(double availableWidth)
    {
        var step = MinItemWidth + ColumnSpacing;
        var columns = step > 0 ? Math.Max(1, (int)Math.Floor((availableWidth + ColumnSpacing) / step)) : 1;
        var itemWidth = (availableWidth - (columns - 1) * ColumnSpacing) / columns;
        return (columns, Math.Max(0, itemWidth));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        if (count == 0)
        {
            return default;
        }

        var availableWidth = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
        var (columns, itemWidth) = ComputeLayout(availableWidth);
        var constraint = new Size(itemWidth, ItemHeight);
        foreach (var child in Children)
        {
            child.Measure(constraint);
        }

        var rows = (count + columns - 1) / columns;
        var height = rows > 0 ? rows * ItemHeight + (rows - 1) * RowSpacing : 0;
        return new Size(availableWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = ComputeLayout(finalSize.Width);
        var rowPitch = ItemHeight + RowSpacing;
        for (var index = 0; index < Children.Count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            var x = column * (itemWidth + ColumnSpacing);
            var y = row * rowPitch;
            Children[index].Arrange(new Rect(x, y, itemWidth, ItemHeight));
        }

        return finalSize;
    }
}
