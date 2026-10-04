# M2.7：Player/Headless、发布布局与默认入口切换

更新日期：2026-10-04。状态：A/B 及图形 Player、框架依赖候选包/构建接线已实施；自包含/人工验收/正式切换未完成。
实际交付与测试见 [M2.7 交付记录](M2_7_DELIVERY_REPORT.md)，不是完整 H7 已通过。
候选 Player/发布包可以先开发和测试；正式切换依赖 H1–H6 完成，且 M1/新 Editor 人工验收记录已补齐。
本阶段才允许把 out/bin/NcmaEngine.exe 从旧 C++ EXE 切到托管 apphost，但不能把 H7 候选测试通过当成前置门禁已通过。

## 0. 当前基线与实施前提

| 项目 | 实际状态 | 对 M2.7 的影响 |
| --- | --- | --- |
| M2.1/M2.2 | 应用服务、插件加载器自动门禁通过 | 复用生命周期、owner-thread 和 ABI，不另造宿主 |
| M2.3/M2.4 | 候选 Platform/Gui/DX11 自动验证通过，人工验收待完成 | 可做候选 Player；不能宣称 Vulkan 或全环境输入验证完成 |
| M2.5 | 候选业务/预览/偏好/Console 已迁移；H5 人工未过 | 默认切换前必须补齐预览、剩余面板及 UI/MCP 人工验收 |
| M2.6 | 薄 C++ Physics + C# 高层服务实现，H6 自动门禁通过 | 可选独立服务；不自动创建刚体，不进入 Play 固定步 |
| 默认入口 | out/bin/NcmaEngine.exe 仍是旧 C++ 外壳 | 候选开发继续使用 verification 目录 |
| M2.7/M2.8 | M2.7 候选实施；M2.8 总验收待后续实施 | 候选能力单独标记，H7/生产入口不冒充完成 |

候选实现可推进，不豁免 H3/H4/H5 和 M1 的人工验收。先业务迁移，全部 M2 门禁结束后按 H8 清单一次性清旧入口。

## 1. 目标与应用组合

新增 Ncma.Player.App，复用 Application、Scripting、Gameplay、Scene、Rendering/Physics 客户端。
Player 不依赖 Editor.Core/Services/Transport/Mcp/Gui，不生成活动 EditorEndpoint、不拥有用户配对/授权 UI。
Headless 是同一运行服务的无窗口模式，默认不加载 GLFW/Gui/D3D/Physics；
项目显式要求某个 native kernel 时才加载对应依赖，不把“没有窗口”称为“完全不依赖任何 native”。

Player 当前终点仅 C# 场景/脚本/固定步 + 已有参考渲染，不称作完整动作游戏导出器。
完整资产 cook、GPU FBX/Animator/HUD/网络属于 M3–M9。

## 2. 分步实施

### M2.7-A：共享运行服务与启动场景

加载已确认 .ncmaproject，读唯一 .ncmascene JSON v1，注册可信组件/type schema，再校验并启动。
Player 直接拥有运行 SceneDocument，不创建一个无用的 EditSession 或隐藏编辑副本；
Editor 的 Play 仍必须克隆并隔离。两者使用同一 PlaySession/Behaviour factory。
明确缺少组件注册/绑定类型/DLL 的失败策略，默认拒绝而非丢组件/跳脚本。
本阶段提供可信样例 bootstrap/schema 注册，完整第三方动态扩展系统留 M8。

当前 EditorSessionOwner 同时引用 Editor.Core/Transport，并容纳 activateEditor=false 分支。
Player 不得通过“关闭编辑 UI”来复用这个类，否则依赖仍被带入发布包。
建议增加 Ncma.Application.Runtime 共享库，引用 Application/Scene/Gameplay/Scripting，不引用 Editor/Gui/Platform/Renderer/Physics。
其中 RuntimeSessionOwner 负责文档、脚本 catalog/lease 和 Play 生命周期；EditorSessionOwner 保留编辑文档、克隆与授权，并组合共享运行服务。
不将 EditSession、BehaviourCatalog 的 UI 描述或 Endpoint 下移。原先借用 catalog 的过渡桥保持明确所有权，暂不删除。

启动顺序：

