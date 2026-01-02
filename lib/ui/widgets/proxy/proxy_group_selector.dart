import 'package:flutter/material.dart';
import 'package:flutter/gestures.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 代理组选择器组件
class ProxyGroupSelector extends StatefulWidget {
  final ClashProvider clashProvider;
  final int currentGroupIndex;
  final ScrollController scrollController;
  final Function(int) onGroupChanged;
  final double mouseScrollSpeedMultiplier;
  final double tabScrollDistance;

  const ProxyGroupSelector({
    super.key,
    required this.clashProvider,
    required this.currentGroupIndex,
    required this.scrollController,
    required this.onGroupChanged,
    this.mouseScrollSpeedMultiplier = 2.0,
    this.tabScrollDistance = 300.0,
  });

  @override
  State<ProxyGroupSelector> createState() => _ProxyGroupSelectorState();
}

class _ProxyGroupSelectorState extends State<ProxyGroupSelector> {
  // 样式常量

  // 滚动判断阈值
  static const double _scrollThreshold = 0.5;

  // 动画时长
  static const Duration _buttonScrollDuration = Duration(milliseconds: 200);
  static const Duration _mouseScrollDuration = Duration(milliseconds: 100);
  static const Duration _underlineAnimationDuration = Duration(
    milliseconds: 200,
  );

  // 外层布局
  static const double _outerPaddingTop = 12.0;
  static const double _outerPaddingBottom = 12.0;
  static const double _outerPaddingRight = 20.0; // 向右滚动按钮距右边缘
  static const double _outerScrollButtonMargin = 10.0; // 末尾代理组距滚动按钮

  // 代理组间距
  static const double _startGroupSpacing = 20.0; // 左侧起始间距
  static const double _groupSpacing = 20.0; // 代理组之间间距
  static const double _endGroupSpacing = 0.0; // 末尾代理组距右间距

  // 标签样式
  static const double _tabHorizontalPadding = 8.0;
  static const double _tabVerticalPadding = 2.0;
  static const double _tabTextHorizontalPadding = 4.0;
  static const double _tabTextVerticalPadding = 4.0;
  static const double _tabBorderRadius = 8.0;
  static const double _tabFontSize = 14.0;

  // 透明度
  static const double _hoverAlphaLight = 0.5;
  static const double _hoverAlphaDark = 0.3;
  static const double _unselectedTextAlpha = 0.7;

  // 下划线样式
  static const double _underlineHeight = 2.0;
  static const double _underlineWidth = 40.0;
  static const double _underlineBorderRadius = 1.0;

  // 滚动按钮样式
  static const double _scrollButtonSize = 20.0; // 图标尺寸
  static const double _scrollButtonConstraint = 40.0; // 按钮约束尺寸
  static const double _scrollButtonBorderRadius = 20.0; // 圆角半径
  static const double _scrollButtonBackgroundAlpha = 0.3; // 常驻背景透明度
  static const double _scrollButtonHoverAlpha = 0.5; // 悬停背景透明度
  static const double _scrollButtonGap = 8.0; // 两个滚动按钮之间间距

  int? _hoveredIndex;
  bool _needsScrolling = false;
  bool _canScrollLeft = false;
  bool _canScrollRight = false;

