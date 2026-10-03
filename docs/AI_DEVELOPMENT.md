# A 方案与 AI 深度开发：职责、权限及首个实现切片

更新：2026-10-04。用户已确认 C# 主引擎 + C++ 性能插件，AI 参与场景、动画、UI、工具与引擎扩展。
这是产品方向，不代表所有操作已经接入当前编辑器。

## 已实现的 C# headless 基础

项目：managed/Ncma.Runtime；验证：managed/Ncma.Runtime.Tests，已纳入 Build.bat/CTest。
无原生 DLL、Ncma.Managed 或 Python 依赖。运行时库已是编辑器/Play 的唯一 World 权威；不存在原生 World 的双向镜像。Ncma.Editor.Core（依赖 Scene → Runtime）已接入活动 ImGui 场景，持有唯一历史；C++ 只提交意图与绘制临时预览。

- World/GameObject：扁平空对象、可选 Transform、对象 UUID 索引、创建/删除、owner-thread 检查。
- ComponentRegistry：显式可信注册稳定 typeId/version/schema 和值组件校验器。拒绝可变引用字段；启动后冻结。校验器须纯函数/幂等，不修改外部 World 或做 IO。
- 托管快照：managed JSON v1，校验大小/身份/组件/数值后原子恢复；运行时引用不保存。
- WorldRunner/PlaySession：已接入活动编辑器，OnFixedUpdate/顺序 Systems 有界固定步、OnUpdate 只读；Pause/Resume/Step/Faulted、严格与交互时间策略、丢时诊断。读已提交值、写暂存，错误中止；私有状态/IO 不回滚。
- EditSession：编辑器与 Agent 共用事务入口，原子 create/rename/set_component/remove_component/set_bindings、完整文档 Undo/Redo、版本检查和权限。

组件存储当前使用按类型字典/装箱，事务使用整场景快照；这是正确性基础，不是已优化的 ECS。
schema 校验仅支持 closed object、number/integer/string/boolean 与 local $defs，不是完整 JSON Schema。
System 热路径目前也有校验/序列化分配，帧预算、类型池与低分配优化尚未验收。
已有 Behaviour 宿主已迁入此 World，`.ncmascene` JSON v1 由 C# 完整读写，旧 .ncscene 兼容代码已删除；运行结构命令与活动编辑器 MCP 已实现基础；通用查询/类型池优化未实现；共享 EditSession 已接入活动编辑场景。

## 当前可调用能力（C# Core + 活动编辑器 MCP）

| 稳定名称 | 风险 | 已实现范围 |
|---|---|---|
| ncma.capabilities.list | read_only | 描述已实现能力及 JSON 输入/输出 schema |
| ncma.engine.component_types | read_only | 注册组件身份、版本与 schema |
| ncma.scene.inspect | read_only | 托管场景 UUID、组件、World 身份与 tick |
| ncma.engine.behaviour_types | read_only | 可信已加载 Behaviour/Export catalog，分页读取、不执行脚本 |
| ncma.scene.object.inspect | read_only | 完整组件/绑定/Export 配置，有界分页 |
| ncma.scene.validate | read_only | 验证当前托管场景，不改状态 |
| ncma.scene.transaction | reversible | 1..128 项基础对象/组件/绑定操作及局部 add_binding/remove_binding/set_binding_enabled/set_export，原子提交 |
| ncma.scene.delete_object | destructive | 显式批准一个对象 UUID，仍可撤销 |
| ncma.history.undo | reversible | 撤销，仍校验原命令权限 |
| ncma.history.redo | reversible | 重做，仍校验原命令及删除目标授权 |

所有返回包含 v2 contractVersion/requestId/sessionId/revision/status/code/changed/data/executionRevision/replayed。
status 为 ok/error/conflict/denied；错误不包含待执行脚本。输入关闭未知/重复字段，组件使用注册 schema。
同一成功请求的精确重试返回原执行结果及当前 history，replayed=true，executionRevision 为原执行版本；不重复执行；缓存上限 128，不承诺无限期 exactly-once。
客户端不能修改旧 requestId 的内容，读取/修改应使用新 requestId。

权限由宿主使用 CapabilityPermissions 显式授予，不能从 AI JSON 内的 permission 字段获得。
默认只读；修改必须提供当前 sessionId 与 expectedRevision。
删除还必须批准实际 persistent UUID，不能用删除命令枚举路径、删除文件或批量清空项目。
撤销/重做不绕过原命令权限，重做删除仍需要同一 UUID 授权。

