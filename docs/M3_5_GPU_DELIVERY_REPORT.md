# M3.5 场景角色、片段播放与 GPU 蒙皮交付记录

日期：2026-10-06。基线：M3.4 `3c7d7b61b4de474b973186536af254d8b8c1cba4`。
状态：实现与最终顺序 Debug/Release 完整回归均通过，最小片段播放/DX11 蒙皮切片 G5 关闭。本记录不将最小 ClipPlayer 称为完整 Animator、完整 3D 后端或整个 M3 完成。

## 实现

- 纯 C# `Ncma.Animation` 持久 ClipPlaybackData 与 committed-tick ClipClock；成功固定步后的只读观察器推进时钟，失败步不推进。Play Pause/Resume/Step、30/60/144Hz 插值、speed/loop/end/pause、故障后的成功 tick 计数均有测试。Edit 预览独立，不改 Play/World，不自动加入 Undo。
- 新 `NcmaAnimationKernel.dll` 只执行不可变 rig/clip 的 bounded pose-only 数值，最多 32×1024 骨骼。mesh-local geometryToBone × boneModel binding palette 保留辅助节点/不同 mesh 绑定；不使用统一 skeleton inverseBind，不由 C++ 驱动 gameplay。独立 pose ABI 1.0 不是旧 Animation ABI 1。
- 正式 NCA、精确 model/skeleton/clip UUID 与 generation、SceneDocument 保存/重启、派生 Edit/Play 生命周期接通。Play pin 旧代；reimport 候选失败不破坏文档/已安装资源；reload 保留当前 World 的时钟/租约。缺 clip 产生诊断、保留 UUID，不自动 bind-pose fallback；严格 Player 拒绝。没有 runtime handle 序列化或逐帧文档编码。
- Renderer 新增独立 query 5，原 query 1–4 冻结。原始 p/n/uv/tangent/joints4/weights4 常驻 GPU；SM5 compute prepass 输出 GPU 顶点缓冲，正式 query 4 主画面与阴影共用结果。每帧只上传 bounded palettes；normal inverse-transpose、tangent Gram-Schmidt、UV/sign 保持原值；对象变换只应用一次。
- 三个 GPU palette 槽通过非阻塞完成查询避免覆盖在用数据；压力返回 BUSY，C# 记录 backpressure/轻量 clear，不阻塞 simulation tick、不异常 fault 玩法、不自动 CPU 蒙皮。animated draw 必须使用 exact frame 输出；未经验证的 bind-AABB 相机/light 裁剪关闭，两个集合/layer 仍独立。
- Editor/Player 接线可信懒加载 pose 数值插件，Player 运行不需原 FBX 或 Editor 导入 DLL。Null 包不部署 native pose；通用 3D-capable DX11 包含懒加载 DLL，纯 2D 专用裁剪包尚未实现。资源按 GPU scene → clip/rig/context → renderer/plugin 顺序关闭。
- 现有只读 `ncma.render.get_profile` 添加 animation costs/ABI/backpressure。无新增 Agent GPU capture/live World 写权限；持久 authoring 仍走同一 Editor.Core 事务/Undo。

完整布局、容量、线程、错误/释放与 fail-stop 边界见 [契约](M3_5_RENDER_ANIMATION_ABI.md)。GPU 时间是最近完成的 timestamp 样本，不保证当前 frame；capture/wait 只用于可信测试或关闭，不是正常帧操作。

## 自动验证范围

保留静态/reference/render graph/材质/纹理/格式与 removed-format 拒绝测试；新增：

- 原生 pose 与 query 5 的布局/短表/预算/owner-thread、bad rig/palette、zero/negative weights、bad joints、foreign/stale、重复 output、frame mismatch、释放后拒绝。完整预检失败不 dispatch；GPU 执行故障是 fail-stop，不称回滚。
- 15 项 pose/clock 托管基础检查，包括独立 System.Numerics 模型矩阵 oracle、不同 mesh binding、32×1024 batch 稳态 GC=0、真实 ASCII/binary FBX。保留 ufbx_evaluate_scene 原始全权重位置误差 <0.002m 的导入测试。
- 真实 ASCII/binary FBX GPU vs Character ABI 2 四权重 CPU 的 start/middle/end-left-limit/loop-edge 位置，normal/tangent 单位正交、UV/sign。Pose inclusive endpoint 单独验证；旧 Character exact duration 的循环语义不拿来放宽误差。
- 双骨骼辅助节点/mixed weights、非对称正非均匀对象 TRS、相机外 caster；CPU 测试基准与 GPU 动态 PBR/HDR/主画面/阴影逐像素对应。CPU 变形只在测试基准，不作为产品顶点上传路径。
- 正式 NCA 保存/重启 → GPU；8 次 Edit/Play/Pause/Step/Reload/Stop，根位移只报告而不写 Transform；成功 reimport pin/失败候选/缺失 clip 严格与宽松检查；最终 native/GPU leases 归零。
- 0/1/8/32角色，同 mesh/不同 model 两组：sample/palette/C# ABI/native dispatch/GPU timestamp/GC/上传/live bytes。缓存帧托管分配与 CPU-skinned vertex upload 均为 0。测量为 8 帧、每帧强制 drain GPU 的小夹具；不作为 FPS/吞吐或性能生产验收。
- 真实 ASCII/binary FBX 导入派生文件后，运行项目移除 source FBX，PlayerRunner DX11 各重启 3 次、固定步推进/真实绘制/validation 0/0/关闭无错误/文件 pin 释放。独立包迁移/hash/路径/篡改检查仍保留；新增包中 pose ABI/数组形状/lazy 和 Null 排除负例。

