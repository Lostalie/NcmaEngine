# ECS E1-B：身份与单一门面迁移

2026-10-16。第一增量已提交推送 `0fa22943ae8e955ebd3e67a44ac51a0ad580c9e1`，本次开始前已核对本地和远端 main 一致。第二增量完成低层消费者与单一门面迁移，同源码 Debug/Release 自动门禁及最终文件核验均通过，**E1-B 自动候选完成**；Git 交付以本增量提交和远端核验为准。下一步 E2，但本次未启动 E2/M8.3，也未替换正式 EXE。

## 第二增量：低层消费者迁移

- `World.CreateEntity(name, uuid?)`、`FindEntity(uuid)`、`GetEntities()` 返回值身份；删除 `Ncma.Runtime.GameObject` 及 Runtime 的 CreateObject/FindObject/GetObjects，没有别名或兼容转发。
- Runtime 的组件权威、SceneDocument、Editor 事务/检查、角色/运动、PlaySession、渲染/动画缓存、样例、Player 和测试改用 World + Entity。Entity 缓存保留完整 World/Index/Generation，场景和编辑命令仍只以 UUID 持久化。
- 高层 `Ncma.GameObject` 保留创建、查找、组件和脚本服务；SceneWorld 直接包装 Entity，不再分配临时低层 GameObject。实体创建本身只建立 World 元数据，用户需要时才创建高层门面。没有增加第二份组件存储或 Runtime→Managed 依赖。
- Entity 枚举仍按创建序返回独立数组，不是 E2 查询缓存。不存在的相机改为检查默认 Entity；可选选中相机显式解包 Entity?。事务、权限、只读检查、现有快照/整池候选和全部预算不变。
- 身份测试扩展至 15 组：旧类型/入口反射拒绝、预热 UUID 查找无逐实体包装分配、枚举复制与候选可见性、公开入口线程/只读约束。Managed smoke 新增“先建 Entity、后取门面”，双向读写/移除同一 Transform 和删除失效检查。真实角色测试新增缺失相机负例。
- E0 工具只迁移 API，未改变负载规模、断言或原始归档；不把本次 API 改写当作性能验收。

这是低层源码/程序集 API 的破坏性调整：直接引用旧 Runtime.GameObject 或旧 World 创建/查找/枚举方法的 C# 扩展须迁移并重新编译。现役 SceneWorld/GameObject 与 EditorWorkspace 的同名高层业务入口保留；它们不是旧 Runtime API 的兼容壳。Query、增量 tick、领域热路径优化及 E5 完整性能/部署验收仍未完成。

调用边界示例（低层不依赖 GameObject）：

```csharp
var world = new Ncma.Runtime.World();
var entity = world.CreateEntity("Character");
world.Set(entity, Ncma.Runtime.TransformData.Identity);
var uuid = world.GetUuid(entity); // 只有 UUID 可以持久化。
var sameEntity = world.FindEntity(uuid);
var transform = world.Get<Ncma.Runtime.TransformData>(sameEntity);
```

本次失败证据保留：迁移辅助工具首次语法树语言配置不一致，未改产品源码；旧临时 apphost 被占用导致第二/第三次工具构建失败。改用独立输出目录与 DLL 入口完成迁移，没有强删占用文件。首次产品构建发现 Entity 的旧 null 判断，第二次发现真实 Editor/Player 探针入口遗漏；均修正后重新执行规范构建，不删除测试或恢复兼容 API。详见 `out/ecs-e1b2-*`。

首轮 Debug 完整 CTest 为 **85/89**（832.93 秒），不是全部通过：4 个 GraphicsRecoveryJoint 画质变体在共同 fixture 第 81 行拒绝跨 World Entity。fixture 的 `doc=AddRecoveryJointCharacter(...)` 创建新 World，而局部 character 仍来自旧 World；修正为文档替换后以原 UUID 重新解析，新增 World 身份变化断言，未改变产品 Require 校验或降低负例要求。首轮源码冻结与失败日志保留；修复后重新冻结 913 个文件并重复完整双配置门禁。期间进度曾误将已执行编号称为通过数，最终以完整 CTest 结果为准。

