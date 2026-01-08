import 'dart:convert';
import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:stelliberty/clash/state/override_states.dart';
import 'package:stelliberty/clash/model/override_model.dart';
import 'package:stelliberty/clash/services/override_service.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 全局覆写管理 Provider
class OverrideProvider extends ChangeNotifier {
  final OverrideService _service;

  // 覆写状态（Provider 直接管理）
  OverrideState _state = OverrideState.idle();
  OverrideState get overrideState => _state;

  // 更新状态并通知
  void _updateState(OverrideState newState) {
    _state = newState;
    notifyListeners();
  }

  // 全局覆写列表
  List<OverrideConfig> _overrides = [];
  List<OverrideConfig> get overrides => _overrides;

  // 覆写删除回调（通知订阅系统清理引用）
  Future<void> Function(String overrideId)? _onOverrideDeleted;

  // 覆写内容更新回调（通知订阅系统重载配置）
  Future<void> Function(String overrideId)? _onOverrideContentUpdated;

  // 设置覆写删除回调
  void setOnOverrideDeleted(Future<void> Function(String) callback) {
    _onOverrideDeleted = callback;
    Logger.debug('已设置覆写删除回调');
  }

  // 设置覆写内容更新回调
  void setOnOverrideContentUpdated(Future<void> Function(String) callback) {
    _onOverrideContentUpdated = callback;
    Logger.debug('已设置覆写内容更新回调');
  }

  bool get isLoading => _state.isLoading;
  bool get isBatchUpdatingOverrides => _state.isBatchUpdating;
  String? get errorMessage => _state.errorMessage;

  // 检查指定覆写是否正在更新
  bool isOverrideUpdating(String overrideId) {
    return _state.isOverrideUpdating(overrideId);
  }

  OverrideProvider(this._service);

  // 初始化 Provider
  Future<void> initialize() async {
    _updateState(
      _state.copyWith(
        operationState: OverrideOperationState.loading,
        clearError: true,
      ),
    ); // '初始化覆写管理');
    notifyListeners();

    try {
      // 加载覆写列表
      _overrides = await _loadOverrideList();

      Logger.info('覆写 Provider 初始化成功，共 ${_overrides.length} 个覆写');
      _updateState(OverrideState.idle()); // '初始化完成');
    } catch (e) {
      final errorMsg = '初始化覆写失败: $e';
      Logger.error(errorMsg);
      _overrides = [];
      _updateState(
        _state.copyWith(
          errorState: OverrideErrorState.initializationError,
          errorMessage: errorMsg,
        ),
      );
    } finally {
      notifyListeners();
    }
  }

  // 添加覆写
  Future<bool> addOverride(OverrideConfig override) async {
    Logger.info('开始添加覆写');
    Logger.info('覆写名称：${override.name}');
    Logger.info('覆写类型：${override.type.displayName}');
    Logger.info('覆写格式：${override.format.displayName}');
    Logger.info('覆写 ID：${override.id}');

    try {
      // 如果是远程覆写，先下载内容
      if (override.type == OverrideType.remote) {
        Logger.info('处理远程覆写：${override.name}');
        Logger.info('URL：${override.url}');

        try {
          final content = await _service.downloadRemoteOverride(override);
          Logger.info('远程覆写下载成功，内容长度：${content.length}');

          // 下载成功，更新覆写配置
          final updatedOverride = override.copyWith(
            content: content,
            lastUpdate: DateTime.now(),
          );

          _overrides.add(updatedOverride);
          await _saveOverrideList();
          notifyListeners();
          Logger.info('添加远程覆写成功：${override.name}');
          return true;
        } catch (e) {
          Logger.error('下载远程覆写失败：${override.name} - $e');
          return false;
        }
      }

      // 本地覆写需要保存文件
      Logger.info('处理本地覆写：${override.name}');
      Logger.info('源文件路径 (url)：${override.url}');
      Logger.info('源文件路径 (localPath): ${override.localPath}');
      Logger.info('内容 (content): ${override.content != null ? "已提供" : "null"}');

      try {
        String content;

        // 检查是否是新建空白文件（content 不为 null）
        if (override.content != null) {
          Logger.info('新建空白覆写文件');
          // 直接使用提供的内容（可能是空字符串）
          content = override.content!;
          // 保存到覆写目录
          await _service.saveOverrideContent(override, content);
          Logger.info('空白覆写文件创建成功');
        } else {
          // 导入本地文件模式
          final sourceFilePath = override.localPath ?? override.url;

          if (sourceFilePath == null || sourceFilePath.isEmpty) {
            Logger.error('本地覆写源文件路径为空');
            Logger.error('url 字段：${override.url}');
            Logger.error('localPath 字段：${override.localPath}');
            return false;
          }

          // 从源文件复制到覆写目录
          Logger.info('调用 saveLocalOverride 保存文件，源路径：$sourceFilePath');
          content = await _service.saveLocalOverride(override, sourceFilePath);
          Logger.info('本地覆写文件保存成功，内容长度：${content.length}');
        }

        // 更新覆写配置（添加内容和更新时间）
        final updatedOverride = override.copyWith(
          content: content,
          lastUpdate: DateTime.now(),
        );

        _overrides.add(updatedOverride);
        Logger.info('覆写已添加到列表');

        await _saveOverrideList();
        Logger.info('覆写列表已保存');

        notifyListeners();
        Logger.info('添加本地覆写成功：${override.name}');
        return true;
      } catch (e) {
        Logger.error('保存本地覆写文件失败：${override.name} - $e');
        return false;
      }
    } catch (e) {
      Logger.error('添加覆写失败：${override.name} - $e');
      return false;
    }
  }

