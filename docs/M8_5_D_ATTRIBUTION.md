# M8.5-D 分组与归因（2026-10-28）

本文件取代“把全部 158 入口的成功/失败直接作为 M8.5-D 结论”的口径，不改变历史日志、测试断言或安装权限。

## 明确清单

逐项唯一归属见 [M8_5-TestGroups.psd1](../scripts/M8_5-TestGroups.psd1)，不是按失败数量或模糊前缀事后排除。158 项名称须完整匹配：重复、缺失、未知入口全部拒绝归因。

| 分组 | 历史 158 清单 | D 的处理 |
| --- | ---: | --- |
| `extension-new` | 7 | 唯一测试结论范围，任何缺失/失败均不能宣布该组通过 |
| `m84-legacy` | 141 | 引用 M8.4 F6/F7，不作本轮 D 的成功/失败结论 |
| `environment-blocked` | 10 | 本轮记“未执行（环境）”；不计入通过分母 |

后续新增 `NcmaExtensionTests.CapturedBuild` 单独登记，归属 `extension-new`。因此当前应为 **159 = 8 + 141 + 10**；不能把第 159 项补记到旧 158 实跑中。

### extension-new

六个原扩展专用入口：`NcmaExtensionTests`、`NcmaExtensionTests.Editor.extensions`、`NcmaExtensionTests.Player.extensions`、`NcmaExtensionTests.Editor.ai-extensions`、`NcmaExtensionTests.Player.ai-extensions`、`NcmaExtensionTests.PackageContracts`。

另纳入 `NcmaCodeGenTests`：新增 `ExtensionSampleTag` 曾造成全组件差分闭包遗漏，是 M8.5 引入的真实集成缺陷，不能转移为 legacy 债务。第一轮这组实际为 **Debug/Release 各 6 通过、1 失败**；后来独立修正复测才通过，旧记录不改写。新增的捕获构建入口使当前组为 8。

### m84-legacy

完整 141 项逐名列于清单。legacy 表示“进入 M8.5 前已有、归原验证轨道”，不声称所有这些子系统都在 M8.4 才开发。

- [M8.4-F6 自动验证报告](M8_4_F6_TEST_REPORT_2026_10_26.md)：完整历史执行结果、源码指纹、当前环境前提和待验收项。
- [M8.4-F7 修复与诊断报告](M8_4_F7_REPAIR_REPORT_2026_10_26.md)：独立 EXE 存储拒绝、诊断修复、人工/原子保存等边界。

`ai_verification_file_set`（测试污染）、未知原因的宿主断言失败、旧生成器构建/发布用例失败不冒称“环境”。Debug 临时原生启动/TLS 失败也保留原始配置结果，不因 Release 后来通过而重写。

### environment-blocked

明确 10 项：`NcmaRenderingTests`、`NcmaRenderingTests.SceneUiPublicationProducts`、`NcmaRenderingTests.SceneUiJointProducts`、`NcmaRenderingTests.ShadowJointProducts`、`NcmaRenderingTests.GraphicsRecoveryJoint`、`NcmaRenderingTests.GraphicsRecoveryJointOff`、`NcmaRenderingTests.GraphicsRecoveryJointLow`、`NcmaRenderingTests.GraphicsRecoveryJointMedium`、`NcmaPlayerTests`、`NcmaEditorDeploymentTests`。

依据为第一轮 Release JUnit 中明确的 `editor_user_data_access_denied / 0x80070005`；部署项另有原 `deployment-tests/a33dfea0d9d44cba935cb1c413c8ab37/success/deployment/86f5a1822d794bcd808988858804d224/smoke-host.stderr` 同码证据。见 F7 关于进程完整性/存储前提的分析，不将其泛化为其他所有失败的根因。

这里的“未执行（环境）”是**本轮 D 验收状态**。历史中它们已经启动甚至跑完部分 case，原始 `fail/notrun`、退出码和部分 case 结果保留在 `rawStatus`/原 JUnit 中，不伪造“从未执行”。满足支持环境后应回到原轨道完整重测，不调整 ACL、安全标签、宿主或日志根绕过问题。

## 执行与证据契约

`Test-M8_5.ps1 -Mode Tests` 先完整核对 `ctest -N` 与 JSON 库存，再由明确清单生成精确名字过滤，只执行 `extension-new`。仍逐入口跑完、保留原超时；缺依赖/未执行/跳过都不算通过。输出独立 `attribution.json`，绑定库存、JUnit、分类策略哈希，逐项记录分组和原始状态。`result.json` 显式 `conclusionScope=extension-new`。

历史库存和结果由同一归因器另存报告，不覆盖原文件。新增/未知测试必须先更新清单才能进入结论；报告始终 `accepted=false/wholeEngineAccepted=false/installationAuthorized=false`。此专项结论不替代全引擎、正式 EXE、人工关闭安装重启验收；现有安装回执验证器不放宽。

## 本轮测试

分组静态合同 **5/5**：158 精确覆盖、159 显式增项、未知拒绝、缺失拒绝、重复拒绝。报表额外检查 **3/3**：历史扩展失败与环境尝试记录保留、旧 158 不能满足当前库存、仅有库存绝不计通过。PowerShell 5.1/7 均验证；首次 PS5 调用策略拒绝与随后发现的 UTF-8 无 BOM 中文解析失败日志保留。改用 ASCII 源码中的 Unicode 码点生成中文状态，沿原 CTest 的进程级脚本调用方式复测成功，无系统策略变更。

