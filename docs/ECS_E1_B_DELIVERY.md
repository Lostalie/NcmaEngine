# ECS E1-B：身份迁移进度

2026-10-16。基点 `92a1b3084736a20ae45db6310d511b0786c3621f` 已核对本地与远端 main 一致。E1-B 第一增量自动候选门禁通过，Git 交付以本增量提交和远端核验为准；**整个 E1-B 尚未完成**，不能进入 E2 或 M8.3，也不替换正式 EXE。

## 本增量实现

- 唯一 `Runtime.World` 使用 `Entity(WorldIdentity, Index, Generation)` 校验运行时身份；默认、越界、跨 World、零代次、已删除和旧代次均拒绝。Entity 不携带 World 引用或权限。
- 槽位可复用；已提交 Entry 独立保留有效代次，代次分配器的预约不随候选回滚。候选删除/复用期间原 Entity 仍可读取已提交值；失败候选永不通过后来复用重新生效。
- 最大代次可以使用一次，之后槽位永久退役，不能绕回；全部可用槽位耗尽时明确失败。创建序号独立于槽位，保持公开枚举/快照创建顺序，序号溢出也明确失败。
- 恢复在完整验证后切换 World 身份；失败保留原身份和数据。新 World 身份允许使用新代次空间，但旧身份不能访问它。
- 两个旧 `ObjectReference` 类型、旧 `Reference` 属性、用户侧数值 `GameObject.Id` 和未使用的 `FindId` 已删除，未添加别名或转发兼容 API。现有批次、信号、权限、PlaySession 和相应测试改用完整 Entity。
- 用户侧 `Ncma.GameObject` 直接持有 SceneWorld 服务与 Entity，不再持有 Runtime.GameObject；相等性及哈希基于完整 Entity，旧门面不重绑。
- 注册检查拒绝直接、嵌套及 Nullable Entity 组件字段；Entity 默认 JSON converter 拒绝读写，场景仍只持久化 UUID，不改变 SceneDocument 格式。
- `World.Get/Set/Has/Remove/GetUuid/GetName/Rename/Destroy/Resolve(Entity)` 是受检入口，继续执行线程、只读、组件权威、候选提交和预算约束。

## 尚未完成的 E1-B 工作

低层 Runtime/Scene/渲染/领域消费者仍使用原 `Runtime.GameObject` 和 CreateObject/FindObject/GetObjects。此类型尚未完成消费者替换，**本增量不宣称单一公开门面已完成**。下一增量必须迁移到 World + Entity 的创建/查找/枚举 API，并删除该类型及旧签名，不增加旧签名转发壳或换名包装。

当前 SceneWorld 创建/枚举仍会从低层旧 API 获得临时包装再取 Entity；不宣称该冷路径零分配。全量快照/JSON/整池首次写复制仍保留，Query、增量 tick 和性能收益验证仍属 E2–E5。

下一增量的迁移清单（文本检索 CreateObject/FindObject/GetObjects 共涉及 123 个文件，含用户门面调用，不能直接视作 123 个低层消费者）：

1. Runtime 创建/UUID 查找/创建序枚举、ComponentAuthority 改用 Entity；不能向 Runtime 引入 Managed 依赖。
2. SceneDocument、Editor 命令/检查、Character/Movement 与 PlaySession 改为 World + Entity，保留 UUID 事务边界。
3. RenderSceneExtractor 的筛选/选中相机，以及 SceneAnimationSession.Character 的缓存，明确保存 Entity 与所属 World；不得仅存 Index。
4. 样例、Player、领域测试及 E0 测量工具同步迁移；E0 原始归档程序集不能覆盖，基准工作量/语义断言不能减少。
5. 删除 Runtime.GameObject、低层旧创建/查找/枚举签名；用户门面创建不再临时分配低层包装。对新旧类型消失、同一实体存储、相等性、提交/恢复失效再做自动门禁。

## 验证记录

