# 框架迁移：C# 主运行时 + 独立 Python 模块 + C++ 性能插件

2026-10-05 状态：C# apphost 已成为正式默认入口，编辑器业务、场景、Play 与唯一命令历史由 C# 拥有。
C++ SceneWorld、旧 Editor 外壳、hostfxr/ManagedSceneClient/Gameplay 桥与专属消费者已移除；
Dear ImGui/GLFW/渲染/物理/数值资源仍为原生插件。旧 .ncscene 兼容 codec 已删除。
用户调整门槛仅允许本次自动回归后的切换/精确清理，未完成 UI/MCP、自包含、性能/长稳验收；
剩余旧策略原型仍待审查。下文迁移顺序和旧桥描述为历史方案，当前状态以
[ARCHITECTURE.md](ARCHITECTURE.md) 和 [M2.8 交付记录](M2_8_DELIVERY_REPORT.md) 为准。

## 1. 新职责

C# 负责游戏逻辑及高层引擎：World/GameObject/Component、System 调度、场景/资产元数据、
序列化、生命周期、编辑器文档/命令/Undo 与应用服务。C# 不再只是原生 SceneWorld 的 wrapper。

Python 是独立 AI/推理/训练、分析、生成和自动化模块；不挂 Behaviour、不创建 Python GameObject。
默认建议 gRPC worker，可选 pythonnet/ZeroMQ，共用版本化模块契约。
服务结果只是提案，由 C# 校验并执行；不会阻塞每个游戏 tick。详见 [PYTHON_MODULES.md](PYTHON_MODULES.md)。

C++ 仅用于渲染、物理和有性能证据的动画/导入/计算插件。
高层状态机、动作/伤害规则、资产数据库、网络会话与编辑器业务不继续建在 C++。
现有原生算法能保留，并不意味着每个旧 C++ 模块都应永久保留。

## 2. 对象模型不变，权威所有者改变

| 概念 | 目标责任 | 所属 |
|---|---|---|
| SceneAsset | 可持久对象/组件记录、资产引用、Prefab 覆盖 | C# |
| World | 扁平对象身份、组件实例、启用与创建/销毁 | C# |
| GameObject | 空容器 + 组合组件，只挂 C# Behaviour | C# |
| Component / TypeRegistry | 稳定 TypeId/schema、字段和迁移规则 | C# |
| WorldRunner / Systems | 固定步/呈现步、依赖、结构命令提交 | C# |
| Renderer / Physics | 原生资源、批量绘制/求解输入输出 | C++ 插件 |
| Animation kernels | 必要时批量采样/混合/蒙皮/根运动数值求值 | C++ 插件 |
| Python modules | 观察数据到服务结果，不拥有 World | 独立模块 |

扁平对象列表、单对象删除、UUID、组合式行为保持；无场景 Node、空间父子关系或变换继承。
骨骼、动画图、UI 文档内部层级独立，不恢复 Godot/Actor 场景模型。
已支持无 Transform 的逻辑对象；编辑器新建对象显式附加 Transform，.ncmascene JSON v1 可保存/恢复空容器与全部注册组件。

类型池/批量查询优先，不因为采用 C# 就要求完整 archetype ECS。
持久数据使用 UUID 与稳定 schema，GPU/物理句柄、Python 引用、托管脚本实例和运行时代次不序列化。
World 与组件权威数据只保存在 C#，插件仅保留必要的子系统副本/资源；
禁止长期维护一份 C# World 加一份 C++ 通用 World 并让双方无序同步。

## 3. 迁移顺序与完成门槛（首切片已实现，其余待迁移）

1. **建立 C# Runtime 与无图形测试入口**：新增托管 World、GameObject、组件/TypeRegistry、
   代次引用、命令队列、快照和固定步 WorldRunner，不依赖 NcmaNative 世界存储或 Python。
   已实现 Ncma.Runtime headless 空容器/可选值组件、UUID/失效引用、managed JSON v1 快照和顺序固定步，测试不依赖原生 DLL/Python。
   已有 Behaviour 生命周期和唯一 .ncmascene 文档接入同一 C# World；结构命令已实现，类型池/通用查询未实现。
2. **场景/编辑器命令与 Play 数据迁移**：C# Editor.Core 已接管活动 ImGui 场景事务、完整快照 Undo/Redo 和 10 个 v2 能力；活动场景本地 MCP 已实现，人工客户端验收待完成。
   托管 SceneAsset/serializer/Undo 需进一步共享完整资产 schema；
   新 .ncmascene 文档已接入 C# World，完整保存/撤销/隔离 Play/UUID 已回归，旧格式不兼容；下一步将编辑器业务及旧命令栈迁到 C# 服务。
   当前 Python/未知语言记录拒绝加载，原文件保留；新托管格式另行设计迁移，不静默丢弃。
3. **主入口反转与最小 Renderer plugin**：C# 应用 owns main loop，按版本化 C ABI 加载 C++ RHI；
   保留 DX11 原生渲染，不改算法同时大幅换底层库。必须验证窗口/输入/线程/resize/资源销毁、
   参考场景与托管 headless 模式。Vulkan 当前仅 probe，仍需真实渲染与 API 验证。
