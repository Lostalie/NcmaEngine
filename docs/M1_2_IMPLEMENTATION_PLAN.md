# M1.2：统一 C# 场景编辑命令、事务与历史

状态：M1.2-A/B 先通过完整测试门槛，之后完成 C/D/E；全部切片已通过最终 Debug 回归。下文保留原设计约束，第 15 节记录交付证据。

## 1. 阶段目标

把场景编辑命令、事务和 Undo/Redo 统一到 C#，保留 C++/Dear ImGui 外壳作为交互与显示层。

本阶段不同时重写编辑器入口、不接入 Behaviour 固定步调度、不实现 live MCP 传输。先完成共享编辑底座，再让这些模块接入。

保持以下已确认边界：

- C# 是唯一游戏逻辑语言，拥有场景与大部分引擎高层功能。
- Python 仅用于独立 AI、工具和特殊模块，不挂载游戏 Behaviour。
- C++ 保留性能关键内核和当前 ImGui 外壳，不新增通用场景存储。
- 场景是扁平 GameObject 列表，空容器配合可选组件；不引入场景父子所有权。
- 唯一场景文件格式为 `.ncmascene` SceneDocument JSON v1。
- 不恢复旧 `.ncscene`、SceneSnapshot、SceneObjectSnapshot、SceneSnapshotCodec 或任何兼容转换。

## 2. 实施前基线与缺口（历史记录）

| 当前实现 | 已有能力 | 需要解决的问题 |
| --- | --- | --- |
| C++ SceneCommandStack | 完整文档快照、Undo/Redo、选择恢复、修改标记 | 编辑历史与业务规则仍在 C++ |
| C# Runtime.EditSession | 权限、事务、版本检查、幂等请求、能力描述 | 只保存 WorldSnapshot，不包含 Behaviour 和 Export |
| C# SceneDocument | 完整组件、脚本配置、原子恢复、文档版本 | 尚未统一接入命令与历史 |
| ImGui Inspector | 名称、Transform、脚本属性编辑 | 先直接修改场景，再由 C++ 补记历史 |

不能直接把现有 Runtime.EditSession 接到活动编辑器，否则可能形成两份历史，或 Undo 丢失脚本配置。

M1.2 应将 EditSession 升级为面向完整 SceneDocument 的编辑服务，并最终删除原生场景命令栈。

## 3. 模块边界

建议新增独立 `Ncma.Editor.Core` 项目，无窗口、ImGui、Python或图形 API 依赖。

```text
C++ / Dear ImGui                后续 Agent / MCP
       │ 用户编辑意图                 │ 授权请求
       └──────────┬─────────────────┘
                  ▼
         C# Editor.Core.EditSession
         权限 → 版本 → 校验 → 事务 → 历史
                  │
                  ▼
           C# SceneDocument
        完整组件、Behaviour、Export
                  │
                  ▼
           C# Runtime.World
          唯一对象与组件存储
```

| 模块 | M1.2 职责 |
| --- | --- |
| Ncma.Runtime | World、组件、运行时引用、Systems；不承担编辑器历史业务 |
| Ncma.Scene | 完整文档、编码、校验、文件读写 |
| Ncma.Editor.Core | 编辑会话、命令、事务、历史、交互编辑、保存状态、能力入口 |
| Ncma.Managed.Host | 会话生命周期与 C ABI 适配，不实现命令规则 |
| C++/ImGui | 控件、快捷键、展示、提交命令、消费结果 |

依赖方向：Editor.Core → Scene → Runtime。Runtime 不反向引用 Editor.Core，避免循环依赖。

现有 Runtime.EditSession 的能力迁移并升级，不长期保留另一套独立实现。其编辑测试迁入 Editor.Core 测试项目；Runtime 的纯运行时测试继续保留。

## 4. 统一编辑会话

一个活动编辑文档对应一个 EditSession，持有：

- SceneDocument：唯一权威文档。
- SessionId：编辑会话身份，不写入场景文件。
- DocumentRevision：命令并发检查依据。
- 单一 Undo/Redo 历史。
- 保存内容指纹与文件关联。
- 有界请求去重缓存。
- 当前交互编辑状态。
- 主机授予的权限。

