using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Stelliberty.Desktop.Controls;

public sealed class GridReorderController
{
    // 长按加 6 px 移动才开始拖拽，避免卡片点击误触。
    private const double LongPressMilliseconds = 200;
    private const double DragThresholdSquared = 36;

    private readonly ItemsControl _list;
    private readonly Func<object?, string?> _getId;
    private readonly Func<Control, Control> _getDragControl;
    private readonly Action<string, int> _move;
    private readonly DispatcherTimer _longPressTimer;

    private readonly List<Slot> _slots = [];
    private string? _pressId;
    private Control? _pressControl;
    private Point _pressPoint;
    private int _sourceIndex = -1;
    private int _targetIndex = -1;
    private bool _canDrag;
    private bool _isDragging;

    private OverlayLayer? _overlay;
    private Image? _ghost;
    private RenderTargetBitmap? _ghostBitmap;
    private Point _ghostOrigin;
    private bool _isAttached;

    public GridReorderController(
        ItemsControl list,
        Func<object?, string?> getId,
        Func<Control, Control> getDragControl,
        Action<string, int> move)
    {
        _list = list;
        _getId = getId;
        _getDragControl = getDragControl;
        _move = move;
        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LongPressMilliseconds) };
        _longPressTimer.Tick += OnLongPressElapsed;
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        // Tunnel 处理器先于卡片按钮执行，让点击和长按拖拽并存。
        _list.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
        _list.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
        _list.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        _list.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Tunnel);
    }

    public void Detach()
    {
        _longPressTimer.Stop();
        if (_isAttached)
        {
            _list.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            _list.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
            _list.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
            _list.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
            _isAttached = false;
        }

        ResetVisuals();
        ClearState();
    }

    private void OnLongPressElapsed(object? sender, EventArgs args)
    {
        _longPressTimer.Stop();
        _canDrag = _pressId is not null;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(_list).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = args.GetPosition(_list);
        var (container, id, index) = HitContainer(point);
        if (id is null || container is null)
        {
            return;
        }

        var dragControl = _getDragControl(container);
        if (!Contains(dragControl, point))
        {
            return;
        }

        _pressId = id;
        _pressControl = dragControl;
        _pressPoint = point;
        _sourceIndex = index;
        _targetIndex = index;
        _canDrag = false;
        _isDragging = false;
        _longPressTimer.Start();
    }

    private void OnMoved(object? sender, PointerEventArgs args)
    {
        if (_pressId is null)
        {
            return;
        }

        var point = args.GetPosition(_list);
        var dx = point.X - _pressPoint.X;
        var dy = point.Y - _pressPoint.Y;
        var distanceSquared = dx * dx + dy * dy;
        if (!_canDrag)
        {
            if (distanceSquared >= DragThresholdSquared)
            {
                _longPressTimer.Stop();
                ClearState();
            }

            return;
        }

        if (!_isDragging)
        {
            if (distanceSquared < DragThresholdSquared)
            {
                return;
            }

            BeginDrag(args);
        }

        if (_ghost is not null)
        {
            Canvas.SetLeft(_ghost, _ghostOrigin.X + dx);
            Canvas.SetTop(_ghost, _ghostOrigin.Y + dy);
        }
        else if (_pressControl is not null)
        {
            _pressControl.RenderTransform = new TranslateTransform(dx, dy);
        }

        _targetIndex = ResolveTargetIndex(point);
        ApplyPreview(_targetIndex);
        args.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        _longPressTimer.Stop();
        var moved = _isDragging;
        if (_isDragging && _pressId is not null)
        {
            var id = _pressId;
            var target = _targetIndex;
            var source = _sourceIndex;
            ResetVisuals();
            args.Pointer.Capture(null);
            if (target >= 0 && target != source)
            {
                _move(id, target);
            }
        }
        else
        {
            ResetVisuals();
        }

        args.Handled = moved;
        ClearState();
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        ResetVisuals();
        ClearState();
    }

    private void BeginDrag(PointerEventArgs args)
    {
        _isDragging = true;
        SnapshotSlots();
        args.Pointer.Capture(_list);

        if (_pressControl is null)
        {
            return;
        }

        _overlay = OverlayLayer.GetOverlayLayer(_list);
        if (_overlay is not null
            && CreateGhost(_pressControl) is { } ghost
            && _pressControl.TranslatePoint(default, _overlay) is { } origin)
        {
            _ghost = ghost;
            _ghostOrigin = origin;
            Canvas.SetLeft(ghost, origin.X);
            Canvas.SetTop(ghost, origin.Y);
            _overlay.Children.Add(ghost);
            _pressControl.Opacity = 0d;
            return;
        }

        _pressControl.ZIndex = 1000;
        _pressControl.Opacity = 0.85;
    }

    private Image? CreateGhost(Control source)
    {
        var size = source.Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
        {
            return null;
        }

        _ghostBitmap = new RenderTargetBitmap(new PixelSize(
            (int)Math.Ceiling(size.Width),
            (int)Math.Ceiling(size.Height)));
        _ghostBitmap.Render(source);
        return new Image
        {
            Source = _ghostBitmap,
            Width = size.Width,
            Height = size.Height,
            Opacity = 0.92,
            IsHitTestVisible = false
        };
    }

    private void SnapshotSlots()
    {
        _slots.Clear();
        foreach (var container in _list.GetRealizedContainers())
        {
            if (_getId(container.DataContext) is not { } id)
            {
                continue;
            }

            if (container.TranslatePoint(default, _list) is not { } origin)
            {
                continue;
            }

            var dragControl = _getDragControl(container);
            if (dragControl.TranslatePoint(default, _list) is not { } dragOrigin)
            {
                continue;
            }

            _slots.Add(new Slot(
                dragControl,
                id,
                _list.IndexFromContainer(container),
                origin,
                dragOrigin,
                container.Bounds.Size));
        }

        _slots.Sort((left, right) => left.Index.CompareTo(right.Index));
    }

    // 间隙选择最近的中心点，避免跨列抖动。
    private int ResolveTargetIndex(Point point)
    {
        if (_slots.Count == 0)
        {
            return _sourceIndex;
        }

        var best = _sourceIndex;
        var bestDistance = double.MaxValue;
        foreach (var slot in _slots)
        {
            var centerX = slot.Origin.X + slot.Size.Width / 2;
            var centerY = slot.Origin.Y + slot.Size.Height / 2;
            var distance = (point.X - centerX) * (point.X - centerX) + (point.Y - centerY) * (point.Y - centerY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = slot.Index;
            }
        }

        return best;
    }

    private void ApplyPreview(int target)
    {
        foreach (var slot in _slots)
        {
            if (slot.Id == _pressId)
            {
                continue;
            }

            var finalIndex = ComputeFinalIndex(slot.Index, _sourceIndex, target);
            var destination = SlotDragOrigin(finalIndex);
            var dx = destination.X - slot.DragOrigin.X;
            var dy = destination.Y - slot.DragOrigin.Y;
            slot.DragControl.RenderTransform = Math.Abs(dx) < 0.1 && Math.Abs(dy) < 0.1
                ? null
                : new TranslateTransform(dx, dy);
        }
    }

    // 先移除再插入；目标索引必须按缩短后的列表修正。
    private static int ComputeFinalIndex(int i, int source, int target)
    {
        var positionAfterRemoval = i < source ? i : i - 1;
        return positionAfterRemoval < target ? positionAfterRemoval : positionAfterRemoval + 1;
    }

    private Point SlotDragOrigin(int index)
    {
        return _slots.FirstOrDefault(slot => slot.Index == index)?.DragOrigin ?? default;
    }

    private (Control? Container, string? Id, int Index) HitContainer(Point point)
    {
        foreach (var container in _list.GetRealizedContainers())
        {
            if (container.TranslatePoint(default, _list) is not { } origin)
            {
                continue;
            }

            if (new Rect(origin, container.Bounds.Size).Contains(point))
            {
                return (container, _getId(container.DataContext), _list.IndexFromContainer(container));
            }
        }

        return (null, null, -1);
    }

    private bool Contains(Control control, Point point)
    {
        return control.TranslatePoint(default, _list) is { } origin
            && new Rect(origin, control.Bounds.Size).Contains(point);
    }

    private void ResetVisuals()
    {
        foreach (var slot in _slots)
        {
            slot.DragControl.RenderTransform = null;
            slot.DragControl.Opacity = 1d;
            slot.DragControl.ZIndex = 0;
        }

        if (_pressControl is not null)
        {
            _pressControl.RenderTransform = null;
            _pressControl.Opacity = 1d;
            _pressControl.ZIndex = 0;
        }

        if (_ghost is not null)
        {
            _overlay?.Children.Remove(_ghost);
        }

        _ghost = null;
        _overlay = null;
        _ghostBitmap?.Dispose();
        _ghostBitmap = null;
    }

    private void ClearState()
    {
        _slots.Clear();
        _pressId = null;
        _pressControl = null;
        _sourceIndex = -1;
        _targetIndex = -1;
        _canDrag = false;
        _isDragging = false;
    }

    private sealed record Slot(
        Control DragControl,
        string Id,
        int Index,
        Point Origin,
        Point DragOrigin,
        Size Size);
}
