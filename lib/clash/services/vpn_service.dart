import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart';

import '../config/clash_defaults.dart';
import '../state/access_control_states.dart';

// VPN 通道封装：通过 MethodChannel 控制原生 VpnService。
// 目前仅支持 Android，未来可扩展支持 iOS。
class VpnService {
  static const MethodChannel _channel = MethodChannel(
    'io.github.stelliberty/vpn',
  );

  static const EventChannel _coreLogChannel = EventChannel(
    'io.github.stelliberty/core_log',
  );

  // 核心日志事件流（广播流，支持多订阅者）
  static StreamController<String>? _coreLogController;

  // 是否支持当前平台
  static bool get isSupported => Platform.isAndroid;

  // 获取核心日志事件流
  static Stream<String>? get coreLogStream {
    if (!isSupported) return null;
    if (_coreLogController == null) {
      _coreLogController = StreamController<String>.broadcast();
      _coreLogChannel.receiveBroadcastStream().listen(
        (event) => _coreLogController?.add(event as String),
        onError: (error) => _coreLogController?.addError(error),
      );
    }
    return _coreLogController?.stream;
  }

  static Future<Map<String, dynamic>?> initCore({
    required String? configPath,
  }) async {
    if (!isSupported) return null;
    final res = await _channel.invokeMethod<Map<dynamic, dynamic>>('initCore', {
      'configPath': configPath,
    });
    return res?.map((key, value) => MapEntry(key.toString(), value));
  }

  static Future<String?> getCoreVersion() async {
    if (!isSupported) return null;
    return await _channel.invokeMethod<String>('getCoreVersion');
  }

  static Future<bool> getCoreState() async {
    if (!isSupported) return false;
    final res = await _channel.invokeMethod<bool>('getCoreState');
    return res ?? false;
  }

  static Future<int?> getCoreStartedAtMs() async {
    if (!isSupported) return null;
    return await _channel.invokeMethod<int>('getCoreStartedAtMs');
  }

  static Future<bool> startVpn({
    required String? configPath,
    AccessControlConfig? accessControl,
  }) async {
    if (!isSupported) return false;
    final res = await _channel.invokeMethod<bool>('startVpn', {
      'configPath': configPath,
      'accessControlMode': accessControl?.mode.toAndroidValue() ?? 0,
      'accessControlList': accessControl?.selectedPackages.toList() ?? [],
    });
    return res ?? false;
  }

  static Future<bool> stopVpn() async {
    if (!isSupported) return false;
    final res = await _channel.invokeMethod<bool>('stopVpn');
    return res ?? false;
  }

  static Future<bool> getVpnState() async {
    if (!isSupported) return false;
    final res = await _channel.invokeMethod<bool>('getVpnState');
    return res ?? false;
  }

  // 通用核心方法调用（底层接口）
  // method: 方法名，参考 core-compiled/android-wrapper/constant.go
  // data: 方法参数（会被 JSON 序列化）
  // 返回解析后的响应 Map
  static Future<Map<String, dynamic>?> invokeAction({
    required String method,
    dynamic data,
  }) async {
    if (!isSupported) return null;
    final dataStr = data == null ? null : jsonEncode(data);
    final res = await _channel.invokeMethod<String>('invokeAction', {
      'method': method,
      'data': dataStr,
    });
    if (res == null) return null;
    return jsonDecode(res) as Map<String, dynamic>;
  }

  // 获取代理列表
  static Future<Map<String, dynamic>?> getProxies() async {
    final res = await invokeAction(method: 'getProxies');
    if (res == null) return null;
    // 响应格式: {"id":"...","method":"getProxies","data":{...},"code":0}
    final data = res['data'];
    if (data == null) return null;
    if (data is String) {
      // 可能是 JSON 字符串
      return jsonDecode(data) as Map<String, dynamic>;
    }
    return data as Map<String, dynamic>;
  }

  // 切换代理
  // groupName: 代理组名
  // proxyName: 目标代理名
  static Future<bool> changeProxy({
    required String groupName,
    required String proxyName,
  }) async {
    final res = await invokeAction(
      method: 'changeProxy',
      data: {'group-name': groupName, 'proxy-name': proxyName},
    );
    if (res == null) return false;
    final code = res['code'];
    final dataVal = res['data'];
    // code == 0 且 data 为空字符串表示成功
    return code == 0 && (dataVal == '' || dataVal == null || dataVal == true);
  }

