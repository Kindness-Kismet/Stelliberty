import 'package:flutter/material.dart';
import 'package:stelliberty/clash/model/log_message_model.dart';

/// 日志卡片组件 - 与代理节点卡片风格一致
///
/// 设计特点：
/// - 采用半透明背景 + 混色效果
/// - 与代理节点卡片风格保持一致
/// - 移除 BackdropFilter 避免渲染闪烁
class LogCard extends StatelessWidget {
  final ClashLogMessage log;

  const LogCard({super.key, required this.log});

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    // 混色：亮色主题混入 10% 白色，暗色主题混入 10% 黑色
    final mixColor = isDark ? Colors.black : Colors.white;
    final mixOpacity = 0.1;

    return Container(
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(16),
        // 半透明背景，并混入白色/黑色
        color: Color.alphaBlend(
          mixColor.withValues(alpha: mixOpacity),
          colorScheme.surface.withValues(alpha: isDark ? 0.7 : 0.85),
        ),
        border: Border.all(
          color: colorScheme.outline.withValues(alpha: 0.4),
          width: 1,
        ),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: isDark ? 0.2 : 0.06),
            blurRadius: 8,
            offset: const Offset(0, 2),
          ),
        ],
      ),
      child: Container(
        height: 72,
        padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            // 时间戳
            Container(
              width: 72,
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
              decoration: BoxDecoration(
                color: colorScheme.surfaceContainerHighest.withValues(
                  alpha: 0.4,
                ),
                borderRadius: BorderRadius.circular(8),
              ),
              child: Text(
                log.formattedTime,
                style: TextStyle(
                  fontSize: 11,
                  fontWeight: FontWeight.w600,
                  color: colorScheme.onSurfaceVariant,
                  letterSpacing: 0.3,
                ),
                textAlign: TextAlign.center,
              ),
            ),
            const SizedBox(width: 14),

            // 日志级别标签
            Container(
              width: 70,
              height: 28,
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  colors: [
                    _getLogLevelColor(log.level).withValues(alpha: 0.2),
                    _getLogLevelColor(log.level).withValues(alpha: 0.15),
                  ],
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                ),
                borderRadius: BorderRadius.circular(10),
                border: Border.all(
                  color: _getLogLevelColor(log.level).withValues(alpha: 0.3),
                  width: 1,
                ),
              ),
              child: Center(
                child: Text(
                  log.type.toUpperCase(),
                  style: TextStyle(
                    fontSize: 10,
                    fontWeight: FontWeight.bold,
                    color: _getLogLevelColor(log.level),
                    letterSpacing: 0.8,
                  ),
                ),
              ),
            ),
            const SizedBox(width: 16),

            // 日志内容
            Expanded(
              child: Text(
                log.payload,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 13,
                  height: 1.5,
                  color: colorScheme.onSurface.withValues(
                    alpha: isDark ? 0.95 : 0.9,
                  ),
                  fontWeight: FontWeight.w500,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  /// 获取日志级别颜色
  Color _getLogLevelColor(ClashLogLevel level) {
    switch (level) {
      case ClashLogLevel.error:
        return const Color(0xFFEF5350); // Red
      case ClashLogLevel.warning:
        return const Color(0xFFFF9800); // Orange
      case ClashLogLevel.debug:
        return const Color(0xFFAB47BC); // Purple
      case ClashLogLevel.info:
        return const Color(0xFF42A5F5); // Blue
      case ClashLogLevel.silent:
        return const Color(0xFF9E9E9E); // Grey
    }
  }
}
