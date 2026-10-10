# M7 DX11 渲染与画质完成度：小阶段执行方案

入口基线：M6.10 `74be5b41e1b595aa43c9a4e04fcfd9a30684f60f` 已推送且远端main一致。
M6最终完整Debug/Release自动门禁通过，详见[M6.10交付](M6_10_DELIVERY_REPORT.md)。
M7.1-A纯C#描述/目录/声明验证自动候选已实现，70项定向及完整顺序Debug/Release/checked deployment通过；
真实编译/反射B41项及完整门禁通过；C1 Tone24项、C2 Geometry/Shadow/Skin40项、C3独立Flat2D32项、C4-A77/B42/C62及各自最终顺序Debug/Release/checked deployment通过。M7.1自动候选闭合；M7.2–M7.7未实现。最新项目文件/运行包/18文件独立UI及联合结果见[C4-C交付](M7_1_C4C_DELIVERY_REPORT.md)，历史切片见[C1交付](M7_1_C1_DELIVERY_REPORT.md)、[C2交付](M7_1_C2_DELIVERY_REPORT.md)、[C3交付](M7_1_C3_DELIVERY_REPORT.md)、[C4-B交付](M7_1_C4B_DELIVERY_REPORT.md)。

## 本版本边界

仅DX11实际后端。保留API中立的设备能力、资源格式／使用、shader语义、执行图与模块版本边界；
Vulkan/OpenGL实际device/swapchain/shader/draw及验证明确延期下一版本，不注册假支持或静默回退。
C#拥有组合／配置／资产／编辑事务／审批，C++仅GPU执行／资源／必要数值内核；无新C++World。
2D与3D独立产品管线：纯2D不创建shadow/HDR/IBL/动画资源，3D提供可直接使用的默认与简单定制入口。
不在simulation/render tick编译shader、扫描文件、等待推理或同步IPC。

## 现有基线与缺口（不是从零重做）

- 已有：DX11真实静态/资源PBR/scene HDR/tone/PCSS/compute skin/shared shadow/UI合成、resize/关闭、API0/0测试。
- 已有：`managed/Ncma.Rendering/RenderGraph.cs`有界编译、依赖/尺寸/role/操作验证；
  `ScenePipeline.cs`的Feature/Geometry/Tone接口。现有操作/shader契约仍固定，不能称通用自定义shader框架。
- 已有：`D3D11RenderBackend.cpp`的D3DCompile及无输入layout分支的局部VS signature检查。
  B候选新增独立完整常量/SRV/sampler/stage反射准备服务；C1闭合Tone、C2闭合Geometry/Shadow/Skin、C3闭合独立Flat2D实际GPU接入；C4-A/B/C已有无源码包、实际准入、正式共享Scene/UI准备、项目文件选择/pin与checked部署。内部参考反射仍需编译器；不是任意Shader执行沙箱。
- 已有：`ScenePipelineKernel.cpp`真实纹理PBR/普通PCF及近似PCSS，不能把当前ambient常量称IBL。
- 未实现：可发布typed shader运行资产与通用跨阶段受控绑定，IBL，完整后处理栈与用户定制GPU模块；B完整闭合反射准备不代表GPU接入。
- 未实现：带origin frame/device generation的完整GPU成本证据。现有ResolveTiming是DONOTFLUSH
  last-valid query，没有采样帧ID；不能按当前提交帧归属或把重复观察算独立样本。
- 现有RenderPipelineService配置更换用同步WaitIdle安全退役，不冒称已有异步retirement/device-loss恢复。

## M7.1 Shader与绑定契约（A/B/C1/C2/C3/C4-A/B/C自动闭环）

C4最新交付：A source-free NCS1/native-free预检77项、B实际准入/共享宿主42项、C项目文件/运行包发布62项及完整联合回归和顺序Debug/Release/checked部署通过，见[C4方案](M7_1_C4_IMPLEMENTATION_PLAN.md)、[A交付](M7_1_C4A_DELIVERY_REPORT.md)、[B交付](M7_1_C4B_DELIVERY_REPORT.md)、[C交付](M7_1_C4C_DELIVERY_REPORT.md)。CPU包不是GPU安装凭证；自动链闭合但人工/用户FBX/目标/自包含/完整性能/1h仍开放。

