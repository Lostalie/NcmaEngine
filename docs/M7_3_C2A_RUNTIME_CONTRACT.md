# M7.3-C2-A 环境 Shader 文件与准备合同

本片为 C2 的前置合同，不是正式 Editor/Player IBL 开关。C1 的正式宿主 pending guards 保持；C2-B/C 和 C3 仍待实现，见[C2详细方案](M7_3_C2_IMPLEMENTATION_PLAN.md)。

## 显式选择与来源边界

ShaderPackageSelection JSON 保留闭合字段、required/重复字段/路径/副本检查，最多16KiB。v1仍只接受1–5个Flat2D/Scene3D包；v2显式接受1–9个Flat2D/Scene3D/SceneEnvironment包。Flat2D不带shadow/skin；两个场景profile均要求每个skin状态的shadow/off成对。最多9个不同(profile,shadow,skin)与不重复的规范ncshader路径。不存在未知profile→Scene3D转换，不存在环境缺包回退。它不是旧格式迁移或任意shader图。

RuntimeShaderFileSet精确核对package实际profile/flags/externalSHA，两个shadow变体按同一个profile配对，并要求所有选中的SkinCompute字节相同：同一个Renderer内的Scene3D/SceneEnvironment、Edit/Play共用一个计算核。单独自定义环境skin pair可有效，但混合不同计算核的两个profile在文件准备时就拒绝，不能延后到创建第二组网格时才失败。复用原RuntimeReadPin所有路径/父目录/链接/硬链接/文件大小/owner/shutdown规则；默认部署index仍核对独立manifest记录。没有修改NCS1、native query12/15或现有五包默认部署的字节。

## 准入与默认服务

InstallSelection完整遍历并分别调用原query12或独立环境query15，全部实际reflection成功后才UseFiles发布引用。只读preflight成功或文件声明的hash不能绕过实际反射；最后一组失败也没有半组选项发布，旧GPU资源不被修改，允许新正确selection重试。

DefaultRuntimeShaderService.Prepare只接原Flat2D/Scene3D；独立PrepareEnvironment返回EnvironmentShaderPreparation。文件模式永不Cook缺失variant。无文件的源布局工具按原明确startup/off-frame规则Compile+Cook；最多9个不可变缓存项，命中也重新校验owner/healthy/off-frame/非重入与可信off-simulation审批并实际准入，不产生安装资格跨renderer。CPU package.GpuValidated始终false；内部参考编译器仍存在，不是compiler-free宿主或代码安全沙箱。

SceneEnvironmentRuntimeShaders持有同一renderer实际准入的unshadowed/shadowed pair。构造拒绝反转shadow、不同skin/不同实际skin字节与foreign owner；VerifyFor再检查两个wrapper的权限/owner/寿命/非frame边界。它不持有GPU环境或pipeline，不读取World，不授权Agent，不提供推理/文件写/编译capability。

## 产品与后续接入

旧正式Editor/Player默认5包+index完全不变；环境资产与正式宿主开关不是本片内容。纯UI selection仅加载UI文件，不创建scene/skin/environment资源。C2-B须真正组合profile3场景、确切NCE资源与原可撤销配置；C2-C再换正式guards并验证pre-gameplay/动态变动。C3负责合成默认预设、产品部署和属性/检查入口。仅DX11；Vulkan/OpenGL下一版本，所有人工/用户素材/目标/自包含/完整性能/1h门禁保持开放。
