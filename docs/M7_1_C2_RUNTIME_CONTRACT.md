# M7.1-C2 注册 Geometry／Shadow／Skin 运行契约

C1远端4f853a1核验后执行。仅DX11，同源官方和trusted用户使用相同C#目录、B strict/werror/O3编译／真实反射和绑定验证，再进入实际GPU。不是任意shader/pass、World服务或Agent执行授权；C3纯2D注册、C4正式宿主准备边界／运行包仍未实现。

## 同源准备

DefaultSceneShaders复制实际ScenePipelineKernel HLSL；Scene3D目录包含Geometry VS/PS、Tone VS/PS及可选Shadow VS/PS。无阴影分支使用明确NO_SCENE_SHADOW源码变体，完整组只有4份，不能含Shadow pair或资源。所有Tone pass共用组内Tone。用户可用精确UUID/hash选择注册模块，但不能以不存在的stage枚举假冒支持。

DefaultSkinShader复制实际SkinKernel HLSL，独立Skinning目录只有选择的compute stage，不强制2D初始化Scene/Skin。准备只持有不可变复制bytecode/反射与准确RendererSession身份；CopyMetadata返回role/catalog/descriptor/bytecode hash和Compiled=true，不包含源码、指针或GPU资源，也不等于已安装。

C#先核对选中profile/stage/完整声明/mesh byte offsets，再通过相同B服务逐stage真实反射；非空selected dependencies拒绝（没有可执行的模块依赖闭包）。Scene完整候选最多6份；当前query8预算仍256KiB source/1MiB code/256rows。Skin共享资源语义Source/t0/80byte、Bones/t1/128byte、Output/u0/raw、Settings/b0/16byte四uint。Scene C/b0为400byte/13成员；geometry POSITION/NORMAL/TEXCOORD/TANGENT固定48byte输出顶点布局。

## 新ABI与native复核

现有Renderer1.2/152byte及query1–9全部保持。独立query10/API1/56byte/caps3：

- copy_source(kind0 scene/kind1 skin)：有界caller-owned复制、最多256KiB，无文件／网络。short失败不动output/required，Error.required_bytes报告实际所需。
- create_scene/replace_scene：104byte SceneShaders，size/version + 三份32byte VS/PS pair；每stage完整bytecode4byte–1MiB，absent Shadow整个pair必须零。
- create_skin/replace_skin：24byte ComputeShader，size/version/length/reserved + caller-owned pointer，无独立shader句柄、持久地址或序列化native对象。

native从bytecode D3DReflect，与实际内部同源HLSL在B相同flags下编译的闭合reference契约核对；只在可信off-frame准备时做，不能声称有跨设备异步缓存或零准备成本。检查真实版本、全部输入输出signature/system value/index/type/mask/stream/minPrecision、资源kind/slot/count/dimension/float/comparison属性、全部constant buffer/member layout。

StructuredBuffer的D3D_CT_RESOURCE_BIND_INFO也完整核对：递归深度最多8、每变量最多64type节点，字段名/type/class/rows/columns/elements/offset逐项匹配，包括Input p/n/uv/t/joints/weights、Palette model/normal；额外／末成员拒绝。Skin另从实际disassembly读出Source80/Bones128 stride（不是NumSamples），实际GetThreadGroupSize必须64x1x1，匹配原ceil(vertexCount/64) dispatch。

这只是可信模块的闭合资源布局约定，不是任意HLSL安全沙箱；反射不能证明代码必有边界检查或任意GPU写入安全。没有Agent源码修改、编译、模块加载或直接GPU写权限。

## 原子安装／共享内核／生命周期

Scene所有候选program先创建，RAII覆盖部分成功／异常；全组诊断成功、现有有界GPU drain成功且device健康后ClearState，一次noexcept交换，释放旧组。发布后不做可抛异常分配。create只在全部资源成功后发布key/counters；失败不发布，换组不重建mesh/texture/HDR/depth/shadow。Scene.RegisteredShaders仅native成功后更新；Tone-only替换清除整组metadata，防止旧metadata假称当前完整组。

Skin第一次注册create创建原共享SkinKernel和三palette槽，原mesh/computeoutput/提交／计数ABI不变。已存在kernel时create只能同一完整bytecode；不同模块必须显式ReplaceSkinShader，不隐式切换或按对象单独shader。替换影响该renderer ALL skin meshes的后续dispatch，保留mesh/output/palette槽及frame identity。候选COM shader/复制code先准备，诊断/drain后noexcept交换；超时释放候选，旧shader保持。最后一个Skin释放后全部kernel资源和code关闭。原query5保留独立数值入口；正式宿主默认选择未在此片暗中改变。

所有准备／安装重查owner/exact renderer lifetime/healthy/idle及trusted host preparationAllowed，使用与C1同一个renderer级非重入保护。此callback是可信宿主的off-simulation/off-render约定，不是完整World/tick authority：C4尚未接正式调度。既有三槽UpdateSkins依然非阻塞，simulation/render ticks没有编译/替换/同步drain/IPC或推理。drain失败保留所有权；设备故障既有fail-stop，不声称GPU回滚／自动恢复。

## 验证与剩余边界

独立PBR scalar oracle、用户Geometry BGR整幅图、用户Shadow alpha discard与未遮挡图、用户compute平移的CPU position/normal/tangent/UV/sign，容限沿用既有图像1／PBR2byte、compute1e-6；不放宽原FBX position/normal或animated-shadow容限。原ASCIIBinary FBX四时刻数值验证改由注册Skin执行，原NCA inclusive start/middle/end CPU/GPU geometry/shared-shadow图像改由完整注册组和Skin执行，保留所有原断言及M6联合路径。

诊断异常、GPU drain超时、末组stage、末cbuffer成员、同stride不同结构offset、线程组、stage/link/target/resource/vertex offset/owner/revoke/active/nonreentry/生命周期拒绝均不发布失败候选。详见交付记录。C2自动测试不是人工／用户FBX材质／目标环境／自包含／性能／1h或整个C验收；不实现inference、Python gameplay、UI/MCP编译工具、shader运行包或Vulkan/OpenGL实际后端。