### 第二增量验证记录

- 冻结 `out/verification/ecs-e1b2/source-freeze-second.json`：913 个源码/构建文件，SHA256 `CFB85DB88C0ED396AFADE21D3C4FC68EFB00BC55CE711BB0D966C4EAAA13954D`。修复前的 first 冻结保留，不作为最终放行证据。
- Debug 第四次规范 `Build.bat -Configuration Debug -SkipTests` 通过，零编译警告；候选包 `cf4fb097434e410ab79a3b7b72f06fcc`，不部署。完整第二轮 CTest **89/89**，1098.49 秒；日志 `out/ecs-e1b2-build-debug-fourth.log`、`out/ecs-e1b2-ctest-debug-{second,details-second}.log`。
- Debug 补充 managed/native smoke、Python **45/45**、inspect 通过；日志 `out/ecs-e1b2-{supplemental,smoke,python,inspect}-debug-first.log`。测试结束后 913 文件重新核对一致。
- Release 第一次规范 SkipTests 构建通过，零编译警告；候选包 `46571f2d663b4f73ba868b650aea915f`。完整 CTest **89/89**，1013.50 秒；日志 `out/ecs-e1b2-build-release-first.log`、`out/ecs-e1b2-ctest-release-{first,details-first}.log`。补充 smoke、Python **45/45**、inspect 全部通过，`out/ecs-e1b2-{supplemental,smoke,python,inspect}-release-first.log`。
- 两配置明细确认 Runtime **13/13**、typed storage **14/14**、Entity identity **15/15**、Gameplay **53/53**、Movement **38/38**（fake，不冒充 Jolt 验收）、Gameplay UI **18/18**、E0 fixtures **11/11**（非性能测量）。真实渲染、角色/物理、Player 和恢复联合用例仍包含于全量 CTest。
- 用户仅批准处理早期异常迁移工具 PID 26276/15844；操作前重新查询发现二者均已退出，原会话 42951 终态 exit 1，没有执行任何进程终止，没有删除文件。核对记录 `out/ecs-e1b2-approved-helper-close-first.log` 保留。

### 第二增量最终文件核验

最终顺序门禁会话 45748 **exit 0**，全部构建/测试和早期辅助工具会话均结束，无待等待任务。`out/ecs-e1b2-delivery-verification-first.log` 通过，SHA256 `A9B2C242999F1633CA1C4110F87BF012A76373A38EC6475008625578794A5F92`，记录逐项证据哈希：

- 913 个源码/构建文件与第二次冻结完全一致，未将第一轮失败源码的证据混用于放行。
- Debug/Release 各 5 个候选包产品身份、路径、size/SHA256 及文件闭包通过；清单文件数均为 144/56/113/67/116。Editor 只沿用现有有界运行日志允许规则，Player 维持严格闭包。
- 正式 M8.2 `Complete cd023930bb454a6fbee9b1cd70b1b7a0`：144 个清单文件逐一匹配原包、实际 148 文件，原备份 148 文件逐一匹配原 journal；EXE SHA256 `C80EDA73C7BA9F97E71B372CD14CF6A3F44A02FDBA16561F593B0D5527217E4E` 未变。
- E0 原始 `baseline-binaries` 18 个程序集/文件逐一匹配原 inputs 清单。
- 本片是规范 SkipTests 候选构建加全量 CTest 与补充测试，不是 E5 的无 Skip 构建/三轮历史 profile/新旧配对性能/正式部署验收；不关闭人工、目标机、自包含、完整性能或长稳门禁。

## 第一增量历史记录

以下记录对应第一增量，不作为第二增量的最终测试证据；其中“尚未完成”清单为当时交接范围。

## 第一增量实现（历史）

