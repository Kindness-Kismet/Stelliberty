import 'package:flutter/material.dart';
import 'package:stelliberty/clash/data/subscription_model.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/option_selector.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/text_input_field.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/file_selector.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/proxy_mode_selector.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
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
    return showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => SubscriptionDialog(
        title: context.translate.subscriptionDialog.addTitle,
        confirmText: context.translate.subscriptionDialog.addButton,
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
    return showDialog<SubscriptionDialogResult>(
      context: context,
      barrierDismissible: false,
      builder: (context) => SubscriptionDialog(
        title: context.translate.subscriptionDialog.editTitle,
        initialName: subscription.name,
        initialUrl: subscription.url,
        initialAutoUpdateMode: subscription.autoUpdateMode,
        initialIntervalMinutes: subscription.intervalMinutes,
        initialUpdateOnStartup: subscription.updateOnStartup,
        initialProxyMode: subscription.proxyMode,
        initialUserAgent: subscription.userAgent,
        confirmText: context.translate.subscriptionDialog.saveButton,
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
  late bool _updateOnStartup;
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
    // 编辑模式：使用订阅的 UA；添加模式：使用全局默认 UA
    _userAgentController = TextEditingController(
      text: widget.initialUserAgent ?? _defaultUserAgent,
    );

    // 初始化自动更新模式和启动时更新
    _autoUpdateMode = widget.initialAutoUpdateMode ?? AutoUpdateMode.disabled;
    _updateOnStartup = widget.initialUpdateOnStartup ?? false;
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

      if (_updateOnStartup != (widget.initialUpdateOnStartup ?? false)) {
        return true;
      }

      if (_proxyMode !=
          (widget.initialProxyMode ?? SubscriptionProxyMode.direct)) {
        return true;
      }

      if (_userAgentController.text.trim() !=
          (widget.initialUserAgent ?? _defaultUserAgent)) {
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
    return ModernDialog(
      title: widget.title,
      titleIcon: widget.titleIcon,
      isModified: !widget.isAddMode && _hasChanges,
      maxWidth: 720,
      maxHeightRatio: 0.85,
      content: _buildContent(),
      actionsLeft: widget.isAddMode
          ? Text(
              context.translate.subscriptionDialog.addModeHint,
              style: TextStyle(
                fontSize: 12,
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.6),
              ),
            )
          : Text(
              context.translate.subscriptionDialog.editModeHint,
              style: TextStyle(
                fontSize: 12,
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.6),
              ),
            ),
      actionsRight: [
        DialogActionButton(
          label: context.translate.subscriptionDialog.cancelButton,
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
              label: context.translate.subscriptionDialog.configNameLabel,
              hint: context.translate.subscriptionDialog.configNameHint,
              icon: Icons.label_outline,
              validator: (value) {
                if (value == null || value.trim().isEmpty) {
                  return context.translate.subscriptionDialog.configNameError;
                }
                return null;
              },
            ),

            // 根据导入方式显示不同的输入控件
            // 添加模式：根据 _importMethod 显示
            // 编辑模式：本地文件订阅不显示 URL 字段
            if (widget.isAddMode &&
                    _importMethod == SubscriptionImportMethod.link ||
                !widget.isAddMode && !widget.isLocalFile) ...[
              const SizedBox(height: _dialogItemSpacing),
              TextInputField(
                controller: _urlController,
                label:
                    context.translate.subscriptionDialog.subscriptionLinkLabel,
                hint: context.translate.subscriptionDialog.subscriptionLinkHint,
                icon: Icons.link,
                minLines: 1,
                maxLines: null,
                validator: (value) {
                  if (value == null || value.trim().isEmpty) {
                    return context.translate.subscriptionDialog.linkError;
                  }

                  final uri = Uri.tryParse(value.trim());
                  if (uri == null) {
                    return context.translate.subscriptionDialog.linkFormatError;
                  }

                  if (uri.scheme != 'http' && uri.scheme != 'https') {
                    return context
                        .translate
                        .subscriptionDialog
                        .linkProtocolError;
                  }

                  if (uri.host.isEmpty) {
                    return context.translate.subscriptionDialog.linkMissingHost;
                  }

                  // 验证域名格式：必须包含点，或者是 localhost/IP
                  final host = uri.host.toLowerCase();
                  if (host != 'localhost' &&
                      host != '127.0.0.1' &&
                      !host.contains('.')) {
                    return context
                        .translate
                        .subscriptionDialog
                        .linkHostFormatError;
                  }

                  if (host.length < 3) {
                    return context
                        .translate
                        .subscriptionDialog
                        .linkHostTooShort;
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

            // 只有链接导入才显示自动更新选项
            // 添加模式：只有选择链接导入时显示
            // 编辑模式：只有非本地文件才显示
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
    final trans = context.translate.subscriptionDialog;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      color: Colors.transparent,
      child: Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: isDark
              ? Colors.white.withValues(alpha: 0.04)
              : Colors.white.withValues(alpha: 0.6),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(
            color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
          ),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // 标题
            Row(
              children: [
                Icon(
                  Icons.refresh,
                  color: Theme.of(context).colorScheme.primary,
                  size: 16,
                ),
                const SizedBox(width: 8),
                Text(
                  trans.autoUpdateTitle,
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w600,
                    color: Theme.of(context).colorScheme.primary,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),

            // 两个模式选项（横向排列）
            _buildUpdateModeOptions(),

            // 禁用更新时的"启动时更新"选项
            if (_autoUpdateMode == AutoUpdateMode.disabled) ...[
              const SizedBox(height: 16),
              _buildUpdateOnStartupCheckbox(),
            ],

            // 间隔更新配置（当选择间隔更新时展开）
            if (_autoUpdateMode == AutoUpdateMode.interval) ...[
              const SizedBox(height: 16),
              TextInputField(
                controller: _intervalController,
                label: trans.updateIntervalLabel,
                hint: trans.updateIntervalHint,
                icon: Icons.schedule,
                validator: (value) {
                  if (_autoUpdateMode == AutoUpdateMode.interval) {
                    final minutes = int.tryParse(value?.trim() ?? '');
                    if (minutes == null || minutes < 1) {
                      return trans.updateIntervalError;
                    }
                  }
                  return null;
                },
              ),
            ],
          ],
        ),
      ),
    );
  }

  // 构建更新模式选项（横向排列）
  Widget _buildUpdateModeOptions() {
    final trans = context.translate.subscriptionDialog;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    final options = [
      (
        AutoUpdateMode.disabled,
        trans.autoUpdateDisabled,
        trans.autoUpdateDisabledDesc,
      ),
      (
        AutoUpdateMode.interval,
        trans.autoUpdateInterval,
        trans.autoUpdateIntervalDesc,
      ),
    ];

    return Row(
      children: options.map((option) {
        final value = option.$1;
        final title = option.$2;
        final subtitle = option.$3;
        final isSelected = _autoUpdateMode == value;

        return Expanded(
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 4),
            child: Material(
              color: Colors.transparent,
              borderRadius: BorderRadius.circular(8),
              child: InkWell(
                borderRadius: BorderRadius.circular(8),
                onTap: () {
                  setState(() {
                    _autoUpdateMode = value;
                  });
                },
                child: Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(
                    color: isSelected
                        ? Theme.of(
                            context,
                          ).colorScheme.primary.withValues(alpha: 0.15)
                        : (isDark
                              ? Colors.white.withValues(alpha: 0.03)
                              : Colors.white.withValues(alpha: 0.4)),
                    borderRadius: BorderRadius.circular(8),
                    border: Border.all(
                      color: isSelected
                          ? Theme.of(context).colorScheme.primary
                          : Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
                      width: isSelected ? 1.5 : 1,
                    ),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        style: TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                          color: isSelected
                              ? Theme.of(context).colorScheme.primary
                              : Theme.of(context).colorScheme.onSurface,
                        ),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        subtitle,
                        style: TextStyle(
                          fontSize: 11,
                          color: Theme.of(
                            context,
                          ).colorScheme.onSurface.withValues(alpha: 0.6),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        );
      }).toList(),
    );
  }

  // 构建"启动时更新"开关卡片
  Widget _buildUpdateOnStartupCheckbox() {
    final trans = context.translate.subscriptionDialog;
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        borderRadius: BorderRadius.circular(8),
        onTap: () {
          setState(() {
            _updateOnStartup = !_updateOnStartup;
          });
        },
        child: Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: isDark
                ? Colors.white.withValues(alpha: 0.04)
                : Colors.white.withValues(alpha: 0.4),
            borderRadius: BorderRadius.circular(8),
            border: Border.all(
              color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
            ),
          ),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      trans.updateOnStartup,
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                        color: Theme.of(context).colorScheme.onSurface,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      trans.updateOnStartupDesc,
                      style: TextStyle(
                        fontSize: 11,
                        color: Theme.of(
                          context,
                        ).colorScheme.onSurface.withValues(alpha: 0.6),
                      ),
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 12),
              ModernSwitch(
                value: _updateOnStartup,
                onChanged: (value) {
                  setState(() {
                    _updateOnStartup = value;
                  });
                },
              ),
            ],
          ),
        ),
      ),
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
    return TextInputField(
      controller: _userAgentController,
      label: 'User-Agent',
      hint: ClashDefaults.defaultUserAgent,
      icon: Icons.badge,
      validator: (value) {
        if (value == null || value.trim().isEmpty) {
          return 'User-Agent 不能为空';
        }
        return null;
      },
    );
  }

  // 构建导入方式选择器
  Widget _buildImportModeSelector() {
    final trans = context.translate.subscriptionDialog;

    return OptionSelectorWidget<SubscriptionImportMethod>(
      title: trans.importMethodTitle,
      titleIcon: Icons.import_export,
      isHorizontal: true,
      options: [
        OptionItem(
          value: SubscriptionImportMethod.link,
          title: trans.importLink,
          subtitle: trans.importLinkSupport,
        ),
        OptionItem(
          value: SubscriptionImportMethod.localFile,
          title: trans.importLocal,
          subtitle: trans.importLocalNoSupport,
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
    final trans = context.translate.subscriptionDialog;

    return FileSelectorWidget(
      onFileSelected: (result) {
        setState(() {
          _selectedFile = result;
        });
      },
      initialFile: _selectedFile,
      hintText: trans.selectFileLabel,
      selectedText: trans.fileSelectedLabel,
      draggingText: trans.dropToImport,
      dragHintText: trans.clickOrDrag,
    );
  }

  void _handleConfirm() async {
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
      final result = SubscriptionDialogResult(
        name: _nameController.text.trim(),
        url: _importMethod == SubscriptionImportMethod.link
            ? _urlController.text.trim()
            : null,
        autoUpdateMode: _autoUpdateMode,
        intervalMinutes: int.tryParse(_intervalController.text.trim()) ?? 60,
        updateOnStartup: _updateOnStartup,
        isLocalImport: _importMethod == SubscriptionImportMethod.localFile,
        localFilePath: _selectedFile?.file.path,
        proxyMode: _proxyMode,
        userAgent: _userAgentController.text.trim(),
      );

      // 如果有确认回调，调用它并等待结果
      if (widget.onConfirm != null) {
        bool success = false;
        String? errorMessage;

        try {
          success = await widget.onConfirm!(result);
        } catch (error) {
          success = false;
          errorMessage = error.toString();
          Logger.error('订阅操作异常: $error');
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
                ? context.translate.subscriptionDialog.localImportFailed
                : context.translate.subscriptionDialog.remoteImportFailed;

            ModernToast.error(context, errorMessage ?? defaultErrorMessage);
          }
        }
      } else {
        // 没有回调，直接返回结果（编辑模式）
        await Future.delayed(const Duration(milliseconds: 300));
        if (mounted) {
          ModernToast.success(
            context,
            context.translate.subscriptionDialog.saveSuccess,
          );
          Navigator.of(context).pop(result);
        }
      }
    } catch (error) {
      Logger.error('对话框确认操作异常: $error');
      if (mounted) {
        setState(() => _isLoading = false);
        ModernToast.error(
          context,
          context.translate.subscriptionDialog.operationError.replaceAll(
            '{error}',
            error.toString(),
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
  final bool updateOnStartup;
  final bool isLocalImport;
  final String? localFilePath;
  final SubscriptionProxyMode proxyMode;
  final String userAgent;

  const SubscriptionDialogResult({
    required this.name,
    this.url,
    required this.autoUpdateMode,
    this.intervalMinutes = 60,
    this.updateOnStartup = false,
    this.isLocalImport = false,
    this.localFilePath,
    this.proxyMode = SubscriptionProxyMode.direct,
    String? userAgent,
  }) : userAgent = userAgent ?? ClashDefaults.defaultUserAgent;
}
