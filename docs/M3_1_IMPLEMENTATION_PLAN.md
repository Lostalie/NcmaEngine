# M3.1 资产身份与数据库方案

日期：2026-10-05。状态：已实现，最终 Debug/Release 回归通过，G1 关闭。前置：[M3 总览](M3_IMPLEMENTATION_PLAN.md) 的模块/坐标/历史边界已获执行授权。
格式/索引/watcher、元数据与联合移动/复制历史、项目生命周期和文件事务恢复已交付；详细范围/限制见 [交付记录](M3_1_DELIVERY_REPORT.md)。
目标是建立可重启、可检查、可重建的资产身份基础，不先实现 FBX 解析或 GPU 绘制。

## 1 数据模型

拟新增 Ncma.Assets：AssetId/AssetRef 值类型、AssetKind、AssetRecord、SubassetRecord、
AssetDependency、ImportSettings、AssetGeneration、AssetDiagnostic 与只读 AssetCatalog。
AssetRef 包含持久资产 UUID 与期望种类；子资产有自己的 UUID，并保留所属根 UUID。
版本 hash/generation 不作为身份，索引 key 不使用绝对文件名或本次数组顺序。

来源与 .ncmeta 配置保存在 assets/，元数据记录来源相对路径、source hash、导入器/格式版本、
设置、子资产 UUID 映射、依赖和上次成功 generation。材质/Prefab 作者文档亦为 source。
派生块位于 out/assets/<projectUuid>/，只读索引可由描述重建；删缓存不能丢 UUID。
MaterialSet 数组属于资产，不属于值组件；不为了它给 Runtime 接入 native 或引用类型组件。

## 2 实施切片

### A 格式和数值约定

冻结 JSON v1 字段、closed schema、UTF-8、有限值、UUID 编码和类型表。
确定右手/Y-up/metres/-Z 前向、矩阵布局、UV/绕序及单位转换字段；以非对称夹具测试。
派生二进制采用显式 little-endian/块表/版本/长度/hash，禁止直接写 C++ struct 内存布局。
.ncmascene 仍为既有 v1，不写入 GPU/native handle 或恢复旧格式。

### B 路径与目录索引

统一批准的项目相对路径，拒绝绝对路径、..、ADS、符号链接/junction/reparse 和大小写冲突。
创建目录与写入前再次验证所有父路径；duplicate UUID、未知类型、未知字段、溢出/NaN 均拒绝。
文件 watcher 只发送有界刷新意图，不自行写元数据或触碰 live World。
首次扫描不把未知文件变成已批准资产，不删除用户文件；报告 orphan/missing/ambiguous。

### C 元数据命令参与者

扩展 Editor.Core 唯一历史的受控参与者接口，拟含 Prepare/Publish/Compensate/Memento/ValidateUndo。
接口使用 opaque 事务标识和有界值结果，Core 不引用 Assets/native/磁盘实现。
Assets.Authoring 提供创建元数据、修改导入设置、显式移动源文件与元数据命令；所有写入可恢复。
目录索引的刷新本身不生成历史，已批准的持久修改才进入同一 Undo。
为后续异步任务预留 expectedAssetRevision、source hash 与 project generation；不能只依赖 scene revision。

### D 原子保存与引用诊断

使用同卷 staging、CreateNew、flush、明确 publish 点与小 journal；重启后先恢复/拒绝未完成提交。
索引/descriptor 多文件并非天然原子，必须注入每个发布点故障并验证恢复，不只测试 File.Move。
缺资源不自动生成新 UUID。Editor 保留 dangling 引用并诊断，Player/cook 对必需缺资源拒绝启动。
外部改写被 Undo hash 检测捕获；拒绝覆盖新内容，不把源文件 IO 冒充 World 快照事务。

## 3 测试与退出门禁 G1

- 重启、重建索引、删 ignored 缓存后身份相同；移动命令保留 UUID，复制生成新根身份。
- 重名、重复 UUID、Unicode/空格/大小写路径、重解析点、路径穿越、损坏 JSON/块表负例。
- 资产类型/依赖检查、dangling 引用诊断、超限原子拒绝、未知文件保留。
- 创建/设置/移动的 Undo/Redo、redo 分支、外部改写冲突、旧授权拒绝和故障 journal 恢复。
- 现有 Scene/Runtime/Core/Play/IPC 不改变；Assets 基础库无 native/Editor 依赖。

拟新增 Ncma.Assets.Tests 与资产命令测试，接入完整 Build.bat 双配置。
交付文档须说明实际格式、目录、预算、恢复方法和仍未实现的解析/GPU。
G1 通过只表示资产元数据底座可用，不能称 FBX 已进入场景。
