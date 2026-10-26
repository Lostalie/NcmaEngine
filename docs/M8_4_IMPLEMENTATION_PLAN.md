# M8.4 模型连接与正式 AI 工作区实施方案

## 用户请求提交推送与 EXE 更新（不代表 F6 通过）

2026-10-26。用户在F6输入切换失败后明确要求提交推送并更新EXE。本轮发布范围为现有M8.3/M8.4源码、测试、方案和失败状态记录；个人`.vs`缓存及`.user`设置不纳入提交，不删除其本地修改。

使用与当前源码676354AF一致的Release**默认编辑器**候选 `4cb48ea17d8a48d6ba5e5f216641fc5e/editor`，沿用既有受检部署流程更新 `out/bin/NcmaEngine.exe`；AI候选产品类型尚未被正式部署流程支持，本次不绕过限制安装AI版，也不配置或连接模型。

- 部署代次 `06ac7112d0f84ab286da83ae82d0e51b`，日志阶段Complete。
- 包校验及实际DX11启动烟测通过：8帧，资源释放；不是完整功能/GUI人工验收。
- 安装148文件；旧148文件全部备份并复核哈希，路径 `out/deployment/06ac7112d0f84ab286da83ae82d0e51b/backup`。
- 新正式EXE SHA256：`5CDFFED9C2760BAD02F367D32D72DFC11A03DCC59059B019BD4595EA9F7C060A`。
- 安装清单SHA256：`1AC8B0B0FF63DE7F94C808B3E2865F3F02940B37395C6E3006A54AE98E2827B0`。

证据 `out/verification/m8-4/user-publish-first/deployment-result.json`。F6仍为0/304，旧失败日志/临时副本/备份保留，未重试其输入切换。**旧F5冻结绑定了此前正式EXE，因此后续执行须保留旧证据并重新建立受检基线**，不能修改旧回执使其“通过”。部署脚本的journal已使用正确的NullString，不是前次失败的F6辅助脚本；两条流程不可混为一谈。E5-D继续延期。

## 最新状态：F6 首次切换被参数异常阻止，304 项引擎测试未运行

2026-10-26。完整F5输入复核、Debug152项库存核对通过；开始原受检切换流程，35份旧目标备份完成。首个 `File.Replace(temp, target, $null)` 因PowerShell7.6.5将空字符串参数传入备份路径，抛出 `ArgumentException: The path is empty`，立即停止，无自动重试或强制覆盖。

只读复核确认：35个目标全部保持切换前哈希、35份备份均准确、0个applied记录、无complete回执；保留一份6,160,384字节临时NcmaPhysics副本及全部失败现场。正式EXE哈希595CFEB4不变。串行执行器将Debug/Release全部304项明确登记为not-run，不能把未执行计为测试通过，也不是引擎断言失败。

隔离的合成文件诊断复现了空备份参数异常，并验证使用显式新备份路径时，**同一File.Replace原子替换**能保留旧字节并安装新字节；这不等于实际35项切换或恢复验收。原冻结辅助脚本、旧freeze和产品源码均未修改。证据 `out/verification/m8-4/f-tests-first/`：runner/fatal日志、result.json、activation-failure-audit.json、replace-diagnostic.json。

按原“失败停止、不自动重试”约束，实际切换等待用户确认后继续。获准后：保留第一轮证据，以明确备份路径形成受检辅助脚本新版本及新回执，再执行原流程切换与完整152项/配置。无需删除临时证据、降低哈希/备份校验或替换正式EXE。未运行模型/worker、未部署/提交推送，E5-D继续延期。以下F5完成仅代表静态准备，不覆盖本次实际切换失败。

## 当前进度：F5 输入冻结与执行交接，下一步 F6

2026-10-26。完成有界输入冻结：9,088个文件、75个精确目录文件集合；Debug/Release各152个CTest入口，命令、属性、原超时与F0–F3一致。冻结摘要SHA256为 `0B5C363DA0268931C077801004CFDA668792D53476EF662C1FE35C6E198661E8`，来源摘要仍为676354AF。证据位于 `out/verification/m8-4/f-freeze-first`，不是整个仓库/操作系统/SDK/NuGet缓存的冻结，更不是功能验收。

12个引擎候选与两份ModuleClient再次核对来源、配置、代次、清单与完整文件哈希。已登记真实worker依赖文件、测试资产、运行程序集、原生插件、工具入口；补充字体、DX11调试层、显卡与.NET版本的只读前提清单。本机SDK仅9.0.315；存在文件不替代实际worker/IPC/GPU/GUI验证，也不代表SDK8或目标机性能通过。

两份staging清单各35项，涉及38个不同目标（原目标全部存在）。辅助 `Activate.ps1` 默认只预检；Debug/Release已分别完成全部冻结输入复核与35项映射预检，未写入目标。实际F6切换必须显式 `-Apply`，先备份全部旧文件，再逐文件原子替换并复核。**35文件批次不是整体原子事务**；任何失败保留备份、计划与已应用记录，停止测试，不删除/强制覆盖/自动重试。共享out/managed仍未激活；Debug/Release和GPU/窗口操作必须串行，正式out/bin不在切换目标内。具体预检结果见本目录dry-run日志与交接回执。

本次发现并登记两项证据边界：

- 历史资产负例输出含链接，首次冻结安全拒绝，失败原脚本和异常转录均保留。仅按实际夹具源码将对应输出目录排除出运行输入树，不跟随/删除链接，不排除真实依赖或测试用例。
- AIContracts会创建其运行目录中的workflow-evidence，使原“完整目录”编译回执失效。因此6个产品宿主+4个真实apphost搬移检查须先于AIContracts；后续复测依赖该回执时必须重新准备，不能删除输出或绕过校验。完整执行队列按精确名称对账：65核心/合同、4 IPC/worker、72原生/图形/宿主、10搬移、最后1 AIContracts，共152/配置；这些是串行调度分组，不保证每组内部无原生依赖。

PS5直接启动语法辅助脚本被执行策略拒绝的记录保留；未改策略，改为内联只读解析API，PS5/7均零语法错误。下一步F6实际激活、完整双配置测试，失败留证进入F7修复回归，最终F8报告。**本轮无测试/烟测/基准、无服务或真实模型调用、无发布/部署/Git写入；E5-D继续延期。** 正式EXE仍为595CFEB4。以下F4及更早“下一步F5”等为历史切片。

## 当前进度：F4 候选发布与输入准备完成，下一步 F5

2026-10-26。显式执行 `Package-M2_7.ps1 -IncludeAI`，按配置串行完成实际DX11资源烘焙；每配置6种新候选：默认Editor、AI Editor、Null/DX11 Player及各自物理版本。Debug代次 `87a921ed686f41f78515a11b01808ac3`（86.82秒）；Release代次 `4cb48ea17d8a48d6ba5e5f216641fc5e`（77.10秒）。两配置均包含editor/player shader包、24 Tone+Bloom、4 High shadow及中性环境预设，cook报告API0/0；这不等于整套渲染回归通过。

12个候选的完整文件集合、大小、SHA、配置/代次和依赖约束复核通过。源码仍为676354AF，与F3两配置编译回执一致；回执运行目录复核仍有效。默认产品不混入AI/Modules/gRPC/Python，AI Editor只增加约定的可选托管模块。候选包索引已生成，旧默认索引已保留，不替换正式EXE。

另完成：

- 两配置ModuleClient新发布，依赖与锁定NuGet原始归档逐项验证；代次分别 `eff04d1e4bf1428bb86eaf72179bf9c7` / `fe03d8dabb624f93bb2eb06567e90220`。
- 两配置全新程序化动作样例，使用各自当前Gameplay程序集；不是用户FBX或E5-D代表性负载验收。
- 每配置35项native/gameplay/MCP/transport运行输入的不可变副本与哈希，保存在 `f-packages-first/inputs-second/<Configuration>`。**共享out/managed尚未激活**；F6运行每个配置前必须备份旧目标、按对应staging清单复制并复核哈希，不能两配置同时使用共享目录。
- 现有worker解释器只做标准库元数据检查：CPython3.12.8/x64，grpcio1.84.0、typing-extensions4.16.0；记录解释器/源码/锁文件哈希，未启动服务、未下载或安装，不能以版本元数据代替worker实际握手与故障测试。

证据 `out/verification/m8-4/f-packages-first/delivery.json`。辅助启动/暂存/metadata探针出现的3次失败均保留原脚本或工具输出转录：PS5误发现PS7模块、out-only guard误用于managed路径、Python内联引号丢失；已修复证据辅助流程，不修改产品校验，未覆盖失败证据。最终所有进程已退出，未发现遗留测试辅助进程，正式EXE哈希595CFEB4不变。

本轮实际执行发布/烘焙/资产生成，**未执行全量测试、搬移验证、GUI游玩或真实推理**，未部署/提交推送。下一步F5冻结完整输入和152项库存、核对剩余运行前提并准备配置激活；随后F6完整双配置测试、F7失败修复回归、F8独立报告。以下F0–F3“候选索引不存在”为历史状态，已由本节覆盖。

## 当前进度：F0–F3 编译与静态库存完成，F4 输入准备待执行

2026-10-26。本轮继续整体F阶段，**未运行测试、模型、publish/cook或部署**。正式EXE SHA256保持 `595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`；前后只读检查无相关遗留测试辅助进程。

- 对仓库实际66个csproj生成独立验证解决方案，成员逐项核对；Debug103.87秒、Release103.57秒，均0警告/0错误。未使用整条Build.ps1/Build.bat。原生全目标两配置均exit0，依赖检查无需重新编译。
- 两配置重新配置CMake，`ctest -N`与JSON库存均152项；对比上一库存，全部命令与属性（含超时）不变，显式入口文件均存在，无禁用项。这里只证明入口存在，不证明脚本内部所需资源/包/worker/共享暂存输入已齐全。
- 当前选定源码SHA256：`676354AFD6F16957730ED18B20A989EE07535E63681CB4EF2317A22E040BA12E`。可选实际Editor+完整Services无增量重编译Debug90.68秒/Release92.54秒；AIContracts80.13秒/79.31秒，均0警告/0错误。来源前后相等，完整运行目录文件清单/大小/哈希复核通过，旧索引保留。
- 新编译回执代次：Debug `8b570746626142deac2bd60aef779cf2`，Release `004c1a1da1204a45a4ad48d5653e97ca`。默认Editor/Player依赖未引入AI/Modules/gRPC/Python。上述均为compile-only，不是152项执行结果。

证据：`out/verification/m8-4/f-prerequisites-first/readiness.json`及同目录完整日志/binlog/清单；不是全仓冻结。**下一步F4**：两配置AI候选索引当前均不存在；须按最新源码显式生成默认/AI候选包，准备匹配配置ModuleClient、原生/示例/worker/IPC运行输入，再做F5输入冻结、F6完整152项/配置测试。不能复用历史候选包作为本次通过证据。真实模型/人工验收仍需独立授权，E5-D继续延期。

下方“来源回执失效”描述针对专项修复时的旧回执；已由上述新回执替代，历史记录不删除。

## 最新专项修复：响应缓冲池化

2026-10-26。`DeepSeekChatAdapter` 改用 `ArrayPool<byte>.Shared.Rent(262145)`，在finally中 `Return(clearArray:true)`，覆盖成功、异常、取消及来源失效，并在归还后才释放active槽。池可能给出更大数组，读取容量仍固定262145（256KiB正文+1字节超限探测），不以数组实际长度扩大预算。

解码改为同步消费有效 `ReadOnlyMemory<byte>` 切片，移除整段响应 `ToArray()`；内部JsonDocument在归还前释放，最终ModulePayload独立持有提案字节。新增4项夹具检查短/恰好256KiB响应、512KiB池桶不扩大未知长度读限、JSON/部分IO/取消时归还并清零、头部超限不租用。Debug/Release均33/33专项通过，编译各12.36秒、0警告0错误；原有测试不删减。主套件93项登记，未全跑。

证据：`out/verification/m8-4/f-response-pool`。这是减少重复大缓冲分配，不是全链零分配或完全消除LOH：冷启动/池不足仍可能分配更大桶，清零及JSON/提案分配仍有成本。未测分配性能、未调用真实模型、未更新正式EXE/部署/提交推送；F全阶段与E5-D验收状态不变。

## 最新专项修复：短周期连接池与异常类型诊断

2026-10-26。制作请求和显式连接诊断统一设置 `ConnectionClose=false`；共享生产策略设置 `PooledConnectionLifetime=30s`、`PooledConnectionIdleTimeout=15s`。池归各自 HttpClient/adapter 所有，不跨配置、凭据或工作流共享；同一适配器的连续请求可以复用连接，单次诊断会话仍只批准一次应用调用，关闭时释放池。30秒是连接寿命，不是请求时限，不打断在途请求；下一次连接建立时重新解析DNS，现有deadline/取消保持不变。参见 [Microsoft HttpClient 指南](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)。

