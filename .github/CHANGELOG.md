## Important upgrade notice

This release is a major rewrite. To improve code readability and long-term maintainability, compatibility with older installations was not retained.

Before installing this version, complete the following steps in order:

1. Disable and uninstall startup integration in the old version.
2. Uninstall service mode in the old version.
3. Exit and uninstall the old application.
4. Install this version only after the previous steps are complete.

Skipping these steps may leave old startup or service components on the system and cause unexpected behavior.

## What's new

- Rebuilt on .NET with a modern cross-platform architecture for efficient performance and easier long-term maintenance.
- Redesigned the application structure, interface, runtime integration, subscription management, proxy selection, and configuration workflow.
- Improved automated testing and diagnostics to make problems easier to reproduce and resolve.

## Feedback

If you encounter a problem, please open an issue and attach the relevant application logs, reproduction steps, operating system, and application version.

---

## 重要升级说明

本次更新是一次大规模重构。为了提高代码可读性和长期维护性，本版本未保留对旧版安装状态的兼容处理。

安装本版本前，请务必按顺序完成以下操作：

1. 在旧版本中关闭并卸载开机启动。
2. 在旧版本中卸载服务模式。
3. 完全退出并卸载旧应用。
4. 确认以上步骤全部完成后，再安装本版本。

如果跳过这些步骤，系统中可能残留旧版开机启动项或服务组件，进而引发异常行为。

## 本次更新

- 全面采用 .NET 现代跨平台架构，在保持高效性能的同时，提升代码可读性和长期维护能力。
- 重新设计应用结构、界面、运行时集成、订阅管理、代理选择及配置流程。
- 完善自动化测试与诊断能力，让问题更容易复现和定位。

## 问题反馈

如果遇到问题，请前往项目 Issue 页面反馈，并附上相关应用日志、复现步骤、操作系统及应用版本，以便快速定位问题。
