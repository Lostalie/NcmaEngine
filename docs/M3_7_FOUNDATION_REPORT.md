# M3.7 Prefab 只读基础候选记录

日期：2026-10-06。源码基线：`5c14bb9150d7638c179bfd08f4586b458850a40c`。
状态：A 格式/提取和 B 放置预检候选完整顺序 Debug/Release 回归通过；不是完整 A 保存流程或 B/C/D 交付，G7 开放。用户已明确要求开始并继续 M3.7；G6 剩余缩略图、独立材质浏览和人工窗口项仍开放。

## 已落实的边界

- `Ncma.Scene.Prefabs` 提供严格 `.ncprefab` JSON v1 DTO/codec：≤16 对象、4 MiB、显式 pivot、模板版本、内部对象引用和 typed 外部资产依赖。拒绝重复/缺失/未知字段、旧 scene 格式、嵌套/变体和身份碰撞。只有扩展名检查，**尚无生产文件读写服务**；测试夹具读写不等于持久化交付。
- 从宿主批准的精确 Edit 对象集合提取副本，检查 owner thread、World identity、revision、当前范围及只读 guard。对象/绑定换成新模板 UUID；只按可信登记的组件属性路径重映射对象引用，不扫描 UUID 样式字符串，不携带 runtime handle/tick/private script state。
- 组件需在宿主显式许可；Behaviour 默认拒绝，必须提供可信类型/Exports 校验器。RenderPrefabPolicy 登记已实现 mesh/skin/clip/material 的 typed UUID 路径和组合约束；不添加 Python 玩法、动态代码加载或 native World。
- 独立展开预览生成新 instance/object/binding UUID 和映射。放置用 `delta=placement-pivot` 平移每个独立 Transform，保留旋转/缩放；System.Numerics 行向量为 `S*R*T*Translation(delta)`。无 Transform 的空对象仍为空，不引入 parent 或变换继承。
- `EditorPrefabWorkspace` 是可信本地宿主 API，拒绝旧 view stamp、Play（含暂停）、frozen、未提交 draft、失效历史、外线程和重入；**没有 GUI 按钮或 MCP 注册**。提取不取消草稿、不修改选择/历史/场景。
- 放置检查模板 base version、规范化内容 hash、已有对象/绑定与资产 UUID 冲突、可信缓存提供的资源依赖可用性，保留宿主的场景组合校验，在隔离副本中检查“原场景 + 展开对象”的完整 4096 对象/4 MiB 预算。计算创建/组件/绑定操作数并额外预留每个对象的 membership 写入，拒绝超过128，不拆批绕过。
- 放置报告是可修改的**检查副本而非执行票据**；其 CandidateBytes 尚不含未来 membership/OverrideSet 数据，最终项目事务必须重新校验全部预算、资源版本/租约、权限和文件范围。没有直接 World 写入或第二套 Undo。

## 测试及证据

Scene 测试覆盖严格格式/未知组件/绑定、范围/陈旧/线程/回调写入、精确引用清单、多个实例 UUID、矩阵 oracle、空注册表/空对象、规范化与 owned data、冷 bytes、超限拒绝；Rendering 测试覆盖静态/角色 typed 依赖和组合校验。

新增 Editor 测试覆盖只读提取/放置和保持句柄/选择/历史、caller DTO 改写隔离、缺依赖/模板版本/身份碰撞、Play/draft/frozen/外部修改、回调写入/重入、宿主组合策略、完整对象/字节预算，以及未来 membership 的操作预留。

最终顺序完整 `Build.bat -Configuration Debug`、`Build.bat -Configuration Release` 均退出0，无 Skip；每配置执行31次 CTest（native11 + managed/application20）、Python43/43、managed/native smoke、inspect、新格式/旧格式拒绝、三轮保留 profile 和部署前后审计。Scene36/36、Scene Rendering70/70、Editor Services50/50；本候选新增19个测试（11 + 2 + 6），没有新增编译警告。原有 Scene Rendering 输出的 G4 pending 是该 CPU foundation 套件的历史范围，不用它覆盖已有独立 GPU/G4 记录。

完整日志：`out/verification/m3-7/Debug-final.log`、`Release-final.log`；复制的 managed CTest 明细为 `Debug-ctest.log`、`Release-ctest.log`。Release 已按 checked 全包部署至 `out/bin/NcmaEngine.exe`；可恢复备份保留在 `out/deployment/17e5d52be66f4ada8bb5a4252873a955/backup`。部署前后 `audit_passed=true`，`h8_accepted=false`，pending 门禁不改。

初次单独运行 Editor Services 在既有 M3.6 SourcePlan 文件操作中超时（`Asset file operation failed`），详见 `editor-services-debug.log`；原因未确认，不宣称已修复该偶发问题。之后最终完整 Debug/Release 中该真实导入/重启用例均通过，分别13.19s/9.17s。所有失败/中间日志保留，不删除证据或降低断言以换取通过。

## 未实现与下一步

1. 完成 A 的受控 `.ncprefab` 文件/资产发布与读回，关联唯一 EditSession participant/journal，不能自行加独立文件 Undo。
2. B：membership 值组件、完整实例/OverrideSet 身份、精确创建/组件/绑定范围及资源代数租约预检，文件与 scene 一次原子发布/选择，失败无部分实例。当前仅 detached preview；单对象删除测试仅证明普通扁平 World 不递归，不冒充已发布 Prefab 删除验收。
3. C：登记字段覆盖、revert/apply、三方同步/冲突、普通 scene-only v2 的 `requires_project_transaction` 防越权、明确 destructive UUID 范围。
4. D：联合历史、外部文件冲突、generation/history/Play pin、publish 恢复与 unpack。

Runtime spawn、嵌套 Prefab、Variants、通用继承、Prefab UI/MCP 尚未实现。完整 M3.7 和 G7 不能由本候选的自动回归代替；M2 人工/自包含/性能/长稳亦不关闭。