1. 完整解析参数，确认项目与输出目录；此时不创建窗口、不加载 native DLL。
2. 严格加载 .ncmaproject，确认 JSON v1、UUID、路径、文件大小和无 reparse 越界。
3. 由可信 C# bootstrap 建立 ComponentRegistry/schema；样例与 Editor/Player 使用同一注册定义。
4. 读取 .ncmascene，恢复到新运行文档，验证全部组件与 Behaviour/Export；不丢弃未知项。
5. 加载可信 gameplay 程序集候选，预检后提交 catalog、取得 lease；暂不执行 Play lifecycle。
6. 按模式初始化所需 presentation/可选 Physics，然后 Start Play；所有启动失败逆序释放已取得资源，不暴露半运行状态。

这里的程序集加载不是安全沙箱。反射可能执行构造函数/Export getter，信任必须在加载前确认，不能声称预检能回滚脚本 IO。
不开放从 Agent 输入加载 DLL/注册任意类型；完整扩展授权留 M8。Player 本阶段不提供在线热重载入口。
共享服务重构必须保留 Editor 的克隆隔离、reload 失败暂停/故障语义和 lease 释放顺序。

交付：共享运行服务、可信样例 bootstrap、Ncma.Player.App 和运行服务测试；候选路径启动，不改正式入口。
门禁：Player 依赖图无 Editor/Mcp/Gui，缺 schema/脚本/Export 明确失败，Editor 原有启动/停止/重载测试全部通过。

### M2.7-B：CLI、纯固定步 Headless 与故障报告

参数采用下表的完整名称与显式值形式，不依赖 substring 或隐含后端猜测。
参数由完整 parser 处理，禁止 substring误匹配；非法/冲突参数不启动部分服务。
headless 默认严格时间策略，一次推进明确固定 h，不以机器 wall time/JIT速度改变目标 tick。
ticks、运行时长、输入报告大小设置硬上限；Ctrl+C/关闭只发 cancel意图到 owner。
report 为忽略输出里的结构化结果：project/session/world/tick/state/fault/versions/测量，不携带私有脚本字段/凭证。
默认不从 stdin 接收任意 eval、组件方法调用或未授权实时 MCP；测试输入回放仅可信 fixture。

Editor GUI 与 Player/headless 的 stdout/stderr行为分开：测试报告不被窗口日志污染。

参数约定（拟实施，不是当前已支持命令）：

| 参数 | 约定 |
| --- | --- |
| `--project <path>` | 必填 .ncmaproject；进入加载前规范化 |
| `--headless` | 不创建窗口/GUI；无 renderer override 时采用 Null，不改写项目配置 |
| `--renderer <name>` | d3d11/null/vulkan；Headless 显式 d3d11/vulkan 属冲突；vulkan 尚未实现，明确退出 |
| `--ticks <N>` | Headless 必填，建议 1–1,000,000；图形模式可作为自动结束条件 |
| `--fixed-delta <h>` | 默认 1/60，建议范围 1/240–1/10 秒；只接受有限值 |
| `--max-runtime-seconds <N>` | Headless 默认 120，建议 1–3600；是运行预算，不参与模拟 dt |
| `--report <path>` | 有界 JSON v1；开发验证默认 out/verification，独立包默认用户日志目录，最大 1 MiB；不覆盖场景/脚本/配置 |
| `--help / --version` | 无需项目，不启动服务 |

禁止未知参数、重复参数、缺失值、NaN/Infinity、溢出和互相矛盾的模式。
路径统一相对调用者 cwd 解析一次；随后业务仅用已确认绝对路径。日志/report 不跟着只读项目写入。
显式外部报告路径只由本地可信 CLI 提供，不能成为 MCP 任意文件写工具；默认 CreateNew，已有文件不静默覆盖。

当前 AdvanceFrame 即使 Strict 也会调用 OnUpdate；Step 仅在 Paused 可用，不能靠反复 Pause/Resume 拼 Headless。
应在 PlaySession 增加直接固定步推进契约（拟名 AdvanceFixedStep），仍使用原来的唯一 WorldRunner：

