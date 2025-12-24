import 'dart:async';
import 'dart:io';

import 'package:stelliberty/clash/services/geo_service.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/src/bindings/bindings.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/utils/logger.dart';

// Clash 进程管理服务
// Dart 端负责业务逻辑和端口检查，Rust 端负责实际进程管理
class ProcessService {
  bool _isCoreRunning = false;
  bool get isCoreRunning => _isCoreRunning;

  // netstat 输出缓存（优化性能，避免频繁调用）
  String? _netstatCache;
  DateTime? _cacheTime;
  static const _cacheDuration = Duration(milliseconds: 100);

  // 启动 Clash 进程（通过 Rust）
  Future<void> start({
    required String executablePath,
    String? configPath,
    required String apiHost,
    required int apiPort,
    List<int>? portsToCheck, // 启动前需要检查的端口列表
  }) async {
    if (_isCoreRunning) {
      throw StateError('进程已在运行');
    }

    // 启动前确保关键端口可用（防止端口占用）
    if (portsToCheck != null && portsToCheck.isNotEmpty) {
      await _ensurePortsAvailable(portsToCheck);
    }

    // 获取 Geodata 数据目录
    final geoDataDir = await GeoService.getGeoDataDir();

    // 构建启动参数
    final args = <String>[];

    if (configPath != null && configPath.isNotEmpty) {
      args.addAll(['-f', configPath]);
    }

    // 指定数据目录（Geodata 文件位置）
    args.addAll(['-d', geoDataDir]);

    // 不再传递 -ext-ctl 参数！
    // IPC 端点（Named Pipe / Unix Socket）从配置文件读取：
    // - Windows: external-controller-pipe
    // - Unix:    external-controller-unix
    // HTTP API 端点（如果启用）也从配置文件读取：
    // - external-controller

    // 调用 Rust 端启动进程（详细信息由 Rust 端日志输出）
    StartClashProcess(
      executablePath: executablePath,
      args: args,
    ).sendSignalToRust();

    // 等待 Rust 端返回结果
    final resultReceiver = ClashProcessResult.rustSignalStream;
    final result = await resultReceiver.first;

    if (!result.message.isSuccessful) {
      final error = result.message.errorMessage ?? '未知错误';
      Logger.error('Clash 进程启动失败：$error');
      throw Exception('启动 Clash 进程失败：$error');
    }

    _isCoreRunning = true;
  }

  // 停止 Clash 进程（通过 Rust）
  Future<void> stop({
    Duration timeout = const Duration(seconds: 5),
    List<int>? portsToRelease, // 停止后需要等待释放的端口列表
  }) async {
    if (!_isCoreRunning) {
      return;
    }

    // 调用 Rust 端停止进程
    StopClashProcess().sendSignalToRust();

    // 等待 Rust 端返回结果
    final resultReceiver = ClashProcessResult.rustSignalStream;
    final result = await resultReceiver.first;

    if (!result.message.isSuccessful) {
      final error = result.message.errorMessage ?? '未知错误';
      Logger.warning('Clash 进程停止失败：$error');
    }

    _isCoreRunning = false;

    // 等待端口释放（如果需要）
    if (portsToRelease != null && portsToRelease.isNotEmpty) {
      Logger.debug('等待端口释放：${portsToRelease.join(", ")}');
      for (final port in portsToRelease) {
        await _waitForPortRelease(port, maxWait: const Duration(seconds: 5));
      }
    }

    Logger.info('Clash 进程已停止');
  }

