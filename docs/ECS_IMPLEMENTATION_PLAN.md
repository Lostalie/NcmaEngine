# ECS 重构实施合同（E0–E5）

2026-10-16。用户已批准按方案实施。顺序：M8.2 自动候选交付 → 本重构 → M8.3 及后续 → M9 Windows。
本文件规定目标和门禁；各项是否实现以第 7、8 节的状态和证据为准。M8.2 基点为 `51010c33755ab35ac5fa3f45bc297c074ea3addb`。

## 1. 不变边界

- C# `Runtime.World` 是唯一实体/组件权威；不得新建与其镜像同步的第二个 World。
- GameObject 是 Entity 的高层组合式入口，不要求继承；脚本与批处理 System 使用同一份组件。
- 扁平对象组织、可选 Transform、单对象删除。骨骼/动画/UI 自身层级不成为场景父子树。
- C# 承担游戏逻辑。Python 仅可选独立模块，异步接收有界快照并返回提案；不得等待推理或 IPC 后再完成 tick。
- C++ 保留数值/渲染/物理等性能插件；版本化批量 C ABI 不携带托管引用、STL 或 ECS 内存地址。
- UUID 是持久身份；运行时句柄不得进入 SceneDocument、资产、Agent 提案或网络持久数据。
- 仅一个 EditSession/Undo；编辑事务继续允许完整快照。网络独立，无 GameObject RPC/复制标志。
- 保留现行 `.ncmascene`/组件 schema，不恢复旧格式、旧入口或兼容空壳。

## 2. 身份与公开 API

目标 Entity：`WorldIdentity + Index + Generation`，默认值无效。World 身份变化、删除、槽位复用、恢复、越界和跨 World 都必须拒绝陈旧引用。
代次溢出时退役槽位或明确拒绝，不绕回。恢复只在完整准备成功后更换身份；失败不能使有效句柄失效。

第一版保留现有对象的 UUID；UUID-less 瞬态实体不在此次范围。UUID→Entity 为冷路径索引，查询/批量更新不逐组件反查 UUID。
GameObject 惰性创建；不能为每个 Entity 强制分配包装对象。包装相等性依赖实体身份，不依赖引用相等；旧包装不得重新绑定新实体。
现有 `Ncma.Runtime.GameObject` 和 `Ncma.GameObject` 消费者迁移后收敛为单一公开门面，不长期保留两层逐对象包装。

程序集依赖须保持单向：低层 Runtime 提供 Entity/World/组件访问，不引用 Managed 或 Gameplay。现有用户侧 `Ncma.GameObject` 可直接持有 World 服务和 Entity；移除其中的 `Ncma.Runtime.GameObject` 二次包装。Runtime/Scene/渲染等低层消费者改为 World + Entity API；Behaviour 上下文仍由 Gameplay/Managed 服务注入，不能为了门面合并把脚本生命周期搬入 Runtime。实施时统一迁移 Create/Find/Resolve/GetObjects 消费者和两个 ObjectReference 表达，不添加旧签名转发壳。

## 3. 存储与 Query

首选强类型 Sparse Set：每种 `T` 独立 dense entity/value 数组及 sparse 索引。热循环不 `object` 装箱；字符串等现有合法值成员仍支持，不能草率改为全量 unmanaged 限制。
名称/注册元数据为冷数据；Behaviour 生命周期由 Gameplay 持有；资源使用有生命周期约束的句柄。

候选页/COW 与脏写集用于 E3，具体页大小须实测；E0 仅冻结原实现基线，不能从原实现数据推断最优页大小。
查询支持有界 All/None、常见多组件组合；复用查询计划，不缓存可跨提交使用的裸指针。成员版本、数据版本和 World 身份分开。
默认按创建序号确定性遍历；既有 UUID 排序消费者保持语义。dense swap-delete 顺序不成为公开承诺。
结构变化后更新成员缓存；未提交创建的实体不可出现在公开查询。公共 API 返回副本/受检写入；不提供不受限 `ref`、跨 await/tick 的可写租约。

## 4. 固定步与失败语义

顺序：Begin → 输入/信号 → Behaviour/System 暂存 → 全批预算/依赖/权限预检 → 必需时物理预检及求解 → 元数据/实例/回执/插值准备 → 最终验证 → 发布 tick/状态 → 只读观察者。

