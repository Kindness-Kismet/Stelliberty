import 'package:flutter/material.dart';
import 'package:stelliberty/clash/model/subscription_model.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/option_selector.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/text_input_field.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/file_selector.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/proxy_mode_selector.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/auto_update_mode_selector.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 对话框间距常量
const double _dialogContentPadding = 20.0;
const double _dialogItemSpacing = 20.0;

// 订阅导入方式枚举
enum SubscriptionImportMethod {
  // 链接导入（远程订阅）
  link,

  // 本地文件导入
  localFile,
}

// 订阅对话框 - 支持添加和编辑两种模式
// 添加模式可选链接或本地文件导入，编辑模式修改现有配置
class SubscriptionDialog extends StatefulWidget {
  final String title;
  final String? initialName;
  final String? initialUrl;
  final AutoUpdateMode? initialAutoUpdateMode;
  final int? initialIntervalMinutes;
  final bool? initialUpdateOnStartup;
  final SubscriptionProxyMode? initialProxyMode;
  final String? initialUserAgent;
  final String confirmText;
  final IconData titleIcon;
  final bool isAddMode;
  final bool isLocalFile;
  final Future<bool> Function(SubscriptionDialogResult)? onConfirm;

  const SubscriptionDialog({
    super.key,
    required this.title,
    this.initialName,
    this.initialUrl,
    this.initialAutoUpdateMode,
    this.initialIntervalMinutes,
    this.initialUpdateOnStartup,
    this.initialProxyMode,
    this.initialUserAgent,
    this.confirmText = 'Confirm',
    this.titleIcon = Icons.rss_feed,
    this.isAddMode = false,
    this.isLocalFile = false,
    this.onConfirm,
  });

  // 显示添加配置对话框
  static Future<void> showAddDialog(
    BuildContext context, {
    required Future<bool> Function(SubscriptionDialogResult) onConfirm,
  }) {
    final trans = context.translate;

    return showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => SubscriptionDialog(
        title: trans.subscription_dialog.add_title,
        confirmText: trans.subscription_dialog.add_button,
        titleIcon: Icons.add_circle_outline,
        isAddMode: true, // 标记为添加模式
        onConfirm: onConfirm,
      ),
    );
  }

  // 显示编辑订阅对话框
  static Future<SubscriptionDialogResult?> showEditDialog(
    BuildContext context,
    Subscription subscription,
  ) {
    final trans = context.translate;

    return showDialog<SubscriptionDialogResult>(
      context: context,
      barrierDismissible: false,
      builder: (context) => SubscriptionDialog(
        title: trans.subscription_dialog.edit_title,
        initialName: subscription.name,
        initialUrl: subscription.url,
        initialAutoUpdateMode: subscription.autoUpdateMode,
        initialIntervalMinutes: subscription.intervalMinutes,
        initialUpdateOnStartup: subscription.shouldUpdateOnStartup,
        initialProxyMode: subscription.proxyMode,
        initialUserAgent: subscription.userAgent,
        confirmText: trans.subscription_dialog.save_button,
        titleIcon: Icons.edit_outlined,
        isLocalFile: subscription.isLocalFile,
      ),
    );
  }

  @override
  State<SubscriptionDialog> createState() => _SubscriptionDialogState();
}

class _SubscriptionDialogState extends State<SubscriptionDialog> {
  late final TextEditingController _nameController;
  late final TextEditingController _urlController;
  late final TextEditingController _intervalController;
  late final TextEditingController _userAgentController;
  late AutoUpdateMode _autoUpdateMode;
  late SubscriptionProxyMode _proxyMode;

  // 缓存的全局默认 UA，避免重复调用
  late final String _defaultUserAgent;

  // 导入方式选择
  SubscriptionImportMethod _importMethod = SubscriptionImportMethod.link;

  // 选中的文件信息
  FileSelectionResult? _selectedFile;

  final _formKey = GlobalKey<FormState>();
  bool _isLoading = false;

  // 重建延迟标志，避免输入时频繁重建
  bool _needsRebuild = false;

