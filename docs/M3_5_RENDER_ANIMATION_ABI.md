# M3.5 姿态与 GPU 蒙皮契约

日期：2026-10-06。本文件描述实现边界；验收结论见 [交付记录](M3_5_GPU_DELIVERY_REPORT.md)。不恢复旧 animation ABI 1、C++ World 或玩法 host。

## 所有权

C# `Ncma.Animation` 拥有持久 ClipPlaybackData 与 committed-tick 时钟；`Ncma.Rendering.Scene` 拥有独立 Edit/Play 动画实例、NCA/文件租约、native rig/clip 和 GPU 输出租约。准备在可信帧外边界执行，Player 不打开 FBX。源 UUID 与资产 generation 必须准确匹配，runtime 句柄不保存进文档。

`NcmaAnimationKernel.dll` 仅保存不可变数值数据，调用者传入 previous/current time 与 alpha。C++ 不推进时钟、选择 clip、写 World 或执行 Undo。采样 local TRS（旋转 slerp）后按排序父节点合成模型矩阵；保留辅助节点。每 mesh 以自己的 geometryToBone 与 boneModel 合成 binding palette，不使用统一 skeleton inverseBind 代替。

## Pose ABI 1.0

入口 `ncma_pose_get_api`，独立于原 Character/Animation ABI 2。Win64 固定布局如下：

| 结构 | 字节 |
| --- | ---: |
| TRS / Bone / Key / Track | 40 / 44 / 48 / 16 |
| Request / Matrix / Stats / Api | 40 / 64 / 48 / 72 |

操作为 create/close、create_rig/create_clip、release、sample、stats。opaque uint64 资源属于创建 context 与 owner thread。上下文上限 4；每 context 64 rig、128 clip；每 rig 1024 节点；每 batch 32 请求、32768 输出节点。clip 上限 64MiB/200万 keys；context retained 上限 512MiB，另有固定采样 scratch 约 3.4MiB。

keys 与时间、父顺序/骨骼索引、完整 batch 范围预检；数值结果先在私有 scratch 完成，成功后一次复制输出与更新计数。无每帧 CPU 顶点蒙皮。骨骼只支持正 uniform scale，拒绝非均匀/奇异/负缩放；对象正非均匀缩放在场景 draw 以 inverse-transpose 处理。输入输出必须独立，不允许 alias。

可信 C# loader 校验绝对路径、精确 DLL 名与 SHA256，持有同一文件/目录 pin，拒绝重解析点与 DLL 硬链接。它不是不可信代码安全沙箱。释放顺序为 clip → rig → context → 文件 pin/卸载；失败保留尚未成功关闭的所有权，不用 finalizer 跨线程释放。

## Renderer query 5

Renderer 模块增加独立 query 5，query 1–4 表和既有 reference/static 图像语义冻结。内部仍使用 DX11；不宣称 Vulkan 蒙皮。

| 结构 | 字节 |
| --- | ---: |
| SkinMesh / SkinPalette / SkinRequest | 56 / 128 / 24 |
| SkinBatch / SkinStats / SkinApi | 32 / 96 / 56 |

操作 create/destroy/update/capture/stats。source layout 3：p/n/uv/tangent 48 字节、uint4 mesh-local joints 16 字节、float4 weights 16 字节，共 stride 80。原始顶点与索引仅准备时上传并驻留 GPU。Shader Model 5 compute prepass 写 stride 48 的 GPU raw UAV 顶点输出，query 4 的 geometry/shadow 使用同一个输出；这不是每帧 CPU 蒙皮，也不是在两个 pass 各执行一遍蒙皮。

palette 包含 column-major model 与 inverse-transpose normal 矩阵各 64 字节。四权重必须非负、归一化；joints 在 mesh binding 范围内；normal/tangent 为合法单位正交基。shader 做 weighted position、inverse-transpose normal 与 tangent Gram-Schmidt，塌缩基有确定性 fallback。UV/tangent sign 保持原值；对象变换只在后续 draw 应用一次。

