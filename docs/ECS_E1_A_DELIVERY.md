# ECS E1-A：唯一 World 的强类型组件存储

2026-10-16。本片自动候选门禁完成，Git 交付以本片提交与远端核验为准。基点为 E0 `936b51f7f38e072fb554be9c820ddf24752cb3ba`，开始前已核对远端 main。E1 整体尚未完成；正式 EXE 不变。

## 本片范围

- 删除 World.Entry 中逐对象的 `Dictionary<Type, object>`。Entry 只保留 UUID、名称和内部存储 slot。
- 每种组件使用独立 `ComponentPool<T>`：dense `T[]`、dense slot 数组、4096 项 sparse 索引。空对象不分配组件字典；池按首次使用创建。交换删除修正反向索引并清空尾部引用。
- 泛型注册验证、Get/Set 和权威写返回值不经异构 object 存储；异构场景解码仍允许冷路径临时装箱，JSON 验证/完整文档路径仍分配，不能宣称整个 tick 零分配。
- 候选池在首次修改时整池复制，未改池共享只读数据。读取始终使用已提交存储；创建/删除/增删组件只在候选中可见。准备完成后交换唯一权威，不新增影子 World。
- slot 可复用，但本片公开引用仍为原 WorldId/单调 ID。公开引用不暴露 slot；新 Entity 代次、统一 ObjectReference 和 GameObject 门面属于 E1-B，尚未完成。
- 场景 JSON、4096 对象/64 组件/64 KiB payload/4 MiB 快照预算、可选 Transform、权威与冻结成员、确定性遍历、完整快照 Undo 和原生 ABI 均保留。

没有兼容新旧 World 的同步层。原身份/包装 API 尚未被替代，因此暂不删除；E1-B 必须迁移消费者后直接删除，不得增加兼容壳。

## 测试与证据

新增 `managed/Ncma.Runtime.Tests/EcsStorageTests.cs`，在既有 `NcmaManagedHeadlessTests` 入口默认执行：

1. dense 交换删除、尾部字符串引用清理、越界拒绝。
2. 固定随机种子，30000 次写/删与字典 oracle 逐槽对照。
3. 预热后池内 Get/Set 20000 次零线程分配（不含 World/JSON 验证）。
4. 父/子及嵌套候选相互隔离。
5. 已持有对象的公开 Has/Get 20000 次零线程分配（不含枚举/新包装分配）。
6. 交换删除不改变公开创建顺序；已删除对象拒绝访问。
7. 两轮各 4096 对象创建/删除，容量上限不松动，复用 slot 不继承旧组件。
8. 已提交读取、跨写 last-write-wins、提交/中止。
9. 候选删除并复用 slot，提交前不泄漏，撤销后原值保留。
10. 候选已移除组件的受检写失败，即使捕获仍毒化提交。
11. Unicode 字符串往返、失败恢复保留身份、成功恢复失效旧引用，不序列化 slot。
12. 多池更新后最终验证失败，全体已提交值/revision/tick 保留。
13. 权威/冻结成员仍拦截无权写、移除、删除，并要求全部 owner 发布。
14. owner-thread 和只读访问约束仍生效。

首轮 Debug 独立 Runtime：原有 13/13 + 新增 14/14 通过，日志 `out/ecs-e1a-headless-debug-first.log`。这不是完整产品回归或整体 ECS 验收。

新源码冻结（不复用 M8.2/E0）：`out/verification/ecs-e1a/source-freeze-first.json`，911 项，含全部当前 managed/native/build/Python/test 源码及 solution，不含文档、IDE 和输出。双配置回归后再次核对一致；SHA256 `339D4B56F2446567ADC7EFFC1D52059F9081E1A658E3689F07ACFC4C0BAD4CD9`。