  // 更新覆写信息
  Future<bool> updateOverride(
    String overrideId,
    OverrideConfig updatedOverride,
  ) async {
    final index = _overrides.indexWhere((o) => o.id == overrideId);
    if (index == -1) {
      Logger.error('更新失败：覆写不存在 (ID：$overrideId)');
      return false;
    }

    try {
      _overrides[index] = updatedOverride;
      await _saveOverrideList();
      notifyListeners();
      Logger.info('更新覆写成功：${updatedOverride.name}');
      return true;
    } catch (e) {
      Logger.error('更新覆写失败：${updatedOverride.name} - $e');
      return false;
    }
  }

  // 更新远程覆写内容
  Future<bool> updateRemoteOverride(String overrideId) async {
    final index = _overrides.indexWhere((o) => o.id == overrideId);
    if (index == -1) {
      Logger.error('更新失败：覆写不存在 (ID：$overrideId)');
      return false;
    }

    final override = _overrides[index];

    if (override.type != OverrideType.remote) {
      Logger.info('本地覆写无需更新：${override.name}');
      return true;
    }

    // 添加到更新中列表
    _updateState(
      _state.copyWith(updatingIds: {..._state.updatingIds, overrideId}),
    ); // '开始更新远程覆写');
    notifyListeners();

    try {
      Logger.info('开始更新远程覆写：${override.name}');

      // 下载远程覆写
      final content = await _service.downloadRemoteOverride(override);

      // 更新覆写配置
      _overrides[index] = override.copyWith(
        content: content,
        lastUpdate: DateTime.now(),
      );

      await _saveOverrideList();
      Logger.info('更新远程覆写成功：${override.name}');
      return true;
    } catch (e) {
      Logger.error('更新远程覆写失败：${override.name} - $e');
      _updateState(
        _state.copyWith(
          errorState: OverrideErrorState.networkError,
          errorMessage: '更新远程覆写失败: $e',
        ),
      );
      return false;
    } finally {
      _updateState(
        _state.copyWith(
          updatingIds: _state.updatingIds
              .where((id) => id != overrideId)
              .toSet(),
        ),
      ); // '更新完成');
      notifyListeners();
    }
  }

  // 删除覆写
  Future<bool> deleteOverride(String overrideId) async {
    final index = _overrides.indexWhere((o) => o.id == overrideId);
    if (index == -1) {
      Logger.error('删除失败：覆写不存在 (ID：$overrideId)');
      return false;
    }

    final override = _overrides[index];

    try {
      // 从列表中移除
      _overrides.removeAt(index);
      await _saveOverrideList();

      // 删除覆写文件
      await _service.deleteOverride(override.id, override.format);

      // 通知订阅系统清理引用
      if (_onOverrideDeleted != null) {
        try {
          await _onOverrideDeleted!(overrideId);
          Logger.debug('已通知订阅系统清理覆写引用：$overrideId');
        } catch (e) {
          Logger.warning('清理订阅引用失败：$e');
        }
      }

      notifyListeners();
      Logger.info('删除覆写成功：${override.name}');
      return true;
    } catch (e) {
      Logger.error('删除覆写失败：${override.name} - $e');
      return false;
    }
  }

  // 重新排序覆写
  //
  // [autoAdjust] 是否自动调整索引（默认 true）
  // - true: 用于 ReorderableListView（需要调整插入点）
  // - false: 用于 GridView DragTarget（目标索引即实际位置）
  Future<bool> reorderOverrides(
    int oldIndex,
    int newIndex, {
    bool autoAdjust = true,
  }) async {
    try {
      // 根据需要调整 newIndex
      if (autoAdjust && oldIndex < newIndex) {
        newIndex -= 1;
      }

      final item = _overrides.removeAt(oldIndex);
      _overrides.insert(newIndex, item);
      await _saveOverrideList();
      notifyListeners();
      Logger.debug('覆写重新排序：$oldIndex -> $newIndex');
      return true;
    } catch (e) {
      Logger.error('重新排序覆写失败：$e');
      return false;
    }
  }

