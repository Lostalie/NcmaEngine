# Python 游戏逻辑：首个编辑器运行链路

C# 与 Python 均可挂载节点脚本；C++ 继续负责场景、渲染、物理、资产与动画求值。
Python API/lifecycle 参考 Infernux 的分层思路，独立实现，不是其资产或插件兼容层。
当前范围是编辑器运行预览，不是完整游戏 SDK 或 Player 发布系统。

## 开始使用

1. 安装 x64 CPython 3.10+，包含 `include/Python.h`、导入库、DLL 和标准库。
   当前验证环境是 CPython 3.12.8；其他版本尚未逐一验证，暂不支持自由线程构建。
2. 运行根目录 `Build.bat`，启动 `out/bin/NcmaEngine.exe`。
3. 选择 Character 节点，在 Inspector > C# / Python Behaviours > + Add Behaviour 中
   选择 `rotator.RotatorBehaviour [Python]`。
4. 调整 degrees_per_second、clockwise、multiplier，点击 Play。参考立方体会使用
   被选节点的世界变换旋转；Pause 暂停，Stop 丢弃运行时场景变化。
5. 修改 `gameplay/python/rotator.py` 后，在 Gameplay > Reload Python Scripts 手动重载；
   Python 修改无需编译 C++。新增 `.py` 脚本也通过该操作发现。
6. Ctrl+S 保存 `.ncscene` v3；Ctrl+O 读取。v1/v2 仍可读取，v2 脚本默认迁移为 C#。

Inspector 的挂载、移除、启用、Export 编辑和 Update Export Schema 均走场景撤销路径。
Play 期间禁止编辑这些绑定。当前样例可以仅用 C# 或仅用 Python，也可在不同节点挂载
不同语言；基础同场景执行已回归，但还没有跨语言对象引用或组件优先级调度系统。

## 脚本示例

保存到 `gameplay/python/mover.py`，再点击 Reload Python Scripts：

```python
from ncma_gameplay import Behaviour, export

class Mover(Behaviour):
    speed = export(2.0, display_name="Speed", category="Movement")

    def on_update(self, delta_seconds):
        transform = self.node.local_transform
        transform.position.x += self.speed * delta_seconds
        self.node.local_transform = transform
```

`local_transform` 返回值副本，必须写回，不能只修改取出的副本。
其他 Node 操作包括 create_child、set_parent 和 destroy，使用同一原生场景 C ABI v1。
`export` 支持有限浮点数、int32 和 bool；默认浮点类型是 Double，显式 Float 可使用
`export(1.0, kind=ExportKind.FLOAT)`。暂不支持字符串、列表、资产引用或嵌套对象。

## 生命周期与所有权

- 加载时仅发现类型和 Export 描述，不实例化 Behaviour；可信模块的顶层代码会执行。
- Play 为每个挂载实例构造对象，分配 Node、应用保存的 Export，然后调用 on_create。
  启用的实例依次执行 on_enable 和每帧 on_update。
- 禁用实例仍接收 on_create/on_destroy，但不接收 on_enable/on_update/on_disable。
- 停止时反序清理，启用实例先 on_disable 再 on_destroy。某个清理回调抛异常时仍清理其他实例。
- Node 借用 Play 场景，不持有 C++ 世界所有权；结束后保留的 Node 会报错，不再调用已销毁的世界。
- 原生场景访问限定会话主线程。当前两种语言按 C# → Python 分组更新，不提供逐组件优先级。
- on_fixed_update 只有接口，固定步调度仍未实现；不可宣称已参与物理固定更新。

运行异常会暂停编辑器播放并记录 traceback，SystemExit 也转换为宿主错误，不退出编辑器。
绑定失败清理实例；重载失败停止播放并保留编辑场景。重载从源文件重新编译，不依赖 `.pyc`
时间戳，清除本次项目模块；私有状态、运行时修改的 Export 值不迁移。
项目脚本必须自行释放其外部资源、订阅和回调，不能宣称任意第三方模块都能完全热重载。

## 嵌入与安全边界

`NcmaPythonGameplay` 是独立 C++ 适配目标；CPython 头文件不进入场景/动画/渲染接口。
脚本通过 ctypes 调用现有版本化 C ABI，不把 Python 对象或 C++ STL 暴露到跨语言 ABI。
解释器使用隔离启动配置，忽略外部 Python 环境变量，不自动载入 site-packages，也不写字节码。
它在进程中初始化一次，Stop/Reload 释放项目模块和场景会话，但不反复销毁/初始化 CPython。
游戏回调在初始化主线程执行；后台脚本调度、虚拟环境和第三方包管理尚未实现。

隔离启动与场景副本不是安全沙箱。脚本是可信项目代码，可能访问文件、网络或执行其他代码；
不能给 Agent 增加任意 Python 执行权限。现有 MCP 仍只控制独立动画实验室，不连接游戏会话。

## 仍未实现

项目默认语言设置、项目脚本之间的包/模块导入与依赖热重载、文件监听自动重载、
虚拟环境/第三方包管理、固定步调度、协程、私有状态迁移、完整输入/物理/音频/UI/动画组件 API、
脚本性能预算、进程隔离和独立游戏导出。当前脚本模块支持标准库与 ncma_gameplay 导入；
不要将它理解为完整 Infernux 插件运行环境。

## 验证

Build.bat 覆盖原生 CPython 宿主、隐藏编辑器 Python 播放/重载/撤销、C#/Python 同场景执行，
并保留 C#、FBX、动画和 MCP 回归。Python 单元测试额外覆盖生命周期、失效 Node、线程保护、
源文件重载、Unicode 脚本、参数校验、语法错误和清理失败。

嵌入配置参考 [CPython 初始化文档](https://docs.python.org/3.12/c-api/init_config.html)。
