import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/model/traffic_data_model.dart';
import 'package:stelliberty/clash/providers/traffic_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/home/base_card.dart';

// 网速显示卡片：展示实时波形与上下行速度。
class TrafficSpeedCard extends StatelessWidget {
  final TrafficData traffic;
  final bool isCoreRunning;
  final VoidCallback onReset;

  const TrafficSpeedCard({
    super.key,
    required this.traffic,
    required this.isCoreRunning,
    required this.onReset,
  });

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return BaseCard(
      icon: Icons.data_usage,
      title: trans.home.traffic_stats,
      trailing: isCoreRunning ? _ResetButton(onPressed: onReset) : null,
      child: _buildTrafficContent(context, traffic),
    );
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

  Widget _buildTrafficContent(BuildContext context, TrafficData traffic) {
    // 从 TrafficProvider 读取波形图历史数据
    final trafficProvider = context.read<TrafficProvider>();

    final uploadColor = Theme.of(context).colorScheme.primary;
    final downloadColor = Colors.green;

    final totalUpload = traffic.totalUpload;
    final totalDownload = traffic.totalDownload;
    final trans = context.translate;

    return SizedBox(
      height: 176,
      child: LayoutBuilder(
        builder: (context, constraints) {
          const gap = 24.0;
          final availableWidth = constraints.maxWidth - gap;
          final segmentWidth = availableWidth > 0 ? availableWidth / 2 : 0.0;

          return Stack(
            children: [
              Align(
                alignment: Alignment.bottomLeft,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    RepaintBoundary(
                      child: SizedBox(
                        width: double.infinity,
                        height: 120,
                        child: CustomPaint(
                          size: const Size(double.infinity, 120),
                          painter: _TrafficWavePainter(
                            uploadHistory: trafficProvider.uploadHistory,
                            downloadHistory: trafficProvider.downloadHistory,
                            uploadColor: uploadColor,
                            downloadColor: downloadColor,
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(height: 16),
                    _buildTrafficTotals(
                      context,
                      uploadTotal: totalUpload,
                      downloadTotal: totalDownload,
                      uploadColor: uploadColor,
                      downloadColor: downloadColor,
                    ),
                  ],
                ),
              ),
              Positioned(
                top: 0,
                left: 0,
                right: 0,
                child: Row(
                  children: [
                    SizedBox(
                      width: segmentWidth,
                      child: Align(
                        alignment: Alignment.topRight,
                        child: _buildSpeedStat(
                          context,
                          label: trans.connection.upload_speed,
                          value: _formatSpeed(traffic.upload.toDouble()),
                          color: uploadColor,
                          icon: Icons.arrow_upward_rounded,
                        ),
                      ),
                    ),
                    const SizedBox(width: gap),
                    SizedBox(
                      width: segmentWidth,
                      child: Align(
                        alignment: Alignment.topRight,
                        child: _buildSpeedStat(
                          context,
                          label: trans.connection.download_speed,
                          value: _formatSpeed(traffic.download.toDouble()),
                          color: downloadColor,
                          icon: Icons.arrow_downward_rounded,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          );
        },
      ),
    );
  }

  Widget _buildSpeedStat(
    BuildContext context, {
    required String label,
    required String value,
    required Color color,
    required IconData icon,
  }) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.end,
      children: [
        Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 14, color: color.withValues(alpha: 0.8)),
            const SizedBox(width: 4),
            Text(
              label,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: color.withValues(alpha: 0.7),
                fontSize: 11,
                fontWeight: FontWeight.w500,
              ),
            ),
          ],
        ),
        const SizedBox(height: 4),
        Text(
          value,
          style: Theme.of(context).textTheme.titleSmall?.copyWith(
            color: color,
            fontWeight: FontWeight.w600,
            fontFeatures: [const FontFeature.tabularFigures()],
          ),
        ),
      ],
    );
  }

  Widget _buildTrafficTotals(
    BuildContext context, {
    required int uploadTotal,
    required int downloadTotal,
    required Color uploadColor,
    required Color downloadColor,
  }) {
    final trans = context.translate;

    return Row(
      children: [
        Expanded(
          child: _buildTrafficTotalStat(
            context,
            label: trans.home.upload,
            value: _formatBytes(uploadTotal),
            color: uploadColor,
            icon: Icons.arrow_upward_rounded,
          ),
        ),
        const SizedBox(width: 24),
        Expanded(
          child: _buildTrafficTotalStat(
            context,
            label: trans.home.download,
            value: _formatBytes(downloadTotal),
            color: downloadColor,
            icon: Icons.arrow_downward_rounded,
          ),
        ),
      ],
    );
  }

  Widget _buildTrafficTotalStat(
    BuildContext context, {
    required String label,
    required String value,
    required Color color,
    required IconData icon,
  }) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 12, color: color.withValues(alpha: 0.8)),
        const SizedBox(width: 4),
        Text(
          label,
          style: Theme.of(context).textTheme.bodySmall?.copyWith(
            color: color.withValues(alpha: 0.7),
            fontSize: 11,
          ),
        ),
        const SizedBox(width: 4),
        Flexible(
          child: Text(
            value,
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: color,
              fontWeight: FontWeight.w600,
              fontFeatures: [const FontFeature.tabularFigures()],
            ),
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }

  String _formatBytes(int bytes) {
    return TrafficData.formatBytes(bytes);
  }
}

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
    final colorScheme = Theme.of(context).colorScheme;

    return MouseRegion(
      onEnter: (_) => setState(() => _isHovered = true),
      onExit: (_) => setState(() => _isHovered = false),
      child: InkWell(
        onTap: widget.onPressed,
        borderRadius: BorderRadius.circular(8),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 200),
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
          decoration: BoxDecoration(
            color: _isHovered
                ? colorScheme.surfaceContainerHighest.withValues(alpha: 0.5)
                : colorScheme.surfaceContainerHighest.withValues(alpha: 0.3),
            borderRadius: BorderRadius.circular(8),
            border: Border.all(
              color: _isHovered
                  ? colorScheme.outline.withValues(alpha: 0.3)
                  : colorScheme.outline.withValues(alpha: 0.2),
            ),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.restart_alt, size: 14, color: colorScheme.primary),
              const SizedBox(width: 4),
              Text(
                trans.home.reset,
                style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: colorScheme.primary,
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

// 流量波形图绘制器。
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
    const gap = 24.0;
    final segmentWidth = (size.width - gap) / 2;
    final uploadRect = Rect.fromLTWH(0, 0, segmentWidth, size.height);
    final downloadRect = Rect.fromLTWH(
      segmentWidth + gap,
      0,
      segmentWidth,
      size.height,
    );

    _drawWaveSegment(canvas, uploadRect, uploadHistory, uploadColor);
    _drawWaveSegment(canvas, downloadRect, downloadHistory, downloadColor);
  }

  void _drawWaveSegment(
    Canvas canvas,
    Rect rect,
    List<double> history,
    Color color,
  ) {
    if (history.length < 2) {
      return;
    }

    final maxValue = history.reduce((a, b) => a > b ? a : b);
    final normalizedMax = maxValue > 0 ? maxValue : 1.0;

    final linePaint = Paint()
      ..color = color.withValues(alpha: 0.85)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2.0
      ..strokeCap = StrokeCap.round;

    final fillPaint = Paint()
      ..color = color.withValues(alpha: 0.12)
      ..style = PaintingStyle.fill;

    const dotRadius = 6.0;
    final dotPaint = Paint()
      ..color = color.withValues(alpha: 0.9)
      ..style = PaintingStyle.fill;
    final dotBorderPaint = Paint()
      ..color = Colors.white
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2.0;

    final path = Path();
    final fillPath = Path();

    final stepX = rect.width / (history.length - 1);

    // 起始点
    final firstY =
        rect.bottom - (history[0] / normalizedMax * rect.height * 0.75);
    path.moveTo(rect.left, firstY);
    fillPath.moveTo(rect.left, rect.bottom);
    fillPath.lineTo(rect.left, firstY);

    // 绘制曲线（使用中点平滑算法）
    for (int i = 1; i < history.length - 1; i++) {
      final currentX = rect.left + i * stepX;
      final currentY =
          rect.bottom - (history[i] / normalizedMax * rect.height * 0.75);
      final nextX = rect.left + (i + 1) * stepX;
      final nextY =
          rect.bottom - (history[i + 1] / normalizedMax * rect.height * 0.75);

      // 计算当前点和下一个点的中点
      final midX = (currentX + nextX) / 2;
      final midY = (currentY + nextY) / 2;

      // 贝塞尔曲线经过当前点，终点是到下一个点的中点
      path.quadraticBezierTo(currentX, currentY, midX, midY);
      fillPath.quadraticBezierTo(currentX, currentY, midX, midY);
    }

    // 处理最后一个点
    Offset? lastPoint;
    if (history.length > 1) {
      final lastX = rect.left + (history.length - 1) * stepX;
      final lastY =
          rect.bottom - (history.last / normalizedMax * rect.height * 0.75);
      path.lineTo(lastX, lastY);
      fillPath.lineTo(lastX, lastY);
      lastPoint = Offset(lastX, lastY);
    }

    // 填充区域
    fillPath.lineTo(rect.right, rect.bottom);
    fillPath.close();
    canvas.drawPath(fillPath, fillPaint);

    // 绘制线条
    canvas.drawPath(path, linePaint);

    if (lastPoint != null) {
      canvas.drawCircle(lastPoint, dotRadius, dotPaint);
      canvas.drawCircle(lastPoint, dotRadius, dotBorderPaint);
    }
  }

  @override
  bool shouldRepaint(_TrafficWavePainter oldDelegate) {
    return uploadHistory != oldDelegate.uploadHistory ||
        downloadHistory != oldDelegate.downloadHistory;
  }
}
