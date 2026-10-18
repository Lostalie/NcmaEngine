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

E2-A 查询基础与 E2-B 结构批次分别测试、交付。E2-A 已实现并提交推送 `5d46ef0596566cfff85370c84e3a36e5b84c58ca`，本片开始时精确核对本地与远端 main 一致。E2-B 已通过同源码完整双配置回归和最终交付核验，E2 自动候选门禁完成；E3–E5、M8.3 尚未开始。本片只构建候选，不替换用户刚更新的正式 E1-B EXE。

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

### E2-B 有界结构批次契约

2026-10-18 实施。`IRuntimeCommands.SubmitBatch(ReadOnlySpan<StructuralCommand>)` 接入既有 RuntimeCommandBuffer，由同一个 PlaySession/World 固定步拥有最终提交权。声明式命令包括 Create/Destroy/Rename/Add/Remove/Attach/Detach/Enable；不是执行任意委托的公开 API，也不是新编辑历史、Agent 写入口或 Python 调度器。

- **完整身份和创建依赖**：已有目标用 `StructuralTarget.Existing(Entity)`，校验完整 World/Index/Generation 后定位候选；不能用过期 Entity 命中复用槽位。`CreatedAt(i)` 只能指向当前批次中更早的 Create。`PendingCreation(handle)` 只能引用同一 session/generation/固定步 attempt 内已接收、尚未删除的创建；上一步已提交对象应通过 `Resolve(handle)` 获取 Entity，再用 Existing。
- **重复/冲突**：同批按“对象 + 变更字段”去重，name、每个 component type、每个 binding UUID 各为一个字段。因而 Create → Rename/Add/Attach 是合法依赖，不被误判为重复对象；同一组件 Remove+Add、同一 binding Enable+Detach、两次 Rename 拒绝。Destroy 在同批内对整个对象独占；创建后删除可在两个有序批次中执行。跨批以提交调用顺序执行，改名 last-write-wins，组件/绑定仍须满足每一步前置条件。
- **共享预算**：每固定步所有单命令与批次累计最多 1024 条、1 MiB 命令 payload；不能通过拆批重置预算。空批、超限、序号溢出拒绝。新批次每条额外计入 128 字节预算，字符串按 UTF-8，组件和绑定使用有界 JSON 编码；组件按原始/归一化后批次编码总量中较大者计费。单组件 64 KiB、每对象 64 组件、4096 对象、绑定/Export 容量和完整 World/SceneDocument 4 MiB 验证不放宽。输入 JsonElement、绑定 Export 数组复制；输出数组不暴露。
- **全批准备**：先做输入预算/目标/依赖/冲突预检，再在同一个 World 的当前 step 上建立 metadata/slot/组件池 COW 候选分支。逐条执行现有 Stage 权限、类型/schema、归一化、runtime-attachable 和冻结成员检查，最后执行完整 World/SceneDocument 编码及 binding 全局验证。所有可能失败的复制/回执队列分配发生在接入 step 候选之前；失败恢复此前候选根和绑定表，不能留下批次前缀。代次预约、创建序号、已预约 token 不回滚，避免失败身份复活。
- **失败仍毒化整个 step**：拒绝后即使调用者 catch，也不能提交此前其他单命令、组件写入或批次。输入/信号消费、tick、公开查询成员和成功回执只在原有最终提交点发布。后续 Behaviour factory、跨域最终准备仍可能拒绝已接收的批次；`StructuralSubmission` 不是成功回执。私有脚本状态、外部 IO 不回滚，原生物理仍遵守 fail-stop。
- **待提交句柄**：删除旧 `PendingObject` 及其消费者声明，使用不公开候选 Entity 的 `PendingEntity`，包含只读 ObjectId/Token 与内部 attempt/完整 Entity。旧单命令 SpawnEmpty 同样返回此类型，保留其余单命令现行业务语义，不增加旧类型兼容壳。该句柄不授予实时 World 写权限，不能通过普通 World.Resolve 提前获得实体。
- **回执生命周期**：`CommandReceipt` 增加完整 `Entity?` 和 WorldIdentity，成功 step 后才可 GetReceipt。创建同一步又删除得到 removed/null；只有 committed 且完整身份匹配的创建可 Resolve。删除/槽位复用、恢复 World、Stop/Reload、错误 session/generation、失败 step、淘汰回执均不能重新解析旧创建；缓存仍只保留最近 1024 条，不持久化运行时身份。
- **保留边界**：owner-thread-only；OnUpdate/只读准备拒绝写入；可信 validator 重入被拒绝且无法通过 catch 绕过毒化。Runtime 不依赖 Gameplay/Managed。组件读写仍经过 World 权限，没有直接池写、裸指针、不受限 ref 或多线程写入。

