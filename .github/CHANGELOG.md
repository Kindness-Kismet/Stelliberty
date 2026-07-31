- Saved subscriptions and overrides are no longer cleared when a state file is temporarily locked or inaccessible.
- Core restarts now stop when the previous core process cannot be confirmed closed.
- Subscription and override operations no longer report false success, and canceled updates stay silent.
- File picker failures now show a clear message while normal cancellation remains silent.

---

- 状态文件暂时被占用或无法访问时，已保存的订阅和覆写不再被清空。
- 无法确认旧核心进程已关闭时，核心重启会立即停止。
- 订阅和覆写操作不再虚假提示成功，主动取消更新时也不会提示失败。
- 文件选择器异常时会显示明确提示，正常取消仍保持静默。
