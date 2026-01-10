import 'dart:async';
import 'dart:io';
import 'package:stelliberty/clash/services/process_service.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 进程管理器
// 负责进程启动/停止的业务逻辑
class ProcessManager {
  final ProcessService _service;

  ProcessManager({required ProcessService service}) : _service = service;

  // 启动 Clash 进程
  // 包含端口检查、进程启动、状态管理等业务逻辑
  Future<void> startProcess({
    required String executablePath,
    String? configPath,
    required String apiHost,
    required int apiPort,
    List<int>? portsToCheck,
  }) async {
    // 启动前确保关键端口可用（防止端口占用）
    if (portsToCheck != null && portsToCheck.isNotEmpty) {
      await _ensurePortsAvailable(portsToCheck);
    }

    // 调用 Service 层启动进程
    await _service.startProcess(
      executablePath: executablePath,
      configPath: configPath,
      apiHost: apiHost,
      apiPort: apiPort,
    );
  }

  // 停止 Clash 进程
  // 包含进程停止、端口释放检查等业务逻辑
  Future<void> stopProcess({
    Duration timeout = const Duration(seconds: 5),
    List<int>? portsToRelease,
  }) async {
    // 调用 Service 层停止进程
    await _service.stopProcess(timeout: timeout);

    // 等待端口释放（如果需要）
    if (portsToRelease != null && portsToRelease.isNotEmpty) {
      final stopwatch = Stopwatch()..start();
      for (final port in portsToRelease) {
        await _service.waitForPortRelease(
          port,
          maxWait: const Duration(seconds: 5),
        );
      }
      Logger.debug(
        '端口已释放 (${portsToRelease.join(", ")}) - 耗时：${stopwatch.elapsedMilliseconds}ms',
      );
    }
  }

  // 确保端口可用（如果被占用则尝试清理）
  Future<void> _ensurePortsAvailable(List<int> ports) async {
    // 批量检查所有端口（一次 netstat）
    final portStatus = await _service.checkMultiplePorts(ports);

    final availablePorts = <int>[];
    final occupiedPorts = <int>[];

    for (final port in ports) {
      final isInUse = portStatus[port] ?? false;
      if (isInUse) {
        occupiedPorts.add(port);
      } else {
        availablePorts.add(port);
      }
    }

    if (availablePorts.isNotEmpty) {
      Logger.debug('端口检查通过：${availablePorts.join(", ")}');
    }

    for (final port in occupiedPorts) {
      // 端口被占用，尝试清理（最多3次）
      for (int attempt = 1; attempt <= 3; attempt++) {
        Logger.warning('端口 $port 被占用（尝试 $attempt/3），查找并终止占用进程…');
        await _killProcessUsingPort(port);

        // 等待端口释放
        await _service.waitForPortRelease(
          port,
          maxWait: const Duration(seconds: 5),
        );

        // 检查是否成功释放
        final isStillInUse = await _service.isPortInUse(port);
        if (!isStillInUse) {
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
      // 使用 Service 层查找占用端口的进程
      final output = await _service.getNetstatOutput();
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
              _service.clearNetstatCache();

              // 优化：等待端口释放（事件驱动，最多 1 秒）
              await _service.waitForPortRelease(
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
                final wasStopped = await _service.tryStopViaService();
                if (wasStopped) {
                  Logger.info('已通过服务模式停止核心');
                  _service.clearNetstatCache();
                  await _service.waitForPortRelease(
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

  // 获取 Clash 可执行文件路径
  static Future<String> getExecutablePath() {
    return ProcessService.getExecutablePath();
  }
}
