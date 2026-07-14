- Windows startup settings now wait for permission-sensitive changes to succeed, avoid unrelated task prompts, and start after a one-second delay.
- Proxy sorting now persists across app and core restarts, while horizontal group navigation supports smooth wheel and button scrolling.
- Replacing the macOS or Linux application now preserves portable user data outside the replaceable application payload.
- macOS now reports the correct application name and follows simplified Chinese system localization more reliably.
- Subscription imports no longer fail when the stored subscription list is missing or incomplete.

## Upgrading from versions before 2.0.0 [v2.0.0]

Version 2.0.0 was a major rewrite and does not retain compatibility with installation state from the old Flutter version. Before installing this release over an older version:

1. Disable and uninstall startup integration in the old version.
2. Uninstall service mode in the old version.
3. Exit and uninstall the old application completely.
4. Install the current version only after the previous steps are complete.

Skipping these steps may leave old startup or service components on the system and cause unexpected behavior.

---

- Windows 开机启动设置现在会等待权限相关操作成功后再更新状态，不再触发无关的任务弹窗，并在 1 秒延时后启动。
- 代理排序现在能够在应用和内核重启后保持，横向分组导航同时支持平滑的滚轮与按钮滚动。
- 替换 macOS 或 Linux 应用时，便携用户数据现在会保存在可替换应用文件之外，不再随应用一起被覆盖。
- macOS 现在能够显示正确的应用名称，并能更可靠地跟随简体中文系统语言。
- 订阅列表缺失或内容不完整时，导入订阅不再失败。

## 从 2.0.0 之前的版本升级 [v2.0.0]

2.0.0 是一次大规模重构，不兼容旧 Flutter 版本留下的安装状态。从旧版本升级到当前版本前，请依次完成以下操作：

1. 在旧版本中关闭并卸载开机启动。
2. 在旧版本中卸载服务模式。
3. 完全退出并卸载旧应用。
4. 确认以上步骤完成后，再安装当前版本。

跳过这些步骤可能导致系统中残留旧版开机启动项或服务组件，进而引发异常行为。
