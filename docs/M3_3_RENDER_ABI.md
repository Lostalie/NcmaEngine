# M3.3 静态网格 GPU 契约

日期：2026-10-05。状态：static-unlit-v1 与 cpu-bind-pose-v2，G3 未关闭。无光照/双面预览不是 PBR/透明/AlphaMask/动画 GPU 蒙皮。
定义：engine/source/plugins/contracts/NcmaSceneRender.h；托管适配：managed/Ncma.Rendering/StaticMeshRendering.cs
中的 RendererSession、GpuMesh、StaticMeshDraw。

## 协商与固定布局（Windows x64）

Renderer 主 ABI 1.0 表 136 bytes、1.1 表 144 bytes 不变。
新增 1.2 表 152 bytes，offset 144 的 query_scene_render(module, serviceVersion, output, capacity, error)
查询独立服务版本 1。表 size=48、version=1、capabilities=1（bit0 static-unlit-v1）。
未知版本拒绝；不足 buffer 返回 required_bytes，不返回部分表。
未来纹理/材质能力必须独立协商新服务版本，不能改变版本 1 的布局/颜色/坐标含义。

| 类型 | bytes | 内容 |
| --- | ---: | --- |
| GpuMesh | 16 | opaque uint64 handle、uint64 device generation |
| MeshDescription | 48 | size/layout/counts/byte counts/stride/reserved、caller-owned vertices/indices 指针 |
| MeshDraw | 112 | GpuMesh、firstIndex/indexCount/reserved、float16 MVP、float4 linearColor |
| MeshFrame | 64 | size/drawCount、frame/generation、viewport/linearClear、reserved |
| SceneRenderStats | 56 | size/reserved、generation、liveMeshes/residentBytes/meshCreates/uploadedBytes/draws |
| SceneRenderApi | 48 | size/version/capabilities、createMesh/destroyMesh/submitMeshes/stats 函数指针 |
| BindPoseMeshDescriptionV2 | 64 | base MeshDescription(size=64/layout=2/stride=80)、paletteCount/Bytes、caller-owned palette |
| SceneRenderApiV2 | 56 | base(size=56/version=2/capabilities=3)、createBindPoseMesh@48；v1 操作不变 |

无 STL、场景引用、UUID 或 COM 字段。reserved 必须为零；C/C++/C# 三方布局测试。
所有调用为初始化线程专属；服务查询/读 stats 不创建 mesh pipeline。

## 数据与空间

上传 layout1：LE float3 position@0、unit normal@12、float2 UV@24、unit tangent.xyz@32/sign@44，stride48。
索引 uint32 triangles，严格长度/count/finite/basis/索引范围检查，调用期间调用者不得修改 buffer。
C# 在帧外从 MSH1 typed 资产生成独立副本和材质槽分段；不修改 NCA/MSH1 持久格式。
native 不保存材质槽策略，调用者提交合法 firstIndex/indexCount 和预览颜色。

RH 资产空间 +Y/-Z forward，MVP 为 column-vector/column-major，D3D 深度 [0,1]。
System.Numerics CreateLookAt/CreateOrthographic/CreatePerspectiveFieldOfView 的 RH row-vector
model*view*projection 按行展平，即 HLSL 所需等价转置；不得沿用旧 reference 的 LH 隐含相机。
viewport=(x,y,width,height) 为 framebuffer top-left pixels。whole framebuffer 先 clear，draw 限制在 viewport。
颜色/clear 接受有限 [0,1] 线性 RGBA，RGB 明确 sRGB 编码，alpha 不编码；不会声称 UV/normal map 已采样。

## 显式绑定姿态（独立服务 v2）

查询 serviceVersion=2 返回 56-byte 表，bit1=cpu-bind-pose-v2，未知 3 拒绝；v1 的布局、能力和语义不变。
layout2 stride80：前48为**已经变换一次**的预览 position/normal/UV/tangent；uint16x4 原始 binding joints@48，
float4 原始 weights@56，bytes72..79 全零。权重有限非负且≤1、sum 容差0.001，正权重 joint < paletteCount。
palette 为每 geometry binding 的 boneGlobalBind * geometryToBone（column-vector）；
C# row-vector 等价顺序 geometryToBone * boneGlobalBind，展平后为 LE column-major。
1..4096 个 finite affine float16；byteCount=count*64。native 只同步验证 palette，上传原始影响字段与已烘焙几何，
shader **不再次应用 palette**。palette 不是每骨骼表，不存在每帧 palette 更新入口。

C# 由 UUID 已验证的 mesh/skeleton 资产配对后调用 Prepare，严格 codec 深复制，ordered parent 全局 bind、
线性权重混合矩阵，位置变换与 inverse-transpose normal；源 MSH1、原始 joints/weights 与 palette 由 lease 保留。
API 检查骨骼数量，不凭数量证明 rig 身份；UUID 配对归高层资产调用者，通用 UUID cache 属 D。
combined retained/prepared budget≤64MiB；奇异、反射或溢出 blended transform 显式拒绝，不 silently strip skin。
切线使用具名正交 fallback，normal map 禁用，不声称与 DCC/动画蒙皮切线等价。
只在帧外做一次 CPU bake，稳定帧复用 80-byte resident geometry；不执行每帧全顶点上传。
v1 createMesh 拒绝 layout2；显式 v2 createBindPoseMesh 创建，沿用已发布 mesh 的 draw/destroy/stats 生命周期。

## 所有权与故障

C# GpuMesh owner-thread lease 管理，不序列化句柄。设备 generation 当前取单调 renderer identity，
resize 不改变代次；销毁/重建设备产生新代次。不同代次与释放后 handle 拒绝。
目前一个 native renderer 实例，不宣称支持并发多设备或自动 device-loss 重建。

单 mesh 64MiB、每 renderer 256MiB geometry/128 mesh、每帧最多 4096 draws，frame 单调递增。
创建时先全校验，候选 RAII 私有构建、诊断/容器分配成功后发布；C# 注册候选 lease 在 native create 前完成，
native 返回成功后只做无分配的句柄赋值。顶点/索引常驻，无公开 update 操作。
只有首 mesh 创建共享 shader/constant pipeline，最后 mesh 释放它；empty batch 不创建这个 pipeline。

错误 batch 全部预检，frame/present/resource 不变；GPU 执行开始后失败为 fail-stop。
submit/present 与旧 reference 共用 active-frame 和递增 frame 状态，GUI 仍可夹在二者之间。
active frame 不允许 create/destroy/resize。释放先等待 GPU 最长 2 秒，失败保留原句柄可重试；
真实 device removed 允许资源清理，不会自动恢复。退出须先释放 GUI borrow、mesh/reference，再 renderer/window/module。
无 finalizer 强卸载，模块不得在存在 native 资源或托管 lease 时 unload。

## 测量限制

scene stats 的 uploadedBytes 只计首次 geometry 数据，residentBytes 不含共享 shader/constant、swapchain/depth。
每个单 draw 帧传 64+112=176 bytes；GPU 常量更新仍每 draw 一次，native 保留有界 vector copy。
C# 缓存 batch 提交线程分配测试不等于整个进程/native driver/GPU 零分配或 FPS 承诺。
帧数据与 image readback 分离；capture 仅为测试，生产提交不包含同步读回。
纹理、PBR 材质、view-target、Graph stage、UUID cache/跨资产 pin 与场景组件接入仍待后续切片。
