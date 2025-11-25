import 'package:flutter/material.dart';
import 'package:stelliberty/clash/data/clash_model.dart';
import 'package:stelliberty/ui/common/empty.dart';

/// 代理节点卡片组件 - 磨砂玻璃风格
///
/// 设计特点：
/// - 采用半透明背景 + 高斯模糊，营造磨砂玻璃效果
/// - 与日志卡片风格保持一致
/// - 选中状态使用主题色高亮
class ProxyNodeCard extends StatefulWidget {
  final ProxyNode node;
  final bool isSelected;
  final VoidCallback onTap;
  final Future<void> Function()? onTestDelay; // 测试延迟回调
  final bool isClashRunning; // Clash 是否正在运行
  final bool isWaitingTest; // 是否正在等待测试（批量测试时）

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

    return Tooltip(
      message: widget.node.name,
      child: Container(
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(16),
          color: backgroundColor,
          border: Border.all(
            color: widget.isSelected
                ? colorScheme.primary.withValues(alpha: isDark ? 0.7 : 0.6)
                : colorScheme.outline.withValues(alpha: 0.4),
            width: widget.isSelected ? 2 : 1,
          ),
          boxShadow: [
            BoxShadow(
              color: widget.isSelected
                  ? colorScheme.primary.withValues(alpha: isDark ? 0.3 : 0.15)
                  : Colors.black.withValues(alpha: isDark ? 0.2 : 0.06),
              blurRadius: widget.isSelected ? 12 : 8,
              offset: Offset(0, widget.isSelected ? 3 : 2),
            ),
          ],
        ),
        child: Container(
          decoration: BoxDecoration(borderRadius: BorderRadius.circular(16)),
          child: InkWell(
            borderRadius: BorderRadius.circular(16),
            onTap: widget.onTap,
            splashColor: Colors.transparent,
            highlightColor: Colors.transparent,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
              child: Row(
                children: [
                  // 选中指示器
                  AnimatedContainer(
                    duration: const Duration(milliseconds: 200),
                    width: 4,
                    height: 40,
                    decoration: BoxDecoration(
                      color: widget.isSelected
                          ? colorScheme.primary
                          : Colors.transparent,
                      borderRadius: BorderRadius.circular(2),
                      boxShadow: widget.isSelected
                          ? [
                              BoxShadow(
                                color: colorScheme.primary.withValues(
                                  alpha: 0.5,
                                ),
                                blurRadius: 8,
                              ),
                            ]
                          : null,
                    ),
                  ),
                  const SizedBox(width: 12),

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
                            fontSize: 14,
                            color: colorScheme.onSurface.withValues(
                              alpha: isDark ? 0.95 : 0.9,
                            ),
                          ),
                          overflow: TextOverflow.ellipsis,
                          maxLines: 1,
                        ),
                        const SizedBox(height: 6),
                        Transform.translate(
                          offset: const Offset(-6, 0),
                          child: Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 6,
                              vertical: 2,
                            ),
                            decoration: BoxDecoration(
                              color: colorScheme.surfaceContainerHighest
                                  .withValues(alpha: 0.4),
                              borderRadius: BorderRadius.circular(6),
                            ),
                            child: Text(
                              _formatProxyType(widget.node.type),
                              style: TextStyle(
                                fontSize: 10,
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
                  const SizedBox(width: 8),

                  // 延迟显示区域
                  SizedBox(
                    width: 85,
                    child: widget.isClashRunning
                        ? _buildDelaySection(context, colorScheme, isDark)
                        : empty,
                  ),
                ],
              ),
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
  ) {
    if (_isSingleTesting || widget.isWaitingTest) {
      return _buildLoadingIndicator(colorScheme);
    } else if (_hasTested) {
      return _buildDelayBadge(colorScheme, isDark);
    } else {
      return _buildTestIcon(colorScheme);
    }
  }

  // 加载指示器
  Widget _buildLoadingIndicator(ColorScheme colorScheme) {
    return Align(
      alignment: Alignment.centerRight,
      child: SizedBox(
        width: 18,
        height: 18,
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
  Widget _buildDelayBadge(ColorScheme colorScheme, bool isDark) {
    return _HoverableWidget(
      builder: (isHovering, onEnter, onExit) {
        return MouseRegion(
          cursor: SystemMouseCursors.click,
          onEnter: onEnter,
          onExit: onExit,
          child: GestureDetector(
            onTap: _testDelay,
            child: Align(
              alignment: Alignment.centerRight,
              child: AnimatedOpacity(
                duration: const Duration(milliseconds: 200),
                opacity: isHovering ? 0.7 : 1.0,
                child: Text(
                  '${widget.node.delay}ms',
                  textAlign: TextAlign.right,
                  style: TextStyle(
                    color: _getDelayColor(widget.node.delay),
                    fontWeight: FontWeight.w600,
                    fontSize: 12,
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
  Widget _buildTestIcon(ColorScheme colorScheme) {
    return _HoverableWidget(
      builder: (isHovering, onEnter, onExit) {
        return MouseRegion(
          cursor: SystemMouseCursors.click,
          onEnter: onEnter,
          onExit: onExit,
          child: GestureDetector(
            onTap: _testDelay,
            child: Align(
              alignment: Alignment.centerRight,
              child: AnimatedOpacity(
                duration: const Duration(milliseconds: 200),
                opacity: isHovering ? 0.7 : 1.0,
                child: Icon(
                  Icons.speed_rounded,
                  size: 20,
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

  // 格式化代理协议类型显示（首字母大写）
  String _formatProxyType(String type) {
    if (type.isEmpty) return type;

    final lowerType = type.toLowerCase();

    // 特殊处理缩写形式
    if (lowerType == 'ss') return 'SS';
    if (lowerType == 'ssr') return 'SSR';
    if (lowerType == 'http') return 'HTTP';
    if (lowerType == 'https') return 'HTTPS';
    if (lowerType == 'socks5') return 'Socks5';
    if (lowerType == 'socks') return 'Socks';

    // 特殊处理包含数字的协议名
    if (lowerType == 'hysteria2') return 'Hysteria2';

    // 特殊处理驼峰命名
    if (lowerType == 'vmess') return 'VMess';
    if (lowerType == 'vless') return 'VLess';
    if (lowerType == 'tuic') return 'Tuic';

    // 其他协议首字母大写
    return type[0].toUpperCase() + type.substring(1).toLowerCase();
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
