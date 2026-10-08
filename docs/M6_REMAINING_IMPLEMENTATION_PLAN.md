# M6 剩余阶段实施与验收

M6.6自动候选已完成，基线main为ea4ef33421f7f298c7dbbb41b5da971dc5388862。
本轮依次推进M6.7到M6.10。每个实现切片通过完整无Skip Build.bat Debug/Release后提交推送、
核对远端，再开始下一切片；失败保留证据并修复重测。人工及真实目标机器门禁不由自动结果关闭。

## M6.7 分层与缓存

A实现C#骨架内容哈希与稳定完整骨骼路径遮罩、复制准备权重，以及同一PoseKernel上下文的
独立可选layer1.0数值扩展。Override按骨权重混合；additive显式参考pose，局部旋转顺序为
base × slerp(identity, inverse(reference) × layer, weight)，平移差分和正uniform scale比值。
原pose1.0/blend1.0接口保持；全部输入和输出scratch验证后才复制，失败不改输出或成功计数。
遮罩默认根权重0，C#游戏策略仍唯一拥有movement，数值内核本身不拥有World/图/时钟。

B新增严格图分层和CachePose节点，删除上一作者格式支持，不自动改写拒绝文件。骨架身份在实际
NCA准备期核对；cache同实例/state/tick/参数候选内复用一次依赖，不能跨实例/World/reload共享。
缓存依赖和生命周期预编译，bounded scratch不得膨胀；根与事件取基础层，覆盖层不能制造攻击回调。
真实CPU/native/GPU姿态、中断、主根剥离、fault/Reload/资源保留必须重新验证。

C同期typed遮罩/层/cache作者、精确获批骨骼清单与序列诊断；UI/Agent共用闭合语义、文件/资源
双审阅和唯一history，无姿态内存读写或Agent live控制。测试取消/Undo/权限过期/队列/重导入。

## M6.8 Slot 与 Section

A定义有界严格动作编排数据，包含骨架/clipUUID、Slot优先级、Section区间/后继、blend窗口和
事件；拒绝零长区间、缺引用、无界循环越界，不支持UE资产兼容或任意表达式。
B同一个固定步Prepare/Commit/Abort与安全边界请求接入正式runtime；每量子最多32边界，
请求带instance/session/world/tick，唯一movement接收已准备根意图，不直接写Transform/Health。
C可视轨道和获批语义编辑/隔离序列，测试结束/Combo/打断/取消/重入/Stop/Reload、实际NCA与Player。

## M6.9 AI 工作流可靠性

编排已实现的读取、提案、验证、人工审阅、事务及独立测试能力，采用有界步骤/并发/deadline/
有限修复预算。未实现域明确unsupported；取消停止未执行步骤，已提交步骤保留独立回执，
不能自动Undo后续用户编辑，跨文件/native/IO不伪称原子回滚。重复、过期、重新配对和排队请求
重检exact scope；Agent不能批准自己。无实际推理配置时仍明确未接入，不代配凭证或外发素材。
自动真实stdio/UI同语义回归与人工可见第三方客户端验收分开。

## M6.10 包与纵向联合验收

严格预编译图及真实NCA闭包纳入现有有界NCP1，Player无Editor/MCP/Python/源FBX依赖，
Headless无GPU，普通2D不初始化无用动画资源。验证损坏包、版本/代次/闭包缺失与关闭恢复。
自动0/1/8/32、不同frame调度、128cycles资源基线、CPU/GC/native/GPU身份成本后完整双配置。
用户真实Idle/Run/Attack/Dodge素材、可见审批、目标机器预算及1h长稳需要相应素材/人机证据，
无法取得时保留待验收，不能用程序化片段或共享shader oracle替代，报告自动候选与正式验收差异。
