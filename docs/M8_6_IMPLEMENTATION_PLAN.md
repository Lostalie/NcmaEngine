# M8.6 可审计开发工作流

## 当前状态与边界（2026-11-06）

**最新用户顺序调整：PS5.1 测试延后，继续 M8.7。** 原 24 项/配置仍保留为未通过，以下预检失败不删除、不改判。M8.6 不宣布整体验收；M8.7 可继续独立实现与编译准备，实际 Editor/人工/性能门禁另行满足。详见 [M8.7 计划](M8_7_IMPLEMENTATION_PLAN.md)。不再重复 PS5.1 拒绝测试或修改安全策略。

### 最新：用户要求整体验收，原 PS5.1 宿主预检失败

从 M8.7 准备返回 M8.6 整体验收。先检查当前执行前提，未提前运行整套旧代次测试或重新签发旧回执。独立证据目录 `out/verification/m8-6/whole-acceptance-2026-11-06/`：

- 原 `NcmaWorkflowTests.InventoryContracts` 的命令、全部属性与当前 180 项清单逐项比较一致，仅放入新的单入口 CTest 目录以避免覆盖旧 Testing 日志。仍使用 Windows PowerShell 5.1、`-NoLogo -NoProfile -File`、原工作目录与 30 秒超时。
- 实际 CTest **0/1 通过、1 失败**，退出码 **8**，约 0.25 秒；PowerShell 在测试脚本执行前报告 `running scripts is disabled on this system` / `UnauthorizedAccess`。用例主体没有执行，不能记为 10 个 case 失败或通过。其余 23 个原 PS5.1 后置入口本次未执行；不是全 180 项/配置重跑。
- 只读安全标签检查仍看到 canonical workspace 的继承 deny ACE 与 Low 标签，以及正式和历史候选 EXE 的 Low 标签。未修改 ACL/MIC/策略，不替换宿主、不使用 Bypass；本次没有启动 Editor 或重新测进程令牌，不能据此新增“实际 apphost 已复测”的结论。
- 正式 EXE SHA `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F` 与 journal SHA `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1` 再次核对未变。本次未重新审计整个正式文件集合或所有历史冻结。
- 结果 `host-result.json` SHA `40F026BA0DBCD701267501E8BB19B00231529E608662F85E30B858057C8BEFDD`；日志 SHA `49C5AEB47C325DADDDDAA4E8712342F8131C681DBB2850A9F3EA5EE2C0D89837`；JUnit SHA `E7D85E3FC1386217F56595CD6C421E6BC448BA4443197027B63840E8217D9FFE`。进程已正常终止。

**M8.6 整体验收受阻，未通过。** 需要用户提供可按原安全策略执行受信 PS5.1 脚本、正常运行实际 Editor apphost 的受支持 Windows 环境；不能通过修改本会话安全边界来制造通过。该前提满足后，仍须补齐 `b69a0d52` 对应的可选/原生/包与统一测试输入、可恢复切换并重新冻结，再执行完整 Debug/Release 及原 24 项后置门禁。当前已有全 69 默认托管编译与 Headless 专项通过保持原范围；M8.7 只保留已完成的准备，不继续推进执行。未构建新包、部署、Git 发布或调用模型。

### 最新：Find 分配测试 v2 已获批准、实现并通过双配置专项

用户明确批准单项 Find v2，覆盖下文“待确认”状态。实施前完成原完整冻结复核，并独立备份两个原测试文件；备份记录 SHA256 `6DC30D7961D2719CC5AB9766BDCB2569837C96FA289280D15C650B73A595350E`。只修改 `Ncma.Runtime.Tests` 三个文件，没有产品源码改动：新增测试域专用 `ScheduleFindAllocationScope`、注册六组控制、为原 Find 微测试增加受检边界。

- 原 4096 次逆序 Find、严格 0B、0/2 worker、每种三个回调、原 5 秒任务与 30 秒 CTest 超时均保留。每回调原正序功能预热和其余功能测试仍使用正常 GC。
- 仅字节测量循环处于 4MiB 受检 NoGCRegion；进入可能执行完整阻塞 GC，进入/退出仍在原任务超时内。它验证该微测试是否分配，**不证明整帧 0B 或吞吐/延迟性能**。
- 拒绝或抛错的进入、主体异常、失效退出均传播为失败，不跳过/自动重试。区域在所属线程 finally 结束；额外覆盖跨线程释放、重复/并发进入、外部区域所有权和退出失败后的所有权回收。
- 真实空区间 0B 与已知对象 80B 的正控制分别在 owner/worker 执行，证明测试没有吞掉真实分配；旧 UTF8 辅助类没有变更。

首次捕获 `9a916997d91a4f53ab98eb5132fb5a0f` / source `BE25811E...` 的全 69 项目双配置编译均 0 警告/错误，但 Headless 各 **89/90**：Find 六次回调与真实控制均通过，新嵌套进入检查失败。原源码、产物和日志完整保留于 `e-find-allocation-v2-first`。辅助类随后增加进程级 CAS 所有权和外部模式检查，在触碰 CLR 之前拒绝重复进入，失败路径释放自身 claim，不释放其他 owner 的 claim。保留这次失败，不回写为通过。

修复后形成新的捕获 `b69a0d5224a4412d932e680143b41910`：5035 文件，source SHA256 `32DEBF68137E573953ACAE725531721B4B434393657E61D44F59815934FD1BD6`，manifest `2E64BBF11090B178598660E0BCE8F29A57C306E0288901053D7025D1EE9187A2`。相对首次 v2 仅辅助类改变；相对 fe553 仅上述三个测试文件改变。

| 验证范围 | Debug | Release |
| --- | --- | --- |
| 全部 69 托管项目，显式离线准备后无还原编译 | 0 警告/0 错误，160.34 秒含准备 | 0 警告/0 错误，159.96 秒含准备 |
| 原 Headless 入口仅重绑定新 DLL，原工作目录/超时不变 | PASS，6.96 秒 | PASS，6.44 秒 |
| 主用例组，含新增六组边界检查 | 90/90 | 90/90 |
| 其余九套原 ECS 测试 | 118/118 | 118/118 |
| owner/worker 正控制 | 均 0B/80B | 均 0B/80B |
| Find 六次回调 | 均严格 0B | 均严格 0B |

原固定完整 CTest 的 `-N` 与 JSON 清单仍核对为 **180 项/配置**；本次隔离重绑定范围明确是 **1 个 Headless 入口/配置**，不是 180 项全重跑。当前固定整阶段输入仍为 fe553、共享配置仍为 Release，未把它们静默切换到新源码，也未重签旧可选编译/包回执。

最终审计核验原存储冻结、两轮完整编译产物、两个捕获、原 v1/首轮 v2 失败、源码备份，以及正式 151 文件/journal；记录 `out/verification/m8-6/e-find-allocation-v2-second/final/result.json` SHA256 `FE2225A17404A44F81C164491F321B669ED4326919C3492FE624B4819620853A`。正式 EXE SHA256 `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`，journal `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1`，均未变。全部会话终态，未部署/提交/推送/调用模型或改变安全设置。

**Find v2 专项门禁通过，M8.6 整体仍未验收。** 后续需按新源码范围准备整阶段证据/回执，完成剩余受支持环境下的 Editor 验收；不得把历史 Release 145/11 改成 146/10。PowerShell 5.1 的 24 项/配置仍在主体完成后补测，人工闭环与 E5-D 债务不变。以下保留历史诊断和当时的待确认状态。

### 最新：事件关联与纯 BCL 计数异常复现；Find 测试 v2 待确认

本片完成新的因果诊断步骤，不是继续重复队列取绿。独立全上下文诊断增加有界 CLR 事件观察器，原 Runtime DLL、原 Find 循环、严格 0B 及用例顺序不变。离线编译 0 警告/0 错误，预先固定两轮进程分别 exit0/exit1，记录 7683/7549 个事件。第二轮 worker=2、call=0 报告 **7752B**，索引/求和/缺失 UUID 均正确。该区间 583.2 微秒，位于已配对的第 19 次后台 GC 起止区间内；测量线程没有采样分配事件，**但采样缺失不能证明零分配**。观察器自身无容量溢出/回调错误，不宣称未测量的传输丢失为零。

