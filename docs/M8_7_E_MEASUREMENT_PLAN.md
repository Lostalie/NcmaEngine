# M8.7 E 测量与长稳准备

2026-11-13 E2-E 第三片：显式独立候选 CPU 空闲入口、当前入口路径核对、请求/证据编解码及新文件导出已实现，捕获5d14cd54d0d14acb870c5799724bf03a的合同与AI版实际Editor依赖图双配置四次编译均0警告/错误。新增12组用例待F；本轮未运行入口、人工确认、模型、采样或导出。它是现有CPU预览窗口的基线诊断，不是完整编辑器/大世界/AI负载或性能验收。以下各片为历史记录。

2026-11-13 E2-E 第二片：已加入原应用循环的显式有界窗口与 `MeasurementRunDriver.RunAndClose`，修正后捕获 fc80164e0e644b2bb531d0cb695fda0b 的合同及实际Editor依赖图 Debug/Release 四次编译均0警告/错误，10组新增用例待F。驱动仅接收已启动的可信宿主，不启动EXE、装载负载、调用模型或启用正式Program。输入租约不是实际已装载产品/负载证明；独立候选入口、真实负载与审批绑定及完整导出仍待补。以下首片说明保留为历史记录。

2026-11-13，E2-E 首片已补可信本地产品/负载只读输入租约，保留原包校验并支持同句柄及精确文件集复核；尚未实际执行该入口。捕获清单只核对清单自身，不验证全部源码或已装载进程，回执不批准负载/模型/运行。**功能测试未运行，采样/导出未执行，性能与长稳未验收**。当前捕获 `16bc41d58b58417e8c443dcd43f3af31`，source `9C941F9BB62CC7EA447D3C20987136BC409FDD5F6867E0F239EE705AF66A1A95`；相关合同依赖图双配置编译0警告/错误，新增10组用例待F。下一步将显式输入租约与原宿主循环、停止/关闭和封存关联的受控驱动；正式Program仍不启用。固定整阶段输入仍b69，F前匹配全套产品与冻结，不授予外发/安装权限。

## 1 比较矩阵

同机器、同场景资产与操作序列、同构建配置、分辨率和帧率策略下，分别保留以下结果，不把不同业务工作量混成一个速度排名：

- 默认产品，无可选模块：测编辑交互/帧循环基线；没有 AI 请求，不填“请求延迟 0”。
- AI＋扩展产品空闲：未配置、无请求；衡量可选模块装配成本。
- 本地 Python worker：使用原 IPC 与固定合法任务，只证明该任务链；不是本地大模型推理速度。
- 本地模型 HTTP 与远程 DeepSeek HTTP：分别测量；同模型/参数/输入才可做模型部署方式对照。不同模型只能分列描述，不能把差异归因于引擎。
- AI＋扩展共同活动：沿原审阅、批准、提交或取消、正常关闭流程；不得用循环自动确认代替人工许可。

Debug 用于合同和诊断，Release 用于正式比较；不合并分位数。真实模型的接收方、内容、次数/费用或本地资源预算仍须单独确认。用户已要求代表性大世界负载稍后手动确认，本阶段不代选四对象夹具作为目标游戏负载。

## 2 指标与可观测边界

具体源码锚点在 [机器可读计划](M8_7_E_METRICS.json)。每个值必须注明区间、线程/进程和覆盖率。

| 指标 | 现有来源 | 正确解释与待补项 |
| --- | --- | --- |
| 请求/响应字节、状态、token | `AiCallDiagnostic`、显式 `AiTransportObservations` | UTF-8请求体长度和实际读入响应体字节，不是网络/TLS总流量；HTTP错误不读响应体，0不能解释为服务器返回空体。usage缺失为null，不推算费用。默认仅最近诊断；显式历史与领域数值输出/字节读取已实现，受控实际采集/归档未执行。 |
| 适配器耗时 | `ElapsedMilliseconds` | 从 InvokeAsync 进入到 finally 记录；含拒绝/准备/IO/解码和清理，不含之前排队或之后领域审阅/提交，不称纯 RPC 或模型耗时。 |
| 端到端耗时 | 领域 request/review/receipt 身份 | 需同一请求的单调时间戳：入队→可审阅结果、明确提交调用→原回执、取消→实际排空。人工思考等待另列；缺阶段不得用适配器时间补齐。跨进程只用同进程起止测往返，不相减不同进程时钟。 |
| 背压与生命周期 | `ModuleAdmission`、`RetainedCount`、`ActiveCount` | Retained 含待交付/待排空；Active 是已有 Work 的条目，可能已完成，不等于 CPU 正在执行。两数相减不能冒充准确队列深度；准确排队驻留需生命周期事件。保留 16 outstanding、4096 lifetime 等原预算。 |
| 调度任务耗时/分配 | `ScheduleTaskReport` | Bytes 是预算计费，不是堆大小；AllocatedBytes 仅同步回调同线程，不含上下文、合并、报告、子线程、原生/GPU。不得外推为整帧 0B。 |
| 循环/Present 间隔 | `LoopCostCapture`、`LoopCostStream` | 512 前缀不能算完整长稳尾延迟；使用完整流和最终宿主关闭报告。Present 返回不是屏幕显示完成；启动和最终关闭另测。 |
| 进程/GC与系统资源 | `ProcessMemoryCapture`、`ProcessSeriesCapture`、`ProcessMeasurement` | 原四检查点不变；原循环支持可信宿主显式传入owner采样对象，正式Program未开启，不是独立后台观察器。读取非原子、最近GC信息可能过期；累计CPU不是CPU百分比，内存不是原生/GPU独占驻留。纯字节读取及预期标签已补，实际运行编排、领域联合消费与泄漏阈值待补。 |
| 关闭与资源保留 | AI status、扩展内层与 OuterRetained、原宿主退出 | 取消提出、结果终态、传输排空和所有者释放分别记录。D11 日志不能代替 case 清理后的终态；内层0不证明外层已关闭。 |

不得在 async await 两端相减 `GetAllocatedBytesForCurrentThread` 后称请求分配；如需进程分配，明确其包含其他线程/观察器并保留 GC 信息。Find/UTF-8 的获批 NoGCRegion 仅属于原微测试，不扩展到本方案。

## 3 统计合同

请求以唯一 run/session/request 关联，入队拒绝、接受、结果终态、领域提交与排空分别计数。一次模型回复不等于一次成功编辑，恢复或重试不得重复算成功。每个窗口保留 pendingAtStart、admitted、admissionRejected、terminalByStatus、pendingAtEnd、committed；核对 pendingAtStart + accepted = terminal + pendingAtEnd。前一窗口遗留请求单独关联，不能重复计为新入队；未到终态样本保留截尾状态，不填0毫秒或当成功样本。

成功、拒绝、失败、取消、超时按相同边界分桶。每桶排序后取 nearest-rank `ceil(p*N)`，报告 N、p50/p95/p99/max 和原始样本哈希；N=0 时所有分位数 null，N较小时标注尾部样本不足。吞吐按固定窗口完成数/窗口秒数，另报成功提交吞吐。各轮独立报告，不挑最快轮、不求各轮分位数的平均作为总分位数。

完整 Present 返回间隔只作该观测口径的诊断。目标游戏平均FPS、1% Low、Hitch仍由 E5-D 正式合同和用户确认路线裁定，不在本阶段新设或修改性能门槛。观察器本身成本需用同路径启用/停用的配对记录，不能从帧时间中事后扣掉后声称游戏更快。

## 4 有界运行草案与停止条件

以下是**待确认建议，不是执行许可或通过阈值**：单次预热60秒并另存冷态；诊断窗口600秒、3个独立进程轮次；长稳3600秒；外部进程读数每1000毫秒一次。请求按原审批节奏，建议活动测试每10秒最多一个请求，受更小的次数/费用上限约束，不制造自动人审。每个配置/模式先冻结实际时长、负载、种子、调度与预算后才采样。

