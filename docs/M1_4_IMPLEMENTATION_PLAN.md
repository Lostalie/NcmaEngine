# M1.4 详细实施方案：活动编辑器本地 MCP

更新日期：2026-10-04。状态：代码与自动流程已交付；第 1–13 节保留原方案，第 14 节为交付记录。人工 UI/第三方客户端验收待完成。

前置条件：M1.3 全链路验收通过。实施顺序：A → B → 只读完整门禁 → C → D → E → F。导航：[M1 剩余总览](M1_REMAINING_IMPLEMENTATION_PLAN.md)、[M1.3](M1_3_IMPLEMENTATION_PLAN.md)、[最终验收](M1_ACCEPTANCE_PLAN.md)。

## 1. 目标与当前能力差异

目标：让 AI 客户端检查并受控修改当前 ImGui 编辑器的真实编辑文档，与用户共享唯一 Editor.Core 命令、revision、Undo/Redo 和草稿竞争规则。

当前已有：

- Ncma.Editor.Core 的 8 项能力、JSON Schema、风险、v2 信封、默认只读、版本/会话检查、128 项成功请求缓存与完整文档历史。
- managed/Ncma.Managed.Host/EditorEntry.cs 的 operation 31，只调用只读能力；UI 的其他操作是可信宿主入口，不能直接赋予 Agent。
- Python 的独立动画 stdio MCP；每个进程拥有自己的预览会话，不是活动编辑器。
- ncma.scene.inspect 当前只输出对象摘要、组件身份和绑定摘要，不包含完整组件/Export 值。
- CapabilityPermissions 当前只有能力名和破坏性 UUID 授权，不具备普通事务全目标范围、连接身份或过期时间；这些必须新增，不能误称现成安全机制。

本阶段不是运行时 AI 推理模块，不引入 Python Gameplay、外网服务、自动源码改写、shell/eval、资产导入或场景文件删除工具。

## 2. 推荐拓扑与职责

~~~text
AI/MCP 客户端
    ↕ MCP stdio（UTF-8 JSON-RPC）
Ncma.Editor.Mcp（建议新增 C# 辅助进程）
    ↕ Windows 本地 named pipe（私有 IPC v1）
Ncma.Editor.Transport（建议新增纯托管连接/队列模块）
    → owner-thread 有界意图队列
Ncma.Managed.Host → 当前 Ncma.Editor.Core.EditSession
    → 同一 SceneDocument / World / Undo 历史
C++/ImGui → 在安全边界泵送 + 连接/授权/结果界面
~~~

- MCP 进程只做协议、Schema 映射和 IPC，不创建自己的 EditSession，不加载 NcmaNative 另建场景。
- Transport 的 IO 线程只做有界解析/认证/入队/发送复制结果；不能读 EditState、World 或执行验证器。
- 唯一实际 Invoke 在 owner thread；不让主线程等待辅助进程响应。
- 管理授权/连接的 UI 仍在 ImGui，业务规则在 C#。
- Transport 不依赖 D3D/Vulkan；协议模型可由新 Ncma.Editor.Protocol 项目共享，具体拆分以避免循环依赖为准。
- 保留当前 C++ 主入口。一个 C# stdio 辅助进程不等于 M2 的 C# Editor/Player 迁移完成。
- 原独立动画 MCP 保留原边界与名称，不借本阶段悄悄连接 live World。

## 3. 本地端点、身份和连接安全

### 3.1 启用与发现

默认关闭外部接入；用户在编辑器启用后建立随机名称的本地管道，不监听 TCP、不开放公共 HTTP。Windows 管道仅允许当前用户、拒绝远程客户端；创建时防管道名抢占，失败明确报错。

每个编辑器实例独立 EditorInstanceId、启动 generation、规范化项目根标识与端点。辅助进程必须指定目标实例/项目，不自动连接“最后一个活跃编辑器”。

发现描述文件可放在 out/sessions/ 下，内容只含管道名、实例/项目标识、IPC 版本与非秘密状态，不含访问凭证。descriptor 不能授予权限；陈旧描述需实际握手确认，不凭 PID 存活就认定正确会话。