- 新增 `EntityIdentityTests` 默认接入现有 Runtime 测试入口：12 组身份/失败候选/代次退役/创建序号/恢复/序列化/线程负例。首轮 Debug 原 Runtime 13/13、E1-A 存储 14/14、新身份 12/12 通过，`out/ecs-e1b-headless-debug-first.log`。
- Managed smoke 增加门面相等性、同槽旧代次、旧信号、跨 World、批次无前缀提交、旧引用类型和二次包装移除检查。Entity/批次结构是托管内部布局，不是新原生 World ABI。
- 首次 Debug 构建失败：新增注册负例遗漏必填 validate 回调，已修正；第二/第三次构建成功但新增 null 相等性测试的后续访问触发 CS8602。保留 `out/ecs-e1b-build-debug-{first,second,third}.log`，不把带新警告构建算作最终通过。将 null 相等性断言放在实例最后一次使用处后，冒烟项目 `-warnaserror` 编译成功；未加 null-forgiving 或禁止警告。
- 最终冻结 `out/verification/ecs-e1b/source-freeze-second.json` 包含 913 个源码/构建文件，SHA256 `A91AA92FBE4B8D24AEC7ABE5958E9F9AD1ECE166937E00F0D1611D2201451456`；首份冻结保留但不作为最终证据。Debug 第四次 canonical SkipTests 构建通过、零编译警告，候选 `198b63a2ccf4466bba66fe0eb1cf01c7`。
- Debug 全部 CTest **89/89** 通过，1104.88 秒，日志 `out/ecs-e1b-ctest-debug-first.log`，细项 `out/ecs-e1b-ctest-debug-details-first.log`。最终 Debug 冒烟、Python **45/45** 及 inspect 通过，`out/ecs-e1b-{supplemental,smoke,python,inspect}-debug-second.log`；早期 first 补充日志仍保留。
- Release canonical SkipTests 构建通过、零编译警告，候选 `ad68da9c6e2846f88f322a5fdacbe5f3`。全部 CTest **89/89** 通过，999.39 秒，日志 `out/ecs-e1b-ctest-release-first.log`，细项 `out/ecs-e1b-ctest-release-details-first.log`；最终冒烟、Python **45/45** 及 inspect 通过，`out/ecs-e1b-{supplemental,smoke,python,inspect}-release-second.log`。
- 两配置明细均核实 Runtime **13/13**、typed storage **14/14**、identity **12/12**、Gameplay **53/53**、Movement **38/38**（fake，不冒充 Jolt 验收）、Gameplay UI **18/18**、E0 fixtures **11/11**（非测量）。真实渲染/物理/Player 联合用例独立包含于完整 CTest。
- Release 测试并行期间的一次只读冻结核对遇到 FBX fixture 排他文件锁，未能完成。证据 `out/ecs-e1b-concurrent-freeze-refusal.log`。外层 shell 后续 git diff 返回 0 不代表哈希核对通过；未解除锁、跳过素材或改变冻结。全部测试退出后最终完整核对通过，包含该素材。

## 最终文件核验

`out/ecs-e1b-delivery-verification-first.log` 通过，SHA256 `18B44D59FB6E8B25D886F60D0F76F293ADCB6A8E79DA6D1691029E8939BD072E`；记录全部日志和 10 个候选包 manifest 哈希。

- 同一 913 项源码及 solution 冻结与双配置回归后完全一致。
- 两配置各 5 个候选包的产品身份、合法路径、size、SHA256 和文件闭包通过。Editor/Player-Null/Player-DX11/Null-Physics/DX11-Physics 清单分别 144/56/113/67/116 文件；Editor 允许既有有界测试日志，其余 Player 严格闭包，不弱化产品检查。
- 正式 M8.2 安装 144 个清单文件与原始 Release 包逐一一致，实际 148 文件；原备份 148 文件与 Complete journal 完全一致。正式 EXE SHA256 `C80EDA73C7BA9F97E71B372CD14CF6A3F44A02FDBA16561F593B0D5527217E4E`。
- E0 `baseline-binaries` 原始 18 个文件逐一匹配测量 inputs 清单，没有被新编译覆盖。
- 所有构建/测试会话已结束，最终顺序回归会话 48926 exit 0，无残留待等待测试。本增量并非无 Skip Build.bat/历史三轮 profile/受检部署门禁，不宣称整个 E1-B 或 ECS 完成。

## 保留门禁

正式 M8.2 `out/bin/NcmaEngine.exe`、Complete `cd023930bb454a6fbee9b1cd70b1b7a0`、备份、E0 原始 18 个测量程序集及全部失败记录保留。SkipTests 构建不部署；无 Skip 全回归、三轮历史 profile、新旧配对性能、人工、目标机、自包含、1h 和历史部署调查均不因本增量关闭。