沿用现有 `.ncloop` CreateNew、4096队列、1000000记录、1GiB文件上限及原5秒关闭上限；原 `ecs_e5d_stream_evidence.py` 校验完整流与最终宿主报告。该 Python 校验器不在 c7c3 编译源码捕获中，本片仅单独登记源码 SHA `E3741063964491DE54FCA50C4990EB6F089B87A060F1A1A8B7B064E5CD1626A1`；执行前还须绑定完整 Python 运行时/导入依赖闭包，单个文件哈希不证明整套工具同源。建议新增摘要/事件记录最多65536条、64MiB，缺测/丢失显式计数；达到边界正常停止并标记不完整，不覆盖旧文件、不循环续期掩盖截断。

若出现身份/权限/源变化、采集写失败/丢样、操作预算耗尽、异常崩溃、持有未排空或用户停止，停止新增工作，按原取消/关闭流程收尾；真实未响应进程只记录精确身份，常规关闭失败后请求定点处理授权，不强杀、不换宿主/日志路径。超预算或未排空轮次仍归档，不能自动重跑取绿。原 CTest 超时不因长稳计划而延长。

## 5 E2 接线待办与执行前确认

E2-A至D已有纯数据合同、原ModuleSession/领域观测、原循环可选接线及两类字节消费者。E2-E首片增加产品/负载输入只读准备与检查点复核，但尚未实际执行准备入口或驱动。正式Program仍不开启，不是后台采样器；没有Presenter/MCP入口、实际运行或文件导出，不覆盖直接领域确认。后续将输入租约与原宿主、停止/关闭及封存流程关联，不新增AI发送/批准/自动提交或任意进程控制权限，保持measurementReady=false。

正式运行前还需：匹配捕获的完整产品/worker/native；独立受支持的实际 apphost；用户确认代表性负载、时长、硬件/显示条件及模型预算；稳定期内存增长/尾延迟的判定阈值事先确认。没有批准阈值只能做诊断，不能写“性能通过/无泄漏”。[空白运行记录](M8_7_E_RECORD_TEMPLATE.json) 保留未测值为 null，不授权任何动作。

E2 完成后再进入 F 同源输入准备与整阶段执行；所有新增功能测试集中 F。F 主体终态并汇总后开始原 PS5.1 24项/配置，未通过及环境阻断逐项保留。M8.6、实际客户端/人工、长稳、目标机 E5-D 债务分别结论。

## 6 E2-A 被动观测数据合同

`Ncma.Modules` 新增 `ModuleObservationCapture`，不依赖 Application/AI、没有 IO、时钟采集、回调、线程/进程启动或审批接口；由可信宿主显式提供同一时钟域、owner 顺序的数值事件。运行身份固定 run/source/workload SHA、配置、变体和频率；请求按 session/request 二元标识区分。它验证提供的数据，不证明宿主确实报告了所有真实事件。

事件区分窗口遗留、新接纳、接纳拒绝、派发、结果终态、可审阅、提交开始、已提交、取消请求、排空及 transport 数值。接纳拒绝不再计入已接纳请求的终态；结果为 Proposal 不代表已提交。Carried 仅允许窗口起点且不能伪造前窗口入队时间；缺失延迟保持 null。取消到排空与请求到结果终态分别分桶，迟到 transport 数值不会改变先前的取消结果。

硬上限 65536 事件、4096 个不同已接纳/遗留请求，默认4096事件；达到任一上限保留前缀并永久拒收后续事件，累计 DroppedEvents，不自动换窗或续期。无效事件拒绝且使 InvalidEvents 永久增加，已接纳状态不变；因此吞吐在不完整覆盖时返回 null。外线程/封存后访问被拒，不构成新并发队列。报告保留 pending/undrained，封存数据不代替停止业务或释放 transport。

分位数使用整型 nearest-rank，按相同运行和结果分类；空样本为 null，小于100个样本标记 SparseTail。原始定长事件和身份按版本化小端字段计算 SHA256，绑定窗口、容量、丢失及无效计数；不是签名、可信宿主证明、完整候选/工具链证明或“性能通过”。返回数组是独立副本但可由调用者修改，后续导出必须重验原始记录/派生值一致性，不能信任修改后的报告对象。没有文件序列化，本片不声称实现64MiB文件预算或稳态零分配。

原 `NcmaModuleTests.AIContracts` 登记11组新用例：空/缺失、精确阶段和非零控制、carry与拒绝守恒、100点分位数及状态隔离、畸形输入、非法/重复状态、双预算截断、取消/排空、线程/封存/边界、证据哈希与副本、跨session身份。原60秒和逐case汇总不变，均留 F 运行。只编译 AIContracts 完整依赖图，不代表 Editor 各变体、原生/worker、真实调用或功能门禁已通过。

## 7 E2-B1 模块生命周期和证据字节

`ModuleSession.BeginObservation` 只能在首次请求前，由 owner 显式启用一次；默认关闭。身份频率须与原 TimeProvider 相等，不能中途换窗消除丢失。`SealObservation` 只封存证据，允许仍有 pending/undrained，不停止、发送、批准或恢复业务。记录失败和超限保持原请求返回及关闭流程；无法关联请求标识的原拒绝会标记不完整，而不是漏计后宣称完整。OOM 不隐藏。

派发时间记录 owner 向 ThreadPool 交接，而非 adapter 真正开始执行；Queue 只涵盖入队到交接。Terminal 记录最终 `TryTake` 交付或 `TryClose` 丢弃时的状态，不把尚未领取、仍可被超时/取消覆盖的 Proposal 当终态。Drained 只在原条目真正移除时记录，晚于取消和结果交付。模块层不采集 adapter 字节/耗时或领域审阅/提交；`ObservationScope.ModuleSession` 拒绝这类伪造事件，DomainStagesAvailable=false、CommitsPerSecond=null，零条已观测提交不是“游戏没有提交”。

新增报告重放校验：复制有界数组，用同一状态机重算计数/分位数/哈希并核对；缺失事件只能声明，不能还原，更不能当宿主可信证明。范围进入 SHA 的 v2 域；此前 v1 纯数据来源捕获保留，不兼容为当前证据。编码后产生规范 UTF-8 字节，默认最多64MiB，可缩小不能扩大；无路径或外部Stream参数，不落盘，不覆盖文件。字节预算不等于整个编解码过程的堆峰值或零分配承诺。

解码先检查深度、字符串/数值长度、对象成员、重复属性和事件/分位数数组预算，再反序列化和重放；只接受版本2的规范表示，额外/缺失/重排/改写只读派生字段及未知属性均须拒绝，错误不回显输入。64MiB不是对任意用户文件的读取授权。实际落盘仍需独立的受检 CreateNew 入口及完整捕获/工具身份记录。

新增10组原入口用例：默认关闭/owner边界、真实交付排空与正文隔离、未领取Proposal超时、忽略取消的迟到排空、预算/观察时钟错误和空ID拒绝、封存不停止业务、完整/不完整报告往返、篡改拒绝、畸形/重复/超限JSON及模块范围限制。只使用受控原适配器任务，不访问模型或网络；功能测试仍待 F。

## 8 E2-B2 首片：适配器历史与模块证据文件输出

`DeepSeekChatAdapter.BeginObservation` 是可信宿主显式调用，默认关闭，owner开启/封存，原gate内首次调用登记前且只能开启一次。并发调用可能在开启取得gate之前已进入方法：此类 StartedTimestamp 早于窗口起点时计 BoundaryOverlaps，不能声称完整覆盖。跨线程适配器完成记录仍在原gate内串行写入，没有第二线程、回调或发送器。

每条保留 run/source/workload 上下文、session/request、独立调用序号与完成序号、入口/完成时间戳、原 active 槽归属和原安全诊断。busy/duplicate/disposed/lifetime 拒绝也保留；同一 request 的不同调用不去重，不能用完成顺序推断派发顺序。最多4096条完成记录、可收紧容量；满后丢弃后续记录并累计Dropped，不循环覆盖前缀。Invoked=Completed+Pending、Completed=Samples.Length+Dropped；封存时pending和跨边界调用使覆盖不完整。封存不取消IO、不释放active、不等待任务；晚到完成不修改已封存报告，也不自动续窗。LastDiagnostic原语义保持。

