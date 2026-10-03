# NcmaEngine

## 当前目标：C# 主运行时、独立 Python 模块、C++ 性能插件

游戏方向仍是模型/动画驱动的动作游戏，场景仍采用扁平 GameObject + 行为组件，
不恢复 Godot Node、Actor 网络复制或对象父子变换继承。

- C# 承担游戏逻辑，并拥有 World/组件、System 调度、场景/资产元数据、序列化、编辑器业务与 Undo。
- Python 为独立 AI/推理/训练、数据分析、内容生成与工具模块，不再作为 GameObject/Behaviour 语言。
- C++ 仅用于渲染、物理和有性能依据的骨骼/导入/计算等原生插件，通过版本化批量 C ABI 接入。
- Python 通信建议先做异步 gRPC worker；pythonnet 可信进程内适配、ZeroMQ 批量消息适配按需求增加。
  三种传输均未实现，AI 结果由 C# 校验并在安全边界应用，不阻塞游戏 tick。
- 网络继续是独立服务，目标由 C# 组装，不添加组件 RPC、复制标记或网络角色。

A 方案已确认：C# 主引擎 + C++ 性能插件，AI 深度参与场景、动画、UI、工具与引擎扩展。
Python 游戏脚本特性已移除；C# World、Editor.Core 场景事务/唯一 Undo 和 M1.3 固定步/输入/插值/运行命令/安全重载及 M1.4 活动场景 MCP已接入当前编辑器。C++ SceneWorld 已移除，C# 主入口、剩余面板业务和独立插件迁移尚未完成。
目前 out/bin/NcmaEngine.exe 仍是 C++/ImGui 外壳，场景权威存储已迁为 C# Runtime.World。
C++ SceneWorld、对象/组件索引和原生 World 导出已删除；编辑器通过 hostfxr 和不透明场景令牌访问 C#。Python 游戏宿主、SDK、示例、编辑器挂载与重载入口已删除。
Ncma.Gameplay.PlaySession 统一 OnFixedUpdate、只读 OnUpdate、Pause/Resume/Step 与故障状态；C++ 不再拥有 Begin/Commit 阶段。输入快照/插值/运行命令、事务信号、安全重载与本地 scoped MCP 已实现基础。托管主入口、独立 Renderer/Physics plugins 与 Python AI worker 未实现；M1 人工客户端/UI 验收待完成。
新的 Ncma.Runtime 已接管编辑器/隔离 Play，不维护两份 live 权威 World；M1.2 的 Ncma.Editor.Core 已接管 ImGui 的场景命令与唯一 Undo/Redo；原生命令栈已删除，交互草稿不修改已提交场景。Ncma.Scene 完整组件/脚本快照用于托管历史与隔离 Play；C# `.ncmascene` JSON v1 保存全部注册组件与脚本配置，并原子替换文件。
旧 `.ncscene` v1-v6 格式及其兼容/迁移入口已删除，不再可读。默认场景为 `assets/scenes/EditorScene.ncmascene`；新格式支持空容器与可选 Transform，加载失败保留原文件和当前场景。

已实现基线：D3D11 PBR/阴影预览（Vulkan 仅探测）、FBX 导入/CPU 蒙皮线框、
独立动作动画实验室与 stdio MCP、C# Behaviour/Exports/隔离 Play/手动重载、
旧 World Access 批量 Transform/安全引用/信号邮箱。以上不是完整场景角色或新架构已完成的证明。

构建仍使用 Build.bat，输出 out/bin/NcmaEngine.exe；迁移采用独立可验证切片。

