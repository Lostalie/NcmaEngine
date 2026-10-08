# M6.5-C 事件作者与独立序列契约

2026-10-08。C 是事件/中断策略持久化、作者工具和获批独立序列分析，不是 AI 推理服务或 live Play 控制。
完整回归状态见 M6_5_C_DELIVERY_REPORT.md；D 联合门禁单独交付。

## 严格格式和同一资源闭包

`.ncmaanim` 当前严格 JSON **v2**，必需 `events` 数组和 `interruptTransitions` 布尔字段。
v1 被拒绝，不迁移、不兼容、不改写被拒绝输入；不添加旧 DTO 或 fallback。C# 新图显式编码 v2。
历史 M6.1–B 文档中的 v1 描述只是当时证据，不能作为当前格式支持承诺。
原始十个 NCA tags、NCP1 的 animgraph 路由以及原生 pose/blend/GUI ABI 保持不变。

事件是 `{id,clipId,time,name}` 数据：4096 上限、全图唯一 UUID、已引用 Clip、有限 `(0,600]` 秒、
有界纯文本；结构验证不猜 Clip 长度。实际同代次 NCA 的 Skeleton/Character/Clip 闭包准备时
再次验证 `(0,duration]`，禁止 Agent 自报 duration 代替资源证据。
编译内容 hash 包括事件和策略；独立事件 hash 也参与精确用例审阅。
`interruptTransitions=true` 只允许单个既有状态机，默认 false；保存后运行时自动按 B 的固定缓存契约执行，
不能把策略作为跳过资源准备或新时钟/运动权威的理由。

## 作者与可见调试

`event.upsert/delete`、`graph.interruptions` 是闭合语义编辑；UI 与 Agent 复用 M6.4 proposals、
精确原始 before/after、实际 NCA 代次/内容 review、独立 endpoint 请求批准及唯一完整文档 Undo/Redo。
删去最后一个引用 Clip 的节点同时删除对应标记；骨架/图/Clip UUID 不可与标记 UUID 重用。
草稿拖动只修改复制事件数据；轨道显示绝对秒时标而非伪造 Clip 时长。元素按时间/UUID排序、每页16个，
可编辑名称/Clip/时间；取消不写文件，批准保存才触发既有 scene snapshot/history 恢复。
事件没有回调、伤害、命令、表达式或代码权限。

独立真实角色预览仍使用既有单 pose/skin-shadow/Present 路径，World tick=0。
复制 debug 显示 validity/outcome、当前/目标/源状态、转换权重、Frozen generation 和本量子事件，
相同 UUID 元素可定位活动状态/转换/事件；无效准备不重新发布旧成功姿态为当前结果。
预览参数/暂停/单步不控制场景 Play；没有 seek 发送玩法事件或 Agent fault-clear。

## 独立序列与 MCP

共享 AnimationGraphSequence 最多256量子、fixedDelta .001–1s、512精确类型写入、64闭合断言、
8192总输出事件；用例 JSON ≤48KiB，未知字段/重复属性/数字枚举/大小写别名/代码/路径全部拒绝。
完整输入先验证，实例使用独立 session/world 身份；参数、状态、转换、事件、rootX/rootYaw 断言是数据，
不是任意 C# 断言。失败断言返回明确 `assertion_failed`，不能宣称通过。

可信本机主机在帧外显式准备实际不可变 NCA lease、程序、必要姿态缓存 provider，
以及可选根数值轨迹（默认 root bone0，128 tracks/262144 keys、公共平面 anchor）。
根结果是未碰撞的 desired local root intent：冻结源区间仍为零；不创建 Jolt/Character/World/native pose/GPU。
纯编译器结果始终 `resourcesPrepared=false`；只有已保留实际 lease 的服务输出能声明资源已准备。
根未准备时不接受 root 断言、结果为 null/unsupported，不提供自动估计。

工具 `ncma.animgraph.sequence.propose` 只存复制用例，不执行；`sequence.run` 只执行/读取精确获批独立用例。
两者默认拒绝：需已有获批图读取范围和可信资源准备；run 另需人类审阅精确 case hash、graph/event hash、
publication、所有 NCA UUID/代次/内容、Edit/图/asset stamp、endpoint/audience epoch 和读取 permission revision。
GUI 逐页审阅完整用例/所有资源受众；未访问全部页不能批准。批准60s，不存在 Agent 自批准工具。
最多4用例/结果/批准、256活动与退休 IDs；取消退休 ID。相同 Agent ID 相同内容可重提，不同内容拒绝。
本机用例可由人直接运行，但 Agent 用例即使走本机 RunLocal 也必须精确批准。

summary/timeline/events/checks 各页最多8行；重复请求仍复核范围、TTL、配对及身份后读取同一独立缓存结果。
准备/草稿/保存/Undo/Redo/资源 revision/Play freeze/取消/撤销/重新配对使旧批准失效。
端点处理不读取文件、不编译、不接受 Agent 资源路径；唯一运行在显式独立分析请求，不在 simulation/render tick。
同步有界分析不是推理、IPC 等待或 live Play Step。结果明确 `livePlay=false, collisionExecuted=false`。

## 开放门禁

人工可见第三方 MCP、真实用户 FBX 素材、DPI/IME、目标环境、完整 CPU/GPU 性能和1小时长稳仍开放。
没有模型连接、凭据/外发授权、Python transport、代码/build/publish 能力、泛化游戏通知回调或碰撞序列执行。