  @override
  void initState() {
    super.initState();

    // 缓存全局默认 UA，避免重复调用
    _defaultUserAgent = ClashPreferences.instance.getDefaultUserAgent();

    // 初始化控制器
    _nameController = TextEditingController(text: widget.initialName ?? '');
    _urlController = TextEditingController(text: widget.initialUrl ?? '');
    _intervalController = TextEditingController(
      text: (widget.initialIntervalMinutes ?? 60).toString(),
    );
    // 编辑模式：使用订阅的 UA；添加模式：留空（使用 placeholder 显示默认值）
    _userAgentController = TextEditingController(
      text: widget.initialUserAgent ?? '',
    );

    // 初始化自动更新模式和代理模式
    _autoUpdateMode = widget.initialAutoUpdateMode ?? AutoUpdateMode.disabled;
    _proxyMode = widget.initialProxyMode ?? SubscriptionProxyMode.direct;

    // 添加监听器以检测内容变化
    _nameController.addListener(_checkForChanges);
    _urlController.addListener(_checkForChanges);
    _intervalController.addListener(_checkForChanges);
    _userAgentController.addListener(_checkForChanges);
  }

  // 检查内容是否发生变化
  bool get _hasChanges {
    if (widget.isAddMode) return true;

    if (_nameController.text.trim() != (widget.initialName ?? '')) return true;

    if (!widget.isLocalFile) {
      if (_urlController.text.trim() != (widget.initialUrl ?? '')) return true;

      if (_autoUpdateMode !=
          (widget.initialAutoUpdateMode ?? AutoUpdateMode.disabled)) {
        return true;
      }

      if (_autoUpdateMode == AutoUpdateMode.interval &&
          int.tryParse(_intervalController.text.trim()) !=
              (widget.initialIntervalMinutes ?? 60)) {
        return true;
      }

      if (_proxyMode !=
          (widget.initialProxyMode ?? SubscriptionProxyMode.direct)) {
        return true;
      }

      // 比较 UA 变化：空值视为默认值
      final currentUA = _userAgentController.text.trim();
      final initialUA = widget.initialUserAgent ?? '';
      if (currentUA != initialUA) {
        return true;
      }
    }

    return false;
  }