传输历史只含原安全 code/exception type 和数值、usage（允许null），不含prompt、正文、密钥、endpoint或model；它仍不是完整业务链路/提交成功/网络总流量。新增ticks从适配器方法进入到finally原gate内采样，含锁等待和少量诊断开销；原ElapsedMilliseconds边界保持，不声称两者完全相同或零开销。报告数组为脱离内部状态的副本，但调用者可改；**目前没有传输报告规范编码/重放/联合文件导出，不得把任意JSON当受检证据**。

`ModuleObservationFiles.ExportNew` 只导出现有模块v2报告，先沿原Encode重放和预算验证，再调用 `DiagnosticEvidenceFiles.WriteNew`。底层是可信本地字节写入原语，不具备业务schema/模型权限判定；可选模块wrapper才验证报告。现有Services不新增AI/Modules依赖，未新增Presenter/MCP入口。仅绝对本地固定盘、受限单文件名 `.ncobserve`、既存父目录；复用原WorkflowPhysicalPins拒绝路径别名/reparse/异常文件身份并保留全部父目录句柄。CreateNew + FileShare.None，持有同一文件句柄写入、Flush(true)、长度/SHA回读并正常关闭后才返回receipt；没有自动mkdir、覆盖、重试、删除失败文件、换目录或安全策略修改。同步导出在测量后显式调用，不作为逐帧或持续流写入器。receipt只证明本次成功写入/回读/关闭，不证明未来磁盘未变、模型/人工/安装验收或传输历史完整。

原AIContracts增加7组适配器历史用例＋4组文件输出用例，原ChatAdapterTests两个既有专项分支也包含前7组；不增加CTest或改超时。无真实服务，文件用例仅在F创建独立temp证据目录，不清理历史文件；非Windows明确失败而非跳过通过。全部尚未执行，代码/API存在不等于实际文件导出或功能通过。后续仍需领域关联、联合证据校验输出和持续进程采样。

## 9 E2-B2 工作流关联与数值联合输出

本节为输出片的历史边界；第12节新增严格schema1字节读取和进程证据标签关联，不重建已省略的适配器诊断，也不新增文件读取/执行权限。

`AiDomainWorkflow.BeginObservation` 在首次桥接Begin前显式开启一次，默认关闭，沿原owner与活动配置检查；不开启模型/进程、不批准发送或提交。原DomainModuleBridge每次创建真实ModuleSession时附加32事件观测，排空释放前封存独立报告；后续请求使用不同mailbox身份，前份报告不被覆盖。最多256请求、4096领域事件，可向下收紧，达到上限保留前缀并累计DroppedRequests/DroppedEvents，不阻断后续原业务。非OOM观测异常计ObservationFailures，不代替原异常、强行关闭或自动重试。Seal仅取快照，允许pending/undrained，不Stop/Pump/Cancel；晚到完成不改写旧报告。

领域侧记录资源审阅就绪、原DomainProposalReview就绪，以及**通过工作流包装器检查后实际进入原Confirm服务**的每次返回/抛错。调用起止涵盖原校验、权限和命令处理，不是纯命令执行；人工等待在调用前，不混入确认调用耗时。被拒绝的人审尝试保留独立ConfirmThrew，不与之后成功调用合并。即时查询原有有界回执队列，按原request/proposal ID、fingerprint、status/code、changed及executionRevision核对，缺匹配回执的返回记录为观测缺口。输出仅保留安全枚举、UUID、SHA、布尔及数值，不复制回执code、命令payload或场景内容。

通用领域面板或可信C#仍可直接调用原 `EditorDomainProposals.Confirm`；此路径不属于本观察器。报告和派生摘要固定标注 `WorkflowConfirmCallsOnly` / `not all domain commits`，0条观测确认不表示没有发生提交。ReceiptBackedSuccesses仅统计返回ok、有匹配原回执、无recovery、非replay的观察调用；ChangedReceipts另计。重放/恢复/失败不会算成新的成功操作。真实业务回执和权限始终由原服务持有，观测报告不是授权或可执行回执。

`AiWorkflowObservationEvidence.Inspect/Encode/ExportNew` 对脱离副本重新校验：统一run/source/workload/时钟域，模块报告沿原v2重放，唯一request与session组合，适配器序号/计数/时间和数值形状，原Proposal→审阅→确认顺序及指纹匹配。线程池任务可能先于owner记录交接事件开始，因此**不强行断言adapter-start晚于owner-handoff记录**；仅核对真实派发存在与各自时钟范围。丢失请求造成的无法关联传输单列UnjoinedTransportSamples并使覆盖不完整，不丢弃后伪报完整。

联合文件为新的输出schema Version1，嵌套模块报告仍是原v2语义；全部容量有上限、输出默认64MiB且只能收紧。传输输出主动省略code/exceptionType/endpoint/model等任意文本，仅投影身份、时间、HTTP状态、实际准备/读取字节与usage数值。文件沿原受检CreateNew/同句柄回读/正常关闭出口，失败保留、不覆盖。SHA/一致性校验只证明给定报告的内部一致性与已写字节，不证明真实宿主完整记录、源码新鲜、人工审批或性能达标。只提供受检输出API，**没有任意联合JSON导入/执行器，也不宣称完整离线消费工具已交付**；领域分位数后续应从这些分离边界计算，不混入既有模块延迟桶。

本片新增10组原入口用例（测试标签E2B3仅为联合片标识，不新增路线图阶段）：默认关闭与owner/一次性边界、原场景审阅/拒绝确认/回执与Undo、pending封存、取消后迟到排空、连续请求、跨运行/会话/计数/哈希/事件篡改、领域事件预算、请求预算、通用服务绕过的覆盖边界、数值导出与禁止覆盖。均只登记待F，不调用真实模型。动画资源二次人审接线已保留原入口，但本片新用例只验证场景链，其他领域实际效果/人审仍由原门禁裁定。

## 10 E2-C 首片：有界当前进程序列核心

本节为首片边界；2026-11-13 第11节补原循环的显式可选接线及进程证据字节读取，正式Program仍不启用采样。

基础层 `Ncma.Application` 新增 `ProcessSeriesCapture/Counters/Evidence`，不依赖AI/Modules，原四检查点和原应用循环不修改。公开构造必须显式提供runId、间隔、时长，仅绑定当前进程，无外部PID、公开读取回调、文件/网络、sleep、timer、worker、启动/结束进程或GC控制权限。测试通过命名测试程序集的internal时钟/读取器进行确定性模拟；原AIContracts依赖图编译这些用例，不为此改变固定离线六图或新增测试执行器。

间隔100ms至60s、最长1小时、整毫秒计划，容量默认4096/最大8192，最多36000个计划时隙；这些是API硬上限，不是已经批准的实际时长/负载。计划为 `[start,start+duration)`，间隔向上换算为时钟tick、时长向下换算，不凭浮点累计推进。每次owner调用Poll至多读取一次，只读当前到期时隙，跳过的到期时隙计MissingDueSlots；不突发补采、插值或轮转覆盖。未到期Poll不读系统计数器。容量满后停止读取；到期结束或显式Seal后不能重新采样/续窗，保留首段和终止原因。

计数满足 `PlannedSlots = Samples.Length + MissingDueSlots + UnobservedSlots`。到达计划结束时未观测的计划时隙全部成为缺失，提前停止的未来尾部仍为Unobserved，不假称全部“丢失”；时钟倒退/读取失败记ClockFailure并停止，不伪造时间戳。慢系统读取占用真实时间，开始在窗口内的样本允许结束越过窗口；后续时隙缺口如实保留。计数器失败保存该时隙和分组故障/null值，后续时隙可以继续诊断，但不会用后次值改写失败样本；上层运行控制仍须按既定停止条件处理业务工作。

