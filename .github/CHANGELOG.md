- Applying config changes in service mode now reloads the core in place, so proxies no longer drop for a few seconds on subscription updates and setting changes
- Fixed service-mode core status briefly showing as unavailable while the core kept running, which also caused unnecessary proxy selection reloads
- Fixed a race during app shutdown that could crash the app while disposing the service-mode core client
- Added a --minimized startup flag that launches the app with the main window minimized

---

- 服务模式下应用配置变更改为原地重载核心，更新订阅或修改设置时代理节点不再短暂丢失
- 修复了服务模式下核心仍在运行时状态短暂显示为不可用的问题，该问题此前还会引发多余的代理选择重载
- 修复了应用退出时服务模式核心客户端释放竞态可能导致崩溃的问题
- 新增了 --minimized 启动参数，现在可以让应用以主窗口最小化的方式启动
