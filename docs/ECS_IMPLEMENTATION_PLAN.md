# ECS 重构实施合同（E0–E5）

2026-10-16。用户已批准按方案实施。顺序：M8.2 自动候选交付 → 本重构 → M8.3 及后续 → M9 Windows。
本文件规定目标和门禁；各项是否实现以第 7、8、9 节的状态和证据为准。M8.2 基点为 `51010c33755ab35ac5fa3f45bc297c074ea3addb`。

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

### E3-C 高层提交（自动候选完成）

E3-B 本地及 GitHub main 精确核对为 `a430a2ab43daafa6d5c4e7db1af5e9402071205a` 后开始。此片移除运行步的完整文档/快照消费者，不删除现役作者编辑、Undo、持久化、Start/Reload 冷路径的完整格式。

| 消费者 | E3-C 路径 | 保留约束与成本 |
| --- | --- | --- |
| Commands.Begin | 有版本元数据副本；成功步复用根、绑定写入时 fork | 文档/World 变化失效；单命令和批次共享 1024/1 MiB |
| Scene 元数据准备 | 逐对象精确预算检查；未改根复用 | 全局绑定 UUID/Export 验证仍遍历；失败不安装 |
| Behaviour 实例 | 稳定根复用；变化时以类型和完整 Entity 匹配 | UUID/绑定排序、延迟生命周期、退休清理；仍检查存活 |
| 成功回执 | 候选 UUID 解析为完整 Entity | 仅提交后可读；旧代次/世界、删除和淘汰拒绝 |
| 插值 | 按值读取候选 TransformData | 仍遍历 Transform、准备输出；没有 GPU/整帧性能结论 |
| 作者编辑/Undo/持久化/Start/Reload | 原现役完整快照冷路径 | 不是旧格式兼容层，不删除也不伪称已增量化 |

- Commands.Begin 只在新 lineage、文档 revision 或 World identity 改变时复制私有 Behaviour/Export 元数据；成功步保留自己的元数据根。单条结构命令只在第一次修改绑定时复制字典，数组始终替换；批次仍是原子的候选分支，纯改名/组件批次不复制绑定根。失败候选不安装到已提交缓存；其他受信运行生产者删除对象后，提交准备过滤已失效的绑定 UUID。
- SceneDocument 保留原有全局绑定 UUID/Export 和精确文档预算验证；元数据相同时复用私有字典根，改名、删除、创建或绑定变化时才准备变化后的根。没有共享可写数组给公开 API、Behaviour 工厂或 Agent；工厂得到自己的 Export 数组副本。
- 内部受检候选视图增加 UUID → 完整 Entity 解析和按值强类型组件读取；提交前公共 World.Get 仍读旧值。视图仍由 attempt/mutation/root/线程检查保护，写入、丢弃分支、撤销、提交、替换后失效；没有可写 ref 或新公开权限。
- PlaySession 不再在固定步开始 CaptureSnapshot，也不再在最终预检 CopySnapshot。稳定绑定/实体状态直接保留实例数组；结构变化按 UUID/绑定 ID 原有顺序准备，类型与完整 Entity 相同才复用实例。检查发现原分支在相同绑定 UUID 改类型时会提前移除旧实例、漏掉清理；现在保留其退休记录，下一边界恰好 Disable/Destroy 一次。新实例的 Create/Enable 仍在下一步执行，失败工厂不发布任何高层状态。
- 回执通过受检候选解析存活 Entity，保留原提交后可见性、容量及世界身份检查；插值直接读取候选 TransformData，不解码 JSON。新对象、无 Transform、删除、暂停和旧值插值规则不变。初始化/Reload 插值也使用强类型候选；两者作者快照仍是明确冷路径。
- 无写/稀疏步骤的测试收紧为**没有最终 validation snapshot 复制**（原 E3-B 测试明确允许每步一个），并检查实例和绑定根复用。两配置定向 Runtime 增量 22/22、Scene 9/9、真实 Play 3/3、新高层提交 10/10，原 Gameplay 53/结构批次 26/Movement 38 fake/UI 18/E0 fixtures 11 全通过。新增覆盖完整身份/值副本/分支与线程失效、绑定复制别名、作者编辑/Restore 失效、启停及类型替换生命周期、工厂修改 Export/失败回滚、插值和确定性工厂顺序。
- 首次 Runtime 定向编译失败为测试误写 struct 返回值的 Position.X，改成完整值替换后通过，不改产品约束或断言。证据 `out/ecs-e3c-runtime-debug-first.log` 保留；最终定向日志 Runtime Debug second、Scene Debug first、Gameplay Debug second，Release 三项均 first。首次 Gameplay Debug 仅含旧及增量 Play 三组，新增十组以 second 为准。
- 932 文件冻结 `out/verification/ecs-e3c/source-freeze-first.json`，SHA256 `7BE7DAD2383F4D5510C7DC974D58A4A792A43B97BC1E1BE3B8E9B94355A3DEB1`。顺序标准 Build.bat -SkipTests、两配置全部 89 CTest、smoke/Python45/inspect 会话 65547 正在执行；不能提前标记全门禁通过或提交。正式 E1-B 安装/备份和 E0 原始基线待最终核验，不部署。
- 仍有 World 元数据复制、全局绑定身份验证、实例存活检查、Transform 遍历/插值数组与回执处理成本；移除整文档不是零分配承诺或整帧性能结论。E3-D 联合验收、E4 领域迁移、E5 性能与既有人工/目标机/自包含/1h 门禁保留。
- 第一轮期间的等价性复查发现：作者用 CopyBindings 会排序 Export，不能用于保留已恢复文档顺序的运行时复制。修正为保序数组副本，增加完整文档字节 oracle 和真实 Play 无写步骤测试。另对旧 JSON copy 的异常 UTF-16 规范化保留**绑定元数据专用**有界冷路径；规范化后的重复 Export 名仍拒绝。正常绑定保留纯数组复制/跨步复用，不重新序列化组件；新提交的非规范字符串仍按原语义在下一 Begin 规范化，不提前更改当前 factory 输入。新增 Scene 2 组/真实提交 2 组尚待重新构建。源码已与 first 冻结不同；65547 原 Debug 结束后应在冻结核验处拒绝、不执行 first Release。该轮及 first 冻结全部保留，修正后须重新跑 second 同源码完整双配置。
- 第一轮已结束：Debug **89/89，1082.12 秒**，补充 smoke/Python45/inspect 通过；随后源码冻结检查按预期拒绝，会话 65547 exit 1，未运行 first Release，不作为最终验收。最终修正源码重新完成两配置定向 Runtime 22/Scene 11/真实 Play 3/提交路径 12 组及原用例，日志 `out/ecs-e3c-{runtime,scene,gameplay}-{debug,release}-final-first.log`；定向会话 81486 exit 0。第二份 932 文件冻结 SHA256 `4DCCBEA037329B171672F659E3008E86BA27480E53F44911C34CA66160A27FC9`，路径 `out/verification/ecs-e3c/source-freeze-second.json`。第二轮完整两配置门禁开始；正式入口不变，全部首轮/定向失败证据保留。
- 第二轮 Debug **89/89，1079.76 秒**，smoke/Python45/inspect 及前后同 932 文件冻结复核通过；候选 `b7e40b7191314de7bc148e6e9f708b1a`，正式入口不变。顺序会话 14376 继续执行 Release 标准构建与完整回归，不启动重复任务。尚未完成双配置最终验收和交付核验，不提交。

### E3-C 最终验证与交付记录（2026-10-19）

上述进行中段落为过程历史，以本段为最终状态。E3-B 基点已核对本地/远端 `a430a2ab43daafa6d5c4e7db1af5e9402071205a`；E3-C 不代表整个 E3/ECS 完成。

