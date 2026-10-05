# M3.5 姿态/固定步时钟候选记录

本文件保留首轮 foundation 的历史范围与证据；后续正式场景/query 5 GPU 实现及最终门禁以 [M3.5 交付记录](M3_5_GPU_DELIVERY_REPORT.md) 为准。以下“未实现/未提交”仅指当时 foundation 检查点，不是当前完整阶段状态。

日期：2026-10-06。基线：已推送的 M3.4 `3c7d7b61b4de474b973186536af254d8b8c1cba4`。
状态：M3.5-A/B 数值与调度基础候选；不等于完整 A/B、GPU 蒙皮、角色播放闭环或 G5。
候选源码尚未提交；不提交为“完成 M3.5”。最终顺序 Debug/Release 完整回归通过，但 G5 仍未关闭。

## 已有候选

- 独立 `NcmaAnimationKernel`（仅 Eigen，无 NcmaCore/World/Renderer/Physics/FBX 依赖）：pose C ABI 1.0，table72、TRS40、bone44、key48、request40、matrix64、stats48 bytes；四个 owner-thread context、64 rigs/128 clips、每 clip 64MiB、全部 retained resource 512MiB。每 context 另有约 3.4MiB 有界复用 scratch，stats retained_bytes 不含该固定 scratch。
- 不可变 rig/clip，显式 previous/current time 与 alpha；二分采样 local TRS、四元数 slerp 后组合 helper/parent model matrices。最多 32×1024 骨骼，一批发布 local/model，不采样/上传 CPU 蒙皮顶点。正 uniform 骨骼缩放；非均匀骨骼缩放暂拒绝，不推广到任意 DCC scale inheritance。
- 完整 batch/时刻/rig-clip 归属/输出容量与 alias 预检；采样 overflow 不发布部分输出或 counters。Clip 先释放、Rig 后释放、context 最后关闭；busy/stale/foreign/wrong-thread 拒绝。C# 租约及容量先准备再发布 native 资源；绝对可信 DLL 路径 + SHA256 + 文件/祖先 pin，拒绝 reparse/hardlink；不开放 Agent 代码加载。
- 纯 C# `Ncma.Animation` 持久 ClipPlaybackData（仅 UUID/playing/loop/speed/start）、独立 ClipClock；没有 native/Python 依赖，也没有第二个 WorldRunner。`Ncma.Animation.Native` 承接有界数值调用与 mesh-local binding×bone-model palette；不使用全 rig 单一 inverseBind。共享 rig 的不同 mesh 保留不同 binding。
- Runtime 的可信 `ICommittedStepObserver` 在成功 step 后、只读 World guard 内执行；无 OnUpdate 世界写入，失败 step 不触发。观察器报错 fault runner，但不会回滚已提交 tick；Player exact-step 和暂停 single-step 的 status 必须计入该成功 tick。禁止在回调内更改调度；observer 上限 128。观察器私有状态不属于 World rollback，不能执行 IO/IPC 或推理等待。
- 核心时钟在 30/60/144Hz render 下按每个 committed tick 推进，支持 settings pause/speed/loop/exact endpoint，暂停呈现取 current pose；clip/start 改变在提交边界重置。原始资源与 metadata 加载、应用会话 attach 和 GPU 呈现尚未接线。

## 定向验证

15 项 C# candidate 检查：纯托管/持久值、失败 tick、未提交 step 内观察拒绝、只读 post-commit、呈现频率一致、pause/speed/loop/endpoint/owner、Play Pause/Step、post-commit fault 计数、文件 pin/hardlink 拒绝与锁释放、独立 System.Numerics local/model oracle、每 mesh binding/候选原子性、foreign/stale/预算/释放、32×1024 batch 稳态 GC=0、ASCII/binary FBX。

