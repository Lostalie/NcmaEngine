# M1.3 详细实施方案：固定步 Play 会话与运行态安全边界

更新日期：2026-10-04。状态：A–F 代码与自动接线已交付；第 1–13 节为原始方案，第 14 节是历史 A/B 门禁，第 15 节为本次 C–F 交付。人工 UI 联合验收尚待完成。

前置条件：M1.1/M1.2 已完成。实施顺序：A → B → 完整测试门禁 → C → D → E → F。导航：[M1 剩余总览](M1_REMAINING_IMPLEMENTATION_PLAN.md)、[M1.4](M1_4_IMPLEMENTATION_PLAN.md)、[最终验收](M1_ACCEPTANCE_PLAN.md)。

## 1. 阶段目标与代码基线

目标是让当前 C++/ImGui 编辑器真正通过 C# PlaySession 驱动固定步游戏逻辑，并将异常、输入、结构修改和生命周期收敛到同一安全边界。

当前代码证据：

| 位置 | 已有基础 | 本阶段缺口 |
| --- | --- | --- |
| managed/Ncma.Runtime/WorldRunner.cs | 有界固定步、Systems 顺序、Faulted、严格超预算拒绝 | 活动 Play 未接入；缺统一时间读模型与交互追赶策略 |
| managed/Ncma.Managed/Behaviour.cs | OnFixedUpdate 已声明 | 宿主尚未派发；OnUpdate 仍承担可写玩法 |
| managed/Ncma.Managed.Host/NativeEntry.cs | 程序集、脚本实例、生命周期、Tick | 业务集中在静态宿主；预检/激活/重载/错误状态未统一 |
| managed/Ncma.Managed/WorldAccess.cs | 暂存组件与信号基础 | Commit 后信号分配、消费即删除等路径尚非联合原子提交 |
| managed/Ncma.Scene/SceneDocument.cs | 唯一 World 与脚本配置 | 删除后元数据惰性清理不等于运行结构命令原子性 |
| engine/source/editor/EditorApplication.cpp | Play 副本、编辑冻结、渲染帧 Tick | C++ 包裹 Begin/Commit/Abort，暂停状态与托管宿主分散 |

不修改场景文件格式；不同时重写应用入口、物理框架、Animator 或资产系统。

## 2. 模块、依赖与所有权

建议新增 managed/Ncma.Gameplay，专门容纳 PlaySession、BehaviourDispatcher、运行命令与游戏会话服务；该项目已在 A/B 建立，原始职责规划中的结构命令/输入服务仍待后续切片。

~~~text
Ncma.Managed.Host → Ncma.Gameplay → Ncma.Managed（玩法 SDK）
                                → Ncma.Scene → Ncma.Runtime
Ncma.Editor.Core                 → Ncma.Scene → Ncma.Runtime
C++/ImGui 外壳                   → 版本化 Host Bridge → C# 会话
~~~

- Runtime 保持独立，不依赖 Behaviour、窗口、Editor.Core、Python 或 IPC。
- Managed SDK 可依赖 Runtime，但不能反向依赖 Gameplay；时间/输入/命令接口在 SDK 定义，由 Gameplay 注入实现，避免循环引用。
- Scene 拥有配置元数据，不另建组件存储；Gameplay 使用该 Play 文档的同一个 World。
- Host 保留 CLR/ALC 引导、版本检查、参数编解码与异常转换，业务转交会话实例。
- 一个 PlaySession 拥有一个 WorldRunner；只有它推进固定步，外壳不得调用 Begin/Commit。
- 物理/动画后续通过 System 或受控阶段适配器接入，不在 M1.3 伪造“已经物理同步”的能力。

同一 owner thread 调用控制入口、调度器、World 和脚本。异线程/重入请求必须先拒绝；后台线程只能提交有界意图，不能执行实例回调。

## 3. 会话状态与操作规则

