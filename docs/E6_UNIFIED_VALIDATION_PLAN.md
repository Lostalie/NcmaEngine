# E6 整体统一验证清单

## 后续授权：正式 Release EXE 更新（2026-10-25）

完整双配置回归结束后，用户另行要求更新 EXE。核对测试源码未变化、现有安装和候选包完整且编辑器未运行后，通过原原子部署流程安装本轮测试的 Release 候选 `9a8e4d8d859b4d169fc42651a2dd82aa/editor`，没有调用整条 Build 包装或强制覆盖。

- 正式入口：`F:\NcmaEngine\out\bin\NcmaEngine.exe`。
- 暂存包 `--validate-package` 与正式安装图形冒烟均退出 0，部署日志状态 `Complete`。
- EXE SHA256：`595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`。
- 主程序集 `NcmaEngine.dll` SHA256：`0B43DAB320A62A4A6E0DFDB2EBEE32312BF13D3EFF80AD05A261A567811562A5`。
- 可恢复旧安装：`out/deployment/9589fd6663f9434fa1cf56e74f5cc7a4/backup`，保留未删除。
- 部署证据：`out/verification/ecs-e6/deploy-approved-first.log`，以及该部署代次中的校验/冒烟进程与双输出流记录。

下文“正式 EXE 未变／未部署”是之前完整测试运行期间的事实，不代表本次授权更新后的状态。此次更新不替代人工、目标机或 E5-D 验收，也不关闭偶发原子保存的根因调查。

## 修复后第三轮整体实测（2026-10-25，本机双配置通过）

按用户“修复问题后重新测试”执行。**本机已配置的完整 E6 清单双配置通过**；下方第二轮失败是保留的历史证据，不再作为当前测试结果。偶发原子保存的根因仍未关闭，不能据本轮全绿宣称该问题已根治。

### 修复内容

| 范围 | 本轮处理及保留的约束 |
| --- | --- |
| Release 原生依赖跟踪 | 当 MSVC 只有 2052 语言资源时采用实际中文 include 前缀，不再依赖重新生成时是否继承 `VSLANG`。双配置真实错误前缀负例、正确前缀已知头依赖、头变化重编译及无变化不编译检查均通过。 |
| Gameplay 4096 超时 | 只把冷夹具逐对象 Create/Set 改为一次完整规范化作者快照，避免重复全量复制；原 UUID、值、数量、5 种运行负载、步数、查询/字节/隔离断言不变。未修改引擎作者写入 API，也不将冷构造优化宣称为 E5-D 性能验收。 |
| Export 分页 | 对齐未知声明的完整标签，额外确认该行是只读 Label；分页与作者字节不变断言保留。 |
| 重复渲染配置 | 无效作者候选在烘焙前被拒绝时仍记录一次 `render.ambiguous_configuration`；保持最近有效配置，不发布无效运行投影。 |
| Headless 资源合同 | Simulation 不读取纯显示用途的模型/环境/后处理库；图形宿主仍拒绝错误闭包。UI 路径、未知后处理模块、无效阴影配置等原拒绝断言保留，未放宽产品校验。 |
| 后处理生命周期 | 分开验证未知模块在烘焙前拒绝，以及“合法注册但缺少运行文件绑定”在资源构造后失败。后者继续检查待清理资源保留、原帧状态拒绝清理、明确新请求恢复及像素一致，未删除原生命周期覆盖。 |
| 渲染回调隔离 | 新架构下只保护运行 World 不足以阻止回调写作者文档。正式 Editor 提交路径增加作者文档只读作用域，测试增加回调前后作者字节逐项一致；非法写入和卸载被拒后，原命令/Undo/恢复仍正常。管线入口每配置 1616 项检查通过。 |
| 部署测试等待 | 等待精确创建的 apphost 进程，不再依赖 `Start-Process -Wait` 的进程树等待；保留 PID/创建时间及双输出流，并对该子进程设置有界等待。原 120 秒 CTest 预算、文件锁/未知文件拒绝、原子切换/恢复断言不变，仅操作受控测试目标。 |
| 原子场景保存 | 仅增加完整失败路径及原始 HResult；没有删除目标、强制覆盖、扩大共享模式或自动重试。见下方未关闭事项。 |

### 五项门禁与最终结果

- 实际 63 个 csproj 与验证 solution 成员逐项相等。最终全托管 Debug 26.57s、Release 26.28s，均 **0 警告 / 0 错误**；原生两配置全目标构建退出 0，Release 实际重编译而非只复用旧成功结果。
- 分别执行 `ctest -N` 和 JSON 清单，各 131 项；32 个 Gameplay 入口仍为 `TIMEOUT 30`，其他预算未修改。
- 每配置准备当前原生/托管测试输入和全新 5 种隔离候选包，先归档旧输入及索引。缺失依赖不跳过后算通过。
- 完整 CTest 使用 `--verbose --no-tests=error -j 1`，无过滤、排除、失败即停；实际 JUnit 名称与登记清单逐项相等。
- 未使用整条 `Build.ps1` / `Build.bat` 或根 solution 的 NMake 包装作为无部署捷径。正式 EXE 未更新，前后 SHA256 均为 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。

| 配置 | 实际入口 | 通过 | 失败 | 超时 | 跳过 | 完整 CTest 耗时 |
| --- | --- | --- | --- | --- | --- | --- |
| Debug | 131 | 131 | 0 | 0 | 0 | 1249.27s |
| Release | 131 | 131 | 0 | 0 | 0 | 1138.39s |

两轮退出码均为 0。Editor Services 各 243/243、CodeGen 各 26/26、真实构建/两种发布合同、部署 8 项检查及全部 Gameplay 分组通过。4096 五格最终每格 Debug 2.61–3.01s、Release 3.02–3.36s；部署入口分别 11.33s / 9.74s。完整像素、权限、回滚、零分配等原断言未降低；不扩大“0B”到整个 Editor 宿主。

