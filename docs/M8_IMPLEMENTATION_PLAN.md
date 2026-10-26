# M8：可选 AI 模块与深度引擎扩展

2026-10-26 最新实施：**M8.4-E实现/静态准备基线已具备，下一步F整体统一验收**。新增同源/同配置编译回执，搬移探针不再只信DLL路径；默认AIContracts缺口已补建。可选Editor/Services及AIContracts双配置编译0警告/0错误；静态库存各152，原151项不变。修复排序/参数展开两轮准备失败并保留证据，全部测试未运行。F仍须准备全解决方案/原生/worker/Sample/实际包，模型与真人验收独立；EXE不变，无部署/Git写入，E5-D延期。[当前记录](M8_4_IMPLEMENTATION_PLAN.md#当前e编译来源回执与f执行准备) · [F执行清单](M8_4_F_EXECUTION_CHECKLIST.md)。

2026-10-26 最新实施：**M8.4-E受检搬移产品宿主探针与输入合同已登记/编译，未运行**。新增6个实际产品宿主编辑/Play/Undo入口和28个输入合同case（3组），默认/可选Debug均0警告/0错误；静态CTest双配置各151，原142项不变。与原真实EXE启动入口分别取证，不称真人验收。下一步E完整同源输入/探针来源核对，再F统一执行；无包/模型/部署/Git写入，EXE不变，E5-D延期。[当前范围](M8_4_IMPLEMENTATION_PLAN.md#当前e受检搬移产品宿主与输入合同)。

2026-10-26 最新实施：**M8.4-E真实apphost搬移与离线宿主矩阵已登记/编译，未运行**。4条搬移启动/退出入口和4组实际宿主离线编辑/Play/Undo矩阵分开取证，不混称搬移编辑闭环或真人验收。双配置静态CTest各142，原137项未改；实际可选/默认Editor+Services Debug均0警告/0错误。下一步受检搬移产品宿主编辑/Play探针及来源/指纹负例，之后F统一测试；无实际包/模型/部署/Git写入，EXE不变，E5-D延期。[最新范围](M8_4_IMPLEMENTATION_PLAN.md#当前e真实apphost搬移与离线宿主增量)。

2026-10-26 最新实施：**M8.4-E候选打包流程/清单合同增量完成，纯静态检查，E未完成**。显式AI变体复用本代默认原生/资产闭包，隔离托管publish、前后输入SHA、独立索引及严格文件/依赖合同；新增17个合成清单用例未执行。PowerShell5/7解析通过，CTest双配置各137项，旧136项不变；未生成包/cook/publish/测试。下一步真实搬移、无服务/无Python及实际Editor编辑/Play宿主门禁，再F统一执行。正式EXE未变、无提交推送，E5-D延期。[实现与限制](M8_4_IMPLEMENTATION_PLAN.md#当前e候选包与清单合同增量)。

2026-10-26 最新实施：**M8.4-E隔离验证目标与静态库存增量完成，E未完成**。4个可选AI CTest入口覆盖25组实际Editor夹具；Debug/Release新目标均0警告/0错误，双配置库存各136，旧131项命令/属性不变，未执行测试。AIContracts默认路径DLL缺失已显式记录，F前重建，不跳过。真实服务8项队列/拟议预算仅文档，尚未获实际调用授权。下一步E同源候选发布与搬移/无服务/无Python正负例准备，再F统一验收；EXE不变、无部署/提交推送、E5-D延期。[库存与交付准备](M8_4_IMPLEMENTATION_PLAN.md#当前e测试库存与交付准备)。

2026-10-26 最新实施：**M8.4-D领域presenter新增9组夹具登记/编译，A–D有界实现与夹具基线可进入E准备，未验收**。真实UI/资源浏览器/动画界面事件覆盖完整外发字节、独立资源/差异批准、原Commit/Undo/Redo、撤权/文件变化/取消；动画仍保留原真实MCP读grant，无新权限。完整可选Services+Editor16.75秒/默认Services+Editor9.55秒/AI.Tests3.70秒Debug0警告/0错误。所有用例未执行，无GUI/IPC/模型/部署/Git写入，EXE不变。下一步E清单、候选包/搬移和固定验收预算准备，再F统一测试；E5-D延期。[领域事件与E衔接](M8_4_IMPLEMENTATION_PLAN.md#当前d领域presenter矩阵与e衔接)。

2026-10-26 最新实施：**M8.4-D原生凭据边界/真实宿主夹具登记，关闭守卫修补并编译，D未完成**。凭据仍用原Windows API，内部替身覆盖6组清零/取消/错误/重入；2组真实CPU GUI/DX11宿主夹具覆盖保留资源、实际排空和幂等关闭。宿主关闭意图提前到AI等待前，阻止未排空时恢复帧循环/图形恢复/重启。两种完整Services+实际Editor Debug8.75/7.93秒0警告/0错误；所有用例未运行，无原生GUI/模型/部署/Git写入，EXE不变。下一步D其余领域presenter矩阵，再E/F统一测试；E5-D延期。[边界与关闭记录](M8_4_IMPLEMENTATION_PLAN.md#当前d凭据边界与真实宿主关闭增量)。

2026-10-26 最新实施：**M8.4-D实际Presenter事件夹具已登记并编译，D未完成**。真实GUI包/事件/快捷键入口覆盖8组可选AI场景/诊断/审批/取消矩阵，默认产品另1组无AI入口；专用入口在默认构建明确失败，不跳过宣布通过。完整Services+实际Editor两个隔离变体最终Debug4.41/4.50秒，均0警告/0错误；首次恢复TLS和夹具unsafe/async编译失败源码/日志保留。全部用例未运行、无模型调用/部署/Git写入。下一步D原生凭据及完整宿主关闭顺序夹具、其余领域界面矩阵，再E/F统一测试；E5-D延期。[事件夹具边界](M8_4_IMPLEMENTATION_PLAN.md#当前d实际presenter事件夹具增量)。

2026-10-26 最新实施：**M8.4-D显式连接诊断实现并编译，D未完成**。独立固定请求不附带项目数据，完整正文/接收方/预算审阅后单次人工批准；最多64请求输出token、10秒、16KiB响应，无自动探测/重试/领域写入。取消与撤权先于实际排空，忽略取消的transport保留；成功仅表示本次协议回复。新增8组AI夹具（累计81）未运行；最终可选Editor3.66秒/默认完整Services含Editor8.29秒/AI.Tests3.64秒Debug均0警告/0错误。下一步D实际presenter/宿主/原生凭据夹具，然后E/F统一测试；无模型调用、部署或Git写入，EXE不变，E5-D延期。[诊断边界](M8_4_IMPLEMENTATION_PLAN.md#当前d显式连接诊断增量)。以下为历史切片。

2026-10-26 最新实施：**M8.4-D元数据独立外发审批/settings桥接实现并编译，D未完成**。精确typed观察、完整路径/record/HTTP正文预览与原一次外发批准；模型只返回UUID+settings，宿主构造原metadata命令，再独立人工差异确认/Commit，原文件与写scope不变。新增8组Services用例未运行，可选Editor19.27秒/默认Services10.77秒/AI.Tests4.21秒Debug均0警告/0错误；2次NU1900失败保留，正常恢复未关闭审计。下一步D连接诊断、实际GUI/宿主/原生凭据夹具，再E/F统一测试；EXE不变、未部署/Git写入，E5-D延期。[桥接边界](M8_4_IMPLEMENTATION_PLAN.md#当前d元数据外发与settings桥接增量)。

2026-10-26 最新实施：**M8.4-D元数据本地完整审阅实现并编译，D仍未完成**。实际可选Editor精确根UUID内容捕获、完整缓存分页/显式文件校验；来源/clock/期限/线程/取消绑定，原已导入资产限制不放宽。仅本机查看，尚无typed metadata外发观察和settings草稿转换；不得使用旧摘要授权发送完整路径/record。新增6组用例未运行，默认完整Services24.67秒/可选Editor10.41秒/AI.Tests8.00秒Debug0警告/0错误，NuGet失败证据保留。下一步补独立完整外发批准+原写事务桥接，再连接诊断/实际GUI宿主夹具及E/F统一测试；正式EXE未变，E5-D延期。[本次边界](M8_4_IMPLEMENTATION_PLAN.md#当前d元数据本地内容增量)。

2026-10-26 最新实施：**M8.4-D动画摘要/资源面板实现、编译，D未完成**。现役图工作区接入可选AI产品；精确当前图摘要（默认重命名）外发、完整候选与原资源独立审阅/精确UUID确认、再单独原事务差异/Commit，不自动授权/配对/下载。新增6组Services用例未运行，最终可选Editor12.00秒/默认完整Services11.02秒/AI.Tests4.90秒Debug均0警告/0错误。元数据原读接口仅脱敏摘要且写切片不支持已导入记录，须补精确内容授权后再接UI；不从浏览器缓存隐式导出路径。下一步仍D元数据、连接诊断和实际GUI/宿主夹具，F统一测试；EXE未变、未部署/Git写入，E5-D延期。[动画面板记录](M8_4_IMPLEMENTATION_PLAN.md#当前d动画工作区增量)。

2026-10-25 最新实施：**M8.4-D远程凭据增量实现/编译，D未完成**。可选Editor新增固定DeepSeek远程入口和Windows原生会话密钥输入，不经过GUI Text包/缓存，不保存或自动查找；撤权先于排空，不合作transport保留，取消输入不恢复旧key。认证头单独告知，原两次人工审批不变。不是安全桌面或物理内存擦除承诺。新增8组（AI累计73）未运行；最终AI可选Editor10.22秒/默认完整Services6.81秒/AI.Tests5.13秒Debug均0警告/0错误；首次NuGet失败保留。无测试/模型调用/部署/Git写入，原生对话框尚未实际验收。**下一步D元数据/动画面板、连接诊断及实际presenter/宿主夹具**，F统一测试，E5-D延期。[远程凭据边界](M8_4_IMPLEMENTATION_PLAN.md#当前d远程凭据增量)。以下为历史切片。

2026-10-25 最新实施：**M8.4-D本机工作区首增量实现/编译，D未完成**。正式Editor编译期开关与可信宿主装配、未配置默认、场景/UI任务、完整逻辑+HTTP正文外发审阅、独立完整差异确认、取消/回收/回执已接线；默认产品依赖不增加AI。GUI仅无凭据loopback，远程安全密钥UI/元数据/动画面板/真实GUI宿主夹具仍待续做。新增7组（AI累计65）未运行；最终AI可选Editor13.52秒/默认完整Services11.28秒/AI.Tests4.61秒Debug均0警告/0错误。无测试/模型调用/部署/Git写入，F统一测试，E5-D延期。[D交付边界](M8_4_IMPLEMENTATION_PLAN.md#当前d本机工作区首增量)。以下为历史切片。

2026-10-25 最新实施：**M8.4-C UI增量实现/编译，四类受限转换已接线但未验收**。精确本机UI选择与原外发审批不依赖MCP；模型有界definition经原UiCodec/资源/C# Action校验，宿主生成原提案并保留单独人工Commit/Undo。提交前内容检查与原Publish内部身份检查分开（UI/动画），原权限不变。新增18组（AI累计58）未运行；最终AI8.48秒/完整Services+Editor9.47秒Debug0警告/0错误，首轮NuGet失败保留。**下一步D正式可选产品/AI工作区**；动画仍要求原配对MCP图读grant，全部测试留M8.4末，无模型调用/部署/Git写入，E5-D延期。[UI交付边界](M8_4_IMPLEMENTATION_PLAN.md#当前c-ui增量交付)。以下为历史切片。

2026-10-25 最新实施：**M8.4-C动画增量实现/编译，C仍未完成**。模型只提供有界图操作，宿主生成原候选句柄；独立显式资源审阅/批准后转原领域差异和最终Commit，默认不自动加载资源/执行。原配对MCP图读授权仍是前提，不宣称无MCP动画闭环。新增3组AI+6组Services用例未运行；完整Services24.37秒/AI7.88秒Debug0警告/0错误。下一步C UI草稿转换与精确观察授权，随后D；无模型调用/部署/Git写入，E5-D延期。[动画交付边界](M8_4_IMPLEMENTATION_PLAN.md#当前c动画增量交付)。

2026-10-25 最新实施：**M8.4-C首增量实现/编译，C未完成**。可选AI场景/元数据工作流已连接实际adapter、原mailbox、发送审批与原领域提交；回包后继续检查原观察/配置/取消来源，注销须实际排空。新增10组（累计37组）用例未运行，最终AI5.93秒/完整Editor服务8.68秒Debug0警告/0错误。下一步仍是C动画/UI草稿到原Prepare及资源审批；D正式UI未装配，无模型调用/部署/提交推送，E5-D延期。[C首增量记录](M8_4_IMPLEMENTATION_PLAN.md#当前c首增量交付)。

2026-10-25 最新实施：**M8.4-B实现/编译，未测试**。DeepSeek真实HTTP adapter、有界编码/读取、精确发送预览、会话凭据作用域、剩余deadline/取消/回收和严格提案解码已接入可选库；新增13组（累计27组）待整体末测试。AI/完整Editor服务Debug编译均0警告/0错误，无真实HTTP/模型调用。下一步C领域桥与动画/UI草稿转换；见[B交付边界](M8_4_IMPLEMENTATION_PLAN.md#当前b阶段交付)。正式EXE/Git未变，E5-D延期。

2026-10-25 最新实施：M8.4-A按用户指定DeepSeek完成配置/完整发送审批合同和Debug编译，支持本地/远程配置及其他供应商显式扩展，尚无推理adapter。14组用例已登记未运行，统一测试仍在整个M8.4末。[A交付与B边界](M8_4_IMPLEMENTATION_PLAN.md#当前a阶段交付)。模型端点/凭据/外发/费用待实际调用前确认；E5-D继续延期。

2026-10-25 最新安排：用户再次暂缓E5-D验证，转为准备 **M8.4方案**。[详细实施方案](M8_4_IMPLEMENTATION_PLAN.md)划分A–F，复用M8.3原授权/提案/事务链，仅接入一个用户指定的真实服务。目前仅规划，未实现/联网/测试；小阶段全部实现后统一测试。E5-D大世界探索方向已确定，但项目/路线/目标机未就绪，性能未验收。

2026-10-25：**M8.3 A/B/C/D已通过本机双配置完整自动验收**，范围及证据见[交付记录](M8_3_DELIVERY_REPORT.md)。实际推理仍未接入；M8.4需明确服务选择/授权，不自动开始。正式EXE未部署，未提交推送。

2026-10-25 用户要求整个M8.3：A/B/C/D候选现已实现，进入整体统一测试，详见[M8.3当前状态](M8_3_IMPLEMENTATION_PLAN.md#当前状态)。支持原场景、资产元数据、已准备动画/UI事务的明确审批闭环；实际推理属于M8.4。未宣称测试通过，未部署或提交推送；下方“首增量/未实现”描述为先前记录。

2026-10-25：E6 本机双配置完整自动门禁通过后，用户明确启动下一阶段，现恢复 **M8.3**。[详细实施方案](M8_3_IMPLEMENTATION_PLAN.md)将工作拆为编辑来源/预检、场景原事务确认、资产/动画/UI、正式宿主接入与验收准备。首增量不连接推理、不授予权限、不执行提案；其余能力继续标记未实现。E5-D及人工/环境门禁未豁免。

2026-10-23 最新执行调整：E5-D 剩余测试延期，先实施 E6-A/B/C，E6 整体实现结束后统一测试。所有整体阶段的小阶段测试集中在阶段末，开发中仅做必要静态/编译检查、准备测试用例，不执行片间测试或标记验收通过；M8 后续阶段同样适用。下方历史“E5 收尾后才能开始 E6”不再作为启动条件。E5 未完成债务及既有安全/权限/人工验收门禁仍保留，具体顺序以 [路线图](ROADMAP.md) 为准。

2026-10-16 用户确认顺序调整：**M8.2 收尾 → ECS 重构 → M8.3 及后续 → M9**。当时规划 E0–E5 完成后恢复 M8.3；该段“尚未实施”属于当时状态，实际进度见路线图及 ECS 交付记录。

2026-10-23 路线图追加：在 E0–E5 收尾后、M8.3 前规划 **E6-A 编辑模型与运行模型分离 → E6-B 元数据与访问代码生成 → E6-C 声明读写依赖的任务调度**。三项完整能力未实现，详见 [E6 架构演进](ROADMAP.md#e6-架构演进新增未实现)。M8.3 领域提案接入分离后的编辑模型、生成描述符及原授权事务；不通过模块/AI 绕过任务访问声明或直接写 live World。本次只更新规划，原验收/权限和当前 E5 冻结不变，历史“E5 后直接开始 M8.3”的顺序以新增路线图为准。

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

交付：独立纯 BCL `Ncma.Modules`；不可变复制请求/提案、请求 UUID、模块/配置/schema 身份、上下文与 SHA 指纹；可信启动注册操作目录；owner-thread inbox，后台 adapter，超时/取消/背压/失效代次/单次领取。M8.1最初为v1；当前已由M8.3升级为严格v2编辑来源，不保留旧服务别名。

预算：64 操作、64 KiB 单份 JSON、深度16、16 outstanding、1–4 活跃请求、4096 会话终身请求 ID、1–60000ms 请求时限（含排队）。重复属性拒绝；结构验证不冒充领域 JSON Schema 审批。目录注册不等于 Editor capability grant。

当前策略是 authoring 精确上下文：项目/宿主/EditSession/DocumentSession、文档代次/revision、脚本目录代次、reviewEpoch、选择、端点/配对代次均绑定。旧World/tick字段已移除，不虚构运行身份。权限撤销即使revision不变也失效，并由领域桥重查原读授权；领取提案不是执行许可。运行时观察须另行授权，不混入编辑提案。

慢 adapter 与取消回调保留其活跃槽直到结束，不能通过超时/取消无限新增后台任务。Stop 后拒绝新请求，TryClose 非阻塞收敛，未收敛时保留 adapter/transport 所有权；无自动重试。可信 adapter 不是内存/权限/CPU 沙箱。

测试：数据别名、大小/深度/重复键、目录错误、同步前置阻塞、乱序、异常、伪造回复身份/指纹/schema、超时迟到、取消回调阻塞/抛出、换场景/revision/revoke、队列与终身预算、owner-thread、关闭与无 native/Python 依赖。测试 adapter 明确是测试替身，不是实际服务。

完成门禁：针对性 Debug/Release + 原有契约回归；纳入 Build.bat/CMake/解决方案；最终同一源码完整无 Skip Debug/Release、原验收不降级，方可阶段提交推送与受检部署。当前实现进展/实际测试见文末。

## M8.2：一个真实独立 worker 传输

优先评估当前官方 grpc-dotnet + Python gRPC；选版本并锁定依赖后只实现这一种。握手协商、最大消息/请求预算、默认 loopback、会话认证、显式启动/退出、deadline/取消、断连与有限重启；禁止匿名远程监听。协议解码严格拒绝未知版本/字段与超界消息。

先提供确定性的真实 Python worker 验证运输，不把它称为模型推理。测实际跨进程大小/延迟/取消/退出/进程回收。无 Python 环境仍可编辑与运行普通 C# 项目。pythonnet/ZeroMQ 保持未实现。

### M8.2 实施顺序与必须证明的边界（实施中，完成证据见文末）

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

### M8.2 自动候选完成（2026-10-16）

最终同一906份源码完整无Skip Debug SECOND / Release FIRST全部通过：每配置13原生、76托管/图形/产品、Python45、原ABI/严格格式/MCP/smoke/inspect、3轮测量、前后审计和受检部署；编译零警告。Release会话31765 exit0，正式部署Complete `cd023930bb454a6fbee9b1cd70b1b7a0`，148实际/148备份文件与候选逐SHA/size核验，真实EXE包校验exit0。新核心8370、实际worker4583、客户端搬移1174、无Python614均再次通过。完整证据、包身份、失败与性能边界见 [M8.2交付记录](M8_2_DELIVERY_REPORT.md)。不关闭整个M8或人工/目标环境/性能/长稳门禁；下一阶段为ECS E0，而不是直接M8.3。

#### 历史实施与失败记录（以下进行中状态不覆盖上述最终结果）

完整验收执行中：906个源码/构建输入冻结于 `out/verification/m8-2/source-freeze-second.json`。第一轮Debug在新打包脚本找不到Get-FileHash处失败；已换为BCL SHA256，不改校验边界，保留失败日志。第二轮 `out/m8-2-full-debug-second.log` 当前仍运行；13原生通过、构建/冒烟通过，76托管回归中主渲染183.27秒通过。不得把这条进行中记录读成最终通过，须检查实际终态，再执行同源码完整Release及审计/部署/提交。当前阶段仍未提交推送。

最新客户端搬移增量：可选库独立publish、锁定NuGet原始压缩包/发布DLL闭包、严格隔离实际加载、真实独立worker共192次调用与64KiB边界、正常退出已完成定向Debug/Release各1174项；原worker4583项双配置再次通过。源发布包/原Python路径26文件排他锁定，七个DLL缺失/篡改及清单/额外文件拒绝；驱动是SDK测试，不进入产品或变成通用插件加载接口。明确记录含轮询的本机成本而非完整性能结论。新增正式CTest `NcmaModuleTests.RelocatedClient` 与独立库打包步骤；待完整无Skip两配置验证。详细边界/哈希语义/日志/失败保留见 [M8.2契约](M8_2_RUNTIME_CONTRACT.md)。下方旧“仍需客户端搬移”段落由此增量更新，不代表已完成M8.2最终门禁。

最新标准构建/失败矩阵增量（2026-10-16）：`Build.bat`、CMake与解决方案已登记可选 `Ncma.Modules.Grpc`/Tests。CTest新增 `NcmaModuleTests.Grpc`（300秒）和 `NcmaModuleTests.NoPythonProducts`（180秒），两者RUN_SERIAL，原native/managed分类正则通过既有 `NcmaModuleTests` 前缀覆盖。完整构建默认验证真实worker；`-SkipPython`仅关闭该worker测试的CMake开关，仍保留默认产品独立性测试，且按原规则禁止正式部署。不会自动创建venv、pip安装、下载Python或模型；完整worker开发测试需先显式准备 `out/modules/python312` 的锁定依赖，普通已发布Editor/Player没有该目录/库依赖。

安装扫描新增增量枚举，避免先排序无限目录再检查数量；`._pth`须先验证固定字节长度再读取，已取消的扫描在IO前退出。16个安装拒绝检查覆盖非本地/相对/根/ADS/超长路径、缺文件、取消、错误site/search path/UTF8、venv、超过256MiB/4096文件/4096目录、真实私有硬链接。12个真实进程负例覆盖静默ready触发产品10秒时限并实际退出、伪造宿主PID不能取代持有进程、ready后大于8KiB stderr撤销会话且不公开原始秘密文本、实际gRPC服务stop而进程继续存活时连接失效/受检关闭、换文档使旧结果失效。测试清理仅对自己创建的Host独立批准恢复，不为产品增加自动终止路径。首轮负例测试漏捕获InvalidDataException（不属于IOException）导致失败，补充测试类型分支后通过；产品拒绝未放宽，保留 `out/m8-2-host-negative-debug-first.log`。

新增默认产品独立性证据：拷贝候选Editor与Null Player到独立中文目录，逐清单核对SHA/size、检查所有文件及deps.json没有Modules/gRPC/Python部署依赖，清空环境/PATH只含System32、排他锁定已知源base与venv两个解释器。真实Editor apphost package-validation及8帧smoke退出0，真实Null Player恰好2tick/0native modules。新鲜Debug/Release候选各613项通过；这证明所测包与已知Python路径独立，不是实际卸载所有Python、干净目标机或自包含环境验收。初次较早候选有额外可变配置文件，614项记录保留，不以总数相等冒充同包证据。

规范 `Build.bat -Configuration Debug -SkipTests` / Release同参两轮通过、零编译警告，日志 `out/m8-2-canonical-{debug,release}-skip-first.log`；候选分别 `b4f5b05dad3b4cdc91b0641389a0c257`、`b7b984da77f6463595de72a537ce5b74`。随后各运行已登记三个模块CTest，均3/3通过：原核心17组8370、实际worker4583、默认产品613，耗时37.96/38.48秒（worker34.74/34.93秒）；证据 `out/m8-2-module-ctest-{debug,release}-first.log`。这些是Skip构建加定向CTest，不是完整无Skip回归。原正式M8.1 Release Complete `b5e91a8f84654161879c4a3ac857ffe3` 及EXE SHA `3C1177C83B0AC9F4B6F373BAB10BDDDD4CD8284B9AF4DF4277B932CA41FB7521` 未改；未提交推送。全部构建/测试进程已终止。

仍需完成：可选客户端库/运行依赖的严格受检搬移加载（不能只用源码测试程序集或仅搬移Python目录冒充）、复核可选部署/版本与成本证据，然后冻结同源码运行完整无Skip Debug/Release及所有原验收/审计/恢复/部署门禁。M8.3/M8.4的领域闭环/正式AI服务尚未开始，本阶段不能宣称推理已接入。隔离路径机制依据[Python 3.12 Windows官方说明](https://docs.python.org/3.12/using/windows.html#finding-modules)：显式`._pth`可隔离注册表/环境搜索路径，未列`import site`时不加载site；实际受检安装还逐文件锁定内容，这不是安全沙箱。

最新受检启动/恢复增量（2026-10-16，仍未交付整个M8.2）：新增产品 `WorkerInstallation` / `LocalWorkerHost` / `WorkerAttempt`，是可选库公开的可信本地API，尚未接入正式Editor按钮/MCP。默认被动，不因请求自动启动、安装依赖或下载模型。独立Windows CPython3.12目录必须包含固定 `python.exe` / `python312.dll` / `python312.zip` / `python312._pth` / worker / grpc与typing模块；`._pth` 仅允许 `python312.zip`、`DLLs`、`site-packages`，启动参数固定 `-I -S -B <worker>`，清空继承环境但保留SystemRoot。不使用venv redirector或全局site-packages，直接持有自己创建的唯一Process并严格核验ready/握手PID及实际镜像路径。

整包最多4096文件/4096目录/256MiB；off-frame Inspect生成含绝对目录、文件路径/大小/SHA的不可变复制库存。每次启动重新获取祖先目录与文件只读句柄、拒绝reparse/hardlink/额外或改变文件，确认精确已批准内容；运行和关闭未收敛期间保留文件/目录句柄。此内容审批不能证明代码安全，也不是同用户恶意进程或OS沙箱。目录可由可信安装流程准备；本阶段测试复制的Python/依赖不等于正式分发安装包、重分发法律授权或目标机自包含验收。

启动review绑定项目/host/document/generation/revision/World/tick、整包指纹、根目录/文件数/大小、下一attempt和60秒寿命；本地权限回调、owner-thread与非重入边界均检查，拒绝/撤销后旧review不能复活。启动在后台完成，Pump无IO等待，只在原owner创建/调度ModuleSession；崩溃、输出预算故障、检测到的传输连接丢失、权限撤销或上下文变化会停止原会话，不交付旧结果。最多三次显式启动，每次新实例/凭据/session，无自动重试或重放，保留有界历史失败摘要。正常关闭先收敛会话/连接，再EOF及等待进程/输出，最后释放资源；超时进入FailedRetained，不能Dispose或开启替代worker。再次关闭/终止需要另一个精确实例/PID/操作/60秒本地review；只可终止持有的原Process句柄，不按进程名或报告PID终止其他进程。原始stdout/stderr不进入诊断，仅有界计数与固定错误码，持续消费防止管道撑满。

最终Debug/Release整个传输专项各4555项、零警告，其中新增启动器44项：独立中文空格目录、原始worker与grpc源码排他锁定、真实工具结果、执行文件不可写、审批错误/过期/撤销/重入/线程拒绝、实际进程崩溃后结果作废、新实例显式重启且不重放、三次上限、正常退出、启动取消、整包字节变化在启动前拒绝、过长ready拒绝与后续输出消费、EOF不合作进程保留和独立审批终止。日志 `out/m8-2-owned-full-{debug,release}-first.log`；编译 `out/m8-2-host-build-debug-sixth.log` / `out/m8-2-host-build-release-first.log`。Modules两配置17组8370、原Python45及inspect再通过（`out/m8-2-owned-core-{debug,release}.log`、`out/m8-2-owned-python-{regression,inspect}.log`）。全部测试进程/会话已终止，正式EXE与M8.1部署未改。

保留新失败记录：`out/m8-2-host-debug-first.log`是测试将未规范化混合斜线目录与OS路径直接比较，改为双方FullPath+OrdinalIgnoreCase后继续，产品身份检查未放宽。`out/m8-2-host-debug-third.log`过长ready负例在Wait后被Dispose未收敛错误遮蔽；代码检查发现先写较大bootstrap再读stdout会形成管道互等，产品改为并行读ready/写bootstrap，并在失败时有界取消/观察reader后继续有界消费。`out/m8-2-host-debug-fourth.log`及最终双配置同负例均通过；不删除原失败或生成测试目录，不把单个专项通过写成全部阶段门禁。

上方最新增量已补充失败矩阵、默认产品无Python依赖路径与标准构建登记；接下来以最新剩余范围为准，仍需可选客户端库严格产品搬移/依赖闭包、规范可选部署及最终同源码完整无Skip Debug/Release、所有原门禁与受检部署。不得把测试生成独立目录当成正式可分发self-contained包；Editor真实AI/领域提案接入在M8.3/M8.4，当前仍无推理服务。阶段未提交推送。

最新协议与实际工作保留增量（2026-10-16）：服务端要求已认证RPC携带有限deadline，将计算期限限制为剩余时限和注册操作上限中的较小值，并在接纳工作、计算前后检查。新增独立任务所有权集合：RPC取消只请求取消计算，不提前释放实际工作槽；四个尚未结束的任务会使后续请求收到明确的 `module work budget`，实际结束后才移除并观察异常。停止请求撤销接纳、取消任务并有界等待；未收敛不能冒称卸载成功，仍需后续正式进程owner保留并恢复。当前确定性实现不启动线程池/模型；这不是任意Python代码的CPU/内存安全沙箱。

最终针对性 Debug/Release各4511项、零编译警告，日志 `out/m8-2-wire-replies-{debug,release}-first.log`，构建 `out/m8-2-wire-replies-build-{debug,release}.log`。原Modules CPU回归两配置仍17组8370项（`out/m8-2-wire-core-{debug,release}.log`）；原Python45项和CLI inspect通过（`out/m8-2-wire-python-regression.log`、`out/m8-2-wire-python-inspect.log`），未改完整传输/推理状态标志。正式EXE SHA仍为 `3C1177C83B0AC9F4B6F373BAB10BDDDD4CD8284B9AF4DF4277B932CA41FB7521`，未部署。实际覆盖：

- 独立原始TLS/gRPC测试peer仍执行完整链/主机名/叶证书pin校验，向真实产品Worker发送缺失/重复认证、无deadline、错版本/实例/schema、未知/缺失/重复字段（含转义后的重复键和嵌套重复）、非法UUID/整数/UTF8/base64、超界wire/payload、重放/跨session/旧epoch等；拒绝后合法请求仍正确。精确4096个成功请求后第4097个拒绝，新epoch不能清空终身ID账本，共4212项。
- 测试专用 `retained_work_probe.py` 只替换实际Worker的计算函数为可控、不合作取消等待，并增加带凭据的测试观察/释放RPC；产品源码没有启用开关、不会导入该文件。三次真实取消及一次真实gRPC deadline后，四个Python工作仍占槽；32次补发均由产品工作预算拒绝。精确释放后四个实际任务终止，正常调用恢复，退出0/无stderr，共46项。不能把测试计算函数称为正式模型或生产慢任务服务。
- 测试专用 `malformed_reply_probe.py` 在产品请求校验/计算之后改写出站回复，通过真实TLS/gRPC验证C#拒绝未知/重复/缺失/超界/坏UTF8/错误实例/枚举/base64/提案等（AdapterFault），及精确request/session/epoch/fingerprint/schema不匹配（InvalidReply）；每次坏回复后健康回复仍成功，错误结果不暴露Proposal、不得重复领取，Unavailable/Rejected合法返回也验证，共211项。
- 原42项真实确定性工具/认证/关闭测试全部保留。以上是定向传输证据，不是正式审批启动器、Editor/MCP接入、搬移包或完整Build.bat验收。

真实传输首个增量：新增 `LoopbackModuleAdapter` 与 `python/worker/ncma_module_worker.py`。使用官方gRPC HTTP/2/TLS、自定义严格UTF-8 JSON消息体（不是protobuf/ProtoJSON，不要求安装protobuf编译器）；128KiB收发、64KiB复制payload，闭合字段/重复键/版本/UUID/schema/实例/session/epoch/指纹检查，单worker最多4并发与4096已发ID。仅注册确定性 `ncma.tools.text_stats`，返回Unicode码点数、UTF-8字节数与SHA，不调用模型、不执行代码、不修改World。仅字面127.0.0.1；无代理/重定向/配置式重试，短期自有CA完整链/主机名校验加精确叶证书SHA pin，每次启动独立32字节凭据由stdin传递，不进入命令行或日志。连接准备/停止/关闭必须离开运行时tick。

独立真实子进程Debug/Release各42检查通过、编译0警告：错误token→Unauthenticated、错误pin→TLS AuthenticationException且正确连接随后成功，空/ASCII/中文/emoji/转义/32KiB文本结果与C#独立计算一致，复制提案单次领取，关闭会话/连接后stdin EOF使worker及启动器exit0；全新健康worker stderr0，刻意错误TLS peer产生325B有界诊断且不含凭据/私钥。日志 `out/m8-2-grpc-tests-debug-eighth.log`、`out/m8-2-grpc-tests-release-first.log`，构建 `out/m8-2-grpc-tests-build-debug-final.log` / `out/m8-2-grpc-tests-build-release-first.log`。

保留首次失败：Windows venv使用redirector，启动器PID与实际Python PID不同；测试改为OS父子关系、创建时间、精确解释器路径与私有ready pipe实例共同核验，只有验证后的进程句柄进入清理状态，未接受任意报告PID。沙箱Schannel报“安全包中没有可用的凭证”，正常权限运行同断言通过，未关闭证书检查。另两处测试假设修正为TLS失败实际Internal+AuthenticationException而非Unavailable，以及将刻意坏TLS诊断与全新健康worker零stderr要求分开；原日志first至seventh保留。测试启动器是TEST OWNER，尚不是正式批准启动器，不等于目标机/自包含/全生命周期验收。

本段协议增量之后，审批/pin/受控启动/崩溃/有限显式重启/保留恢复已有上方新实现和44项证据；剩余闭包、更完整负例、无Python、标准构建与双配置门禁以最新段落为准。双向畸形协议、真实取消/deadline后工作保留/过载/重放不得替代完整生命周期验收。未提交推送或正式部署，inspect的完整传输集成标志、推理状态和人工门禁仍未改为通过。

历史第一增量：M8.1已提交推送 `139040e48af8d9e4b36aa08ccc9b4fede16509e1`，当时本地与GitHub main逐字核对一致。M8.2新增实际派发剩余预算：adapter三参数签名传入扣除队列/ThreadPool时间的TimeSpan，移除旧签名；测试时钟改为线程安全。目标Debug/Release均17组8370检查、0编译警告，日志 `out/m8-2-budget-{build,tests}-{debug,release}.log`。该轮仅基础CPU增量，没有RPC/worker/推理证明；后续真实链路证据见上文，仍未进行本阶段完整Build.bat门禁、未提交推送或部署。

依赖限定到可选 `Ncma.Modules.Grpc` 项目（net8.0、仅向下引用Modules），默认locked restore、官方NuGet来源与仓库 `out/modules/nuget` 缓存。已恢复grpc-dotnet2.84.0及传递依赖并生成packages.lock.json；正式Editor/Player与纯BCL核心不引用它。专用 `out/modules/python312` 虚拟环境仅安装官方PyPI固定SHA的grpcio1.84.0/typing-extensions4.16.0，`pip check`通过；锁文件为 `python/worker/requirements-win-py312.lock`，只覆盖CPython3.12/Windows x64，不宣称其他目标环境已验收。全局Python未改，未下载模型；仅后续测试启动过有界生命周期的loopback worker，没有正式常驻服务。恢复/安装日志为 `out/m8-2-grpc-restore.log`、`out/m8-2-worker-dependency-install.log`。

版本与来源核对：[grpc-dotnet包元数据](https://www.nuget.org/packages/Grpc.Net.Client/2.84.0)（2026-09-21发布，net8.0兼容）、[grpcio包元数据](https://pypi.org/project/grpcio/1.84.0/)（2026-09-14发布）；实际wheel SHA从官方PyPI JSON取得并由pip require-hashes校验。恢复需要正常网络权限，未绕过TLS校验。后续必须完成真实协议/认证/worker/超时取消与有界关闭/搬移等原定范围，再做本阶段完整验收，不能把依赖就绪当作服务已接入。

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
