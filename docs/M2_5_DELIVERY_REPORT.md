# M2.5 首切片：托管编辑器业务迁移

日期：2026-10-04。状态：A/B/C/D 首切片已落地；**完整 M2.5/H5 尚未完成，旧入口未删除、默认入口未切换**。
此文保留历史首切片与 FBX 批次，不代表最新功能/ABI/计数；后续 A–F 迁移见 [当前 H5 交付记录](M2_5_H5_DELIVERY_REPORT.md)。

本轮按用户“先完成业务迁移，再删除旧入口”的指令推进。H3/H4 的真实输入法、跨屏 DPI、最小化恢复及第三方 MCP 客户端验收仍未通过，不因推进首切片而改写人工状态。

## 1. 实际交付

- EditorWorkspace：唯一 EditSession 的可信本地 UI 路由，不新建 World、历史或权限系统。New/Open/Save、Dirty/关联文件、替换未保存文档确认、Undo/Redo、UUID 选择和精确 UUID 删除确认。启动项目使用已保存文件作为基线，不把初始加载加入用户历史。
- 扁平对象列表分页 32 行；不创建场景树/父子关系。创建对象默认无 Transform。按 revision/generation 缓存完整文档读取，读取与 Inspector 输出是复制数据，不能修改权威存储。
- 名称、Transform、短 JSON 值组件、绑定启停/移除、可信 catalog 挂载、Export 编辑经既有命令路由。组件/绑定每页 8 项，Export 每页 16 项，catalog 每页 8 项；未知元数据保持，不静默截断文档。
- Inspector activate/change/commit/cancel 使用 Core 草稿。预览可影响选中参考模型，不泄漏到 committed World 或 Play 克隆。未完成 JSON 输入可留在控件，非法提交取消；Quaternion/非有限数字等由 validator 拒绝。未改变值的控件失活也结束草稿，不把 EditSession 留在 busy。
- 场景修改仍复用 .ncmascene JSON v1、原子保存、内容指纹、完整快照、revision、共享历史和 UUID 恢复；文件路径是人工 UI 参数，不加入 Agent Schema。
- Play/Stop/Pause/Resume/Step/Restart、配置程序集重载复用独立 PlaySession/Scripting。GUI capture 过滤输入，提交有序 InputFrame；只对 Running 执行 AdvanceFrame，Faulted 留在 UI 可 Stop/Restart，不自动关闭编辑器或恢复。视口只使用选中 Transform/已有 Play 插值视图控制参考 cube，不宣称资产网格渲染。
- EditorAuthorizationController + 候选 UI：默认关闭 MCP，项目配置确定 root 后可人工启用/停用。配对/拒绝/撤权是本地 UI 操作。展示完整提案 scope 和 raw input（Unicode-safe 分块，可水平/垂直滚动）、授权余量；审批重查 Endpoint 实例、session/revision/generation、当前提案内容/范围指纹，删除仍需精确 UUID，历史授权默认不勾选。未新增 pair/approve/eval/load 等 MCP 工具。
- GUI 模块 ABI **1.1**：新增无事件的 SameLine 展示布局项；表仍为 104 字节。1.0 消费者保留已知旧控件语义，1.1 消费者明确请求新版本；未知 1.2 拒绝。Platform/Renderer/common 布局与旧 host bridge 版本不变。
- Ncma.Editor.Services.Tests 进入 solution、CMake、Build.bat/CTest。旧对照和全部现有回归保留。

## 2. 候选启动

构建仍使用 Build.bat；正式 out/bin/NcmaEngine.exe 保持旧版。

~~~powershell
& 'F:\NcmaEngine\out\verification\m2\candidate\Release\NcmaEngine.exe' --editor
~~~

--editor 打开空业务候选，可创建/编辑/保存场景；未指定项目时不加载玩法 DLL、MCP 不可启用。
带 --project <绝对 .ncmaproject 路径> 加载既有严格配置/启动场景/可信玩法程序集，人工 UI 可启用对应项目的 Endpoint。
--preview 仍是 M2.3/M2.4 参考/中文输入诊断模式，不与 --editor 混用；--smoke-test 是隐藏窗口有限帧自动模式。
不把候选演示当成完整发布、场景资产渲染或全部旧编辑器业务已迁移。

## 3. 历史首切片测试记录（非当前批次计数）