### 保留的失败与验收边界

- 定向第一轮 9/14 通过，第二轮 12/14 通过，日志与原失败保留；随后才运行上述完整双配置清单。第一次编译的局部变量重名 3 错误及精确失败源也保留，最终全解决方案重新编译 0/0。
- 定向第二轮 `PostHosts` 在 `SceneDocumentFiles.Save` 再现 `0x80070497`（无法删除被替换文件），日志含唯一夹具完整路径。没有在该失败路径强制重试或删除目标；最终全新夹具的双配置完整运行未复现。**根因未确认，状态为待诊断，而非已修复。**
- SDK 实际为本机 9.0.315；SDK 8 未安装/未测试，VS 全框架宿主本轮未复测。E5-D 代表性负载和 GTX 1060 / RX 580 目标硬件验收仍延期。`configuredE6TestsPassed=true` 只表示本机已登记清单通过，不代表所有可移植性、人工或目标机性能验收完成。
- 未正式部署、提交或推送。全部构建/测试会话终止，最终只读进程核对没有匹配的测试辅助进程；未手动终止进程。受限 CIM 查询最初被拒，授权只读查询成功，不影响测试结果。

证据目录：`out/verification/ecs-e6/repairs-third`。含完整日志/binlog/JUnit/LastTest、清单、输入归档、新包索引及 `execution-result.json`。修改前归档 1047 个选定文件；最终 `tested-source-receipt.json` 封存其中 10 个改动源，完整双配置测试期间逐项哈希未变；随后才更新本记录及 AGENTS。这不是全仓库冻结。

## 整体第二轮实测（2026-10-25，未通过）

用户要求开始整体 E6 测试。本轮只验证并记录结果，**未修改产品代码、测试断言、工作负载或超时，未自动修复/重试失败项**。下方“分组入口已编译／未测试”是上一轮历史状态；本节是当前实际执行结果。

### 构建与执行门禁

- 当前实际63个csproj与验证solution成员逐项相等。全托管Debug11.55s、Release11.40s，均0警告/0错误；原生双配置全目标构建退出0（无增量任务）。未运行根solution的NMake包装或整条Build.ps1/Build.bat。
- 两配置分别运行`ctest -N`与JSON inventory，各131项。32个Gameplay分组的TIMEOUT逐项仍为30秒；未修改其他入口预算。
- 各配置准备当前样例、MCP/Transport宿主、原生插件及全新5种隔离候选包。旧共享输入和候选包索引先归档并核对哈希；没有复用历史候选产品作为本轮新构建。
- 使用CTest完整清单、`--verbose --no-tests=error -j 1`，没有名称过滤、排除或stop-on-failure。实际JUnit名称与inventory逐项相等；每配置131项均被调度，disabled/skipped均0。依赖缺失不作跳过通过处理。
- 正式EXE前后SHA256保持`C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。没有正式部署、提交或推送。

| 配置 | 入口数 | 通过 | 断言/异常失败 | 超时 | 跳过 | CTest耗时 |
| --- | --- | --- | --- | --- | --- | --- |
| Debug | 131 | 119 | 6 | 6 | 0 | 1459.01s |
| Release | 131 | 118 | 7 | 6 | 0 | 1336.53s |

两轮CTest均退出8，**整体E6未验收**。全部入口执行不等于内部所有断言完成：失败的依赖工作流在该失败点退出；超时入口没有完成；其他独立入口和已改造harness的独立case继续执行。

### 失败清单与直接证据

| 入口/范围 | Debug | Release | 当前直接证据 |
| --- | --- | --- | --- |
| NcmaDependencyTrackingTests | 通过 | 失败 | Release保存的MSVC包含文件前缀乱码，`correct-deps.stdout`中C/C++两个对象均为`#deps 0`，违反每对象一个已知头文件的断言；Debug前缀正常。构建退出0不能替代依赖跟踪正确性。尚未修复配置/编码根因。 |
| Rendering.PostHosts | 失败 | 失败 | `PostHostTests.cs:56`错误显式后处理库哈希应在gameplay前拒绝的断言；循环包含图形与Headless，需要按当前显式资产策略进一步核实分支，不能直接删掉负例。 |
| Rendering.PostAuthorHosts | 失败 | 失败 | Debug在`SceneDocumentFiles.Save:60`原子`File.Replace`抛IOException（无法删除要被替换的文件）；Release则到达与PostHosts相同的库哈希断言。两个原因分别保留，不能合并改判。 |
| Rendering.PipelineHosts | 失败 | 失败 | `PipelineHostProbe.cs:150`未观察到重复RenderConfiguration的`render.ambiguous_configuration` warning；失败前保持上次有效配置的断言已通过。 |
| Rendering.SceneUiJointProducts / ShadowJointProducts | 失败 | 失败 | `SceneUiJointPlayerProbe.cs:29`的`bad-model`闭包“开始gameplay前拒绝”断言；需核实Headless策略与资源消费者，当前不认定为通过。 |
| EditorServiceTests | 243case中241通过/2失败 | 243case中242通过/1失败 | 两配置均在`Program.cs:647`Binding/Export分页最后一页标签断言失败。Debug另有M3.6资源工作流`M36WorkflowTests.cs:116`保存时`File.Replace`异常；Release该case通过，原Debug失败仍保留。 |
| EditorDeploymentTests | 120s超时 | 120s超时 | 未输出首个完成检查即耗尽原预算；受控夹具保留，未删除日志/强制覆盖/延长预算。不是正式部署。 |
| Gameplay.Joint.4096.none/sparse/full/membership/replace | 五格各30s超时 | 五格各30s超时 | 每格仅输出BEGIN，尚无完成检查点，不能把耗时确认为运行热路径或冷构造。源码显示冷构造逐对象Create/Set会反复复制/规范化作者模型，是下一轮分段计时应优先确认的候选原因，不是已测得的耗时归因。 |

