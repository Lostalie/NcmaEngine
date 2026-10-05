# M3.3 网格、纹理与材质 GPU 契约

日期：2026-10-05。冻结 static-unlit-v1/cpu-bind-pose-v2；新增独立 resource-pbr-v3。v1/v2 仍不是 PBR/AlphaMask/动画 GPU 蒙皮，v3 明确提供受限材质资源绘制，不表示完整场景后端。
定义：engine/source/plugins/contracts/NcmaSceneRender.h；托管适配：managed/Ncma.Rendering/StaticMeshRendering.cs
中的 RendererSession、GpuMesh、StaticMeshDraw。

## 协商与固定布局（Windows x64）

Renderer 主 ABI 1.0 表 136 bytes、1.1 表 144 bytes 不变。
新增 1.2 表 152 bytes，offset 144 的 query_scene_render(module, serviceVersion, output, capacity, error)
查询独立服务版本 1。表 size=48、version=1、capabilities=1（bit0 static-unlit-v1）。
未知版本拒绝；不足 buffer 返回 required_bytes，不返回部分表。
纹理/材质能力独立协商服务版本 3，不能改变版本 1/2 的布局/颜色/坐标含义。

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

查询 serviceVersion=2 返回 56-byte 表，bit1=cpu-bind-pose-v2；未知 4 拒绝，3 为独立资源表；v1 的布局、能力和语义不变。
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
纹理、PBR 材质、view-target、受限 Graph stage 与 UUID cache/跨资产 pin 在独立 v3 切片实现；正式场景组件接入仍属于 M3.4。

## 独立 resource-pbr-v3（不是 v1/v2 的前缀扩展）

定义 NcmaResourceRender.h；Renderer 1.2 同一 query 的 serviceVersion=3 返回 size/version=72/3、capabilities=0x3c。
表为七个函数指针：createTexture/createMaterial/createTarget/destroyResource/submit/stats/captureTarget。
纹理和离屏 target 使用不同类型的 generation-scoped opaque key；mesh 由 v1/v2 创建，不能互换类型。

| 类型 | bytes | 主要契约 |
| --- | ---: | --- |
| TextureMip / TextureDescription | 16 / 48 | 完整 floor-halved RGBA8 mip；连续 offset/rowPitch=width*4，无尾部字节 |
| MaterialDescription | 160 | 6 texture keys、linear base/emissive、metal/rough/normalScale/cutoff、数据通道和 flags |
| TargetDescription | 16 | RGBA8 color + D32 depth；有界尺寸，无 COM/TextureID |
| ResourceDraw | 240 | mesh/material keys、合法 triangle range、column-major Model/MVP/inverse-transpose Normal |
| ResourceFrame | 136 | 单调 frame/device generation、target/viewport/linear clear、camera/directional light/exposure/ambient/mode |
| ResourceStats / ResourceApi | 72 / 72 | 纹理/材质/target 的数量、resident/upload/create/draw 与函数表 |

格式1=linear RGBA8，2=sRGB RGBA8；baseColor/emissive 必须 sRGB，normal/metal/rough/AO 必须 linear。
tex slots0..5 顺序上述；missing key={0,0} 使用 native white/flat normal。数据通道0..3，metal/rough/AO packed bytes0..2。
flags bit0 AlphaMask、bit1 normal-map、bit2 normalYDown；bit2 必须与 bit1 同用。
surface={metallic[0,1],roughness[.045,1],normalScale[0,4],alphaCutoff[0,1]}。
UV 从左上开始；normal Y 反转只在 shader 按显式 flag 执行；切线 sign 决定 B=cross(N,T)*sign。
Model 必须有限、正 determinant 的 invertible affine，Normal 上3×3须与 Model inverse-transpose 一致；MVP 有限。
拒绝负/奇异 model，不代表动画缩放/MikkTSpace 已验收。