  // 获取 Clash 可执行文件路径
  // 直接返回 flutter_assets 中的可执行文件路径
  static Future<String> getExecutablePath() async {
    final String fileName;
    if (Platform.isWindows) {
      fileName = 'clash-core.exe';
    } else if (Platform.isMacOS || Platform.isLinux) {
      fileName = 'clash-core';
    } else if (Platform.isAndroid) {
      throw UnsupportedError('移动端不支持这个方式');
    } else {
      throw UnsupportedError('不支持的平台: ${Platform.operatingSystem}');
    }

    // 使用 PathService 获取可执行文件路径
    final executablePath = PathService.instance.getClashCoreExecutablePath(
      fileName,
    );

    final executableFile = File(executablePath);

    // 验证文件存在
    if (!await executableFile.exists()) {
      Logger.error('Clash 可执行文件不存在：$executablePath');
      throw Exception('Clash 可执行文件不存在，请检查应用打包是否正确');
    }

    Logger.info('使用内置 Clash 可执行文件：$executablePath');
    return executablePath;
  }

  // 获取 netstat 输出（带缓存，100ms 内复用）
  Future<String> _getNetstatOutput() async {
    if (!Platform.isWindows) {
      return '';
    }

    final now = DateTime.now();

    // 100ms 内复用缓存
    if (_netstatCache != null &&
        _cacheTime != null &&
        now.difference(_cacheTime!) < _cacheDuration) {
      return _netstatCache!;
    }

    try {
      // 执行 netstat -ano 并缓存结果
      final result = await Process.run('netstat', ['-ano']);
      if (result.exitCode == 0) {
        _netstatCache = result.stdout.toString();
        _cacheTime = now;
        return _netstatCache!;
      }
    } catch (e) {
      Logger.warning('执行 netstat 失败：$e');
    }

    return '';
  }

  // 清除 netstat 缓存（在进程状态改变后调用）
  void _clearNetstatCache() {
    _netstatCache = null;
    _cacheTime = null;
  }

  // 从 netstat 输出中解析端口是否被占用
  bool _parsePortInOutput(String output, int port) {
    if (output.isEmpty) {
      return false;
    }

    final lines = output.split('\n');
    final portPattern = RegExp(r':' + port.toString() + r'\b');

    for (final line in lines) {
      // 必须同时满足：包含 LISTENING 状态 + 精确匹配端口号
      if (line.contains('LISTENING') && portPattern.hasMatch(line)) {
        return true;
      }
    }

    return false;
  }

  // 批量检查多个端口（一次 netstat 扫描）
  Future<Map<int, bool>> _checkMultiplePorts(List<int> ports) async {
    final output = await _getNetstatOutput();
    final results = <int, bool>{};

    for (final port in ports) {
      results[port] = _parsePortInOutput(output, port);
    }

    return results;
  }

  // 检查端口是否被占用（Windows 使用 netstat）
  Future<bool> _isPortInUse(int port) async {
    if (!Platform.isWindows) {
      return false; // 非 Windows 系统暂不检查
    }

    try {
      final output = await _getNetstatOutput();
      final inUse = _parsePortInOutput(output, port);

      if (inUse) {
        Logger.debug('检测到端口 $port 正在被监听');
      }

      return inUse;
    } catch (e) {
      Logger.warning('检查端口占用失败：$e');
      return false;
    }
  }

  // 等待端口释放
  Future<void> _waitForPortRelease(
    int port, {
    required Duration maxWait,
  }) async {
    final stopwatch = Stopwatch()..start();
    const checkInterval = Duration(milliseconds: 100);

    while (stopwatch.elapsed < maxWait) {
      final inUse = await _isPortInUse(port);
      if (!inUse) {
        Logger.debug('端口 $port 已释放 (耗时：${stopwatch.elapsedMilliseconds}ms)');
        return;
      }
      await Future.delayed(checkInterval);
    }

    Logger.warning('端口 $port 在 ${maxWait.inSeconds} 秒后仍未释放');
  }

