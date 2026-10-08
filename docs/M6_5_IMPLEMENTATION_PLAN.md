# M6.5 过渡、事件和调试实施计划

2026-10-08：按 M6 顺序进入 M6.5，逐切片实现、完整 Debug/Release 测试、修复、提交推送并验证远端。
随后用户要求先补齐M6.3-C/D，本轮保留A基础供C/D共享复制debug；C/D状态见其交付记录。
本文件不授予live控制，不宣称完整M6.5、真实用户FBX、人工MCP、性能或长稳验收完成。
A基础随C/D完整Debug/Release回归通过（graph/pose/clock45）；见M6_3_CD_DELIVERY_REPORT.md。
下一切片B尚未实施，不能把本轮共享debug/序列基础称为已交付中断/事件作者工具。

## A：提交事件、调试和隔离序列基础（本次）

- 纯 C# 编译程序接收可信主机提供的独立事件元数据副本；它不是 `.ncmaanim` 持久格式扩展，也不是已准备的 NCA 资源。
- 每个标记包含 UUID、Clip UUID、时间、纯文本名称；时间严格在 `(0, duration]`，禁用表达式、回调和伤害权限。
- 一个播放源负责事件：状态机目标状态的递归左侧主 Clip；无状态机使用 Output 的递归左侧主 Clip。
  Blend 权重不改变归属，源状态和次要贡献源不发重复事件。该规则在 B/C 中仍保持明确。
- Prepare 收集 `(previous,current]` 的候选事件；成功 Commit 才公开；Abort 或准备失败不推进时钟、消费 trigger 或替换成功事件。
  循环终点事件每周期一次，重入从零开始；零时间标记禁止，查询不发送事件。
- 上限：4096 个标记、每量子 256 次事件、现有 32 次 Clip 边界；超限整量子拒绝，不截断。
  图、实例、session/world/tick、数值 sequence、状态/转换/参数和采样配方进入复制 DebugFrame。
  失败/准备中有显式 validity/outcome，不能把旧成功帧当成本次成功。
- 独立纯托管序列检查器：最多 256 个固定步、512 次类型化参数写入、64 个闭合断言、8192 个输出事件。
  只接受已编译程序、创建独立实例与身份；不访问 World、GPU、物理、文件或推理服务。
  返回逐步身份/状态/事件/断言结果；根意图明确 unsupported。UI 与后续 MCP 用同一服务。
- SceneAnimatorRuntime 提供可信 host 的只读、精确 committed stamp 调试接口；fault/pending/reload 仍拒绝。
- 自动测试：边界、多周期、非循环终点、重入、源归属、事件过量、Abort/失败/身份、32 实例、预热零分配、
  闭合序列断言、复制隔离、真实 World 失败边界。完整 Build.bat Debug/Release 后记录交付。

## B：可中断过渡与真实姿态连续性（未实现）

用固定预算的当前可见姿态缓存作为中断源，不堆叠历史过渡配方；目标播放源继续负责事件。
明确中断优先级、剩余时长、一次量子一次选边，Prepare/Commit/Abort 与数值缓存发布保持一致。
测试重复中断、同目标重入、条件/trigger 优先级、CPU/native 姿态 oracle、失败与资源关闭。
缓存只属于单实例，不能成为第二个时钟、World 或运动权威。

## C：轨道编辑、可见调试与获批 AI 序列工具（未实现）

先明确事件资产的严格新格式及 NCP 身份闭包，不暗中放宽现有 v1 或恢复旧格式。
标记编辑走 M6.4 的 shared semantic edits、精确资源/文件 review 和唯一 Undo/Redo。
UI 显示当前/目标状态、选中转换、事件时间线、failed validity，定位同一 UUID。
MCP 序列检查需可信主机批准精确编译程序和事件指纹、资源 generation、配对受众及有效期；
默认拒绝，不接受资源路径或 Agent 自报 duration 作为真实资源准备证据。
同请求 UI/MCP 一致；超量、陈旧、重复、取消、排队失效和真实 stdio 测试。
独立预览参数序列/固定步不是 live Play Step，无 Agent fault 清除、回调、伤害或代码执行。

## D：联合回归与剩余门禁（未完成）

Editor/Player/Headless 32 actors、过渡事件、实际姿态/共享 skin-shadow、fail-stop/Stop/reload、
部署包身份和双配置完整回归。保留 M6.3-C/D 依赖与人工、目标环境、性能、1 小时长稳开放状态。
只有 A–D 相应证据满足才将 M6.5 自动候选标为完整；A 通过不等于 M6.5 已完成或已有活动 AI 工具。
