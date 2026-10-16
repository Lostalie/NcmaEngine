# M8.2 可选本机工具 worker 交付记录

日期：2026-10-16。状态：M8.2 自动候选验收完成；最终同源码完整 Debug SECOND / Release FIRST、部署和附加核验全部通过。人工、目标机、自包含、模型推理、完整性能和长稳门禁不因此关闭。

## 交付范围

- 纯 BCL `Ncma.Modules` 保持传输中立；adapter 获得实际排队/派发后剩余预算，移除旧二参数签名，不保留兼容入口。
- 独立可选 `Ncma.Modules.Grpc`：gRPC over HTTP/2 + TLS，本机字面 loopback，严格 UTF-8 JSON 自定义消息而非 protobuf，真实 Python `ncma.tools.text_stats` 确定性工具。
- 可信本地进程 owner：精确安装库存/上下文/60秒审批，固定解释器/参数、受检 pin，异步启动与停止，实际进程身份验证、有限显式重启、未收敛资源保留和单独批准恢复。
- 版本/nonce/实例/PID/schema/请求身份验证，认证/证书拒绝测试，严格消息与任务预算、取消后实际任务保留、重放/迟到/坏回包拒绝。
- 独立库发布、七个运行程序集闭包、五个官方 NuGet 原始签名归档 SHA512 锁、运行 DLL 与归档比对、许可声明随包；严格搬移加载及实际独立 worker 测试。
- 默认 Editor/Player 无 Modules/gRPC/Python 依赖；独立可选库不进入默认部署。

本阶段没有模型推理、正式 AI 面板/领域提案接线、远程服务、pythonnet、ZeroMQ 或任意代码加载器。可信进程执行不是 OS 沙箱；测试生成的 Python 安装不是正式分发安装器或许可授权证明。

实际契约见 [M8.2 runtime contract](M8_2_RUNTIME_CONTRACT.md)，后续工作顺序为 **M8.2 收尾 → ECS E0–E5 → M8.3 及后续 → M9**。

## 冻结与完整回归

