# M3.4 scene-pbr-v4 契约

Renderer 模块仍为 ABI 1.2；通过 offset 144 的 query_scene_render 查询独立服务版本 4。
v1/v2/v3 不改布局、颜色或绘制语义。v4 表 48 bytes、capabilities=7（静态数值绘制、方向阴影、HDR/tone）。
未知版本、短 buffer 明确拒绝；短表返回 required_bytes，不发布部分表。

## POD（Windows x64）

| 结构 | bytes | 内容 |
| --- | ---: | --- |
| ScenePipelineDescription | 16 | size、HDR/depth 宽高、shadow resolution；0 不分配阴影资源，否则 256/512/1024/2048 |
| SceneDraw | 256 | 240-byte v3 resource draw；float4 metallic、roughness、scalar-enable 0/1、exclude-direct-light 0/1 |
| ScenePass | 16 | operation 6/7/8；float3 参数：shadow 全零，geometry 为 ambient/0/0，tone 为 exposure/0/0 |
| SceneFrame | 240 | 136-byte base（size=240）+ pipeline opaque key + caster/pass counts + column-major light VP + shadow float4 |
| ScenePipelineStats | 72 | size/maxDraws、device generation、live groups/accounted bytes/creates、geometry/shadow draw totals、copied batch/constant upload bytes |
| ScenePipelineApi | 48 | size/version/capabilities；create/destroy/submit/stats 函数指针 |

SceneFrame.base 的 generation 必须为当前 renderer；frame 单调非零。
camera.w=1、toLight.w=0；RGB 是线性值，主方向光 intensity 与场景 schema 一致。
base.exposure/ambient/mode 不驱动 scene policy（mode 必须 0）；stage 参数来自已验证的 C# graph。
shadow float4 为常量 depth bias、角度相关 bias、0 PCF/1 PCSS、以 texels 表示的光半径。
单 light VP 的覆盖由 C# `ShadowVolume` 计算；`SceneShadowSettings.Distance` 是 C# 有界配置，不是原生相机所有权。

## 安全与语义

- 所有调用仅在 renderer owner thread，submit 数组仅借用到返回，调用时不得并发修改；native 完整复制/验证后执行，不存 caller 指针。
- geometry/caster 各最多 4096，passes 2–16；一个 geometry、可选 shadow 且必须先于 geometry、至少一个 tone、最后为 tone。关闭阴影资源的 group 不可提交 shadow pass/casters。
- 目标 ≤4096×4096，显式 viewport 的宽高必须等于 group HDR/depth；独立 target 可更大。它使用 framebuffer top-left 像素；offscreen letterbox 明确清屏，不读取未初始化像素。
- mesh/material/target/pipeline 必须是当前 renderer 的未释放 opaque key；整个批次最后一项错误同样拒绝，不部分绘制或推进 frame。
- 模型需正可逆 affine；MVP/light VP 需有限非奇异；normal 与模型的 inverse-transpose 一致。C# World/UUID/托管对象不进入 ABI。
- resource v3 的 mesh/texture/material 仍不可变。标量与 layer exclusion 是逐 draw 数值，不修改共享材质。
- main 与 shadow 用相同对象变换、UV 与 alpha-mask；只有已注册静态 geometry，不隐式执行骨架时钟/蒙皮。
- HDR 为 Rgba16Float，有限亮度超过可表达范围钳至 65504；tone 为 ACES/sRGB，非负线性 clear 在视口内/独立目标 letterbox 同一编码。
- group 自有 HDR、depth、可选 shadow 与 shader/常量/默认采样资源。create 候选失败不发布；最多 8 个 group，独立 group accounted budget 512MiB（非显存全局用量保证）。
- active frame 禁止释放；destroy 有界等待 GPU（2s），失败保留句柄/资源，不 unload。执行故障 fail-stop；销毁失效后不可复用 generation。
- C# `ScenePipelineSession.Configure` 在相同 target/shadow allocation 上更新公开 graph 参数，先编译候选；失败保留上一计划。需要资源尺寸变化时重新创建租约。

性能 counters 的 constant upload 是实际 400-byte dynamic buffer updates，包含每个 geometry/caster draw 和 tone。
它不把顶点/纹理缓存无重传称为“零 GPU 上传”；native vector batch copies 和逐 draw 调度成本仍需优化。
GPU timestamp 为最近完成的测量，不承诺每次调用同步给出当前 GPU 时长；正常 tick 不做图像读回。
