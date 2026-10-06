# M4 动画驱动动作游戏实施方案

日期：2026-10-06。基线：`f31d5915c07bcfe0d8e96f1522eb8d9d5d19a49b`。
状态：2026-10-06 用户在阅读本方案后要求逐个小阶段执行、测试通过后提交推送；
按此后续执行指令确认第2节的C#运动权威/跨域fail-stop边界。M4.1已通过K1及顺序完整Debug/Release回归，
见[M4.1交付](M4_1_DELIVERY_REPORT.md)。M4.2实际Jolt角色数值插件及C#客户端已通过K2与顺序完整
Debug/Release回归，见[M4.2交付](M4_2_DELIVERY_REPORT.md)。M4.3已接线实际角色移动/跟随相机，
验收状态见[M4.3交付](M4_3_DELIVERY_REPORT.md)；M4.4根运动及门禁见[M4.4交付](M4_4_DELIVERY_REPORT.md)，M4.5动作战斗候选见[M4.5交付](M4_5_DELIVERY_REPORT.md)。M4.6–M4.7尚未实现。每阶段通过后独立提交推送再推进。
仅“开始M4”未被当作当时未审方案的自动批准。真实Physics/角色与根运动仍按后续阶段分别验收。

M3 G4/G5的静态/片段/蒙皮切片可作为基础；G6–G9、M2人工/目标环境/性能/长稳仍开放。
允许从已烘焙普通场景与已有NCA开始，不依赖尚未交付的Prefab实例发布或Agent写工具。
M4推进不代表M3完成，不绕过用户真实素材与人工验收。

## 1 源码核对结论

以下为方案建立时的基线核对；M4.1/M4.2进展与证据按上方交付记录，不把基线缺口当当前实现状态。

- `Ncma.Runtime.World.BeginStep/PrepareStep/AbortStep` 暂存托管对象/组件，提交才增加tick；
  `Ncma.Gameplay.PlaySession.RunStep` 还协调metadata、输入消费、信号/命令receipt与插值视图。
- `ICommittedStepObserver` 在提交后、只读World下运行；失败只能Faulted，不能撤回已经提交的tick。
  不能把物理同步放到该observer里并声称它参与原有事务。
- `Ncma.Physics.PhysicsSimulation` 是独立数值服务，Physics ABI1.1仅boxes/velocity/step/copied states；
  Step/flush/read发生在一个执行路径，失败可能已有native修改。状态失效/资源关闭有现有fail-stop检查。
- Jolt SDK已有Character/CharacterVirtual，但引擎插件未导出capsule角色、sweep/raycast或contacts。
  “SDK有类型”不等于引擎已集成；用盒体速度移动不能冒充CharacterMotor。
- `ClipClock` 只观察已提交tick；M3根位移仅报告，当前片段不是Animator，也没有动作/Notify gameplay运行契约。
  旧Action实验室是隔离预览，不能当作生产动作系统直接挂到World。

## 2 已确认的核心边界

### 2.1 C#唯一运动权威

C# `MovementCoordinator` 负责每对象、每固定步的唯一最终Transform发布。
脚本/输入/动作/动画提交有界意图或根增量，不能分别写最终位置。
native角色求解/rigid-body solver返回复制数值，不拥有GameObject、组件或场景树。

| 对象模式 | 最终运动结果来源 | 写入边界 |
| --- | --- | --- |
| 无物理绑定的普通对象 | 原有C# Behaviour/System | 保留当前固定步暂存规则 |
| Static collider | Play启动时的场景值 | 初期Play中不移动；Edit修改走原Undo后重新Play |
| Dynamic rigid body | native solver状态 | C#协调器验证后唯一发布Transform |
| Character capsule | C#运动策略 + native碰撞约束结果 | C#协调器验证后唯一发布Transform |

已绑定对象的完整Transform写权限归协调器，不仅约束Behaviour，也约束普通System、批量写和结构命令。
Runtime只增加通用、可信宿主建立的组件写权威约束，不依赖Physics、Jolt、Editor或图形头。
非法写须poison整个候选步，捕获异常不能绕过；不允许脚本伪造授权token。
Scale/形状改变、teleport、绑定对象删除/加删组件在首版Play明确拒绝，后续另做受控生命周期计划，
不让可中止结构命令提前创建/删除native资源。未绑定普通对象的合法结构命令保留。

Locomotion使用输入水平速度；Attack/Dodge的root-motion模式**替换**水平locomotion，不直接相加。
首版root motion限定XZ及绕+Y旋转，重力/grounding由角色运动策略承担；全3D根运动另行验收。
配置明确rig root UUID内索引、model→collision capsule偏移与单位，拒绝未支持的缩放/奇异变换。
视觉姿态移除被抽取的root-motion通道（按采样/root基准与每mesh绑定转换），
不是仅减去碰撞后实际移动距离；阻挡时不能留下未移动的根量让可见角色穿墙。
对象Transform只应用经过碰撞约束的结果，防止同一根位移在蒙皮和对象上应用两次。

