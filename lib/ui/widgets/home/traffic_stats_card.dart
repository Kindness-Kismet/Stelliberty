import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/model/traffic_data_model.dart';
import 'package:stelliberty/ui/widgets/home/base_card.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 流量统计卡片
//
// 显示累计上传/下载流量和实时速度波形图
class TrafficStatsCard extends StatefulWidget {
  const TrafficStatsCard({super.key});

  @override
  State<TrafficStatsCard> createState() => _TrafficStatsCardState();
}

class _TrafficStatsCardState extends State<TrafficStatsCard> {
  // 缓存最后一次的流量数据，避免页面切换时显示零值
  TrafficData? _trafficDataCache;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // 在 Widget 树构建完成后获取缓存数据，避免 initState 中使用 context 的问题
    _trafficDataCache ??= context
        .read<ClashProvider>()
        .clashManager
        .lastTrafficData;
  }

  @override
  Widget build(BuildContext context) {
    final manager = context.read<ClashProvider>().clashManager;
    final trans = context.translate;

    // 使用 select 精确监听 isRunning 状态，避免不必要的重建
    final isRunning = context.select<ClashProvider, bool>(
      (m) => m.isCoreRunning,
    );

    return isRunning
        ? StreamBuilder<TrafficData>(
            stream: context.read<ClashProvider>().trafficStream,
            builder: (context, snapshot) {
              // 优先使用流中的数据，如果没有则使用缓存的最后数据
              final traffic =
                  snapshot.data ?? _trafficDataCache ?? TrafficData.zero;

              // 缓存最新的数据（只在有新数据时更新）
              if (snapshot.hasData) {
                _trafficDataCache = snapshot.data;
              }

              return BaseCard(
                icon: Icons.data_usage,
                title: trans.home.traffic_stats,
                trailing: _buildTotalTrafficDisplay(context, traffic),
                child: _buildTrafficContent(context, traffic, isRunning),
              );
            },
          )
        : BaseCard(
            icon: Icons.data_usage,
            title: trans.home.traffic_stats,
            trailing: _buildTotalTrafficDisplay(
              context,
              manager.lastTrafficData ?? TrafficData.zero,
            ),
            child: _buildTrafficContent(
              context,
              manager.lastTrafficData ?? TrafficData.zero,
              isRunning,
            ),
          );
  }

  // 构建累计流量显示
  Widget _buildTotalTrafficDisplay(BuildContext context, TrafficData traffic) {
    final trans = context.translate;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          '${trans.home.upload}：',
          style: Theme.of(context).textTheme.bodySmall?.copyWith(
            color: Theme.of(
              context,
            ).colorScheme.onSurface.withValues(alpha: 0.6),
            fontSize: 11,
          ),
        ),
        Flexible(
          child: Text(
            _formatBytes(traffic.totalUpload),
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: Theme.of(context).colorScheme.primary,
              fontSize: 11,
              fontWeight: FontWeight.w500,
              fontFeatures: [const FontFeature.tabularFigures()],
            ),
            overflow: TextOverflow.ellipsis,
          ),
        ),
        const SizedBox(width: 12),
        Text(
          '${trans.home.download}：',
          style: Theme.of(context).textTheme.bodySmall?.copyWith(
            color: Theme.of(
              context,
            ).colorScheme.onSurface.withValues(alpha: 0.6),
            fontSize: 11,
          ),
        ),
        Flexible(
          child: Text(
            _formatBytes(traffic.totalDownload),
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: Colors.green,
              fontSize: 11,
              fontWeight: FontWeight.w500,
              fontFeatures: [const FontFeature.tabularFigures()],
            ),
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }

  // 格式化字节数显示（自动选择 B、KB、MB、GB）
  String _formatBytes(int bytes) {
    if (bytes < 1024) {
      return '${bytes}B';
    } else if (bytes < 1024 * 1024) {
      final kb = bytes / 1024;
      return '${kb.toStringAsFixed(kb < 100 ? 1 : 0)}KB';
    } else if (bytes < 1024 * 1024 * 1024) {
      final mb = bytes / (1024 * 1024);
      return '${mb.toStringAsFixed(mb < 100 ? 1 : 0)}MB';
    } else {
      final gb = bytes / (1024 * 1024 * 1024);
      return '${gb.toStringAsFixed(2)}GB';
    }
  }

  // 格式化速度显示（自动选择 B/s、KB/s、MB/s、GB/s）
  String _formatSpeed(double bytesPerSecond) {
    if (bytesPerSecond < 1024) {
      // < 1 KB/s，显示 B/s
      return '${bytesPerSecond.toStringAsFixed(0)} B/s';
    } else if (bytesPerSecond < 1024 * 1024) {
      // < 1 MB/s，显示 KB/s
      final kb = bytesPerSecond / 1024;
      return '${kb.toStringAsFixed(kb < 100 ? 1 : 0)} KB/s';
    } else if (bytesPerSecond < 1024 * 1024 * 1024) {
      // < 1 GB/s，显示 MB/s
      final mb = bytesPerSecond / (1024 * 1024);
      return '${mb.toStringAsFixed(mb < 100 ? 1 : 0)} MB/s';
    } else {
      // >= 1 GB/s，显示 GB/s
      final gb = bytesPerSecond / (1024 * 1024 * 1024);
      return '${gb.toStringAsFixed(2)} GB/s';
    }
  }

  Widget _buildTrafficContent(
    BuildContext context,
    TrafficData traffic,
    bool isRunning,
  ) {
    // 从 ClashManager 读取全局波形图历史数据
    final manager = context.read<ClashProvider>().clashManager;

    return Column(
      children: [
        // 波形图 - 使用 RepaintBoundary 隔离重绘区域
        RepaintBoundary(
          child: SizedBox(
            width: double.infinity,
            height: 120,
            child: CustomPaint(
              size: const Size(double.infinity, 120),
              painter: _TrafficWavePainter(
                // 从全局服务读取历史数据
                uploadHistory: manager.uploadHistory,
                downloadHistory: manager.downloadHistory,
                uploadColor: Theme.of(context).colorScheme.primary,
                downloadColor: Colors.green,
              ),
            ),
          ),
        ),

        const SizedBox(height: 16),

        // 速度统计和重置按钮
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            // 左侧：重置按钮
            if (isRunning)
              _ResetButton(onPressed: () => _resetTraffic(context))
            else
              const SizedBox.shrink(),

            // 右侧：上传下载速度 - 使用 Flexible 避免溢出
            Flexible(
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  // 上传速度
                  Flexible(
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          Icons.upload,
                          size: 16,
                          color: Theme.of(
                            context,
                          ).colorScheme.primary.withValues(alpha: 0.7),
                        ),
                        const SizedBox(width: 4),
                        Flexible(
                          child: Text(
                            _formatSpeed(traffic.upload.toDouble()),
                            style: Theme.of(context).textTheme.bodyMedium
                                ?.copyWith(fontWeight: FontWeight.w500),
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                      ],
                    ),
                  ),

                  const SizedBox(width: 12),

                  // 下载速度
                  Flexible(
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          Icons.download,
                          size: 16,
                          color: Colors.green.withValues(alpha: 0.7),
                        ),
                        const SizedBox(width: 4),
                        Flexible(
                          child: Text(
                            _formatSpeed(traffic.download.toDouble()),
                            style: Theme.of(context).textTheme.bodyMedium
                                ?.copyWith(fontWeight: FontWeight.w500),
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ],
    );
  }

  // 重置流量统计（无需确认对话框）
  void _resetTraffic(BuildContext context) {
    final clashManager = context.read<ClashProvider>().clashManager;
    clashManager.resetTrafficStats();
    // 清空本地缓存
    setState(() {
      _trafficDataCache = TrafficData.zero;
    });
  }
}

/// 重置按钮组件
class _ResetButton extends StatefulWidget {
  final VoidCallback onPressed;

  const _ResetButton({required this.onPressed});

  @override
  State<_ResetButton> createState() => _ResetButtonState();
}

class _ResetButtonState extends State<_ResetButton> {
  bool _isHovered = false;

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return MouseRegion(
      onEnter: (_) => setState(() => _isHovered = true),
      onExit: (_) => setState(() => _isHovered = false),
      child: InkWell(
        onTap: widget.onPressed,
        borderRadius: BorderRadius.circular(8),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 200),
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          decoration: BoxDecoration(
            color: _isHovered
                ? Theme.of(
                    context,
                  ).colorScheme.surfaceContainerHighest.withValues(alpha: 0.5)
                : Theme.of(
                    context,
                  ).colorScheme.surfaceContainerHighest.withValues(alpha: 0.3),
            borderRadius: BorderRadius.circular(8),
            border: Border.all(
              color: _isHovered
                  ? Theme.of(context).colorScheme.outline.withValues(alpha: 0.3)
                  : Theme.of(
                      context,
                    ).colorScheme.outline.withValues(alpha: 0.2),
            ),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                Icons.restart_alt,
                size: 16,
                color: Theme.of(context).colorScheme.primary,
              ),
              const SizedBox(width: 6),
              Text(
                trans.home.reset,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: Theme.of(context).colorScheme.primary,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// 流量波形图绘制器
class _TrafficWavePainter extends CustomPainter {
  final List<double> uploadHistory;
  final List<double> downloadHistory;
  final Color uploadColor;
  final Color downloadColor;

  _TrafficWavePainter({
    required this.uploadHistory,
    required this.downloadHistory,
    required this.uploadColor,
    required this.downloadColor,
  });

  @override
  void paint(Canvas canvas, Size size) {
    // 找到最大值用于归一化
    final allValues = [...uploadHistory, ...downloadHistory];
    final maxValue = allValues.reduce((a, b) => a > b ? a : b);
    final normalizedMax = maxValue > 0 ? maxValue : 1.0;

    // 绘制下载曲线（绿色，在下层）
    _drawWave(canvas, size, downloadHistory, downloadColor, normalizedMax);

    // 绘制上传曲线（主题色，在上层）
    _drawWave(canvas, size, uploadHistory, uploadColor, normalizedMax);
  }

  void _drawWave(
    Canvas canvas,
    Size size,
    List<double> history,
    Color color,
    double maxValue,
  ) {
    if (history.isEmpty) return;

    final paint = Paint()
      ..color = color.withValues(alpha: 0.7)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2.0
      ..strokeCap = StrokeCap.round;

    final fillPaint = Paint()
      ..color = color.withValues(alpha: 0.15)
      ..style = PaintingStyle.fill;

    final path = Path();
    final fillPath = Path();

    final stepX = size.width / (history.length - 1);

    // 起始点
    final firstY = size.height - (history[0] / maxValue * size.height * 0.8);
    path.moveTo(0, firstY);
    fillPath.moveTo(0, size.height);
    fillPath.lineTo(0, firstY);

    // 绘制曲线（使用中点平滑算法）
    for (int i = 1; i < history.length - 1; i++) {
      final currentX = i * stepX;
      final currentY =
          size.height - (history[i] / maxValue * size.height * 0.8);
      final nextX = (i + 1) * stepX;
      final nextY =
          size.height - (history[i + 1] / maxValue * size.height * 0.8);

      // 计算当前点和下一个点的中点
      final midX = (currentX + nextX) / 2;
      final midY = (currentY + nextY) / 2;

      // 贝塞尔曲线经过当前点，终点是到下一个点的中点
      path.quadraticBezierTo(currentX, currentY, midX, midY);
      fillPath.quadraticBezierTo(currentX, currentY, midX, midY);
    }

    // 处理最后一个点
    if (history.length > 1) {
      final lastX = (history.length - 1) * stepX;
      final lastY = size.height - (history.last / maxValue * size.height * 0.8);
      path.lineTo(lastX, lastY);
      fillPath.lineTo(lastX, lastY);
    }

    // 填充区域
    fillPath.lineTo(size.width, size.height);
    fillPath.close();
    canvas.drawPath(fillPath, fillPaint);

    // 绘制线条
    canvas.drawPath(path, paint);
  }

  @override
  bool shouldRepaint(_TrafficWavePainter oldDelegate) {
    return uploadHistory != oldDelegate.uploadHistory ||
        downloadHistory != oldDelegate.downloadHistory;
  }
}
