# M6.8-B2b-2 正式 Montage 运行契约

基线 main `b88ea74812e8a2cc51938988b10dadb7946c805c`。本切片接通正式同 Animator 的
持久化 Slot、实际 NCA 姿态/root/Notify、现有 NCP1 和 Editor/Player；作者轨道和新的
typed Montage 编辑在 C，不在本切片宣称完成。

## 严格数据与资源

当前 `.ncmaanim` graph **v5 替代 v4**，v1–v4 不读取、不兼容、不迁移、不重写拒绝文件。
每节点必须有 `slotId`/`playOnStart`，非 Slot 为 empty/false；图必须有 nullable `montage`。
Montage 仍严格 v1，作为图内复制数据：16 Slots、64 Sections、256KiB；UUID 不可与图元素混同。
全部 Slot 一一绑定，组成唯一连续最终输出链，不进入 State/Cache/分支；链顺序定义叠加顺序。
Section Clip 纳入原128 Clip资源闭包。准备实际相同 model/skeleton/generation/duration NCA，
复制和 pin 生命周期沿用当前资源服务。原 native ABI/10个NCA tags/NCP1 animgraph 编码不变。
删除临时外部 `PrepareMontage`/`BindMontage` 和构造器注入；不保留兼容入口。

## 唯一量子与姿态

Montage 子状态使用 SAME instance/session/world/tick/attempt 与 graph token。Prepare/Commit/Abort
统一；16 Slots/queue64/recentID256/每Slot32 boundaries/总528 intervals 不变。
trusted safe-boundary Play/Cancel/Jump 沿用严格请求；`playOnStart` 只在首次成功量子执行，
不会在构造/tick0推进。Stop/Reload 新身份和队列；不能从回调/Agent开放 live 控制。

基础姿态继续原时钟；Slot 是 Section 当前 clip 的 final committed static sample，Previous=Current、
non-loop，再用 final Slot Weight 与基础姿态混合。跨 Section 不插值贯穿错误 clip 区间。
CPU 和现有 native sample/blend 使用同一闭合 recipe，验证全部行（包括未使用行）。原TRS/bone/actor
和 scratch budget 不扩大。无活动或 traversal 时 alias base；终止 Weight0 且有 interval 仍保留行。
状态过渡中断冻结 pre-global-Slot base，避免全局独立叠加层被冻结后重复应用。

## 根意图与 Notify

root = baseIntent × (1 − integralCoverage) + 本 Slot 跨段贡献；顺序沿最终 Slot 链。
RootMotion=false、Cancel 无新 traversal 时保留基础根意图；terminal Weight0 的最后有效区间仍贡献。
复制全部 interval 使用原始 **double fixedDelta** 校验 fraction；仅物理插件边界转 float。
位移/对数yaw采用 B2b-1 离散区间策略、段贡献均值加权与累计heading。包络均值积分解析精确，
但不是任意非线性 root 的连续积分，不宣称不同 fixedDelta 恒等或 SE(3) 精确积分。
本切片不增加求解器/时钟/World写入口；唯一 Movement/Jolt 执行，solver faults fail-stop 非回滚。
可视root剥离沿既有完整 pose，不把 desired root 当作 accepted collision displacement。

Notify 为已有 marker 数据按每真实 Section `(previous,current]` 穿越输出，保留 base target-only 事件。
带 SAME成功tick/sequence/instance/graph/Slot node；终止保留、Abort不发布、Cancel不制造事件、Jump不catch-up。
沿用总事件预算，超额拒绝整个候选；不是脚本攻击/伤害回调。

## 包与 AI 边界

图嵌入 Montage 精确 bytes 走原 animgraph NCP1；正式 Player 自动加载并执行 authored startup，
不需要 Editor或外部 host 注入，搬移包不依赖源FBX/import cache。Headless 无 GPU；0 actors 不初始化3D。
隔离 sequence 使用同一实际 NCA interval root recipe，无 live World/solver/GPU权限。
既有 exact-reviewed `ncma.animgraph.inspect` 增加 `montage` 摘要、`montageSlots`、`montageSections`
有界分页，全部 cached/off-frame，同整图hash/dependencies/publication/editor/audience/TTL/grant/revoke。
不是新的 live 能力或推理服务；typed Montage author/AI 编辑在 C 后续实现。

## 证据与未关闭门禁

核心严格格式/ownership/terminal/Notify/Abort/Cancel/Jump/错误未使用行测试；实际 NCA1/8/32、
独立 GPU顶点预期、唯一Jolt根意图、Reload、隔离sequence、pre-Slot frozen cache、pre/post solver fail-stop。
联合 Editor0/1/8/32每组2×8ticks、close-retention、唯一实例、Edit不变；搬移正式Player16组
Headless/DX11。Player报告证明实际启动/tick/渲染/API/drain与精确已cooked图，**不独立导出逐Actor根数值**。
自动stdio分页/default-deny/revoke不替代可见第三方人工验收。
用户真实Idle/Run/Attack/Dodge素材、人工/目标环境/性能预算/1h仍开放。