### 4.1 身份区分

| 标识 | 用途 |
| --- | --- |
| 对象 UUID | 持久对象身份、命令目标 |
| Behaviour UUID | 某个脚本绑定的身份 |
| SessionId | 防止请求误作用于其他编辑会话 |
| DocumentRevision | 判断请求是否基于当前文档 |
| World 身份与运行时句柄 | 当前运行时访问；恢复后可能失效 |

普通编辑和 Undo/Redo 不更换 SessionId，但递增文档版本。真正关闭、重新创建会话后，旧 SessionId 失效。

所有编辑命令使用 UUID，不使用运行时 GameObjectId。

## 5. 命令范围

首个编辑器接入切片先实现创建、重命名、Transform。整个 M1.2 最终覆盖以下操作：

| 命令 | 语义 |
| --- | --- |
| 创建对象 | 创建空 GameObject；空间对象通过同一事务显式添加 Transform |
| 删除对象 | 只删除指定对象及其绑定，不递归删除其他对象 |
| 重命名对象 | 校验名称并更新 |
| 设置组件 | 按已注册 TypeId、版本和 schema 校验 |
| 移除组件 | 明确移除可选组件 |
| 挂载 Behaviour | 添加绑定 UUID、类型名、启用状态和 Export |
| 移除 Behaviour | 移除指定绑定 |
| 启用/禁用 Behaviour | 更新配置，不执行脚本 |
| 修改 Export | 校验名称、类型与值 |
| 替换文档内容 | 支撑新建、打开，以及对应的撤销 |
| Undo/Redo | 恢复完整文档，不丢失脚本配置 |

约束：

- 不增加语言选择器，只支持 C# Behaviour。
- 不恢复旧格式、旧 DTO 或转换入口。
- 未知组件拒绝提交。
- 未安装的脚本类型可以保留配置；进入 Play 时再诊断，不在编辑事务中执行代码。
- 自动更新 Export 配置也必须成为可撤销命令，不能在 Inspector 绘制时直接修改文档。
- 删除保持独立的 destructive 能力，不通过普通事务绕过精确 UUID 授权。

## 6. 原子事务

初期继续使用完整文档快照，先保证正确性，不立即开发复杂的增量撤销系统。

一次事务按以下顺序执行：

1. 检查请求大小、结构和协议版本。
2. 检查会话、权限、目标 UUID 和预期版本。
3. 检查 RequestId 是否为重复请求。
4. 捕获完整 Before 文档。
5. 在临时候选文档上依次应用命令。
6. 校验组件、绑定、Export、UUID 和完整载荷大小。
7. 准备 After 快照及历史记录，检查历史预算。
8. 再次确认活动文档版本未变化。
9. 原子安装候选，递增一次文档版本。
10. 安装历史记录，返回结构化结果。

必须保证：

- 任意准备或校验步骤失败，活动文档和历史游标都不变。
- 不先修改活动 World，再尝试补救回滚。
- 无实际内容变化的命令，不新增历史、不递增版本。
- 历史安装所需数据在提交前准备，避免提交后再执行可失败的校验或扩展逻辑。
- 扩展验证器只在准备阶段运行；最终提交不执行脚本或扩展回调。
- 原子性覆盖引擎持有的文档状态，不承诺回滚验证器产生的外部 IO；验证器应为纯校验/归一化逻辑。

完整文档仍遵守现有 4 MiB 上限、所有者线程和安全更新边界。事务操作数、请求大小、历史与幂等缓存都必须有界。

## 7. Undo/Redo 与保存状态

每条历史记录保存：

- 完整 Before / After 文档。
- 命令标签。
- 原始能力及授权目标。
- 选择恢复提示：对象 UUID。
- 内存占用。
- 文档替换涉及的文件关联和保存基准信息。

初期可沿用现有预算：最多 64 条历史、总计 16 MiB。实际保留条数受文档大小影响；超限优先淘汰最旧记录，单条无法容纳时在提交前明确拒绝。预算后续依据测量调整。

