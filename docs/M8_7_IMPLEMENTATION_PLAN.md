# M8.7 联合验收实施计划

## 最新交付：已纳入19项变化、重建和定向测试（2026-11-15）

用户明确要求将18改1新全部纳入，重新构建后提交推送。此前文件数保护失败和旧候选全部保留，不改判。

- 新捕获 `747776306c4a488b87ee5f22ec6062b3`，5096文件；source `8BB33BF03B571489DBD5928518957047D0275B6F57615F95D1F0F8CB4C60DED7`；manifest `46F4048B9C013E64AFE1FE28046B7179095B53D65B9864F76F40F7CC5C366C00`。
- 原六任务离线准备，原生265步、原六产品Release发布成功384.79秒，完整文件闭包核验通过。AI候选 `out/package/m2-7/Release/0aabbefb5fc84e5f94e9ad7ee7c214f0/editor-ai`，151文件，AI开启、扩展关闭。
- EXE `F60240AE65E0C8D0D1F9671DB369EEBC85FFF3F059D194AA4C291FEF1E938182`；DLL `E03AF7A64F9782AEFE424D175B4E8C1D64878B660C98DFF794918F0FF4694106`；部署manifest `C75AB6322D29AB1FDE56C44C83D84EAFE814E3A503249439AE4E3D33F43F61EA`。引导apphost同哈希是正常的，不能只看EXE哈希判断托管逻辑是否更新；不得只复制单个EXE脱离包闭包。
- 独立原六图离线verification准备；实际Services.Tests（包括实际Editor）及AI.Tests图：Debug85.80/101.18秒、Release94.18/77.33秒，均0警告/错误。不是全解决方案或全部可选变体构建。
- 私有CTest仅绑定原13个workflow contracts入口和原AIContracts入口，保留30/60秒超时；`ctest -N`和JSON各14项，Debug14/14（9.31秒）、Release14/14（8.86秒）。每配置374个主用例通过；嵌套8用例7通过1故意失败属于原观测harness负控制，父用例验证完整执行和失败保留，不计为额外产品失败或额外374用例。
- 新证据目录 `out/verification/m8-7/delivery-current-2026-11-15`；compile-result SHA `5E7226A4A5D2A984987B418461E0E63F09A2BF3CD407C4083D534FCB58AEC8A6`，test-result SHA `A32A5EE06670F0C80F395213892626399CA9CCCCDEC95EAF1A219497D4BF76ED`。完整LastTest.log与JUnit分别固定，不能仅以截断stdout推断case覆盖。
- 当前混合输入checkpoint SHA `E025CB6046DC0DAB44075794DB6FB5A0CA228658AC84801E314F6BF708D996A2`：固定25运行/支持树仍63ed/Release，只有两个Release包索引推进7477（m2-7 `63B166E0...`，m8-4 `E3717B57...`）。8索引均预先独立备份，旧567874六产品仍完整。不能把旧全量输入/回执改签为当前全量通过。
- 正式151文件、EXE379AA113、journalF5BDC19D保持；未启动实际EXE、PS5.1或真实模型，未部署。两类环境门禁仍延期；本次定向通过不是M8.7/M8/E5-D/人工/长稳验收。
- Git范围224项源码/测试/构建脚本/文档，排除AI配置、密钥、IDE及out；提交/远端结果以本目录git-commit.json、git-push.json及Git为准，不提前宣布成功。包Git元数据仍是构建时4fac检查点，精确源码以本次捕获指纹为准，不重写manifest。

以下为历史交付与暂停记录，由上述最新授权和证据更新当前状态。

> 最新交付暂停（2026-11-15）：Publish.ps1 在任何 staging 之前因文件数不符停止（实际 224，初始记录 223）。随后确认 18 个已捕获源码修改、新增 WorkflowOfflinePreparationTests.Receipts.cs，当前 source8BB33BF0 与候选 source3855A93E 不同。全部在途修改保留，HEAD4fac2dd、索引空、未提交/推送/安装。候选567874b9仅为保留的旧捕获产物；原 git-publish.log 失败不改判，后续只读核验见 delivery-2026-11-15/source-drift.json。需先确认统一源码范围，不得只增加计数或改签旧回执；此前223计数和准备交付措辞由本段更新，历史记录保留。

## 1. 当前入口与结论边界（2026-11-15）

### 最新：源码交付与Release AI候选重建，正式安装未更新

用户要求“提交推送 更新exe”。源码交付限源码、测试、构建脚本和文档，共223个待提交文件；不包含本地AI配置、密钥、IDE状态或out产物。Git main起点与远端核对均为 `4fac2ddfa05bc18a3db810d00c810a64540d2843`；交付按**开发检查点**记录，不能称完整验收通过。具体提交与推送结果以Git和本次 `git-commit.json` / `git-push.json` 为准，未完成前不预先宣称远端成功。

待交付内容检查包括路径/类型/大小白名单、JSON解析、差异空白检查和凭据模式扫描。首次宽泛 `sk-...` 匹配位于历史task证据路径内部，人工核对前后文本确认不是密钥；改用凭据前缀边界后0项命中。没有读取机器AI设置或密钥库；模式扫描不等于任意隐私内容不存在的数学证明。

#### EXE构建结果与安装边界

复用已核验的5a899捕获（source `3855A93E5CDF4FE9D822B468F3C4DCFDC45139C6778F2687D468556F2298C5FC`），原6-job独立离线依赖准备后，以VS2022/MSVC14.44.35207执行原打包器。新原生265步构建及托管发布成功，日志未发现编译警告/错误；574.50秒含校验，不作为性能数据。生成原默认Editor、4种Player和AI Editor共6产品、652文件，清单/哈希/同捕获绑定及依赖检查通过；不是全69工程或完整测试矩阵验收。

新AI候选完整目录：`F:\NcmaEngineRelease\source\out\package\m2-7\Release\567874b99c35469c952b36408b8dfb05\editor-ai`。Release / Windows x64 / framework-dependent，AI启用、扩展关闭，151文件。

| 文件 | SHA256 |
| --- | --- |
| `NcmaEngine.exe` | `F60240AE65E0C8D0D1F9671DB369EEBC85FFF3F059D194AA4C291FEF1E938182` |
| `NcmaEngine.dll` | `7ABFB62CEEDD5A16FB693D94B48EA4A0C312A87AEEC30CC89B3552D77EF71C39` |
| `deployment-manifest.json` | `D763BEF61A5C784DABC18BE6930F51EDF03C353CC16BD3BD4DAC9CEF4830EAA6` |

**正式 `out/bin/NcmaEngine.exe` 未替换。** 用户延期两类环境门禁没有豁免原部署流程中的实际apphost验证和安装烟测。本次不重新尝试已知受阻门禁、不启动候选、不调用部署、不通过dotnet替代/日志回退/改ACL或策略制造通过。正式151文件、EXE379AA113及journalF5BDC19D保持原值；候选不可标已安装、已运行或已验收。正式安装须恢复并通过原门禁后另行完成。

包清单保留构建时的提交前Git元数据，以捕获SHA绑定真实编译输入；不在后续提交后重写清单冒充其Git提交ID。

#### 两个包索引变化与后续验证

先独立备份全部8索引，再由原打包器更新以下两项，原流程生成的旧索引备份也已核验；其余6索引和25固定运行/捕获支持目录均未变。这是两个独立写入，不是跨文件原子事务。

| 索引 | 更新后SHA256 |
| --- | --- |
| `out/verification/m2-7/Release/packages.json` | `26E8E4C19DDC53811B47CC57C5AE4114FA01AADEBB48D27D17E5A1F97BC456D5` |
| `out/verification/m8-4/Release/packages.json` | `EDE9898F76068A33D04489A2FB2C9708956BC5BCD32DD440DA576341D72DD37E` |

当前受保护输入检查点：`out/verification/m8-7/delivery-2026-11-15/protected-inputs.json`，SHA `F5E405F446A10EF52C5E89C3370D552AE1F05F36D12A4D2FFBA568C45E909C5B`。固定运行/其他编译回执仍63ed/Release，两个Release包索引为新5a899；**当前是明确混合的准备状态，不可跑旧完整绑定并当作新源验证，也不可继续使用原03EA全索引检查而不处理这两项变化。** 原完整冻结和失败记录保留，不修改旧回执或宣称历史所有路径仍原样。

本次证据目录 `out/verification/m8-7/delivery-2026-11-15/` 包含baseline、独立索引备份、offline preparation、toolchain、package.log、products、package-result和candidate记录。`candidate.json` SHA `5051EEC8D42C051D497E616897C6E5CB50EDA446FE710CF5F3CBB4D7B1A61ABB`。新测试0；先前动画定向20/20保留，M8.7/M8整体、人工/模型/目标机/长稳门禁不因此完成。

### 最新用户决定：两类环境门禁延期（2026-11-15）

用户明确指示：**“延期这两类环境门禁”**。这项决定只改变以下两类门禁的执行时间，替代之前“等待环境后才能继续”的安排，以及自动“F主体结束后立即运行原PS5.1组”的顺序；不改变原测试结论、测试实现、发布或安全校验。

| 延期类别 | 范围 | 当前安排 |
| --- | --- | --- |
| 实际apphost执行环境 | 当前Low标签/外部用户日志访问拒绝相关的实际EXE宿主环境门禁；已有每配置10个受影响入口的证据 | `deferred(user)`，不反复重试已知环境失败；原失败日志保留 |
| Windows PowerShell 5.1脚本环境 | 原24项/配置专项及其间接PS5.1依赖（含SDK链），当前有效Restricted | `deferred(user)`，不立即追加原宿主测试，不以PS7/内联脚本/Bypass替代 |

未指定恢复日期。后续用户明确恢复时，先确认受支持环境、当前源码捕获及匹配运行输入/回执，再执行原命令、宿主、超时和断言。延期期间不重复询问同一环境前提，不因此自动更换唯一工作区或修改ACL/MIC/执行策略。

#### 保留的证据与非延期范围

- 原360项账本仍是 **290通过、24失败、46未执行**。本次没有执行新测试；不把旧apphost失败或PS5.1启动失败改写为通过、跳过或未执行。
- 动画契约修复仅证明5a899捕获的双配置各20/20；旧固定运行输入/索引仍63ed/Release，不能把不同源码的结果合并成完整通过。
- 这两类是**环境门禁范围**，不是整组禁用测试的许可。`NcmaRenderingTests`、`NcmaPlayerTests` 等混合入口中的非环境用例仍须验证；后续执行前需列明精确延期步骤、保留用例及覆盖对应关系，不能简单排除整条入口后宣称其余全覆盖。
- 新功能失败、动画回归、权限与安全合同、资产闭包、ABI/格式、代码生成等不因环境延期而免测。人工客户端/真实模型、E5-D目标机、性能与长稳仍按各自原状态处理，不能扩大本次延期范围。
- 不删除原CTest条目或缩小原清单；未来报告分别记执行结果与用户延期范围。延期不是正式安装许可，也不自动使M8.7/M8完成或授权进入M9。

#### 下一步顺序

1. 核对M8.7剩余**非延期**范围，特别是受影响混合入口中的功能覆盖；保存显式分类，未知依赖不得默认归入环境延期。
2. 按当前源码补齐同一捕获的构建、运行依赖及原回执；恢复必要的原始完整清单，不复用旧63ed验收或覆盖已有固定参考输出。
3. 执行可运行范围的Debug/Release回归，失败仍定位修复；阶段报告保留两类用户延期和其他独立债务，不能标“完整验收通过”。
4. 待用户明确恢复这两类门禁后，在受支持环境按原门禁补验；正式部署和Git交付仍是独立授权动作。

本次仅更新协作记录、README和路线图/阶段计划，没有产品或测试源码变化、构建、测试、部署或Git发布。下文环境诊断及失败记录属于历史事实，继续保留。

### 历史：只读环境诊断完成，宿主门禁等待外部前提

动画修复后的5a899捕获/source3855A93E未变。此次不启动引擎、测试或模型，不修改产品、测试、生产脚本、安全设置或部署；只读查询并将结果写入新的证据目录。正式151文件、EXE379AA113、journalF5BDC19D及当前捕获已重新核对。

#### 已观察事实与归因限制

