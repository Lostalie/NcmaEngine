# NcmaEngine 开发路线图

2026-10-05 入口/清理更新：用户确认调整验收门槛，自动回归后默认入口已切换为 C# apphost，
旧 C++ Editor 与专属宿主/场景/游戏桥及消费者已移除。保留 native ImGui/性能插件、分代部署恢复。
人工 UI/MCP、自包含、完整性能/一小时长稳和剩余旧策略审查仍待完成；M2/H8 不标记完成。
本更新覆盖下文旧“必须保留 C++ 默认入口”的阶段限制，证据见 [最新交付](M2_8_DELIVERY_REPORT.md)。

更新日期：2026-10-06（交换 M5/M6：先 UI/HUD，后可视化动画系统）。依据当前代码与已确认的架构决策整理；不以预览、接口声明或模型骨架代替完整功能。

编辑器视觉参考更新：采用[用户图1](EDITOR_INTERFACE_REFERENCE.md)的深蓝工作区、顶部模块工具栏、
左侧对象列表/中央视口/右侧Inspector/可折叠AI侧栏和底部资产/Console布局。取消先前的设计编辑器
产品参照；保留独立UI制作/运行时HUD目标。图中Node树、品牌、AI服务、多语言游戏脚本和性能数值
不成为架构或已实现能力。参考调整本身不关闭M4 K7；随后用户已明确授权实施M5.1至M5.6，
其范围和开放项见[M5基础方案](M5_1_6_IMPLEMENTATION_PLAN.md)。2026-10-07最新请求已授权实施M5.7，统一场景工作区初版已接入，UI制作及联合验收未完成；M5.8至M5.10不提前启动。

状态说明：“已实现基础”表示有代码与对应测试；“部分实现”表示尚未形成可用于游戏制作的完整流程；“未实现”表示仍是计划。下列阶段是推荐开发顺序，不是工期承诺。

## 1. 固定架构边界

本轮资产只读UI授权与长prepared路径修复见 [M3.8后续记录](M3_8_UI_AUTHORIZATION_REPORT.md)；异步导入/Prefab修改/人工闭环尚未完成，G7/G8不关闭。

- **C#：主引擎与唯一游戏逻辑语言。** 承担 World、GameObject、组件、调度、场景/资产文档、编辑器业务、动画行为、UI 逻辑和独立服务。
- **C++：性能关键原生插件。** 承担渲染、物理及经测量确有必要的动画采样、蒙皮、导入和数学内核；不再新增通用原生 World 或游戏框架。
- **Python：可选 AI、工具与特殊模块。** 不挂载游戏脚本，不拥有场景，不进入每帧同步等待路径。pythonnet、gRPC、ZeroMQ 均未实现，后续按需求选择，不同时铺开。
- **场景：扁平 GameObject 列表，空容器 + 组件。** Transform 可选；不恢复 Godot Node、父子对象树或 Transform 继承。骨骼、动画图、UI 文档内部层级不等于场景层级。
- **网络：独立可选服务。** 不向 GameObject、Behaviour 或组件植入 RPC、自动复制和网络所有权。
- **编辑器与 AI：共用 C# 命令路径。** 修改必须可撤销；AI 默认只读，事务需权限、会话和版本检查。不得另建一套绕过编辑器的修改接口。
- **AI高度集成：各功能阶段同步交付语义工具。** 上下文读取、结构化诊断、提案/dry-run、精确审批、共享事务/Undo与测试证据形成闭环，不把AI仅作为聊天面板或等M8再加接口。模型连接器、推理和Python模块仍未实现，配置与数据外发须独立授权。
- **跨语言：版本化 C ABI。** 明确线程、缓冲区、资源所有权与释放；不跨边界暴露 STL、C++ 异常或托管对象引用。资产保存 UUID，不保存运行时句柄。
- **编辑器 UI：保留原生 Dear ImGui 插件。** “imgui”就是同一库；C# 通过已有版本化 GUI C ABI 提交复制的呈现数据，不引入 ImGui.NET。游戏运行时 UI 使用独立文档、布局和渲染系统。
- **已有 SDK 继续复用：** Eigen、GLFW、Dear ImGui、spdlog、Box2D、Jolt Physics、ufbx 位于 `engine/sdk/`。C# 高层模型不得依赖其平台/GPU 头文件。

详细边界见 [引擎架构](ARCHITECTURE.md)、[框架迁移](FRAMEWORK_REFACTOR.md)、[AI 开发接口](AI_DEVELOPMENT.md)。

## 2. 当前功能基线

| 模块 | 当前状态 | 尚未完成的关键部分 |
| --- | --- | --- |
| 构建与验证 | 已实现基础：`Build.bat`、CMake/C++20、.NET、CTest、托管/原生 smoke、Python 工具测试；主程序输出 `out/bin/NcmaEngine.exe` | 完整 C# Editor/Player 发布、安装与持续集成矩阵 |
| World / GameObject / 组件 | 已实现基础：`Ncma.Runtime.World` 是唯一场景权威；扁平对象、可选值组件、UUID、引用校验、快照；C++ SceneWorld 与原生 World 导出已移除 | 通用查询/类型池、完整组件编辑与资产引用；性能优化尚未完成 |
| C# 游戏脚本 | 部分实现：生命周期/Export/隔离 Play、M1.3 PlaySession 固定步/输入/插值/运行命令/信号原子性/重载预检、只读 OnUpdate、暂停/单步/故障 | 动作角色/完整属性类型、低分配优化；私有状态迁移未实现 |
| 场景文档与 Undo | M1.1/M1.2 完整 SceneDocument/唯一历史/事务/Play 隔离；M3 typed UUID 引用与文件事务；唯一 .ncmascene JSON v1 | 完整资产面板/Prefab/cook |
| 编辑器与命令服务 | C# 主入口/业务与原生 ImGui 呈现插件；Editor.Core 事务/权限/版本/幂等/草稿；原生旧入口/桥已移除 | 完整资产/材质/动画 Inspector 与第三方客户端人工验收 |
| 资产系统 | M3.1/M3.2 已通过 G1/G2：UUID/严格元数据、索引、唯一 history、文件事务、异步持久导入、确定重导入/tombstone、typed NCA、generation journal/Play pin/精确 GC；M3.6候选浏览/本地审批/导入/放置与类型选择；M3.7严格 Prefab 格式/只读提取与放置预检候选 | 缩略图/独立材质浏览、完整资产 MCP、Prefab 保存/实例发布/覆盖/恢复、cook、G6/G7验收 |
| D3D11 / PBR / 软阴影 | 部分实现：reference GGX/HDR/CSM/PCF/PCSS/contact；G3 资源/纹理/材质/typed Graph，G4 正式静态 Scene/Editor/Player 主画面/单方向光 shadow/HDR，G5 DX11 GPU 动画蒙皮共享主画面/阴影；M3.6候选GUI离屏合成 | 通用多阶段资源图、IBL/透明/多光、场景 CSM/contact、生产级联合验收、G6人工 |
| Vulkan | **未实现渲染**：仅加载器探测 | Device/Queue/Swapchain、资源与管线、Shader、Draw、双 API 一致性及验证层测试 |
| 物理 | M2.6 独立Box2D/Jolt服务；M4.1运动权威；M4.2真实Jolt角色数值；M4.3 Editor/Player固定步角色/盒体接线；M4.4碰撞约束根运动；M4.5候选动作/closest-ray战斗 | 连续武器hitbox/通用Gameplay碰撞事件、人工/性能/长稳联合验收；默认物理仍disabled |
| FBX 角色 | G2 持久导入/异步 Worker/UUID/generation；G5 NCA 保存重启 → 场景骨架片段 → GPU/Player，不在 Player 解析 FBX | 完整源材质/纹理、真实用户模型/所有 DCC 骨骼缩放/skin mode 覆盖 |
| 动画 | 独立动作实验室/数值ABI2；M3 NCA/committed时钟/pose/GPU蒙皮，M4碰撞约束根运动与动作候选；M6.1严格托管图数据/验证 | 图执行与节点编辑、图MCP、BlendSpace/Montage、IK/重定向未实现；M4真实素材/性能/人工门禁开放 |
| UI | M5.1–M5.7托管文档/本机事务/布局/控件、DX11批次/独立文字内核、场景/UI制作工作区/缓存预览自动候选；旧C++ UI模型已删除 | UI Agent读写、组件实例/变体、glyph atlas/完整IME、正式Player HUD与发布仍未实现；人工/性能验收开放 |
| AI / MCP / Python | 项目只读CLI、隔离程序化动画MCP、活动场景共享事务MCP；M3获批资产只读、M4获批角色/战斗只读候选 | 动画图/UI修改、跨系统工作流、源码扩展网关和内置推理未实现；M6按功能同期接工具，Python传输仍未实现 |
| 网络与引擎扩展 | 独立网络系统未实现；组件 Schema 注册已有基础 | 网络传输/会话/同步服务、完整托管插件生命周期、编辑器扩展、发布与沙箱边界未实现 |

补充说明：

- Character/独立 demo Animation ABI 均为 2；新 pose-only ABI 1.0 与 Renderer query 5 独立，不恢复旧 Animation ABI 1。旧 Gameplay/Scene host 桥已经删除，不是当前入口。
- World 是 C# 唯一权威；现役 `Ncma.Managed.SceneWorld` 是 SDK 对该 World 的纯托管外观，不是旧 C++ SceneWorld 或兼容别名。native 仅拿 opaque 数值/GPU 资源与 bounded POD，不保留 ManagedSceneClient 或 native World。
- 当前编辑器入口和业务在 C#，原生 ImGui/GLFW/GPU 为插件，旧 hostfxr 桥已删除。整个 M2 人工/性能验收尚未完成。
- 完整快照恢复会使运行时引用失效；后续命令、选择和调试接口必须按 UUID 重新解析。外部直接修改也不能与 EditSession 历史混用。

## 3. 分阶段开发计划

