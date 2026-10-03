# NcmaEngine 引擎架构

更新：2026-10-04。定位：模型与动画驱动的动作游戏引擎。

本文区分目标架构和实际实现。标记“已实现”只表示所述切片可运行，不代表整个系统完成。
A 方案已确认，AI 深度参与场景、动画、UI、工具和引擎扩展。
Python 游戏脚本已移除；托管 World/固定步/事务基础已实现，C++ SceneWorld 和原生 World 导出已删除。
编辑器及隔离 Play 已接入 C# World；统一 Editor.Core/EditSession 编辑链路已实现；C# 主入口和独立原生插件化尚未完成。

## 1. 总体分层

```text
C# Editor / Player / Headless                         [headless 测试入口已实现，Editor/Player 未迁移]
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
| Application / Services | Editor、Player、Headless 入口和服务生命周期 | Editor/Player 未实现；已有托管 headless 测试入口，编辑器仍为 C++ EXE |
| World / GameObject | 扁平对象存储、UUID、运行时引用、创建/销毁 | C# World 已接管编辑器和 Play，C++ 只持有不透明会话令牌 |
| Component / TypeRegistry | 稳定 TypeId、类型池、字段/schema、通用查询 | 托管类型/schema 注册和组件存储已实现基础；类型池/通用查询未实现 |
| Behaviour Host | C# 生命周期、实例绑定、Export、程序集加载/重载 | 已接入托管 World；私有状态迁移、自动重建未实现 |
| WorldRunner / Systems | 固定步、呈现步、依赖和结构命令提交 | PlaySession 固定步/顺序 Systems 已接入编辑器；输入/插值/运行结构命令已实现基础 |
| SceneAsset / Serialization | 通用组件记录、资产引用、版本迁移 | C# `.ncmascene` JSON v1 通用组件读写/原子保存已实现；旧 .ncscene 不兼容；资产引用/迁移流水线未实现 |
| Editor Commands | 文档修改、事务、Undo/Redo、隔离 Play | Editor.Core 持有完整文档事务/唯一 Undo；ImGui 提交 UUID 意图，原生命令栈已删除 |
| Gameplay Services | 输入、角色、动作、战斗、任务等游戏 API | 未实现完整 SDK；当前只有基础门面/示例 |

当前 C# Behaviour 已支持挂载、禁用、删除、数值/布尔 Export 编辑及隔离 Play。
M1.3 已新增 Ncma.Gameplay.PlaySession：一个 WorldRunner 派发 OnFixedUpdate/顺序 Systems，帧后 OnUpdate 只读。默认 1/60 秒、最多追赶 8 步；严格与交互时间策略、Pause/Resume/Step/Faulted 已接入 ImGui。安全重载先隔离预检再清理/激活，成功 Paused，失败保留旧暂停或激活后 Faulted；重置私有状态，私有状态迁移未实现。
详细构建与作者流程见 [BUILDING.md](BUILDING.md)。

高频访问优先在托管组件池内完成；跨原生插件使用可复用批量缓冲区，不逐对象反射或 IPC。
当前过渡门面保留 Transform 批量读写、安全引用和信号邮箱，最多 4096 项。
这些接口现在直接访问 C# Runtime.World，不再通过 P/Invoke 访问原生 World。见 [WORLD_ACCESS.md](WORLD_ACCESS.md)。

## 4. 原生性能插件

目标为可按需装载的 Renderer、Physics 和必要数值内核。当前 NcmaNative 仍是聚合桥，
并无独立 Renderer/Physics 插件发布与加载体系。

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

目标：统一 RHI，D3D11 / Vulkan 显式选择，共享 API 无关资源与提交契约。

| 能力 | 当前状态 |
|---|---|
| RHI 资源、管线、RenderPass/Draw 契约与校验 | 已实现基础 |
| D3D11 设备、交换链、缓冲/纹理、深度、绘制与 resize | 已实现预览路径；不代表完整生产后端验收 |
| Metallic-Roughness / GGX、HDR、色调映射 | 已实现参考场景预览 |
| 方向光级联阴影、PCF 与接触硬化过滤 | 已实现预览；仍需真实动作场景质量/性能验证 |
| Vulkan loader/runtime 探测 | 已实现 |
| Vulkan 设备/交换链/绘制和双 API 参考场景一致性 | 未实现 |
| 完整材质/贴图资产、IBL、延迟/聚类渲染 | 未实现 |
| GPU 蒙皮角色、场景渲染提取和可合成视口纹理 | 未实现 |
| 独立 Renderer 插件和 C# 应用装载 | 未实现 |

当前 D3D11 使用 HLSL shader model 5 编译；共享着色器反射/SPIR-V 管线未实现。
构建和编辑器 smoke 通过不等于完成双 API 验证；不能宣称 Vulkan 已可渲染。

### 物理

Box2D/Jolt 已有独立原生世界、基础 Box 刚体、步进和位置/速度读写测试。
C# 场景组件同步、碰撞事件、CharacterMotor、Root Motion 碰撞解算和独立 Physics 插件未实现。
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
| Root Motion 提取、循环累计与通知区间派发 | 已实现原生预览；场景碰撞应用未实现 |
| Idle/Run/Attack/Dodge 实验室、调试与 Undo/Redo | 已实现独立程序化预览 |
| C# ActionAnimationSession / 动画 C ABI | 已实现独立实验会话 |
| FBX 骨架/蒙皮网格/动画导入与 CPU 线框预览 | 已实现，基于 ufbx |
| 动画图类型/引脚/连线及基础合法性检查 | 已实现数据模型 |
| 通用图编译/执行、完整可视化节点编辑器 | 未实现；节点枚举不代表对应求值器存在 |
| BlendSpace、Montage、IK、重定向、动画压缩 | 未实现完整功能 |
| 场景 SkinnedMesh/Animator/CharacterMotor 与 GPU 蒙皮 | 未实现 |
| 正式动作/连击/命中规则及 C# 通知到游戏事件链路 | 未实现 |

FBX 窗口与动作实验室是独立会话，尚未与场景角色或实时 MCP 贯通。
详见 [ANIMATION.md](ANIMATION.md)、[FBX_IMPORT.md](FBX_IMPORT.md)。

目标动作帧顺序（调度未实现）：
输入/已验证模块结果 → C# 游戏逻辑与动画状态 → 原生姿势/根运动 →
角色运动/物理 → C# 合并权威状态与事件 → 渲染数据提取。
根运动提出位移，CharacterMotor 经过碰撞后决定最终位置，不由多个系统同时写 Transform。

## 6. 编辑器与类 Figma UI

当前编辑器：C++ + GLFW + ImGui，包含对象列表、Inspector、视口、资产分类、Console、
Play 控制、动作实验室与 FBX 窗口，场景修改使用快照 Undo/Redo。
当前为固定工作区；自由 docking、多文档业务和托管编辑器未实现。

目标 C# Editor services：资产/场景文档、Selection、Inspector schema、命令/事务、
动画图文档和 UI 文档。原生仅承担平台/绘制能力，不承载高层业务规则。

UI 单独使用 UiDocument，不采用场景 GameObject 的父子树：

| 能力 | 当前状态 |
|---|---|
| Frame/Group/Rectangle/Text/Image、布局/样式与 Design Token 数据模型 | 已实现基础类型 |
| 类 Figma 画布、选择/拖拽/缩放/对齐、Auto Layout 求解 | 未实现 |
| UI 组件实例/覆盖、持久化、运行时布局/绘制/事件 | 未实现；声明类型不是完整实现 |
| UI 编辑 Undo/事务与 Agent 操作 | 未实现完整链路 |

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
out/bin/NcmaEngine.exe                 C++ / GLFW / ImGui
    ├── ManagedSceneClient            不透明 token / 复制 DTO，无原生场景存储
    │        └── hostfxr → Ncma.Managed.Host → Editor.Core → Scene → Runtime.World（唯一场景权威）
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
- 托管 Gameplay bridge v5 改用 uint64 场景令牌，Scene host 二进制交换 v6 使用有界 4 MiB 缓冲；不传 CLR 对象或 C++ 指针。
- 唯一场景文件为 `.ncmascene` JSON v1；不带语言选择或兼容槽，支持空对象、全部注册组件与 C# 脚本配置。
- 旧 `.ncscene` v1-v6 不再读取、转换或保存；旧读写和迁移代码已删除。
- 加载失败不改源文件或目标场景；不静默删除/转译。旧 Python 逻辑需作者改为 C# 或独立模块。
- 不序列化运行时句柄；编辑与 Agent 都使用可撤销命令。
- Build.bat 是 Windows 规范入口；原生不再需要 Python 开发 SDK 或运行时 DLL。
  Python 仅为可选工具验证依赖，使用 -SkipPython 可跳过；完整 C# 游戏发布仍未实现。
- 不宣称新应用入口、独立 Renderer/Physics 插件或 Python 通信已完成。

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
| Figma | UI 文档与编辑交互 |
| Infernux | 独立 Python 模块/工具分层 |

Godot、Piccolo、Hazel 不作为框架参考。参考不等于运行依赖、资产兼容或对应功能已实现。

## M1.1 implemented document boundary

Complete Ncma.Scene document snapshots v1 now cover all registered components and Behaviour/Export metadata. The retained C++/ImGui shell uses opaque snapshots for Undo and Play; C# .ncmascene JSON v1 files persist complete documents with atomic saves. Old .ncscene compatibility is removed. Shared commands, fixed-step/input/runtime commands and scoped live MCP are implemented; asset references/pipeline remain pending. See [M1.1 implementation](M1_1_SCENE_DOCUMENT.md).

## M1 新增边界（2026-10-04）

Ncma.Gameplay 负责输入消费、RenderFrameView、运行命令、生命周期和原子信号；结构安装不全量 Restore，不使存活对象引用整体失效。Editor.Core 编辑/Undo 仍是全量文档恢复。
Ncma.Editor.Protocol（纯 DTO）→ Editor.Transport（owner-thread 安全队列）→ 当前 Editor.Core。Editor.Mcp 仅依赖 Protocol，stdio helper 不加载场景或原生 DLL。IO 线程不执行 validator/脚本。
默认关闭/只读；用户批准精确请求后允许同事务修改/局部绑定/有范围历史，删除 UUID 另确认；Play/文档替换撤权。没有 Agent 任意代码或文件网关。版本：Scene 6、Gameplay 5、IPC 1、能力 2、native 2、场景 JSON 1、manifest 10。
见 [EDITOR_MCP](EDITOR_MCP.md) 与 [交付报告](M1_DELIVERY_REPORT.md)；人工客户端验收仍待完成。
