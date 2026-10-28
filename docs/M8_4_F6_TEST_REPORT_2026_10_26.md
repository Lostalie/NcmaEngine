# M8.4-F6 自动验证报告（2026-10-26）

## 2026-10-27：恢复验收，前置条件仍阻塞

用户明确继续 M8.4 验收，不延期后启动 M8.5。本轮完成只读前置审计，没有运行新的功能测试，也没有重新执行旧失败替换：

- 当前选定源码指纹 `111DF3E09AA51CE99A1CA5424901A925EA23F87D10D4E4C67EA5CFE6485C20C3` 与发布/预检记录一致；当前 Release AI 候选清单再次校验通过。
- 正式安装全部 154 文件（包含清单外允许的用户状态）、原默认版 148 文件备份和部署日志未变；两份历史原子保存失败场景 SHA256 未变。
- `ctest -N` 与 JSON 库存分别核对 Debug/Release 各 152 项、名称一致、无禁用项；`-N` 未出现缺少可执行文件提示。**这只是库存检查，不证明全部运行依赖就绪或测试通过。** 本轮执行数为 0，旧完整实跑结果不改写。
- 只读标签检查仍显示工作区、正式与候选 EXE 的 Low 完整性标签。最新实际启动结果仍是本日此前的包校验 exit 0、DX11 启动 exit 4 / `editor_user_data_access_denied` / `0x80070005`；本轮没有把它重跑成新结果。

完整复验需要用户管理的受支持独立 Windows 测试环境，不移除当前安全标签/放宽 ACL，不换宿主或日志根来绕过边界。届时重新建立完整双配置构建、匹配输入回执/候选/冻结，再执行全部登记项；不能套用旧源码回执。原子保存 `0x80070497` 的因果诊断、真实模型调用审批和人工闭环仍是独立未完成项。

证据：`out/verification/m8-4/f6-readiness-2026-10-27/result.json`、`labels.json` 和两配置 CTest 库存。没有产品代码修改、真实密钥读取、模型调用、部署或新提交推送。F6/M8.4 保持未通过。

## 最新诊断改进：拒绝原因可见，仍不是完整 F6 通过

源码更新至 `111DF3E0...`，日志初始化现在输出精确的访问拒绝/IO 错误码、异常类型及 HRESULT，不打印原始路径消息、不重试或换目录。
四组 Services+Editor 依赖图编译 0/0，专项各 9/9，共 36/36；当前独立 EXE 无钩子负例两次 exit 4，正确报告访问拒绝，**不计为正常启动通过**。
正式安装、安全标签及旧失败场景未变。见 [F7 第四轮](M8_4_F7_REPAIR_REPORT_2026_10_26.md)。新源码尚未完整 F6，环境安全前提与原子保存问题仍待解决。

## 最新诊断：EXE 继承低完整性标签，完整验收仍未通过

F7 第三轮只读核对确认 `F:\NcmaEngine` 显式低完整性标签向构建/正式 EXE 继承；Main 前 EXE 为 Low、相同代码的 dotnet 对照为 High。
两次对照均 EXE exit 4 / dotnet 八帧 exit 0；普通外部日志目录的访问检查对 Low 拒绝。详见 [F7 第三轮证据](M8_4_F7_REPAIR_REPORT_2026_10_26.md)。
这解释已观察的日志拒绝，但不等于所有历史失败已定位或修复。用户无火绒拦截记录，不归因于火绒；未修改安全标签/ACL或替换入口，原子保存仍待确认。
源码仍 `09101FB1...`，没有新的完整 F6；必须先满足正常运行的安全前提，再全新匹配输入复验。正式安装未变。

## 后续 F7 第二轮修复状态（不是新的完整 F6）

源码更新至 `09101FB1...`：包内项目状态迁出程序目录，四组默认/AI Debug/Release Services+Editor 依赖图编译 0/0，用户数据专项各 6/6。
当前构建独立 EXE 已越过路径重叠检查，但 CPU 包内项目与 DX11 烟测都仍在外部日志目录创建时被拒，exit 4；原子保存问题未解决。
未修改权限/防护或重试旧场景；正式安装不变。见 [第二轮修复证据](M8_4_F7_REPAIR_REPORT_2026_10_26.md)。
下方 rerun2 是源码 `49F2020F...` 的完整历史结果，**不能作为当前源码的全量验证；F6 仍未通过**。

