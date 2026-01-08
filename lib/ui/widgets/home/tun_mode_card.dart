import 'dart:io';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/service_provider.dart';
import 'package:stelliberty/clash/state/service_states.dart';
import 'package:stelliberty/atomic/permission_checker.dart';
import 'package:stelliberty/tray/tray_manager.dart';
import 'package:stelliberty/ui/widgets/home/base_card.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 虚拟网卡模式控制卡片
//
// 提供 TUN 模式开关和状态显示
// 支持服务模式、管理员模式或 root 模式启动
class TunModeCard extends StatefulWidget {
  const TunModeCard({super.key});

  @override
  State<TunModeCard> createState() => _TunModeCardState();
}

class _TunModeCardState extends State<TunModeCard> {
  bool _isElevated = false;

  @override
  void initState() {
    super.initState();
    _checkElevation();
  }

  Future<void> _checkElevation() async {
    final elevated = await PermissionService.isElevated();
    if (mounted) {
      setState(() {
        _isElevated = elevated;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final serviceProvider = context.watch<ServiceProvider>();
    final isServiceModeInstalled =
        serviceProvider.serviceState.isServiceModeInstalled;
    final trans = context.translate;

    // TUN 模式可用条件：服务模式已安装 或 以管理员/root 权限运行
    final canEnableTun = isServiceModeInstalled || _isElevated;

    // Android 平台不支持 TUN 模式
    if (Platform.isAndroid) {
      return BaseCard(
        icon: Icons.router_outlined,
        title: trans.proxy.tun_mode,
        // Android 平台禁用开关
        trailing: ModernSwitch(value: false, onChanged: null),
        child: _buildUnsupportedContent(context),
      );
    }

    return Selector<ClashProvider, bool>(
      selector: (_, provider) => provider.isTunEnabled,
      builder: (context, isTunEnabled, child) {
        return BaseCard(
          icon: Icons.router_outlined,
          title: trans.proxy.tun_mode,
          // 右边只有开关
          trailing: ModernSwitch(
            value: isTunEnabled,
            onChanged: !canEnableTun
                ? null
                : (value) async {
                    await context.read<ClashProvider>().setTunMode(value);
                    // TUN 模式切换后手动更新托盘菜单
                    AppTrayManager().updateTrayMenuManually();
                  },
          ),
          // 下方显示状态指示器
          child: _buildStatusIndicator(context, canEnableTun),
        );
      },
    );
  }

  // 根据平台返回不同的权限要求提示
  String _getPlatformRequirementHint(BuildContext context) {
    final trans = context.translate;

    if (Platform.isWindows) {
      return trans.home.tun_requires_windows;
    } else if (Platform.isLinux) {
      return trans.home.tun_requires_linux;
    } else if (Platform.isMacOS) {
      return trans.home.tun_requires_macos;
    }
    return trans.proxy.tun_requires_service;
  }

  // 构建状态指示器
  Widget _buildStatusIndicator(BuildContext context, bool canEnableTun) {
    final trans = context.translate;

    final theme = Theme.of(context);
    final isAvailable = canEnableTun;

    return ModernTooltip(
      message: isAvailable ? '' : _getPlatformRequirementHint(context),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            isAvailable
                ? Icons.check_circle_outline
                : Icons.warning_amber_rounded,
            size: 18,
            color: isAvailable
                ? theme.colorScheme.primary
                : theme.colorScheme.error,
          ),
          const SizedBox(width: 6),
          Text(
            isAvailable
                ? trans.home.tun_status_available
                : trans.home.tun_status_unavailable,
            style: theme.textTheme.bodySmall?.copyWith(
              fontSize: 13,
              color: isAvailable
                  ? theme.colorScheme.primary
                  : theme.colorScheme.error,
            ),
          ),
        ],
      ),
    );
  }

  // 构建不支持平台的内容
  Widget _buildUnsupportedContent(BuildContext context) {
    final trans = context.translate;

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: Theme.of(
          context,
        ).colorScheme.surfaceContainerHighest.withValues(alpha: 0.3),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          Icon(
            Icons.info_outline,
            size: 20,
            color: Theme.of(
              context,
            ).colorScheme.onSurface.withValues(alpha: 0.5),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              trans.home.tun_not_supported,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.6),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
