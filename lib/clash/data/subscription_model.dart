import '../config/clash_defaults.dart';

// 订阅更新代理模式
enum SubscriptionProxyMode {
  // 直连，不使用代理
  direct('direct', '直连'),

  // 使用系统代理
  system('system', '系统代理'),

  // 使用 Clash 核心代理（自举）
  core('core', '核心代理');

  const SubscriptionProxyMode(this.value, this.displayName);

  final String value;
  final String displayName;

  static SubscriptionProxyMode fromString(String value) {
    return SubscriptionProxyMode.values.firstWhere(
      (mode) => mode.value == value,
      orElse: () => SubscriptionProxyMode.direct,
    );
  }
}

// 自动更新模式
enum AutoUpdateMode {
  // 禁用自动更新
  disabled('disabled'),

  // 间隔更新（按分钟）
  interval('interval');

  const AutoUpdateMode(this.value);

  final String value;

  static AutoUpdateMode fromString(String value) {
    return AutoUpdateMode.values.firstWhere(
      (mode) => mode.value == value,
      orElse: () => AutoUpdateMode.disabled,
    );
  }
}

// 订阅信息（流量统计）
class SubscriptionInfo {
  final int upload; // 已上传（字节）
  final int download; // 已下载（字节）
  final int total; // 总流量（字节）
  final int expire; // 过期时间（Unix 时间戳）

  const SubscriptionInfo({
    this.upload = 0,
    this.download = 0,
    this.total = 0,
    this.expire = 0,
  });

  // 从 HTTP 响应头解析订阅信息
  // 格式: "upload=1234; download=5678; total=10000; expire=1234567890"
  factory SubscriptionInfo.fromHeader(String? header) {
    if (header == null || header.isEmpty) {
      return const SubscriptionInfo();
    }

    final data = <String, int>{};
    for (final part in header.split(';')) {
      final keyValue = part.trim().split('=');
      if (keyValue.length == 2) {
        final key = keyValue[0].trim();
        final value = int.tryParse(keyValue[1].trim());
        if (value != null) data[key] = value;
      }
    }

    return SubscriptionInfo(
      upload: data['upload'] ?? 0,
      download: data['download'] ?? 0,
      total: data['total'] ?? 0,
      expire: data['expire'] ?? 0,
    );
  }

  // 已使用流量（字节）
  int get used => upload + download;

  // 剩余流量（字节）
  int get remaining => total > 0 ? total - used : 0;

  // 流量使用百分比（0-100）
  double get usagePercentage => total > 0 ? used / total * 100 : 0;

  // 是否已过期
  bool get isExpired {
    if (expire <= 0) return false;
    return DateTime.now().millisecondsSinceEpoch > expire * 1000;
  }

  Map<String, dynamic> toJson() => {
    'upload': upload,
    'download': download,
    'total': total,
    'expire': expire,
  };

  factory SubscriptionInfo.fromJson(Map<String, dynamic> json) {
    return SubscriptionInfo(
      upload: json['upload'] as int? ?? 0,
      download: json['download'] as int? ?? 0,
      total: json['total'] as int? ?? 0,
      expire: json['expire'] as int? ?? 0,
    );
  }
}

// 订阅配置
class Subscription {
  final String id; // 唯一标识
  final String name; // 订阅名称
  final String url; // 订阅链接
  final AutoUpdateMode autoUpdateMode; // 自动更新模式
  final int intervalMinutes; // 间隔更新时长（分钟，仅当模式为 interval 时有效）
  final bool updateOnStartup; // 启动时更新（禁用自动更新时可选）
  final DateTime? lastUpdateTime; // 上次更新时间
  final SubscriptionInfo? info; // 订阅信息
  final bool isUpdating; // 是否正在更新
  final bool isLocalFile; // 是否为本地文件
  final SubscriptionProxyMode proxyMode; // 订阅更新代理模式
  final String? lastError; // 最后一次更新错误信息
  final List<String> overrideIds; // 规则覆写ID列表
  final List<String> failedOverrideIds; // 失败的覆写ID列表(启动失败时记录)
  final String userAgent; // User-Agent（仅远程订阅有效，默认为 clash.meta）

  const Subscription({
    required this.id,
    required this.name,
    required this.url,
    this.autoUpdateMode = AutoUpdateMode.disabled,
    this.intervalMinutes = 60,
    this.updateOnStartup = false,
    this.lastUpdateTime,
    this.info,
    this.isUpdating = false,
    this.isLocalFile = false,
    this.proxyMode = SubscriptionProxyMode.direct,
    this.lastError,
    this.overrideIds = const [],
    this.failedOverrideIds = const [],
    this.userAgent = ClashDefaults.defaultUserAgent,
  });

