# M3.2 异步 FBX 导入与派生数据方案

日期：2026-10-05。状态：实施中，G2 未关闭；G1 最终双配置通过后才开始。独立数值内核/分页 C ABI 与隔离 Worker/候选协调切片已实现，持久导入尚未完成。目标是源 FBX 到可重建派生资源，含取消、重导入和失败保护。
复用 ufbx/Eigen 数值代码，不直接把已有同步预览放到 Task.Run。

## 1 导入任务与插件边界

C# ImportCoordinator 管理任务与设置；默认使用 C# Ncma.Asset.ImportWorker 工具进程，
调用拟新增 NcmaImportKernel 的版本化、caller-owned 分页 C ABI。
native 只解析/三角化/采样/复制数值，不决定持久 UUID、写数据库或运行 gameplay。
worker 有独立资源上下文，不使用现有全局 Character 资源锁阻塞编辑器。
旧 Character ABI 2 预览先保留，不把它重新解释为新导入协议。

任务状态拟为 Queued/Parsing/Converting/Validating/Ready/Committed/Cancelled/Failed。
进度是已完成工作量/阶段提示，不伪装全文件 ETA；1 活动任务、4 候选上限、10Hz 通知初值。
进程隔离不是安全沙箱；内存/时长、路径/结果大小和父进程 watchdog 均要限制。

## 2 实施切片

### A 导出完整数据流

增加原始位置/法线/UV/切线、indices、submesh 材质槽、mesh cluster bindings、骨架辅助节点、
完整 clip TRS keys 和精确终点的有界读取，不能仅导出当前 CPU-skinned XYZ/报告。
派生角色包含 Mesh/Skeleton/Clip/MaterialSet 的类型化块；静态模式另提供 StaticMesh 结果。
保留现有角色模式的严格含蒙皮要求，不偷偷放宽旧 API 为任意模型导入器。
持久化加载后不再逐次解析 FBX；二进制读取验证块范围/hash/索引/拓扑/有限矩阵。

### B 取消与隔离

worker 内使用 vendored ufbx progress_cb 合作取消，三角化/采样循环检查取消 token；
回调只检查工具进程内的原子标志，不回调 live CLR/GUI/World。
正常取消释放所有数值资源；卡死时父进程只结束自己启动且确认身份的 worker，绝不强卸载 DLL。
退出不再接受候选；迟到 Ready 不能触发提交。失败/取消 staging 保留诊断或按精确生成清单回收。

### C 重导入身份与依赖

C# 对 root UUID/既有子资产映射生成匹配计划，采用可信来源 ID 或限定路径/类型/结构特征。
禁止只按名称/数组下标匹配；duplicate/rename/topology/骨架变更不唯一时进入冲突计划。
未匹配旧 UUID 保留 tombstone，已引用子资产不自动重定向；需人工确认映射或创建新子资产。
匹配结果要有原因、置信条件、受影响场景/Prefab/clip 列表，不用模型猜测替代确定校验。

保持 load_external_files=false；C# 解析经批准的纹理依赖，再按项目相对范围复制/导入。
工具只报告 FBX 外部路径，不跟随网络盘/绝对路径/..。内嵌纹理初版可明确拒绝，后续需解码预算与精确来源许可。
本地用户可显式批准将外部文件复制进 assets/sources；Agent 仅处理项目内已批准来源。

### D 发布与 Undo

Ready 结果包含 request/job/project generation、base asset revision、source/settings hash、
来源快照和 candidate manifest。owner 在提交前重检文件/hash、权限/取消/Play pin 和全部预算。
文件在导入中变化返回 source_changed；新来源需要新计划，不提交混合前后版本。
最终 hash/manifest 校验在后台准备，来源以防写/防删除 lease 保持身份至提交确认。
owner 不同步重新读取大文件；只能验证可靠租约与准备结果，无法锁定来源则重新异步 prepare 或拒绝。
准备不可变 blobs→准备 descriptor/UUID map→journal→发布版本→写入唯一历史。
每一步都有恢复/补偿；Undo 只改已批准描述/版本引用，raw blobs 由 lease/history pin 保留。
保存场景、旧资源与用户原始 FBX 不受失败/取消影响；不自动替换活动 Play 的 rig。