  // 加载覆写列表
  Future<List<OverrideConfig>> _loadOverrideList() async {
    try {
      final listPath = PathService.instance.overrideListPath;
      final listFile = File(listPath);

      if (!await listFile.exists()) {
        Logger.info('覆写列表文件不存在，返回空列表');
        return [];
      }

      final content = await listFile.readAsString();
      final jsonData = jsonDecode(content) as Map<String, dynamic>;
      final overridesJson = jsonData['overrides'] as List;

      final overrides = overridesJson
          .map((json) => OverrideConfig.fromJson(json))
          .toList();

      Logger.info('已加载覆写列表，共 ${overrides.length} 个覆写');

      // 修复：并发读取所有覆写文件内容
      Logger.debug('开始并发读取覆写文件内容...');
      final loadedOverrides = await Future.wait(
        overrides.map((override) async {
          try {
            final fileContent = await _service.getOverrideContent(
              override.id,
              override.format,
            );

            if (fileContent.isNotEmpty) {
              Logger.debug(
                '读取覆写 ${override.name} 内容: ${fileContent.length} 字符',
              );
              return override.copyWith(content: fileContent);
            } else {
              Logger.warning('覆写 ${override.name} 的文件内容为空');
              return override;
            }
          } catch (e) {
            Logger.error('读取覆写 ${override.name} 文件失败：$e');
            return override;
          }
        }),
      );

      Logger.info('覆写列表加载完成，共 ${loadedOverrides.length} 个覆写（含 content）');
      return loadedOverrides;
    } catch (e) {
      Logger.error('加载覆写列表失败：$e');
      return [];
    }
  }

  // 保存覆写列表
  Future<void> _saveOverrideList() async {
    try {
      final listPath = PathService.instance.overrideListPath;
      final listFile = File(listPath);

      // 确保目录存在
      await listFile.parent.create(recursive: true);

      final jsonData = {
        'overrides': _overrides.map((o) => o.toJson()).toList(),
      };

      await listFile.writeAsString(
        const JsonEncoder.withIndent('  ').convert(jsonData),
      );
      Logger.debug('已保存覆写列表，共 ${_overrides.length} 个覆写');
    } catch (e) {
      Logger.error('保存覆写列表失败：$e');
      rethrow;
    }
  }

  // 批量更新所有远程覆写
  Future<List<String>> updateAllRemoteOverrides() async {
    final errors = <String>[];

    // 筛选出远程覆写
    final remoteOverrides = _overrides
        .where((o) => o.type == OverrideType.remote)
        .toList();

    if (remoteOverrides.isEmpty) {
      Logger.info('没有远程覆写需要更新');
      return errors;
    }

    _updateState(
      _state.copyWith(
        operationState: OverrideOperationState.batchUpdating,
        updateTotal: remoteOverrides.length,
        updateCurrent: 0,
      ),
    );
    notifyListeners();

    Logger.info('开始批量更新 ${remoteOverrides.length} 个远程覆写');

    try {
      // 并发更新所有远程覆写
      final results = await Future.wait(
        remoteOverrides.map((override) async {
          final success = await updateRemoteOverride(override.id);
          if (!success) {
            return '${override.name}: 更新失败';
          }
          return null;
        }),
      );

      // 收集错误
      for (final error in results) {
        if (error != null) {
          errors.add(error);
        }
      }

      Logger.info(
        '批量更新完成: 成功=${remoteOverrides.length - errors.length}, 失败=${errors.length}',
      );

      // 批量更新完成后，检查当前订阅是否使用了任何已更新的覆写
      if (_onOverrideContentUpdated != null) {
        final updatedOverrideIds = remoteOverrides
            .where((o) => !errors.any((err) => err.contains(o.name)))
            .map((o) => o.id)
            .toList();

        if (updatedOverrideIds.isNotEmpty) {
          Logger.info('批量更新成功 ${updatedOverrideIds.length} 个覆写，触发配置重载检查');
          for (final overrideId in updatedOverrideIds) {
            await _onOverrideContentUpdated!(overrideId);
          }
        }
      }
    } finally {
      _updateState(OverrideState.idle()); // '批量更新完成');
      notifyListeners();
    }

    return errors;
  }

  // 保存覆写文件内容（用于编辑后保存）
  Future<void> saveOverrideFileContent(
    OverrideConfig override,
    String content,
  ) async {
    try {
      Logger.info('保存覆写文件内容：${override.name}');
      await _service.saveOverrideContent(override, content);
      Logger.info('覆写文件内容保存成功');

      // 通知订阅系统：如果当前订阅使用了这个覆写，需要重载配置
      if (_onOverrideContentUpdated != null) {
        Logger.debug('触发覆写内容更新回调：${override.id}');
        await _onOverrideContentUpdated!(override.id);
      }
    } catch (e) {
      Logger.error('保存覆写文件内容失败：${override.name} - $e');
      rethrow;
    }
  }

  // 根据 ID 获取覆写
  OverrideConfig? getOverrideById(String id) {
    try {
      return _overrides.firstWhere((o) => o.id == id);
    } catch (_) {
      return null;
    }
  }

  @override
  void dispose() {
    // 清理回调，避免内存泄漏
    _onOverrideDeleted = null;
    super.dispose();
  }
}
