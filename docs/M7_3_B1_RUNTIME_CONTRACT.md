# M7.3-B1 环境 GPU 资源合同

B1只建立真实 DX11 数值资源与生命周期，不是场景 IBL、Shader 采样、环境旋转/强度应用、HDR/EXR 文件导入或正式宿主配置。B2/B3/C仍待实施，持久 `EnvironmentPackage.GpuValidated` 保持false。

## 所有权

C# `RendererSession.CreateEnvironment` / `GpuEnvironmentResource` 持有 immutable NCE1 的 UUID/generation/hash 与 renderer-scoped token；包 `CopyValues` 返回有界副本。替换成功才交换托管包引用，发布后没有分配/回调。模块被 renderer lease 保留，环境不释放则 renderer/module 不能卸载；最后释放后才允许关闭。运行句柄不得序列化。

准备与回读要求显式可信 off-simulation 许可，native owner/healthy/off-frame/nonreentry/pureUI门禁在许可前后复验。共享既有 shader preparation 非重入边界，回调不能递归上传/替换/回读、释放环境或卸载 renderer。许可不是 World/revision/MCP 权限，也不是代码或进程沙箱。没有新增 Agent endpoint、live World/Python/inference 调用。

## 独立原生合同

Renderer主ABI与query1–13、旧Scene/Shader绑定/NCS1包保持不变。query14/API1表56B、capabilities3，Publish/Destroy/Capture/Stats/Validate五函数；输入40B、输出token16B、Stats64B。所有调用x64 cdecl、POD、opaque handle、调用期间借用数组，无 retained pointer。

输入布局与NCE1相同：六面irradiance RGBA物理E、specular按mip-major六面RGBA、roughness-y/NoV-x的BRDF RG。面序+X,-X,+Y,-Y,+Z,-Z。cube2–64/irradiance2–16/LUT2–64二次幂，spec完整mips；全数值有限/非负/无负零，alpha1，E≤65504π、spec≤65504、LUT≤2。原生独立复验描述与所有数值，复制后才能创建纹理；hash完整性不等于Cook来源证明。

真实GPU存储为 immutable RGBA32F cube(E)、RGBA32F cube(完整spec mips)、RG32F 2D LUT以及linear/clamp sampler。RHI新格式追加枚举，旧值不变；cube初始数据为face-major/mip次序，插件重排NCE1的mip-major来源。旧纹理/数组/深度合同保持，禁止不完整初始数据、错误rowPitch/bytes和非square cube。

最多8环境，32MiB纹理payload预算含替换候选。最大单环境581600 bytes（145400 floats），sampler/驱动对象头与staging临时空间不计入payload计数；这不是GPU总内存测量。Stats反映Live/ResidentBytes/Publications/UploadedBytes/Captures/Retirements/BudgetBytes，owner-thread只读，不触发上传或readback。

## 原子发布与释放

Publish的old{0,0}创建；其他token必须完整匹配renderer generation和现存环境。资源数/预算与所有数据校验在GPU分配前。候选三纹理/sampler使用RAII；实际API诊断在发布前收集，替换还须有界2秒event drain与诊断复验。交换只用noexcept unique_ptr swap，旧纹理随后释放；新建map登记的可失败分配也在输出/计数发布前。失败不改变旧token/资源/内容/统计或caller output。

Destroy要求owner/正确token/off-frame，支持fail-stop清理；先诊断/drain再清除引用、释放所有资源、更新计数。timeout保留资源和计数供明确重试，不能强删。设备已移除时不等待已失效GPU，仍需正常资源销毁。没有模拟真实设备reset的验收。

## 诊断回读

Capture为离线测试/诊断，不是渲染必需步骤，不允许tick/frame内调用。容量元素数必须严格等于float_count，short error.required_bytes是字节数。三个真实GPU资源复制到临时staging，bounded event drain之后DO_NOT_WAIT Map全部subresources；按标准NCE1顺序复制到局部数组，完整成功/诊断后才一次写caller output。未缓存上传的CPU值，也不回放包；异常/short/timeout不写任何caller输出或capture计数。

Off/新renderer/纯2D不创建环境资源；B1没有默认初始化或部署环境包。既有正式宿主的ambient仍不是IBL。resize保留环境资源，不重新上传。下一片B2新增显式环境Shader binding和绘制，再由B3提供独立IBL像素/积分验收，C接资产闭包/命令/正式宿主。人工UI/DPI/MCP、用户FBX/HDR/材质、目标/自包含/性能/1h门禁仍开放。
