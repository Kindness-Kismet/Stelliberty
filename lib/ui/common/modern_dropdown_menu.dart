import 'package:flutter/material.dart';

// 现代化下拉菜单路由
// 基于 ModernPopupRoute 的架构，实现 Q弹效果
class _ModernDropdownRoute<T> extends PopupRoute<T> {
  final WidgetBuilder builder;
  final ValueNotifier<Offset> offsetNotifier;
  final ValueNotifier<bool> showAboveNotifier;

  _ModernDropdownRoute({
    required this.barrierLabel,
    required this.builder,
    required this.offsetNotifier,
    required this.showAboveNotifier,
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
    final curveAnimation = animation
        .drive(Tween(begin: 0.0, end: 1.0))
        .drive(CurveTween(curve: Curves.easeOutBack));
    return SafeArea(
      child: ValueListenableBuilder(
        valueListenable: offsetNotifier,
        builder: (_, offset, child) {
          return ValueListenableBuilder(
            valueListenable: showAboveNotifier,
            builder: (_, showAbove, child) {
              final align = showAbove ? Alignment.bottomRight : Alignment.topRight;
              return Align(
                alignment: Alignment.topRight,
                child: CustomSingleChildLayout(
                  delegate: _DropdownLayoutDelegate(
                    offset: offset,
                    showAbove: showAbove,
                  ),
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
                              Tween(
                                begin: Offset(0, showAbove ? 0.02 : -0.02),
                                end: Offset.zero,
                              ),
                            ),
                            child: child,
                          ),
                        ),
                      );
                    },
                    child: child,
                  ),
                ),
              );
            },
            child: child,
          );
        },
        child: builder(context),
      ),
    );
  }

  @override
  Duration get transitionDuration => const Duration(milliseconds: 250);
}

// 智能定位委托（适配下拉菜单）
class _DropdownLayoutDelegate extends SingleChildLayoutDelegate {
  final Offset offset;
  final bool showAbove;

  _DropdownLayoutDelegate({
    required this.offset,
    required this.showAbove,
  });

  @override
  Size getSize(BoxConstraints constraints) {
    return Size(constraints.maxWidth, constraints.maxHeight);
  }

  @override
  Offset getPositionForChild(Size size, Size childSize) {
    // X 坐标：offset.dx 是按钮右边缘，菜单右对齐
    double x = offset.dx - childSize.width;
    
    // Y 坐标：
    // showAbove 为 true 时，offset.dy 是按钮底部，需要向上偏移菜单高度
    // showAbove 为 false 时，offset.dy 是按钮顶部，菜单从这里开始向下
    double y = showAbove ? offset.dy - childSize.height : offset.dy;

    // 确保不超出屏幕边界
    const safeMargin = 16.0;
    if (x < safeMargin) {
      x = safeMargin;
    }
    if (x + childSize.width > size.width - safeMargin) {
      x = size.width - childSize.width - safeMargin;
    }
    if (y < safeMargin) {
      y = safeMargin;
    }
    if (y + childSize.height > size.height - safeMargin) {
      y = size.height - childSize.height - safeMargin;
    }

    return Offset(x, y);
  }

  @override
  bool shouldRelayout(covariant _DropdownLayoutDelegate oldDelegate) {
    return oldDelegate.offset != offset || oldDelegate.showAbove != showAbove;
  }
}

// Q弹风格的下拉菜单组件
//
// 特性：
// - Curves.easeOutBack 回弹动画（参考 modern_popup_menu）
// - Windows 11 风格外观
// - 适配下拉菜单的间距和尺寸
// - 支持滚动的长列表
class ModernDropdownMenu<T> extends StatefulWidget {
  // 触发菜单的子组件
  final Widget child;

  // 菜单项的数据列表
  final List<T> items;

  // 当前选中的数据项
  final T selectedItem;

  // 将数据项转换为显示字符串的函数
  final String Function(T item) itemToString;

  // 选中菜单项时的回调
  final ValueChanged<T> onSelected;

  const ModernDropdownMenu({
    super.key,
    required this.child,
    required this.items,
    required this.selectedItem,
    required this.itemToString,
    required this.onSelected,
  });

  @override
  State<ModernDropdownMenu<T>> createState() => _ModernDropdownMenuState<T>();
}

class _ModernDropdownMenuState<T> extends State<ModernDropdownMenu<T>> {
  bool _isOpen = false;
  late final ValueNotifier<Offset> _targetOffsetValueNotifier;
  late final ValueNotifier<bool> _showAboveNotifier;