| 状态 | 允许操作 | 必须保证 |
| --- | --- | --- |
| Stopped | Start | 没有 Play 实例、积累时间、信号或运行命令 |
| Starting | 内部准备；失败清理 | 不发布半初始化会话，不提前损坏编辑态 |
| Running | AdvanceFrame、Pause、Stop、受控 Reload | 不重入；固定步是唯一模拟推进入口 |
| Paused | Resume、Step、Stop、受控 Reload | 不积累墙钟时间，普通帧不调用玩法回调 |
| Reloading | 内部预检/清理/激活 | 不执行普通帧；禁止修改调度集合 |
| Faulted | Inspect、Stop、Restart | 不自动继续，不将故障仅包装为 Paused |
| Stopping | 内部清理 | 尽力释放所有实例，聚合错误后仍解冻编辑态 |

Start 从已提交编辑文档克隆配置与组件；对象持久 UUID 保持，World 身份与运行句柄重新分配。构造器、Export 注入、OnCreate/OnEnable 在隔离候选上完成；成功后才发布 PlaySession 并冻结编辑。启动回调若需写入，使用独立初始化暂存/提交，不伪造一个固定步 Tick。

Stop 不把运行结果复制回编辑文档，不修改 Undo、文件关联、保存指纹或选择；重复 Stop 无副作用。Restart 先完整停止，再从编辑文档重新创建，生成新的 PlaySessionId，不能继续使用旧输入/命令令牌。

## 4. 生命周期函数与可写范围

这是建议的新语义，属于需要迁移样例与宿主版本的行为变更，不应静默保留帧率相关写入。

| 回调/入口 | 时机 | 允许的 World 操作 |
| --- | --- | --- |
| OnCreate | 初次构建；运行时新绑定在下一安全生命周期边界 | 初始化暂存；运行时遵守固定步规则 |
| OnEnable | 创建后且启用，或禁用 → 启用 | 初始化/固定步暂存；不直接递归创建对象 |
| OnFixedUpdate | 每个成功尝试的固定步一次，限启用实例 | 写已附着组件、排队结构命令、发送/消费信号 |
| OnUpdate | 非暂停 Running 帧固定步处理后一次 | 只读 World；允许自己的呈现状态，不改组件/结构/信号 |
| OnDisable / OnDestroy | 安全生命周期边界或 Stop 清理 | 只读清理上下文与私有资源；不再发起 World 修改 |
| 注册 System | 固定注册顺序、固定步一次 | 与 OnFixedUpdate 相同；注册变更只在安全边界 |

禁用实例仍完成一次 OnCreate，不调用 OnEnable/OnFixedUpdate/OnUpdate，直到启用。每个实例按标记记录已创建、启用状态与回调尝试，保证失败后不自动重复调用同一生命周期回调。

运行时删除/解绑提交后，旧实例不再参与普通更新；清理回调使用保存的对象 UUID/绑定身份与只读清理信息，不能要求已销毁 GameObject 句柄仍有效。失败清理不会复活对象。

脚本调度按对象 UUID、绑定 UUID 的固定排序执行；Systems 按可信注册顺序执行。重复身份拒绝。不依赖 Dictionary 枚举顺序或程序集扫描顺序。相同输入、UUID 与构建才可比较确定性，不承诺跨 CPU 浮点逐位相同。

## 5. 一帧与一步的确切顺序

~~~text
AdvanceFrame
  → 校验会话/线程/dt/输入序列/状态
  → 计算本帧时间预算与 0..N 个固定步
  → 每步：生命周期待办 → Behaviour → Systems
           → 联合 Prepare → 无回调 Install → Tick + 1
  → OnUpdate（只读 World，每个 Running 帧至多一次）
  → 提取复制的 RenderFrameView
~~~

每步读取已提交数据；组件写入暂存，本步后续 Behaviour/System 不会自动读到别人尚未提交的写入。同一目标允许确定顺序下的最后一次合法组件提交覆盖前值；批量 API 内重复目标仍按既有规则拒绝。

需要物理写回/动画后处理可见性时，后续阶段必须显式新增受控阶段，不允许本阶段默默改成“即时写即可见”。