- 同一第二份 **932 文件**源码冻结在双配置前后保持一致，SHA256 `4DCCBEA037329B171672F659E3008E86BA27480E53F44911C34CA66160A27FC9`。标准 Build.bat -SkipTests 两配置零编译警告；Debug 第二轮 **89/89，1079.76 秒**，Release 第二轮 **89/89，968.99 秒**。顺序会话 **14376 exit 0**，全部构建和测试结束，不再轮询。
- 两配置增量 Runtime **22/22**、Scene **11/11**、真实 Play **3/3**、高层提交 **12/12**；原 Runtime 13/storage 14/identity 15/Query 17/结构候选 8/Paged 10、Scene 36、Gameplay 53/结构批次 26/Movement 38 fake/UI 18/E0 fixtures 11 保留并通过。托管/原生 smoke、Python **45**、inspect 全通过；没有以 fake Movement 代替新的真实 Jolt 角色验收。
- 完整日志 `out/ecs-e3c-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-second.log`；详细输出 `out/ecs-e3c-ctest-{debug,release}-details-second.log`。Debug 候选 `b7e40b7191314de7bc148e6e9f708b1a`，Release 候选 `748552c447514c89a50b86b7ddae835c`；两配置各五包 Editor/Null/DX11/Null-Physics/DX11-Physics 清单 **144/56/113/67/116** 文件，身份、路径、size/SHA256 与完整闭包核验通过。有界 Editor 日志白名单未修改。
- 最终 `out/ecs-e3c-delivery-verification-second.log` exit 0，SHA256 `0FC5849505CA90D9896C64E48B4C4065907CC141309F66170A508D596AB6D59D`：源码冻结和十包通过；正式 E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 的 **144 清单/148 实际/148 备份**完整；E0 原始 **18** 个归档程序集、E3-A 原始 **8** 个测量输入与 **9** 份测量日志均逐一匹配。正式 EXE SHA256 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61` 未变。
- 第一轮 Debug 89/89 及补充通过，但追加保序/Unicode 修正使冻结失效；65547 已结束 exit 1，**不算最终验收**，没有 first Release。首次定向测试 struct 返回值赋值编译失败、两份冻结及全部日志保留。最终定向会话 81486 已结束 exit 0。四项 IDE 改动、out 候选/失败/备份不提交。
- 本轮只完成 E3-C 自动候选；无新无 Skip 部署、三轮历史 profile 或 E5 整帧配对性能验收。保留所有人工/目标素材与目标机/自包含/完整性能/1h/历史 File.Replace 调查门禁。按既定门禁提交推送并核对远端后，下一片为 **E3-D 联合验收**，其后 E4/E5，通过后才恢复 M8.3，最后 M9 Windows。

### E3-D 联合验收（进行中，2026-10-19）

E3-C 本地和 GitHub main 已核对为 `28aecf523c6dfc6b2634549f7ab4cd67cf592dc6`。本片同时检验 A–C 的路径不变量、真实产品领域回归和同驱动旧/新工作负载，不能只用局部池测试或冒烟代替阶段验收。

- 新增 `EcsJointCases`：100/1000/4096 对象 × 无写、稀疏写、全写、组件成员变化、1% 创建替换，共 15 组。实际 PlaySession 预热后四步检查完整 World 捕获、validation snapshot 复制、SceneDocument 捕获计数均不增加；无写/稀疏/全写检查精确组件编码增量。公共读取保持已提交视图，查询/最终值/暂停插值与完整 codec 冷路径对照一致。计数器私有，不构成新的公开写权限或性能 API。
- Debug/Release 定向 second 日志均通过联合 15/15、原 Gameplay 53/53、增量 Play 3/3、高层提交 12/12，以及结构批次、Movement fake、UI 和 E0 fixtures。证据 `out/ecs-e3d-gameplay-{debug,release}-second.log`。这是定向验证，不是完整双配置门禁；真实物理/动画/渲染/Editor/Player 组合仍须完整 CTest。
- `Measure-EcsPaired.ps1` 用同一个新测试驱动分别加载 E2-B 已交付 Release 与当前候选六个真实产品程序集；每侧闭包为六 DLL 加驱动 DLL/deps/runtimeconfig 九文件。校验原包身份、清单和复制 SHA/size、实际已加载程序集路径/哈希，并在工作负载外用完整场景编码摘要比较最终语义。测量模式三轮交替顺序，各 33 项 × 100 样本，保留原始时间/线程分配/GC/分层记录；TieredCompilation=0 是明确记录的局部实验条件，不是产品默认设置。
- 原 E0 工作负载计时区、12 次预热和 100 次采样不变；只增加计时外语义回调。初始化/Start/Reload/Undo 仍是冷路径，不宣称完全增量化。CPU 工作负载不代表 GPU、物理、推理、整个引擎帧或 E5 验收。
- 配对 smoke third 已完成：`out/verification/ecs-e3d/537c74946bda4ad78b7300dec6e5fa59`，旧 E2-B 与已交付 E3-C 各 11 项语义相等。**此处 E3-C 只用于证明驱动兼容，不是最终 E3-D 候选或正式测量。** 最终需标准 Release 构建后新归档并测量。
- 输入负例 second **11/11** 正确拒绝，原输入不变：`negative-8a6e4ba3ebd24042b872b32a9a05f4ef`，包括缺失/损坏 DLL、哈希/大小、重复/越界名、schema/variant、额外文件、坏 JSON 和多模式参数。失败前不能运行工作负载。
- 负例 first 保留：PowerShell 单项参数被展开成字符，缺失程序集在旧顶层入口 JIT 时抛出未捕获异常；修正为显式 string[] 参数与独立 BCL 入口捕获、被调用入口 NoInlining。原会话 79529 仍有三个已核对的辅助进程 17524/12936/7156，常规停止 17524 报错后未升级终止。已请求用户许可；未获答复，不强杀、不删除失败副本。正式测量须等这些进程结束后再进行，不能隐藏环境污染。
- 下一门禁：冻结最终源码 → 标准 Debug/Release Build.bat -SkipTests（不部署）及全部 89 CTest、smoke/Python45/inspect → 同驱动真实配对测量与摘要分析 → 同源码/十包/正式安装/备份/E0 与 E3-A 原始证据核验 → 文档与受限提交推送。任一失败保留证据、修正后重跑受影响门禁；目前不得标记 E3-D/E3/ECS 完成。

#### E3-D 全域回归范围与证据边界

已核对 CMake 的现役测试注册：下列领域包含在完整 89 项中，不能用 Gameplay 定向结果替代。表格是门禁映射，不表示正在执行的本轮测试已经通过。

| 领域 | 现役测试入口 | 本片依赖的证据；不能推导的结论 |
| --- | --- | --- |
| 唯一存储、查询、结构事务、回执 | NcmaManagedHeadlessTests、NcmaGameplayTests、NcmaSceneDocumentTests | E0–E3 断言及新增 15 组；不代表整帧性能 |
| 真实原生数值边界 | NcmaPhysicsNativeTests、NcmaCharacterNativeTests、NcmaPhysicsTests、NcmaPoseManagedTests | 原生插件/托管消费仍兼容；Gameplay 的 38 组 fake Movement 不代替这些入口 |
| 场景、动画、DX11、2D/UI | NcmaSceneRenderingTests、NcmaRenderingTests、SceneUiJointProducts、ShadowJointProducts、UiCostJoint、SceneCostJoint 四档 | 场景与真实渲染/输出组合、原有精度与资源约束；不新增 Vulkan/OpenGL 实现声明 |
| 世界替换与恢复 | GraphicsRecoveryJoint 四档、EditorGraphicsRecovery、PlayGraphicsRecovery、PlayerGraphicsRecovery、EditorRecoveryLoop | 完整 Entity 在世界恢复后的失效与现役恢复路径；不是 1h 稳定性验收 |
| 编辑/历史/资产/独立 Player | NcmaEditorCoreTests、NcmaEditorServiceTests、NcmaAssetTests、NcmaAssetImportTests、NcmaPlayerTests、NcmaEditorDeploymentTests | 原持久化、Undo、资产与包约束；不是手动 UI 或干净目标机验收 |
| 可选 Python 与传输隔离 | NcmaModuleTests、NoPythonProducts、Grpc、RelocatedClient、NcmaEditorTransportTests，另 Python45/inspect | 原可选模块与命令边界不退化；不是接入推理服务 |

静态冷/热路径复核：Gameplay 的完整捕获仅在组成生命周期注册、Start/Reload；MovementCoordinator 仅在构造和 `ICoupledStepParticipant.Start` 核对启动文档；RuntimeSessionOwner 的捕获属于脚本候选 Preflight（加载/重载）；SceneAnimatorRuntime 的捕获属于 Compose。它们不能被计入固定步收益，也不能以删除启动校验来改善数据。当前没有在这些模块的现役固定步发现完整捕获调用；该搜索是补充证据，动态 15 组计数检查和完整领域回归仍不可省略。

本轮第一份冻结 937 文件，SHA256 `498B12B4F35F5EF36255B6A75C2009E0260940B73FE64308432E834AC5972F3B`。Debug 标准 SkipTests 构建已通过，候选 `0397270b82dd4c0786267ea94b81c214`；第一轮完整 **89/89，1101.03 秒**，补充原生/托管 smoke、Python45/inspect 及前后源码冻结核验均通过。详细日志含 Runtime 增量22/Scene增量11/提交12/新增联合15组及原有领域断言。会话 11250 已进入 Release 标准构建，尚无双配置最终验收、最终配对测量或交付核验；不得以 Debug 单配置通过替代 E3-D 完成。

#### E3-D 双配置回归及包核验结果（配对测量待完成）

上述进行中记录由本段更新：会话 **11250 已结束 exit 0**。同一 937 文件冻结在完整 Debug/Release 构建、回归前后均一致；两配置标准 SkipTests 构建零编译警告，Debug **89/89，1101.03 秒**，Release **89/89，978.60 秒**，两边原生/托管 smoke、Python **45**、inspect 全通过。Runtime22/Scene11/提交12/新增联合15组与全部旧领域测试保留通过，不把 fake Movement 代替真实数值插件或将 CPU 合成检查冒称完整性能验收。

- Debug 候选 `0397270b82dd4c0786267ea94b81c214`，Release 候选 `881d1a0599444fe488d8ee47dbe1378e`；日志 `out/ecs-e3d-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-first.log`，详细测试输出另存 `ctest-{debug,release}-details-first.log`。
- 最终回归/闭包核验 `out/ecs-e3d-delivery-verification-second.log` exit 0，SHA256 `33796E7024A6B9DC0E238B2088715C850F35F7463021F6923A5907372228D2E2`。十包清单各配置144/56/113/67/116项通过；正式 E1-B 的144清单/148实际文件、148备份，E0原始18个程序集，E3-A原始8输入与9日志均保持原SHA/size。正式 EXE 仍为 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`，未部署。
- 交付核验 first 失败来自辅助脚本机械替换后误指向 E3-A 的 first 失败日志，旧正确 second 成功日志 SHA `26026528ADEBAD1BF1617A9BE8E710DB1D9845387182BEC6668262A47BD4DEC9` 实际不变。保留首轮日志与辅助脚本；second 仅修正历史证据路径，不修改产品、断言或历史文件。
- **本轮标准 Release** 新配对冒烟归档 `out/verification/ecs-e3d/11b670bfc7424fd0845c57bc8ca08153`：E2-B/最终候选各11项语义一致。对该新归档的负例11/11通过，目录 `negative-3956fc6f210a40c8878b614874ddb3e1`；日志 `out/ecs-e3d-canonical-paired-{smoke,negative}-first.log`。它仍是冒烟，不是33项×100样本×三轮的正式测量。
- 唯一当前待处理步骤：旧负例三个精确辅助进程仍存活，常规停止失败后的终止许可尚未收到。未杀进程、未删除失败文件。正式三轮配对测量须等待处置完成，之后复核新旧输入/原始样本/语义与全部分位数、分配和GC数据，再完成E3-D文档、受限提交推送。当前 **E3-D/E3仍未完成**；不跳到E4/M8.3，不把本轮回归通过等同E5性能/人工/目标机/自包含/1h验收。

