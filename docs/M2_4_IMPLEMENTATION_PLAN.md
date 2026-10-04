# M2.4：C# 可编程渲染管线、独立 Renderer 插件与 DX11 参考预览

更新日期：2026-10-04。状态：独立 DX11 参考插件与 C# 可编程首切片已实施，自动 H4 联合验证通过；人工窗口验收仍待完成，不能标为整体 H4 完成。依赖 H2/H3；本阶段不是 Vulkan 或完整场景渲染阶段。

实际接口、定制限制、配置路径、门禁证据见 [联合报告](M2_3_4_TEST_REPORT.md)。本文件保留完整阶段验收要求，未实现的通用资源 API/材质功能不因候选实现而自动完成。

## 1. 目标、现状与边界

将现有 D3D11 RHI 从 NcmaCore/EditorApplication 的绑定中抽成 NcmaRenderer.dll。
C# Rendering 服务拥有渲染意图、读模型与资源租约；原生只管理设备/交换链/资源与绘制数值。
本阶段确认采用渲染方案 A：C# 主导默认管线与自定义管线的流程组织，C++ 负责性能关键的 GPU 执行。
不采用方案 B（C++ 提供独立完整默认管线、C# 仅在扩展点介入）。此处的 A/B 与实施小节字母无关。
IRenderBackend 是 C++ 内部接口，不直接穿过 C ABI。
原生窗口/Renderer/Gui 初始化只发生一次；C# 不处理 ID3D11Device/Context、COM 或 Vulkan 句柄。

来源：renderer/rhi/RenderBackend.h、RhiResources、d3d11/D3D11RenderBackend、
pipeline/PbrPipelineSettings、rendergraph/RenderGraph、EditorApplication 的预览资源/绘制函数。
Null 是无图形执行服务，不把空绘制记录成渲染成功；Vulkan 仍只 probe，显式请求必须明确失败，不自动改成 DX11。

### 1.1 可编程框架与官方默认方案

设计分为 Ncma Rendering Framework（可编程框架）和 Ncma Standard Render Pipeline（NSRP，官方默认管线）。
参考 Unity 的可编程管线/Feature/Pass 扩展方式；默认方案以成熟引擎的开箱可用、完整效果、调试和质量管理为长期目标，不承诺 M2.4 达到 UE 功能规模。
NSRP 由 C# 组织，并使用与用户管线相同的公开接口；共享的 Shader、数值内核和 GPU 操作由原生后端执行。
用户不编写渲染代码时，由托管应用自动选择并创建官方默认管线，而不是启动另一套原生默认流程。
C++ Renderer 可以脱离 CLR 接受测试程序提交的合法批次；这不意味着原生插件拥有完整的产品默认管线、场景或游戏逻辑。

### 1.2 所有权与模块边界

| C# Rendering | C++ Renderer 插件 |
| --- | --- |
| Pipeline 配置、实例、质量预设、默认流程 | 设备、交换链、GPU 资源、上传和回收 |
| RenderFeature/Pass 组织、逻辑资源和依赖 | 后端绑定、绘制、计算与 API 特定同步 |
| 材质/相机/灯光规则、场景渲染数据提取 | Shader 后端支持、经过测量的数值内核 |
| 图语义验证、逻辑生命周期、批次编码 | 原生句柄/批次安全验证、物理资源执行 |
| 编辑器配置、Undo/事务、受限 Agent 能力 | GPU 时间戳和原生执行诊断 |

托管渲染服务不依赖 Editor.Core；编辑器通过业务适配层接入，Player 也能使用同一框架。
逻辑 RenderPipeline 与 GPU graphics/compute pipeline 资源分别命名，避免把托管流程实例当作原生管线句柄。
World 所属线程提取有界且不可变的 RenderFrameView；原生与渲染线程不访问 live World，不保存 GameObject/组件引用。
声明并检查渲染线程/资源 owner-thread 归属；不要通过迁移渲染服务改变 World 的 owner-thread 或固定步调度规则。

### 1.3 四级定制与统一执行体系

1. 配置：参数、质量预设和效果开关，无需用户编写代码。
2. Feature：以 C# 声明效果的插入阶段、Shader、输入输出、参数和所需设备能力，例如描边或后处理。
3. 阶段替换：替换标准阴影、光照或色调映射等阶段，必须满足版本化输入输出契约。
4. 完整管线替换：受信任的 C# RenderPipeline 可自行构图，不强制继承 NSRP 固定流程，并可复用官方模块。

