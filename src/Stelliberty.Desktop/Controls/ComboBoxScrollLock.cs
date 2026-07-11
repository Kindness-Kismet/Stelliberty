using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Stelliberty.Desktop.Controls;

// ComboBox 在 ScrollViewer 中可能请求 BringIntoView，把对话框顶到顶部。
// 这里只拦截 ComboBox 触发的滚动；弹出列表在 OverlayLayer 中。
public static class ComboBoxScrollLock
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, bool>("IsEnabled", typeof(ComboBoxScrollLock));

    public static void SetIsEnabled(ComboBox control, bool value) => control.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(ComboBox control) => control.GetValue(IsEnabledProperty);

    static ComboBoxScrollLock()
    {
        IsEnabledProperty.Changed.AddClassHandler<ComboBox>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(ComboBox control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            control.AddHandler(Control.RequestBringIntoViewEvent, OnRequestBringIntoView);
        }
        else
        {
            control.RemoveHandler(Control.RequestBringIntoViewEvent, OnRequestBringIntoView);
        }
    }

    private static void OnRequestBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        // 拦截 ComboBox 模板发出的滚动；OverlayLayer 弹出列表绕过此路径。
        e.Handled = true;
    }
}
