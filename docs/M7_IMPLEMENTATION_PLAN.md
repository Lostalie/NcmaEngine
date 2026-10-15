# M7 DX11 渲染与画质完成度：小阶段执行方案

入口基线：M6.10 `74be5b41e1b595aa43c9a4e04fcfd9a30684f60f` 已推送且远端main一致。
M6最终完整Debug/Release自动门禁通过，详见[M6.10交付](M6_10_DELIVERY_REPORT.md)。
M7.1-A纯C#描述/目录/声明验证自动候选已实现，70项定向及完整顺序Debug/Release/checked deployment通过；
真实编译/反射B41项及完整门禁通过；C1 Tone24项、C2 Geometry/Shadow/Skin40项、C3独立Flat2D32项、C4-A77/B42/C62及各自最终顺序Debug/Release/checked deployment通过。M7.1自动候选闭合；M7.2材质合同/预设/运行包及68项真实GPU检查、完整双配置/部署通过，见[M7.2交付](M7_2_DELIVERY_REPORT.md)。M7.3-A离线环境资产/数值Cook的123项检查与完整双配置/部署通过；B1环境资源54项、B2独立Shader/场景绑定46项、B3完整自动图像353项及各自完整双配置/部署通过，见[B3交付](M7_3_B3_DELIVERY_REPORT.md)。B自动链闭合；C1环境资产/场景配置/运行包新增29+1项及完整双配置/部署通过，见[C1交付](M7_3_C1_DELIVERY_REPORT.md)。C2-A显式环境Shader文件/整组准入71项及完整双配置/部署通过，见[C2-A交付](M7_3_C2A_DELIVERY_REPORT.md)。C2-B共享场景执行87项及完整双配置/部署通过，见[C2-B交付](M7_3_C2B_DELIVERY_REPORT.md)；C2-C正式宿主43项及完整双配置/部署通过，见[C2-C交付](M7_3_C2C_DELIVERY_REPORT.md)；C3-A默认资源/部署83项及完整双配置通过，见[C3-A交付](M7_3_C3A_DELIVERY_REPORT.md)；C3-B1属性/状态8组、正式宿主51项及完整双配置通过，见[C3-B1交付](M7_3_C3B1_DELIVERY_REPORT.md)；C3-B2预设可逆发布/独立目录授权完成，C3-C双配置490项产品检查/8个探针及完整回归通过，见[C3-C交付](M7_3_C3C_DELIVERY_REPORT.md)；M7.3自动候选链闭合；M7.4-A三种Tone预设/颜色流程与538项检查、完整双配置通过，见[A交付](M7_4_A_DELIVERY_REPORT.md)；B公开HDR图/Bloom/独立NPH1的172项及完整双配置通过，见[B交付](M7_4_B_DELIVERY_REPORT.md)；C1程序库149项及完整双配置通过，见[C1交付](M7_4_C1_DELIVERY_REPORT.md)；C2正式配置/共享宿主及完整双配置通过，见[C2交付](M7_4_C2_DELIVERY_REPORT.md)；C3专用编辑/AI目录与原事务及完整双配置通过，见[C3交付](M7_4_C3_DELIVERY_REPORT.md)；D1公开定制示例98项及完整双配置通过，见[D1交付](M7_4_D1_DELIVERY_REPORT.md)；D2联合图像582次检查/120图、独立后处理整图最大误差1/API0/0及完整顺序Debug/Release通过，见[D2合同](M7_4_D2_IMAGE_CONTRACT.md)、[D2交付](M7_4_D2_DELIVERY_REPORT.md)；D3-A正式宿主可信管线定制/599项检查及完整双配置通过，见[D3-A交付](M7_4_D3A_DELIVERY_REPORT.md)；D3-B正式Player被动HUD/受检NCU1闭包、336项检查及完整顺序Debug/Release通过，见[D3-B合同](M7_4_D3B_RUNTIME_CONTRACT.md)、[D3-B交付](M7_4_D3B_DELIVERY_REPORT.md)；D3-C已完成Editor场景HUD、可信交互路由、作者不可变准备与同一历史的选择发布，以及完整FBX/IBL/阴影/Bloom/UI搬移联合；64份冻结源码完整顺序Debug/Release各12原生/39联合组/Python44通过，正式Release受检部署完成，见[D3-C交付](M7_4_D3C_DELIVERY_REPORT.md)。D3/D/M7.4自动候选链闭合；人工、用户素材、目标环境及完整性能门禁保留，M7.5软阴影档位自动候选已完成，见[M7.5实施与交付](M7_5_IMPLEMENTATION_PLAN.md)；下一阶段M7.6资源更新与故障恢复。M7.6–M7.7及人工/用户素材/完整性能门禁未完成。项目文件/运行包/18文件独立UI及联合结果见[C4-C交付](M7_1_C4C_DELIVERY_REPORT.md)，历史切片见[C1交付](M7_1_C1_DELIVERY_REPORT.md)、[C2交付](M7_1_C2_DELIVERY_REPORT.md)、[C3交付](M7_1_C3_DELIVERY_REPORT.md)、[C4-B交付](M7_1_C4B_DELIVERY_REPORT.md)。

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
- 已有：闭合 source-free shader 运行资产与实际准入；M7.3-B 独立真实 GPU IBL 及图像验证，C1 环境资产/场景配置/运行包，C2/C3 正式宿主显式 IBL、默认资源、属性/预设和产品联合自动验收。未实现：通用跨阶段受控绑定、完整后处理栈与任意用户定制 GPU 模块；人工/用户素材/性能门禁仍开放。
- 未实现：带origin frame/device generation的完整GPU成本证据。现有ResolveTiming是DONOTFLUSH
  last-valid query，没有采样帧ID；不能按当前提交帧归属或把重复观察算独立样本。
- 现有RenderPipelineService配置更换用同步WaitIdle安全退役，不冒称已有异步retirement/device-loss恢复。

## M7.1 Shader与绑定契约（A/B/C1/C2/C3/C4-A/B/C自动闭环）

C4最新交付：A source-free NCS1/native-free预检77项、B实际准入/共享宿主42项、C项目文件/运行包发布62项及完整联合回归和顺序Debug/Release/checked部署通过，见[C4方案](M7_1_C4_IMPLEMENTATION_PLAN.md)、[A交付](M7_1_C4A_DELIVERY_REPORT.md)、[B交付](M7_1_C4B_DELIVERY_REPORT.md)、[C交付](M7_1_C4C_DELIVERY_REPORT.md)。CPU包不是GPU安装凭证；自动链闭合但人工/用户FBX/目标/自包含/完整性能/1h仍开放。

详见[M7.1方案](M7_1_IMPLEMENTATION_PLAN.md)。先A纯C#受控描述/验证，再B薄native真实编译/反射，
最后C同一个注册目录接入官方默认与用户管线。shader源代码与普通数据事务分权限，不开任意Agent执行。
测试错误stage/vertex语义/cbuffer布局/资源种类/预算、真实shader像素及编译失败保留原管线。

## M7.2 3D PBR纹理与默认材质（自动候选完成）

统一颜色/数据纹理语义、法线切线/符号、metal-rough-AO打包通道、材质defaults/静态和动画一致性。
复用已有GPU缓存/增量更新，不每帧上传网格或完整场景文档。给出材质诊断/受控preset及同源Undo。
测试颜色空间、UV/非均匀变换/alpha cutoff、缺失或错误语义纹理拒绝，独立CPU预期与固定图像容限。
已有测试原样保留，新覆盖通过后才称补齐。
实际范围、正变换限制及证据见[实施方案](M7_2_IMPLEMENTATION_PLAN.md)、[合同](M7_2_RUNTIME_CONTRACT.md)、[交付](M7_2_DELIVERY_REPORT.md)。材质面板人工操作/用户素材/目标环境/完整性能/1h验收仍待完成；下一阶段M7.3。

## M7.3 环境光与IBL（A/B/C自动候选完成，人工/用户素材/完整性能门禁保留）

A离线资产/Cook123项、B1真实环境资源54项、B2独立环境Shader/场景绑定46项、B3完整自动图像353项及各自完整Debug/Release/checked部署通过，见[分片方案](M7_3_IMPLEMENTATION_PLAN.md)、[B3交付](M7_3_B3_DELIVERY_REPORT.md)。C1追加29项资产/文件/包/历史与1项正式Editor命令测试，完整双配置/部署通过，见[C分片](M7_3_C_IMPLEMENTATION_PLAN.md)、[C1合同](M7_3_C1_RUNTIME_CONTRACT.md)、[C1交付](M7_3_C1_DELIVERY_REPORT.md)。独立GPU合同已真实消费IBL；正式宿主C2-C已支持显式IBL，默认环境资源已由C3-A部署、已有绑定属性/状态面板C3-B1已通过8组/正式宿主51项及完整双配置，C3-B2预设发布已完成，C3-C产品联合及完整双配置已通过；ambient保持独立含义。没有HDR/EXR文件解码器，GpuValidated=false；M7.3自动候选链闭合，人工/用户素材/完整性能仍待验收。C2-A显式v2/9包/双变体/全profile共享skin与整组准入71项及完整双配置/部署通过，见[C2分片](M7_3_C2_IMPLEMENTATION_PLAN.md)、[C2-A交付](M7_3_C2A_DELIVERY_REPORT.md)。C2-B共享服务87项及完整双配置已通过，见[C2-B交付](M7_3_C2B_DELIVERY_REPORT.md)。C2-C正式宿主43项及完整双配置通过，C3-A默认资源/部署83项及完整双配置通过，C3-B1属性/状态8组及正式宿主51项、完整双配置通过；下一片M7.4，详见[分片方案](M7_4_IMPLEMENTATION_PLAN.md)。

C#拥有环境资产/强度/旋转/配置与闭包；native执行环境纹理、diffuse irradiance、prefiltered specular
及BRDF LUT数值工作。离线准备与精确hash/generation，默认环境可直接用，纯2D完全不分配。
验证常量环境、方向环境、金属/粗糙度扫图、energy/色彩空间、损坏包、重新配置与GPU关闭。
用独立数值预期，不仅与共享shader互相比较；任何环境生成器都不被称真实用户HDR素材验收。

## M7.4 HDR后处理与简单定制（A/B/C1/C2/C3/D1/D2/D3-A/D3-B/D3-C自动候选完成，人工门禁保留）

分为A颜色流程/Tone预设、B真实HDR Feature/Bloom、C正式配置/编辑器/AI、D替换与产品联合，见[详细方案](M7_4_IMPLEMENTATION_PLAN.md)、[A合同](M7_4_A_RUNTIME_CONTRACT.md)。A新增538项检查及完整顺序Debug/Release门禁已通过，见[A交付](M7_4_A_DELIVERY_REPORT.md)；B新增公开HDR子图/独立NPH1/薄native执行及172项真实图像，完整顺序Debug/Release已通过，见[B合同](M7_4_B_RUNTIME_CONTRACT.md)、[B交付](M7_4_B_DELIVERY_REPORT.md)。C1程序库与产品闭包149项及完整双配置通过，见[C详细方案](M7_4_C_IMPLEMENTATION_PLAN.md)、[C1交付](M7_4_C1_DELIVERY_REPORT.md)。C2正式配置/共享执行及完整双配置通过，见[C2合同](M7_4_C2_RUNTIME_CONTRACT.md)、[C2交付](M7_4_C2_DELIVERY_REPORT.md)。C3专用编辑/AI原事务与独立只读目录、9组作者测试及正式DX11控件联动、完整双配置通过，见[C3合同](M7_4_C3_RUNTIME_CONTRACT.md)、[C3交付](M7_4_C3_DELIVERY_REPORT.md)。C自动候选链闭合。D拆为D1示例、D2独立联合图、D3搬移产品，见[D方案](M7_4_D_IMPLEMENTATION_PLAN.md)。D1公开示例98项/API0/0/独立标量整图及完整双配置通过，见[示例](M7_4_D1_CUSTOMIZATION_EXAMPLES.md)、[交付](M7_4_D1_DELIVERY_REPORT.md)；D2联合图像582次检查/120图、独立后处理整图最大误差1/API0/0及完整顺序Debug/Release通过，见[D2合同](M7_4_D2_IMAGE_CONTRACT.md)、[D2交付](M7_4_D2_DELIVERY_REPORT.md)；D3-A正式宿主可信管线定制、599项源/搬移检查及完整顺序Debug/Release通过，见[D3-A合同](M7_4_D3A_RUNTIME_CONTRACT.md)、[D3-A交付](M7_4_D3A_DELIVERY_REPORT.md)；D3-B正式Player被动HUD/受检NCU1闭包、336项检查及完整顺序Debug/Release通过，见[D3-B合同](M7_4_D3B_RUNTIME_CONTRACT.md)、[D3-B交付](M7_4_D3B_DELIVERY_REPORT.md)；D3-C已完成Editor场景HUD、可信交互路由、作者不可变准备与同一历史的选择发布，以及完整FBX/IBL/阴影/Bloom/UI搬移联合；64份冻结源码完整顺序Debug/Release各12原生/39联合组/Python44通过，正式Release受检部署完成，见[D3-C交付](M7_4_D3C_DELIVERY_REPORT.md)。D3/D/M7.4自动候选链闭合；人工、用户素材、目标环境及完整性能门禁保留，M7.5软阴影档位自动候选已完成，见[M7.5实施与交付](M7_5_IMPLEMENTATION_PLAN.md)；下一阶段M7.6资源更新与故障恢复。

明确linear HDR→tone→display/UI顺序；Exposure/Tone预设及可关闭Bloom等首版Feature。
官方默认和用户registered Feature使用同一契约，依赖/尺寸/格式/生命周期自动校验。
只插入实际注册实现，未实现效果返回unsupported。新增stage replacement/full replacement实际样例。
验证高动态范围/过曝/暗部、UI不受场景tone重复处理、Feature增删/顺序/错误候选保留active plan。

## M7.5 软阴影档位（自动候选完成）

详细执行与验收见[M7.5实施与交付](M7_5_IMPLEMENTATION_PLAN.md)：A公开C#四档预设与预算，B严格单场景配置/正式共享宿主/Inspector及原Undo/Agent命令，C独立图像、动画与搬移产品联合均已完成自动验收。真实ASCII/Binary FBX四档各64姿态，共512整图CPU/GPU蒙皮对照3016检查；40张独立边界图81检查；默认/stage/full High/IBL/Bloom/HUD搬移联合902检查，均保留原测试。47份冻结源码完整顺序无Skip Debug第三轮、Release第一轮各12原生/47联合组/Python44、3轮profile/审计/恢复及实际EXE核验通过；Release受检部署完成。前两次Debug超时和诊断保留，原门限未放宽，根因未声称修复；人工/用户素材/完整性能/1h仍待验收。

在现有PCF/近似PCSS真实路径上定义Low/Medium/High的清晰分辨率、采样预算、bias/light radius/distance。
C#参数/preset拥有策略，native执行过滤；Off不分配shadow资源。先量化现有实现，不把近似算法称完整PCSS。
静态/动画同一蒙皮结果、off-camera caster、近远接触/漏光/自阴影、半影、resize和关闭都需固定容限。
每档CPU/GPU/显存/采样证据与目标预算分别记录；未经目标机器批准不称性能验收通过。