  // 测试代理延迟
  // proxyName: 代理名
  // testUrl: 测试 URL
  // timeoutMs: 超时时间（毫秒）
  // 返回延迟（毫秒），-1 表示失败/超时
  static Future<int> testProxyDelay({
    required String proxyName,
    String? testUrl,
    int? timeoutMs,
  }) async {
    final res = await invokeAction(
      method: 'asyncTestDelay',
      data: {
        'proxy-name': proxyName,
        'test-url': testUrl ?? ClashDefaults.defaultTestUrl,
        'timeout': timeoutMs ?? ClashDefaults.proxyDelayTestTimeout,
      },
    );
    if (res == null) return -1;
    final data = res['data'];
    if (data == null) return -1;
    // 响应 data 可能是字符串或对象
    final Map<String, dynamic> delayData;
    if (data is String) {
      delayData = jsonDecode(data) as Map<String, dynamic>;
    } else {
      delayData = data as Map<String, dynamic>;
    }
    final value = delayData['value'];
    if (value is int) return value;
    return -1;
  }

  // 获取当前连接列表
  static Future<String?> getConnections() async {
    final res = await invokeAction(method: 'getConnections');
    if (res == null) return null;
    final data = res['data'];
    if (data == null) return null;
    if (data is String) return data;
    return jsonEncode(data);
  }

  // 关闭单个连接
  static Future<bool> closeConnection(String connectionId) async {
    final res = await invokeAction(
      method: 'closeConnection',
      data: connectionId,
    );
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 关闭所有连接
  static Future<bool> closeAllConnections() async {
    final res = await invokeAction(method: 'closeConnections');
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 重置连接池
  static Future<bool> resetConnections() async {
    final res = await invokeAction(method: 'resetConnections');
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 获取实时流量
  static Future<Map<String, int>?> getTraffic({bool onlyProxy = false}) async {
    final res = await invokeAction(method: 'getTraffic', data: onlyProxy);
    if (res == null) return null;
    final data = res['data'];
    if (data == null) return null;
    final Map<String, dynamic> trafficData;
    if (data is String) {
      trafficData = jsonDecode(data) as Map<String, dynamic>;
    } else {
      trafficData = data as Map<String, dynamic>;
    }
    return {
      'up': (trafficData['up'] as num?)?.toInt() ?? 0,
      'down': (trafficData['down'] as num?)?.toInt() ?? 0,
    };
  }

  // 获取累计流量
  static Future<Map<String, int>?> getTotalTraffic({
    bool onlyProxy = false,
  }) async {
    final res = await invokeAction(method: 'getTotalTraffic', data: onlyProxy);
    if (res == null) return null;
    final data = res['data'];
    if (data == null) return null;
    final Map<String, dynamic> trafficData;
    if (data is String) {
      trafficData = jsonDecode(data) as Map<String, dynamic>;
    } else {
      trafficData = data as Map<String, dynamic>;
    }
    return {
      'up': (trafficData['up'] as num?)?.toInt() ?? 0,
      'down': (trafficData['down'] as num?)?.toInt() ?? 0,
    };
  }

  // 重置流量统计
  static Future<bool> resetTraffic() async {
    final res = await invokeAction(method: 'resetTraffic');
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 获取外部订阅列表
  static Future<List<Map<String, dynamic>>?> getExternalProviders() async {
    final res = await invokeAction(method: 'getExternalProviders');
    if (res == null) return null;
    final data = res['data'];
    if (data == null) return null;
    final List<dynamic> providers;
    if (data is String) {
      providers = jsonDecode(data) as List<dynamic>;
    } else {
      providers = data as List<dynamic>;
    }
    return providers.cast<Map<String, dynamic>>();
  }

  // 更新外部订阅
  static Future<bool> updateExternalProvider(String providerName) async {
    final res = await invokeAction(
      method: 'updateExternalProvider',
      data: providerName,
    );
    if (res == null) return false;
    final code = res['code'];
    final dataVal = res['data'];
    return code == 0 && (dataVal == '' || dataVal == null);
  }

  // 更新 GeoData（MMDB/GEOIP/GEOSITE/ASN）
  static Future<bool> updateGeoData(String geoType) async {
    final res = await invokeAction(
      method: 'updateGeoData',
      data: jsonEncode({'geo-type': geoType}),
    );
    if (res == null) return false;
    final code = res['code'];
    final dataVal = res['data'];
    return code == 0 && (dataVal == '' || dataVal == null);
  }

  // 获取内存占用
  static Future<int?> getMemory() async {
    final res = await invokeAction(method: 'getMemory');
    if (res == null) return null;
    final data = res['data'];
    if (data == null) return null;
    if (data is int) return data;
    if (data is String) return int.tryParse(data);
    return null;
  }

  // 强制 GC
  static Future<bool> forceGc() async {
    final res = await invokeAction(method: 'forceGc');
    if (res == null) return false;
    return res['code'] == 0;
  }

  // 关闭核心
  static Future<bool> shutdown() async {
    final res = await invokeAction(method: 'shutdown');
    if (res == null) return false;
    return res['code'] == 0;
  }
}