### 实际通过范围与边界

- 新Harness失败注入自检双配置通过；Editor Services失败后继续执行所有243个case并汇总，证明该入口不再首错即停。内部依赖步骤不强行继续，进程超时也不伪称已跑完。
- Gameplay32组中27组通过：9个功能/调度组、7个原诊断夹具、Harness及100/1000对象10格；4096五格仍未通过。原数量、步数、阈值没有缩减。
- CodeGen26/26、每配置17条真实构建命令、Editor/Player两次真实隔离发布均通过。共享字段差分、程序集聚合/引用移除的实际合同已执行，不只是编译成功。
- ManagedHeadless主表84/84及尾部原组、SceneDocument、SceneRendering、EditorCore、Application、Transport、Assets/Import、完整Player均通过。Player汇总为37个顶层case/group，不与其内部检查数混用。
- 主渲染、四档联合成本/图形恢复、资源生命周期等通过；本轮局部图像/成本诊断不等于E5-D目标机性能通过。既有诊断输出中的历史性能验收字段仍为false。
- SDK只实际使用本机9.0.315；SDK8未安装、未测试；本轮未复测VS全框架宿主。E5-D代表性负载及GTX1060/RX580目标机验收仍延期。

### 证据与下一步

证据根：`out/verification/ecs-e6/unified-second`。包括双配置完整构建日志/binlog、CTest verbose/JUnit/LastTest、实际131项清单、输入归档/新包索引、1047个选定源码归档与前后哈希、`execution-result.json`、依赖前缀故障副本。选定源码全部未变，不是全仓冻结。源码归档工具第一次引用不存在的engine/src和engine/include时即停止、未归档，原脚本保留；改为实际engine/source后成功，不把工具错误当产品失败。

全部运行会话已终止；最终只读进程核对未发现本次测试匹配的遗留辅助进程，没有手动终止进程。建议下一轮按“Release依赖前缀 → 显式资产策略与宿主负例 → 分页/重复配置警告 → 原子文件替换与部署夹具 → 4096构造/执行分段诊断”的顺序修复，保留失败证据和原阈值，之后重新执行双配置完整清单。该修复工作**本轮未执行**。

## 分组入口与跑完后汇总（2026-10-25，已编译／未测试）

用户明确要求不将30秒改成300秒，并要求修改后由用户复测。本轮**没有运行测试、smoke、性能测量、自检、构建合同或发布合同**；只执行静态源码核对、编译和 `ctest -N` 清单检查。前一节历史通过记录不作为本轮新harness的功能验收。

原单条 `NcmaGameplayTests` 已从CTest清单移除，改为32个独立进程入口，全部保留 `TIMEOUT 30`：

- 功能/调度：Core、Schedule、Recovery、SimulationCosts、IncrementalGameplay、IncrementalCommit、StructuralBatch、Movement、Ui。
- 原尾部诊断夹具：Baseline、ReadBaseline、MemoryV2、BaselineV2、ReadBaselineV2、DenseDiagnostics、ReadDiagnostics，全部保留，不因拆入口漏掉。
- 联合矩阵：`Joint.{100|1000|4096}.{none|sparse|full|membership|replace}`，原15格完整保留；每格是独立入口，原构造、4步窗口、数值/查询/状态/作者隔离断言逐字核对未变。
- Harness：新增失败注入自检，检查第一项失败、另一项失败、末项仍运行、finally清理、失败退出及空选择拒绝；已注册但未运行。

可直接以 `--group <名称>` 选择一个组；未知名称/空选择失败，不静默运行零case。`--list-groups` 只列清单。无参数手动运行仍遍历完整组集合；原显式测量选项仍保留。现有Build脚本使用名称前缀正则，因此仍覆盖所有新入口；本轮没有执行整条Build.ps1/Build.bat。

测试专用 `managed/TestSupport/CaseHarness.cs` 只链接到名称以 `.Tests` 结尾的工程，不进入产品程序集。独立case边界输出BEGIN、PASS/FAIL、END耗时和最终SUMMARY；保存完整异常并汇总失败名称，任何失败最终返回非零。异常继续发生时不会把已失败组改判为通过。Case内原依赖步骤、断言、using/finally清理保持原语义，不在无效前置上逐条吞掉断言。注册枚举失败和空集合也是失败；无法枚举剩余项时明确报告，不能声称剩余项已执行。进程崩溃/卡死仍由原CTest预算约束，不通过新线程、强杀单case或加长超时伪造“全跑完”。

修改的独立case/group循环覆盖Gameplay及内层增量/结构/Movement/UI、Application及Scene UI routing、Editor Services完整入口与专项选择、Player完整入口、SceneRendering、Interop、Presentation、UI、Animation及最终清理、Modules、gRPC、Transport与Physics的独立工作流、Rendering的独立CPU前置合同。Runtime、Scene、EditorCore、CodeGen、Assets、AssetImport现有已经捕获并汇总的case循环保留。真实Editor/Player隔离发布也改为两者都尝试后汇总异常，未实际执行。

单个依赖工作流不是若干可随意交换的独立case：原生GPU帧/资源生命周期、配对授权步骤、构建合同的clean→修改→删除→恢复等仍在本case首个失败处退出并展开清理；后续独立case继续。没有修改产品调度、权限、资源回收规则或任何0B/像素/数值门槛。

静态/编译结果：全63托管工程Debug212.01s、Release212.48s，均0警告/0错误；原生Debug重生成后无增量任务，Release实际重建成功，均退出0。两配置CTest清单从100变为131（移除1，新增32），每个Gameplay组的30秒属性已逐项静态核对；**没有执行这些131项**。仍需用户复测单格是否满足30秒预算，不能从编译成功推断时限通过。

