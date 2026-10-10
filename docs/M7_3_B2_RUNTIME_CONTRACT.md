# M7.3-B2 环境 Shader 与场景绑定合同

本片实现独立、显式选择的 DX11 IBL Scene kernel。B3完整独立图像验收、C正式Editor/Player配置/文件闭包/可撤销命令仍未完成。正式宿主原ambient不是IBL；没有HDR/EXR解码器、AI推理服务或Agent GPU/编译权限。

## 冻结与独立合同

Renderer主ABI1.2、query1–14和SceneFrame240B/v4不变。新增query15/API1/56B/caps3：复制可信参考源、完整包实际反射准入、完整Scene组创建/替换、环境绑定。Scene组104B/version2，RuntimeShaders176B/version2/profile3；旧group/version1/profile1显式拒绝，不升级或降级。

ShaderProfile追加SceneEnvironment=3；NCS1仍v1，但明确新profile3及environment.geometry.vertex/pixel角色9/10闭包。原profile0/1格式、角色、绑定hash算法和现存数值布局保留。geometry C416包含旧400B所有13项以及offset400的float4 EnvironmentSettings；cube t7/t8、LUT t9、sampler s7。Tone/Shadow仍旧C400数值布局，profile3拥有独立binding hash；可选原SkinCompute/profile2。geometry默认/用户均经同一catalog、真实compiler、逐项声明和实际反射。包Preflight没有原生调用，只检查格式和外部expectedHash；GpuValidated始终false。

新包只能通过EnvironmentShaderPreparation进行实际整组几何、Tone、可选Shadow、可选Skin准入，然后产生source-free wrappers。native安装仍实际复验；不接受伪造binding hash替代真实RDEF/signature/resources/cbuffer递归字段检查。新环境参考反射cache最多4（两个shadow变体VS/PS），旧cache10不扩展。运行包不含source/path/native handle；内部参考反射仍使用D3DCompiler，不称compiler-free或代码沙箱。

## 采样与控制

使用线性物理irradiance E/pi与roughness GGX specular mip、NoV-x/roughness-y BRDF A/B LUT split-sum。LUT端点使用texel中心映射；specular mip=roughness*(levels-1)。环境AO只乘环境项；direct light、emissive及独立原ambient保留。strength0–16，rotation[-pi,pi]：lookup方向(x*cos-z*sin,y,x*sin+z*cos)。环境强度在线性HDR中应用、随后走既有Tone。

Off为精确空identity/strength/rotation配置，无环境GPU纹理创建/上传；复用已有场景sampler在s7，SRV为null，由明确0强度分支避开采样。RHI新sampler-only opt-in默认false，旧严格纹理/采样器配对检查不变；即便opt-in也不能有texture但无sampler。不掩盖DX11 debug layer警告。绑定enabled且strength0仍保留资源pin，像素等于Off。

## 所有权、原子性与寿命

C#持有immutable NCE1 UUID/generation/hash和配置；native只接收40B复制的renderer-scoped resource token、strength/rotation和reserved0，绝不序列化runtime handle。C#验证session/renderer、exact包identity、owner、显式可信off-simulation许可，在审批前后复验；共享非重入准备边界。没有World/revision/MCP权限或新调度器。用户回调不允许同步tick/inference/IPC等待。

native要求owner、healthy、off-frame、非重入、非pureUI，绑定前完整验证、诊断、有界2秒event drain、设备状态复验。全部可失败分配/回调/诊断先于无异常发布。绑定增加资源scenePins；相同资源重新绑定计数稳定，解绑/Scene成功关闭退pin；最多8 Scene。B1已绑定资源替换/释放返回Busy，失败不修改托管包、输出、计数或旧绑定。Scene关闭timeout保持pin可明确重试；设备移除时允许既有fail-stop清理，不宣称真实device-reset验收。

完整Shader候选RAII/实际反射/诊断/drain之后才交换全部programs，保持mesh/material/HDR/depth/shadow/environment和pins。旧Tone-only/query9或旧Scene/query10不能降级环境Scene；环境组也不能隐式升级旧Scene。constant upload和resident统计按416B真实值，旧Scene仍400B。off-frame诊断readback继续B1合同，不在渲染tick内使用。

## 边界

B2定向像素仅证明绑定、控制、default/user/source-free和skin/shadow路径实际工作；不替代B3独立IBL积分/材质网格/实际FBX-NCA联合图像，亦不关闭C配置或任何人工/目标环境/自包含/性能/1h门禁。纯2D不创建环境资源。DX11only，Vulkan/OpenGL下一版本。没有Python gameplay、C++ World、兼容旧格式或新Agent执行权限。
