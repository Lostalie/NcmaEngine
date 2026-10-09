# M6.8 Slot 和 Section 动作编排实施

M6.7远端基线0eebe79a2f6b619247a11b5dba5bbd402373b4a9已核对。
按A数据/准备、B唯一固定步运行和真实NCA、C作者/AI工具/联合验收顺序推进。
每切片完整无Skip Build.bat Debug/Release通过后提交推送、核对main再前进；不关闭人工/目标/长稳门禁。

## A 严格数据和准备 自动候选通过

定义有界纯C# Montage v1数据，不支持UE资产兼容。Persistent definition/skeleton/slot/section/clip UUID，
Slot名称/优先级/是否可中断/root策略/blend窗口；Section明确slot、clip秒区间和后继，不猜缺失片段。
至多16slots/64sections/128clips/256KiB，闭合字段、有限数值、唯一身份和名称、entry归属与完整可达。
后继循环允许，但运行期每量子最多32边界，不用静态无环限制禁止可控循环。
准备只接复制的same-skeleton/same-generation actual-duration描述；这不是NCA pins或执行。
独立只读数据格式用于作者/工具，正式runtime/package接线在B；不注册空节点冒充支持。

## B 唯一固定步运行与数值路径

B按B1合作固定步状态→B2正式Animator/NCA/pose-root接线分步验证，各自完整双配置通过并提交后前进。
B1合作状态完整双配置自动候选通过，但不能当作B运行时完成，契约见M6_8_B1_RUNTIME_CONTRACT.md；B2完整数值路径尚未完成。

B2按B2a同一Graph/Animator事务与实际NCA准备、B2b姿态/root/Notify/持久化接线验证。
B2a完整双配置自动候选通过：同instance/tick/attempt及sole graph token、两向失败/Abort、trusted stopped-host实际
NCA闭包绑定和1/8/32对象/Reload/fault；详见M6_8_B2A_RUNTIME_CONTRACT.md。图仍严格v4，
搬移包测试只证明现有NCA可准备host提供的定义，不表示Montage已cooked或姿态/root/Player接通。
完整门禁及保持User-only ACL的elevated-token Owner修复见M6_8_B2A_DELIVERY_REPORT.md；B2b/C待实现。

B2b进一步拆为B2b-1跨段blend积分/fraction与独立root数值准备、B2b-2严格新图与正式pose/root/
Notify/package消费者。先验证解析包络、终止区间、actual NCA1/8/32及16Slot528区间/原子输出，
完整双配置通过提交推送后再切图。B2b-1不向Movement写入、不注册placeholder、不部署半成品v5；
见M6_8_B2B1_RUNTIME_CONTRACT.md及M6_8_B2B1_DELIVERY_REPORT.md。B2b-1完整双配置自动候选通过；
B2b-2随后完成正式接线并通过完整双配置；B2b-1独立记录仍仅是数值准备证据。C仍待完成。

复用AnimationGraphInstance和SceneAnimatorRuntime的唯一Prepare/Commit/Abort及安全边界控制，
没有独立World、OnUpdate时钟、solver或Actor网络字段。Slot播放/取消/Section跳转带精确instance/session/world/tick。
有限请求/队列、同Slot优先级和明确中断，完整候选验证后提交；Abort/fault不消费请求或发布新时间/事件。
跨Section区间最多32；事件按实际穿越区间输出数据，root意图经现有唯一Movement/Jolt，不能teleport/改Health。
实际NCA/骨架/代次/闭包准备、共享pose/skin/shadow、Headless不GPU、Reload/close-retention与CPU-GPU误差验证。
若扩展图格式，严格新格式替代旧格式、保留拒绝文件、不恢复兼容或自动重写。

## C 作者和 AI 同源工具

B2b-2当前候选正式graph v5/Slot最后链/实际NCA pose-root-Notify/NCP1/startup/Player接通，
删除B2a外部临时绑定。定向核心109及真实GPU/uniqueJolt/Editor0-1-8-32/16搬移Player通过；
完整顺序无Skip Debug/Release已通过：12native/22managed、core109/Editor105/Player56、
Python43/smokes/formats/inspect/3profiles/audits/checkeddeploy。见M6_8_B2B2_RUNTIME_CONTRACT.md及M6_8_B2B2_DELIVERY_REPORT.md。
前文v4/外部绑定描述只属于已留证据的历史切片，不是当前入口。C typed作者/AI尚未实现。

typed Slot/Section/Notify轨道、引用/区间/时间/优先级/重入规则编辑走原shared closed语义、
精确文件/资源/受众审阅和唯一history。Agent只提议已审阅素材的编排与隔离用例，不获live攻击/移动/伤害控制。
真实stdio default-denied/TTL/revoke/queued/re-pair、完整审阅/Undo/取消/故障，以及Editor/Player搬移包联合测试。
新增工具随功能交付，不拖到M6.9；没有模型凭证配置、推理服务或Python gameplay。

## 完成与开放验收

A仅数据/准备，不提前声称Slot运行、图节点、作者、MCP或NCP1已接通。
B/C完成并完整双配置通过才标M6.8自动候选完成。用户Idle/Run/Attack/Dodge FBX与人工可见MCP、
目标环境/性能预算/1h仍单列，不能用程序化NCA、共享shader或自动UI截图替代。