冻结记录：`out/verification/m8-2/source-freeze-second.json`，906份实现/测试/构建源文件。范围含 managed、原生源码/配置、scripts、python、tests、CMakeLists.txt、Build.bat；文档/AGENTS 不在该库存，解决方案单独记录 SHA。核对命令：

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File out/verification/m8-2/freeze-source.ps1 -Verify -Generation second
Build.bat -Configuration Debug
Build.bat -Configuration Release
```

| 门禁 | Debug SECOND | Release FIRST |
| --- | --- | --- |
| 原生 CTest | 13/13 通过 | 13/13 通过 |
| 托管/图形/产品 CTest | 76/76 通过 | 76/76 通过 |
| Python unittest + inspect | 45/45 + inspect 通过 | 45/45 + inspect 通过 |
| ABI/smoke/严格格式/MCP | 完整流程通过 | 完整流程通过 |
| 运行时测量 | 3轮完成 | 3轮完成 |
| 部署前后审计 | 两次 audit_passed=true，h8_accepted=false | 两次 audit_passed=true，h8_accepted=false |
| 受检部署 | Complete a8f3ad5e75fb4bd49a0dcc0535d09bc3 | Complete cd023930bb454a6fbee9b1cd70b1b7a0 |

日志：`out/m8-2-full-debug-second.log`、`out/m8-2-full-release-first.log`。原生耗时9.22/8.32秒，托管产品1147.95/998.20秒，Python 1.612/1.379秒；两配置编译零警告。Release 执行会话31765正常终止 exit0，无并行/重复构建。

Debug/Release 五类候选包独立校验已通过，每配置清单数量为144/56/113/67/116（Editor、Null、DX11、Null+Physics、DX11+Physics）；记录 `out/m8-2-final-five-package-audit-first.log`。最终附加验证 `out/m8-2-final-delivery-verification-first.log` 同时核对全部源码、完整日志与实际包/备份，真实 apphost `--validate-package` exit0；没有用包完整性代替测试。

## 最终包与部署身份

| 项目 | Debug | Release |
| --- | --- | --- |
| 五类运行包 generation | 10e1a76c476244aeabf7fb150572ce51 | f827714b75e742cbb6a5fe5a0de3bb87 |
| 可选客户端 generation | 7bb60645094841b8b0c07ab54ccdb600 | 32919c7a60264bf7a8bdc03308de75c5 |
| 可选客户端清单文件数 | 21（另有manifest） | 21（另有manifest） |
| 完整日志 SHA256 | D8247EF8C6DEF3CA10486BCDD3732F7F9DC65AFF942CE7C17F90C0171CC2DBD5 | B8521C9D31EAF3E40271CEF4F841976EF3DC4CE66A163DD017CF03AED020E06A |

正式 `out/bin` 为最终 Release：144清单文件/148实际文件，148文件的恢复备份对应已验证最终 Debug；逐项大小/SHA一致，Debug Complete归档保留。EXE SHA256：`C80EDA73C7BA9F97E71B372CD14CF6A3F44A02FDBA16561F593B0D5527217E4E`。原生/图形/用户数据不进行无关清理。

906源码冻结记录 SHA256：`159B23B845E0C7B68F79673FB74583DCD1C63CE084371889AE6AAE1353EC6C34`；解决方案 SHA256：`726EB65B74372932E064D2756F5E72EE2D899D6EA5971CDB309D88F4E3160FC2`。可选客户端 manifest SHA256：Debug `DDC407A6AE7042B91C1941D0429E1B5D9DE8F13A74B43B6C142F5C7B0A635C97`，Release `30AEB5A30A866490D91D834136F4B629BAC1669C6C038106977B4AD25B4B32F1`。

## 新增测试与成本证据

- 原模块会话：17组8370检查，验证身份、复制、预算、取消、并发槽与实际停止。
- 实际 TLS 工具 transport：4583检查，含严格 wire、坏回包、实际取消/deadline后未退出工作、进程审批与退出/崩溃/断连/恢复。
- 严格客户端搬移：1174检查；七 DLL 全部从精确 SHA/路径的复制包加载，发布源与已知 Python 原路径共26文件排他锁定。真实复制 worker 192次请求，含64KiB输入，确认退出及资源释放。坏包拒绝是 SDK 验证夹具的职责，不冒充正式 Editor 插件加载功能。
- 默认产品独立性：最终完整两配置各614检查（先前定向候选各613）；完整回归前序测试生成的允许范围内日志文件也被逐项检查，数量差异不代表放宽产品清单。最小环境/PATH、锁定已知解释器、搬移 Editor 实际 apphost 校验和8帧 smoke、Null Player 恰好2tick/0原生模块。不是卸载全机 Python、干净目标机或自包含验收。

定向日志：`out/m8-2-grpc-final-source-{debug,release}-first.log`、`out/m8-2-relocated-client-debug-second.log`、`out/m8-2-relocated-client-release-first.log`。完整门禁已再次执行所有登记的新旧测试，实际计数保存在两配置 `Testing/Temporary/LastTest.log`。

已记录的 Release 本机搬移样本在 `out/verification/m8-2/relocated-client/ac899763eb494c2c803a75039020ebb3`：启动约1388ms，worker working set约36.6MB；27/32011/65536字节输入各64次，含测试轮询的往返 p95约16.04/16.14/15.96ms。不是纯RPC延迟、TLS线上字节、全线程分配、推理成本或完整帧性能验收。完整回归生成的更新样本独立保留，不覆盖旧样本。

最终完整回归的搬移证据：Debug `dd60c97156a945c19ab71d4653a26eeb`、Release `9a5153ca3b9d4fa59e678d3b16e4ab4c`（同一父目录）。Release启动1515.13ms、working set36,012,032B；三个输入档 p95为16.08/16.17/16.01ms，p99为24.77/17.83/18.58ms；每64次owner线程含测试分配501224/4508480/8798216B。上述证据边界仍适用，不比较不同机器或宣称达到完整性能门禁。

## 失败证据保留

- 完整 Debug FIRST：13个原生测试通过后，打包脚本在 VS 初始化环境中找不到 Get-FileHash。改为 BCL SHA256 流式实现，未取消任何哈希校验；重新冻结后运行 Debug SECOND/Release FIRST。失败日志 `out/m8-2-full-debug-first.log` 保留。
- 打包 first/third/fourth/fifth：ordered字典 size 测量、签名 contentHash 与整包哈希语义、PowerShell JSON数组包装、ZipArchive程序集加载错误；修复后通过，旧目录/日志不删除。
- owner/negative/TLS 历史测试失败及修复记录保留在 [M8实施记录](M8_IMPLEMENTATION_PLAN.md)，没有通过扩大权限、关闭证书验证或忽略断言掩盖错误。

## 未关闭门禁

人工 UI/MCP、真实模型/凭据/网络外发、用户素材、干净目标机、自包含分发、完整性能、1h长稳，以及历史 File.Replace/空缓存等调查继续开放。自动回归与部署成功不能将它们标记通过；本阶段不关闭整个 M8 或引擎开发目标。
