# M6 可视化动画系统实施方案

M6 面向真实 FBX 动作角色，交付可保存、可验证、可撤销、可由 Agent 获批修改的动画图。
C# 负责资产、参数、状态机、时间与事件；C++ 只执行姿态数值与 GPU 蒙皮。参考 UE 的工作流，
不复制 Actor/Blueprint 架构，也不承诺 UE 资产兼容或未经测量的性能优势。

2026-10-07 用户授权按小阶段实施：完成后测试，失败修复并重测，通过后提交推送、核对远端 SHA，
再推进下一阶段。M4 K7、M5 后续 HUD/组件、人工窗口/DPI/IME/MCP、目标环境、性能和长稳门禁
继续开放；本授权允许动画候选开发，不将它们标为通过。M5.8–M5.10 不被暗中并入 M6。

## 已有基础和边界

已有 NCA 片段/骨架身份、只读 generation 租约、数值 PoseKernel、DX11 GPU 蒙皮、
ClipClock 成功提交时钟、XZ/Yaw 根运动与 C# 动作/碰撞权威。程序化 Action Animation Lab
不是正式 Animator。旧 C++ AnimationGraph 仅有架构测试消费者，M6.1 删除它和专属构建/测试片段，
由更完整的托管验证替代；保留动画实验室及数值内核，不批量清理其他独立原型或用户资源。

动画图内部依赖和骨架层级不改变扁平 World。正式 Player 不依赖 Editor、MCP、Python 或源 FBX。
游戏 tick 内禁止文件 IO、JSON 序列化、资源装载、AI/IPC 等待和历史记录；使用预编译程序和实例缓冲。
托管准备阶段错误可取消未提交候选，但数值/物理不可逆失败仍 fail-stop，不宣称 solver 回滚。

## 阶段和门禁

| 阶段 | 交付 | 当前状态 |
| --- | --- | --- |
| M6.1 | 严格图资产、类型和结构验证 | 自动候选通过，见交付记录 |
| M6.2 | 编译程序、参数与提交式固定步求值 | 未实现 |
| M6.3 | 真实片段、数值混合与场景角色运行接线 | 未实现 |
| M6.4 | 图工作区、资源审批和共享事务 | 未实现 |
| M6.5 | 过渡中断、事件轨道与调试 | 未实现 |
| M6.6 | BlendSpace | 未实现 |
| M6.7 | 分层遮罩与缓存姿态 | 未实现 |
| M6.8 | Montage 式 Slot 和 Section | 未实现 |
| M6.9 | 活动编辑器动画 MCP | 未实现 |
| M6.10 | 运行包、纵向样例与联合验收 | 未实现 |

## M6.1 图资产和验证

在 Ncma.Animation 增加 `.ncmaanim` JSON v1、纯数据 DTO、严格 codec、结构验证和拥有自己副本的
不可变文档发布对象。保存图/骨架/片段/参数/节点/边/状态/转换的 UUID；不保存 runtime/native handle、
World、时钟、委托或表达式字符串。基础节点为 Clip、Blend、Parameter、StateMachine、Output；
参数为 Float、Int、Bool、Trigger。节点引脚由类型固定，端点为节点 UUID 和语义引脚名。

单一 Output；最多一个非嵌套状态机；每状态引用 Clip/Blend 根；姿态/标量依赖无环，状态转换允许环。
所有执行节点和状态可达；必需姿态输入恰好一条边，标量输入可使用默认值；参数条件为受控比较和 AND，
无任意代码。转换同一源状态 priority 唯一，准备未来确定排序。字节/数量/深度/文本/有限数值严格限额。
精确形状见 [M6.1 契约](M6_1_GRAPH_CONTRACT.md)。

测试：最小/混合/状态机 round-trip 和顺序无关编码，副本隔离，参数/边类型、循环/孤儿/重复UUID、
字段缺失/未知/重复/非法枚举/Unicode/版本/预算拒绝；结构失败不修改输入或发布对象。
完整 Debug/Release Build.bat 通过后提交。此阶段不提供文件审批/保存事务、资源解析、执行或可见节点编辑器。

## M6.2 编译与固定步求值

