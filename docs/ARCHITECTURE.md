# NcmaEngine 引擎架构

2026-10-05 更新：正式应用入口改为 C# apphost → Application/Editor.Services →
Editor.Core/Scene/Gameplay/Runtime；Platform/Gui/Renderer/Physics/FBX/动画通过原生插件接入。
旧 C++ Editor、ManagedHost/ManagedSceneClient/DotNetGameplayRuntime 和 Ncma.Managed.Host
源码、专属测试与构建接线已移除。后文旧 hostfxr/桥描述属于历史迁移记录，不能作为当前入口。
本次采用用户确认的自动回归门槛；人工、自包含和长稳验收继续待完成。

更新：2026-10-04。定位：模型与动画驱动的动作游戏引擎。

本文区分目标架构和实际实现。标记“已实现”只表示所述切片可运行，不代表整个系统完成。
A 方案已确认，AI 深度参与场景、动画、UI、工具和引擎扩展。
Python 游戏脚本已移除；托管 World/固定步/事务基础已实现，C++ SceneWorld 和原生 World 导出已删除。
编辑器及隔离 Play 已接入 C# World；统一 Editor.Core/EditSession 编辑链路已实现；正式默认入口已切换为 C# apphost，保留明确的部署恢复。Platform/GUI/DX11 Renderer 与独立 Physics 插件及 M2.5 A–F 业务迁移已落地，人工验收未完成，见 [H5 记录](M2_5_H5_DELIVERY_REPORT.md)。

## 1. 总体分层

```text
C# Editor / Player / Headless                         [C# 默认 Editor 与 Player/Headless 已实现有限切片]
                  |
C# Runtime: World + GameObject + Component + Systems  [权威 World 已接入现有编辑器]
                  |
          C# 游戏逻辑 / 应用服务                      [PlaySession/WorldRunner 已派发 OnFixedUpdate；OnUpdate 只读]
          /             |                 \
原生插件适配层     可选 Python 模块适配层      独立网络服务
     |              |                         |
版本化批量 C ABI    请求 / 观察 / 结果          应用同步适配
     |              |
C++ Renderer       Python AI / 专长模块 / 插件
C++ Physics        gRPC worker：建议默认，未实现
C++ 数值内核        pythonnet / ZeroMQ：可选，未实现
```

依赖由应用向下组装；网络和 Python 不进入 GameObject/Behaviour 基类。
编辑器、Player 和 Headless 的业务应共用 C# Runtime，图形后端可选。
以上是目标分层，不是当前 EXE 的调用结构。

语言职责：

| 语言 | 负责 | 不负责 |
|---|---|---|
| C# | 游戏逻辑、World/组件、生命周期、System 调度、场景/资产元数据、编辑器命令与应用服务 | 直接调用 D3D/Vulkan、逐帧等待 Python 推理 |
| C++ | 渲染、物理、有测量依据的动画/导入/计算内核 | 通用游戏框架、权威 World、编辑器业务或联机规则的长期实现 |
| Python | 可选 AI、训练、分析、生成、特殊模块与插件/工具开发 | GameObject 脚本、Behaviour 生命周期、游戏主循环与 live World 修改 |

## 2. 场景与对象模型

目标采用 World + GameObject + Component + 独立 Systems，不采用 Godot Node 或 UE Actor 模型。

- SceneAsset：可持久化对象/组件描述与资产 UUID，不保存运行时句柄。
- World：运行时对象/组件权威存储、查询、启用和创建/销毁边界；目标由 C# 持有。
- GameObject：身份与组件容器，没有对象语言选择器，只能挂 C# Behaviour。
- Component：组合数据/能力；Behaviour 是 C# 生命周期扩展，不承担网络或 AI 传输。
- Systems / WorldRunner：在明确更新阶段处理组件，集中提交结构命令。
- 场景组织：扁平对象列表，无父子所有权、变换继承或递归删除。编辑器分组不改变运行时语义。
- 骨骼、动画图与 UI 文档内部可以有层级，但不成为场景对象树。
- 目标支持无 Transform 的逻辑对象；新的托管 World 已支持；当前编辑器新建对象显式附加 Transform；读取/恢复允许无 Transform 的逻辑对象。

UUID 用于持久身份；World/对象运行时引用用于访问校验，删除、恢复或切换 World 后失效。
旧 `.ncscene` 格式与 C++ 兼容 codec 已删除。C# SceneDocument JSON v1 是唯一 `.ncmascene` 格式，保存全部注册值组件与脚本配置，并支持原子文件替换；Prefab、资产引用与完整 SceneAsset 资产流水线未实现。

