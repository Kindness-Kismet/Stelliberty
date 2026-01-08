import 'dart:io';
import 'package:flutter/material.dart';
import 'package:file_picker/file_picker.dart';
import 'package:desktop_drop/desktop_drop.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 文件选择结果
class FileSelectionResult {
  final File file;
  final String fileName;

  const FileSelectionResult({required this.file, required this.fileName});
}

// 文件选择器组件
// 支持拖拽和点击选择文件
// 特性：
// - 毛玻璃背景效果
// - 拖拽导入支持
// - 文件状态显示
// - 文件存在性验证
class FileSelectorWidget extends StatefulWidget {
  // 选择文件后的回调
  final ValueChanged<FileSelectionResult> onFileSelected;

  // 初始选中的文件
  final FileSelectionResult? initialFile;

  // 提示文本（未选择时）
  final String hintText;

  // 已选择文件的提示文本
  final String selectedText;

  // 拖拽时的提示文本
  final String draggingText;

  // 拖拽说明文本
  final String dragHintText;

  // 允许的文件类型（null 表示所有类型）
  final FileType fileType;

  // 允许的文件扩展名（仅当 fileType 为 custom 时使用）
  final List<String>? allowedExtensions;

  const FileSelectorWidget({
    super.key,
    required this.onFileSelected,
    this.initialFile,
    required this.hintText,
    required this.selectedText,
    required this.draggingText,
    required this.dragHintText,
    this.fileType = FileType.any,
    this.allowedExtensions,
  });

  @override
  State<FileSelectorWidget> createState() => _FileSelectorWidgetState();
}

class _FileSelectorWidgetState extends State<FileSelectorWidget> {
  FileSelectionResult? _selectedFile;
  bool _isDragging = false;

  @override
  void initState() {
    super.initState();
    _selectedFile = widget.initialFile;
  }

  @override
  Widget build(BuildContext context) {
    return DropTarget(
      onDragEntered: (details) {
        setState(() {
          _isDragging = true;
        });
      },
      onDragExited: (details) {
        setState(() {
          _isDragging = false;
        });
      },
      onDragDone: (details) async {
        setState(() {
          _isDragging = false;
        });
        final paths = details.files.map((file) => file.path).toList();
        await _handleDroppedFiles(paths);
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
                    color: _isDragging
                        ? Theme.of(context).colorScheme.primary
                        : (_selectedFile != null
                              ? Theme.of(context).colorScheme.primary
                              : Theme.of(
                                  context,
                                ).colorScheme.onSurface.withValues(alpha: 0.6)),
                  ),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          _isDragging
                              ? widget.draggingText
                              : (_selectedFile != null
                                    ? widget.selectedText
                                    : widget.hintText),
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
                          _isDragging
                              ? widget.dragHintText
                              : (_selectedFile != null
                                    ? _selectedFile!.fileName
                                    : widget.dragHintText),
                          style: TextStyle(
                            color: _isDragging
                                ? Theme.of(context).colorScheme.primary
                                : (_selectedFile != null
                                      ? Theme.of(context).colorScheme.primary
                                      : Theme.of(context).colorScheme.onSurface
                                            .withValues(alpha: 0.5)),
                            fontSize: 12,
                            fontWeight: _selectedFile != null || _isDragging
                                ? FontWeight.w500
                                : FontWeight.normal,
                          ),
                        ),
                      ],
                    ),
                  ),
                  Icon(
                    _isDragging
                        ? Icons.download
                        : (_selectedFile != null
                              ? Icons.edit
                              : Icons.folder_open),
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

  // 选择文件
  Future<void> _selectFile() async {
    try {
      FilePickerResult? result = await FilePicker.platform.pickFiles(
        type: widget.fileType,
        allowedExtensions: widget.allowedExtensions,
        allowMultiple: false,
      );

      if (result != null && result.files.single.path != null) {
        final file = File(result.files.single.path!);
        final fileName = result.files.single.name;

        // 验证文件是否存在和可读
        if (await file.exists()) {
          final fileResult = FileSelectionResult(
            file: file,
            fileName: fileName,
          );
          setState(() {
            _selectedFile = fileResult;
          });
          widget.onFileSelected(fileResult);
        } else {
          throw Exception('文件不存在或无法访问');
        }
      }
    } catch (e) {
      Logger.debug('文件选择失败: $e');
    }
  }

  // 处理拖拽文件
  Future<void> _handleDroppedFiles(List<String> paths) async {
    if (paths.isEmpty) return;

    try {
      // 只处理第一个文件
      final filePath = paths.first;
      final file = File(filePath);

      // 验证文件是否存在
      if (!await file.exists()) {
        Logger.warning('拖拽的文件不存在: $filePath');
        return;
      }

      final fileName = filePath.split(Platform.pathSeparator).last;

      final fileResult = FileSelectionResult(file: file, fileName: fileName);

      setState(() {
        _selectedFile = fileResult;
      });
      widget.onFileSelected(fileResult);

      Logger.debug('通过拖拽选择文件: $fileName');
    } catch (e) {
      Logger.error('处理拖拽文件失败: $e');
    }
  }
}
