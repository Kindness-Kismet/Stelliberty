import 'dart:async';
import 'dart:io';
import 'package:path/path.dart' as p;
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// 更新进度回调：progress (0.0-1.0)，message (当前步骤描述)
typedef ProgressCallback = void Function(double progress, String message);

// 核心更新服务：从 GitHub 下载最新的 Mihomo 核心并替换现有核心
class CoreUpdateService {
  // 获取当前安装的核心版本
  static Future<String?> getCurrentCoreVersion() async {
    try {
      final coreDir = await getCoreDirectory();
      final platform = _getCurrentPlatform();
      final coreName = platform == 'windows' ? 'clash-core.exe' : 'clash-core';
      final coreFile = File(p.join(coreDir, coreName));

      if (!await coreFile.exists()) {
        Logger.warning('核心文件不存在：${coreFile.path}');
        return null;
      }

      // 执行核心文件获取版本信息
      final result = await Process.run(coreFile.path, ['-v']).timeout(
        const Duration(seconds: 3),
        onTimeout: () {
          Logger.warning('获取核心版本超时');
          throw TimeoutException('获取核心版本超时');
        },
      );

      Logger.info('核心版本命令退出码：${result.exitCode}');
      final stdout = result.stdout.toString().trim();
      final stderr = result.stderr.toString().trim();
      Logger.info('核心版本输出 (stdout)：$stdout');
      if (stderr.isNotEmpty) {
        Logger.info('核心版本输出 (stderr)：$stderr');
      }

      if (result.exitCode == 0) {
        // 尝试多种版本号格式匹配
        // 格式1: "Mihomo version x.x.x"
        // 格式2: "Meta version x.x.x"
        // 格式3: "version x.x.x"
        // 格式4: "v1.2.3" 或 "1.2.3"

        // 先尝试匹配 "version x.x.x" 格式
        final versionPattern = r'version\s+v?(\d+\.\d+\.\d+)';
        var versionMatch = RegExp(
          versionPattern,
          caseSensitive: false,
        ).firstMatch(stdout);
        if (versionMatch != null) {
          final version = versionMatch.group(1)!;
          Logger.info('成功解析核心版本：$version');
          return version;
        }

        // 再尝试匹配纯版本号 "v1.2.3" 或 "1.2.3"
        final pureVersionPattern = r'v?(\d+\.\d+\.\d+)';
        versionMatch = RegExp(pureVersionPattern).firstMatch(stdout);
        if (versionMatch != null) {
          final version = versionMatch.group(1)!;
          Logger.info('成功解析核心版本（纯数字格式）：$version');
          return version;
        }

        Logger.warning('无法从输出中解析版本号');
      }

      return null;
    } catch (e) {
      Logger.warning('获取当前核心版本失败：$e');
      return null;
    }
  }

  // 比较两个版本号，返回：-1（v1<v2）, 0（v1==v2）, 1（v1>v2）
  static int compareVersions(String v1, String v2) {
    // 移除可能的 'v' 前缀
    final vPrefixPattern = RegExp(r'^v');
    v1 = v1.replaceFirst(vPrefixPattern, '');
    v2 = v2.replaceFirst(vPrefixPattern, '');

    final parts1 = v1.split('.').map((e) => int.tryParse(e) ?? 0).toList();
    final parts2 = v2.split('.').map((e) => int.tryParse(e) ?? 0).toList();

    final maxLength = parts1.length > parts2.length
        ? parts1.length
        : parts2.length;

    for (int i = 0; i < maxLength; i++) {
      final p1 = i < parts1.length ? parts1[i] : 0;
      final p2 = i < parts2.length ? parts2[i] : 0;

      if (p1 < p2) return -1;
      if (p1 > p2) return 1;
    }

    return 0;
  }

