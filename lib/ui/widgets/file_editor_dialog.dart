import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:stelliberty/services/log_print_service.dart';
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

  // 保存回调函数（只读模式下可为 null）
  final Future<bool> Function(String content)? onSave;

  // 是否为只读模式
  final bool isReadOnly;

  // 自定义标题（可选，如果提供则使用此标题而不是默认的"文件编辑器"）
  final String? customTitle;

  // 是否隐藏副标题（文件名）
  final bool shouldHideSubtitle;

  const FileEditorDialog({
    super.key,
    required this.fileName,
    required this.initialContent,
    this.onSave,
    this.isReadOnly = false,
    this.customTitle,
    this.shouldHideSubtitle = false,
  });

  // 显示文件编辑器对话框
  static Future<void> show(
    BuildContext context, {
    required String fileName,
    required String initialContent,
    Future<bool> Function(String content)? onSave,
    bool readOnly = false,
    String? customTitle,
    bool hideSubtitle = false,
  }) {
    return showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (context) => FileEditorDialog(
        fileName: fileName,
        initialContent: initialContent,
        onSave: onSave,
        isReadOnly: readOnly,
        customTitle: customTitle,
        shouldHideSubtitle: hideSubtitle,
      ),
    );
  }

  @override
  State<FileEditorDialog> createState() => _FileEditorDialogState();
}

class _FileEditorDialogState extends State<FileEditorDialog> {
  late final CodeLineEditingController _controller;
  final TextEditingController _searchController = TextEditingController();

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

  // 标记是否已 dispose（防止异步操作在 dispose 后执行）
  bool _disposed = false;

  // 搜索相关
  int _searchResultCount = 0;
  int _currentSearchIndex = -1;
  final List<int> _searchPositions = [];

  @override
  void initState() {
    super.initState();

    // 先创建空编辑器（不添加 listener，避免触发修改检测）
    _controller = CodeLineEditingController.fromText('');

    // 监听搜索框变化，内容改变时清空搜索结果
    _searchController.addListener(_onSearchTextChanged);

    // 等对话框完全显示后再加载内容
    _loadContentAfterDialogReady();
  }

  // 搜索框文本变化回调
  void _onSearchTextChanged() {
    // 如果搜索框内容变化且之前有搜索结果，清空结果提示用户重新搜索
    if (_searchResultCount > 0) {
      setState(() {
        _searchResultCount = 0;
        _currentSearchIndex = -1;
        _searchPositions.clear();
      });
    }
  }

  // 等对话框加载完成后再异步填充内容
  Future<void> _loadContentAfterDialogReady() async {
    // 等待对话框动画完成（500ms）+ 缓冲时间
    await Future.delayed(const Duration(milliseconds: 500));

    if (_disposed || !mounted) return;

    // 加载文本内容
    await Future.microtask(() {
      if (_disposed || !mounted) return;
      _controller.text = widget.initialContent;
      _updateStats();
    });

    if (_disposed || !mounted) return;

    // 内容加载完成后，添加 listener 并显示编辑器
    SchedulerBinding.instance.addPostFrameCallback((_) {
      if (_disposed || !mounted) return;
      _controller.addListener(_onContentChanged);
      setState(() {
        _editorReady = true;
      });
    });
  }

  @override
  void dispose() {
    _disposed = true;
    // 移除 listener 再 dispose
    _controller.removeListener(_onContentChanged);
    _searchController.removeListener(_onSearchTextChanged);
    _controller.dispose();
    _searchController.dispose();
    super.dispose();
  }

