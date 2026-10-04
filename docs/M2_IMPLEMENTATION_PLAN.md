# M2 总体实施方案：C# 应用入口与原生插件边界

更新日期：2026-10-04。基线提交：a1ac9d444063721ed27d5791b059530c49286489。
性质：已按用户指令进入实施。M2.1/M2.2 自动门禁通过；M2.3/M2.4 候选实现及联合自动测试见 [交付报告](M2_3_4_TEST_REPORT.md)，人工验收尚未完成。M2.5 候选面板/预览/偏好/Console 已迁移，H5 人工未过，见 [记录](M2_5_H5_DELIVERY_REPORT.md)；M2.6 独立 Physics 实现见 [交付记录](M2_6_DELIVERY_REPORT.md)；M2.7 候选 Player/Headless 与框架依赖包见 [交付记录](M2_7_DELIVERY_REPORT.md)，完整 H7 未通过；M2.8 总验收与清理预检按未来方案执行，H8 未完成。

## 1. 目标、前置与不变量

M2 结束时，C# 是 Editor/Player/Headless 的应用入口、主循环和服务生命周期所有者；C++ 只保留性能内核及已确认暂时保留的 GLFW/Dear ImGui 薄适配。
不重写 M1 的 World、SceneDocument、Editor.Core、PlaySession 或 MCP 权限模型。

M1 自动验收已通过：Debug/Release 各 14 项 CTest、53 项 Gameplay、24 项 Python 回归。
第三方 MCP 客户端及人工 UI 验收仍待完成；用户已要求推进 M2 候选实现，默认入口仍不得提前切换。
现有 .vs、.user 本机修改保留；未安装 SDK/Graphics Tools，未提交或推送。

固定边界：

- C# 是唯一 World、编辑文档、命令/历史、脚本与调度所有者。GameObject 是扁平空容器，可无 Transform。
- Python 只用于可选工具/AI/特殊模块；不新增 Python Behaviour、游戏脚本或对象语言选择。
- 原生模块仅保存窗口/GPU/物理/数值资源。句柄是资源身份，不是第二份通用 World。
- Editor 和 Agent 共用同一 EditSession、revision、草稿、Undo/Redo、授权与安全队列。
- .ncmascene JSON v1、持久 UUID 与既有运行语义保持；不恢复 .ncscene、旧 SceneSnapshot、Node 或 Actor。
- 网络独立；本阶段不新增网络组件、同步推理、Python 传输或任意代码/文件 MCP。
- 旧外壳只作为迁移验收对照，不能与新入口同进程争抢窗口、CLR、World 或插件。
- 用户确认：M2 结束后统一清除旧入口。M2.1–M2.8 期间保留受控对照源码/产物，不分阶段删除旧入口；H7 可切换正式入口，H8 验收新路径并核定精确清理清单。全部 M2 门禁通过后执行一次性清理，再运行完整双配置回归；这不豁免尚未完成的人工验收。

## 2. 两项需要锁定的设计选择

### UI：默认继续 C++/Dear ImGui

延续用户已确认的 C++/ImGui 方案：C# 定义面板状态、语义视图与控件意图，原生 GUI 仅画控件、处理控件交互并输出复制的事件。
不要求在 M2 更换 ImGui.NET，不逐控件 P/Invoke，不让原生控件直接修改 World。
本地 vendored Dear ImGui 为 1.91.9b；本次未找到 docking 宏，故不宣称已有自由 docking。
自由 docking、多原生窗口与 C# UI 绑定作为另行验证的扩展，不是 M2 首入口的必需项。

