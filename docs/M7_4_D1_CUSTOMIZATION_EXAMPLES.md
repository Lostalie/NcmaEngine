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
using var scene = new SceneRenderSession(renderer, cache, world, assets, startup,
    pipelineFactory: SceneCustomizationExamples.ReplacePipeline,
    environmentPreparationAllowed: preparationAllowed,
    postProcessing: post);

// C# host-owned, healthy/off-frame/non-simulation approval; not an Agent-supplied flag.
scene.PrepareEnvironmentView(width, height, camera, exposure: 1, ambient: .2f);
// In the rendering phase, submit only ready resources.
scene.Submit(frame, width, height, camera, exposure: 1, ambient: .2f, target: target);
```

保留文件租约/renderer/cache/world/assets的拥有者，先结束scene，再结束post，再释放文件/GPU服务。带骨骼的场景还必须传现有pose kernel/animation绑定，不允许从示例跳过。编辑器原草稿/事务/唯一Undo仍是高层参数修改入口；模块目录只读审批不授予执行权。CPU构图通过和GpuValidated=false不是GPU准入，也不是可信代码安全沙箱。

## 显式程序库闭包

`ExampleHdrGain.ModuleId` 必须在项目的v1 post library索引拥有自己的NPH文件路径和SHA。示例重用现有Combine数值程序，因此可以复制已受检Bloom NPH字节到独立路径（如`assets/postprocess/example-gain.nph`），添加精确模块UUID/路径/hash记录，再保存索引并把新索引SHA写入项目`PostProcessLibrary`。不能给两个模块使用同一路径，也不能覆盖已pin文件或修改部署默认索引。

Feature插入由原PostProcessData有界stack表达；Tone UUID/版本和程序库双shadow/skin/profile闭包保持精确。完整库需经过原CPU文件pin、外部SHA、真实完整反射、同一shared skin及准入审批。缺程序/错SHA/撤销/未知模块均拒绝，不回退到默认模块。`CustomizationExampleTests`中的自有临时项目演示完整索引发布；它不修改用户项目或生产部署。

## 验证范围

独立CLI `--customization-example-tests <plugins> <output>`，完整Build.bat通过独立120秒CTest调用。测试CPU-only/有界坏输入、真实linear gain→Bloom→Tone整图与独立double/Half标量、共享静态场景+IBL、替换拓扑/实际阴影、坏图候选、World写入/关闭/重入/帧内/owner/撤销拒绝、奇数resize与关闭、暖64 Submit分配/构图/上传。

这不是D2的真实FBX/skin/PBR/IBL/shadow/Bloom/UI独立联合图，也不是D3的搬移Editor/Player产品验收；二者仍需完成。GPU逐帧origin-frame时间、人工UI/DPI/第三方MCP、用户素材/HDR解码、目标机/自包含、完整性能/1h仍开放。98项检查与完整双配置结果见[D1交付记录](M7_4_D1_DELIVERY_REPORT.md)。