正式EXE仍为 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`；未改正式部署、未更新共享测试输入、未提交推送。证据在 `out/verification/ecs-e6/harness-groups-first`，包含原始源码归档、完整构建日志/binlog、两份清单及 `audit.json`；选定指纹不代表全仓冻结。

用户复测可先运行以下命令（使用VS开发终端中的ctest；不要添加 `--stop-on-failure`）：

```powershell
ctest --test-dir out/build/windows-ninja-debug -R '^NcmaGameplayTests\.' --output-on-failure --no-tests=error -j 1
ctest --test-dir out/build/windows-ninja-release -R '^NcmaGameplayTests\.' --output-on-failure --no-tests=error -j 1
```

其余完整入口仍按既有依赖准备流程测试；缺少依赖不得跳过后宣布通过。原E6其他失败和E5-D延期状态不变。

## CodeGen / 调度夹具四项修复（2026-10-24）

本轮仅处理用户列出的四项，不替其他尚未通过的E6入口改判。先加入诊断输出、完成双配置门禁后运行完整CodeGen入口，诊断轮仍为22/25；失败日志和两份诊断版源码保存在 `out/verification/ecs-e6/codegen-fixture-repair-first`。

1. 原畸形身份清单实际为五个样本：foreign-type、missing-declaration、id-mismatch、version-mismatch、重复类型。现在逐个输出名称、完整构造、导入后的清单条数及诊断。第五个原构造使用两条完全相同特性，编译/导入后只有一条，不能触发重复条目检测；改为同一类型、不同ID的两个特性并先断言确实保留两条。没有放宽身份验证器。
2. `GeneratedComponentModuleAttribute` 分开拒绝参数、归属、Provider；Provider及其外层类型须可公开访问且非泛型，Module须为公开静态可读、无索引参数、返回实际ComponentModule符号的属性。新增14个负例（包括零/双/字符串参数的合成元数据形状）和两个合法公开/公开嵌套Provider正例，逐项打印构造与诊断，无崩溃且拒绝后不生成部分聚合入口。
3. 引用失效采用用户允许的**强制重编译断言路线**，未拆分增量provider。持久driver移除引用后与相同无引用输入的新driver比较，原无关组件输出保持不变，聚合依赖必须消失；恢复后再与干净driver比较。真实MSBuild合同新增 `11b-reference-remove-clean`，在全新artifacts根重新编译，不删除旧产物；同时核对增量/干净聚合文本相同、PE不再引用Right，恢复后文本及PE均重新包含Right。构建模式因此由16条增至17条实际命令。
4. 字段负例逐项输出两侧 `(case, memberPath, exceptionType)` 以及完整消息/预期。memberPath明确是受控fixture目标，不冒称异常内部携带的路径。十对诊断仅Entity预期不符：实际Entity带JsonConverter，两侧共享规则均先拒绝自定义JSON。修正该预期，继续要求两侧相等、精确ArgumentException类型及预期消息类别；符号适配器、反射适配器和共享规则均不变。调度指标fixture仅将资源r声明为 `new ScheduleResource("r", WorkerSafe: true)`；执行器、worker非安全资源拒绝用例、8帧/0与2worker及严格0B断言不变。

全63工程Debug/Release最终分别13.29s/13.26s，0警告/0错误；修复后完整重编译分别161.42s/161.77s，亦0/0。原生双配置完整目标无增量工作、退出0；CTest清单各100。未运行根solution NMake包装或整条Build.ps1/Build.bat，正式EXE SHA256仍为 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。

| 实际验证范围 | Debug | Release |
| --- | --- | --- |
| 完整CodeGen + ManagedHeadless两个CTest入口（最终精确诊断前缀断言） | 2/2，12.89s | 2/2，12.36s |
| CodeGen内部组 | 26/26 | 26/26 |
| Headless主组及原附加组 | 主组84/84，附加组全部通过 | 主组84/84，附加组全部通过 |
| 实际BuildContracts + PublishContracts | 2/2，149.35s | 2/2，149.70s |

每个配置均已执行17条实际构建/发布夹具命令，以及真实Editor/Player两种隔离发布；负例必须命中原诊断，发布只检查文件/deps/PE依赖，不启动或部署。最终仅把模块负例诊断收紧为精确前缀匹配（避免样本名称中的单词误命中），再做上述双配置全量编译及两个完整入口复测；实际构建/发布合同涉及的源文件指纹未变，收尾回执逐项复核。初次修复回归2/2（13.13s/12.49s）也完整保留，不覆盖日志。

证据索引 `out/verification/ecs-e6/codegen-fixture-repair-first/execution-result.json` 汇总原始/诊断版源码、双配置编译binlog、CTest日志/JUnit、四份真实构建/发布回执、选定源码/程序集指纹和EXE前后哈希；是选定证据，不宣称全仓冻结。所有执行已终止，无辅助进程人工终止。其他基线E6失败仍待修复并重跑全100入口；E5-D延期，SDK8缺失/VS宿主本轮未复测，不宣称整体E6通过。未部署、提交或推送。

## Owner 夹具准备与全量静态巡检（2026-10-24）

按用户要求，使用既有 `PrepareRenderAssets` 补齐夹具依赖，不修改产品构造器、`StartPlay`、资产闭包或权限。[逐文件审计](EDITOR_OWNER_FIXTURE_AUDIT.md)覆盖47个原测试文件、103处显式构造（102行）和11处目标类型简写，并检查共享Fixture与间接PlayControl。仅静态巡检不声称114处都完成运行验证。

环境Inspector以真实NCE/描述文件/哈希替代随机占位引用；后处理夹具有几何时生成真实NCA网格及材质槽，geometry=false则持有真实空租约。巡检追加修复环境发布的两条Play路径及M3.6导入放置后的PlayControl；空场景断言借用同一租约身份，发布场景准备实际资产。SceneRendering隔离夹具恢复完整策略并使用原AssetFixture，不以Simulation绕过。所有原字节不变、Play/Stop隔离、Undo/历史与权限断言保留，额外检查准备过程不会写作者数据或历史。

双配置全63托管工程最终编译0警告/0错误（Debug15.69s，Release15.45s）；原生全目标无增量工作且退出0；CTest清单各100入口。未调用整条Build.ps1/Build.bat，原共享测试输入归档后按配置更新，正式EXE SHA256保持`C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。

