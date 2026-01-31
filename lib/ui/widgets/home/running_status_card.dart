import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/atomic/platform_helper.dart';
import 'package:stelliberty/clash/model/traffic_data_model.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/resource_usage_provider.dart';
import 'package:stelliberty/clash/providers/service_provider.dart';
import 'package:stelliberty/clash/services/core_update_service.dart';
import 'package:stelliberty/clash/state/service_states.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/ui/widgets/home/base_card.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

class RunningStatusCard extends StatefulWidget {
  const RunningStatusCard({super.key});

  @override
  State<RunningStatusCard> createState() => _RunningStatusCardState();
}

class _RunningStatusCardState extends State<RunningStatusCard> {
  static const double _metricDividerWidth = 1;
  static const double _metricDividerMargin = 16;
  static const double _metricDividerSpace =
      _metricDividerWidth + _metricDividerMargin * 2;

  Timer? _uptimeRefreshTimer;
  bool _isCoreUpdating = false;
  bool _isCoreRestarting = false;

  @override
  void dispose() {
    _uptimeRefreshTimer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;
    final colorScheme = Theme.of(context).colorScheme;
    final isMobilePlatform = PlatformHelper.isMobile;
    final desktopCoreRunning = context.select<ClashProvider, bool>(
      (provider) => provider.isCoreRunning,
    );
    final mobileCoreRunning = Platform.isAndroid
        ? context.select<ClashProvider, bool>(
            (provider) => provider.isAndroidCoreRunning,
          )
        : false;
    final isCoreRunning = isMobilePlatform
        ? mobileCoreRunning
        : desktopCoreRunning;
    final isCoreRestarting = isMobilePlatform
        ? false
        : context.select<ClashProvider, bool>(
            (provider) => provider.isCoreRestarting,
          );
    final coreStartedAt = isMobilePlatform
        ? Platform.isAndroid
              ? context.select<ClashProvider, DateTime?>(
                  (provider) => provider.androidCoreStartedAt,
                )
              : null
        : context.select<ClashProvider, DateTime?>(
            (provider) => provider.coreStartedAt,
          );
    final appMemoryBytes = context.select<ResourceUsageProvider, int?>(
      (provider) => provider.appMemoryBytes,
    );
    final coreMemoryBytes = context.select<ResourceUsageProvider, int?>(
      (provider) => provider.coreMemoryBytes,
    );
    final coreVersion = context.select<ClashProvider, String>(
      (provider) => provider.coreVersion,
    );
    final mixedPort = context.select<ClashProvider, int>(
      (provider) => provider.configState.mixedPort,
    );
    final serviceState = context.select<ServiceProvider, ServiceState>(
      (provider) => provider.serviceState,
    );

    _syncTimer(isCoreRunning);

    final uptime = _resolveUptime(isCoreRunning, coreStartedAt);
    final uptimeDisplay = uptime == null ? '--' : _formatUptime(uptime);
    final combinedMemory = _formatMemoryText(appMemoryBytes, coreMemoryBytes);
    final runMode = _resolveRunModeText(serviceState, trans);
    final platformName = Platform.operatingSystem;
    final platform = _formatPlatformName(platformName);
    final platformIcon = _resolvePlatformIcon(platformName);

    // 核心状态文字和颜色
    final String coreStatus;
    final Color coreStatusColor;
    if (isCoreRestarting) {
      coreStatus = trans.home.core_status_restarting;
      coreStatusColor = Colors.orange;
    } else if (isCoreRunning) {
      coreStatus = trans.home.core_status_running;
      coreStatusColor = Colors.green;
    } else {
      coreStatus = trans.home.core_status_stopped;
      coreStatusColor = colorScheme.onSurface.withValues(alpha: 0.5);
    }

    final proxyHost = ClashPreferences.instance.getProxyHost();
    final proxyAddress = '$proxyHost:$mixedPort';
    final proxyAddressColor = isCoreRunning
        ? colorScheme.onSurface
        : colorScheme.onSurface.withValues(alpha: 0.4);

    return BaseCard(
      icon: Icons.monitor_heart_outlined,
      title: trans.home.running_status,
      trailing: isMobilePlatform
          ? null
          : _buildHeaderActions(context, isCoreRunning: desktopCoreRunning),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _buildMetricSpanRow(
            context,
            left: _buildMetric(
              context,
              label: trans.home.running_status_uptime,
              value: uptimeDisplay,
              color: colorScheme.primary,
              icon: Icons.timer_outlined,
              iconColor: colorScheme.primary,
            ),
            right: _buildMetric(
              context,
              label: trans.home.memory_usage,
              value: combinedMemory,
              color: colorScheme.secondary,
              icon: Icons.memory,
              iconColor: colorScheme.secondary,
            ),
          ),
          const SizedBox(height: 16),
          _buildMetricRow(
            context,
            items: [
              _buildMetric(
                context,
                label: trans.home.running_status_platform,
                value: platform,
                color: colorScheme.onSurface,
                icon: platformIcon,
                iconColor: colorScheme.tertiary,
              ),
              _buildMetric(
                context,
                label: trans.home.core_version,
                value: coreVersion,
                color: colorScheme.onSurface,
                icon: Icons.code,
                iconColor: colorScheme.primary,
              ),
              _buildMetric(
                context,
                label: trans.home.core_run_mode,
                value: runMode,
                color: colorScheme.onSurface,
                icon: Icons.settings_suggest,
                iconColor: colorScheme.secondary,
              ),
            ],
          ),
          const SizedBox(height: 16),
          _buildMetricSpanRow(
            context,
            left: _buildMetric(
              context,
              label: trans.home.core_status,
              value: coreStatus,
              color: coreStatusColor,
              icon: Icons.circle,
              iconColor: coreStatusColor,
            ),
            right: _buildMetric(
              context,
              label: trans.home.proxy_address,
              value: proxyAddress,
              color: proxyAddressColor,
              icon: Icons.lan,
              iconColor: colorScheme.primary,
            ),
          ),
        ],
      ),
    );
  }

  void _syncTimer(bool isCoreRunning) {
    if (!isCoreRunning) {
      _uptimeRefreshTimer?.cancel();
      _uptimeRefreshTimer = null;
      return;
    }

    if (_uptimeRefreshTimer != null && _uptimeRefreshTimer!.isActive) {
      return;
    }

    _uptimeRefreshTimer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) {
        setState(() {});
      }
    });
  }

  Duration? _resolveUptime(bool isCoreRunning, DateTime? coreStartedAt) {
    if (!isCoreRunning || coreStartedAt == null) {
      return null;
    }

    final duration = DateTime.now().difference(coreStartedAt);
    if (duration.isNegative) {
      return null;
    }

    return duration;
  }

  String _formatUptime(Duration duration) {
    final hours = duration.inHours;
    final minutes = duration.inMinutes.remainder(60);
    final seconds = duration.inSeconds.remainder(60);

    if (hours > 0) {
      return '$hours:${minutes.toString().padLeft(2, '0')}:${seconds.toString().padLeft(2, '0')}';
    }

    return '${minutes.toString()}:${seconds.toString().padLeft(2, '0')}';
  }

  String _formatMemoryText(int? appMemoryBytes, int? coreMemoryBytes) {
    final appText = appMemoryBytes == null
        ? '--'
        : TrafficData.formatBytes(appMemoryBytes);
    final coreText = coreMemoryBytes == null
        ? '--'
        : TrafficData.formatBytes(coreMemoryBytes);
    return '$appText + $coreText';
  }

  String _resolveRunModeText(ServiceState serviceState, Translations trans) {
    if (serviceState.isServiceModeInstalled) {
      return trans.home.service_mode;
    }

    return trans.home.normal_mode;
  }

  String _formatPlatformName(String rawName) {
    switch (rawName.toLowerCase()) {
      case 'windows':
        return 'Windows';
      case 'macos':
        return 'macOS';
      case 'linux':
        return 'Linux';
      case 'android':
        return 'Android';
      case 'ios':
        return 'iOS';
      default:
        if (rawName.isEmpty) {
          return 'Unknown';
        }
        return rawName[0].toUpperCase() + rawName.substring(1);
    }
  }

  IconData _resolvePlatformIcon(String rawName) {
    switch (rawName.toLowerCase()) {
      case 'windows':
        return Icons.desktop_windows;
      case 'macos':
        return Icons.laptop_mac;
      case 'linux':
        return Icons.laptop;
      case 'android':
        return Icons.android;
      case 'ios':
        return Icons.phone_iphone;
      default:
        return Icons.devices;
    }
  }

  Widget _buildHeaderActions(
    BuildContext context, {
    required bool isCoreRunning,
  }) {
    final trans = context.translate;
    final canUpdate = !_isCoreUpdating && !_isCoreRestarting;
    final canRestart = isCoreRunning && !_isCoreUpdating && !_isCoreRestarting;
    final actionColor = Theme.of(context).colorScheme.primary;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        ModernTooltip(
          message: trans.home.update_core,
          child: _HeaderActionButton(
            icon: Icons.system_update_alt,
            color: actionColor,
            isBusy: _isCoreUpdating,
            onPressed: canUpdate ? () => _updateCore(context) : null,
          ),
        ),
        const SizedBox(width: 8),
        ModernTooltip(
          message: trans.proxy.restart_core,
          child: _HeaderActionButton(
            icon: Icons.restart_alt,
            color: actionColor,
            isBusy: _isCoreRestarting,
            onPressed: canRestart ? () => _restartCore(context) : null,
          ),
        ),
      ],
    );
  }

  Widget _buildMetricRow(
    BuildContext context, {
    required List<Widget> items,
    List<bool>? dividerVisibility,
  }) {
    final dividerColor = Theme.of(
      context,
    ).colorScheme.outline.withValues(alpha: 0.2);
    final children = <Widget>[];

    for (int i = 0; i < items.length; i++) {
      children.add(Expanded(child: items[i]));
      if (i < items.length - 1) {
        final shouldShowDivider = dividerVisibility == null
            ? true
            : (dividerVisibility.length > i ? dividerVisibility[i] : true);
        final divider = _buildVerticalDivider(dividerColor);
        children.add(
          Visibility(
            visible: shouldShowDivider,
            maintainSize: true,
            maintainAnimation: true,
            maintainState: true,
            child: divider,
          ),
        );
      }
    }

    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: children,
      ),
    );
  }

  Widget _buildVerticalDivider(Color color) {
    return Container(
      width: _metricDividerWidth,
      margin: const EdgeInsets.symmetric(horizontal: _metricDividerMargin),
      color: color,
    );
  }

  Widget _buildMetricSpanRow(
    BuildContext context, {
    required Widget left,
    required Widget right,
  }) {
    final dividerColor = Theme.of(
      context,
    ).colorScheme.outline.withValues(alpha: 0.2);

    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(flex: 1, child: left),
          _buildVerticalDivider(dividerColor),
          Expanded(flex: 2, child: right),
          SizedBox(width: _metricDividerSpace),
        ],
      ),
    );
  }

  Widget _buildMetric(
    BuildContext context, {
    required String label,
    required String value,
    required Color color,
    IconData? icon,
    Color? iconColor,
  }) {
    final textTheme = Theme.of(context).textTheme;
    final valueStyle = textTheme.bodyMedium;
    final labelColor = Theme.of(
      context,
    ).colorScheme.onSurface.withValues(alpha: 0.6);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            if (icon != null) ...[
              Icon(icon, size: 12, color: iconColor ?? labelColor),
              const SizedBox(width: 6),
            ],
            Expanded(
              child: Text(
                label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: textTheme.bodySmall?.copyWith(
                  color: labelColor,
                  fontSize: 11,
                  fontWeight: FontWeight.w500,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 8),
        Text(
          value,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: valueStyle?.copyWith(
            color: color,
            fontWeight: FontWeight.w600,
            fontFeatures: [const FontFeature.tabularFigures()],
          ),
        ),
      ],
    );
  }

  Future<void> _updateCore(BuildContext context) async {
    final trans = context.translate;

    if (_isCoreUpdating) return;

    final clashProvider = context.read<ClashProvider>();

    setState(() {
      _isCoreUpdating = true;
    });

    Logger.info('开始更新 Clash 核心');

    if (context.mounted) {
      ModernToast.info(trans.home.updating_core);
    }

    final wasRunning = clashProvider.isCoreRunning;
    final currentConfigPath = clashProvider.currentConfigPath;

    try {
      Logger.info('检查核心版本');
      final currentVersion = await CoreUpdateService.getCurrentCoreVersion();

      final latestVersionTag = await CoreUpdateService.getLatestRelease();
      final latestVersion = latestVersionTag.replaceFirst('v', '');

      Logger.info('当前版本: ${currentVersion ?? "未知"}, 最新版本: $latestVersion');

      if (currentVersion != null) {
        final comparison = CoreUpdateService.compareVersions(
          currentVersion,
          latestVersion,
        );
        if (comparison >= 0) {
          Logger.info('核心已是最新版本: $currentVersion');
          if (context.mounted) {
            ModernToast.info(trans.home.core_already_latest);
          }
          return;
        }
      }

      Logger.info('开始下载核心文件');
      final (version, coreBytes) = await CoreUpdateService.downloadCore();

      Logger.info('核心下载成功: $version，准备替换');

      if (wasRunning) {
        Logger.info('停止核心以便更新');
        await clashProvider.stop();
        await Future.delayed(const Duration(milliseconds: 500));
      }

      final coreDir = await CoreUpdateService.getCoreDirectory();
      await CoreUpdateService.replaceCore(
        coreDir: coreDir,
        coreBytes: coreBytes,
      );

      Logger.info('核心文件替换成功');

      if (wasRunning && currentConfigPath != null) {
        Logger.info('重新启动核心');
        await Future.delayed(const Duration(milliseconds: 500));
        await clashProvider.start(configPath: currentConfigPath);
      }

      await CoreUpdateService.deleteOldCore(coreDir);

      if (context.mounted) {
        ModernToast.success(
          trans.home.core_updated_to.replaceAll('{version}', version),
        );
      }
    } catch (e) {
      Logger.error('核心更新失败: $e');

      if (wasRunning &&
          !clashProvider.isCoreRunning &&
          currentConfigPath != null) {
        try {
          Logger.info('文件替换失败，重新启动旧核心');
          await Future.delayed(const Duration(milliseconds: 500));
          await clashProvider.start(configPath: currentConfigPath);
        } catch (e) {
          Logger.error('重启核心失败: $e');
        }
      }

      if (context.mounted) {
        ModernToast.error(
          trans.home.core_update_error.replaceAll('{error}', e.toString()),
        );
      }
    } finally {
      if (mounted) {
        setState(() {
          _isCoreUpdating = false;
        });
      }
    }
  }

  Future<void> _restartCore(BuildContext context) async {
    final trans = context.translate;

    if (_isCoreRestarting) return;

    setState(() {
      _isCoreRestarting = true;
    });

    final clashProvider = context.read<ClashProvider>();
    Logger.info('用户点击重启核心按钮');

    try {
      await clashProvider.restart();

      if (context.mounted) {
        ModernToast.success(trans.proxy.core_restarted);
      }
    } catch (e) {
      Logger.error('重启核心失败: $e');

      if (context.mounted) {
        ModernToast.error(
          trans.proxy.restart_failed_with_error.replaceAll(
            '{error}',
            e.toString(),
          ),
        );
      }
    } finally {
      if (mounted) {
        setState(() {
          _isCoreRestarting = false;
        });
      }
    }
  }
}