```csharp
// 仅所属 PlaySession 的固定步回调；组件值是受注册 schema 约束的副本。
StructuralSubmission batch = commands.SubmitBatch([
    new StructuralCommand.Create(Guid.NewGuid(), "Projectile"),
    new StructuralCommand.Add(StructuralTarget.CreatedAt(0), transformSnapshot)
]);
PendingEntity pending = batch.Created(0); // 尚不能 Resolve/GetReceipt
```

保存该句柄，在**整个固定步成功提交之后**的 owner-thread 回调中读取（不是在上述提交回调中紧接着执行）：

```csharp
Entity entity = commands.Resolve(pending);
CommandReceipt receipt = commands.GetReceipt(batch[0]);
```

此片仍保留全量快照验证和整池首次写复制；每批额外准备全量候选有成本，不声称它比逐条命令更快。E3 才处理增量写集/编码缓存；E5 才给出新旧配对性能结论。

### E2-B 验证与交付记录

- 新增 `StructuralCandidateTests` 8 组：候选根/池值无前缀、失败代次保留、提交前不可见、元数据失败、权限与冻结成员、多批 COW 隔离、捕获验证器写入、完整 4 MiB 世界预算、失效准备动作；`StructuralBatchCases` 26 组覆盖创建依赖、全部结构/绑定命令、重复/冲突、空/默认/外来/过期输入、原始/归一化/聚合预算、1024/1025 边界、迟发 factory 失败、输入回滚、回执淘汰、删除/复用/Stop/恢复/重试、复制输入、重入及 owner/read-only/序号溢出。
- 定向 Debug 最终 `out/ecs-e2b-runtime-debug-first.log` 和 `out/ecs-e2b-gameplay-debug-sixth.log` 通过：Runtime 13、storage 14、identity 15、Query 17、新候选 8；Gameplay 53、新批次 26、Movement 38、UI 18、E0 fixtures 11。Movement 此处仍是确定性 fake，不是新 Jolt 角色验收。
- 定向失败保留：Gameplay Debug second 是新增测试误用 InputFrame 参数类型导致编译失败；third 为首次获取焦点会按原契约丢弃按键边沿，而测试未先建立焦点，修正为先聚焦再提交边沿；fifth 是新增 fixture 两处 nullable 编译错误，改为明确检查已产生的提交。没有削弱产品输入规则、断言或预算；原日志全部保留。
- 全回归源码冻结 `out/verification/ecs-e2b/source-freeze-first.json`：920 文件，SHA256 `B6D3B8393373F18FDB94A6520ABAC211A87E5DB8A9BB983742D5E97BB6CD3BC7`。顺序执行两配置 Build.bat -SkipTests（不部署）后各完整 89 项 CTest，再做托管/原生冒烟、Python 45/inspect 和包/正式安装/备份/E0 原始程序集核验。
- Debug/Release 第一轮标准构建均零编译警告，完整 CTest 分别 **89/89，1057.84 秒**、**89/89，935.32 秒**。两配置新候选 8/8、新批次 26/26、原 Runtime 13/storage 14/identity 15/Query 17/Gameplay 53/Movement 38/UI 18/E0 fixtures 11 全通过；smoke/Python 45/inspect 全通过，构建前后均核验同一 920 文件冻结。顺序会话 23322 已终止 exit 0，没有待轮询构建或测试。
- 日志 `out/ecs-e2b-build-{debug,release}-first.log`、`out/ecs-e2b-ctest-{debug,release}-first.log`、`out/ecs-e2b-ctest-{debug,release}-details-first.log` 和 `out/ecs-e2b-{supplemental,smoke,python,inspect}-{debug,release}-first.log`。本轮两个完整配置均首轮通过；先前定向失败证据照常保留。
- Debug 候选 `e577b51b849049b3979b16c54ed56395`；Release 候选 `47f4cceb6906453e9ba274442320fde8`。两配置各五包的产品身份、路径、size/SHA256 与闭包核验通过；Editor/Null/DX11/Null-Physics/DX11-Physics 清单分别 144/56/113/67/116 文件，沿用原有有界 Editor 日志白名单。
- 最终 `out/ecs-e2b-delivery-verification-first.log` exit 0，SHA256 `8A2FD7C2A8877345DC791EB1D9671B40D7AA8EB80B7DD2195854F75A231BB439`：冻结相同、十包完整；正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 的 144 清单/148 实际文件及 148 备份完整，E0 原始 18 个程序集 SHA 不变。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未改变。
- 本轮没有新的无 Skip 部署、三轮历史 profile 或 E5 新旧性能配对验收；人工、目标素材/目标机、自包含、完整性能、1h 和历史故障调查仍开放。保留四项无关 IDE 设置，不将 out 产物/日志加入源码提交。下一步 E3 增量固定步，不进入 M8.3。

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