## 3. C# 运行时与游戏逻辑

目标模块划分：

| 模块 | 职责 | 当前状态 |
|---|---|---|
| Application / Services | Editor、Player、Headless 入口和服务生命周期 | C# apphost 与共享运行服务已实现；默认 Editor 已切换，部署/恢复自动回归通过；自包含/人工待验 |
| World / GameObject | 扁平对象存储、UUID、运行时引用、创建/销毁 | C# World 已接管编辑器和 Play，C++ 只持有不透明会话令牌 |
| Component / TypeRegistry | 稳定 TypeId、类型池、字段/schema、通用查询 | 托管类型/schema 注册和组件存储已实现基础；类型池/通用查询未实现 |
| Behaviour Host | C# 生命周期、实例绑定、Export、程序集加载/重载 | 已接入托管 World；私有状态迁移、自动重建未实现 |
| WorldRunner / Systems | 固定步、呈现步、依赖和结构命令提交 | PlaySession 固定步/顺序 Systems 已接入编辑器；输入/插值/运行结构命令已实现基础 |
| SceneAsset / Serialization | 通用组件记录、资产引用、版本迁移 | C# `.ncmascene` JSON v1 通用组件读写/原子保存已实现；旧 .ncscene 不兼容；资产引用/迁移流水线未实现 |
| Editor Commands | 文档修改、事务、Undo/Redo、隔离 Play | Editor.Core 持有完整文档事务/唯一 Undo；ImGui 提交 UUID 意图，原生命令栈已删除 |
| Gameplay Services | 输入、角色、动作、战斗、任务等游戏 API | 未实现完整 SDK；当前只有基础门面/示例 |
| Movement Coordination | 绑定组件唯一发布、数值边界/有界意图与跨域故障 | M4.1 C# `Ncma.Movement`；M4.2真实Jolt数值；M4.3固定步角色/跟随相机；M4.4 XZ/Yaw根运动及视觉去重；M4.5同量子Health发布/动作战斗；M4.6复制Debug与审批只读MCP |

M4.7增加默认关闭的可信Character分段测量及联合压力测试。托管snapshot/boxing/校验路径仍是
正确性底座，不是已验证高性能ECS；本机程序化测量记录实际CPU/GC/upload成本，不作FPS承诺。
现有GPU last-valid统计没有sample-frame ID，不能冒充独立逐帧GPU样本。见[M4.7交付](M4_7_DELIVERY_REPORT.md)。

当前 C# Behaviour 已支持挂载、禁用、删除、数值/布尔 Export 编辑及隔离 Play。
M1.3 已新增 Ncma.Gameplay.PlaySession：一个 WorldRunner 派发 OnFixedUpdate/顺序 Systems，帧后 OnUpdate 只读。默认 1/60 秒、最多追赶 8 步；严格与交互时间策略、Pause/Resume/Step/Faulted 已接入 ImGui。安全重载先隔离预检再清理/激活，成功 Paused，失败保留旧暂停或激活后 Faulted；重置私有状态，私有状态迁移未实现。
详细构建与作者流程见 [BUILDING.md](BUILDING.md)。

M4.1：Runtime 通用组件权威由可信宿主建立，不是脚本公开 token；每个非初始化固定步必须
由 owner 发布所有需发布组件，独立 WorldRunner 不能跳过运动发布而推进 tick。
M4.3增加同一通用权威的只读冻结配置（不重复发布）；捕获非法配置写同样poison候选步。
完整类型成员集合也冻结（含空集合）；三类物理/跟随配置以可信`runtimeAttachable:false`注册，
即使无绑定Play没有数值服务也拒绝运行时新增，其他普通组件保留默认挂载规则。
Gameplay 保留无物理依赖的内部协调钩子，`Ncma.Movement -> Gameplay -> Scene/Runtime/Managed`，
数值 adapter 只交换有界值（不是 native ABI）。默认无物理项目不创建数值world；M4.3显式绑定的
Editor/Player由应用层组合 `Ncma.Characters -> Movement/Physics/Scene.Rendering`，底层无反向依赖。
数值执行后托管提交失败保持旧 World tick，但可能已改变数值域：耦合快照 invalid、会话 Faulted，
不能 Resume 或伪装 solver rollback。Stop 关闭失败保留资源/权威、允许显式重试；耦合 Reload
关闭旧 adapter 后从冻结 startup 文档重建 world identity/session，重置资源 sequence，**不倒退 tick**。
普通无耦合 Reload 的原有预检/暂停语义不变。见 [M4.1交付](M4_1_DELIVERY_REPORT.md)。

