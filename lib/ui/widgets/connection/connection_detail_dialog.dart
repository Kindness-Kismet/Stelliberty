import 'package:flutter/material.dart';
import 'package:stelliberty/clash/data/connection_model.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';

// 连接详情对话框
// 显示连接的完整信息，包括所有新增的字段
class ConnectionDetailDialog extends StatelessWidget {
  final ConnectionInfo connection;

  const ConnectionDetailDialog({super.key, required this.connection});

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return ModernDialog(
      title: trans.connection.connectionDetails,
      titleIcon: Icons.info_outline_rounded,
      maxWidth: 600,
      maxHeightRatio: 0.8,
      content: _buildContent(context),
      actionsRight: [
        DialogActionButton(
          label: trans.connection.exitButton,
          isPrimary: false,
          onPressed: () => Navigator.of(context).pop(),
        ),
      ],
    );
  }

  Widget _buildContent(BuildContext context) {
    final trans = context.translate;

    final metadata = connection.metadata;
    final t = trans.connection;

    // 定义所有信息项，用于分组
    final infoItems = {
      t.groups.general: [
        _InfoItem(t.connectionType, metadata.type),
        _InfoItem(t.protocol, metadata.network.toUpperCase()),
        _InfoItem(t.targetAddress, metadata.displayHost),
        if (metadata.host.isNotEmpty) _InfoItem(t.hostLabel, metadata.host),
        if (metadata.sniffHost.isNotEmpty)
          _InfoItem(t.sniffHost, metadata.sniffHost),
        _InfoItem(t.proxyGroup, connection.proxyNode),
        _InfoItem(t.proxyNode, connection.proxyGroup),
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
      return const SizedBox.shrink();
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

  // 显示连接详情对话框
  static void show(BuildContext context, ConnectionInfo connection) {
    showDialog(
      context: context,
      barrierDismissible: true,
      builder: (context) => ConnectionDetailDialog(connection: connection),
    );
  }
}

/// 辅助类，用于封装信息项
class _InfoItem {
  final String label;
  final String value;

  _InfoItem(this.label, this.value);
}