## 9. E3 增量固定步实施顺序

2026-10-18，E2-B 本地及 GitHub main 精确核对为 `cb50b4f25e9acfaa85ddf9568717ec30e1e254ea`。E3 要同时消除热点整池复制、稳定固定步全量 World/SceneDocument JSON、以及与变更量无关的高层重复准备。不能只完成分页存储就宣告 E3 完成；编辑 Undo/持久化冷路径仍允许完整文档。

| 分片 | 改动和依赖 | 放行条件 |
| --- | --- | --- |
| E3-A 存储候选 | 按测量选择值页大小；稀疏/实体索引结构性 COW；替换整值池首次写复制，保留当前完整提交检查 | 多分支共享/撤销隔离、跨页 swap-delete/清空引用、所有池准备后再删除、实际旧 DLL 读路径对照、页粒度测量、双配置完整回归 |
| E3-B 增量验证 | 建立候选写集和受限的内部已准备视图；逐项精确编码长度与聚合预算代替稳定帧完整 JSON；统一 World/SceneDocument 预算，不改变场景格式 | 原始与归一化后大小、对象/组件/绑定/Export、schema、UUID/代次/引用、跨域权限、溢出、失败恢复全部保留；自定义序列化/可信验证器无法证明可缓存时保留明确有界冷路径，不能静默漏检 |
| E3-C 高层提交 | Commands.Begin 不再捕获整文档；绑定与实例按变更准备；信号/回执按完整 Entity 检查存活；插值从候选强类型 Transform 读取，不经 JSON 解码 | 固定顺序、提交前读取旧值、单一提交器、生命周期/信号/输入一次性消费、删除/复用、观察者错误与原生 fail-stop 语义保持；不新增 AI/网络写权限 |
| E3-D 联合验收 | 对 A–C 做无写/稀疏/全写/结构变化与真实领域测试，检查稳定固定步没有完整文档构造 | 精确计数/负例证据、完整 Debug/Release 与包闭包；同输入旧实现/候选配对数据。人工/目标机/完整性能/长稳仍归原门禁，不用局部基准代替 |