依据 [Microsoft GC 事件字段定义](https://learn.microsoft.com/en-us/dotnet/fundamentals/diagnostics/runtime-garbage-collection-events)，`GCStart_V2.Type=1` 为后台 GC。事件证明重叠，不证明原历史 CTest 失败的直接原因。进一步创建**无引擎引用**的 BCL 对照：只在独立诊断进程中保留有界对象图、发起 32 次显式后台 GC 刺激；正式引擎与原测试 GC 配置不变。测量窗口只含计数器和 Sleep/SpinWait，不创建托管对象。

| 对照 | 完整记录结果 |
| --- | --- |
| 16 个刺激前空区间 | 全部 0B，无后台 GC 重叠 |
| 32 个刺激后空区间 | 全部非零，全部与已配对后台 GC 重叠 |
| 窗口前已知锚对象分配 | 32/32 精确为 80/256/1024/2048B |
| 例：相同 Sleep 条件的前四项 | 空区间分别计入 8016/7840/7072/6048B |

BCL 对照编译 0 警告/0 错误，进程 exit0，1747 个事件、观察器容量溢出 0；记录保留所有 48 个区间。**本机 8.0.28 的空区间计数异常已复现；原历史失败具体原因仍未证明，历史失败不改判。** 本片未替换运行时、使用 NoGCRegion、放宽阈值或延长原超时。

证据：`out/verification/m8-6/e-fe553-find-event-trace-first`、`e-fe553-bcl-gc-control-first`；汇总 `e-fe553-find-trace-report/result.json` SHA256 `7B4BFDB8A4A388B6A7264D043CDCB7146E0F435F309F94C31283CE5FF5960BD3`。三进程均终态，完整 Release 冻结 `6A1A89DA...`、所有诊断绑定输入/日志、正式 151 文件及 journal 核验不变。没有产品或原正式测试源码改动，无部署/Git/模型/密钥/安全策略操作。

#### 待确认的单项 Find 分配测试 v2（尚未实施）

已向用户提出明确选择，自动继续不视为批准，先前 UTF8 专项批准也不适用：

1. 仅将原 4096 次 Find 测量放入受检 NoGCRegion；保持 0/2 worker、每配置三个回调、原循环与严格 0B，不改变引擎运行设置。现有每回调预热/索引功能检查和其他功能用例仍处于正常 GC；该专项不作为整帧分配或性能证明。
2. 进入失败/抛错、退出失败/预算失效均令测试失败，不能跳过、自动重试或只记录警告；通过 finally 在原所属线程结束区域，故障仍走原调度清理。
3. 增加真实 0B 与已知非零分配控制，以及进入失败、主体异常、退出异常、跨线程结束等边界检查，证明并未吞掉真实分配或清理失败。
4. 获批后先归档受影响输入与旧失败，形成新捕获、双配置编译和相应原入口测试；旧包/冻结/回执不得重标为新源码通过。若用户选择保留 v1，则继续诊断，不实施测试变更。

当前等待此项选择，M8.6/独立 Editor 环境/人工闭环/E5-D 仍未验收；PowerShell 5.1 的 24 项/配置仍安排在主体完成后。以下为历史记录。

### 最新：Headless 诊断定位到分配计数，原因尚未证明

保留原完整 harness 的所有用例和顺序、原 Runtime DLL `E8A391FC...` 与 4096 次 Find 循环，在独立诊断目录内补充测量区间外的失败值输出。首次诊断全通过；随后固定三轮原版/诊断版交替对照，原版三轮通过，诊断版两次失败、一次通过。两次失败均为 worker=2、call=1，索引正确、sum=8386560、missing/empty=-1，**线程分配计数增量均为 8168B**。这是诊断复现的实际值，不能倒填原始 CTest 失败时未记录的值，也不能把后续原版通过改写为问题已修复。

第二批固定三轮增加区间前后 GC 观测，原版三轮通过，诊断版两次失败（7880B、6560B）和一次通过；两次失败的 Gen0/1/2 次数及已完成后台 GC 索引均未变化。计数不变不能证明没有正在进行的后台阶段，但本轮也没有证明 GC 导致这些增量。另做三轮纯 BCL SpinWait/整数求和替代测量循环，以及三轮原 Find 逐次计数写入预分配数组的诊断，均未复现；这些改动有观察扰动，只作为对照，不是正式测试修复或通过依据。

- 五组证据依次为 `out/verification/m8-6/e-fe553-find-full-context-first`、`e-fe553-find-paired-first`、`e-fe553-find-gc-boundary-first`、`e-fe553-find-bcl-control-first`、`e-fe553-find-per-call-first`。共 19 个实际进程全部终态；四个独立诊断构建仅使用本地离线输入，均 0 警告/0 错误。
- 未改产品源码、原正式测试、普通 GC 配置、严格 0B、原超时或正式安装；未使用 NoGCRegion。原始全轮与 CTest 复测失败继续保留。当前共享配置仍为 Release。
- 最终复核完整 Release 冻结 `6A1A89DA...`（368 树/17229 树内文件及 5220 单独文件）及全部诊断输入/日志哈希，均未变化；正式 151 文件与 journal 也保持原样。汇总 `e-fe553-find-diagnostic-report/result.json` SHA256 `0B898DD95A665959767A545EDC2B5C2887CC712B53E0593ACD7DA34B19A69B47`。
- 重新核对 [.NET 8.0.28 GC 源码](https://raw.githubusercontent.com/dotnet/runtime/v8.0.28/src/coreclr/gc/gc.cpp)及[上游分配上下文记账修复草案](https://github.com/dotnet/runtime/pull/134855)。草案提供待验证机制，不是已发布修复，也不是本轮四个区间的因果证据；没有替换运行时。

下一步是有界分配事件/计数器归因，不再重复相同队列取绿。不得扩大早先仅针对 UTF8 微测试的 NoGCRegion 授权。M8.6 仍未验收，Editor 受支持运行环境、人工闭环与 E5-D 债务不变；PowerShell 5.1 每配置 24 项仍在主体工作完成后补测。以下为历史阶段记录。

### 最新：双配置选集已执行，Release 145/11；Headless 问题未归因

Release 按原命令、原超时完整执行 156 项：**145 通过、11 失败、另 24 项 Windows PowerShell 5.1 后置**。CodeGen 原构建/发布入口 2/2（145.08 秒），全部测试含审计 937.29 秒；运行前、CodeGen 后、全部测试后的冻结复核均通过。原 Debug 144/12 不重写。两配置当前 M8.6 新增 21 个工作流入口均为 **16 通过、5 后置**，其中真实 SDK 链仍后置，不宣称完整阶段/人工验收通过。

1. Release 切换仅涉及 canonical `out/managed` 及捕获内 `out/managed`、`out/bin` 三个测试支持目录；219 个旧文件有原目录归档与独立备份，219 个新文件从同源 Release 产物激活。旧冻结除这三处外完全不变，正式安装不在切换范围内。当前共享运行配置为 **Release**，不能直接在此状态运行 Debug。新活动检查点 `e-fe553-release-switch-first/active-inputs.json`，SHA256 `746DAB7B1C0B23DDD0D3CE5437E99295B3A9BD2CCE328AA74210F90D90F195B2`。
2. Release 冻结 `6A1A89DAA04241F53CFDB78E57AE23C43CC540D141690476F267D7F6D2B3E3E8` 覆盖 368 棵树/17229 树内文件、5220 单独文件；已纳入新增六个 canonical 运行目录和旧产物备份。完整结果 SHA256 `A45BCC38EBF87144EAAAD85FDFED06A9C7A3A8CB32E35FE5B9D233BCE2A18B61`，JUnit `841671C68B6553FD778C0E4749E693EE56A680B622DAE8671FD046D556846350`，证据 `e-fe553-release-first`。
3. Release 11 个失败中，10 项为实际 Editor 日志访问拒绝；另 1 项为 `ScheduleBufferTests.cs` 中 4096 行 Find 的联合断言。该断言同时包含行号、求和、分配字节和缺失 UUID 返回值；原日志未输出各值，**不能先断言是 GC、零分配失败或运行环境造成**。原完整 Headless 入口单独复测仍失败（主组 83/84，其他套件继续运行），原冻结前后不变；证据 `e-fe553-release-headless-rerun`。
4. 独立诊断探针仅引用原 Runtime DLL（SHA256 `E8A391FC817E14E0BB10C7DCEE46921A81EC37293323E44B3A69B2685A834774`），显式空源离线还原后编译 0 警告/错误；24 次同样的公共 Find 循环各值正确、均 0B，正常 GC 未改变。证据 `e-fe553-find-diagnostic`。它没有完整原 harness 的前序上下文，**不能替代失败或标为修复**。下一步在独立全上下文诊断构建中保留所有前序用例，只在测量区间外补充实际值，再决定产品修复点；不放宽 0B、不推定 UTF8 微测试的 NoGCRegion 授权可扩展至本测试。
5. 完整渲染 Debug 复测前，参考输出及渲染输出共 187 文件已完整保留。第一次归档被故意创建的硬链接负例拒绝，第二次遇 PowerShell 空数组包装错误；两次脚本和部分备份保留，未移动原目录。第三次归档仅核验并支持该精确双链接负例，未改产品源码捕获单链接规则；计划 SHA256 `62D8710FF798455DDDEF86AEDB90D12A89DC997966E32CD4D566EBE0C1D8A975`。原参考+Rendering 复测 1/2：参考通过，找不到 EXE 已修复，现失败于真实 Editor 日志访问；旧输出、新增输入和原冻结均保持不变。
6. `e-fe553-release-token-observe` 只读捕获本轮实际 Release candidate 的 PID 18920，时间 `2026-11-05T18:14:01Z`，完整性 SID 为 `S-1-16-4096`（Low）。未启动额外 Editor、未修改令牌/ACL/标签/策略，标签设置来源仍未知。已询问是否有现成受支持的其他 Windows 环境；不等待该答复来阻止独立 Headless 诊断。

汇总证据 `e-fe553-double-config-report/result.json`。本轮没有产品/正式测试源码改动，正式 151 文件、EXE `379AA113...`、journal `F5BDC19D...` 未变；未部署/提交/推送/真实模型调用。全部已启动会话均结束。后续还需解决 Headless 原因、完成受支持环境下的独立 Editor 验收、按修复范围重新验证，再在主体工作完成后补测 PowerShell 5.1。M8.6、人工与 E5-D 仍未验收。以下为历史记录。

### 最新：Debug 统一验证完成，未通过；运行目录修复后两项复测通过

同一 fe553 捕获的 Debug 本轮 156 项全部执行，**144 通过、12 失败，另 24 项 Windows PowerShell 5.1 后置**。包含原 CodeGen 构建/发布两入口 2/2（144.44 秒，日志逐项列出原构建步骤和两个发布产品）；其余 154 项未遇首错即停。整轮含输入审计 1031.26 秒。所有入口执行不代表早退的历史 harness 内所有 case 都已到达；本轮没有改超时、断言或通过门槛。

- 旧 Debug/Release 各两份参考图文件先完整备份、再保留原目录到 retired，未删除；原夹具在空目标生成新参考输出。证据 `e-fe553-reference-preserve-first`。
- 执行前重新核验 Python 独立输入、离线回执和两配置配方。Debug 冻结覆盖 351 棵树/16020 树内文件及 5162 单独文件，SHA256 `75015AD4FDD7E380246DD4DA9D1A63874A2F3E95D221235738C432BD5FDAE6F9`。运行前、CodeGen 后和全部测试后均完整核对通过。
- 完整结果 `e-fe553-debug-first/result.json` SHA256 `7AA09EF0DE7DA44F7B4F214B8F0118D6AF4FB1E08C7A3FF4C12D74879E74DAD3`，其他组 JUnit `D958F6A3A92D192EDC126690C50FCAC4839989B5AE8DF92B95BB8049ADAB5FD8`；runner 因失败正确返回非零。
- 逐项归因：3 项缺少夹具运行时动态查找的 canonical 托管目录（Rendering、PlayerRunnerLifetime、SceneRendering）；9 项为实际 Editor apphost 日志初始化 `editor_user_data_access_denied/0x80070005`，其中 SceneUiHosts 另外核对原子进程日志。只读检查当前工作区及候选 EXE 存在继承 Low 标签，根目录还有拒绝 ACE；标签设置来源未知，未测量本轮子进程 token，不将其当作所有历史失败的共同原因。证据 `e-fe553-debug-diagnosis-first`，未改安全设置。
- 已修复运行准备缺口：仅向此前不存在的 canonical `managed/{Ncma.Editor.App,Ncma.Player.App,Ncma.Player.Tests}/bin/{Debug,Release}/net8.0` 六个目录复制同源完整产物，共 360 文件；不修改产品或测试源码。复制前后原冻结核验通过。证据 `e-fe553-canonical-runtime-first`，计划 SHA256 `25A46B997DEC5B49B4BFE23AA9CAE4451F1EBD4FDDE45E9CE81BB0D5FA881D02`。
- 原 PlayerRunnerLifetime 与 SceneRendering 两入口定向 Debug 复测 **2/2，5.08 秒**；原命令/属性/超时未变，原冻结与新增六树前后核验通过。证据 `e-fe553-canonical-runtime-focused`，结果 SHA256 `EB061CB1106D4F591E8EA048AD5489C3947531DFC95F2F27458A91482881F447`。不把定向结果合并改写为整轮 146/10，原 144/12 保持不变。

**剩余顺序：** 完整渲染入口重跑前保留本轮新参考图输出；处理实际 Editor 启动访问问题；成套切换 Release 共享/捕获内支持，重新冻结后运行 Release 156 项，并按修复范围复验 Debug。所有后续冻结必须额外纳入上述六个 canonical 目录，不能只保留原 AI 两目录及 Shader.Cook/UiFixture 四目录。Release 尚未执行，PowerShell 5.1 仍在主体完成后补测。正式 151 文件、EXE `379AA113...`、journal `F5BDC19D...` 未变；无部署/Git/模型调用。M8.6、人工与 E5-D 仍未验收。以下准备记录为历史时点。

### 最新顺序：Windows PowerShell 5.1 测试移至 M8.6 主体完成后

用户明确要求在 M8.6 完成后再进行 Windows PowerShell 5.1 测试。执行安排为：先完成主体实现及其余 156 项/配置的验证，再单独补测这 24 项/配置并汇总；保留完整 180 项分母。暂缓项不阻塞当前主体推进，也不计通过；主体交付状态与完整测试验收状态必须分开。补测仍要求受支持的原宿主环境，不覆盖执行策略、不改超时、不删除入口、不用 PowerShell 7 替换。此安排与 PlayStation 5 主机的下一版本规划无关。

### 最新：固定输入已切换，同源回执、运行目录和离线准备已核验

本轮仍为 fe553 捕获，没有产品或测试源码改动。原激活脚本经过精确 13 目标预演后完成切换：947 个旧文件保留于 retired，并另有完整独立备份；947 个新文件激活，未删除、强制覆盖或重试移动。原 AI/扩展生产器双配置各六个构建图 0 警告/0 错误，按原流程生成同源回执。新活动检查点为 `e-fe553-input-cutover-first/active-inputs.json`，SHA256 `7AC329112516BAF58630F20F069BCAD7C834F58AEFC01A0365212CD185B94D48`。共享运行配置仍为 Debug，Release 尚未成套激活。

- `e-fe553-runtime-staging-first`：148 个运行目录共 3790 文件，其中新建 146 目录/复制 3640 文件；此前验证的两个 Rendering.Tests 目录共 150 文件逐项匹配后只读复用。其他已存在目标仍拒绝，不放宽为任意覆盖。
- `e-fe553-test-binding-first`：每配置七个生成配置文件先独立备份，再接线原 SDK/捕获/Git 参数。CTest 各 180 项名称、属性和超时不变，各 141 个直接托管 DLL 存在。检查点 SHA256 `21B2AC2F28FC9BCCFE8A279377CB95255B6505DCE8C8824760C12ED4E0C84445`；枚举不代表功能运行。
- `e-fe553-runtime-support-first`：向捕获内两个空目录复制 Debug 专用 Editor 145 文件和共享运行 37 文件。四个原默认/AI 包上下文校验通过；58 个不同宿主声明的依赖文件缺失 0，未解析 RID 项 0。没有实际启动宿主，不宣称完整运行闭包已经验证。
- `e-fe553-shell-deferral-first`：CTest 自身核对精确 156 项选集及 154 项非 CodeGen 子集，八个间接调用源码哈希未变。每配置仍 180=24 后置+156 当前入口；选择记录 SHA256 `12986987A46D1770F2F7AE95FD7DB7F890C464F9113962D8749C5FFA71DACA32`。
- `e-fe553-codegen-offline-first`：17 个独立复制的本地包、双配置共 10 次仅还原、128 个实际项目还原绑定及 34 个还原包的 SHA512 已核验。测试环境绑定 SHA256 `5F09BB4396169280C578CB45DCE96E57F22FB98F9A81D0198AB40133F3A66947`。原 16 条构建命令和 2 个发布模式尚未执行，不把还原计为功能通过。

准备脚本曾因一次合并补丁超过 Windows 命令长度在写入前失败，拆为逐文件 apply_patch 后成功；未影响产品或测试规则。所有本轮执行会话已结束，正式 151 文件、EXE `379AA113...`、journal `F5BDC19D...` 不变；未功能测试、部署、提交推送、真实模型调用或安全策略改动。

下一步：先完整保留已有 kernel-Debug/Release 参考图输出，防止原夹具再次运行时覆盖历史证据；核对实际执行配方与 Python 回执，再完整冻结并启动两配置各 156 项验证。冻结范围要纳入四个 canonical Shader.Cook/UiFixture 目录，使用适用的托管目录校验器；不能只沿用旧冻结器的两个 AI 目录特例。Release 之前成套切换共享及捕获内运行支持。PowerShell 5.1 24 项/配置仍后置，apphost、人工、E5-D 和整阶段验收状态未改变。

### 此前：当前捕获完整候选包已生成，固定输入备份就绪

同一 fe553 捕获按原生产流程完成 Debug/Release 各 13 个候选包，耗时 485.59/492.71 秒；首次原生配置即使用匹配 MSVC，未修改编译参数或安全策略。包代次分别为 `ac88d960913b44a89f42e497b7c74ca0`、`b179a6f06e5a48a4b57ca3808e1bcc86`。两个模块客户端包分别为 `589a15e89468449da0aec7fe5c8d3a78`、`2876f21785ec4dbfbaee8c909a2ae6c6`，各 22 文件。

- 最终核验：26 个完整候选共 2778 文件、两个模块共 44 文件，完整 manifest/源绑定/依赖均通过；旧 26 个候选与两个模块、全部独立备份和原生产器备份完整保留。正式 151 文件、EXE `379AA113...` 和 journal `F5BDC19D...` 未变。
- 六个测试包索引已更新；原默认索引采用受检 copy-before/write，AI/模块索引沿用带 previous 备份的 File.Replace，不宣称跨文件原子性。新权威检查点 `e-fe553-module-packages-first/final-audit.json`，SHA256 `1FF0FB39C24E76290AA00C281E10CB9E3DF9DF84DC0319CAADD08035817C539E`；旧 4d79 包索引哈希仅为历史证据。
- `e-fe553-input-cutover-first` 已列出精确 12 个可选目录＋原 37 文件共享目录和 8 个索引；`e-fe553-shared-snapshot-first` 生成双配置各 37 文件私有副本，共 74 文件，未激活。
- 13 目录旧 947 文件已完整备份，新 947 文件独立暂存，8 个索引完整备份并核验。计划 SHA256 `83AFF1FFB68A6E5ED1D1BADF1CC39464ECDFB924C8A586F38DC45A161721E213`；`backup-verified.json` 为 backupVerified/stagingVerified=true、cutoverExecuted=false。尚未移动或删除旧目录。

本轮会话全部正常结束，未运行功能测试、未部署或提交推送。下一步检查原激活脚本并预演，然后按既有授权执行精确、可恢复的目录切换，生成同源原 AI/扩展编译回执并核验；固定输入和编译回执当前仍属旧捕获，不宣称已切换。之后再完成运行闭包、冻结与其余 156 项/配置复验。PowerShell 5.1 的 24 项/配置仍安排在 M8.6 主体完成后；apphost 环境、人工与 E5-D 欠项不因此消除。

### 此前准备：同源可选图、SDK 声明和包离线输入已完成

仍使用下述 fe553 捕获（5034 文件、source D8FD5BBD、manifest B2E803EC），本轮无产品/测试源码变化：

- `e-fe553-optional-first`：Debug/Release 各六个可选 AI/扩展构建图，全部 0 警告/0 错误；每配置六个私有产物树共 455 文件，未切换固定输入。
- `e-fe553-sdk-first`：双配置原扩展测试图编译 0 警告/0 错误，114.88/114.71 秒；原 16 字段 SDK 声明通过只读观察和回执核验，各配置 1297 依赖文件。不是 SDK 链功能测试，未启动后置的 PowerShell 5.1 测试。
- `e-fe553-package-inputs-first`：四组离线准备完成。每配置 package 10 个作业/1621 依赖文件、modules 2 个作业/1344 依赖文件；两个模块测试图编译通过。六个精确旧包索引已完整独立备份并逐字节核验，未更新索引、未发布包。
- `e-fe553-optional-first/final-audit.json`：重新打开依赖回执，核对完整默认/可选/SDK/原生产物和十个实际 Editor/Player 依赖组合；六个已暂存运行目录及历史模块/索引不变。当前源码与捕获一致，正式 151 文件、EXE `379AA113...`、journal `F5BDC19D...` 均未变。审计脚本 SHA256 `B7CA6E075109BCA25E04D42929ACE627B47B95E0A34168002ABA96C79BB06741`。

所有本轮执行会话已结束。下一步是当前 fe553 的完整候选包（首次配置即使用匹配 MSVC 环境），再按已授权备份核验流程切换固定测试输入、核对运行闭包并冻结后复验。旧 4d79 包与固定回执仍是历史输入，不能重标新源码通过。此轮未运行功能测试、未更新正式 EXE、未提交推送；M8.6 整体验收、apphost 环境、人工和 E5-D 欠项仍开放。以下记录保留此前进度。

**术语与范围确认：** PlayStation 5 主机平台规划移至下一版本，当前版本不开展该平台开发。Windows PowerShell 5.1 是独立的测试宿主，其测试仍暂缓：每配置 180 项总清单中的 24 项标记未执行，其余 156 项继续按原条件推进；不计暂缓项为通过、不删除测试入口、不绕过策略或替换宿主。本文历史“PS5”/“nonPS5”及 `user-deferred-ps5` 状态码均指 PowerShell 5.1，保留原证据和标识。此范围确认不构成 M8.6 整体验收通过。

### 最新：工作流/gRPC 回归通过，原生依赖修复与渲染库复测完成

上一批 bce72 捕获的补充回归已结束：Debug/Release 均 DomainSourceHost 12/12、Python 输入合同 6/6、完整本地 TLS/gRPC 9/9；两条 CTest 入口均 2/2（44.82/45.40 秒）。MCP 使用同捕获产物的独立 7 文件闭包，Python 使用原独立离线准备回执并在测试前后重新验证；没有模型调用、联网下载或真实 SDK 工作流执行。证据 out/verification/m8-6/e-debug-repairs-hosts-first；这是 bce72 范围结果，不能冒充后续整个新捕获验收。

继续诊断发现参考截图 EXE 导入 glfw3.dll，但原 bin 目录缺少该 DLL；动画插件此前也依赖整条 Build 脚本复制。已在 CMake 原目标 POST_BUILD 补齐：GLFW 复制到参考 EXE 同目录，动画 DLL 复制到 m2/plugins，保持原库输出位置和所有构建警告/检查。

最新捕获 fe55350272b14df6abb0404ae1cdd174（5034 文件），相对 bce72 仅 CMakeLists.txt 变化；source SHA D8FD5BBD40CE1B597F653376183C0D4C2346412B9FF1E9A0BB61E4D0B88FE708，manifest SHA B2E803EC498E76988B8CEC48F70A6DDC0512DE36B535BCCD57B46F1B5B320611。

- 原生全目标双配置各 322 步通过：105.47/107.55 秒，无编译诊断，两份新增 DLL 与源文件哈希一致。原 CTest 库存各 180，名称、属性和超时不变。SDK 链仍未绑定且属用户暂缓 PS5 范围；枚举没有缺 EXE 警告不代表运行依赖已全部齐备。
- 同一 fe553 捕获的全 69 个默认托管项目，Debug 174.78 秒/Release 162.49 秒（含显式离线准备）均 0 警告/0 错误。证据 e-debug-runtime-closure-first、e-debug-runtime-managed-first。
- 原参考截图入口在双配置均通过，保留原 30 秒超时；256×256 reference.rgba 为 262144 字节，DX11 验证 0/0、GPU 资源计数 0。此图与渲染器共享数值着色器，不是独立算法基准。证据 e-debug-reference-first；原入口会截断既有参考文件，后续复测须保留已有输出并使用新输出代次，不能直接覆盖。
- 从同捕获编译结果向四个原本不存在的 canonical Shader.Cook/UiFixture 双配置目录复制完整 50 文件；CreateNew、逐字节和完整集合核验，无覆盖。证据 e-debug-dynamic-inputs-first。
- 首轮渲染库聚焦测试因把 DLL 直接映射到 artifacts 布局而失败：原夹具从父目录推断配置，得到 Ncma.Rendering.Tests 而非 Debug/Release。双配置各两项失败全部保留于 e-debug-library-focused-first，没有改判通过。随后把同一批完整 Rendering.Tests 产物共 150 文件放入两个原注册目录，仅新建生成目录、没有修改源码或断言（e-debug-render-layout-first）。
- 第二轮采用原注册命令、目录布局及超时：PostLibrary/ShadowLibrary 双配置各 2/2，31.77/30.10 秒；后处理 149 个用例、阴影 180 项检查/12 幅图像，maxError=0、API0/0。证据 e-debug-library-focused-second；托管产物、原生插件、全部六个准备目录及正式安装 151 文件/journal 均未变。

这不是完整 M8.6 验收。接下来仍需为 fe553 准备匹配的可选构建、SDK/离线依赖、完整包及精确运行布局，再冻结并执行两配置各 156 项非 PS5 测试；导入、其余场景 UI/图形恢复、apphost 环境债务仍待相应验证。PS5 24 项继续暂缓，完整分母仍为 180；人工和 E5-D 验收不变。正式 EXE 未更新，未提交推送。

### 最新修复批次：双配置全项目编译和四入口回归通过，整轮仍未验收

本轮相对 4d79 捕获共修改 13 个 managed 路径，没有改动原生源、构建配方或原失败证据：

- 资产、导入、Prefab 夹具改用共享的排他 OS 临时目录，并保留所有测试输出；新增唯一性及不可写入程序集目录检查。不删除旧夹具和旧链接。
- 工作流环境在路径规范化前拒绝点路径及尾点/尾空格别名；修复原先 GetFullPath 擦除 `..` 导致后续拒绝检查失效的问题。保留原拒绝断言，并覆盖工作区、工具、Runner 三个路径角色。
- UI 提交接上现有 IEditCommandProposal 纯验证接口，复用封闭输入、内存提案和版本检查。端点可生成独立人工审批提案；纯验证不读写文件、不调用文件/资源授权。Prepare 继续检查原权限及原始作者字节。新增独占锁文件下的影响审阅、无隐式授权、撤销/未知提案/畸形输入回归。
- UI 外部编辑负例改为断言现行明确的 ui_document_conflict，并增加外部文件、场景和历史不变检查。Python 合成回执夹具规范化绝对路径，并补上明确的 InvalidDataException 捕获；生产回执验证器未放宽。

新捕获 bce72efa0fae4106b77dd44d3b1faaaa，5034 文件；source SHA 48E94408A1F0BC88F65E7917926DBDD042549A58E2FFDF2380D628A2CC7919F3，manifest SHA 148E80D2F7301E3EE8FC51B90535838E219BC4FCE8846415334CE7DD2FC2FD2A。原本地离线缓存显式准备后，从捕获副本构建全部 69 项目：Debug 160.36 秒、Release 160.05 秒（含依赖准备），均 0 警告/0 错误。证据 out/verification/m8-6/e-debug-repairs-build-first；未用整条 Build 入口、未发布或部署。

聚焦复测原 Ui、Asset、SceneDocument、Workflow.Output 四个入口：Debug 4/4（17.70 秒）、Release 4/4（17.26 秒）。UI 37/37、资产 41/41、工作流输出 10/10；保留原超时及命令参数，仅映射到新编译 DLL。全部托管产物与原生插件完整集合在测试前后相等，正式安装 151 文件和 journal 未变。证据 out/verification/m8-6/e-debug-repairs-focused-first；原生仅凭全部非 managed 捕获文件及原二进制字节相等复用于本次聚焦测试，不冒充新捕获的原生构建/整包验收。

导入、gRPC、完整 DomainSourceHost 尚未复测；动画 DLL/UiFixture/Shader.Cook 命令式运行依赖准备、apphost 访问异常及完整双配置回归仍待处理。4d79 的可选构建、包及固定输入回执随源码改变转为历史，不能改签成新源码通过。原 Debug 132/23/1 结果保持原样，PS5 的 24 项继续用户暂缓，M8.6 整阶段、真实 SDK 链、人工及 E5-D 性能验收均未通过。

### 最新：PS5 按用户要求暂缓；非 PS5 Debug 已执行，但未通过

仍使用捕获 4d79ccad4ed541cda38902a8d333e0ee/source DE1859；本轮没有新增产品、测试或配方源码变更。固定测试输入完成精确备份及可恢复切换，原验证回执生产器双配置共 12 图构建均为 0 警告/0 错误。当前 active-inputs 回执位于 out/verification/m8-6/e-ui-repair-input-cutover-first，SHA 7765F834136D9CEDDDCDFFF5EFB3BC2A03B2F8DDB85CA3486268D65493FBBCDD；下方旧 f705 输入记录仅属历史。

- 每配置完整库存仍为 180 项：24 项标记 not-executed(user-deferred-ps5)，其余 156 项可选。没有改用 PS7 执行原 PS5 测试，也没有修改安全策略、原超时或断言。
- 实际 Debug 结果：132 通过、23 失败、1 项因依赖失败未执行、24 项用户暂缓。CodeGen 两入口均通过（69.27 秒、76.30 秒）；整轮 817.79 秒。原 result.json 的 allEligibleExecuted 只表示收到 156 条结果记录，不能解释为全部实际执行。
- 原始结果、JUnit、输出及失败保留于 out/verification/m8-6/e-ui-repair-nonps5-debug-first。运行进程已终止，Release 尚未开测或切换共用输入；不能据此声明 M8.6 验收通过。
- 冻结 SHA F40E521F933205040057A1AFD133B6AD5D45FD236142F3C7BEE49EE5133C3B1C；开测前及 CodeGen 后审计通过，全部测试后严格审计遇到重解析点失败。新增独立、不跟随链接的审计位于 e-ui-repair-debug-diagnosis-first/input-audit.json：5158 个独立文件未变，343 个目录中 340 个未变，Import.Tests 增加 294 文件、Scene.Tests 增加 1 文件（这两处原文件无改写/丢失）；Assets.Tests 超过诊断器 8192 文件上限，仍记未完成核验。
- 对 Assets.Tests 的单独有界目录枚举发现 16557 文件、293 目录及 asset-test-work/0a97e04d989f499ab06a28a872892fc3/9/assets/linked 目录链接。源码中对应拒绝链接夹具在程序集目录创建自有测试 junction；没有跟随、删除或重建基线。这些测试输出应迁出被冻结输入目录，再重新捕获验证。
- 正式安装 151 文件、部署 journal 和选定源码均独立核对未变；EXE SHA 379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F，journal SHA F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1。没有部署、Git 发布、模型调用或密钥访问。

失败初查同时包含命令式运行依赖准备遗漏（动画原生 DLL、canonical UiFixture/Shader.Cook 路径）、UI/工作流断言、gRPC 夹具拒绝类型以及 apphost 访问异常；尚未逐项最终归因，不能全部归为环境问题。下一步先完成分类并集中修复，再形成新的匹配输入重测 Debug/Release；PS5、人工验收和 E5-D 仍单独留账。

### 最新：当前捕获 PS5 暂缓范围复核、双配置候选准备完成；尚未统一开测

本轮仍使用 UI 夹具修复后的捕获 4d79ccad4ed541cda38902a8d333e0ee（5033 文件），source SHA DE1859B761EDC355D21CE8970B11E3190BB8E016777E282DAF57C72D1FE75D36、manifest SHA DD6D6196C248A47B443015CAC823399B91BB004F99E3AD8C726BEA21EA508CE2。没有新增产品、测试或构建配方源码修改；以下准备结果不替代功能测试。

1. **按用户要求暂缓 PS5。** 每配置仍登记 180 项，其中 21 个直接 PS5 入口及 3 个间接启动生产 PS5 worker 的入口（CaptureHost、SourceCreationHost、SdkChain），共 24 项标记 not-executed(user-deferred-ps5)，不计通过。剩余 156 项为 CodeGen 2 项及其他 154 项。e-ui-repair-ps5-deferral-first 对当前捕获重新核对 8 个间接启动相关源码，用 CTest 自身分别验证精确 156/154 选集；结果 SHA F05466B1EDB3A4F3B43FF54321720C83752B55643EA5A412DC1579BF4B73E76D。没有改执行策略、替换宿主、扩大超时或删除测试。
2. **当前捕获准备。** 全 69 默认托管项目的双配置产物及还原绑定已最终审计；可选图每配置 6 组、合计 12 组编译均 0 警告/0 错误，私有产物每配置 455 文件。SDK 图双配置编译 135.92/88.15 秒，均 0/0，并完成只读输入观察；不是 SDK 链测试。新原生验证构建每配置 322 步成功，99.58/113.78 秒，清单各 180；SDKchain 尚未绑定且继续用户暂缓。证据分别为 e-ui-repair-optional-first、e-ui-repair-sdk-first、e-ui-repair-native-second；首次缺编译器的配置失败保留。
3. **打包阻塞与修复。** e-ui-repair-full-packages-first 双配置因未初始化 MSVC、找不到 C/C++ 编译器失败。第二版校验在修改前因 HostX64/Hostx64 大小写误判被拦，随后按 Windows 路径比较并额外验证文件 SHA256。第三轮加载正确 MSVC 后配置成功，但首次失败缓存留下空 C++ 默认参数，两个 shader 工具触发 C4530/C2220，原 /WX 规则保持。第四轮精确列出并核验路径、完整文件集和无重解析点，完整备份后仅将失败的两个原生输出目录可恢复地移至各配置的 failed-native-retired；没有删除文件、覆盖历史证据或修改编译选项。原打包流程在空输出目录重新配置，正常生成 /EHsc 及 Debug/Release 默认参数。
4. **候选包结果。** e-ui-repair-full-packages-fourth Debug 493.07 秒、Release 488.72 秒成功，各 13 个候选包；代号 d9b00d781ff84691b2d53df370a4b040、716e90ae6b594b06897c9c52fde8c663。原模块客户端离线发布也完成，代号 97815e5c23e74435bc4a796060975887、e52ba65aa611404e9584be20edbdade9，各 22 文件。所有候选均未验收，没有运行 Editor/Player、Python worker 或模型。
5. **最终检查点。** e-ui-repair-module-packages-first/final-audit.json 核对新 26 个候选包的 2778 文件、2 个模块包的 44 文件、全部源/依赖/manifest 绑定及六个索引的独立/原生产器备份；旧 26 个候选包、旧 2 个模块包和 14 个固定测试输入目录均未变。六个固定包索引已指向当前捕获，旧索引哈希只能作历史证据。默认索引保留原备份后写入机制，AI/模块索引保留原 File.Replace，不声称六文件跨文件原子性。最终审计脚本 SHA 648C72A9B719796E13C3C3AEFB16407BDC15CCE8451B1C9B6E6418EFAC940902。

正式安装 151 文件、EXE SHA 379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F 及 journal SHA F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1 均未变。全部已启动任务均终态；本轮没有功能测试、联网下载、策略/ACL/MIC 修改、部署或 Git 发布。所有旧失败证据保留。

**后续前提：** 固定可选测试目录及 AI 编译回执仍为旧 f705 源码，不能据此启动新捕获全测。先产生匹配的原验证回执、按既有授权备份切换测试输入，准备每配置共享 37 文件快照和运行时支持映射，完成当前 CodeGen 离线绑定及全量冻结；再执行 Debug 156 项及受检配套 Release 输入切换后的 Release 156 项。始终保留 180 分母与 24 项用户暂缓。runtimeReady=false、stageAccepted=false；独立 apphost 的 Low/访问问题、完整 SDK 链、人工验收及 E5-D 欠项仍开放。以下旧记录不改判。

### 最新：实际Debug测试、取消错误选集、UI夹具修复及专项通过；全阶段未验收

1. **实际冻结及CodeGen。** `e-nonps5-debug-second/freeze.json` SHA `DF2BA93D366BB8DFB460BDAE51523CCDC5076CAF35620C4A87B4A356B0E953DB` 覆盖旧捕获 `f7059c63`、343目录/15958目录内文件/5156单独文件。首次冻结错误地把两个合法canonical managed AI目录交给仅限out的校验器，拒绝后保留；第二次仅对此精确两目录使用原AI校验器。Debug CodeGen两原入口均通过（70.92/76.26秒），其原回执记录SDK选择＋16条构建相关命令及2个Editor/Player发布命令，包含预期负例错误；测试后冻结输入复核未变。只对旧捕获及Debug有效。
2. **执行器错误不掩盖。** 第二组使用.NET非捕获分组表达式，CTest没有正确应用排除，实际选180而非154。会话11973已正常Ctrl-C取消、退出1；CTest PID16544及测试子进程确认不在运行。日志44个Start记录，并不代表44个case集合全部执行；没有完整JUnit。误选的 `NcmaUiSampleSmoke` 为 `Not Run: could not find executable powershell.exe`，并未执行PS5、不计通过。原失败、日志和中断状态全部保留。原清理环境漏传PATHEXT还引发dotnet工具解析NullReference；新执行器补传该标准变量。`e-nonps5-selector-repair-first` 改普通括号表达式，CTest自身JSON双配置各确认精确154项；此步骤只枚举，不运行测试。今后每次启动前必须用CTest原生选集核对，不能只用PowerShell正则校验。
3. **冻结发现夹具污染。** 新执行器 `e-nonps5-debug-third` 在启动前复核失败，没有再次运行测试。完整审计发现仅 `Ncma.Ui.Tests/bin/Debug/net8.0/ui-authoring-tests/<GUID>/assets/Hud.ncmaui` 新增5文件；所有已有输入字节、5156单独文件、正式151文件和journal未变。Canvas还有同类潜在路径。旧文件不删除、不忽略、不重设旧冻结。`previous-input-audit.json` 保存精确差异；原生apphost部分失败包含 `editor_user_data_access_denied`，只读标签检查发现当前canonical source、正式EXE及捕获支持EXE继承Low。没有修改标签/ACL，也不能将所有失败一概归因于环境。
4. **实际修复。** Authoring/Canvas共用测试专用 `TestProjects.Create`，使用OS临时目录的独占随机叶目录，拒绝临时根落在不可变应用目录内，保留成功/失败夹具并输出路径；不清理、不重试。新增目录独立/唯一/类别拒绝用例，原Undo、历史、恢复、GPU、输入、文本等断言全部保留。新捕获 `4d79ccad4ed541cda38902a8d333e0ee`，5033文件；source SHA `DE1859B761EDC355D21CE8970B11E3190BB8E016777E282DAF57C72D1FE75D36`，manifest SHA `DD6D6196C248A47B443015CAC823399B91BB004F99E3AD8C726BEA21EA508CE2`。相对旧捕获精确4个UI测试路径变化。
5. **新源码验证。** `e-ui-fixture-repair-build-first` 全69托管项目独立本地离线还原＋无还原编译，Debug161.08秒/Release162.28秒，均0警告/0错误；不包含新原生、可选图或包构建。`e-ui-fixture-focused-first` 双配置原UI完整36例全部通过（5.42/4.82秒），保留原60秒超时与原生插件参数，没有CPU-only跳过；全1508托管产物树及原生插件文件集/哈希前后不变。专项使用此前原生插件，已证明原生源码与字节未变化，但不是当前捕获完整包/原生门禁证明。JUnit SHA分别 `E1C024BA28B45F678C39F4EC3EFF3E199DE729015F38D3AD69F2C2814B0C1B06`、`44CC737F5CBCF3AA060E844F980F396189A86736388819460C0AD2B2F366E17A`。

所有本轮进程会话均终态，旧失败证据保留。正式EXE `379AA113…` 和journal `F5BDC19D…` 未变，无部署、Git、模型调用、密钥访问或联网下载。**M8.6仍未统一验收**：下一步新捕获匹配准备/输入绑定、实际启动环境核对和冻结后重测156项/配置，24项PS5继续用户暂缓；独立apphost环境问题、完整SDK链、人工与E5-D欠项仍开放。不得把旧捕获回执重标为新捕获通过。以下为此前进度。

### 最新：非PS5执行分组与新捕获离线准备核验完成，尚未开测

同一 `f7059c63` 捕获完成CodeGen双配置10次离线还原，仅使用17个本地归档；128个实际还原记录、34个还原归档的SHA256/SHA512和每配置1177个私有包文件均已核验。原2项CodeGen的命令、属性及宿主哈希保持不变，离线环境绑定SHA `8070D481CA870710EB926C379F86B37F72A9CA3812C1856506DEF707487CE506`。证据：`out/verification/m8-6/e-policy-codegen-offline-first/{result,audit,test-environment-bindings}.json`。这不是原16条构建/2个发布测试通过。

`e-nonps5-execution-readiness-first` 重新枚举双配置CTest，完整180项命令/属性与既有库存逐项相等，明确分成2项CodeGen、154项其他可执行入口、24项用户暂缓。独立Python环境及输入回执通过原校验器重开，3个原测试入口的环境参数一致，检查后释放全部持有句柄；没有运行Python worker。生成的是未启动的执行配方，不是功能测试结果。两配置计划SHA分别为 `F695642BA81DE1DB813BCB93F5F7FF6E096221C4A61B074FEA9D35A82985DF20` / `C8A165E261A54D0365616C8C10D35B5371F2B101DDFCA245CDB1AC3A9C1D2A97`，报告脚本SHA `CEF39EC24C7469AD12AD3C98722BCB11B237157BE35862D810E930D4732D4F27`。

新准备与审计均终态退出0；正式151文件、journal、固定索引、Debug支持副本及捕获源码未变。没有PS5启动、策略修改、宿主替换、模型调用、部署或Git操作。`testsExecuted=false`、`launchEnabled=false`、`stageAccepted=false`；下一步仍需实际启动环境核对、最终完整冻结，以及Release测试前成套支持切换。以下旧记录中“新捕获CodeGen离线准备待迁移”已被本节取代，原测试未执行状态不变。

### 最新用户决定：暂缓PS5相关测试，其余入口不再等待PS5环境

精确选择记录：`out/verification/m8-6/e-user-ps5-deferral-second/selection.json`，SHA `B391A0CC0A68BD3C066C9A1CEBCFB6F62BCD8A9BD2D72453B76FA2707767BE72`。双配置原库存哈希与8个间接调用源码文件已核对；锚定排除表达式各命中精确24项，保留入口没有依赖被暂缓的CTest项。首次辅助工具仅因路径分隔符拼写比较失败，原脚本/失败记录保留；第二轮规范化绝对路径，原哈希检查不变。未执行测试，launchReady仍false。

用户明确要求“先跳过PS5环境测试”。当前双配置各180项保留完整登记，按原调用链将21个直接 `powershell.exe` 入口及3个间接启动生产PS5工作进程的入口暂缓：`NcmaWorkflowTests.CaptureHost`、`SourceCreationHost`、`SdkChain`。共24项记为 `not-executed(user-deferred-ps5)`，不计入通过；不修改原超时、断言、宿主或系统策略。

其余156项继续准备和测试，其中CodeGen构建/发布2项使用上节的新源码离线环境绑定，其余154项保留原入口。仅使用伪造runner路径、被动租约或启动前负例的纯契约测试不随PS5入口一起跳过。测试报告须同时列出完整180项、实际通过/失败/未运行以及用户暂缓24项；不能写成180/180或整阶段验收成功。待正常输入冻结和配置对应的支持副本就绪后，可以先执行非PS5部分，不再等待PS5环境。这不豁免其他依赖门禁，也不授权正式安装或Git发布。

### 最新：E 新捕获固定输入与原回执切换完成，运行目录和SDK入口绑定完成

继续使用 `f7059c63` /源码 `A3CCE0C0` /manifest `00F95671`，没有产品/配方源码修改。所有操作仅在唯一工作区内；未部署正式EXE，未运行功能测试、工作流worker、模型或联网下载。

**可恢复切换。** 先生成精确清单，完整备份13目录/947旧文件、8份当前索引，并暂存947新文件。只读dry-run核验路径、物理目录身份、无链接和完整字节集后，原目录移动到retired、新暂存目录激活；无删除、强制覆盖或自动回滚。12个可选目录含双配置AI、扩展及组合变体，另一个为Debug共享目录。52条不可变事件保留，旧947文件同时存在于独立backup和原目录retired。计划SHA `7A3CDCDE8E600711CB2DDF8D88675E9BB12845F71A87D15C34D526F6D55AFA89`。

原 `Prepare-AiVerification` 和 `Prepare-ExtensionVerification` 随后按原离线依赖回执运行，双配置各6图编译0警告/错误，活动文件与编译后文件完全一致。AI回执生成ID Debug `df5b0d3dec534cbb9d535847836a07f9`、Release `3788bb1436b44775ad056028298d90d9`；扩展回执 `61e3b6519aae49efaa6b711a6371cf38` / `5c53a26e902340c5adef02939b6aff98`。两个AI索引通过原File.Replace更新，原previous回执与独立备份均核验；六个新包索引不变。

新的活动输入检查点为 `e-policy-input-cutover-first/active-inputs.json`，SHA `C74C284C4D9D131DB21F6A8F3826286DF5B90779FBF9148EDAB9C4963C142199`。上一活动检查点及包审计中“两个编译索引未改”的描述已成为历史，不能用来核对当前路径。独立双配置共享快照各37文件准备完成，只有Debug激活；Release仍需成套切换。

**生成运行目录与CTest。** CreateNew暂存138个默认＋10个可选测试目录，共3790文件；没有覆盖历史捕获。最初每配置仅SdkChain的空DLL参数缺失，随后绑定原同源SDK声明与宿主。首次配置比较失败：CMake将Ninja工具长路径改为8.3短路径，使两条旧测试命令发生变化；`e-policy-current-test-binding-first` 的原失败、配置备份和库存保留，未执行测试。第二轮显式指定原CMAKE_MAKE_PROGRAM长路径，严格比较未放宽。

第二轮双配置各180项（21workflow＋159历史）、原超时/调度属性不变；仅允许已声明SDK、Git与N格式捕获ID参数替换，其余命令精确一致。原库存校验器全部ExplicitInputsConfigured=true，各141个直接托管DLL路径均存在。最新库存为 `e-policy-current-test-binding-second/<配置>/inventory.json`：Debug SHA `820AFC933CDBE01081672990C2140963C34A1B5307B9C787FD37837B49057A72`，Release `69E3B4C405949DE6B9F0FFCBB79C392D7E777A5FE72B54D3F43A85530CA74175`。Python三个原绑定保留，本轮未重新准备/重验其独立环境。综合检查点同目录 `checkpoint.json`，SHA `96B3B2821046F0E8E95DE6260CCFA02A68D147EB47AA76EF63E9CDFA157D2B17`。

**测试支持与静态检查。** 当前捕获内新建Debug测试专用 `source/out/bin`（145文件）与 `source/out/managed`（37文件），共182文件，不是正式安装。43个正式渲染资产字节与新双配置候选一致，不宣称代码/运行等价。原包上下文与编译回执校验器验证4个默认/AI组合；58个不同宿主声明的runtime/native/resources资产缺失0、未解析RID映射0。仅观察本机dotnet运行时列表，未实际装载宿主或验证全部P/Invoke/运行期路径。证据 `e-policy-current-runtime-support-first`，审计SHA `1C922CD007057107EFE9DBC3F8A05A5F3B966A659B6FB189EA21BE35FAD19A42`。

以上驱动均已终态；首次配置失败保持失败，第二轮为独立证据。正式151文件、EXE `379AA113`、journal `F5BDC19D`未变。下一步为**新捕获CodeGen离线还原/执行环境绑定、Release支持成套切换、最终完整冻结**，再在受支持原PS5环境具备时统一测试。当前PS5前提仍未满足，不能绕过/替换宿主或跳过后宣布通过。`fullRuntimeClosureVerified`、`testsExecuted`、`stageAccepted`均false，历史人工与E5-D欠项保留。以下各段为各自历史状态。

### 最新：E 新捕获完整候选包与模块包完成，六个包索引可恢复更新

同一 `f7059c63877c46589f402b244e4fd925`，源码SHA `A3CCE0C0CAA9F377205518A9733D8328A63F54FC19CE778DE23193C28198521B`，manifest SHA `00F9567187B17B67B98E12377B6439DF0AE6B1ACEBFE1652A37DF609D49EFE02`。未再修改产品或构建配方源码。

先独立备份六个精确索引并核验旧26候选/两个模块包，再完成双配置package/modules四份离线依赖准备。每配置package依赖1621文件、modules依赖1344文件；模块测试宿主编译0警告/错误，各14文件。没有联网下载或测试执行。

原 `Package-M2_7 -IncludeAI -IncludeExtensions` 从捕获副本新建原生输出、编译发布并烘焙着色器，Debug489.26秒、Release505.61秒；每配置13个完整未验收候选，共26包/2778文件，无捕获到的编译警告或错误。生成目录分别 `out/package/m2-7/Debug/02345a75546c4b82b94441551980a4a5` 与 `Release/793c6ea5dc844423a35740963664c761`。原模块发布入口4.30/4.20秒完成，各22文件，生成ID `640db9f6de264ca28e4fcbfc8e67cac5` / `60318d049a8b4f2980cc2dfe488ae3fc`。

六个固定包索引已按用户授权更新：m2-7、m8-4、m8-2各Debug/Release；独立备份与原生产器previous备份逐一核验。默认包索引沿用原受检先备份后写入，AI与module索引沿用原File.Replace；不宣称六文件整体原子事务，也未演练回滚。旧26候选和两个模块包保持原字节。

最终核验26包完整集合、manifest与实际依赖组合、两个模块包、四份原离线依赖回执及模块宿主；14个固定/历史输入目录、两个旧AI编译索引和两个旧不可变扩展索引未变。**固定AI编译回执和运行目录仍是上一捕获，不得把新包等同于整套运行输入已切换。** 六个包索引的新权威检查点是 `out/verification/m8-6/e-policy-current-module-packages-first/final-audit.json`，不要继续用旧检查点六个索引哈希验证现路径。

证据目录：`e-policy-package-inputs-first`、`e-policy-current-full-packages-first`、`e-policy-current-module-packages-first`。最终审计脚本SHA `8CC59D28753D64D9C14FECC2E8A4236DEC0722D34B7C3A33243C39C86C82442B`；82157/8150/94974/42951均终态退出0。正式151文件、EXE `379AA113`、journal `F5BDC19D`未变。未运行Editor/Player、功能测试、SDK链、Python worker、模型或部署/Git；`runtimeReady=false`、`stageAccepted=false`。

下一步仍需原固定验证回执、受检运行目录切换、SDK/CodeGen入口绑定和最终冻结。用户已确认没有现成受支持PS5环境，不再重复询问；不绕过策略、不替换生产PS5宿主，相关测试保留未执行状态。M8.6整体验收及历史人工/E5-D欠项仍开放。以下记录为各自历史状态。

### 最新：E 新捕获可选图与SDK输入准备完成，未执行SDK链

保持 `f7059c63877c46589f402b244e4fd925` /5032文件/源码 `A3CCE0C0CAA9F377205518A9733D8328A63F54FC19CE778DE23193C28198521B` /manifest `00F9567187B17B67B98E12377B6439DF0AE6B1ACEBFE1652A37DF609D49EFE02` 不变。本轮没有产品或源码配方修改；驱动和记录仅写入新的 `e-policy-current-optional-first`、`e-policy-current-sdk-first`。

**可选编译。** Debug/Release各独立离线准备固定6图，然后从捕获副本无还原编译AI Editor、AI contracts、扩展Editor/Player、AI＋扩展Editor/Player，共12图，全部退出0且0警告/0错误。每配置六个完整测试产物目录文件数81/68/85/68/85/68，共455；私有准备回执的1642个依赖文件重新打开核验。Debug依赖代次 `694f61cf7cc640ca9ed19f3aa5693580`，回执SHA `D04D587609F58E24BD9B3B34577946B96097128B58364BC4BB3BF83A7CF27FE7`；Release `4e12aac7560f4c86bcda4e856a47b696`，SHA `C21AAACF15BC3A06328576538C3C719162C0799CB0DB2FDC8351FDA0D3410403`。这些是私有编译产物，尚未激活固定测试目录或通过原生产器生成新固定AI/扩展编译回执。

**SDK准备。** 使用原 `Prepare-PackageDependencies -Scope sdk` 与 `Prepare-WorkflowSdkInputs`，双配置完整图编译0警告/0错误，112.94/116.65秒。随后仅执行原测试宿主的 `--workflow-sdk-prepare-inputs` 只读输入观察分支；不是SDK链、测试或worker。每配置1297个依赖文件/164063130字节回执重新核验，harness85文件、MCP7文件及7个原生工具EXE观察；工具路径仍为原显式本机选择，不扩张为完整工具链可信证明。

- Debug SDK依赖 `95c486da819449e4bb44b9052cc9c83c`，SHA `33A0AA9E8D7125048A08CBCE7E70174A7D39CB9FEE415A06518DC1D2D37128D5`；声明 `sdk-inputs/77c8373446cd4ee896d0c877d4d53449/inputs.json`，SHA `A6A17C2E45B51F5BEAEBD26D5A130359C9A6320462BF06FBCF7ECCE6AD3506B1`，回执SHA `185D1B8AEBAE8642A051CFF01283E4FB600925F8A5C3C31E5A6D397216622B4C`。
- Release SDK依赖 `304b9949ee1444869e69a7bdcdaa76d9`，SHA `464A66797C5056EB873BE361C062E6109ABCC271391FFAE76E02C9608D49FF19`；声明 `sdk-inputs/01100d4540e349ccbfe5b64abc9c1265/inputs.json`，SHA `630C60EAE5B94BFF25C78A61AD68A92857F4431899E58527503D780B5C95FBE3`，回执SHA `E84B992C2A998510301A549C7A99D9FB717C609913BA79AEBB84BEB4D9E21DE3`。

**最终核验。** 原依赖回执、源码/config/SDK声明/观察记录/编译日志哈希和完整产物集均一致。检查实际产品而非测试宿主的deps：每配置5个Editor/Player组合，AI只在指定Editor变体存在，扩展按开关存在，不包含Tests或gRPC测试传输依赖。默认编译1508文件/配置和原生产物仍匹配上一轮；14个保护输入目录、10索引、2个既有模块包及正式151文件/journal未变。最终审计SHA `9A06ADCA72F9F7AA83AEC85E282519456B9F093BDD95F65C21EB764651EA7C52`，记录 `e-policy-current-optional-first/final-audit.json`；未执行过的审计草稿仅将含混字段明确为 `pythonPreparationRevalidated=false`，不改变已有Python回执结论。99536/39590/2397/81391四会话均终态退出0。

尚未执行功能测试、实际SDK链、worker、模型、网络下载、发布/安装或Git操作；固定测试输入与SDK测试入口还未切到这些新产物。下一步需新捕获的完整发布包、原固定验证回执、CodeGen离线测试绑定和运行映射，再受检切换与冻结；PS5仍Restricted且用户无现成替代环境。`runtimeReady`/`stageAccepted`仍false，不能将编译/准备结论当作整阶段验收。历史人工/E5-D欠项保留。

### 最新：E 策略清理后的新捕获，默认全量托管与原生双配置构建完成

继续使用 `f7059c63877c46589f402b244e4fd925`（5032文件，源码SHA `A3CCE0C0CAA9F377205518A9733D8328A63F54FC19CE778DE23193C28198521B`，manifest `00F9567187B17B67B98E12377B6439DF0AE6B1ACEBFE1652A37DF609D49EFE02`），没有再修改产品/配方源码。证据新建于 `e-policy-current-base-first`，不覆盖之前的构建或失败证据。

**默认托管全量。** 枚举捕获中全部69个csproj并组成私有解决方案，不仅依赖根sln成员。显式单一本地NuGet缓存/清空继承源的配置、私有包目录，先离线还原后 `--no-restore --no-incremental` 编译。Debug总217.82秒（MSBuild207.94秒）、Release总161.69秒（MSBuild154.85秒），两者退出0、0警告/0错误。默认Editor/Player的实际deps未包含AI/扩展/测试依赖。会话82201正常退出。

**原生全量。** 先核对实际Ninja dry-run的all图不调用宽泛Build、dotnet、PowerShell或部署，再对同一捕获已有独立配置目录执行all。Debug/Release各322步，88.96/116.02秒，退出0且没有捕获到编译/链接警告或错误。原生成配置仍在 `e-test-policy-cleanup-second/inventory-<配置>`；并未调用托管custom target。会话46339正常退出。

**最终核验。** 每配置69份 `project.assets.json` 对应完整捕获项目集，唯一显式本地源/配置、私有包目录、项目引用边界及生成NuGet props/targets均匹配。每配置1508个托管产物与编译后清单一致，1332个私有包文件登记哈希；原生插件/夹具完整集合与构建后记录相同。两配置CTest仍180项、无缺EXE提示，但这只是原生构建后库存，不证明所有托管DLL参数、可选产品、SDK或传递运行依赖已就绪。没有执行CTest用例。

捕获与当前5032源码一致；13个原活动输入目录＋1个保留历史目录、10个固定索引、两个既有模块包及正式151文件/journal未变。三个新驱动均终态：82201/46339/87525退出0。最终审计脚本SHA `0400A176FC07092C84F24560DE63100D21CE2FDBCCD036A0D5F9A40BA72519D3`，记录 `final-audit.json`。正式EXE仍 `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`，journal仍 `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1`。

**尚未完成：** 当前捕获的可选AI/扩展图、SDK声明、发布包、CodeGen离线测试绑定及最终运行映射/切换/冻结。旧 `cfb6e8f7` 的对应回执不能改作新源码证明。本轮base审计中的 `pythonPrepared=false` 表示本轮未进行Python准备或重新核验，并不撤销之前独立Python准备及其回执；后续按独立来源再次核对。PS5仍Restricted且用户无现成替代环境。未进行功能测试、worker/模型调用、网络下载、部署或Git发布，`runtimeReady`/`stageAccepted`仍false，历史人工/E5-D欠项保留。

### 最新：E 测试策略覆盖清理，新源码需重新完成匹配门禁

**源码修改仅四个文件。** 原文件完整备份到 `e-test-policy-cleanup-first`，保留已有工作区修改。`CMakeLists.txt` 的10处调用删除 `-ExecutionPolicy Bypass`，覆盖展开后的19个CTest入口及托管构建目标；`Test-AiCandidateInputs.ps1` 的CrossShell子进程同样移除该参数。原PS5/PS7宿主、参数内容、断言和超时不变，没有设置系统/用户/进程执行策略，也没有用PS7替换产品PS5 worker。

`M8_6-TestInventory.ps1` 将禁止执行策略覆盖的检查扩展到历史及新增PS5/PS7入口，拒绝完整名称、大小写变体、`-ep`、`-Ex`及冒号形式。仍允许CTest对尚未构建的历史原生入口省略command，只作库存记录，不将其视为运行依赖已验证；新增入口原有严格宿主/绝对路径/目录校验不放宽。`Test-M8_6Inventory.ps1` 新增3组回归：历史宿主/参数变体拒绝、普通历史调用保留、缺原生EXE时仅库存语义；总10组，**均未运行本轮测试**。

**新捕获与旧证据严格分离。** `f7059c63877c46589f402b244e4fd925`，5032文件，源码SHA `A3CCE0C0CAA9F377205518A9733D8328A63F54FC19CE778DE23193C28198521B`，manifest SHA `00F9567187B17B67B98E12377B6439DF0AE6B1ACEBFE1652A37DF609D49EFE02`。与旧 `cfb6e8f7` 捕获逐项比较，只有上述四个源码文件变化。此前全部69项目/原生/可选图/SDK/包与固定回执保留，不改写、不换stamp，**不能作为新源码验证结论**。

首次准备（会话51157退出1）已产生新捕获和Debug配置，但比较器未映射 `NcmaUiSampleSmoke -NativePluginRoot` 的新独立输出目录，原日志/result/脚本保留。这是辅助比较失败，不是功能测试执行失败。第二轮 `e-test-policy-cleanup-second` 沿用同一新捕获、创建新配置目录；仅加入精确旧到新输出根映射，其余比较仍逐项执行，不忽略任意参数差异。

双配置配置/库存观察成功，会话14584退出0，驱动SHA `8677F034060A3F0AC7E56B92A90643652F13BDEDFE12646CDEE50345559FB804`。各180项名称、原TIMEOUT/RUN_SERIAL/LABELS/DEPENDS/DISABLED/PROCESSORS/RESOURCE_LOCK不变；21个原PS5入口保留，19个旧覆盖参数移除。脚本argv仅允许删除原两项策略参数及精确捕获根/manifest/原生输出根替换。Debug库存SHA `F170FE67C92B8199AC0947B35F72FD1D06B79F8F91EC58DC3F01DB3D98F92905`，Release `34BB832F58510D6414509BAABB234890053F2DE322D59E6B4DEAB338D9E81A91`，均在 `e-test-policy-cleanup-second/inventory-<配置>.json`。

**这些只是新源码侧的未构建库存，不是固定测试入口切换或完整运行冻结。** 各配置15个原生入口没有EXE，SDK链新同源声明未配置；旧二进制/SDK声明没有冒充新源码前提。独立Python环境经原入口重开校验后绑定，仍有自己的捕获与回执，不属于引擎5032文件源码证明。PS7三份脚本语法检查0错误；实际PS5仅通过内置Command调用AST解析器观察同三文件，0解析错误、未执行脚本，策略仍Restricted。

人工恢复入口 `Recover-Editor.bat` 还含历史Bypass参数，本轮测试入口清理未扩张到恢复/安装流程；它未执行，也不得作为当前环境的绕过方式。不能宣称全仓库所有覆盖参数均已移除。

正式151文件、EXE SHA `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`、journal SHA `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1` 和旧固定索引不变。未进行完整托管/原生构建、发布、功能测试、worker/模型调用、下载、部署或Git操作。下一步需要新捕获的完整匹配构建/SDK/包及运行输入、受检切换和冻结，并取得受支持原PS5执行条件；整阶段及历史人工/E5-D欠项仍未验收。

### 最新：E 捕获ID接线修复、完整执行准备清单；PS5前提仍阻塞

用户确认没有现成可用的受支持PowerShell 5.1环境。本轮未改安全策略、未使用Bypass、未替换产品worker宿主，也未执行功能测试；继续完成静态和输入准备工作。

**发现并修复真实接线错误。** `e-unified-execution-readiness-first` 调用原库存校验器时因 `Inventory incomplete` 失败。实际 `NcmaWorkflowTests.CaptureHost` 参数为 `cfb6e8f7-b0fa-4e95-bc03-393a36ccb88a`，原 `Program.cs` 与库存校验器都要求 `Guid.TryParseExact(...,"N",...)`。根因是旧准备驱动将Guid对象直接插入字符串，默认生成D格式；不是产品解析器问题，不修改或放宽断言。首次脚本/result保留，退出1、测试启动数0、正式文件和journal不变。

新 `e-capture-id-binding-first/Bind.ps1` 在每配置完整备份并核验7个生成配置文件后，通过原CMake配置入口只把同一ID转换为 `cfb6e8f7b0fa4e95bc03393a36ccb88a`。Debug/Release各180项名称、全部属性/超时及179条其他命令完全不变；原生插件/夹具、固定包索引和源码仍匹配原基线。两配置原库存校验均报告显式输入已配置，**不是运行依赖齐备或测试通过**。修复脚本SHA `281E4BE8843DD71831DBC420DD19506E6DEAA0D892975D1B371DB1E6850CAA40`，会话55518退出0。

最新活动CTest库存位于 `e-capture-id-binding-first/<配置>/inventory.json`：Debug SHA `A064FE697A453199AD2AD85A1B661BE4936C2382BBA7692A2BC005ACDE5115A6`；Release SHA `BB4F608D9C9A7A80A28F092D5738207EB0F6073F97035B162842DF32275293BE`。此前 `e-python-test-binding-first` 库存保持原样，已是这一次接线修正前的历史证据。

**完整执行准备清单。** 新 `e-unified-execution-readiness-second` 重新运行CTest库存观察（不运行测试），与上述修正后库存逐项一致。将各180项精确划分为CodeGen构建/发布2项和其余178项，无新增、丢失、重复或缩小验收分母。原CodeGen命令、属性及harness字节与此前离线绑定一致；重核17个本地feed归档/配置和每配置1177个私有包文件，物化清空继承环境、只恢复受检本地源/包路径的 `ProcessStartInfo` 准备配方，但**从未Start该进程**。不能据此声称真实测试子进程继承环境已验证或16构建＋2发布测试通过。

Python环境通过原入口重开核验，Debug专用运行副本、正式151文件/journal及源码5032输入不变；没有Release激活、最终完整运行冻结或连续锁证明。第二轮脚本SHA `78237C9D0B738DE510794D490D0ADF50015F0345346B76699FF9A39A5D424D28`，会话46424退出0；两配置执行准备清单SHA分别 `8AE46D61D12064727FC2122E6DEB4EDE2037C5F656929754D8135AE7B08633E7` / `0D6A3FE0A6F9376CA51113B83C54DD3FEE5F7AC1873502DB954BBB07187BF326`。三份新证据脚本PS5语法解析0错误，仅AST观察，不是PS5脚本执行。

**仍不得启动整体测试。** 只读内置命令确认PS5 `5.1.19041.6456`、`Restricted`、FullLanguage、各scope Undefined；各配置21个直接PS5入口中19个带旧ExecutionPolicy覆盖参数，均未执行。嵌套PS5调用并不止这些直接入口，不能从这份清单推断其余159项已满足运行条件。所有准备清单明确 `launchEnabled=false`、`testProcessesStarted=0`、`stageAccepted=false`。仍需最终完整冻结、Release成套输入切换、受支持原PS5环境与旧调用策略处理；不把本次接线修复或准备成功当成M8.6验收。正式EXE SHA `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`、journal SHA `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1` 未变。未提交推送、部署、下载、运行模型或访问密钥；历史人工/E5-D欠项仍分别记账。

### 最新：E CodeGen离线依赖与Python正向准备完成，模块测试输入已绑定

**CodeGen独立准备。** 保留原16条构建命令及2产品发布模式，不把它们改成无需还原的弱化用例。在 `e-codegen-offline-preparation-first` 从已核验包复制17个归档构成唯一离线源，生成清空继承源/回退源的专用NuGet配置，双配置分别使用私有包目录。对子进程以明确环境传递本地源、配置、包路径和离线审计设置，实际执行clean/runtime-leak/publish-leak三种合成项目形状及原Editor/Player共5图×2配置，**仅Restore目标**。

10次退出0；实际读取128个 `project.assets.json`，确认项目、唯一源/配置、隔离包/输出路径；两套17个实际包归档SHA256与复制源相同，SHA512与NuGet元数据一致。原编译测试产物、182运行文件、正式安装均未变。准备脚本SHA `545D87D4A01E2F7287E3ACE3A0EB0A4AE8D92ADD3D1AE7EF4529D7D80C98218F`；独立审计SHA `0641F68900AE780120F9D9837D89979F214A49506F1865B79248BC351063AE75`。`test-environment-bindings.json`（SHA `9F49ED2FA168A14D71714878A82616223260982000347BF2B6F84E040A39A69B`）保存两个原CTest命令与环境清单；尚未实际绑定执行器/运行原测试，不是16＋2用例通过，也不是SDK/NuGet完整沙箱证明。

**Python精确wheel已经找到，不再缺包。** 递归pip缓存未找到同名wheel，但进一步仅按SHA检查HTTP响应体，675个body中对预算范围内453个/219743881字节计算哈希，找到与锁文件完全相同的两份归档。`grpcio-1.84.0-cp312-cp312-win_amd64.whl` 为5253534字节，SHA `49717E857899F4136D7657BF5ADED61AC479110A075438290923A4D86AF7CD02`；`typing_extensions-4.16.0-py3-none-any.whl` 为45571字节，SHA `481CAA481374E813C1B176ADA14E97F1F67A4539CE9CFEB3F350D78D6370C2E8`。缓存路径和副本来源写入新证据，没有联网下载、改缓存或读取凭证。

首次 `e-python-positive-preparation-first` 驱动误把独立Python锁文件定位到引擎capture/source/python，执行解释器及复制前即失败；原脚本/result完整保留，不能改判通过。修正驱动使用独立worker四文件的实际读锁/字节，新建 `e-python-positive-preparation-second`；**Python并不属于5032文件的引擎源码捕获**，两种捕获分别记账。复制的10个配方脚本与引擎捕获对应脚本一致。

原 `Prepare-PythonWorkerInputs` 产生 `python-inputs/5ada437a298d46a5acc9937edb91433c`：2443文件/72197372字节，输入SHA `8EA81DE89EEDD2EAA6B7C65EB5236754C9180F4E346454DBDEE73EF295340D36`，回执SHA `907417F875655C64B21DF2356EAD7370B134AE7D234C6F46BCCABEA975A05A7D`。原离线环境入口使用捕获CPython和内置pip，`-I -B`、无全局包/网络/源码构建，精确安装两项，产生 `python-environments/65e54d5107ee4418b9b409b148a210e3`：86文件/13491831字节，回执SHA `B17D3FCC356D237B554A984C2ADD9E6558D5C68516F1045779F50B3581EF0108`。两份原回执均重新打开并完整核验；没有导入grpc原生模块、启动worker/模型或执行测试。第二轮驱动SHA `1A2C47E3663DB1E2A621CAD8EB2AB9447A9399BF2F2016D5AFA8C52B91850B4D`。

**CTest接线。** `e-python-test-binding-first` 每配置备份7个生成配置文件，仅通过原CMake选项给Grpc/RelocatedClient/NoPythonProducts添加环境路径及回执SHA；各180项名称/超时/属性不变，其余命令完全相同。最新活动库存为该目录的Debug/Release `inventory.json`；旧 `e-current-test-binding-first` 库存是接Python前的历史观察，其他输入检查点仍有效。驱动SHA `432E8D9214AB0695821EE09C1D10AE2161342093A9A95132F647C54E50711635`。输出字符串的执行前解析失败保存在 `Bind.parse-failed.ps1`，修正后进程退出0，未改产品源码。Python共享读锁在准备结束释放，不能跨未来的FileShare.None隔离夹具持锁。

引擎仍是 `cfb6e8f7` / source `3882990A…`，正式151文件、EXE `379AA113…`、journal `F5BDC19D…`及当前包索引均未变。没有阶段功能测试、部署、Git提交推送、模型调用或安全配置变更。原PS5有效策略仍Restricted；只读库存发现19个旧CTest入口及 `Test-AiCandidateInputs` CrossShell子调用携带ExecutionPolicy Bypass，**未执行，不能用其绕过现有策略**。已询问用户现有受支持PS5环境；此前wheel目录问题不再阻塞。最终运行输入冻结、离线测试环境执行接线、成套Release切换、旧调用策略处理及完整测试仍待完成，M8.6未验收，历史人工/E5-D边界不变。

### 最新：E 补齐捕获内测试运行副本，完成静态资产与原包上下文核验

核对 `Ncma.Editor.Services.Tests/Program.cs`、相关AI/动画/环境夹具及Transport实际读取路径，确认它们使用传入的捕获源码根目录中的 `out/managed` / `out/bin`；此前这两个目录不存在。现从同一 `cfb6e8f7` 的完整Debug默认候选与共享快照，只用CreateNew分别复制145与37文件，共182文件。目标均为 **当前捕获/source/out** 下的测试目录，不是canonical正式安装；完整来源/目标集合及源码字节核验通过。旧捕获不动，没有覆盖、部署或重新标记旧证据。

另完成两项准备审计：

- 直接调用原只读 `Open-PackageVerificationContext`、`Assert-AiVerificationReceipt`、`Get-AiCandidateIndexedProduct`、`Assert-AiCandidateManifest` 和捕获绑定校验，对Debug/Release × 默认/AI四个组合核对原索引、回执、路径及完整字节，全部通过；未运行任何测试脚本、产品EXE或模型。
- 双配置141直接托管入口归并为58个不同宿主，逐项读取实际 `.deps.json` / `.runtimeconfig.json`，映射其runtime/native/resource声明文件：缺失0，未解决RID目标0。保存逐文件SHA及已安装.NET运行时清单（包含8.0.28）。这是声明资产存在性审计，**不是实际CLR解析/加载、PInvoke闭包、SDK子进程运行或完整传递依赖验收**。

还验证正式安装43个 `assets/` 渲染资源与当前Debug和Release默认候选逐字节一致，可据此明确旧夹具只读资源来源；不能推导正式安装的代码或二进制已更新。正式151文件、EXE `379AA113…`、journal `F5BDC19D…`、当前索引与13活动树/历史共享目录均未改变。

证据 `out/verification/m8-6/e-current-runtime-support-first`：`plan.json` / `result.json` 记录新测试副本；`package-context.json`、`managed-runtime-assets.json`、`dotnet-runtimes.txt` 和 `audit.json` 记录只读审计。暂存脚本SHA `7586CF4CFE2E5D0EB2E864CC78C20A7662E0CDE8140363E78FB307D89BE97046`；审计脚本SHA `C040325AE59F7AD08C652B12C3FF779ACB32F8C3AEB80D2F8D3128BAE5A737AE`。两个进程均退出0。原准备检查点对其原范围仍有效，本记录是新增运行副本的补充，不重写此前清单。

**未完成项**：canonical共享目录、捕获共享目录和捕获默认Editor副本现在都是Debug；Release测试前必须核验备份后成套切换，不能只换入口DLL。源码检查还发现旧 `BuildContractTests` 的16构建命令与2发布模式直接调用build/publish，未传 `--no-restore`；其测试专用的本地唯一还原来源/隔离输出尚需明确核验，不能在任意继承源下直接启动。Python精确wheel、正向环境和原PS5条件仍缺；完整运行检查与最终冻结未完成，仍未执行整阶段功能测试/部署/Git操作。E5-D和历史人工项不变。

### 最新：E 当前完整包、生成测试目录与SDK入口准备完成；尚未功能测试

沿用同一 `cfb6e8f7b0fa4e95bc03393a36ccb88a` 捕获、5032文件、source SHA `3882990AF6E23B5D7AD41586A6EB236856D2A96AEE5D9946520B8E6B271BD292`。没有修改产品或构建脚本源码，也未把其他代次的证据重新标记为当前通过。

1. **真正调用原完整打包入口**：当前捕获原生源码独立配置/编译，复用已核验的显式离线依赖，原托管发布、ShaderCook和资源/许可证封包全部执行。Debug115.52秒、Release134.97秒，各13个完整候选包（不是此前私有托管输出），总26包/2778文件；未发现编译器警告/错误诊断。完整manifest文件集/字节、来源与AI/扩展依赖核验通过。Debug代 `75cef85f46ee470694d86089424b3468`，Release代 `8be0e5229d89421ba09f3a2f6143cb79`。未执行Editor/Player验证或烟测，仍是未验收候选。
2. **真正调用原模块打包入口**：同源离线 `Package-ModuleClient` Debug4.44秒、Release4.30秒，代 `47a3725cb5d74df09bf56a4132d4fd90` / `ed3ef6a3c2fc4029ae1fa703257c84dc`，各21负载＋manifest共22文件。原精确7DLL、NuGet档案SHA512及实际runtime资产校验、声明/许可证来源复制保持；没有Python解释器安装、worker或模型执行，来源记录不等于法律批准。
3. **授权范围内更新包索引**：六个包索引路径已改变，其中两个此前不存在；既有索引先CreateNew独立备份，再由原生产器更新并保留原previous备份。独立备份/原备份逐项相等，旧模块包未变。不删除旧索引备份或旧包，不声称演练过恢复。早期 `active-inputs.json` 的包索引哈希是历史值，不能再据此断言当前包索引。
4. **生成目录暂存**：双配置69项目各自映射到对应TFM/配置的空生成目录，共138树；另将当前可选Editor/Player完整产物复制到实际CTest的10个空目录。合计148树/3790文件，只用CreateNew并核对完整来源与目标集合，拒绝已有目录，不覆盖旧源码或旧产物。捕获清单中的源码字节不变；新生成目录不是新增捕获源码。
5. **显式绑定SDK测试**：每配置先备份7个既有CMake生成配置文件，再绑定当前SDK声明、独立扩展测试程序集、捕获ID及既有单链接Git。只做CMake生成、`ctest -N`和JSON库存，不构建/运行SDK工作流。各180项名称、原超时及全部属性不变，各141个直接dotnet程序集入口均存在。**这只证明直接入口存在，不证明Python、共享目录、包上下文和所有传递运行依赖已就绪。**

证据位于 `out/verification/m8-6/` 下：

- `e-current-full-packages-first`：两配置完整候选清单、原日志、索引前镜像与结果；原驱动SHA `DEA676B02FF218220AE5F6C31760F46BF5C973BF00E7EF3E05246B1F0EFB4FDF`。
- `e-current-module-packages-first`：两配置模块包、原/独立备份及保护核验；驱动SHA `62ACA40F88E1A1D21375F4D06C1508E7DBD56A148860DE5EBC55BD02B377EB13`。
- `e-current-runtime-staging-first`：完整148树计划、3790文件来源/目标、直接入口检查；驱动SHA `3444D0124998C2FBBB8D546000E1B47EF7AC48FE1758D752D13041C70AD263C0`。
- `e-current-test-binding-first`：双配置原配置备份、新CTest库存及独立审计；绑定脚本SHA `4AA5301582E86308B659DE023793A22C54E5492D56CABD4DADF07FC82984EB1E`，最终审计脚本SHA `2F5D45E30192D97DF4FE29F9A93C030B18D69F01BD474F81012EFE7DDD548C06`。

**最新准备检查点：`e-current-test-binding-first/checkpoint.json`**。它引用当前完整包、模块包、暂存和CTest清单及各自SHA，保存当前8固定索引与2扩展代次索引；13活动树仍使用原cutover检查点，旧947文件在backup/retired两处重新核验。最终确认26候选包＋2模块包、148树及来源、13活动树、旧模块包与备份、当前5032源码、正式151文件/journal一致。它是准备检查点，不是完整运行输入冻结或阶段验收回执。

正式EXE SHA仍 `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`；journal仍 `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1`。模块、暂存、绑定、最终审计会话均退出0；最初完整包工具输出被截断不是构建失败，已按结果文件与进程状态确认结束，没有重复启动。未执行功能测试、部署、Git提交推送、模型调用或秘密配置读取。

**下一步**：剩余传递运行依赖、共享配置切换与包上下文核验 → Python正向离线环境准备/绑定 → 完整输入冻结 → 整阶段双配置测试。Release共享快照尚未激活。只读复核原PS5仍Restricted（各scope Undefined）；指定pip缓存根目录无两个锁定wheel，不能推出全机不存在；已再次询问本地目录，不自行下载/改策略/替换worker宿主。缺依赖不跳过后宣布通过。历史M8.4人工、原子保存与E5-D性能欠项仍单独保留。

以下“最新”是此前步骤的历史记录。

### 最新：E 已获授权并完成本轮测试目录与原编译回执切换

用户明确允许“完整备份并核验旧测试输入后，可恢复地更新固定测试目录及对应回执/索引”。此前等待此授权的状态结束；授权不涉及正式EXE、用户配置、密钥、旧源码归档、删除旧证据或绕过执行策略。

本轮精确范围：12个Debug/Release可选测试目录＋canonical `out/managed`（先用Debug），旧/新各947文件；盘点8索引路径，其中6个现有文件完整备份、2个缺失状态记录。原目录/新来源完整清单、祖先与重解析点检查、单链接文件读锁及目录物理身份均在切换前核验；先CreateNew独立备份与暂存并逐项比对，再只读预演精确13对路径，最后原生PowerShell无Force地移动旧目录到本轮retired、移动暂存目录到固定路径。52份不可覆盖事件记录保存每步意图与结果。没有删除旧数据；947旧文件同时保留独立backup和原目录retired副本。

随后真正调用原 `Prepare-AiVerification` / `Prepare-ExtensionVerification`，复用同一 `cfb6e8f7` 捕获及已准备离线依赖，禁止还原。双配置各6图编译均0警告/0错误；原复制函数确认固定目录与本次编译完整字节一致，未放宽原根路径/文件集约束。原AI生产器以既有File.Replace更新两个managed-inputs索引、保留previous-receipt；扩展回执使用原新代目录，未伪造/改写旧回执。

| 配置 | AI原回执代 | 扩展原回执代 |
| --- | --- | --- |
| Debug | `c3d168ea364e4e9bbf56d90298489542` | `a048e27f1a434d4b811f59b94232d52c` |
| Release | `ad8c8890052047dc9ba4b6def8b9c0e4` | `8374e82b38bd4eac969dee5c3a29ab84` |

证据：`out/verification/m8-6/e-authorized-input-cutover-first`。计划SHA `0E2CA6A60DC7702CD24C1CC3870D8D5360A2B792A0F18597CF8CB7BEC2CBF1E6`；最终审计脚本SHA `F31AE9B4095DCD108E7A6F44539F2F5E70F9FC52D8F32CFC7D2B4E6EBA328473`。最终核对13活动目录947文件、两份旧数据保留、原回执/日志/依赖重开、当前5032源码均通过；正式151文件、EXE `379AA113…`、journal `F5BDC19D…` 未变。六个包索引路径未变，历史 `f34d20d7` 捕获中的共享目录未动；Release共享快照尚未激活。

**后续必须使用新的活动基线** `active-inputs.json`（SHA `19378B0DE4EF560D9B8B4889893B939E8F27856C8634EFF24FA0516C0035D109`）。旧14树报告仍是有效历史证据，其中被替换的旧字节应在backup/retired核验，不能继续拿旧清单断言新固定路径，也不能重写旧报告。备份恢复没有实际演练，不声称恢复测试通过；若需恢复，先按事件及实际目录身份重新核对，再受检操作，不删除/强制覆盖。

本轮未执行功能测试、完整候选包生成、剩余默认运行路径绑定或正式部署/Git操作，M8.6未验收。下一步在已授权范围继续完整包与运行绑定/冻结，不再重复询问同一切换许可；Python精确wheel及受支持原PS5前提仍单独待解决，历史M8.4人工与E5-D边界保留。

### 最新：E 当前共享运行文件双配置私有快照完成，未激活

沿用 `cfb6e8f7`，核对当前默认托管构建各1508文件后，提取Gameplay8、MCP7、Transport测试宿主20个文件，加NcmaNative/NcmaPhysics两个原生库，映射为原共享目录37文件布局。仅在新 `out/verification/m8-6/e-current-shared-snapshot-first/Debug/shared` 与 `Release/shared` 中CreateNew复制；事前校验全部来源并保留文件/祖先句柄，复制后再核对完整文件集、字节与来源未变。

Debug37文件/12378756字节，Release37文件/4421812字节，总计74个新副本。Physics根DLL与此前记录的同源插件哈希一致；NcmaNative来自当前源码/配置对应的原生构建目录，本轮才新增哈希检查点，**不称其为此前完整冻结的一部分**。它们不等于已验证的进程运行依赖闭包。

证据目录保存 `plan.json`、两配置 `snapshot.json` 和 `result.json`；脚本SHA `0BC3E0F7F28FB2EE1587606C4F8C5CDD5070CAECABD0EBF6693444227FAA824D`。进程退出0，当前5032选定源码匹配捕获，正式151文件/journal、14历史输入树和8索引路径状态/字节不变。未写canonical或历史捕获中的out/managed，没有编译、测试、启动运行产物、完整打包、部署或Git操作。

剩余前提本轮再次只读核对：原Windows PowerShell有效策略为Restricted，各scope为Undefined，未改策略或采用绕过；指定 `C:/Users/liu/AppData/Local/pip/Cache/wheels` 根目录仍无 `grpcio-1.84.0-cp312-cp312-win_amd64.whl` 与 `typing_extensions-4.16.0-py3-none-any.whl`，不作全机不存在推断。固定输入/包索引的备份核验后可恢复切换仍未收到确认。新快照不授予切换权，完整运行绑定/冻结、Python正向准备与整阶段功能测试尚未完成。

### 最新：E 当前可选 gRPC 私有发布与只读切换盘点

同一 `cfb6e8f7` 捕获，复用当前模块依赖 `ffe7d767…` / `d3d05812…`，由原 `Open-OfflinePackageDependencies` 严格重开，未重新还原。原固定 `publish-library` 参数＋`--no-restore` 发布到新私有目录，Debug1.19s、Release1.17s退出0，无编译器警告/错误诊断。各10文件、精确7个运行DLL；独立只读来源审计核验5个实际NuGet压缩包SHA512、所选5个运行DLL的SHA256与捕获锁/`.deps.json`一致，记录9个notice/nuspec来源哈希。尚未复制这些声明，不作许可证法律结论。

本轮**未调用原Package-ModuleClient完整入口**，没有生成其模块包manifest/索引，不把私有发布当成完整模块包。当前14文件Grpc测试程序集各自未变，发布后依赖回执重新打开成功；当前5032源码、正式151文件/journal、14历史输入树、六固定索引路径和两份旧模块包均保持不变。没有运行输出、Python、worker、模型、apphost、功能测试、部署或Git操作。

证据：`out/verification/m8-6/e-current-module-private-first/result.json`，运行报告脚本SHA `EA72296AC4343EEB21BC91128C0E7ABF78B86D0511CB811499A44B6E3852E44C`。`activation-inventory.json`由只读盘点脚本生成（SHA `899ABE14731F052FE3411F357558542500DCDD12C888F56A98EB4E9BB7D59C24`），明确12个原固定测试树均与当前私有替换来源不同，记录各自完整文件集、8个准确索引路径及37文件canonical共享目录。

盘点不是备份、切换授权、持续文件身份租约或完整切换方案。尚未执行移动/覆盖/删除；执行前仍须重新核对精确身份、路径、链接、文件集并完整备份、验证可恢复性。当前共享目录的新替换快照尚待准备；历史捕获中的共享目录须保留不动。固定输入/包索引切换确认、精确Pythonwheel和受支持PS5条件仍未取得，M8.6继续未验收。

### 最新：E 当前同源私有托管发布完成，整包与固定索引未切换

仍使用 `cfb6e8f7` / 5032选定文件 / source `3882990A…` / manifest `FB8CA9A2…`。本轮没有修改产品/测试/配方源码，单独补齐当前捕获的打包依赖与托管发布产物，不复用旧 `92e8928a` 的Release结果当作当前证据。

- 原 `Prepare-PackageDependencies -Scope package -IncludeAI -IncludeExtensions` 双配置各10固定作业；仅显式本地缓存。Debug准备24.20s、Release23.30s，每份1621文件/165654448字节，发布后以原消费函数重开核验通过。
- Debug依赖 `package-dependencies/2ee58806e1f44ebdadba8a1fe9301d49`，回执SHA `90DF5F60AD34D67D894C74F31DE7D83D986BB617DC5F77402134089F076BEAD2`；Release `854a4104ccae4aa59b2ce91538da8d27`，SHA `CDE31C8CEA18506B00E5ED8A8EC36ED73F74058C849C46ABAD5AA29D79EE7232`。
- 每配置2工具构建＋8托管发布，固定 `--no-restore`、私有ArtifactsPath/输出，全部20任务退出0、无编译器警告/错误诊断。累计各任务耗时Debug367.60s、Release368.80s，不含准备/前后审计。没有运行任何生成的DLL/EXE。
- 每配置完整输出文件数：Gameplay8、ShaderCook17；默认Editor65、ImportWorker9、Player51；AI Editor71；扩展Editor69/Player55；AI＋扩展Editor75/Player55。重新核对全部20树的文件集/字节；16产品逐个通过原 `Assert-AiCandidateDependencies`，两种扩展变体的Editor/Player `Ncma.Extensions.Hosting.dll` 哈希一致。

证据 `out/verification/m8-6/e-current-package-dependencies-first`：原准备result、双配置managed/result与全部日志/binlog/products清单、最终audit。审计脚本SHA `7712C6EB1E71B3BAC35B262361488F5C1D7FD5B8B08FCA23ED0CBD144C64F062`。当前5032源码仍匹配捕获，正式151文件/EXE `379AA113…`/journal `F5BDC19D…`、14历史输入树不变。六包索引路径存在状态/字节均未变：Debug m2-7与m8-4仍缺，Release两者仍为历史版本，m8-2两配置旧索引保留。初次只读索引探查命令管道语法错误发生在IO前，改正后读取成功；没有将其作为构建/测试结论。全部本轮进程退出0。

**范围限制：** 私有托管publish不是完整候选包。本轮未调用整条Package-M2_7（会更新固定索引），未进行原生打包构建/ShaderCook执行/资源组装/清单签收/实际apphost/功能测试/正式部署/Git操作。完整原包生成、固定输入/回执切换、运行绑定与冻结尚未完成；可恢复切换授权仍待用户回答，不从自动继续推定同意。Python精确wheel与受支持PS5条件仍缺，不绕过、不跳过后宣布通过。保留M8.4人工和E5-D延期边界；M8.6未验收。

### 最新：E 同一捕获的可选图与 SDK 输入准备完成，未切换固定入口

继续使用下节的 `cfb6e8f7b0fa4e95bc03393a36ccb88a` / 5032文件 / source `3882990A…` / manifest `FB8CA9A2…`，没有改产品、测试或配方源码。本轮只新增独立证据与文档。

| 私有构建范围 | Debug | Release |
| --- | --- | --- |
| AI Services.Tests | 90.38s，0警告/错误 | 91.77s，0警告/错误 |
| AI.Contracts（AI.Tests） | 75.03s，0警告/错误 | 74.77s，0警告/错误 |
| Extensions Services.Tests / Player.Tests | 95.02s / 10.83s，均0/0 | 95.14s / 11.05s，均0/0 |
| AI+Extensions Services.Tests / Player.Tests | 95.70s / 10.71s，均0/0 | 95.26s / 10.86s，均0/0 |
| 原 SDK 输入准备（编译＋观察） | 87.52s，0/0 | 86.78s，0/0 |

可选图使用原 `Prepare-PackageDependencies -Scope verification` 的固定6作业，独立本地缓存还原后 `--no-restore`；每个新variant的首图全重编，Player只复用同一私有variant的当前依赖，与原扩展生产器一致，不是借旧产物。每配置6个完整测试树文件数为81/68/85/68/85/68。没有调用会激活固定路径的原验证生产器；旧固定12树未覆盖，不能把私有编译结果称为已交付的原测试回执。

SDK仍走原 `-Scope sdk` → `Prepare-WorkflowSdkInputs`，显式选择既有单链接Git；原观察器生成16字段声明，读取实际harness85/MCP7/Gameplay8文件与7个原生工具信息，不执行SDK链/原生工具/工作流。新声明：Debug `sdk-inputs/30a2d2a064f74d988ee7979a3f960897`，Release `sdk-inputs/64df37ddd1c2486ea57cfeccfe13bb3c`。

证据：`out/verification/m8-6/e-current-optional-first`、`e-current-sdk-first`。最终审计脚本SHA `24CA2242CCA822EAFD29FC75662626D58907F80CAE835BA91664882BB7ADE52A`，重开两配置verification依赖各1642文件、SDK依赖各1297文件；核验12树、SDK回执/声明/日志/全产物、实际Editor/Player的AI/扩展依赖组合、默认各1508文件及原生产物未变。当前5032选定源码匹配捕获；正式151文件、EXE `379AA113…`、journal `F5BDC19D…`、14历史输入树与两旧模块包未变。所有本轮进程正常退出0。

**未完成/不作通过结论：** 未运行任何功能测试、SDK链、Python、模型或apphost，未发布部署/提交推送；`runtimeReady=false`、`stageAccepted=false`。固定入口/索引的完整备份核验和可恢复切换已询问用户，尚未收到确认，不先行覆盖。下一步仍需同源完整包、运行输入绑定及冻结；精确Python离线wheel/环境正向准备和受支持PS5执行前提未齐，不使用绕过/缺依赖跳过。历史人工验收、M8.4和E5-D债务保留。

### 最新：E 当前捕获默认全69托管项目与原生双配置构建完成

沿用并重验 `cfb6e8f7b0fa4e95bc03393a36ccb88a`，5032个选定源码文件，source `3882990AF6E23B5D7AD41586A6EB236856D2A96AEE5D9946520B8E6B271BD292`，manifest `FB8CA9A2A758D9AE43DB4B413ACF2080B06761B9C3266B8D3EF06DEA72A5C703`。没有修改产品/测试源码或重新解释旧结果。

证据目录：`out/verification/m8-6/e-current-base-first`。

| 范围 | Debug | Release |
| --- | --- | --- |
| 默认全部69托管项目 | 0警告/0错误；离线准备+构建221.23s | 0警告/0错误；离线准备+构建167.83s |
| 其中MSBuild构建耗时 | 214.03s | 155.92s |
| 同源原生all | 322步，退出0，84.76s | 322步，退出0，112.51s |
| 托管编译产物 | 1508文件 | 1508文件 |
| 私有还原包文件 | 1332文件 | 1332文件 |
| CTest -N/JSON | 180项，未运行 | 180项，未运行 |

托管方案从捕获清单枚举全部69个csproj生成独立验证sln，避免只看根sln遗漏项目。AI/扩展开关均关闭；显式本地缓存、独立清空来源的NuGet配置、私有packages/artifacts；还原与构建分离，构建固定 `--no-restore --no-incremental`。没有调用整条Build.ps1/Build.bat。`FinalAudit.ps1`逐项验证69份assets的projectPath/项目引用均属于捕获，outputPath属于本轮obj、唯一packageFolder为私有目录、唯一source为显式本地缓存、唯一config为本轮离线配置，记录生成props/targets哈希与完整包/产物清单。此为编译准备检查点，**不是签名/完整SDK密闭证明或生产作业执行许可**。

原生使用此前同捕获、同配置CMake目录，先Ninja干跑确认默认all不调用托管/整Build/部署，再构建native all。原MSVC诊断检查及补充英中文warning/error检索均无命中。只执行编译/链接与原构建工具，不运行测试目标、引擎apphost或Python。构建后各180项、名称未变，`ctest -N`无缺EXE提示；这不证明后续dotnet参数中的程序集、SDK声明、完整包或Python环境都可用。

最终核对：当前5032源码仍与捕获匹配；正式151文件、EXE `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`、journal `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1`、14历史输入树和两份模块包未变。首次最终审计调用因工具自动审批超时未启动（结果/还原核对文件均不存在），按相同范围唯一重试正常退出0；没有改权限、放宽检查或重跑构建。构建会话46798/81395和审计55913均已退出0。

仍未完成：同一捕获的可选AI/扩展/组合图、SDK声明、完整包与受检运行输入重绑定/冻结；精确Python wheel及实际环境正向准备，PS5受支持执行环境；随后才执行全阶段统一测试。当前 **testsExecuted=false / runtimeReady=false / stageAccepted=false**，未发布/部署/提交推送，历史人工与E5-D债务不变。之前其他捕获的可选图或包不能拼接为本次整阶段验收。

### 最新：E 原Python夹具显式接线，双配置模块图编译通过，功能未执行

测试程序集增加 `PreparedPythonInputs`，只读接收 `--python-environment <独立环境代次绝对路径> <回执SHA256>`。先按原 `--verification-workspace` 分离编译来源与运行IO，再解析Python独立来源；成功前不改原参数。捕获来源与运行工作区不同时，worker入口缺Python准备明确失败，不回读活动工作区worker或全局解释器。原NoPythonProducts在未提供Python环境时仍可验证默认产品不依赖Python，未变成“需要安装Python才能验证无Python”。

校验包括独立环境/输入两级回执SHA、kind/根/来源/无执行授权声明、完整文件集与长度分帧指纹、文件哈希/单链接/非reparse/预算/路径别名及pyvenv的基础解释器确实指向捕获副本。验证前后所有文件重新扫描；不声称检查间隔没有并发变化，也不将此测试输入审计当成生产安全租约。

**所有权与原断言：** 完成前置文件检查后释放读取句柄，让原OwnedHost/RelocatedClient/NoPython夹具照旧对实际使用的来源文件取得 `FileShare.None`，不是把原断言改成共享读。协议worker已有的 `-I -B`、PID/父子/镜像核对不变；独立worker仍由原 `PrepareOwnedBundle` 复制/压缩并走原 `WorkerInstallation`。测试结束（包括原测试失败）再核对输入，后置失败强制非零且保留原错误输出。PS `Open-PythonWorkerEnvironment` 持锁消费接口未放宽；不能用它持有所有共享文件锁的同时启动需要独占来源锁的夹具。两者承担不同工作。

CMake新增显式环境根/SHA变量 `NCMA_PYTHON_VERIFICATION_ENVIRONMENT` 与 `NCMA_PYTHON_VERIFICATION_RECEIPT_SHA256`，配置时核对回执SHA，按原dotnet入口传入参数；不新增启动宿主、不替换PS5、不改三个入口名称或300/180/180秒超时。空绑定保留为缺准备，不能跳过后报通过。

新增6组**合成元数据/文件合同**（不启动Python、不是实际venv或wheel正向证明）：缺准备/参数、精确映射与独占锁、产物变更、来源增加/变更、错误SHA/重复JSON字段、基础解释器替换；已加入原Grpc汇总harness，注册未运行。原CaseHarness跑完所有case再汇总的行为不变。

证据 `out/verification/m8-6/e-python-routing-first/`：

- 新完整选定源码捕获 `cfb6e8f7b0fa4e95bc03393a36ccb88a`，5032文件；source `3882990AF6E23B5D7AD41586A6EB236856D2A96AEE5D9946520B8E6B271BD292`，manifest `FB8CA9A2A758D9AE43DB4B413ACF2080B06761B9C3266B8D3EF06DEA72A5C703`。
- 独立 `-Scope modules` 本地缓存准备后，原Grpc.Tests完整引用图 `--no-restore --no-incremental`：Debug8.39s、Release8.51s各0警告/0错误（MSBuild自身8.19/8.32s）。私有代次 `ffe7d767dd8f432da2af2351293be6bd` / `d3d0581292d4470990759affb11b14cf`，每份harness14文件；依赖回执重开未变。没有再次发布/激活模块包或替换固定测试树。
- 同捕获CMake Debug/Release配置成功；CTest `-N`/JSON各180项，名称与此前180项一致。模块入口仍原dotnet/工作区/超时；Python参数未绑定。存在缺EXE提示，其他SDK/harness/原生输入并未补齐：**仅库存观察，不是运行就绪或功能验收**。未使用历史harness补齐空声明；正向Python CMake绑定尚未实测。
- 正式151文件、journal、14历史输入树和两份原模块包不变。无Python/pip/worker/模型执行、全69/native目标构建、部署或Git发布。两个辅助命令均正常退出0，未遗留已知会话。此前只读CIM进程查询因权限拒绝，未采用其结果判断进程状态；完成状态以原exec会话退出为准。

下一步：精确wheel可用后完成真实捕获/离线环境准备/回执重开与三入口正向绑定；新同源全量构建、运行产物、包和SDK声明/冻结，再统一执行M8.6测试。当前既不确认Python功能通过，也不将此前其他捕获的编译/包结果拼为本轮全门禁。PS5受支持环境及历史人工/E5-D开放项保持。

### 最新：E Python捕获副本离线环境入口已编码，缺包阻断正向准备

新增 `PythonWorkerEnvironment.ps1`、`Prepare-PythonWorkerEnvironment.ps1` 和捕获配方 `PythonWorkerEnvironment.py`。原输入配方清单扩为10项，包含新入口与此前转引的 `EditorPackageDependencies.ps1`；旧输入回执不能冒充新配方。实际准备必须先 `Open-PythonWorkerInputs` 核验指定回执SHA、完整文件集/字节/身份，持有输入至两个同步准备进程结束，再次核验输入未增加/修改。

固定两个步骤：

1. 捕获的 `runtime/python.exe -I -B` 从捕获的配方脚本创建新GUID目录下的venv：无pip、无全局site-packages、无符号链接、无升级/清空操作。
2. 新venv的解释器仍以 `-I -B` 执行同一捕获配方；使用捕获stdlib随附的pip wheel，不安装pip自身。`PIP_CONFIG_FILE=os.devnull` 加 `--isolated` 防止系统/用户/venv配置导入；固定 `--no-index --require-hashes --only-binary=:all: --no-deps --no-cache-dir --no-compile`，仅两个原锁定依赖。独立TEMP/TMP恢复原值；不读取用户AI配置，不下载、不改全局Python、不调用worker或grpc原生代码。

完成后检查pip报告只指向这两个捕获本地wheel、原SHA/版本、已安装distribution恰好两项、模块解析位置在独立venv，登记完整文件集与输入回执。新 `Open-PythonWorkerEnvironment` 重验输入/环境回执及所有文件，返回两组显式可释放的持锁所有者。它是本地依赖准备，不是AI执行许可、正式安装、OS网络沙箱或任意Python隔离器；JSON回执本身不是存活租约。同步调用没有自动超时终止，也不替代原生产Job/日志/排空合同。

本轮证据：`out/verification/m8-6/e-python-environment-first/`。

- `syntax.json`：5份PS7语法0错误；Python仅执行AST解析，未导入/运行新配方。只读检查实际bundled pip 24.3.1的配置禁用分支，wheel SHA `3790624780082365F47549D032F3770EEB2B1E8BD1F7B2E02DACE1AFA361B4ED`。
- `result.json`：真实离线输入准备仍退出1，`python_offline_wheel_missing:grpcio-1.84.0-cp312-cp312-win_amd64.whl`；只查指定flat pip wheel目录，非全机不存在结论。输入/环境输出集合未变，**未执行pip、worker、环境正向准备或功能测试**，缺包不是通过的负例。
- 补充失败记录仅写本次成功创建目录；最终脚本语法/文件哈希另记 `final-audit.json`，保留首次检查原件，不覆盖旧证据。
- 正式151文件、journal、14历史输入树、两份模块包核对未变。无C#改动/新编译、全69/native/SDK链/发布部署/Git操作；此前 `de591179` SDK声明仍为历史捕获。
- 新增3个纯配方/输入范围/观察合同用例，CaptureInputs共34组；**注册未运行**，原60秒入口不变。没有把语法检查当功能验收。

未完成：取得精确两个本地wheel并执行正向捕获/准备/重开，原gRPC/OwnedHost/RelocatedClient/NoPython夹具的显式输入接线及独立worker包验证，随后新同源全量构建/包/CTest绑定和阶段末统一测试。尤其原迁移夹具对来源使用 `FileShare.None`；不得因准备输入持锁而移除断言、放宽共享模式或借用全局环境，需要明确准备租约与夹具独占所有权的交接。PS5受支持执行环境与历史人工/E5-D债务不变。

### 最新：E 可重复SDK输入准备，双配置编译与原声明生成通过

新增 `Prepare-PackageDependencies -Scope sdk`，固定一个extensions Services.Tests项目图，沿原项目引用构建MCP和Gameplay；AI关闭、无RID、隔离 `workflow-sdk` 中间目录。使用独立 `sdk-dependencies` 回执，不能替换成模块/产品/验证scope，也不允许产品开关改变任务。

`Prepare-WorkflowSdkInputs.ps1` 要求显式capture、依赖目录/回执SHA、dotnet/Git、本地缓存及VS/WindowsSDK根与版本。持有原捕获/依赖后从副本执行原项目图 `--no-restore --no-incremental`，检查0警告/错误、配置和完整三个产物树。随后调用**测试程序集内**的准备分支 `--workflow-sdk-prepare-inputs`，在普通测试分发前返回，不进入测试CaseHarness或工作流执行器。没有新增Editor/Player产品能力、AI许可或隐式restore。

准备请求14字段，不接受调用方填写harness/MCP指纹。原 `WorkflowManagedRuntime` 观察实际宿主和MCP完整运行闭包；原 `WorkflowNativeSelection/Toolchain` 核验七个EXE及include/lib目录；最终由原 `WorkflowSdkChainInputs.Parse` 验证16字段声明。请求、观察、编译回执分别存储，关联capture/source、offline preparation、全部文件集和日志SHA。观察的SDK目录存在/工具字节不证明完整SDK封闭、实际选择版本或未来缓存包都齐备；不会变成执行许可。观察器退出后JSON不是仍存活的锁，真正SDK链仍须按原流程重新核验并申请独立审批。

新增1组PS固定配方合同和3组C#请求/诊断合同：CaptureInputs31组、SdkInputContracts5组，全部未运行，原60秒/30秒及SDK链3900秒不变。四个PS7脚本解析0错误。

首轮/第二轮失败与修复：

- 首轮捕获 `799660fd` 的Debug编译83.31秒、0警告/错误，观察阶段失败，仅有IOException；证据留 `e-sdk-offline-preparation-first`。
- 增加固定步骤名+异常类型+有界机器码，不输出任意异常正文；第二轮捕获 `de591179` Debug83.60秒、0警告/错误，明确失败于 `tools / workflow_file_identity`。原始日志/回执留 `e-sdk-offline-preparation-second`，不改判通过。
- 只读 `fsutil hardlink list` 确认：系统安装Git的cmd/git.exe有2个链接、mingw64/bin/git.exe有4个；dotnet.exe为1个。原工作流本就要求单链接，未放宽此规则。
- 第三轮显式选择本机已存在的 `C:\Users\liu\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\git\cmd\git.exe`（文件版本2.56.0.windows.2、1链接）。没有复制/安装工具、拆硬链接、修改ACL/MIC/执行策略或暗中PATH后备。Git文件/版本为只读观察，未据此宣称整个Git依赖闭包封闭或实际SDK源捕获工作流已运行。

第三轮使用同一第二轮捕获，但新建两份依赖准备及产物目录，不覆盖失败产物：

- 捕获 `out/captured-build/de591179bbb54332a5fbdfe39ef310d2`，5027项；source `76F7F88C97BE9B4A79B84D53AEE059FC41A2E2E1184F4A10F913699A526E87BF`；manifest `3860F41F62BB18D8C41114AA904F40CEDCFF16436722DAB27D66D9F78502971F`。
- Debug依赖 `sdk-dependencies/7a4196cfe56843b3b88962808a3b0700`，receipt `CAC8ABD8ECB51270D3DDE47C1176F38E283EFFF4AF0DD33929226FA67A4D17A1`。
- Release依赖 `sdk-dependencies/9837861e86974c88867710a74ddfa1a3`，receipt `08CE56FE32C5D3937CB0719579935189F55ED47B3FBA964DAE3E69D3AB0C54CA`。
- 每配置1297依赖文件/164063130字节，完成后重开原准备回执核验通过；原三个输出树各85/7/8文件，完整集合/字节一致。

| 配置 | 原准备入口（含编译/只读观察） | 声明目录（out/verification/m8-6/sdk-inputs下） | inputs.json SHA256 |
| --- | --- | --- | --- |
| Debug | 86.56秒，0警告/0错误 | `c2d5a10e8a574cf397f4fd6399ca67a9` | `B4C81CB1E7E55A91B83C2FEDA758378B85CEEFDE3FFB3B25F4956A7F7BA4B055` |
| Release | 86.46秒，0警告/0错误 | `36b65bec0e384d97930eab97ec16d919` | `44915288E170AB7CEBC6B05C0C5D77E116B6A12B562664D4C4A988B3903B8221` |

证据 `out/verification/m8-6/e-sdk-offline-preparation-third` 含原构建日志/binlog、来源/准备回执、git-selection、result与audit。最终5027份作者/捕获选定源码一致；正式151文件、journal、EXE SHA `379AA113…`、14个历史验证/共享树以及两份此前模块包索引/全部字节均不变。所有本轮进程会话已终结。

**边界：**只运行了准备观察器，不是SDK11步骤链、功能测试、全69项目或原生构建通过；未重配CTest、生成完整引擎包、部署/提交推送、调用模型或读取密钥。PS5只读查询仍Restricted，未运行其脚本或绕过；Python缺精确wheel且捕获副本消费尚未完成。新声明还需在最终同源全量准备后绑定到实际CTest配置，旧 `f34d/723de/37df` 证据保持各自来源，不能拼成最新全阶段通过。下一步继续Python消费与完整同源托管/原生/发布包/共享输入、最终冻结和统一测试，D/E及人工/E5-D债务未关闭。

### 最新：E Python独立输入捕获基础，实际准备缺包，未运行解释器

新增 `PythonWorkerInputs.ps1` 与明确调用的 `Prepare-PythonWorkerInputs.ps1 -PythonExecutable <本地python.exe> -OfflineWheelDirectory <本地平铺wheel目录>`。只准备数据，不启动解释器/pip/worker、不联网或安装。它不是C#构建许可的附带能力，也没有新增AI审批/执行入口。

- 原requirements锁作为唯一包版本/hash来源，严格UTF8、恰好grpcio与typing-extensions两项；拒绝重复、额外pip选项、未知包和错误hash。按锁生成cp312/win_amd64及pure-Python精确文件名，只读取指定目录的这些文件，不扫描私有HTTP缓存/配置、不用近似版本替代。
- 持有实际文件/祖先身份，先读全输入，再创建新GUID目录：四份worker源码/锁、两份wheel、CPython七个顶层必需文件和Lib/DLLs（不含已安装site-packages、pycache/pyc）、六个配方助手。整体8192文件/512MiB、单文件64MiB有界；PE必须x64，文件版本观测必须3.12，含明确venv基础文件及单个内置pip wheel。文件版本不是发行者签名证明，所选运行文件不是整个CPython发行版。
- 在 `out/verification/m8-6/python-inputs/<GUID>/inputs` 写CreateNew副本，按Ordinal顺序和原长度分帧算法记录完整文件哈希，拒绝大小写别名。Python有独立输入SHA，不能冒用引擎捕获SHA。
- 消费核验入口要求明确回执SHA和独立scope根，检查完整文件集合/字节、必需闭包、锁与wheel、当前配方，持有副本文件直至调用方释放；不会因作者源码后来变化而重新读取作者字节。回执不是签名、审批、OS沙箱或安装授权。

新增五组回归合同：精确wheel映射、重复/缺项/注入/UTF8拒绝、Ordinal排序/大小写别名、x64 PE边界、缺wheel早拒绝。CaptureInputs共30组，原60秒和跑完后汇总策略保持，**未运行**。

实际证据位于 `out/verification/m8-6/e-python-inputs-first`：

1. 三份PS7脚本语法解析0错误。
2. 指定现有pip `Cache/wheels` 执行真正准备，退出1：`python_offline_wheel_missing:grpcio-1.84.0-cp312-cp312-win_amd64.whl`。没有捕获输出代次或receipt，`prepared=false`，不是负例测试通过或Python准备完成。仅证明这个指定平铺目录缺包，不推断全机缓存状态。已询问用户是否有两个精确wheel的本地目录，不自动联网。
3. 另外只读核对实际CPython：3.12.8、x64 PE；所选2427文件、66790350字节，完整清单SHA `8957189144B9A2F01F80461BE47EF7DFA948940D004503BD0F4CC8DCE03D37F8`；内置pip24.3.1 wheel SHA `3790624780082365F47549D032F3770EEB2B1E8BD1F7B2E02DACE1AFA361B4ED`。这只是实际文件前提，不证明解释器运行或wheel安装成功。
4. 正式151文件、journal、14个历史验证/共享树、两份此前gRPC模块包索引及完整文件集合均未变。没有Python启动、安装、功能测试、部署、提交推送、模型/密钥访问。

**未完成：**完整输入正向捕获与重开验证；从副本生成协议测试venv和产品宿主测试的独立worker包；把原测试实际消费根绑定到这些输入。现役测试仍有canonical worker读取，不能声称本片已实现所有Python运行来源隔离。后续保留两类环境区别和原断言，不以venv冒充独立产品安装。两个新助手和测试源码变化后，先前 `37df0b39` 模块包仍是历史捕获，不能重标为最新同源包。SDK链及其他安全准备仍可推进；M8.6 D/E、PS5执行前提和人工/E5-D债务均未关闭。

### 最新：E 可选gRPC模块捕获发布与离线准备完成，未执行功能测试

`Prepare-PackageDependencies -Scope modules` 使用两个固定任务：`Ncma.Modules.Grpc` 为无RID的 `publish-library`，`Ncma.Modules.Grpc.Tests` 仅build。两者共用本次准备的私有package目录与module-transport中间目录，不借用旧树或canonical out/modules/nuget；拒绝产品特性开关、其他scope回执及额外RID。准备回执为独立 `ncma.modules.offline-dependencies.v1`，仍验证完整依赖集合、配置、捕获/配方与dotnet字节。可信MSBuild不是沙箱，准备成功不是AI构建/联网/安装许可。

原 `Package-ModuleClient.ps1` **本来已有 `--no-restore`**；本片接入明确的capture/tool/dependencies/SHA，源码与两个锁文件从持有的捕获副本读取，NuGet档案从受检私有准备目录读取。原7DLL白名单、真实档案SHA512、deps选择的runtimeasset逐字节比较、声明/notice复制及64文件/32MiB预算均不放宽。原schema1模块manifest和m8-2 index附加捕获及准备绑定，保持原消费者文件闭包校验；普通未捕获开发路径仍保留。这里只发布optional-library，不安装Python，不向默认Editor/Player加入传输库。

新增3组回归用例（固定无RID配方/参数、防scope和配置特性替代/实际文件持有、错误RID拒绝），CaptureInputs共25组，原60秒和全部case运行后汇总策略不变，**均未运行**。四份相关PS7脚本语法解析0错误。

本次实际来源：

- 捕获 `out/captured-build/37df0b398faa4090a5f1eac23d76326f`，5023文件；source `3F46942217D6E47BEDE7FB03A6EE5E5C0968A1695C83C8748A2E6FF7DD65FC8E`；manifest `30E4EDE34CDF0505563CB11C216E95187E984770A5F4866A4D86323909329C7F`。
- Debug准备 `modules-dependencies/c38d04f38eaf427aa58965a63ef9aa9a`，receipt `9421D8C045CE748E6B55664EF7390DD922F917AF8EAEB75FB01A02DAC75EC1FF`。
- Release准备 `modules-dependencies/9dd9543e57ac41d39bd096afa678d802`，receipt `212E6BD037CB157474C765BAAF14EB626899085B816608741748DB3A15EBE930`。
- 每份准备1344文件/173747479字节，发布/编译后原消费者重开核验通过；只使用本地NuGet缓存，未联网下载。

| 配置 | 原模块发布 | 测试项目编译（不运行） | 模块代次 |
| --- | --- | --- | --- |
| Debug | 8.74秒，退出成功，无warning/error诊断 | 8.06秒，0警告/0错误 | `bf2d64c1a17d4471958e15288366673a` |
| Release | 8.70秒，退出成功，无warning/error诊断 | 8.15秒，0警告/0错误 | `a2ed7a131bf44d4bb7a826ea14ea974a` |

模块位于 `out/package/modules/<Configuration>/<代次>/client`，各21个载荷文件+manifest，含7个DLL；测试项目各14个输出文件留在准备目录，没有启用为当前统一测试输入。原m8-2客户端索引此前不存在，本片新建，不替换正式安装。

证据 `out/verification/m8-6/e-offline-modules-first`：原日志/binlog、capture、prepare/manifest/index副本、result与audit。最初证据脚本误用out-only部署遍历器检查managed内的旧验证目录，捕获/还原前即拒绝；原脚本与失败另存，改用原验证目录遍历器后成功。产品路径限制和断言未修改，失败不抹除。

最终5023份作者/捕获输入一致；正式151文件、journal与EXE SHA `379AA113…`不变，原12个AI/扩展验证树及2个历史共享Debug树共14个目录完整集合/字节不变。此前 `723de640`、`92e8928a` 等仍为不同来源的历史证据，不可合并为本次全69/原生/完整包通过。PS5只读观测仍Restricted，未运行PS5脚本或绕过策略；没有功能测试、apphost、部署、Git发布、模型或密钥访问。

Python只读前提记录在 `python-readiness.json`：本机CPython文件版本3.12.8，未启动解释器；引擎专用 `out/modules/python312/Scripts/python.exe` 与pyvenv.cfg不存在。仅检索用户pip `Cache/wheels` 的文件名，未发现锁定的grpcio1.84.0和typing_extensions4.16.0轮子，**不是全机缓存缺包结论**。Python源码不在当前引擎捕获选择内，后续必须独立绑定源码、解释器/venv和精确轮子，不能冒用本次sourceSHA；没有安装或联网权限扩张。

下一步仍为Python离线运行前提、SDK链与完整同源产品/测试输入准备，之后再统一测试。当前M8.6 D/E、M8.4人工闭环与E5-D延期项均未关闭。

### 最新：E 原AI/扩展验证编译入口离线接线，双配置编译与输入审计通过

原独立准备入口增加 `-Scope verification`：六个固定、无RID的build任务，分别为AI Services、AI contracts、extensions Services/Player、AI+extensions Services/Player，使用四个隔离的中间目录。该scope不接受产品开关来删减任务，使用独立 `verification-dependencies` 目录和 `ncma.verification.offline-dependencies.v1` 回执，不能用package scope回执替代。与打包路径共享原参数、配置/来源/工具/完整依赖字节校验，不另造执行器或AI授权。

`Prepare-AiVerification.ps1` 的捕获模式及 `Prepare-ExtensionVerification.ps1` 现在要求明确capture、dotnet、依赖目录和预期回执SHA。原脚本执行原六个项目图，全部加 `--no-restore`；扩展入口不再自动捕获或还原。编译只写准备目录中的隔离artifacts。随后 `VerificationInputStaging.ps1` 保持原固定CTest/回执路径，持有完整来源文件后以CreateNew复制整个输出树，复核完整集合/大小/哈希。目标已存在时仅允许逐字节完全相同的复用；不同版本明确拒绝，保留旧目录，不合并、不删除。原AI回执验证器及其严格root规则未修改。

原两个编译回执增加依赖准备目录/SHA关联，仍标记compile-only、testsExecuted=false。Build.ps1与Test-M8_5的Compile调用方已转发参数，缺准备参数在昂贵工作前拒绝；**本片没有运行这两个整体入口**。Build的完整managed路径现需显式传入 `VerificationCapturedBuildRoot`、`VerificationDependenciesRoot`、`VerificationDependenciesSha256`、`VerificationDotnetExecutable`；GameplayOnly/SkipManaged原分支不要求这份验证准备。普通未捕获的AI开发路径及其余历史构建步骤仍有implicit restore，不能宣称整个仓库已全量迁移。

新增4组测试：六项配方与参数；原AI/扩展路径完全保持；完整树复制及仅相同字节可复用；缺输出时在创建目标前拒绝。原CaptureInputs现22组，原60秒不变，全部留阶段末统一运行。

本次实际证据：

- 捕获 `723de640fa994dc5b971901a0632cec4`，5023文件；source SHA `BA049C953C066DC74A78DD3436D9F6957677BF368670B3D8D201BBC0B08810DE`，manifest SHA `56395988724DE09A65E2E4DEC8B949BC607AED04E5EAEB4E5D5B3074B1F18206`。
- 原离线producer分别准备Debug `45d74157e4dd4d97a09d959d574e134c`（回执 `F814E6560DD239C6A71BC032A1E3AADAD907C45EBBA25121C86A166835294E52`）和Release `871bfdf2d9e441de9d9a0d8d32e6d05a`（回执 `405AC9AE7A0486282E9E16D8DEF145AAD89AD35810D0F5F98AD9D77B4EDA822C`）。各1642个依赖文件、165385593字节，编译后原消费校验重新打开通过。
- **原编译脚本**Debug/Release各6图，均0警告/0错误，日志输出配置检查通过。AI两图驱动含IO耗时150.26/152.33秒；扩展四图驱动189.77/190.46秒。不是模拟命令或单独替代编译器。
- 起始12个目标目录都不存在；现每配置6个完整固定树文件数依次为81/68/85/68/85/68。每树与独立编译目录完整一致；原AI回执及扩展回执文件集核对通过。新AI回执generation为Debug `111d91c06fb24ecda6672446ae4babfe`、Release `46d7bf3636e44de990763b94dfd913f9`；扩展为 `2589f20f9b054c7c8a628d8f40fa4247` / `c7d4493dc29343b698bcd16a69a3d73e`。
- 八份PS脚本PS7解析0错误；PS5只读查询有效策略仍Restricted，未执行PS5脚本/改变策略/绕过。原两个历史共享Debug37文件树完整不变；5023源码与捕获一致；正式151文件、journal、EXE `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`不变。

证据 `out/verification/m8-6/e-offline-verification-first/{syntax,capture,result,audit}.json`、各配置准备/回执副本及原build日志/binlog；所有会话终结。没有功能测试、全69项目门禁、原生构建、完整包、apphost烟测、部署、Git提交推送或模型/密钥访问。历史package scope Release10任务证据仍属于旧捕获，不冒充本次新版准备器全量回归。

下一步需要备齐剩余运行输入、完整同源码包和SDK链。只读检查确认真正使用的 `out/modules/python312/Scripts/python.exe` / `pyvenv.cfg`、`out/modules/nuget`、Debug/Release的m8-2/client-package.json都不存在。`Package-ModuleClient.ps1` 本来已使用 `--no-restore`，后续要核对捕获来源、原锁定档案和目录映射，不放宽运行闭包/档案哈希断言。固定PS5支持环境及人工/E5-D/D/E边界仍开放。**现在固定验证输入已有本次产物，之后不同源码更新必须先保存并核对旧输入再激活，不得强制覆盖**。以下为此前记录。

### 最新：E 捕获打包独立离线准备接线，Release托管路径检查通过

新增 `Prepare-PackageDependencies.ps1`，作为**单独调用**的本地开发依赖准备入口，参数为配置、原捕获目录、明确dotnet.exe、固定磁盘本地NuGet缓存源，以及AI/扩展开关。不编译、不运行测试、不生成安装包或授权AI。清空NuGet配置中的其他源/回退/映射，关闭审计和失败源忽略；使用新的私有packages目录，缺包直接失败。可信MSBuild目标/SDK仍可执行代码，**这不是进程网络沙箱或封闭SDK证明**；未修改用户NuGet配置、系统策略、ACL或全局SDK固定版本。

固定任务集合按功能为5/6/7/10项：Gameplay.Sample和Shader.Cook使用独立 `tools` 中间目录且无RID；default/ai/default-extensions/ai-extensions发布使用分别隔离的中间目录与win-x64、framework-dependent属性。这样无RID工具准备不会覆盖发布需要的assets，也不会让AI/扩展开关互相覆盖。Editor与同变体Player共享确定的组件构建上下文。

准备回执绑定原source/manifest/捕获props SHA、配置、功能标志、准备目录、工具路径/哈希、完整固定任务，以及所有私有NuGet文件和各项目生成的assets/props/targets。消费端要求调用方明确提供回执SHA，重查完整集合/字节和根项目assets的project/output/package-root/RID后，复用原物理文件/祖先锁保持到消费结束。文件数量/大小有界；不以一份可修改的JSON作为AI审批或安装许可，不宣称已绑定整套SDK/MSBuild外部环境。

原 `Package-M2_7.ps1` 的**捕获或扩展模式**现在必须同时提供 `CapturedBuildRoot`、`DependenciesRoot`、`ExpectedDependenciesSha256`、`DotnetExecutable`；缺失在输出创建前拒绝，不自动创建新捕获或补restore。其样本、烘焙器的编译和所有托管publish均取同一固定参数生成器并加 `--no-restore`，失败不重试还原。该脚本的原生构建、烘焙、包文件闭包、manifest/索引与安装边界保留。本片只完成该捕获路径，**普通未捕获开发路径以及 Prepare-AiVerification/Prepare-ExtensionVerification 仍需后续迁移**；不能声称所有原入口已消除隐式restore。

新增5组用例：四种固定任务集合、精确参数/输出/RID、网络/相对/属性注入路径拒绝、回执配置/功能匹配及持有依赖字节、依赖修改/新增拒绝。合成工具和依赖不执行；原CaptureInputs现18组，保持60秒入口并继续跑完组后汇总，**全部留阶段末，尚未运行**。

本次真实准备/编译证据：

- 新捕获 `92e8928a03eb46cbba308eeb1a7477dd`，5022文件，source SHA `3203689961BCC92046ED64DDE2BFA3B9629AF22869EDCD246D22FC89956D66A6`，manifest SHA `446CD1997B7534D439C8A428BD60AE2A463D38A851BD48B406A9F589B3F081F6`。
- 原新增准备入口Release、AI+扩展开启，全部10条离线restore成功。准备目录 `out/verification/m8-6/package-dependencies/708b6f60506246308908938b5e4f62b9`；回执SHA `39C66E5C04DD2375EB04AFF2D35C91A9FEDDF4D5749A2DF17D192BB3A5E75FA1`，1621个依赖文件、165654448字节。
- 持有回执后，10条固定托管 `--no-restore` 构建/发布均退出0、无编译警告/错误：工具2项约8.17/13.11秒；default Editor/ImportWorker/Player约64.94/3.14/6.65秒；AI Editor71.08秒；扩展Editor/Player69.07/6.78秒；AI+扩展Editor/Player74.36/6.95秒。未执行任何发布产物。
- 编译后重新通过原回执消费校验，1621文件未变化；所有日志无Debug输出。原可选依赖验证器检查8个发布目录通过，文件数分别65/9/51/71/69/55/75/55；两种扩展变体的Editor/Player共享Hosting DLL哈希各自相等，没有把AI放入Player。
- 四份PS脚本PS7解析0错误，未使用PS5绕过。最后5022项作者源码与捕获一致；正式151文件、journal、EXE `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`不变。

证据 `out/verification/m8-6/e-offline-package-first/{syntax,capture,preparation,result,audit}.json` 及日志/binlog/托管输出；所有会话终结。本轮**没有执行整个Package-M2_7脚本**，因此不是完整原生/着色器/资源包、manifest/index、实际apphost、Debug、全69项目或阶段验收。没有功能测试、部署、Git发布、模型/密钥访问。下一步接原AI/扩展验证编译入口的独立准备与原根/回执约束，再备齐完整同源码包、SDK链输入、支持的PS5环境，最后统一阶段测试；历史证据和E5-D/人工/D/E开放项保留。以下为此前记录。

### 最新：E 托管运行根接线及双配置清单核对，尚未功能测试

测试专用 `VerificationWorkspace` 接受唯一末尾 `--verification-workspace <绝对目录>`，拒绝重复、缺参、不存在目录及重解析祖先；提取后保留原source/configuration/mode参数。普通调用仍取原源码根；此参数仅负责路由，不是捕获回执或执行授权。它通过既有测试支持项链接，不进入产品API。新增3组测试（参数保留/普通调用/无效参数拒绝）留整体阶段末运行。

原7个CTest入口接入该参数：默认Player、extensions/ai-extensions Player、AssetImport、NoPythonProducts、Grpc、RelocatedClient。FBX素材、Import Worker及直接Player程序集仍来自捕获源树；包索引、生成验证包、共享Gameplay样本、可选Python运行依赖来自canonical运行根。FBX Player的Debug/Release改为明确传入，删除按插件目录名猜测配置的逻辑。未替换原包、资源、隔离或运行断言；不代表可选Python运行依赖已经准备完成。

新捕获 `8e1bed98e6ec42bcb4a0054b41afbb8f`：5020项源码、69个项目；source SHA `ABD9F23A032F90133951FADA0035A7A877F1F058BD8B55A8969CED7490FD9CC6`，manifest SHA `68904684B733199DBC1079F71C8483A6A4EF9B76F193B3CFABBB6C28FC62F01A`。仅从捕获副本编译，先显式本地缓存离线还原，再 `--no-restore`，没有联网还原授权。

编译记录：

- 默认五根测试图Debug：94.43秒，0警告/0错误，45条图输出均为Debug。
- 扩展Player完整图Debug/Release：60.62/60.99秒，均0警告/0错误，各31条输出配置正确。
- **首轮默认Release不予配置验收**：94.27秒虽为0警告/0错误，但40条依赖输出为Debug。临时方案只登记5个项目，SDK在solution模式下对未映射依赖取消父配置；原日志/二进制完整保留，未修改产品配置来规避。
- 第二轮改为直接构建同一捕获下的Player.Tests、Asset.Import.Tests、Modules.Grpc.Tests、Gameplay.Tests、Editor.Services.Tests，各自先离线还原再no-restore Release构建：57.41/15.10/6.75/4.52/22.74秒，均0警告/0错误。逐条检查所有图输出为Release，未产生Debug目录；共享输出仅用于相同源码/配置/功能标志。这不是全69项目或AI可选图验证。

新CMake Debug/Release目录只配置和执行 `ctest -N` / JSON show-only，各180项；确认7个托管尾参、11个PS上下文绑定，以及原测试名称/超时/标签均不变。Release工作流harness绑定第二轮正确产物，不能使用混合配置产物。未构建这些目录的原生目标，**missing-executable提示存在、SDK链harness/输入声明未准备，InputsConfigured=false**；不能从清单数量宣称运行依赖齐备。

证据：`out/verification/m8-6/e-managed-routing-first/{result,configure-result,final-audit}.json` 与 `e-managed-routing-release-second/result.json`；日志、binlog、失败配置证据及捕获均保留。最后复核5020项作者/捕获源码完整一致，正式151文件、部署journal和EXE SHA `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`不变。所有进程会话终结；无功能测试、新工作流worker、模型/密钥访问、包生成、部署或Git提交推送。

**下一步**：在原准备/打包脚本补齐独立离线准备与匹配no-restore路径，准备其余运行依赖及SDK链输入，再做完整同源码门禁。旧f34d的共享Debug输入不自动覆盖/复用，配置切换需保留并核对原集合。固定PS5受支持执行前提、M8.4人工、E5-D及D/E未验收边界不变。以下为此前记录。

### 最新：E 包验收显式上下文已接线，静态检查通过

新增 `PackageVerificationContext.ps1`，把canonical包/回执/输出根与捕获脚本/编译根分开。捕获模式必须同时提供工作区、捕获目录和预期manifest SHA；确认真实Git checkout根、脚本位于该捕获的scripts目录后，复用原 `Open-CapturedBuild` 逐文件/身份/字节/配方检查，调用方持有租约并在finally关闭。缺少一项、错误根或manifest变化均拒绝；非捕获的普通本地调用保持原路径语义，不作为捕获模式的降级回退。

捕获模式下，AI索引、产品manifest、HostProbe编译回执及部署夹具默认索引必须具有同一 `capturedBuildRoot`、`captureManifestSha256`、`sourceInputsSha256`。原索引查询额外返回其已解析的同一索引对象，避免另读一次索引再关联。原打包器写入这三个字段，但**尚未执行打包，没有产生新索引/回执**。不再将作者工作区后来变化的哈希冒充已捕获的审阅afterimage；原包文件集合、模块、manifest哈希、回执、探针路径及运行断言仍保留。

新增CMake模块给原11个PowerShell包入口接入显式参数：4个Relocated、6个ProductHost和1个Deployment。配置时要求捕获拥有当前CMake源码树且manifest哈希匹配；运行时再作完整捕获检查。HostProbe路径改为原回执校验所要求的canonical配置目录，不放宽验证器。测试名称、属性、超时不变；原固定PS5及历史策略参数未在本片执行，未修改/绕过策略。

新增3组：canonical输出/捕获锁与作者后来变化隔离；缺绑定/错误manifest/外来脚本根/错误Git根；索引、manifest与编译回执错捕获或未绑定拒绝。CaptureInputs现13组，全部留整体阶段末。六份PS脚本PS7解析0错误，独立CMake模块普通模式解析退出0；源码文本核对原test属性声明不变。**未重新配置完整CTest、未执行捕获分支、未编译或运行功能测试**。证据 `out/verification/m8-6/e-package-context-first/result.json`；正式151文件/journal/EXE不变。

继续只读排查发现17个托管源码文件读取packages.json，这不是17项实际失败：部分Rendering夹具从插件路径回溯到canonical根，Player/Import/NoPython等通过参数取根，须逐入口审计。下一步完成其余运行根映射与原独立离线准备/no-restore发布，再建立新捕获、编译、配置、包与测试证据。本片不能宣布所有运行根问题已解决。历史捕获不改写，D/E及PS5/人工/E5-D前提仍开放。以下为此前记录。

### 最新：E 源码选择已收敛，只读核对一致，功能测试未运行

本片修复上次发现的54项输入选择差异。`Get-AiCandidateSourceInputs` 现在维护唯一Git路径规则，覆盖managed/engine/scripts、tools/tests及根级MSBuild配置和.editorconfig；`Get-CapturedOriginInputs` 只负责捕获路径适配，不再另列目录、另做忽略和追加排序。原长度分帧哈希算法、大小/字节检查及生成文件排除不变。额外确认传入路径就是Git checkout根，避免捕获目录/子目录借用祖先仓库身份。

原CaptureInputs用例增加两侧完整字段/顺序和未打补丁捕获manifest哈希相等断言；新增“根构建配置/工具/测试完整选择与生成文件排除、工具变化影响指纹”和“子目录误用Git根拒绝”两组。该入口现10组，**均未在本片运行**；没有增加CTest入口或修改超时。这里收敛的是来源发现规则，不代表带审阅afterimage或删除记录的作者状态天然等同于捕获内容；后续包验收仍须绑定明确的捕获身份。

实际只读检查：三份修改脚本PS7解析0错误；两个原适配函数各返回5016项，完整数据及SHA均相同：`1E7709DCBC10FD069CF91CEB15FF1BD1DE990AEE04BE68F7F61A0C1414BC76A7`。路径集合与历史捕获一致，仅本片三份脚本字节变化。证据 `out/verification/m8-6/e-source-selection-first/result.json`。正式151文件/journal及EXE哈希不变。

**旧f34d20d7捕获仍完整，但不再代表当前源码。** 不改写旧声明/产物/回执，不用旧构建记录宣称当前源码通过。本片没有新捕获、编译、功能测试或发布。下一步先完成canonical运行根与捕获编译根的显式绑定、独立离线准备/no-restore打包，再生成新匹配捕获及验收证据。PS5受支持执行前提及历史/人工/E5-D边界不变，D/E未验收。以下为此前记录。

### 最新：E Debug 共享输入已激活，发现发布验收合同缺口

同一捕获 `f34d20d7/A61C9BB8/1DC7CA70`，先核验原编译目录及完整共享快照集合/大小/哈希，再以CreateNew复制到全部预先确认不存在的目标：

- canonical工作区与捕获副本的 `out/managed` 各37文件，均为Debug；Gameplay放根目录，MCP和Transport保留各自完整子目录，Native/Physics使用同源Debug产物。
- 两配置的Managed.SmokeTest各补Native/Physics两个DLL，原生插件目录各补一个AnimationKernel，共新增80文件。仅补原构建流程要求的位置，不运行烟测、不覆盖已有文件。
- 逐项核对复制后的完整共享集合；5016个作者/捕获源码、正式151文件和journal不变。Release共享快照尚未激活，切换时必须先核验并保留当前37×2文件，禁止两配置混用。新增后的插件/烟测清单单独登记，不改写原构建时的清单。

证据 `out/verification/m8-6/e-shared-activation-first/{plan,result,active-shared-Debug}.json` 及双配置plugins/smoke清单；未测试、未发布、未部署、未提交推送。

**只读审计改变了下一步：剩余问题不仅是缺文件。** `package-boundary.json` 经原函数实际计算：完整捕获5016项，SHA `A61C9BB8CA06ED7F967F399916567DACAE184D07A779E2FEEE06CA15F531AA4E`；旧AI选择器4962项，SHA `8898156ABD2A48D27355537BE57F7CDE18FA1F42EBF47FFD569AD789FABA2980`。完整集合多54项，重叠文件字节相等，不是磁盘源码漂移。原Package在捕获模式绑定完整集合，而旧Relocated/Host验收重新用旧选择器计算；直接执行会发生身份口径冲突。此外11个包/宿主/部署CTest入口仍从捕获脚本目录定位运行工作区，原打包索引和回执却位于canonical工作区。不能靠复制或重写索引、放宽源码哈希/路径校验解决。

下一片实施顺序：

1. 分离**捕获编译源码根**与**canonical验证运行根**；显式传递并校验原脚本、包/回执/工具所在根，保留原测试名称、断言、超时与固定PS5宿主。不把捕获副本伪装成Git仓库。
2. 收敛完整源码输入选择与捕获绑定，明确比较同一集合/算法；校验捕获清单与实际字节，不能把旧集合哈希重贴成完整集合哈希。新增针对集合缺失、错误根和错捕获的负例，留整体阶段末运行。
3. 给原打包/编译准备入口补显式离线依赖准备与no-restore路径，输出路径/配置/RID/特性与准备完全匹配；缺包明确失败，不拦截dotnet命令或借旧回执制造通过。联网另行授权。
4. 若修复改变选定源码，生成新捕获并重新建立对应编译/配置/包证据；本轮旧捕获记录仅保留为历史准备成果。固定PS5受支持条件仍是实际工作流验收前提。

D/E未验收，历史失败、人工及E5-D边界保持；以下为此前记录。

### 最新：E SDK 声明与清单绑定完成，尚未执行测试

继续使用同一捕获 `f34d20d7`（source `A61C9BB8`，manifest `1DC7CA70`）。诊断助手仅加载本轮已核验的程序集，调用原 `WorkflowManagedRuntime` 对四种 Services.Tests 变体及 MCP、Gameplay 的双配置共12组输入执行原准备/新鲜度/指纹读取；另一个助手调用原 `WorkflowSdkChainInputs.Parse`。没有调用测试入口、SDK工作流、模型或人工审批；助手不进入产品。

- 初始声明把捕获 GUID 序列化为 N 格式，两配置被原解析器以 `JsonException` 拒绝。原文件/失败输出保留；新 `sdk-inputs-*-v2.json` 仅将同一 GUID 改为 D 格式，其余15字段不变，两份均通过原解析器。Debug声明SHA `BF9BFFBDB2A768C24DEE8A778B6E1409513E37CBF92522878FBC3B8AB0845C6C`，Release `DC16B87678004EC68AD752B192B518AFBF539B5047486C70E80E5E5842319115`。
- CTest使用STRING缓存保留严格Windows接口需要的反斜杠参数。前两次配置虽生成成功，但Ninja长路径变为8.3别名，原命令严格比较失败；失败记录均保留。第三次恢复原vcvars及未显式指定缓存类型的MAKE_PROGRAM调用后，两配置各180项（历史159＋工作流21）精确比对通过。只允许SourceCreationHost的Git路径格式及SdkChain的v2声明路径两项预期变化；原名称、属性、超时及其他命令不变，没有扩大允许差异。
- 一次助手启动使用了不存在的 `C:\Program Files\PowerShell\7\pwsh.exe`，在脚本运行前失败；随后使用实际安装的PS7运行配置助手。这不是把产品固定PS5工作流换成PS7。固定PS5只读观测仍为Restricted、各scope Undefined；没有修改策略或绕过。

证据目录 `out/verification/m8-6/e-sdk-inputs-first`：原12组观察、`parser-result-v2.json`、三次配置结果、最终 `configured-result-v3.json` 和 `final-audit.json`。最终核验5016份选定作者/捕获源码一致、正式151文件及journal未变；EXE SHA仍 `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`。文档更新不在选定捕获输入内，不声称整个仓库冻结。

**声明配置完成不代表运行依赖齐全或测试通过。** 共享快照尚未激活，发布包/原构建回执来源仍待准备；原打包入口存在隐式restore，不能直接用它规避独立离线准备合同。下一步完成这些前提，再在受支持的固定PS5环境下执行整体双配置测试。未发布、安装、提交推送；D/E及历史/人工/E5-D仍未验收。以下为此前记录。

### 最新：E 可选 Player 补编译与完整生成目录暂存

使用同一捕获 `f34d20d77f34486f89ad4f64e6108907`（source `A61C9BB8CA06ED7F967F399916567DACAE184D07A779E2FEEE06CA15F531AA4E`，manifest `1DC7CA706C68214026C2E027F032C943EBD42C92F62AC5F9E7940E6EC15B2C53`），补齐此前尚未构建的可选Player完整测试图：扩展版Debug/Release 63.83/61.05秒、AI＋扩展版60.54/61.06秒，全部0警告/0错误。独立离线准备加构建67.89/64.97/64.56/65.08秒；不使用默认Player代替扩展版。原始日志/binlog/结果位于 `out/verification/m8-6/e-player-optional-first`。

暂存前核对原8组构建的完整bin文件集合与已固定检查点SHA、4组Player新编译结果和同源原生记录；验证所有目标不存在、无重解析、无重复、与捕获清单中的源码路径不重叠。保留完整映射后仅CreateNew复制，并核对每个完整目标集合/大小/哈希，失败保留现场不覆盖。结果：**154组目录加原生快照，共3866文件**。

- 默认69项目×2配置复制到现有CTest预期的SDK式 `managed/<project>/bin/<config>/<tfm>` **新生成目录**，包含各目录的完整依赖/资源，而非只补一个DLL。这些目录位于捕获副本内但不属于捕获源码清单，显式编译配方不把生成文件纳入源码；原5016个捕获源码文件逐一核验不变。
- 扩展/AI/组合Editor及扩展/组合Player测试目录复制到各配置现有CMake声明的位置。不修改测试名称/参数/断言/超时，不把复制记录冒充新构建回执；原隔离构建目录及日志全部保留。
- MCP、Gameplay、Transport测试完整目录与三个原生DLL分别准备为Debug/Release独立共享快照。**尚未激活到 canonical 或捕获根的 `out/managed`**，尚未进行配置切换，不把这些快照视为已验证的整个运行闭包。

证据 `out/verification/m8-6/e-runtime-staging-first/plan.json`、`result.json`、Player来源清单和native清单。正式151文件/journal不变，无产品源码改动、部署或Git写入，所有构建/暂存会话正常结束。

暂存后仍用原180条命令做只读直接路径检查，两配置缺失入口均由128降到 **0**；报告 `out/verification/m8-6/e-native-first/runtime-readiness-post-stage.json`，旧缺失报告保留。**这不是180项测试通过或运行前提全齐**：SDK输入仍有1个未配置入口，19个历史策略参数入口未执行；共享激活、深层运行/发布包输入及PS5受支持环境仍待完成。未运行功能测试/新工作流助手、未读模型密钥、未发布安装。

下一步：同源共享输入的精确配置激活、其余运行/包来源准备及SDK声明；然后在受支持PS5环境下执行阶段统一测试。不能因直接文件都存在就省略完整依赖和来源检查。D/E未验收，历史/人工/E5-D债务保留。以下为历史记录。

### 最新：E 同源原生构建完成，运行输入路径尚未接齐

继续使用捕获 `f34d20d77f34486f89ad4f64e6108907`（source `A61C9BB8CA06ED7F967F399916567DACAE184D07A779E2FEEE06CA15F531AA4E`，manifest `1DC7CA706C68214026C2E027F032C943EBD42C92F62AC5F9E7940E6EC15B2C53`）。检查实际 Ninja 默认 all 图及 `-n -v all`，确认不含托管目标、隐式restore、PowerShell或部署入口后，在原VS工具链环境执行原生构建；没有使用整个Build脚本，也没有调用旧可选目标中的Bypass命令。

Debug/Release均完成 **322个原生构建步骤，退出0**，耗时107.24/98.41秒；日志未发现MSVC编译/链接警告或错误。固定着色器编译工具属于构建步骤，不是游戏/测试执行。插件目录文件清单Debug32、Release12，夹具目录Debug18、Release6，均登记大小/哈希（Debug含符号等额外文件，不是模块数量）。正式151文件/journal保持不变；构建会话已正常结束。证据 `out/verification/m8-6/e-native-first` 的原始dryrun/build日志、result及plugins/fixtures清单。

构建后两配置 `ctest -N`/JSON各180项，**不再有缺EXE提示，但并不代表依赖齐全**。逐项只读检查直接命令文件参数：各配置128个入口引用的文件路径尚未准备，其中113个入口已找到本轮默认图的对应程序集。多数原因是历史测试期待 `managed/<project>/bin/<config>/net8.0`，本轮受检构建输出在隔离 `artifacts/bin`；其余可选/发布包输入和SDK声明仍需明确准备。没有实际复制、修改命令或产生新回执，也未宣称只查DLL就完成传递运行闭包。

现有19个历史入口含ExecutionPolicy参数，原样保留但未执行；不会以这些旧命令绕过当前Restricted条件。已向用户询问受支持PS5环境准备情况，目前仍可继续安全的依赖工作。缺文件、未配置输入和历史environment标签均不是已执行失败或当前环境归因。

只读核对助手首轮因PowerShell把单元素命令数组展开成字符串，在StrictMode下访问Count失败；原 `Inspect-Runtime.ps1` 和 `runtime-readiness-first-failure.json` 保留。新 `Inspect-Runtime-v2.ps1` 明确收集数组，完成上述核对，结果 `runtime-readiness-v2.json` 含每条入口、缺路径、候选位置和未暂存标志。未修改产品/测试断言，不把助手修复当功能复测通过。

**下一步：按同一捕获/配置做运行输入的精确映射或受检暂存、完整文件集合核对与原编译来源关联，再准备SDK声明和其余包依赖。** 不能只复制单DLL、改用旧包/旧回执、跳过缺依赖或凭 `ctest -N` 宣布就绪。功能测试、新工作流助手/模型、发布/安装、Git仍未执行，D/E未验收。以下为此前记录。

### 最新：E 同源托管构建准备完成，尚未执行功能测试

新捕获 `f34d20d77f34486f89ad4f64e6108907` 包含最新库存检查；source `A61C9BB8CA06ED7F967F399916567DACAE184D07A779E2FEEE06CA15F531AA4E`，manifest `1DC7CA706C68214026C2E027F032C943EBD42C92F62AC5F9E7940E6EC15B2C53`。从捕获清单枚举69项目生成临时完整解决方案，默认版全部项目及三个可选服务/Editor/MCP/Gameplay图均使用相同捕获副本。分别显式仅本地缓存还原，再 `--no-restore` 构建；隔离产物/obj/包目录，无联网后备、整条Build或部署。

| 实际构建范围 | Debug 编译秒数 | Release 编译秒数 | 结果 |
| --- | ---: | ---: | --- |
| 默认全部69项目 | 151.05 | 151.92 | 各0警告/0错误 |
| 扩展版完整服务/Editor/MCP/Gameplay图 | 82.15 | 90.52 | 各0警告/0错误 |
| AI版同上 | 85.54 | 78.91 | 各0警告/0错误 |
| AI＋扩展版同上 | 82.91 | 84.85 | 各0警告/0错误 |

离线准备加构建分别为157.13/157.76、86.47/94.83、90.58/83.11、87.27/89.28秒。证据 `out/verification/m8-6/e-managed-first` 和 `e-managed-ai-first`：原恢复/编译日志、binlog、每组result/products及最终审计；所有构建会话已正常结束。每配置完整bin文件清单分别1508/675/657/681项，各大小/哈希已登记，但不是发布包或运行闭包通过证明。

同捕获重新 CMake 配置和 `ctest -N`/JSON：Debug/Release各180=159历史+21新增。已明确本轮默认/扩展测试DLL、捕获ID及Git；SDK声明仍未配置、缺可执行文件诊断存在，原生/其他运行依赖尚未准备，不能把180项库存当180项测试通过。没有原生目标构建或测试执行。

最终只读审计：5016个选定当前源码与捕获逐项相符；实际Editor的默认/扩展/AI/组合依赖分别符合0或2个对应库，均无Tests运行引用。正式151文件、journal `F5BDC19D5269B3253489FC0D0E36B73FC8B89E08E14FA2A898B0E6E0FCFEBEF1`、EXE `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F` 不变。完整原始数据见 `e-managed-first/final-audit.json`。

**本片未执行功能测试、新工作流助手/SDK链、发布、模型/密钥、部署或提交推送。** 本机只观察到SDK9.0.315，不是重新钉SDK，也不证明SDK8/其他宿主验收。PS5只读查询仍为Restricted、各scope Undefined；未修改策略或绕过。下一步在同一捕获下准备原生及其余运行/包依赖、精确SDK声明，并解决受支持PS5环境前提；然后才统一执行阶段测试（含两配置扩展续接）。D/E尚未验收，历史失败、人工和目标机债务保留。以下为历史状态。

### 最新：D 覆盖核对与 E 统一验证前提（2026-11-04，仅静态）

按 A–E 原完成条件核对源码后，现有证据只证明实现/登记/部分编译，不能关闭 D/E。当前托管项目实际枚举为69个，不可只编译根解决方案就声称全项目通过。覆盖对应如下，所有本阶段功能执行结果仍待 E：

| 必需行为 | 现役入口/源码 | E 必须取得的证据 |
| --- | --- | --- |
| 场景事务、两层批准、取消后保留提交 | `OriginalHost` / `WorkflowTests` 和 `WorkflowAuditTests` | 原 MCP 实际结果、场景字节/Undo、账本哈希链 |
| 动画图提交、验证、隔离断言及失败停止 | `workflow-host-v1` 第6/8项（从0计） | 原动画服务与资源许可、实际文件、断言结果，不以UI模拟代替 |
| UI 提交/Undo、后续人工修改、资产刷新 | `DomainSourceHost` / `WorkflowDomainSourceTests` 12组 | 原 UI 文件服务、精确前后字节、重新提案与独立批准 |
| 换会话/旧来源/撤权/修复/停止与关闭残留 | `ContinuationHost`、Lifecycle、Observation、CaptureHost/SourceCreationHost | 合成合同与实际进程分列；实际 Job/传输/后代排空前不得释放 |
| 扩展源码完整人工页面和交接 | 扩展版 `WorkflowSourceHandoffTests` / `ContinuationCases` | **补充运行扩展版 `--workflow-continuation-tests`**；默认版在条件编译下不含可选 presenter case，不能替代 |
| 审阅字节确实成为编译输入 | 扩展版 `SdkChain` 的11个依赖步骤 | 原捕获精确差异、实际 SDK 编译/PE标记/回执、原生准备、合同8项及宿主14项；未执行依赖步骤独立列出 |
| 完整库存、坏证据与缺依赖拒绝 | `InventoryContracts`、Contracts、各独立入口 | 逐名结果及完整case汇总；库存正确、编译成功、退出0均不能独自证明验收 |

本次发现并修复清单校验缺口：原检查只核对参数/超时，未约束启动宿主和工作目录。现在新增入口仅接受绝对本地 Windows 路径和对应 `dotnet.exe` / `powershell.exe` 名称，托管参数工作区须匹配 WORKING_DIRECTORY，脚本须在该工作区原 `scripts` 目录；相对/驱动器相对路径、错误宿主和替换脚本目录拒绝。仅为清单结构约束，**不是宿主文件哈希、签名、身份或存在性验证**。未改历史159条入口/超时或产品工作线程，未添加 Bypass。新增两组负例（共7组清单合同），均未运行；路径检查使用 PS5 可用 API。

验证范围：两个脚本 PS7 语法解析0错误；新版分类器只读检查上一捕获保留的 Debug/Release JSON，各180=159+21、输入仍未就绪。没有重新生成 CMake 库存、没有功能测试或新编译，旧捕获 `77cd1599` 不包含本次脚本变更，不能继续称作当前完整源码同源结果。证据 `out/verification/m8-6/e-readiness-audit-first/static-result.json` 单独固定分类器和夹具 SHA、原库存 SHA。原失败和旧报告不修改。

#### E 执行次序与禁止提前通过的门禁

1. **前提清单**：确认固定 PS5 worker 在受支持环境允许正常 `-File`，不改策略、不使用 Bypass/替代宿主。现有 Restricted 问题保留未解决。显式固定 dotnet/Git/VS工具集/WindowsSDK和离线缓存；缺包停在独立依赖准备，联网须另批。核对正式151文件、journal及EXE哈希，禁止用整条 Build 脚本代替无部署验证。
2. **统一源码与构建**：最终实现冻结后重新调用原捕获流程（包含本次脚本）。从同一捕获枚举全部69项目，Debug/Release分别独立离线准备后 `--no-restore` 构建，0警告/0错误。实际 default、extensions、AI相关图及运行所需原生/客户端/包依赖使用明确同源/配置产物；不能借旧包、旧树或本次仅Debug结果补齐。
3. **入口准备**：为每配置明确 `NCMA_WORKFLOW_TEST_DLL`、扩展版 `NCMA_WORKFLOW_SDK_TEST_DLL`、捕获ID、Git、SDK输入JSON、原生/MCP/Gameplay闭包；16字段 SDK 声明不冒充构建回执。原库存每项的运行依赖必须单独核对，`ctest -N` 无缺EXE也不足以证明运行闭包。配置前后保留完整清单及哈希，缺失仍登记为未就绪。
4. **整个阶段末统一运行**：只有实现/前提就绪后开始。独立入口保留原超时，跑完所有独立case后汇总；真实SDK链按依赖顺序，失败后继标未执行、不删除分母。除两配置21个新增入口外，显式补充两配置扩展版续接入口及既有回归范围；记录每项配置/变体/实际case集合，禁止默认版代替扩展case。
5. **归因与收尾**：180项总库存中的159历史项保留独立归因，历史environment标签不是当前机器阻断证明；本轮实际执行/未执行及原因逐项记录。D/E全流程不能仅凭新增组通过关闭，缺历史回归或运行前提须明列。失败保留原日志/源码/产物后修正、重新同源验证；无自动重试失败副本/强杀/部署。核对原正式151文件/journal和EXE哈希未变后再给结论，模型/密钥/安装批准及E5-D目标机仍各自独立。

**下一步是 E 前提与同源构建准备，不是已开始测试或已完成 M8.6。** 如静态核对或实际验证暴露实现缺口，先补齐并重建受影响同源集合，不能以修改断言或缩减验收范围收尾。以下为历史记录。

### 最新：D 领域来源绑定与 UI 提案原始内容保护（仅编译）

领域计划现在独立保存原资产服务的 `ProjectId / Generation / Clock.Revision`，贯穿完整审阅、源码交接、作业绑定/指纹、严格原生请求与审计 schema3 的预期/观测来源。资产变化使尚未批准和已批准的旧领域计划冲突，已完成计划不能借后续资产版本继续验证；纯场景计划不因无关资产刷新失效。只有原命令成功后的监视器推进预期来源。该时钟不是磁盘实时新鲜度证明，也不替代原 UI/动画资源范围及独立工具许可。

UI 提案在提出时保留精确原文件字节或不存在状态，提交前再次比较；后续人工改动返回 `ui_document_conflict`，不以提交时文件作为新的原始内容。修复需要重新审阅/提案/计划批准和独立工具许可，原发布、Undo 与历史约束不变。

新增真实 MCP/UI 领域夹具12组，覆盖资产/动画/UI 的未批准与已批准失效、纯场景隔离、原 UI 提交和显式 Undo、独立范围撤销、完成后来源失效、后续人工字节及重新批准、旧 presenter 事件。均登记未执行；其中未启动回执为明确合成数据，不冒充 SDK 执行。已有绑定/原生请求合同增加来源指纹及未知嵌套字段拒绝断言。新增独立 `DomainSourceHost` 180秒入口，旧超时、宿主14项和合同8项配方不变。当前清单 **180=历史159+M8.6新增21**。

前两次默认图编译分别失败3项（测试命名空间/nullable）及1项（剩余 nullable）；失败捕获、日志和 binlog 保留，未降低诊断。最终同一捕获 `77cd1599c4264b839fc81754c6f6ac5a`：source `423A51E1713BAB592456ED4F0B1B4AAA2075E8AEB700E8268C86C2D42783DCEF`，manifest `72759CA7CDCF250FDE62DD5B3E5484ECE69BC6F37EF16B046E6954B0DCFC582C`。完整 Services.Tests＋Editor＋MCP＋Gameplay 默认/扩展 Debug **0警告/0错误，78.67/84.05秒**，独立离线准备加编译82.97/88.49秒。证据 `out/verification/m8-6/d-domain-source-{default,extensions}-third`；失败记录位于对应 first/second，未覆盖。

同源 Debug/Release 仅 CMake 配置与 CTest show-only，各180项；输入未配置、缺可执行文件诊断存在，运行依赖未就绪。最终静态核对5016个选定源码文件与捕获一致、默认/扩展产品分别0/2个扩展库且无 Tests 运行引用；三个脚本在 PS7 仅解析、0错误。正式151文件/journal不变。**没有功能测试、新助手/SDK链运行、Release托管或原生产品构建、模型/密钥访问、部署或Git发布。** 文档更新属后续记录，不冒充全仓冻结。

下一步完成 D 最终覆盖/就绪核对，准备 E 明确同源输入，再按整个阶段末统一测试。PS5 Restricted 执行前提仍未解决、未绕过；缺依赖不跳过报通过。D/E 尚未验收，M8.4 人工/完整复验和 E5-D 目标机等债务保留。下方“最新”均为历史时点。

### 最新：D 真实 SDK 链夹具已登记（仅编译，尚未执行）

新增 `WorkflowSdkChainInputs` 和 `WorkflowSdkChainTests`，只在扩展版测试宿主中执行真实链；默认宿主调用明确失败，不跳过。复用原场景 MCP／独立工具审批、原工作流账本和 `EditorWorkflowJobs`，不使用合成执行租约，不新增产品执行器或模型权限。旧宿主14项与合同8项配方保持原样。

夹具的11个依赖步骤为：显式前提 → 原场景创建与验证工作流 → 已审阅源码捕获 → 独立离线准备 → 独立编译 → 编译产物审阅标记 → 合同8项 → 原生依赖准备 → 原宿主14项 → 回执审计与场景不变 → 逆序显式关闭。每个作业分别核对完整审批绑定、批准不启动、未批准无输出、显式启动、实际排空、退出及实际日志／产物／case 回执。失败保留 owner、逐步证据和原始产物；后续依赖步骤写 `not-run`，不会计为已运行或通过。关闭只轮询原显式关闭链自然排空，不重新执行失败的构建／测试，不强杀或删除证据。

审阅差异仅在测试候选副本的 `ExtensionSampleTag.cs` 增加唯一程序集元数据标记；原 `ExtensionSourceCapture` 导出原包，原异步助手创建完整捕获。新 manifest 必须与明确基线的完整文件集合／大小／哈希逐项一致，仅该审阅文件允许精确 afterimage 差异。编译后使用 PE 元数据读取器（不反射加载 DLL）核对唯一标记及回执中的程序集 SHA，避免“SDK 编译成功但未采用审阅字节”的假阳性。原工作区源码不修改。

阶段 E 需独立生成受信本地输入 JSON（最大16KiB、精确16字段、拒重复／未知／缺失）：`Version=1`、`Workspace`、`Configuration`、`BaselineCaptureId`、`BaselineManifestSha256`、`BaselineSourceSha256`、`HarnessSha256`、`McpRoot`、`McpFingerprint`、`DotnetExecutable`、`GitExecutable`、`OfflineCache`、`VisualStudioRoot`、`ToolsetVersion`、`WindowsSdkRoot`、`WindowsSdkVersion`。均为明确本地路径/版本/哈希，无密钥、URL、命令文本或 PATH 后备。该声明是阶段 E 输入关联，不是自行签发的构建证明；需由同源双配置准备记录提供。测试持有原 MCP 完整运行闭包及必要文件锁。新 CMake 参数 `NCMA_WORKFLOW_SDK_TEST_DLL` 和 `NCMA_WORKFLOW_SDK_INPUTS` 不设置默认后备，缺失保持登记、运行时失败。

新增输入合同2组（独立30秒入口）与真实链1组（独立3900秒入口），当前 **179=历史159+M8.6新增20**。3900秒仅用于新增的同进程依赖链：捕获125秒＋5×（准备45秒＋SDK615秒）＋6×关闭35秒＋账本/IO余量；所有旧入口超时未改变。不能把依赖链拆成多个进程再用序列化 JSON 冒充存活租约/审批；合同与既有宿主测试仍独立分组、由原 harness 跑完所有 case 汇总。

共享捕获 `25deecc279d34ad6bba9c47a66cb4d2d`，source `03ED3C4A2BA11D5E6512301300E75D81DCF1BD34FBF6736715B512668D35A3E0`，manifest `AFC607AAA5E603694D65D36C757225079B4C1D390D19D83F76A270B3D2877F8D`。完整 Services.Tests＋Editor＋MCP＋Gameplay 默认/扩展 Debug 均 **0警告/0错误，85.71/93.08秒**（离线准备+编译90.48/98.06秒）；同源 Debug/Release CMake配置与只读清单各179项。SDK声明/可选宿主、捕获ID/Git未配置；缺可执行文件诊断存在，运行依赖未验证。证据 `out/verification/m8-6/d-sdk-chain-{default,extensions}-first`。

**这不是实际SDK链通过，也不是D/E完成。** 新夹具/新生产助手未运行，无Release托管/原生产品构建、模型/密钥、部署或Git发布。正式151文件/journal不变，默认产品无扩展/Tests依赖、可选产品仍仅两个扩展库。PS5脚本策略前提沿用上一记录、未绕过。下一步补 D 领域/失败路径覆盖与 E 明确同源输入准备，然后按整体阶段末统一执行测试；原M8.4人工、E5-D目标机等债务保留。

### 最新：D 异步原捕获创建与关闭已接入（仅编译，尚未执行）

- 实际工作流面板新增审阅包/Git绝对路径、显式“生成完整捕获副本”、捕获状态、使用该结果准备离线依赖、取消及显式关闭。交接动作本身不启动助手；生成只复制，不还原/构建/测试/安装，后续仍需原独立完整审阅批准和显式启动。
- 固定 `WorkflowSourceCaptureWorker.ps1` 直接复用原 `New-CapturedBuild` / `Open-CapturedBuild`，无第二套复制/哈希算法。Git EXE 显式指定并与助手、PowerShell、完整审阅包一起持锁；原只读 inventory 经脚本作用域函数调用该 Git，不靠 PATH 搜索。Git/PowerShell 支持 DLL 仍是可信外部工具边界，不宣称完整运行库快照或安全沙箱。
- 原创建时 Windows Job、当前用户管道/PID/nonce 检查、原捕获句柄复制继续使用；owner 在 Begin 前已登记，部分失败保留。新完整复制操作有独立120秒 IPC期限，并未调整旧30秒入口；期限或取消只断开通信，正在复制的助手可自然结束。必须传输、根进程及整个 Job 排空才释放助手/工具/bundle/捕获锁；失败副本保留，不强杀、不删除、不自动重试。源码捕获关闭纳入原会话关闭顺序。
- UI 生成捕获只能由相同原 owner 的 Ready 结果进入离线准备；除审阅包 SHA 外，再核对捕获 ID、manifest SHA、源码集合 SHA、配方 SHA及文件数。检查先于工具/缓存读取和准备器启动；请求过生成的交接不能改填其他 ID 走手动路径。原准备器继续重新核对并持有完整输入，不以返回 JSON 冒充构建许可。
- 新4组合约夹具（精确事实/无执行权限、异常字段预算、被动 owner、失败部分持锁）及1组真实助手创建/退出后父持锁/显式关闭夹具均**已登记未执行**。真实入口明确要求 Git，缺依赖失败不跳过。新增独立30秒合同和180秒宿主入口，当前 **177=历史159+新增18**；所有旧名字/超时/原宿主14项配方不变。

本次共享捕获 `471dcc51eadd4f57bc413d8dbfe4ce56`，source `FAAF5B212B1BD09000DA649FCA7EF4D325978E728211168646BB448ADC4F94F5`，manifest `5FAB52F82FD93F7BD135529795A8E36B9DDAC98925E7B6B50F1BB25872D7CCEE`。完整 Tests＋Editor＋MCP＋Gameplay 默认/扩展 Debug 分别 **0警告/0错误，84.04/87.89秒**（独立离线准备+编译88.71/92.72秒）。同捕获 Debug/Release CMake配置与只读 CTest 清单各177项，新属性和名称核对；capture ID/Git未配置，缺可执行文件诊断存在，运行依赖未就绪。没有原生产品目标构建或测试执行。

PowerShell7四脚本语法解析各0错误；PowerShell5用 `-File` 加载只读解析 helper 时，在脚本体执行前被现有执行策略拒绝（UnauthorizedAccess、running scripts is disabled）。只读查询各策略 scope 为 Undefined；未修改策略、未加 Bypass、未替换助手宿主或将拒绝记为通过。**新生产助手未运行，不能据此宣布其运行成功或将未执行用例改归环境失败。** 阶段末需受支持的脚本执行策略及完整依赖前提，任何缺失入口仍不跳过。

证据：`out/verification/m8-6/d-source-creation-{default,extensions}-first`。正式151文件/journal未变；无功能测试、Release托管编译、模型/密钥、部署或Git发布。**D 仍需真实 SDK 全链路及领域/审批夹具；完成整体实现后才进入 E 统一测试。** 下方“仅手工CLI/异步捕获未实现”为历史状态；本节仅覆盖实现/编译，不覆盖运行验收。

### 最新：D 已审阅源码与原作业控制器的精确交接（仅编译）

实际扩展源码页面增加“交接到当前工作流”。必须先选择计划、完成原完整页面确认/重检并导出；交接只取 `CopyBuildBundle()` 的原保留字节，核对导出 SHA，不重新读取候选文件。界面显示计划、编辑版本、补丁、完整审阅包及交接指纹，切换到原作业页；不产生还原/构建/测试/安装许可。

- 原 `EditorWorkflowJobs` 持有有界16个不透明交接对象，绑定原计划哈希、会话/版本、工作区和完整包字节哈希；不能凭相同 JSON/哈希重建对象权限。无回调、MCP、自批入口或默认 Extensions 依赖。
- 普通 `PrepareOffline` 只接受未带补丁的捕获；补丁捕获必须使用本宿主交接，且 manifest 的 `reviewBundleSha256` 精确相等，重复顶层字段拒绝。检查先于工具/缓存扫描及助手启动，随后仍由原准备器验证并持有完整捕获副本/配方/文件集合。局部审阅 stamp 不冒充完整工程 source SHA。
- 尝试准备消耗一次交接，失败不自动重试；原捕获 ID 不可通过另一个相同交接复活旧作业。撤销使该捕获的构建/原生/测试后继失效，由原队列请求停止，不提前释放、不回滚领域命令。修复需新审阅、捕获、独立审批。
- **完整捕获创建目前仍须显式调用原 `Capture-ExtensionBuild.ps1`，再填写输出 ID。** 本片没有启动捕获助手，UI 异步创建/排空持有链仍待接入，不能标“源码→构建全自动闭环完成”。原导出文件/命令不能授权编译或安装。

新增3组交接合同/真实账本用例及1组可选扩展真实 presenter 用例，接现有 continuation 入口；全部未运行，测试中谱系部分为明确合成状态，不代替真实 SDK/Job 证明。CTest 登记未改，仍175项，本片未重跑库存。更新过时的“发布/安装未实现”界面提示，不改变独立安装人工许可。

首轮捕获 `f8cc0d7d7f8044c5be1f984292a98bd1`：默认 Debug 0/0、84.08秒；扩展测试缺 `Ncma.Editor.App` using 导致 CS0246、0警告/1错误，产品 Editor 本身编译成功。失败源码和日志保留。修复后新共享捕获 `565de3e0842d44bfa39cbad875506f6a`，source `909D42FBAC699F7139DA4E16ADD6A41C344D284F4128663ACE56943A752B7A34`，manifest `C408F1EE3ED9B4C712125ED7C387647586061FE8BC3B28AD608FADFD550391C7`；完整 Tests＋Editor＋MCP＋Gameplay 图默认/扩展 Debug 分别 **0警告/0错误，84.17/88.58秒**（明确离线准备+编译88.79/93.31秒）。默认产品依赖无 Extensions，可选产品仅2个扩展库，两者无 Tests 运行依赖。证据 `out/verification/m8-6/d-source-handoff-{default,extensions}-{first,second}`。

正式151文件和journal未变，无功能测试、新生产链路、Release/原生产品构建、模型/凭据访问、部署或提交推送。**下一步仍是 D 原捕获异步创建及真实 SDK/领域全链路夹具，随后 E 整体测试。** 下方记录按历史时点保留。

### 最新：D 工作流续接、失效停止与分离诊断（仅编译）

- 原账本统一检查计划状态、计划哈希、预期编辑版本、会话及冻结/历史失效/编辑忙碌。取消、撤权、过期、失败不再因哈希未变而允许继续；已完成计划可验证其精确提交版本，但不能接纳之后不相关的人工编辑。
- 离线准备在磁盘/缓存读取、助手启动、owner 分配之前拒绝失效计划。准备中的来源失效仅请求一次停止，资源仍保留；停止失败与释放失败分栏显示、只允许显式重试，不强杀、不隐式 Undo、不回滚已发生的外部 IO。实际 presenter 隐藏失效审批页，禁用后续准备按钮。
- 已失效或观测失败的作业即使最终真实退出码为 0，也不转换为正常成功回执；保留队列原因，证据标为 ObservationLost，不完整或已有失败，不得报 CompileSucceeded / TestsPassed。
- 新五组真实编辑器账本/presenter 夹具接独立 `--workflow-continuation-tests` 和完整 harness，覆盖四种同哈希失效、停止/关闭分别失败与显式重试、完成后人工编辑隔离、修复不复活旧批准、旧 UI 事件。**执行租约为合成对象，不是实际 SDK/Job 排空验收；全部未运行。** 原观测用例增加退出 0 的五种异常队列结果，未放宽断言。

独立 `ContinuationHost` 入口 180 秒；原合同入口 30 秒、原宿主 14 项配方及所有历史入口不变。当前清单 **175=历史159+新增16**；不修改此前 174 项记录。

本次共享捕获 `f2e4d558d751422998c6895f67c2f71d`，source `1F287914E81D5A5AE34298776A32875D17C97011866985CCFD65E52CE48AEA43`，manifest `9E7BBF556B52676EB82956FDFF88FA807F9420228608221D985083E54981FB42`。完整 Services.Tests＋Editor＋MCP＋Gameplay Debug **0 警告/0 错误，85.41 秒**（离线准备+构建90.25秒）。同捕获 Debug/Release CMake 配置与 CTest 只读清单各175项，新入口属性及两配置名称一致。缺可执行文件诊断存在、capture ID 未配置，运行依赖未就绪；没有执行 case、新生产链路或原生产品目标。正式151文件/journal未变。证据：`out/verification/m8-6/d-continuation-first`。

**D 仍未完成：** 编辑版本检查不代替外部资产/源码磁盘新鲜度校验；仍需场景/动画/UI/扩展的完整来源串联、已审阅源码→原捕获副本→独立离线准备/构建的 UI 交接，以及真实 SDK 全链路夹具。整体实现后才进入 E 双配置统一测试；本片不宣称运行验收，无部署、提交推送或模型/密钥访问。下方“最新”均为历史时点。

### 最新：C3 测试库存登记与捕获原生测试源码闭包修复（未执行测试）

新增 `engine/build/cmake/NcmaWorkflowTests.cmake`，只追加 15 项 Windows CTest 入口；原入口、超时和历史 M8.5 清单/报告器不改。11 组合同入口各 30 秒；真实捕获桥接和原宿主入口各 180 秒（桥接内部原 30 秒等待不变），捕获输入脚本 60 秒、库存合同 30 秒。不是将旧 30 秒入口抬高以制造通过；所有 harness 保持跑完所有 case 汇总失败。

- 新 `M8_6-TestGroups.psd1` / `M8_6-TestInventory.ps1` 必须逐名核对 **174=历史159+workflow-new15**，拒绝未知、缺失、重复、禁用以及新命令开关/超时/串行/标签漂移。保留原历史三组归因，其中旧 `environment-blocked` 仅是历史来源，不自动诊断当前环境，更不删分母或判通过。旧 M8.5 报告器仍会拒绝未分类新项，不暗中扩大其结论范围。
- `NCMA_WORKFLOW_WORKSPACE_ROOT` 明确测试 IO/捕获查找工作区，区别于生成图的捕获源码根；`NCMA_WORKFLOW_TEST_DLL` 显式指定测试程序集。真实捕获桥接需 `NCMA_WORKFLOW_CAPTURE_ID`，缺省为空仍登记测试，执行会明确失败，不能跳过或退回旧目录。配置参数本身不证明同源、依赖闭包或执行许可。
- 新五组库存合同已登记但未运行：精确新旧总数/无通过权限；未知/缺失/重复/禁用；命令及属性漂移；捕获 ID 未配置仍保留；新脚本不获得执行策略绕过。原捕获桥接基础四组增加独立 CLI，不改变用例断言。
- 首次捕获 `b00c1b09e0c340519e015c268b317860` 的完整 CMake 配置失败：旧捕获选择器遗漏 `tests/`，导致原生测试和插件夹具源文件缺失。原失败日志和捕获保留在 `c3-inventory-first`，不能归为环境问题或改记通过。修复 `Get-CapturedOriginInputs`，将原生测试 `.cpp/.c/.h/.inl` 等完整目录与 tools 纳入原规范哈希/复制/身份持锁流程，不关闭 `NCMA_BUILD_TESTS`、不回读现场补洞。`Test-WorkflowBuildInputs.ps1` 增加第八组真实复制/持锁/作者后续修改不污染副本的夹具，尚未运行。

最终编译/库存共用捕获 `4c67e6f95a0941d0ba197522c60cdd8e`，source SHA256 `63B00037A220A0B3D03D164EAE1FFDE22E64983CE2DAA2FFC26CFF5EE7DB5F70`，manifest `71EE19B4889CF92500A2800DE4307DB5B3531C6E36D96A14B5732F2EEE335CB7`。完整 Services.Tests＋Editor＋MCP＋Gameplay Debug 离线编译 **0 警告/0 错误，78.40 秒**（独立离线准备+构建 82.72 秒）；首轮 `abbf72a1` 编译 0/0、78.28 秒亦保留。最新证据 `out/verification/m8-6/c3-inventory-second`。

同一捕获以测试开关保持 ON 生成 Debug/Release CMake 图，两次 `ctest -N` 和 JSON 均 **174 项、名称一致、新入口属性一致**。只执行配置/编译器探测和库存读取，**未构建原生产品目标、未执行任何 CTest/case/新工作流**。两配置都实际出现缺可执行文件诊断，捕获 ID 未配置；报告明确 `inputsConfigured=false`、`runtimeDependenciesVerified=false`、`accepted=false`，不是就绪或验收结论。五份脚本 PS7/PS5 语法解析各 0 错误。正式 151 文件和 journal 未变，无模型/密钥、部署、提交推送。

**下一步 D（仍未完成）：**

1. 补齐场景、动画、UI、托管扩展的同一来源/计划/审批/真实回执串联，明确显示已提交领域命令与不能隐式回滚的外部 IO；保留原工具审批，不增加模型自批入口。
2. 完整接通已审阅源码补丁→原捕获副本→独立离线准备→无还原构建→原生准备/测试→只读证据；修复需新捕获和新审批，不能覆盖后续作者编辑或复活旧票据。
3. 增加真实全链路夹具与实际 presenter 的停止/换会话/过期/错源/残留资源检查，不能以本次库存或合成状态机代替。完成整体实现后进入 E：完整双配置编译、明确匹配运行前提、174 项及新增必要集成入口逐项测试；新增入口须显式更新清单，所有失败保留并修复重测。人工模型/目标机性能债务独立，不标通过。

下方“尚未登记 CTest/缺闭包”均为历史状态，本节仅覆盖对应实现事实，不覆盖运行验收。

### 最新：宿主精确暂存与借用链已接通（仅编译）

- `PrepareHostTests(nativeJob)` 只接收当前工作流/编辑版本下、未关闭且已有 `NativeInputsPrepared` 的原生租约。原生租约已借用同一托管生产者；宿主借用原生产品，禁止从任意路径或序列化成功回执恢复权限。旧 `PrepareTests(buildJob, host)` 仍拒绝绕过原生前提。
- 审阅页展示每个批准输入及目标映射；审批、启动、证据封存、关闭仍是分离的人类动作。宿主按钮只在正确的活体原生产品上可用，不自动执行或授予模型审批权。
- 许可消费/输出预留后，才独占创建 `jobs/<id>/inputs/out/managed`。同源 MCP 全文件放 `editor-mcp` 子目录，Gameplay 全文件和原生插件全文件放其父目录。测试 driver 在原有持锁编译目录运行，不从现场/旧树 `out/managed` 取依赖。
- 纯映射检查先拒绝缺失/混合/自身生产者、重复与大小写别名、文件目录冲突、单文件 64 MiB/总 512 MiB/2048 文件/2048 目录预算。复制读取原保留句柄而非重新打开来源路径，整批复用 64 KiB 缓冲并逐文件校验 SHA256。目标完整集合及字节再次测量并持锁到原根进程、后代和传输完全排空；新增/移除目录条目在执行边界再核对。**不是防恶意并发新增文件的 OS 沙箱。**
- 失败保留部分目录/文件和资源 owner，显式排空关闭后才释放；无覆盖、自动重试、删除或强杀。上游有借用者时不能释放，正常关闭逆序走原生命周期。
- 新五组夹具：精确映射/构造不创建输出、映射别名及文件目录碰撞、原生生产者/缺失/预算、原句柄多次大文件及空文件复制/原字节锁、目录碰撞/遍历/未持锁父目录。接完整 harness 和 `--workflow-host-staging-tests`，**均未执行**。原 managed closure 断言保留并补原生前提；这些夹具也不代替真实宿主全链路验收。

新捕获 `f82a9a70c84c4cf5ae3dfb24683cf28d`，输入 SHA256 `D1838A8F7B8D0052102B33CD97E68BFF433C7349DB475D784EC522B56BC9D18E`，manifest SHA256 `0DFE809F75EBCD3ACF568A54807BA6378B7D2B2B5536F0E9336B6F8E3578A8D2`。完整 Services.Tests＋Editor＋MCP＋Gameplay 图离线 Debug 编译 **0 警告/0 错误，81.27 秒**；含独立本地缓存准备 85.56 秒。证据 `out/verification/m8-6/c3-host-staging-first`，正式 151 文件/journal 校验未变。无新生产作业/暂存/原生配方/功能测试执行，无模型/密钥访问、部署或 Git 发布。

**剩余：C3 的 CTest 库存及分组、D 完整领域/扩展链和 E 双配置统一测试。整个 M8.6 尚未完成或验收。** 下方“宿主禁用/缺暂存”是历史时点，仅由本节覆盖实现状态，不覆盖旧失败及未验收债务。

### 最新：原生依赖独立准备已接线（仅托管编译，未运行）

新增固定 `workflow-native-prepare-v1`，沿用原捕获助手、审批队列、单次许可、Job、限额日志、取消与排空，不引入另一个执行器。它借用同源活体托管构建中的固定 driver，独立人工审阅并点击启动后才能执行；原 build/test 许可不会自动授权原生准备。

- 本地明确选择 Visual Studio 根目录、MSVC 版本、Windows SDK 根目录与版本。准备核对并持锁七个 EXE：CMake/Ninja/cl/link/lib/rc/mt；审阅页包含这些路径/哈希、include/lib 路径、实际配置/构建参数和输出路径。**头文件、库和编译器支持 DLL 仍是受信安装 SDK，不声称完整字节冻结或任意代码沙箱。**
- 只从捕获副本配置新独占 `jobs/<id>/native`，使用 Ninja，目标固定 `NcmaPlatform`、`NcmaRenderer`、`NcmaGui`，并关闭 managed 构建、测试、Python worker 验证。显式指定七个工具路径和 `/WX`，不用 shell/vcvars 脚本，不传任意附加目标/参数，不 restore、publish、安装或自动联网。
- CMake 子进程采用结构化参数和收敛环境；复用父级 Windows Job，日志有界转发。普通关闭不支持时等待自然退出；不因取消、失联或超时强杀。配置及构建完成后核对 CMakeCache 的源码、配置、生成器、工具路径、警告策略和 launcher；父租约在排空后再次核对并封存结果、cache 和插件目录。
- 新结果 `NativeInputsPrepared` 只表示依赖已准备，不是 `CompileSucceeded`、`TestsPassed` 或安装许可。警告/错误计数未单独观测，保存为 null，计数范围明确标为 `native_tool_exit_counts_unobserved`，不得把 null 改称 0。完整阶段的原生验证仍须另行执行。
- Editor 本地面板增加工具版本输入、独立原生准备按钮、完整审阅和回执封存。原工作流只关联不可变证据，不自动完成计划或授予模型权限。源托管构建和工具持锁到完整排空/显式关闭；原生产品提供受限借用，不允许有借用者时释放。

七组新合同夹具已注册到完整 harness 与 `--workflow-native-preparation-tests`：工具清单/副本/版本语法、固定目标/路径、收敛环境、坏缓存、错请求绑定、未执行与未知计数、人工审阅权限。**均未运行**。现有 PowerShell 准备助手语法解析 0 错误，不代表实际捕获或原生配方运行。

新捕获 `b8f0addb60cc4267995d2af7d42daa4b`，输入 SHA256 `0FB0B7E69FE887735DB7EE6FF4219B19BFDB1220DE5F0EAEC2471B8F7D49725C`，manifest SHA256 `5B2399E1A476BAC1DB5284B8A1468487CCDB833E8AA2F7FEB249BF242932F8BE`。完整 Services.Tests+Editor+MCP+Gameplay 图 **Debug 0 警告/0 错误，83.19 秒**，含独立离线本地缓存准备 87.53 秒。证据 `out/verification/m8-6/c3-native-first`；原生 driver、租约与 CMake 均未运行。两个自动权限审核超时均在写入前发生，核对后各重试一次成功，不记为编译/运行失败。

**仍未完成**：批准后的精确 managed/native 暂存、真实宿主借用链和 14 项入口接通；CTest 库存/分组、D 完整领域/扩展链、E 双配置统一测试。宿主入口仍禁用，不回退旧产物、不标通过。本轮正式 151 文件和 journal 未变，无功能测试/原生运行/模型或密钥访问/部署/Git 发布。

### 最新：同源托管宿主依赖已接入（仅编译）

`Ncma.Editor.Services.Tests` 通过 `ReferenceOutputAssembly=false` 构建引用原 `Ncma.Editor.Mcp` 和 `Ncma.Gameplay.Sample`，二者随同一捕获/配置/产物根生成，不从工作树 `out/managed` 取旧 DLL，也没有成为生产 Editor 的运行引用。

`WorkflowCompiledProduct` 现在同时持有测试运行目录、MCP 目录、Gameplay 目录；三个 owner 在 IO 前创建，失败仍保留直至显式关闭。共同依赖校验检查必需 DLL/deps/runtimeconfig（库无需 runtimeconfig）、实际选定 target 和主程序集、声明的依赖文件、重复 JSON 字段；不支持的 RID 回退明确拒绝。编译回执和指纹包含三组产物；宿主任务要求 MCP/Gameplay 与测试 DLL 来自同一生产作业，合同测试不接受额外宿主组。

新增 5 组夹具，接入完整 harness 和 `--workflow-runtime-closure-tests`，覆盖库/可执行文件差异、缺少依赖、无效或 RID 清单、生产者混用以及合成文件的持锁/新增项失效。**均未运行**，不把合成依赖或编译当成实际宿主执行。

新捕获 `a15edd9e73b143d4ac810e10a9bc3440`，源码输入 SHA256 `FE83EC9712C96FD2675D785027FE75A9A92E8EC04998634C488288EEDC8BC5FD`，manifest SHA256 `4EE86F6FB4CB89A0251BAE5E724AE41A95151D589BD3E2B0B94D1EA372BAEC9E`。完整 Tests+Editor+MCP+Gameplay 图离线 Debug 编译 **0 警告/0 错误，78.47 秒**（含独立本地缓存准备 82.73 秒）。证据 `out/verification/m8-6/c3-managed-host-first`。只读检查实际输出目录分别 81/7/8 文件，deps 中选定 target/主程序集存在，Editor deps 未出现两个测试专用引用；这不是功能验证。

正式安装 151 文件与 journal 不变。原生依赖准备、批准后的精确暂存/借用链仍未实现，**14 项宿主入口继续禁用**；C3 库存、D 完整链路与 E 统一测试仍待完成。未执行测试/新生产作业/原生编译、模型或密钥访问、部署、Git 发布。

以下“尚缺 MCP/Gameplay”指较早记录：现已加入同源构建和封存，但完整宿主准备/暂存尚未接通。

### 最新：C3 逐项回执与原审计关联（仅编译，C3/D 尚未完成）

已实现以下接线，**尚未执行新作业或功能测试**：

- 单一测试清单：合同 8 项、原宿主 14 项；实际 harness 注册、人工审批及请求校验共用清单，未知/重复/缺项在执行前拒绝。独立 `--workflow-evidence` 通过严格请求选择固定配方，仍使用原 CaseHarness，失败后继续其余 case，再汇总。
- 每条 header/begin/case/complete 写入有界 UTF-8 JSONL 并 flush；排空后逐项核对任务/清单及汇总，保留中断前已执行行，未执行项不删分母。坏 UTF-8、重复字段/用例、外国任务、尾部追加和缺失 footer 都不能通过。异常只在结构化结果中记录类型，不含 message；原本地日志不是通用脱敏器，不外发模型。
- 实际构建封存 `WorkflowCompiledProduct` 持有完整测试运行目录字节及依赖清单；测试借用同一活体结果，仍走原捕获/单次审批/Job/排空。测试命令只有 DLL、固定开关、独占请求路径，不 build/restore。运行时 RID 扩展闭包当前不支持时明确拒绝，不静默加载其他文件；SDK/框架仍是受信外部前提，并非完整机器冻结或任意代码沙箱。
- 回执 schema v2：警告/错误计数未知时为 null；增加 EvidenceComplete、InputsObserved、DiagnosticCountScope 与 ObservationLost。编译计数来自唯一英文 summary，测试计数明确是 case_harness，不能混作编译警告。测试通过还必须包含非空 `test-results.jsonl`。配方指纹 v2 绑定真实统一入口；旧独立 CLI 仍用于阶段测试，不能把旧捕获回执套用到新源码。
- 不用辅助 Worker 退出码冒充 SDK/测试退出码。真实 Job、传输和日志队列已排空但结果丢失时，可保留未知结果/失败诊断并显式关闭，不能宣布通过。输入失效或日志封存失败不会遮掉可读取的已执行 case 前缀。过早封存先拒绝，不消耗最后一次观察机会。
- 本地证据页面展示完整任务/输入/输出/日志/逐项结果；原工作流审计新增 schema2 Verification 哈希关联，完整回执留在作业 owner，不复制大文件清单进计划账本。重复观察幂等、同一 Job 冲突拒绝；保留原计划版本/来源。关联不会推进工作流、改变完成状态或 Undo，也不授予构建/测试/安装/MCP 权限。

**尚未完成的阻断项**：`workflow-host-v1` 还缺同一捕获和配置的 MCP 客户端、Gameplay 及原生插件准备/暂存。当前 UI 明确禁用此入口，API 在创建租约/占用额度前拒绝；不从旧树或 live `out/managed` 借产物，不把它记为环境通过或已执行。C3 的 CTest 库存/分组注册、D 托管扩展及完整端到端串联、E Debug/Release 统一测试仍待完成。此片不能称作整个 M8.6 完成。

新增 8 组观察合同用例（清单一致性、完整/失败、截断、非法记录、首败后继续、未知编译计数、固定命令、退出观测丢失），接入原完整 harness 及 `--workflow-observation-tests`。原合同/生命周期/宿主审计断言保留，补充结果文件、可排空未知结果、哈希链关联及状态/历史不变断言。**均登记未运行，CTest 未新增，超时未改。**

两轮新捕获副本均仅用本地缓存显式 offline restore，再 `dotnet build --no-restore`，完整 Services.Tests + Editor Debug 0 警告/0 错误：

| 记录 | 捕获 / 输入 SHA256 | MSBuild / 含离线准备 |
| --- | --- | --- |
| `out/verification/m8-6/c3-observed-first` | `f35034f985864c23abc93c284428c93c` / `F7B4268DAADFD559F1ED93CD799C9786AF501B1FF71D67F416C89A4C0F38D886` | 74.73 / 78.89 秒 |
| `out/verification/m8-6/c3-ledger-first`（最终编译源码） | `876f6f8e1cc647c3a6f27ff6c84a7382` / `CD0FF5E7DAF131D195F0B7AF1D527245CC1626C0F589FCB5198BC0E40F33CF68` | 75.01 / 79.26 秒 |

最终 capture manifest SHA256 `5E36E0C638D1D2C452BBA8B8342452C578CC98F9F1A67A6B4B1D7EFBD2C9EE14`。两轮正式安装 151 文件和 journal 均核对未变。文档随后更新不冒充全仓冻结。未运行功能测试/新生产作业/映射/原生/Release/GUI，不访问模型或密钥，不部署、提交或推送；历史失败和 E5-D/人工验收仍独立保留。

以下为历史实施记录，当前剩余项以上段为准。

### 最新：独立离线审批与同源构建链路（仅编译，尚未运行）

已按用户选择实现独立离线准备：固定 `editor-services-restore-offline-v1`、独立清空源配置、本地归档 feed、独立 packages/artifacts。批准只入原队列，启动仍需独立点击；构建保持 `--no-restore`。不存在隐式下载、自动批准、自动构建/测试/安装、模型自审批或新的 MCP 执行器。

`EditorWorkflowJobs` 由原 `EditorSessionOwner` 持有；本地面板展示完整绑定/配置/命令/缓存路径/工具/辅助脚本/输入列表与指纹，要求全部分页读过和明确勾选。准备、审阅、批准、启动、取消、封存与关闭分开；隐藏面板仍泵日志/状态，不启动或重试。工作流面板打开时禁用背景场景 HUD 输入。

`WorkflowRestoreProduct` 只从实际已退出且排空的成功 SDK 租约形成活体产物；持有完整有界 artifacts/packages 文件字节和目录身份，检查新增/缺失项。日志通过追加时 SHA256 与落盘字节核对，空 stderr 也纳入清单。`WorkflowCompileLease` 借用该产物，绑定同一捕获/配置/变体/SDK，核对项目图、包归档 SHA512/文件 SHA256、生成 NuGet 导入入口，写入独占映射 props 后通过原单次许可启动固定命令。源还原仍被借用时不能释放，关闭按逆序处理。**这是可信引擎/SDK/包构建逻辑，不是任意 MSBuild 自定义代码的禁网/隔离沙箱；目录持锁也不能阻止恶意并发新增子项。实际求值、完整运行闭包及阶段验收仍须测试，不据路径/退出码直接生成通过结论。**

取消/超时时有界 UI 日志队列可退出等待，标记日志不完整并断开传输；Worker 继续排空 SDK stdout/stderr，等待真实 Job 排空，不强杀。关闭后的终态观测保留，避免将已启动进程重新报告成“未启动”。Worker 内嵌 C# 静态编译通过，**Run 尚未调用**。

本轮新增 5 组离线参数/精确缓存/空文件/树闭包/被动宿主用例，以及 1 组日志封存用例；均登记于原聚合 harness 和相应独立入口，**尚未执行**。构建固定参数从 17 改为 18，原断言保留并明确增加映射参数断言。

验证记录：

| 捕获编译 | 来源与结果 |
| --- | --- |
| 离线审批 UI 首轮 | 捕获 `ba48b576b28a4dbf8c29a9b7d81ca686`，source `88B5B70D5DC265331570F30B8BA5754992925C7B930DA862B59C4D201FBD897C`；本地离线准备后 Services.Tests＋Editor Debug `--no-restore` 编译 0/0，74.64 秒（合计 78.97 秒） |
| 还原→构建链路最新轮 | 捕获 `4b79ca254c1c402aaa2fd193334ca2a0`，source `64FC7D61CC644D3FAAFBE41674EF3472E31A12C7D688D30281A2E64F89929A13`，manifest `C87A8D4137F323E7F4F0E999B7A43560C3D93C6325178008E50D0A3B69D63358`；同图 Debug 0/0，74.86 秒（合计 79.04 秒） |

证据分别在 `out/verification/m8-6/offline-approval-first`、`offline-build-chain-first`。最初的普通 restore 编译入口被权限检查拒绝，未执行；`Compile.ps1` 保留为拒绝证据，**禁止运行**。替代 `Compile-Offline.ps1` 使用显式清空源配置和唯一 `C:\Users\liu\.nuget\packages` 本地源，在线漏洞审计关闭（不声称已扫描漏洞），缺包即停，后续编译强制无还原。两轮正式 151 文件和 journal 均未变，未联网下载、运行产品新 Worker、测试、部署或 Git 发布。

**M8.6 仍在实施，不标为完成。下一步：C3 固定测试配方生产租约、真实产品/逐 case 回执与库存接线，D 完整工作流关联，再统一 E 双配置测试。** 当前只有 Debug 编译，不是全 69 项、原生、Release、实际 SDK 作业、人工或性能验收。以下“尚缺构建租约/UI”等为本轮之前的历史状态，不覆盖本节。

### 之前的实施记录（保留原始范围与结论）

**执行范围已扩大为整个 M8.6（进行中，尚未验收）。用户已明确批准新增独立“离线依赖准备”审批；联网恢复仍未授权。** 正在接通 `editor-services-restore-offline-v1`，复用原队列、单次许可、捕获桥接及输出 owner；固定本地 feed、独立 packages/artifacts、独立 NuGet 配置、不读取用户源/凭据配置，禁用 HTTP 缓存与在线漏洞审计（不声称完成漏洞扫描）。构建配方仍保持 `--no-restore`。固定参数依据 [dotnet restore 官方合同](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-restore)；这不是针对任意 MSBuild 自定义代码的禁网沙箱。

已实现包清单文件 SHA256 与归档 SHA512 的同句柄核验/持锁、独立审批后精确归档复制、Job 管理的真实离线 SDK 租约与有界 IPC 日志。SDK CLI 不支持普通关闭时报告 `stop-unsupported`，等待自然排空、保留 owner，不强杀。新 `WorkflowJobWorker.ps1` 与 SDK 路径尚未实际执行；脚本 AST 仅解析，内嵌 C# 仍需编译检查。**尚缺构建/测试生产租约、完整还原来源与导入核验、真实回执和 UI 接线；整个 C/D/E 未完成，不开始统一测试。**

包核验片 captured `bd1008dc889e4e8ea2365644632eb68d` / 输入 `F4E8A5C148E59EDE3BAC70B7D86BCAAFEBA448B7C148AFB0AAB8EFDFE3AAA50B`，Services.Tests＋Editor Debug 0 警告/0 错误，75.52 秒；离线租约首次 captured `9d6e48c4c36246afab30f8b43d291d28` / 输入 `509C4F3726C037B0D4C60FC23C192A70F8947A33169F4C2C5CBC061AADAF7B10`，同图 Debug 0/0，82.12 秒。证据 `out/verification/m8-6/c2b2-packages-first` 与 `c2b2-offline-first`；后续继续编辑会使首次编译不再覆盖最新源码，阶段末须重新编译。两次正式 151 文件/journal 未变，未测试/部署/Git 发布。

本片完整 Services.Tests＋Editor Debug 捕获编译 **0 警告 / 0 错误，MSBuild 77.57 秒（助手计时 78.04 秒）**。捕获 `d376b5dd34da4a41bf0fba77b1a9193d`，输入 SHA256 `AC4E38BA0A8F6872DBDD49C6C442752D58C346FDD98F7F16AF4F4FA462635B3A`，manifest SHA256 `6291FAAC41792EA0E92F3476FDF038D166308D91518C51ED30F40C67190A6306`。证据 `out/verification/m8-6/c2b2-layout-first`；首次补丁工具旧路径在写入前失败，已记录。正式 151 文件及 journal 未变。使用既有捕获编译入口和普通依赖还原；没有运行新工作流、映射求值、测试、模型、部署或 Git 发布。下一步仍需完整前提核验、映射求值/命令/审批精确绑定与真实构建适配，C2b-2 尚未完成。

**当前 C2b-2 输出映射片：项目引用图持锁检查及显式 MSBuild 映射数据已实现，尚未执行。** 新增 `WorkflowRestoreGraph`：从任务声明发现最多 128 个项目，拥有每项目三文件检查器后再获取锁；检查固定根项目、完整可达引用、缺件、循环与无关额外项目。失败保留已取得的输入锁，显式关闭后释放。派生 `restores/<id>/artifacts/obj/<project>` 为只读还原输入，`jobs/<id>/artifacts/obj/<project>` 为可写中间产物；不复制或重写资产清单、不隐式 restore、不复用旧树。

映射生成独立 UTF-8 props 字节和指纹，拟作为 `CustomAfterDirectoryBuildProps` 输入，设置每项目 `MSBuildProjectExtensionsPath`、`ProjectAssetsFile` 与 `BaseIntermediateOutputPath`；含未知/偏离映射的构建前诊断。该数据**尚未落盘或执行 MSBuild 求值，也未接入现有命令/人工审批/生产租约**，`CompilationAuthorized` 始终 false。SDK 选择、配置/特性与 restore 来源证明、包字节、生成导入的完整依赖及通配符目录闭包仍需核验；仅有映射不能放行构建。新增 3 组夹具接入原 restore harness（现 11 组），均未运行。以下保留上一片证据。

**当前 C2b-2 前检片：离线还原输入检查与只读工具链观测已实现并编译，未测试；C2b-2/C2/C 尚未完成。** 新增 `WorkflowRestoreInspection`/`WorkflowRestoreAssets`（对声明的 `project.assets.json` 与两个生成 NuGet props/targets 先整体校验、再按字节钉住并解析项目/输出/包/引用绑定）与 `WorkflowToolchain`（在安装目录内观测 SDK 版本及 `dotnet.exe`、`MSBuild.dll`、`NuGet.Build.Tasks.dll`、`csc.dll` 的字节）。两片共 15 组用例登记，全部未运行。各自从新捕获副本编译：restore 片 **0 警告/0 错误 85.51 秒**（捕获 `0f93a968905d47c6aaee8b6fcb10a136`，输入 `EFF148EFEB17AC95A5CE7CA16CC548CF1B5BE31AC14478B7391A2F5252B05EAB`，manifest `BCA103F6B3615FC859CA7341FB2933918DB4E26AF7E11AC8B5D9046609D8A604`）；toolchain 片 **0 警告/0 错误 80.51 秒**（捕获 `98552cf2dd2049d6854ec7b416b18944`，输入 `917F577CDDDDA3B91A5EDC607A5E18BF4EEF392B6CD7822C05AC045259210260`，manifest `BFF09CE4B60AE402DD49D5E5C88CBA5F26563DCB3180B0827D12CDADC6F068D1`）。两片都**没有启动任何进程、没有执行 restore、没有证明宿主的 SDK 选择**，也没有 `IWorkflowBuildLease`、构建启动或输出预留；正式 151 文件与 journal 逐项未变。toolchain 片保留两次失败编译：一次是 harness PATH 缺 git（构建前失败），一次是新测试文件缺 `using Ncma.Editor.Services;`（10 条 CS0246，产品文件本身已编译）。证据 `out/verification/m8-6/c2b2-restore-first` 与 `c2b2-toolchain-first`。**仍缺**：还原产物位置与固定 `-p:ArtifactsPath` 输出的精确映射（不成立时必须显式阻断，不得静默 restore 或复制），以及生产租约、真实异步启动、有界日志泵与 Job 排空。以下为前一片记录。

**C2b-2 第一片（历史）：固定命令与输出所有权已实现并编译，未测试；C2b-2/C2/C 尚未完成。** 新 `WorkflowBuildCommand`、`WorkflowBuildOutput` 与原队列启动许可已连接；当前真实准备 Worker 仍只支持 prepare/close，**没有真实构建租约、SDK/离线 restore 核验或实际构建启动接线**。本片新增 8 组用例（执行状态使用合成租约，目录/日志是实际 IO 夹具），登记全 harness 及 `--workflow-build-output-tests`，均未运行；不把这些夹具当成真实进程/Job/SDK 证明。

完整 Services.Tests＋Editor Debug 首次编译 **0 警告/0 错误，84.64 秒**。捕获 `b52b63c37a2a40aa81d2d63c9577d113`，输入 SHA256 `5C4BC649A8A68578C2D6A4F84DF07AD9F52C825FF77AABF302D22689C94FB703`，manifest SHA256 `9C3E3BE79B2739689FE3BBB5EB91ECC5C5CFFFD49CF4E1A4998727119586FC8D`。证据 `out/verification/m8-6/c2b2-output-first`：4 份原源码归档、完整日志/binlog、捕获与编译回执。基线格式错误原件保留，正确基线为 `baseline-verified.json`；这是证据生成工具错误，不是测试或产品编译失败。正式 151 文件和 journal 逐项未变；未执行新 Worker/输出代码/测试/模型/部署/Git 操作。本次验证仍用**既有捕获编译入口及其显式普通依赖恢复**，不冒充新工作流的无恢复构建运行证据。以下 C2b-1 为此前记录。

**当前推进 C2b-1：真实准备进程与原锁句柄桥接已实现、编译，尚未运行；C2b/C2/C 均未整体完成。** `WorkflowCaptureBridge` 调用固定 `WorkflowCaptureWorker.ps1`，Worker 复用原 C1；宿主通过私有管道验证进程身份并复制原始文件锁句柄，保留至完整作业排空。当前只允许准备和关闭，没有 build 指令、`IWorkflowBuildLease` 实际构建适配、C2a 队列或审批 UI 接线。新增 4 组基础＋2 组显式捕获 ID 宿主夹具，全部未运行。

本片首次完整编译 **失败 11 条语法诊断/0 警告，67.48 秒**，原因是新增路径检查中两处字符转义错误；失败捕获 `2cb6390b9b36408e8143e995d481932f`、完整日志/binlog 均保留。修正后完整 Services.Tests＋Editor Debug **0 警告/0 错误，80.25 秒**，新捕获 `73372695ce814eb4a1a0eac9a434024c`，输入 SHA256 `F8C30788391A8BA717728BC4DB3338B7040E01D1BE0ACE98507584A7EF907F4E`，manifest SHA256 `5253673525A682CC7A43DCC4D42731E78E76D2C8418C0E3F0E8FD1796EF2A46F`。`CapturedBuild.ps1` 和新 Worker 经当前 PowerShell/Windows PowerShell 5.1.19041.6456 语法解析通过，**没有启动新 Worker/IPC/测试**。证据 `out/verification/m8-6/c2b1-first` 包含原助手/原入口、两轮捕获与完整日志、结果、PS5 解析及选择性最终指纹（不是文档更新后的整仓冻结）。原正式 151 文件/journal、C1 适配脚本未变；无部署/提交/推送，旧树未写入。首次较大补丁自动审批超时、核对未落盘后以较小补丁获批重试，不是产品或测试失败。

下一步 **C2b-2：实际构建命令、工具/依赖前提、独立输出、C2a 审批队列接线**；之后 C3 实际回执、D UI、E 统一验证。下面 C2a/C1 为历史交付。

**当前推进 C2a：内部审批与作业生命周期基础已实现、编译；未测试，C2/C 整体未完成。** 单次/60 秒审阅、有界队列、启动后异常保留、取消/超时/排空及独立关闭重试已经实现为内部控制器；仅有合成测试租约，没有真实进程适配器/人工审批 UI/Agent 接线。12 组用例登记未运行，详见下方 C2a 合同。下一步 C2b 真实 C1 租约桥接与异步构建，之后 C3 真实回执、D UI、E 统一测试。

本片完整 Services.Tests＋Editor Debug 两次编译 **0 警告/0 错误，79.14 秒/76.11 秒**。第二次补强异常观测不能撤回“已启动”状态，采用独立捕获 `a5c5e4a69bd5472cbdcfd668c0f1290e`，输入 SHA256 `D9849889F451296CC10CF9B17F59605A106CB3F125B08B2B9E5A92F745DA7BB2`，manifest SHA256 `C91758A8DF21276C1D2E280BB84DFBA234553778D4BB47ED3991B5C1AEE3356B`。证据 `out/verification/m8-6/c2a-first` 保留原入口及两份捕获/完整日志/binlog/回执；最终选择性源码指纹不冒充文档更新后的整仓冻结。正式 151 文件/journal、原捕获助手和 C1 适配器均未变；旧树未写入，无测试、模型、部署、提交或推送。

**C1 历史交付：捕获输入租约已实现，仅静态检查/编译；C 整体未完成。** 新增 `scripts/WorkflowBuildInputs.ps1`，只允许脚本所在工作区、GUID 派生的捕获/作业路径与固定 Services 构建项目；复用原 `Open-CapturedBuild` 校验完整捕获文件/manifest/输入集/配方/可选审阅包，并持有原文件及祖先锁。输出目录必须不存在，只检查/锁定最近已存在祖先，不创建或预留目录。准备结果显式 `processStarted=false/launchAuthorized=false/outputReserved=false`；不启动作业、不产生审批许可。C1 目前明确拒绝测试配方，后续 C3 才准备其实际运行闭包。

`Test-WorkflowBuildInputs.ps1` 准备 7 组真实文件用例：持锁/无执行权限、四种预期指纹不符及失败释放、实际捕获字节变化、配方/配置/身份、异工作区/旧输出保留、缺固定项目、junction 别名。脚本跑完全部 case 后汇总；**未运行，尚未接入 CTest**。实际测试时每份合成捕获和 junction 映射单独留证，不清理、不遍历链接；不得以跳过权限/环境负例算通过。本轮只做两脚本语法解析（当前 PowerShell 及 Windows PowerShell 5.1.19041.6456），没有调用新的 `Open-WorkflowBuildInputs`。

完整 Services.Tests+Editor Debug 从新捕获副本编译 **0 警告/0 错误，78.38 秒**。这是既有托管图编译，不证明新 PowerShell 适配器运行正确。捕获 `fb6791c8a889402ebdb4c9140a370d81`，输入 SHA256 `87532BB3720D5FBBD4563D35BD14BE86F1B49C5AC0CE5DE622EC5540657DCADE`，manifest SHA256 `FE56C0A572D17CE6FD39EAB40CB177EAF9BA0F87892B2F77B06469953D469AF1`。证据 `out/verification/m8-6/c1-first`：基线、完整日志/binlog、两宿主解析、编译结果。正式安装全文件/journal、原 CapturedBuild 助手及既有托管入口均未变；旧树未写入，无测试/模型/部署/Git 操作。下一步 C2 人工审批与异步构建作业生命周期；C3 测试配方与实际证据回接随后实施。

**A、B 历史交付：已实现并编译，尚未测试。** B 的 `WorkflowVerificationTask`/`WorkflowVerificationReceipt` 为纯数据合同，已绑定版本化任务/工作区/工作流/计划来源、捕获 ID/manifest/输入集/捕获配方/可选补丁包、固定操作配方、配置/产品变体、工具版本和二进制哈希。完整声明测试清单与执行输入文件集合分别有指纹；回执必须匹配精确任务，且逐名覆盖清单。区分编译成功、测试通过、失败、不完整、未运行、环境阻断、取消、超时和输入变化；无安装授权。

新增 7 组纯合同用例，既接入原 Services 全部用例，也提供 `--workflow-contract-tests` 独立入口；原工作流/A 用例单独入口为 `--workflow-host-tests`。均复用跑完所有 case 后汇总的 CaseHarness，未执行、不调整超时，也尚未新增 CTest 注册（留 C/E 接线与库存核对）。完整 Services.Tests+Editor Debug 首次编译 **0 警告/0 错误，77.28 秒**；捕获 `1bdd58928f674bce98680359a8fc1519`，源码输入 SHA256 `E0327B9F42E17845A56234FE55B770374F207904342FF43BC1656ACE182C043D`，capture manifest SHA256 `F7B3D88B752EB574B9105519EDBA72D9CE3B5BDDD4D4409A22A72D472F6E6994`。新树 `out/verification/m8-6/b-first` 留存原入口、完整编译日志/binlog、回执及正式安装基线；正式 151 文件和 journal 未变。无测试/模型调用/部署/Git 写入。以下 A 记录为上一片证据。

**A 历史交付：已实现并编译，未测试。** 原账本已保留完整计划修订及审批/票据/回执/停止事件；新增本地 `CaptureAudit`，没有注册新远程能力、执行器或持久化恢复。4 组真实 Endpoint 用例登记于原 Services harness：哈希链/独立副本/线程/完成终态；独立权限等待与部分提交取消；12 步+2 次修复完整留存；过期/配对撤权/版本冲突只记一次。原用例不删除、不降低断言。等待原工具审批的重复尝试仍由原 Endpoint 审计承担，不膨胀本计划事件日志。

完整 `Ncma.Editor.Services.Tests` + Editor 依赖图从新捕获副本 Debug 编译成功，**0 警告 / 0 错误，MSBuild 78.17 秒**。捕获 `bd66e7b140144c1ba267d7dbe27ac37b`，输入 SHA256 `726E2C0B67766493E0FC68FDC3155BA438D1BF920130B9F2EF278A9A64197F68`，manifest SHA256 `70D3E584F5CEF108936067144B06AD4270DC313152A4E609AFA38899244349B8`。证据 `out/verification/m8-6/a-first` 含两份原始源码、正式安装基线、完整编译日志/binlog 和 compile-result.json。正式安装全部文件/journal 未变；未运行测试、模型调用、部署、提交或推送。下一片 B：精确捕获输入与固定配方的验证证据合同。

M8.6 尚未整体验收。唯一工作区 `F:\NcmaEngineRelease\source`；旧树只读。M8.5 extension-new 双配置 8/8 是历史专项证据，新工作区正式 AI EXE 的部署烟测不是完整回归。M8.4 legacy/环境、人工模型闭环和 E5-D 性能债务仍单独保留。

复用 `EditorWorkflows`、原 Endpoint、领域审批/事务和 M8.5 捕获编译，不新建命令执行器、Undo 历史或审批权限。一个工作流的计划批准不授权其中的工具、源码写入、构建进程、安装、网络外发或 Git。原 MCP 合同暂不扩张。实施期只做静态/编译检查，全部小阶段完成后统一测试；不自动部署或提交推送。

## 小阶段

| 阶段 | 实施内容 | 完成条件 |
| --- | --- | --- |
| A 现役账本可归因 | 原计划及最多两次修订完整保留；观察来源、计划批准、精确票据、结果和停止原因关联；只读有界审计副本 | 原执行链接线；冷路径预算、别名/线程/终态及取消后不回滚用例登记并编译 |
| B 证据合同 | 固定版本任务/产物/构建与测试回执；绑定精确捕获 manifest、输入集、配方、配置、工具版本、退出码、日志哈希与实际覆盖 | 区分失败/未运行/环境阻断/部分成功；旧源码、缺件、错配置、只编译不可计测试通过；未知配方拒绝 |
| C 可信本地作业 | 仅人工批准的固定构建/测试配方与精确捕获输入；参数结构化，无 shell 文本；异步有界队列、取消、排空、资源残留 | 检查新工作区与链接边界；不得回读旧树编译；原捕获输入锁及独立输出；失败停止且无自动重试/发布 |
| D 编辑器串联 | 观察→计划→独立审批→领域命令或已审阅捕获补丁→构建/测试→证据页面；完整分页与明确停止点 | 状态来自真实作业/回执，显示已提交与不可回滚外部 IO；修复需要新捕获/审批，不能覆盖后续编辑 |
| E 整体统一验证 | 场景、动画、UI、一个托管扩展真实链路；取消、过期、换会话、错源、目录越界、坏证据、失败修复、资源残留 | Debug/Release 编译零警告错误；独立入口跑完所有 case 后汇总；原库存核对、缺依赖不算通过；不以 mock 或编译代替真实链路 |

## A 合同

- 仍由原 workflow 发精确票据，原 Endpoint 独立检查权限并执行。审计仅记录，不触发工具、不恢复或自动重试。
- 保留 version=1 原计划全文和每次修订；事件使用独立 schemaVersion=1、递增序号、工作流/连接/编辑来源、计划 SHA256、批准指纹/范围指纹、票据/输入哈希、原结果回执及状态。
- 原预算 16 个计划、每计划 12 步、2 次修复、48 KiB 计划/16 KiB 输入保持不变。每计划最多 3 份计划、64 条事件；正常状态机上界小于此预算。重复轮询/等待独立审批不追加事件；不允许日志驱逐丢失旧证据。
- 哈希链仅检测副本或日志变化，不是签名、防管理员篡改或新的授权凭据。不得接受调用方伪造的审计数据用于执行。事件不保存响应正文、密钥或私有脚本反射内容；计划本身可能含业务数据，仅本地显式读取，未授权外发。
- `CaptureAudit` 返回独立数组和不可变记录，owner-thread 检查；不新增远程 capability/schema。关闭前由宿主显式捕获，不承诺崩溃持久化或重启恢复。
- 完成状态不因后续 TTL、编辑或换连接被改写；取消/撤权/过期/冲突只停止未执行步骤，已提交回执保留。不执行隐式 Undo。

## B 证据合同与 C 的信任边界

- 配方仅三种：`editor-services-build-v1`（完整 Services.Tests/Editor 图）、`workflow-contracts-v1`（纯合同用例）、`workflow-host-v1`（原工作流真实宿主用例）。项目、入口开关、必需产物都由代码固定并纳入配方指纹；不接受任意命令/脚本/项目路径。配置仅 Debug/Release，产品变体仅 default/ai/extensions/ai-extensions，不等于已经实际构建全部变体。
- 每集合最多 2048 文件、每文件 4 GiB/240 字符可移植相对路径；测试最多 4096 个 128 字符稳定 ID。先有界遍历再排序/复制；拒绝路径逃逸、盘符/ADS/设备名、大小写别名、文件/目录重叠、错误哈希/负大小、重复或缺失测试行。快照返回独立数组，记录不可变；审计字段无日志正文/凭据，诊断码有界。
- `InputFiles` 表示该次工具/运行的依赖文件集合，不冒充完整源码列表；捕获源码集合由 manifest/SourceInputsSha256 绑定。C 必须复用 `Open-CapturedBuild` 对完整捕获副本逐文件核验并持有原身份锁，实际编译捕获副本，不从作者树/旧归档读取编译输入。
- **当前纯模型只验证调用方提供的数据一致性，不证明磁盘新鲜度、物理身份、工具版本真实性、进程执行或测试真的发生。** 指纹不是签名或审批票据。C 需要从实际受检文件、进程退出/日志和完整用例库存生成这些值；拒绝伪造证据、未知入口、失效审批和重复任务。模型不得作为原安装回执的替代或放宽。
- 测试结果必须与声明清单逐名精确匹配（排序无关）；缺失/新增/重复行拒绝。实际未跑项保留 NotRun/Skipped/EnvironmentBlocked/Cancelled，不可删减分母。编译任务不允许测试行；退出码 0 但有警告、缺产物或任何非通过用例，只能是 Incomplete。退出失败/错误/失败用例为 Failed；输入集合/大小/字节变化为 InputsChanged；不把失败擦除成通过。
- NotStarted/EnvironmentBlocked 不允许声称已经通过或失败的用例及已生成的产物；部分执行后发生环境问题需保留已执行行、实际退出与剩余阻断行，不能把整次执行改记为从未运行。环境归因本身必须另有宿主证据，不从枚举值推定根因。
- 本片未把验证结果写入原工作流的完成状态、审批或 Undo。C 的可信本地作业连接和 D 的显示/回执关联仍待实现；原 Endpoint 的独立授权保持不变。

## C 分片与未完成边界

1. **C1 捕获准备（本片实现待验证）**：返回在进程内持有的原捕获租约，以及独立 JSON 事实副本；只适配构建配方，未探测/启动 SDK 或任何子进程。租约关闭后不允许把事实副本当成仍被锁定的输入。没有新增自己的内容 stamp 算法，也未替换原捕获助手。
2. **C2 审批与异步构建（C2a 内部控制器已实现，C2b 真实适配待实现）**：可信 C# 宿主绑定工作区会话/计划/源代次、精确配方/工具链与租约，完整人工审阅及有期限单次许可；受限队列、实际固定命令、取消/超时/排空/残留持有。启动前核对并锁定适配脚本及点源依赖与已批准版本相同字节，不能只信路径；原捕获输入锁贯穿实际作业。输出未预留，必须在批准后再次检查并排他创建，碰撞停止，不覆盖重试。捕获租约失效/重新准备须重新审阅，不能自动复活审批。运行可信 MSBuild/项目代码不等于安全沙箱；网络/依赖下载与构建许可不能混为一谈。
3. **C3 测试与真实回执（待实施）**：准备精确运行闭包/原生和客户端前提、两个固定测试入口与完整 case 库存；实际输出汇总绑定到 B，缺依赖不执行后算通过。环境归因必须有证据，失败不得自动发布/安装/推送。新增 CTest 库存/分组在此及 E 中显式登记，不能默默规避 M8.5 的历史精确清单。

以上均未接入正式 Editor 按钮或 Agent 能力；D 才做真实 UI/原账本关联，E 统一测试。不能把 C1 的准备成功当成作业执行、审批或整个 C 验收。

## C2a 内部审批与生命周期基础（2026-10-28）

新增 `WorkflowBuildJobs.cs`，全部为 `internal` 类型，只给现役 Services.Tests 精确程序集 friend 可见性。**当前没有生产租约适配器、真实异步构建进程、人工审批 UI 或启动接线**；唯一 `IWorkflowBuildLease` 实现是测试中的显式合成租约。不能以本片状态机编译或未来纯模型用例通过宣称物理文件锁、实际进程、审批 UI 或 C2 验收。

- 绑定 B 的完整不可变任务、精确租约 ID、辅助文件清单、Runner/工具路径和哈希、工作区、派生作业输出及可见超时；返回不可反序列化的内部审阅对象。审批必须是同控制器保存的同一对象及其指纹，外来/复制对象拒绝。有效确认尝试消耗票据，错误指纹、未勾选、过期或失效都不能重用；原计划批准不等于本审批。
- 单调时钟 60 秒有效期，从审阅创建计时，入队不续期；Approve 与 StartNext 在输入/来源回调前后检查。确认后只排队，显式 `StartNext` 才尝试启动。最多 3 个排队、1 个活动作业；按批准次序 FIFO。每控制器最多 16 个终身作业记录（关闭后也不驱逐 ID），无自动重试、自动恢复或自动下一作业启动。
- owner-thread 与回调重入防护；Review 成功返回后才转移租约所有权，失败仍由调用者持有。启动前先登记所有权，因此适配器在创建子进程后抛错也不能丢失租约。真实适配器必须遵守这一所有权契约，而不是先创建进程后抛掉 owner。
- `Poll` 不阻塞，不做每帧文件扫描；来源失效、取消、超时或观测异常只发一次停止信号。停止失败留独立异常类型，可显式 RetryStop；不执行进程扫描、强杀或隐式 Undo。需要宿主持续 Pump；当前超时是 owner 观测驱动，不宣称后台独立限时保证。适配器必须提供真实截止时间和完整借用者排空证据。
- 主进程退出不是 `ExecutionDrained`。已观察到启动不能被后续异常/反向状态撤回；观测丢失不代表没有进程，仍阻止下一次启动。所有借用者确认排空才允许 Close；Close 失败保留原 owner、首个关闭异常与重试状态，Poll/Shutdown 不暗中重试。已排空但关闭失败的租约不影响健康新作业启动，仍受总预算限制；成功关闭才清除 owner 引用。
- 执行结果、停止失败与关闭失败分别保留。退出 0 只记 `ProcessExited`，**不是 CompileSucceeded/TestsPassed**；C3 仍须检查真实产物、警告、日志、输入闭包和完整 case。安装/恢复依赖下载授权均不存在；`IsSandbox=false` 明确可信 MSBuild 可以执行项目代码，不能从“固定配方”推导 OS 隔离或禁网保证。

12 组纯生命周期用例已接原完整 harness，并新增 `--workflow-job-lifecycle-tests` 独立入口（跑完全部 case 再汇总）。覆盖 FIFO/无隐式启动、外来/重复/拒绝票据、审阅/排队/回调耗时到期、来源/租约变化、启动后异常、主进程与借用者排空/超时/取消、关闭失败独立保留、Shutdown、线程/重入、队列/终身预算、错误观测/启动状态单调、审阅及状态副本。**均未执行**，没有新增 CTest 注册或改变既有超时；C3/E 再做显式库存接线。

### C2b 的必要边界（C2b-1 完成准备实现，余项留 C2b-2）

1. 用真实固定桥接保持 C1 原 `Open-CapturedBuild` 租约贯穿等待审批、构建和排空；不能把 JSON 事实重新包装为“仍持锁”的租约。准备失败/断连/失效后重新捕获审阅，不复活旧票据。
2. 进程启动前物理锁定 C1/Worker 和完整点源依赖链，与已审阅版本逐字节一致；C2a 当前 helper 清单只是有界一致性数据，**未证明依赖完整性/文件身份**。工具链也必须核验，不将路径/调用方版本当实际 SDK 身份。
3. 批准后再次验证并排他创建 GUID 输出目录，既有路径碰撞即失败。固定项目、结构化参数、无 shell 命令文本，默认 `--no-restore`；缺依赖显式阻断，不能悄悄联网恢复或从旧树取产物。
4. 异步日志有界落盘、真实子进程/后代退出和取消协议、失联/超时/关闭失败保留资源；日志/异常不暴露密钥或响应正文。明确拒绝不支持的宿主，不降级成无身份保护执行。
5. C3 将实际执行证据接回 B；D 才把内部审阅对象接到正式本地人工 UI 与原工作流账本。当前没有 Agent 可调用审批或任意执行入口。

## C2b-1 真实准备桥接合同（2026-10-28，未执行）

- 原 `CapturedBuild.ps1` 只新增 `NcmaCapturedBuildPins.BorrowHandles()`；内容/完整集合/配方/审阅包哈希算法与核验流程不变。原助手源码已归档并核对。借用方法仅用于可信内部准备桥接，必须保持原 lease 存活；数值不是本进程可用句柄，不接受 Agent 传入或从日志恢复。
- 新 Worker 仍调用原 `Open-WorkflowBuildInputs`，持有原租约等待关闭。C# 宿主当前锁定固定 7 个辅助脚本及工具/Runner 可执行文件，脚本清单以 `WorkflowCaptureBridge.RequiredHelpers()` 为准：`scripts/AiCandidatePackage.ps1`、`scripts/CapturedBuild.ps1`、`scripts/EditorDeployment.ps1`、`scripts/EditorPackageDependencies.ps1`、`scripts/WorkflowBuildInputs.ps1`、`scripts/WorkflowCaptureWorker.ps1`、`scripts/WorkflowJobWorker.ps1`（后续新增）。拒绝 reparse/hardlink/路径替代和字节不符；只启动固定 Worker，参数结构化引用，无任意脚本/命令文本，无执行策略绕过。原 C1 脚本未修改。
- 私有管道为当前用户限定，并核对真实 Worker PID、仍存活的进程句柄及 nonce；不是只验证管道名。请求最大 16 KiB、回复最大 2 MiB；严格事实字段/精确任务绑定，最多 81924 个借用句柄，逐个从该进程复制到宿主并检查磁盘对象类型。原 Worker 正常或异常退出后，宿主镜像仍持有同一底层文件锁；部分复制失败也保留已复制句柄，直到排空。此行为**仅实现，未运行证明**。
- 使用创建时 Job 列表关联，而非先启动再加入 Job；主进程退出与 Job 活动进程计数分别观察。无 breakaway、Kill-on-close、强杀或不受监管的降级启动。失败/超时保留 owner、句柄和仅类型诊断；显式关闭只发 close/断开准备管道，只有 IPC 任务完成且整个 Job 排空才释放镜像与辅助文件锁。该设计不声称防管理员、对任意恶意代码的 OS 沙箱或宿主崩溃持久化恢复。[Windows 进程创建属性](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute)、[Job 活动进程计数](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_accounting_information)。
- 最小子进程环境不透传任意 API-key/proxy/SDK hook 环境项；工作目录/编译临时文件只在新工作区 `out/verification/m8-6/preparers/<nonce>`，末级目录排他创建，证据不自动清理。**这是准备进程临时目录，不是构建输出预留**；C1 的 `outputReserved=false` 保持不变。Windows64/Job/宿主不满足要求即失败，不调 ACL/MIC、不提升运行身份。
- 已锁定的 EXE 字节不等于完整 PowerShell/SDK DLL 闭包或真实 SDK 版本证明。当前 B 的版本字段仍不能当实际工具探测结果。C2b-2 必须补齐实际工具选择、依赖/restore 输入前提和审批后排他构建输出；缺依赖显式阻断，不允许隐式联网恢复。Worker 现在只认 prepare/close，无法启动用户项目代码，也不是完整 `IWorkflowBuildLease` 实现。
- 4 组基础夹具接完整 Services harness：固定辅助闭包/无执行许可，精确事实/路径/重复字段，Windows 参数引用/路径拒绝，真实脚本锁及哈希失败释放。另 2 组宿主夹具通过 `--workflow-capture-bridge-tests <captureIdN>`，验证真实 C1 准备/Worker 退出后宿主仍持锁，以及错误 manifest 从不 Ready；必须显式提供完整捕获目录，缺依赖失败不跳过。失败 owner 留在测试进程内，不自动强杀或清理。六组均**未运行**、未注册 CTest，之后由 C3/E 接线并核对库存。

## C2b-2 命令与输出所有权片（2026-10-28，仅编译）

- `WorkflowBuildCommand` 只生成独立参数数组：固定 Services.Tests 项目位于捕获副本，原 capture.props/targets 与 GUID 作业 artifacts 路径明确传入。Debug/Release × default/ai/extensions/ai-extensions 全部显式设置两个特性开关，默认值不会继承上次 AI/扩展构建。无任意项目、附加属性、shell 文本、publish/install 或不受控二进制日志入口；参数副本不能修改原命令。
- 固定 `--no-restore`、`-noAutoResponse`、禁用构建服务器/节点复用/共享编译器，警告视为错误。路径额外拒绝 MSBuild 的分号、百分号、属性展开等语法，不能只靠引号防止属性注入。依据官方 [dotnet build](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-build) 与 [MSBuild 开关](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference)。**这些参数不是 SDK 实测、依赖完整性、禁网或任意项目代码沙箱**；工具链、项目自定义目标仍需可信输入边界。
- 原 `WorkflowBuildReview` 增加固定命令指纹；批准与 StartNext 仍分离。队列私有实现的启动许可仅在同步 `Start` 回调期间有效，绑定同一 active lease，消耗时再核对 owner 线程、来源/租约及 60 秒有效期，返回或抛错后失效；无 JSON/哈希还原许可。异租约、重复消耗、过期或异线程不能创建输出。仍只限可信内部代码，不授予模型能力或把内部类型当安全沙箱。
- `WorkflowBuildOutput` 在任何 IO 前由宿主持有。许可消费后才使用固定盘的精确祖先锁，排他创建 `jobs/<jobIdN>` 及 artifacts/logs/temp/cli-home；已有目录或文件碰撞失败，不覆盖、不换新 ID 自动重试。失败留下目录/日志与 owner，**无自动删除或清理**。准备阶段 C1 的 `outputReserved=false` 事实不被改写为执行许可。
- stdout/stderr 各固定 CreateNew 文件、各 4 MiB 上限。超限在追加前失败并锁存，不静默丢尾或继续写另一通道；落盘失败亦锁存。关闭前需要实际适配器报告完整 borrower drain，观测或 flush 失败保留所有权，显式重试；当前测试使用合成 lease，不能据此证明真实 OS 排空。日志为本地项目诊断原文，**不是通用秘密脱敏器**，不得自动提交/外发给模型；未来日志泵必须有界，且将超限/IO 失败标为不完整而不是编译成功。
- 新增 8 组用例：8 个配置/变体组合与参数副本；属性语法/非编译配方；批准不创建、显式启动与排空；已有输出保护；异租约/过期/回调逃逸许可；双路日志精确预算；观测丢失/线程边界；缺许可与重复消耗。原 C2a 的 12 组断言保留，仅随 Start 签名增加许可消费。所有用例未运行；CTest 库存仍留 C3/E 明确登记。

### C2b-2 仍需完成（下一步）

0. 此清单为早期前检片的剩余项；当前实现进展以页首最新记录为准，不能把映射数据或编译通过等同于运行验收。
1. **实际工具和离线依赖前检**：在捕获上下文观测实际 SDK，绑定其具体位置/版本/字节与所需运行依赖，不用调用方版本字符串冒充观测，也不重新钉死全仓 SDK 版本。既有 project.assets.json、生成 NuGet props/targets、项目引用图与包文件必须属于同一捕获/配置/特性/工具条件；存在文件或上次编译通过不等于可复用。确认新输出布局与 restore 中绝对路径的精确映射，必要时显式阻断，不悄悄 restore 或借旧树产物。
2. **生产租约和实际启动**：把前检通过的物理租约、原 C1 句柄、当前单次许可/输出 owner 接入唯一 `IWorkflowBuildLease`。真实进程启动前完成所有权登记，固定参数和最小环境；stdout/stderr 有界异步泵，后台期限/取消及根进程与整个 Job 排空分别观察，失败 owner 不丢失、无强杀或自动重试。
3. C3 才生成真实日志/产品/case 回执和 CTest 库存，D 接本地审批 UI/原账本，E 统一测试。退出 0、目录创建或命令生成都不是 CompileSucceeded/TestsPassed；正式 EXE/安装权限不改变。

## 交付记录原则

每次编译使用新的捕获副本与独立产物目录；保存失败源码/日志，不能改写旧通过证据。实施记录放新树 `out/verification/m8-6`。正式 EXE 保持切换代次 `69274b950c50443aba1427724484cf03`，SHA256 `379AA113435A8E7F44CC02120BDE9DF95AA5129E1DD154005712694A1E9B1C3F`。只有阶段 E 完成才给相应范围测试结论；人工模型、目标机、自包含和长稳分别记账。