联合提交提前准备组件、结构、SceneDocument 元数据、实例调度表、信号收发结果、输入消费游标、回执、渲染缓冲区及 Tick/Revision 溢出检查。Install 不进行用户回调、校验器、序列化或可能扩容的容器写入；使用已准备的存储替换/安装。

最后一步提交后调用 OnUpdate。它若抛异常，会话 Faulted，但已成功提交的固定步保留，不把整个渲染帧回滚。

## 6. 时间策略

### 6.1 默认值与两种策略

建议默认 h = 1/60 秒，MaxStepsPerFrame = 8；最大接受帧 delta 为 0.25 秒，仅用于交互策略。

- 严格策略：继续保留 WorldRunner 原有超预算预检拒绝语义。dt + accumulator 超预算时，在状态改变前拒绝，不丢时间，不部分推进，适合 headless/重放测试。
- 交互策略：编辑器先校验有限非负 dt；将超过 0.25 秒部分计入 DroppedSeconds。加上 accumulator 后最多执行 8 步，超出的整步债务明确丢弃，保留不足一步的小数余量。
- WorldRunner 的严格 Advance 不被静默改成丢时模式；PlaySession 使用显式策略入口/配置将两个行为区分。
- 每帧及会话累计丢时量都可查询。输入事件的消费规则不依赖被丢弃的模拟时间。

定义 acceptedDelta = min(dt, 0.25)，初始欠时 total = accumulator + acceptedDelta。交互模式应执行 min(floor(total/h), 8) 步；仅在计划执行的步全部成功后丢弃多余整步并保留余量。失败不继续推进，返回成功步数、故障步及时间诊断；不能把未执行步骤描述为成功消费。

### 6.2 暂停与单步

Pause/Resume 只在安全边界生效；暂停清除积累时间和瞬态输入，恢复不补暂停期间时间。Step 只允许 Paused，恰好执行一步 h，不调用 OnUpdate，渲染显示最新提交值；失败转 Faulted。

FrameCount、SimulationTick、SimulationSeconds、Accumulator、InterpolationAlpha、本帧 StepsExecuted、DroppedSeconds 分别报告。SimulationTick 只在固定步成功后增加；创建对象、Start 初始化、暂停帧不能冒充模拟步。

拒绝 NaN、Infinity、负 delta、非法固定步参数、累计溢出、重入与异线程调用。拒绝的控制请求不改变有效状态；尚未支持时间缩放、倒带、跳时间和网络回滚。

## 7. 输入快照与渲染读模型

### 7.1 输入

C++/GLFW 收集输入，传入版本化固定布局/有界复制快照；SDK 不依赖 GLFW。M1 最小集合为 Held、Pressed、Released、PointerDelta、FrameSequence、PlaySessionId 与焦点状态。

- 一个渲染帧没有固定步时，边沿事件保留到下一个成功固定步。
- 同帧多步时，瞬态事件只供第一成功步消费；Held 在各步持续可读。
- 按键在没有固定步的两个帧间完成按下/释放时，两种边沿都保留。该版快照不承诺恢复任意次数的完整事件顺序，需此能力时使用未来有界事件流。
- 鼠标增量有界累加并按同样提交游标消费，拒绝非有限值和序列回退。
- 固定步失败不提交消费游标；会话进入 Faulted，不自动再次执行同一事件。
- 暂停、失焦、Resume、Stop/Restart 清除瞬态与陈旧输入；重新同步 Held，避免卡键与恢复后误攻击。
- ImGui 捕获键鼠时不向玩法发送被 UI 消费的动作；单步按钮不能意外同时产生游戏输入。

本阶段不实现完整 Action Map、手柄适配或玩家多设备管理，接口留在 C# 输入服务。

### 7.2 插值

GameObject 中有 Transform 的对象提取 previous/current 两个已提交值；位置/缩放线性插值，旋转归一化四元数短弧插值，alpha = accumulator/h。