  @override
  void initState() {
    super.initState();
    // 监听滚动位置变化（普通滚动）
    widget.scrollController.addListener(_updateButtonStates);
    // 延迟初始化按钮状态
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _updateButtonStates();
    });
  }

  @override
  void dispose() {
    widget.scrollController.removeListener(_updateButtonStates);
    super.dispose();
  }

  void _updateButtonStates() {
    if (!mounted || !widget.scrollController.hasClients) return;

    final position = widget.scrollController.position;
    if (!position.hasContentDimensions) return;

    final needsScrolling = position.maxScrollExtent > _scrollThreshold;
    final canScrollLeft = needsScrolling && position.pixels > _scrollThreshold;
    final canScrollRight =
        needsScrolling &&
        position.pixels < position.maxScrollExtent - _scrollThreshold;

    // 只有状态真正改变时才调用 setState
    if (_needsScrolling != needsScrolling ||
        _canScrollLeft != canScrollLeft ||
        _canScrollRight != canScrollRight) {
      setState(() {
        _needsScrolling = needsScrolling;
        _canScrollLeft = canScrollLeft;
        _canScrollRight = canScrollRight;
      });
    }
  }

  void _scrollByDistance(double distance) {
    if (!widget.scrollController.hasClients) return;

    final offset = widget.scrollController.offset + distance;
    widget.scrollController.animateTo(
      offset.clamp(0.0, widget.scrollController.position.maxScrollExtent),
      duration: _buttonScrollDuration,
      curve: Curves.easeOut,
    );
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(
        top: _outerPaddingTop,
        bottom: _outerPaddingBottom,
        right: _outerPaddingRight,
      ),
      child: Row(
        children: [
          Expanded(
            child: Listener(
              onPointerSignal: (pointerSignal) {
                if (pointerSignal is PointerScrollEvent &&
                    widget.scrollController.hasClients) {
                  final offset =
                      widget.scrollController.offset +
                      pointerSignal.scrollDelta.dy *
                          widget.mouseScrollSpeedMultiplier;
                  widget.scrollController.animateTo(
                    offset.clamp(
                      0.0,
                      widget.scrollController.position.maxScrollExtent,
                    ),
                    duration: _mouseScrollDuration,
                    curve: Curves.easeOut,
                  );
                }
              },
              child: NotificationListener<ScrollMetricsNotification>(
                onNotification: (notification) {
                  // 监听滚动指标改变（包括滚动和窗口大小改变）
                  _updateButtonStates();
                  return false;
                },
                child: SingleChildScrollView(
                  controller: widget.scrollController,
                  scrollDirection: Axis.horizontal,
                  physics: const BouncingScrollPhysics(),
                  child: Row(
                    children: [
                      // 左侧起始间距
                      const SizedBox(width: _startGroupSpacing),
                      ...List.generate(widget.clashProvider.proxyGroups.length, (
                        index,
                      ) {
                        final group = widget.clashProvider.proxyGroups[index];
                        final isSelected = index == widget.currentGroupIndex;
                        final isHovered = _hoveredIndex == index;
                        final isLastGroup =
                            index ==
                            widget.clashProvider.proxyGroups.length - 1;

                        return Padding(
                          padding: EdgeInsets.only(
                            right: isLastGroup
                                ? _endGroupSpacing
                                : _groupSpacing,
                          ),
                          child: MouseRegion(
                            onEnter: (_) =>
                                setState(() => _hoveredIndex = index),
                            onExit: (_) => setState(() => _hoveredIndex = null),
                            cursor: SystemMouseCursors.click,
                            child: GestureDetector(
                              onTap: () => widget.onGroupChanged(index),
                              child: Container(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: _tabHorizontalPadding,
                                  vertical: _tabVerticalPadding,
                                ),
                                decoration: BoxDecoration(
                                  color: isHovered && !isSelected
                                      ? Theme.of(context)
                                            .colorScheme
                                            .surfaceContainerHighest
                                            .withValues(
                                              alpha:
                                                  Theme.of(
                                                        context,
                                                      ).brightness ==
                                                      Brightness.light
                                                  ? _hoverAlphaLight
                                                  : _hoverAlphaDark,
                                            )
                                      : Colors.transparent,
                                  borderRadius: BorderRadius.circular(
                                    _tabBorderRadius,
                                  ),
                                ),
                                child: Column(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    // 代理组名称
                                    Padding(
                                      padding: const EdgeInsets.symmetric(
                                        horizontal: _tabTextHorizontalPadding,
                                        vertical: _tabTextVerticalPadding,
                                      ),
                                      child: Text(
                                        group.name,
                                        style: TextStyle(
                                          color: isSelected
                                              ? Theme.of(
                                                  context,
                                                ).colorScheme.primary
                                              : Theme.of(context)
                                                    .colorScheme
                                                    .onSurface
                                                    .withValues(
                                                      alpha:
                                                          _unselectedTextAlpha,
                                                    ),
                                          fontWeight: isSelected
                                              ? FontWeight.w600
                                              : FontWeight.normal,
                                          fontSize: _tabFontSize,
                                        ),
                                        overflow: TextOverflow.ellipsis,
                                        maxLines: 1,
                                      ),
                                    ),
                                    // 底部下划线
                                    AnimatedContainer(
                                      duration: _underlineAnimationDuration,
                                      height: _underlineHeight,
                                      width: isSelected ? _underlineWidth : 0,
                                      decoration: BoxDecoration(
                                        color: Theme.of(
                                          context,
                                        ).colorScheme.primary,
                                        borderRadius: BorderRadius.circular(
                                          _underlineBorderRadius,
                                        ),
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ),
                        );
                      }),
                    ],
                  ),
                ),
              ),
            ),
          ),
          const SizedBox(width: _outerScrollButtonMargin),
          _buildScrollButtons(context),
        ],
      ),
    );
  }

  Widget _buildScrollButtons(BuildContext context) {
    final trans = context.translate;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        ModernTooltip(
          message: trans.proxy.scroll_left,
          child: IconButton(
            constraints: const BoxConstraints(
              minWidth: _scrollButtonConstraint,
              minHeight: _scrollButtonConstraint,
              maxWidth: _scrollButtonConstraint,
              maxHeight: _scrollButtonConstraint,
            ),
            padding: EdgeInsets.zero,
            style: ButtonStyle(
              backgroundColor: WidgetStateProperty.resolveWith((states) {
                if (states.contains(WidgetState.disabled)) {
                  return Colors.transparent;
                }
                if (states.contains(WidgetState.hovered)) {
                  return Theme.of(context).colorScheme.surfaceContainerHighest
                      .withValues(alpha: _scrollButtonHoverAlpha);
                }
                return Theme.of(context).colorScheme.surfaceContainerHighest
                    .withValues(alpha: _scrollButtonBackgroundAlpha);
              }),
              shape: WidgetStateProperty.all(
                RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(
                    _scrollButtonBorderRadius,
                  ),
                ),
              ),
            ),
            onPressed: _canScrollLeft
                ? () => _scrollByDistance(-widget.tabScrollDistance)
                : null,
            icon: const Icon(Icons.chevron_left),
            iconSize: _scrollButtonSize,
          ),
        ),
        const SizedBox(width: _scrollButtonGap),
        ModernTooltip(
          message: trans.proxy.scroll_right,
          child: IconButton(
            constraints: const BoxConstraints(
              minWidth: _scrollButtonConstraint,
              minHeight: _scrollButtonConstraint,
              maxWidth: _scrollButtonConstraint,
              maxHeight: _scrollButtonConstraint,
            ),
            padding: EdgeInsets.zero,
            style: ButtonStyle(
              backgroundColor: WidgetStateProperty.resolveWith((states) {
                if (states.contains(WidgetState.disabled)) {
                  return Colors.transparent;
                }
                if (states.contains(WidgetState.hovered)) {
                  return Theme.of(context).colorScheme.surfaceContainerHighest
                      .withValues(alpha: _scrollButtonHoverAlpha);
                }
                return Theme.of(context).colorScheme.surfaceContainerHighest
                    .withValues(alpha: _scrollButtonBackgroundAlpha);
              }),
              shape: WidgetStateProperty.all(
                RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(
                    _scrollButtonBorderRadius,
                  ),
                ),
              ),
            ),
            onPressed: _canScrollRight
                ? () => _scrollByDistance(widget.tabScrollDistance)
                : null,
            icon: const Icon(Icons.chevron_right),
            iconSize: _scrollButtonSize,
          ),
        ),
      ],
    );
  }
}
