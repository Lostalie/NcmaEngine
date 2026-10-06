# M2.5：托管编辑器业务、MCP 与既有面板迁移

更新日期：2026-10-04。状态：A–F 候选业务迁移与自动回归已落地；H5 真机输入法/DPI/窗口及第三方可见 MCP 客户端人工门禁仍待验收，完整 H5 未关闭。依赖 H1–H4。
用户确认“先完成业务迁移，再删除旧入口”；按此指令推进候选业务，H3/H4 的真实输入法/DPI 人工项仍待验收，不视为通过。当前交付与人工验收清单见 [H5 交付记录](M2_5_H5_DELIVERY_REPORT.md)；历史首切片见 [早期记录](M2_5_DELIVERY_REPORT.md)。
FBX 新切片的实际接口、测试和限制见 [FBX 交付记录](M2_5_FBX_DELIVERY_REPORT.md)。
目标是既有编辑器功能对等迁移，不在本阶段实现 M3/M5/M6 的新资产/UI/动画图制作功能。

## 1. 权威与迁移表

Ncma.Editor.App 是组合根；Editor.Services 保存应用业务，Editor.Core 保存唯一文档命令/历史。
NcmaGui 只画 C# 提供的语义视图和返回控件事件，不载入项目/脚本、不决定权限、不执行 World 修改。

| 旧 EditorApplication 责任 | 建议托管目标 | 原生保留 |
| --- | --- | --- |
| m_Scene/m_PlayScene、m_GameplayRuntime | EditorSessionOwner + 既有 EditSession/PlaySession/Scripting | 无 native 场景或 host token |
| New/Open/Save/Undo/Redo | DocumentService → Editor.Core | 文件对话框展示适配 |
| Selection/InspectorDraft/Bindings | Selection/InspectorPresenter → Core draft/intents | 活跃控件展示暂态 |
| 菜单/Toolbar/Status/Console | EditorViewModel + 复制 GUI 描述 | 控件绘制 |
| m_McpState / pairing/approval | 当前 EditorEndpoint + TrustedAuthorizationController | 提案/风险展示及按钮事件 |
| PBR/camera reference settings | ReferencePreviewService | GPU/数值内核 |
| ActionAnimationWorkspace/实验室时钟/Undo | Managed Preview workspace/命令 | 采样/混合/姿势数值 |
| FbxPreviewState/独立 Undo/暂停 | Managed FbxPreviewSession | ufbx 导入、蒙皮数值 |

Native Gui 每帧不保有 EditSession，也不能因为 enabled=true 就认定可提交。
原生不执行 create/delete/save/reload/pair/approve；所有按钮只是给 C# 的意图。

## 2. 分步实施

### A. 菜单、对象列表、选择与文件状态

使用 UUID，而非 GameObjectId/row index 作为选择；Undo/Restore 后重新解析。
只显示扁平列表，不恢复 Scene Hierarchy/World 树语义；无 Transform 对象保留空容器。
New/Open/Save 与当前 M1 内容指纹、关联/Dirty、文件替换、未知字段拒绝一致；路径对话框只是用户选择。
UI 与 Agent 都调用原有能力/可信命令路由，不私下调用 World.Create/Destroy/Restore。
文件操作开始先结束/取消草稿与权限，失败保留旧文档/原文件；外部变化使 history 失效的规则不变。
列表虚拟化，Core 读取尽量缓存 revision/catalog generation，不能每控件一份完整 JSON 快照。

### B. Inspector 与完整交互草稿

C# 管理 activate/change/commit/cancel，对照 Core draft token、UUID、session、revision、generation。
预览只读草稿，未提交值不能出现在 MCP committed inspect 或 Play 克隆。
Esc/失焦/关闭/切换选择/文档替换/GUI事件溢出取消草稿，明确防止回放上帧事件误提交。
Transform/名称、可信值组件、绑定启停/Export 的模式保持 M1；未知绑定元数据不得静默丢失。
数值输入 NaN/Infinity/类型非法通过 validator 拒绝，错误展示不会创建新历史。
绑定新增/删除/启停/Export 使用已有局部操作和 catalog，不用全量 set_bindings 绕过信任检查。

### C. Play、脚本与呈现

Start/Stop/Pause/Resume/Step/Restart 全部调用 PlaySession；游戏与 GUI 没有各自 Begin/Commit。
Edit 冻结、隔离 Play、只读 OnUpdate、失败步中止、上一成功步保留、输入与插值和丢时展示保持。
Scripting service 创建/重载 catalog，预检失败保留旧暂停，激活失败 Faulted；不新增私有字段迁移。
Status 展示 session/world/tick/attemptTick、fault phase/object/binding/type、alpha 与 dropped time。
程序集重建动作可由已明确的本地构建流程触发，MCP 不获得 build/eval/load 任意路径能力。

### D. 原样重托管活动 MCP

