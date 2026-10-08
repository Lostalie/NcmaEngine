# M6.3 真实动画图运行接线方案

M6.3按可独立验证的四个切片落实真实资产、数值、场景/角色与GPU闭环。M6.2已经推送并核对远端，
后续每切片完整测试通过后提交推送再推进；只有A至D均完成其自动门禁才标记M6.3自动候选完成。
既有人工/目标硬件/真实用户素材/性能/长稳验收不因此关闭。

## A 数值混合扩展

状态：自动候选完整Debug/Release已通过，见 [A交付记录](M6_3_A_DELIVERY_REPORT.md)。B也已通过，C/D最新状态见下文。

在现有NcmaAnimationKernel同一context/rig上增设独立pose-blend1.0导出，原pose1.0的72字节表、
request和stats保持原样。输入固定TRS、opaque rig、source offsets和weight；输出复制TRS/模型矩阵。
局部平移/统一正scale插值、最短路径单位四元数slerp、同骨架父先子后模型组合。不能传图、时钟、
场景对象或委托。最多32请求/65536输入TRS/32768输出骨骼；完整引用区域验证/计算成功后才发布输出与
独立计数。owner线程、输入/输出别名拒绝、无额外资源，原clip/rig/context释放顺序不变。

C# PoseKernel通过显式blendSupport启用，既有消费者默认不启用；复用已有code pin和context，不建立
第二套模块/rig所有者。测试C/C++/C#布局、独立协商、TRS/父子矩阵oracle、完整批次失败无前缀、
非有限/scale/weight/offset、stale/foreign/线程、32×1024与warm分配。A不声称场景图或GPU混合已实现。

## B 固定资产和场景图

状态：完整顺序Debug-final/Release自动候选通过，见 [B交付记录](M6_3_B_DELIVERY_REPORT.md) 与
[B运行契约](M6_3_B_RUNTIME_CONTRACT.md)。Animator与clip/action互斥；B暂不允许Animator + RootMotion，
后者属于C。包含真实NCA、明确NCP1图路由、正式Headless Player和既有数值/GPU场景接线；不称完整M6.3。

为Animator持久绑定注册纯值组件，明确Graph/Skeleton UUID；与同对象ClipPlayback/ActionDefinition
互斥。准备从可信已批准图副本出发，使用RuntimeAssetLease解析精确clip/model/skeleton generation，
不可使用M6.2用户提供metadata代替真实租约。图资源本体的资产身份/运行包路由须明确，不能在运行tick
扫描作者文件或序列化整个文档。Editor和Player需同一应用服务，不加载Editor/MCP到Player。

图程序和数值clip/rig共享不可变准备数据；实例/参数/clock隔离，失败保持旧session/pins，明确释放派生
GPU资源后clip/rig，最后资产/模块。正式场景将M6.2采样/混合计划送入A数值扩展，caller-owned有界scratch
按依赖生命期复用，帧间插值读取committed计划，不另推时钟。配置/类型集合冻结防止中途非法附加。
验证真实NCA、缺依赖/错误类型/同骨架不同时generation、Editor/Player/Headless、Stop/reload/close失败保留。

## C 唯一角色运动权威

状态：C2/C3真实准备/图候选根/Jolt接线及C4完整双配置自动门禁通过；见
[C 实施方案](M6_3_C_IMPLEMENTATION_PLAN.md)、[C/D契约](M6_3_CD_RUNTIME_CONTRACT.md)和
[交付记录](M6_3_CD_DELIVERY_REPORT.md)。安全组合已开放 Animator + RootMotion，不开放Action混挂。

图参数/动作输入由C#游戏逻辑或可信主机在安全边界提出；AI只有复制观察，不获live输入控制。
Animator的Prepare与现有Movement/Character协调器同量子：先准备所有图/根意图，再一次Jolt执行，
唯一Transform/Health权威发布，World成功commit后消费图token/trigger/clock。准备错误可丢弃未提交候选，
数值执行后错误则invalidate/fail-stop，不宣称solver回滚。源/目标/Blend对根意图和展示剥离使用同一
规则，碰撞约束结果不能被可见动画回写为期望位移。现有M4动作规则/伤害保持独立权威，不被图表达式绕过。

测试根位移替换输入、阻挡/坡面/跳跃/转向、过渡重入、catch非法写poison、32 actor、多帧率同tick、
关闭/停止/冻结startup恢复/重载前未提交以及事故后观察失效。

## D GPU 和 AI 联合证据

状态：同一已提交图/skin/shadow、精确当前pose stamp、独立审批只读运行MCP和共享本机观察已实现，
专项及完整双配置自动门禁通过；最终回归结果见[C/D交付记录](M6_3_CD_DELIVERY_REPORT.md)。

蒙皮源网格驻留，最终palette由同一已提交图/姿态服务产生；geometry/shadow共享精确skin frame，
无每帧CPU skinned vertices上传。验证实际DX11图角色渲染与API层、独立CPU oracle/误差及生命周期。
只读图运行观察带graph/instance/session/world/tick/pose/resource身份和snapshotValid，权限由可信UI
审批精确对象/资产与受众；查询无native采样/IO/tick推进。隔离预览不能控制livePlay。

0/1/8/32角色、失败/恢复/重载资源基线、Play/Stop循环与错误palette/buffer负例进入完整双配置回归，
日志/包哈希/实际截图和计数分别记录。真实用户素材、第三方可见客户端、目标机性能预算/1h验证仍列开放，
没有材料不得以程序化clip、旧实验室或共享shader对照替代。
