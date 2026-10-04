# M2.1：托管应用服务与宿主去静态化

更新日期：2026-10-04。状态：候选实施及自动 H1 测试通过；仍保留 net8.0 过渡基线与旧桥接对照。M1 人工验收仍待确认；总体边界见 [M2 总览](M2_IMPLEMENTATION_PLAN.md)。

实交付：Ncma.Application / Ncma.Scripting / Ncma.Editor.Services；Application.Tests 17 项，详见 [联合报告](M2_3_4_TEST_REPORT.md)。

## 1. 目标与本阶段终点

从 Ncma.Managed.Host 抽取可由 C# 应用直接调用的服务，不再把主机静态字段作为引擎服务入口。
旧 C++/hostfxr 在此阶段仍可运行，但只是这些服务的适配消费者；不复制另一套 SceneDocument/PlaySession。
终点是可测试的 Application + 项目上下文 + Scripting catalog + Editor session owner，不是已经发布托管编辑器。

## 2. 现有代码与建议新增项

重点来源：

- managed/Ncma.Managed.Host/NativeEntry.cs：s_ownerThread/s_loadContext/s_types/s_play、反射/Export 与 ALC。
- SceneEntry.cs：静态 s_scenes、SceneSession、World 门面/EditorEndpoint 归属。
- EditorEntry.cs/EditorCatalogEntry.cs/PlayViewsEntry.cs：可信 UI 意图与复制视图。
- 已有 Ncma.Gameplay.PlaySession、Editor.Core.EditSession 及 Editor.Transport.EditorEndpoint：直接复用。
- scripts/Build.ps1：存在硬编码 net8.0 输出路径，TFM 评估不能只改 csproj。

建议新增 Ncma.Application、Ncma.Scripting、Ncma.Editor.Services 及对应 Tests。
旧 Managed.Host 暂改为薄桥接，保留 bridge 5/6 的既有消费者测试；不让新服务再调用 NativeEntry。

## 3. 分步实施

### A. 基线/工具链锁定

记录 M1 提交、全部测试、当前 SDK/TFM/OS/ImGui 版本和结构布局。
评估 .NET 10 LTS：先确认用户与工具环境，再独立修改所有相关 TFM、runtimeconfig、Build/CMake 路径与 smoke。
升级与服务抽取分两次门禁；SDK 缺失只报告，不自动下载安装。未确认则继续 net8 过渡开发并记录发布风险。
新增 global.json/统一 TFM 属性如被采纳，应测试 SDK 缺失诊断和受控 roll-forward，禁止“碰巧某台机器能编译”。

### B. 提取可信脚本服务

提取实例化 ScriptCatalogService：LoadCandidate、Describe、Instantiate、CommitCandidate、Dispose。
catalog 描述是纯值复制；类型/factory 只供可信宿主。只加载项目明确配置的 C# DLL，不由 MCP 传入任意路径。
保持默认上下文 SDK 单份身份、隔离 Export 类型校验、读阶段构造预检、12 次弱引用释放测试。
将硬编码 Sample 路径改为 ProjectContext 的 gameplayAssembly；构造失败不能覆盖旧 catalog。
Play 的安全重载仍委托现有 PlaySession.Reload，不重新实现生命周期顺序或私有状态迁移。

### C. 提取编辑会话与应用组合根

EditorSessionOwner 拥有一份 EditSession、可选 Endpoint、一份隔离 PlaySession、catalog 租约与选择。
ProjectContext 是当前项目配置及受控根路径；文档替换继续使用既有 generation，而非换一个全局静态 token。
SessionService 明确 Edit/Play/Scripting 的创建、冻结、Stop、释放责任；不同时发布裸 World 与另一个 World wrapper。
旧 SceneSession 桥接借用同一服务，原生令牌只映射已有 owner，不再保有独立高层状态。

### D. 应用生命周期与错误/日志

ApplicationLifetime：Created → Starting → Running → Stopping → Stopped，初始化失败进入 Failed 并逆序释放。
要求所有应用/窗口相关调用在同步 Main 的 owner thread，后台 callback 只入有界队列。
IFrameClock 提供单调时间；测试 FakeClock；真实 dt 只计算一次，再传 PlaySession，不能 GUI 与游戏各算一套时间。
IApplicationLog 是统一托管结构化日志入口，使用有界环形 Console 视图和文件轮转。
原生 spdlog 仅留原生日志，主线程拉取复制事件归入同一 correlation；不使 Runtime 依赖日志框架或 spdlog。

### E. 项目配置最小闭环

建议 .ncmaproject JSON v1：project UUID/name/schemaVersion、startupScene、gameplayAssembly、renderer、明确插件清单。
持久路径项目相对；运行时配置可以显式选择 backend/headless。禁止未知字段、路径越界、重复插件 ID、无效 renderer。
ProjectConfiguration 与本机 EditorPreferences 分开：前者可版本管理，后者默认在 out/user/（忽略），不写入场景或随项目推送。
本阶段只做配置加载/验证与显式保存；不提供 Project New 向导、资产数据库或自动更新资源 UUID。
本机字体/布局配置与引擎 engine/config 下已配置 ini 的实际用途先核对，不新增随机 cwd 的 NcmaEditor.ini。

### F. 假平台主循环

定义 IPlatformPump、IRenderService、IEditorPresentation、IShutdownParticipant 等窄接口。
以 fake 服务完整执行时间、人工意图、MCP pump、输入、Play、视图、关闭顺序。
采用 owner-thread polling，不用 Timer/Task.Run 拥有 World，不在主循环中等待网络/模型结果。
非图形测试不加载 Native/GLFW/D3D/Python；本阶段新 CLI 只放候选输出。

## 4. 建议接口契约

- ProjectContext：ProjectId、规范根、schema、已解析启动场景/DLL/插件配置；失败不产生可用会话。
- ScriptCatalogSnapshot：Generation、完整 TypeId/Export 描述；不得含 CLR Type 给 Agent。
- EditorSessionOwner：Document、EditSession、Play 状态与显式 Start/Stop/Reload，Dispose 只能由 owner 调用。
- FrameContext：FrameId、monotonic dt、平台 seq、viewport/焦点/capture；不序列化为场景。
- ApplicationFailure：稳定 code、phase、service/plugin ID、可公开消息、内部 correlation；避免记录凭证。
- 宿主薄桥 Guard 继续异常转错误；新托管内部不用二进制往返查询自己已有的对象。

## 5. 测试与 H1 门禁

必须覆盖：

1. 两个独立服务实例无 catalog/Play/Document 串扰；不承诺 GUI 同时多实例。
2. 初始化每一步故障、逆序 Dispose、重复关闭、关闭时生命周期抛异常。
3. 异线程/回调重入、FakeClock 0/1/8 步、非法 dt；新循环不会双 Tick。
4. 脚本缺失/SDK 身份错误/预检失败/重载激活失败，弱引用释放。
5. 项目路径含空格/中文、未知字段、越界、错误启动场景，不修改旧现场。
6. Player 无 Editor 依赖的架构测试；fake headless 不加载 native。
7. bridge 5/6 对照 smoke 和全套 M1 语义仍通过。

H1：Build.bat Debug 全矩阵通过、新代码零警告；若升级 TFM，则升级单独 Debug/Release 全门禁。
不因已有 headless 测试就标记 Editor/Player 完成。

## 6. 交付与清理

提交服务、测试、配置 schema、生命周期说明及现有桥接改为服务适配的变更。
此时不能删除 Managed.Host/hostfxr 测试，不能改默认 out/bin 生产入口。
后续 H5 直接复用服务；H8 才移除已失去生产消费者的旧静态桥接。
