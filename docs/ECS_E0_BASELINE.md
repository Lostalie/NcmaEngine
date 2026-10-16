# ECS E0：合同、替换映射与基线

2026-10-16，E0 合同/自动基线门禁完成；Git 交付以本片提交及远端核验为准。生产基点 `51010c33755ab35ac5fa3f45bc297c074ea3addb`。
本片只新增测试/测量和文档，不实现 Entity/Sparse Set、不改变正式 EXE。未完成的测量不得标记为通过。

## 消费者和测试映射

| 现有机制/消费者 | 替换片与策略 | 保留的自动证据入口 |
| --- | --- | --- |
| Runtime.World / ObjectReference / Entry、UUID 索引 | E1：原 World 换代次身份及强类型池 | Ncma.Runtime.Tests：跨 World/线程、旧引用、原子恢复、值验证、固定步可见性 |
| Runtime.GameObject + Managed.SceneWorld/GameObject | E1/E2：迁移消费者、收敛单一门面，删除被替代包装类型；不能照搬 C++ SceneWorld | Build.bat 的 managed/native smoke、Ncma.Gameplay.Tests，Behaviour 与批量读写/信号 |
| Runtime.ComponentRegistry / ComponentAuthority | E1/E3：类型注册冷路径保留；增量验证和权威检查覆盖所有写入口 | Runtime.Tests；Gameplay.Tests/MovementCases（捕获错误仍毒化、权威/故障/恢复） |
| WorldRunner / PlaySession / RuntimeCommandBuffer | E2/E3：固定顺序、候选写集、待提交命令及回执 | Gameplay.Tests/Program、MovementCases、GameplayUiCases |
| SceneDocument / SceneDocumentCodec/Files | E3/E4：UUID 持久化不变，仅增量运行时元数据 | Ncma.Scene.Tests，严格新格式/旧格式拒绝 |
| Editor.Core.EditSession / CommandParticipants | E4：适配身份；保持唯一全快照 Undo 与权限 | Ncma.Editor.Core.Tests、Ncma.Editor.Transport.Tests、Ncma.Editor.Services.Tests |
| Gameplay.RenderBuffer | E3：消除每帧 JSON 解码并准备只读插值数据 | Gameplay.Tests；Player/角色联合提交和显示测试 |
| Scene.Rendering.RenderSceneExtractor | E2/E4：利用成员/数据版本；保持现有无 JSON 的缓存命中路径 | Ncma.Scene.Rendering.Tests；真实 SceneCostJoint* |
| Characters / Movement / Animation / Physics | E4：批量查询/权威句柄适配，内部骨骼不 ECS 化 | Ncma.Player.Tests/ActionJointChecks、Ncma.Animation.Tests、Ncma.Physics.Tests |
| UI / Presentation / Editor / Player | E4：对象绑定适配；UI 自有文档层级不改变 | Ncma.Ui.Tests、Presentation.Tests、RenderingTests.UiCostJoint/SceneUiJointProducts、PlayerTests |
| AI/MCP / 可选 Modules | E4：继续 UUID 快照/版本/权限提案，固定批准查询集合 | Editor.Transport.Tests、Modules.Tests、Grpc/RelocatedClient/NoPythonProducts |

以上为现有入口，不代表已经新增或通过 Entity 代次/槽位复用测试。E1 必须补这些尚不存在的行为测试。
不得删除任一旧测试只因为涉及旧存储；逐项迁移到新 API 并保持断言。旧类型残留审计至少覆盖 `ObjectReference`、双 GameObject 包装、`Dictionary<Type,object>`、全量 BeginStep/PrepareStep 和运行时文档构造；通用冷路径字典/编辑快照不盲删。

## CPU 基线入口

