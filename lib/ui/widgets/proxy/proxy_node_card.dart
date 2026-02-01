import 'package:flutter/material.dart';
import 'package:stelliberty/atomic/platform_helper.dart';
import 'package:stelliberty/clash/model/clash_model.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 代理节点卡片：以磨砂风格展示节点信息。
// 支持选中态与延迟测试入口。
class ProxyNodeCard extends StatefulWidget {
  final ProxyNode node;
  final bool isSelected;
  final VoidCallback onTap;
  final Future<void> Function()? onTestDelay;
  final bool isClashRunning;
  final bool isWaitingTest;

  const ProxyNodeCard({
    super.key,
    required this.node,
    required this.isSelected,
    required this.onTap,
    this.onTestDelay,
    required this.isClashRunning,
    this.isWaitingTest = false,
  });

  @override
  State<ProxyNodeCard> createState() => _ProxyNodeCardState();
}

class _ProxyNodeCardState extends State<ProxyNodeCard> {
  bool _isSingleTesting = false; // 是否正在单独测试延迟（区别于批量测试）

  Future<void> _testDelay() async {
    // 如果正在批量测试或单独测试，不允许再次点击
    if (_isSingleTesting || widget.isWaitingTest) return;

    if (!mounted) return;
    setState(() {
      _isSingleTesting = true;
    });

    // 调用测试延迟回调
    if (widget.onTestDelay != null) {
      await widget.onTestDelay!();
    }

    if (!mounted) return;
    setState(() {
      _isSingleTesting = false;
    });
  }

  // 判断是否已经测试过延迟（delay != null 就表示测试过）
  bool get _hasTested => widget.node.delay != null;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;
    final isDark = theme.brightness == Brightness.dark;
    final isMobile = PlatformHelper.isMobile;

    // 移动端使用更紧凑的尺寸
    final horizontalPadding = isMobile ? 10.0 : 16.0;
    final verticalPadding = isMobile ? 10.0 : 14.0;
    final titleFontSize = isMobile ? 12.0 : 14.0;
    final typeFontSize = isMobile ? 9.0 : 10.0;
    final delayFontSize = isMobile ? 11.0 : 12.0;
    final iconSize = isMobile ? 16.0 : 20.0;
    final delayAreaWidth = isMobile ? 60.0 : 85.0;
    final borderRadius = isMobile ? 12.0 : 16.0;

    // 混色：亮色主题混入 10% 白色，暗色主题混入 10% 黑色
    final mixColor = isDark
        ? const Color.fromARGB(255, 42, 42, 42)
        : Colors.white;
    const mixOpacity = 0.05;

    // 预计算背景色,避免每次 build 重复计算
    final backgroundColor = Color.alphaBlend(
      mixColor.withValues(alpha: mixOpacity),
      colorScheme.surface.withValues(alpha: isDark ? 0.7 : 0.85),
    );

