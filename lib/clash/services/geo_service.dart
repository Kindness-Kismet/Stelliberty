import 'dart:io';

import 'package:path/path.dart' as p;
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/services/log_print_service.dart';

// Geodata 数据文件服务
// 负责定位 Geodata 数据库文件（GeoIP、GeoSite、ASN、Country）
class GeoService {
  // Geodata 文件名常量（小写命名，与实际文件名一致）
  static const String asnMmdb = 'asn.mmdb';
  static const String geoipDat = 'geoip.dat';
  static const String geoipMetadb = 'geoip.metadb';
  static const String geositeDat = 'geosite.dat';
  static const String countryMmdb = 'country.mmdb';

  // 所有需要的 Geodata 文件列表
  static const List<String> geoFileNames = [
    asnMmdb,
    geoipDat,
    geoipMetadb,
    geositeDat,
    countryMmdb,
  ];

  // 性能优化：路径缓存
  static String? _cachedGeoDataDir;
  static bool _isValidated = false;

  // 获取 Geodata 数据目录路径
  //
  // **性能优化**：
  // - 首次调用验证文件完整性并打印详细日志
  // - 后续调用直接返回缓存路径（避免重复 I/O）
  // - 提供 forceValidate 参数强制重新验证
  //
  // 返回 Geodata 数据目录路径
  static Future<String> getGeoDataDir({bool forceValidate = false}) async {
    // 如果已缓存且不强制验证，直接返回
    if (_cachedGeoDataDir != null && !forceValidate) {
      return _cachedGeoDataDir!;
    }

    // 使用 PathService 获取 Clash 核心数据目录
    final geoDataDir = PathService.instance.clashCoreDataPath;

    final dir = Directory(geoDataDir);

    // 验证目录存在
    if (!await dir.exists()) {
      Logger.error('Geodata 目录不存在：$geoDataDir');
      throw Exception('Geodata 目录不存在，请检查应用打包是否正确');
    }

    // 只在首次或强制验证时打印详细日志
    if (!_isValidated || forceValidate) {
      Logger.info('检查 Geodata 文件（内置方案）…');
      Logger.info('目录：$geoDataDir');

      for (final fileName in geoFileNames) {
        final file = File(p.join(geoDataDir, fileName));
        if (await file.exists()) {
          final fileSize = await file.length();
          Logger.info('✓ $fileName (${_formatBytes(fileSize)})');
        } else {
          Logger.warning('✗ $fileName 缺失');
        }
      }

      _isValidated = true;
    }

    // 缓存路径
    _cachedGeoDataDir = geoDataDir;
    return geoDataDir;
  }

  // 格式化字节数为可读格式
  static String _formatBytes(int bytes) {
    if (bytes < 1024) {
      return '$bytes B';
    } else if (bytes < 1024 * 1024) {
      return '${(bytes / 1024).toStringAsFixed(1)} KB';
    } else {
      return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
    }
  }

  // 检查 Geodata 文件是否都存在
  static Future<bool> checkGeoFilesExist() async {
    try {
      final geoDataDir = await getGeoDataDir();
      final dir = Directory(geoDataDir);

      if (!await dir.exists()) {
        return false;
      }

      for (final fileName in geoFileNames) {
        final file = File(p.join(geoDataDir, fileName));
        if (!await file.exists()) {
          return false;
        }
      }

      return true;
    } catch (e) {
      return false;
    }
  }
}