Undo/Redo 规则：

- 重新检查原操作权限，不能通过 Undo 绕过权限。
- 先成功恢复文档，再移动历史游标。
- 恢复后根据 UUID 重新获取对象和选择，不复用旧运行时引用。
- 新操作提交后清除 Redo 分支。
- 外部绕过命令修改文档时，明确使历史失效；不能继续应用旧历史。
- 历史失效后的重新同步必须显式处理，不能悄悄认领外部修改。

修改标记不再只依赖历史游标。保存成功后记录完整文档内容指纹；当前内容与保存指纹不同即为 Dirty。这样历史淘汰、分支和 Undo 回到保存内容时都能正确判断。

## 8. Inspector 连续编辑

拖动 Transform 不能每帧生成一条完整快照历史，也不能继续先直接写 World、结束后补历史。

```text
激活控件 → 建立编辑草稿 → 连续预览 → 松开后一次提交
                              └── Esc：取消
```

规则：

- 名称、Transform、Export 的交互值先进入临时草稿。
- Inspector 和视口可以展示预览，但预览不修改权威文档。
- 鼠标松开或文本确认时，提交一次完整事务。
- 一次拖动对应一次 Undo。
- Esc 取消后，文档版本与历史不变。
- 切换选择、保存、打开或进入 Play 前，明确提交或取消草稿。
- 草稿开始时记录基准版本；版本冲突时不得覆盖其他修改。
- 同一文档交互编辑期间，其他写请求返回 edit_busy；只读检查返回已提交状态。
- 窗口失焦、控件中断和会话关闭应有确定的草稿结束策略。

C++ 可以持有控件临时值和绘制预览数据，但不能维护第二份权威对象数据库。

## 9. 原生桥接与旧入口退出

建议 Scene Host Bridge 升级到 v4，让旧消费者明确失败，而不是隐式继续走旧编辑路径。Gameplay Host Bridge 与原生插件 ABI 独立版本化，不因本次迁移自动升级。

新增桥接能力：

- 获取编辑会话和历史状态。
- 提交编辑请求。
- 开始、更新、提交、取消交互编辑。
- Undo/Redo。
- 保存、打开、新建文档。

保持版本化 C ABI、不透明令牌、caller-owned 缓冲、显式长度和异常转结构化错误。不得跨 ABI 暴露 STL、C++ 异常、CLR 引用或可写场景内存。

逐步退出：

- 编辑场景的直接创建、删除、重命名、绑定修改入口。
- C++ SceneCommandStack。
- C++ 自己维护的 Dirty、保存游标和 Inspector Before 快照。
- 编辑器通过完整 Restore 绕开历史的入口。

保留：

- 只读 SceneView，不增加投影恢复 API。
- 完整快照用于隔离 Play 克隆。
- Play 世界的运行时组件写入路径。

编辑命令与游戏运行时写入是不同入口。角色每帧移动不会产生 Undo，也不要求 ExpectedRevision；但它只能操作 Play 世界，不能借用运行时入口修改编辑文档。

## 10. Agent-ready 契约

M1.2 不必完成 MCP 传输，但内部入口必须能直接供后续 MCP 使用。

继续使用已有稳定能力名称：

- ncma.capabilities.list
- ncma.engine.component_types
- ncma.scene.inspect
- ncma.scene.validate
- ncma.scene.transaction
- ncma.scene.delete_object
- ncma.history.undo
- ncma.history.redo

请求包含协议版本、RequestId、SessionId、ExpectedRevision、能力名和输入。结果包含状态码、是否改变文档、版本、变更摘要和历史状态。能力必须提供简洁描述、JSON 输入/输出 schema 和 mutation-risk 分类。

既有 WorldSnapshot 契约升级为完整文档时，应显式版本化请求/结果协议，不静默改变旧消费者理解的载荷。

### 10.1 建议错误码