| 对象 | 本次只读结果 |
| --- | --- |
| `F:\` | icacls输出未显示Low完整性标签或拒绝项 |
| `F:\NcmaEngineRelease` | **显式** `Low Mandatory Level:(OI)(CI)(NW)`；`Everyone:(CI)(DENY)(S,DC)` |
| 唯一工作区 `F:\NcmaEngineRelease\source` | 继承上述Low和目录拒绝项 |
| 正式EXE、63ed失败Release候选EXE | 均继承Low标签；字节不变 |
| `%LOCALAPPDATA%\Temp\NcmaEngine\EditorRuns` | 未显示完整性标签；其DACL不继承F盘根目录拒绝项 |
| 本次诊断调用者 | whoami观察High完整性组；**不是失败apphost令牌测量** |
| Windows PowerShell 5.1 | 5.1.19041.6456；有效策略Restricted，MachinePolicy/UserPolicy/Process/CurrentUser/LocalMachine均Undefined，进程策略变量null |

Windows MIC规定未标记对象按Medium处理；普通进程创建完整性取用户与可执行文件两者较低值，Low进程不能向更高完整性对象写入。这与此前实际apphost外部日志目录拒绝相符，且解释了为何单看管理员身份不够。[Microsoft MIC文档](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control)。**这是当前标签和系统规则支持的环境机制判断；本次未启动失败apphost、未重新测量其令牌，不能作为新启动验证、设置者归因或所有失败的统一根因证明。** 根目录的DACL拒绝项另行记录，不能据此声称它直接导致了C盘外部日志目录创建失败。

PS5.1在Windows客户端所有作用域均Undefined时，有效策略仍为Restricted；该策略允许单条命令查询但不允许运行脚本。[Microsoft PowerShell 5.1文档](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_execution_policies?view=powershell-5.1)。本次只运行GetExecutionPolicy查询，**没有执行.ps1测试case**，也没有设置或清除进程策略。此前两条原入口的策略拒绝及46条未执行保持原记录。

2026-10-28最初独立目录“无Low/拒绝项”的历史观察不被改写；但**当前**该环境前提已不成立。标签/ACE设置者和发生时间未知，不归咎防护软件或某工具。仅改子目录名称、重新构建或从高权限调用者启动不能据此视为已排除继承标签影响。

#### 证据与恢复条件

证据：`out/verification/m8-7/f-environment-audit-first/Inspect.ps1`、`acl-0.json`至`acl-5.json`、`diagnostic-caller-groups.json`、`policy.json`、`result.json`。结果SHA256：`066DBB1EA0B6CC5B0CB3F976F0B8E00552B49585351F63066FA22EED9B28ED98`。新增测试0，未启动引擎，未改ACL/MIC/执行策略/令牌，未启用Bypass、PS7替代、日志回退、强制清理、部署或Git发布。

剩余宿主门禁在这里停止，等待用户管理的受支持执行环境或明确延期方向。不能自行删拒绝项、重置标签、换日志根目录、把EXE换成dotnet启动来制造通过；唯一工作区仍是 `F:\NcmaEngineRelease\source`，未经用户确认不自动迁移。

恢复时先核对环境、正式安装/历史证据及当前源码；准备同一新捕获匹配的全量产物、运行输入和原回执后执行原apphost与PS5.1门禁。延期若获确认，只登记未执行和范围变化，不算通过。动画双配置20/20是已完成的定向结果；原360项290/24/46账本、M8.7/M8未验收及人工/性能/长稳债务均保留。

### 最新：动画外部文件冲突测试已修复，双配置定向通过（2026-11-15）

本次只修改 `managed/Ncma.Editor.Services.Tests/Program.cs` 和 `AiAnimationWorkflowTests.Joint.cs`；产品、权限、文件冲突校验及生产脚本不改。正式EXE未更新，没有Git发布、真实模型调用、密钥访问或安全策略变更。

#### 先观测，再修正断言

保留原 `Reject(...)` 和0回执断言，仅在返回路径打印诊断，并增加仅测试使用的 `--ai-animation-workflow-tests` 分组入口，仍由原CaseHarness跑完所有case再汇总，原全量登记不删除。首次诊断图Debug编译0警告/错误、79.66秒；产物收集辅助脚本误以项目名寻找编辑器DLL，实际入口是 `NcmaEngine.dll`，因此在测试前失败。原脚本、捕获6996b9ca和 `compile-result.json` 保留；新BuildSecond脚本只改产物名及证据目录，没有放宽验证。

第二次诊断捕获 `a6499fe8db3c4dfa99fde2eba930bd53` / source `14E8C8171F39440915E632691BB85988D243DBB507BEF88F022132CF9DA721D6`，Debug编译0/0；原19例全部到达，18通过、1失败，11.23秒。实际记录明确：

- `Status=conflict`、`Code=graph_file_conflict`、`Changed=false`、`Replayed=false`，当前与执行Revision均0。
- 原领域服务记录一条相同ProposalId/Fingerprint的失败回执，`RecoveryRequired=false`。
- 文件与场景字节不变，Undo/Redo均0，Play不存在；没有成功提交或越权写入的证据。

原因是原AnimationGraphCommands抛出 `EditCommandRejectedException`，原EditSession把它转成结构化冲突结果，原DomainProposals记录已经尝试执行的失败回执。取消/撤权则在命令执行前被拒，仍应抛异常且无回执。旧夹具把不同层级的拒绝混为同一断言。

#### 最终修复与验证范围

外部文件冲突分支现在逐项核对结果、UUID/会话、Revision、完整回执；确认票被消耗，重复确认必须以 `proposal_confirmation_required` 拒绝且不能增加回执。另新增真实AiAuthoringWorkspace UI确认路径用例，得到相同冲突结果。取消/撤权原异常和0回执要求不变；原文件/场景字节、固定资源CPU播放、Undo/Redo验证均保留，并加强为完整EditState前后一致，取消清理后再检查一次。

新捕获 `5a899c55caeb42fab7547e5fce6db285`，5095输入；source `3855A93E5CDF4FE9D822B468F3C4DCFDC45139C6778F2687D468556F2298C5FC`，manifest `F836FB09564FA6C2569ACEB7DE27215403B946CE5F05CF6CE01D0486B69B4C90`。与63ed逐文件对比只有上述两个测试文件变化。

| 本次验证 | Debug | Release |
| --- | --- | --- |
| 原AI Services.Tests及实际Editor依赖图编译 | 0警告/0错误，79.51秒 | 0警告/0错误，79.90秒 |
| 动画工作流完整分组 | 20/20通过，11.48秒 | 20/20通过，11.66秒 |

两配置先走原独立离线依赖准备，再从捕获副本 `--no-restore --no-incremental` 编译。每配置的Gameplay/MCP依赖从同一编译图复制到新私有运行目录，未替换固定目录；原180秒上限不变。所有19个既有动画工作流用例加1个新UI用例均执行，既有取消/撤权、资源审批、CPU速度、Undo/Redo和原图校验负例均通过。没有执行DX11动画图像门禁、全69工程、全可选矩阵或完整F，因此不据此宣称这些通过。

CTest对通过项的JUnit标准输出默认截断到1024字节；完整逐例证据取自两配置 `Testing/Temporary/LastTest.log`，最终报告固定其SHA并逐项核对20条PASS及两种外部冲突记录，不仅依靠CTest的1/1入口汇总。

证据路径相对 `out/verification/m8-7/`：

| 记录 | SHA256 |
| --- | --- |
| `f-animation-diagnostic-second/test-result.json`（原失败） | `E363314201E1056C95B7158C7B084539D30E13901EC9829EE12C3D9D1A1882B9` |
| `f-animation-repair-second/compile-result.json` | `D8EBF14EF147F33C8C958FCFEDEFA36904ECDF8B78A1727C408868CCB32B6A56` |
| `f-animation-repair-second/test-result.json` | `2766D7D45CC138678C4E0F6B65B1EA309CDBC15323793FF477DA641388F34306` |
| `f-animation-repair-second/final.json` | `122E26E2914548EF092781D2BFE278F12EA9E67B0A1FB852D2E7998192AC77DD` |

最终核对历史Release冻结的10586独立文件、813棵产物树均未变；活动23目录及2捕获支持目录、8索引、正式151文件与部署日志不变，本次路径/名称范围辅助进程0。这里指历史产物与证据完整，**不是旧live-source stamp仍匹配当前代码**。正式EXE仍379AA113，journal仍F5BDC19D；固定/共享输入仍63ed/Release，没有新安装权限或全量验收结论。

**后续：** 动画夹具问题已在新捕获定向关闭；10项实际apphost访问拒绝和PS5.1原宿主策略阻断仍是独立未解决项。历史360项290通过/24失败/46未执行保持原结论，不把其中两条旧动画失败改判。继续门禁前需要受支持环境，以及新源码匹配的全量构建/运行输入/原回执；禁止复用旧63ed回执、策略绕过或盲目覆盖两配置KernelReferenceCapture。M8.7/M8及人工、模型、性能、长稳验收仍未通过。

### 历史：双配置F主体完成；PS5.1原宿主被策略阻断，整体验收未通过

本轮延续同一63ed捕获、5095输入、source `EB1F43A1BFC11609855FB69A785C1649569A6C9DC8BB0B09A2953B6294ED79FB`、manifest `CAA33938EADCA22D8A199038A6A4E6E2D1F196F0FFF51DEE589CE727C71DE0CA`。产品、测试及生产脚本源码没有修改；没有新构建、部署、Git发布、真实模型调用或用户密钥访问。

#### Release受检切换与冻结

仅切换规范 `out/managed`（37文件）、捕获副本 `source/out/bin`（145文件）和 `source/out/managed`（37文件）。先独立备份219旧文件、暂存219新文件并备份8索引；检查精确路径、物理身份、重解析点、完整集合与哈希及219次独占读取，再只读演练和成对移动。三处旧原目录另保留于 `f-release-switch-first/retired/`，不是删除或跨目录原子事务。12条事件、双份旧文件及新文件全部核验；8索引不变，正式 `out/bin` 不在切换范围内。

首次准备脚本筛选共享目录的PowerShell表达式触发ParameterBindingException，复制/移动均0。原 `Prepare.ps1`（SHA `210C97CAAE4FB83738137444E2055C8105BD9F0EBAAF943EE3E59CD1DE4424F0`）和 `prepare-result.json`（SHA `B1D947D2A78A4C1AB869F5C2D884730866FD22623D9137B164006602EC241F01`）保留；`PrepareSecond.ps1` 只改该表达式与新证据文件名，校验规则不变。自动审批曾在启动前拒绝隐式演练请求；只读核实 `[switch]Execute` 与false分支提前return后，显式 `-Execute:$false` 请求获准并通过，随后独立申请实际 `-Execute` 切换。没有另换移动手段绕过拒绝。

原Debug冻结的10,553独立文件均未变，805棵树仅这三处配置变化，原内容在保留位置和独立备份中完整。新Release冻结10,586独立文件、813棵树/39,402树内文件，包含完整Debug证据和新保留文件；运行器精确绑定。原双配置清单仍180项，命令/宿主/超时/断言不改。**当前权威活动输入为 `f-release-switch-first/active-inputs.json`，规范共享与捕获内支持均已是Release；不要再调用默认要求Debug的CheckCurrent/CheckSupport。**

#### 测试总账

| 原清单范围 | 配置 | 通过 | 失败 | 未执行 |
| --- | --- | ---: | ---: | ---: |
| F主体156项 | Debug | 145 | 11 | 0 |
| F主体156项 | Release | 145 | 11 | 0 |
| 后置原PS5.1 24项 | Debug | 0 | 1（入口启动受阻） | 23 |
| 后置原PS5.1 24项 | Release | 0 | 1（入口启动受阻） | 23 |
| 两配置原360项合计 | — | 290 | 24 | 46 |

Release原CodeGen构建/两产品隔离发布2项均通过，144.73秒；其余154项143通过/11失败，660.63秒。运行器全程1016.81秒包含完整哈希审计，不是性能验收。全部156项有终态且无主入口not-run；早退legacy harness不能据此证明内部全部case到达。Editor Services两配置均430=429通过/1失败，Player均37=36通过/1失败。

Release失败名单与Debug逐项相同：10项实际apphost日志目录访问被拒（包括独立固定哈希的SceneUiHosts子日志），1项动画external-file夹具期待异常而原命令返回conflict并记录回执的问题。当前规范工作区与Release捕获EXE仍观察到继承Low标签，目录拒绝项仍存在；未修改ACL/MIC/防护，不把所有失败或标签设置者归因于同一未知原因。动画失败未修复、实际返回值仍待单独记录，不因此声称发生越权写入；原嵌套injected-assertion负控制不另算失败。详细原失败分析见下节Debug记录。

#### 主体之后的原Windows PowerShell 5.1检查

严格在两配置主体终态及独立报告落盘后执行，不要求主测试先全通过。将原 `NcmaWorkflowTests.InventoryContracts` 的两行CTest定义逐字复制到新私有日志目录，重新对比原命令/属性，保留实际 `C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -NoLogo -NoProfile -File .../Test-M8_6Inventory.ps1` 和30秒超时；不覆盖原native Testing日志。这个入口不消费共享运行产物，因此两配置均可在当前共享Release下做原宿主检查，不能据此混跑依赖Debug共享支持的其他入口。

宿主版本5.1.19041.6456。两配置原入口均返回CTest exit8，原日志明确 `running scripts is disabled on this system` / `UnauthorizedAccess`；**case体0启动**，不是10条断言失败或测试通过。未设置或清除策略，未用Bypass，未换PS7。每配置其余23项记 `not-executed(environment-host-policy)`，合计46项没有尝试；包括间接依赖PS5.1的SDK链。要继续这些门禁，需要用户管理的受支持脚本执行环境，不能通过更换宿主或放宽规则制造通过。

#### 精确检查点与后续

以下路径相对 `out/verification/m8-7/`：

| 记录 | SHA256 |
| --- | --- |
| `f-release-switch-first/plan.json` | `DD424087FA5CE66E675E3F4B3F4ECE47864D781DEB77F65D124F7F40261ACF61` |
| `f-release-switch-first/activate-result.json` | `290637A9EF4898C199A47F8D4FF796BD1879BB3C4F91409952AE4FF4171F714A` |
| `f-release-switch-first/active-inputs.json` | `03EAED86107139E22CA271D2B22FE593363EE318FED988A46FBED2EB9D53BBBB` |
| `f-release-first/freeze.json` | `B2042217DFFFF7B6765C405F89C1A29DC1AD79FED8971AFFCF5F92E71C3DD364` |
| `f-release-first/audit.json` | `11CDE8E4DB1B1894D4D0DC990159EBC31A5ABF9F0F0BA2D7ADB0BEDB3219C338` |
| `f-release-first/result.json` | `9AC3B481B034966D04652301866AD73A0BC9990F575904C0B7DD5EBA8F02D64A` |
| `f-release-first/after-all-audit.json` | `E5E291F363165BBE8963D912340F6A3F18B576869EE7318D604845DD4B148DB7` |
| `f-release-first/inspection.json` | `F36990DC20DE157553D665E265F0C71E617DDF9DBE4AF53B673BD3F41402048A` |
| `f-post-ps51-first/result.json` | `73ADB26165C2EE34B0328CAB01CED3255FDA638289E51AB8B09CAD33733C0D89` |
| `f-post-ps51-first/after-audit.json` | `0EFD5ABDF0B71079EA25A15FDE86B5781D4AB886F6CDEE7F712FA1A9B791CD46` |

Release测试前/CodeGen后/全部测试后/独立收尾、PS5.1探测前后完整冻结均未变；旧Debug报告与全部证据保留。正式151文件、EXE `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`、journal `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1` 未变。原Release两个CTest均退出，收尾时受检名称/时间/路径范围内残留辅助进程0，不作为全系统孤儿进程证明。本次受控切换只改变测试运行配置，未更新正式EXE。

**下一步分开处理：** 先单独记录动画冲突的实际CapabilityResult与失败回执，依原契约修复夹具，保留文件字节、播放、Undo/历史、权限和二次确认断言；任何源码改动均需新捕获/匹配验证，不重标本轮结果。实际apphost与PS5.1需要受支持执行环境，原失败不改判。两配置KernelReferenceCapture固定输出现均已生成，禁止盲目重跑覆盖。M8.7/M8仍未验收，不进入下一个正式里程碑；人工、模型预算、长稳、代表性大世界/E5-D性能门禁保持独立未验收。

以下为历史记录。

### 最新：Debug F 主体终态，145通过／11失败，阶段未验收

本轮未修改产品、测试或生产脚本源码，继续使用63ed捕获、5095输入、source `EB1F43A1BFC11609855FB69A785C1649569A6C9DC8BB0B09A2953B6294ED79FB`、manifest `CAA33938EADCA22D8A199038A6A4E6E2D1F196F0FFF51DEE589CE727C71DE0CA`。先补齐上一轮文档，再建立当前完整运行绑定与新冻结；所有测试由捕获副本的原入口执行，未使用整条Build、替换宿主、提高超时或联网还原。

#### 输入与执行结果

- 双配置原 `ctest -N` / JSON均180项，命令及属性逐项相等，分组保持2项CodeGen＋154项其余F＋24项后置Windows PowerShell 5.1。当前共享配置仍Debug，未切换Release。
- 冻结覆盖10,553独立文件、805棵目录树（38,923树内文件），包含当前输入、精确保留的历史输入、新旧备份、选定工具和独立Python；不声称完整已安装SDK hermetic。新Debug运行器路径及SHA绑定到冻结，不再是“准备但未绑定”。
- 原CodeGen构建合同和Editor/Player两种隔离发布已真正执行，**2/2通过，143.20秒**。预期编译拒绝仍按原断言检查，不把其中非零编译退出误算失败；两种产品发布没有启动应用。CodeGen内部 `formalBefore/After` 指捕获内运行支持EXE，正式安装另由整体冻结核验。

| Debug范围 | 已执行 | 通过 | 失败 | 未执行 |
| --- | ---: | ---: | ---: | ---: |
| 原CodeGen入口 | 2 | 2 | 0 | 0 |
| 其余F主体 | 154 | 143 | 11 | 0 |
| F主体合计 | 156 | 145 | 11 | 0 |
| 原后置Windows PowerShell 5.1 | 0 | 0 | 0 | 24 |

其余F主体原CTest耗时760.39秒；运行器全程1113.30秒，含完整哈希审计，均不是性能验收数据。CTest未遇首个失败即停，全部156项有终态；原早退harness不能因此推断内部每个case均执行。Editor Services明确汇总430=429通过/1失败；Player37=36通过/1失败。Release180项本轮均未执行。

#### 失败逐项归因边界

10个实际apphost相关失败入口：`NcmaRenderingTests`、`.SceneUiHosts`、`.SceneUiPublicationProducts`、`.SceneUiJointProducts`、`.ShadowJointProducts`、`.GraphicsRecoveryJoint`、`.GraphicsRecoveryJointOff`、`.GraphicsRecoveryJointLow`、`.GraphicsRecoveryJointMedium`，以及 `NcmaPlayerTests` 的Package case。前缀省略项均为 `NcmaRenderingTests`。均有 `editor_user_data_access_denied / UnauthorizedAccessException / 0x80070005` 原证据；SceneUiHosts的诊断来自另行固定哈希的 `scene-ui-host-Debug/7489d2c1c77a4336a2f7fc5140a9a963/relocated-editor/editor-apphost.log`，不是凭相似栈猜测。

只读icacls显示：**当前规范工作区及失败捕获EXE带继承Low标签，工作区有DENY(S,DC)**。本轮没有识别标签设置者或读取失败瞬间的进程令牌，不能将这一观察扩大为所有失败的唯一根因。正式安装标签也单独留证；没有ACL/MIC、执行策略或防护修改，没有dotnet替代apphost、日志目录回退、自动重试。原历史“独立目录无Low”的观察不代表当前状态。

独立的第11项是 `NcmaEditorServiceTests` 中 **M8.7-C2 animation speed binding external-file keeps original playback**，失败于 `AiAnimationWorkflowTests.Joint.cs:136` 的 `Reject()`。静态核对原链路：`AnimationGraphCommands.Prepare` 对外部字节变化抛 `graph_file_conflict`，原 `EditSession.Invoke` 将其转成 `CapabilityResult(Status=conflict, Changed=false)`，`EditorDomainProposals.Confirm` 随后记录尝试回执；新夹具却要求直接抛异常且0回执。**失败夹具未记录实际返回值，暂不把静态分析当动态通过证明，也没有证据表明发生越权覆盖。** 下一轮须记录精确返回值/回执，保留文件字节、播放、Undo/历史、权限和二次确认断言，按原契约修复并重新捕获测试；不改生产拒绝语义来迎合夹具。

日志中的 `M8.6 fixed workflow-contracts-v1 ... injected assertion` 是原汇总harness负控制，所属外层case通过，不是额外失败；不得通过搜索所有FAIL字样重算分母。旧失败记录、完整JUnit与stdout/stderr不改判。

#### 证据与下一步

| 记录 | SHA256 |
| --- | --- |
| `f-inputs-first/readiness.json` | `6475302C39DE6E249557510CD916D26F1CCC1D43EE07D3359E61E3302655E647` |
| `f-inputs-first/freeze.json` | `6E4446082976FB5AFCF2F13B5626010FD7059177942C38793A6E3FF4E807BB18` |
| `f-inputs-first/audit.json` | `99DD4C344648223E25E5B76C64F630B3D75C9823410D8E311E93FB4A0F0C8C14` |
| `f-debug-first/Run.ps1` | `D76DC24F7F29A77A1CFC79382305EB81FFAD9BA7CAB69E01FFB79432270B52F4` |
| `f-debug-first/result.json` | `2D98AF64242FBE1044C126DC7033ED12F990C2E7839DCBE6F8D31C79B35A2EC7` |
| `f-debug-first/after-all-audit.json` | `914417123C7DBC783F832845B984F2800FEFB0C1A2815BA9A87EA7E8F202E239` |
| `f-debug-first/inspection.json` | `B2F5596DC89339DAE5630F9B8694AF449F91D4EC907F6313E61668B5742E4352` |

以上路径均相对 `out/verification/m8-7/`。测试前、CodeGen后、全部测试后及独立收尾检查均确认完整冻结未变。正式151文件、EXE `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`、journal `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1` 未变；本轮没有部署、Git提交推送、真实模型调用或密钥访问。原两个CTest进程均退出，受检时间/路径/已知名称范围内残留辅助进程0，不扩大为全系统孤儿进程证明。

**下一步：** 按已授权流程备份核验并切换三处Release共享运行支持（规范out/managed、捕获内out/bin与out/managed），重新绑定Release运行器/冻结，再执行同源156项F主体。两配置主体终态报告后执行原24项Windows PowerShell 5.1/配置，不要求主体先全通过；环境不支持则明确记录阻断，不Bypass、不换PS7。动画失败和宿主环境分别修复，任何源码改动需新捕获/回执/相应门禁，不能重标63ed证据。当前Debug固定KernelReferenceCapture输出已经生成，**禁止直接重跑覆盖旧输出**。

本轮不是M8.7或M8整体验收通过。实际人工界面、模型预算、代表性大世界/E5-D性能与长稳门禁仍独立未验收。上一条 `e-fixed-input-cutover-first/post-documentation.json` 固定的是此前文档版本，不作为本次新增测试记录的文档哈希证明。

以下为历史记录。

### 最新补录：固定23目录切换与原回执已完成

本节补录11月14日已完成的工作。上次两次文档写入请求均在进程启动前因审批超时/连接故障未执行；11月15日用户要求继续，先核对原最终记录与活动检查点哈希，再补录。没有重新执行切换或重标旧证据。

仍使用 `63edb4c3d5934fd3986751844f8edafc`、5095输入、source `EB1F43A1BFC11609855FB69A785C1649569A6C9DC8BB0B09A2953B6294ED79FB`、manifest `CAA33938EADCA22D8A199038A6A4E6E2D1F196F0FFF51DEE589CE727C71DE0CA`；产品/测试/生产脚本源码未改。证据根 `out/verification/m8-7/e-fixed-input-cutover-first/`。

- 精确23目录/1357旧文件独立备份、1357新文件暂存、8索引完整备份；进程检查与1357次独占读取、物理目录身份/重解析点/完整集合/字节检查及只读演练通过。占用检查是时点观察，不保证消除并发竞争。
- 原生PowerShell精确成对移动：先保留原目录到 `retired/`，再移入暂存；23项完成、92条事件留证，旧原目录和独立备份各1357文件完整。无删除、Force、合并覆盖、自动重试，不是跨23目录原子事务。计划SHA `009A32A04BD683943FE9A5D47E22BBDDADD0395134960B88BB62A5E074FB9590`；切换结果SHA `E1CB0C83E597498E37F63EB863DAD50DDEC6400439C7E2334576DADB5C4C8523`。
- 原AI入口每配置完整AI/合同图 `--no-restore --no-incremental`，原扩展入口按Editor→Player顺序及原 `--no-restore`。共12图编译0警告/错误，产物与激活输入字节一致；Debug/Release调用全程224.71/224.30秒（含核验，不是性能结果）。四组离线准备上下文重新打开核验，没有功能测试或整条Build捷径。

| 配置 | AI原回执代次 | AI索引SHA256 | 扩展原回执代次 |
| --- | --- | --- | --- |
| Debug | `dd742197c0d8448c965e551d9766db05` | `5E653D692717048B98D3FC929426D05B948181F2245C81D5C5BC31E0CD3CB902` | `eae7c15ccfcb4ef895b825490f3235b5` |
| Release | `1216d37d92b6438d86acaa92eb8727c0` | `D1A5AC0E5CF9F9A53003ACE10CF4338A1D5D7C39B86FB14A82AACA0D8DDC2095` | `352722760f76460fb81a28af026f59c8` |

AI使用原AI上下文，扩展使用原配对上下文，不要求不同输出根DLL相等。两AI索引由原File.Replace更新，原previous-receipt和独立备份均与旧字节一致；六包索引不变。扩展在 `out/verification/m8-5/managed-inputs/<代次>/receipt.json` 新建原回执，不改旧回执/索引，不手写或重标编译回执。

权威活动输入 `active-inputs.json` SHA `4982CE6DE60F7D4EB390F2DAC89F643812AC493C2C0BC4F2F72AD30D31936355`；最终审计 `final-audit.json` SHA `CEE70ACE5533429DDC77AFD9D5F256371BD8C402C9CE3EAAED8D93E7CD25E251`。**固定输入已是63ed，不再是b69；Shared仍Debug。** 旧冻结405树中382原位未变、23旧树在精确保留位置和备份中完整；5171独立文件中5161未变、8项已知索引变化、1项已知签名Git变化、1项旧AI测试DLL随目录搬移且两份旧副本与原哈希相等。当前产物另按新检查点核验，不称旧冻结整体未变。旧CheckPrepared/CheckHistorical默认检查b69路径，后续须使用新活动检查点及精确变动映射，不能直接调用。

当前原生/148生成目录/产品/CodeGen/运行支持、独立Python、双份旧文件及新原回执已核验；正式151文件、EXE `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`、journal `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1` 未变。没有功能测试、实际apphost、模型、worker、SDK链、PS5.1、部署或Git写入；177新case＋3加强case体未运行，executionReady/stageAccepted仍false。

接下来接通当前完整启动绑定和CodeGen离线环境、建立新完整冻结，再每配置156项F主体；Release前受检切换共享运行配置。主体终态报告后原24项Windows PowerShell 5.1/配置，不等主体全通过。原KernelReferenceCapture输出不盲目覆盖；人工、模型预算、长稳与E5-D代表性内容保持独立未验收。

以下为历史记录。

### 最新：CodeGen 离线输入、共享运行支持及只读切换计划

本轮继续使用同一 `63edb4c3d5934fd3986751844f8edafc`、5095项输入、source `EB1F43A1BFC11609855FB69A785C1649569A6C9DC8BB0B09A2953B6294ED79FB`、manifest `CAA33938EADCA22D8A199038A6A4E6E2D1F196F0FFF51DEE589CE727C71DE0CA`。未改产品、测试或生产脚本源码；新增准备证据在 `out/verification/m8-7/e-support-inputs-first/`，不是新产品入口。

- **CodeGen：** 原五种项目形态/配置共10项显式离线Restore完成；仅使用17份已固定本地NuGet归档和私有缓存，没有联网还原或隐式构建。复核128项实际 `project.assets.json` 项目绑定、唯一源/配置/缓存路径，以及34份还原归档的SHA256和SHA512元数据。原两个CTest命令、属性和harness字节绑定于 `codegen/test-environment-bindings.json`，SHA `3E648A19B387DFE7DDE0AC3AD4F07F3C0931BABF6EFD38A61639A56F6F7FFC2D`。**原16条构建＋2种发布测试没有执行**，环境尚未接入统一测试启动器；不把依赖准备算测试通过。
- **共享支持：** Debug/Release分别新建37文件完整快照；仅新捕获副本的 `source/out/bin` 放入同源默认Debug候选145文件、`source/out/managed` 放入37文件，合计复制256文件。均新建、不覆盖旧目录；两个配置的候选43份渲染资产与正式资产字节相等。正式 `out/bin` 和规范固定共享目录未改，Release捕获内共享支持尚未切换。
- **文件就绪：** 58个不同直接托管harness的原deps/runtimeconfig声明逐项核对，缺失声明资产0、未解析RID目标0，所需框架主/次版本存在；4个原默认/AI候选清单及捕获上下文通过原校验。没有加载宿主、执行apphost或证明完整运行时闭包；旧编译回执尚未匹配，明确不将其标为已验证。

只读切换清单 `cutover-plan.json`，SHA `4F523177FE57ADA3B9DEF45255FB9812EA2FDE8BD800871820A97CA9D929049B`：

| 范围 | 数量 | 本轮结论 |
| --- | --- | --- |
| 可选AI/合同/扩展/组合测试目录 | 12 | 精确原目标与当前完整产物绑定 |
| 规范默认Editor/Player/工具测试目录 | 10 | 仅五类项目×双配置的明确白名单 |
| 固定共享运行目录 | 1 | 首次计划Debug；Release快照独立保留 |
| 上述新旧文件 | 1357＋1357 | 各自完整集合/字节已核对，未复制到固定目标 |
| 索引 | 8 | 六当前包索引保持；两旧编译索引只能由原生产入口更新 |
| 原回执生成入口绑定 | 4 | 双配置AI及扩展各一组，仅核验依赖/捕获/脚本身份，未执行 |

拟用 `out/verification/m8-7/e-fixed-input-cutover-first/` 保存独立备份、暂存、保留的原目录；该目录本轮仍不存在，清单不是备份完成或移动许可令牌。执行前必须重新检查精确路径/类型/身份/重解析点、源/旧集合、完整备份与当前进程使用情况，不能仅凭清单移动。遇锁定或身份/字节变化停止，不强删、不扩大目标、不重试覆盖。

**原回执顺序明确：** 受检固定输入切换之后，再执行原 `Prepare-AiVerification.ps1` / `Prepare-ExtensionVerification.ps1`。AI使用 `e-scene-candidate-third` 的同上下文离线准备，扩展使用 `pairs-third/player-<配置>` 的Editor→Player配对上下文；两者同捕获但不是同一个输出根。重编译若改变预期字节，停止并重新审查计划，不手写回执、不重标旧索引、不把跨输出根DLL相等当作前提。上述目录/索引切换不是跨文件原子事务，正式部署不在其范围内。

本轮首次 `CutoverPlan.ps1`（SHA `FCBDB29A4D50C3105869672C6A230BB33A467A7E66675B84AC7486436BB43DD7`）错误地用out专用部署路径校验器检查规范managed测试目录，返回 `Deployment path outside out.`；清单生成、备份、暂存、切换、测试均未发生。原脚本/失败说明保留。第二版只将这一处适配改为原 `Get-OfflinePackagePath`，精确目标白名单、完整文件集合和重解析检查不变；未修改生产校验器。

最终审计 `e-support-inputs-first/final.json`，SHA `9865F400DE0EF22BC5877343239514EA5E3DC88A765292DC55C6FA4543584099`。复核本轮离线输入/共享支持/切换清单、当前原生/148暂存树、既有产品与独立Python输入、正式151文件/journal、旧23固定目录及旧405树/5164其他独立文件。六索引已登记变化和已知签名Git差异仍单列，不能称整个旧冻结未变。正式EXE仍 `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`。

本轮会话均已结束，功能测试、模型、worker、PS5.1、实际候选、部署和Git写入均未执行；177新case＋3加强case仍待F。固定输入仍b69/共享Debug，`executionReady=false`、`stageAccepted=false`。下一步按已有授权备份核验并切换23目录，再原回执/新完整冻结/每配置156项F主体；主体终态报告之后执行原24项Windows PowerShell 5.1/配置。人工、真实模型预算、长稳、E5-D代表性内容与既有失败不由本轮准备关闭。

以下为历史记录。

### 最新：原生测试构建、原清单与生成目录暂存

本轮复用同一 `63edb4c3d5934fd3986751844f8edafc` 捕获、5095文件、source `EB1F43A1BFC11609855FB69A785C1649569A6C9DC8BB0B09A2953B6294ED79FB`、manifest `CAA33938EADCA22D8A199038A6A4E6E2D1F196F0FFF51DEE589CE727C71DE0CA`，未改产品、测试或生产配方源码。证据位于 `out/verification/m8-7/e-native-runtime-second/`。

- 新目录原生 `tests ON`：Debug 102.08秒、Release 96.77秒，各322步，无编译或CMake警告/错误。原VS环境从首次configure即生效；构建前检查all目标不包含托管还原、整条Build或部署。动画插件与GLFW的原CMake运行时复制逐字节核对。时间仅为构建记录，不是性能验收。
- 原 `ctest -N` 和JSON各180项，无缺少可执行文件提示；原全部命令与属性仅允许明确的捕获、输出、Git和SDK路径重绑定后逐项相等，未提高超时、删用例、禁用入口或替换宿主。该提示检查不证明运行依赖完整。
- 原布局新建138个默认＋10个可选生成目录，全部采用CreateNew，合计3790文件，与已绑定的完整托管/配对图产物逐项相等；源产物未变。各141个直接dotnet DLL入口存在且登记哈希，不等于资源、共享runtime、CodeGen动态编译或独立apphost全闭包已就绪。
- 原独立Python输入2443文件及准备环境回执重新打开验证，4份当前worker/lock文件另行比较并持有只读句柄。**Python不属于引擎5095项捕获**，身份单列，不借用引擎source哈希；未运行解释器、pip或worker。环境回执仍 `B17D3FCC356D237B554A984C2ADD9E6558D5C68516F1045779F50B3581EF0108`。
- 原分组仍为每配置156项F主体＋24项后置Windows PowerShell 5.1。21直接脚本＋3间接宿主名单未变，原三个dispatch块完全一致；8个间接来源中6个未变，另2个的新增用例登记/SDK只读回执断言已复核并登记新哈希。只执行CTest显示/选择清单，没有启动测试。后置组必须在F主体终态及报告之后执行，不能算跳过通过。

本轮辅助脚本失败均保留：

1. `e-native-runtime-first` 预检错误地到引擎捕获中查Python，原生configure尚未开始即失败；原结果SHA `2F4D6023D016752C32502C3B339D92DC212010ADC6B81C0D6DF2B401F8798034`。第二轮改为原独立Python合同和外部worker句柄核验，未修改捕获、解释器或历史回执。
2. 首次 `Stage.ps1` 把AI汇总中的文件数量当作详细清单，复制前失败，创建目录/复制文件均0；原结果SHA `3958126FE0C93C0FF00F5EAAE2B8647E33544858D4D12727074713140B9A9F7B`。`StageSecond.ps1` 改为读取汇总所绑定哈希的原详细产物清单；保留完整集合/字节检查，不覆盖首轮记录。

最终审计 `e-native-runtime-second/final.json`，SHA `0E55CD1CDB85D60701C994B5E346DA52EBEB177B92A87AE2BAA09A1A5923D749`。当前原生及暂存产物、既有69项目/配对图/26候选/模块/SDK/六索引核验；正式151文件及journal、旧23固定输入、旧405树与5164其他独立文件未变。六索引已登记变化与已知签名Git差异仍单列，不能称整个旧冻结未变。正式EXE仍 `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`。

全部本轮会话终态，无功能测试/模型/SDK链/PS5.1/部署/提交推送。177新case＋3加强case仍待F；固定输入仍b69/共享Debug，`executionReady=false`、`stageAccepted=false`。下一步补CodeGen离线绑定、共享运行支持与原验证回执，然后受检备份切换固定输入、建立新完整冻结，再统一F。当前捕获参考图尚未生成，不可盲目重跑会覆盖旧输出的固定KernelReferenceCapture。人工、模型预算、长稳与E5-D代表性内容仍独立未验收。

以下为历史记录。

### 最新：同源独立模块与 SDK 声明准备完成

继续使用 `63edb4c3d5934fd3986751844f8edafc` 的5095项捕获，source `EB1F43A1BFC11609855FB69A785C1649569A6C9DC8BB0B09A2953B6294ED79FB`；本轮不改产品、测试或生产脚本源码。原本地离线依赖准备与捕获副本 `--no-restore --no-incremental` 编译不变。

- 独立gRPC客户端：原两项依赖准备/配置，完整测试工程依赖图编译Debug 8.77秒、Release 9.15秒，均0警告/错误。原包生产者生成各22文件；代次分别 `86b9cae399bc4081884fcec894bbe931`、`9b36467452a443d2a804bc434917607a`，位于 `out/package/modules/<配置>/<代次>/client`。核验运行DLL、NuGet归档与实际条目字节，不代表worker/Python或RPC功能已运行。
- 两模块索引生产前精确备份，原 `File.Replace` 及其备份保留并核验。加上上一批四个产品索引，六索引现均匹配同源产物；不是六文件原子事务。固定23测试目录仍b69/共享Debug，原编译回执尚未切换。
- SDK：原单图离线依赖准备、完整依赖图编译和只读输入观察完成。新Debug准备87.24秒（编译83.48秒）、Release准备86.76秒（编译83.19秒），均0警告/错误；各原16字段声明、85文件harness/7文件MCP及三个产物树和七个原生工具身份均核验。没有运行SDK链、原生工具构建或PS5.1；单份可执行文件观察不等于整个工具链/运行时闭包或执行证明。

首轮SDK Debug编译91.97秒、0/0，随后原观察器报 `sdk_input_preparation_failed:tools:IOException:workflow_file_identity`，未生成成功声明。只读诊断确认 `C:\Program Files\Git\cmd\git.exe` 有两条硬链接，触发原 `Links != 1` 拒绝；同安装的 `C:\Program Files\Git\bin\git.exe` 只有一条链接，字节SHA均为 `FEC691D80FCCC35FCC309FBC9F720536C1D795B8A562EC169F28C9923DA9600F`、签名有效、版本2.49.0.windows.1及exec-path相同。新一轮显式选择后者，不改任何链接、ACL/MIC、工具文件或验证器，不使用更新后的应用附带Git。首轮目录 `out/verification/m8-6/sdk-inputs/b39ca85d121f4904963f56acb8f5de46`、失败回执/日志及三个编译产物树全部保留且最终复核未变。

新SDK声明目录为 `out/verification/m8-6/sdk-inputs/d36523fbcdc7484ebdce5e6055b289ed`（Debug）、`6f5280f620f841f681076187cdc5fcd3`（Release）；只执行原准备分支，**功能测试和SDK链均未执行**。新依赖/声明/产品回执不覆盖旧b69声明，原失败不改判通过。

最终审计：`out/verification/m8-7/e-module-sdk-first/final.json`，SHA `2AF1373BE3FBBE16EC157CCC5E58E6A78DF1D58CA3C51FAB0B8935D93EE63D75`。核对当前26候选、上一批全69产物与配对图、新旧模块/SDK、精确备份、捕获与依赖。正式151文件/journal、23固定目录不变；旧405目录和5164其他独立文件不变，六索引变化及已知签名Git变化独立记录，不能称整份旧冻结未变。正式EXE仍 `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`。全部本轮会话终态；未部署、提交推送、调用模型或读取密钥。

下一步是同源native tests ON构建/原180项清单与运行时、CodeGen、Python准备，再完整备份核验切换固定输入、建立新冻结后统一F。177新case＋3加强case仍未运行，`executionReady=false`、`stageAccepted=false`；F主体终态及报告后才执行原24项/配置Windows PowerShell 5.1。人工、真实模型、长稳和E5-D代表性负载保持独立未验收。

以下为历史记录。

### 最新：同源独立候选与 F 输入准备第一批

本轮未修改产品/测试源码，复用原捕获 `63edb4c3d5934fd3986751844f8edafc`，5095文件，source `EB1F43A1BFC11609855FB69A785C1649569A6C9DC8BB0B09A2953B6294ED79FB`，manifest `CAA33938EADCA22D8A199038A6A4E6E2D1F196F0FFF51DEE589CE727C71DE0CA`。仅在唯一工作区的新证据/构建目录准备，不改正式安装、旧源码归档、用户配置或密钥。

| 本轮准备 | Debug | Release |
| --- | ---: | ---: |
| 枚举全69托管项目：独立本地离线还原＋无还原完整编译 | 158.51秒，0警告/错误 | 176.91秒，0警告/错误 |
| 原十项包依赖准备＋原候选生产入口 | 13产品，407.06秒 | 13产品，441.72秒 |
| 新捕获目录原生候选构建（tests OFF） | 265步骤，无编译诊断 | 265步骤，无编译诊断 |
| 扩展Editor→Player同上下文编译 | 83.19 / 9.88秒，均0/0 | 82.97 / 9.52秒，均0/0 |
| 组合Editor→Player同上下文编译 | 82.91 / 9.62秒，均0/0 | 83.59 / 9.60秒，均0/0 |

时间仅构建记录，不是性能指标；部分准备并行执行。配对图遵循原六项离线准备，Editor完整依赖先编译，Player只复用同一捕获/配置/变体/输出上下文的依赖。两份共享扩展DLL在每对实际Editor/Player中逐字节相等，Player无AI/Editor测试/gRPC依赖。结合前片同捕获AI与合同图，当前12项可选图均有编译证据；不是单一输出根，也不承诺不同输出目录的DLL可复现相同哈希。

新产品代次：Debug `b425ded5f6534aa289249985a0fa5b7c`、Release `4712c327f754449895e08acdb6deeab1`，均在 `out/package/m2-7/<配置>/<代次>/`，各13种原矩阵产品、合计26产品/2778文件。AI候选为各目录的 `editor-ai/NcmaEngine.exe`。完整清单、文件哈希、来源和依赖开关已核验，**候选实际EXE未运行、未部署、未获人工验收**；EXE哈希本身不是整个托管产品身份，必须连同manifest和全文件集使用。

生产前独立备份六个固定包索引，原生产者仅更新双配置的 `m2-7/packages.json` 和 `m8-4/packages.json` 四项，并保留原备份；两个模块包索引仍旧。原默认索引copy-before/write及AI索引File.Replace均未改，不声称跨文件原子提交。固定23目录/1357文件仍属b69/共享Debug，原编译回执也未切换；**不可拿新包配旧测试输入开始F**。

失败原样保留于 `out/verification/m8-7/e-candidate-packages-first/`：

- 首次内联cmd引号错误，VS/构建均未启动；固定路径批处理按原方式初始化VS后成功，无策略变更。
- `FullManaged.ps1` 在 `dotnet --info` 的Workload Installer枚举处TypeInitialization/NullReference失败，尚未还原/编译。独立 `managed-second` 使用版本/SDK/运行时清单命令后执行不变的69项目门禁；原workload信息查询仍记失败，未修复/安装SDK。
- 初次Player编译0/0，随后错误地把不同私有输出目录与旧Editor进行字节等同比较而失败；PE中实际观察到不同绝对PDB路径/调试标识，不声称已穷举所有差异。包内同上下文组件一致。恢复原Editor→Player配对范围后仍严格要求字节相等，没有改源码、PathMap/确定性设置或放松原产品约束。
- 第二轮配对包装参数被PowerShell解包成字符串，MSB1001在编译前拒绝；`pairs-third` 改为显式字符串数组，独立新目录8次编译通过。原两轮输出/回执/日志保留。
- 原Audit语法检查失败，未执行/无final；`AuditSecond.ps1` 修正括号后完成。另一次自动审批服务Bad Request在执行前失败，确认目录不存在后经同一审批机制原样重试，未绕过审批。

最终审计 `out/verification/m8-7/e-candidate-packages-first/final.json`，SHA `36119592FFED3CCFD3D06004A5FAEB64741C5290C5921A334ACB660520503C4B`：当前26候选、旧26候选、全69产物、配对图、前片8图、捕获/依赖/备份、正式151文件及journal均核验。旧405目录与5166其他独立文件不变；四索引变化与已知签名Git工具变化分别记录，不能称整个旧冻结未变。正式EXE仍 `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`；journal仍 `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1`。

本轮未运行功能测试、实际Editor/Player、模型、测量、PS5.1，未提交推送；177新case＋3加强case仍待F，executionReady/stageAccepted均false。下一步依次补齐同源独立模块/SDK、native测试配置与原180项清单、运行时/CodeGen/Python输入，再经完整备份核验切换固定输入并建立新冻结。F主体终态及报告后执行原24项PS5.1/配置；实际模型/人工/长稳预算、E5-D大世界内容和历史缺陷保持独立未验收。

以下为历史记录。

### 最新 E2-E 第八片：显式候选、内存场景与联合关闭记录

Ai-only产品新增 `--measure-ai-scene` 四参数入口，原apphost/候选/输入核对后，经原WindowsConfirmation默认“否”逐项展示场景/接收方/预算/输出，再复核同一租约。可见原CPU GUI、非preview，在原SceneDocument内装载副本并接入原领域提案；不载入项目/游戏脚本/资产/扩展，不读写用户布局/偏好。会话composition持续限制无脚本、仅Transform；模式专属UI/分发/快捷键限制阻止文件保存/打开、Play、导入、MCP/jobs，普通模式不变。AI从未配置状态开始，原逐次外发/应用人审仍必须完成，不自动调用服务。详细边界及CLI见[E计划第20节](M8_7_E_MEASUREMENT_PLAN.md)。

原驱动结束与有序关闭不改，失败不能跳过清理或隐式重试；失败持有同时保留宿主和输入。原宿主封存工作区时记录初终态场景SHA，新增56MiB闭合证据保留原联合字节、输入/入口/预算/关闭状态及类型名错误。严格原消费者/规范解码/独立预期，原CreateNew输出，无原场景保存、覆盖或自动重试。退出0只说明诊断循环/关闭/写入正常，不表示AI请求覆盖、模型结果、采样完整、长稳或目标机性能通过。

最终捕获 `63edb4c3d5934fd3986751844f8edafc`，5095文件，source `EB1F43A1BFC11609855FB69A785C1649569A6C9DC8BB0B09A2953B6294ED79FB`，manifest `CAA33938EADCA22D8A199038A6A4E6E2D1F196F0FFF51DEE589CE727C71DE0CA`；相对e104共13文件（7新6改）、6原备份。原六图离线依赖准备、捕获副本no-restore/no-incremental；本轮扩展为以下8次编译，均0警告/错误：

| 原图 | Debug秒 | Release秒 |
| --- | ---: | ---: |
| AIContracts | 65.67 | 65.77 |
| AI Services.Tests（含实际Editor） | 79.70 | 79.82 |
| Extensions Services.Tests（含实际Editor） | 82.32 | 83.35 |
| AI+Extensions Services.Tests（含实际Editor） | 82.93 | 83.34 |

不是全解决方案、default-only独立图、native/worker/完整包或实际运行验收。只读检查Debug实际Editor deps：AI版含AI而无扩展、扩展版无AI、组合版同时含二者；不拿测试harness的依赖集合当产品集合。

首轮 `91739e62` / `CF9770E9` Debug合同在新case第42行CS8602失败（0警告/1错误）；第二轮 `379c6b5a` / `0FDBC493` 合同通过、AI服务harness在新宿主case第47行CS0119失败（0警告/1错误，实际Editor已编过）。只修测试的显式非空副本及枚举全名，三个捕获的产品源码相同、原断言不改。两份失败捕获/日志/binlog/结果和第二轮成功合同产物均保留，不改判通过。准备脚本和宿主case补丁各遇一次执行前权限审查超时，分别按工具许可原样重试成功，无安全策略修改，见 `e-scene-candidate-third/PREPARATION_NOTES.md`。

最终 `out/verification/m8-7/e-scene-candidate-third/final.json` SHA `45B06A2886BFE31341B3A7D26C1923BC85815766E1FD9D1783A9902509A8398F` 核验当前8产物/回执/源码/6备份、原Program/Presenter/观测/旧case逐字重建、失败捕获及精确两处修复、用例登记、旧180清单原超时；前22片44主产物目录＋16 Editor目录、正式151文件/journal、旧405目录和5170其他独立文件未变，已知签名Git变化单列。

新增5合同＋2入口/UI＋3 AI宿主＋1互斥组合拒绝，共11个独立新case体，AI主路径10组；累计177新case＋3加强既有case，**全部留F未执行**。构建/审计已终态，无输入准备API/候选/宿主/模型/采样/导出/worker/PS5.1运行，无部署/提交推送/固定输入切换。下一步匹配独立候选及完整F输入就绪检查；实测仍需独立用户工作负载/模型预算批准。E5-D内容延期保持，F主体终态及报告后原24项PS5.1/配置，不能由本轮编译宣布E/M8.7通过。

以下为历史记录。

### 最新 E2-E 第七片：候选请求与受检单场景输入

已实现规范16KiB请求、原计划/实际租约目录绑定、受检单文件场景副本及显式模型限制。原CPU请求读取块复用，原输入/包验证不改；场景通过原CopyObserved保留句柄复制，不重开路径或累积pin。仅Ai变体、controlled-fixture、唯一scene.ncmascene、最多4MiB/4096对象、Transformv1且无脚本；不隐式注册提供商或加载其他组件/资产/项目。请求无密钥/凭据引用/审批位，准备对象无租约所有权或执行权限。原因是普通项目保存/导入与不可变输入锁冲突，下一片接入原内存文档副本，不放松原文件保护。详见[E计划第19节](M8_7_E_MEASUREMENT_PLAN.md)。

捕获 `e104a1fdf37140eba2eb615a6ac8800f`，5088文件，source `7F046323B6DD0BBAE1F874C91932B4685F4007B99297C77C0B9DAA83DB324047`，manifest `90793B070B0AECE1E142E63D3588B674B0482E6CAE4935735750D8BF57CCCEE1`。相对59a6共5文件（2新3改），3份原文件独立备份。原六图离线准备、捕获副本no-restore/no-incremental：合同Debug66.46秒/Release65.95秒，含实际Editor的AI服务图Debug79.79秒/Release79.98秒，四次均0警告/错误；不是全解决方案/其他变体/native/worker/包或独立宿主验收。

最终 `out/verification/m8-7/e-scene-inputs-first/final.json` SHA `2B94EAFDA1359F323CBF65DBCDE6598F19CB504BE3671FD1EFF4B625244D7B82`：当前捕获/四产物/回执/3备份、原CPU读取/租约方法/旧case体逐字重建、10用例登记及旧180清单原超时核对；前21片42目录＋14个Editor目录、正式151文件/journal、旧405目录及5170其他独立文件未变，已知签名Git变化单列。静态审计不等于功能测试，旧失败不改判。

10组新用例全部待F，累计166新case＋3加强既有case。全部构建/审计进程已终态；没有输入准备API、候选、宿主、模型、采样、导出、worker或PS5.1执行，没有部署/Git/固定输入切换。**AI测量CLI、本地确认、实际内存装载及联合关闭输出仍待下一片**，默认启动不变。当前只提供可组合的请求与输入服务，不能据此开始人工验收。全套同源输入后F，主体终态及报告后原24项PS5.1/配置；E5-D内容继续延后。

以下为历史记录。

### 最新 E2-E 第六片：实际宿主生命周期与受限观测

实际CandidatePresentation新增启动前单次内部配置接口，校验owner/编译配置/AI变体/Stopwatch频率并拒绝preview；默认关闭，未新增CLI/Presenter/MCP入口。原Start在实际工作区创建后挂接；原Dispose仅在AI TryClose成功后、清空引用前封存，失败关闭仍保留原资源及错误，不自动重试。可信调用者可帧外显式封存未排空前缀，报告副本在工作区释放后仍可读取；状态明确区分外层持有、证据失败和预算，读取无清理/IO副作用。

限制由原受检profile的目标与输出token/超时上限、1..32尝试组成；策略指纹不含配置/凭据UUID，更不读取密钥。Send及连接诊断共享保守计数，匹配的失败/人审拒绝尝试不退款、重配不续额，原人审仍执行；目标不符或额度耗尽拒绝。SealMeasuredObservation停止后续工作区发送，不取消或排空当前请求，不改原纯被动Seal合同。限制不是费用/全局网络配额或可信插件沙箱；诊断消耗预算但不进入authoring证据。完整边界见[E计划第18节](M8_7_E_MEASUREMENT_PLAN.md)。实际候选命令、本地运行确认、输入/输出隔离和联合关闭记录绑定仍待下一片。

捕获 `59a6ebbfa31a4ed39c3312bb37482654`，5086文件，source `AA1875297433260CFF35F66325846C855EEE2389B56EFBD1A9514661AC9AEFF0`，manifest `498E00FE7469BFD7B38E7131469C384EA9CD35B01B1E3544E585F1DBDCD966A6`。相对ef6714共8文件（4新4改），4份原文件独立备份。原六图离线准备＋捕获副本no-restore/no-incremental编译：合同图Debug66.13秒/Release66.31秒，含实际Editor的AI服务图Debug80.17秒/Release80.62秒，均0警告/错误；不是全解决方案、默认/组合变体、原生/worker/包或实际宿主验收。

最终 `out/verification/m8-7/e-host-observation-first/final.json` SHA `81E018236D59800B443CC6724BF3CCB492F51CA2B56C77E8A37B93F6195F7D9E`：当前捕获/四产物/回执/4原备份、去除挂接与限额行后原Program/工作区逐字重建、原case体仅登记/partial标记、6合同＋4宿主case及旧180清单原60/180/HostLifetime60秒入口核对。前20片40目录＋12个Editor目录、正式151文件/journal、旧405目录及5170其他独立文件未变；已知签名Git差异单列。首次Audit.ps1因第45行误用PowerShell只读Host变量退出1，未生成final；原脚本和失败记录保留，Audit-retry仅换专用局部变量，核验范围完全相同，重跑通过不抹去首次失败。

新增6合同＋4实际宿主用例待F，累计156新case＋3加强既有case；宿主夹具包含CPU GUI/DX11原循环8帧和未排空关闭，但本轮未执行，不能当独立apphost或人工验收。所有会话终态，无功能测试、原生宿主、模型、测量、导出、候选启动、输入准备、worker或PS5.1执行；无部署/提交/推送。固定b69/共享Debug未切换，无新全阶段冻结。**下一步独立候选入口、本地确认、实际负载/收件人/输入绑定与输出隔离**；E5-D手动内容确认继续延后，全套同源输入/F后再原24项PS5.1/配置，人工/apphost/M8.6/长稳与预算门禁不混结论。以下为历史记录。

### 最新 E2-E 第五片：进程＋多工作流联合证据

新增 `AiWorkspaceProcessEvidence` v1离线有界容器，保留原工作区/进程规范字节与外部独立SHA、完整身份、原输入计划和回执。复用原两个严格消费者，抽取原WorkflowProcessAssociation标签规则共用；包括空工作区，也检查run/source/workload/configuration/AI变体/Editor宿主。采样时长/间隔/容量和完成帧数须匹配计划，声明负载必须非空且非CPUidle；这只是数据合同，不能证明该负载实际执行、租约仍有效或模型调用已批准。

外层最多40MiB，预检深度8/512token/无数组与分字段base64限额，规范字节核对；工作区12MiB、进程16MiB及内部预算不扩大。Inspect仅一次解析进程，按原配置UUID/intent/序号保留工作流摘要、丢弃/失败/未排空和进程缺采；不跨配置平均，不跨报告时间戳相减。共同时间、来源、性能、负载执行和授权标志保持false。原CreateNew/Flush/同句柄回读只由可信调用者显式导出，尚未有实际候选宿主、输出隔离/审批接入；原CPU空闲入口不修改。完整合同见[E计划第17节](M8_7_E_MEASUREMENT_PLAN.md)。

捕获 `ef6714a547d040018bdf30c760b017ea`，5082文件，source `CA4A8D220AB03996A7404F84B97E9F630210E8034234A8E7C3AF32F005FEE75D`，manifest `460748229935A5870BD4EC5B99C4BD09E3E9C0D3CB3DFC2677E1A64ADE0068CA`。相对8696共4文件（2新2改），2份原文件独立备份。原六图离线依赖准备＋捕获副本no-restore/no-incremental编译：合同图Debug66.33秒/Release66.20秒，包含实际Editor的AI服务图Debug79.68秒/Release80.37秒，四次均0警告/错误；不是全解决方案/全部变体/原生/worker/包或运行验收。

最终 `out/verification/m8-7/e-joint-evidence-first/final.json` SHA `D4009D6BD101AB7938720DFB1C5EE9C0C14B53791B782418DA3CA5E03DC8C4EB`：当前源码/四产物/回执、2原备份、原标签规则块逐字复用、原case体仅新增登记、10组case及旧180清单60/180秒入口核对。前19片38目录＋10个Editor目录、正式151文件/journal、旧405目录及5170其他独立文件未变；已知签名Git工具差异单列，不冒称全部旧冻结一致。固定b69/共享Debug未切换，无新全阶段冻结。

10新组原AIContracts全部待F，累计146新case＋3加强既有case；所有会话终态，无功能测试、真实模型、测量、导出、候选启动、输入准备、worker或PS5.1执行，无部署/提交/推送。**下一步可信候选宿主接线、独立负载/模型预算及输出隔离**；联合字节记录不代替实际运行证明。E5-D人工内容确认继续延后，完整匹配输入后F，主体终态报告后原24项PS5.1/配置；apphost/M8.6/人工/长稳及预算门禁仍独立。以下为历史记录。

### 最新 E2-E 第四片：原AI工作区生命周期观测与多工作流证据

原 `AiAuthoringWorkspace` 可由可信宿主单次显式启用被动观测：创建工作流后挂接原观测API；替换/重配/关闭时仅在原TryUnregister成功后封存。主动封存可保留活跃或未排空前缀，不取消/发送/Pump/重试原业务，关闭责任仍属于原owner。默认关闭，Program/Presenter/MCP尚未启用；连接诊断及绕过包装器的领域提交不在范围内，原发送、资源批准、最终应用的人审均保留。

最多前32工作流、1MiB/条、8MiB总嵌套证据，完整预留下一条预算；丢弃和采集失败显式计数。配置UUID分别保留，不回显模型/endpoint/key，不把不同配置混算延迟；丢失条目仍可能隐藏更多变更。新v1多工作流规范JSON最多12MiB，预检结构/字节预算、原单工作流严格重放、计数/序号/窗口/跨条请求与会话去重；输出是脱离数据而不是执行证明或授权。原CreateNew/Flush/同句柄回读导出仅由可信调用者显式触发，宿主接入仍须输入/输出隔离。退休时编码与分配有观测开销，不承诺0B或无扰动。详见[E计划第16节](M8_7_E_MEASUREMENT_PLAN.md)。进程＋多工作流联合容器及产品启用仍待下一片，不能复用CPU空闲记录冒充游戏/AI验收。

捕获 `8696a6d2e97f4508b0a84855d621c89d`，5080文件，source `FAD0BC30E96F3326C2E1256FB5E8681E0ABA1210FC774B223421D7D8466D3AC0`，manifest `96B7C43B0BBB7D37D9DFBED01CE2EBA77C651D694875F79E55DB2CBEFA99D51D`。相对5d14共5文件（3新2改），2份编辑前原文件独立备份；原六图离线依赖准备，捕获副本no-restore/no-incremental编译：合同图Debug66.21秒/Release67.29秒，包含实际Editor的AI服务图Debug80.36秒/Release80.47秒，四次均0警告/错误。不是全解决方案/全部产品变体/原生/worker/包或实际运行验收。

最终 `out/verification/m8-7/e-workspace-observation-first/final.json` SHA `91E59B30F9CBF79779718D7039762988517E69AC6CB18161844D6A3A978EF669`：当前捕获/四产物/回执、2份备份、去除partial及三处状态/挂接行后原owner逐字一致、原case体仅新增登记、12组case数及旧180清单原60/180秒入口核对。前18片36目录＋8个Editor目录、正式151文件/journal、旧405目录及5170其他独立文件未变；已登记签名Git差异单列，不能称全部旧冻结字节一致。审计助手首次内存CRLF查找失败，未创建或执行审计；保留失败记录后统一LF匹配，源码/测试断言不改。固定b69/共享Debug未切换，无新全阶段冻结。

新12组原AIContracts全部待F，累计136新case＋3加强既有case；本片只完成编译和静态核对。所有会话终态，无功能测试、工作区采集、导出、真实模型、候选启动、输入准备、worker或PS5.1执行，无部署/提交/推送。**下一步进程＋多工作流联合证据与独立负载接入**；E5-D内容确认仍延后，模型调用/长稳预算与人工验收没有被本轮批准。之后匹配全套输入/F，主体终态汇总后原24项PS5.1/配置；既有apphost/M8.6/人工/性能债务保持独立。以下为历史记录。

### 最新 E2-E 第三片：独立候选 CPU 空闲诊断入口

原Main新增独立显式 `--measure-cpu-idle` 四参数分支及保留资源守卫，普通默认入口不启用采样。规范请求最多16KiB，外部SHA/原输入固定、包校验、编译配置/变体一致；只接受editor-idle空负载。当前EXE、EntryAssembly与AppContext目录须匹配租约中的候选路径，拒绝dotnet或测试宿主替代；仅路径和保留磁盘字节核对，不声明加载内存、全部模块/系统运行时或源码构建证明。

原WindowsConfirmation默认否，显示运行/路径/预算后才允许继续，等待后复核输入。沿现有CPU preview启动可关闭的可见诊断窗口、独立临时日志、固定30fps上限，不创建项目/authoring presenter/AI工作区/密钥输入/扩展启动组/偏好布局；原CandidatePresentation实现未改。原Start/Run已失败关闭不在finally自动重试，保留资源阻止同进程再次Main。CPU证据封装最多24MiB，沿原严格进程消费者核对数值、计划、PID与完成帧，缺失/失败明确保留；输出无请求路径或秘密，并复用原CreateNew/Flush/回读校验，无覆盖/删除/自动重试。详见[E计划第15节](M8_7_E_MEASUREMENT_PLAN.md)。本片不是AI工作流联合输出或真实游玩负载执行器。

捕获 `5d14cd54d0d14acb870c5799724bf03a`，5077文件，source `38EE5C212941EF0B257C364A0132D4DF0914F2D0E1A270E2F572D2AB8D5475A4`，manifest `35AE17577ADF4BAB023682F138C7D27B4B31D58472C848D060016EA62EA687A9`。相对fc8016共10文件（5新5改）；3份编辑前备份＋2份从已核验旧捕获复制的额外原文件，均独立保留。原六图离线准备后，合同图Debug66.13秒/Release66.60秒、含实际Editor的AI服务图Debug80.29秒/Release80.53秒均0警告/错误；不是全解决方案/全部产品变体/原生/worker/包或实际运行验收。

最终 `out/verification/m8-7/e-candidate-idle-first/final.json` SHA `6215E7244E86DC5B4EEB816E87DEECACB19F5FE6D608AAAE4F21749CADD17F3A`：当前源码/四产物/回执、5份原备份、去除独立分支/守卫/partial标记后原Main逐字一致、原测试体仅两处新增登记、12组新用例及旧180清单的原60/180秒入口核对。前17片34目录＋6个Editor目录、正式151文件/journal、旧405目录和5170其他独立文件未变；已知签名Git差异单列。固定b69/共享Debug未切换，无新整阶段冻结。

新增10组AIContracts＋2组EditorService待F，累计124新case＋3加强既有case；真实Main新用例只覆盖早期参数拒绝，合成证据/测试宿主拒绝不是独立apphost正向验收。所有会话结束，未执行候选、输入准备、确认窗口、功能测试、采样、导出、模型、worker或PS5.1；未部署/提交/推送。**下一步经独立确认的真实负载与原AI工作流观测/联合输出**，CPU预览不替代完整编辑器、大世界或AI性能负载。随后匹配全套输入/F、主体终态汇总后原PS5.1 24项/配置；E5-D手动内容确认仍延后，人工/apphost/M8.6/长稳和预算门禁保持独立。以下为历史记录。

### 最新 E2-E 第二片：受控原循环驱动和失败关闭保留

`ApplicationRunWindow` 为原循环增加默认null的显式时长/停止控制，检查在帧间，不改pipeline身份、每帧时钟基线或默认入口。`MeasurementRunDriver.RunAndClose` 接收已启动的可信宿主和已准备租约，先检查原宿主owner/回调重入与输入计划/身份，再承担关闭责任；预检拒绝不接管。原Run退出后封存进程证据、复核输入、调用原Dispose，只有应用成功释放后才释放租约。业务原异常保留，附加失败按序聚合；关闭失败保留应用与输入，只有显式RetryClose重试，诊断读取无副作用。原故障帧不算完成帧，Closed不是业务/性能成功。

不是看门狗、独立候选启动器、真实负载执行器或自动审批；帧/pacer/IO/关闭可能超时，窗口与采样起点也不是精确同步。正式Program/Editor恢复入口不启用，未调用模型、装载负载或写证据文件。固定产品文件不证明当前进程已加载它们，报告仍明确ExecutedProductVerified/WorkloadApproved/PerformanceAccepted=false。完整边界见[E计划第14节](M8_7_E_MEASUREMENT_PLAN.md)。

首捕获 `18ebe0c496cc4846b02ccbd3f043470f` 四图编译0警告/错误；静态复核发现宿主回调中另一驱动可能在Run拒绝重入之前接管关闭责任，改为沿原CanRecoverPresentation getter执行Verify。修正两文件并加强新用例，原捕获/产物/日志、两份pre-fix备份和STATIC_REVIEW.md完整保留，不把静态发现写成已运行测试失败。

最终捕获 `fc80164e0e644b2bb531d0cb695fda0b`，5072文件，source `4E4D19E8B9C8B7C21F7FAC23AAB3C4B5E90EFF3C4507B2FE987F542CEA3BC0B4`，manifest `B68DFD3A9EE61EAFE7BF0EBFE98A15270069B56C643158C4B8CED534A4F0B997`。相对16bc共6文件（3新增3修改），相对首版仅2文件。原六图离线准备后，合同图Debug66.27秒/Release66.30秒、含实际Editor的AI服务测试图Debug80.42秒/Release80.93秒，全部0警告/错误；不是全解决方案、全部变体/原生/worker/包或运行时验收。

`out/verification/m8-7/e-measurement-driver-second/final.json` SHA `C0BFD0621E82D02C5E66BB6EDDDEB1A6EE7560C4D0F1F9DF8D1BDA3DF92A1CB3` 核对四个新产物目录/回执、两版捕获/精确修正差异、原循环去除可选窗口后逐字一致、原用例体仅夹具可见性/登记变更、10组新登记、旧180清单/原60秒。前16片32目录＋4个Editor目录、正式151文件/journal及旧405目录/5170其他独立文件未变；已知签名Git差异仍单列，非整个旧冻结无差异。固定b69/共享Debug未切换，无新整阶段冻结。

全部会话结束；新增10组待F，累计112新case＋3加强既有case。测试、输入准备、驱动运行、采样、模型、worker及PS5.1均未执行，未部署/提交/推送。**下一步独立候选测量入口、实际装载/负载/审批绑定及完整受检证据输出**，再匹配全套输入/F、主体终态汇总后原PS5.1 24项/配置。人工/apphost/M8.6/E5-D/长稳和预算审批保持独立待验，E/M8.7不改判通过。以下为历史记录。

### 最新 E2-E 首片：可信本地只读测量输入准备

新增 `MeasurementInputsPlan` / `MeasurementInputLease`：绑定独立提供的capture/product/workload清单SHA、原身份和有界采样计划，读取产品与负载清单、固定声明载荷并核对实际文件集合；强制调用原 `DeploymentManifest.Validate`。原 `WorkflowPhysicalPins` 仅增加同句柄复核方法，路径/链接/共享模式/数量及释放规则不变。owner限定、单次Prepare、失败保持Faulted，显式Dispose；没有启动/审批/重试/删除能力。

产品最多1024文件、负载512文件、每载荷64MiB、载荷合计512MiB、单manifest4MiB，仍受原2048文件/4096目录句柄总预算约束。负载schema1严格字段与场景分类，仅editor-idle允许空列表。计划限定1小时、100ms至60秒间隔、8192样本，容量须覆盖计划，不能预设截尾。捕获仅验证manifest，不重验全部捕获源码；目录集合仅在检查点有效，不能阻止之后新增文件；载荷一致也不证明实际加载/执行或模型/负载审批。详见[E计划第13节](M8_7_E_MEASUREMENT_PLAN.md)。本轮没有实际调用准备入口。

捕获 `16bc41d58b58417e8c443dcd43f3af31`，5069文件，source `9C941F9BB62CC7EA447D3C20987136BC409FDD5F6867E0F239EE705AF66A1A95`，manifest `EF1EB8B6BF1EE76814CB9788AC87E42C597D3B591F96EDD25342FD8BE3E4BCE6`。相对5056共5文件：3新增、2修改，2原文件备份。原六图离线依赖准备后，仅verify-contracts AI.Tests依赖图 Debug66.35秒/Release66.51秒均0警告/错误；不是全解决方案、实际Editor宿主图、原生/worker、包或运行时验收。

最终 `out/verification/m8-7/e-measurement-inputs-first/final.json` SHA `5F7C6D82007EE4288C3EFC4D4EF09CC0DE45CC3E84947AA5402FA95333280256`：捕获/回执/产物、原pin规则（剔除新增方法后逐字一致）、原用例体与10组登记、原180清单/60秒、前14片28目录＋2个Editor目录、正式151文件/journal核对通过。旧405目录和5170其他独立文件未变，已知签名Git差异单列。固定b69/共享Debug未切换，无新整阶段冻结。

10组新增用例覆盖计划/身份、同句柄持有、载荷变更、额外文件/目录、清单与原包校验、畸形/预算、owner/状态、空负载及私有夹具硬链接；仅登记、未执行。累计102新case＋3加强既有case待F。测试、输入准备、采样、模型、worker和PS5.1执行均0，未部署/提交/推送。**下一步在显式保留的输入租约和原宿主循环上完成受控测量驱动**，再匹配全套输入/F、主体终态汇总后原PS5.1 24项/配置。负载/模型预算、实际apphost/人工/M8.6/E5-D/长稳仍独立待验，不称E或M8.7通过。以下为历史记录。

### 最新 E2-D：领域联合字节消费者及进程证据标签关联

AiWorkflowObservationEvidence输出与读取共用原领域/模块/传输数值验证器。运行时报告独有的RequestId/ElapsedMilliseconds验证后省略；Decode不捏造丢失的Code/异常类型/耗时，不构造可执行报告或回执。公开Document仍是脱离副本，再编码须重验。schema1输出形状保持，新增显式旧数值投影的字节相等用例；**该兼容性用例尚未运行**。

Decode对输入有界快照先做深度/字段/标量及各级数组预算检查，再反序列化、原模块重放、摘要重算与规范字节比较；重复/未知字段、派生值或摘要篡改拒绝，pending/丢失不改判完整。DecodeExpected绑定完整ObservationIdentity和intent。WorkflowProcessAssociation核对两份独立预期SHA与身份，再核对run/source/workload/config/variant/Editorhost；不读取磁盘、不相减跨报告时间戳，CommonClockVerified/ProvenanceVerified/PerformanceAccepted均false。完整预算和覆盖边界见[E计划第12节](M8_7_E_MEASUREMENT_PLAN.md)。没有路径读取、运行驱动、Program启用或新增依赖。

捕获 `50562164808d440983683cbabe2aa46d`，5066文件，source `2629158306AF764915257E22CBDC37AEBDD447DDF4AD9F4FB3C32EC5961066B4`，manifest `3076CD80C6D06A30F374BD50676C670A761B94542B6D05AC97A1DD58D6942D97`。相对4f72共5文件：3新增、2修改，2个原文件独立备份。原六图离线依赖准备后，仅verify-contracts AI.Tests依赖图 Debug65.87秒/Release65.97秒均0警告/错误；不是新实际Editor、全解决方案、原生/worker、包或功能验收。

最终 `out/verification/m8-7/e-workflow-consumer-first/final.json` SHA `F2C6946AD67961F0985F5D539285208F8906CB4EF02A0A434EBE778F5D058F00`：捕获/回执/产物、原用例体及10组登记、原180清单/60秒、前13片26目录＋上片2个Editor目录、正式151文件/journal核对通过。旧405目录与5170其他独立文件未变，已知签名Git差异单列。初次审计辅助生成命令引号解析失败、任何语句执行前终止，记录于Audit-preparation-failure.md；用literal here-string修正后通过，不改产品/断言，不是功能测试失败。固定b69/共享Debug未切换，无新整阶段冻结。

所有会话结束；测试/采样/导出/模型/worker/PS5.1执行均0，未部署/提交/推送。新增10组用例含v1形状/往返、身份、原模块重放、传输数值、畸形与预算、pending/截断、标签关联与字节/产品记录错配；累计92新case＋3加强既有case待F。**下一步独立固定产品/工作负载输入和受控测量编排与驱动**，随后匹配全套输入/F、主体终态汇总后原PS5.1 24项/配置；真实负载/模型/长稳预算及实际apphost/人工/M8.6/E5-D门禁不改判。字节消费者实现不等于E/M8.7或性能验收。以下为历史记录。

### 最新 E2-C 第二片：显式可选原循环接线及进程字节消费者

新增 `ProcessMeasurement` / `ProcessMeasurementEvidence`，原ApplicationLifetime.Run新增末尾可选参数、EditorRunLoop仅透传；正式Program不启用，不新增CLI/Presenter/MCP、线程/进程或业务执行器。每轮原finally在LoopCostCapture.End之后最多Poll一次，保留完成/停止/故障标记；初始化时钟或Begin失败、恢复等待、启动及关闭未被观察。采样开销不进入同轮LoopCost，却可影响下一帧delta/Present间隔/进程统计，不是无扰动或后台观察器。

要求同owner、同应用和pipeline，仅弱引用核对身份；Seal须在Run外一次执行，不能Stop/Dispose业务。非OOM观察异常只标记并停止Poll，不替换原业务结果；计数器/时钟故障沿原序列保留。源码/捕获manifest/产品manifest/负载SHA及配置、变体、宿主类型只是调用方提供的标签；DecodeExpected逐项核对独立固定的预期身份，不访问磁盘或证明真实执行/审批。只提供进程报告纯字节消费者，尚非领域联合报告完整消费者或可执行测量驱动。

原series Decode与新envelope Decode均先检查16MiB、深度8、每对象32属性、8192样本等边界，再反序列化/重验/规范字节对比，拒绝重复未知字段、计数和派生值篡改；不回显输入，不新增文件读取/导出调用。原AIContracts新增8组边界/原顺序/生命周期/身份/往返/篡改/预算用例，全部待F，原60秒不变。

捕获 `4f72e1cf016d466798cc21516b170746`，5063文件，source `703826DB2C4261E6C5570E1373BF3C7F307777466783B0AC69058A892BFE702F`，manifest `66400DF341CD8B050795FE262DFC8F6CE15819E55A8A786EAC8ADCEF545AA7BF`。相对74ed共7文件：3新增、4修改，4个原文件独立备份。原六图离线准备后，verify-contracts Debug66.33秒/Release67.42秒，verify-ai Services.Tests（含实际Editor）Debug80.15秒/Release80.34秒，全部0警告/错误。不是全解决方案、各产品变体、原生/worker/包或运行时验证。

最终 `out/verification/m8-7/e-process-host-first/final.json` SHA `2A85372E5502474DB3EFF8C535A66EE1D265B5BA2567740CF99972B4D549CACB`：四个新产物目录/回执/捕获源码、反向去除可选观测后原生命周期/恢复文件逐字一致、原测试体及8组登记、180清单/60秒、前12片24个私有目录、正式151文件/journal核对通过。405旧目录和5170其他独立文件未变，已知签名Git差异单列，非整个旧冻结无差异。固定b69/共享Debug未切换，无新整阶段冻结。

全部会话结束；测试、采样、导出、模型、worker及PS5.1执行均0，未部署/提交/推送，累计82个新case＋3个加强既有case待F。**下一步领域联合报告离线消费与独立固定产品/负载输入的受控测量编排**。之后匹配全套输入/F，主体终态汇总后原PS5.1 24项/配置。真实模型/负载/长稳预算须确认；apphost/人工/M8.6/E5-D债务保留，不称E/M8.7或性能通过。详见[E计划第11节](M8_7_E_MEASUREMENT_PLAN.md)。以下为历史记录。

### 最新 E2-C 首片：当前进程有界时序核心已实现，尚未接入宿主

新增 `Ncma.Application/ProcessSeriesCapture.cs`、`ProcessSeriesCounters.cs`、`ProcessSeriesEvidence.cs`。由owner显式Poll，构造不读取计数器、不创建线程/进程/定时器。只读当前进程；采样间隔100ms至60秒、窗口至多1小时、最多8192条（默认4096）及36000时隙。半开窗口、一次Poll最多一条、没有追赶补造或滚动覆盖；漏掉的到期时隙与未来未观察尾段分开，容量停止和时钟故障保留已有前缀。

CPU是累计100ns而非百分比；工作集/私有内存/句柄/线程及近似进程分配、原始托管堆、最近GC指标按组记录。不可用组为null和枚举失败，不泄露异常文本，不强制GC。owner分配差值保留负值并标无效，不钳成0；数据包含观察者开销，连续读取不是原子快照。owner阻塞会漏采、系统读取可能扰动被测进程；本步不是独立后台采样器或泄漏证明，不改原四检查点和生命周期。

纯校验复制报告并生成至多16MiB字节，核对时隙/时间/计数守恒/终止原因/计数器形状；没有任意JSON读取器、文件导出调用、源码/产品/工作负载绑定或完整离线消费工具。增加命名测试程序集内部可见性，不向产品增加AI依赖；原AIContracts登记11组计划/边界/慢读取/漏采/失败/篡改/当前进程用例，保留原60秒，全部待F执行。

捕获 `74ed87c6f8aa4beaab8771cee20fe681`，5060文件；source `F7FFAD2A64DD8A0D101640EE7FA13B10841D9DB671FE450F7F79DAD57EC09D2C`，manifest `0A4CB32E9C15E31BC64DF0B7296D98D4254504E8452AD11196F0A83DBC7B0053`。对0709共6文件变化：5新增、Program仅测试登记，原文件独立备份。原六图离线依赖准备，仅verify-contracts AI.Tests依赖图 Debug65.62秒/Release65.84秒均0警告/错误；不是全解决方案、实际Editor、原生、worker或包验证。

收尾 `out/verification/m8-7/e-process-series-first/final.json` SHA `C1071320E9B2C463B5E7AFBBF5FC819269997A586152E76E2C676BEA6F47EB34`：当前捕获/回执/产品、原登记/180清单60秒、前11片22个私有目录、正式151文件/journal核对通过。旧405目录和5170其他独立文件未变，已知签名Git差异单列；固定b69/共享Debug不切换，没有新整阶段冻结。全部执行会话结束，测试/采样/文件导出/模型/worker/PS5.1执行均0，未部署或提交推送。累计74个新case＋3个加强既有case待F。

**下一步受控宿主测量接线与完整离线消费/来源绑定**，然后匹配全套输入/F，主体终态汇总后原PS5.1 24项/配置。真实负载与长稳预算仍需确认；实际apphost/人工/M8.6/E5-D等独立门禁不改判。详见[E测量计划第10节](M8_7_E_MEASUREMENT_PLAN.md)。以下为历史记录。

### 最新 E2-B2 工作流关联及数值联合输出已实现并编译

可选 `AiDomainWorkflow` 在首次桥接Begin前显式开启观测，默认关闭，复用原owner/配置边界。DomainModuleBridge对每个真实ModuleSession接入32事件捕获，在实际排空释放前封存独立报告，后续请求不覆盖旧会话。最多256请求/4096领域事件，可收紧，超限保留前缀并声明缺口；Seal不Stop/Pump/Cancel，不改变正在进行的业务。没有新增默认产品依赖、Presenter/MCP入口、模型调用或审批权限。

领域记录资源/完整审阅就绪，以及通过包装器检查后实际调用原Confirm服务的返回/抛错。拒绝的人审尝试与成功尝试分开计时，时长是整个原确认调用而非纯命令。即时匹配原回执ID/fingerprint/status/code/changed/revision，只将ok、原回执匹配、非recovery/replay算作观测成功；Changed另计。通用领域面板或可信C#直接调用原服务不在范围内，报告固定标注该限制；0条观测确认不意味着没有业务提交。

新增 `AiWorkflowObservationEvidence` 重验脱离副本：模块沿原v2重放，统一运行/源码/负载/频率，核对唯一session/request、适配器调用/完成序号与计数、窗口/阶段/指纹；合法线程池提前执行不被错误拒绝。丢失造成未关联传输显式保留、不伪报完整。联合输出schema1只投影传输数值，排除code/异常类型等任意文本，默认64MiB且可收紧，沿原受检CreateNew/同句柄回读出口；没有任意JSON导入器或完整离线消费工具。内部一致性和文件SHA不是真实宿主、源码新鲜、人工审批或性能证明。

新增10组原AIContracts用例（标签E2B3仅是片标识）：owner/默认关闭、审阅/被拒确认/原回执/Undo、pending封存、迟到取消排空、连续请求、错配篡改、事件/请求预算、直接领域调用覆盖边界、数值导出禁止覆盖。保留原60秒和旧case体，全部待F；新用例覆盖场景链，动画资源二次人审仅接线编译，不能称动画/人工门禁通过。

当前捕获 `0709b712e0fa4af295dc3d676f3e44af`，5055文件，source `5D26C06915B7873ED64783D4AD9498083CCF76E2701E9F842ACD4A956FD1E42C`，manifest `1CA96C9B43D46F405A7A7410D2EC3957E04A4F2A7ADE91AD67904B6A84BE6EDB`；对03f7为6文件（3修改、3新增），3个原文件已独立备份。原六图离线依赖准备后，仅verify-contracts AI.Tests依赖图 Debug65.18秒/Release65.63秒编译均0警告/错误；不是全工程、实际Editor、原生/worker或包验收。

收尾 `out/verification/m8-7/e-workflow-observation-first/final.json` SHA `65F0C142059328593AB31C9A1D087129A317ACC2D48678374408C96559214AEB`：当前捕获/产物/依赖回执、旧case体与10组登记、原180清单/超时、前10片20个私有目录、正式151文件/journal核对通过；旧405目录与5170其他独立文件未变，已知签名Git差异单列。固定b69/共享Debug不切换，没有新整阶段冻结。

测试/文件导出/测量/模型/worker/PS5.1执行均0，未部署或提交推送。累计63个新case＋3个加强既有case待F。**下一步持续进程采样与受控测量/完整离线消费准备**，完成后匹配全套新输入和冻结/F，主体终态汇总后原PS5.1 24项/配置。实际apphost/人工/M8.6/E5-D/长稳门禁保留；E2-B2实现进度不是E/M8.7功能或性能验收。详细边界见[E计划第9节](M8_7_E_MEASUREMENT_PLAN.md)。以下为历史记录。

### 最新 E2-B2 首片：适配器历史与模块报告文件输出

补齐 `DeepSeekChatAdapter` 显式默认关闭的逐调用观测：原gate内登记和完成，不改变active/LastDiagnostic/审批/传输策略。最多4096条完成前缀，保留调用序号、完成序号、session/request和原安全诊断；重复/忙碌/关闭后拒绝也分别保留。封存pending不取消请求、不等IO，晚到结果不改写报告；窗口开启前已进入方法的并发调用记BoundaryOverlaps，不声称完整覆盖。传输报告仍是可变脱离副本，尚无受检联合编码导出，不能冒充完整编辑事务或性能通过。

新增 `ModuleObservationFiles.ExportNew`，只导出现有v2模块报告，先沿原Encode重放验证，再由可信 `DiagnosticEvidenceFiles.WriteNew` 写入。底层复用原WorkflowPhysicalPins固定现有父目录、拒绝非本地固定盘/别名/链接；受限`.ncobserve`单文件名，CreateNew独占写入、Flush(true)、同句柄长度/SHA回读及正常关闭后返回receipt。没有自动建目录、覆盖、删除失败文件、重试、换目录或Presenter/MCP入口；底层仅可信字节存储，不是schema/模型审批器，Services不增加AI/Modules依赖。同步导出须在测量后显式调用，不当逐帧采样器。

新增7组适配器历史＋4组文件输出用例，登记原AIContracts；原两个适配器专项分支也含前7组。**均未运行**，不新增CTest、不提高原60秒。原适配器既有用例体和主登记的其他case核对未变。当前捕获 `03f7171f84844187b97bdf5e10dfdd54`，5052文件，source `E158CE1EAE2BB38E381B4DC650006BC8CD90EA5F85F0458AA697D79186C31F01`，manifest `8AF068D7B5CCDBA03DD522BF0CB91EAAE724BF13F262DB3BF8EA5AE53DECDB5C`；对54c7为8文件（3修改、5新增），修改前3文件独立备份保留。

原六图离线依赖准备后，仅verify-contracts AI.Tests依赖图 Debug65.96秒/Release65.97秒编译，均0警告/错误；不是所有工程、Editor变体、原生/worker、包或实际客户端通过。收尾 `out/verification/m8-7/e-adapter-export-first/final.json` SHA `B1C5760F6C3FB42F8F3323491A5DB6866AB779B18344F084BA306AFEBB6E5AC5`：当前捕获/产物/回执、11组登记、原180清单与入口超时、前9片18个私有目录、正式151文件/journal核对通过；旧405目录与5170其他独立文件未变，已知签名Git差异单列，不称旧全冻结未变。固定b69/共享Debug未切换。

测试/文件导出/测量/模型/worker/PS5.1执行均0，未部署或提交推送。累计53个新case＋3个加强既有case待F。**下一步继续 E2-B2 领域/模块/适配器身份及阶段关联、受检联合证据导出**，持续进程采样仍待补；本片不是完整E2-B2/E/M8.7验收。完成准备后匹配全套输入和新冻结/F，主体终态及报告后原PS5.1 24项/配置，实际apphost/人工/M8.6/E5-D/长稳门禁独立保留。详细边界见[E计划第8节](M8_7_E_MEASUREMENT_PLAN.md)。以下为历史记录。

### 最新 E2-B1 模块会话被动观测与证据字节已实现并编译

原 `ModuleSession` 增加显式 `BeginObservation` / `SealObservation`，默认关闭、首次请求前一次性开启、owner 与时钟频率检查；不发起请求、不授予审批、不因封存报告停止业务。记录接纳/拒绝、owner 线程池交接、最终交付或关闭丢弃结果、取消意图及实际排空。尚未交付的 Proposal 可被原超时覆盖，不提前算成功终态；队列交接不是适配器实际开始时间。ModuleSession 范围没有领域提交或传输诊断，相关值不可用，不能伪报0或完整链路。

新增 `ModuleObservationEvidence` v2，仅生成/读取自有 UTF-8 字节，最大64MiB且调用者只能收紧；没有路径、外部流或文件写入。报告先复制原事件并重放，逐项核对计数、分位数和数值哈希；读取先做结构/标量/数组预算及重复字段检查，再拒绝未知字段、版本及非规范表示。SHA不是签名或宿主事实证明，声明的丢失计数无法从缺失事件重建；字节上限不代表堆峰值或0B保证。版本域升级v2，旧证据保留、不做兼容读取。

原 AIContracts 入口新增10组正反例（默认关闭/边界、真实交付排空、未领取回复超时、取消晚排空、溢出与观测故障隔离、待处理封存、字节边界、报告篡改、JSON负例、范围约束），**均未执行**，原60秒及逐例汇总不变。初版 `bdb8ec6967bd4e8487054d26c93ee398` 双配置编译成功后，静态检查发现 stopped+empty-ID 原拒绝路径无可关联身份，已保留原返回并标记观测不完整；初版捕获/编译证据和两个修改前副本全部保留，不把静态发现误记为功能测试失败。

最终捕获 `54c7090a3aa2457eabe1d67ba8efd451`，5047文件，source `B9B0EB0D27BAC75AB461CB3605128E4FCB1F38B52FE1CB200CF435FF6C95C1F8`，manifest `161B6BBDA7784891D3E189CBCA650095EF3F1DFCA332492DDDC7C6007A06079D`；对b620为6文件变化，对初版仅2文件。原六图离线依赖准备后，**仅 verify-contracts AI.Tests 依赖图** Debug65.88秒/Release66.16秒编译均0警告/错误；不是全工程、Editor、原生、worker或包验收。

收尾 `out/verification/m8-7/e-session-evidence-second/final.json` SHA `4446273A285C314DA48C5B2EA56D38F0ADF7FC878EBC9F1911AF560258FAC94B`：捕获与副本/私有产物和依赖回执、原两配置180清单与60秒入口、前8片16个私有目录、正式151文件/journal核对通过；旧405目录与5170其他独立文件未变，已知签名Git变化单独列出，不称旧整套冻结未变。固定b69/共享Debug没有切换，没有新整体验收冻结。

**测试/测量/模型/worker/PS5.1执行均为0，未部署或提交推送。** 累计42个新case＋3个加强既有case待F。下一步 **E2-B2 编辑器领域/适配器事件接线与可信 CreateNew 文件输出**，持续进程观察器仍待补。E2-B/E/M8.7尚未完成；随后匹配全套输入/F，主体终态及报告后原PS5.1 24项/配置，实际apphost/人工/M8.6/E5-D/长稳各自门禁仍保留。以下为历史记录。

### 最新 E2-A 被动数据合同已实现并编译

`Ncma.Modules` 新增无执行权限的 `ModuleObservationCapture` 与数值类型：固定运行身份、session/request关联、owner顺序、窗口遗留/接纳/拒绝/终态/提交/取消/排空分类，65536事件和4096请求硬上限。超限保留前缀、持续计丢失；无效观测使覆盖不完整；不完整吞吐及未测延迟为null。nearest-rank按结果分类，原始版本化数值哈希绑定上下文与窗口，不作为签名或业务授权。数据尚未接到真实宿主、没有导出或持续进程观察器，measurementReady仍为false。

沿原 `NcmaModuleTests.AIContracts` 增加11组独立用例，保留原60秒与逐例汇总。新增3个文件、仅修改原Program登记；新捕获 `b620b1a3cddf4da387e825dbb0e71918`，5045文件，source `2B9AC292CCF9F1BB86173AABA46DB7EA012F0ED0A762DDE27AAB9632A5DD75F7`、manifest `C34A87501B70AEAEDAAC4480C8FFC1E524D16B65D440778130EFBFCC483A0ED8`。原六图离线依赖准备后，仅 verify-contracts 完整图 Debug65.32秒/Release65.51秒编译，均0警告/错误；不是所有工程、Editor各变体、原生/worker或运行通过。

收尾证据 `out/verification/m8-7/e-observation-contract-first/final.json` SHA `A3B2B9D70749606F370645B700A69AF94A34BD0FCB8A0F3106145724E4154D14`：新捕获/私有产物、原180清单双配置中入口及超时、前六片12个私有目录、正式151文件/journal核对一致；旧405目录/5170其他独立文件未变，已知签名Git差异单独保留。没有新整体输入冻结或固定目录切换，仍b69/共享Debug。

**测试/测量/模型/worker/PS5.1执行均为0，未部署或提交推送。** 累计32个新case＋3个加强既有case待 F，不宣称功能验收。下一步 **E2-B 原请求生命周期被动接线、有界证据输出与报告一致性校验**；持续进程采样和人工预算确认仍待补。不能信任被调用者修改的报告对象或据SHA声称完整宿主事实。之后新同源全套输入/F，主体终态汇总后原PS5.1组；E1旧静态报告仍属旧文档版，捕获外Python校验器须另绑定完整工具闭包。以下为历史记录。

### 最新 E1 测量口径与采集缺口已整理

新增 [测量与长稳计划](M8_7_E_MEASUREMENT_PLAN.md)、[8 类指标映射](M8_7_E_METRICS.json) 和 [空白运行模板](M8_7_E_RECORD_TEMPLATE.json)。明确请求计数守恒、各阶段与状态分桶、nearest-rank 分位数、缺失/截尾、观察器开销和独立变体/轮次。60秒预热、600秒诊断、3轮、3600秒长稳与采样节奏均是待确认草案，不是运行/外发/自动批准许可。没有擅定性能/泄漏通过线，E5-D 代表性内容继续等待用户手动确认。

E1 静态检查核对 7 个 c7c3 捕获内托管源码锚点和 1 个捕获外 Python 完整流校验器；首次检查错把后者视为编译输入而失败，原脚本和失败保留。修正为单独源码哈希绑定后成功，执行前仍须准备 Python 运行时和导入依赖闭包，不能称完整工具链已冻结。旧两配置 180 清单仅核对名称/哈希，不是新 CTest 枚举。

证据 `out/verification/m8-7/e-measurement-plan-first/result.json`，SHA `5F41D11B33E17E1C7A16830343C19821A07067328CAA460C05E0529FC488ADEE`。重开 c7c3 的5042文件捕获，selected live source不变；前六片12个私有目录、正式151文件/journal核对未变。本片没有重核整个405目录历史冻结，没有新捕获/构建/采样/测试/worker/模型/PS5.1/部署/Git，所有会话终态。

**下一步 E2：补齐有界被动观察记录与统计合同及其拒绝用例**，不新增业务执行器、AI发送/批准或任意进程控制。LastDiagnostic 不是历史、四内存检查点不是连续长稳采样；这些缺口补齐前 measurementReady=false。E 准备完成后匹配当前完整输入/新冻结，再统一 F；F 主体终态汇总后原 PS5.1 24项/配置。E1 不代表性能、长稳或阶段验收，固定输入仍 b69/共享 Debug。以下为历史记录。

### 最新 D2：实际组合宿主与变体归因已准备

两个既有 `AiHostLifetimeTests`（CPU GUI、DX11）在组合开关下检查同一实际扩展实例由 Running 到 Closed、早于 AI 排空撤权/脱离、inner=0 与 outer=true，以及原 presenter 分开显示、无新帧/历史/字节变化；明确排空并成功关闭后 outer=false、transport 只释放一次。通过原 `NcmaExtensionTests.Editor.ai-extensions` / `--extension-editor-tests` 登记，原 60 秒、RUN_SERIAL、CaseHarness 全部逐例汇总不变。仅新增内部 owner-thread 只读观察点，无生命周期行为修改/故障注入工厂/新权限。

新捕获 `c7c3ac605a78490aa537df02a09c68bd`，5042 文件、相对 e696 精确 3 个源码文件修改；source `98E86FD611D9ECDF59410FCD0768F74E3865903CE6B8CA6FCB14C239EB761A91`。仅 **verify-ai-extensions** 服务测试＋实际 Editor/MCP/Gameplay 依赖图：Debug **83.14 秒**、Release **83.02 秒**，均 0 警告/错误；不是全工程、其他变体、原生/worker/包或运行通过。

11 组源码锚点、原历史 180 清单/配置中三个变体的命令和属性、当前组合产品依赖静态核对通过。最终记录 `out/verification/m8-7/d-combined-first/final.json` SHA `40BC29CF68C3163D582881380A18E97CD5B304E9D8B1CC92C1C71834190B8136`；两次报告脚本准备/解析失败原样保留，未运行到核验或测试，修正 Audit2 后终态成功。旧冻结目录/其他独立文件、前五片 10 个私有输出、正式 151 文件/journal 未变，已知 Git 差异单独保留。

**功能测试 0，尚未验收。** D2 是健康扩展关闭＋AI 保留的实际组合路径，不冒充实际组合宿主中的扩展关闭失败注入；该故障仍由 D07/D08 原更窄层级检查及人工项分别归因。累计 21 个新 case＋SDK 1/宿主 2 个加强既有 case 待运行；组合入口多执行两项宿主用例，不能重复计为两个全新 case。固定输入仍 b69/共享 Debug、无新整体冻结/部署/Git。

下一步 **E 有界测量与长稳准备**，再匹配全套当前输入/新冻结、统一 F；F 主体终态并汇总后原 PS5.1 24 项/配置。保留实际 apphost、人工、M8.6、E5-D 与长稳待验边界。[D 详细记录](M8_7_D_FAULT_PREPARATION.md)。以下为历史记录。

### 最新 D1 故障映射与迟到响应联合用例已准备

新增 [故障与生命周期准备](M8_7_D_FAULT_PREPARATION.md) 和 [10 类故障来源映射](M8_7_D_FAULT_MATRIX.json)，明确不同证据层级；新增 **2 个**原 AIContracts 联合用例，检查旧请求在取消/配置失效后迟到返回、明确排空，再建立新工作流，旧批准不得复用、只有新请求可产生一次原提交。原审批、队列、超时和注册入口不变。

新捕获 `e696ac95`，source `8B4781BD8A0AE0BD7755A0FEA8D007EAA5BA4F9309C94BE3BAE34E8FD4240D0D`；AI 合同完整依赖图 Debug **65.19 秒**、Release **65.31 秒**，均 **0 警告/0 错误**。本片不是全工程、Editor 可选图或原生验证，**功能测试未执行**。累计 C2 19＋D1 2＝21 个新 case，加 1 个加强既有 SDK case 均待运行。

最终记录 `out/verification/m8-7/d-late-reply-first/final.json`，SHA `0C67F1E7B1AB2A6122A2EF158C1331F16D905D0CA672743213AEDF6C67286C8F`。原冻结目录/其他独立文件、前三类及资源片私有产物和正式安装未变；已知 Git 工具差异单独记录。固定输入仍 b69，不能混入新 DLL 直接跑完整 F。无模型/worker/PS5.1/部署/提交推送。

下一步 **D2 组合生命周期覆盖与原清单/变体失败归因**，之后 E 测量准备；不把独立 AI 与扩展用例合起来说成组合关闭已通过。F 主体终态报告后再运行原 PS5.1 组，实际 apphost/人工/长稳/E5-D/M8.6 债务保留。D/M8.7 尚未完成或验收。以下为历史记录。

### 最新 C3 人工操作与记录方案已准备

新增 [实际编辑器与人工验收清单](M8_7_C3_MANUAL_ACCEPTANCE.md) 和 [空白记录模板](M8_7_C3_RECORD_TEMPLATE.json)，C 场景 JSON 已关联。8 项覆盖实际 apphost/桌面、会话模型配置与连接、场景、动画、UI、资源、扩展 SDK、取消关闭；均为 **待执行**。记录模板不是运行入口、审批票据或人工签字。

已按现有 UI 核对操作名称及边界：连接保存不联网；外发与写入分开；动画摘要/重命名默认模板不等于全图参数编辑；AI settings 不授予导入；源码审阅/交接不批准构建/安装。实际 UI 缺口如实登记，不能用直接服务调用代替鼠标操作后称人工通过。真实模型内容范围、次数/费用或本地预算、接收方和停止条件在执行前另行确认，不读取或记录密钥。

本次为 **文档与静态检查**：8 项空白状态、12 个界面文字锚点、来源链接通过核对；原 `ff9af4a6` 捕获 5041 文件及 live selected source 未变，前四片共 8 个私有编译目录、正式 151 文件/journal 未变。未重新核对整个历史 405 目录冻结，也未运行 CTest 或构建。记录 `out/verification/m8-7/c3-manual-first/result.json`，SHA `4EE4831C614C0ADFD9A0F8B9917BEF1C301DD12C3AFFEE32C35D3685EF129E4F`。

**测试/人工操作/worker/模型/PS5.1 执行均为 0；无部署或提交推送。** C2 的 19 个新 case＋1 个加强 SDK case 尚未运行；固定输入仍 b69/共享 Debug。下一步 **D 故障与生命周期**、再 E 测量准备；匹配新全套输入和冻结后统一 F，F 主体终态报告后原 PS5.1 24 项/配置。人工待办不妨碍主体终态汇总，但不能因此宣称完整验收通过。以下为历史记录。

### 最新 C2：资源设置→独立导入→运行时闭包用例已补，尚未执行

新增 **3 个**用例，登记在原完整 `NcmaEditorServiceTests`，不塞入短 metadata-only 入口，不新增 CTest，不调整原 180 秒超时。原用例保留。此次只修改 2 个测试文件并新增 `AiMetadataWorkflowTests.Joint.cs`，没有修改产品或审批策略。

1. 使用固定 FBX 测试资产和确定性 HTTP，原 AI 设置提案经外发确认、完整 review、最终提交，关联原 request/source/receipt；证明这一步**没有导入权限**。
2. 可信宿主单独授权精确 FBX 后，走原 worker/coordinator、candidate、prepared ticket 和原 import command；检查请求重放不重复写历史，派生 manifest 的 settings/source 与 AI 设置结果相同。
3. 逐项核对角色、网格、骨架、动画、材质槽的运行时资源闭包和哈希，执行运行时 NCP 编解码/重新打开；只含 metadata/派生数据、没有 FBX 的冷副本须得到相同包。两条原历史分别 Undo/Redo，文件字节准确，已保留运行时资源不因作者数据 Undo 而改变。
4. 单列导入授权缺失和 `source_changed` 拒绝用例；正向用例内另含 6 项控制：缺失派生代次、损坏代次、缺失外部依赖、损坏包、错误 project、错误 required kind。负例只改新建测试副本，不改源资产或历史产物。

`M87-JOINT-ASSET-SETTINGS/REJECT/CLOSURE` 输出必要身份和哈希，不输出密钥、原始响应或任意源码内容。最终结果仍以整个 harness case（包括关闭与 drain）的 PASS/FAIL 为准。运行时 NCP **不是完整 Player 发布包、实际 apphost、图形/性能或人工验收**。

新捕获 `ff9af4a6bad44bf78d2925f583e089fd`，**5041 文件**；source SHA `32F194DFDF586301FA17FAB5D460715B26924EAEC0E7CD4DFDD84491EA612BED`，manifest SHA `28AB0B471D04351C77359BA517626EB68EEF975D25FFDB79D5A8ECF118F3A785`。相对 aced 仅 3 个 Services.Tests 路径变化。原六图离线依赖准备后，仅编译 **AI-enabled Services.Tests＋实际 Editor/MCP/Gameplay 依赖图**：Debug **78.51 秒**、Release **79.33 秒**，均 **0 警告/0 错误**。不是全 69 项、其他变体、原生、worker、包或功能验收。worker 沿原完整入口路径取同配置输入，F 前必须从匹配新捕获准备；缺失即失败，不跳过。

最终记录 `out/verification/m8-7/c2-asset-first/final.json`，SHA `EA10FDB2FE061D4460BC96529252CA3FBB245A3984EB8C3F8869890CA87FEACE`。旧 405 冻结目录、5170 其他独立文件、前三片私有编译产物、正式 151 文件/journal 未变；已知签名 Git 工具差异单独保留，不能称旧冻结整体未变。新输出仅私有目录，固定输入仍 b69/共享 Debug。

累计 **19 个新增 case＋1 个加强既有 SDK case** 已准备、未执行。C2 确定性用例缺口准备完成，**不代表 C2/C/M8.7 功能验收通过**。下一步 **C3：实际编辑器、真实模型及人工确认操作清单与证据关联**，然后 D/E；匹配全套新输入并冻结后统一 F，F 主体终态/汇总之后原 PS5.1 24 项/配置（含 SDK）。本片测试/worker/模型/PS5.1 执行均为 0，无部署/提交推送；原 apphost/长稳/E5-D/M8.6 债务保留。以下为历史记录。

### C2 上一片：扩展 SDK 原回执关联已补，尚未执行

本片只增强 `WorkflowSdkChainTests.cs` 中原有真实 SDK 联合用例，**没有新增 case、CTest 入口、执行器、审批通道或产品账本**；累计新增 case 仍为场景/UI/动画的 **16 个**。本片增加的是 1 个既有 case 内的断言：

- 原审批绑定精确 job/session/assets、捕获配方；实际 manifest 绑定已审阅 bundle、源码及配方字节。
- 构建、契约测试、原生准备、宿主测试四类原回执逐项关联批准任务与原审计，检查输入集、任务/回执指纹、来源/资产、计划、配置、变体、配方、结论与完整性。
- 每类回执增加 **19 个错误关联控制**，只构造测试用副本，不改原审计；反复读取已封存回执必须使原审计序列化字节保持一致。
- 执行时额外保存最小身份/哈希关联 `joint-receipts.json`，不保存源码内容、密钥或模型输出；原 reviewed-marker、原始源码未变、场景/历史隔离和逆序关闭断言保留。该关联文件不替代整个 case（包括关闭阶段）的成功结果。

新捕获 `aced30b0ded24a628b1a1d42152c2c85`，**5040 文件**，source SHA `612045A3F3FD707F11403E1B7CE20C3EDA0B2B9C61130C9F63002AFC58F6D19B`，manifest SHA `38B39782C8158E9385AE6738A3F0A7C80A659D6805BBE7085E18BFA93E9992B0`。相对 bfd9 只改 **1 个测试文件**。原六图离线依赖准备后，仅编译 **extensions-enabled Services.Tests＋实际 Editor/MCP/Gameplay 完整依赖图**：Debug **82.77 秒**、Release **82.85 秒**，均 **0 警告/0 错误**。不是全 69 项、默认/AI 图、原生、发布包或 SDK 链路执行验收。

独立记录 `out/verification/m8-7/c2-extension-first/final.json`，SHA `C10DD0FDB50DB9BB38979CD955BB55987B66D68408F647194086F7FC9B62E6B7`。原 405 冻结目录、5170 个其他独立文件、前两片私有编译产物及正式 151 文件/journal 前后核验一致；已知外部签名 Git 工具变化继续单独登记，不称旧冻结整体未变。新产物仅在私有目录，固定 23 目录/1357 文件仍 b69、共享 Debug。

**功能测试 0；无真实模型/PS5.1/部署/提交推送。** 原 `NcmaWorkflowTests.SdkChain` 有间接 PS5.1 依赖，仍在 **F 主体终态和结果报告后**按原宿主执行；不以 F 主体先全绿为前提，也不绕过宿主策略。Editor/Player 包与模块闭包、安装/重启、人工、实际 apphost、长稳、E5-D、M8.6 等门禁仍独立待验收。

下一步 **C2 资源导入/依赖闭包联合路径**，再 C3/D/E；源码准备完成后匹配全套输入和新冻结，统一 F。C2/C/M8.7 尚未完成或验收。以下为历史记录。

### C2 上一片：动画参数连线到播放/预览联合用例已补，尚未执行

本片新增 **8 个动画 case**：7 个通过原 `AiAnimationWorkflowTests.Cases` 登记，1 个 DX11 用例只登记到已有完整 `NcmaEditorServiceTests`，使用该入口显式传入的插件目录。没有新增 CTest 入口或修改超时，原用例保留。

- 向已有图添加 Float 参数并连到 Clip 的 speed 输入，使用确定性 HTTP 回包、实际可选工作区和原读取审批。精确外发字节、完整资源清单/代次哈希、资源审批、最终 Commit 分开检查；未批准不写文件/历史，不改变原图的播放结果。
- 提交后从持久化图及实际保留的骨架/Clip 载荷准备原动画程序，检查默认速度 2、取消一个 prepared quantum 后时间不推进、连续提交的时钟结果，以及 Undo 回到速度 1、Redo 恢复速度 2。关联请求/source、原资源 proposal、领域 review 和真实原提交回执，不制造第二套权限或账本。
- 完整入口的 DX11 用例走同一 AI→审批→提交链，再调用原 `EditorAnimationGraphPreview`、PoseKernel 与 Renderer：16 帧独立预览、参数/Clip 时间、首尾像素变化、零验证告警/错误、关闭后释放资源。它是**待运行的独立预览用例**，不是已通过的 GPU 测试，不代表 World Play、编辑器面板合成、人审、目标机性能或真实模型质量。原 GPU 画布合成测试继续单独保留。
- 拒绝用例覆盖取消、撤销读取许可、外部文件修改，以及错误输出端口、类型不匹配、循环；结构负例要求原验证器对应 `output_pin` / `link_type` / `pose_cycle`，不能把其他异常当作预期通过。

新捕获 `bfd9c063d204406787a7a41bac71a332`，**5040 文件**，source SHA `B4ECD32E1FD936D30AE4B61BEE0364A47DC4CFE8C3CEBA921AC285B5B3369C42`，manifest SHA `2FADE305E5037C84149BDC23888A3164A78BD9D19C58F3863AED42E0F69C9C57`。相对上一片 aaaa 仅 4 个 Services.Tests 文件变化（2 改/2 新），之前 5 个 AI.Tests 文件保留。本片原六图离线依赖准备后，仅编译 **AI-enabled Services.Tests＋实际 Editor/MCP/Gameplay 完整依赖图**，Debug **78.67 秒**、Release **78.92 秒**，均 **0 警告/0 错误**；不是默认全 69 工程、其他可选图、原生或包验收。

最终审计 `out/verification/m8-7/c2-animation-first/final.json`，SHA `07428A7420C73CFB451E17E05E3AE524866C359BEAFBE08E8EC5B4F449FED1A0`。旧 405 目录、除已记录 Git 差异外的 5170 独立文件、上一片私有编译产物、正式 151 文件/journal 均未变。固定输入仍 b69/共享 Debug，未激活新输出；**不能混用本片编译与旧包/回执作完整验收**。

累计 **16 个 C2 新 case（场景/UI 8＋动画 8）已实现、未执行**。本片功能测试/模型调用/PS5.1 均为 0，无部署/提交推送。下一步 **C2 资源导入/依赖闭包、扩展回执关联**，然后 C3/D/E，准备匹配输入和新冻结后 F 统一运行；F 主体执行终态并汇总后再执行原 PS5.1 专项。C2/C/M8.7 尚未完成或验收。

### C2 上一片：场景组件与 UI 布局/样式用例已补，尚未执行

本片只补联合用例，不修改产品服务、权限或命令执行器。通过原 `NcmaModuleTests.AIContracts` 内的 `All()` 登记 **8 个 case**，没有新增 CTest 入口、修改超时或删除原用例：

- 场景：精确选中对象的 Transform 位置/缩放修改，审批前字节/历史不变，单次提交、重复确认拒绝、完整 Undo/Redo；另有取消、选择变化、用户先修改三个拒绝用例。
- UI：修改已有矩形节点的布局、颜色、透明度与圆角，保持画布和节点身份；持久化文件与工作区一致，原 Undo/Redo 精确恢复，原 CPU 布局引擎在 scale 1/2 下核对几何与透明度；另有取消、撤权、外部文件修改三个拒绝用例。**CPU 几何不是 GPU 像素/实际编辑器人工视觉验收。**
- 回执：关联实际请求/source、原 review 的 ProposalId/Fingerprint 和原提交回执全部字段，输出 diff/wire/before/after 哈希；提交前拒绝断言无回执。只增加测试输出，不增加产品账本，不输出原始响应、密钥或本地配置。`M87-JOINT` 是分步证据，含清理的最终用例结果仍以原 CaseHarness 为准。

旧输入完整保留，新捕获 `aaaa86257c3548609ce44e255ef0eeea`，5038 文件，source SHA `3B0463178825EFF30BF8E6D8F571AF9647E969729ABF835FFDE9184F46F463A8`，manifest SHA `FEDB7BAA87526AD2393B47264289905656F85EBF952A14FD8AE29B07C4F24427`；相对 b69 仅 **5 个 AI.Tests 文件变化（2 改、3 新）**。原离线 verification 六图依赖准备完成，但本片只编译 **AIContracts 完整依赖图**：Debug 63.52 秒、Release 63.35 秒，均 0 警告/0 错误。不是全 69 工程、其他可选图、原生、包或功能验收。

正式 151 文件、journal、原 405 个冻结目录未变；固定 23 目录/1357 文件仍属 **b69**，新编译输出仅在私有目录，未激活。后续全阶段测试必须准备匹配新源码的成套输入和新冻结，不能改签 B3 旧回执。

首次预检在修改/备份测试源码前失败：冻结清单中的应用附带 `git.exe` 从 `54BDA91C…` 变为 `20D0DDE3…`，当前版本 2.56.0.windows.4、签名有效，更新原因未归因。失败保留在 `c2-scene-ui-first`；第二轮显式登记这一个外部工具差异，其他 5170 个独立文件及全部旧目录在前后审计均未变。**旧冻结整体不再相同**，不能称全快照未变；未重写历史记录，也未修改工具或安全策略。捕获/准备使用已安装的 Program Files Git 2.49.0（`FEC691D8…`），不是用新工具伪造旧运行证据。

最终证据 `out/verification/m8-7/c2-scene-ui-second/final.json`，SHA `DAB096A3F88D27567D3BE50A9BA86E2B875B984201EFC74AF5A41881091FB7EB`。本片功能测试 **0**、模型调用 **0**、无部署/提交/推送；C2/C/M8.7 尚未完成或验收。下一步继续 **C2 动画参数/连线到播放联合路径、资源依赖闭包及扩展回执关联**，然后 C3/D/E；统一 F 测试，F 主体终态与报告落盘后再开始原 24 项/配置 PS5.1 专项。以下是历史片段，不覆盖本段。

### 最新顺序与 C1 交付：F 主体执行结束后开始 PS5.1

用户明确要求“F 执行完成后开始 PS5.1 测试”。执行顺序为 **C–E 准备 → F 主体统一测试及结果汇总 → 原 Windows PowerShell 5.1 专项 → 完整门禁汇总**。F 主体原清单为每配置 156 项，另 24 项原 PS5.1 入口（含间接依赖）随后执行；不是等 180 项全通过才启动这 24 项，也不把 F 主体结束写成整阶段验收通过。F 主体的失败保留并登记，不能通过调整分母或删项宣称通过。

届时先核对原 PS5.1 宿主是否具备受支持环境；不修改执行策略、不使用 Bypass、不改用 PS7。若仍受阻，记录该组未执行及具体阻断，保留 F 主体结果，并请求用户提供受支持环境。PlayStation 5 主机平台延期与此无关。

本次完成 **C1 五领域验收映射**，计划文件 [M8_7_C_SCENARIOS.json](M8_7_C_SCENARIOS.json) 是验收计划，不是新执行器、命令注册表或审批凭据。只读检查将五类场景绑定到 11 个现有源码用例、每配置 7 个既有 CTest 入口和 10 处调用登记，记录原命令/属性与源码哈希；未新增 CTest 项，原 180 项不变。

| 领域 | 已有自动用例入口 | 本次明确保留的联合验收缺口 |
| --- | --- | --- |
| 场景 | `NcmaModuleTests.AIContracts` | 现有正例主要为对象重命名；仍需选定对象组件/属性修改及实际编辑器确认 |
| 动画 | `NcmaEditorServiceTests` | AI 图重命名与既有 GPU 画布测试是两条路径，不等于参数/连线修改到实际播放的完整证明 |
| UI | `NcmaModuleTests.AIContracts`、`NcmaWorkflowTests.DomainSourceHost` | 名称/持久化字节断言不证明布局样式视觉效果，需单独画布视口验收 |
| 资源 | `NcmaEditorServiceTests` | metadata settings 的提交与 Undo/Redo 不等于导入/重建后的依赖闭包通过 |
| 托管扩展 | `NcmaExtensionTests.Editor.extensions` / `.ai-extensions`、`NcmaWorkflowTests.ContinuationHost`、`NcmaWorkflowTests.SdkChain` | 前两类不替代真实 SDK 链；`SdkChain` 属间接 PS5.1 依赖，明确在 F 主体后执行 |

回执关联采用现有 `DomainObservationReview` / `DomainProposalReview` / `DomainProposalReceipt` 及 `WorkflowAudit`，保存实际 request/source/review/receipt 的 ID、指纹和执行 revision，不按显示名称拼接。`DomainProposalReceipt` 是 64 条有界队列，不是完整持久审计；最终提交前的拒绝/取消可能没有回执，须记录真实缺席与字节/历史不变，不能伪造成功回执。原工作流的 JobId/CaptureId、任务/回执指纹和审批范围继续使用现有结构，不新增产品日志系统或赋予模型审批能力。

证据：`out/verification/m8-7/c1-scenario-map-first/result.json`，SHA `165ABBBA1D8D63965798CBC8E45E9AB11C71C04E7C251FF1B08581D7A709DFEC`；映射 `scenario-bindings.json` SHA `E7945524F0B3AFA50CE0EFDE370431765976B367ECEF536A83A3C8E238DF88E4`。重新核对 B3 的 405 目录/5,171 独立文件快照，未变化；选定源码仍为 `32DEBF68`。这是静态来源检查，不是已执行用例或覆盖率证明。

下一步 **C2**：按上述缺口补齐最小确定性联合用例与回执断言，保留全部原断言；若修改测试/产品源码，先保留旧输入，重新捕获和准备，不把 B3 冻结改签成新源码。C3 整理人工实际视口、真实模型及 SDK 结果关联步骤；随后 D/E 准备，测试统一到 F。没有本次编译、功能测试、模型调用、配置/密钥读取、EXE 更新、提交或推送，C 阶段尚未验收。

### 最新 B3 收尾：输入切换、离线依赖与准备快照完成

仍使用 `b69a0d52` 同一捕获，未修改产品或测试源码。本次完成剩余 10 个 canonical 运行目录、410 文件的独立备份、预检和逐目录切换；旧目录与独立备份均保留，不删除、不强制覆盖、不重试失败移动。上轮 13 个目录及两个 AI Contracts 可选构建保持不变；八个索引未改动，正式 151 文件与 journal 未变。

新的权威活动输入为 `out/verification/m8-7/b3-canonical-cutover-second/active-inputs.json`，SHA `3CC676BAB282C2805F7D844C8549324826441408F5DAE307B75A890637C59EBF`，共 **23 个目录、1357 文件**。原两批 947+410 文件的独立备份及保留原目录再次核验通过。共享支持仍为 **Debug**，Release 尚未切换，不可直接混跑完整 Release 测试。

| 准备项 | 实际结果 | 未包含 |
| --- | --- | --- |
| CodeGen 离线还原 | 双配置共 10 次 restore-only；17 个复制的本地包、34 份还原包；128 个实际项目绑定均指向指定本地源/配置/私有缓存，每配置缓存 1177 文件 | 无网络下载；未执行原编译/发布测试用例 |
| 原测试分组 | 各 180 = 2 CodeGen + 154 其他待测 + 24 PS5.1 延后；原命令、属性和间接宿主来源核验不变 | 延后不是通过；未替换宿主或放宽超时 |
| Python 输入 | 原 worker 输入/环境回执只读重开通过，并核对三个相关入口的参数绑定 | 未执行 Python worker、模型或测试 |
| 启动计划 | 两配置计划及离线环境已绑定；`launchEnabled=false` | 未授权或启动测试进程 |
| 准备输入快照 | 405 目录、19,286 个目录内文件、5,171 个独立文件，独立重读核验未变化 | 非功能、实际 apphost、人工、模型或性能验收；不是密闭工具链证明 |

快照：`out/verification/m8-7/b3-input-freeze-first/freeze.json`，SHA `70FEC6A4B2D8C73A2F058A9A155B28F3C1613550DCFC96725F188DB88E5AFD2F`。独立审计：同目录 `audit.json`，SHA `1A22E041FDE698FA4EE4F2136757836FF35E9C68C9C5E844DB6309AC07858413`。范围包含当前捕获、23 活动树、双配置产品、SDK 产物、新旧备份、本地 CodeGen 缓存、Python 输入与选定工具；已安装 SDK 仍作为受信工具链，不扩大为 hermetic 保证。

首份 canonical 准备脚本因生成字符串拼接导致第 75 行语法错误，在执行前被解析检查拒绝，复制/移动均为 0；保留于 `b3-canonical-cutover-first`，修正版 `second` 才实际运行。一次脚本创建的权限审批超时未执行，确认目标不存在后按工具允许重试一次成功。以上不是产品或功能测试失败，原证据保留。

**B3 输入准备已收尾，不等于 B/M8.7 功能验收。** 下一步推进 C–E 的五领域任务、故障矩阵与测量场景准备，复用原服务/命令/审批边界。功能测试仍统一在 F：届时重验快照，任何源码/输入变化必须重新捕获或冻结；绑定受检启动器，Release 共享支持按备份流程切换。PS5.1 原 24 项/配置继续延后，实际 apphost、人工、长稳、目标机 E5-D 与 M8.6 未关闭项分别保留。所有本次会话已结束，`testsExecuted=0`、`stageAccepted=false`；没有部署、提交、推送、配置/密钥读取或安全策略修改。

### B3 上轮进展：固定验证输入已切换，运行依赖准备尚未结束

仍使用 `b69a0d52` / source `32DEBF68` / manifest `2E64BBF1`；本次没有修改产品或测试源码，也没有执行功能测试。汇总记录：`out/verification/m8-7/b3-progress-second/result.json`，SHA `C8F17D98EA07264969C6A745182EE18DF0BB9F2CAF452BAB94AC6A069AC8ECA0`。

| 范围 | 实际结果 | 结论边界 |
| --- | --- | --- |
| 当前捕获的原生测试构建 | Debug/Release 各 322 步，111.33/101.31 秒，未检出编译警告/错误 | 仅构建，不是运行通过 |
| 原 `ctest -N` / JSON 清单 | 各 180 项，原名称、超时、依赖和串行约束不变；SDK 声明已绑定，无未配置项 | 未运行测试，PS5.1 原 24 项/配置仍延后 |
| 固定输入切换 | 精确 13 个目录、947 个新文件；947 个旧文件同时保留原目录与独立备份 | 不删除，不强制覆盖；逐目录切换，不是跨目录原子事务 |
| 原 AI/扩展回执 | 双配置各六个验证图编译，0 警告/0 错误；原索引备份核验通过 | 原生产者重建并签发，不将旧回执改签为新源码 |
| 新测试运行目录 | 138 默认 + 10 可选树，共 3790 文件；另补捕获副本内 Debug 支持两树、182 文件 | 全部新建，正式安装未替换 |
| 托管依赖预检 | 每配置 141 个直接托管入口存在；58 个独立宿主声明依赖 0 缺件、0 未解析 RID；原四组包上下文校验通过 | 文件/元数据核验，不证明实际宿主启动或完整运行闭包 |

当前权威活动输入：`out/verification/m8-7/b3-input-cutover-first/active-inputs.json`，SHA `4A1A4699BEC220BDF0D9F4DC1B1AFAF8BA77160897C7E261ED0E781E9D788043`。共享配置已经由旧 Release 切换为 **Debug**；捕获副本的 `out/bin` 与 `out/managed` 也为 Debug。不能直接运行依赖共享 Release 支持的测试。B2 六个包索引未再改动；本次原工具更新两个 AI 编译索引并生成两个扩展回执代次，旧索引、备份和历史失败均保留。

**B3 尚未完成：** `b3-progress-second/canonical-runtime-inventory.json` 列出工作区中 10 个仍属旧代次的运行目录、410 个文件：Editor.App、Player.App、Player.Tests、Gameplay.UiFixture、Shader.Cook 的 Debug/Release。它们与当前构建不一致，尚未覆盖。另两个 `Ncma.AI.Tests` 目录已通过本次原可选构建回执切换；不得用默认构建替代它们。

下一步先独立备份、核验并可恢复地切换上述精确 10 个目录，再核对 CodeGen 离线还原、Python 与间接运行依赖、原 PS5.1 延后名单，生成完整新冻结后才进入统一测试。旧完整冻结已有明确的输入/索引变化，不能声称未变或覆盖原记录。

收尾审计重查新运行目录、活动输入、947 文件的两份旧证据、正式 151 文件与 journal；正式 EXE `379AA113...`、journal `F5BDC19D...` 未变，全部会话结束。第一次收尾脚本误把仅适用于 `out` 的路径校验器用于 `managed`，在只读检查时拒绝，原脚本及失败记录保留于 `b3-progress-first`；第二份报告使用精确 managed 边界和原文件校验，不修改产品校验器。没有实际 Editor/Player/Python/模型执行、部署、提交或推送。`runtimeReady=false`、`testsExecuted=0`、`stageAccepted=false`；M8.6、实际 apphost、人工、长稳与 E5-D 仍独立待验收。

### B2 历史交付：原生、产品候选、独立模块与 SDK 准备完成

同一捕获 `b69a0d52`，本次未修改产品源码。总记录 `out/verification/m8-7/b2-completion-first/result.json`，SHA `88305ACF2BB2274AC0B2F37F76490922CFA7152FC87649BE93377C3B0E3442BD`。

| 准备范围 | Debug | Release |
| --- | --- | --- |
| 离线发布/模块依赖 | 原 10+2 个任务，回执重新打开核验通过 | 原 10+2 个任务，回执重新打开核验通过 |
| 模块验证图编译 | 0 警告/0 错误 | 0 警告/0 错误 |
| 原生构建及 13 产品候选生成 | 265 步完成、485.19 秒，未检出编译警告/错误 | 265 步完成、480.47 秒，未检出编译警告/错误 |
| 独立模块客户端 | 22 文件，4.39 秒 | 22 文件，4.12 秒 |
| SDK 声明准备 | 0 警告/0 错误，95.92 秒 | 0 警告/0 错误，88.49 秒 |

耗时仅是本次构建/准备记录，不是引擎性能测量。SDK 复用原入口生成 16 字段声明并只读观察工具链、MCP 与 harness；各自 1297 个依赖文件的回执重开通过，未执行 SDK 链功能测试或 PS5.1。

实际候选代次：

- 产品 Debug：`out/package/m2-7/Debug/cdb908a78edb415ca5212d59df2af0f0`；Release：`out/package/m2-7/Release/87b87166c46f4e49b109308a9d127948`。
- 模块 Debug：`out/package/modules/Debug/44b00fa8d51e4951a91b7ce72cdaf1b7/client`；Release：`out/package/modules/Release/7cee904c9e7f4fef9fe67fdb80fdc247/client`。
- SDK Debug：`out/verification/m8-6/sdk-inputs/9cc29651252d4d9584e5668ad5d4834b/inputs.json`；Release：`out/verification/m8-6/sdk-inputs/bd96d41826664658bc26845ddca6bdd2/inputs.json`。沿用原工具目录，不代表恢复 M8.6 测试。

生产者执行前独立备份精确六个包索引；执行后核对原工具的备份及独立备份。默认索引沿用原先校验/备份后写入，AI 与模块索引沿用 `File.Replace`；六文件更新不是跨文件原子事务，不能扩大恢复保证。新 26 候选共 2778 文件、新两个模块共 44 文件已逐集合/大小/SHA 核验，旧 26 候选及两个模块字节保留。源码捕获、离线依赖回执、B1 私有 12 树和 SDK 三类输出/配置均再次核验。

正式 151 文件、EXE `379AA113...`、journal `F5BDC19D...` 未变，14 个固定验证树及 12 个 canonical 托管运行目录未变。所有构建/准备会话已结束；本次没有功能测试、实际 Editor/Player/Python/模型运行、部署或 Git 发布。

**当前不能直接运行整套 CTest：** 六个包索引已更新到 `b69a0d52`，但固定 harness 与原 AI 编译回执仍属 `fe553502`，共享输入仍为 Release。原完整冻结具有预期的六索引差异，不再宣称整份旧冻结“未变”，不得回写旧冻结或把旧回执重签为新源码。原包构建关闭 tests，没有生成新的原生测试夹具；本次也没有重新登记完整 180 项清单。

**下一步 B3：** 准备当前捕获对应的测试清单/原生夹具，列出精确切换目标并按原备份校验流程成套切换固定验证输入和编译回执，补齐运行闭包后才进入阶段末统一测试。PS5.1 的 24 项/配置继续后置；真实 apphost、人工、长稳和 E5-D 门禁保持独立。B2 准备完成不等于整个 B/M8.7 通过，`runtimeReady=false`、`testsExecuted=0`、`stageAccepted=false`。

### 用户最新顺序：PS5.1 延后，M8.7 继续

用户明确要求“PS5.1 测试延后，开始 M8.7 阶段”，覆盖此前暂停 M8.7 的顺序安排。A 准备成果沿用，当前推进 **B1：同源码离线依赖与可选图编译**；PS5.1 原 24 项/配置保持后置、未通过，不再重复已知策略拒绝的入口。M8.6 的旧失败、实际 Editor 环境与其他人工/性能门禁保留，但不阻止无关的独立编译与实现准备。

本次 Debug/Release 各六个可选验证图从 `b69a0d52` 捕获编译，复用原离线依赖工具，缺缓存即失败，不联网补包。输出位于新的私有目录；既有固定测试树、原包索引、正式安装、用户配置与密钥不改。B1 编译不包含 B 的原生/发布包/搬移验收，正式功能测试仍在阶段末统一执行。

### B1 实际交付：双配置离线准备与可选图编译完成

证据：`out/verification/m8-7/b-optional-first/final.json`，SHA `E78CA2EFDBEAD65D275B8900A325E40D467958D89947A5304B6996B7F54911D7`。

| 范围 | Debug | Release |
| --- | --- | --- |
| 原六个可选验证图：AI Services、AI Contracts、Extensions Services/Player、AI+Extensions Services/Player | 6/6 编译完成，0 警告/0 错误 | 6/6 编译完成，0 警告/0 错误 |
| 编译耗时合计（不含准备/审计，不是性能基准） | 371.30 秒 | 369.62 秒 |
| 私有离线依赖代次 | `cc91aff2ea71462b9b780268f2aff242` | `34ec85f470e14acaaa7cf05625364db8` |
| 编译结果记录 SHA | `25C5D104519FE293866374E380F457664105C7593F98BB617B5771B519084F0E` | `2FC2E1DF823FDE92F9D9BC6FF36EF847F131B45F0A299280C7191573C0A43F93` |

原工具将依赖产物写入 `out/verification/m8-6/verification-dependencies/<代次>`；这是沿用工具的存储位置，不是重用旧输入或把 M8.7 改名成 M8.6。每次准备只用本地缓存，回执绑定同一 `b69a0d52` 捕获和配置；构建使用 `--no-restore`，未启动 PS5.1。

收尾核验重开捕获与两套依赖回执，核对 12 个私有 harness 文件集合；另检查 10 个实际编译 Editor/Player 依赖视图：AI 仅在启用的 Editor 中出现，Player 不带入 AI/测试/Grpc 传输依赖，扩展按开关出现。两配置各两个扩展构建上下文中，Editor/Player 的 `Ncma.Extensions.dll` 与 `Ncma.Extensions.Hosting.dll` 均逐 SHA 相等。这是静态托管依赖检查，不是原生/资源完整运行闭包或真实 EXE 搬移通过。

正式 151 文件集合/字节、journal、14 个固定输入树及八个旧包索引均未变；全部会话已结束。未激活私有输入、未重签原 AI 编译索引、未运行功能测试、未生成新发布包、未部署/提交/推送/调用模型。`testsExecuted=0`、`stageAccepted=false`。

**下一步 B2：** 为同一捕获准备原生、产品发布、独立模块和 SDK 输入；再按原备份核验流程切换完整固定输入，阶段末统一测试。B1 完成不代表整个 B 或 M8.7 验收。

用户要求开始下一阶段，本阶段先启动 **M8.7-A 验收准备**。这不代表 M8.6 已完成，也不把 Find 专项通过替代整阶段验收。B–E 可先整理清单/用例，真实执行受下列前置条件约束；各小阶段的正式功能测试统一在 F 执行，失败保留、修复后按受影响范围重新冻结与验证。

- 当前源码捕获：`b69a0d5224a4412d932e680143b41910`，5035 个输入，source SHA `32DEBF68137E573953ACAE725531721B4B434393657E61D44F59815934FD1BD6`。
- 已有证据仅覆盖该捕获的全 69 托管默认项目 Debug/Release 编译零警告/错误，以及完整 Headless 专项：各主组 90/90、其他 ECS 118/118。原 4096 次 Find、worker 0/2、三回调、严格 0B 与受检区域边界不变。
- 固定整阶段输入仍为 `fe553502`，共享输入当前为 Release。旧完整测试 Debug 144/12、Release 145/11 的结果不改判，不与新专项拼成一个完整通过率。
- Windows PowerShell 5.1 的 24 项/配置在 M8.6 主体工作完成后补测；原清单仍为 180 项/配置，不删除、不换宿主、不放宽超时，不把暂缓算通过。
- 实际 Editor apphost 环境、历史人工验收、原子保存问题及 E5-D 目标性能分别保留。PlayStation 5 主机平台延期与 PowerShell 5.1 无关。

唯一执行工作区为 `F:\NcmaEngineRelease\source`；`F:\NcmaEngine` 只作证据归档。正式安装、用户配置和密钥不在本次修改范围。

## 2. 目标与不做的事情

目标是证明现有 C# 主引擎、ECS/GameObject 门面、C++ 性能插件、可选 AI/Python 专长模块与人工审批工作流能组合交付，而不是继续创建新的通用执行器。

- 复用 `CompositionRegistry`、现有生命周期、领域命令、workflow ledger、精确审批与原构建/部署入口。
- 默认 Editor/Player 无 Python、推理服务或可选扩展的隐性依赖。Player 的可选运行适配按真实产品依赖验收，不因与 AI Editor 同包生成就声称有 AI 能力。
- 本版只验 Windows/DX11；Null 后端用于无图形验证。Vulkan/OpenGL 和其他发布平台不在当前实现范围。
- 不增加任意 shell、DLL 热加载、模型自批、静默下载、自动安装或新兼容旧格式。
- 只从受检捕获副本编译；独立离线依赖准备与原生准备先有匹配回执，再构建。源码有变化必须新捕获，不重签旧结果。

## 3. 分阶段交付

| 小阶段 | 工作与交付 | 进入/完成条件 |
| --- | --- | --- |
| A 验收准备 | 当前源码、产品矩阵、原测试清单、历史证据和缺口表；领域任务设计 | 准备完成与执行就绪分开记；不能用文档关闭 M8.6 |
| B 产品与依赖闭包 | 同源码默认/AI/扩展/组合产品及独立模块客户端；搬移和缺件/篡改用例 | 每配置精确包集合、编译/依赖回执及清单哈希一致；默认路径无可选依赖 |
| C 五类领域闭环 | 场景、动画、UI、资源、托管扩展各一个真实任务的完整验收步骤 | 原始人工审批、原命令与最终产物可追踪；取消/拒绝不产生未授权修改 |
| D 生命周期与失败矩阵 | worker/服务故障、背压、撤销、停止、保留资源与显式恢复 | 不强退非本任务进程，不以隐藏残留或自动重试制造通过 |
| E 测量与长稳准备 | 指标、测量边界、负载/时长确认、日志与终止条件 | 真实负载经确认；性能、功能、人工与目标机结论分开 |
| F 统一测试与交付 | 完整双配置、领域人工验收、长稳/性能、审计；另行授权部署/Git | 未执行/失败/阻断逐项保留，全部必需门禁有同源证据才验收 |

### A：先消除证据代次混淆

1. 重开当前捕获并校验输入字节/配方，核对 live selected source；不以 Git 提交号替代源码内容标识。
2. 核验已有包索引身份，列出每个产品配置、来源与是否匹配当前捕获。旧包仍可作历史证据，不是当前源码交付物。
3. 原 `ctest -N` 与 JSON 清单一起核对：180 项、无重复/禁用/遗漏；原分类器检查工作流宿主、参数和超时。仅点数不证明依赖齐备或测试通过。
4. 以原报告文件集合核对正式安装及 journal，确认准备阶段无修改；输出独立新证据，旧结果不覆盖。
5. 对 M8.6 未关闭项逐项标记：源码匹配产物、PS5.1 后置测试、实际 apphost 环境、人工任务/历史缺陷。不要把所有失败归为环境问题。

### B：产品矩阵与受检搬移

既有每配置 13 个候选产品为基线，不新造排列组合：

| 产品组 | 每配置产品 |
| --- | --- |
| 默认 | Editor；Player Null、DX11、Null+Physics、DX11+Physics（5） |
| AI | Editor AI（1） |
| 扩展 | Editor Extensions；上述四种 Player Extensions（5） |
| AI+扩展构建上下文 | Editor AI Extensions；Player Null AI Extensions 同上下文伙伴（2，后者不含 AI 运行依赖） |
| 独立模块 | 每配置独立 gRPC 客户端包，另行核对 worker/离线 Python 依赖闭包，不计入上述 13 |

沿用 `CapturedBuild.ps1`、`Prepare-PackageDependencies.ps1`、`Prepare-AiVerification.ps1`、`Prepare-ExtensionVerification.ps1`、`Package-M2_7.ps1` 与 `Package-ModuleClient.ps1`。先审查原入口的精确写入目标，使用原备份/核验流程切换固定输入，不直接覆盖。

搬移验证必须执行实际 apphost，不用 `dotnet DLL` 替代失败的 EXE。保留默认产品已知 Python 路径锁定、独立客户端、缺件/篡改/额外文件/错误版本拒绝、插件与资源闭包断言。当前候选为 framework-dependent，不宣称干净目标机或 self-contained 已验收。

### C：真实任务与回执

以下是待执行验收场景，不是已完成的真实模型结果。先用确定性适配器验证协议与命令边界，真实服务/外发/费用与本地模型执行必须按现有连接和人工授权。

| 领域 | 任务 | 必须保留的判据 |
| --- | --- | --- |
| 场景 | 对选定 Entity/GameObject 提出受限组件/属性修改 | 精确对象、revision、完整 diff；提交一次、Undo/Redo；切换文档或用户先修改后旧提案失效 |
| 动画 | 对已有动画图提出合法参数/连线调整 | 端口/类型/循环校验；未审批不改变播放图；实际播放与取消路径分别记录 |
| UI | 修改既有画布布局/样式 | 目标画布与节点集合精确；视口结果、历史与持久化一致；不恢复类 Figma 参考 |
| 资源 | 检查引用/导入依赖并提出明确修复 | 缺失/修改源资产与过期代次拒绝；产物依赖闭包；不让模型任意读盘或下载资产 |
| 托管扩展 | 已有示例组件的捕获审查→离线准备→编译→测试 | 编译输入就是批准的捕获字节；Editor/Player 模块一致；测试通过也不自动安装/重启 |

每条关联 request/session/document/revision、审批及命令 ID、before/after、构建输入/产物哈希、测试回执、终态。只采集必要元数据，不记录密钥、未授权提示内容或原始响应体。跨领域操作明确部分成功边界，不伪装成一个可全回滚事务。

### D：故障与生命周期

- 验证无服务、慢响应、超时、取消、队列饱和、重复请求、过期会话、撤销许可、响应格式错误；保留原预算与拒绝码。
- 分别覆盖请求前/执行中/完成后取消、文档切换、关闭过程中回调，验证旧结果不可应用。
- 真实 worker 启动失败、异常退出、人工允许的重新启动与资源关闭，记录精确所属进程；卡住不自动扩大终止权限。
- 外层持有与扩展持有分开呈现；关闭失败不得释放依赖、隐藏残留或自动重复关闭。只在明确批准后按原恢复入口继续。
- 记录每个失败的归属和已到达用例；harness 跑完全部安全可运行 case 汇总，依赖缺失明确未执行，不跳过后宣布全绿。

### E：性能与长稳

在批准的同一负载、机器、配置、预热条件下分别测无模块基线、本地 worker、远程适配器。指标包括：请求/响应字节、成功/失败/取消数、队列深度/背压、端到端 p50/p95/p99、吞吐、CPU、托管分配、内存/句柄/线程与游戏帧影响。

端到端包含排队/序列化/传输/模型/应用；驱动轮询不可命名为纯 RPC。分配明确线程与区间，不把单个 Find 的受检 NoGCRegion 0B 扩展成全引擎稳态 0B。没有样本不得写 0；失败/取消样本分桶并给总数。

长稳时长、真实模型预算和代表性场景待确认后才执行，采用有界日志与停止条件；与现有 E5-D 大世界探索目标机验收独立。不能以四对象夹具、编辑器八帧烟测替代目标游戏性能。

### F：统一验收与交付边界

1. 固定同一源码捕获、依赖/工具链身份和实际产品矩阵。全托管项目（现有 69，最终按实际枚举）与原生双配置编译零警告/错误。
2. 完整清单核对后运行原 CTest、ABI/格式拒绝、MCP、Python inspect、原 M6–M7 图形回归及新增联合场景。新增入口必须显式登记并解释总数变化，不静默削减原 180。
3. **2026-11-15用户延期实际apphost与原24项/配置Windows PowerShell 5.1环境门禁**（包括间接PS5.1的SDK链），暂停自动“F后立即PS5.1”安排。保留原命令/宿主/超时与全部登记，非延期范围独立验证；混合入口不得整组豁免。恢复须用户明确安排及环境/输入就绪，不用PS7/Bypass替代。延期结果不计通过，F可执行范围完成不等于M8.6/M8.7/M8完整验收。
4. 原测试超时不抬高；失败留原日志/JUnit/源与输出，定位修复后新捕获重验，严禁原样重复直到偶然通过。
5. 人工实际客户端、模型外发、目标机、长稳与原子保存等分别结论；自动用例不能代签人工确认。
6. Git 提交推送与正式安装是独立授权动作。安装必须沿原校验、正常关闭、备份、journal、原子替换与恢复流程，不用整条 Build 作为“无部署验证”。

## 4. 本次准备证据与下一动作

准备结果：`out/verification/m8-7/a-readiness-second/result.json`，SHA `6D9E180693515FEAA34173205E11399DC9C277B051147E2ECC39D7B5332E500D`。实际核对结果如下：

- 当前捕获 5035 个输入及配方通过原工具重开校验，选定 live source 与 `32DEBF68` 一致；Find 专项报告身份核验通过。
- 六个固定包索引及两个代次扩展索引身份未变，全部仍属 `fe553502` / source `D8FD5BBD`，匹配当前源码的索引为 **0**。
- 每配置 13 个候选、共 26 个产品清单存在并登记哈希；本次不重验所有候选 payload，不执行候选 EXE，不据此宣布包通过。
- 原 `ctest -N` 与 JSON 每配置 180 项，原分类器校验通过；精确后置名单各 24，其余各 156，均无本次测试执行结果。
- 正式安装精确 151 文件集合/字节与 journal 均未变；本次未重新核对整个历史 368 树冻结，不能扩大结论。

首轮准备脚本错误地从直接 PowerShell 宿主推算后置清单，漏掉间接依赖 PS5.1 的入口；在点数断言处失败，脚本和失败记录保留于 `a-readiness-first/`。第二轮改为核验原冻结与执行计划哈希并使用其精确 24 项名单，未修改测试或原分类器。

准备脚本只使用原输入/清单工具执行只读核对，输出新证据，不提供新的产品 API 或替代验收器。`testsExecuted=0`、`executionReady=false`、`accepted=false`；准备记录完成不等于功能测试通过。

下一执行动作是为当前捕获补齐匹配的可选托管、原生、包与统一测试依赖准备；先核对离线缓存、精确输出路径和原索引更新边界，不回写旧捕获或更改正式安装。实际 apphost 环境未满足前，不重复已知失败的部署预检，也不修改 ACL/MIC/策略来绕过。
