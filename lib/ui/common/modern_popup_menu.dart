import 'package:flutter/material.dart';

// 弹出菜单项数据模型
// 定义菜单项的基本属性：图标、文本、回调和危险标识
class PopupMenuItemData {
  const PopupMenuItemData({
    this.icon,
    required this.label,
    required this.onPressed,
    this.danger = false,
  });

  final String label;
  final VoidCallback? onPressed;
  final IconData? icon;
  final bool danger;
}

// 现代化弹出路由
// 自定义弹出路由，实现了：
// - 250ms 弹出动画（淡入 + 缩放 + 轻微滑动）
// - 智能定位（防止菜单超出屏幕边界）
// - Curves.easeOutBack 曲线提供回弹效果
class ModernPopupRoute<T> extends PopupRoute<T> {
  final WidgetBuilder builder;
  final ValueNotifier<Offset> offsetNotifier;

  ModernPopupRoute({
    required this.barrierLabel,
    required this.builder,
    required this.offsetNotifier,
  });

  @override
  String? barrierLabel;

  @override
  Color? get barrierColor => null;

  @override
  bool get barrierDismissible => true;

  @override
  Widget buildPage(
    BuildContext context,
    Animation<double> animation,
    Animation<double> secondaryAnimation,
  ) {
    return builder(context);
  }

  @override
  Widget buildTransitions(
    BuildContext context,
    Animation<double> animation,
    Animation<double> secondaryAnimation,
    Widget child,
  ) {
    const align = Alignment.topRight;
    final curveAnimation = animation
        .drive(Tween(begin: 0.0, end: 1.0))
        .drive(CurveTween(curve: Curves.easeOutBack));
    return SafeArea(
      child: ValueListenableBuilder(
        valueListenable: offsetNotifier,
        builder: (_, value, child) {
          return Align(
            alignment: align,
            child: CustomSingleChildLayout(
              delegate: OverflowAwareLayoutDelegate(
                offset: value.translate(48, -8),
              ),
              child: child,
            ),
          );
        },
        child: AnimatedBuilder(
          animation: animation,
          builder: (_, child) {
            return FadeTransition(
              opacity: curveAnimation,
              child: ScaleTransition(
                alignment: align,
                scale: curveAnimation,
                child: SlideTransition(
                  position: curveAnimation.drive(
                    Tween(begin: const Offset(0, -0.02), end: Offset.zero),
                  ),
                  child: child,
                ),
              ),
            );
          },
          child: builder(context),
        ),
      ),
    );
  }

  @override
  Duration get transitionDuration => const Duration(milliseconds: 250);
}

// 智能定位委托
// 防止菜单超出屏幕边界：
// - 自动调整水平位置避免右侧溢出
// - 自动调整垂直位置避免底部溢出
// - 保留 16px 安全边距
class OverflowAwareLayoutDelegate extends SingleChildLayoutDelegate {
  final Offset offset;

  OverflowAwareLayoutDelegate({required this.offset});

  @override
  Size getSize(BoxConstraints constraints) {
    return Size(constraints.maxWidth, constraints.maxHeight);
  }

  @override
  Offset getPositionForChild(Size size, Size childSize) {
    const safeOffset = Offset(16, 16);
    double x = (offset.dx - childSize.width).clamp(
      0,
      size.width - safeOffset.dx - childSize.width,
    );
    double y = (offset.dy).clamp(
      0,
      size.height - safeOffset.dy - childSize.height,
    );
    return Offset(x, y);
  }

  @override
  bool shouldRelayout(covariant OverflowAwareLayoutDelegate oldDelegate) {
    return oldDelegate.offset != offset;
  }
}

// 现代化弹出菜单容器
// 提供触发器和弹出内容的桥接：
// - targetBuilder: 构建触发按钮（注入 open 回调）
// - popup: 弹出的菜单内容
// - 自动跟踪触发器位置变化
typedef PopupOpen = Function({Offset offset});

class ModernPopupBox extends StatefulWidget {
  final Widget Function(PopupOpen open) targetBuilder;
  final Widget popup;

  const ModernPopupBox({
    super.key,
    required this.targetBuilder,
    required this.popup,
  });

  @override
  State<ModernPopupBox> createState() => _ModernPopupBoxState();
}

class _ModernPopupBoxState extends State<ModernPopupBox> {
  bool _isOpen = false;
  // 缓存 ValueNotifier 避免每次 build 重建
  late final ValueNotifier<Offset> _targetOffsetValueNotifier;
  Offset _offset = Offset.zero;

  @override
  void initState() {
    super.initState();
    _targetOffsetValueNotifier = ValueNotifier<Offset>(Offset.zero);
  }

  @override
  void dispose() {
    _targetOffsetValueNotifier.dispose();
    super.dispose();
  }