先通过 `Build.bat -Configuration Release -SkipTests` 构建测试程序集（Skip 不部署，也不算完整回归）。随后：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Measure-EcsBaseline.ps1 -Configuration Release -Runs 3
```

- 实际执行 `Ncma.Gameplay.Tests.dll --ecs-baseline`，先跑原 Gameplay/Movement/UI 回归，再测原生产 API。
- 固定 100/1000/4096 对象，全体 Transform，偶数对象 Marker；固定 UUID/初始坐标。
- 单类型读取、联合筛选；World 固定步无写/1% 写/全写；Play 固定步无写/1% 写/全写/1% 组件增删/1% 销毁重建，以及全部对象挂载真实 C# Behaviour 更新。各规模 11 项，共 33 项。
- 12 次预热、100 个原始样本；读查询每样本 16 遍，报告每遍成本（并非单次查询尾延迟）。nearest-rank P50/P95/P99。
- 所有用例检查最终坐标/成员/对象数/tick；未测模式作为原 Gameplay 测试默认执行小规模夹具检查。
- 线程分配不含报告排序/输出；GC 和驻留是进程观察值，包含 harness 影响，不能宣称工作负载净驻留。
- Play 使用现有分项计时器；World 单独测量不启用此计时器。两者不能直接把差异全部归因于存储。
- 记录原始样本、配置、程序集 MVID/SHA256、源码与当前 git 提交；独立生成目录，不覆盖失败。

## 领域基线（独立于合成 CPU 用例）

- 角色/动画/物理/蒙皮：`Ncma.Player.Tests/ActionJointChecks.cs` 的 0/1/8/32 角色真实联合路径；含模拟、姿态、palette、提交、上传、原生资源归零和故障循环。
- 纯 2D：`NcmaRenderingTests.UiCostJoint`，使用实际 UI 渲染成本及无多余 3D 资源约束。
- 3D/混合：`NcmaRenderingTests.SceneCostJoint{Off,Low,Medium,High}` 与 `SceneUiJointProducts`；CPU/GPU/上传各自归属，异步 GPU 尚无样本时不可填零。
- M8.2 完整双配置已通过这些既有产品测试，但性能数字必须保存具体证据/配置/hash 才可用于本重构比较；“测试通过”不自动等于性能基线完备。

## E0 放行清单

- [x] 固定身份、事务、权限、失败域合同；标明现状与新目标。
- [x] 现有消费者及不可移除的测试映射。
- [x] Debug/Release 新夹具及约定合同/领域定向回归通过（不是完整 76 项产品回归）。
- [x] Release 三轮相同源码完整 CPU 测量及噪声范围。
- [x] 创建/删除与真实 Behaviour 压力基线补齐，和实际领域成本证据冻结。
- [x] 结合测量确定 E1 试验的比较规则及“不确定”条件；页大小仍留给候选实现实测。
- [x] 记录最终证据；提交推送为本片最后操作，以 Git 交付记录核验。

本片提交推送后才开始 E1；M8.3 仍须等待 E1–E5。完整性能/人工/目标机验收继续待办。

## 准备阶段记录

- Debug canonical SkipTests 构建成功：`out/ecs-e0-build-debug-first.log`；随后补入销毁/重建及 Behaviour 夹具，定向 Debug 编译/原 Gameplay 测试成功：`out/ecs-e0-fixture-build-debug-second.log`、`out/ecs-e0-fixture-debug-second.log`（11/11 夹具）。
- Release canonical SkipTests 构建成功：`out/ecs-e0-build-release-first.log`。两者均不部署，不冒充完整无 Skip 回归。
- Debug 合同定向 CTest 7/7，15.76 秒：`out/ecs-e0-contract-ctest-debug-first.log`。
- 首轮测量准备使用递归文件扫描后过滤 bin/obj，意外遍历大量生成目录；未产生样本。核验本次启动进程的命令/父进程/创建时间后停止，仅修正脚本为 Git 源码库存枚举。保留 `out/ecs-e0-measure-release-first.log` 和 `out/verification/ecs-e0/b9ed2564dbc34b239c4195a33de434de`，不得算作已测轮次。后续源码库存开始/结束均复核文件集合和 SHA，程序集也复核 SHA。

## 三轮 Release 原实现结果

三轮均通过，每轮 33 项×100 样本（读循环每样本 16 遍），638 份 managed 源文件、18 个测试输出文件开始/结束 SHA 一致。
目录 `out/verification/ecs-e0/e8b32dbfe9f6458ab710771b30a06bdf`，总日志 `out/ecs-e0-measure-release-second.log`。
环境：Windows 10.0.19045 / x64 / .NET 8.0.28 / 12 个逻辑处理器 / Workstation GC；未强制关闭分层 JIT，非隔离性能实验室。测试程序集 MVID `d150cfca-5b40-47ab-8102-5b9bbbc74b36`，World 程序集 MVID `e7881408-4606-4ade-959e-4461bb021b7a`。
18 文件的原二进制闭包已按库存 SHA 复制到同目录 `baseline-binaries`，没有移动/删除原文件。E1–E5 重新编译后仍可运行这份旧实现；比较时两份程序必须使用同一受控 CLR 环境，不把历史 .NET 补丁版本差异算作 ECS 收益。

| 文件 | SHA256 |
| --- | --- |
| inputs.json | 11BF647B688ED2F87900F4C40F56DE49D6975815A6B7D00D5726B4B54DAA414B |
| run-1.log | 8E44E4B4302C76C48E230C29E64904C665C42B3D0050854DFC501F6157763362 |
| run-2.log | CA54CB2F6A0AD991D465D4FFAB063E3CFB66496A1BE45858E1830FA0F30472A5 |
| run-3.log | 4D6C3E04658AFBA08CAE127C97663A7F1A165AF9AB1FEC2595100B33E36EDFE8 |

下表为三轮各自统计量的最小–最大值；不是置信区间。MB 使用十进制。线程分配不是净存活内存；这些都是 CPU 合成数据，不能换算游戏 FPS。

| 对象数/工作负载 | P50 ms | P95 ms | 每次线程分配中位数 |
| --- | --- | --- | --- |
| 100 / Transform 遍历 | 0.0133–0.0150 | 0.0141–0.0212 | 6,688 B |
| 1000 / Transform 遍历 | 0.0526–0.0569 | 0.0800–0.0823 | 64,288 B |
| 4096 / Transform 遍历 | 0.1238–0.1302 | 0.1876–0.2040 | 262,432 B |
| 4096 / Transform+Marker 筛选 | 0.1483–0.1679 | 0.2221–0.2460 | 262,432 B |
| 4096 / World 无写固定步 | 18.119–19.315 | 33.096–34.177 | 12.83 MB |
| 100 / Play 无写固定步 | 6.226–7.358 | 8.697–11.404 | 1.66 MB |
| 1000 / Play 无写固定步 | 20.903–21.091 | 30.437–32.286 | 17.96 MB |
| 4096 / Play 无写固定步 | 98.793–100.093 | 114.704–128.328 | 73.78 MB |
| 4096 / Play 每 100 对象写一个 | 98.321–100.320 | 114.224–121.888 | 74.11 MB |
| 4096 / Play 全写 | 115.157–116.735 | 133.616–140.549 | 80.97–80.98 MB |
| 4096 / 全部 Behaviour 写入 | 144.216–149.956 | 164.190–168.071 | 95.39–95.40 MB |
| 4096 / 每 100 对象销毁重建一个 | 100.618–104.079 | 129.019–131.661 | 74.47 MB |

4096 对象无写 Play 的分项 P50：setup 41.50–41.91 ms、world/signals 17.84–18.12 ms、metadata 30.53–31.36 ms、插值准备 5.42–6.32 ms。不能把各自分位数相加当作整体分位数。源码核对确认前几项包含全量捕获、编码验证、绑定字典和文档副本，插值准备仍解码 Transform JSON；不是渲染器 GPU 或 AI 等待。

### 测量结论与 E1/E3 比较规则

1. 保持 Sparse Set/强类型池为 E1 候选，但不宣称 E1 单独会让整帧明显加速；先移除查询中的逐对象包装与热路径装箱。
2. 全量固定步文档开销远高于该夹具的只读查询成本，E3 的增量候选、绑定/回执/插值准备不可省略。所有预算和权限验证仍须等价。
3. 当前默认 CLR/桌面环境下时间噪声明显：100 对象无写 Play 的跨轮 P95 范围宽约 31%，全写约 50%；本数据**不足以判断 5% 小场景退化**。保留全部轮次，不挑最快轮、不改宽原阈值，也不声称已经锁定页大小。
4. 本基线可用于行为等价、分配规模和热点定位；E1/E3 先以正确性、无热装箱和查询包装分配消除为硬门禁。正式性能验收继续要求同机旧/新程序集交替测量、明确 JIT/GC 预热/配置、更长采样；噪声仍大于目标则结果为“不确定”，不能通过 E5 性能门禁。20% 热点收益/5% 小场景退化仍为候选目标，不能把本轮数据标为达成。
5. 页大小、查询缓存容量和全局验证退路只在实现候选后比较；禁止仅因这些基线看起来慢就删掉文档上限、可撤销命令或原生 fail-stop。

## 双配置领域/合同及补充回归

- Debug/Release 约定 CTest 各 **16/16**，219.55 / 154.26 秒。包含 Architecture、Physics、Pose、Player（含实际角色/动画/数值求解/蒙皮联合路径）、SceneUiJointProducts、UiCostJoint、四档 SceneCostJoint，以及 Runtime/Scene/SceneRendering/EditorCore/Gameplay/EditorTransport。
- 原生组单独 **13/13**，Debug 9.54 / Release 8.59 秒；此组与上述 16 项有 Architecture 重叠，不能累加为 29 个不同测试。
- 双配置 managed/native smoke 通过；运行前逐一核对 smoke 中 NcmaNative/NcmaPhysics 与本配置官方构建输出 SHA 相同。
- Gameplay **53/53**，Movement **38/38**（此组是确定性 fake，不能替代实际 Jolt；后者另有 Player 联合证据），Gameplay UI **18/18**；新增 E0 默认夹具 **11/11**。
- 原二进制闭包搬移后运行同一 Gameplay/Movement/UI/E0 默认夹具通过：`out/ecs-e0-archived-baseline-smoke-first.log`。
- Python **45/45**（1.396 秒）与 `python -m ncma_tools.cli inspect .` 通过。
- 以上是 E0 只改测试/文档的分片验证；**未重新执行完整 no-Skip 76 项产品回归，未部署**。M8.2 的既有完整回归不冒充本片新回归。

日志前缀：`out/ecs-e0-domain-ctest-{debug,release}-first.log`、`ecs-e0-domain-detail-{debug,release}-first.log`、`ecs-e0-native-{Debug,Release}-first.log`、`ecs-e0-managed-smoke-{Debug,Release}-first.log`、`ecs-e0-python-first.log`、`ecs-e0-inspect-first.log`。
机器可读冻结索引见 [ECS_E0_EVIDENCE.json](ECS_E0_EVIDENCE.json)；其中 raw 工件仍在本地 ignored `out/`，仓库保存路径、大小和 SHA，不暗示已上传全部原始图片/日志。

### 实际领域结果范围

- 角色联合：Debug `tests-ac8364a78a904009a5671b3e2c9eb4ac`、Release `tests-a112db10303a421fad2c2e48d39fb54d` 的 `out/verification/m2-7/<configuration>/<generation>/m4-joint/profile.json`。0/1/8/32 角色，32 预热+32 样本及 32 故障/恢复/Stop 周期；是实际插件路径上的合成资产，不是用户真实游戏素材验收。
- Release 32 角色模拟 P50/P95 为 8.2433/11.9171 ms；联合线程分配中位数 5,459,344 B。模拟时间与联合分配统计范围不同；GPU 仍是异步 last-valid 观察，不是逐帧 GPU 时间。
- 纯 2D 两配置各 95 检查通过、参考误差 0；3075 元素。Release 256 次静态 Prepare 0.0478 ms、线程分配 0、无新增上传/绘制，未初始化多余 3D 资源。单次本机观察不代替完整 2D 性能验收；partialDirtyUploadImplemented 仍 false。
- 3D Off/Low/Medium/High 每配置分别 806/860/860/920 检查，每档 180 联合图像，最大误差 1、API 0/0。正式混合 FBX/IBL/阴影/Bloom/HUD 产品各 866 检查通过。各域预算/人工/长稳标志保持原值，不翻成已通过。

## 正式产品与下一片

正式部署仍为 M8.2 `Complete cd023930bb454a6fbee9b1cd70b1b7a0`，EXE SHA256 `C80EDA73C7BA9F97E71B372CD14CF6A3F44A02FDBA16561F593B0D5527217E4E`。没有替换正式存储、GUI、包或 EXE；生成的 SkipTests 候选不部署。四份既有 `.vs/.user` 变化未纳入本片。
交付前再次核对 638 份 managed 源码、测量脚本、18 个已搬移基线文件、24 项证据文件和正式部署清单 144 项大小/SHA，全部一致。
下一片 E1：先补 Entity 代次/槽位/恢复拒绝测试和强类型池测试，再原地迁移唯一 World 及其消费者，移除被替代包装类型；不得借机开放不受检 ref 或删除原事务验证。
