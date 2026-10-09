# M7.1-C 注册着色器接入执行计划

前置：B `6e6887c266ae25ecc7228de0cdd937172098cffd` 已推送，开始C前远端main一致。
C不是单个枚举／仅加载bytecode：按以下可测试切片推进，每片通过完整顺序无Skip Debug/Release、checked部署/hash/journal及提交推送后再下一片。

## C1：真实 Scene Tone 替换（自动候选完成）

24项真实定向及修正后完整顺序无Skip Debug/Release、checked deployment/hash/journal通过；见[契约](M7_1_C1_RUNTIME_CONTRACT.md)和[交付](M7_1_C1_DELIVERY_REPORT.md)。C1交付不包含C2–C4；C2最新状态见下节。

默认Tone使用现有实际HLSL，供可信准备代码取得复制；C#建立同一个ShaderCatalog并编译默认和用户定义。
Scene Tone固定HDR纹理、sampler、400字节场景常量布局和SV_VertexID全屏三角形；闭合反射与实际跨阶段签名、输出检查后进入GPU。
新增Renderer query9独立API1，query1–8不变；caller-owned有界VS/PS bytecode，不跨ABI传源文件／COM。
预编译注册Tone可用于新ScenePipelineSession和可信停用模拟时的原子替换。候选先创建，显式GPU drain成功后更换，失败保持原stage及资源所有权。
旧query4仍是当前独立native契约及回归消费，不视为废弃入口；C1不暗中切换应用lazy创建，在render tick中编译。
实际绘制默认和用户Tone，与独立解析像素oracle匹配；错stage／资源／cbuffer／签名／输出／末行／容量／owner／idle／wait故障拒绝。
纯2D未使用Scene Tone时无场景资源；现有M6静态/skin-shadow/Player/MCP原测试全部保留。

## C2：Geometry／Shadow和Skin（自动候选完成）

执行／契约见[C2方案](M7_1_C2_IMPLEMENTATION_PLAN.md)、[契约](M7_1_C2_RUNTIME_CONTRACT.md)、[交付](M7_1_C2_DELIVERY_REPORT.md)。真实定向40项/native故障/完整渲染联合和最终顺序无Skip Debug/Release/checked deployment/hash/journal通过；后续C3状态见下节，C4仍未实现。

注册同源官方Scene geometry/shadow及compute skin描述与闭包；核对实际mesh layout、材质角色、skin stride/dispatch布局，
同一目录和validated模块供trusted用户替换；实际静态/动画/阴影reference与独立oracle及API0/0。
不能把C1的Tone成功称为上述阶段已支持。

## C3：独立2D（自动候选完成）

独立query11/API1、同源Flat2D默认/用户VS/PS、32项真实像素/原子/cache测试、native故障和16文件独立UI apphost通过；最终顺序无Skip Debug/Release/checked部署均通过。见[方案](M7_1_C3_IMPLEMENTATION_PLAN.md)、[契约](M7_1_C3_RUNTIME_CONTRACT.md)、[交付](M7_1_C3_DELIVERY_REPORT.md)。C4仍未实现。

同源2D VS/PS描述和严格vertex/display-list/texture/clip/alpha约定；真实纯平面应用、顺序正确批次、无3D初始化／资源／部署闭包。
明确参数／Feature／替换方式，不能让2D依赖强制Scene/skin。

## C4：正式宿主与运行包／最终联合门禁

在宿主明确off-simulation/off-render准备边界接入官方默认与trusted用户配置；版本化精确descriptor/bytecode/依赖闭包，
运行包不加载Editor／source authoring／native handle，损坏／错版本／不匹配初始化前拒绝。
同源复制有界metadata/诊断，无实现的只读Agent检查保持unsupported；不授予编译／源修改／DLL加载／推理或live World/GPU权限。
完整静态／动画／纯2D／Player/包拒绝回归通过后才关闭C自动候选；人工/用户FBX/目标性能/自包含/1h门禁仍独立。