首次连接经编辑器确认配对，宿主生成不可猜测的短期连接凭证，通过受限的配对通道传递。凭证不放普通命令行、工具结果、日志或可读发现文件；客户端无法自报为可信 UI。

本地同用户 ACL 与配对不是恶意同用户代码的安全沙箱。威胁模型为防意外越权、陈旧连接和错误路由，不宣称可以隔离掌握用户账户权限的攻击者。实施前测试 ACL/远程拒绝/配对过程，不能为了连接方便退化成匿名可写。

### 3.2 多种会话身份

- EditorInstanceId：目标编辑器进程实例。
- ConnectionId/GrantEpoch：认证连接与授权代数。
- EditSessionId：现有编辑命令会话；每次调用必须精确匹配。
- DocumentGeneration：宿主的文档替换代数；New/Open/整文档替换后使旧范围授权失效，即使 EditSessionId 保持不变。
- PlaySessionId：M1.3 运行副本身份，不能代替编辑会话，也不接受其句柄作为编辑目标。
- MCP JSON-RPC id 与业务 requestId 分开；重连可使用新的 RPC id 查询同一业务请求，不得产生新业务写请求掩盖不确定结果。

New/Open 是既有可撤销操作，不强迫修改 M1.2 的 EditSessionId 语义。DocumentGeneration 与基准 revision 联合防止旧授权误作用于被替换内容。

## 4. 能力与 Schema 设计

### 4.1 保留现有 8 项能力

| 名称 | 风险 | M1.4 接线规则 |
| --- | --- | --- |
| ncma.capabilities.list | read_only | 来自当前 Editor.Core 的真实描述，不宣称计划能力 |
| ncma.engine.component_types | read_only | 可信注册 typeId/version/schema |
| ncma.scene.inspect | read_only | 现有分页摘要；带当前 session/revision |
| ncma.scene.validate | read_only | 当前提交文档校验，不执行游戏脚本 |
| ncma.scene.transaction | reversible | 同一 Core 的完整候选、一次原子提交/Undo |
| ncma.scene.delete_object | destructive | 精确单 UUID、用户批准、可撤销 |
| ncma.history.undo | reversible | 校验历史原始能力、范围、风险与删除授权 |
| ncma.history.redo | reversible | 同上；不能借 Redo 绕过已撤销权限 |

MCP tools/list 的 inputSchema 包装业务信封，固定 capability 为工具名称；工具输入为 contractVersion、requestId、sessionId、expectedRevision、input。调用由适配器构造 Core 信封，拒绝名称/信封 capability 不一致，不能允许客户端指定另一个内部操作编号。

保留 Core v2 输出字段：contractVersion、requestId、sessionId、revision、status、code、changed、data、executionRevision、replayed。MCP 输出同时提供结构化结果与可兼容的 JSON 文本；成功和错误都有确定结果，Schema 不应只声明 data 是任意空对象而隐藏关键类型。

状态、历史、对象分页结果应有具体 data Schema；支持的 JSON Schema 是协议描述，不表示当前组件验证器已经实现完整 JSON Schema 标准。

### 4.2 新增必要只读详情能力（建议）

新稳定名 ncma.scene.object.inspect，风险 read_only，避免改变现有 summary 输出语义。

input 为 closed object，至少包含 objectId；section 枚举为 summary/components/bindings/exports，分页 offset/limit；exports 必须给 bindingId。建议默认 limit 16、最大 64，expectedRevision 固定本次分页基准。

data 至少包含 objectId、section、totalCount、offset、returnedCount、nextOffset、complete，及对应具体数组：

- summary：对象名称、存在性/身份、组件/绑定数量；不泄露运行句柄。
- components：typeId/version 与完整注册 data；一个组件不可半截 JSON 输出。
- bindings：绑定 UUID、可信 typeName、enabled、exportCount，不混入私有脚本字段。
- exports：指定绑定的 name/kind/value 分页，不执行 getter/脚本来获取配置值。

输出按 UTF-8 字节预算截短“条目数量”，不截断字符串/JSON；returnedCount 可少于 limit，以 nextOffset 续读。若单条超过输出上限，返回 item_too_large，不假装 complete。跨页 revision 不同返回冲突，用户重新读取，不拼接两版对象。

