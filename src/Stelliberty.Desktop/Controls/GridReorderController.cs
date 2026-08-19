using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Stelliberty.Desktop.Controls;

public sealed class GridReorderController
{
    // 长按加 6 px 移动才开始拖拽，避免卡片点击误触。
    private const double LongPressMilliseconds = 200;
    private const double DragThresholdSquared = 36;
    // 拖到可视区边缘继续向外时自动滚；越靠边越快。
    private const double AutoScrollEdge = 56;
    private const double AutoScrollMaxPixels = 16;
    private const int AutoScrollIntervalMilliseconds = 16;

    private readonly ItemsControl _list;
    private readonly Func<object?, string?> _getId;
    private readonly Func<Control, Control> _getDragControl;
    private readonly Action<string, int> _move;
    private readonly DispatcherTimer _longPressTimer;
    private readonly DispatcherTimer _autoScrollTimer;

    private readonly List<Slot> _slots = [];
    private string? _pressId;
    private Control? _pressControl;
    private Point _pressPoint;
    private Point _lastListPoint;
    private int _sourceIndex = -1;
    private int _targetIndex = -1;
    private bool _canDrag;
    private bool _isDragging;

    private ScrollViewer? _scrollViewer;
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
        _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoScrollIntervalMilliseconds) };
        _autoScrollTimer.Tick += OnAutoScrollTick;
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
        StopAutoScroll();
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
        if (!_canDrag)
        {
            var dx = point.X - _pressPoint.X;
            var dy = point.Y - _pressPoint.Y;
            if (dx * dx + dy * dy >= DragThresholdSquared)
            {
                _longPressTimer.Stop();
                ClearState();
            }

            return;
        }

        if (!_isDragging)
        {
            var dx = point.X - _pressPoint.X;
            var dy = point.Y - _pressPoint.Y;
            if (dx * dx + dy * dy < DragThresholdSquared)
            {
                return;
            }

            BeginDrag(args);
        }

        ApplyDragVisual(point);
        UpdateAutoScroll(point);
        args.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        _longPressTimer.Stop();
        StopAutoScroll();
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
        StopAutoScroll();
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

        _pressControl.ZIndex = 1000;
        _pressControl.Opacity = 0.92;
    }

    private void ApplyDragVisual(Point point)
    {
        _lastListPoint = point;
        var dx = point.X - _pressPoint.X;
        var dy = point.Y - _pressPoint.Y;
        if (_pressControl is not null)
        {
            var translation = SnapToDevicePixels(_pressControl, dx, dy);
            _pressControl.RenderTransform = new TranslateTransform(translation.X, translation.Y);
        }

        _targetIndex = ResolveTargetIndex(point);
        ApplyPreview(_targetIndex);
    }

    private void UpdateAutoScroll(Point listPoint)
    {
        _lastListPoint = listPoint;
        _scrollViewer ??= _list.FindAncestorOfType<ScrollViewer>();
        if (_scrollViewer is null || ResolveAutoScrollDelta(_scrollViewer, listPoint) == default)
        {
            StopAutoScroll();
            return;
        }

        if (!_autoScrollTimer.IsEnabled)
        {
            _autoScrollTimer.Start();
        }
    }

    private void OnAutoScrollTick(object? sender, EventArgs args)
    {
        if (!_isDragging || _scrollViewer is null)
        {
            StopAutoScroll();
            return;
        }

        var delta = ResolveAutoScrollDelta(_scrollViewer, _lastListPoint);
        if (delta == default)
        {
            StopAutoScroll();
            return;
        }

        var next = new Vector(
            Math.Clamp(_scrollViewer.Offset.X + delta.X, 0, Math.Max(0, _scrollViewer.Extent.Width - _scrollViewer.Viewport.Width)),
            Math.Clamp(_scrollViewer.Offset.Y + delta.Y, 0, Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height)));
        if (next == _scrollViewer.Offset)
        {
            StopAutoScroll();
            return;
        }

        // 内容跟着滚，列表坐标整体平移；按压点和指针一起补，幽灵留在手指下。
        var scrolled = next - _scrollViewer.Offset;
        var shift = new Point(scrolled.X, scrolled.Y);
        _pressPoint += shift;
        _lastListPoint += shift;
        foreach (var slot in _slots)
        {
            if (slot.Id != _pressId)
            {
                slot.DragControl.RenderTransform = null;
            }
        }

        _scrollViewer.Offset = next;
        _scrollViewer.UpdateLayout();
        _list.UpdateLayout();
        SnapshotSlots();
        ApplyDragVisual(_lastListPoint);
    }

    // 指针落在滚动视口边缘带内才滚；越靠边越快。
    private Vector ResolveAutoScrollDelta(ScrollViewer scrollViewer, Point listPoint)
    {
        if (_list.TranslatePoint(listPoint, scrollViewer) is not { } local)
        {
            return default;
        }

        var viewport = scrollViewer.Viewport;
        var x = ComputeAxisDelta(local.X, viewport.Width, scrollViewer.Offset.X, scrollViewer.Extent.Width);
        var y = ComputeAxisDelta(local.Y, viewport.Height, scrollViewer.Offset.Y, scrollViewer.Extent.Height);
        return x == 0 && y == 0 ? default : new Vector(x, y);
    }

    private static double ComputeAxisDelta(double pointer, double viewport, double offset, double extent)
    {
        if (viewport <= 0 || extent <= viewport)
        {
            return 0;
        }

        if (pointer < AutoScrollEdge && offset > 0)
        {
            return -AutoScrollMaxPixels * (1 - Math.Clamp(pointer / AutoScrollEdge, 0, 1));
        }

        if (pointer > viewport - AutoScrollEdge && offset < extent - viewport)
        {
            var depth = Math.Clamp((pointer - (viewport - AutoScrollEdge)) / AutoScrollEdge, 0, 1);
            return AutoScrollMaxPixels * depth;
        }

        return 0;
    }

    private void StopAutoScroll()
    {
        _autoScrollTimer.Stop();
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
            var translation = SnapToDevicePixels(slot.DragControl, dx, dy);
            slot.DragControl.RenderTransform = Math.Abs(translation.X) < 0.1 && Math.Abs(translation.Y) < 0.1
                ? null
                : new TranslateTransform(translation.X, translation.Y);
        }
    }

    private static Vector SnapToDevicePixels(Control control, double x, double y)
    {
        var scale = TopLevel.GetTopLevel(control)?.RenderScaling ?? 1d;
        return new Vector(Math.Round(x * scale) / scale, Math.Round(y * scale) / scale);
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
        _scrollViewer = null;
    }

    private sealed record Slot(
        Control DragControl,
        string Id,
        int Index,
        Point Origin,
        Point DragOrigin,
        Size Size);
}