每片先测试再提交推送；当前 E3-A/B 自动候选通过，下一步 E3-C 剩余高层提交，随后 E3-D 联合验收。信号过滤在 B 的视图迁移中已接入完整 Entity，但不代表 C 完成。E3–E5 通过前不启动 M8.3。

### E3-A 混合分页存储

- 生产值池使用 `PagedValues<T>`，当前默认每页 32 个值。固定容量仍为 4096；未写页不分配值数组。Fork 共享目录及页，分别更换父/子写入所有权；首次修改只复制目录及被修改页，不靠不可靠引用计数，也不暴露可写 ref。
- 稀疏索引和 dense slot 索引仍是连续数组，只有 Add/Remove 等结构变化才复制；普通值写入不复制这两组索引。结构性新容量/页分配在修改数据之前准备。RemoveAll 先对所有参与池做 PrepareRemove，再开始删除，避免后半池分配失败造成前缀删除。
- 读操作、公开 Entity、Query 缓存/顺序、权限、恢复和唯一 PlaySession 最终提交器不变；此片仍执行现有完整 World/SceneDocument 验证。没有新增并发写入、公开底层数组或 ABI。
- 删除已不再使用的整值数组复制实现。`WholePoolBaseline` 仅在测试程序集保存 E2-B 已提交的池算法，改名并补无分配 PrepareRemove 方法，用于局部比较；它不是运行时兼容类型、后备存储或第二 World，不进入产品包。

### E3-A 选择依据与测试状态

