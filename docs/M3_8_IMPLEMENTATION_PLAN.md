# M3.8 资产与 Prefab 的 MCP 能力方案

日期：2026-10-05。状态：未实现。依赖 G7 与 M1 已有 scoped endpoint。
目标是 Agent 使用语义资产/实例命令参与开发，不通过鼠标模拟、任意 Python、shell 或磁盘路径绕过编辑器。
AI 推理/运输 worker 仍属 M8；本阶段是引擎可被 Agent 检查与获批修改的 MCP 契约。

## 1 共用边界与授权

继续现有 capability v2 envelope/session/request dedup；新增 asset/project revision 放在各能力的 closed input 中，
result.data 明确 assetRevision/executionAssetRevision/job 状态，不把原 scene revision 偷换为 asset revision。
若实现发现需破坏性修改 envelope，另提 v3 迁移决策并测试旧 scene 客户端，不能本方案默默改协议。
Editor UI 和 Agent 都调用 Assets.Authoring/Prefab 的同一 Prepare/Publish/Undo 参与者。

默认只读；扩展 host-owned 授权包含 projectId、source UUID/精确相对文件、asset/subasset/对象范围、
候选 generation/hash、允许 capability 与有效期。旧 objectScope 不能自动成为文件/assetScope。
任务 begin 与最终 commit 分别授权；长解析任务不能凭已过期 begin 授权提交。
取消/审批/项目切换由 owner safe boundary 处理。授权、配对和审批签发仍是可信 UI，不开放 Agent 自批工具。

## 2 建议能力目录

名称在实施后作为稳定契约。以下全部为拟新增，不写入当前 capabilities/manifest 已实现列表。

| 能力名 | 风险 | 精确用途 |
| --- | --- | --- |
| ncma.assets.list | read_only | 按种类/分页检查已登记资产 |
| ncma.assets.inspect | read_only | 检查指定 UUID 的摘要/依赖/子资产/诊断 |
| ncma.assets.validate | read_only | 有界已提交资产与引用诊断，不强制重导入 |
| ncma.assets.import.plan | read_only | 基于已登记来源/元数据生成预检意图，不解析任意外部文件 |
| ncma.assets.import.begin | reversible | 获批来源启动隔离候选任务，无持久发布 |
| ncma.assets.import.status | read_only | 读取当前会话有权限 job 的进度/诊断 |
| ncma.assets.import.cancel | reversible | 请求取消精确 job，不修改旧成功资产 |
| ncma.assets.import.commit | reversible | 发布获批 Ready generation 与身份映射 |
| ncma.assets.delete | destructive | 精确软删除/墓碑已批准资产，依赖和文件列单审批 |
| ncma.prefab.inspect | read_only | 检查模板 UUID/版本/成员及兼容诊断 |
| ncma.prefab.instantiate.plan | read_only | 计划 UUID/放置/操作预算/资源依赖，不创建对象 |
| ncma.prefab.instantiate.commit | reversible | 创建已批准完整实例计划 |
| ncma.prefab.override | reversible | 已登记字段覆盖，统一资产/场景事务 |
| ncma.prefab.sync.plan | read_only | 检查当前实例与模板差异及冲突/删除目标 |
| ncma.prefab.sync.commit | destructive | 提交可能移除对象的精确同步计划，不批量改所有场景 |

删除默认不级联依赖、场景对象或 raw FBX，先报告使用者；更大删除需重新计划和授权。
未来 bake/build/delete cache 工具不自动包含在本次能力范围。

## 3 输入与输出 schema

每一行实现时必须有独立 JSON Schema，additionalProperties=false、required/type/bounds 明确；
下面定义字段集合和共用类型，不可只用任意 data:{} 冒充完成 schema。

共用类型：uuid 为规范非空 UUID string；revision 为非负安全范围整数；hash 为64位小写十六进制；
page offset≥0、limit=1..64；settings 使用 G1 已登记且有硬预算的类型，不接受任意 importer/type 名称。
通用 result 保留既有 ok/error/conflict/denied、code/changed/revision/executionRevision/replayed。
data 按工具封闭定义；异步 begin 返回 status=ok、changed=false、data.state=Queued，并不表示导入提交成功。

