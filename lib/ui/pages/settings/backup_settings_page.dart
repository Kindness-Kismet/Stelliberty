import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/services/backup_service.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/constants/spacing.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/widgets/confirm_dialog.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/utils/logger.dart';

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
      final result = await FilePicker.platform.saveFile(
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

      ModernToast.show(
        context,
        trans.backup.backup_success,
        type: ToastType.success,
      );

      // 显示安全提示
      if (!mounted) return;
      await showDialog(
        context: context,
        builder: (context) => AlertDialog(
          title: Text(trans.backup.security_warning),
          content: Text(trans.backup.security_warning_message),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(context).pop(),
              child: Text(trans.common.ok),
            ),
          ],
        ),
      );
    } catch (e) {
      Logger.error('创建备份失败：$e');
      if (!mounted) return;
      setState(() => _isCreating = false);

      ModernToast.show(context, _getErrorMessage(e), type: ToastType.error);
    }
  }

  // 还原备份
  Future<void> _restoreBackup() async {
    final trans = context.translate;

    // 选择备份文件
    final result = await FilePicker.platform.pickFiles(
      dialogTitle: trans.backup.select_backup_file,
      type: FileType.custom,
      allowedExtensions: ['stelliberty'],
    );

    if (result == null || result.files.isEmpty) return;

    // 确认对话框
    if (!mounted) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => ConfirmDialog(
        title: trans.backup.restore_confirm,
        message: trans.backup.restore_confirm_message,
      ),
    );

    if (confirmed != true) return;

    setState(() => _isRestoring = true);

    try {
      // 还原备份
      await BackupService.instance.restoreBackup(result.files.first.path!);

      if (!mounted) return;

      // 如果核心正在运行，重启核心以应用新配置
      if (ClashManager.instance.isCoreRunning) {
        Logger.info('备份还原成功，重启核心以应用新配置');
        await ClashManager.instance.restartCore();
      }

      if (!mounted) return;
      setState(() => _isRestoring = false);

      ModernToast.show(
        context,
        trans.backup.restore_success,
        type: ToastType.success,
      );
    } catch (e) {
      Logger.error('还原备份失败：$e');
      if (!mounted) return;
      setState(() => _isRestoring = false);

      ModernToast.show(context, _getErrorMessage(e), type: ToastType.error);
    }
  }

  // 获取友好的错误消息
  String _getErrorMessage(Object error) {
    final trans = context.translate;
    final errorStr = error.toString();
    final t = trans.backup;

    if (errorStr.contains('不存在') || errorStr.contains('not found')) {
      return t.error_file_not_found;
    } else if (errorStr.contains('格式错误') || errorStr.contains('format')) {
      return t.error_invalid_format;
    } else if (errorStr.contains('版本') || errorStr.contains('version')) {
      return t.error_version_mismatch;
    } else if (errorStr.contains('不完整') || errorStr.contains('incomplete')) {
      return t.error_data_incomplete;
    } else {
      return '${t.error_unknown}: $errorStr';
    }
  }
}