分组采集：累计进程CPU时间（100ns，不转换百分比）、WorkingSet/PrivateBytes、句柄数、线程数，以及近似全线程累计分配、GetTotalMemory(false)、最近GC索引/heap/committed和读取前后GC次数。均为顺序非原子读取，不包含子进程/独立模型服务，不是原生独占或GPU驻留；最近GC字段可能过期、内存增长不能直接判泄漏。失败仅保留枚举位和null，不保留路径/异常文本。每次Process/ProcessThread对象显式释放，无强制GC或NoGCRegion。样本记录读取起止及owner线程原始分配差值；若分配差值为负，保留并标ObserverAllocationValid=false，不强改0或宣称测量有效。成本不含构造、最终复制/编码/调用者开销，不是整个应用稳态0B承诺。

当前为**owner-pumped核心**：主线程未Poll或被阻塞就缺采，系统读取本身也可能慢；没有后台线程独立于UI采集，更不能宣称不扰动帧时间。它尚未自动接入Editor/Player或默认产品启动。下一片需选择受控宿主接线/独立观察方式、绑定产品与源码/负载记录、保留观察器开销，完成离线消费；不通过增加隐式进程或降低覆盖要求制造“长稳通过”。

`ProcessSeriesEvidence` 复制并重验版本/身份/计划、严格递增slot、窗口、读数分组、终止原因和计数；默认最多16MiB规范输出字节，可收紧不可扩大，无路径或JSON导入器。只含runId/当前PID及数值，不自称已绑定源码/产品/工作负载；完整关联应由受控宿主记录承担，写文件仍须使用可信CreateNew出口。CompleteCadenceCoverage与AllCountersAvailable分开，任一完整都不等于性能/泄漏通过。

新增11组原AIContracts用例：计划边界/构造不读计数器、精确半开时隙、迟到不补采、容量停止、时钟故障、计数器失败、慢读缺口、owner/重入、篡改/编码预算、部分字段及GC跨越、实际当前进程单次读取。全部未执行；实际读数用例在F运行，Windows依赖缺失明确失败、不跳过后宣布通过。不更改原60秒、原用例或旧NoGCRegion口径，不产生任何真实采样/模型/导出/长期运行许可。

## 11 E2-C 第二片：原循环显式接线及进程证据离线字节读取

`ProcessMeasurement` 由可信宿主显式构造并传入原 `ApplicationLifetime.Run` 新增的末尾可选参数。默认null；EditorRunLoop仅透传同一对象，正式Program/Player入口未开启，没有新菜单/CLI/MCP、后台线程或新业务执行器。每轮原循环的finally中，在原LoopCostCapture.End之后最多Poll一次；Completed/Stopped/Faulted统计是原循环已标记的结果，不是整应用成功或关闭证明。初始化时钟失败或LoopCostCapture.Begin失败等未进入迭代finally的情况没有伪造采样；故障后的恢复等待和原服务关闭不采样。

构造至首次Poll的延迟仍进入窗口并如实出现漏采；既有LoopCostCapture的单轮时长/分配不包含随后采样工作，但下一帧的墙钟delta、Present返回间隔及整个进程统计可能包含它，不能事后扣掉观察器成本制造性能通过。单帧卡住时也不能独立采样，本接线不是后台观察器。读取失败保留原核心的分组null或时钟终止；额外观察异常只标记一次ObserverFailures并停止后续Poll，不替换原业务返回/异常（OOM不隐藏）。报告不能代替上层停止新增工作、原取消/排空和显式关闭控制；相关运行驱动仍待补。

对象要求原owner、同一ApplicationLifetime及pipeline；同一原循环显式重入运行/恢复可继续同一窗口，不能换宿主续窗。只用弱引用识别宿主，不因未封存而强持有业务对象；Seal在Run外显式执行一次，不Stop/Dispose应用，不创建下一窗口。封存期间没有任何自动文件输出。

身份记录包含run UUID及源码/捕获manifest/产品manifest/工作负载四个大写SHA256，配置/变体/宿主类型均为闭合集合。它们是可信调用方提供的标签，构造不读取磁盘、不核验实际产品或负载；`DecodeExpected`逐项核对调用方独立固定的预期身份，不将自身声明当磁盘新鲜/真实执行/审批证明。不能用当前标签给历史报告重新签名。

原进程报告新增Decode；新的ProcessMeasurementEvidence提供Validate/Encode/Decode/DecodeExpected纯字节API，上限16MiB。类型反序列化前先检查深度8、标量长度、每对象32属性、唯一Samples数组和8192条预算；拒绝重复/未知字段、畸形数组、非规范表示、被改写的只读派生值，并重验原时隙/计数器、循环计数守恒和run一致性。异常不回显输入。本片是**进程证据离线字节消费者**，不是路径读取工具、完整领域联合报告导入器或长稳验收器；不增加自动IO权限。

原AIContracts新增8组：身份/构造不读、原顺序及显式采样、停止/故障不改变原结果、owner/同一宿主/封存边界、往返及预期身份拒绝、计数/派生值篡改、字节/结构预算、封存不关闭业务。保留原60秒及既有case体；全部集中F测试。需同时编译原verify-contracts与verify-ai服务测试（含实际Editor）依赖图，不能把单个合同图编译称为宿主编译通过。

后续继续领域联合报告离线消费、独立固定的产品/负载证据和受控运行驱动；正式Program启动、实际采样/文件导出、模型预算、人工负载与长稳阈值仍未执行/未授予。matching全套输入与F顺序不变。

## 12 E2-D：领域联合证据字节消费者与进程标签关联

联合输出schema保持Version1，嵌套模块报告仍沿原重放合同。原导出把运行时报告投影为数值Document后，与Decode/EncodeDocument共用同一个领域/模块/传输验证器；原报告独有的diagnostic.RequestId/ElapsedMilliseconds先验证再省略。读取端直接验证数值Document，**不伪造已省略的Code/ExceptionType/ElapsedMilliseconds，也不反向构建实时AiWorkflowObservationReport或可执行回执**。分组缺失、pending/undrained、截断和确认统计仍按原合同重算，不把成功回复变成成功编辑。

Decode先在64MiB内复制输入快照，对同一副本进行预检、类型读取、原模块重放和规范字节比较。深度12、每对象32属性、字符串512字节、数字32字节；Requests最多256、领域Events最多4096、Transport.Samples最多4096、每个Module.Events最多32、Module.Latencies最多42，其他数组位置拒绝。重复/未知/缺失字段、非规范表示、只读派生值和摘要篡改均拒绝；摘要与实际记录重算结果必须相等。输出模型是可修改的脱离副本，再编码会重新验证，不是不可篡改证明。预算约束输入/输出字节与结构，不是整个解码器的峰值内存或0B承诺。

DecodeExpected核对外部提供的完整ObservationIdentity（run/source/workload/configuration/variant/frequency）及领域intent。WorkflowProcessAssociation另要求两份输入的外部预期SHA，并沿各自严格消费者核验，再比较run/source/workload/configuration/variant和Editor宿主标签；只支持当前Ai/AiExtensions对应模式，不把LocalWorker强映射为HTTP AI。

关联结果只汇总两份声明的身份及各自覆盖状态，不相减/合并两份报告的时间戳。相同频率不足以证明同进程/同一时钟域，故CommonClockVerified和ProvenanceVerified始终false；进程报告里的产品/capture manifest标签只与独立提供的预期记录核对，没有读磁盘、验证真实负载执行或签名。未绑定循环/缺采的进程报告仍以明确false状态保留，不能提升成验收通过。

新增10组原AIContracts用例：输出v1显式投影/往返/省略字段、预期身份/intent、独立副本与模块重放、传输数值/关联、畸形/重复/未知/派生值、各级数组和字节预算、pending/截断保持、标签关联无共同时间证明、跨运行/模式拒绝、预期文件字节与产品/capture标签拒绝。原既有case体和60秒不变，所有用例集中F执行；本步不实际读取证据文件、采样、运行模型或导出文件。

下一步是独立固定产品/工作负载输入的受控测量编排与驱动；纯字节消费者和声明关联不等于完整运行准备。正式Program仍不启用，模型/人工负载/长稳时长和阈值门禁不变。完成准备后匹配全套输入/F，主体终态汇总后原PS5.1。

## 13 E2-E 首片：独立产品/负载输入只读准备

