# M8：可选 AI 模块与深度引擎扩展

基线：M7.7 `98af7c08a6659f95e8156f3cfd6ae48cf5c1452a` 已完成自动候选交付。
M8 已启动；本文件是实施与验收范围，不代表已接入推理。既有人工、目标机器、自包含、完整性能、1h 与历史故障调查仍保留。

## 固定边界

- C# 持有游戏规则、World、编辑业务、权限与唯一事务/Undo。Python 仅为独立可选模块。
- AI 深度参与场景、资产、动画、UI、工具和托管扩展；MCP 不是推理服务，连接器也不自动获得已有 MCP 权限。
- 输入是已批准、限定范围的复制观察；输出只是结构化提案。模型不能批准自己的修改或扩大读范围。
- 异步工作不进入 simulation/render tick；超时与取消不等于能强制终止同进程代码，也不等于撤销已发生的外部副作用。
- 无任意 eval、shell、程序集路径或第二份历史。代码修改、进程启动、依赖安装、网络外发、模型付费与 Git 发布分别审批。
- 不引入 Python Gameplay、C++ World、网络组件、隐式后端回退或旧格式兼容。

## M8.1：传输中立契约与受限异步会话

交付：独立纯 BCL `Ncma.Modules`；不可变复制请求/提案，v1、请求 UUID、模块/配置/schema 身份、上下文与 SHA 指纹；可信启动注册操作目录；owner-thread inbox，后台 adapter，超时/取消/背压/失效代次/单次领取。

预算：64 操作、64 KiB 单份 JSON、深度16、16 outstanding、1–4 活跃请求、4096 会话终身请求 ID、1–60000ms 请求时限（含排队）。重复属性拒绝；结构验证不冒充领域 JSON Schema 审批。目录注册不等于 Editor capability grant。

当前策略是 authoring 精确上下文；文档代次/revision/项目/session/World/tick 变化使旧结果失效。权限撤销即使 revision 不变也必须调用 Invalidate。之后任何提案提交仍需领域服务重新核验；领取提案不是许可。运行时 tick 容忍窗口须单独设计，不将旧观察默认为当前状态。

慢 adapter 与取消回调保留其活跃槽直到结束，不能通过超时/取消无限新增后台任务。Stop 后拒绝新请求，TryClose 非阻塞收敛，未收敛时保留 adapter/transport 所有权；无自动重试。可信 adapter 不是内存/权限/CPU 沙箱。

测试：数据别名、大小/深度/重复键、目录错误、同步前置阻塞、乱序、异常、伪造回复身份/指纹/schema、超时迟到、取消回调阻塞/抛出、换场景/revision/revoke、队列与终身预算、owner-thread、关闭与无 native/Python 依赖。测试 adapter 明确是测试替身，不是实际服务。

完成门禁：针对性 Debug/Release + 原有契约回归；纳入 Build.bat/CMake/解决方案；最终同一源码完整无 Skip Debug/Release、原验收不降级，方可阶段提交推送与受检部署。当前实现进展/实际测试见文末。

## M8.2：一个真实独立 worker 传输

优先评估当前官方 grpc-dotnet + Python gRPC；选版本并锁定依赖后只实现这一种。握手协商、最大消息/请求预算、默认 loopback、会话认证、显式启动/退出、deadline/取消、断连与有限重启；禁止匿名远程监听。协议解码严格拒绝未知版本/字段与超界消息。

先提供确定性的真实 Python worker 验证运输，不把它称为模型推理。测实际跨进程大小/延迟/取消/退出/进程回收。无 Python 环境仍可编辑与运行普通 C# 项目。pythonnet/ZeroMQ 保持未实现。

### M8.2 实施顺序与必须证明的边界（设计，尚未实现）