这对旧 ROADMAP 的“先验证 C# ImGui 绑定”作出细化：先验证 C# 驱动的原生 GUI 适配，而非强制换绑定。
保留的是展示适配，不保留 C++ EditorApplication 的高层业务。依据官方的输入/绘制后端分离机制设计，不是照搬第三方绑定：
[Dear ImGui 官方仓库](https://github.com/ocornut/imgui)。

### 运行时：先确认 TFM，再实施

当前工程是 net8.0；建议 M2.1 单独评估并确认 .NET 10 LTS，不在入口/ABI 切换同时静默升级。
官方截至本次查询列明 .NET 8 于 2026-11-10 结束支持，.NET 10 LTS 至 2028-11-14：
[.NET 支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)。

用户未确认升级前保留现有 TFM 作开发基线，不把过渡 net8 发布当长期发布方案。
升级须独立提交与完整双配置门禁；先预检 VS/Rider、SDK、目标 OS、hostfxr 对照路径和 Python smoke。
不自动安装 SDK、不自动删除旧运行时，也不将 SDK 9 等同于程序集运行时 9。
正式发布时确认受支持的 OS/运行时组合及补丁；Windows 版本的本机可运行不等于正式支持矩阵。

## 3. 小阶段与依赖

| 顺序 | 小阶段 | 核心交付 | 门禁 |
| --- | --- | --- | --- |
| 1 | [M2.1](M2_1_IMPLEMENTATION_PLAN.md) 应用服务 | 非静态项目/脚本/编辑/Play 服务；假平台主循环 | H1：旧入口无回归，headless 服务可独立运行 |
| 2 | [M2.2](M2_2_IMPLEMENTATION_PLAN.md) 插件 ABI/加载器 | 公共 C ABI、路径/版本/所有权/错误、假插件故障测试 | H2：边界与初始化回退可验证 |
| 3 | [M2.3](M2_3_IMPLEMENTATION_PLAN.md) 平台/输入/GUI 适配 | C# 主线程 + GLFW 窗口/事件 + 原生 GUI 协议 | H3：隐藏窗口/输入/关闭通过，不切默认入口 |
| 4 | [M2.4](M2_4_IMPLEMENTATION_PLAN.md) Renderer | 独立 DX11 DLL、参考预览、GUI 合成与资源管理 | H4：实际渲染及 D3D11 验证，不宣称 Vulkan |
| 5 | [M2.5](M2_5_IMPLEMENTATION_PLAN.md) Editor | 托管面板业务、MCP、草稿、脚本与既有预览迁移 | H5：新旧功能对等、权限/历史不回归 |
| 6 | [M2.6](M2_6_IMPLEMENTATION_PLAN.md) Physics | 薄 Box2D/Jolt 插件 + C# 高层物理服务 | H6：独立求解/释放；不提前接入 Play World |
| 7 | [M2.7](M2_7_IMPLEMENTATION_PLAN.md) Player/发布 | GUI-less Player、headless、发布布局、默认入口切换 | H7：离开源码目录可启动，Python 非必需 |
| 8 | [M2.8](M2_8_IMPLEMENTATION_PLAN.md) 总验收/清理预检 | 双配置/故障/人工/性能报告与精确旧入口清单 | H8：旧入口不进入生产，新路径全部验收；M2 结束后统一清理并复测 |

推荐串行。Renderer、GUI 与平台共享资源生命周期，不能各自另建窗口/设备。
M2.6 可在 H2 后单独验证，但正式合并在 H5 后，避免主入口与物理改动同时制造回归。
H3/H4 的候选程序只在 out/verification/m2/candidate/；当前 out/bin/NcmaEngine.exe 到 H7 前不覆盖。

## 4. 建议模块与依赖方向

| 建议模块 | 职责 | 禁止依赖 |
| --- | --- | --- |
| Ncma.Application | owner-thread 主循环、启动/关闭、项目配置、时间/日志、服务组合 | Editor.Core、GPU API、Python |
| Ncma.Application.Runtime | 共享文档/脚本 catalog lease/Play 生命周期；Editor 克隆与 Player 直接运行 | Editor/Core/Transport/Gui、native 服务 |
| Ncma.Scripting | 可信程序集 catalog、SDK 身份、Export、collectible ALC | Editor UI、hostfxr、原生场景 |
| Ncma.Editor.Services | 项目/选择/文档/Play/面板/预览业务 | D3D/Vulkan/Jolt/GLFW 类型 |
| Ncma.Editor.App | Editor 入口及组合根，程序集名 NcmaEngine | 原生高层 World/Undo |
| Ncma.Player.App | Player/headless 入口，程序集名 NcmaPlayer | Editor.Core/Transport/Gui |
| Ncma.Interop | 公共 ABI DTO、装载器、模块租约、释放队列 | Runtime.World、Editor.Core |
| Ncma.Rendering / Ncma.Physics | 原生模块的有界客户端与托管服务 | 场景权威存储、原生组件池 |
| NcmaPlatform / NcmaGui | GLFW 与 Dear ImGui 原生展示/输入适配 | hostfxr、编辑命令/脚本 |
| NcmaRenderer / NcmaPhysics | 原生 DX11 与求解器资源/数值计算 | 通用 World、Editor.Core |
| 现有 Runtime/Scene/Managed/Gameplay/Core/Transport/Mcp | 继续复用 | 新增反向应用/平台依赖 |

这是建议的工程划分，实施时允许合并薄项目，但必须保持依赖测试。
Sdk Behaviour 的基类身份只来自默认上下文。Catalog 不能持有 GUI/endpoint/已卸载插件。
不把 Application 或 Player 放入 Editor.Services，否则 Player 会间接携带编辑器权限服务。

## 5. 主循环与生命周期契约

图形应用使用同步 Main 主线程；不在创建窗口后 await 任意任务并丢失 owner thread。
后台工作完成只排队；主线程从有界队列取复制的结果。

单帧建议顺序：

1. 复制平台事件、关闭/焦点/resize 状态，启动 GUI 帧。
2. 接收上次 GUI 事件；执行人工意图/控制，结束或取消草稿。
3. 同步 EditSession 的冻结/generation/catalog，再 pump MCP（仍是每帧最多 4 项/软 2ms）。
4. 根据本 GUI 帧捕获状态过滤玩法输入，提交 session/sequence 校验过的 InputFrame。
5. 唯一 PlaySession.AdvanceFrame 执行 0/1/最多 8 步，再取只读插值视图。
6. C# 提取参考预览与 GUI 视图，原生画图/Present；新 GUI 编辑意图在下一帧应用。

最后一点确保绘制期间不重入 World。重要权限也在实际应用时重检，不靠上一帧 enabled。
关闭请求优先，提交中已经成功的事务不回滚。最小化时暂停图形提交，仍以有界周期处理关闭/MCP；
不隐式暂停游戏，是否暂停由显式应用策略决定，时间仍遵守 PlaySession 丢时语义。

关闭顺序：停止接收新意图/授权 → 断开 MCP、取消待执行工作 → Stop Play →
释放脚本与预览会话 → 停止/等待物理任务 → GUI/字体/视口 → Renderer GPU 资源 →
窗口/GLFW → 有序卸载模块 → 关闭日志。
各步骤记录错误仍继续安全清理；不能在 DLL 卸载后由 finalizer 调用其函数地址。

## 6. 当前性能限制与 M2 预算

M1 的 4096 对象参考压力单步约 138–156ms、约 91MB 托管分配。M2 反转入口不等于解决它。
M2 测量分离 simulation、UI view、ABI 调用、Renderer、MCP、GC；保持同样 fixture 对比，不以 FPS 隐藏失败原子性代价。
不把全面 ECS/增量快照优化暗中加入主入口迁移；若同 fixture 稳定恶化，应阻止 H8 并定位。

建议初值（实施测量后锁定，所有值有硬上限）：

| 通道 | 建议上限 / 行为 |
| --- | --- |
| 平台事件 | 4096 条 / 帧；溢出显式标记并重同步 held、清边沿 |
| GUI 视图 | 2MiB / 8192 描述项；虚拟化列表，超限不静默截断授权范围 |
| GUI 事件 | 256 条 / 256KiB；溢出取消当前交互，不假装已提交 |
| Native 错误文字 | caller-owned UTF-8 ≤16KiB；长度明确 |
| 渲染读对象 | 4096 项；非空间对象不生成模型矩阵；资源大小另按设备能力硬限 |
| 物理 body 批次 | 4096 项；world 与 2D/3D 分离，禁止无限任务队列 |
| 关闭等待 | 单服务建议 5s；超时不强卸载仍在运行的 DLL，必要时留待进程退出 |

M1 的文档/命令/history/IPC/审计预算原样保留，不因为新入口换了进程结构就扩大授权。

## 7. 版本与发布原则

| 契约 | M1 实际 | M2 计划 |
| --- | --- | --- |
| Native aggregate | 2 | 不恢复 world exports；按数值内核拆分后判断是否退出 |
| Animation/Character | 1/1 | M2.5 如替换高层命令或返回内存契约，须新版本并迁移全部消费者 |
| Gameplay/Scene Host | 5/6 | 过渡对照保留；新应用不调用，M2 结束后统一删除已替代桥接 |
| 公共 Module / Platform / Renderer；GUI / Physics | 1.0；GUI / Physics 1.1 | 候选独立模块已实现，不等于 Native aggregate 版本；生产入口未切换 |
| 场景 JSON / capability / IPC | 1/2/1 | 保持既有语义；真有破坏性变化单独升级 |
| manifest | 10 | 只在实际交付时递增；本次方案不改能力状态 |

开发先用普通目录式 framework-dependent apphost；正式验证 self-contained win-x64。
不默认 NativeAOT、裁剪或单文件，先保留动态脚本/反射与独立 DLL 故障诊断。发布模型依据：
[.NET 发布官方文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/)。
NcmaEngine.exe 保持 out/bin/；Player 使用 out/player/；部署内容/依赖清单由 H7 锁定。
Native 插件固定目录、明确加载路径，不依赖 cwd/PATH 找到随机同名 DLL。

## 8. 交付要求

每个小阶段追加实际交付记录：日期/提交、变化和偏差、实际版本、测试命令、结果/日志、剩余限制。
规范入口始终 Build.bat，先 Core/Native/Architecture，随后 native、managed、ABI、Python 全矩阵。
新增项目进入 solution、Build.ps1 和 CTest；不靠手工 dotnet run 代替集成验证。
H8 建立 M2_DELIVERY_REPORT.md；本次不生成伪造的已通过报告。