数值 oracle 分层：独立 synthetic 数学 + 原始 ufbx/full-weight 导入断言 + 四权重 CPU/GPU + 实际 shadow 图像。Character 和 GPU 使用同一导入数据，后两项不是独立完整 DCC 算法 oracle；不能据此承诺所有用户 FBX、dual-quaternion/morph 或任意 scale inheritance。

## 最终证据

最终完整构建日志：`out/verification/m3-5/Debug-skin-final5.log`、`Release-skin-final.log`，顺序执行、退出码均为 0、无 Skip。每配置通过 31/31 CTest（native 11 + managed/application 20）、Python 43/43、managed/native smoke、新格式严格校验/旧格式拒绝、inspect 和部署前后审计；其中 pose 15/15、scene foundation 68/68、Editor Services 42/42、Player 26 项、部署恢复 8/8、Assets 38/38、Import 24/24、Gameplay 53/53。代码/Shader 编译无新增警告；全包 hash/路径核对后部署 Release 至 `out/bin/NcmaEngine.exe`，保留 backup/journal。M2 audit_passed=true 不代表 h8_accepted（仍 false）。

双配置 `skin-numerics.json`：ASCII 最大位置误差 1.1175871e-8m、法线角误差 0.039565°；binary 最大位置误差 9.5367432e-7m、法线角误差 0.034265°。断言仍为 abs≤1e-4m+1e-5×参考幅度、normal≤1°；原始 ufbx/full-weight 的 <0.002m 断言未放宽。`skin-shadow.json` 在 0/.5/1s 的 CPU/GPU 最大通道误差均 0，阴影变化像素 3668/3606/3213，validation errors/warnings 0/0。

32 个两骨骼角色、8 测量帧（强制 GPU drain）数据如下。sample/palette/ABI/GPU 为最近测量样本，总时长包含所有测试 drain，不作实时 FPS 换算。

| 配置 / 资源 | 8帧总 ms | sample / palette / skin ABI ms | skin GPU ms |
| --- | ---: | --- | ---: |
| Debug / 同 mesh | 20.5772 | 0.8663 / 0.0258 / 0.2110 | 0.010272 |
| Debug / 不同 model | 24.4414 | 0.9983 / 0.0254 / 0.1783 | 0.010400 |
| Release / 同 mesh | 7.3828 | 0.0236 / 0.0115 / 0.0592 | 0.010304 |
| Release / 不同 model | 6.8780 | 0.0285 / 0.0087 / 0.0968 | 0.010432 |

两配置所有 0/1/8/32 行 allocatedBytes=0、immutableVertexUploadBytes=0、validation=0/0；32角色各 8 次 scene ABI +8 次 skin ABI、palette 65536 bytes、skin GPU resident 12600080 bytes，最终 leases 归零。空场景 ABI/upload/resident 为 0，GPU 时间 null，不借用旧样本。此内存数包括固定 ring，不包括 native scratch、driver/进程总内存。

独立满容量 pose profile 为 32×1024 骨骼、8 次采样，Debug native 2981.9235ms / trusted managed bind-matrix baseline 35.4916ms，Release 25.9761ms / 5.6820ms。该 baseline 不做 clip 评价、完整校验或原子 staging；equalContracts=false、nativeAdvantageClaimed=false。它不能证明 C++ 优于 C#，也不是大骨架动画实时预算验收。

foundation 的 `gpuSkinning=false/g5Accepted=false` 和 stdout 仅描述独立 pose/CPU 检查，保持历史范围；整个 G5 的判定依据本节独立 GPU/scene/Player 证据，未把基础用例伪装成 GPU 验收。
构建自动生成 ignored `out/verification/m3-5/<Configuration>/pose-*`、`out/verification/m2/render-<Configuration>/skin-*` 与 Player reports，不提交部署/备份/二进制证据。

先前失败日志保留：native 编译、namespace 类型冲突、端点参考语义、重复使用单次 importer、palette BUSY、interface foreach 的 40-byte 分配、static/skin 混合释放泄漏、注册计数/负例 metadata、包 ABI 审计及 PowerShell singleton array 等。均修复后整轮重跑，未删掉失败证据或放宽图像/数值断言。

## 限制与后续

这是 LBS 四权重/最多32角色/1024骨骼的 DX11 最小片段播放器。骨骼非均匀、负/奇异缩放拒绝；没有 Animator 图、Montage/IK/重定向/压缩或动作/连击通知链路。root displacement 只读，M4 CharacterMotor/物理运动权未实现，未向 Play 添加 Physics Step。

同源 GPU stream 当前按实例复制，palette ring 固定 12MiB+16 GPU bytes，另有 4MiB+768 CPU scratch；不称最优内存。animated conservative bounds/裁剪、增量资源准备与完整 Inspector/时间轴控件留后续。Headless 只推进既有 World 固定步，不创建本切片的呈现派生 rig/clock，玩法动作动画调度留 M4/M5。Debug 满容量 pose 数值验证较重，原生没有未经公平对照就被宣称快于 C#；完整相同契约比较与生产性能门禁仍待后续。

Vulkan 绘制、GUI 合成离屏视口、IBL/透明/多光/场景 CSM/contact 尚未实现。M3.6–M3.9 未开始；M2 人工 UI/输入法/DPI/第三方 MCP、自包含目标环境、长稳/完整性能门禁继续 pending。