| 阶段 | 目标 | 依赖与交付 |
| --- | --- | --- |
| M1 — 自动功能交付，待人工验收 | 统一 C# 场景文档、编辑命令、固定步与场景 AI 接口 | 基于现有 World；先消除双命令栈/脚本元数据分离，形成可靠编辑底座 |
| M2 | C# Editor/Player 主入口及原生插件边界 | 依赖 M1；保留已通过测试的原生窗口/GPU 内核 |
| M3 | 资产系统 + FBX 场景角色 + DX11 GPU 蒙皮 | 依赖 M1/M2；从导入预览走到持久化、可渲染场景角色 |
| M4 | 动画驱动的可玩动作游戏纵向切片 | 依赖 M3；角色控制、物理、根运动与 C# 玩法闭环 |
| M5 | 参考图工作区、UI制作与运行时 HUD | 依赖 M1/M2；视觉/布局以用户图1为准，为动作样例提供可编辑 UI；保留M4 K7门禁 |
| M6 | 仿 UE 的可视化动画编辑系统 | 依赖 M3/M4；编辑资产与游戏运行使用同一图语义 |
| M7 | 本版本 DX11 渲染与画质完成度 | M6结束后启动；Vulkan/OpenGL仅保留后端扩展契约，下一版本实现 |
| M8 | 可选 Python AI 模块、托管扩展与深度开发工具 | M7结束后启动；依赖共享命令/资产契约，既有AI能力随M1/M3/M5/M6交付 |
| M9 | 导出、性能、稳定性与可发布版本 | M8结束后启动；汇总本版本DX11成果，Vulkan/OpenGL验收移到下一版本 |
| N — 可选支线 | 独立网络系统 | 不作为单机 MVP 前置；先确定玩法与协议/安全需求 |

2026-10-06 调整：原 M6 的 UI 文档、画布、运行时 UI/HUD 与对应 Agent 能力整体移至 M5；原 M5 的动画图、编译/求值、调试与对应 Agent 能力整体移至 M6。推荐顺序为 M4 → M5（UI/HUD）→ M6（动画图）；各自技术依赖、实现状态及验收项随内容保留，不因交换而标记完成，也不新增动画图对 UI 系统的强制依赖。当前 M4 进度与测试/提交规则不变。

### M1：统一 C# 编辑与运行时基础

**状态：M1.1–M1.4 的代码与自动流程已实现；G6 双配置验收见交付报告，第三方客户端及人工 UI 验收待完成，不提前宣布整个 M1 已结束。**

1. 建立完整的托管 SceneDocument/快照，包含对象、组件、Behaviour 绑定及 Export 值；使用唯一 `.ncmascene` JSON v1 格式及原子保存；旧 .ncscene 不兼容，不增加迁移/备用读写入口。
2. 将创建、删除、重命名、Transform、组件、脚本挂载/启停/属性编辑接入同一 C# 命令服务；编辑器和 Agent 共用 revision、Undo/Redo 与事务历史。
3. 按 UUID 恢复选择与引用；明确外部修改的历史失效规则、编辑/Play 隔离和运行态可编辑范围。
4. 接入 WorldRunner 和 Behaviour.OnFixedUpdate；明确输入采集、固定步、渲染插值及异常恢复，补充延迟结构修改队列。不得把组件写入回滚误称为私有脚本状态/外部 IO 回滚。
5. 提供面向活动编辑器的本地 MCP：场景检查、组件描述、事务、校验和历史操作。复用已有能力名称/Schema；默认只读，写入按会话、版本、范围授权，删除需精确对象批准。

M1.1 已将脚本配置收敛到 Ncma.Scene，完整快照、新文件读写/原子保存与原生 Undo/Play 接入已实现，见 [M1.1 交付说明](M1_1_SCENE_DOCUMENT.md)。M1.2 已迁入独立 Ncma.Editor.Core，覆盖完整文档并删除原生命令栈，见 [M1.2 交付记录](M1_2_IMPLEMENTATION_PLAN.md)。

验收：人工和 Agent 修改走同一路径；Undo/Redo 不丢脚本或属性；失败事务不改现场；陈旧会话/引用、重复请求、越权和异线程调用有确定结果；Stop 不污染编辑场景；固定步生命周期有测试。

阶段方案与实际交付记录：[M1 剩余总览](M1_REMAINING_IMPLEMENTATION_PLAN.md)、[M1.3 固定步与运行态](M1_3_IMPLEMENTATION_PLAN.md)、[M1.4 活动编辑器 MCP](M1_4_IMPLEMENTATION_PLAN.md)、[M1 最终验收](M1_ACCEPTANCE_PLAN.md)。M1.3/M1.4 各拆为 A–F；最终验收不是新增功能阶段。

下一个验收动作：按 [EDITOR_MCP.md](EDITOR_MCP.md) 完成真实客户端/ImGui 人工闭环并记录版本。自动结果见 [M1_DELIVERY_REPORT.md](M1_DELIVERY_REPORT.md)。本次不进入 M2、不扩大资产/动画/UI 的未实现能力。

### M2：C# 主入口与插件化边界

**状态：未实现完整迁移；复用现有 native 内核。**

- 建立 C# Editor/Player 应用入口、项目配置、主循环、输入/时间服务、错误报告与日志入口。
- 详细方案建议延续已确认的 C++/Dear ImGui，先验证 C# 驱动的原生 GUI 薄适配；C# 接管面板、Inspector、菜单、工具业务，原生只画控件并返回复制意图，补齐 DPI、字体与布局管理。ImGui.NET 不作为强制前置。
- 抽出 Renderer/Physics 插件契约，明确能力查询、资源生命周期、初始化失败、线程和关闭顺序。高层不得直接调用 D3D/Vulkan。
- 复用 D3D11、GLFW、Box2D/Jolt 实现；只保留必要原生引导与后台模块。不要把现有 NcmaNative 聚合 DLL 误称为完整插件体系。
- M2 期间保留受控旧入口用于对照，新入口通过验收后在 H7 切换正式路径；M2 全部结束后统一清除已替代旧 C++ 入口/桥接及关联构建部署项，再完整复测，不分阶段提前删除。
- 维持最终程序名 `NcmaEngine.exe` 和 `out/bin/` 输出约定。

验收：C# 应用可独立管理编辑与运行会话；native 资源无悬挂/重复释放；旧编辑/脚本流程无回归；程序集与 ABI 不匹配能够明确失败。

M2.1/M2.2 已通过自动门禁；M2.3/M2.4 候选链路及联合自动回归见 [交付报告](M2_3_4_TEST_REPORT.md)。M2.5 的场景/Inspector/Play/MCP、大 JSON 分页、完整 reference PBR/阴影控件、FBX 线框/Orbit/报告、动画 ABI 2 与 C#/隔离 Python 策略、偏好/文件选择/快捷键/原生 Console 已迁移；H5 人工门禁未完成，见 [当前记录](M2_5_H5_DELIVERY_REPORT.md)。人工验收待完成，生产入口未切换；M2.6 薄 C++ Physics 插件 + C# 高层物理服务见 [交付记录](M2_6_DELIVERY_REPORT.md)，尚无场景物理；M2.7 已交付共享运行服务、Player/Headless 与 framework-dependent 候选包，见 [记录](M2_7_DELIVERY_REPORT.md)；自包含/H7 正式切换待验，M2.8 已实施冻结参考、内核独立GPU图像/三方对照、直接Export与活动reload、历史运行时三轮测量及只读包/消费者预检，见 [记录](M2_8_DELIVERY_REPORT.md)；H8 未通过，旧入口未清理。

详细方案（2026-10-04，按交付状态区分已实施与后续建议）：[M2 总览](M2_IMPLEMENTATION_PLAN.md)。

| 小阶段 | 方案与主要交付 |
| --- | --- |
| M2.1 | [应用服务与宿主去静态化](M2_1_IMPLEMENTATION_PLAN.md)：项目/脚本/catalog、应用生命周期与假平台循环 |
| M2.2 | [插件 ABI 与加载器](M2_2_IMPLEMENTATION_PLAN.md)：版本、内存、线程、租约、装载/关闭故障矩阵 |
| M2.3 | [平台输入与 GUI 适配](M2_3_IMPLEMENTATION_PLAN.md)：GLFW、输入/capture、DPI/字体、候选托管窗口 |
| M2.4 | [Renderer 插件](M2_4_IMPLEMENTATION_PLAN.md)：DX11 参考预览/GUI合成、资源释放与实际 API 验证 |
| M2.5 | [托管编辑器业务](M2_5_IMPLEMENTATION_PLAN.md)：现有面板、草稿/历史/Play/MCP及独立预览迁移 |
| M2.6 | [Physics 插件](M2_6_IMPLEMENTATION_PLAN.md)：薄 Box2D/Jolt 插件、C# 生命周期/单步/批次/诊断服务，不提前接入场景模拟 |
| M2.7 | [Player 与发布](M2_7_IMPLEMENTATION_PLAN.md)：headless、包布局、Rider/Build/启动器与默认入口切换 |
| M2.8 | [最终验收与清理预检](M2_8_IMPLEMENTATION_PLAN.md)：联合矩阵/人工/性能证据，核定清单；M2 结束后统一清理并复测，再移交 M3 |

可以先完成规划；正式实施前补齐 M1 人工验收，并确认 TFM（当前 net8.0，建议独立评估 .NET 10 LTS）。
H7 前候选程序不覆盖 out/bin 默认入口；H8 前不把 M2 标为完成。计划不新增已实现能力或自动授权工具。

### M3：资产系统与 FBX 角色落地

**状态：导入与 CPU 预览已有基础，资产/场景/GPU 闭环未实现。**

- 建立 C# 资产数据库、UUID 元数据、导入配置、依赖记录与缓存；明确源文件和派生资产，缓存进入忽略目录。
- 持久化模型及子资产身份，重新导入时保持可匹配的引用；拓扑变化、丢失资源或不支持内容必须诊断，不能静默覆盖。
- 添加场景资产引用、相机、灯光、静态/蒙皮网格、材质及最小 Prefab 实例/覆盖方案，仍使用扁平对象与组件。
- 完成 FBX 异步导入、取消/进度、原子提交；支持可验证的材质/纹理转换与限制报告。
- 将 FBX 角色绑定到场景，在 DX11 中实现 GPU 蒙皮和材质渲染；离屏视口可与编辑器/调试绘制组合。
- 提供资产检查、导入预检和授权导入事务能力；文件写入有项目相对范围及备份/失败保护。
- 使用用户真实 FBX 做覆盖测试，不以两个测试模型推断所有 FBX 均兼容。