  // 执行搜索
  void _performSearch() {
    final query = _searchController.text;

    if (query.isEmpty) {
      setState(() {
        _searchResultCount = 0;
        _currentSearchIndex = -1;
        _searchPositions.clear();
      });
      return;
    }

    final text = _controller.text;
    final lowerQuery = query.toLowerCase();
    final lowerText = text.toLowerCase();

    _searchPositions.clear();
    int index = 0;
    while (index < text.length) {
      final foundIndex = lowerText.indexOf(lowerQuery, index);
      if (foundIndex == -1) break;
      _searchPositions.add(foundIndex);
      index = foundIndex + 1;
    }

    setState(() {
      _searchResultCount = _searchPositions.length;
      // 如果已经有搜索结果，循环到下一个；否则从第一个开始
      if (_searchPositions.isNotEmpty) {
        if (_currentSearchIndex == -1) {
          _currentSearchIndex = 0;
        } else {
          _currentSearchIndex =
              (_currentSearchIndex + 1) % _searchPositions.length;
        }
        // 跳转到搜索结果位置
        _jumpToSearchResult();
      } else {
        _currentSearchIndex = -1;
      }
    });
  }

  // 跳转到下一个搜索结果
  void _goToNextResult() {
    if (_searchPositions.isEmpty) return;
    setState(() {
      _currentSearchIndex = (_currentSearchIndex + 1) % _searchPositions.length;
    });
    _jumpToSearchResult();
  }

  // 跳转到上一个搜索结果
  void _goToPreviousResult() {
    if (_searchPositions.isEmpty) return;
    setState(() {
      _currentSearchIndex =
          (_currentSearchIndex - 1 + _searchPositions.length) %
          _searchPositions.length;
    });
    _jumpToSearchResult();
  }

  // 跳转到当前搜索结果并选中文本
  void _jumpToSearchResult() {
    if (_currentSearchIndex < 0 ||
        _currentSearchIndex >= _searchPositions.length) {
      return;
    }

    final position = _searchPositions[_currentSearchIndex];
    final queryLength = _searchController.text.length;

    // 计算目标位置所在的行号和列号
    final textBeforePosition = _controller.text.substring(0, position);
    final lineNumber = '\n'.allMatches(textBeforePosition).length;
    final lastLineBreak = textBeforePosition.lastIndexOf('\n');
    final columnInLine = lastLineBreak == -1
        ? position
        : position - lastLineBreak - 1;

    // 设置光标位置并选中文本
    final endPosition = position + queryLength;
    final textBeforeEnd = _controller.text.substring(0, endPosition);
    final endLineNumber = '\n'.allMatches(textBeforeEnd).length;
    final endLastLineBreak = textBeforeEnd.lastIndexOf('\n');
    final endColumnInLine = endLastLineBreak == -1
        ? endPosition
        : endPosition - endLastLineBreak - 1;

    // 使用 CodeLineSelection 设置选中范围
    final selection = CodeLineSelection(
      baseIndex: lineNumber,
      baseOffset: columnInLine,
      extentIndex: endLineNumber,
      extentOffset: endColumnInLine,
    );

    _controller.selection = selection;

    // 确保选中的文本可见（滚动到视图内）
    SchedulerBinding.instance.addPostFrameCallback((_) {
      if (_disposed || !mounted) return;

      // 使用 makePositionVisible 方法滚动到目标行
      try {
        _controller.makePositionVisible(
          CodeLinePosition(index: lineNumber, offset: columnInLine),
        );
      } catch (e) {
        Logger.error('滚动到搜索结果失败: $e');
      }
    });
  }

  // 内容变化回调
  void _onContentChanged() {
    // 只读模式下不跟踪修改
    if (_disposed || widget.isReadOnly) return;
    final isModified = _controller.text != widget.initialContent;
    if (isModified != _isModified) {
      SchedulerBinding.instance.addPostFrameCallback((_) {
        if (_disposed || !mounted) return;
        setState(() {
          _isModified = isModified;
        });
      });
    }
    _updateStats();
  }

  // 更新统计数据（字符数和行数）
  // 使用缓存避免频繁重新计算和不必要的 setState
  void _updateStats() {
    if (_disposed) return;
    final text = _controller.text;
    final newCharCount = text.length;
    final newLineCount = text.split('\n').length;

    if (newCharCount != _charCount || newLineCount != _lineCount) {
      SchedulerBinding.instance.addPostFrameCallback((_) {
        if (_disposed || !mounted) return;
        setState(() {
          _charCount = newCharCount;
          _lineCount = newLineCount;
        });
      });
    }
  }

