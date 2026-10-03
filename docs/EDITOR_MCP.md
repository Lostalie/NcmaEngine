# 活动编辑器 MCP

更新：2026-10-04。C# stdio helper 经 Windows 本地 named pipe 连接**当前 ImGui 编辑器的 EditSession**。
它不加载第二份场景、不依赖 Python/LLM/模型 key。选定协议版本 2025-11-25，私有 IPC 1，能力契约 2。
第三方客户端人工兼容验收尚待执行；自动测试有真实 stdio 子进程与真实编辑器的读写/共享历史链路。

## 启动与配对

1. 在项目根目录运行 Build.bat -Configuration Debug（或 Release）。
2. 运行 out/bin/NcmaEngine.exe，打开 AI → Editor MCP connections。
3. 选中 Enable local editor endpoint。默认关闭，每次启用生成新的实例 ID/私有管道。
4. Copy descriptor path。将 engine/config/editor-mcp.example.json 中的占位路径替换成此次实例的实际路径，再由你自行加入客户端配置。
5. 客户端 tools/list 首次连接会等待编辑器批准，30 秒内在面板点击 Pair read-only。初始化/notifications/initialized、ping 不需要配对。
6. 保持编辑器运行；关闭/禁用端点后旧描述文件失效。重新启用后必须选择新实例并重新配对。

示例不是自动安装配置，不能原样使用占位路径。已有 Python animation MCP 是独立动画预览服务，不能替代此端点。
仅支持本地 Windows 当前用户；管道 ACL 为当前用户、拒绝远程客户端、首实例独占。发现文件不含凭证。
凭证只经私有管道传递并留在进程内；不要记录、放在命令行或粘贴到聊天。Helper 不自动重连或自动重试写请求。

## 工具与操作

六项读取：ncma.capabilities.list、ncma.engine.component_types、ncma.engine.behaviour_types、
ncma.scene.inspect、ncma.scene.object.inspect、ncma.scene.validate。
四项修改：ncma.scene.transaction、ncma.scene.delete_object、ncma.history.undo、ncma.history.redo。

tools/list 返回真实输入/输出 Schema。调用 arguments 必须包含 contractVersion=2、非空 requestId UUID、
tools/list 获取的 sessionId、expectedRevision（读取可 null，修改必须为当前 revision）、input。
不同业务请求必须使用不同 requestId；仅同一连接、完全相同的成功请求可精确重试。

transaction 支持 create、rename、set_component、remove_component、set_bindings，以及局部
add_binding、remove_binding、set_binding_enabled、set_export。组件来自可信注册 TypeId/版本/schema；
绑定/Export 来自当前可信程序集 catalog。只改指定成员，局部操作不抹掉未知或未指定配置。
没有任意类型加载、文件访问、shell、eval、Python gameplay 或直接 Play World 修改工具。

## 授权闭环

先读取当前场景/详情/catalog → 提交修改（默认 permission_denied）→ 面板检查完整提案 → 批准精确请求
→ 在同一连接重试同一个请求 → validate。首次拒绝不修改文档或历史。
批准时显示 requestId、revision、所有对象/新 UUID、组件/绑定、完整 input 与风险。
每连接仅保留一份 60 秒授权；批准另一提案替换原授权。最多 16 个待审提案。
授权绑定请求内容指纹、会话、文档 generation、对象/创建/组件/绑定范围；
执行及安装前重复校验。不是“模型申请即生效”，也不提供整场景自动写权限。

默认不批准历史操作。用户可勾选 Also allow Undo/Redo within this exact scope，
使 UI Undo 后的 Agent Redo 可用；仍检查历史顶端原能力、全部受影响 UUID/组件/绑定及删除风险。
New/Open/文件替换历史永不授权给 Agent。顶端是范围外的人工修改时拒绝，不能跨过它挑选旧历史。
删除需在面板输入精确目标 UUID 再批准；撤权后的删除重做、缓存重试也会拒绝。

Revoke writes 只撤写权限，Revoke connection 撤配对与排队请求。进入 Play、脚本 catalog 重载、文档/文件关联替换、
历史失效会撤写权限；Stop 后不自动恢复。Play 期间仍能读已提交编辑副本，不是运行副本或 Inspector 草稿。

## 冲突、取消与恢复

Core status/code 原样作为 structuredContent，业务失败 isError=true。传输失败也返回确定代码。
revision_conflict：重新读取并由用户批准新提案；不要自动覆盖人工提交。
edit_busy：交互草稿尚未结束；读取只看提交状态。play_frozen：不可修改编辑副本。
document_generation_mismatch：旧文档路由失效；helper 后续请求显式刷新 generation，但绝不恢复旧写授权。
request_id_reused/request_owner_mismatch：同 ID 改内容或来自其他连接；拒绝。
outcome_unknown：结果已丢失/缓存已淘汰/归属不确定。不能推断“没提交”，也不能自动创建新 ID 重做；
人工 inspect 目标/历史/审计后决定下一步。

支持 notifications/cancelled。只取消未执行排队请求；执行后取消/断线不会 Undo 已提交事务。
结果恢复必须重新检查当前权限。成功结果缓存 128；归属记录 256 + 64 KiB 保守墓碑过滤器，
过滤器可误判为 outcome_unknown，但绝不会因此重放旧修改。没有无限期 exactly-once 保证。

## 预算与审计

4 个连接，合计队列 64、每连接 16；每帧最多 4 个请求、软预算 2 ms；
单个完整文档事务不能被抢占，复杂场景可能超过 2 ms。排队期限 5 秒，不等编辑器泵送也会过期。
IPC/stdio 单消息 1 MiB、深度 32、严格 UTF-8；业务请求 64 KiB；工具结构输出 256 KiB；
对象详情完整成员分页，不能返回半个组件。单项过大返回 item_too_large。
Core 历史上限 64 项/16 MiB，场景 4 MiB/4096 对象，事务最多 128 操作。
审计 out/sessions/audit-<instance>.jsonl，4 份、每份约 1 MiB；面板保留最近 32 条摘要。
含连接/会话/generation/request/capability/风险/批准状态/revision/耗时/changed/replayed/code；
不记凭证、原始 input、完整项目路径或私有脚本状态。

## 人工验收（仍待记录）

请记录客户端名称、版本和脱敏配置。验证：
初始化/列工具/读取详情 → 默认写拒绝 → 批准一次创建/Transform/绑定/Export 事务 →
UI Undo → 授权内 MCP Redo → validate → 删除 UUID 确认 → 撤权重试拒绝 →
Inspector 草稿冲突 → Play 读可用、写拒绝 → Stop 后重新批准 → 关闭端点无旧授权复用。
同时检查焦点/ImGui 输入捕获、Pause/Step、提案完整可读、窗口关闭。
自动测试替代不了第三方客户端与真实人工 UI 操作。不要把 --mcp-smoke-test 的测试专用自动批准当生产授权。
