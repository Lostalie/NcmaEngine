# M1 自动交付与最终验收记录

日期：2026-10-04。工作区：F:/NcmaEngine。
**结论：M1.3、M1.4 的代码及本次自动验收通过；第三方 MCP 客户端与人工 UI 联合验收未完成，不能据此宣布整个 M1 结束。**
没有进入 M2。验收基线 HEAD 为 159cb482901e1117bd0fa92a2abdfff97c876dee；本报告随 M1 实现一起提交，提交标识见 Git 历史。提交/推送不表示人工验收已完成。
已有 .vs 与 NcmaEngine.vcxproj.user 本机修改保留，不作为交付源码处理。

## 1. 实际交付

M1.1/M1.2 已交付基础保持：C# 唯一 World/完整文档/命令/Undo/Redo/草稿/文件指纹/Play 隔离，唯一 .ncmascene JSON v1。
本次继续完成：

- M1.3-C：GLFW 复制输入/事件边沿、焦点/ImGui 过滤、零步暂存/成功步消费；Transform previous/current 只读插值。
- M1.3-D：每步运行命令、预留 UUID、差量候选、Scene 绑定/实例调度/信号/input/receipt/render 联合准备安装，存活引用不整体失效。
- M1.3-E：信号消费回滚/epoch、只读 tombstone 清理、新绑定下一步激活、隔离程序集/catalog 预检、不可逆激活故障与卸载引用检查。
- M1.3-F：ImGui 控制、Tick/alpha/丢时/故障/Restart、Host/SDK 样例与固定布局桥接。
- M1.4-A/B：当前用户私有 named pipe、人工配对、活动 EditSession owner-thread 队列、独立 stdio helper、10 项能力/具体 schema/完整数据分页。
- M1.4-C：可信 UI 批准精确提案，60 秒/每连接一份、全目标/组件/绑定范围；删除 UUID 输入确认；共享有范围 Undo/Redo；局部绑定/Export 命令；缓存及安装前重检权限。
- M1.4-D：关联请求、多客户端公平、有界 reader/writer、pending 合并、所有权、缓存淘汰 fail-closed、取消/过期/断线、Play/文档/catalog 竞争。
- M1.4-E 自动接入：ImGui 连接/完整提案/风险/授权/撤权/审计；配置示例与恢复文档。第三方客户端人工部分尚未完成。
- M1.4-F 自动接入：协议对抗输入/输出 schema、真实编辑器读写及 UI 命令 Undo/MCP Redo、完整回归与负载记录。人工/完整生产负载边界见下文。

生产默认不自动配对/批准，不加载场景副本作 MCP live 权威；只有显式 --mcp-smoke-test 隐藏测试模式自动批准测试提案。
动画 MCP 仍是隔离 Python/native 预览；资产/动画图/UI/源码扩展 Agent 工具未注册空实现。

## 2. 实际版本与边界

| 契约 | 版本 / 大小 |
| --- | --- |
| Native aggregate plugin / Animation / Character | 2 / 1 / 1 |
| Gameplay Host | 5；PlayStatus 120B；InputFrame 240B；RenderHeader 48B；RenderObject 56B |
| Scene Host | 6；caller-owned 输入/输出，最大 4 MiB |
| Editor capability / local IPC / MCP | 2 / 1 / 2025-11-25 |
| 唯一场景文件 / manifest | SceneDocument JSON 1 / 10 |
| SDK managed Signal | 88B；含会话 epoch，不是新增 native C ABI |

C# 游戏逻辑，Python 工具/可选模块，C++ 当前 ImGui 外壳及性能内核。没有恢复 C++ SceneWorld、Godot Node、Python gameplay、旧 .ncscene 或旧 SceneSnapshotCodec。
现有 C# SDK SceneWorld 是托管访问门面，不是删除掉的 C++ World。编辑 Undo 仍全量恢复并使旧引用失效；运行命令不是全量恢复。

## 3. 构建与自动测试证据