- 初版将索引也分页：功能测试通过，但局部读取间接访问显著增加，因此未作为交付布局。保留 `out/ecs-e3a-pool-release-preliminary-{first,second,third}.log`：第一轮未关闭分层 JIT，受预热阶段变化影响，不用于跨页策略的最终结论；后两轮明确关闭分层 JIT。最终采用连续索引/分页值，仍不声称消除了所有直接池读取成本。
- 新增 PagedStorageTests 10 组：懒分配/页大小边界、双向分支隔离、只复制触及值页、跨页删除、发布前准备/发布零分配、跨池准备失败无前缀、8000 次多分支随机映射对照、热读写零分配、分支分配不随对象数增长、引用清空。原 E1 字符串清空断言只改为检查分页值的所有 4096 个位置，没有删除断言。`out/ecs-e3a-runtime-debug-third.log` 全通过；此前 second 的测试参考类机械改名误改接口名导致编译失败，修复后通过，失败日志保留。
- 三个独立进程测量目录 `out/verification/ecs-e3a/measure-first`，每轮 90 个池样例（100/1000/4096 × 整池参考及 16/32/64/128/256 值页 × 读/空 fork/1% 写/全写/1% 结构变化），每项 100 个原始样本，每样本 16 次操作。32 页在小规模稀疏分配、较大规模写入和目录/页对象数之间折中，不声明所有工作负载最优。
- 同目录额外保存两组各四文件的隔离闭包，只有 Runtime DLL 不同：E2-B 来自其 Release 候选 `47f4cceb6906453e9ba274442320fde8` 且 SHA 匹配原清单；E3-A 来自本片 Release 构建。**同一个新测量程序**只调用两版本共有的 World/Query API，三轮交替先后顺序，每轮 6 个读取负载、每项 300 样本 × 16 次。它不是两套不同测试程序的历史时间比较；源码/输入哈希和原始结果保留。
- 最终测量使用 `DOTNET_TieredCompilation=0`，这是局部算法比较条件，不改变产品配置，也不能与 E0 未固定分层 JIT 的历史数字直接计算收益。最终交付核验已确认 925 文件冻结不变，标准 Release 构建的 Runtime/测试程序集与测量输入逐字节一致。
- 测量观察：混合池 fork 在 100/4096 元素下均 **144 B**；4096 元素、约 1% 均匀值写入每次分配 **54,632 B**，整池参考为 **196,728 B**。三轮同项 P50 为 **3.48–4.04 μs** 对 **45.21–47.70 μs**，只包括池算法，不包含 World 验证/文档/完整固定步。直接池读取仍慢于连续整值数组；实际 World/Query 读取全为 0 B，但 P95 波动明显，包括个别退化样本，**未证明 ≤5% 退化门禁或 E5 完整性能通过**。
- 本片源码冻结 `out/verification/ecs-e3a/source-freeze-first.json` 共 925 文件，SHA256 `3DE0E5D5C46BC97BD2BCBB32957EA1184E68963F7E38479C00B4B18B82565351`。标准双配置 Build.bat -SkipTests 零编译警告，完整回归和最终交付核验通过；正式 E1-B EXE 不部署、不替换。
- Debug 第一轮完整 CTest **89/89，1065.54 秒**；Release 第一轮 **89/89，981.89 秒**。两配置详情确认 PagedStorage 10/10，100/4096 元素池 fork 均 144 B，以及原 Runtime 13/storage 14/identity 15/Query 17/候选 8/Gameplay 53/批次 26/Movement 38/UI 18/E0 fixtures 11 全通过。托管/原生冒烟、Python 45/inspect 和前后 925 文件冻结复核通过。顺序会话 78237 已终止 exit 0，测量会话 13169 已终止 exit 0，无待轮询任务。
- 构建/测试/补充日志 `out/ecs-e3a-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-first.log`；CTest 完整输出 `out/ecs-e3a-ctest-{debug,release}-details-first.log`。Debug 候选 `724ea17c9c5b4877855b4ea18bf43f88`，Release 候选 `e9457c62902544deb8dfd8902d814fdb`。两配置各五包 Editor/Null/DX11/Null-Physics/DX11-Physics 清单分别 144/56/113/67/116 文件，身份/路径/size/SHA256/闭包通过；原有有界 Editor 日志白名单不变。
- `out/ecs-e3a-delivery-verification-first.log` exit 0，SHA256 `A33862C13D0D6A4E52ED2E931AEED970F456AC24A82E349072854AB58807F83A`：冻结/十包通过，正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 的 144 清单/148 实际/148 备份保持完整，E0 原始 18 个程序集保持原 SHA。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未变。
- 测量核验 first 失败：Windows PowerShell 5 的 ConvertFrom-Json 返回数组被额外 `@(...)` 包成单项，核验脚本误报闭包数量。仅修正 out 内辅助脚本的数组接收方式，不修改数据、产品、断言或预算。保留 first 失败日志；`out/ecs-e3a-measurement-verification-second.log` exit 0，SHA256 `26026528ADEBAD1BF1617A9BE8E710DB1D9845387182BEC6668262A47BD4DEC9`，确认原 E2-B 算法参考一致、归档 8 文件不变、标准 Release DLL 与测量输入一致、三轮池各 90 × 100 原始样本和旧/新公开读取各 6 × 300 样本及环境/实际加载 DLL SHA 一致。
- 仅 E3-A 自动候选门禁完成，不代表整个 E3/ECS 完成。没有新的无 Skip 部署、三轮历史 profile 或 E5 整帧性能验收；人工/目标机/自包含/完整性能/1h/历史故障调查仍开放。下一片 E3-B 增量精确验证；四项 IDE 设置与 out 产物/证据不提交。

### E3-B 增量精确验证（自动候选完成）

E3-A 已核对本地/GitHub main 为 `3d0a1fb329fbdae77bc5729a9878c9218e28e0e4`。此片不移除持久化、Undo 或完整快照格式；目标是让固定步预检使用同一 World 的受检候选和精确预算，不依赖整份 JSON 往返。

