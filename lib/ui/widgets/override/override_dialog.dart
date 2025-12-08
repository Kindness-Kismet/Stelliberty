import 'package:flutter/material.dart';
import 'package:stelliberty/clash/data/override_model.dart';
import 'package:stelliberty/clash/data/subscription_model.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/option_selector.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/text_input_field.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/file_selector.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/proxy_mode_selector.dart';

// 对话框间距常量
const double _dialogContentPadding = 20.0;
const double _dialogItemSpacing = 20.0;

// 覆写添加方式枚举
enum OverrideAddMethod {
  // 远程 URL 下载
  remote,

  // 新建空白文件
  create,

  // 导入本地文件
  import,
}

// 覆写对话框 - 支持远程下载、新建和导入三种方式
// 支持 YAML 和 JavaScript 格式，远程下载可选代理模式
class OverrideDialog extends StatefulWidget {
  final OverrideConfig? editingOverride;
  final Future<bool> Function(OverrideConfig)? onConfirm;

  const OverrideDialog({super.key, this.editingOverride, this.onConfirm});

  static Future<OverrideConfig?> show(
    BuildContext context, {
    OverrideConfig? editingOverride,
    Future<bool> Function(OverrideConfig)? onConfirm,
  }) {
    return showDialog<OverrideConfig>(
      context: context,
      barrierDismissible: false,
      builder: (context) => OverrideDialog(
        editingOverride: editingOverride,
        onConfirm: onConfirm,
      ),
    );
  }

  @override
  State<OverrideDialog> createState() => _OverrideDialogState();
}

class _OverrideDialogState extends State<OverrideDialog> {
  late final TextEditingController _nameController;
  late final TextEditingController _urlController;
  late OverrideFormat _format;
  late SubscriptionProxyMode _proxyMode;

  // 覆写添加方式
  OverrideAddMethod _addMethod = OverrideAddMethod.remote;

  // 选中的文件信息
  FileSelectionResult? _selectedFile;
  bool _isLoading = false;

  final _formKey = GlobalKey<FormState>();

  // 重建延迟标志，避免输入时频繁重建
  bool _needsRebuild = false;

  @override
  void initState() {
    super.initState();

    _nameController = TextEditingController(
      text: widget.editingOverride?.name ?? '',
    );
    _urlController = TextEditingController(
      text: widget.editingOverride?.url ?? '',
    );
    _format = widget.editingOverride?.format ?? OverrideFormat.yaml;
    _proxyMode =
        widget.editingOverride?.proxyMode ?? SubscriptionProxyMode.direct;

    if (widget.editingOverride != null) {
      _addMethod = widget.editingOverride!.type == OverrideType.remote
          ? OverrideAddMethod.remote
          : OverrideAddMethod.import;
    }

    // 添加监听器以检测内容变化
    _nameController.addListener(_checkForChanges);
    _urlController.addListener(_checkForChanges);
  }

  // 检查内容是否发生变化
  bool get _hasChanges {
    // 添加模式：总是允许保存
    if (widget.editingOverride == null) return true;

    // 编辑模式：检查是否有任何字段发生变化
    final nameChanged =
        _nameController.text.trim() != (widget.editingOverride?.name ?? '');
    final urlChanged =
        widget.editingOverride?.type == OverrideType.remote &&
        _urlController.text.trim() != (widget.editingOverride?.url ?? '');
    final proxyModeChanged =
        widget.editingOverride?.type == OverrideType.remote &&
        _proxyMode !=
            (widget.editingOverride?.proxyMode ?? SubscriptionProxyMode.direct);

    return nameChanged || urlChanged || proxyModeChanged;
  }

  // 延迟重建，合并同一帧内的多次变更
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
    // 释放控制器
    _nameController.dispose();
    _urlController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final isEditing = widget.editingOverride != null;