`MeasurementInputsPlan` 固定原进程测量身份、整毫秒间隔/时长、容量和帧数上限，计算独立计划指纹；仅当前Editor范围。沿用100ms..60秒间隔、最多1小时/8192样本，要求容量能容纳所有计划时隙，不默许一开始就截断；帧数上限1..1000000。计划是准备数据，不是用户对实际负载、时长、模型预算或运行的批准，尚未有运行驱动消费它。

`MeasurementInputLease` 仅供可信本地宿主提供捕获清单路径、独立产品目录、独立负载目录，拒绝盘根、产品/负载相互嵌套、捕获清单落入任一受测目录。复用原WorkflowPhysicalPins，要求Windows本地固定盘、明确路径/文件身份、无reparse或多硬链接；保留文件及祖先目录句柄，不增加权限、改ACL、创建目录、写入/复制文件或启动程序。Prepare单次，失败状态保持，部分句柄必须显式Dispose释放；关闭未完成不能保持Ready。外线程、重复Prepare或关闭后重验均拒绝。

三个清单各最多4MiB、深度16、重复字段拒绝。捕获清单只核对外部预期SHA及原kind/schema/sourceInputsSha256，**不遍历/锁定5066等全部源码，也不证明真正由这些源码编译**。产品清单必须与外部预期SHA/source/capture/configuration/显式AI及扩展标记一致；要求四个原Editor入口文件存在且非空，所有声明文件固定后再调用原DeploymentManifest.Validate，不替代原包构建、运行时闭包、实际EXE或安装验收。

负载采用显式`measurement-workload.json`：仅schemaVersion=1、kind=`ncma.measurement-workload.v1`、scenario、files；scenario为editor-idle/open-world-exploration/controlled-fixture之一。files条目为path/size/sha256，复用原ASCII相对路径、重复/大小写和文件目录碰撞检查。除editor-idle外文件集非空；清单哈希就是该计划的WorkloadSha256。它只声明资产/路线描述文件，不执行指令，不代用户确认代表性游戏内容；没有从用户项目自动生成或扫描负载。

最多1024产品文件＋512负载文件，单文件64MiB、总payload512MiB，清单字节另计；原2048文件/4096目录句柄预算不扩大。每棵树派生目录预算最多2048，整体仍必须满足原句柄预算。树检查只枚举明确根目录，拒绝未声明文件及空目录；出现额外文件时不读取其正文，不自动删除。声明文件从同一保留句柄核对size/SHA；新增VerifyObserved只复核已Observe的同句柄，不重新占用pin槽，原其他调用者行为不变。

RequireCurrent重新核对保留句柄和两棵精确树，返回独立检查序号与文件集合指纹；失败后不允许同实例自动恢复。目录句柄不能阻止新增子项，故这只是检查点上的精确集合，不是连续目录封锁或任意本地代码的沙箱。已有清单/声明文件的只读共享约束也不等于已装载进程、外部.NET运行时/驱动或GPU的完整固定。回执明确SourceContentsVerified/ExecutedProductVerified/LaunchAuthorized/WorkloadApproved/PerformanceAccepted均false，不生成审批票据。

新增10组原AIContracts用例：计划与覆盖预算、只读句柄/重复复核、payload漂移/失败持有、初始及后加文件、清单/原包规则、重复/路径/畸形、文件数及字节预算、owner/关闭/缺目录、显式空闲负载、多硬链接拒绝。均在F运行，非Windows或硬链接环境不可用明确失败、不跳过；测试只用独立临时合成文件，不启动假EXE、不删除历史证据。当前只是代码和编译准备，没有实际执行本准备入口或采样。下一片仍须受控运行驱动将输入租约与原宿主、窗口结束/用户停止/故障关闭和证据封存关联，正式Program继续关闭。

## 14 E2-E 第二片：受控原循环与显式关闭

`ApplicationRunWindow` 是独立于被动采样的可信owner控制对象，首次Run开始单调时钟窗口，最多1小时；同对象不能重复Run。原ApplicationLifetime.Run仅增加可选window参数、首次绑定和迭代开始前检查，默认null保持原循环；不包装pipeline、不重建每帧时钟基线。时长到期或RequestStop在帧间退出，时钟倒退/异常沿原Clock故障路径处理。没有看门狗、抢占、线程/进程终止；单帧、pacer、IO或关闭阻塞可以超过时限，不能称硬实时超时。帧数仍沿原maxFrames。

`MeasurementRunDriver` 固定计划和已Prepare输入租约。RunAndClose是显式可信本地操作：先调用原CanRecoverPresentation getter的Verify检查owner及宿主回调重入（不执行恢复），再核对Running、计划指纹、身份与实际输入，才承担原宿主与租约的关闭责任；预检拒绝时原调用者仍负责资源。构造不执行IO/采样/启动。进入原Run时启用已有ProcessMeasurement，分别报告帧上限、时长、显式停止、平台停止或业务故障；保留已完成帧前缀，不把失败帧当成功帧。采样窗口构造略早于Run窗口起点，两者不是精确同一时间边界。

Run退出后先封存/沿原消费者编码进程证据，再复核输入，之后调用原ApplicationLifetime.Dispose。输入检查不在帧内；后检查发生在关闭前，不宣称关闭期间的目录连续完整。原业务异常保留，附带封存/校验/关闭失败时按发生顺序Aggregate，不把失败改成成功状态。Status只保留异常类型不回显消息；进程报告读取返回经原消费者复核的独立副本。Closed仅表示成功释放，不等于业务/校验/性能通过，必须同时看End/FailureType和输入检查结果。

若原关闭失败，原应用和输入句柄仍被驱动持有；读取Status/ProcessReport不会隐式重试。仅显式RetryClose再次调用原关闭，原应用成功释放后才释放租约；不封存第二份报告、不续窗、不恢复业务、不自动重跑或保存场景。未成功关闭的资源不能提前丢弃。该驱动无公开执行回调，但传入的pipeline本身是可信宿主业务，不是沙箱；调用方仍必须独立确认实际负载、模型费用/外发权限。

这不是独立候选启动器，也未接入正式Program或Editor图形恢复对话框；测量运行发生故障时直接退出并沿原有序关闭，不自动恢复/续测。已固定候选文件不等于当前进程确实加载它们，ExecutedProductVerified/WorkloadApproved/PerformanceAccepted始终false。当前只形成内存进程证据，没有自动写盘、完整工作流封存或长稳完成结论。

10组新用例覆盖假时钟到期/倒退/原空参数路径、owner、真实只读夹具、原故障保留、帧完成前缀、平台与显式停止、后校验变化、关闭保留/显式重试、预检拒绝和重入。沿原AIContracts入口/60秒，全部留F；合成EXE从不启动。独立候选入口与真实装载/负载身份绑定、完整受检文件输出和人工/长稳预算仍是后续工作。

## 15 E2-E 第三片：独立候选 CPU 空闲入口与证据

新增独立 apphost 显式参数形状：`NcmaEngine.exe --measure-cpu-idle <request.json绝对路径> <外部固定的大写SHA256> <新文件.ncobserve绝对路径>`。这里只定义入口合同，不表示本轮已运行或许可未来自动运行。恰好四个参数，不能混入项目/插件/模型/其他CLI选项；普通启动路径保持默认不采样。产品配置/AI与扩展变体必须与编译期标记一致，全部候选文件仍沿原DeploymentManifest校验。

请求由 `MeasurementCandidateRequest.Encode` 生成规范UTF-8（版本1、原身份/时长/间隔/容量/帧数/捕获清单路径/产品根/负载根），最多16KiB。Read要求外部SHA并从原受检只读句柄读入；重复/未知/缺失/非规范字段拒绝，不包含humanApproved、模型配置或脚本字段。请求本身不是运行票据，服务不将自身声明当授权。负载清单必须是editor-idle且files为空；其他场景/文件负载在启动前明确拒绝，不能拿空基线充当用户待确认的大世界内容。

