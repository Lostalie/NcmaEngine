# M7.4-A Tone 预设与颜色流程合同

## 范围与所有权

纯 C# `SceneTonePresets` 提供 ACES fitted、Reinhard、Linear Clamp 三个声明预设；`SceneToneComposition.Compose` 供官方与用户 Tone 共用。没有新增 native ABI、保留字段含义、场景组件/文件格式、Agent 能力或正式宿主默认选项。已有 native Shader 字节保持不变，当前仅 DX11。

这不是完整后处理栈。Bloom/线性 HDR Feature 是 M7.4-B；正式共享宿主设置、编辑器可撤销参数/AI 提案是 C；动画/IBL/Bloom/UI 的产品联合与完整替换样例是 D。A 的新 Environment 测试使用 Off 绑定；此前真实 IBL/FBX/skin/shadow 联合图测试仍完整保留，不把保留测试称为新增预设联合覆盖。

## 颜色定义

1. Geometry 输出非负 scene-linear RGBA16F；写入时量化至 binary16。
2. Tone 对 RGB 应用曝光倍率，先曝光再曲线。
3. ACES fitted 使用原默认有理式；它不是完整 ACES 色彩管理。Reinhard 为 `x/(1+x)`，Linear Clamp 为 `clamp(x,0,1)`。
4. 仅一次分段精确 sRGB 编码，输出 opaque RGBA8。没有 HDR 显示输出、自动曝光、白平衡或颜色分级声明。
5. UI 以显示域颜色在 Tone 后合成，不再通过 Scene Tone；实际 backbuffer 叠加测试覆盖三预设、两 profile、双 shadow 变体。

`ExposureFromStops` 只接受有限 `[-6,+4]`，返回 `2^EV`，对应 `[0.015625,16]`。底层现有 SceneToneStage 仍支持其原 `[0.01,16]` 倍率合同；没有扩大或改写旧界限。材质 emissive 上限仍为 16，测试覆盖线性 `0/.0001/.003/.18/1/4/16` 及 5 档曝光，而非伪造超出现有材质合同的 65504 输入。

## 声明、准入与生命周期

- 工厂仅接受精确 `Scene3D` / `SceneEnvironment`；未知曲线、Flat2D、Skinning 拒绝。稳定 UUID/hash，owned immutable catalog/descriptor，`Compiled=false`。
- Compose 校验原选择的所有精确 UUID/content hash、完整 shadow 对，以及新 Tone 的相同 profile、Vertex/Pixel、空依赖、原 C400/输入/纹理/采样器声明。只替换 Tone 两项，不改 Geometry/Shadow；重复 UUID/名字、缺依赖等由同一目录验证拒绝。独立 Skin 不属于 Tone 选择，也没有新的 Skin 数值实现。
- Compose 返回新的目录和选择，不发布 GPU 状态，不赋予编译权限。声明不是实际反射证明，源码不是安全沙箱。
- 注册准备/编译继续使用原 `RegisteredSceneShaders` / `RegisteredEnvironmentSceneShaders`；source-free NCS1 通过已有整组实际反射与安装。`GpuValidated=false` 不改。纯 UI 允许受信的 CPU 编译，但拒绝 Scene 安装，不能把编译等同 3D GPU 初始化。
- 可信宿主在 owner thread、健康、离帧、非 simulation、安全准备边界执行；原审批回调撤销/重入、实际输出/连接/资源反射和 fail-stop 规则保留。Agent 不能提交 Shader 文本来调用这个入口。
- 候选失败不改变当前管线/plan；曝光仅改变已有常量，不重建网格/纹理/管线。创建/编译/目录组合不是每帧 API，不能放进 simulation/render tick。

## 使用示例（受信离帧准备）

```csharp
var source = DefaultSceneShaders.CopyCatalog(renderer, shadows: false);
var stages = DefaultSceneShaders.Select(source, shadows: false);
var tone = SceneTonePresets.Create(SceneToneCurve.Reinhard);
var replacement = SceneToneComposition.Compose(source, stages, tone);
var prepared = RegisteredSceneShaders.Prepare(
    renderer, replacement.Catalog, replacement.Selection, trustedPreparationAllowed);
using var pipeline = new ScenePipelineSession(renderer,
    new Scene3DPipeline(exposure: SceneTonePresets.ExposureFromStops(1), shadows: false),
    width, height, shaders: prepared);
```

用户用自己经验证的 `SceneToneSelection` 替换 `tone`，继续走同一 Compose/Prepare/安装；不能直接把声明当 native 程序。Environment 使用对应 profile/catalog/registered wrapper，Tone 仍 C400，而 Geometry 独立 C416 合同不变。

## 自动验证与未关闭门禁

`--tone-preset-tests <pluginRoot> <output>` 同时加入完整 Build.bat 渲染测试入口。538 项含 420 幅全图独立 double 标量 oracle（先 binary16 量化）；12 种 profile/shadow/curve 组合的 source-free 等图及 UI 实际叠加，用户 BGR stage、坏输出/撤销/frame/owner 保持旧图、resize/close/纯 UI 和 6 组暖机 64 帧成本。不得仅凭 PNG 目测通过。

首轮测试夹具错误用了未支持的 64 阴影尺寸；次轮错误假设纯 UI 连受信 CPU 编译都必须拒绝。均修正夹具，不放宽生产合同，原始失败保留。最终结果及冻结源码/双配置/部署见 [交付记录](M7_4_A_DELIVERY_REPORT.md)。

静态提交零托管分配仅针对本测试内核路径，排除配置、编译、capture/oracle；不是完整 Editor 性能。现有 last-valid GPU timing 无 origin frame，不报告当前帧 GPU 均值。M7.3-C3-C 源项目故障恢复高分配调查、人工 UI/DPI/第三方 MCP、真实用户素材、HDR 解码、目标机/.NET 自包含、完整性能与 1h 门禁仍开放。
