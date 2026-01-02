import 'package:flutter/material.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 代理页面空状态类型
enum ProxyEmptyStateType { noSubscription, error, directMode, noProxyGroups }

// 代理页面空状态视图组件
class ProxyEmptyState extends StatelessWidget {
  final ProxyEmptyStateType type;
  final String? message;
  final String? subtitle;

  const ProxyEmptyState({
    super.key,
    required this.type,
    this.message,
    this.subtitle,
  });

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16.0),
      child: Center(
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: _buildContent(context),
        ),
      ),
    );
  }

  List<Widget> _buildContent(BuildContext context) {
    final trans = context.translate;
    switch (type) {
      case ProxyEmptyStateType.noSubscription:
        return [
          const Icon(Icons.warning_amber, size: 64, color: Colors.orange),
          const SizedBox(height: 16),
          Text(
            message ?? trans.proxy.empty_no_subscription,
            style: const TextStyle(fontSize: 18, color: Colors.grey),
          ),
          const SizedBox(height: 8),
          Text(
            subtitle ?? trans.proxy.empty_please_add_first,
            style: const TextStyle(color: Colors.grey),
          ),
        ];

      case ProxyEmptyStateType.error:
        return [
          const Icon(Icons.error_outline, size: 64, color: Colors.red),
          const SizedBox(height: 16),
          Text(
            message ?? trans.proxy.empty_error,
            style: const TextStyle(color: Colors.red),
            textAlign: TextAlign.center,
          ),
        ];

      case ProxyEmptyStateType.directMode:
        return [
          const Icon(Icons.settings_ethernet, size: 64, color: Colors.blue),
          const SizedBox(height: 16),
          Text(
            message ?? trans.proxy.empty_direct_mode,
            style: const TextStyle(fontSize: 18, color: Colors.blue),
          ),
          const SizedBox(height: 8),
          Text(
            subtitle ?? trans.proxy.empty_direct_mode_desc,
            style: const TextStyle(color: Colors.grey),
            textAlign: TextAlign.center,
          ),
        ];

      case ProxyEmptyStateType.noProxyGroups:
        return [
          const Icon(Icons.inbox, size: 64, color: Colors.grey),
          const SizedBox(height: 16),
          Text(
            message ?? trans.proxy.empty_no_proxy_groups,
            style: const TextStyle(fontSize: 18, color: Colors.grey),
          ),
          if (subtitle != null) ...[
            const SizedBox(height: 8),
            Text(subtitle!, style: const TextStyle(color: Colors.grey)),
          ],
        ];
    }
  }
}