安全边界不变：HTTP/1.1、正常TLS验证、无代理/重定向/Cookie/默认凭据/自动解压，每服务器最多1连接。**无应用层自动重试/隐式回退，不承诺底层传输恰好一次或供应商恰好计费一次**；不能用关闭连接代替服务端幂等合同。连接池收益限定同一存活适配器内，未实测远端TLS耗时或性能提升。

`AiCallDiagnostic.ExceptionType` 是独立可空字段。制作请求及连接诊断的兜底catch保留原 `invalid_response_or_request`，只复制 `e.GetType().Name`，不持有异常对象、message、stack或inner exception；正常/已分类调用仍为空，避免跨调用残留。

专项证据位于 `out/verification/m8-4/f-connection-pool`：原始8文件与追加修改前版本保留；最初受限还原NU1301失败10.68秒，未进入编译/测试，日志保留。正常网络还原后，连接池版本Debug/Release编译79.42/77.40秒0警告0错误，27/27专项均通过。追加异常类型修复后的最终结果见本节后续记录。F全阶段仍未验收，原来源回执仍失效；无真实模型调用、正式EXE更新、部署或Git操作。

最终合并验证：Debug/Release编译12.35/12.34秒，均0警告/0错误，专项均 **29/29通过**（原17项chat + 新2项连接池 + 新1项chat异常 + 原8项连接诊断 + 新1项诊断异常）。主套件累计89项登记，不代表全跑。真实回环证明单TCP承载两次POST；未测远端TLS性能、30秒到期时序或GUI，ExceptionType保存在诊断对象中，未新增界面字段。

## 最新专项修复：适配器早退诊断

2026-10-26。用户中断F启动核对，优先修复 `DeepSeekChatAdapter.InvokeAsync`：准入拒绝移入try/finally，保留原判断优先级，分别记录 `adapter_disposed`、`adapter_busy`、`lifetime_budget`、`request_duplicate`。拒绝诊断携带本次RequestId，HTTP状态/usage为空、请求/响应字节为0，不再显示上次HTTP数据。仅持有本次active槽的调用能释放它；忙碌拒绝不消费请求ID，也不会让Dispose越过未完成IO。LastDiagnostic仍为最后完成调用的结果，不改变为“最后发起调用”。

新增4项回归，与既有13项通过测试专用 `--chat-adapter-tests` 执行；主AI套件登记总数从81增至85，但本轮只运行17项专项。首轮双配置16/17，新增4096请求夹具构造因逐次Sleep累计触发原10秒限制；失败原文/日志保留。改为Yield等待真实工作完成，保持4096数量与10/12秒夹具限制，不调整产品额度/超时。最终Debug、Release均17/17通过，编译分别8.93/9.05秒、0警告/0错误；采用替身及实际本机回环HTTP，没有真实模型调用。

证据 `out/verification/m8-4/f-adapter-diagnostic`。正式EXE未变，无部署/提交推送。**F全阶段测试仍未执行；下方E阶段077A5149编译回执已因源码修改失效，继续F前须重建，不得复用旧回执/包。** 以下“全部用例未运行”描述为本专项前历史状态，不代表本次17项未执行。

2026-10-26。状态：用户已指定DeepSeek，要求可扩展其他模型、可本地可远程。**A–D有界实现与夹具基线已编译；E实现与静态准备基线已具备，双配置静态库存各152，所有用例未运行**。本轮补齐托管编译来源回执和AIContracts默认入口产物；未生成候选包，F全量编译/原生/worker/sample/实际包准备及测试尚未执行。视觉、原生窗口、DX11运行和真实模型尚未验收。动画仍限定原配对MCP图读授权，UI和元数据有独立本机精确观察路径。M8.3已通过本机双配置自动验收，见[交付记录](M8_3_DELIVERY_REPORT.md)。本阶段复用C#引擎业务和唯一编辑事务，Python仅为可选专长模块。

## 当前E编译来源回执与F执行准备

`NcmaManagedAiVerification` 现在调用明确的纯编译脚本 `Prepare-AiVerification.ps1`：正常NuGet、`--no-incremental`、warnings-as-errors，分别重编译可选实际Editor/完整Services和默认路径AIContracts。构建前后选定源码SHA必须一致；独立generation保存清单/日志/binlog/回执，固定索引原子替换并保留旧回执。`kind=compile-only`、`testsExecuted=false`，不执行测试或打包。

新 `AiVerificationInputs.ps1` 校验配置、选定源码SHA、固定运行目录、完整文件集合/大小/SHA和必要DLL/deps/runtimeconfig。搬移产品宿主探针在启动前必须匹配该回执，同时登记probe SHA和generation；仅有DLL路径不再足够。当前目录可能含历史文件，完整目录钉住不等于已证明每个文件由本次编译生成，F仍须核对deps/实际加载来源。不是全仓冻结、数字签名或恶意并发写防护。

新增 `NcmaAiPackageTests.VerificationInputs`（60秒、10个合成case，全部未运行），覆盖精确闭包、旧源码/配置、伪通过标志、错误根目录、篡改/额外/缺失入口、重复记录和缺回执。另新增1个typed Ordinal排序case，并强化PS5/PS7跨文化组的排序断言；指纹组现15个case。合成字节不加载为产品，不构成候选验收。

实际编译准备发现并修复两个问题，失败原文全部保留：

1. 两配置首轮在C#之前被 `ai_package_source_order` 拒绝。PS5原生输出形成对象数组，原排序未得到所需Ordinal顺序；4853路径诊断及失败脚本已存档。改为显式 `SortedSet<string>(StringComparer.Ordinal)`，不放松顺序检查。
2. 第二轮可选Editor/Services Debug90.72秒、Release90.61秒均0警告/0错误，随后AIContracts遭MSB1001：PowerShell单字符串被splat成字符参数。改为明确 `string[]`，保留错误日志/源文，不将这一轮记为整体成功。

最终第三轮纯编译：

| 输入图 | Debug | Release |
| --- | --- | --- |
| 实际可选Editor + 完整Services | 90.67秒，0警告/0错误 | 92.05秒，0警告/0错误 |
| 默认路径AIContracts + 依赖 | 78.77秒，0警告/0错误 | 77.70秒，0警告/0错误 |

双配置编译回执共享选定源码SHA `077A5149FDE9D6B1287C01F327FA771B5725CCBE05454FEB05F81D13FC5B8A51`；先前AIContracts默认路径缺失已补齐。**这不是全解决方案、原生或运行测试通过。** 六脚本PS5.1/PS7.6最终纯解析0错误；静态CTest双配置各152，原151项命令/属性不变。PS7重新核对当前源码与PS5生成的两份回执一致，两个运行目录哈希匹配；直接列入CTest命令的exe/dll/ps1/cmake入口文件未发现缺失。该检查仅覆盖入口文件，不覆盖动态资源、Python worker、GPU、同源候选包等完整运行前提；这些仍属于F执行准备，不能凭入口DLL存在认定可全部运行。

证据 `out/verification/m8-4/e-readiness-first` 保存6份首批原文、另2份修改前文件、失败原文/诊断、完整构建日志/binlog、双解析器/库存和选定源码回执；不是全仓冻结。辅助静态审计首次在空缺失列表上触发StrictMode属性错误，原脚本和结构化错误转录已保留（不是完整stdout）；修正枚举后重做，不影响产品或测试结论。正式EXE SHA仍 `595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`。无测试/GUI/HTTP/IPC/模型/性能/publish/cook/打包/部署/提交推送，E5-D延期。

**下一步进入F整体统一验收**，按 [F执行清单](M8_4_F_EXECUTION_CHECKLIST.md) 先补齐全解决方案、原生、worker、Sample及实际候选准备，再执行完整双配置库存。所有失败保留；源码/产物变化后重新建立回执，不跳过依赖、不放宽断言。真实服务模型/端点/外发/费用及真人界面另获确认，不能以自动替身结果关闭整个M8.4。

## 当前E受检搬移产品宿主与输入合同

新增测试专用 `AiRelocatedProductBootstrap` / `AiRelocatedProductProbe`，沿用既有受检产品加载方式，但把非框架依赖进一步限定为搬移清单内的精确路径、程序集身份和SHA；只允许共享.NET运行时框架位于产品之外。唯一额外测试程序集为当前Services探针本身。实际资源根设置为搬移目录，采集加载产品程序集清单。产品不增加CLI、任意代码执行入口、写接口或AI授权。

新增 `NcmaAiPackageTests.ProductHost.*` 六入口，每个独立进程/60秒，覆盖默认CPU GUI、默认DX11、AI未配置CPU GUI/DX11、AI已配置但未连接CPU GUI/DX11。全部复用原候选包索引/manifest/当前源码指纹、中文空格搬移路径、原包独占锁、最小环境和有界输出/超时身份留存；缺依赖失败，不借源码树或历史DLL通过。

探针从搬移包复制实际Sample程序集到独立测试项目，调用搬移产品中真实宿主及原编辑事务：Create/Rename、真实8帧Play推进、author字节隔离、Stop、两次Undo/Redo精确字节、双Dispose。可选AI使用原工厂而非HTTP替身；配置仅写入本机会话，不发起诊断/推理。记录托管HTTP/socket/DNS事件为0的断言，不宣称OS网络隔离。默认产品的AI方法位于独立JIT边界，默认产品仍无需AI成员/依赖。

**证据分层不变**：原4条搬移apphost入口只证明未来实际EXE启动/退出；新6条探针入口针对实际搬移产品宿主API编辑闭环，`actualApphost=false`，不能称为EXE真人点击验收。所有这些断言目前尚未执行。

输入合同新增三个独立60秒分组，均逐case运行后汇总：

| 分组 | case数 | 范围 |
| --- | --- | --- |
| `Input.Fingerprint` | 14 | 独立字面UTF-8线格式预期、Unicode/tombstone、内容/长度/路径变化、空输入/乱序/重复、小写SHA、负数/字符串长度、非法tombstone、路径越界/换行 |
| `Input.Index` | 12 | 默认/AI精确选择、旧源码/配置/schema/production、错误SHA、相对/越界/错变体路径、混代默认与AI产品 |
| `Input.CrossShell` | 2 | 实际PS5与PS7子进程，各跑en-US/tr-TR/zh-CN三种文化，与独立预期比较；缺解释器失败，超时保留精确进程身份 |

源指纹版本/排序未改变，补充输入形状拒绝；公共索引验证提取为 `Get-AiCandidateIndexedProduct`，真实搬移入口与负例共用，绑定同一代默认/AI路径。原17项清单合同和原142项CTest命令/属性保持不变；两配置CMake重生成及`ctest -N`/JSON均**151项，仅库存**。

三脚本PowerShell5.1/7.6纯语法解析0错误。完整可选Services+实际AI Editor Debug **10.65秒0警告/0错误**；完整默认Services+实际默认Editor Debug **83.25秒0警告/0错误**，正常NuGet、全部终止，无本轮编译失败。不是全解决方案/Release编译、不是测试通过。证据 `out/verification/m8-4/e-product-probe-first` 保留8份原文、完整日志/默认binlog、双解析器/库存及选定源码回执，非全仓冻结。

下一步E：核对完整同源/同配置执行输入、探针来源及AIContracts默认路径缺口，形成F执行顺序与失败留存清单。随后才运行整个M8.4自动测试和实际候选包准备；真实模型与真人界面另列授权/验收，不以自动替身结果代替。未测试/GUI/HTTP/IPC/推理/性能/publish/cook/打包/部署/提交推送；正式EXE哈希仍 `595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`，E5-D延期。

## 当前E真实apphost搬移与离线宿主增量

本次把两个证据面明确分开，全部只是实现/编译/登记，尚未执行：

| 入口 | 实际对象及预定断言 | 不代表什么 |
| --- | --- | --- |
| `NcmaAiPackageTests.Relocated.DefaultCpu/DefaultDx11/AiCpu/AiDx11` | 4个独立进程入口，每个60秒。当前输入指纹/配置/精确generation/manifest核对；中文空格新路径完整复制；运行期间独占锁住源包全部文件及已知仓库Python解释器（若安装）；受限环境启动真正发布的`NcmaEngine.exe --editor --project ... --frames 8`，CPU GUI或DX11；要求退出0、精确8帧和释放提示、原payload哈希不变 | 尚未运行；不操作搬移产品的编辑/Play按钮，不是无网络OS隔离或真人UI验收 |
| `NcmaAiEditorTests.OfflineHost` | 4组实际`CandidatePresentation`矩阵：CPU GUI/DX11 × 未配置/已配置但未使用。使用生产AI构造（无HTTP工厂替身），原事务Create/Rename、8帧真实Play tick递增、authoring隔离、Stop、两次Undo/Redo和幂等关闭；HTTP/socket/DNS托管EventSource计数必须为0 | 使用编译图中的真实宿主API，不是搬移包apphost或鼠标人工操作；网络事件不等于防火墙证明 |

搬移脚本不启动服务、不读密钥、不请求推理；清空子进程继承环境，仅保留Windows/临时目录前提，不传Python/PATH/NuGet或凭据配置。已知解释器被锁不等于机器完全没有其他Python。stdout/stderr各256KiB有界异步读取，单子进程30秒；超时保留精确PID/创建时间/exe/参数及失败，不强制杀进程。F若出现保留进程，必须先核对并按用户批准处理，不能把后续测试/性能结果当作无污染通过。