mode0=双面 opaque/AlphaMask GGX+Schlick，直接 directional/ambient/AO/emissive，ACES exposure→sRGB；
无 shadow/IBL/独立 HDR attachment。mode1=unlit sRGB，mode2=raw normal diagnostic，不 tone-map。
target={0,0} 写 swapchain，否则写独立 color/depth；capture 是测试专用 staging，不在生产提交路径。
每 batch最多4096 draws；单 draw/frame ABI字节240+136=376，不带场景 JSON、UUID、对象引用或 Python。

单 texture mip合计≤64MiB/最大边4096，每 renderer≤256textures/256materials/16targets，texture+target≤256MiB。
target resident 按 width*height*8，texture upload/resident按 RGBA mip字节，material counters不含共享常量/白色与法线fallback。
geometry 的128mesh/256MiB预算独立。查询/texture upload不创建 PBR kernel；首次 material 创建，最后 material 释放时销毁。
immutable [D3D11 initial subresource](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11device-createtexture2d) 只在 create 时复制；caller buffers不保留。
material 在 native pin 它引用的 texture；active帧/依赖未释放时 destroy Busy。
所有 create 是全预检/候选 RAII/发布前诊断；执行故障 fail-stop；GPU完成等待/模块退出规则沿用上文。

C# RenderResourceCache 以 UUID+asset generation+content SHA+类型/变体在单设备 owner 内缓存；lease pin不可变版本。
MaterialSet 仅为 C# UUID槽列表，不新建 native 场景容器。缺材质/纹理/UV/安全切线用结构化诊断，不能静默错用法线。
公共 Graph ResourceGeometry=5/ShaderContract=3/ResourceOutput，要求 ResourcePbr capability；目前只准一个 typed stage，禁止与旧 reference passes 混搭。
预设/参数/可信 Feature、Stage替换、全管线替换共用此契约；不支持任意 shader/DLL 或多阶段资源流水线。

## 工具解码与作者格式

NcmaImage.h 的 ncma_image_decode_v1 输入已批准的 copied memory，Info32/Error528，PNG/JPEG≤16MiB。
WIC仅在 NcmaImportKernel 工具 DLL，Player不部署；无来源路径参数，不自动跟随 FBX 外部/内嵌纹理。
验证 container、单帧、4096边长/RGBA≤64MiB；ICC和非identity EXIF orientation 明确拒绝，需另行离线转换。
managed 在 RGBA分配前验证完整 mip≤64MiB；准备/解码不得在模拟/渲染 tick 中调用。
输入不trusted sandbox，WIC owner必须MTA，取消在受限 WIC调用前后和每 mip行检查，不承诺中途强制终止 WIC。
参见 [Microsoft WIC stream decoder](https://learn.microsoft.com/en-us/windows/win32/api/wincodec/nf-wincodec-iwicimagingfactory-createdecoderfromstream)。

TXR1：LE32 magic0x31525854/version1/width/height/semantic(1color,2normal,3data)/normalYFlag/mipCount/pixelBytes；
随后每 mip16 bytes{width,height,offset,rowPitch} 和 owned RGBA字节。严格canonical full-chain/无尾部字节。
v1 mips用整数 area footprint（含odd edges）；Color先linear alpha-aware平均再sRGB，Normal重归一，Data线性平均。
Color无 profile 的输入按 sRGB解释。生成算法为本引擎确定v1，非DCC/Mikk或压缩纹理等价。
.ncmaterial/.ncmatset 严格 JSON1/required/closed/duplicate-free/有限数值，只持 UUID，独立于 immutable imported MAT1。
G2只有来源名称/槽，不存在Phong值；转换返回 material_slots_only/pbr_default 损失诊断，不伪造任意DCC PBR匹配。
ncma.assets.material.edit 是可逆 participant，经 host exact path→UUID/dependency-kind/current grants，asset revision、
EditSession原权限/世界revision与 filehash校验；复用统一历史和受控 journal恢复，默认不授权。