代码文件使用 `CreateFileW(FILE_FLAG_OPEN_REPARSE_POINT)` 获取文件本身的句柄，检查后交给 FileStream 用于 hash 和租约，不在预检后另开一个跟随链接的句柄。仅允许读共享，文件写入/重命名被 pin 阻止；普通释放和 hardlink 拒绝路径都测试句柄释放。固定隔离夹具保留在 out 下。有关 flag/共享行为见 [Microsoft CreateFileW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)。这仍是可信代码完整性检查，不是进程安全沙箱，也不声称完成对抗性竞态验收。

FBX 对照新 pose 与现有 Character CPU 数值资源：ASCII 最大绝对误差 `2.3842e-7`，binary `1.1921e-6`；容差 `1e-4 + 1e-5×参考幅度`。原有 native FBX/ufbx 直接参考位置 `<0.002m` 测试继续保留。这是 CPU 数值链路对照，不是独立 GPU oracle，不把共享导入算法当作唯一真值。绑定约定另核对 [ufbx deformers](https://ufbx.github.io/elements/deformers/)。

证据位于 `out/verification/m3-5/<Configuration>/*.pose.json`，完整 Build 日志位于 `out/verification/m3-5/`。前次编译/夹具失败证据保留；不删除/过滤失败结果。

首轮 Release 完整构建在 CTest 发现新测试 DLL 未生成（`Release-pose-clock-final.log`）：CMake 的 `NcmaManaged` 自定义目标已包含项目，但 canonical `Build.bat` 直接使用 `scripts/Build.ps1` 的托管构建列表，后者缺少该项目。已在列表增加明确的 Animation.Tests 配置构建及失败检查；不复制 Debug 产物、不跳过用例。此前 Debug 有预编译产物，不能作为该入口无缺陷的证据。修复后重新从完整 Debug 开始顺序验证两配置。

## 最终回归结果（候选范围）

源码冻结后按顺序完整执行 `Build.bat -Configuration Debug`、`Build.bat -Configuration Release`，未使用 Skip：

- `out/verification/m3-5/Debug-pose-clock-final5.log`：退出码 0；31/31 CTest（native 11 + managed/application 20）；动画候选 15/15；Python 42/42。
- `out/verification/m3-5/Release-pose-clock-final2.log`：退出码 0；同样 31/31、15/15、42/42。入口显式生成 Release 动画测试 DLL，不再依赖预编译 Debug 产物。
- managed/native smoke、严格新格式/旧格式拒绝、inspect、package/preflight 与检查后完整部署通过；发布路径仍为 `out/bin/NcmaEngine.exe`，保留 recoverable backup/journal。新的 pose 插件没有接入正式应用/部署，不向空 2D 包添加未使用的动画资源。
- native 新目标 `/W4 /WX`、managed warnings-as-errors 均通过；既有 Git 用户 ignore 权限诊断不属于编译警告。失败日志仍保留。

这些结果只验收本候选的数值/调度/租约基础与既有模块回归，不代替正式角色生命周期、GPU 蒙皮/阴影图像、G5 性能或人工验收；既有 M2 manual/self-contained/long-run 门禁未因此关闭。

## 剩余（未实现）

1. 已验证 NCA/skeleton/clip 的 generation/model identity 绑定与 Editor/Play 生命周期；ClipPlaybackData 注册到正式 scene bootstrap、严格资源闭包与场景组合校验。
2. 应用 trusted safe-boundary attach/detach、Edit 独立预览/Play pin、资源候选失败与 reimport/Stop 闭环；root motion 只读报告/明确 in-place 策略，不写对象位置（运动权留 M4）。
3. 常驻原始 skin streams、bounded structured palette、同代 geometry/shadow skin、GPU 使用边界与 readback 数值对照。当前 Renderer query v4 **仍只静态**；SkinnedMesh 启动依然明确拒绝，不能宣称新内核已接 GPU。
4. CPU/ufbx/GPU 误差、法线/切线、四权重损失独立测量；实际角色主视口/阴影参考图、0/1/8/32 角色成本与资源归零、导入→保存→重启→片段播放。
5. 完整 G5 Debug/Release 门禁与提交推送；用户真实许可 FBX 及人工/目标环境验收仍另行记录。