源指纹改为明确版本的长度分帧UTF-8与Ordinal排序，避免PS5/PS7的JSON序列化或文化差异改变同一输入摘要；已删除文件仍记录tombstone。这不是旧包兼容层：当前源码指纹不匹配就要求重新准备；此前本轮未生成任何实际AI包。新算法尚未运行验收。

检查记录：最终脚本PS5.1/PS7.6纯解析0错误；Debug/Release CMake重生成和`ctest -N`/JSON均142项，之前137项命令/属性不变。可选完整Services+实际AI Editor Debug **10.34秒0警告/0错误**，默认完整Services+实际默认Editor Debug **79.89秒0警告/0错误**；不是全解决方案或Release编译。本次正常NuGet恢复，未降低TLS/签名/审计，无编译失败。默认产品deps无AI/Modules，AI产品恰为原3个可选库；测试程序集可依赖AI，不冒充默认产品闭包。

证据 `out/verification/m8-4/e-relocation-first`：7份修改前原文、完整编译日志/默认binlog、双解析器/双配置库存及选定源码回执，不是全仓冻结。所有进程已终止。本轮未测试/GUI/HTTP/IPC/推理/性能，未publish/cook/生成包/部署/提交/推送；正式EXE SHA256仍 `595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`。

本段为前一增量记录。上方最新增量已接入受检搬移产品宿主探针和输入合同；E仍待完整同源输入/编译准备核对。F才运行全部用例、真实候选准备和人工/真实服务独立门禁。AIContracts默认路径DLL缺失仍须在F前重建，缺依赖不能跳过；E5-D继续延期。

## 当前E候选包与清单合同增量

`scripts/Package-M2_7.ps1` 新增显式 `-IncludeAI -CMakeExecutable <cmake.exe绝对路径>`，不更改默认Build开关、不调用部署函数。未来执行时在全新generation中刷新原生构建，分别隔离default/ai托管发布，显式设置 `NcmaEnableAI=false/true`；原默认五产品保留，增加 `editor-ai`。AI仅共享本代默认Editor的插件、cooked资产、Sample、import-worker和licenses，不复制历史包、默认managed输出或测试程序集冒充AI产品。

新 `scripts/AiCandidatePackage.ps1` 复用原路径/有界文件遍历与SHA工具，额外约束：

- 明确选择CMake、managed、engine、scripts的已跟踪/非忽略新文件作为构建输入，记录路径/长度/SHA，已删除跟踪文件记录tombstone；排除bin/obj/.vs/.git和.user。包生成前后指纹必须一致。**这是所选构建输入证明，不是全仓冻结或恶意并发修改防护。**
- manifest绑定configuration、RID/TFM、framework-dependent、非production、sourceInputsSha256和aiEnabled；索引钉住manifest哈希。严格检查文件集合（含额外文件）、大小/SHA、路径/重复项，不遍历reparse目录，不覆盖意外文件。
- 默认候选禁止AI/Modules/Editor.Modules；AI Editor明确需要这三个可选库；双方拒绝gRPC/Python/编译器运行依赖及脚本/密钥类文件。deps和落盘文件都检查；不把依赖名称检查当作运行证明或数字签名。
- 新M8.4索引只在完成本代检查后发布，保留旧索引；原M2.7索引供既有回归继续使用，IncludeAI路径更新前保留其原文。没有写入正式 `out/bin` 的逻辑。

准备调用须在匹配MSVC环境中，提供已配置同configuration原生构建树与正常NuGet访问；原shader cook会创建实际原生窗口/renderer，因此**本轮未执行打包、cook、publish或任何启动验证**。新流程仍须F实际执行证明，默认打包共用的copy/manifest改动也必须回归。默认/AI依赖集合的当前断言仅是实现，不是新包验收结果。

新增 `NcmaAiPackageTests.Contracts`：17个独立合成清单用例，覆盖正向默认/可选闭包、manifest篡改、错误来源/配置/变体、production错误、文件缺失/篡改/额外、默认AI依赖泄漏、可选gRPC/Python混入、不完整可选依赖、重复路径与越界。每case执行后汇总失败，60秒，不调用exe/模型/网络；合成exe/DLL字节明确**不可当作真实候选包**。用例全部未运行。

最终三个脚本分别通过PowerShell 5.1与7.6纯语法解析；CMake重新生成后 `ctest -N` / JSON双配置各137项，之前136项命令/属性不变。未重新构建C#/C++，不声称本轮全解决方案零警告。辅助检查脚本插值错误、Windows执行策略拒绝以及BOM-less中文literal导致的WinPS解析失败均保留；最终采用ASCII源码+显式字符构造，实际夹具目录仍包含中文/空格，不降低负载。仅对解析进程使用既有CTest同款ExecutionPolicy参数，未改系统策略。

证据：`out/verification/m8-4/e-package-first`（6份修改前原文、失败原文/诊断、两种解析器最终结果、双配置静态库存、选定源码回执）；不是全仓冻结。正式EXE仍SHA256 `595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`。未运行测试/GUI/HTTP/IPC/推理/性能，未生成包、部署、提交或推送。AIContracts默认路径DLL缺失仍待F前全量输入准备解决，不能跳过。

本段为前一增量记录；后续已登记真实apphost启动/退出搬移与非搬移宿主编辑/Play矩阵，见上方最新增量。搬移后的编辑/Play完整闭环仍需补齐，缺依赖失败、默认产品门禁保留，F整体统一执行尚未开始。E5-D继续延期。

## 当前E测试库存与交付准备

本增量只准备执行入口、依赖和验收输入，不执行测试。新显式构建目标 `NcmaManagedAiVerification` 编译实际 `NcmaEnableAI=true` Editor 与完整 Services 夹具，输出固定在各配置构建树的 `m8-4-ai` 隔离目录；不加入默认产品依赖，不替换 `out/bin`，不调用整条 Build 脚本。F 必须先构建此目标，缺少 DLL 即失败，不依据文件是否存在隐藏 CTest 入口。

2026-10-26 重新生成现有 Debug/Release CMake，分别核对 `ctest -N` 和 `--show-only=json-v1`：**各136个入口**（M8.3原131 + AIContracts 1 + 可选编辑器4）。这是静态库存，不是136项通过；后续新增搬移门禁后须重新计数。独立分组仍复用逐case运行并最终汇总失败的 CaseHarness，既有超时/工作量不变。

| 新编辑器入口 | 隔离实现 | 已登记case数（未运行） | 依赖/预算 |
| --- | --- | --- | --- |
| `NcmaAiEditorTests.Presenter` | 实际可选Editor Build/GuiEvent | 8 | 精确发送、独立确认、来源失效/取消；60秒 |
| `NcmaAiEditorTests.Credentials` | 原生凭据API边界替身、实际控制器 | 6 | 不使用真实密钥/对话框；60秒 |
| `NcmaAiEditorTests.HostLifetime` | 实际CPU GUI与DX11宿主，仅HTTP替身 | 2 | 真实插件、字体、同配置Sample；60秒，缺依赖失败 |
| `NcmaAiEditorTests.DomainPresenter` | 实际UI/元数据/动画界面与原事务 | 9 | 动画含真实配对MCP和ActionSample；60秒 |

独立 `NcmaModuleTests.AIContracts` 已登记81组，默认Services保留无AI入口用例及原领域桥用例；这不是整个Services用例总数。四个可选入口串行执行，不扩大旧单入口TIMEOUT。静态文件存在也不能证明同源/同配置、动态依赖可加载或无Python运行。

本次新目标完整可选Services+实际Editor Debug编译89.20秒、Release89.04秒，均0警告/0错误；仅这个依赖图，不是全解决方案双配置验收。两次沙箱内自动NuGet恢复分别失败1/2个NU1301（7.90/4.51秒），原日志保留，正常网络环境恢复和实际CMake目标编译成功，未放宽TLS/签名/审计。双配置新Editor deps的可选运行库恰为 `Ncma.AI/Ncma.Modules/Ncma.Editor.Modules`，没有gRPC/Python/编译器泄漏；这是编译输出检查，不是发布闭包证明。

静态审计逐项比对M8.3旧131项的命令/属性均未变化。**当前AIContracts所指默认路径的Debug/Release DLL均缺失**（此前只在证据隔离目录编译）；登记缺口，F执行前必须由当前源码重建/核对全量输入，禁止借旧DLL或忽略该项。本次其他入口文件存在也不意味着来源已冻结。证据 `out/verification/m8-4/e-inventory-first` 保留5份原文、两次失败、完整成功日志、双配置库存和静态审计；选定源码回执不是全仓冻结。正式EXE SHA256保持 `595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`；未运行任何测试/GUI/HTTP/IPC/推理、未部署/提交推送。

### E余项：候选产品和搬移门禁（流程/清单已实现，实际包与运行门禁仍未验收）

1. 同一源码指纹分别准备默认Editor、默认Player及可选AI Editor；AI publish明确传 `NcmaEnableAI=true` 和隔离 `ArtifactsPath`。沿用原本机插件、shader cook、导入worker、sample、license闭包，不把已有编译DLL复制后称作完整可搬移包；不把历史包重新登记为本次产物。
2. 新一代独立目录与manifest记录精确文件集合/大小/SHA、配置、RID、源码指纹、依赖和环境前提；不得覆盖正式入口或旧证据。默认 `.deps.json` 排除 AI/Modules/Editor.Modules/gRPC/Python；可选产品仅新增 AI/Modules/Editor.Modules，不附带gRPC/Python/模型SDK或服务配置/密钥。
3. 在新路径（包含空格/中文）从manifest复制并复核集合，受限环境启动实际apphost。默认Editor/Null Player原无Python门禁继续；另加可选Editor无配置、无服务仍启动/编辑/Play的真实门禁。`--validate-package`/`--smoke-test` 单独不能替代完整交互与无网络探测证据。锁住已知Python输入不等于机器未安装其他Python。
4. 候选缺依赖、集合多出/缺失、哈希错、默认混入AI、可选混入Python、旧配置/旧源码等负例均应明确失败；失败不回退到源码树/正式EXE。新入口最终纳入F清单，不以本次136作为永久点数。

### 真实服务固定队列 v1（拟定，未批准，0次调用）

只用待人工确认的合成项目和固定对象/资产，队列与版本/指纹在首调用前冻结。每项仍单独批准完整内容/接收方；队列本身不是联网、外发或费用授权。固定供应商DeepSeek，实际模型、端点、服务侧费用限额待用户确认，密钥只在本机原生输入，不放入聊天/仓库。

| 顺序 | 人工场景 | 验收记录 |
| --- | --- | --- |
| R1 | 固定无项目数据连接诊断 | 只证明本次协议回包；输出请求上限64 tokens/10秒 |
| R2 | 选定合成对象重命名或Transform | 完整发送批准→真实回复→独立原差异确认→Commit→Undo/Redo |
| R3 | 支持的根资产settings修改 | 精确record批准，原metadata事务/Undo，不测试未支持导入资产 |
| R4 | 有界UI草稿 | 精确文档/资源观察、原Prepare、独立Commit/Undo，无自动资源获取 |
| R5 | 已获原读授权的动画图重命名 | 摘要局限明确；资源审阅与精确UUID确认，再独立Commit/Undo |
| R6 | 合成观察中的越权/提示注入文本 | 不扩大读写、工具、文件或网络权限；如返回合法提案也只人工审阅，不自动提交 |
| R7 | 请求后主动修改原文档 | 返回结果失效，历史不新增；不伪造服务输出 |
| R8 | 已发送后人工取消并等待实际排空 | 迟到回复不可提交；不能保证服务端停止计费 |

拟定最多8次真实发送（含诊断），R2–R8各最多4096请求输出tokens/60秒，总请求输出上限28736 tokens；每项0自动重试/0自动修复/0后备模型。这个总数不是输入tokens、账单或远端实际token硬上限。必须另行批准实际支出上限及服务侧控制；计量缺失标“未知”。成功制作门禁要求支持域的成功闭环，安全拒绝不能顶替；不能只重跑挑绿，修改负载或重试须新版本与新批准。

默认保存仅case ID、源码/候选/输入指纹、批准回执引用、模型报告标识、字节/耗时/usage（缺失明确）、固定错误码、原事务/Undo回执、实际发送次数。不得保存Authorization、密钥、原始异常响应、完整提示或业务record。保存原文/截图须另外确认范围和期限并人工审查脱敏。替身用例、真实HTTP和真人审批三类证据分别标记，不互相替代。真实凭据窗口焦点/视觉、DX11实际关闭及SDK8/VS等人工门禁仍在F；E5-D仍延期。

## 当前D领域Presenter矩阵与E衔接

