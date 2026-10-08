# M6.3-C 唯一角色运动权威接线

前置：B 已完整通过顺序 Debug/Release 回归并推送，远端 main 核对为
`d718f0e781fcc4ea6b03be4269e0506d973ed0d3`。C 当前开始实施，不是已交付。
每个完整小阶段仍须测试通过、提交推送、核对远端后再推进；C 的内部工作切分不降低原门禁。

## C1 有界根运动配方

新增内部 C# GraphRootMotionRecipe，仅解释图的 Clip/Blend 数值配方，不拥有时钟、World、
物理或 GPU。真实租约及 generation 证明必须由后续应用服务准备，不能用该辅助类冒充。
每个 Clip 使用图 token 的 Previous/Current，根轨道复用 M4 的单根、单位尺度、XZ/+Y yaw
约束及最多32次穿越。Blend 对同一量子的局部位移线性混合、yaw 最短路径混合，含过渡产生的
Blend；显式 output，不假定最后一行。整个配方校验成功后才返回一个意图，错误无对外前缀。
初始化复制轨道索引；最多128轨道/262144 keys/769配方行，owner线程，复用 scratch。

专项验证嵌套 Blend、循环穿越、显式输出、四元数独立 yaw oracle、失效 clip/duration/区间/
权重/前向引用/未知操作、未使用错误分支仍拒绝、调用者索引改动、线程和 warm allocation。
C1 不开放 Animator + RootMotion，不接 Jolt，不能据此宣称 C 完成。

当前验证（2026-10-08）：内部根配方及专项负例已实现，Debug/Release 的
NcmaRenderingTests 均以0退出；`out/m6-3-c1-render-debug.log`、
`out/m6-3-c1-render-release.log` 保留完整输出，两配置
`out/verification/m2/render-<Configuration>/graph-root-recipe-results.json` 记录独立 yaw
oracle/未使用分支拒绝及1024次 warm 求值0分配，characterIntegration=false。
首次编译的 Vector3 命名空间遮蔽已修复为 namespace 内显式别名，失败日志保留；
重编两配置均零警告/错误。它们是定向测试，不是 C 的完整 Build.bat 门禁。
C1源码随用户随后要求的M6.4自动回归保留为数值检查点，不是完整C交付。
out/bin按最新完整检查式构建更新；C2–C4待实施，当前部署/提交结果见M6.4交付记录。

## C2 精确准备与唯一数值执行

ScenePlayRuntime 的图 System 必须先于 Character System 准备；应用装配发生在 Start 前。
同时保持现有唯一 MovementCoordinator、Jolt world/factory/job pool，不创建第二角色求解器。
图运行服务须以 exact pending token/context 暴露可信内部 prepared recipe 与明确 output，
不能使用上一 committed Frame.Output 解释新候选。公开观察仍仅 committed，AI 无 pending 访问。

图根集合按实际 prepared asset publication、graph/rig/model/clip generation 构造，并独立持有
租约。与 legacy ClipClock 根集合互斥，不能 Get<ClipPlaybackData> 或给图另建 ClipClock。
RootMotionData 完整类型集合及绑定 skin/Animator 配置冻结。先准备所有根意图，再执行一次
数值量子，只有协调器发布受约束 Transform；图服务不写 World，根水平位移替换输入而非相加，
垂直重力/跳跃/地面速度仍归角色策略。ActionDefinition 与 Animator 继续互斥，不能绕过 M4
存活、伤害、动作控制权威；组合开放需要独立明确策略，不借此默认接入。

图根的 visual anchor 必须明确且与实际 rig 相符；C2 先限定参与 clip 的初始根平面基准一致，
不将不同 clip 初始位姿任意拼接。复用数值姿态的最终根平面变换剥离 desired root，保留垂直/骨骼
局部动画；geometry/shadow 使用同一修正。绝不能用 Jolt accepted displacement 修正源姿态，
或由可见动画回写碰撞结果。非规范基准/scale/pitch/roll 在 off-frame preparation 拒绝。

## C3 参数提交与生命周期

现有 SetFloat/Bool/Int/Trigger 是可信安全边界控制，tick/readonly 调用仍应 poison；不直接放宽。
游戏逻辑若需量子输入，另定义有界、类型化、exact session/world/fromTick/actor/parameter
提案队列及 host preparation phase；必须规定同目标冲突、容量、失败清理、trigger 重试规则，
不能由 Behaviour 得到 live instance 或越过图准备。无同步推理/IPC，AI 不获 live 输入工具。

Prepare 错误中止未提交 token；数值执行后错误 fail-stop 并使联合观察失效，不宣称 solver 回滚。
World 成功 commit 后推进图 clock/trigger，observer 故障保留真实 committed tick 并停机。
Stop/new Play/frozen startup Reload 使用 fresh session/world/instance/resource identity；close 失败
保留所有者/租约，可显式重试，不继续卸载。GPU→pose→图/根 pins→solver 的依赖释放须逐项核对。
Headless 同样准备图根资源，但不加载 GPU/pose 插件；physicsEnabled 默认 false 不变。

## C4 自动门禁

真实 NCA/Jolt 验证根替换输入、墙/坡/跳跃/转向、过渡重入及混合权重、32 actor/33拒绝、
30/60/144呈现频率同 fixed tick、Headless 与正式 Editor/Player 共用服务、caught 非法配置/
参数写 poison、Prepare/数值后/commit后故障、Stop/Reload/close重试及资源基线。
独立 root/pose 数值 oracle，actual draw 的 geometry/shadow 精确帧一致，禁止双时钟/第二写者。
按 Build.bat 完整顺序 Debug→Release（无 Skip）运行所有保留测试、smoke、格式拒绝、inspect、
profile、审计和检查式部署后提交推送核对 SHA。C/D、真实用户素材、目标机预算、可见第三方
MCP、DPI/IME、目标环境及1小时长稳门禁没有证据时继续开放。
