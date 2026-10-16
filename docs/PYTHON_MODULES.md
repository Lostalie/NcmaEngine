# 独立 Python AI 与专长模块

2026-10-16：M8.1 纯 C# `Ncma.Modules` 复制契约与有界异步会话已交付；M8.2 增加可选 `Ncma.Modules.Grpc`、精确本地审批的进程 owner 和真实 Python 工具 worker，最终交付状态见 [M8 实施记录](M8_IMPLEMENTATION_PLAN.md)。
默认 Editor/Player 仍不接线该模块，不依赖 Python/gRPC，也未提供模型推理或领域修改权限。
结构合法的提案必须继续通过领域 schema、身份/权限与现有事务审批。
实际协议/部署边界见 [M8.2 契约](M8_2_RUNTIME_CONTRACT.md)。下述运行时 AI 决策、pythonnet/ZeroMQ 与完整领域链路仍为设计目标。

Python 不再是 GameObject 脚本语言。适合承接模型推理、离线训练、数据分析、内容生成与工具自动化。
游戏规则、主循环和 World 权威状态由 C# 持有；模块退出/超时不应让游戏停止推进。
现有 Python CLI、FBX 工具与隔离动画 MCP 保留。ncma_gameplay SDK、CPython 游戏宿主、
游戏脚本示例和编辑器语言入口已删除。Python 仅为特殊模块、插件和工具开发的可选语言。
gRPC 已用于独立工具模块，具有单独锁定依赖；pythonnet、ZeroMQ 和推理服务未实现，没有通用插件加载器。

## 统一边界

C# Application → PythonModuleAdapter → 版本化观察/请求 → Python module → 结构化结果 →
C# 校验/结果队列 → 领域服务重新验证与审批 → 编辑事务或明确 runtime tick 边界应用命令（后半段为后续集成目标，M8.2 不提交领域修改）。

适配器应在 C# 定义独立契约：异步请求/取消、健康与能力查询、版本协商、开始/结束会话。
传输类型、Python.Runtime/PyObject、protobuf/ZeroMQ 对象都不进入 GameObject/Component 接口。
训练、推理与内容编辑使用不同能力和权限，不提供通用 eval/exec 或任意文件写入端点。

M8.2 实际首个操作是 `ncma.tools.text_stats`，只计算确定性文本统计/摘要。以下 `ncma.ai.decide` 是未来运行时 AI 的设计示例，不是已注册的 MCP 工具或 worker 操作：

- 请求：contract_version、request_id、session_id、world_id、world_revision、observation_tick、
  deadline/budget、model/config UUID 与有界 observation 数据。
- 结果：对应身份字段、model/config 版本、status、建议动作及数值参数、诊断码。
- 失败使用明确 status/error，不把 Python traceback 作为可执行指令。
- 仅传可序列化快照/观测、UUID 或协议 ID，不传 C# live World/GameObject、原生地址或组件指针。
- C# 校验 schema/范围/授权动作、请求身份、World/会话代次和结果时效；去重并拒绝过期结果。
- 不给 AI 直接伤害/瞬移/资产删除权。建议动作交给现有游戏规则，不绕过命中/物理/权限校验。

定义请求/响应大小、最大并发、有界队列、背压、重试/幂等与断开语义。
不逐帧逐对象发一个 RPC；按模块采样率批量提交观察，高频运动与战斗在 C# 本地执行，
数学/物理/姿势热点交给原生插件。大数组是否采用共享内存须另测并定义所有权，当前未设计成零拷贝。

## 传输选择

| 方式 | 建议用途 | 约束与状态 |
|---|---|---|
| gRPC / 独立 worker | 当前首个可选工具传输；未来可承接 AI 重依赖 | 已实现本机 HTTP/2 + TLS、严格 UTF-8 JSON 自定义消息（不是 protobuf）、异步调用/deadline/取消及显式启动/恢复；无流式推理或正式宿主领域接线 |
| pythonnet / 同进程 | 明确可信、规模受控且需要近距离互操作的模块 | GIL、解释器唯一所有者、对象释放；崩溃共享进程；未实现 |
| ZeroMQ / 独立 worker | 有测量依据的批量消息/流式模块 | 应用自定义消息 schema/可靠性/背压/恢复；未实现 |