## M7.6 资源更新与故障恢复（自动候选完成）

2026-10-15 **整阶段最终自动验收通过**：下方逐次增量的“未完成/未部署/运行中”是历史检查点，以本节为准。A准备与资源所有权、B候选交换/失败退役保留、C正式宿主显式恢复/原始诊断/跨DLL身份、D四档完整搬移联合及独立2D恢复均完成自动范围。不是物理显卡掉线、人工操作或完整性能验收。

同一861文件冻结摘要 `66C640482AC4417C75D5F0CF4C0AFA00D565956CF0ECCCCD767FB58793121F0B` 顺序无Skip Debug第一轮、Release第一轮均退出0；最终逐路径和SHA再次一致，未放宽旧超时、断言或批准边界。

| 最终验证 | Debug | Release |
| --- | --- | --- |
| 原生CTest | 13/13，9.66秒 | 13/13，8.32秒 |
| 托管/图形CTest | 64/64，996.76秒 | 64/64，925.56秒 |
| 原Rendering / Editor回归 | 185.46 / 79.96秒 | 184.82 / 77.51秒 |
| High / Off / Low / Medium完整恢复 | 67.86 / 57.94 / 59.47 / 59.38秒 | 66.16 / 57.13 / 56.44 / 57.06秒 |
| Python | 45项，1.501秒 | 45项，1.233秒 |
| 五种产品闭包文件数 | 144 / 56 / 113 / 67 / 116 | 144 / 56 / 113 / 67 / 116 |

原有ABI/格式拒绝/MCP/烟测/inspect、三轮历史profile及部署前后审计/失败恢复测试均通过，编译警告0。完整日志 `out/m7-6-full-{debug,release}-first.log`；候选Debug `59771e8fcefa489f901536ee904de2f5`、Release `22d60ba74e084e28843540da8a9dcc3e`。物理Player是显式选项，默认两种Player不附带求解器；四种Player不包含导入编辑工具。

正式Release受检部署journal `cf190e5c6f1746048e520611fd13fc74` 为Complete，144清单/148实际/148备份文件全部核验，真实 `out/bin/NcmaEngine.exe --validate-package` Hidden/Wait退出0。EXE SHA `A0586BCFD4B2EE8A82B39CDDEBCC29D2C31C55F4188A5E6A43C6BEA577677636`，入口DLL SHA `7807324C8828449B87F49258D039138FE506B8473CD13B08959B1A83E99E2A62`。Debug备份journal `ace46b5019aa4a0fa605e1852ceab839` 同样Complete且精确核验；证据 `out/m7-6-{debug,release}-deployment-check.json`。所有工作构建/测试会话终结；下一步源码/文档提交推送并核对远端，再推进M7.7，不将本阶段增量分拆提交。

保留全部历史失败、4项IDE用户配置、SDK、用户数据和恢复备份。人工IME/DPI/第三方MCP、真实用户素材/HDR解码边界、物理硬件掉线、目标环境/自包含、完整CPU/GPU/GC预算与1小时长稳仍未通过；File.Replace、空缓存及既有分配异常调查不因本轮绿色而宣告解决。`audit_passed` 仍不等于 `h8_accepted`。M7.7继续补齐GPU计时来源帧/设备/有效性与联合参考成本验收，M7整体未关闭。

2026-10-15 **D 四档恢复覆盖与整阶段冻结（全门禁执行中）**：新增独立Off/Low/Medium联合CTest，保留High原测试，每项300秒；没有修改旧测试期限。四档全部覆盖相同ASCII/Binary FBX、显式合成图/root、Jolt、IBL/Bloom/交互HUD、默认/阶段/完整替换、窗口和两次设备恢复及重复Play生命周期。新增检查实际已提交Plan的档位、Off零阴影图/零绘制，以及Low512/Medium1024/High2048尺寸和阴影批次，恢复前后精确像素与业务状态不变。

第一次Off失败为负例夹具错误：程序库仅供High且按既有产品契约懒加载，不能要求Off读取未使用的损坏库。损坏库项目现明确选High（独立required-high场景），保留启动前拒绝断言；不改变生产加载策略。最终Debug Skip候选 `8b673629221342068e8b9b9e979e3135` 构建0警告，四档245.99秒全部通过（High68.09/Off58.06/Low59.61/Medium60.21秒），日志 `out/m7-6-four-quality-{off-debug-first,debug-build,debug-tests}.log`。自定义测试管线只新增明确尺寸参数，没有产品GPU/游戏逻辑/Agent权限改动。

冻结861份实现/测试/构建/Python文件，摘要 `66C640482AC4417C75D5F0CF4C0AFA00D565956CF0ECCCCD767FB58793121F0B`，逐文件清单 `out/verification/m7-6/frozen-source-20261015.json`。规范无Skip Debug第一轮已终结退出0（`out/m7-6-full-debug-first.log`）：13原生、64托管/图形组及Python45全部通过，三轮历史性能观测、前后审计和受检部署通过。候选 `59771e8fcefa489f901536ee904de2f5` 五种产品包SHA/闭包通过；正式journal `ace46b5019aa4a0fa605e1852ceab839` Complete，144清单/148实际/148精确备份文件，实际apphost验证退出0，详见 `out/m7-6-debug-deployment-check.json`。冻结文件再次核对无变化，同源无Skip Release第一轮正在执行（`out/m7-6-full-release-first.log`，会话19530）。Release、最终源码核对、提交推送仍未完成；M7.6/M7.7未关闭，所有人工、真实硬件、用户素材、目标/自包含、完整性能/1h及历史异常调查继续保留。Debug入口的人工核验命令最初未等待GUI apphost而无法读取LASTEXITCODE，已改为Hidden/Wait/ExitCode并通过，不是产品构建失败。

整阶段验收映射（最终全门禁结果待补，不以单项绿色代替整阶段）：

| 范围 | 当前实现边界 | 直接验证入口 |
| --- | --- | --- |
| A 准备/身份 | C#候选验证、owner/read-only/重入保护；精确资产代/hash与设备身份 | ResourceUpdateSafety、SceneCandidateSafety、SceneConstructionSafety、Plugin ABI |
| B 交换/退役 | 新旧资源显式持有；首次关闭失败停止并保留可重试所有权；不强制卸载 | SceneExchangeSafety、PlayerRunnerLifetime、EditorLifetime、原发布/Undo组 |
| C 宿主恢复 | 原World/Play/历史/CPU动画保持；精确单次批准；窗口暂停/重建；首个原始诊断保留 | WindowSurface、EditorRecoveryLoop、PlayGraphicsRecovery、PlayerGraphicsRecovery、GpuDiagnostics、真实GraphicsReload |
| D 联合与2D隔离 | 四档×默认/阶段/完整管线×ASCII/Binary；图/root/Jolt/IBL/Bloom/交互HUD；两次重建/三轮Play；原18文件独立UI三代恢复 | GraphicsRecoveryJoint及Off/Low/Medium、SceneUi/ShadowJointProducts、NcmaUiSampleSmoke |
| 全交付 | 冻结同源顺序无Skip Debug/Release；全部CTest/Python/格式/ABI/MCP/三轮历史性能观测/审计；受检备份部署与实际apphost | Build.bat、m2_audit、EditorDeployment、最终逐文件清单和恢复journal |

隔离说明：D的根运动数值伴随角色为明确合成数据，FBX角色仍使用实际导入动画；图像前后精确相同不是新的独立全光照oracle。失败为受控已提交帧/资源借用，不证明物理显卡掉线。原始诊断的帧号是CPU观察帧，不是GPU计时来源帧；完整性能和长稳验收不能由短时缓存不增长或历史观测通过推导。

2026-10-15 **D 窗口/重复生命周期/独立2D恢复增量（双配置核验通过，非整阶段交付）**：在上一增量相同受检搬移的 ASCII/Binary FBX + 合成图/root + Jolt/High/IBL/Bloom/交互HUD场景中，Editor新增最小化和零宽无提交、真实尺寸变化/还原精确像素、恢复后第二次已提交帧故障及新设备重建，以及共三轮 Play/Reload/Stop。每轮保留 Edit 文档/历史并在Stop后排空数值世界。Player在原运行器第一次批准恢复后，通过可信SDK边界执行相同窗口变化和第二次重建，保持CPU动画/物理状态、UI实例及首个数值诊断。它不是第二次自动系统确认或人工OS窗口验收。

最终规范Skip Debug `cf2c17b95e9e43009007141a7ffa0e61` / Release `6eaea34f8417478388ce3aa32f4dc490` 均构建0编译警告，两配置各10组定向CTest通过187.32/178.57秒（联合71.42/68.37秒），包含原UI/Gameplay、UI/阴影联合、窗口/状态/Editor和Player恢复及独立UI apphost。Python45通过1.808秒；两配置五产品清单SHA/闭包再次通过144/56/113/67/116文件。所有工作会话终结，最后61132退出0。861份选定源码摘要在最终Release验证前后一致 `11F815D4F07DE63822C1A9124A2C9FDCC881A471318C9F295671A9FA4C61A732`，不是整阶段冻结；未在本轮重跑全部Rendering/Editor回归，不能用上轮结果冒充本轮全门禁。

联合证据 `out/verification/m7-6/graphics-recovery-joint-{Debug/376f4586c8194096a94381977f7133a9,Release/51f82ad4343e44edb4f3e59de28c05e2}`，Editor每素材157项语义检查，外层1228项仍主要为复制/清单/入口检查。独立UI搬移证据 `out/verification/m7-1-c3/{Debug/5a21c89d5d1f480182f2f0a9991b06ae,Release/ed7dd5ce4fc143218656918a5bbd9370}/result.json` 各40项、三代设备、runtimeShaderPackage=true、原18文件不变。日志 `out/m7-6-joint-lifecycle-final-{debug-build,release-build,debug-tests,release-tests,python,package-audit-debug,package-audit-release}.log`。正式M7.5 EXE/DLL及HEAD未变，无正式部署/提交推送。

下一步：补齐/审计 Off/Low/Medium/High 在完整故障恢复链的覆盖（已有四档正常绘制不等于恢复）；之后执行整阶段冻结无Skip Debug/Release、受检部署/备份恢复与实际入口核验，再提交推送整个M7.6。窗口/重复生命周期及独立纯2D自动链以上述最新证据为准；人工/物理显卡故障/用户素材/目标环境/自包含/完整性能/1h和历史异常调查仍保持开放。

独立 `NcmaUiSample --recovery-smoke` 为显式自动验证模式，不改变普通交互示例的恢复授权策略；采用不含字体的内存UI数据，保留实际滑块输入值，在三代真实DX11设备上验证像素一致、尺寸/最小化、暖态缓存、已提交未完成帧显式abandon、真实外借图像阻止退休及明确重试。每代排空资源，证明无3D/物理/字体初始化；不加载World/Scene/Editor/ImGui/动画模块。`Test-UiSamplePackage.ps1` 保留原烟测并追加搬移后的真实apphost恢复检查，原18文件闭包和运行前后hash不变，不部署新依赖。源40项和Debug搬移40项已通过，源码路径允许原shader机制，搬移路径明确使用唯一Flat2D包。

保留失败：首轮测试把GuiImageToken误写为ulong、独立示例误用测试友元内部IsClosed，均编译拒绝后修正为公开类型/状态，不扩大访问权限；第一次联合Reload断言误把数值epoch内序列等同World tick，现按原MovementCoordinator.Start契约检查新序列恰好+1、CommittedTick及动画tick都等于World tick，生产保护条件未改。第二次完整Editor联合65.68秒、加入Player窗口链后第三次70.18秒通过。最终规范Debug Skip候选 `cf2c17b95e9e43009007141a7ffa0e61` 已成功；最终双配置测试尚在执行，未部署/提交推送，不关闭D/M7.6。

2026-10-15 **D 搬移产品联合恢复增量（双配置核验通过，非整阶段交付）**：新增独立 `GraphicsRecoveryJoint`，从清单受检产品复制并锁住原始运行文件和作者输入。ASCII/Binary FBX 与显式合成的 0.5m/s 动画图/root-motion 伴随角色、Jolt、IBL、High 阴影、Bloom、交互 HUD 同时运行；根运动数值预期属于合成伴随角色，不冒称用户 FBX 动作 oracle。官方默认、Geometry 替换、完整管线替换分别验证故障后新设备恢复，保留同一 Play/动画实例、私有 C# 计数、滑块值、求解器 epoch/步序列和原 Edit 历史；暂停最终像素与蒙皮顶点精确一致。真实借用资源退休失败、单次批准失效、自定义准备失败与新批准重试均保留状态；Stop 排空物理资源并恢复 Edit 图像。

最终结果（替代下文首次 Debug 通过时的进行中状态）：规范 Skip Debug `57e6356204e044a1b8f072176b900431` / Release `836c0edadb634d56ad7a5df09d2d6819` 均构建成功，0编译警告；两配置各15组CTest全部通过，Python45通过1.424秒。重点6组 Debug133.13+107.19秒、Release225.71秒；补充9组 Debug280.81秒、Release281.71秒。原 Rendering193.90/193.78秒，原240秒门限不变；原 Editor78.06/79.40秒，原 Player77.30/72.87秒。所有工作会话已正常结束，最后23514退出0。

联合证据 `out/verification/m7-6/graphics-recovery-joint-{Debug/6c5ced01355942d58fc7e29ef98a5949,Release/98ddf0cec890443cb05a700be9471054}`：每配置1228项外层复制/清单/入口检查，内层独立 `probe.json` 记录实际语义断言（Editor每素材109项，Player计数受实际帧数影响），不可将1228都称作不同恢复场景。两配置五产品逐文件SHA/闭包审计通过，文件数 Editor144/Null56/DX11113/NullPhysics67/DX11Physics116。16帧Editor暖态仍约1.2MB当前线程分配，保持性能门禁开放。

最终日志 `out/m7-6-joint-recovery-final-{debug-build,release-build,debug-tests,debug-original-joints,release-tests,debug-original,release-original,python,package-audit-debug,package-audit-release}.log`；859份选定源码/测试/脚本/Python文件前后摘要一致 `2E3F16CA75FAA4134A2C10165B9D8D14E7677E57520D3E5B35604D94F820BDE6`，不是整阶段冻结。正式M7.5 EXE/DLL及HEAD未变；本增量无正式部署/提交推送，整阶段仍开放。

联合测试实际暴露原默认 Player 不含可选求解器的部署缺口。现增加明确的 `player-null-physics` / `player-dx11-physics` 受检产品，声明 Physics ABI1.2/caps255 和许可证闭包；复制原轻量产品的相同文件，不把求解器悄悄加进默认 Player。Null 物理版只带求解器，普通 Null 使用明确去掉物理组件的测试项目，原完整物理项目在普通 Null 中必须拒绝。新增实际 apphost 搬移启动、文件 SHA、求解器篡改拒绝、Python 严格目录审计；Player 仍不得依赖 Editor/GUI/导入器。

