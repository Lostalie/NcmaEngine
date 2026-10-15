# M7.4-D1 C# 渲染定制示例

示例源码：`managed/Ncma.Rendering.Scene/Examples/SceneCustomization.cs`。属于可复制的可信启动代码，不自动启用、不从场景/MCP加载代码。默认渲染策略及Off不变。只使用公共托管合同，无平台API；无需修改C++。

## 三种接入深度

1. Feature：`ExampleHdrGain.Registration()` 注册有界gain 0–3、默认.25、顺序50。它用已受检的NPH Combine程序把线性HDR乘以1+gain，在官方Bloom（顺序100）之前运行；gain0不建立临时图。它不是Tone，也不是给display输出再次曝光。`SceneCustomizationExamples.Modules()` 展示官方Bloom与用户Feature共用注册表/元数据/参数校验/图组合。
2. Stage：`SceneCustomizationExamples.ReplaceGeometryStage` 只替换几何阶段的高层策略（示例将ambient减半）；Scene3DPipeline继续组织其余阶段。Shader替换仍是独立的闭合、受检程序对，不通过修改stage参数偷偷替换Shader。
3. Full pipeline：`SceneCustomizationExamples.ReplacePipeline` 自行创建shadow/HDR/depth/output资源及依赖，使用512阴影目标、用户几何stage和单次最终Tone。没有转发默认pipeline.Build。完整替换仍受当前已实现scene操作及HDR子图合同约束；不意味任意GPU算法都已实现。无shadow时不分配shadow资源。

示例方法组可在可信启动时传入已有共享入口：

```csharp
using Ncma.Rendering.Scene;
using Ncma.Rendering.Scene.Examples;

using var post = new PostProcessRuntime(renderer, shaderFiles, postLibrary,
    SceneCustomizationExamples.Modules(), preparationAllowed);
using var scene = new SceneRenderSession(renderer, cache, world);
scene.Prepare(assets, startup,
    pipelineFactory: SceneCustomizationExamples.ReplacePipeline,
    environmentPreparationAllowed: preparationAllowed,
    postProcessing: post);

// C# host-owned, healthy/off-frame/non-simulation approval; not an Agent-supplied flag.
scene.PrepareEnvironmentView(width, height, camera, exposure: 1, ambient: .2f);
// In the rendering phase, submit only ready resources.
scene.Submit(frame, width, height, camera, exposure: 1, ambient: .2f, target: target);
```

M7.6起持有者必须先保存再Prepare，旧可失败构造入口已删除。上例是健康离帧路径；长期宿主将scene保存在字段中，Prepare失败或Dispose遇到Busy时不得清空/覆盖该字段，必须保留依赖，待安全边界显式关闭成功后再释放。不要将 `new(...).Prepare(...)` 包装成可丢失临时持有者的工厂。

保留文件租约/renderer/cache/world/assets的拥有者，先结束scene，再结束post，再释放文件/GPU服务。带骨骼的场景还必须传现有pose kernel/animation绑定，不允许从示例跳过。编辑器原草稿/事务/唯一Undo仍是高层参数修改入口；模块目录只读审批不授予执行权。CPU构图通过和GpuValidated=false不是GPU准入，也不是可信代码安全沙箱。

## Player 宿主持有者（M7.6）

`PlayerRunner` 现在是由调用方持有的实例，旧静态 `Run` 已移除，无兼容转发。长期宿主先将实例保存到自己的字段，再调用 `Run(options, pipelineFactory: ...)`。正常结束会按依赖顺序关闭资源并进入 `Idle`；关闭失败会返回原失败报告，同时进入 `Retained`，不得覆盖该字段或重新 Run。

通过 `Status` 读取复制的阶段、generation、关闭尝试次数、失败服务/原生结果和资源持有标记。可信宿主在相同线程显式调用 `RetryShutdown()`；它只重试关闭，不推进游戏、不 Present、不改写 `LastReport` 或原报告文件。成功排空后才能开始新的 World/session。`Dispose()` 遇到释放失败仍会抛出并保留资源；不能用丢失局部变量的异常路径代替长期持有。

若关闭被一个未完成的图形帧阻塞，可显式调用 `AbandonFrameAndRetryShutdown()`。可选 renderer query17/v1 按精确 renderer/frame 身份放弃当前帧，该 renderer 随即只能关闭，不能继续提交/Present；仍遵守原资源租约和有界GPU退休等待，绝不强制卸载。旧模块没有 query17 时明确拒绝，不隐式回退。正常路径不查询该可选接口，Null 不引入渲染模块。此操作不是设备丢失恢复或游戏回滚，也不是新的 Agent 能力。

独立 CLI 在自身进程字段中保留关闭失败的运行器直到退出，保持原失败退出码；不自动重试、不强制释放。进程退出后的操作系统回收不称为引擎故障恢复。

## 显式程序库闭包

`ExampleHdrGain.ModuleId` 必须在项目的v1 post library索引拥有自己的NPH文件路径和SHA。示例重用现有Combine数值程序，因此可以复制已受检Bloom NPH字节到独立路径（如`assets/postprocess/example-gain.nph`），添加精确模块UUID/路径/hash记录，再保存索引并把新索引SHA写入项目`PostProcessLibrary`。不能给两个模块使用同一路径，也不能覆盖已pin文件或修改部署默认索引。

Feature插入由原PostProcessData有界stack表达；Tone UUID/版本和程序库双shadow/skin/profile闭包保持精确。完整库需经过原CPU文件pin、外部SHA、真实完整反射、同一shared skin及准入审批。缺程序/错SHA/撤销/未知模块均拒绝，不回退到默认模块。`CustomizationExampleTests`中的自有临时项目演示完整索引发布；它不修改用户项目或生产部署。

## 验证范围

独立CLI `--customization-example-tests <plugins> <output>`，完整Build.bat通过独立120秒CTest调用。测试CPU-only/有界坏输入、真实linear gain→Bloom→Tone整图与独立double/Half标量、共享静态场景+IBL、替换拓扑/实际阴影、坏图候选、World写入/关闭/重入/帧内/owner/撤销拒绝、奇数resize与关闭、暖64 Submit分配/构图/上传。

这不是D2的真实FBX/skin/PBR/IBL/shadow/Bloom/UI独立联合图，也不是D3的搬移Editor/Player产品验收；二者仍需完成。GPU逐帧origin-frame时间、人工UI/DPI/第三方MCP、用户素材/HDR解码、目标机/自包含、完整性能/1h仍开放。98项检查与完整双配置结果见[D1交付记录](M7_4_D1_DELIVERY_REPORT.md)。