- 新增9组`AiDomainPresenterTests`，全部走实际`EditorPresenter.Build/GuiEvent.Apply`，不反射私有状态或另造UI。UI接真实`EditorUiWorkspace`，元数据通过实际资源浏览器选择精确UUID，动画接真实graph workspace并复用原MCP配对/读授权与ActionSample资源夹具。仅HTTP使用内部替身，所有测试及其文件/IPC/网络活动本轮均未执行。
- UI三组：完整分页显示正文与实际发送字节一致；独立完整差异确认、唯一原Commit和文件Undo/Redo；写scope撤销后旧确认拒绝；12次取消/重开不能复活发送批准。元数据三组：仅本机内容页不出现外发/提交批准控件；明确settings意图后完整typed内容外发，原文件/哈希/写scope及独立Commit/Undo不变；外部精确字节变化在发送前拒绝，撤销写scope在提交前拒绝。每组可包含多个独立断言，不代表已执行通过。
- 动画三组：未获原读授权不能生成发送批准；实际回包只能进入资源待审，完整候选/依赖页、勾选及手输精确图UUID后才生成原事务差异；资源批准后重置最终审批，须再独立确认提交并可Undo/Redo。撤销读grant、取消资源批准后的旧提交事件不写文件/不增加历史，取消释放原pending候选。不会自动配对、加载缺失资源或绕过原资源许可。
- 仅给已有内部HTTP工厂构造器补传真实UI workspace，便于与正式公共构造器相同的UI接线；没有新增公共权限API、可信模型句柄或另一个writer。完整Services主入口登记9组，另设`--ai-domain-presenter-tests`；默认产品误调用明确失败，不跳过后宣布通过。可选Services新增界面/边界/宿主/领域组累计25，默认单独入口用例1组；AI独立入口仍81组。完整测试库存由E重新审计，不能以这两个局部计数代替全量库存。
- 完整可选Services+实际AI Editor Debug16.75秒、完整默认Services+实际默认Editor9.55秒、AI.Tests3.70秒，均0警告/0错误且所有进程终止；正常NuGet TLS/签名/审计恢复，本增量没有构建失败。`out/verification/m8-4/d-domain-presenter-first`保留6份改前原文、完整日志/binlog及选定源码回执，非全仓冻结。正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`，未测试/GUI/替身HTTP/IPC/模型/性能/部署/提交推送。

**下一步E**：依赖清单与实际入口库存、同源默认/AI可选候选包和搬移准备、无服务/无Python与负例清单、真实服务固定队列/调用预算及脱敏证据策略。D有界实现与夹具基线可进入E，不等于功能/视觉/原生/模型验收，也不关闭整个M8.4。全部执行留F统一门禁，缺依赖、禁用、跳过必须记未通过；真实端点/模型/密钥/外发/费用仍待独立批准，E5-D继续延期。以下历史“领域presenter未实现”由本节覆盖。

## 当前D凭据边界与真实宿主关闭增量

- `WindowsAiCredentialPrompt`增加内部`NativeApi`替换点，正式路径仍是原Windows P/Invoke、GENERIC、空输入、无保存复选框、当前线程/进程的活动父窗口。未增加公共凭据读取器、环境/文件/剪贴板发现、模型工具或GUI普通文本密钥入口。新增owner/busy保护，原生modal期间同步重入拒绝；提取公共内部清理函数供原finally和夹具共用，三个4097字符缓冲全量清零，原生blob先清零再释放。
- 新增6组凭据夹具：16次获取/精确接收方与模型/空缓冲；取消/原生错误/原生调用抛异常仍清零释放返回blob；解包失败/错误用户名/非空domain/空或未终止密码/4096边界；错误profile/父窗口/线程/同步重入；三个完整缓冲清零；真实controller替换密钥时取消或非法key保持断开。全部使用固定假密钥和原生边界替身，不打开Windows对话框；实际OS父窗口归属、视觉、焦点及真实native API行为仍待F人工/环境验收，不宣称托管string物理擦除。
- 静态检查发现正式宿主`Dispose`在AI请求未排空时抛出，但原先此时尚未设置`_graphicsShutdown`。现将终止关闭意图和旧图形review失效移到AI回收前：原帧/输入/Agent/Play/Present/图形恢复守卫立即拒绝继续，`Start`也拒绝closing/closed。没有提前调用下游Dispose或强制销毁HTTP；AI未结束仍保留全部资源，实际完成后沿原关闭顺序释放。
- 新增2组真实`CandidatePresentation`宿主夹具，分别CPU GUI和DX11：真实工程/场景/Gameplay样本/平台/GUI/渲染器/物理服务对象，只用internal init-only factory替换HTTP边界。断言非owner关闭不撤权、3次关闭重试保留原对象身份及模块数量、撤销配置、全部帧入口/重启/恢复拒绝、场景字节不变、HTTP真正完成后一次释放与幂等关闭。只读内部lifetime视图不提供关闭或权限捷径；没有反射伪造宿主。缺少插件、样本、字体或其他原启动依赖直接失败，不降级跳过。
- 完整Services主入口登记这8组；新增`--ai-credential-tests`和`--ai-host-lifetime-tests`，默认无AI产品误调用明确失败。它们不是运行时Editor CLI/MCP能力。结合上一增量，可选Services现有16组新增界面/边界/宿主用例，默认独立界面用例仍1组；AI独立入口仍81组。**所有新增用例未运行，真实GUI/DX11/替身HTTP均未执行**。
- 完整可选Services+实际AI Editor Debug8.75秒、完整默认Services+实际默认Editor7.93秒，均0警告/0错误且已终止；正常NuGet TLS/签名/审计恢复，本增量无构建失败。证据`out/verification/m8-4/d-native-host-first`保存7份改前原文、完整日志/binlog和选定源码回执，非全仓冻结。正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`，未部署/提交推送。

**下一步继续D**：UI/动画/元数据的实际presenter事件矩阵，随后E打包/搬移准备和F整个M8.4统一测试。原生对话框视觉与DX11关闭运行验收均未完成，实际服务端点/密钥/外发/费用仍需单独批准；E5-D继续延期。以下历史“凭据/完整宿主夹具未实现”由本节覆盖，不代表D结束或功能通过。

## 当前D实际Presenter事件夹具增量

- 新增`AiPresenterTests`，直接使用实际编译的`EditorPresenter.Build/Apply/Shortcut`和正式`GuiItem/GuiEvent`协议，配真实Editor owner/workspace/domain/controller；只有HTTP在内部替身边界替换。没有读取或修改presenter私有字段、反射造票据、模拟另一套界面或扩大产品权限。未启动原生窗口、模型服务或测试进程。
- 可选AI版本登记8组：启动/反复绘制不探测；模态面板阻止后台创建和快捷键；非法输入及未应用配置不发送；全部分页与勾选、过期帧及重复事件；场景发送和原事务完整差异分别人工确认、唯一Commit及Undo/Redo；文档变更拒绝缓存发送；面板关闭/断开/重配保留忽略取消的HTTP；12次取消/重开不复活旧批准。场景与诊断都将展示分页还原后的完整HTTP正文与实际替身收到的字节逐项比较，不只检查摘要或hash。
- 默认产品另登记1组，检查真实菜单只显示“未包含模型模块”的说明，不存在可调用AI工作区按钮。`Services.Tests`只在`NcmaEnableAI=true`时定义同名条件符号，与所引用实际Editor一致；沿用Editor已有独立`ArtifactsPath`保护。新增专用`--ai-presenter-tests`入口，默认产品误调用明确返回失败，不能跳过8组后宣称通过。完整Services入口也登记当前产品对应用例，沿用跑完case再汇总的harness。AI独立入口仍81组。
- **本轮全部用例未运行。**最终完整可选Services+实际AI Editor Debug4.41秒、完整默认Services+实际默认Editor4.50秒，均0警告/0错误且进程终止。首次NuGet恢复失败2条NU1301/TLS（4.35秒），保留完整日志；正常TLS/签名/审计恢复成功，无关闭校验。随后新增夹具的多余类级`unsafe`导致异步函数2条CS4004（16.65秒），原失败源码保留；移除该声明后可选5.60秒、默认8.16秒通过，补充正文逐字节断言后取得上述最终编译结果。
- 证据`out/verification/m8-4/d-presenter-first`含6份改前原文、失败源码、静态复查前源码、完整日志/binlog及选定源码回执，非全仓冻结。正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`。未测试/原生GUI/HTTP替身/IPC/模型/性能/部署/提交推送；编译不等于运行验收。

**下一步继续D**：原生凭据prompt边界夹具、实际完整EditorHost关闭顺序夹具；本轮关闭覆盖仅实际presenter/controller，不是完整宿主/GPU资源回收证明。UI/动画/元数据的实际presenter事件矩阵与视觉验收也仍待补齐。然后E交付/搬移准备和F整个M8.4统一测试；实际端点/密钥/外发/费用调用需单独批准，E5-D延期。以下历史“实际presenter夹具未实现”仅由本增量部分覆盖，D仍未完成。

## 当前D显式连接诊断增量

- 新增独立`AiConnectionDiagnosticSession`，固定请求只要求`{"ncmaDiagnostic":"ok"}`，不携带项目名、场景、资产、代码或领域观察，也不构造假ModuleContext。无模块注册、模型提案或Commit入口，诊断结果不能通过原创作结果解码。
- 正式可选Editor新增“准备单次连接诊断审阅（不发送）”：完整HTTP正文、接收方、模型、hash、配置和预算进入缓存分页。全部阅读并明确勾选外发与潜在费用后，才允许单次发送；没有启动探测、配置探测、自动重试或重连。远程认证头单独告知，预览不含密钥。60秒ticket绑定原完整profile/配置epoch/凭据及最新引用，错票据、重放、过期、撤权、跨线程和重入拒绝。
- 预算最多一次、请求输出token上限64、10秒deadline、16KiB响应（更低profile预算优先）；完整固定正文最多4096B。服务可能收费，输出token请求和本地超时不是服务端费用硬上限。共用现有禁止重定向/代理/cookie/压缩的HTTP策略及严格completion包解析；原创作capability白名单不变。非200不读错误正文，区分认证失败/限流/服务失败/重定向；只接受闭合诊断JSON，不回显原始错误体或密钥。
- Cancel、断开、配置替换和关闭先撤销可用性，再等实际请求结束；不合作transport继续被持有，不能强制销毁后假称完成或开启新凭据窗口。取消回调/同步handler重入有边界。界面“本次协议回包通过”仅表示该次固定回复，不等于持续连接、领域能力或模型质量验收。
- 新增8组AI夹具，累计81组，**全部未运行**：完整固定正文/显式一次批准；60秒/foreign/thread/配置撤销；header-only密钥及脱敏；HTTP失败不重试/不读错误体；闭合JSON/错误领域结果/字节及token预算；deadline/忽略取消的保留；实际controller12次替换及同步重入；撤权先于排空/阻止新凭据prompt。替身HTTP代码没有执行，实际presenter/原生窗口验收仍待完成。
- 最终可选AI Editor Debug3.66秒、默认完整Services含Editor8.29秒、AI.Tests3.64秒，均0警告/0错误；首轮可选Editor7.64秒同样通过，所有构建终止。正常NuGet恢复未关闭TLS/签名/审计。证据`out/verification/m8-4/d-diagnostic-first`保存9份改前原文、3份静态复查前版本、完整日志/binlog及选定源码回执，非全仓冻结。默认依赖隔离仅静态核对，不是搬移验收。
- 正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`；未运行测试、GUI、HTTP替身、IPC、模型调用或性能测量，未部署、提交、推送。

**下一步继续D**：实际presenter/宿主/原生凭据UI夹具，然后E打包与搬移准备、F整个M8.4统一测试。实际端点/模型/密钥/外发/费用仍待调用前授权；E5-D延期。以下历史记录的“连接诊断待实现”由本节覆盖，但D尚未完成，更未通过功能验收。

## 当前D元数据外发与settings桥接增量

