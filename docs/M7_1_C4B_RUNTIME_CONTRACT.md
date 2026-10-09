# M7.1-C4-B 运行包准入与正式宿主契约

日期：2026-10-10。前置C4-A main a7f377794126aaa11905f875c3e947faa0a5e8bb 已独立核对远端。仅DX11；C4-C及人工/用户FBX/目标/自包含/完整性能/1h门禁保持开放。

## 无源码准入

- NCS1 v1和A纯托管预检不变，Package.GpuValidated始终false。RuntimeShaderPreparation是另一种实例绑定准备结果，BindingsValidated只表示真实原生反射通过，不表示指令安全或GPU资源已经创建。
- 独立query12/API1：24字节表、176字节调用者拥有的POD。profile仅UI/Scene，flags仅shadow/skin，未用组必须全零；每stage最多1MiB。函数同步检查完整组，不保留调用者指针、不产生可序列化GPU句柄、不移交分配。
- UI真实signature/Frame16/vertex52/纹理采样；Scene完整Geometry/Shadow/Tone闭合signature、C400及所有字段/slot；Skin完整结构字段/80和128 stride/64x1x1线程组。复用C1–C3原生校验，不能用包中自报hash替代DXBC反射。
- 全组成功后才返回准备对象。对象含不可变source-free包，不反向构造ShaderDefinition/作者source。现有RegisteredUi/Scene/Skin增加内部包构造路径，只有成功准入后可从公开接口取得。安装仍走原来的实际字节验证、RAII候选、drain和原子交换。
- exact RendererSession/owner thread/healthy/idle、非重入及受信回调在准备前后/安装前检查；外来、释放、撤权、活动帧拒绝。不增Agent编译、GPU安装、Python或任意代码执行权限。
- native最多10个内置参考反射缓存，随renderer释放，仅缓存确定的内部source/entry/profile组合；不缓存用户bytecode，不替代每次实际反射。准备阶段预热；同组创建、resize和replace不重复编译参考HLSL。不是性能验收。

## 正式共享宿主

- DefaultRuntimeShaderService缓存5种以内官方包（UI1，Scene shadow×skin4），每次返回准备仍重验owner/授权和实际反射。首次官方Cook在显式off-frame准备中完成；C4-C前不声称正式Player无编译器或已从磁盘包启动。
- SceneRuntimeShaders持有shadow off/on完整组，skin存在性一致且计算bytecode必须完全相同。SceneRenderSession构造阶段准备，SceneGpuResources用已准入Skin创建；Submit仅选择已准备组，尺寸改变也不重新编译。正式Editor/Player/独立动画预览共用这条服务。
- 空场景没有SceneRuntimeShaders、没有3D GPU初始化。Headless不走这些服务。SceneRenderSession的可选可信输入支持已加载用户包；项目配置、文件pin与发布仍归C4-C。
- 共享UiCanvas在首个image/cache之前显式准备默认UI；已有明确用户程序不被覆盖。已存在原始UI kernel时走原子replace，已有presentation lease仍拒绝。独立UI sample同样走NCS1准入；只有Flat2D契约，没有强制3D程序/资源。
- 此处是正式宿主默认程序接入，不删除保留的原生参考测试入口；不是任意render pass/shader graph或热加载项目协议。

## 验证要求

实际加载包对比默认和用户UI像素、四种Scene闭包像素与identity Skin数值，CPU可读但错误SV_TARGET1/末尾compute线程组在原生门禁拒绝；原程序/metadata/像素不变。原生query12短表/旧设备/active/闭包/最后stage、原C1–C3超时及诊断异常原子测试保留。原全部静态/动画/FBX/NCA/Jolt/Player/UI/MCP及顺序无Skip Debug/Release为提交前必要门禁，结果见交付记录。