- 读取仍看已提交状态，后一个 System 不隐式看见前一个 System 的暂存写入。
- 同批重复目标拒绝；跨批按确定性顺序 last-write-wins。无效结构命令即使被调用者捕获，也毒化整个候选步。
- 外部写权限、运动权威和冻结成员资格不能被直接组件访问绕过。
- 所有可能失败的验证、分配、扩展回调在发布之前；发布点不执行任意扩展/IO。
- 移除每帧完整文档不能移除它承担的验证：类型/schema、归一化后 payload、对象/组件/绑定预算、引用依赖、全局精确编码预算均须保留。
- 可缓存每项精确编码/长度和聚合预算；跨对象验证器需明确依赖。无法增量化的可信验证器保留显式有界冷路径，不能静默跳过。
- 提交前失败回滚托管候选组件/结构/信号消费/回执；私有 Behaviour 状态与外部 IO 不回滚。
- 原生求解进入 `ExecuteAndStage` 后不可声称数值回滚；故障 fail-stop，必须显式恢复。
- 提交后观察者出错只故障 runner，不能撤销已提交 tick。编辑持久化补偿失败保留证据并冻结。

## 5. 分阶段工作及放行

| 阶段 | 交付 | 必须通过 |
| --- | --- | --- |
| E0 | 本合同、消费者/测试映射、可重复原实现性能基线及差距清单 | 100/1000/4096 对象读/联合筛选、固定步零/稀疏/全写、结构操作；真实领域成本证据索引；固定数据和原始样本；噪声说明 |
| E1 | World 原地替换身份与强类型池；UUID 映射；GameObject 同实体门面 | 失效/复用/溢出/恢复/跨 World/线程；可选组件/字符串；严格场景往返；所有受影响消费者编译；此阶段可保留旧全量提交机制 |
| E2 | Query 计划与确定性顺序；有界结构批次/待提交句柄和回执 | All/None/组合交集、成员变化、迭代失效、命令预算、全批预检、没有前缀提交 |
| E3 | 增量固定步写集、绑定/实例/信号/回执/插值 | 原有事务/权威/私有状态/观察者/物理故障范围等价；保留全部预算；稳定帧不构造完整 SceneDocument/World JSON |
| E4 | 角色/动画/渲染/编辑/Agent 消费者迁移 | 真实 Editor/Player/Headless 联合回归；保留批量 C ABI 与唯一命令历史；Agent 不增加写权限 |
| E5 | 精确旧机制审计、完整双配置回归、测量对照、交付记录 | Debug/Release 无 Skip、原格式/ABI/权限/包/备份恢复/无 Python 门禁；确认收益与小场景退化范围；提交推送后才进入 M8.3 |

每片先测试再提交推送；不通过先修复并保留失败证据。旧类型在替代消费者就绪的片内删除，不拖到 E5 才删除；E5 是残留审计，不是保留兼容层的理由。
E0 不修改正式存储或部署 EXE。后续运行时代码改动必须建立新源码冻结，不能沿用 M8.2 的 906 文件冻结作为新源码证据。

## 6. 性能判定协议

- 同机同配置，固定输入、工作量、语义输出；Release 独立进程至少三次，保留原始样本和环境/程序集身份。
- CPU 分项和整体固定步同时记录 P50/P95/P99、线程分配、GC；内存采样注明进程累计峰值与局部值区别。
- GPU/上传/原生驻留须从实际领域工作负载获取，不能用 CPU 合成场景替代。
- 查询预热后无逐实体分配/装箱为目标；有结构修改/输出复制时分开核算。
- 原方案的“热点 ≥20% 改善、小场景 P95 ≤5% 退化”为待测目标，不是当前已达成或噪声不明时机械通过的数字。E0 三轮波动超过阈值时增加样本/隔离测量，不能放宽阈值冒充收益。
- 容量仍为现行 4096 对象和各域既有预算。不得单独增大 ECS 容量而绕过文档、Agent、插件、资源约束。
- 人工、真实用户素材、目标机、自包含、完整性能、1h 与历史故障调查独立保留。

## 7. 实施状态

E0 合同/自动基线门禁完成并已提交推送 `936b51f7f38e072fb554be9c820ddf24752cb3ba`。原实现基线工具位于 `managed/Ncma.Gameplay.Tests/EcsBaseline.cs`；详细证据、时间噪声和仍开放的正式性能门禁见 `ECS_E0_BASELINE.md`。

E1 分两个可独立回归的实施片，整体门禁不变：

- **E1-A 强类型存储**：在唯一 World 内替换逐对象 `Dictionary<Type, object>`，建立按类型 dense 值数组/slot 数组及有界 sparse 索引；保留现行公共身份和完整快照事务。内部 slot 不是公开 Entity，不能序列化或用于绕过 World 校验。整池首次写复制是此片的过渡提交策略，不是 E3 的分页/增量实现。
- **E1-B 身份与单一门面**：引入 WorldIdentity/Index/Generation、代次溢出退役、失败候选句柄永不复活、确定性创建序号；迁移所有消费者和两种 ObjectReference；用户侧 GameObject 直接访问同一 Entity，删除被替代的 Runtime.GameObject，不添加旧 API 转发壳。信号/批次目标同样校验完整身份；注册/序列化负例覆盖组件内嵌套运行时 Entity，不能因为它是值类型便允许持久化句柄。代次保留/烧号不能随候选回滚而使旧候选引用复活。