EditorEndpoint 直接借用唯一 EditSession；helper/Protocol/IPC 1/capability 2 不因入口变化而换工具名。
默认关闭/只读，人工配对；UI 显示完整原始 input、风险、全部 UUID/组件/绑定与授权余量。
approve/pair/revoke 只走 C# 可信 UI 控制器，不注册为 MCP 工具。
GuiEvent 携带 proposal ID、scope/content fingerprint、doc generation；批准时重新取得当前提案，
过期/目标变化/Play/catalog reload/历史失效撤权；不能批准缓存显示出来的旧提案。
删除仍需精确 UUID 输入，可选范围内历史；用户 Undo/Agent Redo 走同一 history。
endpoint 的 IO 线程与假/原生 GUI 不接触 World，主循环每帧 pump 4/软2ms及5s过期不改。
主窗口关闭时先撤权、拒绝新请求、取消队列、移除自己拥有的描述，再释放 Edit owner。
保留 outcome_unknown 恢复规则，不因重启换入口而自动重放旧写入。

### E. 动作实验室/FBX 独立预览

这是 M2 容易漏掉的旧高层业务，不能仅把原 C++ RenderAnimationLab/RenderFbxCharacter 搬进 DLL。
C# PreviewSession 管理参数、暂停/时间、动作选择、预览文档和有界命令历史；native 只返回数值姿态/根运动/Notify/蒙皮结果。
预览历史明确标识独立文档 scope，入口仍经托管命令调度；场景 Undo 不跨预览文档，不能藏第二份 Scene Undo。隔离动画工具的历史不冒充活动场景共享历史。
FbxPreviewSession 管理导入请求/缓存租约与 preview Undo；ufbx 和 CPU 数值计算继续原生。
保留当前角色检查、CPU 线框/参考蒙皮、动作实验室效果；不新增资产 UUID 数据库、场景 Animator 或 GPU 蒙皮。
大报告按 caller-owned buffer/明确 owner 复制，不借用跨帧 native string 指针。

迁移前 animation v1 C ABI 暴露 pause/动作/Undo 等高层命令；真正移除这些 native policy 时是破坏性变更，
实际已迁移至数值 animation ABI 2，并同步 C#、native Tests 与 Python 隔离工具；实际函数与限制见当前 H5 记录。下列为设计目标，不能据此宣称所有建议 API 已实现。
最小数值契约建议：CreateSkeleton/CreateClip/ReleaseResource、EvaluatePoseBatch、ExtractRootMotionRange、QueryNotifyRange、SkinVerticesBatch；输入含 clip/skeleton handle、前后采样时间/循环区间、blend/骨骼 mask 参数，输出是 caller-owned pose/matrix/root-motion/Notify ID 批次。
动画速度/暂停、Idle/Run/Attack/Dodge 选择与中断、累计时钟、Undo/Redo 和 Notify 到玩法事件的消费在 C# 或隔离工具层；native 只计算数值/区间，不因名为 kernel 而继续保存这些高层规则。
FBX 最小数值/导入契约建议 CreateImportedCharacter、ReadReport/ReadSkeleton/ReadMesh/ReadClip、ReleaseImportedCharacter；持有不可变导入资源，不持有 Preview Undo/场景对象。所有查询先返回 required bytes/count 并验证资源代次，读取不重新导入源文件。新契约是建议，不能在函数尚未存在时置能力为 true。
Python 仍是工具：其独立 preview 状态/history 在工具层，不是 Python Gameplay，不拥有活动 EditSession。
保持原隔离动画 MCP 的 8 个工具语义/默认只读和真实 stdio测试，不把它误注册到 live scene capabilities。
若 character 的借用 JSON 返回改 caller-owned，character 也独立升版；不只改 consumer 签名而忘了 ABI。
H5 必须列明最终实际版本及所有已知 consumer；旧版本显式拒绝，不恢复场景旧格式兼容。
数值 API 设计和 reference fixtures 先冻结；不在本阶段扩展 BlendSpace/IK/Montage 图。

### F. 本机偏好、Console 与诊断

管理主题、窗口布局、面板开关、路径/字体 preference；与项目/场景和 Undo 分离。
Console 使用有界结构化日志页，native spdlog 被复制/归并；不在帧内一次读无限日志文件。
全部已迁移面板使用稳定 IDs，DPI重建不覆盖权威 Inspector值。
M1 已发现高频全量快照分配，只对 presenter 视图缓存/缓冲复用做局部优化；
若修改核心快照/事务语义须独立方案与失败注入，不能为了 UI FPS 取消原子性。
固定布局先保持；docking 另锁版本，不能在菜单写“已支持”而无布局恢复/窗口资源测试。

## 3. H5 对等验收

- 新旧入口分开进程、同场景/输入/命令序列，比较完整文档、revision/Dirty/selection/history/Play结果。
- 名称/Transform/绑定/启停/Export、New/Open/Save、Undo/Redo、草稿取消不回归。
- 真实 stdio → pipe → 新应用 EditSession，默认拒写、批准事务 → UI Undo → MCP Redo → validate。
- 删除 UUID、范围、撤权、过期、generation、重复/冲突/缓存淘汰/取消/断线全矩阵。
- Inspector 草稿/用户编辑/Agent/Play/catalog reload 竞争，没有第二份文档。
- 输入/失焦/Pause/Step/重载/窗口关闭真机人工验收；第三方客户端版本及脱敏配置记录。
- 动作实验室、FBX reference、native/managed/Python consumer 回归，旧 kernel version 明确拒绝。
- Build.bat Debug/Release 完整矩阵；零警告/错误；没有 NativeEntry/hostfxr 被新入口引用。

M2 全阶段保留受控旧入口作对照；H7 才切默认路径，H8 验收并锁定清理清单，M2 结束后统一删除已替代外壳及桥接并复测，不在 H5 提前删除。