| 能力组 | 必需 input 和可选项 | 封闭 data 输出 |
| --- | --- | --- |
| assets.list | 可选 kind/offset/limit | assetRevision,total,items,nextOffset，每项UUID/kind/name/state |
| assets.inspect | assetId,section；可选page | assetRevision,assetId,generation,section,total,items |
| assets.validate | assetId；可选page | assetRevision,valid,total,diagnostics，每条code/assetId/field/severity |
| import.plan | sourceAssetId,expectedAssetRevision,settings | planId,baseRevision,sourceHash,settingsHash,expiresAt,estimatedBudgets,diagnostics |
| import.begin | planId,expectedAssetRevision | jobId,state,assetRevision,sourceHash,settingsHash |
| import.status/cancel | jobId | jobId,state,stage,completed,total,candidateId,diagnostics；未知/foreign job不泄漏内容 |
| import.commit | jobId,candidateId,expectedAssetRevision,mappingPlanId | assetRevision,executionAssetRevision,assetId,generation,affectedIds,receiptId |
| assets.delete | assetId,expectedAssetRevision,deletePlanId | assetRevision,assetId,tombstone,receiptId；deletePlanId来自可信审批UI |
| prefab.inspect | prefabId；可选page | assetRevision,prefabId,version,total,items,diagnostics |
| instantiate.plan | prefabId,expectedAssetRevision,placement | planId,prefabVersion,plannedInstanceId,plannedObjectIds,operationCount,diagnostics |
| instantiate.commit | planId,expectedAssetRevision | assetRevision,instanceId,createdObjectIds,selectionId,receiptId |
| prefab.override | instanceId,expectedAssetRevision,templateObjectId,typeId,registeredFieldPath,value | assetRevision,overrideSetId,affectedObjectIds,receiptId |
| sync.plan | instanceId,expectedAssetRevision | planId,baseVersion,targetVersion,changes,conflicts,destructiveObjectIds |
| sync.commit | planId,expectedAssetRevision | assetRevision,affectedObjectIds,removedObjectIds,receiptId |

读结果不得泄露绝对路径、用户名、私有源码、native指针或审批凭据。
placement 仅有限 TRS；value 由指定已登记字段 schema 验证，不接受代码/expression。
大矩阵/mesh/texture/二进制不通过 MCP JSON 传输。分页最大64，超单项/256KiB输出返回明确错误。
path 不作为 UUID 替代；能力只操作已登记且受许可来源，跨项目和任意文件访问默认拒绝。

## 4 异步提交与重试

owner 的 existing pump 保持有界；begin 只排队，解析/hash/cook 不在 pump 中同步等待。
IO/hash在后台准备并持可靠来源租约；owner提交不做大文件同步重读，锁失败/来源变动要求重新异步准备。
任务结果验证 project/session/document generation、scene expectedRevision、asset revision、source/settings hash、
候选完整 manifest、mapping、取消状态、授权、Play pin、文件锁与所有受影响目标。
源文件变动或 UUID 匹配冲突要求新计划/审批，不用旧 token 自动改新路径。

同 requestId 与相同输入复用相同 job/receipt，冲突输入拒绝；cache eviction 后也不能重发布候选。
候选一旦消费保留有界 receipt/消耗标识；过期未知请求返回 request_expired，绝不当新任务重执行。
Undo/Redo 回走唯一历史，重新检查原始 asset/file/object 授权、版本和外部 hash。
asset commit 成功、scene commit 失败时按 journal 恢复；GPU 提交故障不改写“文件事务失败”证据。

## 5 测试与退出门禁 G8

- 对所有 descriptor 做稳定名称、说明、风险与 input/output schema golden tests，实际返回逐项校验。
- 默认只读、越权来源/资产/文件/对象、跨项目/foreign job、审批过期/撤权、失败历史授权。
- source/settings/asset/scene revision 改变、取消与commit竞争、mapping冲突、重复/缓存淘汰/迟到结果。
- malformed/duplicate JSON、UTF-8/depth/大小、分页越界、输出过大、计划替换与路径穿越。
- 第三方可见客户端：检查→可信UI审批→导入/实例化→UI Undo→MCP Redo→撤权→拒绝旧请求。
- 多客户端有界队列、公平/超时/关闭、错误结果确定，禁用 Python 仍完成流程。

自动/stdio/hidden窗口结果和真实客户端记录分别交付；运行完整 Debug/Release Build.bat。
没有人工闭环只标 G8 自动部分通过，不扩大 M1/M2 的人工验收声明。