## 最新：修复后完整双配置复验（rerun2），仍未通过

本节优先于下方保留的 rerun1 历史。当前源码 `49F2020FD874878A8FB5F068DDBE1E608EB9F834584FAAEAE55E36DFECF25120`，重新构建、发布候选、冻结后，**双配置 304 个登记入口全部执行：273 通过、31 失败，无跳过、无未运行入口**。未修改产品/用例源码，未放宽超时、断言或包清单。不能标为 F6 / M8.4 验收通过。

| 范围 | 执行 | 通过 | 失败 | 跳过/未执行入口 |
| --- | ---: | ---: | ---: | ---: |
| Debug CTest | 152 | 137 | 15 | 0 |
| Release CTest | 152 | 136 | 16 | 0 |
| Debug 补充 case | 16 | 15 | 1 | 0 |
| Release 补充 case | 16 | 15 | 1 | 0 |

入口数不等于内部 case 数。失败的部分旧 harness 仍会在异常处提前退出，其未到达的内部检查没有通过证据；不能由“304 入口全执行”推断所有子用例全执行。CTest 自动展开的前置 fixture 按实际计划/JUnit 单独对账，不重复计入 304 主入口；本轮没有导入历史通过结果。

### 本轮构建、候选与冻结

- 全部 66 托管项目：Debug 90.28 秒、Release 87.86 秒，均 0 警告 / 0 错误；两配置原生全目标退出 0。未使用仓库整条 Build 脚本作为验证捷径。
- 可选 AI / AIContracts 重新编译及来源回执通过，代次 Debug `d4a1a96b5b374e428ce942b590264aef`，Release `8a8b2c71f1014b2c9afbb6714756ed5d`。
- 每配置 6 种新候选，代次 Debug `d136625712c645e5b3766850d3d7b80a`，Release `67ceb2eb840b400fa6ea6b185ff490df`；ModuleClient、动作样例和 35 项配置输入同步重新准备。
- `ctest -N` / JSON 各 152，命令、属性、原超时逐项核对，无禁用/缺失入口。F5 冻结 9,098 文件 / 75 目录集合 / 38 个不同可切换目标，SHA256 `ECD7F59E6B077531B44ED0835E8C34755B7BF36DEE99287FBB33A08673E11051`。
- 两配置均按清单备份后切换 35 项输入。Debug 之后和整轮结束的完整冻结复核均通过；没有重现旧日志污染导致 Release 无法开始的问题。共享测试输入最终为 Release，不是正式部署。
- 六个产品宿主 API 入口在两配置均通过；指纹/跨壳、完整 EditorServices、DomainPresenter、AIContracts 均通过。与独立 apphost 失败分别记录。

### 失败清单

下列 15 个入口在 Debug / Release 各失败一次（共 30），另有 Release `NcmaRenderingTests.PostHosts` 一次失败：

- `NcmaRenderingTests`
- `NcmaRenderingTests.SceneUiHosts`
- `NcmaRenderingTests.SceneUiPublicationProducts`
- `NcmaRenderingTests.SceneUiJointProducts`
- `NcmaRenderingTests.ShadowJointProducts`
- `NcmaRenderingTests.GraphicsRecoveryJoint`
- `NcmaRenderingTests.GraphicsRecoveryJointOff`
- `NcmaRenderingTests.GraphicsRecoveryJointLow`
- `NcmaRenderingTests.GraphicsRecoveryJointMedium`
- `NcmaPlayerTests`（完整 37 case 汇总为 36 通过、Package 1 失败）
- `NcmaEditorDeploymentTests`
- `NcmaAiPackageTests.Relocated.DefaultCpu`
- `NcmaAiPackageTests.Relocated.DefaultDx11`
- `NcmaAiPackageTests.Relocated.AiCpu`
- `NcmaAiPackageTests.Relocated.AiDx11`

