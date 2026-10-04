# M2.6：薄 C++ 物理插件 + C# 高层物理服务

更新日期：2026-10-04。采用用户确认的职责划分；实现已落地，Debug/Release 完整回归通过，H6 自动门禁通过。
交付及测量证据见 [M2_6_DELIVERY_REPORT.md](M2_6_DELIVERY_REPORT.md)。独立 H6 不替代 H3/H4/H5，也不授权删除旧入口。

## 1. 终点与禁止项

终点是独立数值服务：NcmaPhysics.dll + Ncma.Physics.PhysicsService/PhysicsSimulation。
C# 高层物理服务不是 Runtime.World 的替代品，也不是自动挂入 Play 的 PhysicsSystem。

| 层 | 职责 | 禁止 |
|---|---|---|
| C++ Physics 插件 | SDK 求解、数值资源、边界验证、原子 primitive 批次、作业与 fail-stop | GameObject/组件、应用更新策略、场景文件、Agent/编辑器业务 |
| C# PhysicsWorld | 缓存 C ABI 委托、Span 批次、lease、复制原始计数 | 场景组件池、逐对象 P/Invoke、native memory view |
| C# PhysicsService | 可选装载、世界预算/集中关闭、关联诊断 | 直接替代 Gameplay.WorldRunner |
| C# PhysicsSimulation | 固定量子、暂停/运行/单步、速度暂存、资源引用、复制观察、profile | 模拟整帧事务/回滚、自动写 Transform |
| 应用层 | 显式配置服务、拥有主线程和关闭次序 | 隐式启动求解、在 Play 固定步中直接调用 Step |

本阶段只有静态/动态 Box、重力、线速度、步进、位置/旋转/速度观察。
raycast/contact/constraints/capsule、CharacterMotor、Root Motion 碰撞、场景刚体组件及物理 MCP 工具未实现，不注册占位。

## 2. M2.6-A：薄原生模块和资源

- 数值内核只编译进 NcmaPhysics，Box2D/Jolt/Eigen 私有链接；不依赖 Core、Renderer、GLFW、GUI 或 hostfxr。
- Jolt Factory/注册与两线程 pool 由 DLL 单一共享 owner 持有；world 临时分配器独立。
- Update 同步等待 barrier，最后 3D world 释放后 join workers 再销毁 Factory。
- 2D 和 3D 是两个明确求解维度，不是 GameObject 世界。
- 原生保留 owner-thread/句柄/预算/finite 验证：即使 C# 已校验，也不能移除插件自保。
- 旧 C++ 比较调用暂时借助 private 方法复用 DLL；不是跨语言公共契约，H8 审计时统一处理。

验收：独立构建、SDK 类型不进入公共 C ABI、重复关闭无孤儿作业。不承诺可中断的 solver 超时或强制热卸载。

## 3. M2.6-B：版本化 ABI

保留 Physics 1.0 的 144-byte 函数表；1.1 使用 152-byte 表，在末尾 offset 144 增加 ReadCounters，旧字段/函数偏移不变。
两种 minor 分别协商；1.0 不伪装支持 raw counters，未知 major/minor 拒绝。

- 公共操作：CreateWorld、CreateBoxes2D/3D、DestroyBodies、SetVelocities2D/3D、Step、ReadStates2D/3D、Stats、DestroyWorld。
- 1.1 counters POD 72 bytes：尺寸/状态/维度/liveWorlds、liveBodies/sequence/batches/copiedBytes/errors/liveJobs、lastStepMilliseconds。
- 1.0 统计兼容路径保留历史原始样本，只有显式旧 Stats 查询才计算 quantile；不在 Step 中排序。
- C# 高层只使用 1.1 原始样本，自己聚合 profile。Native 不制定高层调度或帧预算。
- 2D/3D DTO 明确区分；内存借用仅限调用期，输出复制。C/C++/C# 进行尺寸与偏移检查。
- 资源 token 装载期不复用、跨 world/维度/失效检查；卸载后禁止使用，不进入资产。
- correlation 只是数值返回标签，不授权原生访问 GameObject。

单位与限制：米/秒，2D kg/m²、3D kg/m³，2D radians、3D xyzw quaternion。
最多16 worlds，每 world/批次4096 bodies；half extents [0.001,1000]，density [0.001,10000]，位置/速度/重力有界有限。
dt (0,0.25]；2D substeps 1..16，3D 当前1。3D density 不是质量。

## 4. M2.6-C：primitive 原子性与失败

- Create 全量校验与输出容量预检，内部失败回收本批新资源，输出不部分写入。
- Set/Destroy 全量验证 targets，拒绝重复/跨域/失效；静态 body 不接受速度写入。
- Step 先检查输出，再推进；只成功步进增加 sequence。
- Read 使用精确当前 sequence；不足容量/旧 sequence 不重试 Step。
- sequence 是模拟步数，不是不可变快照；创建/写速度可以改变同一序号下的状态。
- 内部求解异常 fail-stop，仅保留原始诊断与关闭；不宣称 native rollback。
- C# 在 Step/flush/read/counters 任一执行阶段不确定时也停止发布、进入 Faulted；已完成的 native 修改不撤销。
- 所有异常停留在 C++ Guard 内，以 bounded error POD 返回。