1. 独立可选 transport 项目引用 `Ncma.Modules`，不反向增加核心依赖；Python worker 使用单独依赖锁与受检启动配置。先核验 SDK、运行时和包的兼容版本，记录包来源与哈希；不得把机器上已有的全局 grpcio 当成产品已部署，普通项目启动不得自动安装依赖或联网。
2. 固定有版本的握手与调用协议。握手绑定本次 worker 实例、目录/模块/配置/schema 身份及预算，业务调用仍回传 M8.1 request/session/epoch/fingerprint。拒绝未握手、错误实例、缺失字段、未知枚举、重放和超界数据；不要用生成类解析成功冒充严格协议验证。Proto3 默认保留未知字段、重复单值字段可能采用后值，因此严格拒绝须在真实入站字节上单独验证并跨语言测试，而不能只检查解析后的 DTO。[Protobuf 官方语言指南](https://protobuf.dev/programming-guides/proto3/#unknowns)
3. 仅显式本地启动：精确已批准的解释器与 worker 文件，固定参数列表，不接受 shell 命令或用户提供的模块路径。监听字面 loopback 地址、每次新实例独立凭据，验证服务端身份，不关闭证书检查；具体 TLS/引导通道在实现评审中冻结。凭据不进命令行、普通日志或场景文件；本地身份认证不是同用户恶意进程/操作系统级安全沙箱。远程模型连接不在此阶段偷偷启用。
4. 两端显式限制原始收发大小、解码后的字段/深度/数量、活跃 handler、排队与进程输出缓存。库会话超时覆盖排队至领取；传输必须使用剩余预算或取消信号，不在派发后重新给予完整时限。gRPC 默认没有 deadline，且 RPC 取消不保证应用 handler 已终止；因此要保留实际 worker 工作预算，不能仅凭客户端完成就无限补发后台工作。[gRPC deadlines](https://grpc.io/docs/guides/deadlines/)、[cancellation](https://grpc.io/docs/guides/cancellation/)
5. 启动、就绪、停止和进程退出都要有界且在帧循环外执行。使用持有的子进程身份，不按进程名杀死其他 Python；异常退出后撤销会话并使旧回复失效。重启只能由宿主显式发起、有次数预算并产生新身份，不自动重放请求；关闭失败如实保留故障状态与诊断，不假称已卸载。输出持续有界消费，避免 stdout/stderr 管道撑满造成死锁。
6. 验收先跑实际 C#→gRPC→Python→C# 的确定性操作，再测坏握手/认证/字段/边界、服务端过载、取消但 handler 未退出、断连/退出/重启、迟到回复、关闭和无 Python 路径。测试双端收发字节与身份，证明不是同进程替身；追加移动到独立目录后的真实 worker 测试。M8.1 关闭并提交推送后才开始实现；M8.2 不发布模型已接入、领域修改已批准或全平台支持的声明。

## M8.3：深度领域提案闭环

接入既有 scene/assets/animation/UI 工具目录与只读快照权限，避免重建第二个网关。绑定项目、session、文档代次/revision、资产 UUID/generation/SHA，纯 dry-run/diff，精确本地审批，调用已有唯一事务与 Undo；逐域明确跨文件提交与失败补偿边界。

验收真实编辑服务链：读批准范围→复制提案→拒绝越权/陈旧/重复→审批→原事务→Undo/Redo→原测试证据。删除与可执行代码权限独立，不靠模型输出布尔值放行。

## M8.4：模型连接与正式 AI 面板

用户选本地或远程推理。连接凭据、上下文外发、下载与费用单独确认。面板显示实际连接状态、正在访问的范围、提案/diff、取消与审计；无服务时如实保持未接入。限制上下文/输出/步骤预算，日志不泄露密钥或完整敏感提示词。

验收真实模型连接（需要用户服务选择/凭据），恶意文本不能执行工具或授予权限，取消/重连不误提交旧提案。不用测试替身代替真实模型验收。

## M8.5：受控托管扩展

为组件/schema、System、面板、导入器、工具建立版本、依赖、owner-thread、注册/注销与资源寿命契约；基于已存在注册机制实现，不增加通用反射/任意程序集加载端点。扩展源码与执行需独立审查；已批准可信扩展也不称为安全沙箱。

验收依赖冲突、重复注册、卸载仍被引用、异常隔离、停止/重载后的旧回调失效，以及正常 C# gameplay 无模块路径。

## M8.6：开发任务与验证证据

结合现有 M6 workflow ledger 实现有界开发任务：指定项目文件补丁→人工审查→固定构建/测试操作→结构化证据。白名单操作不接受任意 shell 文本。明确每步提交范围、停止点、取消与部分成功，不自动 Undo 后来的用户修改，不假装多领域事务天然原子。

验收场景/动画/UI 与一个托管扩展任务、失败修复预算、取消、审批到期、工作目录越界、测试失败不发布；提交推送始终是独立用户授权动作。

## M8.7：联合交付与可选部署

正式 Editor 深度闭环与可选 Player runtime adapter；严格搬移包/源文件锁定、无 Python/无模块、真实 worker 异常退出/重启、长时间慢请求/队列背压。测请求字节、吞吐、p95/p99、线程分配、降级频率与对游戏帧影响；仅报告实际证据。

最终同源码完整 Debug/Release、ABI/格式拒绝/MCP/Python inspect/原 M6–M7 GPU 回归、审计与可恢复部署通过后提交推送。人工推理服务/外发/目标机与长跑门禁逐项记录，不以自动测试冒充通过。完成 M8 后进入 M9：按2026-10-15用户确认，本版本仅Windows发布；微信小游戏、Android、iOS在后续版本实现。M8模块/扩展契约保持平台无关，不提前实现其他平台发布或将其算作本版验收前提。

## 当前进展

### M8.1 自动候选完成（2026-10-16）

最终同一份883源码/test/build库存顺序无Skip Debug THIRD、Release SECOND均完整成功，session37429 exit0。每配置13原生、73托管/图形/产品CTest、Python45、原格式拒绝/ABI/MCP/smokes/inspect、三轮运行时测量、前后审计和受检原子部署通过；原生耗时9.96/8.67s，托管1124.77/1025.95s，编译零警告。新 `NcmaModuleTests` 两配置均16组8364检查。

Debug候选 `ca6dc20af8714f6894957079c754acf6`、Release候选 `c9826b8d031a4975bc6b7eea46d1847d`；两配置五包均144/56/113/67/116清单文件校验通过。正式Release Complete `b5e91a8f84654161879c4a3ac857ffe3`，144清单/148实际/148备份逐文件SHA/size一致；备份对应最终Debug候选，Debug Complete归档 `8afa7d41130e4a37aaafe1cd40f2d332` 保留。真实 `out/bin/NcmaEngine.exe --validate-package` exit0；EXE SHA `3C1177C83B0AC9F4B6F373BAB10BDDDD4CD8284B9AF4DF4277B932CA41FB7521`，Release DLL SHA `858B575AF923478BE6B968ED37DCAE8DAA86A23AAC7D83C66A9BA628DAAA45DB`。

证据：`out/m8-1-full-debug-third.log`、`out/m8-1-full-release-second.log`、`out/m8-1-final-package-audit-{debug,release}.log`、`out/m8-1-final-delivery-verification-fourth.log`。最终附加核验第一次受Git全局ignore沙箱读取限制，第二/三次证实Windows PowerShell5.1把JSON顶层数组再包装成一项（实际883/包装1）；修正仅验证脚本的数组赋值后第四次通过，883条及全部断言未变，产品源码未改。所有失败日志保留。完整Release ShadowAnimation `8697b89a4a9f458cb168c614022c73ed` 实际经历1次Busy，16组各64成功提交/测量区间分配0，512图像对max1/API0/0；仍非无等待整帧/完整性能验收。

范围仅为独立纯C#模块基础库及受限异步会话。正式Editor/Player不依赖该库；gRPC/Python worker、推理、领域提案应用和完整M8尚未实现。人工、真实服务/外发、目标环境、自包含、完整性能、1h与历史File.Replace根因调查仍开放。阶段提交推送须精确排除IDE与生成物；核对远端后再开始M8.2，不因本节完成声明关闭整个引擎目标。

### 历史验证与恢复记录（不覆盖上述最终结果）

最终无Skip Debug THIRD 完整通过：13原生9.96s、73托管/产品1124.77s、Python45（1.522s）、三轮profile与前后审计、原子部署均成功，日志 `out/m8-1-full-debug-third.log` 末尾Build succeeded。候选 `ca6dc20af8714f6894957079c754acf6`，Complete `8afa7d41130e4a37aaafe1cd40f2d332`，144清单/148实际/148备份SHA及真实apphost validate0已核验；五包独立完整性审计 `out/m8-1-final-package-audit-debug.log` 通过。883冻结源码SHA未变。原顺序session37429继续执行最终无Skip Release SECOND，日志 `out/m8-1-full-release-second.log`；Release尚未完成，不能提交或关闭M8.1。之前部署失败与单独恢复的记录仍保留，不改写为成功。

最新恢复结果（2026-10-15）：用户批准的原流程重试成功，session80521 exit0，日志 `out/m8-1-debug-deployment-approved-retry.log`。沿用已完整测试的Debug候选 `20bcbbe9146848a4a685183d5885f109`、原 `Invoke-EditorDeployment` 和前后审计，无产品脚本或原子性降级。新Complete `9dd6898725ca42ce8ede1e07e7f59c1d`；144清单/148实际/148备份逐文件SHA/size及真实隐藏apphost validate0通过，原失败stage和两份同SHA证据完整。当前EXE SHA `3C1177C83B0AC9F4B6F373BAB10BDDDD4CD8284B9AF4DF4277B932CA41FB7521`。原失败Build仍记失败，根因未证明。

Release无部署轮85027 exit0：13原生(8.79s)/73托管(1034.84s)全部通过；全日志无失败标记，883源文件和solution SHA未变。该轮SkipPython仍不算完整Release交付。接下来按原Build.bat顺序执行最终无Skip Debug THIRD / Release SECOND（`out/m8-1-full-debug-third.log` / `out/m8-1-full-release-second.log`）；任一失败立即停止后续配置。最终双配置门禁、提交推送仍待完成，人工/目标环境/完整性能/长跑门禁不变。

2026-10-15：M8.1 基础库和16组8364检查已实现并通过独立Debug/Release，零编译警告；无实际传输、worker、推理、领域提案提交或生产入口接线。已保留并修复“完成但未领取的提案不过期”红色测试。

完整无Skip Debug FIRST 已通过13原生（10.04s）+73托管/产品（1131.19s）+Python45、三轮运行时测量、前后审计与可恢复部署；候选 `e4703edcd1d741709a09049cf9c82c71`，Complete `22c3db6462054e19b5dcacee64fecb13`。五包144/56/113/67/116清单校验，正式144清单/148实际/148备份SHA和实际apphost `--validate-package` exit0；Modules未进入正式包依赖。第一次PowerShell GUI退出码采集未等待进程，校验脚本已改为显式隐藏启动+Wait/ExitCode后通过，没有放宽产品校验。

初始883份source/test/build库存双轮间未变，解决方案另核SHA `135A4FDAAA8F62736EEE2B63E1DFEA1BB59EBD9E0610ABF41389E6AC30F64E54`。Release FIRST 最终13原生通过，托管72/73通过，唯一失败为旧 `ShadowAnimation` 预热的 `All bounded palette slots are in use`；总1032.28s，构建中止，未执行该轮Python/审计/部署。保留 `out/m8-1-full-release-first.log`，不将日志尾部的后续通过项当成完整通过。70339已exit1，93795已exit0。

定位：query5契约明确三槽满允许无副作用Busy，原64帧预热循环错误使用必成功UpdateSkins。修复仅限该测试：TryUpdateSkins先尝试，Busy核验frame/batch/palette/copied无变化，在测量区间外调用已有有界WaitIdle，最多重试一次；仍须64实际成功skin/geometry/caster提交、精确palette字节、零测量区间分配及全部512图像对比。报告新增attempt/busy/drain/含等待wall时间，CPU值明确仅active submission attempts，不伪称无等待整帧性能；生产C#/native代码、原超时/图像阈值均未改。

针对性Debug4056/Release4057检查通过、各512图像对max1/API0/0；Release实际一次Busy分支、每组仍64成功/分配0。证据 `shadow-backpressure-debug/2d5938092b9e48758c9cf11ffd7137fd` 与 `shadow-backpressure-release/5f6dc73a182f4135b401081718f58434`；两次编译0警告。旧完整Debug并非最终测试源版本，必须重跑双配置。

重新冻结883份 `out/verification/m8-1/source-freeze-second.json`，与初始清单唯一差异是 `managed/Ncma.Rendering.Tests/ShadowAnimationTests.cs`。Debug SECOND 的13原生(10.12s)/73托管产品(1131.06s)/Python45(1.499s)/3轮profile与前置审计均通过，阴影动画亦通过；但正式部署发布Prepared日志时File.Replace失败，40741已exit1，顺序Release SECOND未启动。保留 `out/m8-1-full-debug-second.log`，不得把全部测试通过等同完整Build/交付通过。

失败发生在正式目录Move之前：旧Complete `22c3db6462054e19b5dcacee64fecb13`、144清单/148实际/148旧备份SHA和真实apphost validate0均完整；旧journal SHA `4C6E59A24287E67B1ABC8780ED08CCD4ADC323679CFE799AE0C786E6EC60F3A8`。新generation `413c8aed9ffd4275b16e53c48dad9cb2` 仅staging存在、无backup，tmp phase Prepared SHA `EBC847A9C820DE743CAAAF8E87792058229D30224FD6896E389B24095E38A577`；旧日志属性Archive，事后只读独占打开成功，但不证明失败原因已消失。两日志原始字节已无覆盖复制到本阶段 `deployment-failure-old-complete.json` / `deployment-failure-prepared.json` 并核验同SHA。未修改原子替换代码、未删除或强制覆盖文件；根因仍未证明。

用户于2026-10-15明确回复“允许按原流程重试”：允许保留失败证据后，仅按现有受检原子部署流程重试，不删除日志、不强制覆盖、不降低校验。先等待当前 `Build.bat -Configuration Release -SkipPython`（session85027，日志 `out/m8-1-release-regression-no-deploy.log`）结束，避免并发构建；该标志保留原生/托管完整测试但禁止正式部署，不能宣称完整无Skip Release门禁通过。随后恢复原流程及最终完整Debug/Release门禁；若再次发生文件操作失败，停止并报告，不改用更强删除或覆盖手段。M8.1尚未完成最终交付、未提交推送；M9 Windows-only仅文档调整，不提前实现M9。所有旧进程93795/70339/40741均终止，勿再次等待或自动重启。