环境：Windows NT 10.0.19045 x64；12 个逻辑处理器；MSVC 19.44.35228、VS2022；CMake 3.31；
dotnet SDK 9.0.315，net8.0 测试运行时 .NET 8.0.28。
规范命令都通过，未使用 SkipTests/SkipManaged/SkipPython：

~~~bat
Build.bat -Configuration Debug
Build.bat -Configuration Release
~~~

另外执行了 Build.bat -Configuration Release -CleanNative 验证干净原生重建；
最后按最新源码重新执行了上面两个完整配置。

| 验证 | Debug | Release |
| --- | --- | --- |
| Core / Native / Architecture 优先构建 | 通过 | 通过 |
| CTest 全集（3 native + 11 managed/editor） | 14/14 | 14/14 |
| Runtime | 13/13 | 13/13 |
| SceneDocument | 25/25 | 25/25 |
| Editor.Core | 36/36 | 36/36 |
| Gameplay | 53/53 | 53/53 |
| Collectible catalog / shared SDK identity | 12 次释放 | 12 次释放 |
| Transport：授权/并发/协议/stdio/关闭 | 通过 | 通过 |
| Native Host、Editor、Gameplay、FBX、live MCP smoke | 通过 | 通过 |
| Managed/native ABI smoke | 通过 | 通过 |
| Python inspect + 工具/FBX/动画 MCP 回归 | 24/24 | 24/24 |
| 新代码最终编译诊断 | 0 警告 / 0 错误 | 0 警告 / 0 错误 |

日志：out/verification/m1/final-debug.log、final-release.log。
按用户要求提交前再次完整运行 Debug/Release，结果一致：CTest 各 14/14、Gameplay 各 53/53、Python 各 24/24，零编译警告/错误。
本次复验日志：out/verification/m1/precommit-debug.log、precommit-release.log；构建与测试产物仍留在忽略目录，不纳入源码提交。
CTest 明细：各 out/build/windows-ninja-<config>/Testing/Temporary/LastTest.log。
阶段证据：g2-debug.log、g3-debug.log、g4-debug.log、g5-debug.log。
补充连续运行：Release Host 10/10（host-repeat-release.log）；Transport 重复结果见 transport-repeat-release.log，不用一次 smoke 代替稳定性。

本次清理：仅删除一个已核对身份、无活动管道的早期失败 smoke 实例描述 JSON（157B、忽略目录 out/sessions）；没有删除源码、客户端配置或用户数据。

本次失败/修复记录：

- 新增测试误用了不存在的 SceneDocument.Capture、值结构成员直接修改；已修复编译错误。
- 新 manifest 断言的局部变量缺失；已修复并在完整双配置中通过。
- 一次 Debug Host 场景原子保存失败，原 assert 隐藏错误并触发超时；改为输出具体错误的异常。
  后续多个完整构建及连续 Host 10 次均未复现，无法归因于某个确定的文件系统原因，不声称已证明根因。
- 中文-only MSVC 在 VSLANG=1033 时回退中文资源；CMake 曾错误解码 /showIncludes 前缀，导致 Ninja 丢失头文件依赖。
  已修正 env 的尾空格与资源 fallback 前缀，并干净重建 Release；生成规则现为“注意: 包含文件:”正确前缀。
- Debug/Release 原先共享链接路径，时间戳可能错误复用另一个配置的 exe。现在内部链接产物隔离，每次规范构建明确部署目标配置。

对外程序仍是 out/bin/NcmaEngine.exe；最终部署为 Release。
其 SHA256 与内部 Release exe 一致：
89191E9A9B7294D24659E514D3BF71A4F4D7D7D9519209944B9A74623AC25863。
内部 bin/symbols 在各 out/build/ 配置目录，不新增对用户的启动路径。

## 4. 性能与容量记录