  // 确保端口可用（如果被占用则尝试清理）
  Future<void> _ensurePortsAvailable(List<int> ports) async {
    // 批量检查所有端口（一次 netstat）
    final portStatus = await _checkMultiplePorts(ports);

    for (final port in ports) {
      final inUse = portStatus[port] ?? false;

      if (!inUse) {
        Logger.debug('端口 $port 可用');
        continue;
      }

      // 端口被占用，尝试清理（最多3次）
      for (int attempt = 1; attempt <= 3; attempt++) {
        Logger.warning('端口 $port 被占用（尝试 $attempt/3），查找并终止占用进程…');
        await _killProcessUsingPort(port);

        // 等待端口释放
        await _waitForPortRelease(port, maxWait: Duration(seconds: 5));

        // 检查是否成功释放
        final stillInUse = await _isPortInUse(port);
        if (!stillInUse) {
          Logger.info('端口 $port 已成功释放');
          break;
        }

        // 最后一次尝试后仍被占用，记录错误
        if (attempt == 3) {
          Logger.error('端口 $port 在 3 次尝试后仍被占用，启动可能失败');
        }
      }
    }
  }

  // 终止占用指定端口的进程（Windows）
  Future<void> _killProcessUsingPort(int port) async {
    if (!Platform.isWindows) {
      return;
    }

    try {
      // 使用缓存的 netstat 输出查找占用端口的进程
      final output = await _getNetstatOutput();
      if (output.isEmpty) {
        Logger.warning('无法查询端口占用：netstat 失败');
        return;
      }

      final lines = output.split('\n');
      final portPattern = RegExp(r':' + port.toString() + r'\b');

      // 查找包含该端口的行（使用精确匹配避免误判）
      for (final line in lines) {
        if (portPattern.hasMatch(line) && line.contains('LISTENING')) {
          // 提取 PID（最后一列）
          final parts = line.trim().split(RegExp(r'\s+'));
          if (parts.isNotEmpty) {
            final pid = parts.last;
            Logger.info('发现占用端口 $port 的进程 PID=$pid，正在终止…');

            // 使用 taskkill 终止进程
            final killResult = await Process.run('taskkill', [
              '/F',
              '/PID',
              pid,
            ]);
            if (killResult.exitCode == 0) {
              Logger.info('成功终止进程 PID=$pid');

              // 进程被终止，清除缓存（端口状态已改变）
              _clearNetstatCache();

              // 优化：等待端口释放（事件驱动，最多 1 秒）
              await _waitForPortRelease(
                port,
                maxWait: const Duration(seconds: 1),
              );
            } else {
              final error = killResult.stderr.toString();
              Logger.warning('终止进程失败：$error');

              // 检测是否为权限不足（可能是服务模式启动的进程）
              if (error.contains('Access is denied') ||
                  error.contains('拒绝访问')) {
                Logger.info('检测到权限不足，尝试通过服务模式停止核心…');
                final stopped = await _tryStopViaService();
                if (stopped) {
                  Logger.info('已通过服务模式停止核心');
                  _clearNetstatCache();
                  await _waitForPortRelease(
                    port,
                    maxWait: const Duration(seconds: 1),
                  );
                } else {
                  Logger.warning('通过服务模式停止失败，可能需要手动清理或重启');
                }
              }
            }
            return;
          }
        }
      }

      Logger.debug('未发现占用端口 $port 的进程');
    } catch (e) {
      Logger.error('终止占用端口进程失败：$e');
    }
  }

  // 尝试通过服务模式停止 Clash 核心
  // 用于清理权限不足无法终止的进程（服务模式启动的进程）
  Future<bool> _tryStopViaService() async {
    try {
      Logger.debug('发送 StopClash 信号到服务…');
      StopClash().sendSignalToRust();

      // 等待服务响应（5秒超时）
      final signal = await ClashProcessResult.rustSignalStream.first.timeout(
        const Duration(seconds: 5),
        onTimeout: () {
          Logger.warning('服务停止超时');
          throw TimeoutException('服务停止超时');
        },
      );

      if (signal.message.isSuccessful) {
        Logger.info('服务已成功停止 Clash');
        return true;
      } else {
        Logger.warning('服务停止失败：${signal.message.errorMessage}');
        return false;
      }
    } catch (e) {
      Logger.error('通过服务停止失败：$e');
      return false;
    }
  }
}