  // 根据行数动态计算左侧间距，避免行号被遮挡
  double _calculateLeftPadding(int lineCount) {
    // 基础间距
    const baseLeftPadding = 8.0;
    // 根据位数增加额外间距（每增加一位数字，增加 2px）
    final digits = _getDigits(lineCount);
    return baseLeftPadding + (digits > 2 ? (digits - 2) * 2.0 : 0);
  }

  // 计算行数的位数
  int _getDigits(int number) {
    if (number <= 0) return 1;
    return number.toString().length;
  }

  // 根据行数动态计算分隔线位置
  double _calculateSeparatorPosition(int lineCount) {
    // 基础位置（适用于 2 位数，即 10-99 行）
    // 编辑器左侧 padding 5px + 行号左侧间距 8px + 行号宽度约 20px + 行号右侧间距 0px + SizedBox 12px 中的 4px = 37px
    const basePosition = 37.0;
    // 根据位数调整
    // 每增加一位数字：左侧间距增加 2px + 数字宽度约 8px = 总共右移 10px
    final digits = _getDigits(lineCount);
    return basePosition + (digits - 2) * 10.0;
  }

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return ModernDialog(
      title: widget.customTitle ?? trans.file_editor.title,
      subtitle: widget.fileName,
      shouldHideSubtitle: widget.shouldHideSubtitle,
      titleIcon: Icons.code,
      isModified: _isModified,
      maxWidth: 900,
      maxHeightRatio: 0.9,
      headerWidget: _buildEnhancedSearchBox(),
      content: _buildEditor(),
      actionsLeft: Text(
        trans.file_editor.stats
            .replaceAll('{chars}', _charCount.toString())
            .replaceAll('{lines}', _lineCount.toString()),
        style: TextStyle(
          fontSize: 12,
          color: Theme.of(context).colorScheme.onSurface.withValues(alpha: 0.6),
        ),
      ),
      actionsRight: [
        if (!widget.isReadOnly) ...[
          DialogActionButton(
            label: trans.file_editor.cancel_button,
            isPrimary: false,
            onPressed: _isSaving ? null : () => Navigator.of(context).pop(),
          ),
          DialogActionButton(
            label: _isSaving
                ? trans.file_editor.saving_button
                : trans.file_editor.save_button,
            isPrimary: true,
            isLoading: _isSaving,
            onPressed: (_isSaving || !_isModified) ? null : _handleSave,
          ),
        ] else
          DialogActionButton(
            label: trans.common.close,
            isPrimary: true,
            onPressed: () => Navigator.of(context).pop(),
          ),
      ],
      onClose: () => Navigator.of(context).pop(),
    );
  }

  // 构建增强的搜索框（带搜索按钮和导航按钮）
  Widget _buildEnhancedSearchBox() {
    final isDark = Theme.of(context).brightness == Brightness.dark;

    return Row(
      children: [
        Expanded(
          child: Material(
            color: Colors.transparent,
            child: TextField(
              controller: _searchController,
              onSubmitted: (_) => _performSearch(),
              decoration: InputDecoration(
                hintText: '搜索文本内容',
                prefixIcon: Icon(
                  Icons.search,
                  size: 20,
                  color: Theme.of(
                    context,
                  ).colorScheme.onSurface.withValues(alpha: 0.5),
                ),
                suffixIcon: _searchController.text.isNotEmpty
                    ? IconButton(
                        icon: Icon(
                          Icons.clear,
                          size: 20,
                          color: Theme.of(
                            context,
                          ).colorScheme.onSurface.withValues(alpha: 0.5),
                        ),
                        onPressed: () {
                          _searchController.clear();
                          _performSearch();
                        },
                      )
                    : null,
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(10),
                  borderSide: BorderSide.none,
                ),
                filled: true,
                fillColor: isDark
                    ? Colors.white.withValues(alpha: 0.05)
                    : Colors.white.withValues(alpha: 0.5),
                contentPadding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 12,
                ),
                hintStyle: TextStyle(
                  fontSize: 14,
                  color: Theme.of(
                    context,
                  ).colorScheme.onSurface.withValues(alpha: 0.5),
                ),
              ),
              style: TextStyle(
                fontSize: 14,
                color: Theme.of(context).colorScheme.onSurface,
              ),
            ),
          ),
        ),
        const SizedBox(width: 8),
        // 搜索按钮
        IconButton(
          onPressed: _performSearch,
          icon: const Icon(Icons.search, size: 20),
          tooltip: '搜索',
          style: IconButton.styleFrom(
            backgroundColor: Theme.of(
              context,
            ).colorScheme.primary.withValues(alpha: 0.1),
          ),
        ),
        const SizedBox(width: 4),
        // 上一个结果
        IconButton(
          onPressed: _searchPositions.isEmpty ? null : _goToPreviousResult,
          icon: const Icon(Icons.keyboard_arrow_up, size: 20),
          tooltip: '上一个',
          style: IconButton.styleFrom(
            backgroundColor: isDark
                ? Colors.white.withValues(alpha: 0.05)
                : Colors.white.withValues(alpha: 0.5),
          ),
        ),
        const SizedBox(width: 4),
        // 下一个结果
        IconButton(
          onPressed: _searchPositions.isEmpty ? null : _goToNextResult,
          icon: const Icon(Icons.keyboard_arrow_down, size: 20),
          tooltip: '下一个',
          style: IconButton.styleFrom(
            backgroundColor: isDark
                ? Colors.white.withValues(alpha: 0.05)
                : Colors.white.withValues(alpha: 0.5),
          ),
        ),
        if (_searchResultCount > 0) ...[
          const SizedBox(width: 12),
          Text(
            '${_currentSearchIndex + 1}/$_searchResultCount',
            style: TextStyle(
              fontSize: 12,
              color: Theme.of(
                context,
              ).colorScheme.onSurface.withValues(alpha: 0.7),
            ),
          ),
        ],
      ],
    );
  }

  // 构建代码编辑器
  Widget _buildEditor() {
    final trans = context.translate;
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
                readOnly: widget.isReadOnly,
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
                      // 根据行数动态计算左侧间距，避免行号被遮挡
                      final leftPadding = _calculateLeftPadding(_lineCount);
                      return Row(
                        children: [
                          Padding(
                            padding: EdgeInsets.only(
                              left: leftPadding,
                              right: 0,
                            ),
                            child: DefaultCodeLineNumber(
                              controller: editingController,
                              notifier: notifier,
                            ),
                          ),
                          const SizedBox(width: 14), // 行号右侧间距 + 分隔线 + 文本左侧间距
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
                        trans.file_editor.loading,
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
            // 分隔线独立覆盖（固定在行号右侧，根据行数动态调整位置）
            // 和编辑器同步淡入，避免视觉不一致
            Positioned(
              left: _calculateSeparatorPosition(_lineCount),
              top: 0,
              bottom: 0,
              child: AnimatedOpacity(
                opacity: _editorReady ? 1.0 : 0.0,
                duration: const Duration(milliseconds: 300),
                curve: Curves.easeIn,
                child: Container(
                  width: 1,
                  color: Theme.of(
                    context,
                  ).colorScheme.outline.withValues(alpha: 0.3),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  // 处理保存操作
  Future<void> _handleSave() async {
    final trans = context.translate;
    // 只读模式或无保存回调时不执行保存
    if (widget.onSave == null) return;

    setState(() {
      _isSaving = true;
    });

    try {
      final success = await widget.onSave!(_controller.text);

      if (!mounted) return;

      if (success) {
        ModernToast.success(trans.file_editor.save_success);
        Navigator.of(context).pop();
      } else {
        setState(() {
          _isSaving = false;
        });
        ModernToast.error(trans.file_editor.save_failed);
      }
    } catch (e) {
      if (!mounted) return;

      setState(() {
        _isSaving = false;
      });

      Logger.error('保存文件失败: $e');
      ModernToast.error(
        trans.file_editor.save_error.replaceAll('{error}', e.toString()),
      );
    }
  }
}