`MeasurementInputLease.BindCurrentEditor` 在原输入复核之后，将OS当前EXE路径、托管EntryAssembly.Location、AppContext.BaseDirectory与同一租约保留的NcmaEngine.exe/NcmaEngine.dll路径对齐，记录PID及声明文件SHA。`dotnet DLL`、其他目录或测试宿主拒绝，无替代宿主。这里只核对当前入口路径及保留的磁盘字节，**不验证加载内存、所有原生/托管模块、.NET/字体/驱动闭包或源码构建来源**。回执与导出都不是签名/可信执行证明；可构造的数据对象不能产生运行审批。

准备输入和输出预检后沿原WindowsConfirmation显示运行UUID、产品/负载/输出路径、时长/帧数/间隔以及固定30fps上限，默认“否”；同意后再复核输入。没有公开确认回调或请求审批位。随后原CandidatePresentation使用project=null、smoke=false、cpuOnly=true、preview=true：可关闭的可见诊断窗口、唯一临时日志，不创建authoring presenter、AI工作区、密钥输入、扩展启动组、项目脚本、偏好或布局存储；原默认呈现构造/方法不修改。此基线仍加载原平台/GUI/插件并使用系统字体，不是纯无原生依赖Headless。关闭窗口、时长或帧数终止沿既有驱动。无自动恢复、模型外发或强制结束进程。

关闭继续遵循原ApplicationLifetime与驱动的责任：Start/Run已失败关闭不在finally自动再试；资源保留时同时保留输入租约和静态宿主，后续同进程Main拒绝进入。不把进程结束视为成功资源关闭。入口目前没有新交互重试按钮；可信嵌入宿主的原驱动RetryClose合同不变。输入拒绝、启动失败、采样缺口、业务失败或关闭持有不改判通过。退出0仅表示这次受控诊断正常完成和证据写入，不代表覆盖完整、无泄漏或性能达标；拒绝参数2、取消5，其余失败4。

`MeasurementCandidateEvidence` 版本1最多24MiB，包装原固定计划、输入回执、入口路径核对、运行/关闭状态和原进程证据（base64，沿16MiB原消费者重验）；只支持cpu-idle。解码在类型构造前限制深度8/512token、无数组、普通字符串1024字节及唯一预期根ProcessEvidence的base64长度，随后校验原计划/身份/PID/采样周期/容量/完成帧和规范字节；摘要不是人工审批/真实CPU负载证明。普通错误只记录类型名，不输出请求路径、项目内容、密钥或异常消息。启动失败可明确缺失Run/Process，不能捏造零样本成功。PerformanceAccepted/RepresentativeGameplay/GrantsExecutionAuthority恒false。

输出不得在产品/负载树或捕获清单所在目录内；原DiagnosticEvidenceFiles抽出同一目标名/祖先路径预检，真正写入仍重新核对并CreateNew、独占句柄、Flush、SHA回读、正常关闭后返回回执。预检不是预约锁，期间冲突只能失败，不覆盖/删除/重试/换目录；失败文件保留。普通运行或没有显式请求不会输出。当前是CPU基线的完整封装，不包含AI工作流长稳联合证据或场景操作轨迹。

新增10组原AIContracts（60秒）覆盖请求规范/外部SHA、只读输出预检、拒绝测试宿主绑定、证据往返/计划PID篡改/缺采启动失败/状态矛盾/预算/CreateNew；另2组原EditorService（180秒）覆盖CLI形状和真实Main的早期拒绝，均留F。正向当前apphost绑定/本地确认/实际窗口与导出仍待实际候选人工验收；不以合成记录或测试宿主代替。下一步仍需经独立确认的真实负载、原AI工作流观测与联合输出，不能解锁此前延后的E5-D或自动批准模型/长稳预算。

## 16 E2-E 第四片：实际工作区生命周期的有界被动观测

`AiAuthoringWorkspace.BeginObservation` 由可信本地宿主显式调用，只能在第一次成功创建 authoring 工作流之前启用一次；当前没有 Program、Presenter、CLI 或 MCP 调用它，普通编辑器默认关闭。配置连接不等于发出请求，观测不打开凭据窗口、不发送、提交、Pump、取消或排空。原 Send/Confirm/资源批准及清理规则完全沿用，旧方法仅在实际工作流创建后 Attach、原 TryUnregister 成功后 Retire。记录范围不包括连接诊断，也不覆盖绕过工作流包装器的直接领域提交。

每个实际工作流继续使用已有 BeginObservation/SealObservation 及严格数值编码器。替换/重配/正常关闭时先由原生命周期释放，随后封存；关闭未收敛则继续由原 owner 持有，观测不提前释放。显式 Seal 可以在工作区关闭后读取，也可以截取活跃/未排空前缀；只封存诊断，不取消或重试原业务。封存单次且清除采集器对工作流的引用，后续业务完成不改写旧报告。OOM 仍传播，其他挂接/封存/编码异常转为 FailedWorkflows，不能破坏原业务或悄悄重试。

最多跟踪前32个已创建工作流，保留最多1MiB/工作流、8MiB总payload；可缩小容量/字节上限，不得扩大。进入下个工作流前要求剩余空间能预留完整单条预算，故不足时即记 DroppedWorkflows，不冒险无限累积；前缀槽包含失败尝试，失败不让后续工作流替补。单条编码超限作为明确采集失败，不截断或伪造完整报告。StartedWorkflows = 保留条数 + DroppedWorkflows + FailedWorkflows；该守恒描述已创建工作流，不宣称成功创建前的所有用户操作都被观测。

每条保留原工作流序号、intent、ConfigurationId、不含收件人信息的配置UUID，以及 ReleasedBeforeSeal。重配可继续观测，但配置不同的结果分别呈现，不合并计算“某个模型”的延迟；MultipleCapturedConfigurations 只表示保留条目，丢失条目可能隐藏更多变更。UUID来自实际Target，离线读者仍不能据此证明真实endpoint/model、进程或调用来源。

`AiWorkspaceObservationEvidence` v1有界规范JSON最多12MiB，嵌套报告是原规范字节的base64。预检限制深度5/1024token、唯一允许数组位置Workflows、最多32条、普通标量256字节/数字32字节/受限base64；再检查完整身份、预算/计数守恒、序号/窗口、活跃封存只能为最后一个已创建工作流、跨工作流请求/会话UUID不能复用，并逐项调用原严格消费者。数据是可变的脱离副本，重编码必须重验；非规范/重复/未知/派生值篡改拒绝。PerformanceAccepted/GrantsExecutionAuthority/ProvenanceVerified始终false。这里的同窗顺序来自本机采集实现，但离线数据本身不是可信执行证明。

导出仍为可信调用者显式原 DiagnosticEvidenceFiles.WriteNew，CreateNew/Flush/同句柄SHA回读，不覆盖、不建父目录、不重试、不自动选择路径；正式宿主接入仍须已有输入/输出路径隔离与独立审批。此API本身不是产品启动器或文件权限沙箱。退休时编码/分配发生在owner线程，会影响该次操作和后续帧；不承诺零分配、无扰动或完整长稳统计，预算亦不是解码峰值内存上限。

新增12组原AIContracts用例：默认关闭/限额、owner/单次/重入、空窗关闭、替换前缀、配置隔离/无敏感文本、原人审与实际回执、阻塞传输封存不排空、容量/预留字节丢弃、编码失败不破坏业务、身份与独立副本、畸形/预算/守恒、原CreateNew。沿原60秒入口，全部留F；不将编译当测试，不改历史失败或新建超时入口。

本片仅接通工作区级观测、分组证据与文件接口；**进程＋多工作流联合容器、真实产品启用与独立负载审批仍待下一片**。不复用CPU空闲记录冒充AI/大世界测试，不启动模型或用户项目。E5-D代表性内容确认继续延后；完整同源输入后F，主体终态汇总后再原24项PS5.1/配置。

## 17 E2-E 第五片：进程与多工作流联合证据

