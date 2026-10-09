# M7.1-C3 独立 2D shader 运行契约

## 所有权与扩展

C# 管理 Flat2D 精确 UUID/hash 目录、选择、批准和复制 metadata。默认与用户均走 B 的真实编译/反射/声明校验；CPU preparation 不创建 GPU 对象。无 live World、AI 推理、Agent 编译授权、Python 游戏脚本或文件扫描入口。

普通用户继续用现有 UiCanvas/style/color/opacity/radius/UV/clip；可信扩展可提供同一固定绑定接口的完整 VS/PS 对。该切片不注册任意 RenderGraph Feature、额外 sampler/常量或任意材质协议，也不承诺任意 HLSL 语义正确或安全。换算法不能改变宿主的顺序/纹理单位/混合约定。完整管线定制继续由明确的模块协议演进。

`RegisteredUiShaders.Prepare` 及 create/replace 都要求 exact renderer 生命周期、owner thread、无活动帧和可信 off-simulation approval；每次安装重新检查，跨准备重入拒绝。没有 tick 中编译、IPC 等待或异步回调直接修改 GPU 的入口。

## ABI 和固定布局

Renderer 原表不变，query11 返回40字节 API1/caps1：copy_source/create/replace。bytecode 使用已有32字节 pair POD，每stage 4..1MiB、源复制最多256KiB，调用者所有。没有 COM/STL/托管引用/可序列化运行句柄跨 ABI。

- 顶点52字节：POSITION0 float2 @0、TEXCOORD0 float2 @8、COLOR0 float4 @16、TEXCOORD1 float2 @32、TEXCOORD2 float2 @40、TEXCOORD3 float @48。
- VS `Frame` b0 长16字节：Size float2 @0、Pad float2 @8；固定像素坐标到裁剪坐标和所有输出语义。严格校验完整 cbuffer 成员，包括未使用 Pad。
- PS `Image` Texture2D t0、`Linear` sampler s0，唯一 SV_TARGET0 float4；stage 输入/输出与同源模板逐项核对。没有额外资源、cbuffer 或接口槽。
- RGBA8 display-space 值，非线性 PBR 输入不适用；straight alpha，color SrcAlpha/InvSrcAlpha、alpha One/InvSrcAlpha；scissor 和 disabled depth/cull 状态归原生数值执行器。
- ordered resident display list 不跨 overlap/texture/clip 排序。UI 图像、列表、pin、内存和批次预算沿用 query6/7，不增加每帧顶点上传。

## 原子替换和缓存

create 只允许尚无 UI kernel；replace 只允许已有 kernel，不暗中创建。全部持有的 UI presentation leases 阻止替换。候选 VS/PS/input layout 用 RAII 建立；实际反射、诊断、2秒有界 drain、device check 成功后 noexcept 交换。非 device 错误保留旧组；device loss fail-stop。GPU等待仅显式可信准备阶段，不进入正常 UI tick。

成功后 C# 发表 RegisteredUiShaders 与递增 UiShaderGeneration；异常不变。图片、列表、纹理 pin、target 不重建。缓存 target 的旧像素及旧 content revision 完全保留，不自动重绘或伪称新效果；宿主 cache key 纳入 UiShaderGeneration，提交更新的 content revision 后产生新效果。无需重建列表。shutdown 仍先释放列表/image/target/lease，最后关闭 renderer/plugin。

## 部署边界

独立 UI 示例启动时创建官方注册 pair，然后关闭 preparation 批准，事件驱动刷新。16文件测试副本包含8个共享 managed依赖、4个 apphost/runtime文件、4个native DLL（Platform/Renderer/Text/glfw3），无3D专用插件、Editor、Gui、Scene、Physics、Animation、导入器或场景资源。字体仍显式读取本机系统文件，不复制/分发。

该包 framework-dependent，仅证明当前机器 moved/source-free apphost；不是 self-contained/目标环境验收，也不是 C4 的持久化 shader runtime package。官方 Editor/Player 原默认创建不在本片切换，旧 query6 是仍在使用的正式契约，不是新旧兼容桩。Vulkan/OpenGL、M7.2以后和既有人工/性能/长跑门禁仍开放。