  // 创建新订阅
  factory Subscription.create({required String name, required String url}) {
    return Subscription(
      id: DateTime.now().millisecondsSinceEpoch.toString(),
      name: name,
      url: url,
    );
  }

  // 是否需要自动更新
  bool get needsUpdate {
    // 如果未启用自动更新，则不需要更新
    if (autoUpdateMode == AutoUpdateMode.disabled) {
      return false;
    }

    // 如果从未更新过，需要更新
    if (lastUpdateTime == null) {
      return true;
    }

    // 间隔更新模式
    if (autoUpdateMode == AutoUpdateMode.interval) {
      final nextUpdateTime = lastUpdateTime!.add(
        Duration(minutes: intervalMinutes),
      );
      return DateTime.now().isAfter(nextUpdateTime);
    }

    return false;
  }

  // 获取配置文件路径
  String getConfigPath(String baseDir) {
    // 需要导入 path 包才能使用，但这里为了避免导入，使用平台无关的方式
    // 使用 path.join 会更好，但需要在调用方处理
    return '$baseDir/subscriptions/$id.yaml';
  }

  // 复制并修改属性
  Subscription copyWith({
    String? id,
    String? name,
    String? url,
    AutoUpdateMode? autoUpdateMode,
    int? intervalMinutes,
    bool? updateOnStartup,
    DateTime? lastUpdateTime,
    SubscriptionInfo? info,
    bool? isUpdating,
    bool? isLocalFile,
    SubscriptionProxyMode? proxyMode,
    String? lastError,
    List<String>? overrideIds,
    List<String>? failedOverrideIds,
    String? userAgent,
  }) {
    return Subscription(
      id: id ?? this.id,
      name: name ?? this.name,
      url: url ?? this.url,
      autoUpdateMode: autoUpdateMode ?? this.autoUpdateMode,
      intervalMinutes: intervalMinutes ?? this.intervalMinutes,
      updateOnStartup: updateOnStartup ?? this.updateOnStartup,
      lastUpdateTime: lastUpdateTime ?? this.lastUpdateTime,
      info: info ?? this.info,
      isUpdating: isUpdating ?? this.isUpdating,
      isLocalFile: isLocalFile ?? this.isLocalFile,
      proxyMode: proxyMode ?? this.proxyMode,
      lastError: lastError,
      overrideIds: overrideIds ?? this.overrideIds,
      failedOverrideIds: failedOverrideIds ?? this.failedOverrideIds,
      userAgent: userAgent ?? this.userAgent,
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'url': url,
    'autoUpdateMode': autoUpdateMode.value,
    'intervalMinutes': intervalMinutes,
    'updateOnStartup': updateOnStartup,
    'lastUpdateTime': lastUpdateTime?.toIso8601String(),
    'info': info?.toJson(),
    'isLocalFile': isLocalFile,
    'proxyMode': proxyMode.value,
    'lastError': lastError,
    'overrideIds': overrideIds,
    'failedOverrideIds': failedOverrideIds,
    'userAgent': userAgent,
  };

  factory Subscription.fromJson(Map<String, dynamic> json) {
    return Subscription(
      id: json['id'] as String,
      name: json['name'] as String,
      url: json['url'] as String,
      autoUpdateMode: AutoUpdateMode.fromString(
        json['autoUpdateMode'] as String? ?? 'disabled',
      ),
      intervalMinutes: json['intervalMinutes'] as int? ?? 60,
      updateOnStartup: json['updateOnStartup'] as bool? ?? false,
      lastUpdateTime: json['lastUpdateTime'] != null
          ? DateTime.parse(json['lastUpdateTime'] as String)
          : null,
      info: json['info'] != null
          ? SubscriptionInfo.fromJson(json['info'] as Map<String, dynamic>)
          : null,
      isLocalFile: json['isLocalFile'] as bool? ?? false,
      proxyMode: SubscriptionProxyMode.fromString(
        json['proxyMode'] as String? ?? 'direct',
      ),
      lastError: json['lastError'] as String?,
      overrideIds: json['overrideIds'] != null
          ? List<String>.from(json['overrideIds'] as List)
          : const [],
      failedOverrideIds: json['failedOverrideIds'] != null
          ? List<String>.from(json['failedOverrideIds'] as List)
          : const [],
      userAgent:
          json['userAgent'] as String? ?? ClashDefaults.defaultUserAgent,
    );
  }

  @override
  String toString() => 'Subscription(id: $id, name: $name, url: $url)';
}