## 3 测试与退出门禁 G2

- ASCII/二进制、单位/轴向/非对称 bind、多个 mesh/clip、静态模式与现有严格角色模式。
- 原始数据 round-trip、采样终点、无权重 fallback、四权重截断诊断与旧 ufbx 数值对照。
- 相同来源重启重导入稳定 UUID；重排/重名/骨架/拓扑变更按计划冲突，不能错误复用。
- 在队列、解析、转换、Ready、提交前取消，worker crash/超时、关闭迟到、来源改写和磁盘失败。
- 逐发布点 fault injection、锁/未知文件/reparse 拒绝、恢复旧版本、Undo/Redo 冲突和 pin 不被回收。
- UI owner 无 native 解析等待；采样预览仍可运行，队列增长与进程资源有界。

新增导入任务/格式/命令测试和 frozen 模型数据，完整双配置 Build.bat 回归。
用户真实模型未提供前只报告现有 fixture 的结果；不以 worker 进程或支持 ASCII/二进制推断任意 FBX 兼容。
外部语义依据见 [ufbx reference](https://ufbx.github.io/reference)，具体接口以 engine/sdk/ufbx/ufbx.h 为准。

## 4 当前实施：持久导入切片已补齐，G2 关闭

独立 Import ABI 1.1 保留 1.0 表，增加严格静态模式；工具进程隔离解析、合作取消、
看门狗与候选生命周期。C# 数值规划器、typed NCA v1、确定身份匹配/冲突/tombstone、
不可变 generation 异步准备、唯一 EditSession import.commit、metadata journal、
故障补偿/重启恢复、session/history/Play pin、精确审批 GC 和正式 Editor 工具部署已实现。

切线是版本化 triangle-UV 算法，不是 MikkTSpace。当前只有材质槽/名称；
外部/内嵌纹理完全不跟随，独立受许可依赖导入与 PBR 映射由 M3.3 继续。
根/子资产保持 RH/+Y/metres/column-major/UV top-left，静态变换只烘焙一次。
确定匹配要求类型、限定名称、数值结构/内容与 rig 证据一致；模糊来源不按索引自动复用，
支持的冲突决策是批准新 UUID + 旧 UUID tombstone，不自动转移未来场景/Prefab 引用。

工具只装入 Editor 的 tools/import-worker，Player 不部署解析器。启动由可信 bootstrap
验证包与代码 hash，依赖文件租约覆盖子进程生命周期；默认项目只读且来源无审批。
启动后台准备已知 generation，Play 仅采用已准备、catalog/revision 一致的版本。
GC 全集合先验证/锁定，当前 catalog、history、Play 或其他 store 的 pin 不能回收。
保留 generation 预算跨重启有界；未知/孤立文件也消耗预算，不自动清理用户数据。

24 项专项覆盖上述发布与恢复路径及 frozen 多 take、静态多实例/单位/轴、
五权重/无权重、重排/重名/拓扑/骨架/采样改变、真实部署 Worker 与依赖篡改。
具体布局、预算、日志、退出状态和未验收范围以 [M3.2 交付记录](M3_2_DELIVERY_REPORT.md) 为准。

## 5 最终门禁和提交顺序

本轮最终 Debug/Release Build.bat 顺序全量回归已通过，G2 关闭。
现在提交推送 M3.1 + M3.2；确认 GitHub 远端 SHA 与本地提交一致后开始 M3.3。
不把旧的 kernel/Worker 日志、局部 24 项或 M2 图像回归当作新的 GPU 场景绘制/G3 证明。
真实用户模型、人工资产面板/第三方 MCP、自包含环境、最大素材吞吐和长稳仍须各自验收。
