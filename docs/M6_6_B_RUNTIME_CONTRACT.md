# M6.6-B BlendSpace 运行契约

2026-10-08。B 是严格格式、实际资源和提交式运行接线；C 作者控件/权重扫描、D联合验收尚未完成。

## 格式与资源

`.ncmaanim` 当前严格 v3，替换 v2；每个节点必须声明 `blendSpace`（普通节点为 null）。
v1/v2、缺字段、重复字段、额外字段拒绝，不迁移、不重写输入、不恢复兼容入口。
新增 BlendSpace 枚举值位于原五种节点之后；独立数值原生 ABI、原十种 NCA 标签和 NCP1 路由保持不变。
嵌入定义持久 UUID，轴绑定已注册 Float 参数；空间/采样/图元素 UUID 唯一，syncGroup 不得冒充元素或资源。
轴 name/unit 是用户的控制量标签，不是自动速度/角度转换器。实际 NCA 坐标/单位由既有导入规范保证，
不靠轴字符串推断素材尺度。程序准备检查真实同 model/skeleton/generation、骨数和实际 clip duration。
最多16空间、每空间32点、128 distinct clips；prepare 在 tick 外排序/剖分并复制实际数据。

## 唯一时钟

`cycleSeconds` 是作者明确周期，不根据权重或片段时长自动猜测。每空间的提交秒数除周期得 unwrapped phase，
各片段时间等于该 phase × 实际 duration。相同评估 state/context 的非空同步组仅推进一次，
同组周期、loop、speed 和 speed 参数来源必须一致；没有跨对象共享可变时钟。
候选重入清除目标 state 时钟，inactive 不推进；Prepare/Commit/Abort 延续实例/序列/session/world/tick检查。
循环每量子最多32边界；非循环停在周期终点。参数变更不能在正在准备时生效。

## 姿态、事件与根运动

A 权重最多3个正贡献，稳定采样 UUID 顺序。配方产生1–3 Clip、0–2 backward Blend，
最后一个 `RootSource` 行的 SourceA 是完整混合姿态，SourceB 是最大权重（平局 UUID 优先）采样 Clip。
该行仅在托管解释器选择已有数值操作，不增加原生 API/时钟/World。
姿态复制完整混合值，事件使用同一主贡献采样区间；切换权重不补发先前非主源事件。
root 数值仅采用主贡献 Clip 区间，进入既有唯一 Movement/Jolt；不是多源根位移求和。
视觉 stripping 去除完整混合姿态的 desired 根，不是 collided 位移或主贡献 Clip 姿态。
外层 Blend/状态过渡仍保留既有 root 混合和目标事件选择规则；不宣称全图所有并行空间会发事件。

每图配方上限 `3*nodeCount+1+10*spaceCount`（全局929）；65536 TRS、场景32768 bones/32 actors等预算不放宽。
中断仍捕获上次成功提交 alpha1 的完整姿态，而非主根源或最后一次渲染插值。
冻结源不推进根区间；失败不发布部分缓存/输出，Abort 不消费新缓存代次。
所有解释器必须验证全配方，包括未引用行；SourceB 必须指向此前 Clip 行。

## 生命周期与权限

Editor/Player/Headless 复用同一个 ScenePlayRuntime/SceneAnimatorRuntime，正式 source-free NCP1 支持同格式。
Reload 是关闭呈现资源、Stop/frozen-startup/new identity/Paused，再准备新呈现，不是 live scene/cache 迁移。
旧 World 身份的渲染观察器不能继续观察新量子，故障不自动恢复或声称 solver rollback。
现有精确图 inspect/validate/propose/transaction/sequence 审批与只读 Play 保持；没有新增 Agent live 参数控制。
B 不提供采样点拖动或新的 scan authority；顶部添加节点按钮暂禁用 BlendSpace，完整语义 JSON 可按同一草稿规则审阅。
没有 Python 游戏脚本、同步推理、云服务或自动批准；人工/真实素材/目标环境/性能/1h门禁保持开放。