定制自由度以已实现且经过查询的原生操作为边界；缺少 compute/格式/资源用途等能力时明确拒绝，不假装支持。
默认与自定义管线共用一套原生执行器，不分别维护两套 RHI。
C# 组织和编码命令，GPU Shader 完成像素/顶点/计算工作；不使用逐物体 P/Invoke 或原生逐 Pass 回调托管委托。

### 1.4 RenderGraph 与插件契约

C# 定义强类型资源、Pass、读写依赖、临时/导入/历史资源种类，并完成排序、循环检测、未初始化读取检查和逻辑生命周期分析。
当前字符串 Reads/Writes 与 C++ 执行回调不是最终公开模型；不得把 std::function、STL 或托管对象跨 ABI 传递。
首期实现单视口所需的有界图与执行批次；拓扑不变时复用编译计划，只更新参数和绘制数据。
原生根据描述分配实际资源、处理绑定冲突并执行命令；DX11 使用对应的冲突处理，Vulkan 的屏障/布局转换留到真实后端实现。
资源池、安全复用、无效 Pass 裁剪和历史资源扩展分切片加入，不能把未实现的资源别名或异步队列优化列为已支持。
C ABI 使用版本化 POD、显式长度、opaque handle 与有界缓冲；冻结时规定最大批次字节数/Pass 数/资源数，并检查溢出、类型、代际、设备归属和线程。
输入描述同步复制；提交返回不代表 GPU 完成。区分逻辑生命周期、提交缓冲生命周期和 GPU 资源实际回收时刻。

### 1.5 默认管线的分阶段目标

M2.4 首期由 C# ReferencePreviewPipeline 组织现有 cube/ground、PBR、级联软阴影、HDR/tonemap 与 GUI 合成，作为默认参考管线。
迁移时保留可比较的绘制算法，不同时强制重写为完整延迟渲染；该首期管线不是生产级完整场景 NSRP。
后续完整 NSRP 建议采用不透明延迟渲染、透明前向渲染；分块/集群灯光、IBL、探针、AO/SSR 和完整资产接入分阶段验证。
基本流程：场景提取/剔除 -> 阴影/可选深度预通道 -> GBuffer -> 光照输入与光照 -> 天空/反射/透明 -> 抗锯齿与 HDR 后处理 -> 色调映射 -> UI/GUI -> 单次 Present。
具体顺序受资源依赖约束；例如 AO 作为光照输入时必须在相应光照之前，不能仅凭任意整数给效果排序。
动作游戏的 TAA 必须具备相机、物体及骨骼变形速度数据，处理相机切换/瞬移/resize 的历史失效；未完成前不宣称支持成熟 TAA。
运动模糊/景深默认关闭。完整灯光、透明、抗锯齿、质量预设与动画正确性是后续 NSRP 验收项，不是 M2.4 迁移时自动获得的能力。

### 1.6 材质、配置、编辑器与 Agent 边界

ShaderContract 版本化声明顶点/骨骼/相机/对象/材质布局、资源绑定、颜色空间以及 Depth/Shadow/GBuffer/Forward/Velocity 等 Pass 的兼容性。
M2.4 先冻结参考 Shader 与自定义效果所需的最小契约；完整材质资产、所有 Shader Pass 和材质节点编辑器留到后续。
Pipeline 配置只持久化版本、稳定类型标识、参数与资产 UUID，不序列化 GPU handle；不扩展旧场景兼容格式。
类型由受信任启动代码显式注册；配置与 Agent 输入不能任意加载 C# 程序集或执行 Shader/C# 文本。
渲染服务暴露结构化配置/图/性能检查；编辑器适配共享 Editor.Core 命令与事务，运行时不因此依赖 Editor.Core。
建议能力为 render.inspect_pipeline、render.inspect_graph、render.get_profile、render.propose_settings、render.propose_feature_config；这些是待实现名称，不作为现有 MCP 能力宣传。
Agent 默认只读；应用提案须重新检查确切权限、配置/session 身份与 revision，支持 Undo，不直写 World/GPU。
候选配置先验证和建立资源，成功后在帧边界切换；验证/Shader 编译失败保留旧配置并报告诊断。GPU 设备故障不属于可回滚的配置事务。
旧计划及资源须在 CPU/GPU 不再引用后释放；不在模拟/渲染 tick 等待 AI 推理或 IPC。

## 2. 分步实施

### A. 模块解耦与能力真值