| 范围 | Debug | Release |
| --- | --- | --- |
| 原环境Inspector/环境发布/后处理/M3.6导入工作流修复回归 | 33/33通过 | 33/33通过 |
| 完整Application、SceneRendering、AssetImport三个入口 | 全部通过 | 全部通过 |
| 完整EditorServices入口 | 未通过；最终源码复测66.26s | 未通过 |
| 本轮四入口CTest | 首次3/4，85.48s；最终Services单独复测仍失败 | 3/4，77.89s |

完整Services已越过原资产准备失败，现停在 `Program.cs:669` 的“Binding and Export pagination preserve complete unknown metadata”断言。该项不调用Play，不属于资产租约补齐；本轮未改其断言或产品代码。原完整注册与失败日志保留，后续用例仍受阻。新增显式 `--asset-owner-fixture-tests` 仅调度本次相关的33个**原用例体**，不删减负载，也不以定向成功改判完整Services或整体E6通过。

证据根：`out/verification/ecs-e6/owner-fixture-audit-first`，总索引`execution-result.json`，包含49份原源码/文档归档复核、逐构造位置、选定源码与程序集指纹、双配置构建日志/binlog、清单、完整CTest日志/JUnit及定向日志。所有进程均终止，未人工终止辅助进程；未正式部署/提交/推送。其他E6首轮失败仍待处理，E5-D继续延期。

## 资产策略定向修复（2026-10-24）

用户选择增强版方案2，具体合同见[场景资产策略](SCENE_ASSET_POLICY.md)。共享策略统一References/Prepare/Inspect/Resolve以及真实Player/Editor宿主：Headless/cpuOnly显式选择Simulation，图形模式默认完整；无租约不是豁免开关。模拟消费者Animator/RootMotion/Action依赖仍严格解析，纯视觉依赖无消费者时不解码。闭包v2包含策略及resolved/not-required区别；不扩大MCP权限，也不修改脚本/物理/UI独立合同。

五项门禁继续执行：全63托管工程Debug/Release最终分别13.74s/14.12s、零警告/零错误；此前两轮0/0编译日志亦保留。原生两配置全目标均无增量工作、退出0，未修改原生源码；`ctest -N`各100入口。没有运行根solution的NMake包装或整条Build.ps1/Build.bat。使用原候选打包脚本重新发布当前源码的五类隔离包/配置，原索引和测试输入改写前归档，不更新正式安装。

| 实际范围 | Debug | Release |
| --- | --- | --- |
| 专项策略（完整SceneRendering中也注册，不重复计数） | 5/5 | 5/5 |
| 实际cpuOnly Editor + 原资产/脚本闭包 | 7/7 | 7/7 |
| NcmaPlayerTests，完整入口 | 66项通过，64.45s | 66项通过，63.59s |
| NcmaSceneRenderingTests，完整入口 | 147/147，2.58s | 147/147，2.38s |
| NcmaRenderingTests.PlayerRunnerLifetime，完整入口 | 46检查，2.62s | 46检查，2.69s |
| 本轮定向CTest（原超时、负载和阈值不变） | 3/3，69.68s，退出0 | 3/3，68.69s，退出0 |

两次修复前失败不删除、不改判：

1. 新CPU宿主夹具把未使用视觉包配置成不存在路径，被ProjectContext的原路径检查拒绝。保留该安全合同，夹具改为存在但内容非法的包，证明CPU宿主不解码它。低层无活动根不打开包，不等于配置可声明任意不存在/非法路径。
2. 首次Debug定向CTest为2/3，Player推进到原FBX纯视觉场景的“损坏Null包必须失败”断言。原场景只有蒙皮/ClipPlayback，没有模拟消费者；现在明确断言Headless不读取无关包。**没有删除损坏包拒绝覆盖**：真实ASCII/binary FBX RootMotion场景各保留打包/未打包正例，另增Headless与DX11损坏包负例，均必须在tick0/零模块/零渲染帧拒绝且无关闭错误、最终文件锁释放。原图形场景与deployment manifest损坏拒绝，以及每模型32次生命周期循环全部保留并实际通过。

NCA/NCPAK仍完整验证且可能保留同代模型成员，不宣称实现最小化驻留或模拟资产完全不包含视觉字节。本轮不是全阶段100入口重跑；完整EditorService等其余8个首轮失败入口尚未修复/复测，不能将专项7组当完整Services通过，也不能由三入口通过推导E6整体通过。后续仍需修复剩余问题，再执行双配置完整回归。

