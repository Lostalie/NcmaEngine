# NcmaEngine

## 当前方向：动画驱动的动作游戏

技术栈定位为 C++20 / CMake 核心，C# 与 Python 均为主游戏逻辑语言，用户可任选一种。
C# 参考 ProwlEngine，Python 参考 Infernux；Python 同时承担工具与 AI。
当前 C# 与 Python 均已接入节点脚本、标量 Export、隔离 Play 场景与手动重载。
Inspector 挂载脚本时可选择 `[C#]` 或 `[Python]`；Python 示例位于 `gameplay/python/rotator.py`。
这是双语言编辑器运行链路，尚无完整游戏 SDK、固定步调度或独立游戏导出。
使用根目录 `Build.bat` 构建，编辑器输出为 `out/bin/NcmaEngine.exe`。

新增动作动画运行时与 Action Animation Lab：骨骼采样、姿势混合、根运动、动作通知、
Idle/Run/Attack/Dodge 示例、撤销/重做；提供 C# API 和真实 stdio MCP 动画控制接口。
已新增 FBX 角色导入与独立 CPU 蒙皮线框预览，读取骨架、网格、UV、材质槽和动画片段；
尚未接入 GPU 蒙皮与游戏场景角色组件。MCP 控制独立动作实验室会话，不连接活动编辑器。

- [构建与运行](docs/BUILDING.md)
- [动作动画设计、已实现能力与后续任务](docs/ANIMATION.md)
- [FBX 角色导入与限制](docs/FBX_IMPORT.md)
- [AI/MCP 接入](docs/ANIMATION_MCP.md)
- [Python 游戏脚本与双语言工作流](docs/PYTHON_GAMEPLAY.md)
- [最新路线图](docs/ROADMAP.md)

## 设计参考

| 引擎 | 参考方向 |
|---|---|
| Unreal Engine 5 | 动作动画、状态机、Root Motion、动画通知与节点编辑器 |
| Godot | 场景树、节点组织与场景复用 |
| Unity | 组件式设计、C# 游戏逻辑与编辑器工作流 |
| [ProwlEngine](https://github.com/ProwlEngine/Prowl) | C# 组件 API、编辑器与运行时分离、Inspector 扩展、资产工作流及撤销/重做设计 |
| [Infernux](https://github.com/ChenlizheMe/Infernux) | Python 游戏逻辑、生命周期、属性暴露、编辑器工具与原生运行时分层设计 |

ProwlEngine 作为设计参考，不作为运行时依赖；不照搬其纯 C# 技术栈。
Ncma 保持 C++ 底层不变，C# / Python 为同等游戏逻辑语言选项，Python 兼任工具/AI；
GLFW、ImGui、Box2D、Jolt 的既定分工不变，不照搬参考引擎的渲染或物理技术栈。
Figma 仅作为 UI 编辑器交互参考，不属于游戏引擎。

以下原型说明保留为历史记录，其中 Python 游戏逻辑、D3D12 等不代表当前 CMake 构建能力。

从零开始撸引擎

## 写在开始之前

Ncma Engine是一个自嗨用游戏引擎。
参考 ProwlEngine、Infernux、虚幻引擎、Godot 和 Unity 的设计，随缘写，随缘停。总之，这是一个坑。

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

### 脚本架构 (Python + C++)

采用 Python + C++ 混合模式，使用 Python 开发游戏逻辑，C++ 驱动底层：

| 组件 | 描述 |
|------|------|
| **PythonHost** | Python C-API 集成，管理 Python 运行时生命周期 |
| **ScriptModule** | 模块接口，IModuleInterface 实现，负责启动/关闭 Python |
| **ScriptLayer** | Layer，每帧更新所有脚本组件 |
| **ScriptComponent** | ECS 组件，挂载到 Entity 上的脚本容器 |
| **ScriptInstance** | Python 脚本实例，管理类加载和生命周期调用 |
| **GameObject** | Python 可访问的 Entity 包装器 |
| **ScriptBindings** | Python <-> C++ 绑定处理 |

**Python 包结构：**
- `NcmaEngine.Component` - 脚本组件基类
- `NcmaEngine.Vector3` - 3D 向量
- `NcmaEngine.Transform` - 变换封装
- `NcmaEngine.Input` - 输入系统访问

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
        ├── script/       # 脚本系统 (C++端)
        │   ├── PythonHost.h/cpp    # Python C-API集成
        │   ├── ScriptModule.h/cpp  # 模块接口
        │   ├── ScriptLayer.h/cpp   # Layer更新
        │   ├── ScriptComponent.h/cpp # 组件容器
        │   ├── ScriptInstance.h/cpp # 脚本实例
        │   ├── GameObject.h/cpp    # Entity包装器
        │   └── ScriptBindings.h/cpp # Python/C++绑定
        ├── engine/      # 引擎核心
        ├── gui/         # GUI层
        └── launch/      # 入口点

scripts/               # Python脚本目录
└── NcmaEngine/        # Python包
    ├── __init__.py
    ├── component.py   # Component基类
    ├── vector3.py     # Vector3数学类
    ├── transform.py   # Transform封装
    ├── input.py       # 输入系统
    └── components/    # 示例组件
        └── Rotator.py # 示例旋转脚本
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

## 脚本系统使用

### C++ 端

```cpp
#include "script/ScriptSystem.h"
#include "scene/Scene.h"

// 在应用启动时初始化脚本模块
ScriptModule::Get().StartupModule();

// 创建带脚本的实体
auto entity = scene.CreateEntity("Player");
auto* script = entity.AddComponent<ScriptComponent>();
script->LoadScript("scripts/NcmaEngine/components/Rotator.py");

// 在应用关闭时
ScriptModule::Get().ShutdownModule();
```

### Python 端

```python
from NcmaEngine import Component, serialized_field, Vector3, Input, KeyCode

class PlayerController(Component):
    speed: float = serialized_field(default=5.0)
    jump_force: float = serialized_field(default=10.0)

    def start(self):
        print("Player started!")
        self._velocity = Vector3.zero()

    def update(self, delta_time: float):
        # 键盘输入
        if Input.is_key_held(KeyCode.W):
            self.transform.position.y += self.speed * delta_time
        if Input.is_key_held(KeyCode.S):
            self.transform.position.y -= self.speed * delta_time

        # 鼠标位置
        mx, my = Input.get_mouse_position()
        print(f"Mouse: ({mx}, {my})")

    def on_destroy(self):
        print("Player destroyed!")
```

### 配置要求

使用脚本系统需要配置 Python 开发环境：
- `PYTHON_INCLUDE` - Python include 目录（如 `C:\Python311\include`）
- `PYTHON_LIB` - Python 库文件（如 `C:\Python311\libs\python311.lib`）

## 致谢

- [ProwlEngine](https://github.com/ProwlEngine/Prowl) - C# 组件 API、编辑器与资产工作流设计参考
- [Infernux](https://github.com/ChenlizheMe/Infernux) - 架构参考
- [spdlog](https://github.com/gabime/spdlog) - 日志库
- [GLFW](https://www.glfw.org/) - 窗口库
- [Dear ImGui](https://github.com/ocornut/imgui) - UI库
