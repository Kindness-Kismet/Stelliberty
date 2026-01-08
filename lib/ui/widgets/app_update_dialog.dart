import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:gpt_markdown/gpt_markdown.dart';
import 'package:stelliberty/services/app_update_service.dart';
import 'package:stelliberty/providers/app_update_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/bindings.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';

// 应用更新对话框
class AppUpdateDialog extends StatefulWidget {
  final AppUpdateInfo updateInfo;

  const AppUpdateDialog({super.key, required this.updateInfo});

  // 显示更新对话框
  static Future<void> show(BuildContext context, AppUpdateInfo updateInfo) {
    return showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => AppUpdateDialog(updateInfo: updateInfo),
    );
  }

  @override
  State<AppUpdateDialog> createState() => _AppUpdateDialogState();
}

class _AppUpdateDialogState extends State<AppUpdateDialog> {
  final _releaseNotesScrollController = ScrollController();

  @override
  void dispose() {
    _releaseNotesScrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return ModernDialog(
      title: trans.app_update.dialog.title,
      subtitle: trans.app_update.dialog.subtitle,
      titleIcon: Icons.system_update_outlined,
      maxWidth: 560,
      maxHeightRatio: 0.75,
      content: _buildContent(),
      actionsLeft: _buildIgnoreButton(),
      actionsRight: [
        DialogActionButton(
          label: trans.app_update.dialog.cancel_button,
          isPrimary: false,
          onPressed: () => Navigator.of(context).pop(),
        ),
        DialogActionButton(
          label: trans.app_update.dialog.download_button,
          isPrimary: true,
          onPressed: _handleDownload,
        ),
      ],
    );
  }

  // 构建内容区域
  Widget _buildContent() {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          // 版本信息卡片
          _buildVersionCard(),
          const SizedBox(height: 16),

          // 更新说明
          if (widget.updateInfo.releaseNotes != null &&
              widget.updateInfo.releaseNotes!.isNotEmpty) ...[
            _buildReleaseNotes(),
          ],
        ],
      ),
    );
  }

  // 构建版本信息卡片
  Widget _buildVersionCard() {
    final trans = context.translate;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: isDark
            ? Colors.white.withValues(alpha: 0.04)
            : Colors.white.withValues(alpha: 0.5),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
        ),
      ),
      child: Column(
        children: [
          _buildVersionRow(
            icon: Icons.phonelink_setup_outlined,
            label: trans.app_update.dialog.current_version_label,
            version: widget.updateInfo.currentVersion,
            color: Theme.of(
              context,
            ).colorScheme.onSurface.withValues(alpha: 0.7),
          ),
          const SizedBox(height: 12),
          _buildVersionRow(
            icon: Icons.new_releases_outlined,
            label: trans.app_update.dialog.latest_version_label,
            version: widget.updateInfo.latestVersion,
            color: Theme.of(context).colorScheme.primary,
            highlight: true,
          ),
        ],
      ),
    );
  }

  // 构建版本信息行
  Widget _buildVersionRow({
    required IconData icon,
    required String label,
    required String version,
    required Color color,
    bool highlight = false,
  }) {
    return Row(
      children: [
        Icon(icon, size: 18, color: color),
        const SizedBox(width: 8),
        Text(
          label,
          style: TextStyle(
            fontSize: 14,
            color: color,
            fontWeight: highlight ? FontWeight.w600 : FontWeight.normal,
          ),
        ),
        const Spacer(),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
          decoration: BoxDecoration(
            color: color.withValues(alpha: 0.1),
            borderRadius: BorderRadius.circular(8),
          ),
          child: Text(
            version,
            style: TextStyle(
              fontSize: 14,
              fontWeight: FontWeight.w600,
              color: color,
            ),
          ),
        ),
      ],
    );
  }

  // 构建更新说明
  Widget _buildReleaseNotes() {
    final trans = context.translate;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      decoration: BoxDecoration(
        color: isDark
            ? Colors.white.withValues(alpha: 0.04)
            : Colors.white.withValues(alpha: 0.5),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 12),
            child: Row(
              children: [
                Icon(
                  Icons.article_outlined,
                  size: 16,
                  color: Theme.of(context).colorScheme.secondary,
                ),
                const SizedBox(width: 8),
                Text(
                  trans.app_update.dialog.release_notes_label,
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: Theme.of(context).colorScheme.secondary,
                  ),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.only(bottom: 5),
            child: SizedBox(
              height: 200,
              child: Scrollbar(
                controller: _releaseNotesScrollController,
                thumbVisibility: true,
                interactive: true,
                child: SingleChildScrollView(
                  controller: _releaseNotesScrollController,
                  padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
                  child: GptMarkdown(
                    widget.updateInfo.releaseNotes!,
                    style: TextStyle(
                      fontSize: 13,
                      height: 1.5,
                      color: Theme.of(
                        context,
                      ).colorScheme.onSurface.withValues(alpha: 0.8),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  // 构建忽略按钮
  Widget _buildIgnoreButton() {
    final trans = context.translate;
    return TextButton.icon(
      onPressed: _handleIgnore,
      style: TextButton.styleFrom(
        padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 12),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
      ),
      icon: Icon(
        Icons.visibility_off_outlined,
        size: 16,
        color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6),
      ),
      label: Text(
        trans.app_update.dialog.ignore_button,
        style: TextStyle(
          fontSize: 13,
          color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6),
        ),
      ),
    );
  }

  // 处理忽略操作
  void _handleIgnore() {
    // 忽略当前版本
    final provider = context.read<AppUpdateProvider>();
    provider.ignoreCurrentVersion();

    // 关闭对话框
    Navigator.of(context).pop();
  }

  // 处理下载操作
  Future<void> _handleDownload() async {
    final downloadUrl = widget.updateInfo.downloadUrl;

    if (downloadUrl == null || downloadUrl.isEmpty) {
      // 如果没有下载链接，打开 Release 页面
      final htmlUrl = widget.updateInfo.htmlUrl;
      if (htmlUrl != null) {
        await _openUrl(htmlUrl);
      }
    } else {
      // 打开浏览器下载
      await _openUrl(downloadUrl);
    }

    // 关闭对话框
    if (mounted) {
      Navigator.of(context).pop();
    }
  }

  // 打开 URL
  Future<void> _openUrl(String url) async {
    try {
      // 使用 Rust 的 URL 启动器
      OpenUrl(url: url).sendSignalToRust();
    } catch (e) {
      Logger.error('打开 URL 失败: $e');
    }
  }
}
