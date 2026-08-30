- Fixed manually picking a node in a url-test or fallback group permanently suspending its automatic selection, which previously could only be undone by deleting the subscription
- Running a delay test now releases manual picks on url-test and fallback groups, and a restart returns them to automatic selection
- Fixed batch delay results disappearing from rows that scrolled out of view while the batch was still running
- Dialogs now open scrolled to the top instead of keeping the position left by the previous session
- Filtered rule lists now show the same centered empty card as the other list pages
- Add and refresh moved into the rule page header, so the toolbar no longer overlaps at the narrowest window width

---

- 修复了在 url-test 或 fallback 分组手动选择节点后自动择优被永久挂起的问题，该问题此前只能通过删除订阅解除
- 测试延迟现在会解除 url-test 和 fallback 分组的手动选择，重启后这类分组也会回到自动择优
- 修复了批量测速期间滚出视图的条目丢失延迟结果的问题
- 弹窗现在每次打开都从顶部开始，不再保留上一次的滚动位置
- 规则列表被过滤为空时，现在显示与其它列表页一致的居中空状态卡片
- 规则页的新增和刷新移入页头，最窄窗口宽度下工具栏不再重叠