建议新增 ncma.engine.behaviour_types，只读，提供可信加载 catalog 的稳定类型名、合法 Export 描述与 catalog generation；分页，不加载客户端提供的程序集、不执行任意构造器。查询无可用 catalog 时明确返回不可用状态。

新能力仅在实现与测试后进入注册表和 tools/list；不先发布占位工具。

### 4.3 有界脚本配置修改

既有 set_bindings 为全量替换，一个对象可有大量绑定，合法文档可能无法在 64 KiB 输入限制内完整回传。M1.4-C 在同一个 ncma.scene.transaction 增加局部配置操作，避免 Agent 为改一个 Export 覆盖未知配置：

- add_binding：objectId 与完整的新 binding；拒绝重复绑定身份。
- remove_binding：objectId、bindingId；只移除该配置。
- set_binding_enabled：objectId、bindingId、enabled。
- set_export：objectId、bindingId、name、kind、value；必须存在且符合可信 catalog 的 Export，不能改私有成员。

全部仍先构造完整候选、统一校验、一次 Undo；不是新旁路或独立脚本 API。新增 variants 同步修改 closed schema/测试，保留现有操作含义；若实际改变现有信封/结果语义，必须递增 Core 契约版本并同步所有调用者，不能假称兼容。

未解析脚本类型可检查/解绑；新增/修改 Export 需要可信 catalog 预检，不能猜测类型。Core 接收可信纯数据 catalog/验证接口，不依赖 Host、反射执行器或 Gameplay。不是任意 CLR 类型注册能力。

## 5. MCP 与私有 IPC 契约

MCP 首版以仓库已有 2025-11-25 为明确支持版本，不声称这是最新版本或支持所有客户端。按规范进行版本协商，仅声明已实现 tools；不声明 resources/prompts/sampling/tasks。握手后才接收工具调用，日志写 stderr，stdout 仅为协议消息。

私有 IPC 与 MCP 分开：长度前缀、UTF-8、有界 request/response、IPC version 1。握手校验协议版本/项目/实例/配对，具体消息名建议为 Hello、InvokeCapability、GetRequestStatus、Close；授权只能由可信 host 控制路径更新，不存在 Agent 自授 Grant 消息。

预算建议：

| 项目 | 上限/默认 | 规则 |
| --- | --- | --- |
| MCP stdio / IPC 单消息 | 1 MiB | 达到限制在解析前拒绝；不无限 ReadLine |
| Core capability 输入 | 64 KiB | 保留既有约束，外层较大不等于允许更大业务输入 |
| 单次只读业务输出 | 256 KiB | 分页条目；必须留足 MCP 外层编码开销并核验最终消息上限 |
| 活动连接 | 4 | 用户显式连接，超额拒绝 |
| 等待执行队列 | 总 64、每连接 16 | 入队前预留容量，按连接公平调度 |
| 每帧执行起始预算 | 最多 4 请求、软预算 2 ms | 不抢占一次 Core Invoke；记录真实耗时，不保证硬实时 |
| 排队等待期限 | 默认 5 秒、可配置硬上限 | 执行前过期不修改状态，执行后不能假称取消成功 |
| 成功请求缓存 | 沿用 Core 128 | 不创建另一个无限缓存来宣称永久去重 |
| 审计日志 | 建议轮转 4 个 1 MiB 文件 | 位于 out/logs/，不记录秘密/完整场景 payload |

同时限制 JSON 深度、字符串长度、数组元素数、stdio 未完成帧缓冲总量与连接写缓冲。未知/重复业务字段拒绝；协议扩展字段按已支持版本处理，不把业务的 closed schema 规则错误套到所有协议扩展。

预算在协议 fixture 和负载测试后锁定；配置值必须有硬上限，不能通过用户输入设为无限。

## 6. Owner-thread 调度与编辑竞争

IO 线程入队只带不可变输入、身份与截止时间。执行前在 owner thread 再检查连接、GrantEpoch、DocumentGeneration、会话、revision、范围、草稿与冻结状态；入队校验不能替代执行前校验。

编辑器泵送位于固定步/文档安装/草稿提交之外的安全位置。先保证窗口与游戏循环进度，再做有界 Agent 工作；不能在持有 live World 更新锁时等待客户端。生命周期/文档替换过程中不执行能力。