验收：导入 → 保存 → 重启 → 场景实例 → 动画播放完整打通；重新导入身份稳定；CPU/GPU 蒙皮误差有证据；材质缺失有诊断；失败/取消不破坏原资产和场景。

2026-10-06：M3.1–M3.3 完整双配置通过，G1/G2/G3 关闭；M3.4 静态 Scene/Editor/Player 与阴影/HDR、G4 关闭，见 [GPU 交付记录](M3_4_GPU_DELIVERY_REPORT.md)。M3.5 正式 NCA 场景角色、最小 ClipPlayback/committed 时钟、DX11 compute 蒙皮与共享阴影完整双配置通过，G5 关闭，见 [交付记录](M3_5_GPU_DELIVERY_REPORT.md) 与 [契约](M3_5_RENDER_ANIMATION_ABI.md)。M3.6 候选增加本地 FBX 导入/放置/同一历史、GUI1.3离屏视口、保守选取/独立浏览相机和Edit scrub；缩略图/独立材质浏览/人工窗口项待补、G6开放，见 [记录](M3_6_DELIVERY_REPORT.md)。M3.7 已按用户要求推进严格格式、只读提取/放置预检候选，实例发布/覆盖/恢复未实现、G7开放，见 [候选记录](M3_7_FOUNDATION_REPORT.md)；M3.8 已启动只读资产 MCP 候选，G8开放，见 [记录](M3_8_READONLY_REPORT.md)；M3.9 已按用户要求启动已提交 generation 的 source-free 运行包/Player 候选，cold cook/正式导出与联合人工验收未实现、G9开放，见 [运行包记录](M3_9_RUNTIME_PACKAGE_REPORT.md) 和 [联合状态](M3_DELIVERY_REPORT.md)。不是整个 M3、Animator 或完整后端完成，M2 人工/自包含/长稳门禁不因此关闭。

| 小阶段 | 详细方案 |
| --- | --- |
| M3.1 | [资产身份与数据库](M3_1_IMPLEMENTATION_PLAN.md) |
| M3.2 | [异步 FBX 导入与派生数据](M3_2_IMPLEMENTATION_PLAN.md) |
| M3.3 | [网格纹理材质与渲染资源](M3_3_IMPLEMENTATION_PLAN.md) |
| M3.4 | [场景组件与 DX11 场景渲染](M3_4_IMPLEMENTATION_PLAN.md) |
| M3.5 | [GPU 蒙皮与 FBX 片段播放](M3_5_IMPLEMENTATION_PLAN.md) |
| M3.6 | [资产浏览与离屏编辑视口](M3_6_IMPLEMENTATION_PLAN.md) |
| M3.7 | [扁平 Prefab 与实例覆盖](M3_7_IMPLEMENTATION_PLAN.md) |
| M3.8 | [资产与 Prefab 的 MCP 能力](M3_8_IMPLEMENTATION_PLAN.md) |
| M3.9 | [Player 资产包与联合验收](M3_9_IMPLEMENTATION_PLAN.md) |

保留 C# 业务、原生数值/GPU 插件、Python 工具/AI边界；不恢复旧宿主/Scene桥。
M2 未完成人工/性能项继续记录；执行授权不自动关闭前置人工验收。

### M4：动作游戏纵向切片

状态：动作纵向切片候选和程序化样例已实现；K7真实素材/可见可玩/性能/环境/长稳验收未完成。

2026-10-06 起按 [M4 小阶段方案](M4_IMPLEMENTATION_PLAN.md) 顺序执行，各小阶段完整测试通过后
单独提交推送。M4.1 新增 C# `Ncma.Movement` / host-only Runtime 组件写权威与 Play 耦合固定步：
一次数值执行、唯一 Transform 发布、输入/信号事务、跨域 fail-stop、关闭失败保留资源、
Reload 从冻结 startup 文档重建（tick 不倒退）。K1专项使用deterministic fake，不能代表Jolt验收。
M4.2新增显式Physics1.2/Character1.0：真实Jolt capsule、grounding/contacts、closest body/character
ray/sweep及C#数值缓冲客户端，见[交付](M4_2_DELIVERY_REPORT.md)。M4.3实现C#固定步角色/盒体绑定、
Editor/Player/Headless实际solver接线、跟随相机与只读插值，见[交付](M4_3_DELIVERY_REPORT.md)。
M4.4候选接入XZ/Yaw根运动、成功提交才消费的共享时钟、真实碰撞约束与GPU视觉根去重，
见[交付](M4_4_DELIVERY_REPORT.md)。M4.5实现动作状态、连击/中断、tick Notify、closest-ray命中/无敌与同量子Health发布，见[交付](M4_5_DELIVERY_REPORT.md)。M4.6实现复制诊断、可信UI审批的只读角色/战斗MCP和可启动程序化样例，见[交付](M4_6_DELIVERY_REPORT.md)。M4.7候选联合0/1/8/32角色测量和32轮共享服务故障/恢复/GPU基线测试见[交付](M4_7_DELIVERY_REPORT.md)；真实素材、GPU归因/预算、窗口/第三方客户端/目标环境/1小时门禁仍开放，M4未关闭。
M4.1 的最终退出门和测试证据见 [交付记录](M4_1_DELIVERY_REPORT.md)。

- 用 C# 建立动作状态/参数、输入缓冲、角色控制、相机和游戏生命周期。
- 以 Jolt 角色碰撞为 3D 动作主线，接入 C# 物理同步与碰撞事件；Box2D 保持独立 2D 路径。
- Animator 输出根运动增量，由 C# 角色运动策略经物理约束应用到对象；不让动画、物理和脚本同时写入最终位置。
- 将 Idle / Run / Attack / Dodge、动作中断、连击、命中/无敌窗口从演示标记变为实际玩法；Notify 事件有稳定时间语义。
- 增加参数、姿态、根运动、碰撞、事件时序的调试视图与只读 AI 检查能力。

验收：真实 FBX 角色在测试场景完成移动、转向、攻击、闪避和障碍碰撞；不同渲染帧率下固定步结果可验证；Play/Stop、重载和事件边界稳定。单机样例不依赖 Python 或网络启动。

### M5：参考图工作区、UI制作与运行时 UI/HUD

状态：M5.1至M5.6已有C#文档/事务/布局/控件、DX11驻留UI批次和独立字体数值插件自动候选；M5.7另行接入参考图场景/UI制作工作区、query7缓存目标、文件/资源审批、画布与共享Undo。旧C++ UI骨架已删除；glyph atlas/完整游戏文本编辑/IME、组件实例、UI Agent写、正式项目HUD与发布仍未完成。见[底座交付](M5_1_6_DELIVERY_REPORT.md)和[M5.7交付](M5_7_DELIVERY_REPORT.md)，不把自动候选等同M5完整验收。

- 整体编辑器采用[用户图1](EDITOR_INTERFACE_REFERENCE.md)的视觉和工作区布局：顶部工具栏、扁平对象列表、主视口、Inspector、可折叠AI/工具侧栏及底部资产/Console。不复制图中的Node树、产品品牌或未实现功能。

- 在 C# 建立带 UUID 的 UiDocument、样式/Token、版本化存储和命令模型；UI 内部树不改变扁平场景设计。
- 实现画布缩放/平移、多选、拖拽、对齐/吸附、图层、Inspector 与撤销。
- 实现 Frame、文本、图像、裁剪、约束和横/纵自动布局；随后加入组件/实例、覆盖和变体。
- 建立独立运行时布局、绘制、字体/文本、输入/焦点、分辨率适配基础；Dear ImGui 承担编辑工具，不直接充当所有游戏 UI。
- 设计文档编译/加载为运行时 UI 数据，C++ 只处理必要 GPU 绘制或性能内核。
- 提供文档检查、布局验证、样式/组件事务和预览能力，让 Agent 操作语义文档而非模拟鼠标。
- 为动作样例制作生命值/动作提示 HUD，后续完善本地化和无障碍支持。

M5.7目标同步为[参考图编辑器工作区与UI制作工作区](M5_7_IMPLEMENTATION_PLAN.md)：按用户图1
调整整体主题、顶部工具栏、扁平对象列表/真实视口/Inspector/可折叠AI区域及底部资产/Console，
UI制作作为同一Editor的独立工作区，保留选择/拖拽/缩放/对齐等功能及共享命令/运行时预览。
当前实施中、未完成、未验收，不恢复原产品参照或图中的Node场景树。

2026-10-07 M5.7继续实施：统一交互门控、分隔条/严格布局设置、场景/UI制作工作区、精确本机文档/字体/图像审批、作者命中/多选/变换/吸附/分组、属性/Unicode分页、独立控件测试与静态缓存已接入。GUI1.6保留旧表，Renderer query7为独立UI-target1.0，queries1–6和旧Image帧约束不变。自动联合流程与最终顺序Debug/Release分开记录，人工视觉/DPI/IME/MCP/目标机/性能/长稳仍开放。最新请求授权修复直到测试通过、然后提交推送，不自动关闭A–H全部验收或启动M5.8–M5.10。详见[交付](M5_7_DELIVERY_REPORT.md)和[使用说明](M5_7_EDITOR_GUIDE.md)。

验收：可编辑、保存和复用 HUD，在至少两种窗口比例下正确布局；输入命中/焦点稳定；组件修改和 Agent 操作可撤销。

### M6：可视化动画系统

状态：已有采样/混合内核，M6.1 开始建立正式 C# 图资产与验证；完整编辑器和图执行未实现。