主要失败位于真实独立 Editor apphost 初始化/启动关闭合同，不再是旧 `ai_package_file_set`。两配置补充验证均为：远程配置/无项目配置及宿主 13 case 通过、部署变体拒绝 2 case 通过、默认→AI→恢复流程在 apphost 烟测失败 1 case。

### 隔离启动诊断：两条路径，未修复

在全部测试和最终审计结束后，从当前 Release 默认候选复制两个新隔离副本（各 145 文件），逐文件核对。诊断用 CLR startup hook 仅观察异常类型/方法/行号，不输出异常消息、凭据或模型内容，不更改异常控制流；没有修改产品代码、重试旧失败部署或安装到正式目录。观察器构建 0 警告 / 0 错误，不是产品验收。

1. `--editor --project <副本内 sample> --frames 8 --cpu-only`：退出 2，`ArgumentException` 来自 `EditorUserDataPaths.Resolve:24` 的程序目录重叠拒绝。当前“普通项目写 project/out/user”与“包内示例项目不可写回程序目录”缺少可用存储策略。
2. `--smoke-test --project <副本内 sample> --frames 8`：退出 4，`UnauthorizedAccessException` 来自 `Directory.CreateDirectory → ApplicationLog..ctor:18 → CandidatePresentation.Start:240`。外部日志目录创建被拒；具体 ACL/运行环境/安全策略来源尚未确认，不能笼统判定为 Defender 或引擎算法问题。未修改 ACL、防护或进程权限。

两个副本运行前后文件集合及哈希一致。该观察器有诊断扰动，仅用于定位，不代替无观察器的正式通过测试，也不证明上述两个原因已逐一解释所有失败入口。

### 原子保存再次失败，保持现场

Release PostHosts 在 `SceneDocumentFiles.Save:60` 再次报 `0x80070497`（无法删除要被替换的文件）。目标：

`out/verification/m2/post-host-Release/2faf9cd289d043cb91dfa7f6367d5522/scene-fixture/b61bd477c11546318adf7d963ddcac6b/start.ncmascene`

只读检查：Archive、2,590 B、SHA256 `35E98B5C522D182893FC81E9AC5D67D2742E9499A727BDCCBE9EC28475264EB1`。不重试该替换、不删除目标、不强制覆盖、不降级非原子保存；原 rerun1 失败文件同样保留。Debug 本轮该入口通过不抵消 Release 失败。根因仍待文件生命周期/占用与系统错误诊断。

### 最终审计与交接

- `f-tests-rerun2/result.json`：304 主入口及原日志、JUnit、展开计划；`audit.json`：独立逐项对账、冻结与候选复核。所有执行进程已结束，审计时遗留辅助进程为 0。
- `f-prerequisites-rerun2/readiness.json`、`f-packages-rerun2/delivery.json`、`f-freeze-rerun2/freeze.json` 是本轮独立输入证据；全部旧证据保留。
- `f7-startup-diagnostic-first/result.json` 及两个副本 stderr/stdout 保存启动诊断，未更改源候选。
- 正式 EXE `29A9F4A7...`、DLL `6E462301...`、清单 `E02CA04A...` 未变；150 个安装清单 payload 文件逐项哈希/大小一致。无真实模型调用/密钥读取、无正式部署、无提交/推送，E5-D 继续延期。
- 下一步：先明确包内示例项目的外部用户数据策略与 apphost 可写性诊断，再处理原子保存失败；不得退回程序目录写日志、忽略包新增文件、自动重试覆盖或放宽断言。修复后需新的同源回执/候选/冻结与双配置复验。暂不进入完成/发布阶段。

---

## 以下为 rerun1 历史记录（保留，不改判）

## 结论

**未通过，未完成双配置验收。** 用户要求“开始 F6 测试”后，本轮重新准备当前源码的构建、包和冻结输入，完整执行 Debug；Release 在输入切换前被冻结完整性检查拦住，没有执行测试或替换 Release 输入。没有删除失败证据、强制覆盖或放宽断言/超时。

| 范围 | 登记/计划 | 实际执行 | 通过 | 失败 | 跳过 | 未执行 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Debug CTest 入口 | 152 | 152 | 143 | 9 | 0 | 0 |
| Release CTest 入口 | 152 | 0 | 0 | 0 | 0 | 152 |
| Debug 配置界面/隔离部署补充 case | 16 | 16 | 16 | 0 | 0 | 0 |
| Release 对应补充 case | 16 | 0 | 0 | 0 | 0 | 16 |