  @override
  void initState() {
    super.initState();
    _targetOffsetValueNotifier = ValueNotifier<Offset>(Offset.zero);
    _showAboveNotifier = ValueNotifier<bool>(false);
  }

  @override
  void dispose() {
    _targetOffsetValueNotifier.dispose();
    _showAboveNotifier.dispose();
    super.dispose();
  }

  // 打开下拉菜单
  void _open() {
    _updateOffset();
    _isOpen = true;
    Navigator.of(context)
        .push(
          _ModernDropdownRoute(
            barrierLabel: 'dropdown_menu',
            builder: (BuildContext context) {
              return _DropdownMenuUI<T>(
                items: widget.items,
                selectedItem: widget.selectedItem,
                itemToString: widget.itemToString,
                onSelected: widget.onSelected,
              );
            },
            offsetNotifier: _targetOffsetValueNotifier,
            showAboveNotifier: _showAboveNotifier,
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
    
    final buttonSize = renderBox.size;
    final buttonPosition = renderBox.localToGlobal(Offset.zero);
    final screenSize = MediaQuery.of(context).size;
    
    // 估算菜单高度
    const itemHeight = 36.0;
    const maxVisibleItems = 6;
    const padding = 8.0;
    final estimatedMenuHeight = (widget.items.length > maxVisibleItems
            ? maxVisibleItems * itemHeight
            : widget.items.length * itemHeight) +
        padding;
    
    // 判断是显示在按钮上方还是下方（覆盖按钮）
    final spaceBelow = screenSize.height - buttonPosition.dy;
    final spaceAbove = buttonPosition.dy + buttonSize.height;
    final showAbove = spaceBelow < estimatedMenuHeight && spaceAbove > spaceBelow;
    
    _showAboveNotifier.value = showAbove;
    
    // 设置菜单位置（覆盖按钮，右对齐）
    // X 坐标：按钮右边缘（菜单会右对齐到这里）
    // Y 坐标：
    //   - 下方显示：从按钮顶部开始
    //   - 上方显示：菜单底部对齐按钮底部
    _targetOffsetValueNotifier.value = Offset(
      buttonPosition.dx + buttonSize.width,  // 按钮右边缘
      showAbove
        ? buttonPosition.dy + buttonSize.height  // 上方显示时，这是菜单的底部位置
        : buttonPosition.dy,  // 下方显示时，这是菜单的顶部位置
    );
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
        return GestureDetector(
          onTap: _open,
          child: widget.child,
        );
      },
    );
  }
}

// 下拉菜单 UI 组件
// 适配下拉菜单的间距和尺寸，保持 Windows 11 风格
class _DropdownMenuUI<T> extends StatefulWidget {
  final List<T> items;
  final T selectedItem;
  final String Function(T item) itemToString;
  final ValueChanged<T> onSelected;

  const _DropdownMenuUI({
    required this.items,
    required this.selectedItem,
    required this.itemToString,
    required this.onSelected,
  });

  @override
  State<_DropdownMenuUI<T>> createState() => _DropdownMenuUIState<T>();
}

class _DropdownMenuUIState<T> extends State<_DropdownMenuUI<T>> {
  int _currentPage = 0;

  @override
  void initState() {
    super.initState();
    _currentPage = 0;
  }

  // 每页显示的项目数（最多7项 + 1行翻页按钮）
  static const int _itemsPerPage = 7;

  // 计算总页数
  int get _totalPages => (widget.items.length / _itemsPerPage).ceil();

  // 获取当前页的项目
  List<T> get _currentPageItems {
    final startIndex = _currentPage * _itemsPerPage;
    final endIndex = (startIndex + _itemsPerPage).clamp(0, widget.items.length);
    return widget.items.sublist(startIndex, endIndex);
  }

  // 上一页
  void _previousPage() {
    if (_currentPage > 0) {
      setState(() {
        _currentPage--;
      });
    }
  }

  // 下一页
  void _nextPage() {
    if (_currentPage < _totalPages - 1) {
      setState(() {
        _currentPage++;
      });
    }
  }