高频访问优先在托管组件池内完成；跨原生插件使用可复用批量缓冲区，不逐对象反射或 IPC。
当前过渡门面保留 Transform 批量读写、安全引用和信号邮箱，最多 4096 项。
这些接口现在直接访问 C# Runtime.World，不再通过 P/Invoke 访问原生 World。见 [WORLD_ACCESS.md](WORLD_ACCESS.md)。

## 4. 原生性能插件

目标为可按需装载的 Renderer、Physics 和必要数值内核。当前 C# 默认入口使用版本化 PluginLoader、Platform/GUI/DX11 Renderer 模块与独立 NcmaPhysics/Ncma.Physics；NcmaNative 仅保留动画/角色等数值资源，不再承担旧场景/游戏宿主桥。正式入口切换不等于完整图形或人工验收完成。

M3.3：Renderer ABI 1.2 additive query → scene-render v1（static-unlit-v1）/v2（cpu-bind-pose-v2）/独立v3（resource-pbr-v3）。
C# 负责帧外 typed mesh/绑定姿态、原始skin/palette保留、工具侧PNG/JPEG/mips、UUID/generation/hash cache/lease、
作者MaterialDefinition/MaterialSet与统一可逆命令、公共Graph受限typed stage/Feature/Stage/pipeline替换；native执行驻留mesh/texture/material/离屏target与最小GGX PBR/AlphaMask/normal。
旧1.0/1.1 reference不变，最终完整Debug/Release与实际DX11图像/Debug Layer通过，G3资源切片关闭。
后续 M3.4 已补正式 Scene/Editor/Player 静态多对象、单方向光阴影/HDR；M3.5 增加最小片段播放与 DX11 GPU 蒙皮（见下文）；M3.6 候选增加 GUI1.3 opaque离屏展示，见 [交付记录](M3_6_DELIVERY_REPORT.md)。该历史切片尚无IBL；当前M7.3-B独立GPU IBL已通过自动验证，C1资产/配置已实现，正式宿主C2与通用多阶段资源图仍未完成，不称完整后端。
契约与边界见 [M3.3 GPU ABI](M3_3_RENDER_ABI.md) 与 [B/C/D交付记录](M3_3_BCD_DELIVERY_REPORT.md)。

M3.4：独立 `Ncma.Scene.Rendering` 注册 UUID/值组件、完整文档组合校验与 committed World 提取/缓存；普通对象不强制 Transform，不逐帧序列化。`Ncma.Assets.Runtime` 帧外严格解析 typed UUID/NCA/hash/材质闭包并 pin 文件，Play 保留旧代。新增 `Ncma.Rendering.Scene` 适配层拥有 CPU/GPU 租约，Renderer ABI 1.2 独立 query v4 执行静态数值批次：单方向光 PCF/近似 PCSS、alpha-mask shadow → GGX HDR → ACES/sRGB；默认及注册 Feature/stage/pipeline 替换共用 typed graph。Editor/Player 实际项目走真实场景，Player 必须显式 `sceneCamera`；空/Headless 不创建 3D 资源。只读检查、light-space 独立裁剪、Edit/Play/Stop/reimport/resize 与图像验证见 [GPU 记录](M3_4_GPU_DELIVERY_REPORT.md) 和 [v4 契约](M3_4_RENDER_ABI.md)。后续 M3.5 additive query 5 的蒙皮见下文，M3.6 GUI离屏候选见 [记录](M3_6_DELIVERY_REPORT.md)；该切片的场景CSM/contact、IBL、透明、多光、Vulkan尚未实现；当前IBL进展见下表；不称完整后端。

统一边界：

- 使用版本化 C ABI、固定布局 POD、opaque 资源句柄、有界批量输入/输出。
- 不跨界传 STL、Eigen/Jolt/Box2D 类型、C++ 异常或语言对象引用。
- 明确内存分配/释放、线程亲和、资源失效、作业结束和 shutdown 顺序。
- C# 保留权威对象状态；插件只保存资源和必要子系统数据，避免两份通用 World。
- 热卸载未经资源生命周期验证不得开放；当前插件热卸载未实现。
- 平台和图形 API 头文件限制在原生模块内。

已有依赖继续使用：Eigen（原生数学）、GLFW（窗口/输入）、ImGui（当前编辑器）、
spdlog（原生日志）、Box2D（2D 求解）、Jolt Physics（3D 求解）、ufbx（FBX 导入）。
它们不自动成为 C# Runtime 的通用依赖。