新对象 previous = current，删除对象不再出现在视图；暂停/单步/重载重置到 current，避免视觉拖尾。无 Transform 对象仍是无空间组件对象，不能为了绘制自动挂组件。

RenderFrameView 带 PlaySessionId、Tick、序列号和有界对象数据；它只读、不能成为第二个 World。插值结果不写回权威 Transform、不改变文档或历史。C++ 获取数据是复制或明确有生命周期的只读缓冲，不得持有 CLR 引用。

只接入当前视口/参考预览可消费的数据；不据此宣称 FBX 场景 GPU 蒙皮或完整场景渲染已经完成。

## 8. 运行态结构命令与联合原子性

### 8.1 操作与句柄

建议 RuntimeCommandBuffer 首版支持：SpawnEmpty、Destroy、Rename、Add/RemoveRegisteredComponent、Attach/RemoveBehaviour、SetBehaviourEnabled。只接受可信注册组件/已加载脚本类型，不能加载任意程序集或 eval。

Spawn 先返回会话限定的 CommandToken 与保留 UUID，不返回尚不存在的 live GameObject。后续命令可以按保留 UUID 配置它，查询对象须等成功回执。

建议每步最多 1024 个命令、总 payload 1 MiB、回执最多 1024 项；对象/组件/绑定数量仍受场景已有上限约束。达到任一上限先拒绝/使本步失败，不能无限增长或静默截断。实际值在测试后锁定并公布，不是性能承诺。

### 8.2 冲突规则

- 命令按 FIFO 建立候选；同 UUID 重复创建拒绝。
- 创建 → 配置/挂载允许；删除 → 修改/挂载拒绝；删除同目标两次拒绝。
- 创建后同批删除允许归一化为空对象结果，不激活临时脚本，仍给出明确回执。
- 添加已有组件/移除不存在组件采用明确错误，不暗中切换成 Set；Set 已附着组件继续走暂存组件 API。
- 既有组件 Set 后又 Remove/Destroy，最终候选按后续结构删除移除写入；Remove 后的后续 Set 拒绝。该顺序由内部操作序列记录，不能只看最终两份独立字典猜测先后。
- 目标属于旧会话/其他 World、陈旧句柄、只读 OnUpdate、非法阶段或异线程请求先拒绝。
- 任一后置命令校验失败，整个当前固定步引擎状态不安装；既有步骤的成功结果保留。

### 8.3 实现策略

在 Runtime 新增仅内部可用的 PreparedStep/结构准备机制；准备触及对象条目、组件存储和 UUID 索引，预留资源后安装。不得每次 Spawn/Destroy 都恢复整场景快照，否则会令所有存活句柄失效并引入不必要成本。

保持 World.Identity 与未删除对象的 runtime ID；新 ID 单调分配、不复用已发布 ID。失败准备不泄露可用对象句柄，删除引用明确失效。准备时 UUID 查找与组件验证只访问隔离候选，验证器纯函数、不得重入修改。

SceneDocument 绑定元数据、Behaviour 调度表与 World 必须同一提交，不在 World 删除后再依赖 ObserveWorld 惰性修补元数据。SDK 信号缓冲、结果回执与输入消费也预先准备，不能在 Commit 之后再 AddRange 扩容。

Engine-managed 原子性不包括构造器、校验器违规 IO、脚本私有字段或外部服务。注册可信脚本/校验器不是沙箱。构造失败可使本步失败；外部副作用需脚本自己设计为可取消/幂等。

## 9. 生命周期、信号与异常

结构命令成功安装后，新实例 OnCreate/OnEnable 与旧实例清理记录进入下一固定步的生命周期队列，不能在结构安装过程中递归回调。新实例初始化成功后可参加该步 OnFixedUpdate；旧实例已不参加普通更新。Stop 即使没有下一步也要清理已发布与待清理实例。

生命周期代码运行前记录“尝试过”；失败进入 Faulted，不自动重试。OnDisable/OnDestroy 尽力执行每个应清理实例，异常聚合，仍释放资源。私有状态若已部分改变，Restart 才是默认 UI 恢复方式。

