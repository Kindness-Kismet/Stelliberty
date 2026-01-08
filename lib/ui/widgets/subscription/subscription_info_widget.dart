import 'package:flutter/material.dart';
import 'package:stelliberty/clash/model/provider_model.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:intl/intl.dart';

class SubscriptionInfoWidget extends StatelessWidget {
  final SubscriptionInfo subscriptionInfo;

  const SubscriptionInfoWidget({super.key, required this.subscriptionInfo});

  String _formatBytes(int bytes) {
    if (bytes < 1024) {
      return '${bytes}B';
    } else if (bytes < 1024 * 1024) {
      return '${(bytes / 1024).toStringAsFixed(2)}KB';
    } else if (bytes < 1024 * 1024 * 1024) {
      return '${(bytes / (1024 * 1024)).toStringAsFixed(2)}MB';
    } else {
      return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(2)}GB';
    }
  }

  String _formatExpireTime(BuildContext context, int expireTimestamp) {
    final trans = context.translate;

    if (expireTimestamp == 0) {
      return trans.provider.permanent;
    }
    final expireDate = DateTime.fromMillisecondsSinceEpoch(
      expireTimestamp * 1000,
    );
    return DateFormat('yyyy-MM-dd').format(expireDate);
  }

  @override
  Widget build(BuildContext context) {
    if (subscriptionInfo.total == 0) {
      return const SizedBox.shrink();
    }

    final used = subscriptionInfo.upload + subscriptionInfo.download;
    final total = subscriptionInfo.total;
    final progress = (used / total).clamp(0.0, 1.0);

    final usedStr = _formatBytes(used);
    final totalStr = _formatBytes(total);
    final expireStr = _formatExpireTime(context, subscriptionInfo.expire);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SizedBox(height: 8),
        LinearProgressIndicator(
          value: progress,
          minHeight: 4,
          backgroundColor: Theme.of(
            context,
          ).colorScheme.primary.withValues(alpha: 0.15),
          valueColor: AlwaysStoppedAnimation<Color>(
            Theme.of(context).colorScheme.primary,
          ),
        ),
        const SizedBox(height: 8),
        Text(
          '$usedStr / $totalStr · $expireStr',
          style: TextStyle(
            fontSize: 11,
            color: Theme.of(
              context,
            ).colorScheme.onSurface.withValues(alpha: 0.6),
          ),
        ),
      ],
    );
  }
}
