import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/services/backup_service.dart';
import 'package:stelliberty/clash/manager/clash_manager.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/behavior_settings_provider.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/clash/providers/override_provider.dart';
import 'package:stelliberty/providers/app_update_provider.dart';
import 'package:stelliberty/providers/language_provider.dart';
import 'package:stelliberty/providers/theme_provider.dart';
import 'package:stelliberty/providers/window_effect_provider.dart';
import 'package:stelliberty/services/hotkey_service.dart';
import 'package:stelliberty/services/window_state_service.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/constants/spacing.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 备份与还原设置页面
class BackupSettingsPage extends StatefulWidget {
  const BackupSettingsPage({super.key});

  @override
  State<BackupSettingsPage> createState() => _BackupSettingsPageState();
}

class _BackupSettingsPageState extends State<BackupSettingsPage> {
  final _scrollController = ScrollController();
  bool _isCreating = false;
  bool _isRestoring = false;

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final provider = Provider.of<ContentProvider>(context, listen: false);
    final trans = context.translate;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // 标题栏
        Padding(
          padding: const EdgeInsets.all(8.0),
          child: Row(
            children: [
              IconButton(
                icon: const Icon(Icons.arrow_back),
                onPressed: () =>
                    provider.switchView(ContentView.settingsOverview),
              ),
              const SizedBox(width: 8),
              Text(
                trans.backup.title,
                style: Theme.of(context).textTheme.titleLarge,
              ),
            ],
          ),
        ),
        // 内容
        Expanded(
          child: Padding(
            padding: SpacingConstants.scrollbarPadding,
            child: SingleChildScrollView(
              controller: _scrollController,
              physics: const BouncingScrollPhysics(),
              padding: const EdgeInsets.fromLTRB(
                32,
                16,
                32 - SpacingConstants.scrollbarRightCompensation,
                16,
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // 创建备份卡片
                  ModernFeatureLayoutCard(
                    icon: Icons.backup_outlined,
                    title: trans.backup.create_backup,
                    subtitle: trans.backup.description,
                    isHoverEnabled: !_isCreating,
                    isTapEnabled: !_isCreating,
                    onTap: _isCreating ? null : _createBackup,
                  ),
                  const SizedBox(height: 16),
                  // 还原备份卡片
                  ModernFeatureLayoutCard(
                    icon: Icons.restore_outlined,
                    title: trans.backup.restore_backup,
                    subtitle: trans.backup.description,
                    isHoverEnabled: !_isRestoring,
                    isTapEnabled: !_isRestoring,
                    onTap: _isRestoring ? null : _restoreBackup,
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }

  // 创建备份
  Future<void> _createBackup() async {
    final trans = context.translate;
    setState(() => _isCreating = true);

    try {
      // 选择保存位置
      final result = await FilePicker.saveFile(
        dialogTitle: trans.backup.create_backup,
        fileName: BackupService.instance.generateBackupFileName(),
        type: FileType.custom,
        allowedExtensions: ['stelliberty'],
      );

      if (result == null) {
        setState(() => _isCreating = false);
        return;
      }

      // 创建备份
      await BackupService.instance.createBackup(result);

      if (!mounted) return;
      setState(() => _isCreating = false);

      ModernToast.show(trans.backup.backup_success, type: ToastType.success);
    } on BackupException catch (e) {
      Logger.error('创建备份失败：$e');
      if (!mounted) return;
      setState(() => _isCreating = false);

      ModernToast.show(_getErrorMessage(e), type: ToastType.error);
    } catch (e) {
      Logger.error('创建备份失败：$e');
      if (!mounted) return;
      setState(() => _isCreating = false);

      ModernToast.show(
        '${trans.backup.error_unknown}: $e',
        type: ToastType.error,
      );
    }
  }

  // 还原备份
  Future<void> _restoreBackup() async {
    final trans = context.translate;

    // 选择备份文件
    final result = await FilePicker.pickFiles(
      dialogTitle: trans.backup.select_backup_file,
      type: FileType.custom,
      allowedExtensions: ['stelliberty'],
    );

    if (result == null || result.files.isEmpty) return;

    setState(() => _isRestoring = true);

    try {
      // 还原备份
      await BackupService.instance.restoreBackup(result.files.first.path!);

      if (!mounted) return;

      // 重新加载所有数据
      await _reloadAfterRestore();

      if (!mounted) return;
      setState(() => _isRestoring = false);

      ModernToast.show(trans.backup.restore_success, type: ToastType.success);
    } on BackupException catch (e) {
      Logger.error('还原备份失败：$e');
      if (!mounted) return;
      setState(() => _isRestoring = false);

      ModernToast.show(_getErrorMessage(e), type: ToastType.error);
    } catch (e) {
      Logger.error('还原备份失败：$e');
      if (!mounted) return;
      setState(() => _isRestoring = false);

      ModernToast.show(
        '${trans.backup.error_unknown}: $e',
        type: ToastType.error,
      );
    }
  }

  // 还原后重新加载所有数据
  Future<void> _reloadAfterRestore() async {
    Logger.info('备份还原成功，重新加载所有数据');

    final themeProvider = Provider.of<ThemeProvider>(context, listen: false);
    final windowEffectProvider = Provider.of<WindowEffectProvider>(
      context,
      listen: false,
    );
    final languageProvider = Provider.of<LanguageProvider>(
      context,
      listen: false,
    );
    final behaviorSettingsProvider = Provider.of<BehaviorSettingsProvider>(
      context,
      listen: false,
    );
    final appUpdateProvider = Provider.of<AppUpdateProvider>(
      context,
      listen: false,
    );
    final clashProvider = Provider.of<ClashProvider>(context, listen: false);
    final subscriptionProvider = Provider.of<SubscriptionProvider>(
      context,
      listen: false,
    );
    final overrideProvider = Provider.of<OverrideProvider>(
      context,
      listen: false,
    );

    // 重新初始化 Preferences
    await AppPreferences.instance.reload();
    await ClashPreferences.instance.reload();

    if (!mounted) return;

    await themeProvider.initialize();
    await windowEffectProvider.initialize();
    await languageProvider.initialize();
    await behaviorSettingsProvider.applyRestoredSettings();
    await appUpdateProvider.refreshFromPreferences();
    await HotkeyService.instance.refreshFromPreferences();
    WindowStateManager.clearCache();

    clashProvider.refreshConfigState();
    await subscriptionProvider.initialize();
    await overrideProvider.initialize();

    // 如果核心正在运行，重启核心以应用新配置
    if (ClashManager.instance.isCoreRunning) {
      Logger.info('重启核心以应用新配置');
      await ClashManager.instance.restartCore();
    }
  }

  // 获取友好的错误消息
  String _getErrorMessage(BackupException error) {
    final trans = context.translate;
    final t = trans.backup;

    switch (error.type) {
      case BackupErrorType.fileNotFound:
        return t.error_file_not_found;
      case BackupErrorType.invalidFormat:
        return t.error_invalid_format;
      case BackupErrorType.versionMismatch:
        return t.error_version_mismatch;
      case BackupErrorType.dataIncomplete:
        return t.error_data_incomplete;
      case BackupErrorType.operationInProgress:
      case BackupErrorType.timeout:
      case BackupErrorType.unknown:
        return error.message;
    }
  }
}