2026-10-07 用户授权逐小阶段实施、修复至测试通过后提交推送并核对远端，再进入下一阶段。
详细范围和门禁见 [M6 实施方案](M6_IMPLEMENTATION_PLAN.md)：M6.1 严格图资产、M6.2 编译求值、
M6.3 场景接线、M6.4 节点工作区与获批图MCP事务、M6.5 过渡事件、M6.6 BlendSpace、M6.7 分层缓存、M6.8 Montage、
M6.9 跨系统AI工作流/MCP可靠性、M6.10 包与联合验收。M6.2开始同步结构化诊断和获批只读图MCP，
M6.5–M6.8同期扩充功能工具，不等M6.9首次接入。M6.2编译/提交式求值和两项获批只读图MCP已通过
完整Debug/Release，图/pose32、Editor72，见[交付](M6_2_DELIVERY_REPORT.md)；后续图写入/真实角色接线/
高级工具未实现，M6.1不因此获得新权限。
M6.3-A独立数值pose-blend1.0完整双配置通过（图/pose35、Editor72），原pose表冻结，使用同一context/rig；
[A交付](M6_3_A_DELIVERY_REPORT.md)仅证明数值扩展。[B交付](M6_3_B_DELIVERY_REPORT.md)完整双配置自动候选
通过，Animator固定真实NCA/图package、共享Editor/Player/Headless、提交式实例与数值/GPU场景接线；
C/D最新补齐图根与唯一Jolt、精确当前skin/shadow和独立获批runtime读取，完整双配置自动门禁通过；
见[C/D契约](M6_3_CD_RUNTIME_CONTRACT.md)和[交付记录](M6_3_CD_DELIVERY_REPORT.md)。真实用户素材和人工门禁仍开放。
2026-10-08按用户请求先实施独立M6.4：深蓝节点画布、类型化属性/条件、精确文件/资源审批、
共享Undo/Redo、活动MCP propose/transaction和独立真实NCA/GPU预览已接入自动候选。
见[M6.4方案](M6_4_IMPLEMENTATION_PLAN.md)与[交付](M6_4_DELIVERY_REPORT.md)。
M6.5-B固定预算、提交边界姿态缓存的可中断过渡已通过完整Debug/Release自动候选，保持唯一图时钟/运动权威和
只读精确runtime MCP。完整门禁见[M6.5-B交付](M6_5_B_DELIVERY_REPORT.md)；
持久事件/策略作者工具与获批独立序列MCP为C，严格v2完整双配置自动候选通过；D联合完整双配置也通过，
M6.5 A–D自动候选完成，见[M6.5-D交付](M6_5_D_DELIVERY_REPORT.md)。
M6.6 A–D完整Debug/Release自动候选通过：严格v3替代v2、准备期1D/2D拓扑、实际NCA共享相位、
主贡献事件/root与全混合pose stripping、中断缓存/搬移NCP1 Player、typed采样编辑与精确获批AI权重扫描。
D的0/1/8/32 Editor、16个搬移正式Player、调度/fault/reload联合测试通过，
见[M6.6方案](M6_6_IMPLEMENTATION_PLAN.md)与[D交付](M6_6_D_DELIVERY_REPORT.md)。
M6.7 A–C完整双配置自动候选通过：精确骨架遮罩、native layer1.0、严格v4/cache、typed作者及单独获批骨清单/
缓存诊断；人工/素材/目标/性能/1h仍开放，见[M6.7交付](M6_7_C_DELIVERY_REPORT.md)。当前推进M6.8。
M6.8 A/B1/B2a完整双配置自动候选通过：strict Montage数据、同Graph/Animator的instance/tick/attempt/token、
合作事务及实际NCA准备，见[B2a交付](M6_8_B2A_DELIVERY_REPORT.md)。B2b-1混合包络积分/partial fraction与
独立root数值准备完整双配置自动候选通过，actual NCA1/8/32和16Slot528区间，见[B2b-1交付](M6_8_B2B1_DELIVERY_REPORT.md)。
当前候选graph strictv5替代v4，B2b-2正式姿态/Movement-root/Notify/持久化/startup/NCP1/Player已接通，
实际NCA/Editor0-1-8-32/16搬移Player及完整顺序无Skip双配置通过（12native/22managed、core109/Editor105/Player56/Python43/checkeddeploy），见[B2b-2交付](M6_8_B2B2_DELIVERY_REPORT.md)。
C按C1作者/同源获批语义与C2 typed隔离控制用例/最终联合验收切片。C1修复后完整顺序无Skip双配置自动候选通过
（12native/22managed、core111/Editor109/Player56/Python43/101hashes/Complete journal），
见[C1契约](M6_8_C1_RUNTIME_CONTRACT.md)和[交付](M6_8_C1_DELIVERY_REPORT.md)，远端a80431e已核对。
C2 typed隔离请求/断言/输出、同源精确用例MCP及前景UI完整顺序无Skip双配置自动候选通过：
12native/22managed、core115/Editor113/Player56/Python43/checkeddeploy、101hashes/Complete journal，
见[C2契约](M6_8_C2_RUNTIME_CONTRACT.md)及[交付](M6_8_C2_DELIVERY_REPORT.md)。
M6.8 A/B/C自动候选闭环；人工/素材/目标/性能/1h仍开放，不标整个M6正式完成。
M6.8-C2远端 fcc4e8a 已核对。M6.9自动候选完成：有界C#计划账本、原工具独立审批的精确ticket、
取消/deadline/有限修复/回执、前台完整分页审批；不嵌套调用或自批准，不接推理。
方案及边界见[M6.9方案](M6_9_IMPLEMENTATION_PLAN.md)、[契约](M6_9_RUNTIME_CONTRACT.md)；
最终顺序无Skip Debug(second)/Release(first)12native/22managed、Editor123/Player56/Python43、
101哈希/journal Complete已核验；提交推送状态以Git回执为准，
见[M6.9交付](M6_9_DELIVERY_REPORT.md)。M6.9远端0978400已核对；按用户要求先完成M6.10再进入M7。
M6.10自动候选闭环：精确publication惰性编译复用，运行包剥离作者布局、typed语义/NCA闭包，
12非法包/24初始化前Player拒绝、完整120量子调度轨迹、128shared cycles/32Reload/32fail-stop，
0/1/8/32组合绘制/成本及真实搬移apphost通过。最终完整顺序无Skip Debug(second)/Release(first)
各12native/22managed、Editor123/Player56/Python43/checkeddeploy、101hashes/Complete journal通过，
详见[方案](M6_10_IMPLEMENTATION_PLAN.md)、[契约](M6_10_RUNTIME_CONTRACT.md)、[交付](M6_10_DELIVERY_REPORT.md)。
提交推送以Git回执为准，核对远端后进入M7；此前M7未启动。人工/用户素材/目标/性能/1h仍开放。
正式Montage Player自动路径通过，不据此标记整个M6或人工/目标验收完成。
并非内置推理接入或真实用户素材/人工/目标/性能/长稳验收通过；Agent不能控制live Play。
旧 C++ 图作者原型删除，不提供兼容层；M4/M5 及既有人工/目标环境/
性能/长稳门禁仍开放。M6.1 进度见 [契约](M6_1_GRAPH_CONTRACT.md) 与 [交付](M6_1_DELIVERY_REPORT.md)。
M6.1 完整顺序Debug/Release已通过，10项新增图专项随25项pose/clock候选测试通过；仅资产/验证基础，
尚无图执行、正式保存事务或节点编辑器，不称完整Animator。

- C# 管理图资产、状态/转换/参数/事件与调试；C++ 保留经测量需要的姿态、蒙皮和批量采样内核。
- 实现节点/引脚/连线、类型验证、搜索、参数编辑、保存与 Undo；先完成状态机、Clip、Blend 和过渡的最小图。
- 编译为受控运行程序，编辑预览与游戏共享语义；验证单步、过渡中断、循环和 Notify 边界。
- 逐步加入 BlendSpace、分层/遮罩、缓存姿态、Montage 式 Slot/Section 与事件轨道。
- 为图查询、节点/连线修改、参数/动作预览提供命令与 MCP；接入真实资产和活动编辑器，不局限于独立程序化实验室。
- IK、重定向、动画压缩、并行求值与 LOD 后续按角色需求和测量推进。参考 UE 的使用方式，不宣称 UE 资产兼容或性能领先。

验收：用户可视化编辑真实角色的移动/攻击图，保存后重启可用；预览与游戏行为一致；Agent 修改同样支持验证和 Undo，非法图无法进入运行时。

### M7：本版本 DX11 渲染与画质完成度

