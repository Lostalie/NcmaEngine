# M7.4-B 线性 HDR 子图与 Bloom 运行合同

2026-10-14。范围为 DX11 实际执行内核、公开 C# 图/效果声明、无源码包、独立图像与资源边界测试。见[总方案](M7_4_IMPLEMENTATION_PLAN.md)、[交付](M7_4_B_DELIVERY_REPORT.md)。正式 Editor/Player 默认配置没有切换；C/D未完成。Vulkan/OpenGL仍是下一版本扩展，不宣称已实现。

## 所有权和执行位置

C# `PostProcessGraph` 描述输入/输出、格式、颜色域、程序与参数；`BloomFeature` 仅使用这套公开入口。C++ `PostProcessKernel` 执行复制后的固定 POD pass，不拥有 World、效果策略、编辑器事务或业务状态。

线性 Scene HDR → 受检 HDR 子图 → 原有一次 Tone/曝光/sRGB → 显示域游戏 UI/编辑器 GUI。已有 Geometry/Shadow/Skin/Environment 程序不变。挂接子图要求当前场景恰好一次 Tone；旧版无子图的多 Tone 行为保留，不能误当作线性中间阶段。资源0是每次调用借用的 scene HDR，原生不跨调用持有它。

构图、cook、准入和配置为可信 owner 线程离帧操作；host 提供显式非 simulation 审批。审批不是隔离沙箱，不能用总是返回 true 的回调声称不可信输入安全。Submit 只消费准备好的资源，不做编译/文件 IO/回读/推理。持有 Renderer 的 lease 期间调用；先关闭 Scene，再关闭 Renderer/plugin。不能直接提供任意 GPU 指针。

## 独立 ABI 与预算

Renderer query16 / API v1，40B函数表、capabilities3；此前 query1–15、SceneFrame、C400/C416和NCS1冻结，无保留字段复用。

| POD | x64字节 | 内容 |
| --- | ---: | --- |
| Program | 40 | 既有ShaderPair32 + 输入数/保留0 |
| Target | 16 | width/height/format/颜色域 |
| Pass | 32 | program/input0/input1/output + float4 |
| Description | 56 | size/version/尺寸/计数/output + 3个数组指针 |
| Stats | 72 | generation、bindings/targets/programs、resident/publications/draws/常量上传 |

最多8个程序、32个target、32个pass；每级Shader≤1MiB、整组复制字节≤8MiB。尺寸每轴1–4096，但还必须满足128MiB的所有已安装子图与候选合计预算，不能承诺任意4K Bloom。RGBA16F且scene-linear为首版唯一目标格式/颜色域；只有1或2个纹理输入、C16/float4参数、固定linear-clamp采样。无depth/velocity/UAV/任意blend/外部图像导入。

`ResidentBytes` 是目标宽×高×8及16B常量缓冲的逻辑payload，并非驱动实际VRAM，也不含复制Shader代码/元数据。程序/纹理/采样器由scene内核独占，退役/关闭释放；调用者数组和bytecode在调用中复制，不保留指针。统计计数为当前renderer生命周期，scalar发布也增加publications。

资源为单写者且不能读写同一资源；读取必须初始化且依赖无环。C#从最终输出追踪并拓扑排序，允许声明逆序，不允许多余pass/无writer目标；native再次验证有序依赖、参数有限值及绝对值≤65504、格式/尺寸/程序索引/arity，最后输出必须是全尺寸。所有复制/验证/初始化成功才发布。

## 程序与无源码包

新增 `ShaderProfile.HdrPostProcess=4` 仅供独立后处理；原NCS1不接受此profile。复用现有目录/编译/严格DXBC envelope/stage校验，新增 NPH1 v1 独立格式，非新NCS兼容分支。

NPH1头24B（magic/version/总长/count/compiler47/flags0x48800）；每项固定92B（UUID、arity、VS/PS长度、两个authorHash）再接VS/PS字节。1–8个唯一非空UUID、大小有界、严格stage/DXBC块/尾随拒绝；先复制输入再计算外部expected SHA，避免验证后换字节。元数据/Hash不是编译来源证明、签名或GPU权限。`GpuValidated=false`。

`PreparedPostProcessPrograms` 属于精确renderer，缓存拥有不可变包，调用前后重查审批。实际native完整反射对照独立绑定fixture：SV_VertexID顶点输入、fullscreen VS/PS接口、SV_TARGET0、Post/C16/Params最终偏移、Input0/可选Input1及LinearClamp；声明正确但实际字节错误同样拒绝。内部D3DCompiler产生的绑定fixture不是Bloom算法，也不是代码安全沙箱。官方与用户程序无特殊旁路；本片没有新增Agent任意编译/执行权限。

## 原子配置与失败

全量候选拥有完整独立程序/RT/常量/采样器；反射、创建、diagnostics、健康检查及有界GPU drain成功后以不抛异常交换替换。旧绑定计数/资源只在发布时退役。失败/预算/timeout释放候选并保留旧安装与托管Plan；真正device故障仍fail-stop，不虚构GPU回滚。

相同字节程序、目标布局和pass资源索引仅参数不同，可更新已复制参数，不重建纹理/程序且无GPU drain；仍执行审批/完整图验证/反射/健康检查，配置本身不是逐tick轻量API。Off用null绑定，经过相同owner/离帧/审批及drain后释放全部子图资源。callback期间拒绝修改/关闭同一Scene、嵌套绑定；跨线程在改变准备标志之前拒绝。

窗口resize后外部scene目标的尺寸仍由scene管理；改变scene尺寸须建立匹配的新scene/子图，不能拿旧尺寸plan绑定新scene。纯UI在程序GPU准入前拒绝，统计读取不初始化后处理。

## Bloom 数值定义

- levels1–6，threshold0–16，softKnee0–1，intensity0–4，scatter0–1，全部有限；Enabled=false或intensity0直接返回null，不需要shader包。
- 每级ceil半尺寸，最小1×1后停止；第一层4个bilinear box样本再亮度maxRGB软阈值，后续层4样本降采样。
- 每级水平/垂直5tap `[1,4,6,4,1]/16`，从小到大用scatter归一化lerp；不是无归一化叠加。常量场能量不随levels改变。
- 最终 `original + intensity × bloom`，非负并限制到65504，alpha1；每次中间RT实际量化binary16。最多24个目标/pass，最后仍由原Tone输出一次sRGB。
- 不包含自动曝光、HDR显示器输出、TAA/SSAO、HDR/EXR文件解码或正式素材设置UI。

## 证据和剩余门禁

独立CPU oracle从settings计算box/threshold/blur/upsample/combine，逐阶段Half量化后独立double显示编码，不读取生产图或Shader文本。实际亮斑/暗区/尺寸/参数/常量能量、无源码、用户程序与逆序依赖、UI、失败保留、静态费用等见交付记录。

Environment+shadow程序的新Bloom图像使用IBL Off；已有真实IBL/FBX联合测试保留，新Bloom与真实FBX/IBL的正式产品联合归D。C负责配置、资源闭包、共享宿主、Undo与AI有界提案，不能仅删除guard。纯UI独立产品仍18文件，无NPH部署/3D效果资源；没有新增Python gameplay/推理服务。人工/UI/DPI/第三方MCP/用户素材/目标机/自包含/完整性能/1h保留。
