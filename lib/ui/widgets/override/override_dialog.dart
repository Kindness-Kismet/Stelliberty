import 'dart:io';
import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';
import 'package:desktop_drop/desktop_drop.dart';
import 'package:stelliberty/clash/data/override_model.dart';
import 'package:stelliberty/clash/data/subscription_model.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';

// 覆写添加方式枚举
enum OverrideAddMethod {
  // 远程 URL 下载
  remote,

  // 新建空白文件
  create,

  // 导入本地文件
  import,
}

// 覆写对话框组件 - 毛玻璃风格
// 支持三种添加方式：
// 1. 远程下载：通过 URL 获取配置
// 2. 新建文件：创建空白配置
// 3. 导入文件：选择本地文件
// 关键特性：
// - 动态高度 URL 输入框（避免右侧空白）
// - 支持 YAML 和 JavaScript 两种格式
// - 代理模式选择（仅远程下载）
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

  File? _selectedFile;
  String? _selectedFileName;
  bool _isDragging = false;
  bool _isLoading = false;

  final _formKey = GlobalKey<FormState>();

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

    // 根据编辑的覆写类型初始化添加方式
    if (widget.editingOverride != null) {
      _addMethod = widget.editingOverride!.type == OverrideType.remote
          ? OverrideAddMethod.remote
          : OverrideAddMethod.import; // 本地文件（已存在的）
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

  // 内容变化时触发重建以更新按钮状态
  void _checkForChanges() {
    setState(() {});
  }

  @override
  void dispose() {
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
      padding: const EdgeInsets.all(24),
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            if (!isEditing) ...[
              _buildAddModeSelector(),
              const SizedBox(height: 20),
            ],

            _buildTextField(
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

            // 编辑模式下不显示格式选择器（格式不可改变）
            if (!isEditing) ...[
              const SizedBox(height: 20),
              _buildFormatSelector(),
            ],

            // 仅在添加模式或远程覆写模式显示相应字段
            if (_addMethod == OverrideAddMethod.remote) ...[
              const SizedBox(height: 20),
              _buildTextField(
                controller: _urlController,
                label: 'URL',
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
              const SizedBox(height: 20),
              _buildProxyModeSection(),
            ] else if (_addMethod == OverrideAddMethod.import &&
                !isEditing) ...[
              const SizedBox(height: 20),
              _buildFileSelector(),
            ],
          ],
        ),
      ),
    );
  }

  // 构建输入框（动态高度支持）
  // minLines: 1, maxLines: null 实现自动扩展，
  // 避免 URL 过长时右侧空白过大
  Widget _buildTextField({
    required TextEditingController controller,
    required String label,
    required String hint,
    required IconData icon,
    int? minLines,
    int? maxLines = 1,
    String? Function(String?)? validator,
  }) {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      color: Colors.transparent,
      child: Container(
        decoration: BoxDecoration(
          color: isDark
              ? Colors.white.withValues(alpha: 0.04)
              : Colors.white.withValues(alpha: 0.6),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(
            color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
          ),
        ),
        child: TextFormField(
          controller: controller,
          minLines: minLines,
          maxLines: maxLines,
          validator: validator,
          decoration: InputDecoration(
            labelText: label,
            hintText: hint,
            prefixIcon: Icon(icon, size: 16),
            border: InputBorder.none,
            contentPadding: const EdgeInsets.symmetric(
              horizontal: 16,
              vertical: 16,
            ),
            labelStyle: TextStyle(
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.7),
              fontSize: 14,
            ),
            hintStyle: TextStyle(
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.5),
              fontSize: 14,
            ),
          ),
        ),
      ),
    );
  }

  Widget _buildAddModeSelector() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      color: Colors.transparent,
      child: Container(
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
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                Icon(
                  Icons.folder,
                  color: Theme.of(context).colorScheme.secondary,
                  size: 16,
                ),
                const SizedBox(width: 8),
                Text(
                  context.translate.kOverride.addMethodTitle,
                  style: TextStyle(
                    color: Theme.of(context).colorScheme.secondary,
                    fontWeight: FontWeight.w600,
                    fontSize: 14,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: _buildAddMethodOption(
                    method: OverrideAddMethod.remote,
                    icon: Icons.cloud,
                    label: context.translate.kOverride.addMethodRemote,
                    description:
                        context.translate.kOverride.addMethodRemoteDesc,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _buildAddMethodOption(
                    method: OverrideAddMethod.create,
                    icon: Icons.add,
                    label: context.translate.kOverride.addMethodCreate,
                    description:
                        context.translate.kOverride.addMethodCreateDesc,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _buildAddMethodOption(
                    method: OverrideAddMethod.import,
                    icon: Icons.folder_open,
                    label: context.translate.kOverride.addMethodImport,
                    description:
                        context.translate.kOverride.addMethodImportDesc,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildAddMethodOption({
    required OverrideAddMethod method,
    required IconData icon,
    required String label,
    required String description,
  }) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final isSelected = _addMethod == method;

    return Material(
      color: Colors.transparent,
      borderRadius: BorderRadius.circular(10),
      child: InkWell(
        borderRadius: BorderRadius.circular(10),
        onTap: () => setState(() => _addMethod = method),
        child: Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(10),
            border: isSelected
                ? Border.all(
                    color: Theme.of(context).colorScheme.primary,
                    width: 2,
                  )
                : Border.all(
                    color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
                  ),
            color: isSelected
                ? Theme.of(context).colorScheme.primary.withValues(alpha: 0.08)
                : (isDark
                      ? Colors.white.withValues(alpha: 0.02)
                      : Colors.white.withValues(alpha: 0.4)),
          ),
          child: Row(
            children: [
              Icon(
                isSelected
                    ? Icons.radio_button_checked
                    : Icons.radio_button_unchecked,
                color: isSelected
                    ? Theme.of(context).colorScheme.primary
                    : Theme.of(
                        context,
                      ).colorScheme.onSurface.withValues(alpha: 0.4),
                size: 18,
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      label,
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: isSelected
                            ? FontWeight.w600
                            : FontWeight.w500,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      description,
                      style: TextStyle(
                        fontSize: 10,
                        color: Theme.of(
                          context,
                        ).colorScheme.onSurface.withValues(alpha: 0.6),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildFormatSelector() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      color: Colors.transparent,
      child: Container(
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
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  Icons.code,
                  color: Theme.of(context).colorScheme.primary,
                  size: 16,
                ),
                const SizedBox(width: 8),
                Text(
                  context.translate.kOverride.formatTitle,
                  style: TextStyle(
                    color: Theme.of(context).colorScheme.primary,
                    fontWeight: FontWeight.w600,
                    fontSize: 14,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(child: _buildFormatOption(OverrideFormat.yaml)),
                const SizedBox(width: 12),
                Expanded(child: _buildFormatOption(OverrideFormat.js)),
              ],
            ),
          ],
        ),
      ),
    );
  }

  // 构建代理模式选择区域（仅远程覆写显示）
  Widget _buildProxyModeSection() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Material(
      color: Colors.transparent,
      child: Container(
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
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Row(
              children: [
                Icon(
                  Icons.public,
                  color: Theme.of(context).colorScheme.primary,
                  size: 16,
                ),
                const SizedBox(width: 8),
                Text(
                  context.translate.kOverride.proxyModeTitle,
                  style: TextStyle(
                    color: Theme.of(context).colorScheme.primary,
                    fontWeight: FontWeight.w600,
                    fontSize: 14,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            ...SubscriptionProxyMode.values.map((mode) {
              final isSelected = _proxyMode == mode;
              return Padding(
                padding: const EdgeInsets.only(bottom: 8.0),
                child: Material(
                  color: Colors.transparent,
                  borderRadius: BorderRadius.circular(10),
                  child: InkWell(
                    borderRadius: BorderRadius.circular(10),
                    onTap: () {
                      setState(() => _proxyMode = mode);
                    },
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 12,
                        vertical: 12,
                      ),
                      decoration: BoxDecoration(
                        borderRadius: BorderRadius.circular(10),
                        border: isSelected
                            ? Border.all(
                                color: Theme.of(context).colorScheme.primary,
                                width: 2,
                              )
                            : Border.all(
                                color: Colors.white.withValues(
                                  alpha: isDark ? 0.1 : 0.2,
                                ),
                              ),
                        color: isSelected
                            ? Theme.of(
                                context,
                              ).colorScheme.primary.withValues(alpha: 0.08)
                            : (isDark
                                  ? Colors.white.withValues(alpha: 0.02)
                                  : Colors.white.withValues(alpha: 0.4)),
                      ),
                      child: Row(
                        children: [
                          Icon(
                            isSelected
                                ? Icons.radio_button_checked
                                : Icons.radio_button_unchecked,
                            color: isSelected
                                ? Theme.of(context).colorScheme.primary
                                : Theme.of(context).colorScheme.onSurface
                                      .withValues(alpha: 0.4),
                            size: 20,
                          ),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  mode.displayName,
                                  style: TextStyle(
                                    fontSize: 14,
                                    fontWeight: isSelected
                                        ? FontWeight.w600
                                        : FontWeight.normal,
                                  ),
                                ),
                                const SizedBox(height: 2),
                                Text(
                                  _getProxyModeDescription(mode),
                                  style: TextStyle(
                                    fontSize: 12,
                                    color: Theme.of(context)
                                        .colorScheme
                                        .onSurface
                                        .withValues(alpha: 0.6),
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              );
            }),
          ],
        ),
      ),
    );
  }

  // 获取代理模式描述
  String _getProxyModeDescription(SubscriptionProxyMode mode) {
    switch (mode) {
      case SubscriptionProxyMode.direct:
        return context.translate.kOverride.proxyModeDirect;
      case SubscriptionProxyMode.system:
        return context.translate.kOverride.proxyModeSystem;
      case SubscriptionProxyMode.core:
        return context.translate.kOverride.proxyModeCore;
    }
  }

  Widget _buildFormatOption(OverrideFormat format) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final isSelected = _format == format;

    return Material(
      color: Colors.transparent,
      borderRadius: BorderRadius.circular(10),
      child: InkWell(
        borderRadius: BorderRadius.circular(10),
        onTap: () => setState(() => _format = format),
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 12),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(10),
            border: isSelected
                ? Border.all(
                    color: Theme.of(context).colorScheme.primary,
                    width: 2,
                  )
                : Border.all(
                    color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
                  ),
            color: isSelected
                ? Theme.of(context).colorScheme.primary.withValues(alpha: 0.08)
                : (isDark
                      ? Colors.white.withValues(alpha: 0.02)
                      : Colors.white.withValues(alpha: 0.4)),
          ),
          child: Row(
            children: [
              Icon(
                isSelected
                    ? Icons.radio_button_checked
                    : Icons.radio_button_unchecked,
                color: isSelected
                    ? Theme.of(context).colorScheme.primary
                    : Theme.of(
                        context,
                      ).colorScheme.onSurface.withValues(alpha: 0.4),
                size: 18,
              ),
              const SizedBox(width: 8),
              Expanded(
                child: Text(
                  format.displayName,
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: isSelected
                        ? FontWeight.w600
                        : FontWeight.normal,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildFileSelector() {
    return DropTarget(
      onDragEntered: (details) => setState(() => _isDragging = true),
      onDragExited: (details) => setState(() => _isDragging = false),
      onDragDone: (details) async {
        setState(() => _isDragging = false);
        if (details.files.isNotEmpty) {
          final file = File(details.files.first.path);
          if (await file.exists()) {
            setState(() {
              _selectedFile = file;
              _selectedFileName = details.files.first.name;
            });
          }
        }
      },
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: BorderRadius.circular(16),
          onTap: _selectFile,
          child: Container(
            decoration: BoxDecoration(
              color: _isDragging
                  ? Theme.of(context).colorScheme.primary.withValues(alpha: 0.1)
                  : Theme.of(
                      context,
                    ).colorScheme.surface.withValues(alpha: 0.5),
              borderRadius: BorderRadius.circular(16),
              border: Border.all(
                color: _isDragging
                    ? Theme.of(context).colorScheme.primary
                    : (_selectedFile != null
                          ? Theme.of(
                              context,
                            ).colorScheme.primary.withValues(alpha: 0.5)
                          : Theme.of(
                              context,
                            ).colorScheme.outline.withValues(alpha: 0.2)),
                width: _isDragging || _selectedFile != null ? 2 : 1,
              ),
            ),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
              child: Row(
                children: [
                  Icon(
                    _isDragging
                        ? Icons.file_download
                        : (_selectedFile != null
                              ? Icons.check_circle
                              : Icons.upload_file),
                    size: 20,
                    color: _isDragging || _selectedFile != null
                        ? Theme.of(context).colorScheme.primary
                        : Theme.of(
                            context,
                          ).colorScheme.onSurface.withValues(alpha: 0.6),
                  ),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          _isDragging
                              ? context.translate.kOverride.fileSelectPrompt
                              : (_selectedFile != null
                                    ? context.translate.kOverride.fileSelected
                                    : context
                                          .translate
                                          .kOverride
                                          .selectLocalFile),
                          style: TextStyle(
                            color: Theme.of(
                              context,
                            ).colorScheme.onSurface.withValues(alpha: 0.7),
                            fontSize: 16,
                            fontWeight: _selectedFile != null || _isDragging
                                ? FontWeight.w600
                                : FontWeight.normal,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          _selectedFile != null
                              ? _selectedFileName ??
                                    context.translate.kOverride.unknownFile
                              : context.translate.kOverride.clickOrDrag,
                          style: TextStyle(
                            color: _selectedFile != null || _isDragging
                                ? Theme.of(context).colorScheme.primary
                                : Theme.of(context).colorScheme.onSurface
                                      .withValues(alpha: 0.5),
                            fontSize: 12,
                          ),
                        ),
                      ],
                    ),
                  ),
                  Icon(
                    _selectedFile != null ? Icons.edit : Icons.folder_open,
                    color: Theme.of(context).colorScheme.primary,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _selectFile() async {
    try {
      final result = await FilePicker.platform.pickFiles(
        type: FileType.any,
        allowMultiple: false,
      );

      if (result != null && result.files.single.path != null) {
        final file = File(result.files.single.path!);
        if (await file.exists()) {
          setState(() {
            _selectedFile = file;
            _selectedFileName = result.files.single.name;
          });
        }
      }
    } catch (error) {
      Logger.debug('文件选择失败: $error');
    }
  }

  Future<void> _handleConfirm() async {
    Logger.info('_handleConfirm 被调用');
    Logger.info('编辑模式: ${widget.editingOverride != null}');
    Logger.info('名称: ${_nameController.text}');

    if (!_formKey.currentState!.validate()) {
      Logger.warning('表单验证失败');
      return;
    }

    // 验证导入模式时是否选择了文件（仅在添加模式检查）
    if (widget.editingOverride == null &&
        _addMethod == OverrideAddMethod.import &&
        _selectedFile == null) {
      Logger.warning('导入模式但未选择文件');
      return;
    }

    Logger.info('表单验证通过，继续处理...');

    final override = widget.editingOverride != null
        ? widget.editingOverride!.copyWith(
            name: _nameController.text.trim(),
            // 编辑模式：只更新名称，不更新其他字段
          )
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
                ? _selectedFile?.path
                : null,
            content: _addMethod == OverrideAddMethod.create
                ? ''
                : null, // 新建时创建空内容
            proxyMode: _addMethod == OverrideAddMethod.remote
                ? _proxyMode
                : SubscriptionProxyMode.direct,
          );

    Logger.info('创建的覆写对象: ${override.name}, ID: ${override.id}');

    // 如果有 onConfirm 回调（添加模式），执行异步操作
    if (widget.onConfirm != null) {
      Logger.info('添加模式：执行 onConfirm 回调');
      setState(() => _isLoading = true);

      try {
        final success = await widget.onConfirm!(override);
        Logger.info('onConfirm 回调结果: $success');

        if (!mounted) return;

        if (success) {
          // 成功后显示 Toast 并关闭对话框
          Logger.info('添加成功，关闭对话框');
          if (mounted) {
            ModernToast.success(
              context,
              context.translate.overrideDialog.addSuccess,
            );
            Navigator.of(context).pop(override);
          }
        } else {
          // 失败时显示错误并保持对话框打开
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