在 Ncma.Animation 生成不可变 AnimationProgram：拓扑排序、紧凑索引、状态出口和条件数组，
编辑坐标不进入运行数据。通过 C# 主机准备的精确骨架/片段描述验证兼容、generation 和时长；
真正资产租约适配由 M6.3 接入，描述数据不是 GPU/native handle。
每实例显式 owner thread、session/world/graph identity、tick 和有界参数槽/触发器；程序共享，实例独立。

Prepare 从最后 committed 状态生成候选，Commit 必须匹配同一成功固定步；Abort 丢弃候选。
每量子最多一次状态转换，priority 小者先选，条件合取，只有获选转换在 Commit 消费 trigger。
无转换也保留 trigger 至显式清除/成功消费，不允许失败 step 丢失输入。非循环时钟钳制终点，循环保留
unwrapped 时间；准备时检查至多32次边界跨越。零 delta 仅查询，不发送事件、不推动状态。

首版支持 Clip/Blend、默认/绑定权重、显式初态与退出相位；中断高级语义在 M6.5 补齐。
输出复制采样/混合计划，不直接修改 World 或调用物理。测试参数类型/范围、trigger消费与失败保留、
循环终点、多帧率相同tick、错误session/tick/双commit、同程序多实例和预算；编译只在 off-frame。
度量 warm path 分配与调用次数，不以字典基础标记优化 ECS。

## M6.3 片段和场景接线

适配已有 RuntimeAssetLease，固定精确 NCA/model/skeleton generation；先准备全部资源再发布程序，
失败释放候选，旧租约保留至新候选完整就绪。真实资产缺失/不同骨架/重导入失配拒绝启动，不用示例替代。
新增可选显式 AnimatorData 绑定。已有 ClipPlayback/动作播放和新 Animator 对同一对象必须互斥，
不同对象可共存；M4动作策略仍拥有命中/Health，图不能绕过它制造伤害或写 Transform。

扩展 numerical-only PoseKernel 的独立版本化混合接口：定长TRS/权重/索引、caller-owned有界批次，
不把图或托管引用传入 C++。先普通 Lerp/Slerp，再 M6.7 加层混合；旧接口冻结。一个 C# 协调器
在固定步 Prepare 图/根意图，唯一 Movement 权威经 Jolt 后发布，成功提交才消费图时钟/触发器；
展示只读 committed/interpolated 姿态，GPU geometry/shadow 共用 skinframe，绝不引入第二个时钟/solverstep。

测试实际 NCA 与独立管理端姿态 oracle、CPU/GPU误差、根剥离/阻挡、paused/single-step/Stop/reload、
准备失败/物理后失败/关闭失败保留，Editor与Player/Headless同语义。已有单片段/动作默认不改变。

## M6.4 可视化图工作区

统一现有深蓝工作区和顶部菜单；动画作为独立workspace，图画布、真实角色预览、参数/状态/属性与
诊断栏。C#拥有图和交互，ImGui只返回复制意图。实现 pan/zoom、多选、拖拽、节点搜索、固定类型引脚连线、
删边/删节点、状态入口/转换和可视条件编辑。草稿允许未连完的临时图，但任何执行/持久提交都要完整验证。

复用 Editor.Core InteractionGate/共享历史及 AssetDiskTransaction 精确路径文件保护；新建/打开/保存
需可信主机审批，验证 sourcehash/revision/resources，失败不覆盖旧图。手势一次Undo；取消无历史；
Play冻结时拒绝资产写入。主场景和动画预览独立，不因换workspace自动step/restart Play。
测试点击/缩放引脚命中、事件generation/旧事件拒绝、取消/Undo、外部文件冲突、重启保存、真实GPU截图。
自由docking不是前置；不能用参考图替换真实预览。

## M6.5 过渡事件和调试

定义同量子单转换、目标播放/源可见姿态缓存、中断策略和剩余时间语义；首版可中断过渡从当前可见姿态
继续，不无限级联。事件轨道按 `(previous,current]`，循环/终点/重新进入各有精确规则；预览scrub不发送
玩法事件。事件按目标播放源归属，姿态混合权重不自动缩放根运动/伤害。损伤依旧走动作授权层。

