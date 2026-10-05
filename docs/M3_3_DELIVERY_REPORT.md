# M3.3-A 交付记录：网格与显式绑定姿态 CPU/GPU

历史 A checkpoint 报告，下文“B/C/D 未实现”为 A 提交时的状态；最新实现/门禁/批次授权以 [B/C/D 交付记录](M3_3_BCD_DELIVERY_REPORT.md) 为准。A 推送身份：e745f9f80f0ffa371d215711a6e840253bd0a633。

日期：2026-10-05。M3.1/M3.2 提交 e7eea5a007e3c3bea2b89fe2f04835c54b6002ef 已推送并核对远端。
随后推进 M3.3；A 静态网格及显式绑定姿态已实现，最终完整双配置门禁通过；M3.3/G3 未完成。
本报告所属独立 A checkpoint 即本次提交身份；提交后推送并核对 origin/main，核对成功前不进入 B。
用户最新要求：每个 A/B/C/D 小阶段通过完整测试后单独提交推送并核对远端，再开始下一个阶段。

## 实现边界

- C# MeshUploadData 在帧外复制 typed MSH1 数据，准备 48-byte position/normal/UV/tangent、uint32 indices、
  材质分段、bounds、64MiB 预算、具名 fallback 和取消。拒绝 skin，不悄悄丢弃绑定。
- Renderer additive ABI 1.2 查询独立 scene-render v1，仅 static-unlit-v1 能力。
  C ABI 有固定大小/version、opaque handle + renderer/device generation、完整 batch 预检；旧 1.0/1.1 和 reference 不变。
- 原生 StaticMeshKernel 仅持有 GPU 数值资源；无 Scene/World/UUID/作者材质逻辑。
  共享无光照 shader/constant pipeline 按需创建，顶点/索引只在 create 时上传，最后一个 mesh 释放时销毁共享资源。
  MVP 为 RH column-major、D3D [0,1] 深度；明确双面预览，颜色与 clear 为线性输入/sRGB 输出。
- C# GpuMesh owner-thread lease、显式 Dispose、renderer 拒绝带资源销毁。
  C# 先分配并登记候选 lease，native 成功后只赋值；native 诊断/候选分配在发布前完成，避免发布后分配失败失去句柄。
  native 全帧校验后执行；错误输入不推进 frame；执行故障 fail-stop；释放等待 GPU 最长 2 秒，超时保留。
  geometry 限制：单 mesh 64MiB、每 renderer 256MiB/128 mesh、每 batch 4096 draws。
  提交每 draw 112-byte 值，不带 live GameObject、COM 或整份场景序列化。
- Ncma.Rendering 仅新增 BCL Ncma.Assets 引用。导入 kernel/Authoring 仅用于验证程序准备真实 FBX fixture，
  不是 Rendering 或 Player 新运行时依赖；未部署 parser 到 Player，也未修改正式 Editor 流程。

## 验证与证据（原静态切片）

专项已通过：真实非对称 FBX → static import/bake → NCA 严格 UUID 引用解析 → CPU upload → DX11 mesh draw。
不是再次画 reference cube。对 256×256 图像采用独立解析投影 oracle，边界附近 0.02 barycentric margin 不比较，
其余像素颜色允许 1/255；内部 7696 像素，实测 max channel error=0。
图像还检查 sRGB、先近后远的深度遮挡、submesh 索引分段、320×200 resize 与 viewport 偏移。
8 次完整创建/释放，每次复用 33 帧；9 mesh creates/1560 geometry upload bytes（含独立分段 fixture），
最终 live/resident=0。实际启用 D3D11 Debug Layer，errors/warnings=0/0。

负例覆盖 C/C++/C# x64 布局、1.0/1.1/1.2 表协商、短表/未知版本、非法 layout/count/buffer/index/reserved、
owner thread、128 mesh 限制、resident budget 算术、旧 handle/device generation、越界 submesh、NaN、
错误批次不推进帧、active frame 拒绝释放/resize、最后资源释放与模块退出。
resident 极限和 device-loss/fail-stop 状态为白盒注入，**不宣称做了真实 256MiB 压力或驱动 reset**。
旧固定参考/GUI/custom graph/32 renderer 周期继续保留并验证。