    return ModernTooltip(
      message: widget.node.name,
      child: Container(
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(borderRadius),
          color: backgroundColor,
          border: Border.all(
            color: widget.isSelected
                ? colorScheme.primary.withValues(alpha: isDark ? 0.7 : 0.6)
                : colorScheme.outline.withValues(alpha: 0.4),
            width: widget.isSelected ? 2 : 1,
          ),
          boxShadow: [
            BoxShadow(
              color: Colors.black.withValues(alpha: isDark ? 0.3 : 0.1),
              blurRadius: widget.isSelected ? 8 : 4,
              offset: Offset(0, widget.isSelected ? 2 : 1),
            ),
          ],
        ),
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: widget.onTap,
          child: Padding(
            padding: EdgeInsets.symmetric(
              horizontal: horizontalPadding,
              vertical: verticalPadding,
            ),
            child: Row(
              children: [
                // 标题和类型
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      Text(
                        widget.node.name,
                        style: TextStyle(
                          fontWeight: widget.isSelected
                              ? FontWeight.w600
                              : FontWeight.w500,
                          fontSize: titleFontSize,
                          color: colorScheme.onSurface.withValues(
                            alpha: isDark ? 0.95 : 0.9,
                          ),
                        ),
                        overflow: TextOverflow.ellipsis,
                        maxLines: 1,
                      ),
                      SizedBox(height: isMobile ? 4 : 6),
                      Transform.translate(
                        offset: const Offset(-3, 0),
                        child: Container(
                          padding: EdgeInsets.symmetric(
                            horizontal: isMobile ? 4 : 6,
                            vertical: isMobile ? 2 : 4,
                          ),
                          decoration: BoxDecoration(
                            color: colorScheme.surfaceContainerHighest
                                .withValues(alpha: 0.4),
                            borderRadius: BorderRadius.circular(
                              isMobile ? 4 : 6,
                            ),
                          ),
                          child: Text(
                            widget.node.type,
                            style: TextStyle(
                              fontSize: typeFontSize,
                              fontWeight: FontWeight.w600,
                              letterSpacing: 0.5,
                              color: colorScheme.onSurface.withValues(
                                alpha: 0.6,
                              ),
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
                SizedBox(width: isMobile ? 4 : 8),

                // 延迟显示区域
                SizedBox(
                  width: delayAreaWidth,
                  child: widget.isClashRunning
                      ? _buildDelaySection(
                          context,
                          colorScheme,
                          isDark,
                          delayFontSize,
                          iconSize,
                        )
                      : const SizedBox.shrink(),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  // 构建延迟显示区域
  Widget _buildDelaySection(
    BuildContext context,
    ColorScheme colorScheme,
    bool isDark,
    double fontSize,
    double iconSize,
  ) {
    if (_isSingleTesting || widget.isWaitingTest) {
      return _buildLoadingIndicator(colorScheme, iconSize);
    } else if (_hasTested) {
      return _buildDelayBadge(colorScheme, isDark, fontSize);
    } else {
      return _buildTestIcon(colorScheme, iconSize);
    }
  }

  // 加载指示器
  Widget _buildLoadingIndicator(ColorScheme colorScheme, double size) {
    final indicatorSize = size * 0.9;
    return Align(
      alignment: Alignment.centerRight,
      child: SizedBox(
        width: indicatorSize,
        height: indicatorSize,
        child: CircularProgressIndicator(
          strokeWidth: 2,
          valueColor: AlwaysStoppedAnimation<Color>(
            colorScheme.primary.withValues(
              alpha: widget.isWaitingTest && !_isSingleTesting ? 0.4 : 1.0,
            ),
          ),
        ),
      ),
    );
  }

  // 延迟徽章
  Widget _buildDelayBadge(
    ColorScheme colorScheme,
    bool isDark,
    double fontSize,
  ) {
    return _HoverableWidget(
      builder: (isHovering, onEnter, onExit) {
        return Align(
          alignment: Alignment.centerRight,
          child: MouseRegion(
            cursor: SystemMouseCursors.click,
            onEnter: onEnter,
            onExit: onExit,
            child: GestureDetector(
              onTap: _testDelay,
              child: AnimatedOpacity(
                duration: const Duration(milliseconds: 200),
                opacity: isHovering ? 0.7 : 1.0,
                child: Text(
                  '${widget.node.delay}ms',
                  textAlign: TextAlign.right,
                  style: TextStyle(
                    color: _getDelayColor(widget.node.delay),
                    fontWeight: FontWeight.w600,
                    fontSize: fontSize,
                    shadows: isHovering
                        ? [
                            Shadow(
                              color: _getDelayColor(
                                widget.node.delay,
                              ).withValues(alpha: 0.8),
                              blurRadius: 8,
                            ),
                          ]
                        : null,
                  ),
                ),
              ),
            ),
          ),
        );
      },
    );
  }

  // 测试图标
  Widget _buildTestIcon(ColorScheme colorScheme, double iconSize) {
    return _HoverableWidget(
      builder: (isHovering, onEnter, onExit) {
        return Align(
          alignment: Alignment.centerRight,
          child: MouseRegion(
            cursor: SystemMouseCursors.click,
            onEnter: onEnter,
            onExit: onExit,
            child: GestureDetector(
              onTap: _testDelay,
              child: AnimatedOpacity(
                duration: const Duration(milliseconds: 200),
                opacity: isHovering ? 0.7 : 1.0,
                child: Icon(
                  Icons.speed_rounded,
                  size: iconSize,
                  color: colorScheme.primary.withValues(alpha: 0.7),
                  shadows: isHovering
                      ? [
                          Shadow(
                            color: colorScheme.primary.withValues(alpha: 0.8),
                            blurRadius: 8,
                          ),
                        ]
                      : null,
                ),
              ),
            ),
          ),
        );
      },
    );
  }

  // 获取延迟颜色
  Color _getDelayColor(int? delay) {
    if (delay == null) {
      return Colors.grey;
    } else if (delay < 0) {
      // 超时显示红色
      return Colors.red;
    } else if (delay <= 300) {
      // 0-300ms 绿色（优秀）
      return Colors.green;
    } else if (delay <= 500) {
      // 300-500ms 蓝色（良好）
      return Colors.blue;
    } else {
      // 500ms+ 黄色（一般）
      return Colors.orange;
    }
  }
}

// 悬停状态辅助组件
class _HoverableWidget extends StatefulWidget {
  final Widget Function(
    bool isHovering,
    void Function(PointerEvent) onEnter,
    void Function(PointerEvent) onExit,
  )
  builder;

  const _HoverableWidget({required this.builder});

  @override
  State<_HoverableWidget> createState() => _HoverableWidgetState();
}

class _HoverableWidgetState extends State<_HoverableWidget> {
  bool _isHovering = false;

  void _onEnter(PointerEvent event) {
    if (!mounted) return;
    setState(() => _isHovering = true);
  }

  void _onExit(PointerEvent event) {
    if (!mounted) return;
    setState(() => _isHovering = false);
  }

  @override
  Widget build(BuildContext context) {
    return widget.builder(_isHovering, _onEnter, _onExit);
  }
}