事务准备独立的完整 SceneDocument 候选；组件验证器只在准备阶段运行，完整内容/历史/结果准备成功后一次提交活动文档。
历史最多 64 项、16 MiB；达到限制丢弃最旧条目，结果报告剩余 undo/redo 数量。
输入最多 64 KiB，组件 payload 最多 64 KiB，World 快照最多 4 MiB，对象最多 4096、每对象最多 64 组件。
事务/Undo 使用全量恢复，所有旧运行时引用失效，必须按 UUID 重新解析。
外部直接 World 修改令已有 EditSession 的历史失效并返回冲突，不覆盖外部修改；必须显式 Resynchronize；不会自动认领外部修改。
固定步内禁止结构/编辑器修改；失败回滚本步组件/结构/绑定/信号消费与发送/回执，不回滚 System 私有状态、IO 等副作用。
Runner fault 后必须显式处理私有状态并 ResetFault；不自动重试，重置清空剩余时间。

## 示例：由宿主批准一次场景创建

```csharp
using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Editor.Core;

var document = new SceneDocument("ActionGame");
var session = new EditSession(document);
var input = JsonSerializer.SerializeToElement(new {
    operations = new[] { new { op = "create", objectId = Guid.NewGuid(), name = "Hero" } }
});
// 这个授权来自用户/编辑器，不是模型自己申请就生效。
var permissions = new CapabilityPermissions(new[] { "ncma.scene.transaction" });
var result = session.Invoke(new CapabilityRequest(
    EditSession.ContractVersion, Guid.NewGuid(), session.SessionId, session.Revision,
    "ncma.scene.transaction", input), permissions);
```

可信 C# 插件可在创建 World 前 Register<T> 新值组件和校验器，通过相同事务设置组件。
AI 不能提交 CLR 类型名、程序集路径、eval/exec 或动态代码作为新组件。
动态插件加载/卸载、生成代码编译与项目文件修改网关均未实现。

## 五个 AI 领域与接入顺序

| 领域 | 目标工作 | 当前状态 / 下一步 |
|---|---|---|
| 场景 | 创建角色、组件、资源引用、参数、Prefab | 共享场景事务已接入 ImGui；活动场景 MCP 已连接相同 Editor.Core；资产引用/Prefab 未实现 |
| 动画 | 图节点/连线、动作片段、过渡、通知、Root Motion 测试 | 隔离原生动画 MCP 已实现；托管图命令、正式 Animator/角色链路未实现 |
| UI | Frame、布局/样式、组件实例、交互、画布预览 | 原生数据模型存在；托管文档/求解/事务网关未实现 |
| 工具 | 资产报告、验证、构建、测试与结构化诊断 | CLI inspect/FBX 报告/动画 MCP 已实现；受约束构建/测试 Agent 网关未实现 |
| 引擎扩展 | 定义组件/schema、System、编辑器面板和插件适配器 | 可信值组件注册已实现；生成代码审查、路径限定文件事务、编译/装载未实现 |

各领域应返回真实能力清单，而不是注册一个看似可用的空实现。
共享场景能力已有 C# stdio → Windows named pipe adapter，默认关闭/只读，用户批准精确提案后共用历史。
见 [EDITOR_MCP.md](EDITOR_MCP.md)。现有 Python stdio 动画 MCP 仍是隔离预览，不与活动场景共享状态。

所有领域使用同一模式：读取/检查 → 有界提案 → 权限与 revision 校验 → 编辑器事务 → 验证 → 结果/撤销。
项目源码修改与场景数据编辑不同：未来代码工具应限制项目相对路径、先呈现 diff、显式批准编译/加载。
不向 AI 开放无约束 shell、任意解释器执行或直接 native 指针；可信扩展本身不等于安全沙箱。

## 引擎职责与迁移顺序

C# 拥有高层 World、组件、System、游戏逻辑、动画控制、编辑器文档/命令和独立网络服务。
C++ 只承接 Renderer、Physics 和经测量必要的数值/导入内核；不新增原生通用框架。
Python 为可选专长模块/插件；开发助手可以直接使用工具协议，不要求游戏安装 Python。
AI 开发助手和运行时 AI 推理服务是不同接口；pythonnet/gRPC/ZeroMQ 仍未实现。

managed Behaviour、托管权威 World 与 `.ncmascene` 完整文件读写已接入编辑器；完整文档命令已由 Editor.Core 接管；交互草稿、UUID 选择恢复、保存指纹和 Play 冻结已接入。M1.3 固定步、输入/插值、运行命令、事务信号与安全重载已接入；M1.4 活动场景 MCP 已接入。
自动验收证据及尚待人工验收见 [M1 交付报告](M1_DELIVERY_REPORT.md)。
切换时保持单一 live 权威，不维护长期双向同步。之后反转 C# 主入口并逐个原生插件化。
动作游戏先打通 FBX 场景角色/GPU 蒙皮/Animator/CharacterMotor，再接动画图命令；UI 文档与画布独立开发。
每个切片通过 Build.bat、headless/原生回归和能力状态检查，不以文档或目录拆分代替运行验收。