输入证据边界：Editor 使用连续的有界验证输入流，Player 使用原 UiWindowInput/SceneUiInput 及可信测试代码显式推进一个固定步；两者均非人工 OS 输入验收。实际 CLI 对被动项目成功，对未注册交互路由的项目仍在游戏运行前拒绝。图形故障是实际已提交帧上的受控回调异常和资源借用，不是真实显卡移除。恢复后 8 帧预热、16 帧无验证读回的 Editor CPU 分配/耗时和资源计数写入 `costs.json`；这不是完整性能结论，也不提供 GPU 计时来源帧。

D 下一增量的明确余项：将 resize/minimize/恢复后的再次故障、同一产品重复 Play/Reload/Stop 与资源归零接入上述完整图/root/Jolt/交互场景；审计 Off/Low/Medium/High 和独立纯2D故障路径的交叉覆盖。既有 `SceneUiJointProducts` 已有三轮 Play/Reload/Stop 和真实视口 resize，`WindowSurface` 有最小化与恢复，但不能拿这些独立证据代替新增完整故障链。最后才冻结源码并顺序执行无 Skip Debug/Release 全门禁、受检部署/备份恢复及实际入口核验，随后提交推送整个 M7.6。

失败记录全部保留：缺失 using/单元素 params 类型推断编译失败；初次缺少物理 Player 产品；混用真实与合成输入触发 stale guard；Player UI observation 次序及未消费固定步；并行 Python 与独占 FBX 夹具冲突。修正产品打包和测试夹具，不放宽生产线程、观察代、格式、权限或原 Rendering 240 秒门限。较早候选 `702e8c9e57c94bcc846a45d8693366ef` 使用较早物理最低元数据，不能代替最终 ABI1.2 产品证据。最终 Debug Skip `57e6356204e044a1b8f072176b900431` 构建成功，联合恢复55.82秒、原 Player77.30秒通过（`out/m7-6-joint-recovery-final-debug-tests.log`）；Release 与其余最终回归尚在核验。正式 M7.5 EXE 未替换，无部署/提交推送。整阶段冻结无 Skip、人工/真实硬件/目标/自包含/完整性能/1h 等门禁保持开放。

2026-10-15 **C 原始GPU诊断增量（本增量双配置核验通过，非整阶段交付）**：新增可选只读 Renderer query18/v1（24字节函数表、48字节固定POD），保持原模块ABI1.2及既有布局。每个设备只保留第一个GPU失败：精确设备代、CPU观察帧、操作类别、原PluginResult、可选原HRESULT与实际GetDeviceRemovedReason；不复制异常文本/栈/路径，不增加World/Agent权限。超时、宿主回调失败和显式abandon不伪造HRESULT；CPU观察帧不是GPU指令因果位置或GPU计时样本来源。此记录是设备生命周期历史，不保证它就是之后无关宿主异常的原因。

实际RHI及UI/Skin/环境数值内核在原错误字符串生成前复制HRESULT；启动候选错误在释放前进入模块startup槽，新一轮获准启动清空。C#比较启动前后设备代，只给本次真实新尝试的原PluginException附加数值副本，准入前失败不得冒领旧错误。Present/readback/resize/completion/timing保留原始错误；后续StopRenderer不覆盖首个terminal分类。旧模块没有query18时诊断查询不改变原失败/原异常。Editor和Player在旧设备关闭前复制，失败重建、明确重试、新设备和最终DLL卸载均不丢失；PlayerRunner仅持有值类型结果，恢复提示只显示有界数值，原业务状态与历史不变。

真实红测 `out/m7-6-gpu-diagnostic-red.log` 复现后续cleanup覆盖DeviceLost，已修复。原completion矩阵扩展476项；新增native startup/Present受控注入及真实非法UI纹理分配检查，后者故意产生API诊断，不能标API0/0；其余正常GPU组单独要求0/0。新managed `GpuDiagnostics` 15项验证精确代/帧、owner、副本、关闭与新代不继承。扩展正式Editor/Player恢复测试覆盖诊断跨重建/关闭保留。首轮旧白盒ExecutionScope构造与signed比较/WX编译失败日志保留，未放宽断言或警告。规范Debug Skip `5ee5ed0dbe5840ea9a7fc2982ddfdad1` 构建通过（`out/m7-6-gpu-diagnostic-host-debug-build.log`）；此前 `09f281661ca14fdaa7bebb3f42fb2e7c`、`b46ab0726ca24c53bef312295bf17896` 早于最终宿主保留修改，不作为最终源码证据。最终验证结果如下，较早候选不代替最终结果。

最终规范Skip候选 Debug `5ee5ed0dbe5840ea9a7fc2982ddfdad1` / Release `31c089011b93450ca39ad28c8957e4e4` 构建0编译警告，两配置各33组定向+7组原回归+3组ABI/托管无窗口/严格场景格式全部通过。Debug分别221.31/338.68/1.38秒，Release212.79/330.13/1.16秒；完整Rendering182.77/177.85秒（原240秒不变），Editor78.99/75.43秒、Player70.31/70.09秒。两配置均包含completion476、原生诊断31、托管诊断15、正式Editor恢复36、Player恢复29检查；Python44通过1.416秒。最终本轮无新增测试失败，之前真实红测及编译/夹具失败保留。

日志 `out/m7-6-gpu-diagnostic-{host-debug-build,release-build,targeted-debug,targeted-release,targeted-debug-details,targeted-release-details,original-debug,original-release,formats-debug,formats-release,python}.log`。诊断证据 Debug `gpu-diagnostics-Debug/35ead9bffb144299bda6725d24981f44` / Release `gpu-diagnostics-Release/304b3cbab7f94555be4d7ee037f024ee`；宿主证据 `editor-graphics-recovery-{Debug/aff3aa193ba949a6bf1f7089ea9c31a3,Release/14a5757e9b734d1c85bcc5d61b889265}`、`player-graphics-recovery-{Debug/2f66af27b73842b1a6e8d15adbd2c949,Release/1e8611476b1a4fbca5f104244dd335d4}`，均在 `out/verification/m7-6/`。两配置Editor144/DX11 Player113/Null56文件逐项SHA/大小通过；选定831源码/测试/构建文件摘要前后一致 `450C8FC4C1314C25A44650D7BF841759965B4BBAC26CAD4F7F0D192C90E06FA9`，不是整阶段冻结。工作会话全部终结（最后43565退出0，Release补充3组退出0）；正式M7.5 EXE/DLL及HEAD仍为下文原值。

此增量未部署/提交推送，不关闭M7.6-C或整阶段。后续D必须验证搬移产品完整动画图/root-motion/物理/IBL/阴影/后处理/HUD故障与恢复联合，再执行整阶段冻结无Skip Debug/Release。人工、真实硬件丢失、目标/自包含、完整性能/1h以及历史间歇File.Replace调查仍开放。下文“下一步原始GPU诊断”是此前增量的历史记录。

2026-10-15 **C：真实图形 DLL 卸载后的 raw 句柄失效（本增量双配置核验通过，非整阶段交付）**：独立 `NcmaGraphicsReloadTests` 不链接任何引擎 DLL，使用绝对路径加载 Platform/Renderer/GUI/Text，每次排空资源、Shutdown、清空所有函数表、逆序 FreeLibrary，再用 GetModuleHandle 确认四个 DLL 真正卸载。只有 POD 数值保留到下一代；绝不调用已卸载代码地址。基础红测 `out/m7-6-graphics-reload-red.log` 实际复现旧四模块 context、window、renderer 被新函数表接受；旧 UI target 也越过句柄检查进入状态检查。每代实际 DX11 clear/readback/Present/API0/0，不是只比较计数器。

