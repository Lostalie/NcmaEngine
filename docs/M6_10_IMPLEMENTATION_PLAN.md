# M6.10 运行包与纵向联合验收方案

基线：M6.9 `097840073a9874ae4d9842727da38ab0c2c6a3cb` 已提交并核对远端。
按用户顺序先关闭 M6.10 自动候选，再提交推送／核对远端，最后进入 M7。
M7 本版本仅 DX11；Vulkan/OpenGL 保留契约扩展点，实际绘制延期下一版本。

## A 运行闭包与一次准备

继续使用有界 NCP1（64MiB、4096 项）和严格当前 graph v5；不新增兼容格式或本机可执行程序序列化。
包内保存规范化图、Montage、遮罩及真实 NCA 数值闭包；加载时先完整验证，再离线编译不可变程序。
图节点X/Y规范化为0，只去除作者布局；所有语义字段/UUID保留，运行hash/generation只依赖规范化字节。
作者移动节点不改运行包；包中非零布局即使重算hash也拒绝，不提供旧包改写或兼容。
在同一 RuntimeAnimationGraphAsset 的精确资源发布内复用编译结果，最多一项；命中前仍检查有效 lease、
owner-thread 和图对象身份。缓存不持有文件 pin／World／播放实例／native 句柄，不共享可变播放状态。

严格验证损坏头／版本／checksum、图版本／图 generation、Skeleton／Clip generation、缺失闭包、
真实 Clip 时长与 Section 区间、精确 Skeleton hash。重新计算校验和仍不能绕过类型／语义验证。
所有拒绝保留原始输入；Player 在 gameplay、GPU、solver 初始化前失败，不降级到作者资源。

## B 同一链路最终自动场景

真实程序化 NCA 的 BlendSpace → CachePose → 遮罩 Layer → Blend → Slot/Montage → Output。
继续原有独立数值／GPU预期、状态机中断、Root/Notify、AI审批／事务测试，不删除或降低断言。
新组合场景检查 0/1/8/32 绘制与成本，1角色在30/60/144Hz 与固定步的每个成功量子共120 tick
同步、复制 TRS/Slot/root/Notify 完整轨迹；多步帧通过只读 committed observer 采集，不漏中间量子。
共享一个 physics service、pose kernel、renderer/cache，128 次 Play／Reload／故障／Stop；
每次恢复冻结启动文档并轮换身份，实际 skin/geometry/shadow 绘制后回到原资源基线。
测量 owner-thread CPU、分配／GC、提交字节、数值身份和 GPU last-valid 样本。
同步测试可用现有 2s 有界 GPU drain，不进入生产 tick；这不是吞吐预算通过。

## C 源文件独立与全部回归

复用正式 Player、搬移 NCP1、Headless 无 GPU、纯 2D 无无用动画、图闭包及损坏包门禁。
加入组合图的 Editor 0/1/8/32 与搬移 Player 16 路径；另从独立cwd启动真实搬移生产apphost
Headless/DX11（manifest逐文件hash、窗口ico、可选Physics和lazy pose DLL），无Editor/Importer/Gui依赖。
保留原有所有专项。
完整顺序无 Skip `Build.bat -Configuration Debug`、Release，失败修复后重新双配置。
核验 checked deployment manifest 所有哈希、Complete journal 和备份，再仅提交源码／文档并核对远端 SHA。

## 不由本阶段自动关闭的门禁

真实用户 Idle/Run/Attack/Dodge FBX、真实第三方可见审批、目标机器／自包含、性能预算、1h 长稳仍待验收。
程序化片段不是用户素材；GPU last-valid 缺少 sample-frame 身份，不宣称逐帧独立 GPU 延迟。
无推理服务／凭据、Python gameplay、live Agent Play 控制、新 native ABI 或旧格式兼容。
