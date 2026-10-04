# M2.1–M2.4 候选交付与联合测试

日期：2026-10-04。状态：M2.1–M2.4 候选代码已落地，自动回归通过；**人工门禁待验收**。后续已按用户确认启动 M2.5 业务首切片，见 [迁移记录](M2_5_DELIVERY_REPORT.md)，不等于完整 H5 通过。
此报告不代表整个 M2 完成；不修改默认 out/bin/NcmaEngine.exe，不提交/推送，不安装 SDK 或系统组件。
Python inspect 的 architecture.implemented 明确指向默认入口；architecture.candidate 单独报告新托管入口和插件的已实现能力及未完成门禁，不将候选状态冒充默认产品状态。
后续时点按用户确认：M2 期间保留旧入口作为受控对照，M2 全部结束后统一清除并完整复测。此确认不代表当前人工门禁通过，也不自动启动 M2.5。
本文接口与测试数记录 M2.3/M2.4 交付时点；后续 GUI 模块的 1.1 展示扩展和 26 项联合矩阵以 M2.5 记录为准，其余原始对照/容差证据保持。

## 1. 实际交付

| 模块 | 实际实现 | 尚未实现/限制 |
| --- | --- | --- |
| Ncma.Application | 生命周期/逆序释放、严格项目配置、单调帧循环、有界日志 | Player/发布入口在 M2.7 |
| Ncma.Scripting | 非静态 catalog、候选预检/提交、collectible ALC、Play 租约 | TFM 保留 net8，升级需独立确认 |
| Ncma.Editor.Services | 单一 EditSession、隔离 Play、Endpoint 所有权 | 旧桥接仍为对照消费者 |
| Ncma.Interop | 模块 ABI 1.0、显式允许名单、依赖/哈希/路径校验、owner-thread 租约和释放队列 | 不热卸载；不是不可信代码沙箱 |
| NcmaPlatform | 共享 GLFW、复制事件/状态、图标、小/大窗口资源、Unicode、溢出重同步 | 单窗口；实际输入法/DPI 待人工验收 |
| NcmaGui | C++ Dear ImGui 1.91.9b、复制语义视图/意图、暂态草稿、DPI 字体 atlas、DX11 合成 | 无 docking/multi-viewport；不是完整业务编辑器 |
| NcmaRenderer | 独立 DX11 DLL、参考 GPU 资源、PBR/级联 PCSS/HDR/contact shadow/tonemap、GPU 完成等待、读回、时间戳与 Debug Layer | 仅 cube/ground 参考；无 Vulkan、IBL、FBX GPU 蒙皮、通用 Shader 编译/资源 API |
| Ncma.Rendering | C# 默认流程、强类型图/缓存/契约验证、配置、管线生命周期、复制 Transform 模型数据 | 支持的 GPU 操作集限定为 shadow/geometry/tonemap/clear |
| Ncma.Editor.App | 同步 C# Main、可见预览/隐藏 smoke、中文展示和非场景输入诊断、规范释放 | 场景/Inspector/Play/MCP 审批等业务面板在 M2.5 |

Renderer 不引用 Scene/Core/Editor/hostfxr。图顺序和定制由 C# 控制；C++ 不保存产品默认图，
只保留参考 Shader、GPU 资源和数值执行。GUI 的 Device/Context 桥是 native-private v1，托管层没有 COM 指针。

## 2. 精确首期契约

- common API 56 字节，平台表 120、GUI 表 104、Renderer 表 136；x64 C ABI 1.0。
- 原玩法/场景 host bridge 5/6 未改版；新入口不使用该桥或 hostfxr。
- 输入最多 4096 条，GUI 8192 项/2MiB 输入、256 条/64KiB 文字输出。
- Renderer 最多 64 Pass、8 个参考资源组、4096×4096 framebuffer；图最多 32 个逻辑资源。
- 渲染 Frame 112、Pass 32、Stats 88 字节。矩阵：列主序、列向量、LH/+Y up/metres；
  System.Numerics 行向量矩阵按行展平后作为其列主序转置解释，translation 位于 12..14。
- 同步复制/提交不等于 GPU 完成。显式 owner-thread wait 使用 event query，最多 2 秒；
  超时保留资源和 DLL，不强制释放。设备故障 fail-stop，不承诺恢复。
- 每递增 frame 一个批次、GUI 合成、一个 Present；GUI 拒绝 submit 前或重复 GPU 合成。
- 强类型图拒绝循环、未初始化读取、输出/格式/尺寸/Shader 契约、能力和预算错误。
  图不实现资源别名、异步队列、历史缓冲或任意纹理组合。
- 默认/参数覆盖/ExposureFeature/ExposureToneStage/ClearPipeline 共用公开接口；
  Feature 和阶段替换使用注册的参考 tonemap 契约，不宣称任意用户 Shader 或完整 NSRP。

## 3. 配置与 Agent 路径

RenderConfiguration 是显式注册的 C# 值组件 ncma.render.configuration（版本 1），
可挂在普通扁平 GameObject；不是原生对象、场景树或网络同步机制。
它保存配置 UUID、稳定已注册 pipeline 类型、曝光/材质/有限定制参数，没有 GPU handle/程序集路径。
没有该组件时使用官方参考默认值；候选入口出现多个配置时保留上一有效配置并记录歧义诊断。