E1-A 已实现并通过同源码双配置各 89 项 CTest 及补充验证，提交 `92a1b3084736a20ae45db6310d511b0786c3621f` 与远端一致，见 `ECS_E1_A_DELIVERY.md`。

E1-B 已完成两个增量：第一增量 `0fa22943ae8e955ebd3e67a44ac51a0ad580c9e1` 引入 Entity 代次、失败候选烧号/退役、两种旧 ObjectReference 移除及信号/批次完整身份；第二增量 `2926bdbfc7c314665ebf5d9d18972730389ecdfc` 迁移所有低层消费者到 World.CreateEntity/FindEntity/GetEntities 和显式组件访问，删除 Runtime.GameObject 与旧签名，仅保留共享同一存储的 Ncma.GameObject 高层门面。第二增量同一 913 文件冻结通过 Debug/Release 各 89 项 CTest、补充冒烟/Python 与最终文件核验，身份测试 15/15；失败修复和全部证据见 `ECS_E1_B_DELIVERY.md`。未保留兼容壳。

用户随后要求更新 EXE：该 E1-B 源码另行完成无 Skip Release Build.bat，13 原生 + 76 托管/产品 CTest、Python 45、三轮历史 profile、前后审计与受检部署通过。正式版本为 E1-B Release，Complete `7f3d3822dd39431b9c203e17194afb8c`；148 个安装文件和 148 个旧版备份文件核验通过，实际 apphost 校验 exit 0。证据 `out/ecs-e1b2-exe-full-release-first.log`、`out/ecs-e1b2-exe-verification-first.log`。这不是 E5 新旧配对性能或完整 ECS 验收。

## 8. E2 分片与 Query 契约

E2-A 查询基础与 E2-B 结构批次分别测试、交付；整个 E2 必须在两片完成后才关闭。E2-A 已实现，完整双配置自动回归及交付核验通过；E2-B、E3–E5、M8.3 尚未开始。本片只构建候选，不替换用户刚更新的正式 E1-B EXE。

### E2-A 已实现的查询边界

- `World.Query<T>()`、`Query<T1,T2>()`、`Query<T1,T2,T3>()` 和 `CreateQuery(all, none, capacity)` 返回调用者持有的 `EntityQuery`。Runtime 不引用 Managed/Gameplay，也不在 World 中无限保留用户计划。
- All/None 合计最多 16 个注册类型；重复、交集、null、未注册类型拒绝。结果预算 1–4096，默认 4096。空 All 包含没有组件的实体，None 按类型存在与否排除，不按组件字段值排除。
- 多组件查询从最小 All 池扫描，做交集和排除后按创建序号排序。dense swap-delete、添加组件的顺序或槽位复用不会改变公开顺序。既有 UUID 排序消费者未迁移，语义不变。
- `World.Identity`、`MembershipVersion`、既有数据/提交 `Revision` 分开。MembershipVersion 是最近一次结构发布的 revision，不是结构操作计数。改名、既有组件值写入、空 tick 不改变成员版本；任何成功结构修改保守地使该 World 的查询缓存失效，包括最终成员相同的 remove/add。
- 固定步暂存结构不更新公开成员版本，不出现在查询中；成功提交才更新，失败或 Abort 不影响旧缓存/迭代器。恢复成功更换 World 身份，旧计划不能静默绑定新 World，须重新创建；恢复失败不使有效计划失效。
- 普通 `foreach` 使用受检 ref-struct 迭代器，不走 IEnumerable 装箱、不跨 await、不暴露池或可写 ref。每次 MoveNext/Current 检查所属 World、线程与成员版本；值写入后仍可迭代并通过 Get 读取提交值。结构提交后即使重新读取过该计划，旧迭代器也必须拒绝。
- `Count`、枚举或 `CopyTo` 超过计划预算直接拒绝，不返回截断结果；CopyTo 目标过小或计划刷新失败时目标不变。返回的 Entity 副本仍受原代次/World/写权限/冻结成员检查约束。
- 缓存只保存 Entity，不保存裸指针或组件值；预热且成员不变时遍历/读取/复制无需逐实体分配。建计划、结构失效后的重新筛选排序及完整 tick 的成本与热循环分开，不宣称 E3 或整帧加速。

```csharp
// 在所属 World 的 owner thread 创建并保留计划；World 恢复后重新创建。
EntityQuery moving = world.Query<TransformData>();
foreach (Entity entity in moving)
{
    TransformData value = world.Get<TransformData>(entity); // 值副本、已提交状态
    // 写入仍通过 World.Set 和既有权限/固定步规则，不提供直接池写入。
}
```