正式EXE SHA256仍为`C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。证据`out/verification/ecs-e6/asset-policy-first/execution-result.json`包含选定源码/原始归档/构建日志/binlog/CTest清单和JUnit/完整输出/候选索引与manifest指纹；不是全仓冻结。失败夹具源码与日志保留。所有执行终止，无正式部署、提交、推送或人工辅助进程终止。E5-D延期、SDK8未安装、本轮VS宿主未复测。下方首轮89/100是历史完整结果，保持原样。

2026-10-24：E6-A、E6-C以及E6-B有界实现已接线。用户要求的五项前置门禁已落实，整体E6首轮双配置验证已完成；**Debug和Release均89/100，整体E6未通过**。E5-D目标机性能验收继续延期；旧E3/E5失败不改判、旧格式和历史性能结论不复用。下方B4-B和“未运行”描述保留为此前状态，以本节最新实测为准。

## 整体E6首轮实测（2026-10-24）

构建门禁覆盖全部63个当前托管工程（包含根solution中的52个托管工程及11个新增工程）和原生CMake全目标。根solution的NMake包装调用整条Build.bat，因此未执行；使用`unified-first/AllManaged.sln`与独立原生构建覆盖目标图，不冒充直接构建根solution。Debug首次因Assets.Tests五处旧Document.World引用失败，原源码、0警告/5错误日志和binlog保留；迁移两项作者模型测试至UUID/ObjectCount/Document只读入口，保留历史、权限、线程断言并增加作者字节/revision不变检查。之后Debug全托管13.36s、Release147.24s均0警告/0错误，原生全新两配置各322步也无警告/错误。没有通过删除工程、恢复兼容入口或抑制警告完成门禁。

两配置`ctest -N`各100入口（93处静态定义加7处循环展开），名称相同，无禁用项；当前插件、Gameplay样例、MCP/传输辅助程序、五类产品候选包及可选Python模块客户端按配置准备。现有共享测试输入改写前归档并逐文件校验，正式out/bin不部署。执行`ctest --parallel 1 --output-on-failure --output-junit ...`，没有排除表达式、缩减负载、延长原超时或放宽分配/数值阈值。

| 配置 | 实际调度 | 通过 | 断言/异常失败 | 超时 | 结论 |
| --- | --- | --- | --- | --- | --- |
| Debug | 100/100 | 89 | 9 | 2 | 未通过，1169.76s，CTest退出码8 |
| Release | 100/100 | 89 | 9 | 2 | 未通过，1052.16s，CTest退出码8 |

两配置失败入口名称完全一致：PostHosts、PostAuthorHosts、PipelineHosts、PlayerRunnerLifetime、EditorService、Player、CodeGen、ManagedHeadless、SceneRendering；Deployment（Debug120.02s/Release120.03s）和Gameplay（30.02s/30.01s）超时。每配置JUnit均disabled=0、skipped=0，实际名称逐项匹配各自的100入口清单。部分聚合入口遇首个失败即停止，**100个CTest入口调度完不等于其内部所有用例执行完**。尤其Services/SceneRendering的显式资产准备异常阻断了后续用例，不能将缺依赖/未准备的业务路径记为通过。

生产字段差分、三组代码生成注册聚合用例、运行时组合注册及严格0B调度热路径等已在两配置实际通过，但CodeGen总体各22/25、ManagedHeadless主组各83/84，不能以局部通过替代整体验收。本轮CTest中的B4-B构建合同和真实Editor/Player发布合同两配置均通过；这与生成器内存fixture失败是不同验证范围。Gameplay基础61组、增量Gameplay3组和增量提交12组完成，但联合负载尚未完成就触发30秒超时，不得记为Gameplay整体通过。

| 未通过入口 | 两配置共同首个失败/未完成点 | 下一轮要求 |
| --- | --- | --- |
| NcmaRenderingTests.PostHosts / PostAuthorHosts | PostHostTests.cs:91，失败准备后构造持有者断言 | 核查作者投影/运行准备的失败位置和所有权；保留真实失败注入与回收断言 |
| NcmaRenderingTests.PipelineHosts | PipelineHostProbe.cs:150，重复配置warning不可见 | 核查配置失效后的诊断路径，不删除警告要求 |
| NcmaRenderingTests.PlayerRunnerLifetime | PlayerRunnerLifetimeTests.cs:30，调用者保留runtime/native/assets状态不符 | 核查关闭失败的真实保留状态和新依赖租约 |
| NcmaEditorServiceTests / NcmaSceneRenderingTests | EnvironmentInspectorTests.cs:103 / Scene.Rendering.Tests/Program.cs:90，Play前资产未显式准备 | 使用真实准备流程补齐路径；不能取消闭包门禁或跳过后续用例 |
| NcmaPlayerTests | Program.cs:398，Headless渲染元数据/资产解析语义不符 | 明确独立运行依赖与Headless无渲染约束，保留相关断言 |
| NcmaCodeGenTests | ComponentIdentityTests.cs:64/77、FieldContractTests.cs:178，畸形manifest诊断、引用移除输出、JSON拒绝差分 | 独立验证实际诊断/生成结果及规则优先级，再区分实现与预期错误，不能仅改为接受现状 |
| NcmaManagedHeadlessTests | ScheduleMetricsTests.cs:14，worker访问默认owner-thread资源被Compile拒绝 | 明确测试资源安全声明；保留线程保护及Bytes/AllocatedBytes实测要求 |
| NcmaEditorDeploymentTests | 120秒超时，无首项断言输出 | 两个隔离夹具均保留，定位阻塞后复测；禁止改正式安装、强制恢复或直接延长时限冒充修复 |
| NcmaGameplayTests | 原30秒内联合负载未完成 | 定位真实工作量/时间消耗；不减少对象数或移除联合矩阵 |

以上是失败定位，不提前判定均为测试问题，也不删除安全校验迁就用例。后续先修复已定位入口并完整执行此前阻断的内部用例，再重新通过双配置构建门禁及100入口整体回归；当前不进入后续里程碑、不宣称E6验收完成。

正式`out/bin/NcmaEngine.exe`构建前、Debug后、Release前及两轮结束后SHA256均为`C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。证据根`out/verification/ecs-e6/unified-first`保留门禁、全部构建日志/binlog、CTest清单/JUnit/完整输出、输入归档和候选包索引；总索引`execution-result.json`、逐入口`debug-first-result.json`/`release-first-result.json`。1115份选定源码/配置快照不是全仓冻结；测试期间不改被测代码。SDK9.0.315实测；SDK8未安装、此次VS宿主未复测，均未覆盖。没有调用整条Build.ps1/Build.bat，没有正式部署、提交或推送。两轮测试进程均已结束，未人工终止辅助进程，失败和超时证据不删除。

## B4-B实际执行结果（2026-10-24）