### 渲染

目标：统一API无关资源与提交契约。本版本仅实现DX11；Vulkan/OpenGL保留扩展边界，实际后端延后下一版本。
2026-10-09执行顺序：M6结束→M7（DX11）→M8→M9；任何顺序调整不改变已有人工/目标/性能/长稳验收状态。

| 能力 | 当前状态 |
|---|---|
| RHI 资源、管线、RenderPass/Draw 契约与校验 | 已实现基础 |
| D3D11 设备、交换链、缓冲/纹理、深度、绘制与 resize | 已实现预览路径；不代表完整生产后端验收 |
| Metallic-Roughness / GGX、HDR、色调映射 | reference 与 M3.4 静态多对象 DX11 场景已实现 |
| 方向光级联阴影、PCF 与接触硬化过滤 | 已实现预览；仍需真实动作场景质量/性能验证 |
| Vulkan loader/runtime 探测 | 已实现 |
| Vulkan 设备/交换链/绘制和双 API 参考场景一致性 | 未实现 |
| OpenGL实际后端 | 未实现；与Vulkan实际渲染一并延后下一版本，本版本只保留扩展边界 |
| 材质/贴图资产与六槽工作流 | M7.2闭合合同/ORM-MRA/预设/运行包及68项GPU自动检查通过；完整用户素材与人工验收待完成 |
| 真实DX11 IBL | M7.3-B资源/绑定/353项独立图像自动验证通过，API0/0；共享服务C2-B已接入，正式Editor/Player接入C2-C未实现 |
| 共享场景环境执行 | C2-B确切配置/资产/profile3/不可变缓存/显式帧外刷新与view准备，87项实际图像/动画/源无关custom/边界及完整双配置通过；不等于正式宿主启用 |
| 延迟/聚类渲染 | 未实现 |
| 环境Shader文件与完整准入 | C2-A显式v2/最多9包/双变体/全profile共享skin/独立query15，71项与完整双配置通过；不等于正式宿主IBL |
| 线性环境资产、离线IBL数值与场景配置 | M7.3-A数值123项；C1 ncenv/NCE read pins/NCP/值组件与原命令合同，新增29+1项和完整双配置通过；C2/C3默认宿主/部署未完成 |
| 场景渲染提取与静态多对象 | M3.4 已实现，M3.5 增加 DX11 蒙皮；M3.6 候选 GUI1.3 合成视口纹理，G6未关闭 |
| 独立 Renderer 插件和 C# 应用装载 | M2 候选 DX11 reference 已实现、自动测试通过；人工/生产验收未完成 |

当前 D3D11 使用 HLSL shader model 5；M7.1已实现闭合官方/user Shader反射、source-free包和实际GPU准入，不是任意Shader安全沙箱；SPIR-V管线未实现。环境CPU包始终GpuValidated=false；独立GPU IBL证据见[B3交付](M7_3_B3_DELIVERY_REPORT.md)，正式宿主当前拒绝enabled环境，资产/配置边界见[C1合同](M7_3_C1_RUNTIME_CONTRACT.md)和[交付](M7_3_C1_DELIVERY_REPORT.md)，新增环境Shader文件与准备边界见[C2-A合同](M7_3_C2A_RUNTIME_CONTRACT.md)及[交付](M7_3_C2A_DELIVERY_REPORT.md)。共享SceneRenderSession已通过87项真实图像/资源/边界/动画/自定义文件与完整双配置，见[C2-B合同](M7_3_C2B_RUNTIME_CONTRACT.md)、[交付](M7_3_C2B_DELIVERY_REPORT.md)；正式宿主C2-C仍待实现。
构建和编辑器 smoke 通过不等于完成双 API 验证；不能宣称 Vulkan 已可渲染。

### 物理