首切片专项 15 项：对象/历史/精确删除、缓存分页/输出复制、草稿 committed 隔离/选择取消/Play 冻结、文件失败保护、异线程/旧 stamp、GUI 重放/未改变值结束、取消/非法组件、伪造 kind/disabled 控件、未完成/非法 JSON、Transform/零 Quaternion/NaN、完整未知 binding/Export 分页、脚本绑定/Play 隔离/重载失败暂停保留、自定义脚本启动异常保持 UI 可操作、真实 named pipe 与 UI 审批/共享 Undo/remote Redo、真实 GUI + DX11 合成与资源释放。

真实管道专项直接复用现有 Protocol/Endpoint，通过同一托管 owner；不能冒充第三方客户端到可见应用的人工闭环。
业务图像由真实 DX11 1280×720 渲染/读回，人工查看其布局、中文、对象列表、Inspector 与参考 cube；不是操作系统窗口/输入法截图。GPU 验证错误/警告要求均为 0。

首切片规范门禁：Build.bat -Configuration Debug / Release，26 项 CTest（7 native + 19 managed/editor）、managed/native smoke、Python inspect 和 24 项 unittest。历史证据为 out/verification/m2-5-{debug,release}.log；当前追加批次结果见第 6 节。
首切片双配置结果：**Debug 与 Release 均退出 0；各 26 项 CTest、15/15 业务专项、managed/native smoke、Python inspect 和 24 项 unittest 全部通过，新代码零编译警告/错误。** 业务 GUI 实际合成与 DX11 验证通过；真实输入法/键鼠、跨屏 DPI 和第三方客户端人工验收仍待完成，不能将自动结果当成完整 H5 通过。
版本协商首次回归发现托管加载器仅接受 minor=0，已修正为逐模块已知兼容范围；GUI 1.0/1.1/未知 1.2 的原生测试保留，不扩大其他模块支持范围。

## 4. 当时的下一批（当前状态见 H5 记录），之后才考虑删旧入口

1. M2.5-E：角色 ABI 2 不可变导入/复制报告/索引/数值姿态和 CPU 蒙皮已实现；C# FBX 播放时间/暂停/片段/独立有界历史已实现，Python/native 检查消费者已迁移并拒绝角色 ABI 1。详见 [FBX 切片](M2_5_FBX_DELIVERY_REPORT.md)。FBX 线框画布/骨架元数据展示及完整 UI 对等验收未完成；动作实验室仍使用带策略的动画 ABI 1，数值契约与 C#/Python 控制状态迁移未完成。
2. Inspector 大 JSON 的 Unicode-safe 分页编辑及参考 Exposure/Metallic/Roughness/曝光扩展/clear RGB/tone 控件已接入 Core 草稿命令；见下方追加记录。完整材质资产/阴影参数/文件选择对话框、快捷键、主题/布局/路径偏好及归并 native 日志 Console 尚未对等迁移；当前 Console 仅为有界业务错误页。
3. 新旧业务对等序列、真实 helper/stdio → 可见候选审批 UI、越权/过期/删除/撤权完整新入口矩阵、真实键鼠/焦点/输入法/DPI/窗口验收。
4. M2.6/H6 自动门禁和 M2.7 候选发布已另行交付；H5 后完成剩余 H7 发布与正式入口切换、M2.8/H8 总验收。完成用户确认的迁移/验收后按精确清单统一删旧入口，并双配置复测。

未进行删除、SDK 安装、提交或推送；保留 .vs/.user 本机改动及用户资产。

## 5. M2.5-E/F 追加批次（2026-10-04）