gRPC 库存在不代表用户机器已安装可用 worker；解释器/依赖必须单独准备并精确批准。先跑通一种，使用相同的传输中立契约；不并行引入三个后端。
不宣称某方案无跨边界成本，或 ZeroMQ 必然比 gRPC 更快。

gRPC 使用现代 grpc-dotnet 的 C# 实现，而非旧 Grpc.Core；Python 使用对应 gRPC 服务端。
[官方 C# 指引](https://grpc.io/docs/languages/csharp/)
调用明确 deadline，取消后仍要拒绝迟到结果；取消不自动撤销已经产生的副作用。
[gRPC deadlines](https://grpc.io/docs/guides/deadlines/)
当前 worker 只监听字面 loopback，使用 TLS 身份校验和每实例认证，不能直接暴露到公网。
服务异常撤销当前会话及结果；当前不隐式使用旧结果或自动重试。最多三次明确批准启动，每次新身份且不重放请求；关闭未收敛保留进程和文件 pin，恢复另行审批。可信进程不是 OS 沙箱。

pythonnet 必须由一个适配器负责 PythonEngine 初始化、Py.GIL()/调用和 PyObject 释放。
旧 C++ Python 游戏宿主已删除；不允许多个适配器管理同一解释器，不把模块重载等同于解释器热重建。
在专用工作通道执行推理，禁止持有 World 锁时等待 GIL、也禁止持有 GIL 同步回调游戏线程。
仅可信模块可用；同进程不是沙箱，原生扩展崩溃仍可能带走游戏进程。
[pythonnet 嵌入文档](https://pythonnet.github.io/pythonnet/dotnet.html)
硬超时无法保证安全强制中止同进程 Python/native 推理，需要强隔离时应选 worker 模式。

ZeroMQ C# 可选 NetMQ，Python 可用对应 ZeroMQ 绑定；具体版本与工程尚未选定。
它提供消息模式，不取代版本化应用协议、请求关联/重复处理、时效校验和恢复规则。
[官方 C# 绑定指引](https://zeromq.org/languages/csharp/)

## 生命周期与安全

C# owns 模块会话：开始 → 初始化/协商 → 发观察 → 收结果入队 → 校验/提交 → 取消 → 释放。
换场景/停止 Play 时先取消旧请求、失效会话身份再释放 World；结果迟到不得影响新场景。
模块创建的子进程、模型和缓存有明确释放责任；后台线程不能直接操作世界/编辑器。
进程隔离不等于权限沙箱，文件目录、网络、模型下载和资源上限需单独配置。
Agent/MCP 编辑仍走 C# 编辑器命令/Undo（目标），不因为 Python 独立就开放任意代码执行。
游戏 AI IPC 与游戏联机网络分别设计，不给组件添加 RPC/网络角色。

## 完整链路验收目标（逐项证据见 M8.2 契约，不能整体标为已通过）

1. 无 Python 安装时，C# headless World 与普通单机游戏仍运行。
2. 一个可替换 adapter 与本机 gRPC worker完成批量观察/确定响应，协议版本/大小限制有测试。
3. 慢请求、超时、取消、重复、乱序、过期结果均不阻塞游戏线程或误改新 World。
4. worker 异常/退出、断线与重启可回退；队列溢出有明确拒绝/合并策略和诊断。
5. AI 动作需经过 C# 游戏规则；编辑命令需经过授权事务并可撤销。
6. 测量实际请求大小、采样率、P95/P99 延迟、托管分配与回退频率，再考虑 pythonnet/ZeroMQ。
