# M7.4-D2 联合图像证据边界

基线：`273ebd662ee2f4db607abdb9e3a208976d96d030`，本地/远端 main 已核对。此片为测试与证据工作，不改变生产 Shader、ABI、默认配置或 AI 权限。完成状态以交付记录为准。

## 分层独立参考

1. 保留原 `EnvironmentJointImages` 的实际静态 FBX 和 ASCII/Binary 角色 FBX → NCA 写入/同进程重读 → 所有 clip 的四个采样时刻。保留独立 CPU 四权重顶点/法线/切线对照、静态模型内部像素独立 IBL 数值参考、IBL/阴影消融。CPU 蒙皮与 GPU 蒙皮的整图同 Shader 对照只证明蒙皮一致性，不是独立光照 oracle。
2. 新测试专用 Tone 读回器把实际 HDR Half 的各通道编码成两字节 UNORM，三次明确诊断提交得到实际几何/PBR/IBL/阴影的 HDR 输入。先用 Half 可精确表示的全屏自发光值校准每个像素。它是测量传输，不是渲染正确性的自证，不加入运行时/产品部署。
3. 独立 `BloomOracle` 不读取生产图或 Shader 文本，执行 box、Gaussian、金字塔、Half 中间舍入；独立 Tone/曝光/sRGB 与顺序 straight-alpha UI 合成逐像素比对最终整图。测得的 HDR 是 oracle 输入，因此不声称独立 CPU 光栅器或完整 PBR/阴影算法 oracle。原数值/积分参考和 API 验证继续保留。

## 覆盖

- 实际静态 FBX，以及两种真实角色 FBX 的每个 clip 中点；不是生成骨架替代真实导入。
- 默认管线与公开独立全图替换示例，相同 512 shadow 合同；每个资源实例验证独立身份和整图一致性。
- 256×256 与奇数 255×193；Bloom 三层，曝光倍数 0.25/1/4，实际 backbuffer 上不透明与半透明重叠 UI。
- UI scissor 在大图元内部，避免把 GPU 导数 AA 当作硬矩形参考。这里验证颜色域、顺序、透明度和裁剪；原 RegisteredUiTests 的圆角/边缘检查不删除。
- Bloom Off、IBL Off、移除阴影 caster 均应产生可测整图变化；拒绝审批后的绑定/图像恢复；暖态提交的 managed 分配、不可变上传、发布次数和有界时长；联合 scene/skin/post 资源归零及 API error/warning 0/0，环境/UI/cache 的测试自有租约随后按所有权顺序关闭。

## 非目标

NCA 重读仍是同进程，不伪称产品重启。搬移 Editor/Player/Null、作者源不可用、正式事务/Undo/Reload/Play 联合及产品成本属于 D3。GPU 时间没有 origin frame，不能写成独立逐帧测量。人工/用户素材、HDR 解码、目标机/自包含、完整性能/长跑、source-recovery 分配调查均保持开放。测试通过也不增加 Agent 编译/执行/GPU 或 Python gameplay 能力。