- 新增`MetadataDomainObservation`：从实际owner的配置根、精确根资产UUID/kind、本地真实文件内容生成typed selection；它不是外发许可。与通用read列表、UI观察互斥，不注册MCP能力。完整record、相对metadata/source路径、原文件hash和asset revision进入原`DomainObservationReview`及实际HTTP正文预览，仍有原64KiB总预算，不能隐藏在instruction/template或附加到旧脱敏摘要中；不附加源blob、项目扫描或私有代码。
- 正式AI可选面板新增“所选资产settings草稿”，与“仅本机查看”分离。选择/准备不联网；完整分页、精确接收方/模型/正文hash及明确人工批准后才允许原单次发送。没有自动读权限提升、MCP配对或文件授权。选择错误UUID/kind/项目、未配置、原来源或文件变化都拒绝。
- 新模型结果`ncma.ai.metadata.settings.draft`（输出schema尾号0005）只含assetId和settings(version=1、sampleRate整数1..120、generateTangents布尔)。闭合成员、重复JSON、错误类型/UUID、path/record/proposalId等均拒绝。宿主使用已观察原记录，只替换settings，自己填入原path、expectedAssetRevision和完整record，转成原`ncma.assets.metadata.edit/settings`；模型不能create/import/改源哈希/借用prepared handle，也不支持已导入generation/subassets/dependencies记录。
- 转换本身不写文件。原metadata participant继续检查真实源文件/hash、独立AssetWriteScope、before/after和事务预算，结果只成为原完整差异。用户另行确认后才进入唯一Commit/历史/Undo/Redo；只读授权不能升级为写权限，缺失权限无回退。原C1可信低层generic metadata工作流保留现有契约，本次产品typed settings入口不使用它作为回退。
- 来源、配置epoch、最新版ticket、60秒期限、取消/撤权/关闭继续贯穿回包和最终原事务。Claim、回包及Commit前重新检查完整文件；等待中的被动检查只比较身份/版本，不每帧读元数据。原participant Publish合法推进asset clock时，仅保留身份/撤权/配置检查，避免把自身提交误判为外部变化；内部source比较不重入公开domain API，没有增加任意reader回调。
- 新增8组Services用例，登记整体与domain入口，**全部未运行**：闭合草稿/畸形字段；真实外发正文与第二次人工确认、原Commit/Undo/Redo；只读及撤销写scope；发送前和提交前文件变化；取消/重配/domain撤权后直接Commit拒绝；错误资产和原raw capability回包拒绝；被动无IO与显式完整检查；foreign项目/线程/旧票据/12次取消。替身HTTP仅测试代码，尚无实际GUI事件、网络模型调用或功能验收证据。
- 可选AI Editor Debug19.27秒、默认完整Services含Editor10.77秒、AI.Tests4.21秒，均0警告/0错误、进程终止。前两次可选构建分别在NuGet漏洞数据获取阶段失败NU1900（9.98/9.25秒），完整日志保留；只读检查官方HTTPS重定向终点返回200，使用`--no-http-cache`正常restore后成功，没有关闭审计、跳过签名/TLS或降低警告要求。
- 证据`out/verification/m8-4/d-metadata-bridge-first`：12份改前原文、完整恢复/编译日志与binlog、选定源码/产物回执；不是全仓冻结或运行验收。正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`，未测试/GUI/HTTP替身/IPC/模型调用/性能/部署/提交推送。

**下一步继续D**：显式、有预算、可取消的连接诊断，以及实际presenter/宿主/原生凭据UI夹具；再E交付准备和F整个M8.4统一测试。实际端点/模型/密钥/外发内容/调用费用仍需用户授权。E5-D继续延期。以下本地内容记录为历史切片，其中“模型入口未实现”已由本节覆盖，但不代表D完成或测试通过。

## 当前D元数据本地内容增量

- 正式可选AI Editor接入`EditorMetadataContent`和“查看所选资产完整元数据（仅本机）”。服务由实际`EditorSessionOwner`用已配置项目根创建，无调用者路径参数；构造不读文件。显式点击才按精确根UUID/kind解析当前catalog的`MetadataFiles`，不假定元数据文件名、不从浏览器缓存拼接record，不扫描全项目或读取源模型/图片字节。
- 只支持原metadata writer的无generation/subassets/dependencies根记录；不自动回退到导入器或任意文件写。复用原安全路径、父目录及文件句柄租约，读取和完整显示均64KiB上限，超限拒绝不截断。拒绝待恢复journal；原codec闭合/重复成员校验，canonical完整record必须与catalog相同，保存原文件精确SHA256。显示完整record和相对路径，明确它们可能含业务数据。
- 私有复制ticket绑定原domain来源、项目/宿主/Edit/document身份与revision、asset clock、60秒期限和最新ticket引用。刷新替换旧ticket；外部修改公开字节副本不能改变审阅。显式重新校验再次读取原文件、比较精确哈希，即使只多一个空白字节也需重新捕获；绘制只检查身份/期限和展示缓存，不每帧读文件，不把“身份有效”声称成文件未变。
- Cancel、重配、断开、宿主关闭清理ticket；错UUID/kind、子资产、跨线程、重入、外部文件/catalog不一致、来源版本变化拒绝。面板显示“仅本机”，不提供外发批准/写入按钮，`Prepare(AssetMetadata)`仍拒绝；不新增MCP能力、修改原读授权或扩展原写权限。这是**本地内容审阅基础，不是完整元数据AI工作流**。
- 新增6组Services用例，登记到整体和domain入口，**未执行**：非默认文件名映射、完整私有副本、缺失源blob仍可审阅/无历史；外部精确字节与语义变化；替换/取消/期限/asset clock；foreign ticket/线程/重入/document变化；已导入根/子资产/超预算/重复成员/recovery；12次取消与重配/断开/关闭/无外发入口。实际GUI事件、hardlink/reparse负例及generation/dependency独立矩阵仍留F补齐，不把编译或调用原安全reader当成这些负例已验收。
- 完整默认Services含Editor Debug24.67秒、可选AI Editor10.41秒、AI.Tests8.00秒，均0警告/0错误且进程终止。首次默认restore失败2条NU1301/TLS（15.79秒）、AI.Tests首次失败2条NU1301/TLS（5.00秒）完整保留，恢复正常官方TLS/签名校验后编译；未降低校验。原子补丁首次上下文不匹配被拒，核验未创建新文件且owner原哈希不变后修正补丁。
- `out/verification/m8-4/d-metadata-local-first`保存9份改前原文、完整日志/binlog、选定源码和编译回执，非全仓冻结。正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`。未测试/GUI/HTTP/IPC/模型调用/部署/Git提交推送。

**下一步继续D**：将精确元数据内容接入独立typed observation，完整接收方/HTTP正文逐次外发批准；模型只能返回settings草稿，宿主构造原metadata命令并执行原权限/差异/独立Commit/Undo。不得将本地缓存的完整record/path塞入instruction/template或原脱敏摘要观察。随后显式连接诊断、实际presenter/宿主/凭据UI夹具、E/F；整个M8.4末统一测试，E5-D延期。以下为历史增量，不能据此声称元数据AI或D已完成。

## 当前D动画工作区增量

- AI可选Editor启动时将现役`EditorAnimationGraphWorkspace`接入`AiAuthoringWorkspace`，面板新增“当前动画图”。目标严格为当前打开图UUID，无图/错UUID/本地未提交草稿/跨线程拒绝。选择图不等于批准读取；沿用原图读取授权及配对受众，不自动启动MCP、配对或授予权限。
- 本次观察只读取原`ncma.animgraph.inspect`的所选图`summary`，固定模板默认重命名；界面与模板明确**没有全图节点、状态、骨骼、资源上下文**。不自动扩大观察/导出路径，不宣称完整AI动画创作。模型结构操作仍由已有C闭合语义、完整候选、资源与原事务校验，不因模板文本而放宽执行器。
- 外发沿用完整逻辑/HTTP正文分页与明确人工批准。回包只进入`resource-review`，新增显式资源准备按钮调用原`CaptureReview`：读取真实依赖、资源hash、实际rig/clip一致性与原预览验证，不提交文件/历史，也不启动场景Play。资源缺失直接拒绝，无自动下载/导入/替代。
- `AiAnimationResourceReview`私有持有原审阅副本与完整候选JSON，资源UUID/hash/受众数组复制，公开仅复制字节、图UUID与指纹；本机显示总预算1MiB，超限拒绝，不截断。刷新取代旧ticket，外部修改字节副本不影响批准。面板完整分页后勾选并输入精确图UUID，才调用原资源Approve；该动作**只生成原事务差异**。随后重置页进度/勾选，须再次审阅完整原差异并单独Confirm，原文件/资源授权和Undo不变。
- Cancel/断开/重配/宿主关闭清理本controller缓存ticket，原候选/transport生命周期不变。配置、图重开、读授权撤销、来源版本失效继续由原边界拒绝；缓存结果不能绕过直接原Commit。被动等待资源阶段改为身份/版本检查，不每帧重读完整观察；显式资源捕获、批准与最终提交保留完整校验。绘制只展示缓存页。
- 新增6组Services夹具，已登记到现有全量及domain入口，**未运行**：无隐式grant、原资源批准与独立Commit/Undo；私有副本/刷新替换/取消；撤权/重开/重配；已批准资源取消后直接Commit拒绝；错UUID/线程/缺资源；12轮暂存取消释放原候选和注册预算。沿用真实配对MCP与ActionSample依赖fixture、替身HTTP，只有整体F才执行；AI入口仍73组，未新增实际GUI事件或原生窗口视觉验收证据。
- 元数据面板仍未接入：静态检查确认`AssetInspectionService`只公开经授权的脱敏摘要/诊断，不能用资源浏览器缓存自动附加完整record、路径或源码。原`AssetMetadataCommands`的create/settings切片还明确拒绝带已导入generation/subassets/dependencies的记录。后续必须建立精确内容授权/支持范围显示，再接面板；不能隐藏回退到任意文件写或自动重导入。
- 隔离Debug首轮AI可选Editor20.43秒、默认完整Services含Editor13.57秒，最终AI可选Editor12.00秒、默认完整Services11.02秒；AI.Tests4.90秒，全部0警告/0错误、进程已终止。只编译，未运行测试/IPC/HTTP替身/实际推理/GUI/GPU。`out/verification/m8-4/d-animation-first`保存9份改前原文、完整日志/binlog和选定源码/产物回执，非全仓冻结。默认Editor无AI/Modules依赖仅静态核对，非搬移验收。
- 正式EXE哈希仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`；无部署、Git提交/推送、用户密钥或实际模型调用。

**下一步继续D**：元数据精确内容授权与受限面板、显式有预算的连接诊断、真实presenter/宿主/原生凭据UI夹具，随后E/F；整个M8.4末统一测试，E5-D继续延期。以下D远程与本机记录为历史切片；不能将动画面板编译视为D完成或实际模型连接验收。

## 当前D远程凭据增量

- 可选AI产品的正式工作区新增本机/DeepSeek远程切换。远程接收方固定为可信注册的`https://api.deepseek.com/chat/completions`，模型由用户填写；其他供应商仍通过原可信Provider扩展，不接受模型提供的URL。配置与输入密钥均不联网、不探测、不自动发送；填写配置不是连接成功。
- 密钥仅从用户点击打开的Windows原生凭据对话框进入，**不使用普通ImGui Text，不进入GUI包、事件和输入缓存**。提示用户名填`deepseek`、密码栏填API密钥，不是Windows账号密码；错误用户名/域拒绝。输入为空、无保存复选框、无自动环境/文件/剪贴板或凭据管理器读取。对话框绑定当前进程/当前线程的活动窗口，无合法窗口时失败，不弹无主窗口。
- 使用[Microsoft CredUIPromptForWindowsCredentialsW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-creduipromptforwindowscredentialsw)的GENERIC模式、空输入和无CHECKBOX；该API本身不保存凭据，但**不是安全桌面**。按[CredUnPackAuthenticationBufferW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credunpackauthenticationbufferw)合同将输出解包到固定4097字符缓冲；原生blob及三个临时字符缓冲在finally清零/释放。既有`AiSessionCredential`严格ASCII Bearer/4096长度校验不变；托管字符串及HttpClient内部副本不保证物理擦除，界面明确此限制。真正原生对话框显示/输入/取消/手工内存与视觉验收仍未执行，不以编译替代安全验收。
- 可信本地`ConfigureRemote`只在旧transport真实排空后调用凭据prompt；候选配置先验证，非法模型不打开对话框也不改原配置。合法替换一旦开始，先撤销原configuration lifetime和credential，再尝试原取消/注销；未排空不获取新密钥，须显式重试。用户取消、输入无效或prompt异常时保持未配置，不回退旧密钥。prompt期间owner/reentry保护，callback不是新MCP能力或模型工具。
- 断开、切换本机/远程、宿主TryClose均在排空前让旧发送/结果批准与密钥失效；不合作请求仍保留原controller/transport，不能提前Dispose或假称结束。凭据绑定整个精确profile，换模型/重配须新引用。断开只撤销本机会话，**不能收回已发数据或吊销服务商账号的API key**。原第二次人工差异/原事务/Undo不变。
- 完整HTTP正文预览仍不包含认证头；界面现在准确提示远程POST另带Authorization密钥，本机/远程均可能转发或计费，批准外发不等于批准写入。没有隐藏上下文、自动模型调用或测试连接；实际外发仍需逐次完整审阅/明确批准。
- 新增8组controller夹具（AI累计73），**登记未运行**：精确远程接收方/预览无密钥、取消使旧批准失效、非法模型不触发prompt、坏密钥/异常失败关闭、owner/重入、20轮轮换、本机撤权但不合作transport保留及不再prompt、回包后直接原Commit拒绝。替身HTTP只在未来F执行，不是实际服务连接证据；真实presenter及原生prompt夹具仍须补齐。
- 首轮可选Editor恢复失败15.18秒/2条NU1301 TLS错误完整保留；正常TLS官方NuGet重试可选Editor21.15秒、默认完整Services+Editor16.24秒、AI.Tests5.13秒均Debug0警告/0错误。随后最终源码编译与选定pins见`out/verification/m8-4/d-remote-first/compile-result.json`。保存7份改前原文、完整日志/binlog、选定源码与产物，**非全仓冻结**。默认产品无AI/Modules依赖仍作静态核对，不是E搬移/运行验收。
- 正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`；未读取用户密钥、未打开对话框、未测试/HTTP/IPC/推理/部署/提交/推送。

**下一步继续D**：元数据/动画资源审阅面板、显式有预算的连接诊断、实际presenter/宿主及原生凭据UI验收夹具；然后E交付准备，整个M8.4末F统一执行测试。动画原配对MCP读授权仍为前提，不自动配对；实际模型/密钥/数据外发/费用须单独明确批准。E5-D继续延期。下面D1的“仅本机/远程UI未接入”是历史状态，由本节覆盖。

## 当前D本机工作区首增量

- 正式`Ncma.Editor.App`新增编译期开关`NcmaEnableAI=true`，仅此变体引用`Ncma.Editor.Modules`并编译模型工作区；默认产品不引用AI/Modules，不使用运行时DLL发现、反射加载或自动安装。开启变体要求显式非空`ArtifactsPath`，本次分别使用`d-local-first/ai-product`与`default`输出，**未改默认构建/部署脚本，也未形成可搬移AI发布包**。E阶段仍须加入正式可选包清单、原生依赖、双配置库存和搬移门禁，不能把本次编译输出称为完整安装包。
- 项目Editor启动时可信装配`AiAuthoringWorkspace`并挂到正式presenter；无项目不创建，没有自动配置/网络/模型探测。宿主关闭在释放UI/动画/Editor依赖前先取消并等待原工作流真实排空；未结束保留controller与所有下游所有者，明确`ai_shutdown_draining`，不强行Dispose或遗失后台请求。配置替换和断开同样需要显式重试回收，不自动重发。
- 沿用现有C++/ImGui深色样式，在AI菜单提供模型工作区；场景/UI/动画工作区均可打开同一面板。AI栏右侧使用实际本机会话状态，“已配置·未验证连接”与“待外发审批/请求进行中/待差异审阅/回收中”区分；MCP仍单列。默认产品显示未包含AI模块，没有把编译或填写配置标为连接成功。
- 本增量GUI只允许**无凭据的literal-loopback本机服务**，模型名由用户填写；没有远程密钥文本框、环境变量发现、命令行密钥或自动读取剪贴板。修改端点/模型必须重新应用才允许准备请求。远程DeepSeek底层adapter仍存在，但远程安全输入/凭据生命周期UI是D后续事项；不要向普通文本框输入密钥。本机服务也可能转发/计费，发送确认明确提示这一点。
- 场景所选UUID与当前UI文档两种任务走实际C工作流；场景默认模板先限定重命名任务，UI返回完整有界definition。输入界面沿用1023 UTF-8字节预算，底层8KiB合同不变。完整指令/模板/逻辑观察/接收者以及精确HTTP JSON正文分别分页显示，包含指纹、正文SHA256；必须浏览全部页并勾选，才允许一次外发。外发不批准写入，回包不自动提交。
- 差异通过按钮显式刷新为完整缓存页，第二次全页审阅/勾选后由原事务提交；删除UUID和C#绑定确认仍独立。显示原回执/恢复标记及使用量（未报告则未知），不推算费用；Undo/Redo仍从原编辑菜单执行。拒绝不回显任意异常/模型响应到日志，只记录固定错误码。GUI使用模态输入拦截，阻止背景场景/UI/快捷键操作；每次AI动作推进视图代次，旧帧或同帧后续事件不能套用旧批准。
- 被动module Pump改用原请求身份/版本/撤权/配置/期限检查，不重复读取完整观察或UI文件；领取、接收结果、显式刷新及最终提交仍执行原完整内容校验。外部损坏/不可读UI文件转为拒绝码，不作为新授权或自动修复。面板绘制不调用完整差异/观察查询，只显示缓存；模型回包中的来源和可信句柄不能覆盖宿主票据。
- 新增6组controller夹具及1组UI被动轮询夹具（AI累计65组），**全部未运行**：默认未配置/无注册、无连接探测；本机端点/模型边界；仅审阅无调用/无修改；重配/断开与20轮注册清理；精确目标/未支持意图/owner线程；owner失效/幂等关闭；文件已损坏但被动身份轮询不读文件、完整结果门禁拒绝。现有场景/UI/动画原事务夹具保留；真实presenter事件、取消不合作worker的宿主集成、GUI视觉验证仍待D后续登记/整体F执行，不能以controller编译替代UI验收。
- 编译首次与最终全部0警告/0错误。最终隔离Debug：AI可选Editor完整依赖13.52秒；默认Editor.Services.Tests含Editor完整依赖11.28秒；AI.Tests完整依赖4.61秒。首轮分别20.34/12.50/5.26秒也保留。仅编译，未启动Editor、测试、IPC、HTTP替身/推理或性能工作。静态deps核对默认Editor无AI/Modules，AI变体包含预期三库且无gRPC/Python；不是搬移/离线运行证明。
- 证据`out/verification/m8-4/d-local-first`保存14份初始及2份追加原文、完整日志/binlog、选定源码/产物/依赖回执（非全仓冻结）。正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`；未部署/提交/推送，未读取用户密钥。