  // 构建单个菜单项（Win11 风格，左侧指示条）
  Widget _dropdownMenuItem(
    BuildContext context, {
    required T item,
  }) {
    final isSelected = item == widget.selectedItem;
    final colorScheme = Theme.of(context).colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: () {
            Navigator.of(context).pop();
            widget.onSelected(item);
          },
          borderRadius: BorderRadius.circular(6),
          splashFactory: InkRipple.splashFactory,
          hoverColor: colorScheme.onSurface.withValues(alpha: 0.05),
          child: Container(
            constraints: const BoxConstraints(minWidth: 120),
            padding: const EdgeInsets.only(left: 6, right: 10, top: 10, bottom: 10),
            decoration: BoxDecoration(
              color: isSelected
                  ? colorScheme.onSurface.withValues(alpha: 0.08)
                  : Colors.transparent,
              borderRadius: BorderRadius.circular(6),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.center,
              children: [
                // Win11 风格左侧选中指示条（加粗版）
                Center(
                  child: AnimatedContainer(
                    duration: const Duration(milliseconds: 200),
                    width: 4,
                    height: 20,
                    decoration: BoxDecoration(
                      color: isSelected
                          ? colorScheme.primary
                          : Colors.transparent,
                      borderRadius: BorderRadius.circular(2),
                    ),
                  ),
                ),
                const SizedBox(width: 6),
                // 文本（单行显示，超出显示省略号）
                Expanded(
                  child: Text(
                    widget.itemToString(item),
                    style: TextStyle(
                      color: isSelected
                          ? colorScheme.primary
                          : colorScheme.onSurface,
                      fontSize: 14,
                      fontWeight: isSelected ? FontWeight.w600 : FontWeight.w400,
                      height: 1.2,
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  // 构建翻页按钮（圆形图标按钮，显示在右下角）
  Widget _buildPaginationButtons(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;
    
    return Padding(
      padding: const EdgeInsets.only(right: 8, bottom: 4, top: 2),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.end,
        children: [
          // 上一页按钮
          Material(
            color: Colors.transparent,
            child: InkWell(
              onTap: _currentPage > 0 ? _previousPage : null,
              borderRadius: BorderRadius.circular(16),
              splashFactory: InkRipple.splashFactory,
              child: Container(
                width: 28,
                height: 28,
                decoration: BoxDecoration(
                  color: _currentPage > 0
                      ? colorScheme.onSurface.withValues(alpha: isDark ? 0.12 : 0.08)
                      : colorScheme.onSurface.withValues(alpha: isDark ? 0.06 : 0.04),
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  Icons.keyboard_arrow_up,
                  size: 18,
                  color: _currentPage > 0
                      ? colorScheme.onSurface
                      : colorScheme.onSurface.withValues(alpha: 0.3),
                ),
              ),
            ),
          ),
          const SizedBox(width: 8),
          // 下一页按钮
          Material(
            color: Colors.transparent,
            child: InkWell(
              onTap: _currentPage < _totalPages - 1 ? _nextPage : null,
              borderRadius: BorderRadius.circular(16),
              splashFactory: InkRipple.splashFactory,
              child: Container(
                width: 28,
                height: 28,
                decoration: BoxDecoration(
                  color: _currentPage < _totalPages - 1
                      ? colorScheme.onSurface.withValues(alpha: isDark ? 0.12 : 0.08)
                      : colorScheme.onSurface.withValues(alpha: isDark ? 0.06 : 0.04),
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  Icons.keyboard_arrow_down,
                  size: 18,
                  color: _currentPage < _totalPages - 1
                      ? colorScheme.onSurface
                      : colorScheme.onSurface.withValues(alpha: 0.3),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final colorScheme = Theme.of(context).colorScheme;

    // 是否需要分页
    final needsPagination = widget.items.length > _itemsPerPage;

    return IntrinsicHeight(
      child: IntrinsicWidth(
        child: Container(
          constraints: const BoxConstraints(
            minWidth: 200,
            maxWidth: 400,
          ),
          decoration: BoxDecoration(
            color: colorScheme.surface,
            borderRadius: BorderRadius.circular(10),
            border: Border.all(
              color: colorScheme.outline.withValues(alpha: isDark ? 0.2 : 0.15),
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
            padding: const EdgeInsets.symmetric(vertical: 4),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // 显示当前页的项目
                ..._currentPageItems.map((item) {
                  return _dropdownMenuItem(context, item: item);
                }),
                // 如果需要分页，显示翻页按钮
                if (needsPagination) _buildPaginationButtons(context),
              ],
            ),
          ),
        ),
      ),
    );
  }
}