  // 打开弹出菜单
  void _open({Offset offset = Offset.zero}) {
    _offset = offset;
    _updateOffset();
    _isOpen = true;
    Navigator.of(context)
        .push(
          ModernPopupRoute(
            barrierLabel: 'popup_menu',
            builder: (BuildContext context) {
              return widget.popup;
            },
            offsetNotifier: _targetOffsetValueNotifier,
          ),
        )
        .then((_) {
          _isOpen = false;
        });
  }

  // 更新菜单位置（响应布局变化）
  void _updateOffset() {
    final renderBox = context.findRenderObject() as RenderBox?;
    if (renderBox == null) {
      return;
    }
    final viewPadding = MediaQuery.of(context).viewPadding;
    _targetOffsetValueNotifier.value = renderBox
        .localToGlobal(
          Offset.zero.translate(viewPadding.right, viewPadding.top),
        )
        .translate(_offset.dx, _offset.dy);
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (_, _) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (_isOpen) {
            _updateOffset();
          }
        });
        return widget.targetBuilder(_open);
      },
    );
  }
}

// 现代化弹出菜单 UI
// Windows 11 风格的弹出菜单：
// - 1px 边框 + 10px 圆角
// - 菜单项带 6px 圆角悬停效果
// - 危险操作显示红色悬停背景
// - 6px 水平内边距，避免贴边
// - 使用 InkRipple 实现快速响应
class ModernPopupMenu extends StatelessWidget {
  final List<PopupMenuItemData> items;
  final double minWidth;
  final double minItemVerticalPadding;
  final double fontSize;

  const ModernPopupMenu({
    super.key,
    required this.items,
    this.minWidth = 220,
    this.minItemVerticalPadding = 16,
    this.fontSize = 15,
  });

  // 构建单个菜单项
  // 特性：
  // - 禁用状态显示 30% 透明度
  // - 危险操作悬停显示 10% 红色背景
  // - 点击后先关闭菜单再执行回调
  Widget _popupMenuItem(
    BuildContext context, {
    required PopupMenuItemData item,
    required int index,
  }) {
    final onPressed = item.onPressed;
    final disabled = onPressed == null;
    final colorScheme = Theme.of(context).colorScheme;
    final color = item.danger ? colorScheme.error : colorScheme.onSurface;
    final foregroundColor = disabled ? color.withValues(alpha: 0.3) : color;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 3),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: onPressed != null
              ? () {
                  Navigator.of(context).pop();
                  onPressed();
                }
              : null,
          borderRadius: BorderRadius.circular(6),
          splashFactory: InkRipple.splashFactory,
          hoverColor: item.danger
              ? colorScheme.error.withValues(alpha: 0.1)
              : null,
          child: Container(
            constraints: BoxConstraints(minWidth: minWidth - 16),
            padding: EdgeInsets.only(
              left: 12,
              right: 12,
              top: minItemVerticalPadding - 4,
              bottom: minItemVerticalPadding - 4,
            ),
            decoration: BoxDecoration(borderRadius: BorderRadius.circular(6)),
            child: Row(
              mainAxisSize: MainAxisSize.max,
              children: [
                if (item.icon != null) ...[
                  Icon(item.icon, size: fontSize + 2, color: foregroundColor),
                  const SizedBox(width: 12),
                ],
                Flexible(
                  child: Text(
                    item.label,
                    style: TextStyle(
                      color: foregroundColor,
                      fontSize: fontSize - 1,
                      fontWeight: FontWeight.w400,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final colorScheme = Theme.of(context).colorScheme;

    return IntrinsicHeight(
      child: IntrinsicWidth(
        child: Container(
          decoration: BoxDecoration(
            color: colorScheme.surface,
            borderRadius: BorderRadius.circular(10),
            border: Border.all(
              color: colorScheme.outline.withValues(alpha: isDark ? 0.3 : 0.2),
              width: 1,
            ),
            boxShadow: [
              BoxShadow(
                color: Colors.black.withValues(alpha: isDark ? 0.4 : 0.15),
                blurRadius: 20,
                offset: const Offset(0, 8),
                spreadRadius: 0,
              ),
              BoxShadow(
                color: Colors.black.withValues(alpha: isDark ? 0.2 : 0.08),
                blurRadius: 8,
                offset: const Offset(0, 2),
                spreadRadius: 0,
              ),
            ],
          ),
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 3),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (final item in items.asMap().entries) ...[
                  _popupMenuItem(context, item: item.value, index: item.key),
                  if (item.value != items.last)
                    Padding(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 6,
                        vertical: 3,
                      ),
                      child: Divider(
                        height: 0,
                        thickness: 1,
                        color: colorScheme.outline.withValues(
                          alpha: isDark ? 0.15 : 0.1,
                        ),
                      ),
                    ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
