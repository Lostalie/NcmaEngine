# M8.7 D 故障与生命周期准备

## 最新 D2（2026-11-12）：组合覆盖准备完成，未执行

实际 `CandidatePresentation` 中，原两项 CPU GUI/DX11 宿主用例增加 `NCMA_EXTENSIONS` 断言，并登记到原组合扩展入口；不是新建模拟宿主。新内部只读属性仅在 AI＋扩展启用时观察原 `_extensions`，要求 owner 线程，不注入 factory、不变更关闭权限或执行顺序。

检查同一实际扩展组：启动 Running/outer=false；外线程关闭被拒且组/授权不变；宿主明确关闭时扩展先 Closed、撤权并从宿主脱离，而不合作 HTTP 仍保留，原底层所有者继续持有。重复观察必须 inner=0、无需扩展重试、outer=true；原 presenter 读取显示“扩展持有 0”和“外层持有：是”，无重试/历史/场景字节修改。传输完成后显式关闭，所有底层引用释放、transport 恰好一次、outer=false；关闭 Editor 后不再 Build presenter。最后清理同属 case 成败。

| 原入口 | 变体 | 本轮准备的证明边界 |
| --- | --- | --- |
| `NcmaAiEditorTests.HostLifetime` | AI-only | D06；不得替代 D11 |
| `NcmaExtensionTests.Editor.extensions` | extensions-only | D08；没有 AI/native 组合关闭证明 |
| `NcmaExtensionTests.Editor.ai-extensions` | AI＋extensions | D08＋D11；原入口额外执行两个加强后的宿主用例 |

三个入口均保持原 60 秒和 RUN_SERIAL；缺原生/Gameplay/font 依赖则明确失败，不跳过。F 判定同时关联 capture/config/variant/entry/case/产物哈希、原日志和 JUnit；`M87-FAULT` 只是中间诊断，必须看到原 CaseHarness 的 case PASS/END/整体 SUMMARY，包含 finally，才可判定。CPU 与 DX11 分别记录；未知超时、启动拒绝不批量标成环境错误。每项环境归因须有直接证据并保留原失败。

D11 证明目标是**健康扩展先关闭＋AI 未排空保留**，未向实际宿主注入扩展 Dispose 故障；后者更窄的服务/原 lifetime 故障覆盖仍由 D07/D08 提供，不能混称整机组合故障已验证。独立 apphost、人工 C3-08、真实模型和 SDK 子进程门禁仍独立待验。

新捕获 `c7c3ac605a78490aa537df02a09c68bd`（5042 文件），source SHA `98E86FD611D9ECDF59410FCD0768F74E3865903CE6B8CA6FCB14C239EB761A91`、manifest SHA `F21BB63489DF9A5A6CE19BA59429F19F713D6A0FB0BF54F6A350C6C57CFA2C42`；只改两份测试源码和一份内部只读观察源码。原六图离线准备后，仅组合 Services.Tests＋实际 Editor/MCP/Gameplay 依赖图双配置编译，Debug 83.14 秒、Release 83.02 秒，0 警告/错误。不是其他变体/全部工程/native/package 或功能验证。

最终记录 `out/verification/m8-7/d-combined-first/final.json` SHA `40BC29CF68C3163D582881380A18E97CD5B304E9D8B1CC92C1C71834190B8136`：11 组锚点及原清单每配置 3 个变体核对通过，当前产品确含 AI 和扩展且无测试/Grpc 运行依赖泄漏。旧 405 目录、5170 其他独立文件、前五片 10 个私有目录、正式 151 文件/journal 未变；已知 Git 工具变化仍单列，不称旧整个冻结未变。核验报告准备的两次解析失败均保留，修正 Audit2 后成功，不属于产品或功能测试失败。

**D1/D2 准备完成不等于 D 验收；测试、worker、模型、PS5.1 执行均为 0，无部署/提交推送。** 下一步 E 有界测量/长稳准备，再匹配新全套输入与冻结后 F，主体终态汇总后 PS5.1。固定输入仍 b69/共享 Debug，不把私有新 DLL 混入旧产品。下面 D1 数据为历史记录。

2026-11-12。当前完成 D1 来源映射和迟到响应补充用例，**未执行功能测试**。映射文件 [M8_7_D_FAULT_MATRIX.json](M8_7_D_FAULT_MATRIX.json) 只是验收计划，不新增执行器、审批通道、CTest 入口或超时。

## 本次补充

原 AIContracts 已有独立的取消、重配置、超时和 mailbox 过期检查。本片新增两项联合用例：原请求忽略取消并返回合法旧提案后，明确排空/注销；随后在同一领域服务上建立新工作流，要求重新审阅批准，只允许新请求提交一次。