- 唯一 `Runtime.World` 使用 `Entity(WorldIdentity, Index, Generation)` 校验运行时身份；默认、越界、跨 World、零代次、已删除和旧代次均拒绝。Entity 不携带 World 引用或权限。
- 槽位可复用；已提交 Entry 独立保留有效代次，代次分配器的预约不随候选回滚。候选删除/复用期间原 Entity 仍可读取已提交值；失败候选永不通过后来复用重新生效。
- 最大代次可以使用一次，之后槽位永久退役，不能绕回；全部可用槽位耗尽时明确失败。创建序号独立于槽位，保持公开枚举/快照创建顺序，序号溢出也明确失败。
- 恢复在完整验证后切换 World 身份；失败保留原身份和数据。新 World 身份允许使用新代次空间，但旧身份不能访问它。
- 两个旧 `ObjectReference` 类型、旧 `Reference` 属性、用户侧数值 `GameObject.Id` 和未使用的 `FindId` 已删除，未添加别名或转发兼容 API。现有批次、信号、权限、PlaySession 和相应测试改用完整 Entity。
- 用户侧 `Ncma.GameObject` 直接持有 SceneWorld 服务与 Entity，不再持有 Runtime.GameObject；相等性及哈希基于完整 Entity，旧门面不重绑。
- 注册检查拒绝直接、嵌套及 Nullable Entity 组件字段；Entity 默认 JSON converter 拒绝读写，场景仍只持久化 UUID，不改变 SceneDocument 格式。
- `World.Get/Set/Has/Remove/GetUuid/GetName/Rename/Destroy/Resolve(Entity)` 是受检入口，继续执行线程、只读、组件权威、候选提交和预算约束。

## 第一增量当时尚未完成的工作（现已迁移）

低层 Runtime/Scene/渲染/领域消费者仍使用原 `Runtime.GameObject` 和 CreateObject/FindObject/GetObjects。此类型尚未完成消费者替换，**本增量不宣称单一公开门面已完成**。下一增量必须迁移到 World + Entity 的创建/查找/枚举 API，并删除该类型及旧签名，不增加旧签名转发壳或换名包装。

当前 SceneWorld 创建/枚举仍会从低层旧 API 获得临时包装再取 Entity；不宣称该冷路径零分配。全量快照/JSON/整池首次写复制仍保留，Query、增量 tick 和性能收益验证仍属 E2–E5。

下一增量的迁移清单（文本检索 CreateObject/FindObject/GetObjects 共涉及 123 个文件，含用户门面调用，不能直接视作 123 个低层消费者）：

1. Runtime 创建/UUID 查找/创建序枚举、ComponentAuthority 改用 Entity；不能向 Runtime 引入 Managed 依赖。
2. SceneDocument、Editor 命令/检查、Character/Movement 与 PlaySession 改为 World + Entity，保留 UUID 事务边界。
3. RenderSceneExtractor 的筛选/选中相机，以及 SceneAnimationSession.Character 的缓存，明确保存 Entity 与所属 World；不得仅存 Index。
4. 样例、Player、领域测试及 E0 测量工具同步迁移；E0 原始归档程序集不能覆盖，基准工作量/语义断言不能减少。
5. 删除 Runtime.GameObject、低层旧创建/查找/枚举签名；用户门面创建不再临时分配低层包装。对新旧类型消失、同一实体存储、相等性、提交/恢复失效再做自动门禁。

## 第一增量验证记录（历史）