| 本轮范围 | Debug | Release |
| --- | --- | --- |
| `ctest -N` / JSON 库存 | 159 / 159 | 159 / 159 |
| `extension-new` | **8/8，通过**，17.84 秒 | **8/8，通过**，17.53 秒 |
| `m84-legacy` | 141，引用 F6/F7 | 141，引用 F6/F7 |
| `environment-blocked` | 10，未执行（环境） | 10，未执行（环境） |

执行前从捕获副本 `4ba07be8e09e4b18850d4f580c49ff7b` 构建扩展合同、CodeGen 集成及扩展/AI+扩展 Editor/Player 的完整依赖图，双配置共 12 次构建均 0 警告/0 错误。不是全部 69 工程或原生构建验收。捕获输入指纹 `29FF0C5B26CCA5E9BBDA57306F5AE762D4CE354C0E2040C912991DDDCA2983C6`。构建后只修正归因器的 PS5 编码和当前库存检查，编译输入均未变；报告另绑定归因器 SHA256 `48AB8D5772A20EC3A5C2FE14F29452231292861EA152146E0ECBCA6C12EC874B` 及清单 SHA256，不冒称包括归因器后改在内的单次全仓冻结。

本轮结果：

- Debug：`out/verification/m8-5/final/Debug/b82e53b82da44efea96143bfdbf9293a`。
- Release：`out/verification/m8-5/final/Release/eb70b9c32e674b3bbf299a554783ae5e`。
- 两处均保留 `ctest-N.log`、完整 `inventory.json`、8 项原始 `junit.xml`、逐项 `attribution.json`、编译输入身份及正式文件哈希。
- `out/verification/m8-5/d-attribution-2026-10-28` 保留第一轮 158 项历史独立归因报告、编译日志、归因脚本失败/复测记录及 `source-reporting-boundary.json`。历史源码对应的 6/7 不能重标为本轮 8/8。

**D 自动测试结论仅为 extension-new 通过**。legacy 的历史失败未消失，环境组不是通过；真实独立 EXE、完整发布/安装和人工确认继续保持独立未验收状态。本轮未部署正式 EXE、未提交推送、未调用真实模型或读取用户密钥。

## 用户要求的再次测试（2026-10-28）

复用上文受检源码捕获副本，先核对来源文件（仍只有已登记归因器差异），并按原两份编译回执逐文件核对四种可选测试产品；测试后再次核对，集合和字节均未变化。扩展合同与 CodeGen 工程从该副本进行双配置增量编译，四次均 0 警告/0 错误；可选 Editor/Player 使用已核验的上一轮产物，没有冒称重新执行完整 12 次构建。

- Debug：**8/8**，0 失败、0 未执行，18.22 秒；证据 `out/verification/m8-5/final/Debug/ddb21727d659471aa2254addb9660c0d`。
- Release：**8/8**，0 失败、0 未执行，17.60 秒；证据 `out/verification/m8-5/final/Release/7bde09a2220e44ad9a7f60c09a853723`。
- 两配置库存仍各 159；legacy 141 继续引用 F6/F7，环境 10 本轮未执行，不计通过分母。
- 执行/编译日志在 `out/verification/m8-5/d-rerun-2026-10-28`；首次辅助脚本字符串插值解析错误未进入测试，修正前源码保留为 `Run.parse-failed.ps1`，未修改产品源码或测试断言。

本轮是独立复测，旧报告不覆盖。正式 EXE 不变，未部署、提交或推送，也不新增安装许可或全引擎验收结论。

## 源码交付与 EXE 更新前置检查（2026-10-28）

用户随后要求更新 EXE、提交推送。源码交付沿用上述 D 专项通过结论，不改判全引擎或安装验收。提交范围为产品、测试、构建脚本和文档；本机 AI 连接配置、密钥、IDE 个人状态和 `out` 构建/证据产物均排除。

为先检查此前阻塞部署的独立启动环境，本次只对历史 AI 候选 `a602f79ea7bd4a45a5471e5a9a0155e1/editor-ai` 执行 `NcmaEngine.exe --validate-package`，**不是最新 M8.5 构建验收**。该进程在 30 秒后仍未退出，正常关闭及额外 5 秒等待也未结束；烟测没有执行。本次只能报告“预检超时”，不能沿用历史访问拒绝作为这次已证实的原因，也不能算验证通过。

未调用正式部署、未生成或安装新的候选，不以 `dotnet` 代跑 EXE，不调整 ACL/完整性标签/日志目录，不降低扩展安装回执要求。预检后重新核对正式安装的 154 个文件，集合、字节和部署日志均未变。正式 EXE SHA256 仍为 `29A9F4A74B0061234715D96D27613F26C199013710C64713B68CFD8E0B6C23D0`。

本地证据：`out/verification/m8-5/delivery-2026-10-28/{Preflight.ps1,baseline.json,validate.process.json,checkpoint.json}`。失败辅助进程的清理需单独精确核对和授权；源码提交推送不代表 EXE 已更新。
