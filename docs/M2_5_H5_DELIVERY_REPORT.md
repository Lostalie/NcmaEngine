# M2.5 / H5 当前交付与验收

日期：2026-10-04。**候选 A–F 业务迁移已实现；H5 自动门禁已通过，人工门禁未关闭。**
正式 out/bin/NcmaEngine.exe 未切换，旧入口/比较桥未删除，未提交或推送。
此文为当前记录；M2_5_DELIVERY_REPORT.md / M2_5_FBX_DELIVERY_REPORT.md 保留早期批次与当时计数。

## 1. 迁移边界

| 业务 | 当前落地 | 权威 / 限制 |
| --- | --- | --- |
| New/Open/Save、Dirty、扁平列表/UUID选择、Inspector/绑定/Export | 候选 C# 命令与草稿、Unicode 大 JSON 分页 | 唯一 Editor.Core / EditSession；只有 .ncmascene JSON v1 |
| Play/暂停/单步/重载/故障展示 | 复用托管 PlaySession/Scripting | 隔离 Play；只读 OnUpdate、失败步保护；无新 native World |
| 活动场景 MCP | 唯一 EditSession 的 Endpoint、可信本地配对/完整提案审批/撤权 | 默认关闭/拒写；UI Undo 与 remote Redo 同一历史；不提供 eval/build/load/pair/approve 工具 |
| FBX 独立预览 | C# 时钟/片段/暂停/8 项资源租约历史；CPU 蒙皮线框、Orbit、骨架与报告 | character ABI 2；最多显示一万三角形；完整报告按 Rune 分块/分页、不截断；导入仍同步 |
| Action Animation Lab | C# 时钟、动作状态/中断、Blend 源姿态、Root/Notify 消费、暂停/单步/128 项历史与骨架画布 | animation ABI 2；仅内置 12 骨骼/4 clip；不是场景 Animator、战斗系统或动画图执行器 |
| Reference PBR/软阴影控件 | 基色、金属度/粗糙度、曝光/光照/环境、级联 bias/distance/lambda/filter/radius、接触阴影参数 | 走 Core 草稿/共享 Undo；4×2048 reference；不是材质资产创作或 Vulkan 实现 |
| 本机偏好 | Dark/Light/Classic、侧栏宽度/工具栏高度、面板开关、字体/字号、路径；16 项独立历史 | out/user/editor/preferences.json；原子保存；字体需重启；固定工作区，不支持自由 docking |
| 文件选择 / 快捷键 | 可信本机 Windows scene/FBX picker；Ctrl N/O/S/Z/Y、Shift Z、Shift R | 平台层仅返回路径；捕获/焦点/草稿/Play 保护；复用 Core 与替换确认，不增加 Agent 文件加载权限 |
| Console | 有界业务日志 + private spdlog 复制页；8 条/页、全文 Rune 分块、序号缓存 | Native ring 512 条、每读最多 32 条，掉页结构化报告；不借用 native string、不污染 MCP stdout |

场景、FBX、动作预览和本机设置是明确隔离的命令域；后面三个不能成为第二份 Scene Undo。
平台/API 头仍位于原生模块；candidate Editor/Player 不调用 NativeEntry 或自定义 hostfxr。
Python 只维护隔离工具会话策略；没有 Python Gameplay、live GameObject 或同步 AI 调度。

## 2. 实际 ABI 与消费者

| 契约 | 版本 / 布局 | 实际行为 |
| --- | --- | --- |
| Platform/common | 1.0 | 窗口/输入/模块；未改变旧场景桥 |
| GUI | 1.2，表 104 字节 | 增加无事件 CanvasBegin/Lines/End 与 Theme；兼容已知 1.0/1.1 控件；未知 1.3 拒绝 |
| Renderer | 1.1，表 144 字节；ReferenceSettings 76 字节 | 新增有界数值 ConfigureReference；旧 1.0 返回原 136 字节表；未知 1.2 拒绝 |
| Physics | 1.1 | 原有薄求解器 / C# 高层服务；不新增场景物理 |
| character resource | 2 | 不可变导入资源；复制报告/骨架/索引/采样；旧 1 拒绝 |
| animation resource | 2 | create/destroy/read_library/sample/motion/notifies/read_error；旧 1 拒绝；command/inspect 仅拒绝桩 |
| native diagnostics | read_v1，JSON schemaVersion 1 | afterSequence + bounded maximum + caller-owned UTF-8 buffer |