CTest 入口数不等于内部 case 数；附加的前置 fixture 执行不重复计入 304 个登记入口。缺依赖、失败、未执行都不能算通过。

## 构建与输入前提

- 按实际项目清单构建全部 66 个托管工程，Debug/Release 均为 0 警告、0 错误；两配置原生全目标构建退出 0。没有调用整条 `Build.ps1` / `Build.bat`。
- 两配置 `ctest -N` 与 JSON 清单各 152，逐项核对命令、属性、原超时和入口文件；没有禁用入口。
- 选定构建输入 SHA256：`E062B5AE3BFF704F3C517740F2E80BF3C5F39B14DB0EC7CC750CAC9328A3E663`，停止后仍一致。本轮没有修改产品或测试用例源码。
- AI/合同编译回执代次：Debug `348d243594cc463d923f7591e2a0fcba`；Release `292f932d47bf472c8cd0b9b62c47be30`。
- 每配置重新准备 6 种候选、ModuleClient、动作样例和 35 项测试输入映射。候选代次：Debug `ed69d895a09e4847a264664423f1277c`；Release `980851447fae468598e0c001740e1838`。
- F5 冻结 9,096 个文件、75 个目录集合及 38 个不同可切换目标；两配置预检均通过。冻结 SHA256：`EF78D318069BD388C80FDBD22B86920ED8AB7DC62DFEEE205302D6B7E5F6993F`。
- Debug 实际切换成功，35 项备份/哈希核验通过；备份位于 `out/verification/m8-4/f-freeze-rerun1/activation-Debug-6a2b3b9a36f647db9c1a63b0f69a5362`。测试共享输入目前仍为 Debug，不把它当作正式部署。

## Debug 失败清单（9 个入口）

| 入口 | 实际证据与边界 |
| --- | --- |
| `NcmaAiPackageTests.Input.Fingerprint` | 15 case 中 1 失败。换行路径负例先触发 PS5/.NET Framework `Path.IsPathRooted` 的包装异常，而非约定的 `ai_package_source_path`。不是接受了非法路径，但异常契约未满足。 |
| `NcmaAiPackageTests.Input.CrossShell` | 2 case 均失败于 `Culture count`。两个子进程输出文件实际各有 en-US/tr-TR/zh-CN 三项，哈希一致；父测试对 JSON 数组的计数需修复/验证，当前结果仍失败。 |
| `NcmaRenderingTests.PostHosts` | 原子场景保存失败，Windows `0x80070497`（无法删除要被替换的文件），位于 `SceneDocumentFiles.Save`。保留该场景与日志，没有重试该替换或使用更强覆盖方式；具体文件占用/文件系统原因尚未确认。 |
| `NcmaEditorServiceTests` | 291 case 完整汇总：284 通过、7 失败。1 项 `WorkspaceTests.CheckAiHeader`（第 90 行）断言；6 项 M8.4-D3 动画资源票据/审批/取消流程 `Animation module drain/complete` 内部超时。未达到 CTest 180 秒总超时；没有提前停止整个 harness。 |
| `NcmaAiEditorTests.DomainPresenter` | 9 case：6 通过、3 失败，均为动画资源审批/撤销/取消流程 `Domain presenter drain` 内部超时。 |
| `NcmaAiPackageTests.Relocated.DefaultCpu` | `ai_package_file_set`。 |
| `NcmaAiPackageTests.Relocated.DefaultDx11` | `ai_package_file_set`。 |
| `NcmaAiPackageTests.ProductHost.DefaultCpu` | `ai_package_file_set`。 |
| `NcmaAiPackageTests.ProductHost.DefaultDx11` | `ai_package_file_set`。 |

停止后完整文件集合审计发现，Debug 默认候选包新增了 `out/user/logs/editor-candidate.jsonl`，不在不可变包清单中，解释了上述四项默认包文件集合失败。不能通过删日志、忽略所有新增文件或放宽包校验改判。