- 边界分别为 `cancel`、`configuration-revoke`。
- 未排空前重复关闭检查不能提前销毁 transport，模块仍登记且无回执、无历史、无文件变化。
- 旧请求返回后必须完全排空，只释放一次；旧提案不可成为可提交审阅或产生原事务回执。
- 新实例不得接收旧外发审阅；新请求独立批准、回包与 proposal/source 对齐，唯一回执对应新 request，历史增长一次，准确 Undo/Redo。
- 再次泵旧实例、重放旧授权或重复提交，不得改变已提交结果。整个 case 结束后两份 transport 各销毁一次、模块清空。

沿原 `DomainWorkflowTests.All()` 和 `NcmaModuleTests.AIContracts` 登记，原 60 秒入口超时及 fixture 3 秒受控等待不变。HTTP 被确定性替代，没有真实模型、费用、进程或网络调用；不是实际 GUI/apphost 或组合扩展关闭验收。`M87-FAULT` 只输出请求/source/原回执和前后哈希，整个 harness case（含最终清理）才是通过与否的依据。

## 既有检查如何复用

映射列出 10 类锚点：HTTP 失败、超时/取消、busy、mailbox、迟到替换、实际宿主、扩展关闭、外层持有、构建进程树、导入 worker。每类声明证据层级，不能混为同一种“真实运行”：

- 受控 HTTP 和短超时检查证明原协议/所有权合同，不是公网可用性、真实模型质量或生产长时超时测量。
- `AiHostLifetimeTests` 使用实际 CandidatePresentation/native 所有者，但诊断传输被替代且在测试进程内运行；不关闭独立 EXE 启动债务。
- ExtensionEditor 外层持有检查使用原 presenter/lifetime 与故障依赖，不证明实际 AI+扩展一起关闭的全链组合。
- WorkflowBuildJobTests 的进程树观测是 synthetic lease，不声称实际 SDK 子进程终止；真实 SDK 仍在原 PS5.1 后置组。
- Import 原例会运行受控子进程，其内置 watchdog 只管理自己的 worker；这不授权代理强杀任意遗留进程或修改系统安全策略。

## F 执行与失败记录约束

执行前须绑定新捕获、原依赖/工具链/变体和原清单，不能把新 DLL 塞进旧冻结直接跑。每个入口保留原命令、TIMEOUT、RUN_SERIAL 和依赖。全部安全可运行 case 按原 harness 汇总；外层退出不证明内部全部 case 已到达。统计区分 passed、failed、not-run、environment-blocked，分母保留，缺依赖不跳过后宣布通过。

故障证据记录输入身份、达到的阶段、首次失败码/异常类型、请求或精确进程身份、持有状态、最终退出/排空及原日志/JUnit哈希；不采集密钥、原始模型响应或不必要路径。保留第一次失败，新修复使用新捕获/新记录；不靠增加超时、重试取绿、切换宿主或放宽断言。

取消不回滚已提交事务，也不等于资源已经释放。未排空则保留所属对象与依赖；关闭失败需要明确人工恢复，不自动重试或恢复新帧。真实进程无法常规停止时，先保留身份/状态并请求精确处理授权。

## 下一步

本片新捕获 `e696ac95647f4518841619a936e4e7e6`，5042 文件，source SHA `8B4781BD8A0AE0BD7755A0FEA8D007EAA5BA4F9309C94BE3BAE34E8FD4240D0D`，manifest SHA `9F10D0011A4A7F5F5158ECB76C21CD38005DD711FD59D3186AE21A89BC696B27`。相对 ff9 只改 1 个 AI.Tests 文件并新增 1 个 partial 文件；原六图离线依赖准备后仅编译 verify-contracts 完整依赖图，Debug 65.19 秒/Release 65.31 秒，均 0 警告/错误。没有编译全部工程/Editor 可选图/原生/worker 或执行功能测试。

最终记录 `out/verification/m8-7/d-late-reply-first/final.json`，SHA `0C67F1E7B1AB2A6122A2EF158C1331F16D905D0CA672743213AEDF6C67286C8F`。10 组锚点和原 180 清单中的入口名称静态核对通过，非新 CTest 枚举或运行结果。旧 405 冻结目录、5170 其他独立文件、前四片 8 个私有编译目录、正式 151 文件/journal 未变；已知签名 Git 工具差异继续单独保留，不称旧整体冻结未变。固定输入仍 b69/共享 Debug，新产物仅私有目录。

继续 D2：核对组合生命周期覆盖、将原清单/变体与失败归因规则固定，区分可由现有入口证明和仍需实际客户端/人工操作的项；随后 E 测量准备。所有功能运行集中在 F，F 主体终态与报告后执行原 24 项/配置 PS5.1，不以主体先全绿为前提。D1 准备不等于 D/C3/M8.7 验收。
