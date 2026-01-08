import 'package:stelliberty/clash/model/subscription_model.dart';

// 覆写类型
enum OverrideType {
  local('local', '本地文件'),
  remote('remote', '远程链接');

  const OverrideType(this.value, this.displayName);

  final String value;
  final String displayName;

  static OverrideType fromString(String value) {
    return values.firstWhere(
      (type) => type.value == value,
      orElse: () => OverrideType.local,
    );
  }
}

// 覆写格式
enum OverrideFormat {
  yaml('yaml', 'Yaml'),
  js('js', 'JavaScript');

  const OverrideFormat(this.value, this.displayName);

  final String value;
  final String displayName;

  static OverrideFormat fromString(String value) {
    return values.firstWhere(
      (format) => format.value == value,
      orElse: () => OverrideFormat.yaml,
    );
  }
}

// 覆写配置
class OverrideConfig {
  final String id;
  final String name;
  final OverrideType type;
  final OverrideFormat format;
  final String? url; // 远程 URL（type=remote）
  final String? localPath; // 本地路径（type=local）
  final String? content; // 缓存的内容
  final DateTime? lastUpdate; // 最后更新时间
  final SubscriptionProxyMode proxyMode; // 代理模式（仅远程覆写）

  const OverrideConfig({
    required this.id,
    required this.name,
    required this.type,
    required this.format,
    this.url,
    this.localPath,
    this.content,
    this.lastUpdate,
    this.proxyMode = SubscriptionProxyMode.direct,
  });

  // 创建新覆写
  factory OverrideConfig.create({
    required String name,
    required OverrideType type,
    required OverrideFormat format,
    String? url,
    String? localPath,
    String? content,
    SubscriptionProxyMode proxyMode = SubscriptionProxyMode.direct,
  }) {
    return OverrideConfig(
      id: DateTime.now().millisecondsSinceEpoch.toString(),
      name: name,
      type: type,
      format: format,
      url: url,
      localPath: localPath,
      content: content,
      lastUpdate: DateTime.now(),
      proxyMode: proxyMode,
    );
  }

  OverrideConfig copyWith({
    String? name,
    OverrideType? type,
    OverrideFormat? format,
    String? url,
    String? localPath,
    String? content,
    DateTime? lastUpdate,
    SubscriptionProxyMode? proxyMode,
  }) {
    return OverrideConfig(
      id: id,
      name: name ?? this.name,
      type: type ?? this.type,
      format: format ?? this.format,
      url: url ?? this.url,
      localPath: localPath ?? this.localPath,
      content: content ?? this.content,
      lastUpdate: lastUpdate ?? this.lastUpdate,
      proxyMode: proxyMode ?? this.proxyMode,
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'type': type.value,
    'format': format.value,
    'url': url,
    'localPath': localPath,
    // content 不序列化到 JSON，仅用于内存缓存
    'lastUpdate': lastUpdate?.toIso8601String(),
    'proxyMode': proxyMode.value,
  };

  factory OverrideConfig.fromJson(Map<String, dynamic> json) {
    return OverrideConfig(
      id: json['id'],
      name: json['name'],
      type: OverrideType.fromString(json['type'] ?? 'local'),
      format: OverrideFormat.fromString(json['format'] ?? 'yaml'),
      url: json['url'],
      localPath: json['localPath'],
      content: json['content'],
      lastUpdate: json['lastUpdate'] != null
          ? DateTime.parse(json['lastUpdate'])
          : null,
      proxyMode: SubscriptionProxyMode.fromString(
        json['proxyMode'] ?? 'direct',
      ),
    );
  }

  @override
  String toString() =>
      'OverrideConfig(id: $id, name: $name, type: ${type.displayName}, format: ${format.displayName})';
}
