import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:re_editor/re_editor.dart';
import 'package:re_highlight/languages/yaml.dart';
import 'package:re_highlight/styles/github-dark.dart';
import 'package:re_highlight/styles/atom-one-dark.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/common/modern_dialog.dart';

// 订阅文件编辑器对话框
//
// 支持编辑订阅配置文件（YAML格式），提供：
// - 代码高亮和行号显示
// - 异步加载优化（大文件友好）
// - 修改状态跟踪和警告
// - 文件保存和验证
class FileEditorDialog extends StatefulWidget {
  // 文件名称
  final String fileName;

  // 初始文件内容
  final String initialContent;

  // 保存回调函数
  final Future<bool> Function(String content) onSave;

  const FileEditorDialog({
    super.key,
    required this.fileName,
    required this.initialContent,
    required this.onSave,
  });

  // 显示文件编辑器对话框
  static Future<void> show(
    BuildContext context, {
    required String fileName,
    required String initialContent,
    required Future<bool> Function(String content) onSave,
  }) {
    return showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => FileEditorDialog(
        fileName: fileName,
        initialContent: initialContent,
        onSave: onSave,
      ),
    );
  }

  @override
  State<FileEditorDialog> createState() => _FileEditorDialogState();
}

class _FileEditorDialogState extends State<FileEditorDialog> {
  late final CodeLineEditingController _controller;

  // 内容是否被修改
  bool _isModified = false;

  // 是否正在保存
  bool _isSaving = false;

  // 编辑器是否已准备好显示内容
  bool _editorReady = false;

  // 缓存的行数（避免频繁计算）
  int _lineCount = 0;

  // 缓存的字符数（避免频繁计算）
  int _charCount = 0;

  @override
  void initState() {
    super.initState();

    // 先创建空编辑器（不添加 listener，避免触发修改检测）
    _controller = CodeLineEditingController.fromText('');

    // 等对话框完全显示后再加载内容
    _loadContentAfterDialogReady();
  }