  // 下载核心文件，成功返回新版本号和解压后的核心字节
  // 返回 (version, coreBytes) 元组，调用方负责停止核心后替换文件
  static Future<(String, List<int>)> downloadCore({
    ProgressCallback? onProgress,
  }) async {
    final completer = Completer<DownloadCoreResponse>();
    StreamSubscription? responseSubscription;
    StreamSubscription? progressSubscription;

    try {
      // 1. 获取当前平台和架构
      final platform = _getCurrentPlatform();
      final arch = _getCurrentArch();

      // 2. 订阅进度通知
      progressSubscription = DownloadCoreProgress.rustSignalStream.listen((
        result,
      ) {
        final progress = result.message;
        onProgress?.call(progress.progress, progress.message);
      });

      // 3. 订阅响应流
      responseSubscription = DownloadCoreResponse.rustSignalStream.listen((
        result,
      ) {
        if (!completer.isCompleted) {
          completer.complete(result.message);
        }
      });

      // 4. 发送下载请求到 Rust
      final request = DownloadCoreRequest(platform: platform, arch: arch);
      request.sendSignalToRust();

      // 5. 等待下载结果
      final result = await completer.future.timeout(
        const Duration(seconds: 30),
        onTimeout: () {
          throw TimeoutException('核心下载超时');
        },
      );

      if (!result.isSuccessful) {
        throw Exception(result.errorMessage ?? '核心下载失败');
      }

      final version = result.version ?? '';
      final coreBytes = result.coreBytes ?? [];

      return (version, coreBytes);
    } catch (e) {
      Logger.error('核心下载失败：$e');
      rethrow;
    } finally {
      await responseSubscription?.cancel();
      await progressSubscription?.cancel();
    }
  }

  // 替换核心文件（在核心停止后调用）
  static Future<void> replaceCore({
    required String coreDir,
    required List<int> coreBytes,
  }) async {
    final completer = Completer<ReplaceCoreResponse>();
    StreamSubscription? subscription;

    try {
      // 订阅 Rust 响应流
      subscription = ReplaceCoreResponse.rustSignalStream.listen((result) {
        if (!completer.isCompleted) {
          completer.complete(result.message);
        }
      });

      // 发送替换请求到 Rust
      final request = ReplaceCoreRequest(
        coreDir: coreDir,
        coreBytes: coreBytes,
        platform: _getCurrentPlatform(),
      );
      request.sendSignalToRust();

      // 等待结果
      final result = await completer.future.timeout(
        const Duration(seconds: 30),
        onTimeout: () {
          throw TimeoutException('核心替换超时');
        },
      );

      if (!result.isSuccessful) {
        throw Exception(result.errorMessage ?? '核心替换失败');
      }
    } catch (e) {
      Logger.error('核心替换失败：$e');
      rethrow;
    } finally {
      await subscription?.cancel();
    }
  }

  // 获取最新的 Release 信息
  static Future<String> getLatestRelease() async {
    final completer = Completer<GetLatestCoreVersionResponse>();
    StreamSubscription? subscription;

    try {
      // 订阅 Rust 响应流
      subscription = GetLatestCoreVersionResponse.rustSignalStream.listen((
        result,
      ) {
        if (!completer.isCompleted) {
          completer.complete(result.message);
        }
      });

      // 发送请求到 Rust
      final request = GetLatestCoreVersionRequest();
      request.sendSignalToRust();

      // 等待结果
      final result = await completer.future.timeout(
        const Duration(seconds: 10),
        onTimeout: () {
          throw TimeoutException('获取版本信息超时');
        },
      );

      if (!result.isSuccessful) {
        throw Exception(result.errorMessage ?? '获取版本信息失败');
      }

      return result.version ?? '';
    } finally {
      await subscription?.cancel();
    }
  }

  // 获取核心文件目录（运行时路径）
  static Future<String> getCoreDirectory() async {
    return PathService.instance.clashCoreBasePath;
  }

  // 删除备份的旧核心
  static Future<void> deleteOldCore(String coreDir) async {
    final platform = _getCurrentPlatform();
    final coreName = platform == 'windows' ? 'clash-core.exe' : 'clash-core';
    final backupFile = File(p.join(coreDir, '${coreName}_old'));

    if (await backupFile.exists()) {
      try {
        await backupFile.delete();
      } catch (e) {
        Logger.warning('删除旧核心备份失败：$e');
      }
    }
  }

  // 获取当前平台
  static String _getCurrentPlatform() {
    if (Platform.isWindows) return 'windows';
    if (Platform.isLinux) return 'linux';
    if (Platform.isMacOS) return 'darwin';
    throw Exception('不支持的平台: ${Platform.operatingSystem}');
  }

  // 获取当前架构（通过 Platform.version 推断）
  static String _getCurrentArch() {
    final is64Bit =
        Platform.version.contains('x64') ||
        Platform.version.contains('aarch64') ||
        !Platform.version.contains('x86');

    if (Platform.isWindows || Platform.isLinux) {
      return is64Bit ? 'amd64' : 'amd64'; // 默认 amd64
    }

    if (Platform.isMacOS) {
      // macOS 可能是 amd64 或 arm64
      return Platform.version.contains('arm64') ||
              Platform.version.contains('aarch64')
          ? 'arm64'
          : 'amd64';
    }

    return 'amd64'; // 默认值
  }
}
