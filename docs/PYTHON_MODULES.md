# 独立 Python AI 与专长模块（目标设计，未实现）

Python 不再是 GameObject 脚本语言。适合承接模型推理、离线训练、数据分析、内容生成与工具自动化。
游戏规则、主循环和 World 权威状态由 C# 持有；模块退出/超时不应让游戏停止推进。
现有 Python CLI、FBX 工具与隔离动画 MCP 保留。ncma_gameplay SDK、CPython 游戏宿主、
游戏脚本示例和编辑器语言入口已删除。Python 仅为特殊模块、插件和工具开发的可选语言。
pythonnet、gRPC、ZeroMQ 均未接入；没有新增这些包、插件加载器或 AI 服务。

## 统一边界

C# Application → PythonModuleAdapter → 版本化观察/请求 → Python module → 结构化结果 →
C# 校验/结果队列 → WorldRunner 在明确 tick 边界应用命令。

适配器应在 C# 定义独立契约：异步请求/取消、健康与能力查询、版本协商、开始/结束会话。
传输类型、Python.Runtime/PyObject、protobuf/ZeroMQ 对象都不进入 GameObject/Component 接口。
训练、推理与内容编辑使用不同能力和权限，不提供通用 eval/exec 或任意文件写入端点。

建议首个操作为 ncma.ai.decide（仅设计名称，不是已注册的 MCP 工具）：

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
| gRPC / 独立 worker | 默认建议：AI 重依赖、可独立部署/重启、请求/流式结果 | protobuf 契约、异步调用、deadline/取消；未实现 |
| pythonnet / 同进程 | 明确可信、规模受控且需要近距离互操作的模块 | GIL、解释器唯一所有者、对象释放；崩溃共享进程；未实现 |
| ZeroMQ / 独立 worker | 有测量依据的批量消息/流式模块 | 应用自定义消息 schema/可靠性/背压/恢复；未实现 |

默认建议不是已安装或最终固定的传输。先跑通一种，使用相同的传输中立契约；不并行引入三个后端。
不宣称某方案无跨边界成本，或 ZeroMQ 必然比 gRPC 更快。

gRPC 使用现代 grpc-dotnet 的 C# 实现，而非旧 Grpc.Core；Python 使用对应 gRPC 服务端。
[官方 C# 指引](https://grpc.io/docs/languages/csharp/)
调用明确 deadline，取消后仍要拒绝迟到结果；取消不自动撤销已经产生的副作用。
[gRPC deadlines](https://grpc.io/docs/guides/deadlines/)
建议本机 worker 默认仅 loopback，带会话认证，不能把本机可信服务直接无认证暴露到公网。
服务异常只影响模块，C# 使用本地回退/上一份仍有效结果，并记录故障；重启策略需有限重试。

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

## 第一条链路的验收（待实现）

1. 无 Python 安装时，C# headless World 与普通单机游戏仍运行。
2. 一个可替换 adapter 与本机 gRPC worker完成批量观察/确定响应，协议版本/大小限制有测试。
3. 慢请求、超时、取消、重复、乱序、过期结果均不阻塞游戏线程或误改新 World。
4. worker 异常/退出、断线与重启可回退；队列溢出有明确拒绝/合并策略和诊断。
5. AI 动作需经过 C# 游戏规则；编辑命令需经过授权事务并可撤销。
6. 测量实际请求大小、采样率、P95/P99 延迟、托管分配与回退频率，再考虑 pythonnet/ZeroMQ。