animation 2 的 create 只创建不可变内置 demo 库，不提供通用 Skeleton/Clip 资产创建 API。
sample 的 pose 是每骨骼 10 floats local Transform，随后每骨骼 16 floats column-major model matrix。
motion/Notify 接收显式时间区间；native 不维护暂停、动作状态、时钟、通知消费或 Undo。
C# Managed SDK、candidate、smoke、native tests 与隔离 Python animation/MCP 消费者已同步升级。
旧直接 C++ Workspace 只保留作测试/旧入口对照，不在 NcmaNative 的 ABI 2 数值资源中保存高层策略。
GUI 每批最多 16 条复制线段，整帧先校验再绘制；native 不持有编辑器文档或决定提交权限。
Renderer 只在配置 generation 改变时更新 uniforms，保留 cached-submit 分配门禁。

M2.7 Editor 包按需部署 NcmaNative.dll，resourceKernels 分别记录 character / animation ABI 2；
GUI/Renderer manifest 已升级，含 ufbx 许可证。Null/DX11 Player 不部署该资源 DLL。
发布仍是 framework-dependent candidate；manualAcceptance / selfContainedVerified 均为 false。

## 3. 自动门禁及证据

规范命令（由 Build.bat 初始化 VS，不要求用户手动运行 Ninja）：

~~~bat
Build.bat -Configuration Debug
Build.bat -Configuration Release
~~~

| 验证项 | Debug | Release |
| --- | --- | --- |
| CTest（8 native + 21 managed/editor） | 29/29 | 29/29 |
| Editor.Services（CTest 内部用例，不重复计入总数） | 34/34 | 34/34 |
| Player（CTest 内部用例） | 22 通过 | 22 通过 |
| Managed SDK + native animation 2 smoke | 通过 | 通过 |
| Python inspect / unittest | 通过 / 28 项 | 通过 / 28 项 |
| DX11 reference/GUI 像素 max / mean | 0 / 0 | 0 / 0 |
| DX11 reference/GUI / Player API errors / warnings | 0 / 0 | 0 / 0 |

关键新增证据：

- 旧 ManagedSceneClient 桥与新 EditorWorkspace **独立进程**：同一 UUID/初始文档，Open/选择/改名/草稿预览-取消/Transform/Undo/Redo/精确删除-恢复/Save/New-Undo，比较完整文档、revision、Dirty、selection、history、busy/frozen/invalidation。无序列化运行时句柄；不比较会话 UUID、文件目的路径或显示标签。
- C# 和 Python 各与旧 ActionAnimationWorkspace 输出比较同一 15 命令序列及初始状态，递归数值误差 < 2e-5；覆盖中断、动作余时、Root/Notify、暂停、Reset、Undo/Redo、128 上限、非法/旧 revision/异线程/释放与旧 ABI 拒绝。
- FBX sausage fixture：576 三角形、全蒙皮线框、Orbit 变化/有限坐标、复制 parent/name/完整报告；保留 ufbx reference 误差 < 0.002m 与资源释放/失败保护测试。
- GUI 1.2 真实 canvas payload 校验失败后可恢复、绘制产生实际顶点且不产事件；候选业务视图/FBX 画布经真实 DX11 合成/读回，API validation 0/0。
- Reference Core shadow drafts/共享 Undo/非法参数拒绝，实际 GPU 像素随基色与阴影配置变化；cached submit 原有分配断言不放宽。
- 偏好严格/重复/未知字段、独立历史、锁文件失败保留字节/状态/revision；文件 picker 使用可注入选择器测试取消/失败/命令保护，不冒充实际系统对话框验收。
- private spdlog ring 520 次资源创建溢出，按序复制、不重复、掉页报告、managed Console 缓存；锁日志文件时 UI 错误仍可读且场景保持，长错误截断不切开 surrogate；所有真实 stdio 测试保持干净 stdout。
- 既有 Core/transport 完整权限/重试/撤权/历史/事务矩阵、managed owner 的真实 named pipe → 可信 UI 审批 → 场景修改 → UI Undo → remote Redo 均保留通过。这不等于第三方 MCP 客户端连接可见候选的人工闭环。

构建日志：out/verification/m2-5-h5-debug.log、out/verification/m2-5-h5-release.log。
CTest 明细已复制保留：out/verification/m2-5-h5-{debug,release}-ctest.log；
原文件 out/build/windows-ninja-{debug,release}/Testing/Temporary/LastTest.log 将随后续测试更新。
包索引：out/verification/m2-7/{Debug,Release}/packages.json。
测试产物/图像均在忽略的 out/verification 下；文档记录稳定计数，生成日志未加入版本库。