独立 CMake SHARED target，D3D11/dxgi/d3dcompiler 是私有依赖，不引入 hostfxr/Scene/Editor/Physics。
能力按实际函数和设备查询返回：backend、max dimensions、texture arrays、reference PBR/shadow path、验证支持。
“不支持”拒绝对应请求；禁止把 backend enum 或 loader 存在当成实现证明。
初期只能描述 ReferencePreview 支持，不标为任意 GameObject/FBX 完整渲染。

### B. 资源生命周期

C ABI 建议覆盖 CreateRenderer/Resize/BeginFrame/Submit/Present/WaitIdle/Destroy 与 Buffer/Texture/Sampler/Pipeline 资源。
资源都是 type-tagged generation handle；跨 Renderer、错误类型、已释放句柄拒绝。
输入描述/初始字节同步复制；buffer size/offset/count 算术检查，输出不足不得创建不可返回的资源。
释放在 owner thread；GPU 尚引用的资源延迟到安全完成点，不靠托管 finalizer 直接 COM Release。
device_lost 转为应用可观察故障，停提交/撤销相关 lease；M2 不承诺自动恢复设备或复活场景资源。

### C. 参考预览重组

先迁移原有 cube/ground、PBR 金属粗糙度、HDR/tonemap、级联与软阴影参考资源，保持画面可比较。
预览配置/选中对象 Transform 由 C# 提交复制的 RenderFrameView；非空间对象明确无预览模型。
资源创建与每帧参数更新分开；重复场景 4096 项不允许逐对象 P/Invoke。
图形批次版本/header/count/偏移/有限浮点完整校验；提交不读 live World。
矩阵的行/列主序、向量相乘顺序、坐标轴/单位、四元数顺序、纹理颜色空间与 viewport 像素原点在 ABI 冻结时逐项指定；用现有 reference fixture 对照，不能因 C#/Eigen/HLSL 约定不同而静默转置。
C# RendererService 可以在迁移中暂用高层 SubmitReferencePreview API 保持画面，但它不是本阶段最终的可编程接口，不能据此宣称完成方案 A。
完成 G/H 后，默认参考流程由 C# 声明并提交到共享原生执行器；原生可保留经过测试的 Shader/数值/资源执行，不保留产品级流程策略或 native 场景层。

### D. GUI 后端连接

首切片复用 imgui_impl_dx11 的 native 绘制适配，Gui 与 Renderer 间通过版本化 native-private 服务联结。
服务生命周期依附 Renderer lease，API 头仅在 native GUI/DX11 适配模块，C# 不通过 GetDevice 获取裸图形对象。
对外 viewport/texture 用 Renderer-owned handle；ImTextureID 内部映射，不把 SRV 指针传入业务/持久化。
Renderer 先画参考预览，再 GUI 合成，最后只有一次 Present；不能 Gui 与 Renderer 各自 EndFrame 两次。
后续 API 无关 GUI triangle batch 可替换内部桥，但 M2 不同时更换后端算法和主入口。
原生私有服务依赖在 H2 注册，Gui 的销毁必须先于 Renderer。

### E. Resize/离屏边界

用 framebuffer 尺寸决定 swapchain，零尺寸暂停图形提交；恢复后重建合法目标。
先满足单视口参考预览组合和 GUI，定义清楚 view rect/scissor/clear/depth 的归属。
若已有 HDR 目标可复用，提供只读纹理租约供 Gui；不将它宣称为 M3 完整多相机/资产离屏视口。
texture 重建会更新 generation，GUI 旧引用被拒绝/显示诊断占位，不崩溃。
帧验证记录 resource create/destroy、live counts、GPU wait、resize failures。

### F. 批次与测量

每帧跨 ABI 以固定少量批次提交，持久资源创建另计；输入缓冲可复用，不长期 pin 可增长托管数组。
测当前线程/全进程分配、ABI bytes/calls、prepare/submit/Present 和 GPU 测量，说明 VSync/分辨率/硬件。
同步提交、GPU 完成与 CPU return 是不同概念；内存释放契约不能靠“函数返回就 GPU 用完”。
M1 simulation 成本单列，不能用 native Preview 的 FPS 掩盖 World 快照瓶颈。

### G. C# 管线与强类型图首切片

在 A-F 迁移可运行后建立托管 RenderPipeline、Pipeline 配置、RenderFeature、RenderFrameView 与 RenderGraph 接口。
实现参考预览实际需要的资源描述、依赖检查、Pass 编码和缓存计划；声明插入点与输出契约，不依赖隐藏的原生默认流程。
用相同公开接口实现并默认选择 ReferencePreviewPipeline；C++ 内部负责共享资源与命令执行。
不要求本切片完成完整延迟/Forward+、通用场景资产或多相机，但完整管线替换须在当前支持的操作集内真实可运行。
准备托管图与原生批次单元测试：循环、未初始化读取、缺少输出、格式/尺寸/用途不匹配、能力不足、有界输入和缓存失效。

