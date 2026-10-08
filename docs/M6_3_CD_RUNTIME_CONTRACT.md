# M6.3-C/D 图根运动与运行观察契约

2026-10-08：用户要求补充 C/D。实现为自动候选，完整双配置门禁和 Git 身份见交付记录。
不以本机程序化素材替代用户 FBX、人工第三方客户端、目标机性能或长稳验收。

## 同一时钟、同一运动权威

ScenePlayRuntime 先装配 SceneAnimatorRuntime，再装配 CharacterPlayRuntime。Start 前固定真实
RuntimeAssetLease 的 graph/model/mesh/rig/clip UUID、generation、hash 和 publication；图根集合独立
保留同一发布租约。RootMotionData 可与 Animator + SkinnedMesh + CapsuleCharacter 组合；同一对象
仍不能同时使用 ClipPlayback 或 ActionDefinition。不恢复旧格式、语言选择或原生 World。

图 System 产生候选 token；内部 CopyPreparedRootPlan 必须使用 exact pending token、session/world/
fromTick 和候选 output，不能把上一帧 output 用于本量子。GraphRootMotionSet 不创建 ClipClock。
Clip/Blend/状态过渡使用同一图区间。复用 M4 根轨道约束：单顶层根、单位尺度、XZ 平移/+Y yaw、
参与片段初始平面 anchor 一致；不合规准备即拒绝。图与既有单片段根集合共用128轨道/262144 keys
总预算；每图最多769配方行，场景最多32 Animator/32768骨骼。tick 复用预分配配方 scratch。

水平根位移替换角色输入，保留重力/跳跃/地面垂直速度。所有角色意图准备后，只执行既有唯一
MovementCoordinator/Jolt 量子；Transform/Health 写权威不变。World 成功提交后，先提交图 clock/
trigger，再提交 root desired/accepted 观察。合成图根没有单一 clip/time；RootMotionStatus 中
ClipId=empty、Time=0 是明确的非适用值，图时钟由已提交配方区间观察，不冒充另一计时器。

根显示剥离作用于最终混合模型矩阵，与参与片段共同 anchor 对齐；geometry/shadow 使用同一 skin
提交。不能以碰撞后的 accepted 位移修改源姿态，也不由显示姿态回写 World。

RootMotion 全类型集合、Animator/Skin 绑定在 Play 冻结。可信参数控制仅安全边界开放，tick/readonly
非法写即使被 catch 也 poison。准备失败不提交 token；数值执行后的错误 fail-stop，不能声称 solver
回滚。Faulted 后无当前成功图/根/pose观察；Stop/new Play 或冻结 startup Reload 才恢复。Reload
创建新 session/world/instance，保留真实 tick 年代；派生关闭失败保留所有者和 pins，允许显式重试。
Headless 不加载 pose/Renderer/Platform；Physics 默认关闭不改变。

## 精确 GPU 观察

SceneRenderSession 仅在实际成功 skin/pipeline 提交后发布复制 AnimationPresentationStamp：World、
asset publication、committed tick、Renderer frame、pose generation、geometry/shadow draw 数。
提交开始先清空旧 stamp；世界 tick 变化或 Faulted 返回 null。它是提交证据，不是 Present/可见像素
人工验收，也不是新增 GPU 查询、独立时钟或 GPU 控制权。保留旧 ABI/查询表和 opaque native leases。

## 独立只读 MCP

工具 ncma.animgraph.runtime：闭合输入 PlaySessionId/WorldId/ObjectId，可选 expectedTick、offset
0..768、limit1..32。输出复制当前状态/参数、分页 Clip/Blend 配方、graph/instance/publication/
session/world/tick、root desired/accepted/数值序列及当前成功 pose stamp。故障为 snapshotValid=false、
observation=null，不能拿旧成功帧冒充当前。Headless pose=null。资源 generation 审批用十进制字符串，
计数/序列限制 JSON 精确整数范围；不输出磁盘路径、native handle 或私有异常。

默认拒绝，不能继承旧 character 或结构图批准。可信本机 UI 审阅完整图/NCA资源闭包 UUID、generation、
hash、publication、Play/World、EditSession/document generation、endpoint 与已配对连接受众 epoch。
完整分页资源和受众审阅后单独批准60秒，最多8对象/4096资源证明。每次查询，包括同 requestId 重试，
重检批准、期限、连接epoch和身份；撤销、断开/重配对、Stop/Reload/新资源身份使其失效。
查询不读文件、不编译、不采样 native、不推进图/World/solver，不赋予 Agent Step/输入/Reset 权限。

本机 Tools/AI「动画图运行观察 / MCP 审批」与 MCP 共用语义观察服务；普通本机复制观察无需外部
批准。图 authoring propose/transaction 仍是 M6.4 独立文件能力，不能借此修改 live Play。
内置推理、Python transport、事件轨道持久化、Agent 序列运行、中断/BlendSpace/层混合/Montage未交付。

## 自动证据与限制

实际 pinned NCA/Jolt：30/60/144 与 exact Headless120ticks、墙/坡/跳跃/转向、过渡重入、当前参数
影响候选根、0/8/32和33拒绝、caught绑定/参数写 poison、数值后失败、Reload/Stop/关闭重试。
GPU 8周期、0/8/32角色、独立管理端采样/混合/父组合/根剥离/加权顶点 oracle，geometry/shadow同提交；
正式共享 PlayerRunner 的 source-free NCP Headless 和独立进程 DX11（不在现有单上下文进程二次初始化）。
真实 stdio MCP 默认拒绝/精确批准/闭合schema/无文件IO/失效期限/受众epoch/UI/Reload/Faulted/
当前pose与过期pose拒绝。本机自动化不等于人工可见第三方验收。

共享复制 debug 基础由本轮已开始的 M6.5-A 提供；其纯内存 event/sequence 基础一起保留，
无持久事件格式、UI事件编辑或Agent序列执行工具，不能将这次 C/D 补齐宣称完整 M6.5。