  // 等对话框加载完成后再异步填充内容
  Future<void> _loadContentAfterDialogReady() async {
    // 等待对话框动画完成（300ms）+ 缓冲时间
    await Future.delayed(const Duration(milliseconds: 300));

    if (!mounted) return;

    // 加载文本内容
    await Future.microtask(() {
      _controller.text = widget.initialContent;
      _updateStats();
    });

    if (!mounted) return;

    // 内容加载完成后，添加 listener 并显示编辑器
    SchedulerBinding.instance.addPostFrameCallback((_) {
      if (mounted) {
        _controller.addListener(_onContentChanged);
        setState(() {
          _editorReady = true;
        });
      }
    });
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  // 内容变化回调
  void _onContentChanged() {
    final isModified = _controller.text != widget.initialContent;
    if (isModified != _isModified) {
      SchedulerBinding.instance.addPostFrameCallback((_) {
        if (mounted) {
          setState(() {
            _isModified = isModified;
          });
        }
      });
    }
    _updateStats();
  }

  // 更新统计数据（字符数和行数）
  // 使用缓存避免频繁重新计算和不必要的 setState
  void _updateStats() {
    final text = _controller.text;
    final newCharCount = text.length;
    final newLineCount = text.split('\n').length;

    if (newCharCount != _charCount || newLineCount != _lineCount) {
      SchedulerBinding.instance.addPostFrameCallback((_) {
        if (mounted) {
          setState(() {
            _charCount = newCharCount;
            _lineCount = newLineCount;
          });
        }
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return ModernDialog(
      title: context.translate.fileEditor.title,
      subtitle: widget.fileName,
      titleIcon: Icons.code,
      isModified: _isModified,
      maxWidth: 900,
      maxHeightRatio: 0.9,
      content: _buildEditor(),
      actionsLeft: Text(
        context.translate.fileEditor.stats
            .replaceAll('{chars}', _charCount.toString())
            .replaceAll('{lines}', _lineCount.toString()),
        style: TextStyle(
          fontSize: 12,
          color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6),
        ),
      ),
      actionsRight: [
        DialogActionButton(
          label: context.translate.fileEditor.cancelButton,
          isPrimary: false,
          onPressed: _isSaving ? null : () => Navigator.of(context).pop(),
        ),
        DialogActionButton(
          label: _isSaving
              ? context.translate.fileEditor.savingButton
              : context.translate.fileEditor.saveButton,
          isPrimary: true,
          isLoading: _isSaving,
          onPressed: (_isSaving || !_isModified) ? null : _handleSave,
        ),
      ],
      onClose: () => Navigator.of(context).pop(),
    );
  }

  // 构建代码编辑器
  Widget _buildEditor() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Container(
      margin: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        color: isDark
            ? Colors.white.withValues(alpha: 0.04)
            : Colors.white.withValues(alpha: 0.5),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: Colors.white.withValues(alpha: isDark ? 0.1 : 0.2),
        ),
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(12),
        child: Stack(
          children: [
            // 编辑器（内容加载完成后平滑淡入）
            AnimatedOpacity(
              opacity: _editorReady ? 1.0 : 0.0,
              duration: const Duration(milliseconds: 300),
              curve: Curves.easeIn,
              child: CodeEditor(
                controller: _controller,
                padding: const EdgeInsets.only(
                  left: 5,
                  right: 0,
                  top: 0,
                  bottom: 0,
                ),
                scrollbarBuilder: (context, child, details) {
                  return Padding(
                    padding: const EdgeInsets.only(top: 6, bottom: 6),
                    child: Scrollbar(
                      controller: details.controller,
                      thumbVisibility: false,
                      child: Transform.translate(
                        offset: const Offset(0, -0),
                        child: Padding(
                          padding: const EdgeInsets.only(bottom: 0),
                          child: child,
                        ),
                      ),
                    ),
                  );
                },
                indicatorBuilder:
                    (context, editingController, chunkController, notifier) {
                      return Row(
                        children: [
                          Padding(
                            padding: const EdgeInsets.only(left: 8, right: 8),
                            child: DefaultCodeLineNumber(
                              controller: editingController,
                              notifier: notifier,
                            ),
                          ),
                          const SizedBox(width: 16), // 分隔线1px + 右侧间距15px
                        ],
                      );
                    },
                style: CodeEditorStyle(
                  fontSize: 14,
                  fontFamily: GoogleFonts.notoSansMono().fontFamily,
                  selectionColor: Theme.of(
                    context,
                  ).colorScheme.primary.withValues(alpha: 0.3),
                  codeTheme: CodeHighlightTheme(
                    languages: {'yaml': CodeHighlightThemeMode(mode: langYaml)},
                    theme: Theme.of(context).brightness == Brightness.dark
                        ? githubDarkTheme
                        : atomOneDarkTheme,
                  ),
                ),
              ),
            ),
            // 加载中的占位提示
            if (!_editorReady)
              Container(
                color: isDark
                    ? Colors.white.withValues(alpha: 0.02)
                    : Colors.black.withValues(alpha: 0.02),
                child: Center(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      SizedBox(
                        width: 24,
                        height: 24,
                        child: CircularProgressIndicator(
                          strokeWidth: 2.5,
                          valueColor: AlwaysStoppedAnimation<Color>(
                            Theme.of(context).colorScheme.primary,
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),
                      Text(
                        context.translate.fileEditor.loading,
                        style: TextStyle(
                          fontSize: 12,
                          color: Theme.of(
                            context,
                          ).colorScheme.onSurface.withValues(alpha: 0.6),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            // 分隔线独立覆盖（固定在行号右侧）
            Positioned(
              left: 50, // 左边距5 + 行号宽度约40 + 右边距5
              top: 0,
              bottom: 0,
              child: Container(
                width: 1,
                color: Theme.of(
                  context,
                ).colorScheme.outline.withValues(alpha: 0.3),
              ),
            ),
          ],
        ),
      ),
    );
  }

  // 处理保存操作
  Future<void> _handleSave() async {
    setState(() {
      _isSaving = true;
    });

    try {
      final success = await widget.onSave(_controller.text);

      if (!mounted) return;

      if (success) {
        ModernToast.success(context, context.translate.fileEditor.saveSuccess);
        if (mounted) {
          Navigator.of(context).pop();
        }
      } else {
        setState(() {
          _isSaving = false;
        });
        ModernToast.error(context, context.translate.fileEditor.saveFailed);
      }
    } catch (e) {
      if (!mounted) return;

      setState(() {
        _isSaving = false;
      });

      Logger.error('保存文件失败: $e');
      ModernToast.error(
        context,
        context.translate.fileEditor.saveError.replaceAll(
          '{error}',
          e.toString(),
        ),
      );
    }
  }
}