用户随后明确批准仅终止三个已核对辅助进程。执行时17524/12936已自行退出，7156的完整命令行、镜像、父进程、精确创建时间及持有句柄的StartTime均匹配；仅对7156执行一次Kill，WaitForExit返回成功。原辅助脚本最后以CIM枚举记录是否消失为断言，遇到已退出记录残留而报错；此失败保留，没有重试更强终止。后续两次只读实际进程枚举均确认三者不存在，旧会话79529已结束exit0（不据此宣称旧负例测试成功）。证据 `out/ecs-e3d-approved-helper-close-first.log` 与 `out/ecs-e3d-approved-helper-exit-first.log`；所有文件保留。上述处置阻塞已解除，937源码冻结再次匹配，正式三轮配对会话88153开始；测量完成前仍不标记E3-D完成。

#### E3-D 三轮配对完成：发现全写回退，暂不收尾

会话 **88153 已结束 exit 0**。最终 Release 候选与原 E2-B 使用同一驱动、TieredCompilation=0，顺序为旧/新、新/旧、旧/新。归档 `out/verification/ecs-e3d/1977744a247f47769370bcb095f87dc2` 包含18份输入、6份运行日志和输入/完成清单；每轮每侧33工作负载，每项100原始时间及分配样本，共 **19,800 个时间样本和19,800个分配样本**。三轮各33个最终场景摘要完全相等，程序集实际加载路径/哈希匹配，输入前后未改变。

独立复核 `out/ecs-e3d-paired-verification-first.log` exit0，SHA256 `D41DE712DBACC312D6645FB1F3BD77B2772935B92B0171931FD0225B9661F0A3`，逐一验证九文件闭包、6轮日志、33个工作负载键、预热12/样本100、读取16遍归一化、原始数值和nearest-rank分位数。测量后的 `out/ecs-e3d-delivery-verification-third.log` 再次通过，SHA仍为 `33796E7024A6B9DC0E238B2088715C850F35F7463021F6923A5907372228D2E2`；同937源码、十包、正式安装/备份及原始基线均不变。

以下为 **4096对象、三轮各自P50的最小–最大范围**，单位毫秒；不是置信区间，也不是GPU/整帧/FPS结论。P95比值为同轮候选/旧版。

| 工作负载 | 旧版 P50 ms | 候选 P50 ms | 候选/旧版 P95 |
| --- | --- | --- | --- |
| World 无写 | 36.176–37.611 | 0.048–0.058 | 约0.002 |
| World 约1%写 | 36.512–37.000 | 1.490–1.574 | 0.054–0.060 |
| **World 全写** | **55.773–56.938** | **129.144–132.864** | **2.198–2.294** |
| Play 无写 | 230.946–231.404 | 1.512–1.632 | 0.009–0.010 |
| Play 约1%写 | 229.475–233.313 | 3.147–3.313 | 0.019–0.020 |
| Play 全写 | 251.254–252.211 | 133.113–136.074 | 0.504–0.567 |
| 每对象 Behaviour 全写 | 290.381–293.652 | 135.143–138.373 | 0.493–0.514 |
| Play 约1%对象替换 | 233.014–235.156 | 5.839–5.910 | 0.030–0.032 |