信号设计沿用有类型的 mailbox：

- 本步发送的信号下一成功步可见，不当作同一步即时跨脚本调用。
- 本步读取采用暂存消费游标；成功提交后删除已消费项，失败保留引擎 mailbox。
- 可用信号、待发送信号和联合数量均有界，现有 4096 上限不能通过多份队列绕过。
- 会话 epoch 与信号序号防陈旧重放；允许中止产生序号空洞，不复用已发出的序号。
- 消费失败可恢复 mailbox，但不能撤销脚本已经复制到私有字段的数据。

FaultDiagnostic 至少包含阶段、PlaySessionId、WorldId、已提交 Tick、尝试步、对象 UUID、绑定 UUID、可信类型名、稳定错误码与有界消息。不得跨 ABI 抛异常。

本步失败中止暂存写入/结构/信号，不改该步 Tick；本帧此前成功步骤仍存在。低层 WorldRunner.ResetFault 保留显式恢复用途，但 UI 不提供“忽略异常直接继续”；默认仅 Stop/Restart，并提示私有状态/IO 未回滚。

## 10. 程序集重载

重载不等于热状态迁移，不宣称保留 Behaviour 私有字段。

编辑态先加载候选程序集并验证类型身份、可信程序集目录、Export 名称/类型/值与绑定可实例化性；预检失败保留原 catalog，不先 UnloadCore 再发现错误。

运行态 Reload 在安全边界暂停，准备候选并基于当前已提交 Play 配置与组件重新绑定。预检失败可保留旧会话 Paused；开始旧实例清理后已跨不可恢复边界，候选激活失败转 Faulted，不能假装原实例可完整恢复。

成功后清除运行命令/瞬态输入/积累时间，重建插值为 current，默认保持 Paused，用户显式 Resume。编辑文档和历史不变。ALC/Behaviour 实例相关令牌换 generation，旧回执和引用不可用于新实例。

共享 SDK/Runtime/Scene/Gameplay 契约程序集必须回到同一主加载上下文；不能只共享 Ncma.Managed 却在私有 ALC 再载入一份 World/接口类型。白名单精确按身份匹配，不把任意插件都共享到主上下文。

清理引擎持有的实例、Systems、事件订阅、委托、读视图及缓存。测试使用 WeakReference 和有界 GC 验证可卸载；生产不靠无限 GC 等待。外部线程/静态订阅可能阻止卸载，需诊断，不宣称任意用户代码都可强制卸载。

## 11. 六个小阶段

### M1.3-A：PlaySession 与唯一所有者

任务：新增 Gameplay/Tests 项目与依赖；从 NativeEntry 提取会话状态/实例管理；Start/Stop 隔离；接入唯一 WorldRunner；新增 AdvanceFrame/控制/状态桥接，撤除 C++ 外部阶段调用。

交付：可测试会话、Host 薄适配、native DTO 布局/版本断言。即使暂保留内部调度适配器，也不能保留两个 phase 所有者。

测试：空文档、无 Transform、多绑定、启动失败、重复 Start/Stop、旧会话/外线程/重入拒绝、编辑文档/历史保全、ABI 不匹配拒绝。此阶段不是完整固定步功能完成。

### M1.3-B：固定步 Behaviour 与时间/故障

任务：派发 OnFixedUpdate；OnUpdate 只读；RotatorBehaviour 改用固定步；实现严格/交互时间策略、Pause/Resume/Step、Faulted 与结构化诊断。

测试：30/60/144 FPS 相等累计模拟时间下 Tick/结果一致到容差；0/N 步；输入错误不改状态；超预算；暂停不欠时；失败步不提交、此前成功步骤保留；OnUpdate 写入拒绝；渲染帧不等于 SimulationTick。

门禁：A+B 完成后必须运行完整 Build.bat Debug 与全部既有回归，未通过不得继续 C/D/E/F。原 smoke 的大 delta Tick 用例要迁移为明确策略/多次固定步，不机械保留旧 .5 秒帧写语义。