- 仅 Running 可调用；固定推进一个 h，提交同一个输入/组件/结构命令/信号事务，不执行 OnUpdate。
- 不累计墙钟债务、不丢步；共享原来的 Faulted/attemptTick、成功步保留和失败步中止语义。
- 不新增另一套生命周期、不绕过 RunStep、不让应用直接 Begin/Commit World。
- 纯固定步与帧模式不能在一个活动会话混用；测试明确 frameCount、steps、alpha 与 accumulator 的含义。
- 输入以 tick 编号注入可信 fixture；不以呈现帧次数消耗边沿，不接受 stdin eval。

模拟结果只承诺在同一版本/注册/输入及确定性测试脚本下可重现；私人字段、随机数、时钟、外部 IO 不会被自动变成确定性。
报告包含 schemaVersion、project/session/world UUID、模式、h、目标/实际 tick、state、fault、exitCode、取消原因、模块版本、受控统计及状态摘要。
比较逻辑状态时排除临时 UUID/时间戳/耗时；需覆盖 Transform/注册组件/绑定、信号及结构命令，不能只比较 tick 数。
运行退出前先复制状态/故障证据，再关闭；Stop 会清理状态，不能 Stop 后才捕获故障。

退出码拟定：0 正常完成；2 参数/配置；3 依赖/ABI/架构；4 初始化；5 运行故障；6 关闭失败；7 后端未实现；8 超预算；9 报告写入失败；130 用户取消。
多重故障保留 primaryFault 与 shutdownErrors/reportError；运行/初始化故障优先，正常运行才用关闭/报告失败码，不用成功掩盖清理错误。
CLR 启动前的 apphost 错误不保证使用上述业务退出码，另记录宿主实际错误与退出码。

Ctrl+C/窗口关闭只设置取消意图，owner 在安全边界执行 Stop。
预算检查不能抢占卡死的可信 C# callback；自动测试由独立父进程提供 watchdog，必要时结束测试进程，不在活跃 native 调用中强制卸载 DLL。

门禁：精确 N 步、零丢步、无 OnUpdate；参数拒绝不产生窗口/DLL 副作用；故障、取消、报告失败与重复关闭均有证据。

### M2.7-C：图形 Player 与可选 native 服务

Player 图形模式使用 Platform + Renderer，不初始化 ImGui，也不依赖 Gui DLL。
C# 主线程拥有输入、帧调度、退出和渲染策略；C++ 只执行既有参考渲染/API 调用。
复用 M1 输入语义、PlaySession、RenderFrameView/插值和 M2.4 RenderPipelineService；只呈现当前已验证的参考模型。
图形模式采用 Interactive 时间策略，报告掉步；Headless 采用 B 的纯固定步策略。
30/60/144 呈现速率比较使用相同 tick 输入、相同累计模拟时长且不触发预算丢步；卡顿另测 dropped time，不要求丢步后仍同结果。

Null 可以在无窗口运行；图形 Player + Null 如不实现诊断窗口，明确拒绝该组合，不能悄悄启动 DX11。
Vulkan 始终返回未实现，不用 DX11 fallback 冒充。DX11 仍须实际渲染参考场景、读回与 API 验证。
初始化图形依赖失败时不开始执行游戏 lifecycle callback；可信程序集预检构造函数的 IO 例外见 A。

PhysicsEnabled=false 不加载 Physics。true 仅建立现有独立 PhysicsService，保持可 inspect/close；
不从 Play/WorldRunner 调 Step，不添加 Rigidbody/PhysicsSystem，不把它称为场景物理完成。
Headless 若显式启用物理，只加载 Physics 及其原生依赖，不加载 GLFW/Renderer/Gui。
Python/gRPC/pythonnet 均不作为 Player 启动前置依赖；运行时 AI/网络接口另阶段设计。

门禁：无 Gui DLL 的 Player 包仍正常参考渲染；无全部 native DLL 的 Null/physics-off 包正常固定步；physics-on 缺 DLL 明确失败并安全清理。

### M2.7-D：apphost、发布模式与精确部署清单

