# M8.2：可选本机 gRPC worker 契约

2026-10-25 当前协议已由 [M8.3-A 模块编辑来源 v2](M8_3_MODULE_CONTEXT_V2.md) 替代：移除旧 World/tick，C#/Python 全链路严格 v2，无 v1 兼容路由。本页下方协议 v1 与验收描述为 M8.2 历史基线；不能作为当前 v2 功能通过证据。所有权、安全和预算约束未放宽，v2 当前仅编译/语法检查。

状态：2026-10-16 自动候选验收完成；最终同源码无 Skip Debug/Release、包/备份核验、审计与受检部署通过，见 [交付记录](M8_2_DELIVERY_REPORT.md)。确定性工具已实现；模型推理、正式 AI 面板和领域提案提交未实现。

## 所有权与部署

- `Ncma.Modules` 保持纯 BCL；`Ncma.Modules.Grpc` 是显式引用的可选 C# 库，不是默认 Editor/Player 依赖，不拥有 World 或事务权限。
- 一个 `LocalWorkerHost` 由一个 owner thread 持有。可信本地代码显式提供安装库存、精确上下文与权限检查；不得将 Approve API 注册成 Agent 能力。返回 proposal 不代表可以修改领域数据。
- `WorkerInstallation.Inspect` 是帧外操作，固定 Windows x64 CPython 3.12 布局。最多4096文件/4096目录/256MiB，拒绝链接/硬链接/越界路径；审批绑定路径、大小、SHA、上下文和60秒 review。启动复核并持有内容句柄直至实际关闭。
- 不使用系统 PATH 搜索解释器、不自动安装依赖。固定 `-I -S -B`，独立 `python312._pth` 仅包含 zip标准库、DLLs与site-packages；无site自动加载。固定 worker 只有 `ncma.tools.text_stats` 操作，没有 eval/shell/用户代码路径。
- 开发验收的 `PrepareOwnedBundle` 仅复制生成测试安装，不能当成已授权正式分发的 Python/第三方二进制安装器、干净目标机或 self-contained 验收。
- `Build.bat` 通过 `Package-ModuleClient.ps1` 发布真正的库及七个运行程序集到新 generation；单独索引 `out/verification/m8-2/<configuration>/client-package.json`，不修改默认产品清单。失败 generation 保留，索引仅在整包校验后原子发布并保留旧索引。
- 依赖由 `packages.lock.json` 锁版本/内容；`package-archives.lock.json` 另锁2026-10-16从官方 NuGet 源复核的完整签名压缩包 SHA512。签名包的 NuGet contentHash 与整个 zip 的哈希含义不同，不能直接互换。打包复核五个完整压缩包并将 `.deps.json` 选择的 DLL 与 zip 内内容逐项比较，保留包自带许可声明/通知；这不是法律授权结论。[NuGet 官方读取实现](https://github.com/NuGet/NuGet.Client/blob/dev/src/NuGet.Core/NuGet.Packaging/PackageArchiveReader.cs)

## 通信

- grpc-dotnet2.84.0、grpcio1.84.0、typing_extensions4.16.0；只有 gRPC over HTTP/2，消息是严格 UTF-8 JSON 字节，不是 protobuf/ProtoJSON。
- 只监听字面 `127.0.0.1` 随机端口；一次性 CA/服务端证书与固定叶证书SHA，验证链、用途与主机名。无代理/重定向/重试；32字节 Bearer 认证、私有 stdin 引导，不将密钥放在命令行或普通日志。
- 握手绑定协议v1、本次实例、nonce、实际持有子进程PID、操作/版本/schema/预算。调用绑定 request/session/epoch/context/fingerprint；严格拒绝未知/重复/缺失字段、坏UTF8、深度、UUID、数字、base64、重放及超界消息。
- 传输消息上限131072字节；操作输入/输出各65536字节，metadata2048字节，最多4实际活跃工作；每会话终身4096请求ID，epoch不能重置重放库存。
- `InvokeAsync` 接受 M8.1 排队/后台派发后剩余预算，编码耗时继续扣除，再设置 RPC deadline 与本地取消；Python 要求有限deadline并在接纳、计算前后检查。
- 客户端RPC取消不等于Python计算完成。Python持有独立实际任务集合，实际结束才释放槽；不能通过连续超时无限增加后台任务。可信进程执行不是OS/CPU/内存沙箱。

## 生命周期与故障

- `ReviewStart` / `ApproveStart` 在帧外显式执行；启动最长10秒，Pump不等待IO/RPC。直接持有唯一子进程句柄，ready PID与OS映像必须相符，不按名称/报告PID终止其他进程。
- stdout只允许一条有界ready；此后持续计数消费stdout/stderr，不保留原始可能含秘密的输出。bootstrap写入与ready读取并发，避免双管道互等。超界输出、进程死亡、连接失效、权限撤销和上下文变化会撤销会话/结果。
- 最多三次显式启动，每次新实例/凭据/session，无自动重启或请求重放。历史记录有界，只保存固定故障码与统计。
- Stop先撤销接纳、收敛实际调用/回调，再关闭连接和stdin，确认进程/输出终止后释放句柄。不能关闭时保持 `FailedRetained`；拒绝 Dispose/替代启动。重试关闭或终止需要单独的本地精确实例/PID/操作/60秒审批，只操作原持有Process。
- 断连只在实际进程/输出/RPC观察后报告，不声称实现持续后台健康探测。pythonnet、ZeroMQ、远程推理未实现。

## 自动验证与证据边界

- `NcmaModuleTests`：原有17组8370契约检查。
- `NcmaModuleTests.Grpc`：实际 TLS 跨进程4583项；包括认证/握手/协议拒绝、坏回包、取消但任务未退出、预算、明确重启、安装及进程故障、独立审批恢复。
- `NcmaModuleTests.RelocatedClient`：1174项（SDK驱动内部1154），真正库publish复制到中文目录；发布源和已知原Python路径共26文件排他锁定。隔离ALC只从逐SHA受检包加载全部七程序集，只共享实际.NET框架；真实worker运行192个请求，包含最大64KiB输入，随后确认退出/释放。
- 缺失/篡改七个DLL、清单改变、额外文件拒绝均在SDK验证器中发生；该严格加载器仅是产品包验证夹具，不是新增任意程序集加载服务或默认Editor的插件安装功能。SDK驱动本身不进入产品清单。
- `NcmaModuleTests.NoPythonProducts`：搬移默认 Editor/Null Player，最小环境、锁定已知解释器、实际apphost运行和恰好2tick；不等于卸载机器上所有Python或目标机验收。
- 完整 `Build.bat` 默认登记并运行上述测试；`-SkipPython`跳过实际worker/客户端搬移测试，`Skip`构建按旧规则禁止正式部署。开发机需显式准备锁定venv，普通项目启动不会下载/安装任何模块。

最新定向 Debug/Release：库1174、worker4583全部通过，构建零警告；见 `out/m8-2-relocated-client-debug-second.log`、`out/m8-2-relocated-client-release-first.log`、`out/m8-2-grpc-final-source-{debug,release}-first.log`。Release样本目录 `out/verification/m8-2/relocated-client/ac899763eb494c2c803a75039020ebb3` 保存实际加载路径/SHA与成本。

Release本机样本：显式启动约1388ms、worker working set约36.6MB；27/32011/65536字节输入各64次，含1ms轮询（Windows调度可能更长）的往返p95约16.04/16.14/15.96ms。owner线程含测试校验分配约0.50/4.51/8.80MB/64次。记录的是应用payload而非HTTP2/TLS线上总字节，分配不含后台线程/worker，不代表推理成本、帧影响、并发吞吐或完整性能验收。

保留失败证据：打包first为PowerShell ordered字典不能由Measure-Object读取size；third为错误混用签名contentHash与完整zip哈希；fourth为Windows PowerShell JSON数组多套一层；fifth为未显式加载ZipArchive程序集。均修复实现而非删除验证/覆盖旧证据，sixth及Releasefirst通过。

最终完整回归与部署核验：906冻结源码不变，两配置各13原生/76托管产品/45Python、原smoke/格式/MCP/inspect、3轮测量与部署前后审计全部通过，编译零警告。最终Release Complete `cd023930bb454a6fbee9b1cd70b1b7a0`，148实际/148备份文件核验、真实EXE包校验exit0。完整门禁的新测试计数为核心8370、worker4583、搬移1174、无Python614；较早定向无Python613仍保留。日志和SHA见交付记录。原人工、真实模型/外发审批、目标环境、自包含、1h、完整性能与历史File.Replace调查仍开放。

完整Debug首轮已保留为失败：13个原生测试通过，但新打包脚本在VS初始化后的PowerShell环境找不到Get-FileHash，尚未进入托管回归/部署。改为原产品打包采用的BCL SHA256流式实现，不放松任何哈希校验；使用新的源码冻结记录重跑。
