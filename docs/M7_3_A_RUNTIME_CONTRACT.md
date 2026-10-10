# M7.3-A 环境资产与离线 IBL 数值合同

范围：线性环境源、离线 CPU Cook、不可变 source-free NCE1 包及纯托管预检。不是 GPU IBL、HDR/EXR 文件导入、正式场景配置或完整 M7.3 验收；ambient 仍不是 IBL。

## 所有权与边界

C# 的 `HdrEnvironmentSource` 复制可信解码后的 RGBA32F 等距圆柱值，拥有持久 UUID 和源内容 hash。宽为高的两倍，高为 2–256 的二次幂，RGB 有限且在 0–65504，alpha 必须为1；作者负零规范化为正零。无文件解码、路径或 gamma 猜测。`Neutral` 是明确的合成 .18 线性环境，不是拍摄 HDR 资产。

`EnvironmentCookService` 持有 Renderer module lease，不能卸载仍使用的函数表。创建/使用/释放要求 owner-thread，禁止重入；host 必须提供显式可信 off-simulation 审批回调。native 在回调后和发布前再次检查模块、非重入、healthy、off-frame；实际 pureUI renderer 拒绝 Cook。无 renderer/window/device 也可计算。审批是 host 边界，不是 World revision/MCP 权限校验或 Agent 自审批授权。

取消在 native 调用前、调用后和 CPU 缓存发布前检查，不在调用中抢占。工作量有界仍可能耗时，只能用于显式离线准备，不能从 simulation/render tick 同步调用。未增加异步调度器、推理服务、Python 游戏脚本或 Agent 编译/执行端点。

单条缓存 key 为输出 asset UUID、generation、含源 UUID 的源 hash、完整 Cook 参数。命中也复验 owner/lifetime/审批/native boundary。失败、撤权或取消不替换此前 immutable 缓存；没有持久失败 key 或无限缓存。包字节与解码数值各最多4MiB，单缓存约8MiB以内（不计对象头/临时输入与计算缓冲）。

## 原生数值 ABI

Renderer ABI 1.2 主表和 query1–12 均冻结；独立 query13/API1 表32B，capabilities=1，Cook/Validate 两函数。输入48B POD、输出metadata32B POD、caller-owned float 缓冲；x64 cdecl。复制输入，所有值验证后局部计算；最终 memcpy 与metadata赋值之后没有可失败步骤。任何错误，包括短输出和注入的计算后异常，metadata/values均不改变。调用中不保留输入/输出指针，无原生环境对象、GPU资源或释放API。

参数：cube2–64、irradiance2–16、LUT2–64、samples64–2048，均二次幂；保守 `(diffuse pixels + all spec mip pixels + LUT pixels) * samples <= 64*1024*1024`。原生表中浮点容量是元素数，short error.required_bytes 是字节数。输入最多2MiB；输出最多4MiB。描述、上下文、线程、活动帧、fail-stop或PureUI不符均拒绝。

## Algorithm1 数学与布局

face order `+X,-X,+Y,-Y,+Z,-Z`；六面 irradiance RGBA，然后 specular mip-major 六面 RGBA，最后 roughness-y / NoV-x 的 RG LUT。RGBA alpha1。方向采用 D3D cube 约定，等距圆柱 `u=atan2(z,x)/(2pi)+.5, v=acos(y)/pi`，像素中心、x接缝 wrap、y极点 clamp、双线性线性颜色采样。

- diffuse：cosine hemisphere Hammersley 积分，存物理辐照度 E，Lambert 消费时除pi。数值上限65504*pi，不按half-float辐亮度上限截断。
- specular：mip0直接源采样；其余 `roughness=level/(levels-1)`，GGX alpha=roughness²，N=V=R，NoL加权归一化预滤波。
- LUT：NoV网格端点最小.0001，roughness端点最小.045；IBL Schlick k=roughness²/2、Schlick Fresnel拆分A/B。数值各0–2范围校验是格式预算，不是任意完整网格的严格能量守恒证明。

依据 [Karis 2013 原始论文](https://cdn2.unrealengine.com/Resources/files/2013SiggraphPresentationsNotes-26915738.pdf)。split-sum是各向同性近似，不是任意高频环境精确积分、多次散射补偿或直接光hotness remap。方向卷积和BRDF另用独立解析/均匀半球积分检查；误差与实际范围见交付记录。

## NCE1 v1

header160 / total<=4MiB，小端；0–44为magic NCE1、version1、algorithm1、header160、源尺寸、四Cook参数、levels、floatCount；48/64为不同非空asset/source UUID；80为非零uint64 generation；88为源SHA256；120为payloadSHA256；152–159必须零。payload为上面的canonical float32顺序，无源像素、名称、路径、native handle或脚本。

Decode接收独立可信大写64位 expectedHash，复制输入后检查whole/payload hash、精确长度、版本/保留字段/预算/UUID/generation、闭合布局与每个有限非负数值，拒绝负零、非法alpha、辐亮度/LUT越界。返回副本访问器；包可在插件关闭后和多个只读线程使用。

Hash只检查完整性和host选择的确切内容，不是签名、Cook来源、物理正确性或安全沙箱。调用方若更改数据且同时批准新expectedHash，格式合法包仍可被读取；`GpuValidated`永远false。B必须另做真实GPU资源/绑定准入。`EnvironmentLightingConfiguration`当前仅独立闭合值（strength0–16/rotation[-pi,pi]/规范Off），未注册scene组件、项目字段或命令。

## 剩余门禁

M7.3-B真实DX11纹理/IBL绑定、官方/user shader反射和像素；C正式宿主/资产闭包/命令/部署；HDR文件解码和用户素材尚待实现/验收。完整自动回归不关闭人工UI/DPI/MCP、真实用户FBX/HDR、目标机、自包含、完整性能、1h长跑。DX11 only，Vulkan/OpenGL下一版本。
