namespace Stelliberty.Desktop.Services;

internal static class WindowPlacement
{
    public static (double Width, double Height) FitSize(
        double requestedWidth,
        double requestedHeight,
        double minWidth,
        double minHeight,
        double maxWidth,
        double maxHeight)
    {
        maxWidth = PositiveOr(maxWidth, 1);
        maxHeight = PositiveOr(maxHeight, 1);
        var width = PositiveOr(requestedWidth, maxWidth);
        var height = PositiveOr(requestedHeight, maxHeight);
        // 先收进工作区，再守住最小尺寸；工作区更小时宁可溢出，也不压破布局下限。
        width = Math.Min(width, maxWidth);
        height = Math.Min(height, maxHeight);
        if (minWidth > 0)
        {
            width = Math.Max(width, minWidth);
        }

        if (minHeight > 0)
        {
            height = Math.Max(height, minHeight);
        }

        return (width, height);
    }

    public static (int X, int Y) CenterInPixels(
        double widthDip,
        double heightDip,
        int workingX,
        int workingY,
        int workingWidth,
        int workingHeight,
        double scale)
    {
        scale = scale > 0 ? scale : 1;
        var widthPx = Math.Max(1, (int)Math.Round(PositiveOr(widthDip, 1) * scale));
        var heightPx = Math.Max(1, (int)Math.Round(PositiveOr(heightDip, 1) * scale));
        var x = workingX + Math.Max(0, (workingWidth - widthPx) / 2);
        var y = workingY + Math.Max(0, (workingHeight - heightPx) / 2);
        return (x, y);
    }

    private static double PositiveOr(double value, double fallback)
        => double.IsFinite(value) && value > 0 ? value : fallback;
}