Gameplay fixture：一个 Transform 对象、一个 Probe Behaviour、反复 Rename 同一 UUID，0/1/8 步 × 0/64/1024 命令。
8 次预热、32 样本；测完整 AdvanceFrame 与当前线程托管分配，不是 GPU FPS、生产动作游戏或大场景 ECS 测试。
0 步并不执行命令（即使 fixture 配置为 64/1024）。最后独立顺序运行 Debug/Release，日志：
final-profile-debug.log / final-profile-release.log。

| Release 步数 / 每成功步命令 | median ms | p95 ms | max ms | median 分配 B/帧 |
| --- | --- | --- | --- | --- |
| 0 / 0 | 0.0004 | 0.0020 | 0.0071 | 328 |
| 0 / 64 | 0.0004 | 0.0006 | 0.0035 | 328 |
| 0 / 1024 | 0.0003 | 0.0004 | 0.0021 | 328 |
| 1 / 0 | 0.2236 | 0.2667 | 0.2757 | 34280 |
| 1 / 64 | 0.4566 | 0.9809 | 1.3149 | 268912 |
| 1 / 1024 | 0.8708 | 2.3020 | 4.1920 | 660720 |
| 8 / 0 | 2.0469 | 2.1524 | 2.1590 | 271944 |
| 8 / 64 | 4.6032 | 5.9770 | 6.3695 | 2149000 |
| 8 / 1024 | 12.5132 | 14.8350 | 15.4146 | 5283464 |

### 多对象 / 组件 / 绑定压力

单个固定步：每个 Behaviour 写 Transform，一个 System 只读遍历；一次批量恢复建立场景，
8 次预热、32 样本；仅可信测试开启内部 POD 分层计时，未注册 MCP 诊断工具。
日志：runtime-pressure-debug.log / runtime-pressure-release.log。
下表 B 为当前线程托管分配，ms 为完整 AdvanceFrame，不包含 GPU。

| 配置：对象 / 每对象组件 / 每对象绑定 | Release median / p95 / max ms | Debug median / p95 / max ms | Release median B/帧 |
| --- | --- | --- | --- |
| 128 / 9 / 4 | 36.4108 / 52.8490 / 57.0704 | 29.4219 / 32.7290 / 33.6330 | 8217368 |
| 32 / 9 / 64 | 16.7577 / 25.3707 / 28.3876 | 18.0135 / 25.9198 / 26.7922 | 11804928 |
| 1024 / 1 / 1 | 27.8893 / 37.7063 / 45.3352 | 32.5166 / 47.5468 / 49.8842 | 22506016 |
| 4096 / 1 / 1 | 138.0619 / 161.1718 / 177.1317 | 156.0802 / 184.9144 / 186.8851 | 90806504 |

4096 对象的序列化场景 1407957B，渲染读视图 4096 项。Release 分层 median：
setup 57.0712ms（包含快照）、Behaviour 10.0014ms、System 0.3043ms、
World/signals prepare 19.5681ms、metadata prepare 42.3694ms、instances prepare 2.2497ms、
receipts prepare 0.1303ms、render prepare 4.8745ms、install 0.0211ms。
各层独立统计 median 不可直接相加当作整帧 median；setup 包含生命周期/快照等，不是纯快照计时。
其余分层 median/p95/max 在日志 JSON 中。

此实现正确性优先，**4096 对象单步约 138–156ms / 91MB 分配，尚不适合生产动作游戏的大场景固定步预算**。
瓶颈是全量快照/元数据准备，不能据此宣称高性能 ECS；进入大场景交付前需增量元数据/只读快照复用、
候选与序列化分配优化，再用相同 fixture 验证失败原子性与性能。
记录真实限制，不在 M1 暗中加入未验证的高性能存储替换。
桌面/JIT/GC 噪声使部分 Debug 样本快于 Release，不作为编译配置优劣的结论。

### IPC 延迟 / 分配

独立 Release transport-load-release.log：32 个单连接样本，8 次预热；fixture 是一个空对象的小文档。