Box2D/Jolt 独立 Physics 插件与 C# 批量客户端已实现，见 [M2.6 交付记录](M2_6_DELIVERY_REPORT.md)：Physics ABI 1.1 原始计数/单次耗时（保留 1.0），独立 2D/3D Box/density/重力/线速度/旋转状态与模拟序号；C# PhysicsService/PhysicsSimulation 管理世界、暂停/单步、有界速度暂存、复制快照、诊断/profile 与故障关闭。C++ 只保留求解器资源、边界自保和数值批处理，应用策略不进入原生。最多 16 world、4096 body/world/batch；Jolt factory/job pool 集中在 DLL，数值内核不再编入 NcmaCore。项目 physicsEnabled 默认 false，启用后仅在显式场景绑定的Play中创建耦合数值world，独立PhysicsSimulation仍保持原路径。
M4.2新增Physics module1.2查询独立Character API1.0：实际Jolt CharacterVirtual capsule、copied grounding/contacts及closest body/character ray/sweep；显式`characterSupport:true`才协商，默认独立宿主1.1及physicsEnabled=false不变。M4.3 C# `CharacterData/BoxColliderData/FollowCameraData`仅存值/UUID；应用独立服务协调真实solver、唯一Transform发布、gravity/jump/grounding/world-space WASD/yaw与只读跟随/插值。Editor隔离Play与Player/Headless共用；失败保持fail-stop，GPU/动画先释放，关闭失败保留依赖并可重试。M4.4接入XZ/Yaw root motion、碰撞约束与视觉根去重；M4.5候选加入closest-ray动作战斗与同量子Health发布，通用Gameplay碰撞事件/连续武器hitbox仍未实现。不把不可逆Step冒充托管事务回滚。契约/测试/限制见[M4.2交付](M4_2_DELIVERY_REPORT.md)、[M4.3交付](M4_3_DELIVERY_REPORT.md)、[M4.4交付](M4_4_DELIVERY_REPORT.md)和[M4.5交付](M4_5_DELIVERY_REPORT.md)。
物理句柄仅标识求解器资源，不替代 GameObject 身份。

## 5. 动作动画与 FBX

参考 UE5 的职责划分，但不兼容 UE 资产，也不复制 Actor/网络耦合。

目标分工：

- C#：Animator 参数与状态、动作/战斗规则、通知消费、运动权威。
- C++：有性能依据的批量采样、姿势混合、蒙皮和根运动数值提取。
- 编辑器：动画图、片段/Montage 时间线与调试工具；编辑是可撤销命令。
- Python：离线分析/生成或异步决策建议，不驱动角色 Behaviour。

| 模块 | 当前状态 |
|---|---|
| Skeleton/Clip、采样、姿势混合、逐骨骼遮罩 | 已实现原生独立运行时 |
| Root Motion/Notify 区间数值 | 独立demo原生 kernel数值；M4.4 C# immutable根轨迹/共享committed clock/场景Jolt约束与视觉去重；M4.5候选tick Notify/中断闭合 |
| Idle/Run/Attack/Dodge 实验室、调试与 Undo/Redo | 已实现独立程序化预览 |
| C# ActionAnimationSession / 动画 C ABI 2 | 已实现：C# 时钟/策略/128 项历史，原生不可变 demo + 复制数值；旧 ABI 1 拒绝 |
| FBX 骨架/蒙皮网格/动画导入与 CPU 线框预览 | 已实现，基于 ufbx；候选 C# Orbit/骨架/CPU 蒙皮线框已迁移（最多一万三角形） |
| FBX 角色资源 C ABI 2 | 已实现：不可变资源、复制报告/索引、显式时间采样/CPU 蒙皮；角色 ABI 1 拒绝 |
| 候选 C# FBX 独立播放/暂停/片段/Undo/Redo | 已实现有界预览服务/完整报告分页及控件；可见窗口人工验收未完成 |
| 动画图类型/引脚/连线及基础合法性检查 | 当前M6.7-B严格v4含BlendSpace/Layer/CachePose与事件/中断，v1/v2/v3拒绝；有界C#配方/提交式状态机及完整B双配置自动门禁通过 |
| 动画图作者工作区/共享事务/获批MCP | M6.4自动候选：类型引脚、复制草稿、参数/状态/条件、checked保存及Undo/Redo、双人工批准propose/transaction、独立真实NCA/GPU预览；人工/高级节点未验收 |
| 图过渡中断/提交事件/debug | M6.5 A–D双配置自动候选完成；持久事件/策略、轨道UI、精确获批独立序列MCP、32actor与正式Player联合通过；非live控制/推理服务，人工/素材/目标/性能/长稳开放，见C契约与D交付 |
| BlendSpace | M6.6-A–D完整双配置自动候选通过；共享相位/主贡献事件-root/真实NCA配方，typed作者与精确获批AI权重扫描；0/1/8/32 Editor/搬移正式Headless-DX11 Player及调度/fault/reload联合测试通过；人工/素材/目标/性能/1h未验收，见M6_6_D_DELIVERY_REPORT.md |
| 分层遮罩与缓存姿态 | M6.7 A–C完整双配置自动候选通过；精确骨架hash/path、override/additive/CachePose、typed作者及独立获批骨骼/缓存诊断，实际NCA/CPU-GPU/正式Player验证；人工/素材/性能/长稳仍开放 |
| Montage、IK、重定向、动画压缩 | 未实现完整功能 |
| 场景 SkinnedMesh/ClipPlayback 与 DX11 GPU 蒙皮 | M3.5 已实现；验收范围见交付记录 |
| Animator 图 / CharacterMotor | M6.3-A/B/C/D完整双配置自动候选通过；真实图根/唯一Jolt/skin-shadow及精确获批运行MCP，人工/素材/性能/长稳未验收；M4独立动作策略不变 |
| 正式动作/连击/命中规则及 C# 通知到游戏事件链路 | M4.5候选Idle/Run/Attack/Dodge、buffer/cancel/combo、closest-ray伤害/无敌与提交后CombatEvent快照；通用GameplaySignal转发/连续武器hitbox未实现 |