- 无写/稀疏/结构变化与高层Play的全量文档消除收益明显，但**不能只报收益**：直接World全写在100/1000/4096均回退，三轮P95比值总范围1.994–2.534；4096每步线程分配从20,152,776–20,153,088 B升为34,214,440 B。其100样本观察的Gen0回收154–155次变为232次；Gen1/Gen2减少不代表该路径成本改善。当前不得将E3-D/E3标为通过，不提交推送、不进入E4。
- Play无写4096线程分配从约77.30 MB降至2,219,048 B，稀疏写降至2,959,872 B；它们仍不是零分配。Play全写降至36,136,784 B，但不能掩盖其底层World全写回退。全部三轮原始分配/GC/分层统计保留在复核报告。
- `read_transform*` 负载有意沿用E0的 `GetEntities()` 复制枚举而非E2-A强类型Query；每遍5712/52512/213504 B在两版本相同。其P95比值存在跨轮波动（最高约1.511），不能用它宣称新Query退化或5%小场景门禁通过；E2-A的预热强类型Query零分配属于另一明确边界。
- 初步代码审查发现待验证热点：每次 `Set` 的Validate已序列化检查payload；提交期脏组件还要有界编码/解析/Clone、schema/depth校验与组件计量，并对脏对象重新计量不变头部。E2-B池Encode是直接SerializeToElement，当前为有界输出后Parse/Clone。**这只是候选原因，不是已证明根因**；不能直接移除64KiB/4MiB/depth/schema/归一化、每次可信验证器或unknown getter/converter保护来取悦基准。
- 下一片保持在E3-D：对上述成本做有界定向分项，优先消除稳定值重复编码和不变头部的重复计量；确保批次分支、重复写、删除复用、abort/restore、未知编码器及失败毒化语义不变，再用新冻结重跑定向、完整双配置和同驱动三轮配对。当前937版本及此次退化数据作为不可覆盖的对照保留；修复前不重复提交或部署。

#### E3-D 全写回退修复候选（定向通过，完整门禁尚未启动）

2026-10-19按用户“开始下一步”继续在E3-D修复，不进入E4。

- 先对原标准Release `881d1a0599444fe488d8ee47dbe1378e` 的真实Runtime DLL做隔离分项诊断，三轮各20,000调用：递归Schema校验约12.9–13.8微秒/3032 B，写入验证约4.4–4.9微秒/1280 B，提交期有界编码/Parse/Clone约4.7–4.8微秒/1768 B，组件计量约2.0–2.6微秒/184 B，对象头计量约0.46–0.54微秒/496 B。最大热点是反复解析Schema及其字段字符串/集合分配，重复序列化是另一项可消除成本。日志 `out/ecs-e3d-profile-first.log`；辅助源码在 `out/verification/ecs-e3d/profile-first`，记录实际程序集/MVID。这是隔离调用诊断，不是整步各项可精确相加的耗时占比。
- 新 `ComponentShape` 在可信注册时预编译既有闭合对象/标量/local-$defs检查。每次仍检查所有数据字段、重复/未知/必需字段、数值有限性和Int64范围；仅避免反复解析同一Schema。每对象最多32字段、展开最多4096节点、深度32；不支持、畸形、过大或循环Schema回到**未修改**的原递归检查，不添加新Schema方言，也不放宽原来对额外字段的拒绝。
- `World.Set` 原本已生成并验证的JSON，仅对现有IL证明稳定的值类型保留到当前候选步；可信验证器仍每次调用。缓存跟随候选分支复制/恢复/安装，包含代次，重复写取最后一次；组件增删使相应项失效，删除对象清理其项，abort全部丢弃。未知getter/converter仍走原有有界冷编码。Schema错误仍在准备提交时拒绝，没有提前到Set或跳过。独立组件值仍只在原强类型池中保存，不产生第二World或新写权限。
- 不变的Entry引用可复用原对象头精确字节数；重命名、替换、编辑/restore仍触发重新计量。组件64KiB、World4MiB、完整JSON深度、原始/归一化校验和失败毒化均保留。
- 新6组Schema等价测试包含1000个确定性随机payload，使用原未修改递归检查作为接受/拒绝oracle；另覆盖UTF8/转义名称、重复字段、嵌套引用、Int64/有限数边界、32字段末位和fallback。新增3组增量测试覆盖重复写与规范化、延迟Schema拒绝、分支丢弃/安装/abort、同槽不同代次和同代次移除再添加；原精确聚合边界、JSON深度、未知转换器与权限测试保留。
- Debug/Release定向均通过：Runtime13/storage14/identity15/Query17/结构候选8/Paged10/增量25/Schema6，Scene原36/增量11，Gameplay53/增量Play3/提交12/联合15/批次26/Movement38 fake/UI18/E0fixtures11。日志 `out/ecs-e3d-repair-{runtime,scene,gameplay}-{debug,release}-targeted-first.log`，会话99567已结束exit0，无编译警告。第一次缓存实现误将代次写为uint，CS1503保存在 `out/ecs-e3d-repair-runtime-release-second.log`，改为实际ulong后通过；没有放宽测试。
- 局部整步诊断每规模仅30样本：仅Schema优化的4096对象P50为80.8185ms，加入编码/头部复用后P50为50.2337ms、P95为66.7157ms、样本范围40.5535–70.3877ms，线程分配约14.081MB。日志 `out/ecs-e3d-profile-{second,third}.log`。早先进度消息44–48ms只引用最后两个样本，**不能作为完整分布或正式收益范围**。这些数据没有同轮旧版配对、不是最终标准候选，也未包含最后补充的删除缓存清理；不据此关闭原来的2倍回退或宣称E5通过。
- 完整门禁审批连续两次遇到服务容量错误：`Automatic approval review failed: Selected model is at capacity`；两次命令均**未执行**，不是产品失败或安全性否决。没有绕过审批降级执行；`source-freeze-repair-first.json` 和新gate日志均尚未生成。忽略目录中已准备 `freeze-source-repair-first.ps1`、`run-gates-repair-first.ps1`、`run-supplemental-repair-first.ps1`、`verify-delivery-repair-first.ps1`，等待审批恢复后按原流程执行。历史E3-A正确second测量证据路径保留，不作全局first/second替换。
- 下一步仍须新939源码冻结、标准双配置构建和全部89CTest/补充检查、最终标准候选配对冒烟与负例、三轮正式配对、十包/正式安装/备份/历史证据核验。全部通过前不提交推送、不部署、不进入E4；第一份937源码/全部退化样本、失败记录和四项IDE改动均保留。

#### E3-D 修复完整验证：首轮原子替换异常与同源码重测

用户要求“进行完整验证”后，审批成功；939文件冻结 `out/verification/ecs-e3d/source-freeze-repair-first.json`，SHA256 `47905BDD40B61E63ED8B89D5DF5D8EABC6E329D735C3C364DCB8442FCE288BFC`。第一轮Debug标准构建通过，候选 `4acbffd0e59d418699cdf696fcf8c0c7`；完整CTest **88/89，1089.95秒**，仅第75项部署回归失败，未开始Release。会话95930已结束exit1，日志与详细用例保存在 `out/ecs-e3d-*-debug-*-repair-first.log` / `out/ecs-e3d-ctest-debug-repair-first.log`，不能记作全通过。

- 失败位置：未修改的 `scripts/EditorDeployment.ps1:77` 的File.Replace，IOException “Unable to remove the file to be replaced”。目标为独立测试目录 `out/verification/deployment-tests/baa25fc617d54fc48a81ee91dc4e85c3/success`，不是正式 `out/bin`。部署自身回滚后journal为RolledBack；只读核验该测试bin的原始3文件与journal记录的SHA/size全部匹配，未对失败目录做删除、强制替换或恢复重试。
- 同一部署CTest在脚本正常新建的全新GUID目录中单独复测 **1/1，17.01秒**，会话18342 exit0。日志 `out/ecs-e3d-deployment-repair-isolated-first.log`，详细输出 `out/ecs-e3d-deployment-repair-isolated-details-first.log`。不修改源码、断言或原子写入策略，不将此次成功视作根因已解决；文件系统异常的根因仍未确证，历史调查保留。
- 源码冻结再次匹配；第二轮顺序完整门禁会话83142已启动，`run-gates-repair-second.ps1`重新执行标准Debug/Release构建、全部89CTest和补充检查，日志使用 `-repair-second`，原失败证据不覆盖。正式三轮配对及最终交付核验仍须等待本轮结束，不能由单项复测通过推导阶段完成。
- 另验证原937版本退化归档18输入/6测量日志及原始冻结、原配对复核报告SHA均未改变，记录 `out/ecs-e3d-regression-archive-verification-first.log`。当前构建目录的DLL正常更新，不把可变的构建产物当作历史测量输入；历史归档始终保留。