- 人工草稿期间，读取只看已提交文档，写返回 edit_busy，不偷偷提交/取消用户草稿。
- Play/Paused/Faulted 期间，编辑文档仍保持冻结；读取可用，写入返回 play_frozen。Stop 后需用户重新启用写权限，不重放暂停期间积压的写入。
- 开始 Play 时撤销写 grant、清空尚未执行写请求；只读连接可保留。运行态的 MCP 观测/调试工具本阶段不开放。
- 用户编辑抢先提交，Agent 请求 expectedRevision 不匹配，返回 revision_conflict，不自动覆盖/重写请求。
- 外部绕过 Core 的修改触发 history_invalidated；不通过 Agent 自动 Resynchronize。
- New/Open/文档替换、关闭或端点重建使旧授权/队列无效。Undo 恢复文档内容也不能恢复已撤销权限。

## 7. 用户授权、范围与历史安全

### 7.1 授权模型

建议新增 host-owned GrantPolicy，并扩展 Core 的可信授权检查/历史元数据。Grant 包含连接身份、能力白名单、目标 UUID 范围、允许的新对象 UUID、可选组件/绑定范围、有效期、generation 与删除批准。

用户在编辑器确认后才能写；可选择单次精确提案或短期指定范围授权。全编辑文档可逆授权必须显式选择，不是默认。输入中的 permission、approved、文件路径或 systemPrompt 都不能增加权限。

普通 transaction 也要检查每个目标，包括 create 的新 UUID 与 selection 变化；多操作跨范围时整批拒绝，不仅校验第一条。非修改的读取可按连接读取范围限制，Schema 与审计明确范围。

授权失效后排队/缓存重试/Undo/Redo 都重新检查；无需等待连接断开。校验在 candidate install 前最后安全点完成，不调用不可信异步授权回调。

### 7.2 删除

删除只通过 ncma.scene.delete_object，精确一个 persistent UUID。UI 显示对象名称、UUID、当前 revision 和影响摘要，用户显式批准；不允许 *、目录、查询表达式或“场景所有对象”。

首版批准绑定 ConnectionId、EditSessionId、DocumentGeneration、objectId、expectedRevision 和提案摘要；删除成功后对应批准可保留在该文档版本链的历史风险记录，直至过期/撤销/替换，以支持精确重试与获批的历史操作。用户如果选择单次授权，重做需重新批准；两者在 UI 明确区分。

事务里不新增隐藏 delete；移除 Behaviour 配置仍为 reversible，并按对象/绑定范围授权，不等于删除项目文件。

### 7.3 Undo/Redo 不能绕过边界

现有 Entry 只记录 capability 和单 destructive Target，不足以表达普通事务多 UUID 范围，也不足以识别 New/Open 的文档替换风险。本阶段需扩充可信历史记录：

- 保存归一化后的实际影响 UUID/类型范围、原始风险与是否文档/文件关联替换。
- Undo/Redo 检查将改变的对象与原命令风险，而不只检查 history.undo 权限。
- Agent 默认只能操作当前获得范围授权的普通场景条目。人工产生的条目也须重新审核，不能视为天然已获 Agent 许可。
- New/Open/整文档或文件关联替换的历史条目，本阶段禁止 Agent Undo/Redo；用户仍可通过可信 UI 操作。
- 删除的 Undo 可恢复对象，但仍按保守规则校验原始风险/目标批准；Redo 删除需要仍有效或重新给出的批准。
- 授权范围涉及多个对象而历史顶端条目越界，返回明确 denied，不能跳过栈顶撤销另一个条目。

UI 可信权限与 Agent 权限不能共用 UserPermissions 自动批准函数；不能将 HistoryTarget 的查询结果直接当成人类批准。

## 8. 幂等、超时、取消与断线