原静态切片完整 canonical Build.bat -Configuration Debug 与 -Configuration Release 顺序执行、无 Skip，均 exit 0：

- 每配置 CTest 10/10 native + 18/18 managed/graphics，共 28/28。
- Assets 36/36、Import 24/24、Editor.Core 36/36、Editor Services 40/40、Player 22、Deployment 8/8、Gameplay 53/53。
- Ncma.Managed 构建、managed/native smoke、python -m ncma_tools.cli inspect . 与 Python 42/42 均通过。
- 新 C++ /W4 /WX 与托管构建 0 warnings/0 errors；旧 reference 图像 max/mean=0，实际 debug-layer=0/0。
- 静态 batch 256 帧，每帧一个 draw 176 ABI bytes，提交线程测得 0 managed allocated bytes；
  本机 Debug 17.3602ms / Release 15.0967ms 为该循环墙钟值（Render/Present、VSync off），非通用 FPS 或全引擎基准。
- M2 audit_passed=true、h8_accepted=false，人工/目标环境/完整性能/长稳仍待验收。

静态切片原闭环日志：out/verification/m3-3/Debug-static-closure.log、Release-static-closure.log；
两配置静态结构结果与详细 managed CTest 输出已复制为 Debug/Release-static-mesh-results.json 和 Debug/Release-managed-ctest.log。
Debug-static-final.log 也曾全量通过，之后补齐候选发布前分配次序。
最终审查另补 active 帧中的 Capture/Present 故障关闭：统一结束 GPU timing query、清除 active/bindings，
使 fail-stop 之后仍可释放 mesh/reference。注入 post-submit capture failure，验证拒绝 Present 与资源安全 drain；
非真实驱动 reset。该边界改动的完整双配置二次复验均 exit 0，最终以 Debug/Release-fault-closure.log 为准，
每配置仍为 CTest 28/28、Python 42/42、上述全部托管/原生测试通过。
最终静态 JSON 和 managed CTest 副本也已更新为本轮结果；Release 部署的 Renderer DLL SHA256 与已测试构建一致。
已保留中间失败：Debug-static-build.log 为测试 unsafe 局部捕获编译错误；Debug-static-recheck.log 为
加载器仍拒绝 Renderer minor=2。另一次专项发现 resize 断言点位于三角形外，已校正为几何内部点，未放宽颜色容差。

专项图像/结构报告：out/verification/m2/render-{Debug,Release}/static-{asymmetric,resized,ranges}.bmp、static-mesh-results.json。
两配置均 7696 内部比较像素/max error=0、9 creates/1560 geometry bytes、结束 live/resident=0、实际 validation=0/0。
不将后续 targeted CTest 替换的 LastTest.log 当作整阶段唯一证明。
out/bin/NcmaEngine.exe 是本轮已测试 Release C# apphost，保留部署 backup/journal；
正式 UI/场景仍使用原 reference/CPU 预览，新的 static GPU 服务未自动接入编辑器面板，不能据部署宣称 G3 已关闭。

## 未完成

WIC/PNG/JPEG/授权纹理与 mip 上传、PBR MaterialDefinition/MaterialSet 和作者覆盖、AlphaMask、normal map、
UUID→GPU generation cache/跨资产 lease、离屏 view-target/GUI 图像、公开 Graph 的 typed mesh stage、
正式 Editor/Player 场景渲染均未实现。A 的 bind/palette 已补齐，动画 GPU 蒙皮仍未实现。native 批次仍有有界 vector copy，
当前测量不等于全引擎零分配、性能目标或 FPS 承诺。GPU skinning 属于 G5，Vulkan 绘制仍未实现。
不创建新 Agent mutation 绕过 EditSession，不关闭 M2 人工/自包含/完整性能/长稳门禁。

下一步按 M3_3_IMPLEMENTATION_PLAN.md 推进 B/C，再补齐 D 和 G3；不能跳到 M3.4 或宣称完整 PBR 后端已实现。

## A 补齐：显式绑定姿态与 skin/palette 契约