#### E3-D 修复完整验证最终结果：功能通过，尾部性能仍未通过

2026-10-19本次“进行完整验证”已执行完毕。**验证流程结束不等于阶段验收通过：E3-D/E3仍不收尾，不提交推送、不部署、不开始E4。** 本轮未修改已冻结的产品或测试源码。

- 同一 **939文件**冻结 SHA256 `47905BDD40B61E63ED8B89D5DF5D8EABC6E329D735C3C364DCB8442FCE288BFC` 前后匹配。标准Build.bat -SkipTests两配置零编译警告；Debug修复第二轮 **89/89，1124.47秒**，Release **89/89，1001.41秒**，两边托管/原生smoke、Python45、inspect通过。完整门禁会话83142已结束exit0，不再轮询。日志 `out/ecs-e3d-{build,ctest,supplemental,smoke,python,inspect}-{debug,release}-repair-second.log`，详细用例日志 `out/ecs-e3d-ctest-{debug,release}-details-repair-second.log`。
- Runtime增量25/Schema6、Scene增量11、增量Play3/提交12/联合15与原Runtime13/storage14/identity15/Query17/结构候选8/Paged10/Scene36/Gameplay53/批次26/Movement38 fake/UI18/E0fixtures11均通过。部署8组在完整Debug/Release中均通过（17.51/17.20秒）；首轮88/89和单独1/1记录不丢弃，原子替换异常根因仍未确证。
- 最终Debug候选 `585453061b454867a566ba192224dafe`，Release候选 `be823509e2ee4a84b3bd593897715dc4`。两配置各五包清单144/56/113/67/116文件；实际146/57/114/68/117，差额是清单和现役允许的Editor日志，既有白名单未更改。所有身份/路径/size/SHA及闭包核验通过。
- 正式E1-B Complete `7f3d3822dd39431b9c203e17194afb8c` 保持144清单/148实际文件、148备份完整；EXE SHA `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`未变。E0原始18程序集、E3-A原始8测量输入和9日志保持原哈希。测量前后交付核验 `out/ecs-e3d-delivery-verification-repair-{first,second}.log` 的SHA相同：`0E485981DF8803A0E4043FF0A2B5B02ECA3C5C4724E9A2A6344F79F33075C15F`。
- 最终标准候选配对冒烟归档 `4f380fec3daa417ca15ad02437644879`，旧/新11项语义一致；负例目录 `negative-33480195240148479d9affbf07887335` 的11项均正确拒绝且原输入不变。日志 `out/ecs-e3d-paired-{smoke,negative}-repair-first.log`。
- 三轮正式配对归档 `out/verification/ecs-e3d/ff40e2cf71514201943d7304632ed924`，旧/新、新/旧、旧/新交替执行，33项×100样本×6进程，共 **19,800个耗时样本和19,800个分配样本**。同驱动、真实产品程序集、TieredCompilation=0，三轮各33个完整场景摘要全部一致，18输入和实际加载九文件闭包前后哈希一致。独立分位数复核 `out/ecs-e3d-paired-verification-repair-first.log`，SHA256 `AD1D1576A153921469B619D879F8FCC94F67F9AD74416ED1B7688C193E87A2E9`。后续核验会话41850已结束exit0，无剩余构建/测试/测量进程会话。

**World全写重点结果**（各轮P50/P95的最小–最大值，ms；区间不是置信区间）：

| 对象数 | 旧版P50 | 修复候选P50 | 旧版P95 | 修复候选P95 | 同轮P95候选/旧版 |
| --- | --- | --- | --- | --- | --- |
| 100 | 1.2110–1.2203 | 0.9365–0.9547 | 1.8391–1.9065 | 1.4552–1.4800 | 0.774–0.805 |
| 1000 | 12.9827–13.3305 | 10.5785–11.3219 | 15.2221–17.7173 | 14.9121–16.0881 | 0.891–1.057 |
| **4096** | **55.5210–56.7901** | **52.9296–54.7862** | **62.5545–65.0129** | **70.6210–75.3443** | **1.102–1.159** |

4096全写三轮P95分别 **+12.895%、+10.202%、+15.891%**，三轮都回退，不能当作一个偶发样本忽略。每步线程分配从20,152,112–20,152,600 B降为14,080,720 B，约下降30%；100样本观察的Gen0/1/2计数也由153–156/119–122/64–67降至98–100/73–75/24–25，但回收次数少不证明停顿短，不能据此解释或关闭尾部回退。先前约2倍的严重回退已显著缩小，**不等于性能问题完全修复**。

其他4096工作负载仍有明显收益：World无写P95比0.00184–0.00275、约1%写0.0281–0.0291；Play无写约0.0093–0.0101、全写0.271–0.282、Behaviour全写约0.233、1%对象替换0.0238–0.0261。它们不能掩盖直接World全写的尾部问题。`read_transform*`继续沿用E0的GetEntities复制枚举而非强类型Query，分配不变；小场景P95波动最大约1.387，未证明5%门禁或整帧收益。

原937退化归档18输入、6日志及原冻结/配对证明再次保持不变，`out/ecs-e3d-regression-archive-verification-second.log` SHA `60C50262ED36425E019B2AD3D63FF9E253AAFBC4E269B8DA08F22C1CF12F3418`。全部失败目录、首轮日志、原始数据和四项IDE改动保留。

**验证结束时的原建议（被下述用户排期决定覆盖）**：先在E3-D定位密集写的回调、准备、安装耗时及瞬时分配/GC停顿/派生缓存存活期，再修复。尚未证明GC或某个缓存是根因；不得通过反复抽样挑选一次通过、放宽阈值、删除边界校验或退回完整快照来收尾。任何产品修改仍须新冻结、完整双配置和配对；人工/目标机/自包含/整帧性能/1h门禁保持开放。

#### E3-D 用户确认提交与性能债务延期

2026-10-19用户明确要求：**E3-D先提交，记录性能未达标状态，在后续阶段统一修复。** 本次作为“功能回归通过、性能未达标”的源码检查点提交，不代表E3/ECS完整验收通过；用户随后明确授权推送，并标记“功能验证通过、性能未达标”，不部署；本轮只调整交付状态与排期，产品/测试源码保持已验证的939文件冻结不变。

下一开发阶段可推进E4领域迁移；下列债务纳入E5统一性能修复与验收，在恢复M8.3前处理。该决定覆盖上文以及历史记录中“必须先修复才允许提交/进入E4”的排期限制，不覆盖测试、安全、权限或性能判定标准。

| 跟踪项 | 已知证据与状态 | 后续修复与关闭条件 |
| --- | --- | --- |
| ECS-PERF-01 密集全写尾延迟 | 未达标；4096对象三轮P95回退12.895%、10.202%、15.891%；约30%分配下降不能抵消尾延迟退化 | E5定向分析回调/准备/安装及GC停顿、缓存存活期；修复后新冻结、完整Debug/Release回归、同驱动三轮配对及原始分位数/语义/闭包核验，按既定性能门禁验收 |
| ECS-PERF-02 小场景读性能 | 未证明5%门禁；E0复制枚举样本P95最大比值约1.387，不是typed Query测量，也不直接断言为已确证退化 | E5控制测量噪声、区分复制枚举与typed Query，对100/1000/4096分别复核耗时、分配和GC；存在回退则修复，不挑样本或放宽标准 |

完整整帧性能、真实素材/目标机、自包含、人工和1h门禁继续独立开放。File.Replace首轮失败及根因未证实的调查保留。提交不覆盖原939修复与937退化归档、失败日志、正式安装或备份。

## 10. E4 子系统迁移：实施拆分与 E4-A 消费者审计

2026-10-19用户授权“开始E4阶段”。基线为已提交推送的 `6f4d666ab5686b15cafda29edf23d8facc5fb230`（E3-D：功能验证通过、性能未达标）。本片 **E4-A仅完成消费者审计、迁移合同与静态验证，不修改运行时代码，不宣称E4整体完成**。ECS-PERF-01/02继续由E5统一修复；不恢复历史“禁止进入E4”的排期限制，也不将延期视为性能通过。