### E2-B 后续范围（未实现）

新增有界结构批次，覆盖创建/删除/增删组件、待提交句柄与回执，接入既有固定步和唯一 RuntimeCommandBuffer/SceneDocument 绑定生命周期，不引入独立 World 或第二提交器。保留现行单命令调用的合法业务语义；被替代入口与消费者必须同片迁移，不保留兼容空壳。先冻结重复目标和有依赖的创建后附加组件的具体规则，再实现全批预算/权限/目标/类型/引用预检。批次失败即使被调用者捕获也毒化整个候选步；不能提交前缀，不能提前发布句柄/回执。E3 再优化完整文档和整池复制，不在 E2 偷换现有验证。

### E2-A 验证记录

首轮 Debug `out/ecs-e2a-runtime-debug-first.log`：原 Runtime 13/13、存储 14/14、身份 15/15，新增 Query 17/17 通过。覆盖 All/None/多类型、缓存更新、迭代失效、恢复、线程、权限/冻结、预算无截断、默认/越界迭代器、固定种子 600 次结构修改的独立创建列表对照、revision 溢出，以及 4096 实体预热后 20 轮遍历/Get/CopyTo 线程分配 **0 字节**。该分配结果不是整帧、GPU、真实领域收益或 E5 配对性能验收。

最终源码冻结 `out/verification/ecs-e2a/source-freeze-first.json` 共 915 项，SHA256 `02B257274860A1661F0288456F3613B08530B6CE406350226BC5A7E9FD0A42FC`。两配置 Build.bat -SkipTests 构建零编译警告，再顺序执行全部 89 项 CTest 和原生托管冒烟、Python 45/inspect。不部署、不覆盖 E0 原始 18 文件基线，不关闭人工、目标机、自包含、完整性能或长稳门禁。

首轮完整 Debug 为 **88/89，1109.42 秒**：`NcmaSceneDocumentTests` 的完整场景保存用例在未改动的 `SceneDocumentFiles.Save` 第 60 行 File.Replace 抛出 IOException（无法删除要被替换的文件）。原保存代码、测试断言和 E2-A 冻结均未修改；使用测试自身新生成的独立目录单独复测原用例 **1/1** 通过，不能据此声称已证明故障根因或关闭历史 File.Replace 调查。保留 `out/ecs-e2a-ctest-debug-first.log`、`out/ecs-e2a-ctest-debug-details-first.log`、`out/ecs-e2a-scene-recheck-debug-first.log` 及原测试目录；不删除失败目标、不采用非原子覆盖或吞掉异常。随后同源码完整 Debug 第二轮，再顺序执行 Release，最终结果另记。

最终结果：

- Debug 第二轮 **89/89，1110.90 秒**；Release 第一轮 **89/89，1000.85 秒**。两配置 Runtime 13/13、typed storage 14/14、identity 15/15、Query 17/17、Gameplay 53/53、E0 fixture 11/11 全部通过；4096 实体 × 20 轮热查询分配均为 0 字节。场景文档 36/36（包括严格新格式/旧格式拒绝）通过。
- 日志 `out/ecs-e2a-build-{debug,release}-first.log`、`out/ecs-e2a-ctest-debug-second.log`、`out/ecs-e2a-ctest-release-first.log`；完整用例输出 `out/ecs-e2a-ctest-{debug-details-second,release-details-first}.log`。补充验证 `out/ecs-e2a-{supplemental,smoke,python,inspect}-{debug,release}-first.log`；两配置 Python 均 45 项通过。
- Debug 候选 `3a411795b39f42ddb4da5d4b0f5e4b83`，Release 候选 `9e1fb6d5571f4e459e606d154ef9d0ec`。两配置各五个包的产品身份、路径、size/SHA256 和闭包核验通过；Editor/Null/DX11/Null-Physics/DX11-Physics 清单各 144/56/113/67/116 文件。未修改原有有界日志白名单。
- 最终 `out/ecs-e2a-delivery-verification-first.log` 通过，SHA256 `D21682225976424589E05D86C0569A9A3360C7FB71D2FAF714D2FD81EB568481`：源码冻结相同；正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 安装 148 文件（144 清单）和备份 148 文件保持完整，E0 原始 18 个归档程序集逐一匹配。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未改动。
- 最终顺序回归会话 57598 exit 0，所有构建/测试结束。第一轮失败会话 44267 exit 1 和失败证据保留。本片未执行新的无 Skip 部署或 E5 新旧配对性能门禁；只关闭 E2-A 自动候选门禁，不关闭整个 E2/ECS。
