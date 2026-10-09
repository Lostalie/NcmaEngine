# M6.8-C1 Montage 作者与获批语义工具

基线为已推送的 B2b-2 `d7a9316f68ea490ebe26f399ec64d45ea4f26adb`。
C 按 C1 作者/语义工具、C2 typed 隔离控制用例与最终联合验收切片。
本切片不新增时钟、World、native ABI、推理服务或 Agent live 权限。

## 作者数据与完整验证

graph 仍严格 v5，Montage 仍严格 v1；不兼容 v4、不改写被拒绝文件。
共用 AnimationGraphEdits 新增闭合 `montage.upsert/delete`、
`montage.slot.upsert/delete`、`montage.section.upsert/delete`。
节点的 SlotId/PlayOnStart 和 Notify 使用原 node/event 操作。
本机 UI 与 ncma.animgraph.propose 输入走相同复制/检查代码。
不执行模型生成的代码/路径/表达式，不自动修复资源或猜测攻击/闪避 Clip。

草稿可暂时包含缺失 entry/next/Slot 引用、不可达 Section 或空集合；
仍要求同骨架、唯一 UUID/名称、16Slots/64Sections/256KiB、闭合字段、有限数值、
优先级0–255、淡入淡出0–10秒、片段最短1ms、明确非空 Slot/Clip UUID。
纯 CopyDraft 不读取文件、不准备资源；复制 arrays，不借出所有权。
Delete 不隐式删除 Section、Slot node 或重接最终链，用户须明确修复完整图。
保存/预览/Agent proposal 必须通过完整 strict graph/Montage 验证；
CaptureReview/批准/发布另用实际 NCA 骨架、代次、闭包及 Clip 长度校验。
草稿不进入 Player/package；NCP1及原 numerical ABI 不变。

## UI

Slot 属性：名称、入口Section、整数优先级、是否可中断、root策略、淡入/淡出。
Section属性：名称、Slot/Clip UUID、源片段起止秒、后继（全零结束）。
Slot node 属性增加明确 SlotId 与 PlayOnStart，默认不自动播放。
新增 Slot 显式插入 Output 前的最终链，并通过同一草稿语义批次排列节点。
Clip来自输入/选中Clip或唯一无歧义现有Clip；多Clip且未选择时拒绝，不猜测素材。
新增 Section 保留为可修复草稿，不默接后继。

源 Clip 时间轨道每页4段（最多16页），显示 Section 区间与同Clip范围内 Notify 数据。
端点拖动有精确 GraphIntent stamp、有限输入、锁定手势 extent，保持最短1ms；
只改已授权本机草稿，不 seek、不发送玩法回调、不推进场景 Play。
Canvas domain59 与原序列55/骨清单58分离，取消草稿/分页停止手势。
独立真实角色预览仍使用原 exact-NCA 审阅与共享 pose/skin 路径。

## AI 与事务

沿用默认拒绝的图读取审阅、端点请求授权及精确文件/资源/受众批准。
新增提案差异单独计入 Montage metadata/Slots/Sections，不漏报编排变化。
提案只复制有界数据；实际资源准备发生于 host off-frame 审阅。
发布与 Undo/Redo仍使用唯一事务/history、原资源重检/日记补偿。
TTL60秒、撤权、重新配对、排队请求执行前检查、有限重试缓存均沿用原服务。
批准编排不等于批准 live攻击/移动/伤害、推理、代码/构建/发布或素材外发。

## 验证边界

同步联合绘制验收使用既有有界2s GPU诊断drain确认每量子的全部geometry/shadow；
额外检查无skin背压及World tick不变。它不是生产等待、吞吐量或性能验收。
实际默认生产TryUpdateSkins仍非阻塞，不能把GPU背压当solver错误或推进模拟重试。

## 尚未交付

C2将扩展隔离用例的 typed Play/Cancel/Jump、结果审阅与正式联合验收；
不将现有 B2b-2 低层控制测试声称为 C2 作者用例已交付。
整个M6.8-C/M6、用户FBX、人工可见MCP、目标环境/性能/1h验收不因C1关闭。