`AiWorkspaceProcessEvidence` v1是离线数据容器，不是运行器。保留原工作区规范字节（最多12MiB）、原进程规范字节（最多16MiB），并封装完整双侧身份、原输入计划参数、输入检查回执和两份独立预期SHA；外层规范JSON最多40MiB。Create先复制调用方字节，再检查传入的外部SHA，返回脱离副本；后续修改数据仍需重验。Decode只检查自洽，DecodeExpected还要求独立预期计划、工作区身份和双SHA；调用方不得从同一未受信封装中抄出这些预期值当作独立验证。

验证沿原严格工作区/进程消费者，不重新解释原报告。单工作流和联合消费者共用原WorkflowProcessAssociation标签比较：run/source/workload/configuration相同、Ai/AiExtensions一致、宿主为Editor。空工作区也必须核对，不允许因没有条目而跳过。进程容量、间隔/时长ticks和已完成帧上限必须与原计划一致；不要求频率相等，不相减或拼接两份时钟。仅接收声明非空的controlled-fixture/open-world-exploration，不接受CPU-idle空负载，防止空基线被包装成此类报告。输入计数/字节预算/检查序号/指纹与哈希均核对。

输入回执和计划仍是数据：本API不读磁盘、不持有租约、不复核加载程序，也不证明该声明负载已执行。open-world标签不是目标机验收、代表性内容批准或模型调用许可。PerformanceAccepted/WorkloadExecutionVerified/CommonClockVerified/GrantsExecutionAuthority/ProvenanceVerified始终false。缺采、未绑定循环、空工作区、丢弃、失败、未排空原样保留；编码成功只说明数据满足该合同。

Inspect只解析一次进程数据，不为每个工作流重复解析最大16MiB进程块；逐条返回原工作流摘要、序号、配置UUID、intent及释放状态，不给跨配置汇总延迟或“完整运行成功”布尔值。工作区嵌套消费者及摘要会分配复制，仍是测量后显式路径，不承诺0B、无扰动或40MiB峰值内存。

Decode先复制有界输入快照，在类型读取前限制深度8/512token、禁止数组、普通字符串1024字节、属性名128字节/数字32字节，仅根WorkspaceEvidence/ProcessEvidence允许各自受限base64；然后原消费者验证与规范字节比较，拒绝重复/未知/非规范字段及派生值篡改。嵌套字节保留双SHA，不把声明哈希当签名。

ExportNew仍显式调用原DiagnosticEvidenceFiles.WriteNew，CreateNew/Flush/同句柄回读；不得覆盖/自动重试/删除。此存储API不替宿主决定输入树隔离、输出路径审批、租约寿命或实际装载身份。当前没有产品入口、Presenter/MCP、模型/worker启动或自动写盘；后续可信候选宿主接入必须先完成这些原边界，不能以纯字节容器代替运行批准。

新增10组原AIContracts（60秒）用例：规范字节与独立预期、跨身份/配置/变体/宿主、输入与非空负载、计划采样与帧数、嵌套篡改/副本、畸形/预算、空窗缺采、多配置分列、丢失失败保留、原CreateNew读回。原既有case体不改，功能执行统一F；本轮不调用模型、不实际采样或导出。下一步可信候选宿主接入、独立负载/模型预算与输出隔离；E5-D手动内容确认保持延后，F主体终态及报告后原PS5.1 24项/配置。

## 18 E2-E 第六片：真实宿主观测挂接与工作区请求限制

`CandidatePresentation.ConfigureAiMeasurement` 是实际Editor类型的内部可信组合接口，只能在首次Start尝试前配置一次；preview拒绝，线程/启动/关闭/重复配置拒绝。核对编译配置、Ai/AiExtensions变体及实际Stopwatch频率。默认未配置；没有新CLI/Presenter/MCP入口，不加载项目或请求审批。原Start仅增加启动边界标志，并在实际AiAuthoringWorkspace创建后、交给Presenter之前挂接原观测API。

`AiWorkspaceMeasurementLimits` 从原受检AiConnectionProfile取provider/protocol/deployment/规范endpoint/model，以及最大输出tokens/超时上限，限制1..32次工作区尝试。配置UUID和凭据引用不进入策略指纹，不读取或保存密钥；凭据轮换不重置限额。同一工作区的Send及BeginConnectionDiagnostic共用计数，先要求收件人和上限匹配，再在原审批/派发之前保守计数；人工拒绝、错误指纹、失败派发等匹配目标的尝试均不退款。目标不匹配、已封存、超限直接拒绝。原人审和请求验证继续执行，没有自动Send/Confirm、授权或重试。

这不是费用上限、实际HTTP完成次数、整个进程网络配额或任意可信插件沙箱；直接调用其他客户端/领域控制器不在该工作区门面预算范围内。连接诊断消耗尝试预算，但仍不进入authoring工作流证据，状态分别报告，不能拿空authoring报告推断没有HTTP。开始受限观测时已有连接诊断或排空中的工作拒绝，以免遗漏活跃调用。此前已完成的诊断在本窗口外，不追溯记账。

`SealMeasuredObservation` 与原纯被动Seal不同：先关闭后续工作区发送，再封存现有观察前缀；不取消、Pump、排空或释放已经存在的请求，也不提交/撤销业务。限制在重配、断开、封存后仍保留，不能用配置变化续费。原纯BeginObservation/SealObservation合同及普通未配置路径不变。

原宿主Dispose在AI TryClose成功后、清空引用之前自动封存一次。TryClose未收敛仍原样抛ai_shutdown_draining，所有下游资源继续保留，不为得到报告重试关闭。可信调用者也可在帧外显式SealAiMeasurement，取得未排空前缀；不在普通状态读取时封存。采集/编码非OOM错误仅记类型并允许原清理继续，证据封存不重试；OOM仍传播。未挂接就失败启动/关闭会明确无证据，不捏造空成功。复制的原工作区字节在AI引用释放后仍保留，状态同时显示外层持有和独立预算快照；读取不触发清理/IO。正常关闭后的状态不被“已封存”误当成全运行或性能通过。

当前接到真实宿主的只是观测生命周期与限制，不是独立候选测量命令、完整运行批准或目标绑定。实际工作负载/装载入口、只读输入租约、输出树隔离、本地人审，以及联合记录与关闭状态的配对仍需下一片完成。工作区/进程原证据API不增加自动调用或安装权限，正式启动不采样。

新增6组原AIContracts（60秒）覆盖指纹/限额、诊断与authoring共享预算、人审拒绝计数、收件人/重配、封存后拒新请求、owner/活跃诊断及未排空请求；新增4组原AiHostLifetime用例覆盖未启动边界、实际CPU GUI/DX11原循环8帧及正常关闭封存、原失败关闭显式封存不排空。宿主用例使用原sample/native/font依赖，缺失即失败，不跳过；HTTP仅按原夹具替身，不访问真实服务。均在F统一运行，编译不算测试或独立apphost/人工验收；原入口超时不改。E5-D内容确认继续延后，F主体终态汇总后原24项PS5.1/配置。

## 19 E2-E 第七片：候选请求与只读场景负载绑定

本片解决候选接线前的输入冲突：原输入租约锁定负载文件，不能直接作为普通可保存/可导入项目目录使用。新增 `AiSceneMeasurementRequest` 明确限制为一份 `controlled-fixture` 场景快照，后续候选组合将把副本放入原内存编辑文档；本片尚未实现该装载、运行命令、本地确认或联合关闭导出。不是任意项目启动器，也不替代多领域人工验收、实际游戏或已延后的E5-D大世界内容。

请求v1为最多16KiB规范JSON：原候选输入计划/目录、场景原始字节SHA、模型收件人与尝试上限；重复、未知、缺失、非规范字段拒绝。原CPU请求的只读读取块提取为 `ReadLocalBytes` 共用，仍按外部独立SHA、原固定本地路径与保留句柄、原16KiB预算读取，CPU Decode规则不变。请求不包含密钥、凭据引用、配置UUID、humanApproved或执行回调。Read仅返回数据；没有读取用户连接配置、自动启用工作区或模型调用。