每 batch 上限 32 请求、32768 palette entries，每实例上限 1024 bindings；contiguous palette ranges 恰好覆盖输入，无重复输出。源+输出+索引每实例上限 64MiB；同 renderer Edit/Play 合计最多 64 skin 实例、128 meshes；skin GPU retained 上限 512MiB，并受 mesh 输出/索引 256MiB 预算约束。原始 stream 当前按实例复制，尚未做同 mesh 多实例共享优化。

每 renderer 懒创建三个 default-memory structured palette slots，固定 GPU 缓冲 12MiB+16 字节（不含 shader/query 元数据）；固定 CPU 请求/palette scratch 4MiB+768 字节。创建无 skin 的场景不初始化这些资源。全部预检后以非阻塞 event GetData 选择可用槽；三个槽均在使用时返回 BUSY，不覆盖 GPU 尚未使用完的数据、不等待 simulation/render tick。C# TryUpdateSkins 返回 false，记录 backpressure 并走轻量 clear；不是自动 CPU fallback 或模拟故障。

更新 frame 必须严格递增；query 4 中每个 animated draw 的输出 frame 必须恰好等于提交 frame，拒绝陈旧主画面或阴影。query 1/3/static 接口不能画 skin。即时上下文顺序保证 compute 写入先于同一帧 draw；SRV/UAV/状态在边界解绑。native 输入预检失败不会 dispatch/发布 frame/更新计数；GPU 执行后的设备故障是 fail-stop，不能宣称 GPU 回滚。

Capture 仅可信测试诊断，复制 caller-owned buffer；有界等待/读回可能最多约 2 秒，正常帧与 Agent capability 不调用它。destroy 在 inactive owner thread 上先 drain GPU；超时保留资源；最后 skin 释放 palette ring，最后 mesh 释放共享 mesh kernel。派生 scene/GPU 资源先于 renderer/device/plugin unload 关闭。

GPU 时间为最近完成的 timestamp/disjoint 样本，不保证是当前 frame；native skin cpu_ms 仅 dispatch 部分，C# SkinAbiMilliseconds 包括 request packing/native preflight。GPU resident 与 native scratch 分开记账，不把 GPU 指标描述为总进程内存。

## 场景、时间与权限

ClipPlaybackData schema 1 只保存 clip UUID、playing、loop、speed、startTime；速度 0..8，持久 startTime 0..600，并在准备时核对真实 clip duration。成功固定步后的只读 observer 推进时钟；失败步不推进，已成功步在 observer 故障后保留。PlayPause/Resume/Step 仍只有一个 WorldRunner。Edit 预览时钟独立，播放不逐帧写文档或加入 Undo。

Play pin 原 generation，reimport 不自动重绑活动 rig；Stop/重新准备才取新代。reload 保留同 World 的 clock/资源并核对设置；恢复后的新 World 重新准备。缺 clip 在 Editor 是保留 UUID 的诊断且不生成 skin/bind-pose fallback；严格 Player 拒绝。删除对象不再提交其输出，资产引用变更需要帧外准备。

root displacement 为只读报告，含根位移仍表现在 mesh，不写 GameObject/Physics；in-place 需素材明确制作，实际运动权限留 M4。未验证的 animated camera/light bind-AABB 裁剪明确关闭；主视图与 caster 的 layer/集合仍分开。未来保守 animated bounds 需独立验收。

既有只读 `ncma.render.get_profile` 添加 animation costs/backpressure，不增加 GPU capture 或 live World 写权限。作者与 Agent 的持久改动仍共享 Editor.Core 命令/事务/Undo，无 Python 同步推理或 IPC。

通用 Editor/3D-capable DX11 Player 包含懒加载 pose DLL；Null Player 不部署它。没有 skin 不初始化 pose/ring/rig。专用纯 2D 图形裁剪部署 profile 尚未实现，不把通用 DX11 包称为已完全剥离 3D。

## 参考

- [D3D11 compute shader resources](https://learn.microsoft.com/en-us/windows/win32/direct3d11/direct3d-11-advanced-stages-cs-resources)：structured/raw buffer 与 compute 资源约束。
- [D3D11 resource flags](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_resource_misc_flag)：raw/structured GPU buffer。
- [GetData](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-getdata)：非阻塞完成查询。
- [ufbx deformers](https://ufbx.github.io/elements/deformers/)：mesh-local geometry_to_bone 绑定语义。