2026-10-06：M3.5 使用独立 `ncma_pose_get_api`/pose ABI 1.0，仅不可变数值，不是已删除的旧 Animation ABI 1，也不恢复 gameplay host；原有 animation/character ABI 2 保持原语义。纯 C# ClipClock/提交后只读观察器、正式 NCA/Scene/Edit/Play 生命周期与 query 5 compute-prepass 已接线；原始 GPU source 常驻，每帧 bounded palette，主画面与阴影共享 GPU output，未验证的 animated bind-AABB 裁剪关闭。根位移只报告不写 World/Physics。资源/GPU/GC 证据与最终 G5 状态见 [交付记录](M3_5_GPU_DELIVERY_REPORT.md)，布局/线程/预算/释放见 [契约](M3_5_RENDER_ANIMATION_ABI.md)。没有 skin 不初始化 pose/rig/palette ring，Null 不部署 pose DLL；通用 3D-capable DX11 包含 lazy DLL，专用纯 2D 裁剪包未实现。

FBX 窗口与动作实验室是独立会话，尚未与场景角色或实时 MCP 贯通。
详见 [ANIMATION.md](ANIMATION.md)、[FBX_IMPORT.md](FBX_IMPORT.md)。

M4.5候选固定步顺序（Animator图/AI传输未实现）：
输入 → C#游戏逻辑/动作候选与根区间 → 原生角色数值 →
C#候选Transform/Health与命中事件 → 托管提交 → committed动作/根时钟 → 姿态/GPU渲染数据。
根运动提出位移，CharacterMotor 经过碰撞后决定最终位置，不由多个系统同时写 Transform。

## 6. 编辑器参考图与独立UI

当前编辑器：C# Ncma.Editor.App正式入口 + 原生GLFW/ImGui/GPU插件，包含对象列表、Inspector、
视口、资产分类、Console、Play控制、动作实验室、FBX窗口及已实现的有界诊断/审批检查。
C#持有共享场景命令/唯一历史、Inspector草稿、Play/脚本/MCP和本机偏好；旧C++ Editor及专属桥已移除。
固定工作区的侧栏宽度/工具栏高度可保存；自由docking、多场景文档和完整UI制作未实现。
新的视觉与工作区参考采用[用户图1](EDITOR_INTERFACE_REFERENCE.md)：深蓝主题、顶部模块工具栏、
左侧扁平对象列表、主视口、Inspector、可折叠AI/工具侧栏及底部资产/Console。
保留NcmaEngine品牌，不采用图中的Node场景树、不将图片中的AI/Vulkan/性能数据当作实现证据。
H5 自动回归与人工未验项见 [当前交付](M2_5_H5_DELIVERY_REPORT.md)，默认入口已切换，但 H5 人工验收仍待完成。

目标 C# Editor services：资产/场景文档、Selection、Inspector schema、命令/事务、
动画图文档和 UI 文档。原生仅承担平台/绘制能力，不承载高层业务规则。

UI 单独使用 UiDocument，不采用场景 GameObject 的父子树：

| 能力 | 当前状态 |
|---|---|
| C# UiDocument/Frame/Group/Rectangle/Text/Image/基础控件、样式与类型化Token | M5.1–M5.6自动候选；旧C++ UI模型已删除，严格.ncmaui v1与复制身份 |
| Free/横/纵、Fixed/Hug/Fill、DPI/安全区/裁剪/缓存布局 | M5.3自动候选；与预览/运行时共享，旋转裁剪明确拒绝 |
| DX11驻留UI批次、图片/透明/圆角、字体数值与控件事件 | M5.4–M5.6实际像素/生命周期/缓存候选；不是glyph atlas、完整IME或Vulkan |
| UI文件持久化、精确授权与Undo | M5.2复用Assets.Authoring日志和Editor.Core唯一历史；正式画布/Agent工具接线仍待后续阶段 |
| UI制作画布、选择/拖拽/缩放/对齐与参考图工作区 | M5.7未实现；视觉/工作区依据用户图1 |
| UI组件实例/覆盖、项目Player HUD加载与裁剪发布 | 未实现；自动候选不关闭M5完整门禁 |

