# M6.8-B2b-1：跨段混合积分与根运动数值前置切片

基线 main `83d7da9328cf1839133d0e6d96aa8edd24fe94f7` 已与远端核对。
本切片是 B2b 的可独立验证前置步骤，**不是完整 Montage 姿态/Movement/Notify/持久化实现**。
图保持 strict v4；不注册空 Slot、不提前部署半成品 v5、不恢复旧格式兼容。

## 同一候选区间上的混合积分

`MontageInterval.AverageWeight` 是本次 Section 遍历的 authored blend envelope 积分均值，
`StepFraction` 是遍历时长 / 原 host fixed delta。二者随原区间走同一个 graph/Montage
Prepare/Commit/Abort，保持 exact instance/session/world/tick/attempt。不新增时钟或 scheduler。

envelope = min(1, incoming ramp, terminal Section outgoing ramp)，与既有 Slot frame 规则一致。
在 incoming/outgoing 饱和点及两 ramp 交点切段，stackalloc5 + 梯形积分精确求 piecewise-linear
包络面积；不是用末帧 Weight 代替平均值。Section 后继存在时不虚构 terminal blend-out。
Jump 不追赶跳过区间；Cancel 冻结 playhead，只做原视觉 fade，区间/root travel 为零。

终止 Slot 的末帧 Weight=0/Active=false 仍保留全部实际区间；终止不足一个 fixed quantum 时
StepFraction<1，不虚构剩余 root 时间。优先级/中断/64请求/256近期ID/owner与失败规则保持。
每 Slot32实际边界、至多33区间、16Slot合计528区间，不放宽边界预算。

## 复制资源的数值消费者，不是 Movement 权威

`AnimationMontageProgram.ClipDuration` 读取准备时复制的 exact same-generation metadata。
`Ncma.Characters.MontageRootMotionRecipe` 是内部 owner-thread 数值准备器，复用已复制的
RootMotionTrack；不拥有 NCA lease/新 pin/World/GPU/solver，不写 Transform 或 Health。
调用方必须保留原资源闭包所有者；本切片实际 NCA 测试复用现有 packed lease。

准备要求 exact Montage Clip 集合/actual durations/canonical root，最多128tracks/262144keys。
求值先验证所有区间的 Slot/Section/Clip、author root policy、finite envelope、fraction 与 host
delta 对应关系、连续后继/end-start、排序、时长及32边界预算；root=false行也须完整验证。
16个候选输出暂存内部，全部成功才复制给 caller Span；最后一行失败也不能部分覆盖目的缓冲。

每段 extracted local delta 乘该段 AverageWeight；按累计 yaw 转换后续 Section 平移并组合 yaw。
Coverage=sum(AverageWeight*StepFraction)，供后续图消费者保留 (1-Coverage) 基础图意图。
root=false 或无区间时 contribution/coverage均零，不能获得移动权威。
这是明确的 **每遍历均值乘完整 extracted delta** 数值策略，不宣称对非线性 root curve 与变化
envelope 的连续积分精确，也不宣称不同 fixed delta 的整宿主一致性或已通过性能预算。
后续正式图/Movement 接线必须测试其唯一 fixed-step 下的 root 合成与独立 oracle。

## 已有与待实现边界

本切片测试解析 ramp/plateau/overlap triangle、区间细分包络面积、跨 Clip/终止部分覆盖、
Abort/Cancel/Jump/root opt-out、32边界/16Slot528区间、完整拒绝和 warmed allocation0。
独立矩阵验证后继 local displacement 跟随累计 heading；真实 source-free NCA + SAME Animator
1/8/32实例复制区间数值 oracle 覆盖最终 Slot Weight0，而不改变现有 Movement/姿态路径。

后续 B2b-2 仍需：严格 v5 persisted graph/Montage/Slot chain 与所有 closed readers/schema 更新，
删除 B2a 临时外部 binding 入口而非兼容 alias，同一 pose/blend/skin/shadow与interruption cache，
跨段 root 经唯一 Movement/Jolt、Notify数据发布、正式 source-free Headless/DX11 Player自动加载。
完整 B2b 通过后进入 C 的 typed作者/同源获批工具与联合验收，不授予 Agent live控制。
没有新 native ABI/NCA tags/NCP1 route、Python gameplay、推理/传输/凭证或执行授权。

完整双配置、部署与提交证据见 [交付记录](M6_8_B2B1_DELIVERY_REPORT.md)。所有人工/实际用户
FBX/第三方可见MCP/目标环境/性能/1h验收仍开放；M6→M7(DX11-only)→M8→M9顺序不变。
