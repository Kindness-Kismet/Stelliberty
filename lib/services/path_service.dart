import 'dart:io';
import 'package:path/path.dart' as path;
import 'package:path_provider/path_provider.dart';
import 'package:package_info_plus/package_info_plus.dart';

// 应用文件路径管理服务，单例模式
// 负责管理所有数据目录和配置文件路径
class PathService {
  // 私有构造函数，防止外部实例化
  PathService._();

  // 单例实例
  static final PathService instance = PathService._();

  // 应用数据根目录，initialize() 后可用
  late final String appDataPath;

  // 应用名称（从 package_info_plus 获取）
  late final String _appName;

  // 缓存的路径，初始化时计算一次避免重复拼接
  late final String _subscriptionsDirCache;
  late final String _overridesDirCache;
  late final String _subscriptionListPathCache;
  late final String _overrideListPathCache;
  late final String _dnsConfigPathCache;
  late final String _pacFilePathCache;

  // 子目录名称常量
  static const String _subscriptionsDirName = 'subscriptions';
  static const String _overridesDirName = 'overrides';

  // 配置文件名称常量
  static const String _subscriptionListFileName = 'subscriptions_list.json';
  static const String _overrideListFileName = 'overrides_list.json';
  static const String _dnsConfigName = 'dns_config.yaml';
  static const String _pacFileName = 'stelliberty_proxy.pac';

  // 订阅目录路径（缓存），存储所有订阅配置文件
  String get subscriptionsDir => _subscriptionsDirCache;

  // 覆写目录路径（缓存），存储 YAML 和 JavaScript 覆写文件
  String get overridesDir => _overridesDirCache;

  // 订阅列表文件路径（缓存），存储订阅元数据
  String get subscriptionListPath => _subscriptionListPathCache;

  // 覆写列表文件路径（缓存），存储覆写元数据
  String get overrideListPath => _overrideListPathCache;

  // DNS 配置文件路径（缓存）
  String get dnsConfigPath => _dnsConfigPathCache;

  // PAC 文件路径（缓存），用于系统代理 PAC 模式
  String get pacFilePath => _pacFilePathCache;

  // 获取指定订阅的配置文件路径
  String getSubscriptionConfigPath(String subscriptionId) {
    return path.join(subscriptionsDir, '$subscriptionId.yaml');
  }

  // 获取指定覆写的文件路径
  String getOverridePath(String overrideId, String extension) {
    return path.join(overridesDir, '$overrideId.$extension');
  }

  // 初始化服务，应用启动时调用一次，创建必要的数据目录
  Future<void> initialize() async {
    // 获取应用包信息
    final packageInfo = await PackageInfo.fromPlatform();
    _appName = packageInfo.appName;

    // 确定应用数据路径
    appDataPath = await _determineAppDataPath();

    // 初始化缓存路径，一次性计算避免重复拼接
    _subscriptionsDirCache = path.join(appDataPath, _subscriptionsDirName);
    _overridesDirCache = path.join(appDataPath, _overridesDirName);
    _subscriptionListPathCache = path.join(
      _subscriptionsDirCache,
      _subscriptionListFileName,
    );
    _overrideListPathCache = path.join(
      _overridesDirCache,
      _overrideListFileName,
    );
    _dnsConfigPathCache = path.join(appDataPath, _dnsConfigName);
    _pacFilePathCache = path.join(appDataPath, _pacFileName);

    // 创建所有必要的目录
    await _createDirectories();
  }

  // 创建所有必要的目录结构
  Future<void> _createDirectories() async {
    final directories = [
      appDataPath,
      _subscriptionsDirCache,
      _overridesDirCache,
    ];

    for (final dirPath in directories) {
      final dir = Directory(dirPath);
      if (!await dir.exists()) {
        await dir.create(recursive: true);
      }
    }
  }

  // 根据平台确定应用数据根目录
  // 移动端：使用系统应用支持目录/<app_name>
  // 桌面端：使用可执行文件同级 data 目录（便携模式，需写入权限）
  Future<String> _determineAppDataPath() async {
    if (Platform.isAndroid || Platform.isIOS) {
      // 移动平台使用应用支持目录
      final appDir = await getApplicationSupportDirectory();
      return path.join(appDir.path, _appName);
    } else {
      // 桌面平台使用可执行文件同级 data 目录
      return path.join(path.dirname(Platform.resolvedExecutable), 'data');
    }
  }
}