### M1.3-C：输入与插值

任务：输入 SDK/bridge、焦点与 ImGui 捕获；边沿暂存消费；previous/current Transform 读视图；视口接线。

测试：无步保留、多个步骤只消费一次、快速按下/释放、鼠标累加、失焦/恢复、防卡键、陈旧序列、暂停/Step/重载、新建/删除/无 Transform、四元数/alpha 合法、渲染不修改 World。

### M1.3-D：运行结构命令

任务：命令/回执类型；差量 PreparedStep；顺序与容量检查；World/配置/调度/信号/输入联合安装；阶段访问守卫。

测试：前述冲突表全部覆盖；候选最后一条无效导致全步失败；验证器失败/重入；普通存活引用保留、删除引用失效；容量边界；组件 Set 与结构移除顺序；准备失败不留下绑定/对象/成功回执。

### M1.3-E：生命周期、信号与重载

任务：生命周期队列与尝试标记；信号事务消费；清理诊断；catalog 预检、ALC 共享/释放、重载安全边界。

测试：禁用初始绑定、Attach/Enable/Disable/Remove 顺序；回调抛异常；Stop 清理聚合；发送/消费失败；重载预检失败保持旧 catalog；激活失败 Faulted；重复重载/关闭；弱引用释放；私有字段不迁移且不误报回滚。

### M1.3-F：编辑器接线与旧入口清理

任务：Play/Pause/Resume/Step/Stop/Restart 全部提交托管控制意图；显示 Tick/alpha/丢时/故障；移除外壳 m_ScenePaused 决策权与旧 Begin/Commit/Tick 旁路；更新样例、构建、文档和 manifest。

测试：原生编辑器 smoke、ImGui 手工控制与最小化/失焦；编辑被冻结/Stop 解冻；完整脚本 Export Undo；FBX/动画独立预览不回归；旧 bridge 拒绝；所有已发布入口只剩一个模拟所有者。

完成条件：全链路测试通过才声明 M1.3 已实现，保留 M1.4/M2+ 未实现状态。

## 12. 文件与版本影响清单

| 文件/模块 | 计划处理 |
| --- | --- |
| managed/Ncma.Gameplay/、Ncma.Gameplay.Tests/ | 新增会话、派发、时间/输入/命令/诊断测试；具体类名允许实施时细化 |
| managed/Ncma.Runtime/World.cs、WorldRunner.cs | 只读/准备状态守卫、差量结构提交、明确时间策略扩展 |
| managed/Ncma.Scene/SceneDocument.cs | 运行结构元数据联合准备，保留文件格式与编辑全量恢复语义 |
| managed/Ncma.Managed/Behaviour.cs、SceneWorld.cs、WorldAccess.cs | SDK 上下文、输入/时间/命令接口与信号事务边界 |
| managed/Ncma.Managed.Host/NativeEntry.cs、SceneEntry.cs | 托管会话接入、薄桥接、ALC 预检/清理 |
| engine/source/runtime/interop/NcmaGameplayBridge.h | Gameplay Bridge 3 → 4，固定布局复制结果、所有权说明 |
| engine/source/runtime/script/runtime/DotNetGameplayRuntime.* | 版本拒绝与新控制入口，不持有玩法权威 |
| engine/source/runtime/scene/ManagedSceneClient.* | Scene Bridge 4 → 5；删除外部 gameplay phase 入口 |
| engine/source/editor/EditorApplication.cpp | 输入、控制意图、只读状态与视图绘制 |
| managed/Ncma.Gameplay.Sample/RotatorBehaviour.cs | 固定步示例与公开语义说明 |
| CMakeLists.txt、scripts/Build.ps1、相关测试工程 | 纳入新增项目、部署依赖与 CTest/桥接回归 |
| docs 与 python/src/ncma_tools/manifest.py | 仅交付后更新真实状态；原生 ABI 2、文件 JSON v1、能力契约 2 不因此改变 |

