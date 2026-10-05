# M3.5 GPU 蒙皮与 FBX 片段播放方案

日期：2026-10-05。状态：未实现。依赖 G4。目标是场景中的真实 FBX 角色可播放片段并投射一致阴影。
本阶段仅最小 ClipPlayer，不提前实现 M4 动作控制/物理根运动或 M5 Animator 图。

## 1 所有权与数据

新增 C# Ncma.Animation 管理 clip 选择、速度、loop/pause、已提交固定步时间和 pose cache。
拟新增 NcmaAnimationKernel 保存不可变 Skeleton/Clip 数值资源，按明确时间批量采样 local TRS/model matrices。
资源来自 G2 的已验证派生数据，不在 Player 重新打开 FBX，不把高层时钟/Undo 放回 native。
Renderer 只接受 palette/mesh/material，不能驱动 AnimationPlayer 或写 GameObject Transform。

现有 Character ABI 2 的 Sample 总是附带所有 CPU-skinned XYZ，因此不能作为新 GPU 热路径。
需要 pose-only 数值契约和可复用 scratch，原 CPU 路径保留为诊断/数值参考，而非每帧顶点上传。

## 2 实施切片

### A rig 与采样数值资源

验证 parent 排序/循环、mesh bindings、clip/skeleton compatibility、keys/时间/有限 TRS、1024节点/预算。
GPU joint 指向 mesh-local binding palette；对每 binding 计算 boneModel × geometryToBone。
父骨骼与辅助节点在 rig 内求值，不创建场景骨骼 GameObjects；多个 mesh 不能共享错误 inverseBind。
明确几何/对象变换的应用次数，保留 G1 的右手系/矩阵约定。
骨骼非均匀缩放/剪切先拒绝；可接受的 uniform/rigid 变换也须固定 normal/tangent 变换政策并有 CPU oracle。
对象正非均匀缩放以 inverse-transpose 正确转换法线，不只蒙皮 position。

### B C# 固定步与渲染插值

持久 ClipPlaybackData 仅保存 clip UUID 与播放设置；runtime 时钟、采样结果与 leases 是独立派生实例状态。
动画时间只按已成功提交 tick 推进，失败步不得先提交 clock；利用 committed tick 或明确成功步通知。
Pause/Resume/Step 遵循现有 PlaySession，无第二个 WorldRunner，无 OnUpdate 写 World。
插值使用相邻 committed local TRS/采样时间及 alpha，旋转采用定义的球面插值，不线性混合任意矩阵。
Editor 时间轴预览独立，不改 Play tick；自动播放不每帧加入 Undo。
root motion 可读取/报告但不写对象位置；in-place/含根位移片段明确区分，实际运动权留 M4。
重载/reimport 时 Play pin 旧 skeleton/clip 版本，默认 Stop 后才采用新 generation；不自动重绑定活动 rig。

### C DX11 skinning

顶点位置/法线/切线/UV/joints4/weights4 常驻 GPU，逐帧上传有界 palettes 与实例参数。
建议采用 Shader Model 5 可用的 structured palette buffer，避免把1024矩阵及其他参数挤进固定 constant buffer。
能力查询返回可支持的 palette/字符数/上传预算，超限显式拒绝；不悄悄 CPU fallback。
main geometry 和 shadow pass 使用相同 palette generation 与 skin function，避免影子停在 bind pose。
前后 frame resources/上传 buffer 有明确 GPU 使用边界，不覆盖仍在 GPU 使用的版本。

### D 数值诊断和资源回收

测试专用 stream-output/等价验证路径读回 GPU 蒙皮结果，正常帧不读回。
比较 bind pose、clip 开头/中间/终点、loop 边界、多 mesh、辅助节点、非对称 object TRS。
先保留已有 ufbx CPU 位置误差 <0.002m 的 fixture 断言；新 CPU/GPU 建议采用
abs error ≤1e-4m + 1e-5×参考坐标幅度，法线角误差 ≤1°，实施前按夹具尺度审查冻结。
四权重截断对原全权重结果的误差另报告，不能把不同权重定义的差异当浮点误差放宽。
GPU/CPU 共享算法对照不是唯一 oracle，还需 ufbx 原始评价和有来源的参考画面。

## 3 测试与退出门禁 G5

- 多角色不同 clip/速度/time、Pause/Step/loop/exact endpoint、30/60/144Hz render 下相同 tick 结果。
- bad skeleton/palette、非法/零权重、stale/foreign generation、错误 clip、已释放资源与预算负例。
- CPU/ufbx、CPU/GPU 数值误差和主视口/阴影对应；validation errors/warnings=0。
- 导入 → 保存 → 重启 → 场景角色 → clip 播放首次闭环；仍不叫完整 Animator。
- 0/1/8/32角色同 mesh、不同 mesh 两组性能，记录 sample/palette/ABI/upload/GPU/GC/live counts。
- 不上传全体 CPU-skinned 顶点，引用释放/Play循环/reimport候选失败不增长 live 资源。

运行所有新增测试与完整 Build.bat 双配置，真实用户 FBX 另需许可与预期动作素材。
绑定语义依据 [ufbx deformers](https://ufbx.github.io/elements/deformers/)，不使用 skeleton inverseBind 简化掉 mesh geometry_to_bone。