### 10.1 E4实施顺序

| 分片 | 具体交付 | 放行条件 |
| --- | --- | --- |
| E4-A 消费者审计 | 可核对的调用清单；区分显示/启动/编辑/保存；明确排序、身份、缺失与冲突语义 | 清单与源码逐项一致；漏项/重复/行号/源码/分类负例拒绝；验证既有源码、候选包、正式安装与备份未变 |
| E4-B 角色与动画 | 角色/碰撞体、根运动、Animator启动查询；跟随相机与Player启动精确UUID查找；保留冻结成员与原生资源映射 | 创建顺序及既有UUID顺序分别等价；组件缺失仍拒绝；不存在的相机仍返回原有结果；恢复/换World/暂停/重启/故障/成员冻结联合回归 |
| E4-C 渲染提取与设置 | 复用按World身份绑定的查询；静态/蒙皮几何并集、灯光、设置读取；Editor/Player共同数据读取但保留各自错误处理 | 不漏诊断、不变绘制和FrameIndex顺序；独立阴影集合、资源代次、插值、2D隔离、DX11参考/API/恢复测试 |
| E4-D 场景、编辑与既有AI查询 | 对象列表/属性读取及导入影响列表；元数据成员观察；现有分页能力的有界读取 | 唯一EditSession/Undo；UUID、文档代次、revision、授权与大小限制不变；编辑撤销/重做、选择/换场景、陈旧请求与MCP权限负例 |
| E4-E 联合验证与残留核对 | Editor/Player/Headless跨领域组合；逐项复核已替代消费者与必须保留的冷路径 | 同一新冻结下完整Debug/Release、原生/托管smoke、Python/inspect、格式/ABI/权限/无Python产品/包与备份核验；不代替E5性能验收 |

每片先验证再提交推送。E4-B及后续凡修改产品/测试/构建脚本，必须建立新源码冻结，不能沿用E3-D的939文件验证结论。替代消费者就绪时删除对应旧实现，不引入兼容壳；显式复制API、编辑快照等现役合同不是“旧类型”，不一概删除。正式EXE仍为E1-B，本次不部署。

### 10.2 枚举调用清单（E4-A基线，非性能采样）

机器可核对清单：[ECS_E4_CONSUMER_AUDIT.json](ECS_E4_CONSUMER_AUDIT.json)。扫描 `managed` 下C#，排除 `bin`、`obj`、`*Tests`、`*SmokeTest` 和 `Ncma.Samples`；匹配 `GetEntities(` 共 **22行/19文件，其中1行是API定义，21处是调用**。该数字是词法入口盘点，不是完整调用图或热点耗时证明。当前生产扫描中 `CreateQuery(`/`.Query<` 只出现在Runtime的查询定义/转发实现，不能把E2查询基础已完成等同于领域消费者已迁移。

下列路径均相对 `managed/`；精确基线行号和原文见JSON，后续迁移不要为保持清单数字而保留旧调用。

| 消费者 | 执行时机 | 处理与必须保留的语义 |
| --- | --- | --- |
| Ncma.Characters/CharacterPlayRuntime.cs:41 | 启动 | E4-B：Character OR BoxCollider并集、去重、UUID顺序；不能把OR改成AND |
| 同文件:48 | 启动 | E4-B：FollowCamera成员集合，包括空集合的冻结 |
| 同文件:160 | 显示帧FollowView | E4-B：精确UUID读取，缺失返回null；不缓存可跨恢复使用的Entity |
| Ncma.Characters/RootMotionSet.cs:34 | 启动 | E4-B：All RootMotion / None Animator，保留创建顺序及资源/组件缺失拒绝 |
| Ncma.Characters/GraphRootMotionSet.cs:33 | 启动 | E4-B：RootMotion与Animator交集，保持同一动画时钟与资源代次 |
| Ncma.Characters/ActionController.cs:47 | 启动 | E4-B：Health与ActionDefinition并集，保持UUID顺序、动作32预算和根运动要求 |
| Ncma.Scene.Rendering/SceneAnimatorRuntime.cs:49 | 启动 | E4-B：Animator查询后验证SkinnedMesh，不能把缺少蒙皮的Animator静默过滤；保持UUID顺序、32角色/32768骨骼预算 |
| Ncma.Player.App/PlayerRunner.cs:69、91 | 启动 | E4-B：RootMotion/Animator存在性及精确相机UUID，保留Headless资源准备与启动失败 |
| Ncma.Scene.Rendering/RenderSceneExtractor.cs:58 | 缓存未命中的显示提取 | E4-C：几何并集和灯光筛选；缺Transform、双几何、多主灯诊断；创建顺序、FrameIndex与独立阴影集合 |
| Ncma.Scene.Rendering/SceneEnvironmentState.cs:19 | 启动/显示共享入口 | E4-C：缺省Off、重复设置拒绝、精确World与已提交边界 |
| Ncma.Scene.Rendering/ScenePostProcessState.cs:12 | 启动/显示共享入口 | E4-C：缺省Off，两个Off也属于重复；不能只查Enabled |
| Ncma.Scene.Rendering/SceneShadowData.cs:31 | 启动/显示共享入口 | E4-C：缺省null与显式Off不同，保留重复拒绝 |
| Ncma.Rendering.Scene/ScenePipelineStartup.cs:38 | 启动 | E4-C：设置唯一性、快照与World一致性、工厂只读边界 |
| Ncma.Player.App/PlayerPresentation.cs:140 | 每次有效显示提交 | E4-C：RenderConfiguration/Environment筛选；Player重复设置报错与失败清屏 |
| Ncma.Editor.App/Program.cs:431 | World身份/revision变化 | E4-C：设置筛选；Editor警告并保留上次有效设置，不能统一成Player策略 |
| Ncma.Editor.App/EditorPresenter.cs:149 | revision/选择/目录/文档变化 | E4-D：清理失效行ID；不是稳定每帧都全扫描 |
| Ncma.Editor.Services/EditorAssetWorkflow.cs:187 | 导入影响检查事件 | E4-D：静态/蒙皮/ClipPlayback引用并集、去重和顺序；保留导入ticket验证 |
| Ncma.Scene/SceneDocument.cs:30 | 观察外部World revision变化 | E4-D：失效UUID绑定清理，区分成员变化与值变化；保留文档revision与历史失效 |
| Ncma.Runtime/ComponentAuthority.cs:48 | 权威申请 | 保留完整冻结成员覆盖校验，不因消除扫描而接受子集；不是逐帧角色扫描 |
| Ncma.Managed/SceneWorld.cs:23 | 用户显式GetObjects调用 | 保留高级GameObject副本列表合同；同一World存储，不是废弃Runtime.GameObject |
| Ncma.Runtime/World.cs:84 | API定义 | 保留显式复制枚举API和E0对照；不是待删除兼容入口 |

### 10.3 快照与间接消费者：保留和迁移的边界

