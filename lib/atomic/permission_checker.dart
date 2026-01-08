import 'dart:io';
import 'package:stelliberty/services/log_print_service.dart';

// 权限检查服务
class PermissionService {
  static bool? _isElevated;

  // 检查当前进程是否以管理员/root 权限运行
  static Future<bool> isElevated() async {
    // 已检查过则返回缓存结果
    if (_isElevated != null) {
      return _isElevated!;
    }

    try {
      if (Platform.isWindows) {
        // Windows: 执行 net session 命令检查权限
        final result = await Process.run('net', ['session'], runInShell: true);

        // net session 仅管理员可成功执行
        _isElevated = result.exitCode == 0;
        Logger.info('Windows 权限检查：${_isElevated! ? "管理员" : "普通用户"}');
        return _isElevated!;
      } else if (Platform.isLinux || Platform.isMacOS) {
        // Linux/macOS: 检查 UID 是否为 0
        final result = await Process.run('id', ['-u']);
        final uid = int.tryParse(result.stdout.toString().trim()) ?? -1;
        _isElevated = uid == 0;
        Logger.info('Unix 权限检查：UID=$uid，${_isElevated! ? "root" : "普通用户"}');
        return _isElevated!;
      }

      // Android 默认没有 root 权限
      _isElevated = false;
      return false;
    } catch (e) {
      Logger.error('权限检查失败：$e');
      // 异常时假设无管理员权限（安全策略）
      _isElevated = false;
      return false;
    }
  }

  // 清除权限状态缓存，用于测试或权限变更场景
  static void clearCache() {
    _isElevated = null;
  }
}
