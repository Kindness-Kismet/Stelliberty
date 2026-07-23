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
    private static readonly TimeSpan EnterDuration = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan HeaderEnterDuration = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan LeaveDuration = TimeSpan.FromMilliseconds(120);
    private static readonly Easing EnterEasing = new SplineEasing
    {
        X1 = 0.16,
        Y1 = 1,
        X2 = 0.3,
        Y2 = 1,
    };
    private static readonly Easing LeaveEasing = new SplineEasing
    {
        X1 = 0.4,
        Y1 = 0,
        X2 = 1,
        Y2 = 1,
    };

    // 小位移与轻微缩放只作用于合成属性，避免触发布局。
    public static readonly ITransform EnterFromTransform = TransformOperations.Parse("translate(0px,14px) scale(0.985)");
    public static readonly ITransform RestTransform = TransformOperations.Parse("translate(0px,0px) scale(1)");
    public static readonly ITransform LeaveToTransform = TransformOperations.Parse("translate(0px,-4px) scale(0.995)");
    public static readonly ITransform HeaderEnterFromTransform = TransformOperations.Parse("translate(0px,8px)");
    public static readonly ITransform HeaderRestTransform = TransformOperations.Parse("translate(0px,0px)");

    public static Transitions CreateEnterTransitions() => new()
    {
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = EnterDuration, Easing = EnterEasing },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = EnterDuration, Easing = EnterEasing },
    };

    public static Transitions CreateHeaderEnterTransitions() => new()
    {
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = HeaderEnterDuration, Easing = EnterEasing },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = HeaderEnterDuration, Easing = EnterEasing },
    };

    public static Transitions CreateLeaveTransitions() => new()
    {
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = LeaveDuration, Easing = LeaveEasing },
        new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = LeaveDuration, Easing = LeaveEasing },
    };
}