- `CharacterPlayRuntime.Compose`、`SceneAnimatorRuntime.Compose`的组合验证和资源准备、`MovementCoordinator`构造/Start的原启动字节对照、`RuntimeSessionOwner.Preflight`及PlaySession启动/重载是冷路径，保留完整验证。固定步已经使用受控绑定/演员列表，不应重写成每步动态重新收集角色。
- `RenderSceneExtractor.Extract`目前没有完整文档序列化，且具备World身份/revision/资产/相机/尺寸/蒙皮/显示变换引用缓存；只在缓存未命中时复制实体列表。E4-C减少该复制/筛选，不将其描述为已存在的“每帧JSON路径”。`SceneRenderEnvironment`、`SceneRenderPostProcess`、`SceneRenderShadows`和两宿主间接调用设置读取，须一起验证，不只改静态Read方法。
- `EditorWorkspace.Snapshot`按文档generation/revision缓存；`EditorSceneHud.RefreshEditSceneHud`在编辑revision变化或显式启动时读取快照，Play时无参路径直接返回。`EditorAnimationGraphPreview`使用隔离预览场景，其资源Open准备与每帧Draw不同。E4-D可优化面板读取，但不把这些已有缓存/隔离全部移除。
- `EditSession`的Documents/CommandParticipants/CommandImpacts/Interactions/InteractionGate/CaptureBytes，以及`SceneDocumentFiles.Save`、Prefab制作/验证、场景克隆、资源重建/显卡恢复、Player最终摘要属于事务、完整验证、保存或恢复合同，保留完整文档路径；不重写Undo存储、场景格式或File.Replace策略。
- `Capabilities.Inspect(ncma.scene.inspect)`和`ObjectDetails`目前为一次请求捕获完整文档再分页；E4-D评估只复制请求对象/页的受检读取。必须保持创建顺序、组件/绑定/Export原有排序、offset非负、limit分别为1..128/1..64的分页限制、256KiB对象详情输出限制、请求授权/身份/revision与复制隔离。`ncma.scene.validate`仍需完整组合验证，不替换成局部Query。
- UI自身的UiDocument/UiRuntime/UiCanvas数据和骨骼内部层次不迁移成场景父子树。2D/UI路径不因“统一ECS”引入3D/物理资源，也不添加Python游戏逻辑、推理服务或Agent直接写权限。

### 10.4 查询适配的具体约束

1. Query计划由消费者拥有并复用；World实例替换或同一实例Restore导致Identity变化时重新建立，不能只观察对象引用。成员列表看MembershipVersion，值/设置缓存仍看Revision；成员版本未变不代表Transform/设置未变。不持有跨结构提交的枚举器。
2. `World.Has<T>`对不存在的类型池返回false，而`Query<T>`要求类型已注册。迁移需覆盖默认/精简注册表，不能让原先“无此可选功能”变成初始化异常。只能在需要该领域且类型注册成立时建立相应计划，不能给纯2D额外注册/初始化所有3D模块。
3. Query默认创建顺序；角色/动作/Animator原有UUID排序继续保持。多查询并集去重后按原消费者顺序输出，不能直接拼接导致双处理或顺序变化。
4. 渲染查询以几何组件为筛选条件，后续仍检查Transform；不能通过Transform交集把错误对象过滤。Static+Skinned冲突、多主灯、重复Off设置仍必须被看到。顺序变化会影响FrameIndex/拾取/诊断，必须逐项对照。
5. 对未知UUID，现有`FindEntity`抛异常；FollowView等原先FirstOrDefault的可空行为不能直接替换为无保护FindEntity。E4-B优先评估一个只读、owner-thread、返回完整Entity的TryFindEntity，不以catch所有异常吞掉身份/线程问题；这是待实现项，不是已存在API。
6. 资源与数值系统依旧使用持久UUID+受检World/session/tick映射到原生不透明句柄。Native批量ABI保持不变；不把Entity序列化，不新增World副本，不扩大组件写权威。

E4-B回归等待期间的只读核对补充（非E4-C实现）：`EntityQuery`已有创建顺序比较及Scene.Rendering友元访问，因此几何并集可以在当前World的受检查询结果中合并、去重并按创建顺序缓存，无须新增公开序号或用UUID/槽位排序替代。缓存须同时记录World身份与MembershipVersion；组件值缓存仍观察Revision。`SceneEnvironmentState.VerifyBoundary`及原生资源会话持有的expectedWorld必须先验证，不能借“自动重建查询”让过期原生会话接受Restore后的World；纯数据Extractor重建查询与原生会话拒绝陈旧身份是不同合同。后续设置读取还须覆盖SceneRenderEnvironment/PostProcess/Shadows的间接消费者，不能只替换三个静态Read函数后每次显示重新分配查询计划。

### 10.5 测试映射与本片结果

E4-B使用Runtime身份/Query/权威用例、Gameplay提交/Movement用例，并扩展真实角色、根运动、Animator、Headless宿主覆盖；fake Movement不能代替Jolt实测。E4-C使用SceneRendering提取/诊断用例及Rendering的SceneCandidateSafety、SceneExchangeSafety、SceneConstructionSafety、SceneCostJoint、GraphicsRecoveryJoint等现役产品测试。E4-D使用SceneDocument、EditorCore、EditorService、EditorTransport、ApplicationService以及UI路由/联合宿主测试。每片需新增对应失败/恢复/排序边界的断言，原测试不能削弱；E4-E回到完整Debug/Release产品组合，不以子集代替。

本片为审计文档变更，不新增运行时代码或测试用例，**未重新构建或运行完整89项回归，也不冒称新性能测试**。验证包括：

