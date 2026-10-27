# M8.4-F7 首轮修复与专项验证（2026-10-26）

## 2026-10-27：源码交付与 EXE 更新预检

用户要求提交推送并更新 EXE，且明确要求 **本机 AI 配置与密钥信息不提交**。
本次交付范围仅为产品、测试、构建脚本和文档；配置界面及安全处理源码包含在内，本机连接配置、凭据、IDE 个人状态及 `out` 产物不在 Git 提交范围。
待提交文件清单未包含配置数据文件，常见凭据格式扫描无命中（不宣称通用秘密检测）；未读取真实密钥、调用模型或修改安全策略。

- 当前选定源码指纹仍为 `111DF3E09AA51CE99A1CA5424901A925EA23F87D10D4E4C67EA5CFE6485C20C3`，产品源码未变。
- 通过原 `Package-M2_7.ps1 -IncludeAI` 新建 Release 候选，耗时 119.47 秒；不是整条 Build 的无部署捷径。
- 新 AI 候选：`out/package/m2-7/Release/a602f79ea7bd4a45a5471e5a9a0155e1/editor-ai`。
- 独立 EXE `--validate-package` **exit 0**；DX11 八帧启动预检 **exit 4**，明确报告 `editor_user_data_access_denied` / `UnauthorizedAccessException` / `0x80070005`。
- **未调用正式部署、未替换 `out/bin`**。完整正式安装、原默认版备份、部署日志及候选文件集合/字节均核对未变；没有钩子、权限调整、备用目录或放宽校验。
- 保留新候选与失败证据；源码交付不代表 F6 或 M8.4 验收通过。正常 EXE 验证环境和原子保存问题仍待解决。

证据：`out/verification/m8-4/publish-2026-10-27/{baseline.json,package.log,package-result.json,preflight-result.json}` 及两项进程/输出记录。

## 第四轮：启动错误可诊断，不绕过访问拒绝

选定源码指纹更新为 `111DF3E09AA51CE99A1CA5424901A925EA23F87D10D4E4C67EA5CFE6485C20C3`。
新增内部 `EditorUserDataStartupException`，仅包装实际 `ApplicationLog` 初始化的 `UnauthorizedAccessException` / `IOException`：

- 标准错误输出分别给出 `editor_user_data_access_denied` / `editor_user_data_io_failed`，保留原异常类型与 HRESULT。
- 不打印原始异常消息或路径；原异常仍作为内部 InnerException 留供本地调试，不宣称全局日志脱敏。
- 提示检查进程完整性与存储访问条件，但不自动认定所有拒绝都来自低完整性标签。
- 原退出码 4、ApplicationLifetime 失败状态/清理顺序保持不变。聚合错误中若首项为该启动错误，会另标记 `startup_cleanup_failed`，不把清理错误误报成首发故障。
- 无重试、备用目录、权限修改、新 CLI/MCP 能力或推理调用。路径选择前置校验及初始化以外的日志写入错误不在这次新增包装范围内。

这是**标准错误诊断改进，不是新增 GUI 弹窗或解决访问权限**。

完整 Services.Tests + 实际 Editor 依赖图四组编译全部 0 警告/0 错误：Debug 默认 83.38 秒、AI 80.40 秒；Release 默认 83.93 秒、AI 84.81 秒。
新增三个 case（分类/脱敏、真实文件阻挡导致的 IO 错误及无备用路径、原生命周期清理与聚合错误格式），原六个路径/CPU/DX11/文件集合断言保留。
四组各 **9/9，共 36/36 通过**，不是全 66 工程或完整 F6 验收。

当前 Release 默认 EXE 的两个独立负例（包内项目 CPU、DX11 烟测）**不加载诊断钩子**，均按原安全边界 exit 4，精确输出
`candidate_failed: editor_user_data_access_denied; exception=UnauthorizedAccessException; hresult=0x80070005; ...`。
两个副本完整程序文件集合和哈希均未变。其结果为“拒绝时诊断正确”，**不是启动成功**；未改用 dotnet 代跑独立 EXE。
诊断副本使用当前程序集、已钉住的历史资源/示例及显式 Release 插件，不冒充新的发布回执。

证据 `out/verification/m8-4/f7-startup-errors-fourth`：修改前七份归档、四组完整日志/binlog、`focused-result.json`、
`current-apphost/result.json` 和最终 `result.json` 审计。正式安装 150 个载荷、EXE/DLL/manifest 及两个旧失败场景保持原哈希，无遗留诊断进程。
未部署、提交或推送；未修改安全边界。**F6/M8.4 仍未通过**，独立 EXE 正常运行需先满足用户管理的受支持环境前提，原子保存另行待查。

