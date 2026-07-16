using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace Stelliberty.Desktop.Controls;

internal static class DialogAnimation
{
    public static readonly ITransform ClosedTransform = TransformOperations.Parse("translate(0px,12px) scale(0.985)");
    public static readonly ITransform OpenTransform = TransformOperations.Parse("translate(0px,0px) scale(1)");
    public static readonly ITransform ExitTransform = TransformOperations.Parse("translate(0px,8px) scale(0.985)");

    public static readonly Easing EnterEasing = new SplineEasing
    {
        X1 = 0.16,
        Y1 = 1,
        X2 = 0.3,
        Y2 = 1,
    };
    public static readonly Easing ExitEasing = new SplineEasing
    {
        X1 = 0.4,
        Y1 = 0,
        X2 = 1,
        Y2 = 1,
    };
}