    return ModernDialog(
      title: isEditing
          ? context.translate.overrideDialog.editOverrideTitle
          : context.translate.overrideDialog.addOverrideTitle,
      titleIcon: isEditing ? Icons.edit : Icons.add_circle_outline,
      isModified: isEditing && _hasChanges,
      maxWidth: 720,
      maxHeightRatio: 0.85,
      content: _buildContent(isEditing),
      actionsRight: [
        DialogActionButton(
          label: context.translate.common.cancel,
          isPrimary: false,
          onPressed: _isLoading ? null : () => Navigator.of(context).pop(),
        ),
        DialogActionButton(
          label: isEditing
              ? context.translate.common.save
              : context.translate.common.add,
          isPrimary: true,
          isLoading: _isLoading,
          onPressed: (_isLoading || !_hasChanges) ? null : _handleConfirm,
        ),
      ],
      onClose: _isLoading ? null : () => Navigator.of(context).pop(),
    );
  }

  Widget _buildContent(bool isEditing) {
    return SingleChildScrollView(
      padding: const EdgeInsets.all(_dialogContentPadding),
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            if (!isEditing) ...[
              _buildAddModeSelector(),
              const SizedBox(height: _dialogItemSpacing),
            ],

            TextInputField(
              controller: _nameController,
              label: context.translate.kOverride.nameLabel,
              hint: context.translate.kOverride.nameHint,
              icon: Icons.label_outline,
              validator: (value) {
                if (value == null || value.trim().isEmpty) {
                  return context.translate.kOverride.nameError;
                }
                return null;
              },
            ),

            // 远程模式显示覆写链接
            if (_addMethod == OverrideAddMethod.remote) ...[
              const SizedBox(height: _dialogItemSpacing),
              TextInputField(
                controller: _urlController,
                label: context.translate.kOverride.urlLabel,
                hint: 'https://example.com/override.yaml',
                icon: Icons.link,
                minLines: 1,
                maxLines: null,
                validator: (value) {
                  if (value == null || value.trim().isEmpty) {
                    return context.translate.kOverride.urlError;
                  }
                  final uri = Uri.tryParse(value.trim());
                  if (uri == null || !uri.hasScheme || !uri.hasAuthority) {
                    return context.translate.kOverride.urlFormatError;
                  }
                  return null;
                },
              ),
            ],

            // 编辑模式不显示格式选择器
            if (!isEditing) ...[
              const SizedBox(height: _dialogItemSpacing),
              _buildFormatSelector(),
            ],

            // 远程模式显示代理模式选择，导入模式显示文件选择器
            if (_addMethod == OverrideAddMethod.remote) ...[
              const SizedBox(height: _dialogItemSpacing),
              _buildProxyModeSection(),
            ] else if (_addMethod == OverrideAddMethod.import &&
                !isEditing) ...[
              const SizedBox(height: _dialogItemSpacing),
              _buildFileSelector(),
            ],
          ],
        ),
      ),
    );
  }

  // 构建添加方式选择器
  Widget _buildAddModeSelector() {
    return OptionSelectorWidget<OverrideAddMethod>(
      title: context.translate.kOverride.addMethodTitle,
      titleIcon: Icons.folder,
      isHorizontal: true,
      options: [
        OptionItem(
          value: OverrideAddMethod.remote,
          title: context.translate.kOverride.addMethodRemote,
          subtitle: context.translate.kOverride.addMethodRemoteDesc,
        ),
        OptionItem(
          value: OverrideAddMethod.create,
          title: context.translate.kOverride.addMethodCreate,
          subtitle: context.translate.kOverride.addMethodCreateDesc,
        ),
        OptionItem(
          value: OverrideAddMethod.import,
          title: context.translate.kOverride.addMethodImport,
          subtitle: context.translate.kOverride.addMethodImportDesc,
        ),
      ],
      selectedValue: _addMethod,
      onChanged: (value) {
        setState(() => _addMethod = value);
      },
    );
  }

  // 构建格式选择器
  Widget _buildFormatSelector() {
    return OptionSelectorWidget<OverrideFormat>(
      title: context.translate.kOverride.formatTitle,
      titleIcon: Icons.code,
      isHorizontal: true,
      options: [
        OptionItem(
          value: OverrideFormat.yaml,
          title: OverrideFormat.yaml.displayName,
        ),
        OptionItem(
          value: OverrideFormat.js,
          title: OverrideFormat.js.displayName,
        ),
      ],
      selectedValue: _format,
      onChanged: (value) {
        setState(() => _format = value);
      },
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

  // 构建文件选择器
  Widget _buildFileSelector() {
    return FileSelectorWidget(
      onFileSelected: (result) {
        setState(() {
          _selectedFile = result;
        });
      },
      initialFile: _selectedFile,
      hintText: context.translate.kOverride.selectLocalFile,
      selectedText: context.translate.kOverride.fileSelected,
      draggingText: context.translate.kOverride.fileSelectPrompt,
      dragHintText: context.translate.kOverride.clickOrDrag,
    );
  }

  Future<void> _handleConfirm() async {
    Logger.info('_handleConfirm 被调用');
    Logger.info('编辑模式: ${widget.editingOverride != null}');
    Logger.info('名称: ${_nameController.text}');

    if (!_formKey.currentState!.validate()) {
      Logger.warning('表单验证失败');
      return;
    }

    // 验证导入模式是否选择了文件
    if (widget.editingOverride == null &&
        _addMethod == OverrideAddMethod.import &&
        _selectedFile == null) {
      Logger.warning('导入模式但未选择文件');
      return;
    }

    Logger.info('表单验证通过，继续处理...');

    final override = widget.editingOverride != null
        ? widget.editingOverride!.copyWith(name: _nameController.text.trim())
        : OverrideConfig(
            id: DateTime.now().millisecondsSinceEpoch.toString(),
            name: _nameController.text.trim(),
            type: _addMethod == OverrideAddMethod.remote
                ? OverrideType.remote
                : OverrideType.local,
            format: _format,
            url: _addMethod == OverrideAddMethod.remote
                ? _urlController.text.trim()
                : null,
            localPath: _addMethod == OverrideAddMethod.import
                ? _selectedFile?.file.path
                : null,
            content: _addMethod == OverrideAddMethod.create ? '' : null,
            proxyMode: _addMethod == OverrideAddMethod.remote
                ? _proxyMode
                : SubscriptionProxyMode.direct,
          );

    Logger.info('创建的覆写对象: ${override.name}, ID: ${override.id}');

    // 添加模式执行异步操作
    if (widget.onConfirm != null) {
      Logger.info('添加模式：执行 onConfirm 回调');
      setState(() => _isLoading = true);

      try {
        final success = await widget.onConfirm!(override);
        Logger.info('onConfirm 回调结果: $success');

        if (!mounted) return;

        if (success) {
          Logger.info('添加成功，关闭对话框');
          if (mounted) {
            ModernToast.success(
              context,
              context.translate.overrideDialog.addSuccess,
            );
            Navigator.of(context).pop(override);
          }
        } else {
          Logger.warning('添加失败');
          setState(() => _isLoading = false);
          if (mounted) {
            ModernToast.error(
              context,
              context.translate.kOverride.addFailed.replaceAll(
                '{error}',
                override.name,
              ),
            );
          }
        }
      } catch (error) {
        Logger.error('添加时发生异常: $error');
        if (!mounted) return;
        setState(() => _isLoading = false);
        ModernToast.error(
          context,
          context.translate.kOverride.addFailed.replaceAll(
            '{error}',
            error.toString(),
          ),
        );
      }
    } else {
      // 编辑模式，直接返回
      Logger.info('编辑模式：直接返回配置对象');
      if (mounted) {
        Logger.info('关闭对话框并返回: ${override.name}');
        Navigator.of(context).pop(override);
      }
    }
    Logger.info('_handleConfirm 完成');
  }
}