## 第三轮：独立 EXE 拒绝写入已定位至继承的低完整性标签

本轮没有修改产品/测试源码，选定指纹仍为 `09101FB1F53C12C68ED3C30E749849F22E001B0EF32C83206F098B1C2394A8E1`。
两轮独立、相同输入的有界启动对照均为：`NcmaEngine.exe` exit 4，`dotnet NcmaEngine.dll` 完成 CPU 八帧并释放资源 exit 0。
每个副本运行前后完整程序文件集合和哈希未变。对照仅用于诊断，**不把 dotnet 当作正式入口替代或发布验收**。

只读证据形成以下链条：

| 检查 | 独立 EXE | dotnet 对照 |
| --- | --- | --- |
| 进入 Main 前的进程完整性 SID | `S-1-16-4096`（Low） | `S-1-16-12288`（High） |
| 当前用户、CLR | 相同用户，.NET 8.0.28 | 相同用户，.NET 8.0.28 |
| 既有 EditorRuns 父目录的添加子目录 AccessCheck | 调用成功，权限拒绝 | 调用成功，权限允许 |
| 文件标签 | EXE 继承 `Low Mandatory Level (NW)` | 系统 dotnet.exe 没有该低标签 |

`icacls` **只读**追溯确认：`F:\NcmaEngine` 显式配置可向文件/目录继承的低完整性标签；`F:\` 没有此标签。
构建产物、诊断副本及正式 `out/bin/NcmaEngine.exe` 均继承它。普通 `Temp/NcmaEngine/EditorRuns` 没有低标签。
Windows 会将进程完整性限制为用户与 EXE 标签中的较低者；未标记对象视为 Medium，Low 进程不能向其写入，即使用户是管理员组成员。
参见 [Microsoft：Mandatory Integrity Control](https://learn.microsoft.com/en-us/windows/win32/secauthz/mandatory-integrity-control)。
因此本次外部日志目录创建拒绝已具有明确的完整性边界解释，不能再仅归为“未知日志路径错误”。**谁最初设置了工作区标签尚未确认**。

相关 AppCompat Layers 查询无匹配项，`__COMPAT_LAYER` 为空；读取到已加载的火绒 sysdiag 驱动，但用户确认没有对应拦截日志，不能归因于火绒。
`TokenHasRestrictions` 保存的是原始非零 DWORD，不据其具体整数值建立结论；完整性结论来自结构化 SID、组 SID 和独立文件标签交叉核验。
诊断钩子只存在于证据目录，未加入产品依赖；没有凭据读取、真实模型请求、提权、重标记、ACL 修改、入口替换或安全软件调整。

原子保存仍独立未解决。只读检查发现失败场景父目录也继承工作区 `DELETE_CHILD` 等拒绝项，但**不能仅据此断言它就是历史 `File.Replace 0x80070497` 的原因**。
两个历史失败场景保持原内容，未再次调用替换、删除或非原子覆盖。

证据 `out/verification/m8-4/f7-access-third`：`result.json`、`inspection.json`、`labels.json`、
`token-comparison/result.json`、逐进程 stdout/stderr、两个钩子完整编译日志以及最终 `audit.json`。两个诊断项目编译 0 警告/错误，不是引擎重新编译或全 F6。
正式安装保持原哈希，未部署/提交/推送。此前 F6 失败不改判通过。

**后续边界**：不自行修改工作区的安全标签或 ACL，不通过高完整性宿主代跑来宣布 EXE 通过，也不把日志搬回程序目录规避检查。
独立 EXE 的正式验收需要先由用户管理的受支持构建/运行环境解决安全边界前提，再建立匹配输入并复验；安全前提未满足时保持未通过。
可继续进行不改变边界的错误提示改进与只读诊断。原子保存问题另行跟踪，E5-D 仍延期。

## 第二轮：包内项目路径修复，独立 EXE 日志拒绝仍未解决

当前选定源码指纹 `09101FB1F53C12C68ED3C30E749849F22E001B0EF32C83206F098B1C2394A8E1`。
`EditorUserDataPaths` 对项目 `out/user` 与程序目录重叠的情况，改用用户 LocalApplicationData 下的
`NcmaEngine/Projects/<规范化项目绝对路径的 SHA256>/Editor`。同一路径重复启动复用状态，不同安装位置隔离；
Windows 路径大小写、末尾分隔符和 `.` 规范化。普通外部项目仍用原 `out/user`，烟测仍用独立临时 GUID。
输出绝对路径、双向非重叠、既有祖先重解析点拒绝保持不变；无安装目录兜底、无新 CLI/环境变量覆盖、无密钥持久化。

新增两个 case，原非法根 case 补充包内项目无可用外部根的拒绝断言；原 CPU/DX11 八帧、资源释放及完整程序文件集合/哈希断言保留。
完整 Services.Tests + 实际 Editor 依赖图四组编译均 0 警告/0 错误：Debug 默认 82.88 秒、AI 80.35 秒；Release 默认 80.68 秒、AI 86.09 秒。
`--editor-user-data-tests` 四组各 **6/6，共 24/24 通过**。这是实际宿主在 `dotnet` 测试进程内的专项证据，**不是独立 EXE 或全解决方案/F6 通过**。

独立 EXE 诊断采用当前 Release 默认构建的完整程序集副本、逐文件核验的历史资源/示例及显式 Release 插件；没有伪造新的发布回执或宣称正式搬移验收。
包内项目 CPU 启动不再触发 `editor_user_data_overlaps_application`，但随后与 DX11 烟测一样，在 `ApplicationLog` 创建外部日志目录时抛出 `UnauthorizedAccessException (0x80070005)`，两次 exit 4。
两个副本运行前后程序文件集合/哈希均未变。诊断钩子仅记录路径环境、身份和异常类型/栈，不进入产品依赖，不读取密钥或调用推理服务。

实际子进程解析到预期用户目录/临时目录，属于当前用户且管理员角色为真。只读 DACL 检查与该时段 Defender 1121–1124 查询不能解释此次拒绝（查询未找到匹配记录），**不能据此排除其他访问限制或归因于 Defender**。
Release 原子保存 `0x80070497` 与此前 Debug 失败场景保持只读、原哈希；未修改保存语义、ACL、防护设置，未重试旧目标。

证据：`out/verification/m8-4/f7-storage-second`，包括 11 份修改前归档、完整编译日志/binlog、`focused-result.json`、
`context/result.json`（旧源码诊断）、`current-apphost/result.json`（当前源码诊断）和最终 `result.json` 审计。属于选定输入证据，不是全仓冻结。
正式 EXE `29A9F4A7...` / DLL `6E462301...` / manifest `E02CA04A...` 保持不变。无部署、Git 或真实模型操作。

下一步先确认独立 apphost 的文件访问拒绝来源，并继续只读调查原子保存，再准备当前源码的全套回执、候选和冻结执行 F6；**F6/M8.4 仍未验收通过**。不要复用旧包/旧回执或将这 24 项专项替代完整清单。

## 后续完整复验揭示未解决边界

`rerun2` 已重新准备同源输入并完成双配置：Debug 137/152、Release 136/152 通过，补充 case 各 15/16；完整冻结复核通过，但 **F6 仍未通过**。独立 apphost 隔离诊断发现包内项目被输出重叠检查拒绝，以及烟测创建外部日志目录时 `UnauthorizedAccessException`；具体权限来源未确认，未改权限/防护。Release PostHosts 再现 `0x80070497`，原子保存问题未解决。详见 [最新 F6 报告](M8_4_F6_TEST_REPORT_2026_10_26.md)。下方专项通过仅为历史局部结果，不构成完整输出策略已可交付的结论。

## 当前结论

已修复运行输出目录边界、路径异常契约及三个夹具问题，专项验证通过。**不等于 F6 全阶段验收通过**：还需要新的同源编译回执、候选包和冻结输入，再执行完整 Debug/Release 清单。原 [F6 报告](M8_4_F6_TEST_REPORT_2026_10_26.md) 的 143 通过 / 9 失败 / Release 152 未执行保持历史事实，不改判。

## 修复内容

| 问题 | 根因与改动 |
| --- | --- |
| 运行日志改写冻结输入、污染默认候选包 | 原无项目宿主将日志写到 `AppContext.BaseDirectory/out/user`。新增内部 `EditorUserDataPaths`：烟测/预览采用临时目录下每宿主独立 GUID；正常无项目编辑器使用用户 LocalApplicationData/NcmaEngine/Editor；有项目的正常编辑器保留项目 out/user。日志、偏好和布局统一使用解析结果。拒绝相对/空根目录、与应用目录重叠、既有输出祖先链接；没有退回安装目录的兜底。 |
| 指纹负例抛出平台异常 | 在调用 `Path.IsPathRooted` 前拒绝控制字符，使 PS5/.NET Framework 与 PS7/.NET 都返回约定的 `ai_package_source_path`；没有接受非法路径或放宽预期异常。 |
| 跨 PowerShell 文化计数失败 | PS5 将 JSON 数组作为单个管道对象输出。先取得反序列化值再展开数组；仍要求精确三项，并增加 en-US/tr-TR/zh-CN 顺序覆盖断言。原独立哈希 oracle 不变。 |
| 动画审批/取消流程内部超时 | 图夹具的假时钟冻结，而生产 `PumpModules` 保留 100ms 节流。夹具显式调用宿主轮询时按 Stopwatch 实际经过毫秒推进假时钟，不按每轮强加 100ms。生产调度、60 秒授权时限、原 3/5 秒等待上限均未修改；原资源审批、撤销、取消、Undo 和无自动提交断言保留。 |
| AI 工具栏旧文字断言 | 无控制器的默认产品应显示“未包含 AI 模块”，AI 产品应显示“模型服务未初始化”。夹具按实际编译变体检查精确文本，仍验证状态位于工具栏、不在正文重复；没有把断言替换为模糊匹配。 |

新路径选择不改变模型配置权限，不新增 CLI/MCP/环境变量覆盖入口，不读取或持久化密钥。临时日志不自动清除。旧包内日志、原失败场景和所有旧回执均保留，没有删除输出来让清单通过。

新增四个 case 已加入完整 Editor services harness，并提供 `--f7-regression-tests` 专项入口：纯路径选择、非法/重叠根拒绝、实际 CPU 8 帧与 DX11 8 帧宿主。宿主前后对其程序目录所有现有文件的集合及哈希进行比对，包括既有输出，不忽略日志文件；验证外部日志实际生成和双重正常关闭。

## 本轮验证

| 验证 | 结果 |
| --- | --- |
| Services.Tests + 实际 Editor 默认产品及依赖，Debug | 0 警告 / 0 错误，80.77 秒 |
| 同图 AI 产品，Debug | 0 警告 / 0 错误，79.01 秒 |
| 同图默认产品，Release | 0 警告 / 0 错误，78.91 秒 |
| 同图 AI 产品，Release | 0 警告 / 0 错误，82.85 秒 |
| Rendering.Tests + 当前依赖，Debug | 0 警告 / 0 错误，84.50 秒 |
| PS5 / PS7 分别运行指纹 15 case + 跨壳 2 case | 34/34 通过 |
| Debug 默认产品专项 | 17/17 通过（新路径 4、原界面/GPU 1、原动画工作流 12） |
| Debug AI 产品专项 | 26/26 通过（同上 17，加原 UI/元数据/动画 presenter 9） |
| 原 PostHosts harness，新的隔离场景 | 80 checks 通过，API 0/0，4.10 秒；原 120 秒总预算不变 |

以上编译不是全 66 工程构建；本轮未执行 Release 运行测试、完整 304 入口或重新发布候选。专项测试资源仅复制原冻结 AI 包中逐文件哈希匹配的未变资源，加载新编译托管程序；不把旧包重新标为当前源码产品。共享夹具输入核验仍为原 Debug 35 项。

## 原子保存失败仍需跟踪

原目标 `out/verification/m2/post-host-Debug/344ef86aa79b4e468c4e3ee209eeb912/scene-fixture/a799af20d2d748dab5f64c894e318c88/start.ncmascene` 仅读取属性和哈希：Archive 属性、2,590 B，SHA256 `886F9754BCDE375FEA7CAC4479EACCFE0E93CD65057A562EE792C3B53B0924CB`。没有重试替换该文件。

在新的随机输出目录运行原 PostHosts 一次后通过，但**未确认之前 0x80070497 的具体原因**。`SceneDocumentFiles.Save` 的实现未修改，没有自动重试、删除目标或降级非原子覆盖。该项仅记录“新夹具未复现”，不宣称根因已修复；完整复验若再次出现，应保留精确文件与进程状态再调查。

## 证据与边界

- 证据根：`out/verification/m8-4/f7-fixes-first`，含 13 份修改前原件、5 次完整编译日志/binlog、双壳测试日志及托管 case 完整汇总。
- 当前选定输入 SHA256：`49F2020FD874878A8FB5F068DDBE1E608EB9F834584FAAEAE55E36DFECF25120`。
- `focused-inputs.json`、`focused-result.json` 绑定本轮源码及专项结果；`post-host-result.json` 明确 `oldFailureResolved=false`。这不是完整 F5 冻结。
- 正式 EXE、托管入口、安装清单和原失败场景未修改；无真实模型调用、无凭据读取、无正式部署、无提交/推送，E5-D 继续延期。

下一步：重新准备当前源码的全解决方案双配置构建、回执、全部候选及测试输入；用修正过的 fixture 展开汇总建立新的冻结与执行记录。继续保持严格包文件集合检查，执行四个默认候选搬移/产品宿主入口以及其余完整清单。不要复用旧 F5 或把本轮专项通过替代全量验收。