- 每个连接的一次业务请求绑定原始 v2 requestId；Core 的缓存仍是唯一成功执行去重源。
- 精确重试先重查当前授权，命中缓存返回 replayed=true 与 executionRevision；revision 是当前值，不能冒充原执行时整个文档仍在。
- 不同 payload/expectedRevision 复用同 requestId 返回 request_id_reused；RPC id 相同/不同不是业务去重依据。
- 同一连接 pending 队列也按 requestId 去重，避免第一次执行未完成时第二次被并行入队；不同连接不能读取对方原始执行详情，连接的 requestId 命名域/归属在 host 可信元数据中检查。
- 跨连接碰撞或恶意复用已有 requestId 拒绝；同连接恢复凭证可查询本连接旧请求，不能靠伪造 clientInfo 认领归属。
- 未开始执行的超时/取消/断连请求标为 not_executed 并移除；安装已完成则 committed，无法通过断连自动 Undo。
- 已执行但响应丢失，客户端不得用新 requestId 再次创建/删除。使用同 requestId 与恢复身份查询/重试。
- 辅助进程与 host 可维护有界 pending/outcome 路由表，但不得独立重执行命令；若 Core 结果已淘汰，返回 outcome_unknown，要求 inspect/用户确认。
- 进程崩溃、缓存淘汰、会话替换后不提供永久 exactly-once 或持久事务日志保证。
- Cancel 通知只请求停止尚未执行工作；不允许外部线程中断正在安装的 Core 事务。
- 宿主关闭先停止接收，拒绝待执行写入，再释放队列、pipe 和连接；不扫描/递归删除整个 out/sessions，只处理本实例拥有的精确描述文件。

## 9. 结果、诊断与审计

业务 status/code 保持 Core 原始含义；协议格式错误使用 JSON-RPC error，Core 的 conflict/denied/error 使用 MCP 工具执行失败结果。不要用“连接成功”包装场景事务失败。

新增 transport 错误码建议包括 endpoint_unavailable、ipc_version_mismatch、pairing_required、queue_full、request_expired、connection_revoked、document_generation_mismatch、outcome_unknown。新增 Core 权限/读取码建议 scope_denied、history_scope_denied、object_not_found、item_too_large。实施时统一注册 Schema 与测试，不假称这些现在可调用。

成功/失败审计至少包含时间、连接/会话/generation、requestId、能力、风险、批准身份、目标摘要、基准/执行/当前 revision、耗时、changed/replayed 与稳定错误码。日志不含凭证、完整文件路径、私有脚本状态或原始大 payload。

为了响应断线后的明确结果，结果/回执必须在 Core install 前准备；序列化/IPC 写失败不能被误报为“事务没有提交”。

## 10. 六个小阶段

### M1.4-A：本地连接与安全队列

任务：IPC v1 共享 DTO、named pipe 端点、配对与实例路由、容量/期限/owner-thread 队列、关闭顺序；只读诊断先行，不启用修改。

测试：真实 pipe 连接、多个实例不串线、协议/项目不匹配、远程/未配对拒绝、队列饱和/过期/异线程、端点关闭、不残留资源。

交付：Transport/Protocol 测试项目、Host 薄接入、安全边界泵送。不得用第二 EditSession 模拟 live 连接通过。

### M1.4-B：stdio MCP 与只读详情

任务：C# stdio helper、握手、tools/list/tools/call/ping、现有只读能力接线、新对象详情/脚本 catalog、具体输出 Schema 和分页。

测试：真实辅助进程初始化；stdout 纯协议；输出 Schema 验证；页间版本冲突；完整组件/Export 读取；未知/重复业务字段、大消息、无效 UTF-8、未知方法、握手前调用、EOF。

门禁：A+B 完成后运行完整 Debug 验证和真实编辑器只读 smoke。工具名称可以描述未来已注册的写能力，但此时所有外部修改必须返回 denied；不拿只读通过作为可写服务已实现证据。

### M1.4-C：受控事务与历史

任务：可信 grant/范围模型；局部绑定操作；历史影响与文档替换标记；UI 批准；transaction/delete/Undo/Redo 进入同一 Core；只对已批准请求开放。

测试：先 inspect → 批准创建/设置 Transform/挂 C# binding/改 Export → validate → UI Undo → MCP Redo 的同历史闭环；错范围/绑定/类型拒绝；删除精确 UUID；历史顶端人工/New/Open 条目不能被越权恢复；缓存重试与撤权重新检查。

门禁：权限与共享 Undo 用例全通过之后，才允许常规外部写接入。

### M1.4-D：并发与故障恢复

任务：连接公平队列、pending 去重/归属、结果恢复、超时取消/响应丢失、版本冲突、Play/草稿/文档替换竞争。