- [目标架构与当前模块缺口](docs/ARCHITECTURE.md)
- [迁移顺序与兼容门槛](docs/FRAMEWORK_REFACTOR.md)
- [独立 Python 模块与通信选型](docs/PYTHON_MODULES.md)
- [AI 深度开发契约与已实现 headless 能力](docs/AI_DEVELOPMENT.md)
- [最新路线图](docs/ROADMAP.md)
- [M1 自动交付与剩余人工验收](docs/M1_DELIVERY_REPORT.md)
- [活动编辑器 MCP 使用与授权](docs/EDITOR_MCP.md)
- [M1.1 完整托管场景文档与清理记录](docs/M1_1_SCENE_DOCUMENT.md)
- [构建与当前运行方式](docs/BUILDING.md)
- [动作动画](docs/ANIMATION.md)
- [FBX 导入](docs/FBX_IMPORT.md)
- [隔离动画 MCP](docs/ANIMATION_MCP.md)
- [独立网络约束](docs/NETWORKING.md)
- [C# 托管 World 与编辑器宿主边界](docs/WORLD_ACCESS.md)

## 设计参考

| 参考 | 保留范围 |
|---|---|
| ProwlEngine / Unity | C# 组合式对象、编辑器/资产工作流 |
| Unreal Engine 5 | 动作动画、状态机、Root Motion、通知与节点编辑 |
| Infernux | Python 模块/工具分层，不再作为 Python 游戏脚本方向 |
| Figma | UI 编辑交互，不是游戏场景模型 |

参考不作为运行依赖或资产兼容承诺；Eigen、GLFW、ImGui、spdlog、Box2D、Jolt、ufbx
继续用于已有原生算法与后端，不扩展为托管运行时的通用依赖。

## 以下为历史原型说明，不是新架构或已实现能力

从零开始撸引擎

## 写在开始之前

Ncma Engine是一个自嗨用游戏引擎。
参考 ProwlEngine、Infernux、虚幻引擎和 Unity 的设计，随缘写，随缘停。总之，这是一个坑。

## 核心架构

NcmaEngine 采用现代游戏引擎架构设计，参考UE的模块化系统。

### 模块系统 (Module System)

参考 UE 的 `IModuleInterface` 和 `FModuleManager`，实现了一套模块化加载/卸载系统：

| 组件 | 描述 |
|------|------|
| **IModuleInterface** | 模块接口基类，定义 `StartupModule()` 和 `ShutdownModule()` |
| **WindowModule** | 窗口模块，封装 GLFW 窗口管理和生命周期 |
| **ModuleManager** | 模块管理器（规划中），负责模块的加载/卸载 |

### 模块生命周期

```
StartupModule()  ->  模块加载时初始化
      |
      v
  Running...
      |
ShutdownModule() ->  模块卸载时清理
```

### 核心层 (Core)

| 模块 | 描述 |
|------|------|
| **Application** | 引擎主循环管理，控制程序的生命周期 |
| **Layer & LayerStack** | 层级系统，支持Layer和Overlay的管理 |
| **Window** | 窗口抽象层，支持GLFW窗口的创建和管理 |
| **Events** | 类型安全的事件系统 |
| **Timer** | 高精度计时器，支持DeltaTime计算 |
| **Log** | 基于spdlog的日志系统 |

### 事件系统

支持以下事件类型：

- `WindowCloseEvent` - 窗口关闭事件
- `WindowResizeEvent` - 窗口大小改变事件
- `KeyEvent` - 键盘事件（按下/释放/重复）
- `MouseMoveEvent` - 鼠标移动事件
- `MouseButtonEvent` - 鼠标按钮事件
- `MouseScrollEvent` - 鼠标滚轮事件

### 渲染架构

- **RenderAPI** - 渲染API抽象层（支持DirectX 12和Vulkan）
- **D3D12Renderer** - DirectX 12渲染器实现
- **VulkanRenderer** - Vulkan渲染器实现（规划中）

### 物理架构

- **IPhysicsWorld** - 物理世界抽象接口
- **Box2DWorld** - Box2D 2D物理引擎实现
- **JoltPhysicsWorld** - Jolt Physics 3D物理引擎实现
- **PhysicsLayer** - 物理层，负责每帧更新物理模拟
- **PhysicsModule** - 物理模块，管理物理世界生命周期
- **RigidBody2DComponent** - 2D刚体组件
- **RigidBody3DComponent** - 3D刚体组件
- **ColliderComponent** - 碰撞体组件

### 游戏脚本

仅支持 C# Behaviour。Python 仅保留为独立模块、插件和工具选项。

### 第三方库

| 库 | 用途 |
|----|------|
| **Eigen** | 数学库（header-only） |
| **GLFW** | 窗口管理和输入处理 |
| **ImGui** | UI和调试界面 |
| **spdlog** | 高性能日志库 |
| **DirectX 12** | 3D图形渲染 |
| **Box2D** | 2D物理引擎 (v3.1.1) |
| **Jolt Physics** | 3D物理引擎 (v5.5.0) |

## 开发路线图

### Phase 1: 核心框架 (已完成)

- [x] 1.1 使用VS2022构建项目
- [x] 1.2 WinMain入口点
- [x] 1.3 ImGui集成
- [x] 1.4 SPDLog日志系统
- [x] 1.5 事件系统（类型安全的事件机制）
- [x] 1.6 预编译头文件规范 (CoreMinimal.h)
- [x] 1.7 GLFW窗口系统
- [x] 1.8 模块化系统 (IModuleInterface, WindowModule)

### Phase 2: 渲染层抽象 (进行中)

- [x] 2.1 RenderAPI抽象接口
- [x] 2.2 OpenGL渲染器实现
- [x] 2.3 DirectX 12渲染器 (框架)
- [x] 2.4 Vulkan渲染器 (框架)

### Phase 3: 引擎核心完善

- [x] 3.1 输入系统 (键盘、鼠标状态跟踪)
- [x] 3.2 时间系统 (DeltaTime, FPS, TimeScale)
- [x] 3.3 资源管理 (资源加载/卸载框架)
- [x] 3.4 Sandbox应用层 (渲染测试)

### Phase 4: 高级特性

- [x] 4.1 场景图系统 (Entity-Component-System)
- [x] 4.2 变换系统 (Transform 位置/旋转/缩放)
- [x] 4.3 材质系统 (PBR 材质着色器)
- [x] 4.4 物理引擎集成 (Box2D 2D物理 + Jolt Physics 3D物理)
- [x] 4.5 脚本系统

## 技术栈

- **语言**: C++17
- **平台**: Windows (x64)
- **IDE**: Visual Studio 2022
- **构建系统**: MSBuild
- **图形API**: DirectX 12, Vulkan

## 项目结构

```
engine/
├── sdk/                 # 第三方库
│   ├── eigen/          # 数学库
│   ├── glfw/           # 窗口库
│   ├── imgui/          # UI库
│   ├── spdlog/         # 日志库
│   ├── box2d/          # 2D物理引擎 (Box2D 3.1.1)
│   └── JoltPhysics/    # 3D物理引擎 (Jolt Physics 5.5.0)
└── source/
    └── runtime/
        ├── application/ # 应用层
        │   ├── module/   # 模块系统
        │   └── SandboxApp # 测试应用
        ├── core/        # 核心层
        │   ├── module/  # 模块接口
        │   ├── event/   # 事件系统
        │   └── time/    # 时间系统
        ├── input/        # 输入系统
        ├── renderer/     # 渲染层
        │   ├── api/      # 渲染API抽象
        │   │   ├── d3d12/ # DirectX 12实现
        │   │   └── vulkan/ # Vulkan实现
        │   ├── buffers/  # 顶点/索引缓冲区
        │   └── shaders/  # 着色器
        ├── physics/       # 物理引擎
        │   ├── Interface/ # 物理世界接口
        │   ├── Box2D/    # Box2D实现
        │   ├── Jolt/     # Jolt Physics实现
        │   ├── Components/ # 物理组件
        │   └── Events/   # 物理事件
        ├── scene/        # 场景系统
        │   ├── entity/   # ECS实体
        │   └── components/ # 组件 (Transform, Mesh)
        ├── material/     # 材质系统
        ├── resource/     # 资源管理
        ├── script/       # C# hostfxr 游戏宿主
        ├── engine/      # 引擎核心
        ├── gui/         # GUI层
        └── launch/      # 入口点

scripts/               # 构建脚本
```

## 模块使用示例

```cpp
// 创建并启动窗口模块
WindowModule* windowModule = new WindowModule();
windowModule->StartupModule();

// 创建窗口
Window* window = windowModule->CreateEngineWindow(WindowProps("MyApp", 1280, 720));

// 关闭模块
windowModule->ShutdownModule();
delete windowModule;
```

## 致谢

- [ProwlEngine](https://github.com/ProwlEngine/Prowl) - C# 组件 API、编辑器与资产工作流设计参考
- [Infernux](https://github.com/ChenlizheMe/Infernux) - 架构参考
- [spdlog](https://github.com/gabime/spdlog) - 日志库
- [GLFW](https://www.glfw.org/) - 窗口库
- [Dear ImGui](https://github.com/ocornut/imgui) - UI库