使用当前源码重新构建CodeGen.Tests：Debug35.34s、Release38.28s，均零警告/错误。通过显式`--build-contracts`与`--publish-contracts`入口串行执行四轮，并非只检查源码或运行历史测试DLL。本次未通过CTest调度，不将直接入口结果冒充整个CTest回归。

| 配置 | 构建合同 | 实际Editor/Player发布 | 结论 |
| --- | --- | --- | --- |
| Debug | 16/16，包括4条预期非零拒绝 | 2/2，68.51s / 8.31s | 本机合同通过 |
| Release | 16/16，包括4条预期非零拒绝 | 2/2，68.68s / 7.96s | 本机合同通过 |

合计36条实际子命令（含两次SDK版本查询），8条负例均必须非零并命中NCMA007/NCMADEP001/NCMADEP002；没有意外失败或超时，没有修改产品代码、测试断言或阈值。干净/no-op/增量/声明移除恢复/引用冲突恢复及fixture发布通过；no-op哈希和写入时间不变，失败编译保留上次成功DLL。

真实发布为win-x64/framework-dependent，无裁剪/AOT/单文件。每配置Editor65文件、Player51文件，apphost/runtimeconfig存在，文件名、deps与托管PE引用无CodeGen/Roslyn泄漏；全部文件尺寸与SHA256再次核对。未启动、未部署，不代表M9完整资源包或终端机器运行验收。正式EXE前后均为`C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。

证据索引：`out/verification/ecs-e6/b4b-execution-first/execution-result.json`，包含四个UUID运行目录与回执哈希。各目录保留36条argv/PID/stdout/stderr、34份MSBuild binlog、负例fixture历史及发布清单；另有当前入口构建日志、31份选定源/文档原文和哈希。仅为选定源码证据，不是全仓冻结；原失败记录不删除。

本机仅SDK9.0.315；SDK8未安装未执行，本次没有复测VS宿主。字段差分、注册聚合新增用例、A/C及其余E6运行回归仍待统一执行；现有构建合同文本比较针对组件生成文件，不冒充新聚合入口的完整功能测试。未提交、推送或更新正式EXE。

## 准备与证据

本轮用户明确的五项硬门禁（2026-10-24）：① Debug、Release分别完成全解决方案目标图构建，零警告/零错误后才能进入运行测试；② 登记正式EXE完整SHA256并复核不变；③ 对两个配置运行`ctest -N`并核对实际点数与条件入口；④ 缺依赖的入口明确未完成，禁止排除后宣布整体通过；⑤ 禁止直接或间接运行整条`Build.ps1`/`Build.bat`作为无部署验证捷径。根sln的NMake包装项目会调用Build.bat，因此本轮用覆盖所有当前csproj的验证solution加独立原生CMake全目标覆盖相同目标图及新增项目，不调用包装入口。证据根为`out/verification/ecs-e6/unified-first`，状态以实际门禁结果为准。

1. 开始时记录当前源码、工作区脏状态、实际SDK/编译器/宿主版本、原生工具链、配置、GPU及正式EXE哈希；不固定SDK补丁版本。生成器netstandard2.0/Roslyn4.8，产品与测试net8/C#12；SDK8/9及VS17.8+宿主矩阵分别记录。此前局部compile-result不等于全源码冻结。
2. Debug与Release分别构建当前全部相关测试、实际Editor/Player、Gameplay.Sample及原生插件；不使用历史测试DLL或out/managed中的旧样例。原样例/资源/着色器/FBX/插件依赖按已有流程准备；不能以空场景或更小负载替代原矩阵。
3. 先用CTest的只列举模式核对配置后的实际入口，再运行完整用例。CMake原生/IPC/GUI/资源测试的参数及插件路径不重新手写猜测；缺少依赖时记为未完成，不能跳过后宣布通过。
4. 不直接运行带正式部署步骤的整个`Build.ps1/Build.bat`作为无部署验证捷径。可分别使用既有构建、准备与CTest入口，但不执行最后的正式EditorDeployment；`-SkipTests`也不意味着脚本不运行其他样例/工具。
5. 每轮失败保留对应源码、完整日志、实际输出和环境。修复后重新运行关联用例，再运行整体回归；原失败记录保留。超时/退出不明不算通过，不自动缩减数据、放宽0B/数值阈值或新增GC例外。

## 已核对的入口矩阵

| 领域 | 当前入口 | 关键覆盖（原用例全部保留） |
| --- | --- | --- |
| 生成器内存fixture与字段差分 | NcmaCodeGenTests | 字段/schema/形状、Export、跨程序集manifest、引用替换、增量driver、原诊断负例；每个生产GenerateComponent的源码模型对实际ComponentMetadata.Prepare逐字段比较ID/顺序/kind/flags及CLR/JSON路径，源码清单与程序集清单必须相等；标量/嵌套/忽略成员/JSON自定义拒绝差分 |
| 真实构建fixture | NcmaCodeGenTests.BuildContracts | 记录实际SDK、生成器PE目标框架/Roslyn4.8依赖、干净/no-op/编辑/移除/恢复、组合引用冲突/移除/修复、编译/发布依赖泄漏与恢复 |
| 隔离真实发布 | NcmaCodeGenTests.PublishContracts | 当前Editor及Player、win-x64/framework-dependent、不裁剪/AOT/单文件；apphost/runtimeconfig、deps及PE程序集引用中不含编译器；不启动、不部署 |
| ECS与调度 | NcmaManagedHeadlessTests | GeneratedComponent、CompositionRegistry复制/去重/预算/失败隔离与Graph/Execution/Permission/Recovery/Completion/Buffer/Budget/Metrics；原4096行、严格0B、稀疏写、超时隔离、阶段/结构预检 |
| 作者/运行分离 | NcmaSceneDocumentTests | AuthoringStore、Bake、RuntimeSceneState/Inspection、Prefab及原分页；作者与真实运行状态分别验证 |
| 编辑事务/元数据 | NcmaEditorCoreTests | 原批次/删除权限/历史、ComponentMetadataConsumer、ReadQuery，不能以编译成功代替Undo/原子性验证 |
| 实际服务/Presenter | NcmaEditorServiceTests及既有子入口 | 运行检查与人工批准、原差异事务/面板、资源脚本闭包、调度面板、声明组件/Export/目录换代、原完整工作流 |
| Gameplay | NcmaGameplayTests | SchedulePlay/ScheduleRecovery及原固定步/故障/元数据/运行事务断言；历史性能合同拒绝不得当性能通过 |
| 宿主/传输 | NcmaApplicationServiceTests、NcmaEditorTransportTests | 恢复、原IPC/MCP/配对/共享受众/过期/撤销；不扩权限、不能以直接服务调用替代传输 |
| 场景渲染/依赖 | NcmaSceneRenderingTests | GeneratedDomain、BakeDependency、真实提取器及完整原环境/UI/资产准备矩阵 |
| Player与实际GPU链 | NcmaPlayerTests、NcmaRenderingTests及全部既有子入口 | 当前runtime与author分离、恢复/选取/资源/动画/阴影/UI/数值和图像/严格分配/时序矩阵 |
| 邻接回归 | 原CTest中UI/动画/资产/导入/物理/模块/ABI/部署负例等入口 | 保留原前置资源、平台条件与负例；部署测试自身只可操作它的受控夹具，不触发正式部署 |

以上是源码入口审计，不是执行证明，也不是用子集代替整套CTest。测试执行时需记录实际发现数量、执行/失败/未执行数量和原因；CMake的条件入口与本机环境不匹配须显式列出。

注册聚合新增门禁：CodeGenTests还须运行每程序集自动聚合/声明删除与顺序/错误声明、真实19类型完整组合及原挂载限制；既有跨程序集菱形身份检查允许单一依赖聚合，不允许重建同一类型注册。实际Editor与Player分别使用自身AssemblyName生成根，验证两者组件目录相同，仍不将目录相同视为场景组合、资源/脚本闭包或权限验收。以上新增6组用例当前均未运行。

## B4-B运行设施合同

两个新模式均由`Ncma.CodeGen.Tests`承载，普通构建不调用它们。CMake按当前配置传入仓库根、Debug/Release和找到的dotnet绝对路径；`Build.ps1`现有`NcmaCodeGenTests`前缀分组同时涵盖二者。串行执行，单个编译子进程180秒超时，外层CTest3600秒，不放宽任何产品性能阈值。

每次在`out/verification/ecs-e6/codegen-build-contracts/<配置>/<模式>-<UUID>`建立全新目录；拒绝越界/reparse路径，保留所有改前fixture版本，不删除目录/失败产物。每条命令保留argv、工作目录、PID/启动时间、stdout/stderr及MSBuild binlog；SDK解析命令没有binlog。超时只停止本次持有的精确子进程树，失败不扩大清理范围或自动重试。

构建模式现准备17条命令（1条SDK版本记录、16条构建/发布，含引用移除后的独立干净重编译）；移除固定SDK/伪造不兼容宿主负例，新增实际生成器PE框架与Roslyn4.8引用断言。失败必须非零退出并命中`NCMA007/NCMADEP001/NCMADEP002`，不能接受无关失败。泄漏负例通过NuGet包引入编译器依赖，不引用机器SDK目录。SDK8与SDK9必须分别实际执行；没有安装的宿主记未执行，不能将包目标框架或单SDK编译替代另一宿主验收。

便携生成器接线状态：已删除仓库global.json及Core-only/9.0.315守卫；分析器使用netstandard2.0、Microsoft.CodeAnalysis.CSharp4.8.0，现有schema仍由System.Text.Json8.0.6校验。JSON依赖仅交给分析器加载器，不通过普通ProjectReference输出泄漏；测试自身显式引用包。当前SDK9完整Editor编译通过；本机VS4.14全框架MSBuild的标准共享编译样例通过。非共享csc在本机F:路径遇到CS8034/0x80131515，读取.NET Framework Zone映射返回Internet；不更改系统信任策略或冒充该模式通过。SDK8未安装，尚未实测。该限制与记录见实施计划最新节。

生成失效以新的PE manifest及Generated类型缺失为准；`EmitCompilerGeneratedFiles`磁盘调试文件可能留存，全部位于Compile输入之外，既不重新导入，也不为“清理输出”删除原证据。干净与增量比较生成文本和实际manifest，不要求不同构建根的PDB/MVID整文件字节相同；no-op则要求同一输出哈希和写入时间均不变。失败构建不能覆盖最后成功DLL。

发布模式只发布真实Editor/Player到私有目录；另有fixture发布正负例。检查文件名、deps库名与每个托管PE的AssemblyReferences，不加载或运行被测程序集；保留每个发布文件的尺寸/哈希。此检查是依赖卫生，不是恶意重命名/篡改的安全扫描，也不是完整M9资源包/终端机器可运行证明。

最终result记录实际通过组、每条退出结果、失败详情、正式EXE前后哈希；任一失败或EXE变化都不报成功。source-pins只是选定源文件，不宣称全冻结。本机SDK9双配置已执行通过（见上方实测记录），其他宿主及整体E6回归尚未完成；后续失败仍须保留证据并修复。

## 完成判据

- 当前双配置完整构建及整个E6适用测试通过；缺失环境/未运行项有明确记录，存在必要未运行项时不宣布整个E6通过。
- 作者/运行隔离、唯一事务/权限、代码生成、实际消费者和调度原语的真实断言均保留；编译与测试、功能与性能结论分开。
- E5-D仍是延期，不能把本机调度0B或小fixture结果换算成GTX1060/RX580目标游戏性能通过。
- 正式EXE/部署目录不变；提交、推送或部署按后续明确授权执行。