契约、样例和开放项见[M5.1至M5.6方案](M5_1_6_IMPLEMENTATION_PLAN.md)与[交付记录](M5_1_6_DELIVERY_REPORT.md)。C#拥有布局/权限/文本缓存/控件策略，Renderer query6和独立NcmaText仅执行数值；原生ImGui仍是编辑器表现，不是游戏UI。

## 7. 可选 Python 特殊模块与插件

Python 游戏宿主、游戏 SDK、示例、GameObject 语言选择、挂载/Play/重载入口已移除。
不提供 Python Behaviour，也不保留双语言游戏脚本兼容运行路径。

保留的已实现能力：ncma_tools CLI、项目清单、只读 FBX 报告、隔离动画 stdio MCP。
未来可选 AI/推理/训练、分析、内容生成和特殊插件，但模块加载器与应用通信未实现。

| 选项 | 定位 | 当前状态 |
|---|---|---|
| gRPC worker | 建议默认；独立进程、异步请求/结果 | 未实现 |
| pythonnet | 可信模块进程内互操作；单一解释器所有者/GIL 管理 | 未实现 |
| ZeroMQ | 有测量需求时使用批量/流式消息 | 未实现 |

不是三个默认依赖。模块接受有界观察/快照，返回结构化建议，不持有 live World。
C# 校验权限、版本、会话/World 身份、tick/revision、时效与范围后应用命令。
模块故障/超时走 C# 回退，不阻塞模拟 tick。进程内模块不是安全沙箱。
详见 [PYTHON_MODULES.md](PYTHON_MODULES.md)。

## 8. 独立网络与 Agent/MCP

网络是可选独立服务，目标以 C# 维护连接、消息、会话和应用同步适配，当前未实现。
GameObject/Component/Behaviour 不含 RPC、复制标记、网络角色或连接生命周期。
收包回调只入队，由应用校验并在安全边界应用；AI IPC 不自动成为多人协议。
详见 [NETWORKING.md](NETWORKING.md)。

Agent/MCP 是能力接口，不依赖某个模型厂商：

- 稳定能力名、描述、JSON 输入/输出 schema、风险分类和确定结构化结果。
- 默认只读；修改经编辑器同一命令/Undo/事务路径，破坏性操作需显式授权。
- 当前能力注册表与隔离动画 stdio MCP 已实现，动画会话有 revision guard 和独立 Undo/Redo。
- C# Editor.Core 已提供 8 个共享 v2 能力及完整文档原子场景事务，含默认只读、权限、版本、重试去重和 Undo/Redo。
- 托管命令网关与本地 MCP 已接入同一 ImGui EditSession；UI、动画图、项目文件修改工具未实现。
- 引擎扩展目前仅支持可信 C# 启动时注册值组件/schema；动态模块加载/编译/发布工具未实现。
- 不开放任意 Python 执行，不因提供 MCP 就赋予 AI 直接写 World 权限。

## 9. 当前实际运行结构与兼容规则

```text
out/bin/NcmaEngine.exe                 C# / .NET apphost
    ├── Application / Editor.Services / Editor.Core → Scene → Runtime.World（唯一场景权威）
    ├── Platform / Gui                C ABI → GLFW / ImGui 原生插件
    ├── C# Behaviour → Ncma.Managed → 同一 C# World（不跨原生 ABI）
    ├── D3D11 参考预览
    └── 动作实验室 / FBX 独立预览

Ncma.Runtime.Tests → headless 固定步与 World 测试
Ncma.Scene.Tests → 完整文档与严格文件格式测试
Ncma.Editor.Core.Tests → 共享命令 / 完整历史 / 草稿 / 文件状态测试
（均无原生依赖；活动 ImGui 复用同一 Editor.Core 服务）

可选 Python CLI / 隔离 MCP → 动画/角色专用 C ABI
（没有 Python 游戏脚本宿主，没有 live 编辑器连接）
```

