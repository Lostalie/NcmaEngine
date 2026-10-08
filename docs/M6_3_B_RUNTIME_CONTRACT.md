# M6.3-B 固定资产与场景 Animator 契约

2026-10-08。这是实现边界说明，不是完整 M6.3 或人工验收记录。

## 资源身份

`AnimatorData` v1 仅持久化 `graphId`、`skeletonId` UUID，注册为不可运行时附加的纯值组件。
同对象必须有 SkinnedMesh/Transform；与 ClipPlayback、ActionDefinition 互斥。B 暂时拒绝
Animator + RootMotion；根运动权威接入属于 C。没有新增 Node、Actor 或对象语言选项。

`AssetKind.AnimationGraph` 追加到枚举尾部；旧枚举编号和 NCA v1 的十种 block tag 不变。
图使用严格 `.ncmaanim` v1 作者数据，准备时读入自有规范副本；generation 是规范 SHA256
首八字节的小端 token（零取一），完整 hash 仍参与身份。图的 content generation 与 NCA
骨架的数值 generation **不是同一计数**，不能比较两者大小或将图 token 充当 rig generation。

RuntimeAssetLoader 在可信启动/显式刷新阶段扫描、验证和解析图的 Skeleton/Clip 闭包。
实际 RuntimeAssetLease 必须解析 Character、Skeleton 和所有 Clip；模型 UUID、骨架 UUID、
NCA generation、manifest 成员与骨数均一致才能编译。M6.2 外部传入的 clip metadata 不是此处的证据。
图 source 文件不保持锁定；保留的是不可变复制数据，NCA/package 文件则持续持有既有 read pins。
改变作者文件不会热改正在运行的图；明确刷新产生新资源身份，失败保留旧资源。

NCP1 增加明确的 `animationGraph` / `animgraph` 路由，payload 为规范图 JSON。
包解码检查完整 hash、content token、payload UUID、闭包及实际骨架/clip generation。
图不伪装成模型 data，也不扩充 NCA block 标签。显式包模式不回退到作者目录/FBX/importer。
包内仍保留作者节点坐标和名字；这不是隐私过滤/裁剪优化、冷构建、签名或发布工具。

## 生命周期与提交

SceneAnimatorRuntime 由 Editor 和 Player 共用，Headless 也先准备真实资源，但不加载 pose/GPU。
同 graph program 可共享；每 GameObject 的实例、参数和时钟独立，最多32对象/32768骨骼。
ScenePlayRuntime 聚合 Character/Animator 的应用生命周期；Player 不引用 Editor/MCP/GUI。

新增 Gameplay 内部 host composition lifetime，不是公开 SDK 权限。Start 使用实际新 Play/World
身份创建实例并冻结 Animator 完整类型集合及绑定对象的 SkinnedMesh/结构；Stop 中止 pending
token、释放冻结权威。System 在候选固定步 Prepare，成功提交后的只读 observer 才 Commit。
trigger 不因失败步消费；Faulted/Stopping、pending、身份或 tick 不匹配时拒绝运行观察。
参数控制仅允许可信 owner 安全边界；在 tick/read-only callback 尝试控制会 poison，即使调用者 catch。
没有 Agent 输入/Step/参数控制权限，也没有推理服务。

带 Animator 的 Reload 明确采用 Stop → 冻结 startup 恢复 → 新 World/Play/instance → Paused，
tick chronology 保留，但不迁移运行图的状态/参数或普通对象的进度。存在 Character 时复用其唯一
coupled startup 恢复路径。不是原位 hot reload，也不是 native solver 回滚。
GPU/派生动画必须先关闭，再关闭应用组合、数值 solver 和资产。关闭失败保留相应 owner/pins；
没有强制卸载。失败实例恢复通过成功 Stop 和新的 Play，不允许 Faulted Reload。

## 数值展示

SceneAnimationSession 复用已有 owner/context/rig/clips；AnimationGraphPose 读取已提交 recipe，
调用原 Sample 与 A 的 Blend，局部 TRS 最短路径插值后父先子后组合，再生成原 GPU palette。
每实例启动预分配 `MaximumPlanInstructions × BoneCount` scratch，硬上限65536 TRS；超过时
在准备阶段拒绝，不静默降级。该实现保留全部 recipe 中间局部姿态，尚未做 liveness 压缩或跨对象图批次优化。
已有 scene batch 仍会采样基础 bind pose，再覆盖图角色的最终姿态；计数不能当作最优批次数。

渲染只读取 committed plan 内的前后时间与当前混合权重，不另推 live clock；暂停取当前姿态。
graph Edit preview 是隔离的固定60Hz实例，最多每帧15步，不改变 Edit/Play World tick；不支持图 seek。
所有 scratch 自有且有界，无 tick 文件扫描/图序列化/AI 等待，不上传 CPU skinned vertices。
单对象数值 oracle 与实际 DX11 geometry/shadow 接线属于 B 证据；完整32角色联合图 GPU/AI门禁留在 D。

## 验证范围

新增真实 NCA 图资源、缺失/错误骨架、包类型/generation伪造、规范复制、source-free包、正式
Headless Player（无 native plugins）、提交时钟/参数、Reload 身份、隔离预览、两 clip 数值混合、
32次 Editor Play/Stop、owner线程、失败步/catch非法写/非法控制 poison、关闭失败保留测试。
完整 Debug/Release 的最终结果另见交付记录；失败原日志不得覆盖或删除。

程序化模型并非用户 FBX 动作素材。M6.3-C/D、节点编辑器、live获批图运行 MCP、Notify、
真实素材、可见第三方客户端、目标机性能/1小时长稳及历史人工门禁仍未完成。
