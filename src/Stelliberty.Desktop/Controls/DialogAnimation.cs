using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Stelliberty.Presentation.Dialogs;

namespace Stelliberty.Desktop.Controls;

internal static class DialogAnimation
{
    public static readonly ITransform ClosedScale = TransformOperations.Parse("scale(0.96)");
    public static readonly ITransform OpenScale = TransformOperations.Parse("scale(1)");

    public static readonly Easing ScaleEasing = new CubicEaseOut();
    public static readonly Easing FadeEasing = new CubicEaseInOut();
}
