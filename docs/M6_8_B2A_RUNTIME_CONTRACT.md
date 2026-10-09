# M6.8-B2a：同一 Animator 的 Montage 候选状态与资源准备

本切片接通真实 NCA 准备和现有 Animator 的提交周期，**不是完整 B2**。
姿态覆盖、跨 Section 根运动/Notify 消费、正式 Montage 作者格式/NCP1 注册属于后续 B2b/C。
当前图仍严格 v4；未注册空 Slot 节点，也没有旧格式兼容或自动改写。

## 所有权与失败

`AnimationGraphInstance` 可持有一个预准备的不可变 Montage program。内部合作状态使用图的同一
instance UUID、session/world/tick 和 attempt sequence；外部只有图的 Prepare/Commit/Abort token。
Montage 不拥有第二个 scheduler、World、OnUpdate、solver、native resource 或事件回调。

图 Prepare 中先准备 Montage，再执行原图及事件检查。任一候选失败都不发布成功时间或消费请求；
图后续检查失败会 Abort 已准备的 Montage。图 Abort 同时 Abort 合作状态；只有同一个成功 World
fixed step 的 observer 才提交两者。图预检失败也不能造成 Montage 与图的成功 sequence 错位。
历史 graph debug 明确 invalid，当前 Montage 读取拒绝。独立核心允许重试；正式 Play fault 后需要
显式 Stop/new Play，不自动恢复或伪称 native rollback。

请求保持 B1 的64队列/256近期ID、16Slot/每Slot每量子32边界/528总区间上限及完整 payload 校验。
跨 Section 的全部区间按序复制，终止 Slot 变 inactive 时仍保留最后区间；本切片只发布数据，
不据此写 Transform、Health、Movement 或执行 Notify。原图姿态和根运动路径不变。

## 实际 NCA 和正式主机边界

`RuntimeAnimationGraphAsset.PrepareMontage` 仅在 off-frame 用实际 retained NCA/package 图闭包准备：
图/骨架/Character/Clip 的同模型、同骨架、同代次、骨骼数、manifest 与真实 duration 继续复核。
Montage 只能引用图已经保留的 Clip 子集；未知 clip、错 skeleton 或越真实 duration 拒绝。
返回复制的纯 managed metadata，不新增资源 pin。调用方持有的 exact publication lease 必须覆盖使用期。

`SceneAnimatorRuntime.BindMontage` 是可信主机、Stopped 且下一次 Start 之前的显式候选 API，每对象每 composition 一次。
它使用 Animator 已持有的实际资源 lease，完成准备后才安装，失败不改变绑定。运行控制复用原
owner-thread/safe-boundary/Running-or-Paused 检查；tick 内或只读回调尝试不获批准，捕获非法 tick
调用仍 poison managed quantum。Stop 清理 pending；Reload/new Start 由原 host 重建 joint instance，
保留 World tick chronology、换 identity、清空旧队列与活动时间。

没有新的 Editor/MCP 工具、Agent Play/Cancel/Jump 权限、推理/IPC/Python gameplay 或 native ABI。
搬移 NCP1 测试仅证明 **host 提供定义可对打包 NCA 准备**，不证明 Montage 定义自身已 cooked，
也不证明正式 Player 自动加载/显示 Montage。Headless 同钟测试不初始化 pose/GPU。

## 验证范围

纯核心：joint token/Abort/两向失败/终止区间、错身份和代次、无绑定拒绝、owner isolation、
预检 sequence gap、1024 warm request+candidate allocation0（不是整个 World 性能）。
实际资源：真实 NCA + source-free packed NCA、1/8/32实例、跨两个不同 Clip 的完整区间、终止、
Reload/new identity、caught tick request/binding、候选及 post-commit fault、显式 Stop 恢复。

完整无Skip Debug/Release、checked deploy、提交推送与远端核对见交付记录。
真实用户 FBX/第三方可见审批/目标环境/性能预算/1h 验收继续开放；不得用本切片证明完整 M6.8/M6。
