import 'dart:async';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/utils/logger.dart';

// 电源事件服务：监听休眠/唤醒，自动重载 TUN
class PowerEventService {
  static final PowerEventService _instance = PowerEventService._internal();
  factory PowerEventService() => _instance;
  PowerEventService._internal();

  StreamSubscription? _subscription;

  DateTime? _lastReloadAt;
  static const _reloadCooldown = Duration(seconds: 5);

  bool _isReloading = false;

  void init() {
    Logger.info('初始化电源事件服务');

    _subscription = SystemPowerEvent.rustSignalStream.listen((signal) {
      _handlePowerEvent(signal.message);
    });
  }

  void _handlePowerEvent(SystemPowerEvent event) {
    if (event.eventType == PowerEventType.resumeAutomatic ||
        event.eventType == PowerEventType.resumeSuspend) {
      _reloadTun();
    }
  }

  Future<void> _reloadTun() async {
    final now = DateTime.now();
    if (_lastReloadAt != null &&
        now.difference(_lastReloadAt!) < _reloadCooldown) {
      Logger.warning(
        'TUN 重载冷却中（距上次 ${now.difference(_lastReloadAt!).inSeconds} 秒）',
      );
      return;
    }

    if (_isReloading) {
      Logger.warning('TUN 重载进行中，跳过');
      return;
    }

    _isReloading = true;
    _lastReloadAt = now;

    try {
      final manager = ClashManager.instance;

      if (!manager.isTunEnabled) {
        Logger.debug('TUN 未启用');
        return;
      }

      if (!manager.isCoreRunning) {
        Logger.debug('核心未运行');
        return;
      }

      Logger.info('开始 TUN 重载');

      await manager.setTunEnabled(false);
      await Future.delayed(const Duration(milliseconds: 500));
      await manager.setTunEnabled(true);

      Logger.info('TUN 重载完成');
    } catch (e) {
      Logger.error('TUN 重载失败：$e');
    } finally {
      _isReloading = false;
    }
  }

  void dispose() {
    _subscription?.cancel();
    _subscription = null;
  }
}