- FBX 不可变角色资源 ABI 2、C# 状态/时钟/独立预览历史及新入口控件、Python/native 消费者升级，详见 [FBX 切片](M2_5_FBX_DELIVERY_REPORT.md)。
- 大 JSON Inspector：按 Rune 切 900 UTF-8 字节的页，单页编辑最多 1023 字节；Core 仍校验**完整重组 JSON**。保持既有组件 64 KiB / 文档 4 MiB 限制，不截断数据、不降低快照原子性。
- 活跃草稿固定源文本/页边界；字段页内容改变长度或缩至单控件大小也不会中途更换 widget。页跳转/选择/取消使用 Core CancelInteraction，非法输入可暂存，非法提交保留原文档。提交一次一条共享历史，MCP/Play 不见未提交数据。
- 页面切片为本地有界展示缓存，revision/generation/selection 改变时重建；不是第二份场景存储或命令栈。
- 注册的 ncma.render.configuration 增加 Exposure、Metallic、Roughness、tone/feature exposure、clear RGB 和 ReplaceTone 专用控件；身份、pipelineType 与其他字段保留。全部走现有 Core validator/Undo；数值只是已有 DX11 reference 配置，不宣称完整材质资产/阴影编辑。
- 新增 4 个分页专项（Unicode/预算、长 JSON 草稿+Undo、非法提交/翻页取消、草稿缩短不掉激活）和 1 个 reference-render 控件专项。编辑器业务专项目前为 25 项。
- GUI ABI 保持 1.1；角色 ABI 为 2；动作动画 ABI 仍为 1、待迁移。正式入口和旧代码未删除。

验证期间既有 Release 物理零分配门禁多次失败；保留 allocatedBytes == 0、
LiveBodies == count、Profile.Samples == 36 精确断言，未放宽门禁或关闭全局分层编译。
先补充 dimension/count/allocated/live/sample 及 stage/step/copy 诊断，完善完整路径预热；
12 次 standalone 通过后 CTest 仍复现，说明不能用短复测或预热当成问题已解决。
细分临时埋点实测 24,624 字节在速度批次 compaction loop；SetVelocities/native Step/read/counters 为零。
JIT 本地 disassembly 确认该方法由 Instrumented Tier0 切到 Tier1-OSR，
但确切 CLR 分配来源未证明，不能将此描述为 Box2D/Jolt 分配或宣称已找到 CLR 内部缺陷。
最后仅为既有 ExecuteStep 数值热路径标记 AggressiveOptimization，
直接使用优化代码，避免首次大型循环中途分层切换，不改任何模拟/批次/故障/lease 语义。
全部临时生产 GC/Console 埋点已移除。参考 [dotnet OSR 设计](https://github.com/dotnet/runtime/blob/main/docs/design/features/OnStackReplacement.md)；
实际门禁以本地反复测试和双配置规范构建结果为准。

## 6. 历史 FBX 批次最终结果核对（2026-10-04）

规范入口 Build.bat -Configuration Debug / Release 均完成并退出 0。

| 验证项 | Debug | Release |
| --- | --- | --- |
| CTest：8 native + 21 managed/editor | 29/29 | 29/29 |
| 编辑器业务专项（CTest 内部用例，不重复计入总数） | 25/25 | 25/25 |
| Player 专项（CTest 内部用例） | 22 项通过 | 22 项通过 |
| managed/native animation ABI 1 smoke | 通过 | 通过 |
| Python inspect / unittest | 通过 / 25 项通过 | 通过 / 25 项通过 |
| DX11 reference/GUI 像素对比 max / mean | 0 / 0 | 0 / 0 |
| DX11 reference/GUI、Player API validation 错误 / 警告 | 0 / 0 | 0 / 0 |

证据为 out/verification/m2-5-fbx-debug.log、out/verification/m2-5-fbx-release.log；
CTest 内部专项输出位于各配置 out/build/windows-ninja-{debug,release}/Testing/Temporary/LastTest.log。
Release 物理零分配专项连续 32 次通过后，再执行上述双配置完整门禁；
重复测试证据为 out/verification/m2-5-physics-repeat.log。此前间歇失败及处理范围保留在第 5 节，未放宽断言。

本次规范构建日志无编译警告/错误，但存在 Git 全局 ignore 文件权限警告；
git diff --check 无空白错误，Git 另有 LF/CRLF 提示。以上环境提示不冒充编译警告，也不宣称日志完全无警告。

包索引 out/verification/m2-7/{Debug,Release}/packages.json 已核对：Editor 包部署 NcmaNative.dll、
角色 resourceKernels ABI 2 元数据及 ufbx 许可证；Null/DX11 Player 不部署该 DLL。
搬迁到中文/空格路径后的 FBX 导入/采样包含在已通过的 Player/package 专项中。

交付结论仍为 **本轮切片自动门禁通过，M2.5/H5 未完成**：动画 kernel ABI/动作实验室、
FBX 线框/Orbit/完整报告、剩余偏好/Console/业务对等及人工验收未完成。
正式入口未切换，旧入口未删除，未提交或推送；候选发布不等同于 H7-Production。
