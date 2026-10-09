# M7.1-C1 注册 Scene Tone 运行契约

C1是C的第一可测试片：同一实际ShaderCatalog中的默认／用户Tone，通过B服务真实编译、反射及GPU执行。
未实现C2 Geometry/Shadow/Skin注册、C3纯2D shader注册、C4正式宿主切换与shader运行包；不关闭整体C/M7。
只DX11，无Vulkan/OpenGL假支持。没有新World、Python gameplay、推理或Agent代码执行权限。

## 同源准备与公开边界

DefaultSceneTone.CopyCatalog取现有ScenePipelineKernel的实际HLSL复制（包括同源场景函数），无文件或网络扫描。
可信C#扩展把用户定义与官方VS/PS加入同一不可变Scene3D ShaderCatalog；Prepare必须Require精确UUID/content hash，
检查Tone闭合声明，再调用同一ShaderCompilerService。其strict/werror/O3/SM5、布局反射／错误脱敏及预算继承B。
仅当前VS/PS准备，不为未选shader初始化GPU。C1不执行外部shader dependency，selected definitions非空依赖拒绝。
RegisteredSceneTone是私有复制bytecode与反射的CPU候选，绑定准确RendererSession；无独立GPU句柄、持久指针或文件发布。
CopyMetadata返回结构化catalog/descriptor/bytecode hash和UUID，不返回源码／live资源；A目录Compiled=false与候选Compiled=true区分。
没有将此metadata注册成MCP endpoint，未来Agent inspection仍需exact approval/session/hash/TTL；不存在的检查不得假支持。

Prepare与每次创建／ReplaceTone均重查owner、准确renderer生命周期、健康／idle和显式trusted host preparationAllowed。
renderer级非重入保护防止审批回调跨候选嵌套准备或安装。回调是可信纯只读宿主约定，不是任意代码安全沙箱；
C1未接入World调度／Editor命令权限，不能仅给`true`就声称正式tick保护或Agent权限已实现。
当前正式宿主的lazy shader初始化路径不在C1暗中改动；主动调用编译／替换必须由可信off-simulation/off-render准备代码完成。

## ABI、绑定和像素输出

Renderer1.2／152字节、query1–8不变。新增query9/API1／40字节/caps1：copy_tone_source、create_scene、replace_tone。
NcmaShaderPairV1为32字节，VS/PS caller-owned显式长度，各4字节至1MiB；无STL/COM/异常跨ABI。
source caller容量最多256KiB，短buffer不写输出或required，NcmaError.required_bytes单独报告。
创建scene继续使用现有query4的16字节SceneDescription和16字节资源key，释放和提交仍走既有scene lease接口。
native同步复制RHI bytecode blob并创建program；不保留caller pointer/span。
RHI选择完整source或完整VS/PS bytecode，拒绝半对／超预算／source+bytecode混用，不发生静默compile fallback。

C#核对完整闭合Tone输入及所有常量／资源，native再次从实际bytecode核对：

- VS SM5：唯一uint SV_VertexID输入，无cbuffer/resource；输出float4 SV_POSITION、float2 TEXCOORD0。
- PS SM5：相同输入签名、系统语义／分量／类型/stream；唯一float4 SV_TARGET0输出。
- C/b0：400字节，4个column-major float4x4 +9个float4，全部13成员的名字/type/offset/span精确，包括末ShadowParameters。
- BaseTex/t0 float Texture2D和S/s0普通sampler；禁止额外资源、错slot/维度/type、comparison sampler。

这是封闭的全屏HDR Tone执行约定，不是任意HLSL/任意mesh/pass布局的通用GPU支持。
geometry/shadow/skin绑定沿用原内核，此片不冒称已注册或可替换这些stage。
同一个ScenePipelineSession内全部Tone pass共用这一对VS/PS；没有逐Feature独立shader或任意pass契约。
独立标量ACES/sRGB整幅图和用户BGR通道排列定义容限1字节；真实PBR geometry图像另与既有独立GGX oracle及完整channel排列核对。

## 原子替换与关闭

创建只有完整bytecode stage/link/output/绑定检查及GPU分配成功后才发布scene key；失败不发布输出／计数。
所有可能分配的诊断Validation位于scene key／计数／Tone交换之前；Tone发布noexcept，发布后不再进行可抛异常的诊断操作。
test-only诊断异常覆盖创建与替换失败：无新key／计数、旧stage保留、候选释放。生产ABI不暴露故障注入。
ReplaceTone先验证，创建候选，再使用现有有界GPU Wait/drain；成功后ClearState并交换Tone、释放旧program。
超时返回ShutdownTimeout，保留旧stage/scene ownership，RAII释放候选（含异常路径）。设备丢失走已有fail-stop，不能冒称回滚GPU或自动恢复。
没有异步retirement或目标性能保证；同步drain仅允许可信off-frame入口，不能放入simulation/render tick。
geometry/HDR/depth/target/材质保持原lease，替换不重建scene／上传mesh/texture。
ScenePipelineSession.RegisteredTone只在native成功后更换；关闭scene沿原owner/active/drain规则，失败保留资源阻止Renderer/插件卸载。
纯2D未使用scene时无scene/tone/skin资源；单独读取服务表／空stats不初始化3D内核。此片不宣称纯2D shader框架已接入。

自动验证与失败记录见[交付](M7_1_C1_DELIVERY_REPORT.md)。既有人工UI/MCP、用户FBX/材质、目标/自包含/性能/1h门禁保留。