**下一步继续D**：远程会话凭据安全输入与撤销/清理，元数据/动画资源审阅入口、明确且有预算的连接诊断，以及实际presenter/宿主生命周期夹具。动画独立无MCP读scope仍未实现，保留原配对图读授权前提。D完成后才进入E交付准备，整个M8.4末F统一测试；真实端点/模型/密钥/外发/费用仍需明确授权，E5-D继续延期。以下C记录是先前切片。

## 当前C UI增量交付

- 新增模型专用标签`ncma.ai.ui.draft`及输出schema末尾0004，**不是注册的编辑/MCP能力**。闭合v1输入只允许精确`documentId`和完整`definition`；总JSON≤64KiB、definition≤48KiB、深度≤16。原`UiCodec`负责完整必填字段、未知/重复字段、Unicode、UUID、树结构、布局/token/资源类型语义；未引入兼容格式或另一套UI模型。范围超过预算明确拒绝，不截断文档。
- 新增`UiDomainObservation`与`PrepareUi(exactDocument,...)`：本机用户必须明确选择当前已打开的一个UI文档。它只是选择凭据，**不是外发或写授权**。原完整发送审阅仍绑定唯一请求/接收者/配置epoch/来源/60秒有效期；批准后才能交给原ModuleSession。观察固定为所选UiDefinition、其已在原资源目录准备的依赖UUID/种类/hash、已注册C#语义Action名，不主动附加路径、字体/图片字节、C#源码或全项目资源清单。文档文本/Action名可能含业务信息，并非自动脱敏；全部内容在发送前展示和确认。
- `local.ui.document`仅是固定数据标签，不在远程能力白名单/注册表中；不能通过任意字符串读入口调用，没有任意delegate读取器。此UI路径不需要MCP配对。源Edit/文档代次/UI内容/资源版本/资产版本、只读状态、重开/撤销、外部文件变化均复核；复制完整观察并保持原64KiB总预算。只读打开可明确批准观察，但无法准备写提案。
- optional `UiDocument` workflow在owner线程用原`UiCodec.Decode`→`EditorUiWorkspace.PrepareDomainProposal`→原`ReceiveModuleResult`转换。文件路径来自宿主已打开文件，提案UUID由原宿主生成；原文件写grant、预先显式准备资源、已注册C# Action、原UiRuntime验证和原事务完整memento/资源证明不变。资源缺失或未知Action直接拒绝，不自动加载/下载/批准；未来需要新资源时，用户先通过原流程准备，再明确重新发送。
- 回包不写文件/历史，最终仍需独立人工领域差异确认，再用原UI事务及原Undo/Redo。取消/失败只清理本工作流自己的原候选；不能消费别的候选或扩展文件权限。再次发送、关闭、配置变化等仍沿用原取消/实际排空规则。
- 静态复核原participant的`Publish`会推进资产版本、随后再次检查权限，因此显式区分**提交前完整内容/资产版本检查**与**原提交内部身份/撤权/配置检查**。UI提交内部不把自己推进的资产版本误判为失效，仍保留UI内容/资源/来源和原memento/resource校验。相同问题一并修正动画workflow的内容guard与身份guard；外部资产版本变化在提交前仍由完整guard拒绝，没有放宽原执行器/资源批准。
- 新增18组AI入口夹具，累计58组，**全部本阶段未运行**：无MCP UI发送→替身HTTP/原mailbox→差异→原提交/Undo/Redo；只读、错文档/definition、伪造句柄、未知Action、缺资源；撤权/资源或资产版本/重配/取消/关闭；foreign/owner线程/远程假标签；发送前及回包后外部文件变化；12轮取消释放原4候选预算；Unicode/私有复制/闭合字段/48KiB边界。各独立负例登记为单独case，沿用全部case完成后汇总的harness。
- 隔离Debug：首轮AI恢复失败24.38秒、3条NU1301 TLS错误保留；使用正常TLS官方NuGet后第二轮AI20.49秒0/0。补齐提交边界后最终AI8.48秒、完整Editor.Services.Tests含Editor9.47秒，均0警告/0错误。早期Services10.29秒也为0/0；最终日志覆盖当前源码。没有测试、IPC、HTTP夹具、真实推理、GPU或性能执行，不能称功能验收通过。
- 证据`out/verification/m8-4/c-ui-first`保存10份初始原文及补充1份动画原文、完整日志/binlog、选定源码和程序集回执，**非全仓冻结**。正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`；未部署/提交/推送，默认Editor依赖无AI/Modules仅作静态核验。

**下一步M8.4-D：正式可选产品装配和AI工作区。** C现已实现受限范围内的四类转换，不代表功能/真人验收完成。动画仍需要原配对MCP读授权，独立无MCP动画读scope仍未实现；D必须如实显示此前提、缺失资源和连接未验收状态，不自动配对/审批。审阅面板不得每帧重读完整文件/全项目，使用显式刷新与来源失效提示。整个M8.4末F统一测试；E5-D继续延期，真实模型/端点/密钥/外发/费用尚待明确授权。以下C动画与首增量是历史切片，其“UI未实现/下一步C”状态由本节覆盖。

## 当前C动画增量交付

- 新增模型结果标签 `ncma.ai.animgraph.draft`，**不是注册的编辑/MCP能力**。严格v1闭合结构：`input`仅包含精确`graphId`和`operations`；禁止模型提供`proposalId`、文件路径、批准字段。新动画输出schema末尾0003与已有场景/元数据意图区分；没有改变原模块v2/原事务合同或新增兼容入口。
- 草稿总量仍≤64KiB、深度≤16，操作数1–64且操作JSON≤48KiB，与原图编辑预算保持一致；严格UTF-8/重复字段/结构检查先执行，再由原 `AnimationGraphEdits.Apply` 验证完整语义和图结构。复制操作后不持有调用方可变字节。支持范围就是既有原图编辑操作，不支持脚本执行、任意读写文件或运行时Animator写入。
- `AiDomainWorkflow`增加明确 `AnimationGraph` 意图，必须传入实际图工作区。发送前绑定同一项目/Edit/文档代次/图/内容/资产版本和读权限代次，只允许该图的原inspect/validate/bones观察。回包在owner线程转换到原 `Propose/PrepareCandidate`；宿主生成新的原提案UUID，模型不能选择可信句柄或改写来源。原read/audience授权仍须有效，没有从发送同意推导写权限。
- 回包只停在`resource-review`：不加载资源、不自动写文件/历史。显式 `ReviewAnimationResources()`才调用原 `CaptureReview`检查源文件、准备骨骼/Clip闭包并生成完整资源hash审阅；显式 `ApproveAnimationResources()`使用原精确资源/图/人工批准，随后转原 `ncma.animgraph.transaction` 的领域完整差异审阅。最终仍需单独 `Confirm`，复用原事务、Undo/Redo与补偿规则。
- 图重开/编辑、读权限撤销、配置变化、原观察过期/替换都会使候选失效；等待资源审阅时Pump继续复核原请求，取消清理本任务原候选，不落盘、不重发模型请求。资源缺失可留下待人工处理草稿，但不能取得可提交审阅。原工作区为历史保留的已批准资源pin仍按原有界生命周期管理，不另建资源或历史系统。
- **当前限制明确保留：**原动画读授权要求真实已配对MCP audience。只有AI连接配置而没有原图读授权时，Prepare拒绝；本次没有自动打开MCP、自动配对或为模型自批。独立本机AI动画读授权尚未设计/实现，不能宣称无MCP完整动画制作。D装配前应明确展示此前提；若要移除此依赖，需要独立审阅的授权合同，不能简单放宽原read校验。
- 新增3组AI合同夹具（AI入口累计40组）和6组Services工作流夹具，**全部未运行**。涵盖有界结构/Unicode/伪造句柄、真实codec+mailbox配HTTP替身的原图stage/资源批准/Commit/Undo、错图/未知操作、撤权/重开/重配、取消后直接原Confirm拒绝、缺失Clip、foreign来源。Services夹具复用原真实MCP配对与真实动作资源夹具，只有整体末执行时才会启动这些IPC/资源工作；替身不等于真实模型或真人验收。
- 首次完整Editor.Services.Tests含Editor依赖图隔离Debug编译0警告/0错误24.37秒；完整AI.Tests依赖图Debug0警告/0错误7.88秒。使用正常TLS官方NuGet恢复，没有运行任何测试、IPC、HTTP夹具、推理、图形冒烟或性能测量。此处仅两个依赖图编译，不是全解决方案/双配置/功能验收。
- 证据`out/verification/m8-4/c-animation-first`：12份修改前原文、完整日志/binlog、选定源码/程序集回执，非全仓冻结。正式EXE仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`，未部署/提交/推送；默认Editor deps仍只做静态无AI/Modules检查。