通过范围包括代码生成器构建/发布合同、实际 IPC/gRPC worker、完整渲染回归、图形恢复/资源/UI/阴影回归，以及 AI 版 2 项实际 apphost 搬移、4 项产品宿主和最后的 AIContracts。真实提供商网络调用没有发生。

## 测试编排问题与 Release 停止原因

1. 首个执行脚本假定每次精确 `-R` 只有一个 JUnit case，但 CTest 为 `NcmaRenderingTests` 自动追加 `NcmaKernelReferenceCapture`。原日志明确显示两项都通过，渲染主测试耗时 173.66 秒；脚本却以 `JUnit entry mismatch` 停止。原停止结果保留不改写。
2. 新建 `f-tests-rerun1-continuation1`，将原 74 行结果和第 75 项的真实成功 JUnit/日志绑定 228 项哈希。续跑脚本按 CTest 展开的实际计划核对每个 fixture 名称、命令、属性和 JUnit，再从第 76 项继续。没有重跑或伪造渲染通过结果。
3. Debug 完整执行后，Release 切换前重新核验全冻结输入，发现 `managed/Ncma.Editor.App/bin/Debug/net8.0/out/user/logs/editor-candidate.jsonl` 被追加，从 245,358 B 变为 246,194 B。检查停止于任何 Release 替换之前，Release 的 152 项全部 `not-run`。
4. 只读停止审计核对全部 9,096 个冻结文件：仅上述 1 个已登记日志内容改变；75 个目录集合中仅 Debug 默认候选包新增同名运行日志。源码哈希不变。应修正“不可变测试输入 / 可变运行输出”的边界，再建立新回执，而不是修改本轮冻结文件使其通过。
5. 停止审计第一版传错路径验证器的工作区根目录，执行失败；日志和脚本保留。第二版只纠正参数，完成只读审计；此辅助错误不计入引擎测试失败数。

## 正式安装与权限边界

- 正式 `out/bin/NcmaEngine.exe` SHA256：`29A9F4A74B0061234715D96D27613F26C199013710C64713B68CFD8E0B6C23D0`，未变。
- 正式 `NcmaEngine.dll` SHA256：`6E4623013E6C8594F52928001568E66907E526AB2FB838B984A3D932ADDE51D7`，未变。
- 正式安装清单 SHA256：`E02CA04AB367DD2EB3A0AD255183DD8851EF231D7F598B45D731527E89779324`，未变；清单内 150 个文件逐项大小/哈希一致。此数是清单 payload 数，不是目录所有运行输出总数。
- 结束时无遗留测试/worker/引擎进程；没有强制终止、清理失败文件或修改用户密钥。
- 无真实 DeepSeek 调用、无凭据读取、无正式部署、无 Git 提交/推送。隔离部署测试只操作其自有测试目录。
- E5-D 目标机性能验收仍延期；不把本机回归或 GPU 诊断当作该项验收。

## 证据与下一步

- 前提：`out/verification/m8-4/f-prerequisites-rerun1/readiness.json`、全量构建日志/binlog。
- 包与映射：`out/verification/m8-4/f-packages-rerun1/delivery.json`。
- 冻结：`out/verification/m8-4/f-freeze-rerun1/freeze.json`、`execution-identity.json`。
- 首轮编排停止：`out/verification/m8-4/f-tests-rerun1/result.json`，包含原日志/JUnit；不是最后汇总。
- **本轮最终汇总**：`out/verification/m8-4/f-tests-rerun1-continuation1/result.json`，包含全部 Debug 152 行和 Release 152 个未执行项。
- **停止审计**：同目录 `stop-audit-v2.json`、`stop-audit-v2.log`；原 `stop-audit.log` 失败记录保留。
- 补充用例：`out/verification/m8-4/f-tests-rerun1/Debug/supplemental/result.json`，16/16。

下一步应先处理运行日志污染包/冻结输入的边界，并按上表逐类诊断、修复；原子保存失败需先核实精确目标状态，不直接重试覆盖。修复后重新准备匹配源码的编译回执、候选包与新冻结，执行两配置全部入口并对账。本轮 F6 不标通过，不进入后续完成/发布阶段。
