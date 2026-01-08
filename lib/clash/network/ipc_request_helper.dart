import 'dart:async';
import 'dart:convert';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/services/log_print_service.dart';

// IPC 请求超时配置
class _IpcTimeouts {
  // 快速查询操作（GET）：8 秒
  // 注：延迟测试需要 5 秒 + IPC 往返 ~2 秒 + 缓冲 1 秒
  static const Duration quick = Duration(seconds: 8);

  // 普通操作（POST/PATCH/DELETE）：15 秒
  // 注：增加缓冲避免边界超时
  static const Duration normal = Duration(seconds: 15);

  // 长操作（PUT 配置更新）：30 秒
  static const Duration long = Duration(seconds: 30);
}

// IPC 重试配置
class _IpcRetryConfig {
  // 最大重试次数
  static const int maxRetries = 3;

  // 重试间隔
  static const Duration retryDelay = Duration(seconds: 1);

  // 判断是否应该重试（排除超时和 IPC 未就绪）
  static bool shouldRetry(dynamic error) {
    final errorMsg = error.toString();

    // 超时不重试（已经等了足够久）
    if (error is TimeoutException) {
      return false;
    }

    // IPC 未就绪不重试（等待 Clash 启动）
    if (_isIpcNotReadyError(errorMsg)) {
      return false;
    }

    // 其他错误（如连接断开、网络问题）可以重试
    return true;
  }
}

// 检查是否为 IPC 尚未就绪的错误（启动时的正常情况）
bool _isIpcNotReadyError(String errorMsg) {
  // Windows: os error 2 (系统找不到指定的文件)
  // Linux: os error 111 (ECONNREFUSED，拒绝连接)
  // macOS: os error 61 (ECONNREFUSED，拒绝连接)
  return errorMsg.contains('系统找不到指定的文件') ||
      errorMsg.contains('os error 2') ||
      errorMsg.contains('拒绝连接') ||
      errorMsg.contains('os error 111') ||
      errorMsg.contains('os error 61') ||
      errorMsg.contains('Connection refused');
}

// IPC 请求辅助类
//
// 简化 Dart → Rust IPC 请求/响应处理
class IpcRequestHelper {
  static final IpcRequestHelper _instance = IpcRequestHelper._();
  IpcRequestHelper._() {
    // 监听所有 IPC 响应
    _startResponseListener();
  }
  static IpcRequestHelper get instance => _instance;

  // 等待响应的 Completer 映射（使用请求 ID 精准匹配）
  int _nextId = 0;
  final Map<int, Completer<IpcResponse>> _pendingRequests = {};

  // 获取下一个请求 ID（防止溢出）
  int _getNextId() {
    final id = _nextId;
    // 使用模运算防止溢出（Dart 安全整数范围：2^53）
    _nextId = (_nextId + 1) % (1 << 53);
    return id;
  }

  // 监听 IPC 响应
  void _startResponseListener() {
    IpcResponse.rustSignalStream.listen((signalPack) {
      // 从 RustSignalPack 中提取实际的消息
      final response = signalPack.message;

      // 使用 request_id 精准匹配（修复乱序问题）
      final completer = _pendingRequests.remove(response.requestId);
      if (completer != null) {
        completer.complete(response);
      } else {
        Logger.warning('收到未知请求 ID 的响应：${response.requestId}');
      }
    });
  }

  // 通用重试包装器
  Future<T> _retryRequest<T>(
    Future<T> Function() request, {
    int maxRetries = _IpcRetryConfig.maxRetries,
  }) async {
    int attempt = 0;
    while (true) {
      try {
        return await request();
      } catch (e) {
        attempt++;

        // 判断是否应该重试
        if (attempt >= maxRetries || !_IpcRetryConfig.shouldRetry(e)) {
          rethrow;
        }

        // 记录重试
        Logger.warning(
          'IPC 请求失败，${_IpcRetryConfig.retryDelay.inSeconds}秒后重试（$attempt/$maxRetries）：$e',
        );

        // 等待后重试
        await Future.delayed(_IpcRetryConfig.retryDelay);
      }
    }
  }

  // 发送 GET 请求
  Future<Map<String, dynamic>> get(String path) async {
    return _retryRequest(() async {
      final completer = Completer<IpcResponse>();
      final id = _getNextId();
      _pendingRequests[id] = completer;

      try {
        // 发送请求（带 request_id）
        IpcGetRequest(requestId: id, path: path).sendSignalToRust();

        // 等待响应（8秒超时 - 快速查询）
        final response = await completer.future.timeout(_IpcTimeouts.quick);

        if (!response.isSuccessful) {
          throw Exception(response.errorMessage ?? 'IPC 请求失败');
        }

        // 解析 JSON 响应体
        if (response.body.isEmpty) {
          return {};
        }
        return json.decode(response.body) as Map<String, dynamic>;
      } on TimeoutException {
        _pendingRequests.remove(id);
        Logger.error('IPC GET 请求超时（8秒）：$path');
        rethrow;
      } catch (e) {
        _pendingRequests.remove(id);

        // 区分 IPC 未就绪（正常等待）和真正的错误
        final errorMsg = e.toString();
        if (_isIpcNotReadyError(errorMsg)) {
          // IPC 尚未就绪，静默处理（不打印日志）
        } else {
          Logger.error('IPC GET 请求失败：$path，error：$e');
        }
        rethrow;
      }
    });
  }

