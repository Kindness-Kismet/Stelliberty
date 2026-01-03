import 'dart:io';
import 'package:flutter_cache_manager/flutter_cache_manager.dart';
import 'package:stelliberty/services/path_service.dart';

// 图片缓存服务
class ImageCacheService {
  ImageCacheService._();

  static final ImageCacheService instance = ImageCacheService._();

  CacheManager? _cacheManager;

  CacheManager get cacheManager {
    _cacheManager ??= CacheManager(
      Config(
        'proxy_icon_cache',
        stalePeriod: const Duration(days: 36135),
        maxNrOfCacheObjects: 999,
        fileSystem: IOFileSystem(PathService.instance.imageCacheDir),
      ),
    );
    return _cacheManager!;
  }

  Future<void> clearCache() async {
    await _cacheManager?.emptyCache();
  }

  Future<int> getCacheSize() async {
    final cacheDir = Directory(PathService.instance.imageCacheDir);
    if (!await cacheDir.exists()) return 0;

    int totalSize = 0;
    await for (final entity in cacheDir.list(recursive: true)) {
      if (entity is File) {
        totalSize += await entity.length();
      }
    }
    return totalSize;
  }
}