- 原生插件基础 ABI 升为 v2，移除所有 ncma_world_* 和旧 World/GameObject 版本导出；旧二进制须重建。
- 旧托管 Gameplay bridge v5 / Scene host v6 随专属消费者一起退出，不保留转发层；C# 游戏服务直接访问托管 World。当前原生插件仍使用版本化 C ABI，不传 CLR 对象或 C++ 指针。
- 唯一场景文件为 `.ncmascene` JSON v1；不带语言选择或兼容槽，支持空对象、全部注册组件与 C# 脚本配置。
- 旧 `.ncscene` v1-v6 不再读取、转换或保存；旧读写和迁移代码已删除。
- 加载失败不改源文件或目标场景；不静默删除/转译。旧 Python 逻辑需作者改为 C# 或独立模块。
- 不序列化运行时句柄；编辑与 Agent 都使用可撤销命令。
- Build.bat 是 Windows 规范入口；原生不再需要 Python 开发 SDK 或运行时 DLL。
  Python 仅为可选工具验证依赖，使用 -SkipPython 可跳过；完整 C# 游戏发布仍未实现。
- 正式默认入口已切换；不宣称完整场景渲染/物理、Python 通信或整个 M2 验收已完成。原生模块和独立 C# Physics 服务仍属于已实现的有限切片。

## 10. 开发顺序与参考边界

已完成首切片：独立 C# headless World/组件、固定步、快照和 AI/编辑器共用事务基础。
本次已接管编辑器/Play 的 World、Behaviour 与完整新场景文档，不保留 C++ World。M1.2 已将场景命令/历史迁入 C# Editor.Core，删除原生撤销栈。M1.3-A/B 已接入固定步与运行控制；下一切片为输入/插值、运行结构命令、信号/生命周期/重载安全边界，随后 live MCP；之后反转主入口并逐个插件化渲染/物理。
新的字典/装箱存储尚未性能优化，不能宣称类型池/ECS 或完整托管游戏运行时完成。
AI 的分域权限、能力状态与闭环见 [AI_DEVELOPMENT.md](AI_DEVELOPMENT.md)。
动作游戏功能优先打通 FBX 场景角色 → GPU 蒙皮 → Animator/CharacterMotor →
碰撞感知 Root Motion/通知 → 动画图与 Montage 编辑。
可选 Python 适配器和网络服务按实际需求接入，不要求基础游戏启动安装 Python。

完整迁移门槛见 [FRAMEWORK_REFACTOR.md](FRAMEWORK_REFACTOR.md)，产品路线见 [ROADMAP.md](ROADMAP.md)。

| 参考 | 仅参考的范围 |
|---|---|
| ProwlEngine / Unity | C# 组合式对象、编辑器与资产工作流 |
| Unreal Engine 5 | 动作动画、状态机、Root Motion、通知与节点编辑 |
| [用户图1](EDITOR_INTERFACE_REFERENCE.md) | 编辑器视觉和工作区布局，不采用图中场景树/品牌/能力声明 |
| Infernux | 独立 Python 模块/工具分层 |

Godot、Piccolo、Hazel 不作为框架参考。参考不等于运行依赖、资产兼容或对应功能已实现。

## M1.1 implemented document boundary

Complete Ncma.Scene document snapshots v1 now cover all registered components and Behaviour/Export metadata. The retained C++/ImGui shell uses opaque snapshots for Undo and Play; C# .ncmascene JSON v1 files persist complete documents with atomic saves. Old .ncscene compatibility is removed. Shared commands, fixed-step/input/runtime commands and scoped live MCP are implemented; asset references/pipeline remain pending. See [M1.1 implementation](M1_1_SCENE_DOCUMENT.md).

## M1 新增边界（2026-10-04）

Ncma.Gameplay 负责输入消费、RenderFrameView、运行命令、生命周期和原子信号；结构安装不全量 Restore，不使存活对象引用整体失效。Editor.Core 编辑/Undo 仍是全量文档恢复。
Ncma.Editor.Protocol（纯 DTO）→ Editor.Transport（owner-thread 安全队列）→ 当前 Editor.Core。Editor.Mcp 仅依赖 Protocol，stdio helper 不加载场景或原生 DLL。IO 线程不执行 validator/脚本。
默认关闭/只读；用户批准精确请求后允许同事务修改/局部绑定/有范围历史，删除 UUID 另确认；Play/文档替换撤权。没有 Agent 任意代码或文件网关。版本：Scene 6、Gameplay 5、IPC 1、能力 2、native 2、场景 JSON 1、manifest 10。
见 [EDITOR_MCP](EDITOR_MCP.md) 与 [交付报告](M1_DELIVERY_REPORT.md)；人工客户端验收仍待完成。