**下一步仍是C：UI模型草稿到原UiDefinition/Prepare的转换、精确UI观察授权与原资源/语义Action验证。** 之后才是D正式可选产品/UI。整个M8.4末统一测试；E5-D继续延期，真实模型/端点/密钥使用/外发/费用尚待明确授权。

## 当前C首增量交付

- 新增可选 `AiDomainWorkflow`，把明确连接epoch/制作意图、`DeepSeekChatAdapter`、原 `ModuleSession/DomainModuleBridge`、完整发送审阅及原领域结果审核接成同一owner会话。构造/配置不联网、不宣称已连接；正式Editor装配与可视入口仍属于D，默认产品不增加AI依赖。
- 当前只支持 `Scene` 与 `AssetMetadata` 意图。返回能力再受意图白名单限制，场景可提出原场景事务/独立删除，元数据只可提出原元数据操作；跨域输出、动画/UI的模型伪造准备句柄一律拒绝。原删除UUID/脚本绑定确认、元数据精确文件/源hash/独立授权、原预检/事务/Undo不变，没有自动Commit。
- 发送审批和提交审批仍是两个不同动作。HTTP编码器使用完整已批准字节；回包进入原领域完整差异审核，只有明确本机人工确认才调用原事务。未增加MCP批准接口、隐式文件读取、Python启动或额外网络调用。
- 修补回包后来源合同：`ReceiveModuleResult`生成的原提案继续保留原观察票据/配置epoch/有效期/host取消代次。显示/确认/预检边界重读并比对原授权观察；原命令内部的权限回调只检查身份/配置/有效期，不重入正在执行的Edit命令。因此配置变更、观察被替换、取消、编辑来源变化在回包后也不能通过原 `domains.Confirm` 绕过；原写域仍自行复核资源和写授权。
- 取消先停原mailbox，不强制回收仍执行的HTTP。新增精确controller身份的 `TryUnregisterModule`：只有Cancel和实际TryClose完成后才移除注册；未排空保留controller和transport。owner关闭沿原Cancel/TryClose链释放adapter；凭据仍由调用宿主负责。重配后必须排空旧会话并重新装配/人工审批，不复活旧票据。
- 新增10组夹具，AI入口累计37组，**全部留M8.4整体末执行**：真实adapter/codec配替身HTTP的场景提交与Undo/Redo、未批/外来/重复发送、回包后配置失效/原Confirm拒绝、编辑/取消/观察替换、意图隔离、忽略取消的回收、线程/注册身份/20次重配、owner关闭、元数据原scope撤销与Undo、回包来源回调重入。没有执行替身、loopback、真实模型或任何业务测试。
- 最终完整AI.Tests依赖图Debug编译0警告/0错误5.93秒；完整Editor.Services.Tests含正式Editor最终Debug0警告/0错误8.68秒。首次NuGet TLS恢复失败（26.65秒、3个NU1301）原日志保留，之后正常TLS官方源恢复，第二次AI20.64秒成功；中间AI9.28秒及Services11.52秒成功记录均保留。不是全解决方案/双配置/功能验收。
- `out/verification/m8-4/c-first`保存10份修改前原文、完整日志/binlog与选定源码/程序集回执（非全仓冻结）。正式EXE仍为 `595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`；没有部署/提交/推送。默认Editor依赖静态审计仍无AI/Modules/Editor.Modules，不代表搬移运行已验收。

**下一步继续C，不进入D：**定义有界动画/UI模型草稿合同，移除模型自带可信proposalId的可能；按原图编辑/UiDefinition校验转换到本地生成的原Prepare，保留精确文档/资源来源、资源审阅和原批准，再进入同一领域完整差异与Commit。UI观察需有独立明确范围，不能默认导出整个UI/项目；动画原读授权、资源准备和人工批准不可合并到发送批准中。当前尚未实现这些转换，C整体不标记完成。

## 当前B阶段交付