  // 发送 POST 请求
  Future<Map<String, dynamic>> post(
    String path, {
    Map<String, dynamic>? body,
  }) async {
    return _retryRequest(() async {
      final completer = Completer<IpcResponse>();
      final id = _getNextId();
      _pendingRequests[id] = completer;

      try {
        final bodyStr = body != null ? json.encode(body) : null;
        IpcPostRequest(
          requestId: id,
          path: path,
          body: bodyStr,
        ).sendSignalToRust();

        // 等待响应（15秒超时 - 普通操作）
        final response = await completer.future.timeout(_IpcTimeouts.normal);

        if (!response.isSuccessful) {
          throw Exception(response.errorMessage ?? 'IPC 请求失败');
        }

        if (response.body.isEmpty) {
          return {};
        }
        return json.decode(response.body) as Map<String, dynamic>;
      } on TimeoutException {
        _pendingRequests.remove(id);
        Logger.error('IPC POST 请求超时（15秒）：$path');
        rethrow;
      } catch (e) {
        _pendingRequests.remove(id);

        final errorMsg = e.toString();
        if (_isIpcNotReadyError(errorMsg)) {
          // IPC 尚未就绪，静默处理
        } else {
          Logger.error('IPC POST 请求失败：$path，error：$e');
        }
        rethrow;
      }
    });
  }

  // 发送 PUT 请求
  Future<Map<String, dynamic>> put(
    String path, {
    Map<String, dynamic>? body,
  }) async {
    return _retryRequest(() async {
      final completer = Completer<IpcResponse>();
      final id = _getNextId();
      _pendingRequests[id] = completer;

      try {
        final bodyStr = body != null ? json.encode(body) : null;
        IpcPutRequest(
          requestId: id,
          path: path,
          body: bodyStr,
        ).sendSignalToRust();

        // 等待响应（30秒超时 - 长操作，用于配置更新）
        final response = await completer.future.timeout(_IpcTimeouts.long);

        if (!response.isSuccessful) {
          throw Exception(response.errorMessage ?? 'IPC 请求失败');
        }

        if (response.body.isEmpty) {
          return {};
        }
        return json.decode(response.body) as Map<String, dynamic>;
      } on TimeoutException {
        _pendingRequests.remove(id);
        Logger.error('IPC PUT 请求超时（30秒）：$path');
        rethrow;
      } catch (e) {
        _pendingRequests.remove(id);

        final errorMsg = e.toString();
        if (_isIpcNotReadyError(errorMsg)) {
          // IPC 尚未就绪，静默处理
        } else {
          Logger.error('IPC PUT 请求失败：$path，error：$e');
        }
        rethrow;
      }
    });
  }

  // 发送 PATCH 请求
  Future<Map<String, dynamic>> patch(
    String path, {
    Map<String, dynamic>? body,
  }) async {
    return _retryRequest(() async {
      final completer = Completer<IpcResponse>();
      final id = _getNextId();
      _pendingRequests[id] = completer;

      try {
        final bodyStr = body != null ? json.encode(body) : null;
        IpcPatchRequest(
          requestId: id,
          path: path,
          body: bodyStr,
        ).sendSignalToRust();

        // 等待响应（15秒超时 - 普通操作）
        final response = await completer.future.timeout(_IpcTimeouts.normal);

        if (!response.isSuccessful) {
          throw Exception(response.errorMessage ?? 'IPC 请求失败');
        }

        if (response.body.isEmpty) {
          return {};
        }
        return json.decode(response.body) as Map<String, dynamic>;
      } on TimeoutException {
        _pendingRequests.remove(id);
        Logger.error('IPC PATCH 请求超时（15秒）：$path');
        rethrow;
      } catch (e) {
        _pendingRequests.remove(id);

        final errorMsg = e.toString();
        if (_isIpcNotReadyError(errorMsg)) {
          // IPC 尚未就绪，静默处理
        } else {
          Logger.error('IPC PATCH 请求失败：$path，error：$e');
        }
        rethrow;
      }
    });
  }

  // 发送 DELETE 请求
  Future<Map<String, dynamic>> delete(String path) async {
    return _retryRequest(() async {
      final completer = Completer<IpcResponse>();
      final id = _getNextId();
      _pendingRequests[id] = completer;

      try {
        IpcDeleteRequest(requestId: id, path: path).sendSignalToRust();

        // 等待响应（15秒超时 - 普通操作）
        final response = await completer.future.timeout(_IpcTimeouts.normal);

        if (!response.isSuccessful) {
          throw Exception(response.errorMessage ?? 'IPC 请求失败');
        }

        if (response.body.isEmpty) {
          return {};
        }
        return json.decode(response.body) as Map<String, dynamic>;
      } on TimeoutException {
        _pendingRequests.remove(id);
        Logger.error('IPC DELETE 请求超时（15秒）：$path');
        rethrow;
      } catch (e) {
        _pendingRequests.remove(id);

        final errorMsg = e.toString();
        if (_isIpcNotReadyError(errorMsg)) {
          // IPC 尚未就绪，静默处理
        } else {
          Logger.error('IPC DELETE 请求失败：$path，error：$e');
        }
        rethrow;
      }
    });
  }

  // 检查响应状态码是否成功
  bool isSuccessStatusCode(int statusCode) {
    return statusCode >= 200 && statusCode < 300;
  }
}