### 2.2 推荐：跨域fail-stop，不伪装solver回滚

一次成功步的拟定顺序：

1. 核对Play/session/world身份、tick T、固定量子、资源generation及body/character映射。
2. 暂存Gameplay/动作/输入/信号与MovementIntent；准备T→T+1片段区间，不推进committed clock。
3. 尽量完成所有托管预算、schema、结构、资源/权威和数值输入预检；预分配有界数值输入/输出。
4. 在唯一协调点执行一次有界native simulation quantum及角色碰撞解算。这里开始不可逆边界。
5. 验证结果的数量、session映射、sequence、finite/TRS、contact/event预算，暂存唯一Transform结果；
   完成最终World/document/input/信号/receipt/render-view准备。这里仍可能失败，不承诺无异常或原子回滚。
6. 安装World T+1及托管派生结果，消费输入并发布committed动画/动作时钟。
7. 提交后观察只读，渲染使用已提交状态/插值，不触发solver、玩法命中或根运动。

| 失败点 | 必须保留的语义 |
| --- | --- |
| native数值执行前 | 托管候选/输入消费/信号消费中止；未调用solver。Play故障要求显式恢复，私有脚本字段/IO不回滚 |
| native执行中或执行后、World提交前 | 托管tick保持T，solver可能推进/部分修改；整个耦合会话Faulted，标记domain snapshot invalid并隔离所有solver结果，不再呈现为同步快照 |
| World成功提交后 | tick T+1保留；后续观察/展示失败Faulted，不把已提交tick报告为被撤回 |

初期恢复仅Stop→关闭耦合服务→从原Edit/startup scene重新Play，重新生成全部native资源映射和session epoch。
不提供自动Resume、继续旧solver、假造sequence相等或只回写body位置的“回滚”。
耦合快照有效性独立于PhysicsSimulation自己的SnapshotValid：solver可数值成功但与World不同步。
关闭失败保留插件/resource leases、禁止强制unload；派生GPU/动画资源先关闭，再关闭physics和插件。

完整native state checkpoint/restore（包括contact、warm-start、character/broadphase等）是另一条方案，
需要新ABI、状态预算/恢复协议/性能和真实一致性测试，本期不选择。推荐fail-stop是明确可见的限制，
不是把“物理可回滚”换个名字。用户确认后才能实现上述不可逆协调路径。

## 3 小阶段和门禁

以下为各阶段约定的实现范围；实际完成状态以上方交付记录为准。各阶段先补自身测试，再按顺序完整Debug/Release Build.bat；
不使用Skip取得部署资格，不把自动事件注入/隐藏窗口当人工通过。

### M4.1 运动权威与跨域协调底座

- 用户确认第2节后冻结契约。新C#角色/动作业务模块按依赖隔离组合Gameplay、Animation与Physics；
  Runtime/Managed SDK不反向依赖该模块，Player不引用Editor/MCP。
- Runtime通用组件写权威与Play受控协调钩子，严格身份/tick/revision、owner-thread、重入和poison-step。
- 持久组件仅值/UUID/小配置；runtime body/character handle、输入队列、native pointer不序列化。
- 可注入的数值adapter/fault点先用deterministic fake证明边界；fake测试不是Jolt碰撞验收。
- 测试每个失败点、捕获非法写、陈旧/外来意图、结构绕过、输入/信号消费、已提交tick保留、
  Pause/Step/Stop/Reload与重新Play；Reload首版销毁耦合状态后重建，不能偷偷带入旧solver。

退出K1：写权威无绕过，边界失败状态/恢复测试通过，默认无物理项目不改变语义。

### M4.2 薄native角色碰撞插件

- 新版本查询提供opaque capsule/CharacterVirtual资源、bounded movement/query/contact数值；
  类型/布局/线程/资源/关闭契约先冻结，原1.0/1.1功能保留原语义，不复用body句柄当角色句柄。
- C++只做Jolt数值/安全验证，无World/Gameplay状态或脚本回调。C#决定地面策略、运动模式、调度与诊断。
- 初始角色数上限32，沿用4096 bodies上限；预算是拒绝阈值，不是性能承诺。
- capsule/floor/wall/slope/ceiling、grounding/sliding、step配置、碰撞mask、query及contacts的坐标/顺序/容量定义。
  分别验证moving obstacle、角色对角色顺序、穿透/高速输入；未支持项明确拒绝。
- 保留Box2D独立测试，不把3D角色策略强加给2D。新增ABI短表/版本、foreign/stale、边界/预算、
  owner-thread、部分执行fail-stop及32次资源生命周期测试。