class _HeaderActionButton extends StatefulWidget {
  final IconData icon;
  final Color color;
  final bool isBusy;
  final VoidCallback? onPressed;

  const _HeaderActionButton({
    required this.icon,
    required this.color,
    required this.isBusy,
    required this.onPressed,
  });

  @override
  State<_HeaderActionButton> createState() => _HeaderActionButtonState();
}

class _HeaderActionButtonState extends State<_HeaderActionButton> {
  bool _isHovered = false;

  @override
  Widget build(BuildContext context) {
    final colorScheme = Theme.of(context).colorScheme;
    final isEnabled = widget.onPressed != null && !widget.isBusy;
    final iconColor = isEnabled
        ? widget.color
        : colorScheme.onSurface.withValues(alpha: 0.4);

    return MouseRegion(
      onEnter: (_) => setState(() => _isHovered = true),
      onExit: (_) => setState(() => _isHovered = false),
      child: InkWell(
        onTap: isEnabled ? widget.onPressed : null,
        borderRadius: BorderRadius.circular(8),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 200),
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
          decoration: BoxDecoration(
            color: _isHovered && isEnabled
                ? colorScheme.surfaceContainerHighest.withValues(alpha: 0.5)
                : colorScheme.surfaceContainerHighest.withValues(alpha: 0.3),
            borderRadius: BorderRadius.circular(8),
            border: Border.all(
              color: _isHovered && isEnabled
                  ? colorScheme.outline.withValues(alpha: 0.3)
                  : colorScheme.outline.withValues(alpha: 0.2),
            ),
          ),
          child: SizedBox(
            width: 16,
            height: 16,
            child: widget.isBusy
                ? CircularProgressIndicator(strokeWidth: 2, color: iconColor)
                : Icon(widget.icon, size: 16, color: iconColor),
          ),
        ),
      ),
    );
  }
}