旧 API 删除前搜索全部引用、迁移调用者和测试；不删除无关 SDK、用户资源、其他预览系统或 IDE 文件。

## 13. 最终验收与后续边界

- Debug 完整验证通过，Release 至少运行完整阶段回归；新增代码零警告/错误。
- 一个 PlaySession/WorldRunner/World；C++ 没有另一套模拟 Tick 或暂停状态权威。
- 固定步、输入、结构、信号、异常、重载与 Stop 各有可重复自动化证据。
- 编辑历史、文件关联、Dirty 和完整脚本配置无回归；运行行为不写编辑文档。
- 运行内存随命令/信号/重载次数有界，不以全量 Restore 作为每步结构修改实现。
- 性能记录 0/1/8 步及 0/64/1024 命令的时间/分配，作为后续优化基线；不虚构固定 FPS 或零分配目标。
- 完成后才能进入 M1.4；完整物理/动画调度、动作游戏、网络回滚与 C# 主入口仍未实现。


## 14. M1.3-A/B 交付记录（2026-10-04）

### 实际完成

- 新增独立 Ncma.Gameplay 与 Ncma.Gameplay.Tests；PlaySession 持有脚本实例、生命周期和唯一 WorldRunner，Host 保留程序集反射/Export 注入和薄 ABI。
- BeginScene 在 C# 一次配置/激活全部绑定；C++ 不再遍历实例逐个调用 CreateBehaviour/SetProperty/ActivateScene。
- 固定步派发 OnFixedUpdate/顺序 Systems，帧后 OnUpdate 只读；SDK 组件/结构/配置/信号及释放入口遵守只读守卫。RotatorBehaviour 已迁移。
- 初始化暂存组件提交但不增加 World.Tick；创建/启用失败清理已尝试实例；禁用绑定只调用创建与销毁。
- 严格模式保留超预算预检拒绝；交互模式默认 h=1/60、最多 8 步、接受 delta 上限 0.25 秒，报告本帧/累计丢时与余量。非法时间与计数溢出先拒绝。
- Pause/Resume 清空欠时；Step 仅 Paused、恰好一次固定步且没有 OnUpdate；Faulted 不自动继续，保留此前成功步，Stop 后重新 Play。
- Gameplay Bridge 4 的状态 POD 为 120 字节，包含 session/world UUID、状态、FrameCount/Tick、步数、固定时间、积累/alpha、丢时与故障标志。Advance/Control/EndScene 校验实际 Play UUID/scene token；活动 Play 阻止无范围加载/卸载。
- Scene Bridge 5 删除外部 16/17/18 阶段控制；原生对绑定 Play 的直接修改被拒绝。旧 unmanaged Tick/CreateBehaviour/SetProperty/ActivateScene/GetTickCount 不再导出；IScriptRuntime 的 native Tick 仅转调新 AdvanceFrame，不拥有阶段。
- ImGui 接入托管 Pause/Resume/Step 和 Tick/Fault 提示，移除原生 m_ScenePaused 决策状态；空场景也建立真实 Play 会话。
- Build.bat、CMake/CTest、程序集部署与 NcmaEngine.sln 纳入新增项目。manifest 升为 9，native ABI 2、动画/角色 ABI 1、能力契约 2、场景 JSON v1 不变。

### 验证证据

规范命令：Build.bat -Configuration Debug。

| 验证 | 结果 |
| --- | --- |
| NcmaCore / NcmaNative / NcmaArchitectureTests 优先构建 | 通过 |
| CTest 分组完整集合 | 11/11（原生 3 + 托管/编辑器 8）通过 |
| 新 Gameplay Debug 用例 | 31/31 通过 |
| 新 Gameplay Release 补充回归 | 31/31 通过 |
| Runtime / Scene / Editor.Core 回归 | 全部通过 |
| managed/native ABI smoke | 通过 |
| 编辑器 / Gameplay / FBX smoke | 通过 |
| Python inspect 与独立动画 MCP/工具回归 | 24/24 通过 |
| 新代码最终构建诊断 | 0 警告、0 错误 |