四个 Windows 图形模块及 Renderer 的 group/mesh/skin/texture/material/scene/environment/target/UI image/list/lease 改用完整64位 LUID，删除重载会归零的局部 ID 计数器，不修改 C ABI 布局。Windows 分配失败抛固定内部错误，由原 Guard 转换，不能回退到可重复计数器。Platform 在 GLFW 初始化/窗口创建前、GUI 在借窗口前、Renderer 在创建设备前预留 ID；其余候选由原 RAII 持有，UI target 在裸 texture 创建前预留 ID，成功入表之后才发表输出/计数/引用。Text run 的 metrics 同样在入表成功后发表。ID 分配只发生于模块/资源/租约创建，不在普通 Draw 中；但缓存UI呈现租约是逐呈现帧创建，因此该路径新增一次系统ID分配，不能称零逐帧开销或完整性能已验收，成本仍须进入D/完整性能测量。ID 不是权限、不是持久 UUID，禁止序列化。依据 [Microsoft AllocateLocallyUniqueId 合同](https://learn.microsoft.com/windows/win32/api/securitybaseapi/nf-securitybaseapi-allocatelocallyuniqueid)：本机唯一到系统重启，不宣称全球永久唯一。Windows-only helper 不进入跨平台 Physics 或公共场景合同。

扩展真实重载测试每轮保留全部旧代：旧 group/GUI/font/text run、完整 UI token、以及伪装成新 device-generation 的旧 UI image/list/target/lease value 均必须拒绝；字体为受检有界 SDK ProggyClean 复制，不搜索 OS 字体。独立 white-box 故障注入仅编入测试程序，无生产 debug 导出；覆盖 module/device/group/UI target/image/list 分配失败、输出/资源/计数/引用保持及后续正常释放。原先夹具预热 UI 破坏原测试 lazy-init 前置条件，已在证实资源归零后释放夹具空 kernel，原断言未变；日志 `out/m7-6-graphics-reload-green-tests.log` 保留。初次测试 API 成员拼写、GUI ID 赋值落入错误同名条件块导致的编译失败也保留，未忽略警告。

定向 Debug 三组通过（RendererNative、GraphicsReload、PresentationNative；6.29秒），见 `out/m7-6-graphics-reload-green-tests-second.log`。最终规范 Skip 候选 Debug `7b4f4e9e09ea4fbb96273d2c52e99077` / Release `b4077b865b66418bb2ff4b683fa09f53` 构建0编译警告，两配置各32组定向+7组原回归+3组ABI/托管无窗口/严格场景格式全部通过。Debug分别219.63/339.82/1.52秒，Release212.80/329.69/1.35秒；原Rendering182.84/178.03秒，原240秒未变；Editor79.62/75.32秒、Player70.74/69.42秒。Python44测试通过（1.108秒），两配置规范构建中的inspect也通过。日志 `out/m7-6-graphics-handles-{debug-build,release-build,targeted-debug,targeted-release,original-debug,original-release,formats-debug,formats-release,python}.log`。全部工作会话终结，最后31041退出0；diff-check通过。本次最终回归无新增产品失败，所有早期红测/夹具/编译失败保留。

两个候选Editor144/DX11 Player113/Null56文件大小与SHA逐项核验；选定827源码/测试/构建文件在两配置前后保持摘要 `5B74559B4DB0577D94E2A328EA2E8FEA3798C6ABC43205F9869BA5A735D21AEF`，不等同整阶段冻结。正式M7.5 EXE/DLL与HEAD仍保持下文原值，未部署/提交推送。此范围是 Windows 图形 DLL；Physics/独立动画和导入数值 ABI 的跨重载 ID 不在本证明内。下一步原始GPU首个HRESULT诊断保留，再做D完整搬移故障链；整阶段冻结无Skip、人工/真实device-loss/目标/自包含/全性能/1h门禁仍开放，M7.6-C/整阶段不关闭。下文“raw epoch 尚无证据”属于本增量之前的历史记录。

2026-10-15 **Closing重试与独立Player恢复（本增量双配置核验通过，非整阶段交付）**：实际提交帧故障后，对原Scene调用公开Dispose复现native Busy/Closing；正式Editor再次恢复读取Animation触发Ready检查而中断，红测 `out/m7-6-closing-recovery-red.log` 保留。恢复现只对Ready旧Scene转移动画CPU所有权，Closing/Failed旧Scene直接沿原Dispose继续，已转移的CPU状态仍保留。Edit/Play两个宿主分支新增10检查，源端33项通过（`closing-recovery-Debug/6fa0853c80504772800d034d3c9aeee4`）。测试仅反射读取真实持有者，不修改字段、不注入native结果、不反射清理；明确关闭尝试由测试直接调用组件公开API，不冒称OS故障验收。

独立Player新增同一宿主GPU重建：单次60秒精确批准绑定Play状态/World/文档revision/CPU资产/renderer代/frame，保持原window、模块、数值kernel、资产和程序库、动画clock、HUD runtime、脚本及运行服务。取消物理/HUD暂存输入，不重启游戏；用原Play只读/控制冻结范围准备新图形代，拒绝捕获重入，完成后才绑定新动画observer/输入准入。旧或新代关闭首败保留持有者，新的批准才可重试；终止关闭之后禁止恢复。累计所有已退休代的提交/验证计数，独立保留首个呈现错误，不被后续Busy或新设备覆盖。

`PlayerRunner`只有Present抛错且Play健康时调用可选可信宿主确认；默认无确认即保持原失败/关闭行为，不把输入、脚本、初始化或最终关闭错误当成可恢复呈现错误。每次失败重试都重新生成精确review并重新确认，单次故障最多16次且受原运行总时限约束；恢复等待不进入下一帧游戏delta，不重放已提交固定步。独立CLI只在非headless且没有显式ticks的交互运行中使用渲染器无关Windows默认“否”确认框；有界自动入口不弹窗。无Agent/MCP自动批准或新增权限；自动回调测试不是人工点击验收。

源端Player22检查通过 `out/verification/m7-6/player-graphics-recovery-Debug/bbcca95485f745b6a424ed40f4d371f1`：真实GPU、暂停动画精确整图/原observer继续、外线程/过期/重放/捕获控制重入、真实额外GPU租约阻止退休、新批准重试；正式Runner加载SDK-only私有状态脚本保持同Session/World/实例，拒绝/抛错/重入批准停止且可显式排空。原HUD source97通过 `player-recovery-hud-Debug/cee450b3ee1145d893b7d7fa0fe77698`，包含独立Player文字/slider/同runtime/精确最终像素/旧动作撤销/新动作一次。早期测试编译缺using/返回值错误、测试通用Exception未被预期拒绝帮助函数捕获，以及在帧内创建阻挡租约而先触发另一Busy的夹具错误均保留日志；修正为帧前创建，不修改native保护或原断言。

最终规范Skip候选 Debug `e225f6613282431cbad6b4e04702e135` / Release `4ca18a216cbf4857a0c9e6dbf9c26bcd` 均0编译警告；两配置各30组定向+7组原回归全部通过，Debug218.25/340.84秒、Release209.22/329.88秒。Rendering183.56/177.13秒，原240秒未改；Editor79.30/76.00秒。日志 `out/m7-6-player-final-{debug-build,release-build,targeted-debug,targeted-release,original-debug,original-release}.log`。最终独立Player22项证据 `out/verification/m7-6/player-graphics-recovery-Debug/5dfe1f4ace5444638b00f6bad038e406`、`player-graphics-recovery-Release/71fd98112d7d4a28b8309146c474c444`；Editor关闭/恢复33项证据 `editor-graphics-recovery-Debug/e0e06c4cb1e54c918699a141c9f4baa9`、`editor-graphics-recovery-Release/5f946c7a804a47b69565368a7e19f12b`。原HUD97、Play29、状态55及选定原生/联合/宿主回归包含在上述通过组合中。

两个候选Editor144/DX11 Player113/Null56文件大小与SHA逐项核验一致；双配置前后所选824源码/测试/构建文件摘要为 `1082F028D59FD6962040AADD9EDF3CF6A618DE2D485EB270E54F37B75F36501B`，不等同整阶段冻结。全部工作会话终结，最后59859退出0，diff-check通过。正式M7.5 EXE/DLL及HEAD保持下文原值，未整阶段无Skip/部署/提交/推送。本轮最终回归无新失败，所有红测和既有保存/分配/超时调查保留；不放宽原断言、保存策略或超时。

下一步：原始GPU诊断、跨DLL raw epoch及D完整动画图/root-motion/物理/IBL/后处理/HUD搬移故障联合。只读检查已定位 `RendererPlugin.cpp` 的 `nextModule/nextRenderer/nextGroup/nextMesh/nextResource` 是从1开始的DLL静态计数；尚无实际卸载/重载后新函数表拒绝旧raw token的证据，须先建立真实红测，绝不调用已卸载DLL的旧函数指针，也不能用同一加载模块的新设备代替跨DLL证明。人工/真实设备丢失/用户素材/目标/自包含/完整性能/1h继续开放。下面上一增量的独立Player未接入/Closing未修复是历史状态，不当作当前结论；M7.6/M7.7不关闭。

2026-10-15 **Play恢复后Undo所有权补齐（本增量最终核验，非整阶段交付）**：修复两条不同的失效引用路径。释放已转移动画的presentation借用时只验证owner/借用计数，不再要求旧World身份仍有效；否则Undo正常换代后无法关闭旧GPU场景。首次恢复若紧接Undo、旧场景尚未同步，则先比较Scene World与CPU资产publication，失配时正常退休旧动画，不将它借用为可保留状态；原身份不变时仍保留动画clock。没有放宽动画读取、外来World借用或提前销毁限制。

第一条修复的54项检查通过，日志 `out/m7-6-play-recovery-undo-first.log`；随后新增“首次恢复前Undo”用例真实复现 `BorrowForRecovery/World identity invalidated`，红测 `out/m7-6-first-undo-recovery-red.log` 保留。第二条修复后55项实际DX11状态检查通过，证据 `out/verification/m7-6/first-undo-recovery-Debug/3ecc110c51964eb1b705b35cda2da4a6`。先前Debug29/7通过和Release候选 `5d9310e36c224b1db01a364f5468232e` 早于本修复，不能当作最终源码证据。

最终规范Skip候选 Debug `6d4a70b42d4a410faa6a3070d5dd5534` / Release `8b597b3f2821489aa866a50d927cef21` 构建0编译警告；两配置各29组定向与7组原回归全部通过，Debug209.73/341.17秒、Release201.90/330.90秒。Rendering184.26/178.01秒，原240秒门限未变；Editor79.47/75.99秒。55项状态保留的最终证据为 `out/verification/m7-6/presentation-state-Debug/acf83c3ec5f9442b8590af48fc88be4c` 与 `presentation-state-Release/29da01a6bc254e809eb9edd923776842`；正式Play恢复29项为 `play-graphics-recovery-Debug/1e497020300e4e309c4beae3db4e5302` 与 `play-graphics-recovery-Release/0684027f28bd40db8c37e3e30b3a8e8c`。原HUD交互89、CPU呈现恢复22及全部选定原联合/宿主回归包含在上述组合中。日志 `out/m7-6-play-final-{debug-build,release-build,targeted-debug,targeted-release,original-debug,original-release}.log`。

两配置Editor144/DX11 Player113/Null56文件大小及SHA逐项核对一致；源码/测试/构建入口所选822文件摘要在双配置前后保持 `8B8032A00D36AB0E2DBA70BECB5319EDB464DC61D9B8F5615468E1099D34DDC3`，这不是整阶段冻结无Skip验收。`git diff --check`通过，全部本轮工作会话终结，最终24244退出0。正式M7.5 EXE/DLL仍为下文原hash，HEAD不变；无部署/提交/推送。此次最终回归无新增失败；本轮红测和历史File.Replace/分配/超时调查仍保留，未修改保存策略、既有断言或超时。

下一步仍在C：优先补“旧Scene部分关闭失败后的再次恢复”组合审查，不能在Closing场景上读取仅Ready可用的Animation属性；现有独立viewport lease阻塞退休测试不等于该路径已覆盖。随后完成独立Player同会话恢复、完整原始故障诊断与跨DLL raw句柄代审查，再做D动画图/root-motion/物理/IBL/后处理/HUD搬移故障联合及整阶段冻结无Skip门禁。人工确认框/真实设备丢失/用户素材/目标环境/自包含/全性能/1h保持开放，M7.6/M7.7不关闭。

2026-10-15 **Play与CPU呈现状态保留增量（进行中，未提交）**：正式恢复review扩展绑定精确PlayStatus（Session/World/状态/帧数/tick/模拟债务/丢帧计数）、Play文档revision和资产身份；只允许Running/Paused，Faulted/Stopped/草稿/不匹配Edit冻结状态拒绝。新增纯C# Play呈现恢复只读范围：所有控制重入均拒绝且捕获拒绝会毒化外层准备；同时保持World只读，离开范围后才能完成首次动画observer注册。输入撤销只清除物理/HUD待消费观察，不调用Pause/Resume，不清模拟accumulator，不重启Behaviour。正式确认文字说明Play状态和已提交tick。退休失败保留原CPU状态，重新review/确认才能重试。

本轮补充记录：首次规范Debug `4af427eb8f5d4b59940df1c011cfef5d` 29组定向中28通过204.62秒；原 `PlayerRunnerLifetimeTests:54` 的Save/File.Replace报“无法删除要被替换的文件”，未进入新增Editor恢复路径，日志 `out/m7-6-play-recovery-targeted-debug.log`。失败原件 `out/verification/m7-6/player-runner-lifetime-Debug/e8b29aca45514507b021f8b6567b5fa1/scene-fixture/637c738a093646b6a53508651b570d6d/start.ncmascene` 保留，2013B/SHA `78D32986573B4F541FC1CD51438BBEC211112F8A0705DA1A661EC8FE04A460AE`，进程退出后JSON/独占读通过，根因仍未证明。未改原子保存/断言/重试策略。新增两个专项50/29及原GPU23均首轮规范通过。随后仅加强HUD测试：三轮故障前零delta排队点击恢复后不得消费、新点击必须到达原脚本恰好一次；为新增的成功点击精确调整后续旧取消断言的X计数2→3，其余语义不变。新89项Debug通过 `out/m7-6-play-hud-recovery-third.log`，证据 `play-hud-recovery-Debug/c50673cab4914987ba727ebba794d1ec`。最终规范Debug候选与重跑中；不将部分定向成功当作全部门禁通过。

`SceneAnimationSession` 的CPU时钟、姿态/clip/rig和资产pin可被显式保留；旧Scene转为单一借用者，成功释放后新GPU Scene以精确World/资产/数值kernel/Play/root-motion/Animator身份借用，禁止提前销毁、并发借用和外来身份。正式Editor同时保留Edit预览与Play动画；真正编辑换代、Stop/Reload、最终关闭仍按原所有权顺序释放。`SceneUiPresentation.RetireGraphics`只释放画布/GPU表面，保留原UiRuntime和精确原包，撤销旧输入generation；重新创建画布不丢文本、滑块值等CPU状态，不重新注册游戏路由。原SceneWorld/历史/脚本/求解器所有者不变，C++ ABI/Shader/默认画质无变化。

首轮新CPU22通过；真实DX11状态保留50检查通过（`out/verification/m7-6/presentation-state-Debug/28bc91e633b649b0a319ba4dee9d0444`，早于最后Play准入/待绑定保护）：合成模型及真实ASCII/Binary FBX、非静止clip、36已提交步、精确GPU顶点/整图、新代同clock及单步继续；独立纯2D保留文本/slider/原runtime、拒绝相同字节的另一包身份和旧输入。正式Running/Paused循环29检查通过（`play-graphics-recovery-Debug/e5f78a1866fd49f9a6b23c0c3d9b80c3`），含真实C# SDK脚本私有ticks/OnEnable计数、stale批准、捕获Play.Pause拒绝、失败新代明确重试、真实GPU lease阻止退休、600秒模拟确认等待不变为游戏delta、Paused精确最终像素、原Stop与pin排空。原同会话GPU23通过；交互HUD增加三轮原runtime/文本/整图检查后83通过（`play-hud-recovery-Debug/45d6dbe3c7ca48b9a18509d3248cc36e`）。全部为受控故障与脚本确认，不是硬件拔卡/人工确认框。

失败证据保留：首次遗漏namespace/nullable编译保护、导入插件路径错误、误选FBX静态Base片段、复用单次import上下文、UI顺序重复、测试脚本同阶段两个整Transform暂存写相互覆盖、旧测试在新增重建后仍读取已释放renderer，以及HUD测试在状态断言前多执行了一次零delta游戏帧。修正夹具/调用时序而不改导入/游戏提交/原子保存/原断言门限；原临时“所有Play拒绝”测试现在验证stale Play拒绝和精确活跃Play保留，不是删除失败保护。最终规范Debug候选与相关原回归正在核验，Release尚待本增量最终构建验证。日志 `out/m7-6-{presentation-state-*,play-recovery-*,play-hud-recovery-*}`。前序“Play尚未接入”仅指前序增量；本轮不等于全部动画图/root-motion/物理/HUD/后处理联合恢复和D搬移链已验收。整阶段冻结无Skip、部署、提交推送仍未执行，正式M7.5保持原样，全部人工/目标/自包含/性能/1h与历史失败继续开放。

2026-10-15 **正式Edit恢复循环增量最终核验**：规范Skip候选 Debug `3ce1819a060b4e688155e48ce88969a6` / Release `df5794b64fc54243a377e24b05722d9f` 均成功，0编译警告。两配置各27组定向及6组原完整回归全部首次通过：Debug202.45/338.99秒，Release185.59/335.06秒；完整Rendering183.25/181.79秒（原240秒门限不变）、Editor79.14/75.40秒、Player70.54/71.29秒。本轮没有新增测试失败，但历史File.Replace/分配等调查不因此关闭。CPU新增39、实际正式共用恢复循环39、原同会话GPU重建23、应用生命周期及全部选定原联合/宿主回归通过。最终GPU循环证据：`out/verification/m7-6/editor-recovery-loop-Debug/98261a077da74c2bb94ad0c56d0abf8a`、`editor-recovery-loop-Release/0e404ec65e8b4c32afc6a8befe63d262`；报告保留 `scriptedConfirmation=true`、`manualWindowsDialog=false`、`physicalDeviceLoss=false`、`playRecovery=false`、`wholeStageAccepted=false`。

最终日志：`out/m7-6-recovery-loop-{debug-build,release-build,targeted-debug,targeted-release,original-debug,original-release}.log`。两配置Editor144、DX11 Player113、Null56文件SHA与大小逐项核验通过。所有本轮构建/测试会话已结束，最后73102退出0。正式M7.5 EXE/DLL仍为下方记录的原hash；HEAD仍 `a25926067d72a89ca731f6444add0e0c04210491`。**未执行整阶段冻结无Skip、未部署、未提交推送**；下面的“构建/回归待核验”是本轮早期记录，以此最终结果为准。后续仍需Play状态保持、完整原始图形故障诊断、跨DLL raw句柄代、D搬移完整故障链与最终整阶段门禁；人工/目标/自包含/完整性能/1h及历史失败保持开放，M7.6/M7.7不关闭。

2026-10-15 **正式Edit恢复循环增量（进行中，未提交）**：`ApplicationLifetime` 记录精确故障票据、所属管线和帧阶段；仅仍持有的同一服务在BeginPresentation/Present失败后可获准恢复。Clock/Platform/EditorIntents/Agent/Input/Gameplay/启动/终止关闭失败均不可借图形恢复恢复执行。可信恢复操作必须匹配同一管线、完成重建并确认Ready；捕获重入也使操作失败。首因与首次恢复失败使用独立固定诊断保留，不保存异常对象或私有路径。全应用帧号跨Run单调递增，失败帧不重放，每次恢复后重建时钟基线，确认等待时间不进入游戏delta。

正式Editor入口与自动探针共用 `EditorRunLoop`。可恢复的Edit呈现故障退出帧循环后，使用不依赖故障Renderer/ImGui的Windows系统确认框；默认“否”，清楚说明“是”会丢弃未完成图形帧并重建GPU、“否”退出且未保存修改不会自动保存。每次失败重试都重新获取精确单次60秒review并重新询问；无自动批准CLI/MCP入口。`--smoke-test`/`--preview`不弹框、不自动恢复。运行中Play/草稿/冻结/终止关闭继续拒绝；Play恢复仍未实现。此处系统对话框代码接入不等于真实窗口人工验收。

首轮CPU新增39检查、实际DX11恢复循环新增39检查均通过：Debug `out/verification/m7-6/editor-recovery-loop-Debug/434700374bde4cf9b13f54178c2bf190`，Release `editor-recovery-loop-Release/3dd72893dd1842278b9ded3e7d553245`。覆盖已提交GPU帧后故障、同World/原历史/CPU pin/精确最终像素、独立GPU lease阻止退休后新批准重试、无确认/拒绝/确认异常保持停止、帧号连续和API0/0；自动确认采用脚本回调，不冒称真实Windows点击或物理device-loss。源码定向构建0警告；最终规范双配置候选与原回归待下面结果记录。日志 `out/m7-6-scheduling-cpu-first.log`、`out/m7-6-recovery-loop-{first,release-first}.log`。本段仅更新前序“无失败调度恢复/可见入口”缺口；Play、跨DLL raw句柄代、完整搬移故障链、最终整阶段无Skip及原人工/目标/完整性能/1h仍开放。正式M7.5不替换，未提交推送。

2026-10-15 **Edit同会话重建增量最终核验**：最终规范Skip候选 Debug `6a4710c1291d4abb9d70ff1e336cb7ab` / Release `e143ee4a62df4a538b1cb69bf7b8a7d8` 均包含整体关闭后禁止恢复的最后保护，构建无新编译警告。最终Debug26组定向全部通过221.34秒；Release26组首轮24通过，2个原保存失败组同源码单独复测通过9.91秒，原6组完整回归全部通过376.29秒（Rendering208.20/Editor79.35/Player80.82）。较早Debug原6组403.96秒通过，但二进制早于最后关闭保护；最终Debug26组已补验这项保护，**不是宣称重新执行过最终Debug全套或整阶段无Skip**。新增实际同World/历史/DX1123项均通过：Debug证据 `out/verification/m7-6/editor-graphics-recovery-Debug/cec3cf834b804d81929554e98ab56d26`，Release `editor-graphics-recovery-Release/0025e40153dd4e99a0478283c7e7041e`；原HUD发布加入4项重建/作者实例/Undo/Redo检查后，最终Debug source+product409通过。原应用生命周期22项包含新的终止关闭拒绝检查。

最终日志：`out/m7-6-edit-rebuild-{debug-build-final,release-build,targeted-debug-final,targeted-release,release-save-recheck,original-debug,original-release}.log`。Debug/Release Editor144、DX11 Player113、Null56文件均逐项核验SHA与大小；`git diff --check`通过。所有本轮工具/构建/测试会话已终结，最后98324退出0。正式M7.5 EXE `5487DF87B13019C24AF2E19C6EB1DB767E04E950AA47ED32E6C7A76619068C76` / DLL `B4AD9CDC660ABF4E4AF416AC6241EF427ABDC8783D3F4FC0E1112ADB2C814D6A` 未变；HEAD仍 `a25926067d72a89ca731f6444add0e0c04210491`。下面的进行中/复测记录作为证据保留，不以复测通过宣称File.Replace根因解决。未冻结整阶段、未部署、未提交推送；下一步为正式可见恢复与失败调度边界、Play状态保留、跨DLL raw句柄代审查及D完整搬移故障链。M7.6/M7.7和全部人工/目标环境/完整性能/一小时门禁保持开放。

2026-10-15 C同会话Edit图形重建增量（进行中、未提交）：`CandidatePresentation` 新增可信宿主的单次60秒精确review，绑定Session/World/Revision/DocumentGeneration/资产身份/renderer代/frame；只允许离帧空闲Edit，拒绝Play、编辑草稿、foreign thread、陈旧或重复批准。默认拒绝未完成帧；只有明确批准才调用原终止帧操作，不Present故障帧。退休与新设备准备分别持有所有字段，首败即停，失败后阻止正常pump/input/渲染，必须新review重试或有序关闭。新设备保留加载中的模块、platform窗口、原World/Document/EditSession、历史/作者参与者、CPU场景/HUD/Shader/post/shadow/font引用；只重建GUI/Renderer/GPU派生预览和场景。检查能力只注册一次，通过当前renderer提供器读取；旧托管GPU对象拒绝访问。第一故障仅保存固定代码，不保存异常/私有路径。

定向Debug实际DX1123项已通过（`out/m7-6-edit-rebuild-fifth.log`，证据 `out/verification/m7-6/editor-graphics-recovery-Debug/3bd55b921db040cdac97c47d46a2fcc5`）：同一实例/原Undo与Redo、精确最终像素、原检查提供器、新设备代、旧对象失效、真实提交后故障与明确终止、实际独立GPU目标引用阻止退休、释放引用后明确重试、caught-reentry毒化及保留部分新代。此资源引用拒绝由现有托管lease保护触发，**不是native Busy或物理device loss**。原HUD发布测试另增4项同会话重建/相同作者与发布参与者/原Undo-Redo像素检查，待下面最终回归核验。首次空Shader开发入口空引用、测试类型名/返回类型/字段编译错误、首轮规范build原inspection测试调用未迁移、错误预期native Busy的测试失败日志均保留；未放宽任何原断言。

本增量仍是实际Candidate宿主API，**没有GUI恢复按钮、失败ApplicationLifetime原地恢复调度、运行中Play重建或物理拔卡验收**。可信C#扩展不是沙箱；对任意扩展私有IO/状态无回滚承诺。保留原World只指重建操作本身；Undo/Redo仍按原合同恢复快照并使运行引用失效。模块不卸载，因此同加载模块的新device代检查不能替代跨DLL卸载的raw token epoch审查。完整环境/高阴影/Bloom/动画/HUD/纯2D的搬移故障联合、最终整阶段冻结无Skip及人工/目标环境/全性能/一小时门禁仍待完成；正式M7.5不替换，不提交推送。Debug/Release规范候选及回归正在执行，不能用此23项定向结果宣称M7.6完成。

增量复核补充：整体Dispose开始即设独立终止关闭标记并撤销review，即使整体关闭失败而Editor业务尚在，也不得转回GPU重建；该路径只允许原关闭重试/明确终止帧。实际EditorLifetime故障测试新增此拒绝检查。Debug第一轮26项定向中25通过，原PostAuthorHosts在Editor启动前的Player场景改写 `File.Replace` 失败（`out/m7-6-edit-rebuild-targeted-debug.log`）；失败文件 `out/verification/m2/post-author-Debug/6109ec330f584aa780c21850188f887c/scene-fixture/26f792f8ef2143fcb4a08835e3d1cf8a/start.ncmascene` 2590B、SHA `B2FB27B4F0C9033CB5C45D787D3A11FAC56A5CB876BEFC67F4371DAEFD5D6BD5`，复核JSON与独占读通过。未改原子保存/重试/断言；相同二进制第二轮26项全部通过227.20秒，日志 `out/m7-6-edit-rebuild-targeted-debug-second.log`。该Debug候选 `ad62c556c9234c1581f0822b0a815786` 及其回归尚不包含后来加入的整体关闭标记，最终候选与回归须补验；不要混称同一最终源码。间歇保存失败根因继续开放。

最终源码Release候选 `e143ee4a62df4a538b1cb69bf7b8a7d8` 已规范Skip构建，包含终止关闭保护；Editor144/DX11 Player113/Null56文件SHA与大小逐项通过。首轮26组定向24通过203.76秒：PostAuthorHosts:50及PlayerRunnerLifetime:26均在原 `SceneDocumentFiles.Save/File.Replace` 报相同IOException（`out/m7-6-edit-rebuild-targeted-release.log`），新23项恢复与实际Editor生命周期22项均通过。两个原失败组不改源码单独复测2/2通过9.91秒（`out/m7-6-edit-rebuild-release-save-recheck.log`），不能称间歇根因已修复。失败场景原件保留且JSON/独占读有效：PostAuthor `out/verification/m2/post-author-Release/00386e0a03d2478ea9570ce2e08c928e/scene-fixture/a2168eb94c98439399350dc7d54505a3/start.ncmascene` 2168B/SHA `BA478D7AD9774B2A5A29C86A473CDC645C915F702AEFD99857469E1A436606CA`；Runner `out/verification/m7-6/player-runner-lifetime-Release/c9de60a232304c87abfb53d571a5315e/scene-fixture/845d4541031048fb8ff6c8b9ff288789/start.ncmascene` 2013B/SHA `D161C7A7ACC655914D30E3604F199A5CB09DFB4F64A9B16954FDE19CAEEAD546`。较早Debug6原完整组通过403.96秒，Rendering224.49/Editor86.03/Player85.30；Release原6组与最终Debug规范候选/定向正在执行，查看当前日志，尚不宣称最终全部通过。

2026-10-15 应用生命周期增量最终核验：规范Skip候选 Debug `019126350bde4e64bcd4007d0f73ebe9` / Release `67856e7e89ec446eac6076d1a0020378` 构建成功，无新代码警告。各25组定向及6组原完整回归均通过：Debug218.31/398.44秒，Release204.08/368.59秒；完整Rendering221.78/201.12秒（原240秒不变），Editor85.04/80.04秒，Player84.69/79.87秒。原HUD搬移包拒绝测试及原SceneRendering缺失插件入口诊断均通过，原断言未改。CPU新增22、实际Editor新增22、原Application17及UI路由9组也通过。两个配置的Editor144/DX11 Player113/Null56清单SHA和大小逐项核验；正式M7.5 EXE/DLL hash未变。最后工作会话82248返回0，所有本增量构建/测试会话已终结。

最终日志：`out/m7-6-lifetime-debug-build-third.log`、`out/m7-6-lifetime-release-build.log`、`out/m7-6-lifetime-targeted-debug-second.log`、`out/m7-6-lifetime-original-debug.log`、`out/m7-6-lifetime-targeted-release.log`、`out/m7-6-lifetime-original-release.log`。实际Editor证据：`out/verification/m7-6/editor-lifetime-Debug/2ba80f18cb56497f83cf6d319264f6dd` 与 `editor-lifetime-Release/b9c5d8ee962e45c49684644bace3f46e`。下面的逐次进行中记录保留为失败/修复历史，当前结果以本段为准。没有整阶段冻结无Skip/部署/提交推送，M7.6不关闭；下一步拆分GPU与编辑业务生命周期，验证保留原World/文档/Undo的获准GPU新代重建、启动诊断与跨DLL重载的句柄代边界，再做D搬移故障联合。新应用重启的正确画面不能替代同会话重建或真实硬件丢失恢复。

2026-10-14 C应用最外层生命周期增量（进行中、未提交）：红测 `out/m7-6-application-lifetime-red.log` 复现失败服务关闭后仍释放前置依赖并清空持有者。ApplicationLifetime现明确启动列表为依赖顺序，逆序关闭在首个失败停止，只有Dispose成功才移除该服务；失败服务及前置依赖保持同一有界列表（最多64），只能owner显式重试关闭，不重启失败生命周期。新增保留数量/服务、关闭尝试次数及独立ShutdownFailure；初始initialize/frame/首次shutdown Failure不被后续关闭错误覆盖，correlation保留，诊断复制且服务名最多256字符，不保留异常对象或私有回调文本。

原“关闭失败仍释放全部服务”测试断言对应不安全旧语义，已明确改为首败停止且保留依赖的断言，并保留原成功逆序释放/部分初始化/线程/重入/帧顺序/无原生依赖检查；不是删除失败断言来通过测试。新增CPU22检查通过。首次生产写法的非空List集合表达式被编译器展开为CollectionsMarshal，触发原无Interop依赖断言，改用显式List初始化后该原断言通过，未豁免依赖边界。红测与此失败进程抛出未处理异常后未自行结束，只中断了对应已知工具会话并确认终结；没有更改系统调试/安全设置或终止其他dotnet进程。日志 `out/m7-6-application-lifetime-{red,debug-second,debug-third}.log` 保留；初次新测试漏捕获ArgumentException也保留。

正式Editor Main不再通过using/finally隐式重复关闭：保存唯一进程持有者，失败关闭不清空、不自动重试，正常关闭后才释放；帧/初始化首因输出受控代码。CandidatePresentation新增可信内部终止未完成帧操作，仅允许在关闭已失败的Closing状态使用query17，不Present、不复活renderer、不增加Agent入口。新EditorLifetime22检查在实际ApplicationLifetime.Start/Run/Dispose和真实Editor/DX11上通过：普通完整帧捕获回调失败→实际原生Busy→保留CPU pin/整个服务→线程拒绝/首因保留→显式终止帧→正常重试归零→新应用重新准入并恢复精确视口；另测实际可信启动factory异常与原生Busy双失败。反射仅读取已存在持有者，不附加替换或反射清理。初次辅助函数没处理Aggregate及纯清屏帧可安全关闭的夹具假设错误已修正并保留日志；原生规则没有修改。最终定向证据 `out/m7-6-editor-lifetime-third.log`，`out/verification/m7-6/editor-lifetime-third/f0e41e060a1f43508b2b79ede5438728`。

首轮规范Debug候选 `27f71007210a4d76b476ae64fa776c8d` 完成，25组定向24组通过、原SceneUiRelocatedInteraction失败：通用生命周期代码遮住原“需要可信startup routes”的明确拒绝原因。保留 `out/m7-6-lifetime-targeted-debug.log`，原测试不改。现用固定消息的sealed `SceneUiRoutesRequiredException` 表达同一准入拒绝；Editor只透传已知安全固定诊断，任意回调文本仍输出受控生命周期代码，交互权限检查不变。第二规范候选 `ad736b76a56d4c65b8f483bc03e771f0` 后，先定向执行原SceneRendering检查又复现缺失插件根目录的固定诊断被遮住（`out/m7-6-lifetime-plugin-diagnostic-red.log`）；将原PluginLoader固定字符串公开为常量，Editor仅精确匹配并返回这个固定值，不透传任意ArgumentException文本。原断言与输入拒绝不变。正在第三次规范Debug构建并重跑，原完整/Release尚未完成。

上述22/22不是整阶段验收。此新应用证明不是保留原World/历史的同一编辑会话GPU重建，也不是物理device-loss或CLI故障注入验收；正式新设备代重建、完整启动诊断/搬移故障链和最终冻结无Skip门禁仍待完成。正式M7.5不替换、不提交推送。下方上一窗口增量最终结果仅覆盖当时源码。

2026-10-14 C共享窗口策略增量（进行中、未提交）：新增纯C# `RendererWindowSurface`，Editor/Player共用 Ready/Suspended/Faulted/Closing/Closed、精确renderer代、已提交/请求尺寸与有界值诊断。最小化/零framebuffer不Resize到零、不Submit/Present、不销毁旧视口；恢复正常尺寸才继续绘制，隐藏时取消HUD提案并屏蔽物理游戏输入，不强制暂停游戏调度。普通尺寸错误仅允许可信owner显式离帧恢复，原生失败/终止代必须完整关闭后新建，不能复活。首个错误保留原生结果/操作/帧和代，不保留异常对象、回调消息或私有路径；关闭重试不覆盖原因。此记录针对窗口准备/Present，尚不覆盖所有启动阶段故障。

实际Editor红测证明最小化曾销毁旧viewport；修改后保留资源。另用真实GUI image pin令旧viewport退休返回Busy，Editor现在同时保留旧target和最多一个pending target，拒绝覆盖；正常关闭先释放GUI pin再关闭两者，不用反射附加所有者或强制卸载。新WindowSurface69项通过（真实DX11奇数尺寸、零分配暖态、old/new generation、纯2D零3D资源、Editor与Player/API0/0）；窗口观察由既有内部测试入口提供，不等于人工OS窗口验收。首次测试stamp错误与nullable编译失败均保留，未放松产品验证。新增Player完整Run生命周期46项和原HUD交互扩展74项通过：首个呈现故障跨Busy/终止关闭保留，新Run清空；游戏关闭故障不冒充呈现故障；隐藏/零尺寸取消积压HUD输入。

本窗口增量最终核验：规范Skip Debug `1663dbf7e60a4ab5982985d3f7faf450` / Release `b52205709d794c6bacb31e267362e8c5` 均构建成功，无新代码警告。每个配置21组定向与6组原完整回归全部通过：Debug定向208.80秒/原回归393.17秒，Release定向199.52秒/原回归389.25秒；完整Rendering分别215.21/218.02秒，原240秒门限不变；Editor85.03/80.26秒，Player85.55/82.99秒。所有本增量工作会话终结，最后91823返回0。日志 `out/m7-6-window-{debug-build,release-build,targeted-debug,original-debug,targeted-release,original-release}.log`；更早首轮窗口/runner/HUD及全部失败日志保留。两个候选的Editor144/DX11 Player113/Null56文件均逐项核对manifest SHA和大小。

窗口69证据：Debug `out/verification/m7-6/window-surface-Debug/2c468fb459c14f898361e881c3b7b09c`、Release `window-surface-Release/1d575417837c4bf3a6fd0dc349de9980`；runner46证据为同目录族的 `player-runner-lifetime-Debug/8076adc4b8864250b0e29f1304eb9fea` / `player-runner-lifetime-Release/5caaa5e9a8774b8c9000ac7d9c97c3e1`。原HUD74及严格搬移交互/发布/完整场景阴影联合均在定向组内通过。正式M7.5 EXE/DLL hash不变。C正式宿主新代重建/完整启动诊断、D搬移故障联合与最终冻结无Skip整阶段门禁仍开放，不提交推送、不部署、不进入M7.7。后面的上一增量“最终核验”只覆盖当时源码，不替代本次回归。

窗口增量曾记录的 `ApplicationLifetime.Release` 丢失持有者/覆盖首因缺口，现已由上述应用生命周期增量修复并完成双配置验证。实际ApplicationLifetime+Editor链路证据与旧WindowSurface类级探针分别记录；仍不能把进程退出、新应用重启或反射替换持有者视为保留原World/历史的同会话GPU恢复。

2026-10-14 本增量最终核验：规范Skip候选 Debug `389c831caddc42c19ff8f6a68dfa52e9` / Release `d2227f23e6dd41a88331b5081791d140` 构建成功，无新代码警告。Debug原完整Rendering225.65秒+原生/kernel通过，16组定向188.54秒、其余4组原回归184.14秒通过（Editor87.57/Player89.08/UI/SceneRendering）；Release16组定向190.16秒、6组原回归396.97秒全部通过（Rendering226.39/Editor79.63/Player83.07），原240秒门限与32轮生命周期不变。两配置125项烘焙检查/冻结数值hash及原独立积分和图像oracle通过，参考图像误差0、API0/0。最终日志 `out/m7-6-completion-cook-{original-debug-third,targeted-debug-third,remaining-debug-third,targeted-release,original-release}.log`。两配置Editor144/DX11 Player113/Null56文件的manifest SHA与大小均逐项核对；所有工作测试进程已结束，最后session27123返回0。正式M7.5 EXE/DLL哈希未变、HEAD仍为 `a25926067d72a89ca731f6444add0e0c04210491`。本增量未部署、未提交推送，未运行整阶段冻结无Skip门禁；C共享resize/minimize/新设备代重建/原始故障诊断保留及D搬移故障联合仍待实现，M7.6不关闭。以下逐次进行中记录按此最终结果更新理解；此前所有失败与人工/目标/完整性能等开放项仍保留。

2026-10-14 验证收尾/超时调查：最终Debug定向16组先通过（152.46秒），但原完整Rendering连续触及原240秒门限；原Editor/Player/UI/SceneRendering/kernel组通过。保留 `out/m7-6-completion-original-debug.log`、`out/m7-6-completion-original-debug-second.log`、`out/m7-6-completion-phase-diagnostic-debug.log` 及两次 `completion-cook-original-debug[-second].log` 超时失败，不以重跑覆盖。仅新增阶段计时点，不减少32轮生命周期、采样、预热、断言或延长deadline。原生离线EnvironmentCook内层重复切线基/采样方向计算改为逐texel基、逐mip/逐LUT行采样预计算，同一临时vector最多2048个Vector3d，无持久cache/额外GPU资源；RGB双线性坐标只解析一次，以相同表达式分组累积，LUT省去未使用的反射x/y及零分量。仍使用Eigen向量存储/基变换，不改SDK、采样或算法版本。新增优化前Debug/Release一致的constant/directional全部数值payload SHA256冻结断言（排除UUID元数据，不排除任一float），原独立积分/完整图像oracle保留。

第三轮规范Debug候选 `389c831caddc42c19ff8f6a68dfa52e9`：125项烘焙检查（含两份冻结hash）通过，原生完整故障测试5.81秒通过，原完整Rendering **225.65秒通过全部32轮**，240秒门限未变。日志 `out/m7-6-completion-cook-{debug-build-third,parity-debug-third,original-debug-third}.log`。同一完整测试的局部测量：环境烘焙11.764→2.226秒、环境图像44.047→8.513秒；这些是本机本轮观测，不是跨机器性能保证，也不证明所有历史耗时波动根因已解决。默认neutral预设字节hash保持 `96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665`。剩余Debug定向/原宿主与Release候选/回归正在执行，尚非整阶段冻结无Skip验收；不关闭M7.6、不提交推送或部署。历史4096B分配/File.Replace、人工/目标/完整性能/1h等门禁继续保留。

device generation/opaque handle/caller-owned POD/线程/lease/释放/失效契约完整；旧ABI冻结，必要扩展用新query。
texture/material/shader/target候选完整验证后安装；安全边界退役，失败保留有效旧配置或明确fail-stop。
resize/minimize/恢复/重建、过期handles、借用关闭失败、异常创建、真实device-loss与注入失败分开证明。
不能用模拟异常声称真实硬件丢失恢复；不force unload有lease的插件。

执行顺序（基线 `a25926067d72a89ca731f6444add0e0c04210491`，四项IDE设置保留）：

- A 资源准备与身份边界：逐项审计renderer/device generation、资源opaque handle、缓存UUID/generation/hash和owner-thread；针对texture/material/shader/target候选的回调、重入、撤销、借用关闭与失败路径补真实回归。先修材质resolver期间缓存可关闭/重入的缺口；不能让回调捕获拒绝异常后继续发布候选。保持正常material-set内部组合和旧代租约，失败不切换场景引用。
- B 更新/交换/退役：C#拥有明确的候选准备、批准、安装、退役状态；完整验证texture/material/shader/target及依赖闭包后，在离帧安全边界交换。候选失败保留旧代，旧代关闭失败保留可重试所有权并明确fail-stop；不得force-unload、吞异常或伪造回滚。原命令/事务仍是场景和Agent唯一可写入口。
- C 正式宿主恢复：Editor/Player共用尺寸、最小化、暂停提交和恢复政策；正常resize与设备失败分开。错误诊断须区分普通无效输入、资源创建失败、实际device-removed、shutdown timeout；恢复新renderer generation后旧handle不可重用，必须重新准入已批准CPU资产/程序，不能直接复用失效GPU对象。C++仅负责数值/GPU执行和真实错误报告，必要扩展使用独立query，不改旧ABI布局。真实设备丢失无证据时保持该项待验收。
- D 联合验收：实际DX11旧画面/新画面对照、失败后旧画面或明确fail-stop；default/stage/full、PBR/IBL/四档阴影/Bloom/HUD、ASCII/Binary FBX动画及纯2D独立路径；Editor/Player/Null受检搬移、resize/minimize/rebuild、旧句柄/关闭失败、重复启停和资源归零。注入失败只能证明对应故障路径，不能代替真实硬件device-loss。保持原测试/门限，最终冻结源码完整顺序无Skip Debug/Release、manifest/恢复备份/实际apphost核验后再提交推送整个M7.6。

当前A已完成三轮准备/所有权边界增量，B正在实现有界交换与失败保留；C/D、真实设备丢失恢复、阶段完整双配置门禁和交付均未完成。既有M7.5正式Release不因定向测试被替换。

2026-10-14 A首增量：新增实际DX11 `ResourceUpdateSafety`。首次红测确认resolver内`Trim`未被拒绝（`out/m7-6-resource-safety-red-test.log`）；修复`RenderResourceCache`的material/texture描述resolver、material-set resolver及environment批准回调边界，owner-thread重入即拒绝，回调捕获拒绝也使外层候选失效，finally清理守卫以允许显式重试，内部合法material-set组合保留。Debug扩展19检查通过：旧材质整图精确保持、新旧generation共存、旧租约保留、首次/最终环境批准拒绝不分配、cache-hit再次批准及GPU texel保持、最终资源归零/API0/0。日志 `out/m7-6-resource-safety-expanded-debug.log`，证据 `out/verification/m7-6/expanded-debug/2511b384434445979c86ad8ba2166c73`。这不意味着任意可信回调的外部副作用能回滚，也未解决所有原始GPU对象直接Dispose的所有权问题；后者仍在A审计范围。规范Debug Skip候选`bee82057873e498cbbe0e390f671e2e9`完成，不部署；双配置定向回归和Release候选仍需核对最终结果。本增量不单独提交、不称M7.6完整交付。

A首增量最终定向结果：规范Release Skip候选`459ac6502af743ecbc0aaae4ac9d718a`完成。Debug/Release各五组CTest（新增ResourceUpdateSafety19、原ShadowScene51/ShadowHosts39/SceneUiJointProducts866/ShadowJointProducts902）通过，分别67.63/64.74秒，日志 `out/m7-6-targeted-{debug,release}-first.log`；原环境GPU54和共享环境场景87另行双配置通过，texel/pixel误差0、暖态分配0、API0/0。所有最终构建/验证进程已退出0，红测失败证据保留。尚未跑全阶段冻结无Skip双配置；未提交推送、未替换正式M7.5 Release（入口DLL仍SHA `B4AD9CDC660ABF4E4AF416AC6241EF427ABDC8783D3F4FC0E1112ADB2C814D6A`）。接着补原始GPU资源所有权与直接Dispose绕过审计，再完成B/C/D，不能将这组守卫测试当作完整资源恢复。

2026-10-14 A第二增量（未提交，仍非阶段交付）：实际红测 `out/m7-6-ownership-red-test.log` 复现 `lease.Resource.Dispose()` 能绕开缓存引用计数销毁材质，构建零警告，测试因“应拒绝但未拒绝”退出1，失败记录保留。现在缓存中的静态/绑定姿态mesh、texture、material、environment均附着精确C#缓存所有权；公开GPU对象只借用、不转移退休权限，只能释放外层租约，由原缓存在引用归零后调用内部退休路径。回调内捕获原始GPU关闭拒绝也会使外层准备失败。原生释放失败不清空token、不扣缓存字节/条目、不丢依赖；直接renderer创建的非缓存资源仍正常Dispose，无native ABI/Shader变更。

首轮真实DX11扩展34检查通过（`out/m7-6-ownership-expanded-test.log`、证据 `out/verification/m7-6/ownership-expanded/162ec72df21e40bd85de4fa837a823ad`）：材质整图/环境全texel保持、纹理依赖与跨线程拒绝、失效租约的原始引用仍不可擅自关闭、静态与绑定姿态退休、真实未Present帧的原生Busy退休失败保留原条目和计数、之后重新取得同一generation并绘制精确原图、显式离帧Trim排空并失效旧引用、直接资源正常释放、API0/0。该Busy证据不是设备丢失，也不是任意失败可回滚的承诺。规范Debug Skip候选 `f63d456878b44a08a770bae5ab680280` 完成，五组定向CTest通过68.35秒；其余原回归及Release核验仍在执行，不部署正式入口。

A第二增量最终结果：规范Release Skip候选 `c97e261ee4314461994c0969c0bbdafc` 完成，未部署。Debug/Release五组定向CTest分别68.35/68.55秒通过（新增34 + 原ShadowScene51/ShadowHosts39/SceneUiJointProducts866/ShadowJointProducts902）。原完整渲染CTest Debug通过231.88秒，Release首次在历史环境作者保存处发生 `File.Replace` IOException（46.57秒，`out/m7-6-ownership-original-release.log`），未修改保存实现、原测试断言或240秒门限；同源码第二次通过165.53秒（`out/m7-6-ownership-original-release-second.log`）。失败场景保留于 `out/verification/m2/render-Release/environment-authoring-hosts/f8b53398269841be9dfb0bf525a233d1/scene-fixture/cf741051eff541c0b2840930957f4163/start.ncmascene`，2731字节，SHA256 `7EE0ACE5FEF0096E878CFDB1CDDC190B8E6A82EC2ADCF260DE9DD468901F22EF`；事后JSON可读取且独占读成功，但这不证明失败原因，历史原子替换调查继续开放。原环境GPU54项再单独双配置通过，全部texel误差0。

最终只收紧新测试：不再以任意异常作为退休失败证据，必须捕获 `renderer / destroy_mesh / PluginResult.Busy`，并在最后一次直接环境资源关闭后重查API0/0。未改变生产源码、原回归或门限；定向零警告Debug/Release重建及新34检查分别通过，日志 `out/m7-6-ownership-exact-{debug,release}.log`，证据 `ownership-exact-debug/1d42a67d35554c5a8a1c87939875d246`、`ownership-exact-release/823be6c6f414491b920f30bba59070d0`（均在 `out/verification/m7-6` 下）。候选生产代码与最终代码一致；候选生成早于这次仅测试断言收紧，非冻结全阶段交付。所有验证会话结束。正式EXE/DLL仍为M7.5原hash，未提交、推送、替换EXE。余下A的shader/target候选边界与B/C/D仍待完成，不把局部34检查或Busy路径称作完整设备恢复。

2026-10-14 A第三增量（场景候选准备边界，仍未提交）：新增独立 `SceneCandidateSafety` CTest。红测 `out/m7-6-scene-candidate-red.log` 在零警告构建后证明 `ScenePipelineSession.Configure` 的可信 `Build` 回调可调用自身Dispose，外层仍返回成功（Expected rejection，退出1）。现改为同一实例的统一准备守卫，覆盖图配置、Tone/完整scene及environment Shader组替换、环境绑定、后处理绑定；owner-thread/liveness在修改守卫前验证，保留原 `PluginResult.WrongThread` 诊断。回调捕获拒绝也会拒绝外层候选；在原生发布前再次检查，无回调的最后字段提交后统一清理守卫供显式重试。构造/图配置必须匹配精确目标尺寸，改变shadow/target分配仍需完整新实例，帧中准备在执行用户Build之前拒绝。不改变原生ABI/Shader/Agent授权，不将可信C#代码当沙箱，也不承诺回滚它对其他对象或外部IO的任意副作用。

首轮真实DX1134检查通过（`out/m7-6-scene-candidate-expanded.log`，证据 `out/verification/m7-6/scene-candidate-expanded/cf4dc36e7d164fb99a6051c31bae21b8`，零警告、API0/0），覆盖self-close、捕获重入、旧图/注册元数据/整图保持、批准失败后显式重试、target尺寸和shadow分配拒绝、owner先于Build、真实帧中拒绝、Tone/Shader组/environment/Bloom旧绑定与资源归零。随后将shadow负例改为合法256分辨率以证明拒绝的是原地分配变更，新增完整独立shadow候选正例，预计35检查；规范Debug Skip候选 `13063ebbc81b415798dfe35b7f349b41` 完成，当前正在跑最终六组定向、原完整渲染及后续Release。尚不代表B/C/D完成；保留全部先前失败、正式M7.5部署与手工/性能/设备丢失待验证项。

A第三增量最终核验：35检查的独立新shadow候选正例及合法分配变更负例均通过。规范Skip Debug `13063ebbc81b415798dfe35b7f349b41`、Release `6d2ab78871d44a9d9fd25358f1520baa` 均完成，无部署。最终六组CTest Debug70.22秒/Release65.47秒全部通过（SceneCandidateSafety35、ResourceUpdateSafety34、原ShadowScene51/ShadowHosts39/SceneUiJointProducts866/ShadowJointProducts902），日志 `out/m7-6-scene-candidate-targeted-{debug,release}.log`。原完整Rendering及kernel-reference fixture双配置首次通过：Rendering Debug232.92秒、Release164.37秒，原240秒门限不变，日志 `out/m7-6-scene-candidate-original-{debug,release}.log`。旧shader/Tone/环境/Bloom/材质/动画/纯2D和owner/lifecycle原断言未删改。新场景检查证据：`out/verification/m7-6/scene-candidate-safety-Debug/89e34b8beddb4903b88e9bb92917c116` 与 `scene-candidate-safety-Release/2a631812e8e3461889f0d03f4632663d`，均API0/0。所有构建/测试会话已正常结束，红测和此前File.Replace失败现场仍保留，历史问题未由本次通过自动关闭。

本轮仍是准备边界增量，不是M7.6整阶段验收；未运行完整冻结无Skip Debug/Release，未提交推送、未替换正式M7.5 EXE。下一步重点是B：把 `SceneRenderSession.Submit` 分配变更、`PrepareEnvironmentView`/`RefreshEnvironment`、`PreparePostView` 中局部candidate清理失败路径，收敛为有界、可诊断、明确fail-stop且保留新旧所有权的交换流程，再做Editor/Player恢复C及联合D。不能把旧画面保持等同任意清理失败可回滚，更不能声称真实设备丢失已恢复。

2026-10-14 B第一增量（交换失败保留，仍未提交）：真实DX11红测 `out/m7-6-scene-exchange-red.log` 复现准备批准失败后，局部candidate的Dispose再遇原生Busy，SceneRenderSession丢失候选引用，随后正常关闭仍剩一条原生管线。现由C#场景服务保存最多一个待安装/退役bundle（pipeline、environment GPU lease、CPU asset pin）；每项在后续可失败操作之前登记，成功释放后才清空字段。准备失败且清理成功保持旧管线；候选或旧管线退休失败保留所有权、标记明确fail-stop，拒绝Submit/Pick/新准备并失效旧提交诊断。原生ABI和shader没有改变。

`RecoverResourceExchange` 是可信宿主的显式离帧操作：同一owner、只读World、再次批准、拒绝回调重入（包括被捕获的拒绝），依次排空候选和旧管线，再允许完整新准备；不恢复故障Gameplay、不修改World/历史、不创建Agent权限，不承诺设备丢失恢复。关闭一旦开始只允许重试关闭，不允许以恢复冒充继续运行。后处理安装门只负责批准；场景候选由场景持有，启动预检尚无场景时由PostProcessRuntime持有唯一probe，失败后该runtime只允许重试关闭，不能再建probe覆盖旧引用。默认与定制启动共用此路径。

定向Debug零警告构建及新36项真实GPU检查已通过（环境20、后处理/两种启动probe16，日志 `out/m7-6-scene-exchange-final-build.log`、`out/m7-6-scene-exchange-final.log`，证据 `out/verification/m7-6/scene-exchange-final/4be94f3efbf648329f85d1e12d23f0a4` 与 `27b519a399724498a17901d4e34002c8`）。覆盖准确的准备异常+原生`destroy_scene_pipeline/Busy`双失败、旧配置与新旧资源保留、环境升级CPU/GPU闭包、撤销/帧中/线程/捕获重入拒绝、关闭失败重试、显式恢复后的整图逐字节一致、World文档不变、最终资源归零/API0/0。故障测试仅在失败收尾时反射读取Renderer所有者清单并正常释放，不是产品恢复实现或通过判据。初次补丁漏迁移Install调用/nullable编译失败，以及测试漏捕获Aggregate导致的收尾失败日志保留，未放松产品或原回归断言。

规范Debug Skip候选 `794bd3481fbb442c9c364a9537539e35` 已成功；七组定向及原完整Rendering回归正在执行，Release尚待验证。此候选未部署，非完整冻结无Skip门禁。本增量仍不关闭B/C/D/M7.6；非后处理启动/构造失败与宿主级保留、实际设备错误分类/新generation恢复及搬移联合故障证据仍待审计和补齐。

后续审计定位（尚未修改或验收）：`SceneEnvironmentPreflight.Prepare` 静态工厂的catch中局部result.Dispose若失败，调用方尚未得到result；需先建立宿主持有者再执行可失败准备，不能仅改为吞异常。`SceneRenderSession` 构造失败清理、非后处理`ScenePipelineStartup`短期binding也需同类所有权审计。正式Editor/Player不得因局部失败丢失仍有租约的服务，更不能绕过插件卸载保护。原生 `Resize` 的Wait失败目前直接返回shutdown-timeout，而WaitIdle另查device-removed；C应统一分类并验证真实与注入证据，不在本增量无证据改动原生ABI。

B第一增量最终核验：规范Skip Debug `794bd3481fbb442c9c364a9537539e35`、Release `0a18cfca27c84894be71d7c3aee0ca6b` 均完成，生产源码相同、零新警告、未部署。最终七组定向CTest双配置全部通过，Debug71.13秒/Release68.62秒（SceneExchangeSafety36、SceneCandidateSafety35、ResourceUpdateSafety34、原ShadowScene51/ShadowHosts39/SceneUiJointProducts866/ShadowJointProducts902）；原完整Rendering及kernel-reference首次双配置通过，Rendering230.62/164.62秒，240秒门限未改，日志 `out/m7-6-exchange-{targeted,original}-{debug,release}.log`。新36项证据位于 `out/verification/m7-6/scene-exchange-safety-Debug`（环境20：`5a83ead98bd14934a601e3718f515548`；后处理16：`0d9238e03fc84ec3b2dc30dda3a128ac`）及 `scene-exchange-safety-Release`（环境20：`34e70ed71dfd4d378a7c4c7fcfe0cae6`；后处理16：`12779436271c47c4b1f1be786ccf7674`），API0/0。Release编辑器候选144项manifest文件实测SHA一致。

全部构建/测试会话结束0；未执行全阶段冻结无Skip门禁、未提交推送、正式EXE/DLL保持M7.5 SHA `5487DF87B13019C24AF2E19C6EB1DB767E04E950AA47ED32E6C7A76619068C76` / `B4AD9CDC660ABF4E4AF416AC6241EF427ABDC8783D3F4FC0E1112ADB2C814D6A`。本段替代上文“Release待验证”的过程状态，不关闭B剩余启动/构造审计、C/D、真实设备丢失、人工/目标/性能/1h门禁，也不将历史File.Replace/超时调查自动关闭。

2026-10-14 B第二增量（环境启动预检，进行中）：移除静态返回持有者的 `SceneEnvironmentPreflight.Prepare` 入口，无兼容转发；改为无GPU/资产分配的构造器 + 实例Prepare。宿主先保存实例，随后才执行可失败准备；Empty/Preparing/Ready/Failed/Closing/Closed状态及复制的资源持有诊断明确，失败不隐藏或自行抛弃候选。每个对象只准备一次，失败/关闭后不能覆盖旧bundle，显式Dispose成功才清空对应引用。owner检查先于状态、回调和释放；批准内捕获Close/Prepare拒绝也使外层准备失败；Off/无3D几何路径不查询环境GPU接口。

已迁移实际Editor启动、Editor Play准备、Player准备三处；Editor Play用独立宿主字段保留预检，不再局部using，GPU预检和正常退休必须在创建游戏/solver运行状态之前完成。准备+关闭双失败保留两个异常及宿主字段，下次Play不能覆盖未关闭预检，宿主Dispose沿依赖顺序重试。未新增AI/Agent权限、设备恢复入口或原生ABI。`RenderResourceCache.VerifyFor`仅内部精确renderer归属检查。

首次零警告生产构建后，新测试自身误用环境统计`Creates`字段编译失败，已改为真实`Publications`，日志保留 `out/m7-6-startup-owner-first-build.log`。随后Debug直接运行通过既有环境20/后处理16、新环境预检15与实际宿主15检查（`out/m7-6-startup-owner-hosts.log`）。预检覆盖真实`destroy_scene_pipeline/Busy`、CPU/GPU/pipeline保持、关闭重试、拒绝重入/撤销/错误线程/帧中准备、文档不变和资源归零/API0/0；实际Player公共启动拒绝证明字段先持有，三个宿主字段的Busy关闭使用明确的测试注入，随后调用真实Dispose验证失败时不卸载依赖和重试成功。反射仅用于测试附加故障持有者/读取证据，不是产品恢复；不冒称这是真实设备丢失或完整失败启动验收。

最终补Off可选API未初始化的两项实际Player断言，新宿主目标17、总交换测试68项；标准Debug Skip构建执行中，后续定向、原完整Rendering及Release门禁尚待结果。正式M7.5未部署替换，B剩余构造失败/无post启动绑定、C恢复与D完整搬移故障链仍开放。

B第二增量Debug结果：规范Skip候选 `9fdd8345598242dca7433eb2a4778eaf` 完成，7组定向CTest全部通过73.66秒，扩展后的SceneExchangeSafety68项包含最终Off两项；原完整Rendering229.43秒及kernel-reference首次通过，日志 `out/m7-6-startup-{targeted,original}-debug.log`。新预检15证据 `out/verification/m7-6/scene-exchange-safety-Debug/0c54934042c44274ac110e26bc42662d`，宿主17证据 `b5928609022149ae824f86e84febbbc2`；原环境20与后处理16保持通过。Release同源构建/核验继续中，尚非整阶段交付。

C后续边界提醒：本轮实际宿主故障检查持有 `PlayerPresentation` / `CandidatePresentation` 引用并直接调用Dispose，证明的是服务实例内的保留/关闭顺序。`PlayerRunner.Run` 在关闭失败后目前只返回字符串诊断，局部presentation引用随Run返回脱离调用方，尚没有对外的明确保留/重试生命周期；不能用直接类测试代替Run/CLI/搬移产品的故障恢复验收。需要在C中明确进程存活时的宿主所有权及诊断/恢复边界，不能force unload、吞异常或把进程退出的系统回收称作设备恢复。

B第二增量最终结果：同源规范Skip Debug `9fdd8345598242dca7433eb2a4778eaf` / Release `cb50004757d744c691daac8f77d1cb93` 均成功，零新警告，未部署。七组定向CTest分别73.66/70.97秒通过（SceneExchangeSafety扩为68，原候选35/缓存34/场景阴影51/宿主39/原UI联合866/阴影联合902）；原完整Rendering及kernel-reference双配置首次通过，Rendering229.43/163.88秒，原240秒门限未改。日志 `out/m7-6-startup-{targeted,original}-{debug,release}.log`。Release新增预检15与宿主17证据分别是 `out/verification/m7-6/scene-exchange-safety-Release/797dadd1e91746d9a6a683117f79a5d6`、`5a6a3751706949f6bd7530d0bb4cf1bb`；既有环境20/后处理16亦通过，API0/0。Release Editor144 / Player-DX11113 / Player-Null56项manifest文件SHA实测一致。所有构建与测试会话正常退出0。

本轮交付记录只闭合该环境预检增量：旧静态入口已删除且消费者全部迁移，不保留兼容路径。正式EXE/DLL仍是M7.5原SHA，未冻结执行全阶段无Skip门禁，未提交推送。B剩余无post启动绑定与场景构造失败资源清理、C的Run/CLI宿主生命周期/设备分类及D搬移故障联合仍未闭合；真实设备丢失、人工/用户素材/目标环境/完整性能/1h等门禁全部保留。所有早期失败与4项IDE改动保留。

2026-10-14 B第三增量（统一管线启动预检，进行中）：`ScenePipelineStartup` 改为宿主先保存的实例化有界持有者，保留无资源的静态Bind工具；旧静态Prepare及 `ScenePostProcessPreflight.Prepare` 删除，无兼容转发。Prepare与PreparePost共用最多一个实际16x16探针的所有权，后处理与无后处理路径均在最终批准之后退休；准备+释放双失败保留两个异常及探针，拒绝覆盖，显式Dispose成功才清空。后处理运行服务回归CPU/已准入程序组合职责，删除其临时_probe/PrepareProbe/HasRetainedProbe，不重复持有GPU对象。Editor/Player均先保存_pipelineStartup，关闭时先排空它，再关闭程序库和原生依赖。

实例owner/liveness/非重入、捕获拒绝污染外层候选、最终批准与空/Off不查询环境GPU接口已经实现。实际源端新增PipelineStartupSafety22检查通过（`out/m7-6-pipeline-owner-expanded.log`，证据 `out/verification/m7-6/pipeline-owner-expanded/fdee57b4c662459789ed717a9c79a2d7`，零警告）；覆盖两种shadow候选真实创建/退休、批准失败前后重入、帧中/线程拒绝、无post最终批准+原生destroy_scene_pipeline/Busy双失败、恰好一个探针保留、显式关闭重试、World不变/API0/0，以及实际Editor/Player类的注入持有者关闭重试。后处理原16项断言保持，仅改为检查新的明确持有者；历史PipelineStartupBoundary原断言/计数不变，仅迁移调用签名。交换组共90项已直接通过；规范Debug Skip与双配置定向/原完整回归接着执行，不提前认定阶段交付。

下一条构造所有权审计不能只改最外层SceneRenderSession：其内部 `SceneGpuResources.Prepare` 静态工厂也在catch里Dispose局部result、随后Trim；有skinned GPU资源时清理失败会使外层尚未取得结果。同样检查 `SceneAnimationSession` 构造catch的Dispose。需要贯通“先保存持有者、再做可失败初始化”的依赖链并补真实skin/资源清理失败测试，而非仅增加未接入的包装类型或吞掉清理异常。C/D与Run生命周期缺口保持开放。

同一次迁移还需覆盖 `CandidatePresentation.SynchronizeSceneResources` 的Edit/Play局部candidate：当前旧Scene关闭失败后再调用局部candidate.Dispose，若第二次释放也失败，宿主仍没有保存候选。必须与上述初始化链一起建立宿主持有的有界待交换Scene，并在场景同步失败后阻止覆盖；低层SceneRenderSession已实现的pipeline交换保留不能替代这一更高层的Scene/资产/动画所有权。

B第三增量Debug结果：规范Skip候选 `8a207a8e40ed4797bd0f9a724bb2ad58` 完成；七组定向CTest通过76.08秒，SceneExchangeSafety90（原环境20、预检15、宿主17、新pipeline22、后处理16）。原完整Rendering230.94秒及kernel-reference首次通过，原240秒门限未改。日志 `out/m7-6-pipeline-{targeted,original}-debug.log`；新增22证据 `out/verification/m7-6/scene-exchange-safety-Debug/730c1f99e57d42828e04a9a4892567fe`。原PipelineStartupBoundary及原正式产品回归仍完整保留，只有所需调用签名迁移，未放宽断言。Release同源构建/测试继续中，正式EXE/DLL原SHA未变。

B第三增量最终结果：同源规范Skip Debug `8a207a8e40ed4797bd0f9a724bb2ad58` / Release `4949a436ac2a426595884c17aa803cc7` 成功，零新警告，均未部署；七组定向分别76.08/73.24秒通过（交换90、候选35、缓存34、原场景51/宿主39/UI联合866/阴影联合902）；原完整Rendering与kernel-reference双配置首次通过，Rendering230.94/163.35秒，240秒门限不变。日志 `out/m7-6-pipeline-{targeted,original}-{debug,release}.log`。Release新增pipeline22证据 `out/verification/m7-6/scene-exchange-safety-Release/e8b8abec77c1469c94384457e275f444`，其余既有68检查全部保留。Release Editor144 / Player-DX11113 / Player-Null56项manifest实际文件SHA通过。

全部工作会话退出0，未跑全阶段冻结无Skip Debug/Release、未提交推送、未部署；正式M7.5 EXE/DLL原SHA保持。B剩余SceneRenderSession/SceneGpuResources/SceneAnimationSession构造与Editor整Scene交换的所有权链，以及C运行器/设备分类/恢复、D搬移故障联合未闭合。已有人工/用户素材/目标/自包含/完整性能/1h门禁与历史故障调查保持开放。本增量是实际资源管理修复，不是设备丢失恢复证明。

2026-10-14 B第四增量（场景构造所有权，进行中）：首先保留真实红测 `out/m7-6-scene-construction-red.log`：实际ASCII FBX第二个蒙皮准备拒绝且GPU帧仍开启，旧静态工厂清理遇到Busy后抛出，调用方未拿到result，但SkinStats.Meshes仍为1。不是模拟设备丢失，也未绕过插件卸载/帧保护。

`SceneRenderSession`、`SceneGpuResources`、`SceneAnimationSession` 现均先构造无GPU/资产租约的持有者，再执行单次实例Prepare；旧可失败构造函数/静态工厂删除，无兼容转发。内部资源与动画持有者先保存再Prepare；准备失败保留原异常和已取得资源，关闭仅在每项释放成功后移除，Busy保留以便显式同持有者重试。Empty/Preparing/Ready/Failed/Closing/Closed状态、owner限制、捕获Prepare/Dispose/使用重入仍污染外层准备；未完成/失败/关闭中的对象不可渲染。准备期间World只读，渲染热路径没有新增快照/IO/堆分配。

Editor同时持有最多一个待交换整Scene，在准备与旧Scene关闭成功后才移入Edit/Play槽；失败后禁止覆盖待交换对象。Player与独立动画图预览均先保存Scene再Prepare，实际宿主关闭顺序在cache/kernel/plugin依赖之前排空Scene。原测试仅迁移调用签名，旧断言和门限未放宽。此处尚不解决PlayerRunner.Run返回后的进程内恢复契约，也不代表完整CLI设备故障恢复。

新增独立CTest `NcmaRenderingTests.SceneConstructionSafety`：源端Debug首次70项通过，真实ASCII/Binary FBX各覆盖低层和完整Scene的第二实例失败、原生destroy_mesh/Busy、同持有者关闭重试、捕获嵌套Prepare/Dispose、错误线程、全资源排空、恢复后实际渲染与World不变/API0/0。实际Editor/Player类关闭重试使用明确测试专用字段注入，不冒称普通入口故障注入或OS操作验收。首次证据 `out/verification/m7-6/scene-construction-debug/b954720340a649e8b9486e0a65cec1f5`；原红测和编译日志全部保留，无反射清理作为通过依据。最终双配置规范候选/定向与原完整回归仍在核对，阶段未提交/未部署。

B第四增量最终核对：规范Skip Debug `f4d0a352696c4d60b51db8038123559a` / Release `a4b1881acc2342089065d78dac576fec` 零警告构建成功；八组定向CTest分别82.96/81.59秒全通过（新增构造70、交换90、候选35、资源34及原阴影51/宿主39/UI联合866/阴影联合902）。原完整Rendering、EditorService、Player、SceneRendering及kernel-reference五组分别385.22/309.89秒全通过；Rendering233.16/166.18秒，原240秒门限未变，Editor80.69/75.76秒，Player69.39/65.70秒。日志 `out/m7-6-construction-{targeted,original}-{debug,release}.log`；新增70正式CTest证据分别为 `out/verification/m7-6/scene-construction-safety-Debug/8ddcfda21d1341f29df6e1651baf7a2a`、`out/verification/m7-6/scene-construction-safety-Release/086f76b7a5984670b795a0612f733b84`。两个候选包Editor144/DX11113/Null56项manifest实际SHA均核对通过。

全部构建/测试会话已正常结束。正式M7.5 EXE `5487DF87B13019C24AF2E19C6EB1DB767E04E950AA47ED32E6C7A76619068C76` / DLL `B4AD9CDC660ABF4E4AF416AC6241EF427ABDC8783D3F4FC0E1112ADB2C814D6A` 未改变；未执行整阶段冻结无Skip回归/部署/提交推送。下一步继续审计完整PlayerRunner.Run返回后持有者存活/关闭重试、普通宿主场景交换故障链，再补C设备错误分类和D搬移联合故障证据；当前注入字段的宿主类测试不能替代这些门禁。人工、用户素材、目标环境、自包含、完整性能、1h及历史异常调查全部保持开放。

2026-10-14 C第一增量（完整Player运行器所有权，未提交）：`PlayerRunner` 由静态方法改为调用方持有的实例；旧静态Run/RunForVerification移除，全部原消费者迁移，原测试断言和超时未放宽。运行器持有presentation/runtime/physics/CPU资产和程序库，按依赖顺序关闭，遇到第一项失败即停止，不释放其依赖；只有成功释放才清空字段。Run返回后仍保留失败持有者及复制的状态，禁止覆盖；显式同线程RetryShutdown只关闭、不推进游戏或改写原报告。Idle完全排空后可创建新World/session；Disposed不可复用。嵌套Run/关闭/重试即使被回调捕获，也使外层运行失败。CLI持有单一进程字段直至正常关闭或进程退出；不把操作系统退出回收称作设备恢复。

新增独立可选renderer query17/v1（API32B、状态40B，原renderer1.2表152B保持）：只读返回renderer generation/frame/active/fail-stop/abandoned计数；终止操作要求精确当前帧和owner线程，结束计时并清理绑定，不Present未完成图像，renderer随即只允许关闭。GPU采样无效，旧资源仍经过原有租约和有界退休等待，不强制卸载或伪造设备重置。C#通过 `AbandonFrameAndRetryShutdown` 显式选择此关闭路径，正常和Null路径不查询可选接口，旧模块缺接口明确拒绝。新增C/C++布局与真实原生生命周期验证。此增量首次改变原生代码，先前B增量的“无native ABI变更”仅对对应历史增量成立。

新独立CTest `PlayerRunnerLifetime` 先以普通Run实际OnDisable抛错验证返回后的所有权/报告与重试，再用受限测试捕获回调在真实Submit后抛错，复现原生Busy；最终调用公开终止帧API排空，不再使用反射Present作为恢复手段。40项覆盖Null/图形、物理与实际CPU资产文件锁、外线程/捕获重入拒绝、原报告脱敏及不重写、完全排空后新World/session/API0/0。原生测试覆盖精确帧、坏句柄、短表、外线程、未Present的终止状态、GPU计时无效、禁止继续绘制及注入退休timeout仍保留资源。注入故障不是真实硬件设备丢失。

Debug规范Skip候选 `b2e156e020fe488d973a23ccee825dca` 成功，11组定向CTest114.76秒全部通过（含原native、PipelineHosts、UI/阴影联合和构造70/交换90/候选35/资源34/新增运行器40），日志 `out/m7-6-runner-targeted-debug.log`。早期34项证据和新测试变量遮蔽编译失败日志保留。最终补C语言布局断言与空白格式后，规范Debug重建及原完整回归执行中；Release尚待验证。未冻结整阶段无Skip门禁，未部署/提交推送。C的设备失败分类、Editor/Player尺寸与重建政策、D完整搬移故障链及所有既有人工/目标/自包含/性能/1h门禁仍开放。

C第一增量完整回归首轮保留失败：`out/m7-6-runner-original-debug.log` 的原Rendering在 `SkinSceneTests` 暖态零分配断言测得4096B（count0/differentMesh=True/warm32），该组失败；其余7组通过，Editor79.55秒、Player68.31秒。尚未定位分配根因，不能归因于新运行器或宣称旧间歇问题已修复。只补失败后分步骤advance/submit/present/wait及view缓存身份诊断，原8帧测量/32暖帧/0B门限/240秒超时均不变，失败仍按原值失败。规范Debug候选 `b256a0f13a5b47d98e6894e061614ab4` 已生成，同原回归复测中，日志 `out/m7-6-runner-original-debug-second.log`；不以定向通过掩盖首次完整失败。

C第一增量最终核对：规范Skip Debug `b256a0f13a5b47d98e6894e061614ab4` / Release `1ae720838e714149b7ae925fd6b81c10` 零警告构建成功。Debug11组定向114.76秒（相同生产源码，早于仅失败诊断补充）及最终8组原完整/原生/运行器385.48秒通过，Rendering231.55秒、Editor79.24秒、Player68.37秒；Release15组定向115.61秒和5组原完整312.16秒通过，Rendering166.30秒、Editor75.89秒、Player67.83秒。原240秒Rendering门限未变。日志 `out/m7-6-runner-{targeted-debug,original-debug-second,targeted-release,original-release}.log`。新增40项证据分别为 `out/verification/m7-6/player-runner-lifetime-Debug/5337e4c1d0284783b266be70310c97e2` 与 `player-runner-lifetime-Release/cd2deba811a449f0a296af6cdc777e96`；C/C++布局、原生、架构/互操作/无图形检查通过。两个候选Editor144/DX11113/Null56项manifest实际文件hash一致，所有工作构建/测试会话已结束。

首次Debug4096B分配失败未被复测通过掩盖，失败日志保留，根因仍待定位；没有增加暖帧/容限/超时或删除断言。正式M7.5 EXE/DLL仍是 `5487DF87B13019C24AF2E19C6EB1DB767E04E950AA47ED32E6C7A76619068C76` / `B4AD9CDC660ABF4E4AF416AC6241EF427ABDC8783D3F4FC0E1112ADB2C814D6A`，未部署/提交推送，未执行整阶段冻结无Skip门禁。本轮只完成完整Run返回后的持有者/显式关闭链增量；下一步C统一Resize/Wait的实际设备错误分类、Editor/Player最小化/尺寸/新generation重建，再补D完整搬移故障链。真实设备丢失、人工/目标/自包含/完整性能/1h及历史故障调查均保持开放。

2026-10-14 C第二增量（GPU完成结果分类，进行中）：红测 `out/m7-6-completion-classification-red-second.log` 通过测试TU注入实际CreateQuery分支的E_OUTOFMEMORY，复现Resize把查询创建失败误报shutdown-timeout。首条命令误用了测试路径，保留其失败记录；随后按CTest生成的实际路径得到上述产品逻辑红测。

底层Wait改为结构化NCMA结果，统一21处完成等待调用：查询创建/轮询失败返回InternalError；实际deadline超时返回ShutdownTimeout；DXGI移除/重置/挂起/driver-internal结果或GetDeviceRemovedReason失败返回DeviceLost。不通过错误字符串猜原因。Resize、WaitIdle、Shader/环境/后处理替换、捕获与资源退休使用相同分类。普通查询失败或超时不发布新尺寸、不丢失旧资源，也不伪造设备丢失；设备丢失进入明确fail-stop，禁止继续渲染。保留原退休策略：只有实际GetDeviceRemovedReason确认移除，才无需等待已失效设备的fence，所有租约/pin检查仍在前；等待中才发现丢失的本次操作保留资源，后续显式关闭重试。没有新增ABI/命令/Agent权限。

Failure、执行异常和上一增量的显式终止帧共用原生停止收尾：结束尚开启的计时，清理绑定但不释放所有权，失效pending/sample-valid/GPU毫秒，防止故障后报告旧的有效计时。正常同尺寸Resize仍立即返回，不新增逐帧Wait或托管分配。

新增 `RendererCompletionNativeTests.inl` 的408项检查在真实健康DX11设备上验证4种操作×5种注入结果（Resize/WaitIdle/target退休/renderer关闭；查询创建失败/轮询失败/超时/等待前移除/等待中移除），包含旧画面逐字节保持、实际奇数尺寸swapchain绘制与读回、坏零尺寸保持、资源/尺寸/句柄保留、明确关闭重试、新renderer代与旧资源拒绝/API0/0。设备移除是测试TU结果注入，测试确认物理设备仍正常，不冒称真实设备重置或Editor/Player窗口最小化验收。既有15组超时故障测试各增加查询/轮询失败排列，覆盖Tone/整组Shader/Skin/UI/环境/readback/后处理。一次新增测试误重复执行成功的scalar publication引起计数失败，已去掉该无等待成功路径的重复调用，保留原断言；首次失败日志仍保留。

原生Debug零警告构建与全套运行通过，日志 `out/m7-6-completion-native-debug-build-second.log`、`out/m7-6-completion-native-debug-second.log`。规范Debug Skip候选 `3f7957402b434f7a85765f247eeaac41` 已生成，联合与原完整回归继续执行，Release尚待验证。正式M7.5未变，M7.6尚未交付/提交推送；接着需要C#宿主的尺寸/最小化/重建政策与诊断保留、实际Editor/Player生命周期，以及D搬移故障联合。真实设备丢失/人工/目标/完整性能/1h和历史4096B间歇分配调查仍开放。

C第二增量原宿主回归发现并修复前序B缺口：首轮14组定向只有原PostHosts失败（`explicit recovery restores pixels`），失败日志 `out/m7-6-completion-targeted-debug.log` 保留。此前 `_pendingScene` 无条件拒绝任何新同步，导致修正错误配置后永远无法恢复。现在只在上次失败之后出现新的已提交SceneSyncKey时，进入只读World边界并通过query17检查实际renderer健康/离帧，再显式Dispose旧候选；关闭成功才清空字段并重新准备。相同失败stamp不重试，帧中/故障/关闭失败保留所有权，主pending保护不删除；同步失败期间不再推进旧Edit动画。此路径不恢复故障设备，不创建Agent权限。

源端新增普通Editor循环证明：错误参数准备得到的空持有者确实被保留、未变请求不重试；真实原生帧开启时新提交请求的恢复被拒绝且保留同一候选；结束该帧后不自动重试；再提交新配置修正后安全关闭旧候选并恢复逐字节相同视口，API0/0。反射仅读取持有者作证据，没有附加/替换字段或反射清理。C2宿主72、C3面板99检查通过，日志 `out/m7-6-completion-editor-recovery-expanded.log` / `out/m7-6-completion-editor-author-recovery-expanded.log`，证据分别为 `out/verification/m7-6/completion-editor-recovery-expanded/3ffe3a8cdf4940d2a1a6fa4cb6ed7bf3` / `completion-editor-author-recovery-expanded/068ab9cfb6b94da0b00566043ebc6701`。最终双配置规范候选、定向/原完整回归重新进行；尚未部署或提交阶段。

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