Editor 项目采用 AssemblyName=NcmaEngine、OutputType=WinExe；Player 采用 AssemblyName=NcmaPlayer、OutputType=Exe，
同一个 NcmaPlayer.exe 提供图形/Headless，本阶段接受图形模式的控制台，不为隐藏控制台加自定义 native CLR 宿主。
WinExe 的日志/错误采用结构化日志及明确启动失败提示，不依赖 Console 可见性；重定向行为专门测试。
不能靠重命名 DLL 得到 exe，也不能只复制 exe 漏掉 deps/runtimeconfig/托管依赖。
复用原 NcmaEngine.ico 到 ApplicationIcon，并验证 Explorer/窗口两种图标来源。

建议布局：

- out/bin/NcmaEngine.exe、dll、deps.json、runtimeconfig.json及托管依赖：Editor 正式开发入口。
- out/bin/plugins/：Platform/Gui/Renderer/Physics 与原生依赖，精确部署清单。
- out/player/：独立 NcmaPlayer apphost/托管依赖/plugins 与样例项目/场景/脚本。
- out/managed/editor-mcp/：保留独立 MCP helper 的既有选择方式。
- out/verification/m2/candidate/：迁移期间候选产物；不作为新永久启动路径。
- out/package/：发布暂存、依赖/许可证清单；全部忽略。

应用根来自 AppContext.BaseDirectory，项目根来自 --project/启动选择，不能向上猜 CMakeLists 才能运行。
plugin registry 只看确认的目录/清单；所有插件 hash/版本/CPU架构与托管内容保持同一次配置。
工作目录改到任意位置仍能启动；空格/中文路径、Rider Run 配置均验证。

发布先生成完整 staging清单，再 promote；若活动 EXE/DLL被锁，停止报告，不强删/覆盖运行文件。
删除陈旧 out 内容只根据精确旧部署清单并验证路径，不递归删工作区/任意用户目录。
Debug/Release配置隔离和符号输出保持，不拿更新的另一配置文件当已构建当前配置。

建议同一次发布产生 Editor、Player-Null、Player-DX11 三个可选内容集；Physics 是显式可选内容，不用“把全部 DLL 拷进去”充当依赖分析。
记录 deployment manifest：schemaVersion、产品/配置/RID/TFM、构建修订、发布模式、文件相对路径/大小/SHA-256、模块 ID/ABI/能力、许可证。
拒绝重复/越界/reparse 路径、同文件多个所有者、Debug/Release 混配以及缺失依赖。
manifest 与应用自己的配置格式区分，不擅自更改 Python inspect 的生产能力状态。
SHA-256 用于一致性检查，不是签名/来源认证；受信任发布和项目程序集选择仍是独立边界。
审计 gameplay .deps.json/依赖、native DLL 依赖和 Shader/字体/图标来源；不得依赖源码相对路径或 ambient PATH 寻找引擎插件。
Windows 系统库/驱动/系统字体属于声明环境依赖；可移植字体如需随包提供，应确认许可证或提供已验证 fallback。
日志/GUI 状态使用独立可写用户目录；本阶段不改场景格式，不借发布迁移个人偏好文件。

### M2.7-E：自包含包与搬迁验证

