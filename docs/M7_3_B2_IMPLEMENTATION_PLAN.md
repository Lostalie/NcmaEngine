# M7.3-B2 环境 Shader 与场景绑定实施方案

2026-10-10：B1本地/远端`0d91eda6bb9672c0bcda17a1a4fa172dfd203da4`已核对。

- 独立Renderer query15/API1：可信参考源复制、完整新Shader组实际反射准入、创建/替换环境Scene、显式环境绑定。原query1–14/Renderer1.2/Scene frame v4冻结，旧Scene3D 400B绑定与NCS1 profile0/1字节/hash不变。
- 追加SceneEnvironment profile3和独立environment.geometry角色9/10；geometry使用C416B（原13项+EnvironmentSettings）、t7/t8 TextureCube、t9 Texture2D BRDF、s7 sampler。Tone/Shadow维持C400B数值布局；新profile内闭合布局独立hash，不用旧合同冒充环境组。可含同一个SkinCompute。完整真实反射在任何GPU创建之前，包预检永远GpuValidated=false。
- default/user统一catalog/compiler/declaration/reflection/包/注册路径；完整候选RAII/诊断/有界drain先于noexcept组发布。单独有界环境参考反射cache4，旧cache10不扩展。旧Tone/Scene替换不能降级新环境Scene。
- C#拥有配置UUID/generation/hash/strength/rotation/Off与准确资源寿命；native仅保存复制的GPU绑定和参数。绑定增加B1资源pin，Pinned资源替换/释放拒绝，解绑/Scene关闭退pin；失败保留旧配置/组/pins/输出。纯UI/Off无环境纹理创建，未接正式项目/宿主/配置命令（C范围）。
- HLSL线性split-sum：diffuse物理E/pi、roughness GGX mip和IBL A/B LUT；AO仅作用环境项，strength缩放、明确Y旋转；保留独立ambient作为原参数，不称其IBL，测试ambient0。off分支无同步cook/inference/IPC/tick等待。
- B2定向：真实default/user与source-free一致、Off/strength/rotation、old-contract rejection、实际错误资源/最后成员/组/skin反射、pin/替换/解绑/关闭/失败原子性、阴影/compute skin联合fixture、API0/0。B3再做完整常量/方向/HDR/材质网格、独立积分/图像和FBX/NCA联合验收；不能用B2关闭B3或C。
- 定向后冻结实现/测试，顺序完整无Skip Debug/Release Build.bat、原全部M6/M7/格式/MCP/Python/smokes/3profiles/audits/recovery/checkeddeploy，核验manifest/hash/journal/备份后提交推送及远端SHA。人工/用户HDR/目标/自包含/全性能/1h门禁仍开放；DX11only，Vulkan/OpenGL下一版本。