- 新增 `EntityIdentityTests` 默认接入现有 Runtime 测试入口：12 组身份/失败候选/代次退役/创建序号/恢复/序列化/线程负例。首轮 Debug 原 Runtime 13/13、E1-A 存储 14/14、新身份 12/12 通过，`out/ecs-e1b-headless-debug-first.log`。
- Managed smoke 增加门面相等性、同槽旧代次、旧信号、跨 World、批次无前缀提交、旧引用类型和二次包装移除检查。Entity/批次结构是托管内部布局，不是新原生 World ABI。
- 首次 Debug 构建失败：新增注册负例遗漏必填 validate 回调，已修正；第二/第三次构建成功但新增 null 相等性测试的后续访问触发 CS8602。保留 `out/ecs-e1b-build-debug-{first,second,third}.log`，不把带新警告构建算作最终通过。将 null 相等性断言放在实例最后一次使用处后，冒烟项目 `-warnaserror` 编译成功；未加 null-forgiving 或禁止警告。
- 最终冻结 `out/verification/ecs-e1b/source-freeze-second.json` 包含 913 个源码/构建文件，SHA256 `A91AA92FBE4B8D24AEC7ABE5958E9F9AD1ECE166937E00F0D1611D2201451456`；首份冻结保留但不作为最终证据。Debug 第四次 canonical SkipTests 构建通过、零编译警告，候选 `198b63a2ccf4466bba66fe0eb1cf01c7`。
- Debug 全部 CTest **89/89** 通过，1104.88 秒，日志 `out/ecs-e1b-ctest-debug-first.log`，细项 `out/ecs-e1b-ctest-debug-details-first.log`。最终 Debug 冒烟、Python **45/45** 及 inspect 通过，`out/ecs-e1b-{supplemental,smoke,python,inspect}-debug-second.log`；早期 first 补充日志仍保留。
- Release canonical SkipTests 构建通过、零编译警告，候选 `ad68da9c6e2846f88f322a5fdacbe5f3`。全部 CTest **89/89** 通过，999.39 秒，日志 `out/ecs-e1b-ctest-release-first.log`，细项 `out/ecs-e1b-ctest-release-details-first.log`；最终冒烟、Python **45/45** 及 inspect 通过，`out/ecs-e1b-{supplemental,smoke,python,inspect}-release-second.log`。
- 两配置明细均核实 Runtime **13/13**、typed storage **14/14**、identity **12/12**、Gameplay **53/53**、Movement **38/38**（fake，不冒充 Jolt 验收）、Gameplay UI **18/18**、E0 fixtures **11/11**（非测量）。真实渲染/物理/Player 联合用例独立包含于完整 CTest。
- Release 测试并行期间的一次只读冻结核对遇到 FBX fixture 排他文件锁，未能完成。证据 `out/ecs-e1b-concurrent-freeze-refusal.log`。外层 shell 后续 git diff 返回 0 不代表哈希核对通过；未解除锁、跳过素材或改变冻结。全部测试退出后最终完整核对通过，包含该素材。

## 第一增量最终文件核验（历史）

`out/ecs-e1b-delivery-verification-first.log` 通过，SHA256 `18B44D59FB6E8B25D886F60D0F76F293ADCB6A8E79DA6D1691029E8939BD072E`；记录全部日志和 10 个候选包 manifest 哈希。

- 同一 913 项源码及 solution 冻结与双配置回归后完全一致。
- 两配置各 5 个候选包的产品身份、合法路径、size、SHA256 和文件闭包通过。Editor/Player-Null/Player-DX11/Null-Physics/DX11-Physics 清单分别 144/56/113/67/116 文件；Editor 允许既有有界测试日志，其余 Player 严格闭包，不弱化产品检查。
- 正式 M8.2 安装 144 个清单文件与原始 Release 包逐一一致，实际 148 文件；原备份 148 文件与 Complete journal 完全一致。正式 EXE SHA256 `C80EDA73C7BA9F97E71B372CD14CF6A3F44A02FDBA16561F593B0D5527217E4E`。
- E0 `baseline-binaries` 原始 18 个文件逐一匹配测量 inputs 清单，没有被新编译覆盖。
- 所有构建/测试会话已结束，最终顺序回归会话 48926 exit 0，无残留待等待测试。本增量并非无 Skip Build.bat/历史三轮 profile/受检部署门禁，不宣称整个 E1-B 或 ECS 完成。

## 保留门禁

正式 M8.2 `out/bin/NcmaEngine.exe`、Complete `cd023930bb454a6fbee9b1cd70b1b7a0`、备份、E0 原始 18 个测量程序集及全部失败记录保留。SkipTests 构建不部署；无 Skip 全回归、三轮历史 profile、新旧配对性能、人工、目标机、自包含、1h 和历史部署调查均不因本增量关闭。