本批曾发现 per-frame uniforms 的 ValueType 比较分配回归，已改为 generation 变化时配置；未放宽零/有界分配门禁。
首次 Windows 锁偏好文件返回 UnauthorizedAccessException，专项确认此类写失败同样必须保留状态/字节/历史。
一次旧 Player 场景 File.Replace 测试出现 UnauthorizedAccessException；单独重跑与最终完整门禁以实际结果为准，
最终 Debug/Release 全矩阵均通过；未证明成因、未修改 SceneDocumentFiles 或增加隐式重试，不宣称已诊断系统原因。
最终规范构建均退出 0，新代码编译警告/错误为 0，git diff --check 无空白错误；Git 全局 ignore 权限警告 / LF-CRLF 提示另列为本机环境提示，不修改用户 Git 配置。

保留证据 SHA-256（后续规范构建可重新生成日志，不应误用此摘要核对新的批次）：

| 文件（out/verification/） | SHA-256 |
| --- | --- |
| m2-5-h5-debug.log | DCB3C5E8D25B1D9981D1EC648A5D4C5094153150B28EC908765E218F0DAF851D |
| m2-5-h5-release.log | D5484439E8E4A45E3D9C6EC250FB588EDE6010261D030096086B13413B083B05 |
| m2-5-h5-debug-ctest.log | AA399E8746ECEB8A2AC7425763A3322FC1B78B9E17E50985716BE518613FACC7 |
| m2-5-h5-release-ctest.log | 3847DC65BD6B5B5525B16A6D55D60295D97C360D686FB3ABE9B134383E73CFA3 |

## 4. H5 仍需真实人工验收（不能用自动结果勾选）

当前没有可核验的真机 / 第三方客户端记录，也未模拟操作系统人工操作。故 **H5 不可标记完成**。
启动候选（不是 out/bin 下旧入口）：

~~~powershell
& 'F:\NcmaEngine\out\verification\m2\candidate\Release\NcmaEngine.exe' --editor
~~~

带项目/可信 Gameplay/启动场景/MCP 请使用 --editor --project <绝对 .ncmaproject 路径>。
样例候选包路径以 packages.json 中 editor 项为准；不自动安装任何客户端配置。

| 人工项 | 最短操作与通过标准 | 当前 |
| --- | --- | --- |
| 输入法/草稿 | 中文名称、长 JSON、路径连续输入/选字；Esc、失焦、换对象、Play 均取消未提交草稿；非法提交保护；无重复输入 | 待验 |
| 键鼠/快捷键/对话框 | Inspector 捕获时 Ctrl Z 不撤销场景；未捕获时正确 Undo/Redo；scene/FBX picker 中文空格路径、取消、失败、未保存替换确认 | 待验 |
| 跨屏 DPI/窗口 | 不同缩放屏间拖动、resize、最小化/恢复；文字/点击区域一致、无黑屏/验证错误；字体/设置重启后保留 | 待验 |
| 动作/FBX/UI | Attack/Dodge/暂停/Step/独立 Undo、FBX Spin/Wiggle 与 Orbit/线框/报告翻页；场景历史不受影响；预览开关不丢资源 | 待验 |
| Play/重载/关闭 | 实际配置项目 Running/Paused/Step、输入失焦、失败重载保留暂停；MCP 有连接/队列时关闭，释放资源并移除自身 descriptor | 待验 |
| 第三方 MCP 到可见候选 | 记录客户端名称/版本、脱敏启动配置；从实际 helper stdio → pipe 配对，只读/默认拒写，完整审批 rename → UI Undo → MCP Redo → validate | 待验 |
| 可见审批风险/撤权 | 非目标/旧提案/删除错误 UUID 不可批准；精确 UUID 删除后可按授权历史恢复；撤权/过期/Play/重载/generation 改变拒绝后续写入 | 待验 |

记录模板：日期 / candidate 配置与包路径 / 客户端名称版本 / Windows 与输入法 /
显示器缩放 / 项目与场景 UUID（脱敏） / 逐项步骤与实际结果 / 日志或截图路径 / 发现的问题。
helper 配置与协议细节见 [活动编辑器 MCP](EDITOR_MCP.md)，其中单独说明候选入口，不能误测正式旧入口。
H3/H4 共享人工项同样未过；不能因本阶段代码补齐而重写其历史状态。

## 5. 下一步范围

收到并核验上述人工记录、修复发现问题后，才可关闭 H5。
之后继续 H7 自包含/目标环境与正式入口切换、H8 总验收；旧入口统一清理仍遵循用户已确定的顺序。
本阶段不扩展 M3 资产数据库、M4 场景物理、M5 Animator/节点图、M6 Figma UI、Vulkan 或实时 AI 服务。