Release 补充命令：dotnet run --project managed/Ncma.Gameplay.Tests/Ncma.Gameplay.Tests.csproj --configuration Release --nologo。它不是完整原生 Release 验收；后者保留给 M1.3 全阶段/M1 最终门禁。

### 该次 A/B 交付时尚未完成（后续已推进，见第 15 节）

- C：GLFW 输入快照/边沿消费、Transform previous/current 插值与真正 RenderFrameView。
- D：运行结构命令、差量准备、World/绑定/调度/回执联合原子安装。
- E：信号事务消费、安全生命周期结构队列、catalog/重载预检、完整卸载压力验收。当前发送仅提前预留容量，消费仍即时删除；不声称信号消费回滚。
- F：完整状态/诊断/Restart UI、后续切片桥接收尾与全阶段验收。已有 A/B 必需控制接线不意味着 F 已全部交付。
- live 编辑器 MCP、C# 应用主入口、完整动画/物理/角色与资产流水线仍未实现。

A/B 门禁通过后，本次在该切片边界交付；下一步为 M1.3-C，不同时开启 M1.4 或重写主入口。


## 15. M1.3-C/D/E/F 实际交付（2026-10-04）

- C：SDK IGameplayContext/InputState 与 Host 240 字节 InputFrame；GLFW 事件保留同 poll 的按下/抬起，按焦点/ImGui 捕获过滤；零步累积，成功固定步一次消费。Pause/Resume 清欠时与瞬态，暂停帧更新 Held。RenderFrameView 持 previous/current、位置/缩放 lerp 与归一化 quaternion slerp，无 Transform 不输出，插值不回写。
- D：IRuntimeCommands，1024 项/1 MiB，预留 UUID/滚动回执；创建、删除、命名、组件、绑定操作按 FIFO 修改候选。World 差量准备、Scene 元数据、实例调度、信号/input/render/receipts 联合准备后安装；保留存活 World identity/运行 ID，不每步 Restore。捕获到的非法命令/组件写入仍使步失败。
- E：信号消费与发送事务化，失败不半消费、Restore/重载 epoch 更新；清理只读、删除 tombstone；新实例安装不执行生命周期，下一步激活。可信构造器应无 IO/资源副作用，未调用 OnCreate 的候选不会调用 OnDestroy，不承诺回滚构造器或私有字段。
- 隔离 collectible ALC/catalog 预检，成功再发布；预检失败旧实例 Paused，开始不可逆清理后的激活失败 Faulted。成功更换 session epoch、输入/邮箱/回执与私有实例，保留组件/Tick，结束 Paused。旧 Systems 引用在成功重载时替换。
- F：ImGui Pause/Resume/Step/Restart、Tick/alpha/dropped/Fault 与 reference preview 只读插值接线；Host 负责元数据/bootstrap，PlaySession 仍独占模拟。Gameplay bridge 5（status 120B）；Scene bridge 6；native ABI 2、动画/角色 1、JSONscene 1、capability 2、manifest 10。

证据：G2 完整 Debug 12/12 CTest、Gameplay 51/51、Python 24/24，out/verification/m1/g2-debug.log；
后续补捕获失败/AttemptTick 与 64 次会话资源回收用例，Gameplay 已为 53/53。collectible catalog 12 次弱引用释放、Native Host 12 次活动重载通过。
0/1/8 步与 0/64/1024 命令的 32 样本/8 预热时间及分配记录：
out/verification/m1/g2-profile-debug.log；最终双配置证据见 M1_DELIVERY_REPORT.md。

限制：字典/装箱/序列化仍有分配，不是生产 ECS；零步不会执行结构命令。只提供 Transform reference preview 插值，不是完整场景 renderer/FBX GPU 蒙皮。私有状态迁移、Animator/CharacterMotor、物理批量调度及 C# 主入口仍未实现。
