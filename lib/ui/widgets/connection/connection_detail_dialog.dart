import 'dart:ui';

import 'package:flutter/material.dart';
import 'package:stelliberty/clash/data/connection_model.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/common/empty.dart';

/// 连接详情对话框
/// 显示连接的完整信息，包括所有新增的字段
class ConnectionDetailDialog extends StatelessWidget {
  final ConnectionInfo connection;

  const ConnectionDetailDialog({super.key, required this.connection});

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    return ConstrainedBox(
      constraints: BoxConstraints(
        maxWidth: 600,
        maxHeight: MediaQuery.of(context).size.height * 0.8,
      ),
      child: Container(
        margin: const EdgeInsets.symmetric(horizontal: 32),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(20),
          child: BackdropFilter(
            filter: ImageFilter.blur(sigmaX: 16, sigmaY: 16),
            child: Container(
              decoration: BoxDecoration(
                color: isDark
                    ? Colors.white.withAlpha(20)
                    : Colors.white.withAlpha(178),
                borderRadius: BorderRadius.circular(20),
                border: Border.all(
                  color: Colors.white.withAlpha(isDark ? 26 : 77),
                  width: 1,
                ),
                boxShadow: [
                  BoxShadow(
                    color: Colors.black.withAlpha(46),
                    blurRadius: 40,
                    offset: const Offset(0, 10),
                  ),
                ],
              ),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _buildHeader(context),
                  Flexible(child: _buildContent(context)),
                  _buildActions(context),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildHeader(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 20),
      decoration: BoxDecoration(
        color: isDark ? Colors.white.withAlpha(15) : Colors.white.withAlpha(77),
        border: Border(
          bottom: BorderSide(
            color: Colors.white.withAlpha(isDark ? 26 : 77),
            width: 1,
          ),
        ),
      ),
      child: Row(
        children: [
          Icon(
            Icons.info_outline_rounded,
            size: 24,
            color: Theme.of(context).colorScheme.primary,
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Text(
              context.translate.connection.connectionDetails,
              style: Theme.of(
                context,
              ).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w600),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildContent(BuildContext context) {
    final metadata = connection.metadata;
    final t = context.translate.connection;

    // 定义所有信息项，用于分组
    final infoItems = {
      t.groups.general: [
        _InfoItem(t.connectionType, metadata.type),
        _InfoItem(t.protocol, metadata.network.toUpperCase()),
        _InfoItem(t.targetAddress, metadata.displayHost),
        if (metadata.host.isNotEmpty) _InfoItem(t.hostLabel, metadata.host),
        if (metadata.sniffHost.isNotEmpty)
          _InfoItem(t.sniffHost, metadata.sniffHost),
        _InfoItem(t.proxyNode, connection.proxyNode),
        if (connection.chains.length > 1)
          _InfoItem(t.proxyChain, connection.chains.reversed.join(' → ')),
        _InfoItem(t.ruleLabel, connection.rule),
        _InfoItem(t.rulePayload, connection.rulePayload),
      ],
      t.groups.source: [
        _InfoItem(t.sourceIP, metadata.sourceIP),
        _InfoItem(t.sourcePort, metadata.sourcePort),
        _InfoItem(t.sourceGeoIP, metadata.sourceGeoIP.join(', ')),
        _InfoItem(t.sourceIPASN, metadata.sourceIPASN),
      ],
      t.groups.destination: [
        _InfoItem(t.destinationIP, metadata.destinationIP),
        _InfoItem(t.destinationPort, metadata.destinationPort),
        _InfoItem(t.destinationGeoIP, metadata.destinationGeoIP.join(', ')),
        _InfoItem(t.destinationIPASN, metadata.destinationIPASN),
        _InfoItem(t.remoteDestination, metadata.remoteDestination),
      ],
      t.groups.inbound: [
        _InfoItem(t.inboundName, metadata.inboundName),
        _InfoItem(t.inboundIP, metadata.inboundIP),
        if (metadata.inboundPort != '0')
          _InfoItem(t.inboundPort, metadata.inboundPort),
        _InfoItem(t.inboundUser, metadata.inboundUser),
      ],
      t.groups.process: [
        _InfoItem(t.processLabel, metadata.process),
        _InfoItem(t.processPath, metadata.processPath),
        if (metadata.uid != null)
          _InfoItem(t.processUID, metadata.uid.toString()),
      ],
      t.groups.advanced: [
        _InfoItem(t.dnsMode, metadata.dnsMode),
        if (metadata.dscp != 0) _InfoItem(t.dscp, metadata.dscp.toString()),
        _InfoItem(t.specialProxy, metadata.specialProxy),
        _InfoItem(t.specialRules, metadata.specialRules),
      ],
      t.groups.traffic: [
        _InfoItem(t.uploadLabel, _formatBytes(connection.upload)),
        _InfoItem(t.downloadLabel, _formatBytes(connection.download)),
        _InfoItem(t.uploadSpeed, '${_formatBytes(connection.uploadSpeed)}/s'),
        _InfoItem(
          t.downloadSpeed,
          '${_formatBytes(connection.downloadSpeed)}/s',
        ),
      ],
      t.groups.meta: [
        _InfoItem(t.durationLabel, connection.formattedDuration),
        _InfoItem(t.connectionId, connection.id),
      ],
    };

    return SingleChildScrollView(
      padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: infoItems.entries
            .map((entry) => _buildInfoSection(context, entry.key, entry.value))
            .toList(),
      ),
    );
  }

  Widget _buildActions(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      color: Colors.transparent,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
        decoration: BoxDecoration(
          color: isDark
              ? Colors.white.withValues(alpha: 0.06)
              : Colors.white.withValues(alpha: 0.3),
          border: Border(
            top: BorderSide(
              color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.3),
              width: 1,
            ),
          ),
        ),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.end,
          children: [
            ElevatedButton(
              onPressed: () => Navigator.of(context).pop(),
              style: ElevatedButton.styleFrom(
                padding: const EdgeInsets.symmetric(
                  vertical: 12,
                  horizontal: 16,
                ),
                backgroundColor: Theme.of(context).colorScheme.primary,
                foregroundColor: Colors.white,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(10),
                ),
                elevation: 0,
                shadowColor: Theme.of(
                  context,
                ).colorScheme.primary.withValues(alpha: 0.5),
              ),
              child: Text(context.translate.connection.exitButton),
            ),
          ],
        ),
      ),
    );
  }

  // 构建信息分组
  Widget _buildInfoSection(
    BuildContext context,
    String title,
    List<_InfoItem> items,
  ) {
    // 过滤掉值为空的项
    final validItems = items.where((item) => item.value.isNotEmpty).toList();

    // 如果分组内没有有效信息，则不显示该分组
    if (validItems.isEmpty) {
      return empty;
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: 16.0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            title,
            style: Theme.of(
              context,
            ).textTheme.titleSmall?.copyWith(fontWeight: FontWeight.bold),
          ),
          const SizedBox(height: 8),
          ...validItems.map(
            (item) => _buildDetailRow(context, item.label, item.value),
          ),
        ],
      ),
    );
  }

  // 构建详情行
  Widget _buildDetailRow(BuildContext context, String label, String value) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: 100,
            child: Text(
              '$label:',
              style: TextStyle(
                fontWeight: FontWeight.normal,
                fontSize: 13,
                color: Theme.of(context).textTheme.bodySmall?.color,
              ),
            ),
          ),
          Expanded(
            child: SelectableText(value, style: const TextStyle(fontSize: 13)),
          ),
        ],
      ),
    );
  }

  // 格式化字节数
  String _formatBytes(int bytes) {
    if (bytes < 1024) {
      return '$bytes B';
    } else if (bytes < 1024 * 1024) {
      return '${(bytes / 1024).toStringAsFixed(1)} KB';
    } else if (bytes < 1024 * 1024 * 1024) {
      return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
    } else {
      return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(1)} GB';
    }
  }

  /// 显示连接详情对话框
  static void show(BuildContext context, ConnectionInfo connection) {
    showGeneralDialog(
      context: context,
      barrierDismissible: true,
      barrierLabel: MaterialLocalizations.of(context).modalBarrierDismissLabel,
      pageBuilder: (context, animation, secondaryAnimation) {
        return Dialog(
          backgroundColor: Colors.transparent,
          elevation: 0,
          child: ConnectionDetailDialog(connection: connection),
        );
      },
      transitionBuilder: (context, animation, secondaryAnimation, child) {
        return FadeTransition(
          opacity: animation,
          child: ScaleTransition(
            scale: animation.drive(Tween(begin: 0.9, end: 1.0)),
            child: child,
          ),
        );
      },
    );
  }
}

/// 辅助类，用于封装信息项
class _InfoItem {
  final String label;
  final String value;

  _InfoItem(this.label, this.value);
}