### H. 定制、配置与诊断首切片

提供参数覆盖、自定义 Feature、阶段替换和独立管线替换的最小 C# 示例；绘制/效果使用注册且经过契约检查的 Shader。
编辑器可以查看生效管线、Pass 顺序、资源与性能数据；首期为结构化只读检查，不同时开发可任意连线的管线节点编辑器。
配置变更与候选计划切换分别处理持久事务和 GPU 生命周期；初始化失败不丢失旧配置，Undo/Redo 在安全边界重建计划。
Agent 只复用已开放的检查/提案与命令适配，不能用本切片绕开 M1 scoped capability 的授权或任意执行代码。
提供资源租约、旧计划延迟回收、拓扑缓存和参数更新的测量；完整默认画质不通过参数列表或枚举值宣称已实现。

## 3. H4 验收

必须是实际渲染，不是编译可用：

1. 同尺寸/配置/相机参考图对比；预先定义图像误差和阴影可容忍变化，记录 GPU/驱动/API 设置。
2. D3D11 Debug Layer 支持环境中无相关验证错误；缺失验证组件记为验收未完成，不假装通过。
3. resize/minimize/restore/DPI/GUI合成，单 Present，参考资源层数/深度正确。
4. 无效资源/批次/overflow/错误线程、初始化与 shader 编译失败、device_lost 的安全状态。
5. 反复 32 次 Renderer 创建关闭，资源计数归零，GPU引用在卸载前结束。
6. candidate 托管 Editor 实际显示 GUI+PBR 参考，不运行 EditorApplication 或 hostfxr。
7. Build.bat Debug 全矩阵，Renderer 专项 Debug/Release，零新警告。
8. 托管应用不编写用户渲染代码即可选择 C# 默认参考管线；默认流程在托管层组织，原生插件不加载 CLR 或引用场景服务。
9. 以公开 C# 接口验证参数覆盖、Feature 插入、阶段替换、完整管线替换；不修改引擎源码或使用特权默认接口。撤销后参考图与资源计数正确。
10. 图循环、未初始化读取、输出/Shader 契约不匹配、能力不足、过大批次与错误句柄均有确定性诊断；候选失败不破坏当前有效计划。
11. 配置/Undo/Redo、resize、切换管线后的旧租约失效与 GPU 延迟释放正确；Agent 变更检查权限和 revision，不直接访问实时 World 或设备。
12. 单列托管构图/编码分配与耗时、ABI calls/bytes 和 GPU 时间；拓扑不变复用计划，4096 项不逐对象跨 ABI。测试报告记录硬件、分辨率、VSync 和图像容差。

原有 1-7 是插件迁移验收，新增 8-12 是方案 A 可编程首切片验收。只有两组均通过，才把 M2.4 整体标为完成；仅迁移通过须明确记录 G/H 仍未完成。

## 4. 交付限制

M2.4 不完成 Vulkan、IBL、GPU FBX 蒙皮、完整材质/纹理资产、多相机场景或 M7 双 API 验收。
M2.4 完成的是 C# 主导的可编程框架首切片与 DX11 默认参考管线，不是完整混合延迟 NSRP、TAA 或 UE 规模的成熟默认效果。
M3 及后续渲染切片接入实际模型/材质/纹理/动画，逐步完善 NSRP；M7 分别验收 DX11/Vulkan 的实际效果、Shader 与 API 验证。
通过对应 H4 项才把“独立 DX11 插件/托管加载”和“C# 可编程参考管线”置为已实现；始终保留“参考预览”限定。

## 5. 参考与决策依据

- Unity C# 自定义 Pass/Feature：[Custom rendering and post-processing in URP](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/customizing-urp.html)。
- Unity 显式资源依赖图：[The render graph system](https://docs.unity3d.com/Packages/com.unity.render-pipelines.core@17.0/manual/render-graph-system.html)。
- UE 默认延迟与可选前向路径：[Forward Shading Renderer](https://dev.epicgames.com/documentation/en-us/unreal-engine/forward-shading-renderer-in-unreal-engine)。

上述资料用于架构参考，不引入 Unity/UE 运行时依赖；NSRP 的阶段规划是 NcmaEngine 设计决定，不代表已实现同等功能或性能。