先完成 framework-dependent 的可诊断开发部署，再测试 self-contained win-x64。
自包含需要主动维护打包 .NET 补丁；NativeAOT/裁剪、离线 SDK 更新和安装器另做方案，不当 M2 的入口必选项。
当前 net8.0 是代码迁移基线，不等于长期发布支持决策。
TFM/SDK 升级、下载 runtime pack 或工具安装均独立确认，不把版本升级混进默认入口迁移。
发布显式指定 RID、configuration、self-contained true/false、UseAppHost=true，保持 PublishTrimmed/PublishAot/PublishSingleFile=false。
由相同参数构建/发布，不能使用不同模式的 --no-build 产物。
动态程序集加载是当前需求，裁剪不应先开再通过保留整个程序集来掩盖未经验证的反射路径。
上述发布行为依据 [.NET 发布模型](https://learn.microsoft.com/en-us/dotnet/core/deploying/) 和 [裁剪不兼容项](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)。

验证先在 verification 中复制独立包，再更改 cwd/根目录名字，使用空格与中文路径；包内保留自己的样例项目与脚本。
候选测试不通过时不修改正式 out/bin。额外验证只读项目/只读程序目录加独立可写日志目录仍可运行。
framework-dependent 在缺少适用共享 runtime 的隔离环境验证宿主诊断；不卸载用户已安装 runtime 来制造条件。
self-contained 必须在声明的无预装 .NET 目标环境启动并跑 fixture；若没有该测试环境，就标此门禁待验，不能用开发机成功替代。
它包含 .NET runtime，但不承诺无需 OS/CRT/GPU 依赖；列出实际环境条件和维护的 runtime 版本。

普通 apphost 启动 CLR 可能仍使用 .NET 官方 hostfxr 组件；禁止的是引擎自己的 C++ hostfxr bootstrap/NativeEntry 桥依赖，
不是要求 self-contained 包没有 hostfxr.dll。参见 [.NET 宿主组件设计](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-components.md)。

### M2.7-F：Build.bat、solution 与 Rider 接线

保持 Build.bat 唯一规范入口，不要求用户先初始化 Ninja/VS 环境。
Build.ps1 的候选流水线先 Core/Native/Architecture，再独立插件、共享运行服务、Editor/Player/测试，最后精确 staging 与联合验证。
将发布校验和 smoke 放在 promote 前；SkipManaged/SkipTests 不可触发首次正式迁移，不可用旧程序集补出看似完整的包。
M2.7 新增测试纳入 CTest/Build.bat，不只独立执行 dotnet run。
Rider/solution 的仓库级构建、运行、调试配置指向规范入口；个人 .vs/.vcxproj.user 只检查不覆盖。
NcmaEngine.vcxproj 的 NMakeOutput 仍指正式 out/bin/NcmaEngine.exe；候选阶段不得把这个路径误指向旧包的新同名文件。
待 G 的门禁通过再修改正式启动路由，之后 Build.bat 不再把旧 CMake EXE 无条件覆盖到 out/bin。
旧 comparison target 保持配置隔离的验证路径，不能成为正式部署依赖，也不能通过新应用失败自动回退。

生命周期仍由同步 Main 的 owner thread 管理，不通过 async continuation 转移 World/native 资源所有权。
关闭先阻止输入/授权/新请求，再 Stop Play、释放脚本实例与 catalog lease；随后关闭 Physics、GUI（Editor 才有）、Renderer、窗口、模块，最后日志。
若服务关闭失败仍持有原生资源，保留其 lease 并报告失败，不因超时强制 FreeLibrary；遵守 M2.6 Closing 重试契约。

### M2.7-G：受控默认路径切换与恢复

先在候选目录逐项证明 Editor/Player 可运行，确认用户正在运行的实例不会受替换影响。
固定 out/bin 包含多个文件，不能把逐个 Copy-Item 宣称为文件系统级原子切换。
采用停机维护窗口、完整 staging、备份/部署 journal 和目录级更名，尽量缩小不完整窗口；不保证外部直启者看不到两次更名之间的空窗。
默认启动器先检查维护/journal 状态；直接运行 exe 的用户须先退出并遵守维护窗口。无需为了真正原子指针再引入新的 native 入口。
具体流程：

1. 校验 H1–H6/M1 人工证据、候选 H7 测试、完整包 manifest 与当前目标；未满足仅保留候选。
2. 在 out 下同卷建立独立 staging/backup/journal，核定确切目录、文件、类型、用户数据和锁状态；不递归操作未知目录/reparse。
3. 检查 EXE/DLL 占用；发现锁即停止，请用户正常退出，不强杀用户进程/强删文件。
4. 进入维护状态，将原正式内容可恢复地备份；只对已核定可整体更名的目录操作，否则先制定精确文件清单策略。
5. promote 完整 staged 包，校验 manifest、配置和正式路径 smoke；同步锁定 Build.bat/LaunchEditor.cmd/Rider 的唯一正式路由。
6. 成功记录新部署代次并退出维护；失败按 journal 恢复先前文件与路由，保留失败候选和证据，恢复失败明确阻止启动。

恢复是部署失败时恢复切换前已知安装，不是在新应用运行时静默 fallback，不增加旧格式或 SceneWorld 兼容代码。
备份留在 ignored 路径，保存到 H8 核定；任何未知用户数据或目录占用都阻止整体更名，不能靠强制操作解决。
旧 native外壳仅为受控对照 target，不允许同时使用正式同名路径。
H7 只切换正式启动/部署路径，不删除旧入口源码或对照目标。它们保留到 M2 全阶段结束，再按 H8 核定清单统一清理。
切换失败停留在候选，不重新添加 native SceneWorld/旧格式来“兼容救场”。

### M2.7-H：交付证据与 H8 接口

每个切片先跑专项测试；A/B 通过再 C，C/D/E 验证后 F/G。候选阶段的失败不得污染正式安装。
建议输出 out/verification/m2-7/{Debug,Release}/ 下的测试 JSON、stdout/stderr、包 manifest、渲染/部署证据与双配置日志。
实际实施记录在 docs/M2_7_DELIVERY_REPORT.md，逐项区分自动通过/人工待验/未实现，不把候选部分通过写成完整 H7。
仅候选通过时不写“生产主入口已实现”；正式切换并验证后才更新架构/路线图及 production manifest。
把仍服务旧入口的 bridge/测试/部署路径和替代消费者交给 H8 审计，不在 H7 删除源码。

## 3. H7 验收

1. 新 Editor apphost 从普通shell/Explorer/Rider启动，场景编辑/Play/MCP对等。
2. Player/headless载入可信场景/脚本，30/60/144呈现速率下相同固定步结果；严格ticks/输入/故障可查询。
3. Player依赖图没有 Editor/Mcp/Gui；禁用 Python/缺省 Physics 时可用。
4. framework-dependent缺运行时有明确诊断；self-contained在声明的目标环境实际测试，不凭 publish成功宣称无依赖。
5. 拷到 out/verification/m2 的独立包目录、改cwd、空格/中文路径仍启动，不能暗读源码文件/shader/DLL。
6. 错位数/缺 DLL/ABI错、readonly项目、重复关闭、Ctrl+C、未知backend均按约定退出，不无提示fallback。
7. out/bin主程序/图标/符号/配置真实正确；Debug/Release完整 Build.bat，零警告。
8. 新程序无自定义 C++ hostfxr bootstrap/NativeEntry桥接依赖；CLR由 .NET apphost正常启动，允许官方 runtime 自带 hostfxr。

验收拆成三个不可混淆的记录：

| 门禁 | 内容 | 允许动作 |
| --- | --- | --- |
| H7-Candidate | A–F 的 Player、Headless、发布/搬迁/故障自动测试 | 候选交付；不能覆盖 out/bin |
| H7-Ready | H1–H6 与 M1/Editor 人工证据、候选包验证完整 | 允许安排受控切换 |
| H7-Production | G 的正式路径切换、图标/普通 shell/Explorer/Rider/MCP 和双配置回归 | 可标正式入口已迁移；旧代码继续留给 H8 |

专项测试至少覆盖：

- 启动注册失败、未知组件/脚本/Export、重复参数、输入回放和固定步故障隔离；共享服务不破坏 Editor 隔离/reload。
- 120/600 tick fixture；30/60/144 帧时序等价；暂停/单步旧 API 回归；不会出现第二个 WorldRunner。
- Player dependency audit 无 Editor/Mcp/Gui/旧桥，Null/physics-off 无 native 加载；关闭安全性和 lease 释放。
- 缺 DLL、错误位数/ABI、未知 Vulkan、只读目录、中文/空格/改 cwd；明确失败且没有无提示 fallback。
- 32 次启动/停止资源循环、Ctrl+C、超预算、报告失败；结构化结果有界、不泄漏脚本私有字段或凭证。
- 精确 manifest 缺文件/hash 失败、混配、运行文件锁、切换中失败/journal 恢复；不覆盖个人数据。
- framework-dependent 和 self-contained 分别验证；缺测试环境标待验，禁止自动降级验收要求。

最终执行 Build.bat -Configuration Debug 与 Release，包括 CTest、Ncma.Managed 构建、managed/native smoke、Python inspect 与工具测试。
M2.6 的基线是每配置 28 项 CTest；增加测试后以实际数量为准，不预写通过数字。新代码警告必须修复，历史警告单独记录且不能隐去。
性能记录模拟/渲染/ABI/GC 各自耗时和分配；当前 boxing/snapshot 路径不是高性能 ECS，本阶段不承诺迁移后 FPS 提升。

完成才标 C# Editor/Player 主入口已实现；完整游戏打包/资产cook仍不宣称完成。