独立 scene-render v2 56-byte 服务，新增 64-byte bind 描述与 layout2 stride80，v1 保持不变。
C# BindPoseMeshUploadData 在帧外根据 ordered bone hierarchy 与 geometry binding 构建 palette，
CPU 计算一次绑定姿态、inverse-transpose normals；GPU 驻留绘制，不再次应用 palette，不每帧重传顶点。
原 MSH1、joints/weights、palette 保留在不可变 DTO，并由 GpuMesh lease 持有直至成功释放。
combined retained/prepared geometry/source/palette budget≤64MiB；奇异/反射/溢出混合矩阵明确拒绝。
重建正交预览切线、禁用 normal map，不承诺 DCC 等价；尚无动画 palette 更新或 GPU skinning。
高层通过已验证 UUID 配对 rig/mesh；该 CPU API 检查骨骼数量，不能凭数量证明 rig 身份，通用 cache 属 D。

专项正例：两个骨骼的全局绑定、不同 geometry-to-bone、0.25/0.75 混合权重、旋转/缩放与 bounds 数值 oracle，
原始 skin 独立快照、80-byte joints/weights/padding、创建时 palette LE 布局。
8 轮 GPU lease × 33 帧，整张绑定姿态图像与独立明确指定的静态三角形逐像素完全相同，证明绑定不重复应用。
实际 ncma_skin_weights_7400_ascii.fbx → Character root/NCA → UUID mesh/skeleton → bind prepare → DX11，5776 个着色像素。
此计数是有可见几何证明，不是任意 FBX/DCC/动画兼容证明。
v2 短表/未知版本、palette byte/count/nonfinite/nonaffine、joint/weight/padding、静态 v1 拒绝 skin 布局、
失败不发布/无 GPU 资源泄漏与正常释放均测试；复用 v1 的 owner/generation/batch/fault/退出保护。
负例 CPU 覆盖错误类型、rig count、影响范围、零权重、反射/奇异变换、缺数组和取消。

首轮 Debug-A-closure.log 在 NcmaRenderingTests 失败：新增测试把导入根资产误写为 SkinnedMesh；
ModelImportPlanner 严格要求 Character 根资产。只修正测试类型，没有放宽生产验证或图像容差。
修复后 targeted NcmaKernelReferenceCapture/NcmaRenderingTests 2/2 通过。

## A 最终门禁（本次提交依据）

最终代码顺序执行 Build.bat -Configuration Debug、Build.bat -Configuration Release，无 Skip，均 exit 0。
日志：out/verification/m3-3/Debug-A-final.log、Release-A-final.log。
每配置 CTest 原生 10/10 + 托管/图形 18/18；Ncma.Managed 与全部托管项目构建、managed/native smoke、
python -m ncma_tools.cli inspect .、Python 42/42、部署测试和完整可恢复部署均通过。
新代码构建 0 warnings/0 errors；旧 reference max/mean=0；真实 Debug Layer errors/warnings=0/0。
静态仍为 7696 内部像素/max error=0、9 creates/1560 bytes、256 稳定帧 owner-thread managed allocation=0。
本次循环墙钟 Debug 20.7603ms / Release 16.4291ms（单 draw/176 ABI bytes，含 Render/Present，VSync off），非 FPS 保证。
绑定专项每配置 8轮×33帧、整图 static oracle 完全相同、实际 FBX 5776 着色像素；
10 creates/2424 geometry uploaded bytes（含 static oracle 与 FBX 各一个 mesh），最后 live/resident=0。
palette/source 在 C# lease 保留，不计入 GPU geometry uploaded/resident 字节；未声称全部进程零分配。

本轮结构报告与详细 CTest 输出已保存在 out/verification/m3-3/{Debug,Release}-A-{static-mesh-results,bind-pose-results}.json
及 {Debug,Release}-A-managed-ctest.log，不以 targeted 覆盖日志代替完整门禁。
out/bin/NcmaEngine.exe 是已测试 Release apphost，Renderer DLL 部署 SHA256 与 Release 已测试构建核对一致。
正式 Editor/Player UI 尚未接入新 mesh 服务；未新建 Agent mutation。M2 audit_passed=true、h8_accepted=false，
人工 UI/MCP、自包含目标环境、完整性能和长稳门禁仍待完成；G3、B/C/D、GPU 动画蒙皮及 Vulkan 绘制未完成。
只提交本次 A 的源代码/测试/文档，排除 .vs、NcmaEngine.vcxproj.user、out/ 和部署备份。