4. **Physics / 动作角色接入**：C# 构造批量步进输入并合并求解结果；动作状态/通知处理在 C#，
   原生只评估骨骼与数值。打通 FBX、SkinnedMesh/Animator/CharacterMotor、Root Motion/碰撞。
   测量托管分配、GC、桥接批次、尾延迟及插件耗时再决定新增 C++ kernels。
5. **独立 Python 服务首条链路**：先实现一种传输（建议 gRPC）与可替换的模块接口，
   跑通 observation → worker → result → C# 校验 → tick 命令；测试超时、取消、异常、
   worker 重启/退出、乱序、过期/重复结果与队列溢出。pythonnet/ZeroMQ 后按具体需求增加。
6. **替换旧宿主与插件发布**：Python gameplay host/SDK 已删除；迁移完成后退出
   C++ hostfxr 主入口；C++ 通用 SceneWorld 已删除，消费者改为托管令牌，严格新格式与旧格式拒绝回归已覆盖。
   验证 C# Editor/Player 打包、原生 DLL 缺失/版本不匹配、释放/卸载与 Python 可选安装。

不同时完成全部层改写；每个切片在 Build.bat 保持基线回归，并增加对应托管测试。
直到新入口真实可运行，不能把 out/bin/NcmaEngine.exe 宣称为托管编辑器。

## 4. 原生插件契约

模块私有 API 头文件，版本与能力协商，固定 POD/资源句柄，批量调用与显式内存归属。
资源 handle 只代表插件资源，不成为 World/GameObject 的权威身份。
shutdown 前停止作业、释放 GPU/物理对象、清理回调，再卸载 DLL；
未验证安全卸载前不提供热卸载。回调禁止抛异常或携带未固定的托管地址。
依赖 Eigen/GLFW/ImGui/spdlog/Box2D/Jolt/ufbx 时，不把其类型泄露到托管 World。

旧 NcmaNative 是兼容聚合桥，不是独立 Renderer/Physics plugins 已完成的证据。
批量/引用/信号语义现在在 C# 内测试，原生 World ABI 不再存在；编辑器改由 C# PlaySession/WorldRunner 独占固定步，OnUpdate 只读；C++ 外部 Begin/Commit/Abort 已删除，不是独立 Python AI 调度。

## 5. Python 与网络边界

模块契约限定稳定操作名、版本、schema、request/session/World/revision/tick、deadline 与大小上限，
结果必须允许拒绝/过期，不得直接注入任意代码或调用组件方法。
pythonnet 的初始化/GIL/线程/释放由一个适配器独占，不允许多个适配器重复管理同一解释器；旧 Python 游戏宿主已删除。
gRPC/ZeroMQ callback 只入队，C# owner thread 在安全边界应用命令。
进程隔离不自动提供沙箱，限制目录/模型/资源访问需单独设计。

联机仍是独立服务，不耦合组件/Behaviour；默认用 C# 实现高层会话/协议，
只有可证实的性能热点才增加 native transport kernel。AI IPC 与联机协议分开。
Agent 修改继续走编辑器同一 Undo/事务，不开放任意 Python 执行。
新 headless EditSession 的能力与原生动画 MCP 是不同入口；深度 AI 开发边界见 [AI_DEVELOPMENT.md](AI_DEVELOPMENT.md)。

## 6. 旧资产与 API 兼容

唯一场景文件为 `.ncmascene` SceneDocument JSON v1；保存全部注册组件与脚本配置，同目录临时写入后原子替换。旧 `.ncscene` v1-v6 编解码/导入/导出/迁移/备份入口已删除，不再兼容。
原生 ABI v2 删除 World/GameObject 导出；Gameplay host v5 和 Scene host v6 使用不透明托管令牌；旧消费者必须重新编译。
新格式不含对象/绑定语言标签。未知字段、组件版本和不支持的格式明确拒绝；失败不改目标 World 或源文件。
Python 游戏宿主、SDK、示例、重载与编辑器语言入口均已删除，不存在停用绑定加载路径。
旧 Python 逻辑需由作者改写为 C# 或独立模块；显式转换前备份，不自动转译或覆盖。

## 7. 性能验收

避免逐对象 P/Invoke、逐帧反射、大规模临时对象和同步 AI 等待。
托管组件池/可复用缓冲区、批量插件调用、异步模块结果与明确运动权威优先。
GC/帧预算须用目标动作场景实测，不承诺仅凭语言选择就保证性能。
旧 Python Transform benchmark 已随游戏 SDK 删除；新运行时和模块通信需各自测量。

参考：ProwlEngine/Unity 用于 C# 组合与工作流，UE5 用于动作动画，Figma 用于 UI 编辑；
Infernux 仅作为 Python 模块工具分层参考。这些不是运行依赖或资产兼容承诺。

## M1.1 implemented document boundary

Complete Ncma.Scene document snapshots v1 now cover all registered components and Behaviour/Export metadata. The retained C++/ImGui shell uses opaque snapshots for Undo and Play; C# .ncmascene JSON v1 files persist all registered components with atomic saves. Old .ncscene compatibility is removed. Shared commands, fixed-step runtime and scoped live MCP are implemented; asset references/pipeline remain pending. See [M1.1 implementation](M1_1_SCENE_DOCUMENT.md).