2026-10-15 **M7.7自动候选完成，接续M8**：完成GPU来源采样／正式成本消费者、2D复制与dirty准备优化、四档3D独立图像及完整搬移Editor/Player成本／恢复链。878份冻结源码完整顺序无Skip Debug/Release首轮各13原生＋72托管／产品、Python45、三轮运行时测量、审计／可恢复正式部署全部通过，零编译警告。最终Release Complete `5b7ffe1b0f0c4028ab3745428311dd7f`，144清单／148实际／148备份SHA与真实EXE核验通过；每配置720幅3D图像及1920正式宿主成本帧，2D六图双配置逐字节一致。GPU待完成／旧来源明确保留，宿主分配和整表dirty上传仍未优化完；人工／真实物理设备故障／用户素材／目标完整性能／自包含／1h及历史File.Replace调查不关闭。交付与证据见[M7.7实施记录](M7_IMPLEMENTATION_PLAN.md#m77-联合参考与成本验收自动候选完成)。下列逐次记录为历史过程，不覆盖本段当前状态。

2026-10-15 **M7.7-C UI文档复制／冷创建增量双配置通过**：保留严格输入校验和精确4MiB序列化预算，外部输入先复制可变数组，已验证文档Capture只复制数组、共享不可变记录。3,075元素文档Capture约30.47MB→24.7KB，实际无字体canvas创建约74MB→6.65／6.68MB；六图优化前／Debug／Release逐字节一致，脏准备仍40B。最终各34＋3组回归、Python45／烟测／五包SHA通过，44份源码哈希一致。Debug首轮原Editor.Core File.Replace间歇性失败已留证，原样三次独测及同源综合复测通过，根因未证明、未增加保存回退或豁免。下一步D正式搬移宿主成本／生命周期及整阶段无Skip；GPU仍整表上传，人工／完整性能／长稳等门禁开放。正式M7.6 EXE未替换，无提交推送，详见[M7.7实施记录](M7_IMPLEMENTATION_PLAN.md#m77-联合参考与成本验收自动候选完成)。

2026-10-15 **M7.7-C UI脏准备／文字输入优化增量双配置通过**：3,075元素单次dirty托管准备分配约3.69MB→40B，Debug／Release平均5.37／1.70ms；静态256次零分配／上传，六幅独立图像与优化前及双配置逐字节一致。保留几何失效与资源生命周期，修复清空文字残影和部分DPI准备失败后的错误缓存复用；真实文字输入31组和每配置26＋3组回归、Python45／烟测／五包SHA通过，41份源码哈希稳定。GPU仍整表959400B/次上传，冷创建约74MB，非完整性能验收；接下来审查剩余成本边界并完成D搬移宿主成本／生命周期及整个M7.7冻结无Skip。正式M7.6 EXE未替换，无提交推送。详见[M7.7实施记录](M7_IMPLEMENTATION_PLAN.md#m77-联合参考与成本验收自动候选完成)，下方“dirty约3.69MB、文本输入待补”为历史记录。

2026-10-15 **M7.7-C四档3D联合验证增量双配置通过**：真实静态及ASCII/Binary FBX、PBR/IBL、Off/Low/Medium/High阴影、Bloom/Tone/HUD与默认／stage／完整pipeline联合。每配置720幅图像最大误差1/API0/0，7680次暖态成本尝试，忙时跳过和旧GPU来源明确记录；240个区间当前线程0分配，不是完整宿主或目标硬件性能承诺。各26组回归与格式／Python45／烟测／五包SHA通过，36份源码哈希一致。接下来补文本／输入成本并继续解决dirty分配／全表上传，再做D搬移宿主成本及完整无Skip门禁；无部署／提交推送。详见[M7.7实施记录](M7_IMPLEMENTATION_PLAN.md#m77-联合参考与成本验收自动候选完成)。

2026-10-15 **M7.7-C首个2D图像／成本增量双配置通过**：3075元素独立全图oracle发现并修复零圆角边缘变暗；默认Flat2D哈希有意更新，3D不变。UiCanvas去除每元素线性查找与冗余layout复制；同机Debug dirty准备约105.64→6.75ms，Release约2.74ms，静态256次0分配/上传/提交。仍有约3.69MB dirty分配及全表上传，不宣称完整性能达标。各22组回归、3组格式、Python45、烟测/五包SHA通过，六幅优化前后及双配置图像逐字节一致。完整3D联合、text/input预算、D冻结全门禁仍待完成；无提交推送或替换正式EXE。详见[M7.7实施记录](M7_IMPLEMENTATION_PLAN.md#m77-联合参考与成本验收自动候选完成)。

2026-10-15 **M7.7-B正式成本消费者增量双配置通过**：Editor/Player/独立2D显式 `--presentation-costs`，默认无采样开销；呈现CPU/线程分配、所选批次/上传差值、带来源的GPU区间分开，旧帧/新设备/异常不会伪装本帧成本。30项新检查、各16组原回归和3组格式测试、真实三类apphost、18文件UI包新增烟测、Python45及五包SHA通过，26份相关源码哈希一致。下一步C完整3D/2D图像与成本联合、D搬移/生命周期与整阶段冻结门禁；未提交推送或替换M7.6 EXE，不宣称完整性能达标。详见[M7.7实施记录](M7_IMPLEMENTATION_PLAN.md#m77-联合参考与成本验收自动候选完成)。

2026-10-15 **M7.7-B工作来源采样增量已双配置核验，整个B仍未完成**：query20/v1分别记录Skin批次和离屏UI目标/内容版本，计时查询与palette安全完成fence分离；失败计时不阻止健康数值计算，缓存展示不伪装新生产。新增原生90/C#18项，最终各12组图形回归和3组互操作/格式测试通过，Python45/烟测/五产品SHA通过；首次Debug历史File.Replace失败保留，复测通过不代表根因已解决。正式宿主成本消费者、C/D联合oracle和整阶段无Skip仍未完成，无提交推送或替换M7.6 EXE。详见[M7.7实施记录](M7_IMPLEMENTATION_PLAN.md#m77-联合参考与成本验收自动候选完成)。

2026-10-15 **M7.7进行中，A主提交GPU采样基础增量已双配置定向核验**：新增只读query19/v1，明确设备、来源帧、观察帧、序列、缺样与有效状态，非阻塞有界轮询。修复真实查询失败复用警告，故障槽不再复用直到新设备。原生61/C#15项及各11组原回归通过，原Rendering183.02/179.03秒，Python45/烟测/五产品SHA通过。范围不是整个GPU帧；Skin/离屏UI、正式成本报告、独立联合oracle和整阶段无Skip门禁仍待B/C/D，尚未提交推送或替换M7.6正式EXE。详见[M7.7实施记录](M7_IMPLEMENTATION_PLAN.md#m77-联合参考与成本验收自动候选完成)。

2026-10-15 **M7.6自动候选完成**：资源准备/所有权、失败退役保留、正式Editor/Player显式图形恢复、原始GPU诊断、跨DLL身份和四档完整搬移联合通过；独立2D三代恢复保持无3D依赖。861份冻结源码无Skip Debug/Release第一轮各13原生/64托管图形/Python45全通过，0编译警告，三轮历史观测/审计/恢复与五产品SHA通过；正式Release受检部署Complete、备份和实际EXE验证通过。详见[M7.6最终记录](M7_IMPLEMENTATION_PLAN.md#m76-资源更新与故障恢复自动候选完成)。提交推送后下一阶段M7.7补齐GPU来源帧与联合成本验收；人工、物理掉线、用户素材、目标/自包含、完整性能/1h及历史异常调查继续开放，M7整体未结束。下方增量“正式M7.5未替换”等为历史记录。

2026-10-15 M7.6-D 窗口/重复生命周期/独立2D恢复增量已核验：完整搬移FBX/图/root/Jolt/High/IBL/Bloom/交互HUD通过窗口暂停、尺寸恢复、第二次设备重建，Editor三轮Play/Reload/Stop排空求解器并恢复Edit历史。独立UI实际搬移apphost以原18文件闭包通过三代恢复40项，无3D/物理/字体初始化。最终Skip Debug/Release各10组定向测试通过，Python45、五产品SHA审计通过；无游戏逻辑/GPU ABI/AI授权变更。下一步四档阴影完整故障链覆盖及整阶段冻结无Skip门禁；未将受控事件/故障当作人工/真实硬件验收。正式M7.5未替换、未提交推送，详见[M7当前记录](M7_IMPLEMENTATION_PLAN.md)。下方较早“窗口/纯2D待补”已由本增量推进，整阶段仍未交付。

2026-10-15 M7.6-D 搬移联合恢复增量双配置核验通过：真实 ASCII/Binary FBX 与显式合成 root-motion 动画图伴随角色、Jolt、High 阴影/IBL/Bloom/交互 HUD 同时恢复，默认/阶段替换/整管线三路径保留 C# 私有状态、动画/物理步序列和暂停最终画面。补齐显式 Null/DX11 物理 Player 受检产品，默认轻量 Player 不增加求解器。最终规范Skip两配置各15组CTest、五产品 SHA/闭包审计与 Python45通过；原 Rendering193.90/193.78秒、240秒门限不变。859份选定源码摘要前后一致，所有工作会话终结。输入与故障均为受控验证，不代替人工/真实硬件验收；短时16帧仍约1.2MB线程分配，不宣称零分配。联合窗口生命周期/档位/纯2D覆盖审计、整阶段冻结无Skip及既有人工/目标/完整性能门禁仍开放。正式M7.5未替换，未提交推送，详见[M7当前记录](M7_IMPLEMENTATION_PLAN.md)。以下 C 记录是前序增量。

2026-10-15 最新M7.6-C原始GPU诊断增量已核验（非整阶段交付）：只读query18/v1保存首个原始HRESULT、实际设备原因、精确设备代和CPU观察帧；启动、重建、关闭与DLL卸载后由C#保留值副本，不覆盖原宿主异常、不泄露私有文本、不增加Agent权限。超时/显式丢弃帧不伪造HRESULT，受控注入不冒称真实硬件故障。最终规范Skip Debug `5ee5ed0dbe5840ea9a7fc2982ddfdad1` / Release `31c089011b93450ca39ad28c8957e4e4` 各33定向+7原回归+3 ABI/无窗口/严格格式通过，Python44通过；0编译警告，两配置144/113/56文件SHA/大小核验通过。831选定源码前后摘要一致，全部工作会话结束。正式M7.5未替换，未部署/提交推送。下一步D完整搬移产品故障/恢复联合及整阶段冻结无Skip；人工/真实硬件/目标/自包含/完整性能/1h门禁和历史失败仍开放。下方原始GPU诊断“待做”条目是此前历史，详见[M7实施记录](M7_IMPLEMENTATION_PLAN.md)。

2026-10-15 最新M7.6-C增量已核验（非整阶段交付）：真实卸载/重载 Platform、Renderer、GUI、Text 的独立测试复现旧 raw 句柄重复，现改用完整64位运行期 LUID；新函数表拒绝旧 context、window/device/group、GUI/font/text/UI列表/目标/租约，以及绑定新device代的旧UI数值。分配失败保持资源/输出/引用，ABI布局不变。最终规范Skip Debug `7b4f4e9e09ea4fbb96273d2c52e99077` / Release `b4077b865b66418bb2ff4b683fa09f53` 各32定向+7原回归+3ABI/无窗口/严格格式通过，Python44通过；0编译警告，两配置144/113/56文件SHA/大小通过。全部会话结束，正式M7.5不变、未部署/提交推送。此证据不覆盖Physics/独立动画/导入数值插件，不等同实际硬件丢失；逐帧UI租约新增系统ID分配成本仍待完整性能测量。后续原始GPU诊断、D完整搬移故障联合与整阶段无Skip/人工/目标/完整性能/长稳门禁保持开放，详见[M7实施记录](M7_IMPLEMENTATION_PLAN.md)。

2026-10-15 上一M7.6-C增量：补齐旧Scene部分关闭后的Edit/Play重试，并接入独立Player同会话GPU恢复；保留World/脚本/动画/HUD，需精确单次批准，默认不自动恢复。最终规范Skip Debug `e225f6613282431cbad6b4e04702e135` / Release `4ca18a216cbf4857a0c9e6dbf9c26bcd` 各30组定向+7组原回归通过，构建0编译警告、两配置144/113/56文件SHA/大小通过；新增Player22、扩展Editor33/HUD97包含在回归中。全部工作会话已结束，正式M7.5不变，未部署/提交推送。当时下一步原始图形诊断、跨实际DLL卸载/重载的raw句柄失效保护及D完整搬移恢复联合；整阶段无Skip与全部原人工/真实硬件/目标/性能/长稳门禁仍开放。下面Play/Undo增量中“独立Player未接入、关闭重试待做”是历史状态；当前以最新增量及[M7实施记录](M7_IMPLEMENTATION_PLAN.md)为准。

2026-10-15 Play/Undo恢复增量已核验，M7.6-C仍进行中：正式Editor支持精确Running/Paused review和控制冻结，只重建GPU，保留原Behaviour实例/私有进度、模拟债务、World/历史、动画CPU clock和HUD runtime；Faulted不恢复。修复Undo后动画借用释放及首次恢复前旧World失效路径。最终规范Skip Debug `6d4a70b42d4a410faa6a3070d5dd5534` / Release `8b597b3f2821489aa866a50d927cef21` 0编译警告，各29组定向+7组原回归全部通过（209.73/341.17秒、201.90/330.90秒），包含CPU22、正式循环29、实际状态55及HUD89；双配置144/113/56文件SHA/大小一致。全部是受控故障/脚本确认，不是物理device-loss/人工验收；旧Scene部分关闭后重试组合、独立Player同会话恢复、动画图/root-motion/求解器/IBL/后处理/HUD完整搬移故障链、原始错误诊断、跨DLL raw代和最终整阶段无Skip门禁仍待完成。正式M7.5未替换，未提交推送；下文旧增量的“尚无Play恢复”不代表本增量状态。详见[M7当前验证记录](M7_IMPLEMENTATION_PLAN.md)。

本轮最终核验：正式Edit恢复循环候选 Debug `3ce1819a060b4e688155e48ce88969a6` / Release `df5794b64fc54243a377e24b05722d9f` 0编译警告；各27组定向与6组原完整回归全通过，Debug202.45/338.99秒、Release185.59/335.06秒，Rendering183.25/181.79秒、原240秒不变。Editor144/DX11 Player113/Null56文件SHA和大小双配置通过，所有会话终结。此结果更新下一段的“仍在核验”，不是整阶段冻结无Skip或人工设备丢失验收。正式M7.5及HEAD不变，未部署/提交/推送；Play恢复、原始故障诊断、跨DLL raw代、D完整搬移故障链和最终门禁仍待完成，历史失败继续保留。

2026-10-15 新增进行中的正式Edit恢复循环：ApplicationLifetime只允许精确所属服务的呈现阶段故障恢复，其他帧阶段/启动/终止关闭保持fail-stop；正式Editor使用默认拒绝的离帧Windows确认，每次恢复失败重新review/确认，恢复后帧号连续、不重放更新、不计入确认等待时间。CPU39与实际DX11循环39首轮通过（双配置图形检查），原World/历史/pin/整图和API0/0保持。自动测试为脚本确认，不是人工系统对话框/物理设备丢失验收；最终规范构建和回归仍在核验。尚无Play恢复，跨DLL raw代、完整搬移故障链及整阶段最终门禁开放；正式M7.5未替换、未提交推送。前序“无调度恢复/可见入口”说明只适用于前序增量，详见[M7当前验证记录](M7_IMPLEMENTATION_PLAN.md)。

2026-10-15 最新Edit同会话重建增量：M7.6-C已有可信宿主离帧、单次精确批准的新DX11设备重建；保持原World/Document/EditSession和Undo/Redo、UI作者/发布参与者与CPU资产引用，失败保留资源并要求明确重试，整体关闭开始后禁止转回恢复。新增23项实际GPU检查双配置通过，原HUD发布补4项重建/历史检查；最终Debug26项定向通过，Release24项首过+2项原File.Replace失败复测通过，Release原6组完整回归通过。较早Debug原6组通过，最后关闭保护用最终Debug26项补验；不冒称最终整阶段无Skip通过。候选包SHA/大小核验通过，正式M7.5未替换，无提交推送。**仍无可见恢复按钮、失败ApplicationLifetime恢复调度、Play重建或真实硬件丢失验收**；跨DLL raw句柄代、D搬移故障链与整阶段最终门禁仍待完成，间歇原子保存根因继续开放。详见[M7当前验证记录](M7_IMPLEMENTATION_PLAN.md)。以下应用/窗口记录保留为较早增量，不能当作整个M7.6已交付。

2026-10-15 最新应用生命周期增量：M7.6-C已修复最外层关闭失败后丢失持有者/覆盖首因的问题；依赖逆序关闭首败即停，显式重试、线程边界和首因诊断保留，正式Editor进程保留唯一失败持有者且不隐式二次Dispose。CPU22与实际Editor/DX1122检查通过；规范Skip Debug/Release各25组定向+6组原回归全部通过（完整Rendering221.78/201.12秒，原240秒门限不变），候选清单SHA/大小通过。原HUD与插件目录的明确拒绝诊断已保留，测试未放宽。正式M7.5未替换，无提交推送；下一步是保留World/文档/Undo的GPU新代重建、跨模块代边界与D搬移故障联合及整阶段无Skip门禁，不将新应用重启等同同会话恢复。以下窗口/更早增量记录以本条及[M7实施记录](M7_IMPLEMENTATION_PLAN.md)的当前结果为准。

2026-10-14 最新窗口增量：M7.6-C 已接入共享C#窗口生命周期，最小化/零framebuffer暂停提交并保留旧视口、取消积压HUD输入；真实GUI借用导致退休失败时保留新旧两份所有权，首个呈现故障跨关闭重试保存。窗口69、Player生命周期46、HUD交互74检查通过，Debug/Release各21组定向及6组原完整回归全部通过；完整Rendering215.21/218.02秒（原240秒不变），原Editor/Player及严格搬移交互/发布/场景阴影联合通过。两配置候选manifest SHA/大小核验通过。仅规范Skip候选，不替换正式M7.5、不提交推送。下一步还需修复最外层ApplicationLifetime关闭失败后清空持有者/覆盖首因的问题，并补正式新代重建与D搬移故障链及整阶段无Skip门禁。以下为此前增量记录，不能当作当前整个M7.6已完成；详见[M7实施记录](M7_IMPLEMENTATION_PLAN.md)。

最新推进 M7.6-C：完整 Player 运行器改为显式宿主持有，关闭失败后保留依赖并允许同线程重试；可选原生 query17 支持精确未完成帧的终止关闭，不 Present、不强制卸载、不恢复继续绘制。旧静态 Run 已移除。新增40项实际 Run/资产pin/物理/报告/重入/终止帧检查已双配置通过。进一步统一21处GPU完成等待的准确错误分类，新增408项实际健康DX11上的注入故障检查（不等于物理设备丢失）；修复Editor修正已提交配置后无法关闭旧候选并恢复视口的问题，原C2/C3宿主扩展检查通过。完整Debug回归暴露240秒超时，保留失败日志并优化原生离线环境烘焙的重复计算，维持采样/数值/全部原断言，新增两份冻结数值hash；最终Debug原完整Rendering225.65秒、16组定向及原Editor/Player/UI/SceneRendering通过，Release16组定向及6组原回归也全部通过（完整Rendering226.39秒），最新证据见 [M7实施记录](M7_IMPLEMENTATION_PLAN.md)。历史暖态4096B分配、File.Replace及耗时波动调查继续，不以重跑通过宣称根因全部解决。整阶段仍未交付；C的共享尺寸/最小化/新设备代重建与故障诊断保留、D搬移故障链及完整冻结无Skip门禁未关闭，未提交推送，正式EXE仍为M7.5。

M7.4详见[分片方案](M7_4_IMPLEMENTATION_PLAN.md)、[A合同](M7_4_A_RUNTIME_CONTRACT.md)、[A交付](M7_4_A_DELIVERY_REPORT.md)。A实现纯C# ACES fitted/Reinhard/Linear Clamp与同一公开Tone组合，538项实际DX11检查及完整双配置通过；没有切换正式默认Shader或新增Agent执行。B公开HDR子图/真实Bloom/无源码NPH1已完成172项实际GPU检查及完整双配置，见[B合同](M7_4_B_RUNTIME_CONTRACT.md)、[B交付](M7_4_B_DELIVERY_REPORT.md)；C1受检程序库/产品闭包149项及完整双配置通过，见[C详细方案](M7_4_C_IMPLEMENTATION_PLAN.md)、[C1交付](M7_4_C1_DELIVERY_REPORT.md)。C2正式配置/共享执行已通过完整双配置，见[C2合同](M7_4_C2_RUNTIME_CONTRACT.md)、[C2交付](M7_4_C2_DELIVERY_REPORT.md)；C3后处理Inspector/AI目录与原事务、9组作者测试、正式DX11控件联动及完整双配置通过，见[C3合同](M7_4_C3_RUNTIME_CONTRACT.md)、[C3交付](M7_4_C3_DELIVERY_REPORT.md)；D1公开定制示例98项实际检查及完整双配置通过，见[D方案](M7_4_D_IMPLEMENTATION_PLAN.md)、[D1示例](M7_4_D1_CUSTOMIZATION_EXAMPLES.md)、[D1交付](M7_4_D1_DELIVERY_REPORT.md)；D2联合图像582次检查/120图、独立后处理整图最大误差1/API0/0及完整顺序Debug/Release通过，见[D2合同](M7_4_D2_IMAGE_CONTRACT.md)、[D2交付](M7_4_D2_DELIVERY_REPORT.md)；D3-A正式宿主可信管线定制、599项源/搬移检查及完整顺序Debug/Release通过，见[D3-A合同](M7_4_D3A_RUNTIME_CONTRACT.md)、[D3-A交付](M7_4_D3A_DELIVERY_REPORT.md)；D3-B正式Player被动HUD/受检NCU1闭包、336项检查及完整顺序Debug/Release通过，见[D3-B合同](M7_4_D3B_RUNTIME_CONTRACT.md)、[D3-B交付](M7_4_D3B_DELIVERY_REPORT.md)；D3-C已完成Editor场景HUD、可信交互路由、作者不可变准备与同一历史的选择发布，以及完整FBX/IBL/阴影/Bloom/UI搬移联合；64份冻结源码完整顺序Debug/Release各12原生/39联合组/Python44通过，正式Release受检部署完成，见[D3-C交付](M7_4_D3C_DELIVERY_REPORT.md)。D3/D/M7.4自动候选链闭合；人工、用户素材、目标环境及完整性能门禁保留，M7.5软阴影档位自动候选已完成，见[M7.5实施与交付](M7_5_IMPLEMENTATION_PLAN.md)；下一阶段M7.6资源更新与故障恢复。

**历史阶段状态（不覆盖上方最新交付）：M6.10最终双配置及提交推送完成，远端74be5b4已核对；M7.3-C3-C正式产品联合验收与完整双配置通过，M7.3自动候选链完成；M7.4-A Tone预设、B实际HDR子图/Bloom及各自完整双配置通过，C1程序库及完整双配置通过，C2正式配置/共享宿主及完整双配置通过，C3专用编辑器/AI及完整双配置通过，D1公开定制示例及完整双配置通过，D2联合图像及完整双配置通过，D3-A正式宿主可信管线定制及完整双配置通过，D3-B正式Player被动HUD与受检闭包及完整双配置通过，D3-C已完成Editor场景HUD、可信交互路由、作者不可变准备与同一历史的选择发布，以及完整FBX/IBL/阴影/Bloom/UI搬移联合；64份冻结源码完整顺序Debug/Release各12原生/39联合组/Python44通过，正式Release受检部署完成，见[D3-C交付](M7_4_D3C_DELIVERY_REPORT.md)。D3/D/M7.4自动候选链闭合；人工、用户素材、目标环境及完整性能门禁保留，M7.5软阴影档位自动候选已完成，见[M7.5实施与交付](M7_5_IMPLEMENTATION_PLAN.md)；下一阶段M7.6资源更新与故障恢复。
M7.1-A纯C# Shader描述/目录/声明验证自动候选已实现，70项定向及完整顺序Debug/Release/checked deployment通过。
M7.1-B41项及完整门禁通过；C1默认／用户Tone24项、C2同源Geometry/Shadow/Skin40项、C3独立Flat2D32项、C4-A77/B42/C62及各自完整顺序Debug/Release/checked deployment通过。C4-C加入项目文件/pin/manifest绑定与运行包，独立UI现为18文件。M7.1自动候选完成；M7.2统一材质合同/预设/68项GPU检查及完整双配置/部署通过。M7.3-A离线环境资产/数值Cook的123项、B1真实环境GPU资源54项检查与完整双配置/部署通过；新增B2独立环境Shader/场景绑定46项及完整双配置/部署通过；B3完整自动图像353项及完整双配置/部署通过；C1环境资产/场景配置/运行包新增29+1项及完整双配置/部署通过；C2-A环境Shader文件/整组准入71项及完整双配置/部署通过；C2-B共享执行87项及完整双配置/部署通过；C2-C正式宿主43项及完整双配置/部署通过；C3-A默认资源/部署83项及完整双配置通过；C3-B1属性/状态8组与正式宿主51项及完整双配置通过；C3-B2预设发布/目录授权13组、正式宿主Debug102/Release111次检查及完整双配置通过；C3-C受检搬移产品490项检查及8个探针、完整双配置通过，M7.3自动候选链完成；M7.4-A/B/C1/C2/C3/D1/D2/D3-A/D3-B/D3-C自动候选完成，M7.5自动候选完成（[实施与交付](M7_5_IMPLEMENTATION_PLAN.md)，四档阴影/受检库/正式宿主与Inspector、真实FBX及搬移联合，47份冻结源码完整Debug/Release各12原生/47联合组/Python44和部署核验通过），M7.6进行中（资源准备回调边界首增量，完整交换/宿主恢复/验收未完成），M7.7未实现，人工/用户素材/完整性能等门禁仍开放。本版本只实现DX11。**

小阶段顺序与验收见[M7总方案](M7_IMPLEMENTATION_PLAN.md)、[M7.1详细方案](M7_1_IMPLEMENTATION_PLAN.md)。

M7.6-A最新增量：资源回调守卫、缓存GPU借用/退休所有权之后，已补场景候选统一准备边界（图/Tone/Shader组/environment/post），捕获拒绝不再放行外层候选；精确目标尺寸、真实帧中拒绝、合法独立新管线及旧GPU画面保持新增35检查，连同资源34及原场景/阴影联合六组双配置通过。原完整渲染最终核验见M7总方案逐次记录；历史原子保存异常根因未关闭。完整交换的失败保留、宿主恢复及全阶段无Skip门禁仍未完成；不提交、不替换M7.5正式入口。

M7.6-B在做有界资源交换：场景直接持有待交换pipeline/环境GPU及CPU租约，准备与释放双失败进入保留所有权的fail-stop；显式批准后离帧排空并重新准备，关闭失败仅允许重试关闭。后处理probe由runtime保留；环境预检进一步改为宿主先持有再实例Prepare，旧静态入口删除，Editor/Play/Player同步迁移。新增预检15与实际宿主17检查（Busy宿主故障为明确测试注入），总交换68与七组定向双配置通过；最新原完整Rendering229.43/163.88秒，240秒门限不变，三种Release候选manifest SHA通过。规范Skip候选未部署，正式M7.5 EXE未变。无post启动/场景构造失败保留、Run/CLI完整恢复及搬移联合故障测试未补齐，不能称设备恢复或整阶段交付，尚未提交推送。

M7.6-B管线启动增量（前序记录）：有post/无post统一由宿主先持有的ScenePipelineStartup实例保留唯一GPU探针，旧静态Prepare和PostProcessRuntime的重复探针持有逻辑已移除。新增22项真实GPU/宿主故障检查，总交换90、七组定向及原完整Rendering双配置通过（230.94/163.35秒），三种Release候选manifest SHA通过。完整冻结无Skip/提交推送/部署未执行，正式EXE仍为M7.5。

M7.6-B最新构造增量：真实FBX红测复现资源准备失败后局部owner丢失；SceneRenderSession/GpuResources/AnimationSession改为先保存持有者再实例Prepare，删除旧可失败构造/静态工厂。Editor保留最多一个待交换整Scene，Player/图预览同步迁移，失败后拒绝覆盖并保留关闭重试能力。新增70项真实ASCII/Binary FBX及实际宿主类故障检查，八组定向与五组原完整回归双配置全通过，Rendering233.16/166.18秒、原240秒门限不变，候选manifest SHA均通过。宿主故障检查含明确的测试字段注入，不代表完整Run/CLI恢复或设备丢失；C设备分类/D搬移故障联合和整阶段冻结无Skip仍未完成，尚未提交推送或部署。

M7.3拆为A离线资产/Cook、B真实DX11 IBL、C正式宿主/配置/闭包。A123/B1资源54/B2绑定46/B3完整自动图像353及各自完整顺序Debug/Release/checked部署通过，见[总方案](M7_3_IMPLEMENTATION_PLAN.md)、[B分片](M7_3_B_IMPLEMENTATION_PLAN.md)、[B3方案](M7_3_B3_IMPLEMENTATION_PLAN.md)、[合同](M7_3_B3_RUNTIME_CONTRACT.md)、[交付](M7_3_B3_DELIVERY_REPORT.md)。B3独立软件采样/解析常量方向积分/uniform BRDF、36个实际ASCII/Binary FBX-NCA动画skin/shadow/material联合整图、AO/normal/emissive/HDR/Off/default-user/source-free、resize/update/关闭及有界成本通过，像素/积分最大1阶/API0/0。14份冻结SHA/107manifest110实际/两110备份/旧5Shader包及索引hash核对，GpuValidated仍false。B自动候选链闭合。C1独立ncenv/NCE pins/NCP environment与扁平值组件/原可撤销命令合同已完成，原ncmeta/MCP黄金schema不变；新增29+1项、完整顺序Debug/Release/checked部署通过，22份冻结SHA/107manifest110实际/两110备份/Complete journal核对，见[C分片方案](M7_3_C_IMPLEMENTATION_PLAN.md)、[C1合同](M7_3_C1_RUNTIME_CONTRACT.md)、[C1交付](M7_3_C1_DELIVERY_REPORT.md)。C2-A显式v2最多9包/双shadow/全profile共享skin字节与query12/15整组准入已完成，71项实际source-free/custom/失败重试/owner/纯UI和完整双配置/checked部署通过，见[C2详细方案](M7_3_C2_IMPLEMENTATION_PLAN.md)、[C2-A交付](M7_3_C2A_DELIVERY_REPORT.md)。C2-B共享场景执行87项及完整双配置/部署通过，见[C2-B合同](M7_3_C2B_RUNTIME_CONTRACT.md)、[C2-B交付](M7_3_C2B_DELIVERY_REPORT.md)。C2-C正式宿主43项及完整双配置/部署通过，见[C2-C交付](M7_3_C2C_DELIVERY_REPORT.md)；C3-A默认资源/部署83项及完整双配置通过，见[C3-A交付](M7_3_C3A_DELIVERY_REPORT.md)，C3-B2已通过完整双配置，见[C3-B2合同](M7_3_C3B2_RUNTIME_CONTRACT.md)、[交付](M7_3_C3B2_DELIVERY_REPORT.md)，C3-C已完成双配置490项检查及8个探针，见[C3-C交付](M7_3_C3C_DELIVERY_REPORT.md)，M7.3自动候选链闭合；M7.4-A/B/C1/C2/C3/D1/D2通过，下一片M7.4-D3；显式IBL已接入，ambient保持独立含义，HDR/EXR解码和人工门禁仍开放。人工/用户素材/目标/自包含/完整性能/1h门禁保持开放。
M7.1-A边界/最终验证记录见[契约](M7_1_A_RUNTIME_CONTRACT.md)、[交付](M7_1_A_DELIVERY_REPORT.md)，不冒称已执行shader。
M7.1-B真实SM5编译／闭合反射、cache/owner/off-frame/原子失败与最终证据见[契约](M7_1_B_RUNTIME_CONTRACT.md)、[交付](M7_1_B_DELIVERY_REPORT.md)，未接入新GPU管线或Agent编译权限。
M7.1-C1闭合Tone实际GPU接入、同源默认／用户像素、PBR联合oracle、故障与最终交付见[契约](M7_1_C1_RUNTIME_CONTRACT.md)、[交付](M7_1_C1_DELIVERY_REPORT.md)。B状态是历史切片；C1不授予Agent编译／源码／GPU写权限，不包含C2–C4或正式默认入口切换。
M7.1-C2闭合Geometry/Shadow/Skin、整组／共享kernel原子替换及最终双配置／部署证据见[方案](M7_1_C2_IMPLEMENTATION_PLAN.md)、[契约](M7_1_C2_RUNTIME_CONTRACT.md)、[交付](M7_1_C2_DELIVERY_REPORT.md)。
M7.1-C3独立Flat2D默认/用户注册VS/PS、原子替换/显式缓存刷新和独立UI示例闭包见[方案](M7_1_C3_IMPLEMENTATION_PLAN.md)、[契约](M7_1_C3_RUNTIME_CONTRACT.md)、[交付](M7_1_C3_DELIVERY_REPORT.md)。后续C4范围及进度见下条；没有Agent编译权限或人工／性能／长跑验收。
M7.1-C4的A包/预检77项、B正式准入/共享宿主42项、C版本化项目选择/运行包/文件pin62项与完整联合门禁均通过，见[C4方案](M7_1_C4_IMPLEMENTATION_PLAN.md)、[A交付](M7_1_C4A_DELIVERY_REPORT.md)、[B交付](M7_1_C4B_DELIVERY_REPORT.md)、[C交付](M7_1_C4C_DELIVERY_REPORT.md)。C4/整个M7.1自动链闭合，下一片M7.2；CPU包GpuValidated仍false，内部参考反射仍依赖编译器，无Agent新执行权限。人工/用户FBX/目标/自包含/完整性能/1h仍待验收。
已核对现有局部VS反射/固定操作契约/PBR-PCSS及GPU last-valid无sample-frame ID的限制，不冒称完整shader/IBL/后处理框架。
M7.2六槽语义、ORM/MRA、原可撤销预设命令、运行包拒绝及静态/compute skin真实像素证据见[方案](M7_2_IMPLEMENTATION_PLAN.md)、[合同](M7_2_RUNTIME_CONTRACT.md)、[交付](M7_2_DELIVERY_REPORT.md)。正变换限制和原测试保留；M7.2已经完成自动候选并推送，当前M7.4-A/B/C1/C2/C3/D1/D2/D3-A/D3-B自动候选完成，下一片为M7.4-D3-C（Editor场景HUD、交互联动与完整搬移联合），D3联合仍待完成。材质面板人工操作与原人工/用户FBX/目标/自包含/性能/长跑验收仍待完成。

- 完成DX11 Shader编译/反射、绑定验证、资源更新及网格/GPU蒙皮/材质/HDR/UI合成，补齐本版本缺口。
- 保持API中立的能力查询、资源/执行/扩展模块契约；Vulkan/OpenGL下一版本再实现device/swapchain/shader/draw等实际后端。
- 本版本请求未实现后端必须明确unsupported，不注册假实现、不静默回退；加载器探测不是渲染实现。
- 完善共同 PBR 参考场景、纹理工作流、IBL、后处理和现有软阴影的质量/性能档位。
- 添加参考图像与误差容限、Resize/资源重建/故障测试，采集帧捕获和 GPU 性能证据。
- 运行D3D11 Debug Layer；帧捕获不代替API验证。本版本不要求Vulkan/OpenGL验证层或跨API图像对比。

验收：DX11静态/动画/PBR/阴影/UI参考场景与独立oracle在定义容限内，API验证无错误，资源生命周期和性能预算有记录。
Vulkan/OpenGL仅扩展边界保留，不称已实现或已验收。待下一版本逐后端实现并单独验收。

### M8：可选 AI 模块与深度引擎扩展

**状态：协议/安全模型部分有基础，实际 Python 通信与完整扩展系统未实现。M7自动候选链与完整双配置门禁已经闭合，接续启动M8；人工／目标性能等保留门禁不自动关闭。**

- Python 独立模块优先建立 C# 请求/快照/结果契约，再选择一个传输实现；gRPC worker 为候选，ZeroMQ 按实测需求考虑，pythonnet 限可信插件。
- 所有 AI 结果通过有界队列异步返回，在 C# 边界校验会话、World/revision/tick、范围和权限；支持超时、取消、过期拒绝、重启与背压。
- 游戏帧不等待推理/IPC；Python 不持有可直接写入的 World，也不作为 Behaviour 执行。
- 托管扩展逐步支持 System、组件 Schema、编辑器面板、导入器和工具能力注册；增加版本/依赖、线程约束与卸载策略。
- 深度开发能力涵盖资产、场景、动画、UI、验证、构建和扩展，但文件/代码修改必须限定项目范围、审批风险并记录审计。
- 不提供默认任意 eval/程序集加载/任意 shell；新增可执行扩展与纯数据事务采用不同权限边界。
- 持续统一能力名称、描述、输入/输出 JSON Schema、风险分类、确定结果、预算和取消语义。

验收：Agent 能从检查到获批事务、验证与证据报告完成闭环；越权、陈旧/重复结果与模块崩溃不破坏场景；禁用 Python 后引擎仍可编辑和运行 C# 游戏。

### M9：导出、性能与可发布版本

**状态：未实现完整 Player 导出与发布；M8结束后执行M9。**

- 建立独立 Player 引导、资产打包/依赖裁剪、项目设置、版本兼容与错误报告；游戏包不强制携带编辑器或 Python。
- 完善文件格式迁移/失败保护、日志诊断、自动化 CI、Debug/Release 与编辑器/Player 回归。
- 以真实动作场景测量 C# 分配/GC、查询/批处理、native 调用次数、CPU/GPU 和动画成本；再决定类型池、缓存、调度与原生内核优化。
- 为目标硬件制定帧时间、内存和加载预算；不把当前字典/装箱原型标记为优化 ECS。
- 建立运行时泄漏、长时间 Play、资产重载、异常退出与损坏输入测试；新代码警告按缺陷处理。

验收：新机器可构建/运行，发布包脱离开发环境可启动动作样例；关键格式拒绝/失败有保护与恢复路径；目标硬件性能预算达标。
本版本发行验收以DX11为范围；Vulkan/OpenGL及跨API一致性验收移至下一版本，不将探测/扩展接口标为已渲染。

### N：独立网络系统（可选支线）

**状态：未实现；不阻塞单机开发。**

- 先确定传输、协议、安全/认证和部署要求，再选择后端；当前未指定协议。
- 独立 C# 库管理连接、会话、消息 Schema、版本协商和有界队列，不能依赖场景/渲染/脚本宿主。
- 游戏通过显式适配器在固定步消费输入/结果；网络身份限定会话，不复用序列化运行时句柄。
- 需要多人动作时，再实现输入序号、权威、插值、预测/校正与延迟补偿服务；不进入组件/Behaviour 基类。
- Python AI IPC 与游戏网络契约保持独立。

验收：无界面回环、离线模式、断线/重连、畸形消息、旧身份和模拟丢包/延迟测试通过。详见 [独立网络设计](NETWORKING.md)。

## 4. 首个可用版本与延后项

首个 DX11 动作制作 MVP：

> 导入真实 FBX → 保存为资产 → 实例化场景角色 → GPU 蒙皮 → 碰撞与根运动 → C# 移动/攻击/闪避 → 可编辑 HUD（M5）→ 最小动画图（M6）→ Agent 事务/Undo → 独立 Player。

这是 M1–M6 与 M9 最小子集的交付目标，不要求等待所有高级功能。本版本只实现DX11，Vulkan/OpenGL保留扩展并延后到下一版本。
执行顺序为M6结束→M7→M8→M9；每个前置阶段完成并测试通过后才启动下一阶段，失败先修复重测。
AI场景/资产/动画/UI能力仍随对应阶段交付，Python运行时AI、网络和通用扩展不作为单机启动依赖。

暂不提前铺开：完整 UE 功能对等、Motion Matching、复杂 IK/重定向、多人大规模同步、无证据的全面 ECS 重写、所有 Python 通信方案同时实现。现有 FBX 限制与旧数据兼容不能被扩大宣传。

## 5. 每阶段的完成规则

- 涉及代码改动时使用 `Build.bat`，先构建 NcmaCore、NcmaNative、NcmaArchitectureTests，再处理旧目标。
- 运行 CTest、托管构建、managed/native smoke 和 `python -m ncma_tools.cli inspect .`；同时补充该阶段的失败路径与真实交互测试。
- 保存 UUID、可撤销命令、权限检查、版本迁移、线程/资源释放属于验收项，不是最后再补的功能。
- 只有模型/声明、仅通过编译、单个预览或加载器探测，不足以标记完整模块完成。
- M1.1 代码验证通过后记录交付证据；后续阶段仍需运行完整构建，见 [构建说明](BUILDING.md)。

专题文档：[动画](ANIMATION.md)、[FBX 导入](FBX_IMPORT.md)、[动画 MCP](ANIMATION_MCP.md)、[World 访问](WORLD_ACCESS.md)、[Python 模块](PYTHON_MODULES.md)。
