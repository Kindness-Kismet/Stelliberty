using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Stelliberty.Desktop.Controls;

internal sealed class PagePointeroverSuppressor
{
    private const string SuppressClass = "suppress-pointerover";
    private static readonly TimeSpan InitialSuppressDuration = TimeSpan.FromMilliseconds(150);
    // 8px 内视为设备抖动或框架命中重算，不解除切页 hover 抑制。
    private const double ReleaseDistanceSquared = 64;

    private readonly Control _scope;
    private readonly HashSet<string> _targetClasses;
    private Point? _lastPointerPosition;
    private Point? _anchorPosition;
    private DateTime _suppressStartedAt;
    private bool _isActive;

    public PagePointeroverSuppressor(Control scope, params string[] targetClasses)
    {
        _scope = scope;
        _targetClasses = [.. targetClasses];
        _scope.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        _scope.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public void Begin()
    {
        _isActive = true;
        _anchorPosition = _lastPointerPosition;
        _suppressStartedAt = DateTime.UtcNow;
    }

    public void Apply()
    {
        if (_isActive)
        {
            SetSuppressed(true);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        _lastPointerPosition = args.GetPosition(_scope);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs args)
    {
        var position = args.GetPosition(_scope);
        if (!_isActive)
        {
            _lastPointerPosition = position;
            return;
        }

        if (IsInsideInitialWindow() || _anchorPosition is null || IsNearAnchor(position))
        {
            _anchorPosition ??= position;
            return;
        }

        _lastPointerPosition = position;
        _anchorPosition = null;
        _isActive = false;
        SetSuppressed(false);
    }

    private bool IsNearAnchor(Point position)
    {
        if (_anchorPosition is not { } anchor)
        {
            return false;
        }

        var deltaX = position.X - anchor.X;
        var deltaY = position.Y - anchor.Y;
        return (deltaX * deltaX) + (deltaY * deltaY) <= ReleaseDistanceSquared;
    }

    private bool IsInsideInitialWindow()
    {
        return DateTime.UtcNow - _suppressStartedAt <= InitialSuppressDuration;
    }

    private void SetSuppressed(bool isSuppressed)
    {
        foreach (var control in _scope.GetVisualDescendants().OfType<Control>())
        {
            if (HasTargetClass(control))
            {
                control.Classes.Set(SuppressClass, isSuppressed);
            }
        }
    }

    private bool HasTargetClass(Control control)
    {
        return _targetClasses.Count == 0 || _targetClasses.Any(control.Classes.Contains);
    }
}
