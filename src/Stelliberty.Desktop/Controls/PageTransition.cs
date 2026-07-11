using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace Stelliberty.Desktop.Controls;

// 页面切换错峰；消除工具栏重影闪烁
internal static class PageTransition
{
    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(120);

    // 进入页起始下移量; 上浮到 0
    public static readonly ITransform EnterFromTransform = TransformOperations.Parse("translateY(8px)");
    public static readonly ITransform RestTransform = TransformOperations.Parse("translateY(0)");

    public static Transitions CreateEnterTransitions() => new()
    {
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = EnterDuration, Easing = new CubicEaseOut() },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = EnterDuration, Easing = new CubicEaseOut() },
    };

    public static Transitions CreateLeaveTransitions() => new()
    {
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = LeaveDuration, Easing = new CubicEaseOut() },
    };
}
