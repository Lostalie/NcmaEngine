# NcmaEngine

面向模型与动画驱动动作游戏的引擎：**C# 主引擎与游戏逻辑 + C++ 性能插件 + 可选 Python 专长模块**。

更新：2026-10-18。当前是持续开发中的可验证候选，不是生产就绪版本。M8.2 已通过同源码完整 Debug/Release 自动回归与受检部署；**ECS E0、E1、E2 与 E3-A 混合分页存储已完成自动候选门禁；E3-A 同源码 Debug/Release 各 89 项 CTest、新增 10 组分页测试通过，下一步 E3-B 增量精确验证，M8.3 未开始**。契约、测量限制、失败修正与交付证据见 [ECS 实施记录](docs/ECS_IMPLEMENTATION_PLAN.md#9-e3-增量固定步实施顺序)。人工、目标机、自包含和完整性能/长稳验收仍开放。

最新目标顺序：**M8.2 收尾 → ECS 重构 → M8.3 及后续 → M9**。源码已采用唯一 World 的强类型池与代次 Entity，删除两种旧 ObjectReference、Runtime.GameObject 和低层旧创建/查找/枚举入口；低层消费者使用 World + Entity，用户侧仅保留 Ncma.GameObject 高层门面，共享同一份组件存储。E2-A 提供有界 All/None、常见类型组合、确定性成员缓存及受检迭代；E2-B 提供共享 1024 条/1 MiB 预算的结构批次、创建依赖、PendingEntity 与提交后回执，旧 PendingObject 已删除。失败批次不留下前缀且毒化整个候选步。E3-A 值池采用 32 值/页的惰性 COW，连续稀疏/实体索引仅在结构写入时复制；全量快照验证仍保留，E3-B/C/D 和完整 ECS 验收尚未完成。局部测量显示稀疏写分配下降、直接池读取变慢，不能据此宣称整体性能通过。正式 EXE 保持已通过完整 Release 构建与受检部署的 E1-B，本轮只生成候选包、不部署。详见 [路线图](docs/ROADMAP.md) 与 [ECS 实施合同](docs/ECS_IMPLEMENTATION_PLAN.md)。

M8.2 交付的是可选本机工具模块，不是模型推理服务；worker 开发环境须单独准备，不随默认 Editor/Player 部署。范围、测试与剩余门禁见 [M8.2 交付记录](docs/M8_2_DELIVERY_REPORT.md)。ECS 的实施边界、分片与验收见 [详细合同](docs/ECS_IMPLEMENTATION_PLAN.md) 和 [E0 基线记录](docs/ECS_E0_BASELINE.md)。

## 架构

```text
C# Editor.App                         C# Player.App / Headless
  Editor.Services / Editor.Core         Application.Runtime
       唯一 EditSession / Undo          Gameplay / Scripting
                    Scene / Runtime
           World + 扁平 GameObject + Component + Systems
                  /                         \
 C# 渲染/物理/动画/UI 高层服务          可选 Modules 异步契约
       版本化批量 C ABI                  Modules.Grpc → Python worker
 C++ 渲染/物理/数值内核/ImGui             有界复制结果，不直接修改 World
```

- C# 拥有对象与组件存储、游戏规则、调度、场景/资产元数据、序列化、编辑器业务、权限和事务。
- 场景是扁平对象列表，GameObject 是空容器，Transform 可选；没有场景父子树或变换继承。骨骼、动画图和 UI 文档内部层级独立。
- C++ 执行性能关键工作；保留原生 Dear ImGui 表现插件，不恢复 C++ World、旧 Editor 主入口或 hostfxr 游戏桥。
- Python 只用于独立工具、AI 或其他专长模块，不是 Behaviour 语言。默认 Editor/Player 不依赖 Python、Modules 或 gRPC 客户端。
- AI 与人工编辑共用经授权、可撤销的领域命令。模型返回提案，不能自行批准、写 live World/GPU 或在 tick 内同步等待。
- 游戏网络是独立可选服务，不向组件加入 RPC、复制标记或网络所有权；Python 模块通信不是游戏联网系统。

[架构合同及历史增量](docs/ARCHITECTURE.md) · [路线图与阶段记录](docs/ROADMAP.md)

## 当前能力与边界

“已实现”仅指列出的代码和测试切片；“候选”不代表人工、目标环境或完整性能验收通过。

| 模块 | 当前能力 | 仍需完成 |
| --- | --- | --- |
| Runtime / C# 游戏逻辑 | 扁平 World、UUID、组件、固定步 Systems、Behaviour、输入/信号/运行命令、隔离 Play、暂停/单步/重载 | 大规模场景与低分配性能验证；不承诺任意私有脚本状态迁移 |
| 场景与编辑器 | C# 主入口；唯一场景事务/Undo；场景、资产、动画、UI 工作区；原生 ImGui 呈现 | 真实输入法/DPI/窗口与第三方 MCP 人工验收、部分制作闭环 |
| 资产与角色 | 持久异步 FBX 导入、UUID/代次、受检 NCA、重导入/引用、DX11 场景 GPU 蒙皮；Player 使用运行包而非解析 FBX | 完整 Prefab 保存/实例/覆盖/恢复、缩略图与材质浏览、冷启动 cook、用户 DCC/素材覆盖 |
| 动作与动画 | 托管图与节点制作、执行/混合/动作序列等已交付切片，角色运动/碰撞约束根运动与战斗候选 | 完整动作游戏人工试玩、真实素材/性能验收；不声称 UE5 全功能等价 |
| 渲染 | 当前只实现 DX11；场景 PBR、GPU 蒙皮、阴影、IBL、Tone/Bloom、公开 C# 管线定制及图像/资源/恢复测试切片 | 真实设备故障、用户素材、完整性能/长稳验收；Vulkan/OpenGL 绘制留后续版本 |
| 2D / UI / HUD | 独立 UI 文档、布局/控件、字体与 2D 绘制、作者工作区、受检 HUD 包、Editor/Player 交互和可撤销选择发布 | 完整 IME/复杂文字与通用 2D 应用覆盖、人工操作及目标性能验收 |
| 物理 | Box2D/Jolt 薄插件，C# 高层服务、固定步运动权威与角色接线 | 通用游戏碰撞事件/武器连续 hitbox 等制作需求和联合验收；不默认启用所有物理功能 |
| AI / 特殊模块 | 已有 scoped MCP、提案/权限/事务工具；M8.1 有界异步契约；M8.2 本机 gRPC/Python 工具 worker、搬移与完整回归通过自动候选门禁 | 正式宿主模块入口、真实模型推理、领域提案整合和自动化工作流；pythonnet/ZeroMQ 未实现 |
| 发布 | 受检 Editor/Player 包、清单/hash、搬移与恢复测试基础 | M9 正式 Windows 游戏导出/发布闭环；微信小游戏、Android、iOS 后续版本 |
| 网络 | 独立服务边界已确定 | 正式游戏网络服务未实现 |

M1–M7 的自动候选交付不关闭历史人工、目标机器、自包含、完整性能或一小时长稳门禁。
详细功能债务见路线图；M8.2 的确定性 `ncma.tools.text_stats` 工具**不是 AI 推理服务**。

## 构建与运行（Windows）

需要 Visual Studio 2022 C++ 工具链、.NET 8 SDK，以及构建文档列出的 Python 工具/可选 worker 测试环境。使用仓库根目录的规范入口：

```bat
Build.bat -Configuration Debug
Build.bat -Configuration Release
LaunchEditor.cmd
```

- 正式编辑器：`out/bin/NcmaEngine.exe`（C# apphost）。
- `Build.bat` 初始化 VS 环境；不要在普通终端直接运行未初始化的 Ninja。
- 完整构建执行原生/托管/Python 测试、严格格式拒绝、包检查和审计，通过后受检部署整包。
- Skip 参数仅用于开发检查，**不部署正式 EXE**。最新部署配置替换上一配置；备份和日志保留在 `out/deployment`。
- 部署中断用 `scripts/Recover-Editor.bat` 显式恢复，不删除日志或强制覆盖绕过检查。
- M8.2 完整开发回归需要预先准备锁定依赖的 `out/modules/python312` 环境；不会自动安装 Python、pip 包或模型。可选 .NET gRPC 客户端使用锁定 NuGet 依赖，首次还原需要可用包源/缓存。
- 普通已发布 Editor/Player 不需要上述 worker 环境；可选模块客户端另行打包，尚未接入正式 AI 面板。

[完整构建说明](docs/BUILDING.md) · [独立模块边界](docs/PYTHON_MODULES.md) · [M8 分阶段计划](docs/M8_IMPLEMENTATION_PLAN.md)

## 源码导航

| 路径 | 职责 |
| --- | --- |
| `managed/Ncma.Runtime`、`Ncma.Scene`、`Ncma.Gameplay` | 权威 World、场景文档、游戏生命周期 |
| `managed/Ncma.Editor.Core`、`Ncma.Editor.Services`、`Ncma.Editor.App` | 编辑命令/历史、业务组装、正式入口 |
| `managed/Ncma.Application.Runtime`、`Ncma.Player.App` | Editor/Player 共用运行服务和独立 Player |
| `managed/Ncma.Assets*`、`Ncma.Asset.Import*` | 资产身份、作者流程、异步导入与运行资产 |
| `managed/Ncma.Animation*`、`Ncma.Movement`、`Ncma.Characters` | 动画、运动与动作角色 |
| `managed/Ncma.Rendering*`、`Ncma.Scene.Rendering`、`Ncma.Ui*` | 托管渲染组合、场景呈现、UI |
| `managed/Ncma.Modules*`、`python/worker` | 可选异步模块与真实工具 worker，不是游戏脚本 |
| `engine/source`、`engine/sdk` | 原生插件/内核及 Eigen、GLFW、ImGui、spdlog、Box2D、Jolt、ufbx |
| `scripts`、`python/src/ncma_tools`、`docs` | 构建/验证/部署工具与合同/交付证据 |

只接受 `.ncmascene` SceneDocument JSON v1；旧 `.ncscene`、旧 SceneSnapshot 兼容路径和 Python 游戏脚本入口不恢复。运行句柄不序列化，持久对象/资产使用 UUID。

## 设计参考

| 参考 | 借鉴范围 |
| --- | --- |
| ProwlEngine / Unity | C# 组合式对象和编辑器/资产工作流 |
| Unreal Engine 5 | 动作动画、状态机、Root Motion、通知与节点制作 |
| Infernux | Python 独立模块/工具分层，不再作为游戏脚本参考 |
| [用户提供的编辑器图片](docs/EDITOR_INTERFACE_REFERENCE.md) | 深蓝统一工作区视觉，不采用图中 Node 场景树、品牌或未经实现的功能声明 |

不再以 Godot、Piccolo、Hazel 或类 Figma 编辑器为设计参考。参考不代表依赖、兼容或功能等价。

更多：[AI 开发接口](docs/AI_DEVELOPMENT.md) · [活动编辑器 MCP](docs/EDITOR_MCP.md) · [动画](docs/ANIMATION.md) · [FBX](docs/FBX_IMPORT.md) · [网络边界](docs/NETWORKING.md)