| 错误码 | 含义 |
| --- | --- |
| session_mismatch | 会话已失效或目标错误 |
| revision_conflict | 请求基于旧文档 |
| permission_denied | 未获得写权限 |
| target_not_authorized | 删除 UUID 未获批准 |
| invalid_input | 参数、组件或配置无效 |
| edit_busy | 存在未结束的交互编辑 |
| history_invalidated | 检测到绕过命令的外部修改 |
| history_empty | 没有可撤销或重做的记录 |
| request_id_reused | 同一 RequestId 被用于不同请求 |

权限由主机根据调用来源授予，不能相信请求 JSON 中的“我是编辑器”声明。默认只读；删除要求精确 UUID 授权。重试需要重新检查权限。

幂等缓存有界，并明确缓存淘汰后的重试处理。历史操作、文档替换与重试都不能重新执行已成功的旧命令；缓存结果应明确区分原执行版本和当前状态。

只读检查支持分页或摘要，不能默认无限量返回整个场景。未来工作线程或 IPC 回调只能排队，由文档所有者线程在安全边界执行。

## 11. 新建、打开、保存与 Play

- 新建/打开：由 C# 文件与文档服务准备候选，再经过文档替换命令提交，保留可撤销行为。
- 保存：调用现有原子文件服务；成功才更新保存指纹，不添加 Undo 记录。
- 打开后 Undo：同时恢复对应文档内容与文件关联/保存基准，避免 Dirty 判断错误。
- 进入 Play：先结束交互草稿，再从已提交完整文档建立隔离世界。
- Play 期间：第一版冻结编辑文档写入与 Undo/Redo，避免引入热编辑同步。
- 停止 Play：销毁运行会话，编辑文档和编辑历史保持不变。
- 关闭或替换会话：先结束相关游戏会话，再释放宿主令牌；避免悬挂借用引用。

Agent 的文件打开/保存授权暂不开放，先开放文档内的受控编辑能力。不暴露任意 Python 执行、程序集加载或文件路径写入能力。

## 12. 实施切片

| 切片 | 交付 | 必须通过的验收 |
| --- | --- | --- |
| M1.2-A | Editor.Core、完整文档 EditSession、权限/版本/幂等、托管历史 | 自定义组件和脚本配置不会因事务或 Undo 丢失 |
| M1.2-B | 创建、重命名、Transform 接入 ImGui；交互草稿 | 一次拖动一次 Undo，Esc 不改场景 |
| M1.2-C | 删除、组件移除、Behaviour、Export 命令 | 混合编辑 Undo/Redo 完整，删除权限精确到 UUID |
| M1.2-D | 新建/打开/保存状态、选择恢复、Play 边界 | 文件失败不改现场，Stop 不污染编辑历史 |
| M1.2-E | 删除原生命令栈与绕过入口，统一能力描述 | 活动编辑场景只有一套命令和历史，无旧格式回退 |

迁移期按功能切片切换，但同一个活动文档不能同时由两套历史负责。首批命令接入活动编辑器前，需要明确处理尚未迁移入口：迁入统一历史或暂时禁用，不能继续双写。

## 13. 验证方案

每个切片使用 Build.bat 作为 Windows 规范验证入口。

1. 先构建 NcmaCore、NcmaNative、NcmaArchitectureTests，再验证其余目标。
2. 构建 Ncma.Managed、Ncma.Editor.Core 及相关托管测试。
3. 运行 CTest、managed/native smoke、Python inspect 与回归测试。
4. 对新增代码要求零警告；不从未初始化的 shell 直接调用 Ninja。

新增测试重点：

- 事务中途失败、无变化事务和完整候选归一化后的大小限制。
- 自定义组件、无 Transform 对象、多 Behaviour 与全部 Export 的混合 Undo/Redo。
- 会话失效、版本冲突、异线程访问和验证重入。
- 重复请求、RequestId 复用、权限撤销与缓存边界。
- 删除 UUID 授权，Undo/Redo 原始权限检查。
- 拖动提交、Esc 取消、控件中断、草稿期间竞争请求。
- 历史淘汰、Redo 分支、保存失败、保存指纹和打开后撤销。
- 外部修改导致历史失效，运行时引用恢复后重新按 UUID 解析。
- Play 隔离、Stop 保全编辑历史、运行时写入不能修改编辑文档。
- 旧 SceneSnapshot、旧 .ncscene 与已移除桥接入口仍被拒绝。
- 场景、FBX、动画、编辑器启动和既有 Python 工具不回归。