退出K2：实际Jolt角色数值案例、关闭guard与旧Physics回归通过；不是可玩角色完成。

### M4.3 固定步角色移动与相机

- 可信启动注册Character/collider配置与场景映射、K1调度实际K2；默认physicsEnabled=false不变。
- C#输入→MovementIntent→运动策略→碰撞结果→唯一Transform。movement/gravity/jump/grounding、
 朝向和相机跟随分开，render interpolation不写simulation或补做Physics Step。
- Play隔离Edit；30/60/144Hz渲染与headless相同固定步输入序列验证，不要求跨CPU solver位级相同。
- floor/wall/坡面、失焦/Pause/Step、无物理配置、失败/Stop/重启、角色native资源释放。

退出K3：Editor/Player中的角色实际移动/转向/碰撞；无穿透默认兜底或reference画面冒充。

### M4.4 根运动接入

- 从真实rig/clip的未wrap时间区间计算有界根增量；loop跨边界、多圈、终点、speed/暂停/换clip均明确定义。
- 预备区间只读immutable lease，成功tick才消费。与M3 committed clock并存但无第二份时钟权威；
  render补帧不得重复移动。body-bound Transform禁止脚本/动画二次写入。
- XZ/Yaw模式、角色朝向/模型偏移、碰撞后的实际移动与动画欲移动分开诊断；视觉根去重。
- 根位移oracle、wall阻挡/滑动、loop/end、不同帧率、故障未消费/Stop重建、重导入pin旧代。

退出K4：真正动画驱动且碰撞约束的运动；不同于当前“根位移报告”。

### M4.5 动作状态、Notify与战斗

- 新持久动作定义用UUID/clip引用/明确schema，C# Idle/Run/Attack/Dodge状态与输入buffer、
  cancel/连击窗口、tick区间Notify、攻击/无敌窗口。不是M6节点图或旧Action实验室兼容别名。
- 命中使用K2有界query，按attack instance/target/tick去重；只暂存命中/伤害/信号到同一步。
  native callback/AI/网络不写World，旧步/重试/渲染不能重复产生伤害。
- Notify采用明确半开区间与稳定事件ID；loop、开始/终点、中断/重新进入、catch-up和Pause边界专项测试。
- 动作参数更改的authoring走已有Editor.Core事务/资源participant；没有安全写流程时不暴露MCP修改入口。

退出K5：动作/中断/连击/命中/无敌语义真实运行并可验证。单一sausage clip不能当成真实攻击/闪避素材验收。

### M4.6 调试、只读AI检查与样例

- Debug视图复制已提交session/tick、状态/时钟、desired/accepted motion、grounding/contact/事件数与故障。
- 有界只读MCP检查使用已批准会话/对象范围、稳定名称、闭合输入/输出schema、risk分类；
  不暴露native handle、私有路径、solver step/任意输入注入或AI直接移动World。
- 发布可启动的普通场景/角色配置样例，独立Player同业务路径；当前NCP1打包支持新增组件的
  typed资源引用和预检，不借此声称M3 cold cook/正式导出完成。

退出K6：自动只读协议/范围与样例通路通过；真实第三方可见客户端/窗口另有人工清单。

### M4.7 联合回归和真实可玩验收

- 完整Debug→Release：保留M1–M3/ABI/Physics/格式/Python/部署恢复；新代码零警告。
- 32轮创建/Play/Stop/失败重启和资源/作业/队列恢复基线；没有真实硬件故障只能记注入证据。
- 0/1/8/32角色，在锁定fixture/hardware/driver/分辨率及warmup/32样本下测simulation、动作、
  root sampling、solver/query、提取/upload/GPU、GC与native live counters；先测再与用户确认预算。
- 用户真实角色的idle/run/attack/dodge片段、root配置、碰撞尺寸、许可和预期画面需提供；
  自动synthetic/现有FBX fixture不代替该素材验收。
- 真机移动/转向/攻击/闪避/障碍，输入/DPI/Play-Stop/重载、第三方只读检查、目标机Player与一小时工作流。

退出K7：上述约定证据齐全才关闭M4；未完成项逐项开放，且不顺带关闭M3 G6–G9/M2待验门禁。

## 4 执行顺序

先确认第2节 → M4.1 → M4.2 → M4.3 → M4.4 → M4.5 → M4.6 → M4.7。
M4.1协调底座和M4.2独立真实数值接口已通过；M4.3应用接线已实现，完整K3门禁见交付记录。
显式启用物理且具有绑定组件的Editor/Player Play派发实际数值步；M4.4显式root组件候选接入
碰撞约束运动/共享committed clock/视觉去重，未更改默认physicsEnabled=false，验收以交付记录为准。
未删除数值内核/SDK、旧策略原型或用户数据，没有引入旧类型兼容层。