收件人复用原 `AiConnectionProfile` 和 `AiWorkspaceMeasurementLimits` 校验：当前仅既有内置DeepSeek，远程固定HTTPS端点或本地字面量回环端点，显式模型、1..4096输出token、1..60000ms超时、1..32次工作区尝试，要求endpoint已规范化。不从请求隐式注册其他提供商。验证内部的固定UUID/远程凭据引用仅是私有比较占位，不导出、不查询、不安装为连接配置、更不交给传输；指纹仍不含这些占位符。这是后续实际UI配置需要满足的限制，不是费用上限、实际HTTP计数、网络沙箱或外发许可。

`MeasurementInputLease.RequireCurrent(request)` 同时核对原计划/身份和实际保留的捕获清单、产品、负载目录，不能仅凭相同声明回执或哈希把另一目录套入。`ReadSingleWorkload` 只接受清单中唯一且精确匹配的受控文件及独立预期SHA，最大4MiB；沿原 `CopyObserved` 从已观察的保留句柄复制并校验，不按路径重开，不为重复读取消耗新pin槽。前后原检查仍执行；实际复制/复核失败将租约置Faulted，不释放、不重试，显式Dispose责任仍在原调用者。参数或路径不匹配不授予访问权限。

AI准备严格要求唯一文件名 `scene.ncmascene`、非空且不超过4096对象，只允许原v1 Transform和无脚本绑定。沿原SceneDocumentCodec及原生成组件解码/校验检查数据，既不加载DLL/项目，也不注册自定义组件、准备资源、生成运行World或启动宿主。拒绝空场景、额外文件、其他组件、脚本、其他场景名、editor-idle及open-world-exploration。4MiB仅为输入预算，不承诺解析峰值内存或零分配。

`PreparedAiSceneMeasurement` 保留脱离副本、原字节/请求/策略指纹、对象数及检查回执；复制方法不暴露内层数组，关闭原租约后副本仍可读取。它**不拥有租约，也不是持续新鲜性、人工批准、已装载产品或已执行负载的证明**。下一步实际候选组合必须独立BindCurrentEditor、原本地确认后再复核同一租约，保持原发送/提交人审与失败关闭保留；不能拿副本或false授权标记替代这些步骤。输出预检只委托原输入树隔离与CreateNew目的地规则，不创建/预约/覆盖文件，最终导出仍须再次校验。

新增10组原AIContracts/60秒用例，覆盖请求规范/外部SHA、原提供商与预算、实际租约路径/计划、单文件/场景类型、脚本与组件拒绝、原Transform校验、owner/只读pin/副本隔离、输出不覆盖。旧case体不改，仅原入口追加登记；全部留F，本片不执行准备API、宿主、模型、采样或输出。编译通过不算功能、候选或整体M8.7验收；默认产品启动、固定测试输入和正式安装不变。完整同源输入后F，主体终态及报告后原24项PS5.1/配置。

## 20 E2-E 第八片：显式候选入口、内存场景与联合关闭证据

仅AI、不含扩展的产品新增 `NcmaEngine.exe --measure-ai-scene <请求绝对路径> <外部固定的大写SHA256> <新文件.ncobserve绝对路径>`；恰好4参数，不混入其他选项。形状错误2，非AI或扩展/组合产品明确不可用3，取消5，其余失败4。普通启动默认不启用；本片定义和编译入口，不运行确认窗口、模型或测量，也不意味着已有匹配候选包/部署。

入口沿第19节规范请求和原只读租约准备实际负载，按编译配置核对，再用原BindCurrentEditor将当前EXE、托管入口及应用目录与受检候选路径对齐，拒绝dotnet/testhost替代。沿原WindowsConfirmation默认“否”展示完整产品/负载/输出路径、场景SHA/对象数、接收方/模型、尝试/输出token/超时上限、帧数/时长/采样间隔和限制。确认只是启动本次诊断窗口，不等于批准任何模型发送、差异应用、费用或性能门禁；人审等待后重新检查同一租约及目的地。路径核对仍不证明加载内存、全部源码/模块或供应链来源。

实际原CandidatePresentation运行可见CPU GUI，project=null、preview=false、30fps；不是原空闲preview、隐藏窗口替代或3D性能测试。临时日志沿原唯一运行目录；不读取/写入用户偏好或布局，不加载项目、游戏脚本、资产工作区或扩展。受检快照副本通过原Document.RestoreBytes/Resynchronize进入原编辑文档，初始路径为空，原领域提案使用本次RunId作为独立诊断项目身份。整个会话的原composition校验同时限制无脚本、仅Transform，防止UI或AI事务以后添加不在本入口范围的组件；仍使用原生成组件验证与原历史/Undo，不另建世界模型。

Presenter仅在该模式使用显式动作子集：选择/对象编辑/Transform/Undo/Redo及原AI界面。未知操作默认禁用，绘制Enabled与实际分发都检查，快捷键仅保留原Undo/Redo；文件打开/保存/新场景、Play、脚本重载、资源导入、MCP/构建工作流及扩展入口不能执行。原composition作为第二道数据检查，任意可信C#调用不被宣称已沙箱化。界面明确仅内存编辑且关闭丢弃修改；不向原负载保存，不自动保存到别处。普通Presenter默认参数false，原分发/模态审阅/过期事件检查保留。

AI工作区仍从未配置状态开始；请求中的比较策略不安装配置、不读取密钥、不探测服务器。用户在原界面显式输入本次配置和必要凭据，每次外发与应用继续原完整审阅。工作区前片配额约束实际匹配收件人；当前UI的输出token/超时使用原默认值，请求若设得更小，实际配置将被拒绝发送，而不是自动放宽预算。诊断消耗尝试但不进入authoring报告；额度不是全局网络、实际HTTP数、金额上限或资源沙箱。

运行仍走原MeasurementRunDriver/ApplicationLifetime；原帧边界时长/帧数结束、窗口关闭/故障及有序关闭规则不变，无自动恢复、强制终止或关闭重试。成功关闭前原宿主封存工作区，同时记录初始/终态规范场景字节SHA，不写场景内容。失败关闭显式封存未排空前缀，但不取消、Pump、排空或重试；静态宿主/驱动/租约继续保留，同进程再进入Main拒绝。业务错误不能使finally跳过清理；输入句柄仅在原宿主成功关闭后释放。进程退出不等于资源成功关闭。

`AiSceneCandidateEvidence` v1最多56MiB，包装原40MiB联合字节、请求/来源场景/策略SHA、原输入检查回执与入口PID/文件SHA、原驱动/预算、初终态场景SHA、外层资源保留及类型名错误。原联合和进程消费者重验；检查身份、输入、PID/完成帧、预算及关闭矛盾。启动失败允许明确缺联合证据，不伪造空成功。Decode在反序列化前限制深度8/512token、无数组、普通字符串1024/数字32字节，仅根JointEvidence放宽base64，再规范重编码比较；DecodeExpected另要求外部准备对象的请求/场景/策略/回执。它不是签名、持续新鲜性或真实执行证明，PerformanceAccepted/RepresentativeGameplay/GrantsExecutionAuthority/ProvenanceVerified恒false。56MiB不是峰值内存保证；工作区/进程窗口不拼成共同时间轴。

出口只使用原受检CreateNew/Flush/同句柄哈希读回，输出前再次禁止与输入树重叠；不预约、覆盖、建父目录、重试或换路径，冲突/写入失败保留原证据并返回4。退出0仅为这次诊断循环、关闭和证据写入正常结束，不能推出采样完整、已执行AI请求、模型结果正确、长稳通过或目标机性能达标。模型/采集失败仍由原嵌套报告分列，不能只看进程退出码判验收。

新增5组AIContracts、2组原EditorService CLI/Presenter、3组AI HostLifetime，以及组合变体1组明确拒绝（与AI版3组互斥）；共11个独立新case体、主AI路径10组，沿原60/180/HostLifetime60秒入口，全部留F。真实宿主用例依赖原native/font，不缺失跳过；仅HTTP使用原替身。静态审查不作为功能结果。下一步准备匹配独立候选与完整F输入，再按独立批准的人工作负载/模型预算运行；E5-D大世界内容仍延期，原PS5.1 24项/配置在F主体终态与报告之后。