  // 内容变化时标记需要重建，延迟到下一帧执行
  void _checkForChanges() {
    if (!_needsRebuild) {
      _needsRebuild = true;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted && _needsRebuild) {
          setState(() {
            _needsRebuild = false;
          });
        }
      });
    }
  }

  @override
  void dispose() {
    // 移除监听器
    _nameController.removeListener(_checkForChanges);
    _urlController.removeListener(_checkForChanges);
    _intervalController.removeListener(_checkForChanges);
    _userAgentController.removeListener(_checkForChanges);
    // 释放控制器
    _nameController.dispose();
    _urlController.dispose();
    _intervalController.dispose();
    _userAgentController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return ModernDialog(
      title: widget.title,
      titleIcon: widget.titleIcon,
      isModified: !widget.isAddMode && _hasChanges,
      maxWidth: 720,
      maxHeightRatio: 0.85,
      content: _buildContent(),
      actionsLeft: widget.isAddMode
          ? Text(
              trans.subscription_dialog.add_mode_hint,
              style: TextStyle(
                fontSize: 12,
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.6),
              ),
            )
          : Text(
              trans.subscription_dialog.edit_mode_hint,
              style: TextStyle(
                fontSize: 12,
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.6),
              ),
            ),
      actionsRight: [
        DialogActionButton(
          label: trans.subscription_dialog.cancel_button,
          isPrimary: false,
          onPressed: _isLoading ? null : () => Navigator.of(context).pop(),
        ),
        DialogActionButton(
          label: widget.confirmText,
          isPrimary: true,
          isLoading: _isLoading,
          onPressed: (_isLoading || !_hasChanges) ? null : _handleConfirm,
        ),
      ],
      onClose: _isLoading ? null : () => Navigator.of(context).pop(),
    );
  }

  Widget _buildContent() {
    final trans = context.translate;

    return SingleChildScrollView(
      padding: const EdgeInsets.all(_dialogContentPadding),
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            // 如果是添加模式，显示导入方式选择
            if (widget.isAddMode) ...[
              _buildImportModeSelector(),
              const SizedBox(height: _dialogItemSpacing),
            ],

            TextInputField(
              controller: _nameController,
              label: trans.subscription_dialog.config_name_label,
              hint: trans.subscription_dialog.config_name_hint,
              icon: Icons.label_outline,
              validator: (value) {
                if (value == null || value.trim().isEmpty) {
                  return trans.subscription_dialog.config_name_error;
                }
                return null;
              },
            ),

            // 根据导入方式选择输入控件。
            // 编辑本地文件订阅时隐藏 URL 字段。
            if (widget.isAddMode &&
                    _importMethod == SubscriptionImportMethod.link ||
                !widget.isAddMode && !widget.isLocalFile) ...[
              const SizedBox(height: _dialogItemSpacing),
              TextInputField(
                controller: _urlController,
                label: trans.subscription_dialog.subscription_link_label,
                hint: trans.subscription_dialog.subscription_link_hint,
                icon: Icons.link,
                minLines: 1,
                maxLines: null,
                validator: (value) {
                  if (value == null || value.trim().isEmpty) {
                    return trans.subscription_dialog.link_error;
                  }

                  final uri = Uri.tryParse(value.trim());
                  if (uri == null) {
                    return trans.subscription_dialog.link_format_error;
                  }

                  if (uri.scheme != 'http' && uri.scheme != 'https') {
                    return context
                        .translate
                        .subscription_dialog
                        .link_protocol_error;
                  }

                  if (uri.host.isEmpty) {
                    return trans.subscription_dialog.link_missing_host;
                  }

                  // 验证域名格式：必须包含点，或者是 localhost/IP
                  final host = uri.host.toLowerCase();
                  if (host != 'localhost' &&
                      host != '127.0.0.1' &&
                      !host.contains('.')) {
                    return context
                        .translate
                        .subscription_dialog
                        .link_host_format_error;
                  }

                  if (host.length < 3) {
                    return context
                        .translate
                        .subscription_dialog
                        .link_host_too_short;
                  }

                  return null;
                },
              ),
              const SizedBox(height: _dialogItemSpacing),
              _buildUserAgentField(),
            ] else if (widget.isAddMode &&
                _importMethod == SubscriptionImportMethod.localFile) ...[
              const SizedBox(height: _dialogItemSpacing),
              _buildFileSelector(),
            ],

            // 自动更新仅在链接导入场景显示。
            // 编辑模式下，本地文件订阅不显示该区域。
            if ((widget.isAddMode &&
                    _importMethod == SubscriptionImportMethod.link) ||
                (!widget.isAddMode && !widget.isLocalFile)) ...[
              const SizedBox(height: _dialogItemSpacing),
              _buildAutoUpdateSection(),
              const SizedBox(height: _dialogItemSpacing),
              _buildProxyModeSection(),
            ],
          ],
        ),
      ),
    );
  }

  Widget _buildAutoUpdateSection() {
    final dialogTrans = context.translate.subscription_dialog;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // 自动更新模式选择器
        AutoUpdateModeSelector(
          selectedValue: _autoUpdateMode,
          onChanged: (value) {
            setState(() => _autoUpdateMode = value);
          },
        ),

        // 间隔更新配置（当选择间隔更新时展开）
        if (_autoUpdateMode == AutoUpdateMode.interval) ...[
          const SizedBox(height: 16),
          TextInputField(
            controller: _intervalController,
            label: dialogTrans.update_interval_label,
            hint: dialogTrans.update_interval_hint,
            icon: Icons.schedule,
            validator: (value) {
              if (_autoUpdateMode == AutoUpdateMode.interval) {
                final minutes = int.tryParse(value?.trim() ?? '');
                if (minutes == null || minutes < 1) {
                  return dialogTrans.update_interval_error;
                }
              }
              return null;
            },
          ),
        ],
      ],
    );
  }

  // 构建代理模式选择区域
  Widget _buildProxyModeSection() {
    return ProxyModeSelector(
      selectedValue: _proxyMode,
      onChanged: (value) {
        setState(() => _proxyMode = value);
      },
    );
  }

  // 构建 User-Agent 输入字段
  Widget _buildUserAgentField() {
    final dialogTrans = context.translate.subscription_dialog;
    return TextInputField(
      controller: _userAgentController,
      label: 'User-Agent',
      hint:
          '${dialogTrans.user_agent_default}: ${ClashDefaults.defaultUserAgent}',
      icon: Icons.badge,
    );
  }

  // 构建导入方式选择器
  Widget _buildImportModeSelector() {
    final dialogTrans = context.translate.subscription_dialog;

    return OptionSelectorWidget<SubscriptionImportMethod>(
      title: dialogTrans.import_method_title,
      titleIcon: Icons.import_export,
      isHorizontal: true,
      options: [
        OptionItem(
          value: SubscriptionImportMethod.link,
          title: dialogTrans.import_link,
          subtitle: dialogTrans.import_link_support,
        ),
        OptionItem(
          value: SubscriptionImportMethod.localFile,
          title: dialogTrans.import_local,
          subtitle: dialogTrans.import_local_no_support,
        ),
      ],
      selectedValue: _importMethod,
      onChanged: (value) {
        setState(() {
          _importMethod = value;
          // 本地文件导入时默认禁用自动更新
          if (value == SubscriptionImportMethod.localFile) {
            _autoUpdateMode = AutoUpdateMode.disabled;
          } else if (_autoUpdateMode == AutoUpdateMode.disabled) {
            // 链接导入时如果当前是禁用状态，切换为间隔更新
            _autoUpdateMode = AutoUpdateMode.interval;
          }
        });
      },
    );
  }

  // 构建文件选择器
  Widget _buildFileSelector() {
    final dialogTrans = context.translate.subscription_dialog;

    return FileSelectorWidget(
      onFileSelected: (result) {
        setState(() {
          _selectedFile = result;
        });
      },
      initialFile: _selectedFile,
      hintText: dialogTrans.select_file_label,
      selectedText: dialogTrans.file_selected_label,
      draggingText: dialogTrans.drop_to_import,
      dragHintText: dialogTrans.click_or_drag,
    );
  }

  void _handleConfirm() async {
    final trans = context.translate;

    // 验证表单
    if (!_formKey.currentState!.validate()) {
      return;
    }

    // 验证本地导入时是否选择了文件
    if (_importMethod == SubscriptionImportMethod.localFile &&
        _selectedFile == null) {
      return;
    }

    setState(() => _isLoading = true);

    try {
      // 获取 UA 值，如果为空则使用默认值
      final userAgent = _userAgentController.text.trim();
      final result = SubscriptionDialogResult(
        name: _nameController.text.trim(),
        url: _importMethod == SubscriptionImportMethod.link
            ? _urlController.text.trim()
            : null,
        autoUpdateMode: _autoUpdateMode,
        intervalMinutes: int.tryParse(_intervalController.text.trim()) ?? 60,
        shouldUpdateOnStartup: _autoUpdateMode == AutoUpdateMode.onStartup,
        isLocalImport: _importMethod == SubscriptionImportMethod.localFile,
        localFilePath: _selectedFile?.file.path,
        proxyMode: _proxyMode,
        userAgent: userAgent.isEmpty ? _defaultUserAgent : userAgent,
      );

      // 如果有确认回调，调用它并等待结果
      if (widget.onConfirm != null) {
        bool success = false;
        String? errorMessage;

        try {
          success = await widget.onConfirm!(result);
        } catch (e) {
          success = false;
          errorMessage = e.toString();
          Logger.error('订阅操作异常: $e');
        }

        if (!mounted) return;

        if (success) {
          // 成功，关闭对话框
          if (mounted) {
            Navigator.of(context).pop();
          }
        } else {
          // 失败，停止加载状态，保持对话框打开
          setState(() => _isLoading = false);

          // 显示错误提示
          if (mounted) {
            final defaultErrorMessage =
                _importMethod == SubscriptionImportMethod.localFile
                ? trans.subscription_dialog.local_import_failed
                : trans.subscription_dialog.remote_import_failed;

            ModernToast.error(errorMessage ?? defaultErrorMessage);
          }
        }
      } else {
        // 没有回调，直接返回结果（编辑模式）
        await Future.delayed(const Duration(milliseconds: 300));
        if (mounted) {
          ModernToast.success(trans.subscription_dialog.save_success);
          Navigator.of(context).pop(result);
        }
      }
    } catch (e) {
      Logger.error('对话框确认操作异常: $e');
      if (mounted) {
        setState(() => _isLoading = false);
        ModernToast.error(
          trans.subscription_dialog.operation_error.replaceAll(
            '{error}',
            e.toString(),
          ),
        );
      }
    }
  }
}

// 订阅对话框结果
class SubscriptionDialogResult {
  final String name;
  final String? url;
  final AutoUpdateMode autoUpdateMode;
  final int intervalMinutes;
  final bool shouldUpdateOnStartup;
  final bool isLocalImport;
  final String? localFilePath;
  final SubscriptionProxyMode proxyMode;
  final String userAgent;

  const SubscriptionDialogResult({
    required this.name,
    this.url,
    required this.autoUpdateMode,
    this.intervalMinutes = 60,
    this.shouldUpdateOnStartup = false,
    this.isLocalImport = false,
    this.localFilePath,
    this.proxyMode = SubscriptionProxyMode.direct,
    String? userAgent,
  }) : userAgent = userAgent ?? ClashDefaults.defaultUserAgent;
}