- World 派生编码缓存按槽/代次保存对象与组件编码、精确字节数，不成为第二组件存储。写集追踪对象及具体组件；普通值写只重新编码被改组件，改名复用组件编码。编码器实际输出的单项长度加原封套/逗号计算完整 4 MiB 预算，算术为 checked；不估算 Unicode、转义、数字或组件类型名长度。编码组件仍受 64 KiB 边界和注册 schema 检查；原始/归一化输入、结构批次 1 MiB 和聚合预算继续检查。
- 缓存资格由引擎保守判定：纯值字段/自动属性及递归稳定类型。属性不只看 CompilerGenerated 标签，还检查 getter 是否严格读取自己的一个实例字段；JSON 属性/自定义转换器/未知访问器走有界冷路径。可信值验证器仍在每次写入执行，没有新增可由 Agent 输入的“跳过验证”选项。动态编码结果每次预检重新测量并检查 payload/schema，不因没有 World 写入而跳过。
- 缓存仅随最终 World 提交安装；编辑直写和 Restore 的 revision/identity 变化会失效。候选回滚、分支丢弃、跨槽复用不污染已提交缓存。准备动作包含 attempt/变更序号/候选根检查；捕获编码/检查异常后仍毒化整步。结构分支还保留自己的脏集及已准备编码版本，避免丢弃分支后复用错误缓存。
- 内部 PreparedStepView 只提供受检元数据、完整 Entity 存活检查和字节计数；没有底层数组、可写 ref 或新权限。线程、写入、撤销、提交、替换及枚举恢复均检查有效性。需要旧 DTO 的现役消费者显式取得新数组壳，不能改动缓存。信号过滤已按候选完整 Entity 检查，不再先构造 WorldSnapshot 的 UUID 集。
- SceneDocument 的运行时预检已移除整文档 Encode/Decode；对象/文档封套和 Behaviour 编码按内容复用，组件数组长度来自同一受检候选。所有跨对象绑定 UUID、Export 类型/有限值/容量仍每次验证；缓存持有私有数组副本。双精度比较使用位模式，不能把 JSON 长度不同的 +0/-0 当作相同缓存项。元数据安装仍在唯一 World 提交后且校验目标 identity/revision，不引入第二事务或编辑历史。
- 现役 Play/结构批次已改用候选视图。**E3-C 尚未完成**：Commands.Begin 仍读取整文档；实例、回执和插值仍共用每步一个最终 DTO；World 元数据复制和信号/绑定遍历等成本仍在。本片测试不宣称完整固定步无全量文档或 E5 性能通过。
- 新增 Runtime 18、Scene 7、真实 Play 3 组验证；包括编码器实际字节 oracle、Unicode/转义、精确世界/文档边界、归一化超预算、组件级脏写、动态转换器/伪造属性标记、冷路径 schema、过期/跨线程视图、丢弃分支、缓存别名/回滚、负零、重复绑定和真实 Play 失败。原 Runtime 13/storage 14/identity 15/Query 17/结构候选 8/Paged 10、Scene 36、Gameplay 53/结构批次 26/Movement 38 fake/UI 18/E0 fixtures 11 保留。
- 定向 Debug/Release 已通过：`out/ecs-e3b-{runtime,scene,gameplay}-{debug,release}-tenth.log`，新增真实 Play 的最终定向结果为 `out/ecs-e3b-gameplay-{debug,release}-eleventh.log`。原始 second 的测试静态字段未初始化编译警告、sixth 的迁移调用点未改完编译失败、eighth 的枚举器最后一项恢复漏检失败均保留；修复产品失效检查后通过，不降低断言。早期 SSH 沙箱只读访问失败后，经正常授权核验精确远端 SHA 成功，没有改安全设置。
- 完整门禁冻结 `out/verification/ecs-e3b/source-freeze-first.json` 共 931 文件，SHA256 `7F9CBF58AA36E298DB0432466DFC0AB094BE0F60A08015FBD258594AF3216D42`。顺序 canonical Build.bat -SkipTests、全 89 CTest、smoke/Python45/inspect 进行中；会话 81334，不启动重复构建。完整门禁/最终十包与正式安装/备份/E0 核验未完成前，不提交推送或开始 E3-C。正式 E1-B EXE 不变。
- 深度专项复查追加：字节长度可以分项相加，但 JSON 最大嵌套不能直接沿用单项限制。World/Scene 在组件 Data 之前增加五层容器，因此候选编码显式保留 `MaxDepth - 5` 的 Data 嵌套限制；不依赖当前 schema 检查恰好更严格。新增第 19 组运行时测试，用深度 1/26/27/28/31 与完整 serializer 的接受/拒绝结果对照。第一份冻结因此作废，原 Debug 二进制仍按原会话完成，不并行改其程序集；第一轮完整结果不作为修复后的验收。结束后另建 second 冻结和两配置全回归，保留 first 全部证据。
- 第一轮 Debug 二进制 **89/89，1102.43 秒**，smoke/Python45/inspect 通过；由于上述源码补充，门禁在回归后的冻结复核处按预期拒绝，81334 已结束 exit 1，未启动第一轮 Release。不能把该轮当作最终同源码通过。补充测试实际覆盖对象和数组嵌套，并与完整编码后的严格 32 层 JSON 读取共同对照；两配置 Runtime 19/19（thirteenth）、Scene 7/7 与 Gameplay 3/3（twelfth）通过。
- 第二份冻结 `out/verification/ecs-e3b/source-freeze-second.json` 931 文件，SHA256 `89FE13F9D9AA66DB81F56C0B5A8A7082B40CE7452B3CE502782E21EBFE622C91`；第二轮完整门禁会话 32308 正在执行。日志后缀为 second，检查脚本 `out/verification/ecs-e3b/verify-delivery-second.ps1`。完整两配置及最终核验完成前，E3-B 仍未交付，不进入 E3-C。