- `out/verification/ecs-e4a/verify-audit-first.ps1`：实际清单逐项匹配，以及内存中模拟漏项、重复、错误行号、错误源码、错误分类5类负例均拒绝；生产Query使用基线匹配，合计 **7/7**。原文件不被负例改写。
- E3-D同一939文件源码冻结再次通过；SHA仍为 `47905BDD40B61E63ED8B89D5DF5D8EABC6E329D735C3C364DCB8442FCE288BFC`。因此本片引用上轮双配置89/89功能结果，不重跑未改变的产品。
- 重新执行只读交付核验：10个候选包、正式144清单/148文件及148备份、E0原始18程序集、E3-A原始8输入/9日志全部保持完整。正式E1-B EXE SHA仍为 `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。
- 原939修复与937退化性能证据、File.Replace首轮失败以及4项无关IDE修改均保留。源代码只读盘点不是整帧成本证据；ECS-PERF-01/02不关闭。

本机静态校验日志 `out/ecs-e4a-audit-first.log` SHA256 `BF0113D24B3B0FE74970D929FA1A0DC12BC71A82931FD5F7EF51C1A7708344AA`；只读完整性日志 `out/ecs-e4a-integrity-first.log` SHA256 `9F9876CFD48DA6112323B776A7132EF38DAAF6533013EDFBC123B179C0004F55`。清单文件SHA256 `DC7F75C8D7D871067836A6EDDD723AA783C26AF12DB04219601D8E8E1FB2B4FC`为本机原始字节哈希（Git换行规范化可能改变工作副本字节，字段/源码逐项匹配才是清单判定）。辅助脚本与日志只保留在ignored out，不作为新增产品入口。

E4-A审计片完成，下一片是E4-B角色/动画查询与身份访问适配。E4-B/C/D/E尚未实现，E4整体与E5仍未验收，不进入M8.3。

### 10.6 E4-B 角色/动画查询与身份访问：自动候选验证完成

2026-10-19，基于已交付的E4-A `0ef48c38ab21b8c843e0b23500c173ef8c5124e0`继续实施。本节覆盖上节“E4-B尚未实现”的旧状态；**候选代码、双配置完整自动回归及交付核验已完成，待本片提交推送；不部署、不关闭ECS整体及性能门禁**。下文按发生顺序保留定向失败、首次完整失败和复测过程，末尾最终结果覆盖运行中记录。

- Runtime新增只读 `World.TryFindEntity(Guid, out Entity)`：只读取已提交UUID索引，缺失/空UUID返回false/default，存在时返回完整WorldIdentity/Index/Generation；仍先检查owner线程。它不授予写权限、公开候选对象或持久化Entity。`ComponentRegistry.IsRegistered<T>()`只报告CLR类型注册，不表示成员存在或可写。
- 内部 `SceneStartupEntities`只用于离帧组合准备：单类型、双类型交集、All/None及按UUID排序的双类型并集；返回拥有的Entity副本。可选类型未注册时沿用原Has的缺省语义，并集去重且最多World.MaxObjects。没有每帧重建Query、第二份组件存储、跨恢复Entity缓存或新增原生ABI。
- 已迁移CharacterPlayRuntime的角色/碰撞体、FollowCamera启动集合；RootMotionSet/GraphRootMotionSet分流；ActionController的Health/动作并集；SceneAnimatorRuntime的Animator收集。既有UUID顺序/创建顺序分别保留；缺蒙皮、缺资源、预算、冻结成员、共享动画时钟与物理fail-stop检查保留。
- FollowView改用TryFindEntity，保留缺相机返回null及缺FollowCamera/Camera行为；Player启动相机也改为精确查找。Player的Headless动画存在性改为RootMotion/Animator Query；该宿主在创建World时明确注册这两个类型，未扩大纯2D/无Python依赖。角色固定步仍遍历受控绑定，动画仍使用原有候选/提交生命周期。
- 按E4-A相同生产源码过滤规则复核：原22行/19文件的GetEntities匹配现为13行/13文件（含1定义、12调用）；本片6个消费者文件中的9处旧遍历已替换。其余为E4-C/D待迁移及ComponentAuthority、高层GetObjects等现役合同；没有为了清零搜索结果删除受检复制API，也未改写原E4-A基线JSON。词法残留核对不是性能证明。
- 新增Runtime查找8组：缺失/空UUID、同UUID删除复用、恢复/跨World、未提交创建、候选槽位替换/撤销、只读/线程、注册≠成员、预热查找无列表分配。新增领域查询8组：可选注册、UUID/创建顺序、并集去重、恢复/复制隔离、300轮固定种子结构变化对照、4096容量、线程、错误组合不被过滤、真实Play跟随相机删除/重建（其中组合项合计8组）。
- 定向第三轮两配置Runtime新增8/8、原Runtime13/storage14/identity15/Query17/候选8/Paged10/增量25/Schema6通过；SceneRendering **121/121**（新增8组）通过；Gameplay53/结构26/增量3/提交12/联合15、Movement38 fake、UI18、E0fixtures11通过。会话65326已结束exit0，不轮询。日志 `out/ecs-e4b-{Ncma.Runtime.Tests,Ncma.Scene.Rendering.Tests,Ncma.Gameplay.Tests}-{Debug,Release}-targeted-third.log`，无编译警告。这不是实际Jolt/DX11产品联合验收或E5性能证明。
- 早期定向失败保留：第一轮新测试把旧Entity拒绝误认为只能抛InvalidOperationException，实际原契约为ArgumentException；第二轮测试在同一候选步删除后重复创建同UUID，被既有StageCreate规则正确拒绝。修正测试为原异常合同及不同UUID的槽位替换，并保留独立安全边界同UUID复用用例；未放宽产品规则。首轮PowerShell stderr提前中止的日志同样保留，后续显式捕获进程退出码。
- 新源码冻结 `out/verification/ecs-e4b/source-freeze-first.json` 共 **942文件**，SHA256 `216E02F6DA690BF15194765D9AB777346FB3FE7180603D6A3B312BCF3DB75931`。顺序完整门禁会话 **51807** 正在执行：标准Build.bat -SkipTests构建两配置，各全89项CTest及smoke/Python45/inspect；脚本 `out/verification/ecs-e4b/run-gates-first.ps1`，总日志 `out/ecs-e4b-gates-first.log`。构建Skip只表示测试由外层单独执行且禁止部署，不表示跳过本片完整验证。期间不修改冻结源码或启动重复构建。

待完整双配置结束后执行 `out/verification/ecs-e4b/verify-delivery-first.ps1`核对新冻结、完整结果、候选包、正式安装与备份，再更新交付状态及提交推送。E4-A JSON保留为6f4d666基线，不将其旧调用行号冒充当前源码；迁移后残留由本片重新扫描。ECS-PERF-01/02、人工/目标机/自包含/整帧/1h、历史File.Replace调查保持开放。

首轮完整门禁终态（覆盖上文运行中记录）：Debug标准构建通过，候选 `0015eda4cd854d43a39e5eaaa570a154`，未发现编译警告；会话51807已结束exit1。Debug **88/89，1203.78秒**，唯一失败是 `NcmaRenderingTests` 超过原240秒上限（240.06秒），Release及补充门禁未启动。此前进度消息将CTest继续执行后续用例误认为主渲染通过，现明确更正；不能据此宣称完整门禁通过。完整失败报告保留于 `out/ecs-e4b-ctest-debug-first.log` 和 `out/ecs-e4b-ctest-debug-details-first.log`，输出停在M6.6-B真实NCA用例后，未出现最终生命周期汇总；根因尚未证明。

只读进程核对未发现残留dotnet/Editor/Player/Rendering测试进程，未终止进程或删除文件。独立原CTest及其fixture复测会话63307已结束exit0，2/2通过，渲染耗时168.06秒（合计168.52秒），最终32次生命周期及validation0/0通过；保持240秒上限和所有断言，不能以独立通过代替完整双配置回归，也不能据此证明首次超时根因。

第二轮完整门禁会话36824正在执行 `run-gates-second.ps1`（总日志 `out/ecs-e4b-gates-second.log`）：同一942冻结，重新标准Debug/Release构建、完整89项及补充验证。build/CTest/details/补充入口使用second日志；尚未执行过的smoke/Python/inspect使用first日志。终态后使用 `verify-delivery-second.ps1`核验，不覆盖首轮失败。未提交、推送、部署或开始E4-C。

第二轮Debug已完整通过：**89/89，1099.17秒**，主渲染170.05秒；托管/原生smoke、Python45、inspect与942源码冻结通过。候选 `cc6067a8935347408d99eb50ddd32bc4`，无编译警告；Release标准构建在同一36824会话继续。此记录只确认Debug，不提前确认Release或最终包/备份核验；首次超时根因仍未证明。

另运行只读 `out/verification/ecs-e4b/verify-deferred-performance-first.ps1`，E3-D修复后性能归档 `ff40e2cf71514201943d7304632ed924` 的18输入、6日志、原939冻结和配对分位数证明全部匹配；日志 `out/ecs-e4b-deferred-performance-first.log` SHA256 `B4BE5E4CD20B71A6A32C18D01811ED5C864C83020C893559325BC7613923626A`。只证明历史证据保留，不是本轮新性能采样或性能通过。

#### E4-B 最终自动验证结果

- 同一942文件冻结，标准Build.bat两配置第二轮构建均成功、无编译警告。SkipTests只把测试移到外层独立完整执行，禁止部署；未宣称新no-Skip/旧三轮历史性能门禁。
- Debug **89/89，1099.17秒**；Release **89/89，996.57秒**。主渲染分别170.05/167.79秒，保持原240秒上限和全部断言。两配置smoke、Python45、inspect及冻结前后核验通过；新Lookup8、SceneRendering121与原Runtime/Gameplay/Movement/UI等完整用例通过。
- 十个候选包清单逐文件SHA/大小与实际闭包通过：每配置Editor144、Player-Null56、DX11113、Null-Physics67、DX11-Physics116项；实际文件分别146/57/114/68/117，额外文件仅为清单及Editor既有允许日志。候选Debug `cc6067a8935347408d99eb50ddd32bc4`、Release `5411fe4c6b824d118b54cd7fdc620ec5`。
- 正式E1-B generation `7f3d3822dd39431b9c203e17194afb8c`仍为144清单/148实际，原148备份逐文件一致；EXE SHA `C50742AD63620FB6EA2E068DBBFA98147EA870EE20C8A07CB6DD04AF8F7DCC61`。E0原始18程序集、E3-A原始8输入/9日志完整。
- 最终核验 `out/verification/ecs-e4b/verify-delivery-second.ps1` exit0，日志 `out/ecs-e4b-delivery-verification-second.log` SHA256 **037B7E4D6CEC3FD0DB9FB3B00E579E7091BBF3877697854A2DF7C82624B1557E**。会话36824已结束exit0；51807和63307也均已终态，不再轮询或重启。
- 首轮Debug88/89渲染超时和早期测试夹具错误完整保留，首次超时根因仍未证明；未修改产品、超时或断言来换取通过，没有终止残留进程或删除失败文件。双配置自动候选通过不等于无偶发故障保证或性能验收。

本片自动候选完成，提交推送后再推进E4-C渲染提取/设置查询。E4-C/D/E、E5、M8.3尚未完成/开始；ECS-PERF-01/02以及人工、目标机、自包含、完整整帧性能、1h和历史File.Replace调查保持开放。正式EXE未更新。