## 5. M2.6-D：C# 服务政策

PhysicsService 仅依赖 Interop 和自身契约：
- 默认/显式禁用不加载 DLL；启用失败报错，不静默降级。
- 最多16个独立 PhysicsSimulation；统一 owner-thread 生命周期。
- Guid ServiceId/WorldId 是运行时服务身份，不是场景资产 UUID。
- 创建前预分配 dense buffers/索引，成功 native 创建后无需增长 tracking。
- PhysicsBodyReference 校验 service/world/handle，不关联 GameObject。
- 集中逆序关闭；失败对象及 lease 保留，closing 状态拒绝新操作，可 inspection/显式重试 Dispose。

PhysicsSimulation：
- 固定步长配置默认1/60，创建后不可变。初始 Paused；Resume 后 Step，Paused 下 StepOnce。
- 不做 wall-clock 累积或跨 world 的“全局原子 Step”；调用者显式驱动每个独立 world。
- StageVelocities 全量验证后暂存，单批重复拒绝，多批同 target 最后提交值胜出。
- 物理步前按 dense 顺序合并成一个有界 native velocity 批次；无 pending 不调用 Set。
- CancelPending 不写原生；删除资源维护索引与 pending，不能把旧速度写给新 body。
- 成功 Step/Read/counters 后更新 published sequence 和可复制观察；故障后拒绝正常状态复制。
- CopyBodies/CopyStates 分页复制，不提供指向内部数组的视图。
- Inspect 明示 managed state、published sequence、SnapshotValid、NativeAvailable、raw counters 与 profile。
- native 不可用时报告缓存数据并标 unavailable，不能假装当前状态有效。
- median/p95 在 C# inspection 时从最近256次样本计算，最大值按 simulation 生命周期累计。
- hot path 复用缓冲，不做 JSON、反射或逐 body native call；观测 DTO/日志属于冷路径。

## 6. M2.6-E：配置、诊断和 AI 边界

项目 physicsEnabled 默认false，缺省仍禁用。候选入口拥有 PhysicsService，只显式启用模块；不自动创建世界。
应用日志复用 correlation；服务独立保留32条复制诊断，message最多512 UTF-8 bytes，记录丢弃数。
诊断有 service/world/sequence/operation/code，非任意 Agent 执行文本。
复制 bytes 不是内存占用，idle workers 不是 outstanding jobs，托管当前线程零分配不等于原生/整帧零分配。

尚未实现：Console 物理面板、注册到 live MCP 的物理工具、场景物理命令。
未来 Agent 只读取复制观察、提出受控建议；场景修改须经 Editor.Core 同一权限/事务/Undo 路径。
本次资源操作只作用于独立数值服务，不是编辑器场景 mutation。

## 7. M2.6-F：H6 验证矩阵

1. C ABI：1.0/1.1 布局、版本、短表、null 输出、wrong thread、stale/foreign/duplicate handles。
2. native：容量预检不修改、真实 Jolt 容量耗尽中途创建失败回收、执行 guard fail-stop、资源/作业关闭。
3. low-level managed：禁用/缺失/错误模块/缺依赖、finite/尺寸/密度/dt、copied readback。
4. service：2D/3D 同时reference，pause/run/single step、完整暂存校验、last-write-wins、删除/重建/取消、复制分页、managed profile。
5. fault/lifecycle：测试内反射使低层资源失效，验证不再发布；测试内破坏/恢复 close handle，证明关闭失败保留 lease 并可重试。
   这些是白盒生命周期/执行 guard 注入，不声称制造真实硬件故障。
6. reference：h=1/60、180 steps、gravity/floor/dynamic box，落地容差0.08m、静止速度<0.1m/s，不要求跨 CPU 位相同。
7. performance：两维度各0/64/1024/4096 bodies，4 warmups +32 rounds。
   low-level Step/Read 与 service stage/flush/Step/copied snapshot 分别记录，后者持续设置0.1m/s速度；不能据此保证碰撞压力帧率。
8. 生命周期：32 world循环、32 module循环、32 managed-service循环；16 world预算和重复Dispose。
9. Build.bat Debug/Release 全矩阵、managed/native smoke、Python inspect/unittest；零新代码警告。保留并披露旧测试宏覆盖警告。

## 8. 为什么不接 Play

WorldRunner 可中止托管组件/结构/信号提交，但不能自动撤回 native solver 已推进的状态。
M4 须先定义跨域提交/恢复或整体 Faulted 策略，明确 Transform/Root Motion/physics 唯一运动权威。
M2.6 不添加 PhysicsSystem、CharacterMotor 或场景刚体，也不把独立服务的暂停/单步冒充游戏 Play。