### E3-B 最终验证与交付记录

- 上述进行中记录为过程历史，以此处最终结果为准。第二份 931 文件冻结前后完全一致；两配置标准 Build.bat -SkipTests 均零编译警告，Debug 第二轮 **89/89，1076.11 秒**，Release 第二轮 **89/89，982.44 秒**。顺序会话 32308 已结束 exit 0；旧 81334 已结束 exit 1（源码补充导致冻结拒绝），不再轮询或重启。
- 两配置新增 Runtime **19/19**、Scene **7/7**、真实 Play **3/3**，原 Runtime 13/storage 14/identity 15/Query 17/结构候选 8/Paged 10、Scene 36、Gameplay 53/结构批次 26/Movement 38 fake/UI 18/E0 fixtures 11 全通过；托管/原生冒烟、Python **45**、inspect 全通过。完整日志 `out/ecs-e3b-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-second.log`，详细用例输出 `out/ecs-e3b-ctest-{debug,release}-details-second.log`。
- Debug 候选 `caa4790ddf74492abf53a5d2bbdcb983`；Release 候选 `25eaacb8544d42a6808861d51701293c`。两配置各五包 Editor/Null/DX11/Null-Physics/DX11-Physics 的清单分别 144/56/113/67/116 文件，产品身份、路径、size/SHA256 和闭包检查通过。未改变既有有界 Editor 日志白名单。
- 最终 `out/ecs-e3b-delivery-verification-second.log` exit 0，SHA256 `C7CFA27066C323B212AA65FF72EC2C80B7F6BF0574181A3305DBD7F2FAA04945`：冻结及十包通过；正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 保持 144 清单/148 实际文件、148 备份完整，E0 原始 18 个程序集逐一匹配。另核对 E3-A 归档测量 8 文件原始 SHA 未变。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未改变。
- 本轮只是 E3-B 自动候选，不部署、不执行新的无 Skip/三轮历史 profile 或 E5 整帧配对性能门禁。C# 候选视图、编码计数和完整 codec oracle 是正确性/路径证据，不是完整性能验收。保留所有早期失败、两份冻结、首轮 89/89 但冻结失效的记录、历史 File.Replace 调查和四项 IDE 改动。下一步 E3-C，不跳到 M8.3；人工/目标素材与目标机/自包含/完整性能/1h 仍开放。