测试：双客户端同 revision 一个成功一个冲突；人工提交领先；执行前取消零变更；执行后断线一次提交；缓存淘汰 outcome_unknown；撤权排队失败；Play 读可用写冻结；主循环不等待 IPC；资源与缓冲始终有界。

### M1.4-E：编辑器可见性与客户端接入

任务：ImGui 连接/授权/撤权/待审提案/风险确认/审计摘要；端点实例选择；只读启动配置示例与恢复说明。

建议新文件 engine/config/editor-mcp.example.json 与 docs/EDITOR_MCP.md，只在实现后生成真实可运行示例；与 animation-mcp.example.json 明确区分。辅助程序/运行库输出遵守 out/ 约定，主编辑器仍为 out/bin/NcmaEngine.exe。

至少测试一个真实支持选定协议版本的客户端；记录其版本/配置与人工闭环。不得自动安装用户客户端配置、默认授予写入或声称所有模型客户端兼容。自动测试仍不依赖这个客户端/LLM。

### M1.4-F：协议、安全与负载交付

任务：合并所有自动/人工证据、对抗输入、边界预算、队列公平/耗时/分配记录、manifest/docs 真实状态、原动画 MCP 回归。

测试：只读默认、全套授权负例、真实 stdio → pipe → 当前 EditSession 的进程链、关闭/reconnect/reload/Play、重复请求、上限、日志脱敏。无外网/模型 key 也可运行。

完成条件：真实活动场景共享命令、可撤销、确定错误与安全验收都通过，才声明 live 场景 MCP 已实现；动画图/UI/资产/源码扩展工具仍标记未实现。

## 11. 文件与版本影响清单

| 文件/模块 | 计划处理 |
| --- | --- |
| managed/Ncma.Editor.Protocol/（建议新增） | 私有 IPC DTO/版本、Schema 映射与复制消息；无场景存储 |
| managed/Ncma.Editor.Transport/（建议新增） | 本地端点、配对/连接、授权、队列、公平调度、诊断 |
| managed/Ncma.Editor.Mcp/（建议新增） | stdio 辅助程序；无原生 DLL/场景加载器 |
| 对应新增 Tests 项目 | 真实子进程/管道与无窗口队列契约回归 |
| managed/Ncma.Editor.Core/Contracts.cs、Capabilities.cs、EditSession.cs | 目标范围、可信 catalog、对象详情、局部配置命令、历史风险与缓存归属所需元数据 |
| managed/Ncma.Managed.Host/EditorEntry.cs、SceneEntry.cs | 活动 session 路由、安全边界执行与授权控制；operation 31 保持只读，不直接扩成匿名可写 |
| engine/source/runtime/scene/ManagedSceneClient.* | 必要的泵送/可信授权/UI 状态桥接，不暴露 World 指针 |
| engine/source/editor/EditorApplication.cpp | 连接/提案/风险/授权界面与非阻塞安全泵送 |
| CMakeLists.txt、scripts/Build.ps1、测试工程 | helper 部署、新测试与真实 live smoke |
| engine/config/、docs/、python/src/ncma_tools/manifest.py | 示例/说明/真实能力状态；保留 Python 独立动画接口 |

Scene Host Bridge 若新增可信操作，在 M1.3 的 5 基础上递增；Gameplay Bridge 4、native ABI 2、场景 JSON v1 不因 MCP 改变。Core v2 优先保持信封与已有操作兼容；新增能力/操作与 Schema 一起发布，破坏性变化单独递增。

版本不匹配明确失败；不通过 sniffing 自动把旧 native/旧场景/旧 Agent 输入当作新版本。

## 12. 全阶段验收与后续扩展

M1.4 通过条件：

- 服务连接当前编辑器而非独立场景；Agent 与人工共享一个 Undo/Redo 和 revision。
- Agent 默认只读；范围/风险/删除/历史/缓存重试都重新验证可信授权。
- 无外线程 live 模型访问、无无限队列、无同步等待 AI 阻塞游戏循环。
- 组件/脚本配置可有界读取与精确修改，不覆盖未知成员。
- 多客户端、草稿、Play、重载、文档替换、断线/取消产生可测试确定结果。
- stdout/日志/发现文件不泄露凭证或污染协议；关闭释放自有资源。
- Build.bat 完整 Debug/Release 验收和原独立动画 MCP/Python 回归通过。
- 后续资产、动画图、UI、源码扩展注册各自真实文档命令，不借本阶段发布空实现；运行时 AI 与 Python 通信另属 M8。