- canonical `Build.bat -Configuration Debug -SkipTests` / `Release -SkipTests` 均成功，零编译警告；候选分别为 `88b0c421d16848079d92f0d2ed1ec291` / `891eddb06b8f4863928c5b1827c164ce`，不部署。
- Debug 全部 CTest **89/89** 通过，1101.80 秒；日志 `out/ecs-e1a-ctest-debug-first.log`，完整细项 `out/ecs-e1a-ctest-debug-details-first.log`，确认 Runtime 13/13、新存储 14/14、Gameplay 53/53、E0 夹具 11/11（非测量）。
- Debug managed/native 冒烟、Python **45/45** 及 inspect 通过，见 `out/ecs-e1a-{smoke,python,inspect}-debug-first.log`；原生 DLL 复制后与 canonical 输出哈希一致。
- Release 全部 CTest **89/89** 通过，980.00 秒；日志 `out/ecs-e1a-ctest-release-first.log`，细项 `out/ecs-e1a-ctest-release-details-first.log`。Runtime 13/13、新存储 14/14、Gameplay 53/53、Movement 38/38（fake，非真实物理验收）、Gameplay UI 18/18、E0 夹具 11/11 全部通过；真实 Player/物理/动画/渲染联合用例独立包含在 CTest 中。
- Release managed/native 冒烟、Python **45/45** 及 inspect 通过，见 `out/ecs-e1a-{smoke,python,inspect}-release-first.log`；两配置的补充步骤收据分别为 `out/ecs-e1a-supplemental-{debug,release}-first.log`。
- 此流程是双配置候选构建与全部 CTest，不是完整无 Skip Build.bat/三轮旧性能 profile/部署门禁；未更新正式安装。

## 最终核验与保留的失败记录

`out/ecs-e1a-delivery-verification-third.log` 通过，SHA256 `4B3666DD3E188B1D9011BC9B860B98ED22505F8C17C930C777465B05CB106A68`。其中记录源码冻结、build/CTest/细项/补充日志哈希及 10 个候选包 manifest 哈希。

- 两配置各 5 包：Editor/Player-Null/Player-DX11/Null-Physics/DX11-Physics 清单分别 **144/56/113/67/116** 文件，身份、路径、size、SHA256 和闭包有效。Editor 另有 manifest 和测试日志，实际 146 文件；各 Player 仅另有 manifest。
- 正式 M8.2 安装的 144 个清单文件逐一匹配原 `f827714b75e742cbb6a5fe5a0de3bb87` 包，实际 148 文件；原备份 148 文件逐一匹配 Complete journal。EXE SHA256 保持 `C80EDA73C7BA9F97E71B372CD14CF6A3F44A02FDBA16561F593B0D5527217E4E`。
- E0 原始 `baseline-binaries` 18 文件逐一匹配原测量清单，未被新构建覆盖。
- `out/ecs-e1a-delivery-verification-first.log`：辅助脚本误将 Player 送入仅接受 Editor 的校验器，按原规则被拒；第二次日志：辅助脚本错误要求 Editor 也没有正常测试日志，闭包检查拒绝。已改为精确产品身份/全文件哈希，Editor 使用既有有界用户日志规则，Player 保持精确文件闭包。诊断输出曾遇到 StrictMode 空数组属性错误，改用逐项枚举；不影响产品测试。
- 以上仅修正 `out/verification/ecs-e1a/verify-delivery.ps1` 辅助取证脚本；产品源码、源码冻结、产品校验器和通过门槛均未变化。不删除失败日志或额外日志以制造通过。

## 暂不关闭的门禁

- E1-B：代次复用/溢出/失败候选身份与单一 GameObject、全部身份消费者迁移。
- E2 Query、E3 增量事务、E4 领域优化、E5 同源码无 Skip Debug/Release、包/部署恢复和新旧成对性能测量。
- 整池复制和 snapshot/JSON 仍可能主导固定步成本；本片不作性能提升或小场景无退化声明。
- 正式 EXE 保持 M8.2 Complete `cd023930bb454a6fbee9b1cd70b1b7a0`；Skip 构建不能部署。
- 人工、目标机、自包含、完整性能、1h 和历史部署故障调查继续开放。保留全部失败记录、E0 原始数据及 18 文件原始程序集归档、4 个 IDE 修改。