| 场景 | median / p95 / max 端到端 ms | median / max owner+harness 分配 B |
| --- | --- | --- |
| 单连接只读 | 30.9866 / 31.9815 / 32.0298 | 30048 / 30584 |
| 单连接批准写入 | 31.0040 / 31.8943 / 31.9066 | 77920 / 81176 |
| 四连接 64 个排队只读 | 138.6268 / 248.4911 / 250.3616 | 85088 / 85624（每 pump） |

四连接本次 16 个 pump：median 1.6420ms / p95 2.0969ms / max 2.1206ms；
第 65 项得到 queue_full。无连接 pump 32 样本 median 0.0001ms / p95 0.0009ms / max 0.0010ms。
批准写入的计时排除人工审批等待；64 请求端到端包含串行有界排队。
测试通过 Thread.Sleep(1) 轮询 pump，Windows 常见约 15.6ms 调度粒度显著影响端到端数值；
**这是受控管道测试，不是真实 ImGui 帧循环/第三方客户端延迟承诺**。
分配包含 owner/test harness，不包含所有后台线程；2ms 是软预算，单请求无法被抢占。

### 有限资源回收基线

自动门禁验证：64 次 Start/Advance/Stop/Dispose 的 PlaySession 与 Probe 弱引用全部释放；
collectible catalog 12 次释放、Native Host 12 次活动重载；
IPC 8 次预热后 32 次连接/配对/断开/Dispose，32 端点弱引用全部释放且各自实例描述删除。
独立 Release 记录初始句柄 268、峰值 295、最终 268，断言允许预热运行时 delta ≤16。
最终 Debug/Release CTest 也执行这些回收测试，具体运行时句柄基线见 LastTest.log。
这些是有限回收/容量测试，不是数小时生产稳定性、恶意脚本安全沙箱或零泄漏证明。

预算：4 连接、总队列64/每连接16；每帧最多4/软2ms；排队5s、审批60s；
IPC/stdio1MiB、深度32；业务64KiB、输出256KiB；运行命令1024/1MiB；
信号4096、滚动回执1024；Core cache128、归属256+64KiB保守墓碑；
历史64/16MiB、场景4MiB/4096对象；审计4×约1MiB、面板最近32条。
完整预算/schema/错误恢复语义见 [EDITOR_MCP.md](EDITOR_MCP.md)。

## 5. 原子性与资源限制

承诺覆盖引擎候选组件/结构/绑定/信号/输入消费/回执；不回滚用户脚本私有字段、外部 IO、构造器副作用。
可信 factory/validator 应纯净；尚未调用 OnCreate 的候选不会执行 OnDestroy，资源应在明确生命周期中创建。
预检失败保留旧暂停实例；不可逆清理后的重载激活失败 Faulted，不能假装复活旧实例。

只读 MCP 读取 committed Edit 文档；没有操作 Play World 的后门。
缓存/墓碑是有界 fail-closed，outcome_unknown 不等于“未提交”，不能自动换 ID 重放。
Audit IO 在 owner pump 内有实际成本，软预算不是硬实时保证。
不宣称完整 JSON Schema validator、安全脚本沙箱或无限期 exactly-once。

## 6. 未完成验收与 M2 门禁

以下尚无人工证据，保持未完成：

- 第三方 MCP 客户端名称、版本、脱敏配置及初始化/读取/批准写入/Undo/Redo/撤权/断线闭环。
- 真正人工 ImGui 焦点/键鼠捕获、失焦/暂停/Step、完整风险提案可读性与窗口关闭验收。

大场景/多组件/多绑定/分层计时/IPC 延迟与有限资源回收自动基线已补齐；长期生产压力、真实动作游戏性能优化仍是后续交付门禁，而不是声称已通过的人工证据。

操作清单与只读启动模板：docs/EDITOR_MCP.md、engine/config/editor-mcp.example.json。
用户确认人工结果并补记录后再关闭 M1；未确认前不进入 M2。
Vulkan 实际渲染、完整资产/FBX GPU 角色/Animator/UI制作与HUD、C# 主入口、独立 Renderer/Physics 发布、
Python 推理通信、独立网络服务仍属于后续阶段，全部保持未实现或已有预览基础状态。