## 13. 协议参考

本方案的 stdio framing、初始化协商和工具结果遵循下列官方规范；named pipe、连接配对、授权与队列预算是 NcmaEngine 的本地工程设计，并非 MCP 标准要求：

- [MCP 2025-11-25 transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)：stdio 采用换行分隔 UTF-8 JSON-RPC，非协议日志送 stderr。
- [MCP 2025-11-25 lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle)：初始化协商协议/能力，随后进入运行阶段。
- [MCP 2025-11-25 tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)：工具描述、输入/输出 Schema 与结构化结果；annotations 不是授权机制。


## 14. 本次实际交付与验收边界（2026-10-04）

A/B：Protocol DTO、当前用户 ACL/拒绝远程/first-instance 管道、端点选择、默认关闭/只读配对、有界 owner-thread pump；
C# stdio helper 不依赖 Core/Scene/native/Python。新增对象详情与可信 Behaviour catalog，10 项 v2 能力有具体 output Schema。
G3 完整 Debug 14/14 CTest、24 Python 通过，包含真实 stdio → pipe → 活动 ImGui EditSession 只读 smoke。

C：精确提案授权 60s，每连接一份，最多 16 提案；完整 input 与所有对象/创建 UUID/组件/绑定展示，删除输入精确 UUID；
可选仅范围内历史。新增局部绑定操作；历史记录实际及意图范围/文档替换，缓存/Undo/Redo/安装前重新检查。
Core 36/36；G4 全 Debug 14/14，out/verification/m1/g4-debug.log。不是开通匿名全场景写入。

D：每连接 reader/有界 writer 分离，关联 CallId；pending 同 ID 同内容合并，改内容拒绝；
成功 Core cache 128，Transport 所有权 256 + 保守 64 KiB 墓碑，不重新执行淘汰请求。
断线/取消/5 秒过期只取消队列，不能回滚已提交；stdio notifications/cancelled 并发读取。
四连接 64 排队、双写版本冲突、用户领先、跨连接缓存归属、取消/过期/断线、Play 撤权/读可用、文档 generation 通过。
G5 Debug 14/14 与 24 Python 通过，out/verification/m1/g5-debug.log。

E：ImGui 配对/提案完整范围与风险/授权/撤权/60 秒余量/审计摘要、实例描述路径复制、配置模板与 EDITOR_MCP.md 已交付。
**第三方客户端名称/版本与真正人工 UI 闭环未验收**；自动 deterministic MCP client 不是第三方兼容证据，不提前标记 E 完整结束。

F 自动部分：真实编辑器批准写入 → trusted UI Undo → MCP Redo → validate；默认写拒绝；
闭对象/重复字段/UTF8/深度/长度对抗测试、具体输出 schema 测试、65th queue_full、容量/公平 pump 时间、端到端延迟/分配、32 次连接回收/句柄基线、脱敏有界审计；
Build.bat 与 solution/deploy/CMake/manifest/docs 已接线。完整 Debug/Release 最终结果见 M1_DELIVERY_REPORT.md。

预算：4 连接/合计 64/每连接 16、每帧 4/软 2ms、排队 5s、审批 60s、IPC/stdio 1MiB、
业务 64KiB、输出 256KiB、审计 4×约1MiB。软预算不抢占单次完整文档校验，实测峰值单独记录。
只批准已加载可信脚本配置；无任意加载/代码执行/文件事务网关，不改变独立动画 MCP。
实际版本：Scene 6、Gameplay 5、Core 2、IPC 1、manifest 10，native2/animation1/character1/JSONscene1不变。

与原建议差异：不提供用户容易误批的长期整场景通配 grant；先使用单提案 + 可选精确历史范围。
恢复授权只作用同一配对连接的精确请求；helper 不自动重连/重试，未知结果需人工检查。
M1 未在缺少人工证据时被宣布完成；不进入 M2。
