# M3.7 扁平 Prefab 与实例覆盖方案

日期：2026-10-05。状态：未实现。依赖 G6。目标是可复用角色/场景对象模板，不恢复 Godot Node 或 Transform 父子继承。
Prefab 是扁平对象集合及组件/绑定数据，不是对象树；初版不支持嵌套 Prefab/Variants。

## 1 数据与实例身份

.ncprefab JSON v1 保存 prefab UUID、template object UUID、组件、可信 Behaviour 元数据及显式内部引用。
实例的 scene object UUID 与 template UUID 不同；InstanceId/TemplateObjectId/PrefabId/BaseVersion/OverrideSetId
以小值 PrefabMembershipData 记录，复杂映射/字段覆盖保存在有界作者文档，不放数组/字典进 World 组件。
OverrideSet 是实例专属作者资产，与 scene 更新通过 G1 命令参与者一起发布。
外部资源继续按资产 UUID 引用，模板内部对象引用在实例化时显式重映射。

单个角色通常是一个对象 + meshSet/rig，而不是每块 mesh/每根骨骼一个子对象。
初值限制模板 ≤16 对象，实际展开操作仍 ≤现有128操作和4MiB scene预算；不能分批绕过原子性。

## 2 实施切片

### A 提取与严格格式

从已批准对象集合生成模板，排除 runtime handle/tick/private script state/native资源。
提取只是复制文档，不自动从 live Play 保存编辑资产。未知组件/绑定类型和越界引用拒绝。
首次不支持嵌套/自引用/变体继承，解析碰到这些必须明确失败，不静默展开。
明确一组 template transforms 与放置基准，锁定提取→实例化的矩阵顺序和坐标测试。

### B 实例化与单对象删除

生成完整实例计划：新 instance/object UUID、资源依赖、模板版本、组件/绑定与总操作预算。
全部类型/来源/权限/UUID冲突预检后，一次原子命令创建所有对象并设置选择；失败零部分对象。
放置偏移在创建时烘焙到各 Transform，之后不存在父对象移动带动子对象的隐式继承。
“移动整组”是显式多对象命令。Delete GameObject 永远只删除该 UUID，不递归删 Prefab 集合。
整实例删除必须另有明确计划、成员快照和每个对象的破坏性授权；初版可以仅提供单对象删除。

### C 覆盖与同步

覆盖 key 采用 templateObjectId/typeId/已登记 fieldPath，不使用列表 index 或任意反射 setter。
显示 override 来源、revert/apply、base version 和变更冲突；允许有限字段/组件覆盖，初版结构覆盖可明确拒绝。
普通 scene v2 客户端不能借 set_component 自动写 OverrideSet 文件：若编辑涉及实例覆盖，
必须走携带 asset revision/文件范围的项目命令，缺授权返回 requires_project_transaction。
没有 Prefab 的旧 scene 命令保持原行为，不能给已有客户端扩大文件权限。

模板更新先生成三方同步计划：base/current template/instance override。
模板新增/移除/改类型、实例独立删除、字段冲突和缺资产分别诊断；不自动覆盖用户变化。
同步可能删对象时风险为 destructive，需对应 UUID 集合逐项批准。
禁止更新模板就自动批量改所有打开场景/活动 Play；只提交当前批准范围。

### D 历史与恢复

Prefab/OverrideSet 创建或修改与 scene metadata 更新加入唯一 EditSession 历史，通过 journal 可恢复。
Undo 后 object handles 失效，按持久 UUID 重新解析 selection/membership/资源；不保留旧 GameObject 引用。
外部模板或 override 文档改写时 Undo/Redo 报冲突，不覆盖；generation 被历史和 Play 租约 pin。
Unpack 是显式解除关联，保留当前普通对象/组件；不删除原 Prefab source。

## 3 测试与退出门禁 G7

- 提取、保存、重启、多实例不同 UUID、内部引用重映射、放置矩阵与扁平实例列表。
- 单对象删除不删除同实例其他对象，骨骼不进入 scene tree；显式整组操作的审批范围。
- 组件/字段覆盖、revert、base变更、重名/删对象/改类型冲突、unpack 与资产缺失诊断。
- stale template/asset/scene revision、外部文件改写、失败/超预算零部分对象、publish恢复和 Undo/Redo。
- Scene-only v2 请求不能触发未经许可的文件写入；Play pin 与 编辑实例同步隔离。

完整双配置回归后记录 G7，注明 Runtime spawn、嵌套 Prefab、Variants 和通用继承尚未实现。
统一使用 `Build.bat -Configuration Debug` 和 `Build.bat -Configuration Release` 执行自动回归，不使用 Skip，也不从未初始化的 shell 直接调用 Ninja。
Prefab 内部关系不改变全引擎扁平场景约束，也不将它设计成 Actor 基类。