提交有界 DebugFrame：参数副本、当前/目标状态、时钟、blend权重、事件/根意图、数值sequence和身份；
失败前后状态区别清楚，fault不能显示旧成功帧为新状态。只读高亮/暂停预览/单步属于独立预览，不新增
Agent liveStep。测试反复中断、trigger优先级、循环边界/多周期、事件去重、32 actor上限和跨域fail-stop。

## M6.6 BlendSpace

增加1D/2D资产节点和有界采样点，1D排序/重复位置检查；2D非退化预编译三角剖分，边外投影到最近边，
可重复排序规则和权重归一。同骨架/单位要求、参数有限范围、没有实时三角化；再加入显式同步组/相位
以避免不同片段周期漂移。root/notifies采取明确主贡献源规则，不自动叠加多个步音/命中。
编辑器显示点/轨迹/权重，事务同一history。测试点/边/退化/域外/相位边界、同帧率tick，记录采样成本。

## M6.7 分层遮罩和缓存姿态

遮罩绑定骨架UUID及稳定骨骼标识，重导入不能静默错配；基础 override 和明确 reference-pose 的 additive
分别实现，校验旋转/scale和权重。根运动默认只来自基础层，层不能获得角色运动写权威。
CachePose 在同一实例同一tick/参数候选内共享一次采样，跨world/tick/reload失效；预编译生命区间和
bounded scratch allocator，禁止共享可变姿态到其他实例。图循环/缓存自引用仍拒绝。
测试不同层权重、骨骼遮罩/绑定变更、cache命中/作废、allocation/native次数和CPU/GPU姿态对照。

## M6.8 Montage 式动作编排

增加C# Slot/Section资产、片段区间、Section跳转和blend窗口，不承诺UE Montage兼容。固定步安全边界
接收受控请求，带会话/tick/目标/instance身份；同Slot优先级与中断策略、最多32次边界/量子、限额事件。
动作规则/输入缓冲继续C#，RootMotion经唯一Movement权威，montage不能直接teleport或任意回调World。
可视Section/Notify轨道和预览；测试结束/重入/打断/Combo/取消、零长度非法、跨多个Section、Play/Stop/reload。

## M6.9 活动编辑器动画 MCP

新增默认拒绝的只读图inspect/validate和获批图节点/连线/参数事务，能力名称/schema/风险分类固定。
范围为可信UI明确review的图UUID、路径、依赖及Editor generation/revision，paired audience/endpoint/到期
每请求重检；Agent不能审批自己或开启推理。只读/修改复用图服务/共享history，支持dry-run和确定诊断，
无任意表达式/eval/Python/程序集加载/文件路径扩权。独立预览请求有自己的实例/tick，不驱动livePlay。
测试默认拒绝、失效grant/重复ID/Undo权限、跨图/资产越权、排队过期、格式/长度、日志不泄漏路径；
真实第三方客户端及可见人工审批单列未通过状态。

## M6.10 包和联合验收

将预编译图与骨架/NCA闭包纳入有界NCP资产包，明确typed格式与版本；运行时不能从包中加载作者代码、
Editor坐标或未审批文件。Player拒绝无效图/错generation/不完整闭包，Headless不加载GPU/pose，
图能力未启用的普通2D/无动画Player不初始化动画资源。

纵向样例覆盖真实用户FBX Idle/Run/Attack/Dodge、图保存/重启、Editor预览/Player同语义、碰撞/根运动和
可见诊断。0/1/8/32角色、不同渲染帧率和固定步、128 Play/Stop/重载资源基线、损坏包与异常退出恢复；
独立测CPU/GC/native调用/GPU帧identity。针对目标机器制定预算，1小时长稳和人工使用单列，不以
程序化片段、共享shaderoracle或历史GPU计数代替。完整Debug/Release、smokes/strictformats/inspect/audit/
checkeddeploy后提交；自动候选通过与整个M6正式验收分开报告。

## 延后内容

IK、重定向、Motion Warping、压缩、并行求值/LOD、Motion Matching 另立方案和性能依据，不以空节点
枚举冒充支持。Python独立模块只返回决策建议；网络是独立服务，两者不进入动画图执行tick。
