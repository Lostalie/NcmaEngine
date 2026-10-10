# M7.3-B 真实 DX11 IBL 分片计划

2026-10-10：开工前本地与远端 main 均为 A 的 `15380f19a5c9d1238d0b125656b29e412c943840`。原 B 目标不缩减，按以下顺序验收、提交、推送后进入下一片。

## B1：环境 GPU 资源（自动候选完成）

54项实际GPU检查、原生故障/闭合字段测试与完整顺序Debug/Release/checked deployment通过，见[B1合同](M7_3_B1_RUNTIME_CONTRACT.md)、[交付记录](M7_3_B1_DELIVERY_REPORT.md)。下一片B2，整个B/M7.3尚未完成。

- 独立 Renderer query14/API1：闭合 POD、复制的 NCE1 数值、renderer-generation opaque handle。冻结 query1–13、旧 Scene/Shader 绑定与 NCS1 包。
- 真正 DX11 immutable RGBA32F irradiance cube、完整 RGBA32F specular mip cube、RG32F BRDF LUT、linear/clamp sampler。RHI 只增加纹理表达，不引入 C++ 高层环境服务。
- 完整候选创建、诊断、必要的有界 GPU drain 后无异常发布；替换/短缓冲/迟到非法值/异常/timeout 保留旧 handle、资源、数值、计数。限定8环境、32MiB，资源释放先 drain，失败可重试。
- C# 拥有包 UUID/generation/hash、显式可信 off-simulation 许可、owner/非重入与 renderer 生命周期。无 Agent endpoint、live World、项目/场景配置或默认宿主切换。
- 有界诊断 readback 从真实 GPU staging 按规范 face/mip 布局读取；不是 CPU 原值回放，不在帧/tick 中使用。验证所有 texel、HDR E 超过65504不截断、最大尺寸、替换/resize/关闭、纯2D无环境分配、API0/0。
- 不宣称 IBL 场景图像已实现；本片没有 Shader 采样、强度/旋转应用。GpuValidated 仍不能写进持久包。

## B2：冻结旧合同之外的新环境 Shader/场景绑定（自动候选完成）

46项实际DX11场景检查、原生故障与完整顺序Debug/Release/checked deployment通过，见[B2方案](M7_3_B2_IMPLEMENTATION_PLAN.md)、[合同](M7_3_B2_RUNTIME_CONTRACT.md)、[交付](M7_3_B2_DELIVERY_REPORT.md)。后续B3也完成自动门禁，见下条；C/整个M7.3尚未完成。

新增独立版本的 geometry binding（cube/mip/LUT/环境参数）；同一个公开合同支持 default/user shader、真实反射及 source-free shader package。旧 Scene shader 包不隐式升级。接入 B1 资源，显式强度/旋转/Off，无同步 cook/inference/IPC tick。整组候选与旧结果保护、阴影/skin 联合路径。

## B3：真实 IBL 图像验收（自动候选完成）

353项实际DX11、独立软件采样/解析常量方向积分/uniform BRDF、36个ASCII/Binary FBX-NCA动画skin/shadow/material整图、强度/旋转/Off/AO/normal/HDR、source-free/default-user、resize/update/8次scene关闭及有界成本通过；像素/积分最大1阶/API0/0。最终完整顺序Debug/Release/checked deployment、14份冻结源码、双配置备份及旧Shader闭包hash通过，见[B3方案](M7_3_B3_IMPLEMENTATION_PLAN.md)、[合同](M7_3_B3_RUNTIME_CONTRACT.md)、[交付](M7_3_B3_DELIVERY_REPORT.md)。B1–B3自动链闭合，提交推送/远端核对后下一片C；正式宿主/项目-NCP配置和整个M7.3、人工/用户素材/目标/自包含/完整性能/1h保持待办。

## 门禁

每片定向测试，再冻结源码并完整顺序无Skip Debug/Release Build.bat，保留原 CTest/managed/Python/M6/M7/格式/MCP/profile/audit/deployment recovery。核验 checked deployment 的 manifest/hash/journal/备份，提交推送与远端 SHA 后再前进。C 的正式宿主、资产闭包、可撤销配置及人工用户 HDR/目标机/自包含/完整性能/1h 验收仍待实现/验收。