详见[M7.1方案](M7_1_IMPLEMENTATION_PLAN.md)。先A纯C#受控描述/验证，再B薄native真实编译/反射，
最后C同一个注册目录接入官方默认与用户管线。shader源代码与普通数据事务分权限，不开任意Agent执行。
测试错误stage/vertex语义/cbuffer布局/资源种类/预算、真实shader像素及编译失败保留原管线。

## M7.2 3D PBR纹理与默认材质（未实现）

统一颜色/数据纹理语义、法线切线/符号、metal-rough-AO打包通道、材质defaults/静态和动画一致性。
复用已有GPU缓存/增量更新，不每帧上传网格或完整场景文档。给出材质诊断/受控preset及同源Undo。
测试颜色空间、UV/非均匀变换/alpha cutoff、缺失或错误语义纹理拒绝，独立CPU预期与固定图像容限。
已有测试原样保留，新覆盖通过后才称补齐。

## M7.3 环境光与IBL（未实现）

C#拥有环境资产/强度/旋转/配置与闭包；native执行环境纹理、diffuse irradiance、prefiltered specular
及BRDF LUT数值工作。离线准备与精确hash/generation，默认环境可直接用，纯2D完全不分配。
验证常量环境、方向环境、金属/粗糙度扫图、energy/色彩空间、损坏包、重新配置与GPU关闭。
用独立数值预期，不仅与共享shader互相比较；任何环境生成器都不被称真实用户HDR素材验收。

## M7.4 HDR后处理与简单定制（未实现）

明确linear HDR→tone→display/UI顺序；Exposure/Tone预设及可关闭Bloom等首版Feature。
官方默认和用户registered Feature使用同一契约，依赖/尺寸/格式/生命周期自动校验。
只插入实际注册实现，未实现效果返回unsupported。新增stage replacement/full replacement实际样例。
验证高动态范围/过曝/暗部、UI不受场景tone重复处理、Feature增删/顺序/错误候选保留active plan。

## M7.5 软阴影档位（未实现）

在现有PCF/近似PCSS真实路径上定义Low/Medium/High的清晰分辨率、采样预算、bias/light radius/distance。
C#参数/preset拥有策略，native执行过滤；Off不分配shadow资源。先量化现有实现，不把近似算法称完整PCSS。
静态/动画同一蒙皮结果、off-camera caster、近远接触/漏光/自阴影、半影、resize和关闭都需固定容限。
每档CPU/GPU/显存/采样证据与目标预算分别记录；未经目标机器批准不称性能验收通过。

## M7.6 资源更新与故障恢复（未实现）

device generation/opaque handle/caller-owned POD/线程/lease/释放/失效契约完整；旧ABI冻结，必要扩展用新query。
texture/material/shader/target候选完整验证后安装；安全边界退役，失败保留有效旧配置或明确fail-stop。
resize/minimize/恢复/重建、过期handles、借用关闭失败、异常创建、真实device-loss与注入失败分开证明。
不能用模拟异常声称真实硬件丢失恢复；不force unload有lease的插件。

## M7.7 联合参考与成本验收（未实现）

补齐有界GPU sample origin frame/device身份、单调序列/valid/disjoint状态；非阻塞读、不可用返回明确状态。
3D静态/动画/PBR/IBL/shadow/HDR/UI联合图，独立数值oracle和事先定义像素容限，API0/0。
2D静态应用验证事件刷新/缓存/dirty上传及无3D资源，区分CPU/GC/ABI/upload/GPU成本。
正式Editor/Player/NCP1搬移及生命周期回归；真实用户素材、可见操作、目标预算/自包含/1h仍需真实证据。

## 每个小阶段统一交付规则

详细契约→实现→定向测试→修复→冻结源码→完整顺序无Skip Debug/Release Build.bat。
两个配置通过后核验manifest全部hash/Complete journal/恢复备份→源码文档提交推送→远端SHA核对→下一阶段。
保留所有失败/SDK/userdata/IDE，默认拒绝AI越权。M7.7自动候选闭环后再进入M8，M8之后M9；
这不是人工/性能/目标验收豁免，也不自动启用推理凭据／Python传输或代码执行。