旧对照 Host 同样注册这一纯托管数据 schema，可保持配置持久化；其旧绘图逻辑不因此成为新可编程管线。
配置修改使用现有 ncma.scene.transaction/set_component；保存到现有 .ncmascene JSON v1，
复用单一 Core revision、范围授权和 Undo/Redo。没有第二份渲染撤销栈或旧格式兼容。
无效类型/非有限参数在提交前拒绝。候选 GPU 计划建立失败保留上一有效计划；
设备故障不被当成可回滚事务。普通 uniform 更新复用拓扑和资源，旧资源在 GPU 完成后同步退休，
未实现异步延迟回收优化。

新增可信启动注册的只读能力：

- ncma.render.inspect_pipeline：生效代次/参考操作集。
- ncma.render.inspect_graph：复制资源契约和有序 Pass。
- ncma.render.get_profile：CPU/GPU 测量、ABI 计数和验证状态。

注册函数不是 MCP 工具，只允许 ReadOnly 描述和有界 JSON schema；World 读取范围禁止写入。
未新增 eval、Shader 文本执行、任意程序集加载、自动批准、render.propose_settings 等工具。
新增配置复用原有 scoped transaction 的权限/revision 检查；真实新入口 MCP 面板/stdio 迁移仍属 M2.5。

## 4. 自动测试证据

规范入口：Build.bat -Configuration Debug、Build.bat -Configuration Release。
每配置 25 项 CTest（7 原生 + 18 managed/editor），managed/native smoke，Python inspect 和 24 项 unittest。
新代码构建零警告/错误；日志保存在 out/verification/m2-h3-h4-{debug,release}-final.log。
Application.Tests 17、Interop.Tests 11、Presentation.Tests 9；Rendering 专项包含图/配置/像素/资源回归。

渲染硬件：NVIDIA GeForce RTX 5060 Ti；D3D feature level 11.1；
驱动版本的原始 LARGE_INTEGER 编码、实际测量见下列 JSON，不把它猜测为厂商版本号。

- out/verification/m2/render-Debug/render-results.json
- out/verification/m2/render-Release/render-results.json

固定 fixture：256×256、identity 模型、原参考相机、2048×2048×4 阴影、VSync off。
**运行前容差：RGBA 每通道最大差 ≤4/255，平均差 ≤0.1/255；实际两配置均为 0。**
旧路径独立进程通过原 EditorApplication Shader/图生成 baseline；
测试辅助代码只固定姿态/视口并读回，不用新 kernel 替代旧画图路径。

自动覆盖：

- 模块/窗口/GUI/Renderer 初始化、依赖、错误线程、错误/陈旧句柄、长度/预算与失败保留。
- 10000 输入事件压力、溢出/refocus、同 poll press/release、合成控件点击、
  中文 UTF-8 修改/提交和 Escape 取消；CPU 字体 100/150/200%。
- 真实 DX11 默认预览/GUI 中文字体合成、resize 256→320×200→256、
  GPU 字体重建、单 Present 和 Debug Layer 错误/警告均为 0。
- 32 次 Renderer + GUI + window 创建关闭，GPU 完成等待和模块 live resources 归零。
- 四级定制产生真实不同像素，恢复默认/配置 Undo 后回到参考图。
- 配置 UUID 持久化、默认拒写、错误 scope/revision、共享 Undo/Redo、候选验证失败保留。
- 缓存构图/编码 64 帧当前 owner-thread 托管分配为 0；GPU 时间戳返回有效样本。
  同时记录该区间的全进程托管分配。此区间只涵盖缓存管线 submit/present，不涵盖完整 GUI、日志、World 快照或完整进程生命周期。
- 4096 个无渲染组件对象存在的文档下，参考批次仍为一次 submit。
  **不是 4096 个模型实际绘制 benchmark，也不是完整场景提取性能证明。**
- Shader 编译拒绝和 white-box device-lost 状态注入通过。**没有制造真实驱动 reset/device removal。**

读回图在 render-{Debug,Release}/reference.bmp、reference-restored.bmp、gui-composited.bmp。
模拟输入/DPI 和 BMP 不能替代下面的真实人工验收。

## 5. 候选预览与未完成门禁

候选程序：out/verification/m2/candidate/Release/Ncma.Editor.App.exe --preview。
不带 --preview/--smoke-test 时必须显式 --project <绝对 .ncmaproject 路径>；
项目显式请求 Vulkan/Null 时返回 renderer_unimplemented，不自动降级；自定义项目插件覆盖暂返回明确未实施。
不能把预览模式当成完整游戏编辑器。预览只有参考模型和中文输入诊断，业务面板尚未迁移。

请人工核对：

1. 真机中文输入法组合/提交/取消、鼠标焦点和 Esc。
2. 实际 100/150/200% DPI/跨显示器、窗口缩放、最小化恢复，GUI 与视口坐标。
3. 窗口标题栏小/大图标、中文清晰度，关闭后无残留进程/资源。
4. M1 的第三方 MCP 客户端/旧业务 UI 验收仍待单独记录。

按用户要求，M2.5 必须在联合测试门禁通过后开始；人工项未完成时不擅自将 H3/H4 标为整体通过。
后续用户确认“先完成业务迁移，再删除旧入口”；据此启动 M2.5 候选业务首切片，见 M2_5_DELIVERY_REPORT.md。此后续指令不等于人工验收通过；H3/H4 完整门禁继续标为待验收，默认路径和旧入口保留。
