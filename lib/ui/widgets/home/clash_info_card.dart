import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/core/service_state.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/clash/services/core_update_service.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/widgets/home/base_card.dart';
import 'package:stelliberty/ui/widgets/home/info_container.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

/// Clash 信息卡片
///
/// 显示核心版本号和代理地址
class ClashInfoCard extends StatefulWidget {
  const ClashInfoCard({super.key});

  @override
  State<ClashInfoCard> createState() => _ClashInfoCardState();
}

class _ClashInfoCardState extends State<ClashInfoCard> {
  bool _isUpdating = false;
  // ignore: prefer_final_fields
  bool _isRestarting = false;

  @override
  Widget build(BuildContext context) {
    final clashManager = context.watch<ClashManager>();
    final serviceStateManager = context.watch<ServiceStateManager>();

    final runMode = _determineRunMode(context, serviceStateManager);
    final isRunning = clashManager.isRunning;
    final mixedPort = clashManager.mixedPort;
    final proxyHost = ClashPreferences.instance.getProxyHost();
    final proxyAddress = '$proxyHost:$mixedPort';
    final version = clashManager.version;

    return BaseCard(
      icon: Icons.info_outline,
      title: context.translate.home.clashInfo,
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          // 更新核心按钮
          ModernTooltip(
            message: context.translate.home.updateCore,
            child: IconButton(
              icon: _isUpdating
                  ? SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: Theme.of(context).colorScheme.primary,
                      ),
                    )
                  : const Icon(Icons.system_update_alt, size: 18),
              onPressed: (_isUpdating || _isRestarting)
                  ? null
                  : () => _updateCore(context),
              style: IconButton.styleFrom(
                minimumSize: const Size(32, 32),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
            ),
          ),
          const SizedBox(width: 4),
          // 重启核心按钮
          ModernTooltip(
            message: context.translate.proxy.restartCore,
            child: IconButton(
              icon: _isRestarting
                  ? SizedBox(
                      width: 18,
                      height: 18,
                      child: CircularProgressIndicator(
                        strokeWidth: 2,
                        color: Theme.of(context).colorScheme.primary,
                      ),
                    )
                  : const Icon(Icons.restart_alt, size: 18),
              onPressed: (isRunning && !_isUpdating && !_isRestarting)
                  ? () => _restartCore(context)
                  : null,
              style: IconButton.styleFrom(
                minimumSize: const Size(32, 32),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
            ),
          ),
        ],
      ),
      child: InfoContainer(
        rows: [
          // 运行模式
          InfoRow.text(
            label: context.translate.home.coreRunMode,
            value: runMode,
          ),
          // 代理地址
          InfoRow.text(
            label: context.translate.home.proxyAddress,
            value: proxyAddress,
            valueStyle: Theme.of(context).textTheme.bodyMedium?.copyWith(
              fontWeight: FontWeight.w600,
              fontFeatures: [const FontFeature.tabularFigures()],
              color: isRunning
                  ? Theme.of(context).colorScheme.onSurface
                  : Theme.of(
                      context,
                    ).colorScheme.onSurface.withValues(alpha: 0.4),
            ),
          ),
          // 核心版本
          InfoRow.text(
            label: context.translate.home.coreVersion,
            value: isRunning ? version : '--',
            valueStyle: Theme.of(context).textTheme.bodyMedium?.copyWith(
              fontWeight: FontWeight.w600,
              fontFeatures: [const FontFeature.tabularFigures()],
              color: isRunning
                  ? Theme.of(context).colorScheme.onSurface
                  : Theme.of(
                      context,
                    ).colorScheme.onSurface.withValues(alpha: 0.4),
            ),
          ),
        ],
      ),
    );
  }

  // 更新核心
  Future<void> _updateCore(BuildContext context) async {
    if (_isUpdating) return;

    final clashProvider = context.read<ClashProvider>();
    final clashManager = context.read<ClashManager>();

    setState(() {
      _isUpdating = true;
    });

    Logger.info('开始更新 Clash 核心');

    // 显示开始更新提示
    if (context.mounted) {
      ModernToast.info(context, context.translate.home.updatingCore);
    }

    // 记录核心状态（用于更新后恢复）
    final wasRunning = clashManager.isRunning;
    final currentConfigPath = clashManager.currentConfigPath;

    try {
      // 1. 获取当前版本和最新版本
      Logger.info('检查核心版本');
      final currentVersion = await CoreUpdateService.getCurrentCoreVersion();

      // 2. 获取最新版本信息（不下载完整文件）
      final releaseInfo = await CoreUpdateService.getLatestRelease();
      final latestVersion = (releaseInfo['tag_name'] as String).replaceFirst(
        'v',
        '',
      );

      Logger.info('当前版本: ${currentVersion ?? "未知"}, 最新版本: $latestVersion');

      // 3. 版本比较
      if (currentVersion != null) {
        final comparison = CoreUpdateService.compareVersions(
          currentVersion,
          latestVersion,
        );
        if (comparison >= 0) {
          // 当前版本已是最新或更新
          Logger.info('核心已是最新版本: $currentVersion');
          if (context.mounted) {
            ModernToast.info(context, context.translate.home.coreAlreadyLatest);
          }
          return;
        }
      }

      // 4. 下载核心到内存（不影响当前运行的核心）
      Logger.info('开始下载核心文件');
      final (version, coreBytes) = await CoreUpdateService.downloadCore();

      Logger.info('核心下载成功: $version，准备替换');

      // 5. 下载成功后，停止核心（如果正在运行）
      if (wasRunning) {
        Logger.info('停止核心以便更新');
        await clashProvider.stop();
        await Future.delayed(const Duration(milliseconds: 500));
      }

      // 6. 获取核心目录并替换文件
      final coreDir = await CoreUpdateService.getCoreDirectory();
      await CoreUpdateService.replaceCore(
        coreDir: coreDir,
        coreBytes: coreBytes,
      );

      Logger.info('核心文件替换成功');

      // 7. 如果之前在运行，重新启动核心
      if (wasRunning && currentConfigPath != null) {
        Logger.info('重新启动核心');
        await Future.delayed(const Duration(milliseconds: 500));
        await clashProvider.start(configPath: currentConfigPath);
      }

      // 8. 删除备份的旧核心
      await CoreUpdateService.deleteOldCore(coreDir);

      // 9. 显示成功消息
      if (context.mounted) {
        ModernToast.success(
          context,
          context.translate.home.coreUpdatedTo.replaceAll('{version}', version),
        );
      }
    } catch (e) {
      Logger.error('核心更新失败: $e');

      // 只有在文件替换阶段失败（核心已停止但更新未完成）时才需要重启
      // 下载阶段失败时核心从未停止，无需重启
      if (wasRunning && !clashManager.isRunning && currentConfigPath != null) {
        try {
          Logger.info('文件替换失败，重新启动旧核心');
          await Future.delayed(const Duration(milliseconds: 500));
          await clashProvider.start(configPath: currentConfigPath);
        } catch (restartError) {
          Logger.error('重启核心失败: $restartError');
        }
      }

      if (context.mounted) {
        ModernToast.error(
          context,
          context.translate.home.coreUpdateError.replaceAll(
            '{error}',
            e.toString(),
          ),
        );
      }
    } finally {
      if (mounted) {
        setState(() {
          _isUpdating = false;
        });
      }
    }
  }

  Future<void> _restartCore(BuildContext context) async {
    if (_isRestarting) return;

    setState(() {
      _isRestarting = true;
    });

    final clashProvider = context.read<ClashProvider>();
    Logger.info('用户点击重启核心按钮');

    try {
      await clashProvider.stop();
      await Future.delayed(const Duration(milliseconds: 300));
      // ⚠️ 修复：不传入 configPath，让 LifecycleManager 使用保存的原始订阅路径
      // 这样可以确保重启时使用干净的原始配置，而不是已包含覆写的 runtime_config.yaml
      await clashProvider.start();

      // 显示成功提示
      if (context.mounted) {
        ModernToast.success(context, context.translate.proxy.coreRestarted);
      }
    } catch (e) {
      Logger.error('重启核心失败: $e');

      // 显示错误提示
      if (context.mounted) {
        ModernToast.error(
          context,
          context.translate.proxy.restartFailedWithError.replaceAll(
            '{error}',
            e.toString(),
          ),
        );
      }
    } finally {
      if (mounted) {
        setState(() {
          _isRestarting = false;
        });
      }
    }
  }

  String _determineRunMode(
    BuildContext context,
    ServiceStateManager serviceStateManager,
  ) {
    final isServiceInstalled = serviceStateManager.isInstalled;

    // 只要服务已安装，就显示服务模式（无论核心是否运行）
    if (isServiceInstalled) {
      return context.translate.home.serviceMode;
    }

    // 服务未安装，使用普通模式
    return context.translate.home.normalMode;
  }
}