- `DeepSeekChatAdapter`实现原`IModuleAdapter`，通过真实`HttpClient/SocketsHttpHandler`发送POST；模型/端点采用明确配置，其他供应商仍需可信注册和独立兼容性验收。本阶段没有实际HTTP或推理调用。
- 固定HTTP/1.1、正常TLS验证；禁止代理、自动重定向、Cookie、默认凭据和自动解压。允许客户端内连接复用，寿命30秒/空闲15秒（覆盖原每请求关闭连接策略），无应用层重试/隐式回退，不承诺底层传输恰好一次。响应头≤16KiB、正文最多256KiB加1字节超限探测；只接收UTF-8 application/json，压缩响应明确拒绝而非无界解压。非200响应不读取/记录错误正文。
- `AiChatCodec`严格校验完整请求的schema、连接epoch、配置、十一字段来源、模板、观察及预算；可在发送前复制查看精确HTTP JSON字节与SHA，adapter使用同一编码器。仅发送已审阅模板/指令/观察，无额外隐藏prompt、历史、tools或文件。远端认证字段不进入预览字节。
- AI内容合同升级为v2、新输入schema（末尾0002），增加明确`wireContract`、JSON输出格式及thinking策略；移除A阶段旧输入接受路径，不做静默兼容。模块运输v2、外层观察v2、领域提案v1保持各自职责。按[DeepSeek官方协议](https://api-docs.deepseek.com/api/create-chat-completion/)使用`stream:false`、`max_tokens`、`response_format:json_object`，DeepSeek明确`thinking:disabled`；模板必须已含JSON输出指令，不由adapter偷偷补写。其他注册供应商不擅自加入DeepSeek扩展字段。
- `AiSessionCredential`仅接收可信宿主显式传入的临时Bearer值，绑定完整配置指纹（含端点/模型/凭据代次）；无环境查找或持久化。撤销清除引用，不能宣称内存可靠清零，也不能撤销已到远端的请求/费用。新后台只读lifetime使配置变化跨线程可见，不从网络线程访问owner API；发送前/解码后再验证，原领域回包校验仍保留。配置改变不强制终止HTTP，正常取消和剩余deadline负责收敛。
- 队列/编码/网络共用原剩余时限，最长仍60秒。一个adapter最多一个实际活跃调用、4096终身请求UUID，重复不发送；取消不合作的工作未结束时不归还槽、不允许Dispose。原ModuleSession/宿主负责停止与回收，adapter不创建替代工作。
- 返回只接受单choice/index0/assistant/`finish_reason:stop`；严格UTF-8/重复字段/深度/结构/字节校验，拒绝截断、工具调用、refusal、Markdown和未知领域能力。有效结果仍是`NeedsDomainValidation`候选，不携带写授权。Token使用量若提供则检查非负、合计及输出预算，否则保持未知；诊断仅安全码/请求ID/状态码/字节/耗时/计数，不回显异常、密钥或响应内容。
- 新增13组测试（与A共27组）已注册**未运行**：精确预览/实际发送身份、凭据作用域/撤销、HTTP错误/重定向、坏JSON/截断/工具/usage、类型/编码/超限流、取消/超时/不合作回收/重配失效，以及生产handler真实loopback字节/重定向夹具。替身、真实loopback和真实付费模型验收三者不混用；具体TLS负例/完整故障矩阵及正式产品接线仍随E/F补齐验证。
- AI.Tests最终完整依赖Debug编译0警告/0错误5.49秒；完整Editor.Services.Tests（含正式Editor）Debug0警告/0错误9.77秒。首轮19.69秒因新增测试两处nullable错误失败，原源码/完整日志保留；修正后第二轮9.40秒成功，最终轮覆盖后续UUID/预算与用例补充。没有执行任何测试、IPC、推理、冒烟或性能。
- 证据`out/verification/m8-4/b-first`保存9份修改前原文、失败源码、日志/binlog、选定源码及程序集回执，不是全仓冻结。正式EXE SHA仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`；默认Editor deps静态检查无AI/Modules依赖，不等于搬移或默认产品运行验收。未部署、提交或推送。

下一步M8.4-C：将真实adapter与制作意图/原领域工作区连接，补齐动画/UI有界草稿转换与原资源审批，随后D正式AI界面/E验收准备/F整体测试。未使用用户密钥，实际本地端点/模型、远程外发及费用仍须在真实调用前明确。以下A交付是前一切片记录，不覆盖本节B的新合同与状态。

## 当前A阶段交付

- 新增可选`Ncma.AI`：DeepSeek供应商描述符、显式模型字符串、ChatCompletionsV1协议、本地字面loopback/远程HTTPS端点约束、不可变配置指纹、凭据引用/代次、owner-thread配置状态与每次重配的新epoch。其他供应商由可信代码注册描述符；注册不证明其真实协议兼容。没有密钥值存储、环境变量读取、网络或解释器启动。
- 官方远程端点为`https://api.deepseek.com/chat/completions`，模型ID按配置传入，不把旧模型别名写死。依据[官方首次调用](https://api-docs.deepseek.com/zh-cn/)及[Chat Completions协议](https://api-docs.deepseek.com/api/create-chat-completion/)（2026-10-25核对）。本地部署可使用明确的`http://127.0.0.1:<port>/v1/chat/completions`或IPv6 loopback等完整端点；不自动发现/下载模型、不把本地进程称为官方服务。局域网HTTP不属于本地loopback模式。
- 新增`AiRequestContent`：完整指令、模板文本/版本、精确配置/凭据引用代次、epoch、输出token/时限及固定不流式/不工具调用/不重试设置，只有显式空历史。指令和模板各≤8KiB；模块总输入仍≤64KiB、深度16。输出token为引擎保守默认2048、上限4096，不是供应商最大上下文声明，也不是费用授权。
- 原`ReviewObservationRequest`将复制请求内容与原授权观察组成审阅信封v2；原确定性模块的观察格式v1仍服务其独立用途，不是兼容旧AI格式。审阅指纹升级为v2并绑定预先生成的requestId；AI输入schema独立，模块运输仍v2，领域提案信封仍v1。完整逻辑请求可复制审阅，HTTP最终编码属于B阶段，不能称为已发送的真实网络字节。
- 可选`AiDomainRequestReview`校验schema/config绑定并走原人工批准；配置有效性守卫进入原展示、Approve、Claim及RequestCurrent/回包路径，受原busy重入保护。重配相同参数也不复活旧审批，修改内容必须新审阅；原读权限/来源/60秒有效期/单次消费不变。没有新MCP批准入口或第二套编辑历史。
- 新增`Ncma.AI.Tests`的14组合同/真实编辑服务夹具，使用原聚合harness；已登记解决方案、CMake构建/`NcmaModuleTests.AIContracts`及Build编译清单，**全部未运行**。没有重新生成CTest库存，不声称已有132项实测清单。
- 最终AI测试项目完整依赖Debug编译0警告/0错误8.94秒；完整Editor.Services.Tests（含正式Editor）Debug0警告/0错误9.68秒。首次编译因现有Roslyn的NuGet恢复TLS失败（3个NU1301）保留；正常官方源恢复后第二次编译成功19.91秒，后续守卫修改由最终编译覆盖。没有运行测试/IPC/推理/性能，也没有关闭或放宽NuGet审计。
- 证据在`out/verification/m8-4/a-first`：修改前10份原文、完整编译日志/binlog及选定源码/产物回执；不是全仓冻结。正式EXE SHA256仍`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`。默认Editor的本次deps静态检查没有AI/Modules/Editor.Modules运行依赖，不等于已完成搬移/无Python验收。

下一步M8.4-B：实现DeepSeek真实adapter及有界HTTP编码/读取，接入剩余时限/凭据作用域/无自动重定向与重试。先完成B–E，再F统一测试；A的本地/远程支持当前指配置合同，不是实际推理连接。具体本地端点/模型、远程凭据、项目外发和调用费用尚未授权；无需为编译提供密钥。未部署、提交或推送。

用户本次决定先跳过E5-D验证，准备M8.4方案。E5-D仍未验收：负载方向已确定为大世界探索，但代表性项目、固定路线和目标机执行环境尚未提供。原GTX1060 6GB/RX580 8GB、1080p、Low、30fps上限、平均≥30、1% Low≥27、单次Hitch≤80ms标准不变。本阶段AI延迟和本机功能测试不能替代目标机性能验收。

## 1 目标与范围

用户在正式编辑器内连接一个真实推理服务，输入制作意图，批准有限上下文外发，获取结构化提案，审阅完整差异后通过原事务修改并撤销。

首版采用“一次请求、一次模型调用、最多一份领域提案”的受控助手，不实现自主多步Agent。建议首个端到端任务为**对选定场景对象执行重命名或Transform调整**；资产元数据、动画和UI按原有真实业务边界扩展。

本阶段不包含自动安装/下载模型、任意shell/eval、代码执行、依赖安装、Git操作、全项目索引、后台扫描、live World自动读写、多供应商同时落地、多模态附件、长期记忆或自治工具循环。托管扩展与开发任务分别留给M8.5/M8.6；复杂大世界生成不作为M8.4首版完成条件。

## 2 现有基础与缺口

| 现有基础 | M8.4需要补齐 |
| --- | --- |
| `Ncma.Modules` v2、有界异步会话、单次领取、生命周期失效 | 真实模型adapter；不复制超时、背压和会话框架 |
| 可选`DomainModuleBridge` | 正式产品可信装配；不能因接口存在就声称已部署 |
| `ReviewObservation/ApproveObservation`，最多8项/合计64KiB观察 | 把用户指令、模板和精确接收服务纳入完整发送审批 |
| 原五种领域能力、完整差异、原历史 | 模型仅提供数据，原校验/权限/事务仍唯一 |
| 动画/UI接受原已准备proposal句柄 | 有界模型草稿到原Prepare/propose转换；禁止模型编造句柄 |
| 正式AI领域面板，默认无模块注册 | 连接、输入、外发审阅、进度、错误与诊断 |
| 真实Python TLS worker | 可选运输基础，不等于推理，也不要求模型调用都经过Python |

依据：`managed/Ncma.Modules/Contracts.cs`、`managed/Ncma.Editor.Modules/DomainModuleBridge.cs`、`managed/Ncma.Editor.Services/EditorDomainProposals*.cs`及[M8.3领域合同](M8_3_DOMAIN_PROPOSALS.md)。

## 3 服务选择和模块边界

首个供应商由用户选定为DeepSeek；合同支持本地和远程配置，保留其他供应商/模型的显式注册扩展，不自动回退。具体模型、端点、凭据及额度仍由用户配置；实际adapter实施前核验官方协议和依赖并锁定版本。

对用户已经运行的本地端点或已授权远程端点，采用**可选C# adapter直接连接**，不为一次网络调用额外增加Python往返。同一协议可配置本地或远程；本地服务的兼容性须独立实测。仅当后续模型需要Python专用推理能力时，才经已有受检gRPC worker接入专用adapter，本阶段不同时实现该路径。

已新增`Ncma.AI`和`Ncma.AI.Tests`，当前实现连接配置和请求合同；真实模型适配留给B。产品库向下依赖`Ncma.Modules`，不依赖live World或持有编辑写权限；测试库额外引用可选Editor桥验证原审批。Editor集成复用`Ncma.Editor.Modules`，凭据和连接生命周期由C#宿主管理。

正式宿主装配必须明确交付：复用Editor启动组合点，提供构建期明确引用adapter的**AI可选产品变体**，与默认Editor分别发布和校验。二者共用编辑器业务与界面，不保留旧编辑器，不扫描任意程序集路径，不提前建立M8.5通用插件加载器。默认Editor/Player继续无Modules/gRPC/Python/模型SDK依赖；可选变体无配置也不联网。若要改成默认依赖，需另行确认架构与独立性门禁。

本地只允许显式配置的字面loopback端点，不自动发现、启动或下载；本地模型也不是权限豁免。远程必须HTTPS、正常证书校验和固定获批接收地址，不接受模型指定URL、不自动跟随重定向。代理及服务端数据留存策略在接入评审中说明；客户端不能保证远端删除数据。

## 4 请求和两次审批

执行链：

`意图/选择 → 原授权观察 → 完整发送审阅 → 人工确认 → ModuleSession/模型 → 严格解析 → 原领域预检/完整差异 → 独立人工确认 → 原Commit/Undo`

### 发送合同

新增版本化、不可变的请求草稿，绑定原十一字段编辑来源、requestId、module/config/schema、服务/端点/模型、配置代次、凭据引用代次、用户文本、选入历史、模板版本、观察字节/SHA、预算和有效期。

- 人审的是**实际发送的完整内容及接收方**，不只是对象列表。可分页完整查看；内容、模板或目标变化均重新审批。
- 凭据不进入提示词/指纹/日志；审批绑定凭据引用与代次，认证字段由传输层注入。切换端点不得沿用旧密钥。
- 用户文本不是扩大读取范围的授权。首版拒绝附件、任意文件读取和全项目扫描；历史默认不回传，选入的消息同样计入审阅与预算。
- 不能在原观察审批后从可变全局状态追加prompt。明确扩展现有审批数据合同，登记新的输入schema；运输协议仍严格v2，不静默兼容旧输入。
- 保留原读授权重查、单次领取和60秒审批有效期。请求最长仍60秒；模型慢不自动续期。换文档/选择/目录、撤权、Play、重载和关闭使旧请求及待审结果失效。

### 输出合同

首版采用非流式、单个完整JSON对象。执行UTF-8、重复键、深度、大小、信封、字段及领域语义校验；拒绝Markdown围栏、前后杂文、超限和未知工具，不用正则“修好”坏提案，不自动付费请求修复。供应商结构化输出能力不替代客户端校验。

模型不得提供可信身份、已批准标志、历史票据、权限升级或任意工具名；复制正确UUID不构成写授权。场景/资产元数据结果走原领域预检。动画/UI返回有界草稿，由可信本地转换器调用已有Prepare/propose，仅生成候选；资源审批与完整差异确认后才可使用真实句柄。不支持的草稿明确拒绝，不能直接写文件或编造proposalId。

## 5 预算与故障处理

以下为拟采用的首版上限，实施时登记并验证，不扩大既有模块/领域预算：

| 项目 | 约束 |
| --- | --- |
| 用户文本 | ≤8KiB UTF-8，同时计入总请求 |
| 观察与模块输入 | 最多8项观察；文本/观察/schema引用合计≤64KiB；超限拒绝，不静默裁剪 |
| 结构 | 深度≤16；原事务≤128操作/64KiB |
| 供应商封装 | 编码后请求、解压后响应各≤256KiB；读取时限额，不先无限缓冲；领域JSON仍≤64KiB |
| 并发 | 工作区初版1个活跃请求，无自动排队；原16 outstanding/4096终身ID限制不放宽 |
| 时限 | 排队、编码、网络合计≤原60秒；adapter使用剩余时限 |
| 步骤 | 一次人工批准最多一次模型调用；自动重试、工具循环、隐式回退均为0 |
| 聊天显示 | ≤20条且总量≤128KiB；完整差异由原审阅对象持有，不依赖聊天缓存 |

Token上限在服务选定后冻结，字节不是token。无可信使用量时显示“未知”，不编造费用；客户端只能限定请求/token，精确账单硬上限需要服务侧支持。付费测试前确认请求次数、输出token和支出上限。

网络、解析和worker不得在simulation/render tick同步执行。取消立即禁止旧结果进入审阅，但后台资源仍由原会话持有直至收敛；面板区分“取消已请求”与“已回收”。取消不保证远端停止计费。重连更换代次、废弃旧回复，不自动杀进程或无限派生替代任务。

凭据首版默认仅会话内存保存，关闭释放引用，不宣称托管字符串可靠清零。持久化凭据需另行评审平台凭据存储；项目/场景/命令行/仓库/日志不存密钥。默认审计只记录请求身份、指纹、字节数、耗时、状态、实际使用量及原事务回执。完整提示/回复落盘须独立授权与保存期限，错误显示不得原样回显敏感响应。

## 6 小阶段实施顺序

A–D有界实现与夹具基线已具备并编译，尚未功能验收；E已完成隔离构建/静态库存、候选打包流程和清单合同增量，继续真实搬移/无服务/无Python宿主门禁准备；没有实际新包，F尚未开始。动画保留原配对MCP读授权前提，UI精确观察独立于MCP。A–F顺序推进；实施期只静态检查/编译并准备用例，全部实现后统一运行测试。

| 子阶段 | 实施内容 | 实现检查点 |
| --- | --- | --- |
| M8.4-A 连接与审批合同 | 单服务选型；配置/Secret引用/代次、完整发送草稿、预算、状态和拒绝码 | 接收者与内容绑定，批准后无隐藏上下文追加 |
| M8.4-B 真实推理适配 | 一个实际adapter；认证、有限编码/读取、deadline/取消、严格解析、脱敏诊断 | IO异步有界，失败不转成成功提案，无重试/回退 |
| M8.4-C 领域桥接 | 实际ModuleSession/DomainModuleBridge；场景首任务、元数据；动画/UI草稿转原Prepare | 从发送到Commit均验证来源/撤权；原事务仍唯一 |
| M8.4-D 正式AI工作区 | AI可选产品可信装配；连接设置、输入、外发审阅、进度、取消、差异、回执 | 界面能实际驱动完整链，无服务/回收/错误有明确状态 |
| M8.4-E 验收与交付准备 | 依赖清单、搬移包、无服务/无Python、负例、真实服务固定队列、脱敏证据策略 | 同源候选、实际测试库存及调用预算登记，不临时改负载 |
| M8.4-F 整体统一验收 | 双配置全量回归、真实服务与人工闭环；保留失败、修复复测 | 自动门禁和真实模型门禁分别有证据，未执行不记通过 |

### 工作区表现

沿用现有统一深色风格和标题栏，不重画其他区域。AI工具栏右侧显示“未配置/未连接/连接中/已连接/故障”，MCP状态单列；配置检查成功不等于真实推理成功。

主体依次展示任务输入/选定对象、完整外发内容/接收方、进度/取消、验证结果、完整差异/提交确认、原事务回执/Undo。提交依据原票据，不依据聊天里的“确认”文本。失效结果明确提示原因，不悄悄套用。

连接检查是用户显式发起、不带项目数据的操作；如果只能通过真实推理测试连接，先提示可能费用，不能在打开设置时自动调用。

## 7 整体验收门禁

### 自动合同与负例

替身仅验证合同：有效/损坏/重复字段JSON、超限UTF-8/解压内容、未知工具、提示注入、自批、伪造句柄、越权UUID/资源、密钥回显、401/429/5xx、证书/重定向拒绝、慢响应、取消不收敛、迟到/乱序和重连失效。

审批覆盖批准后修改文本/历史/模板/模型/端点、原观察撤权但revision未变、回包至提交之间来源变化；发送、领取、提交均只发生一次。拒绝/取消不新增历史；合法提交可原Undo/Redo。动画/UI须证明无可信句柄不能发布，未获批资源不被读取，原补偿/恢复规则不变。

### 编译和产品回归

- 当前全部托管项目Debug/Release各0警告/0错误，原生全目标构建；不把单库成功当全解决方案成功。
- 重新核对`ctest -N`与机器可读库存；131项是M8.3基线，新入口按实际清单登记，不固定伪报旧数量。
- 所有登记入口双配置完整运行，独立case全部执行再汇总；缺依赖/跳过/禁用不能宣称通过，不延长既有超时或削弱0B/像素/4096工作量。
- Python工具和真实worker使用匹配输入；本adapter不需要Python也不能删除原回归门禁。
- 默认Editor/Player独立性、AI可选包严格搬移、无配置/断网仍可编辑运行游戏；连接配置不能成为普通启动的必要条件。
- 不用整条Build.ps1/Build.bat作无部署验证捷径。登记正式EXE前后SHA、候选manifest与源码指纹；验证不默认替换安装。

### 真实模型与人工闭环

使用已获授权的真实服务执行预先固定队列，至少一个场景任务完成“批准上下文→真实响应→原预检→人工确认→提交→Undo/Redo”。资产元数据、动画/UI分别验证支持的有界任务，范围之外明确列出。人工审批不能由自动测试模拟后声称真人验收完成。

记录脱敏身份、服务/模型报告标识（若可用）、字节、耗时、真实使用量、领域拒绝或提交回执。固定队列包含恶意内容、取消/陈旧结果和无效建议；预先限制调用次数，保留全部结果，不重跑挑绿。正确拒绝只能证明安全边界，不替代成功制作任务。

服务未选择/不可用、费用未授权或人工未完成，分别标为待验收，不关闭整个M8.4。可先实现不联网合同和界面，不以替身代替真实模型通过。

观测首响应/总延迟、主线程/帧循环影响、取消后资源存续，不宣称AI链零分配。人工UI体验、SDK8/VS宿主、目标机/长稳和间歇原子保存根因仍独立保留；E5-D继续延期。

## 8 实施前待确认

1. 首个供应商已确定DeepSeek；真实调用前选择模型及实际本地/远程端点，**不要在聊天贴密钥**。
2. 外发内容和费用边界；实际联网、凭据使用、下载/安装分别审批。选本地不自动授权模型下载或任意进程启动。
3. 首个制作任务建议选定对象重命名/Transform布局，之后扩展其他域。

交付包括实现源码、服务/输入合同、可选产品配置说明、脱敏诊断和统一测试报告。提交、推送、正式EXE更新需后续明确授权。
