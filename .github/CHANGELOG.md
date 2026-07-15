- You can now register separate global shortcuts to show or hide the window, toggle the system proxy, and toggle virtual network adapter mode.
- Shortcut registration rejects unsupported or duplicate combinations, pauses activation while recording, and ignores repeated activations for 500 milliseconds.
- Pressing Escape now returns from a settings subpage.
- Windows upgrades now turn off the system proxy after stopping the app, preventing an unusable proxy from remaining enabled.
- Opening the main window after a silent startup is now faster because page preparation begins only after the window appears.
- Locating the selected proxy no longer repeatedly pulls the node list back after you scroll elsewhere.
- Wrapped grid items now stay aligned at fractional display scaling without clipping or overflowing the final column.

## Upgrading from versions before 2.0.0 [v2.0.0]

Version 2.0.0 was a major rewrite and does not retain compatibility with installation state from the old Flutter version. Before installing this release over an older version:

1. Disable and uninstall startup integration in the old version.
2. Uninstall service mode in the old version.
3. Exit and uninstall the old application completely.
4. Install the current version only after the previous steps are complete.

Skipping these steps may leave old startup or service components on the system and cause unexpected behavior.

---

- 现在可以分别注册全局快捷键，用于显示或隐藏窗口、切换系统代理以及切换虚拟网卡模式。
- 快捷键注册会拒绝不支持或重复的组合，录制期间暂停触发，并忽略 500 毫秒内的重复操作。
- 现在可以在设置子页按 Escape 返回上一级。
- Windows 升级安装停止应用后会关闭系统代理，避免留下无法使用的代理开关。
- 静默启动后打开主窗口的速度得到改善，页面准备会在窗口显示后再开始。
- 定位已选代理后，即使滚动到其他位置，节点列表也不会再被后续刷新反复拉回。
- 分数缩放下的网格项目现在能够保持对齐，最后一列不再被裁切或越出边界。

## 从 2.0.0 之前的版本升级 [v2.0.0]

2.0.0 是一次大规模重构，不兼容旧 Flutter 版本留下的安装状态。从旧版本升级到当前版本前，请依次完成以下操作：

1. 在旧版本中关闭并卸载开机启动。
2. 在旧版本中卸载服务模式。
3. 完全退出并卸载旧应用。
4. 确认以上步骤完成后，再安装当前版本。

跳过这些步骤可能导致系统中残留旧版开机启动项或服务组件，进而引发异常行为。