## 14. 完成标准与下一步

M1.2 完成标准：

- 活动编辑文档只有一套 C# 命令服务和历史。
- 所有已支持的场景编辑动作都可撤销，不再先写场景、后补历史。
- Undo/Redo 不丢失组件、Behaviour 或 Export。
- 失败、冲突、越权和陈旧请求具有确定结果，不修改现场。
- C++ 只提交意图、展示结果和保留非权威 UI 临时状态。
- Agent-ready 能力使用同一服务，但不误称 live MCP 已实现。
- 旧格式、旧 DTO 与兼容入口保持删除状态。

推荐先执行 M1.2-A，再做 M1.2-B。完成共享编辑底座后，固定步生命周期和 live MCP 分别作为后续切片接入，不与本阶段混合重写。


## 15. 交付记录（2026-10-03）

实施顺序：先 A、再 B，完整 Build.bat Debug 通过后才开始 C/D/E；最后再次运行完整验收。

- A：新增独立 Ncma.Editor.Core 与测试项目，迁出 Runtime.EditSession；完整 SceneDocument 历史包含注册组件与全部 Behaviour/Export。权限、版本、幂等、失败原子性和外部修改失效规则沿用同一服务。
- B：ImGui 创建、名称、Transform 提交 UUID 命令；草稿预览不写权威文档，结束只提交一次。Esc/失焦/最小化取消；切换选择、文档操作、Undo/Redo、Play 前结束草稿。
- C：单对象 UUID 删除、可选组件移除、Behaviour 挂载/移除/启停、Export 与 Schema 更新均进入统一历史。绑定编辑以 set_bindings 完整配置命令提交，不执行脚本；连续 Export 编辑也使用草稿。
- D：新建/打开为完整文档替换命令；Undo 恢复 UUID 选择、文件关联与保存基准。Dirty 比较内容 SHA-256 而非历史游标；原子保存失败不改现场。Play 克隆已提交文档并冻结编辑，Stop 解冻、不修改历史。
- E：删除 C++ SceneCommandStack 源码和构建引用；编辑宿主拒绝运行态直接写入/Restore/旧文件入口。Scene Host Bridge v4、能力契约 v2；Gameplay Bridge v3、原生插件 ABI v2 不变。manifest v8 明确共享服务已经接入 ImGui、live MCP 传输尚未实现。

验证命令：Build.bat -Configuration Debug。

| 验证 | 结果 |
| --- | --- |
| NcmaCore / NcmaNative / NcmaArchitectureTests 优先构建 | 通过 |
| CTest（原生 + 托管 + 编辑器/FBX/Gameplay smoke） | 10/10 通过 |
| Runtime / Scene / Editor.Core 用例 | 13/13、25/25、30/30 通过 |
| Editor.Core Release 补充回归 | 30/30 通过 |
| managed/native ABI smoke | 通过 |
| Python inspect 与工具/动画 MCP/移除项回归 | 24/24 通过 |
| 新代码构建诊断 | 0 警告、0 错误 |

ImGui smoke 覆盖草稿拖动十次只生成一个 Undo、键盘 Esc 取消、草稿提交时切换 UUID 选择；对象列表逐行重新解析 UUID，避免同帧句柄失效；Gameplay smoke 覆盖挂载/重命名、隔离 Play、重载、Stop 和完整配置 Undo/Redo。桥接回归覆盖 Export 草稿、UUID 删除/选择、文件关联及冻结边界。

唯一场景格式仍为 .ncmascene SceneDocument JSON v1；不恢复旧 .ncscene/SceneSnapshot 兼容入口。

下一切片：M1.3 固定步生命周期与安全结构命令；随后 M1.4 活动编辑器 MCP 传输。C# 主应用入口、完整资产/动画图/UI 编辑器、Vulkan 渲染仍在后续路线图，不因 M1.2 完成而标记已实现。
