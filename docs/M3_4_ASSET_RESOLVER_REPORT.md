# M3.4 生产只读资产解析与 CPU 租约切片

日期：2026-10-05。基线为已推送的 M3.3 `7919a87fc8572178d25afa80bce0e1791fe88d39`。
状态：本切片实现完成，完整双配置回归通过；这是 A/B/D 的资产基础补齐，不是 C 或 G4 完整交付。

## 所有权与实现

新增 `Ncma.Assets.Runtime`，仅依赖 `Ncma.Assets`，不依赖 Editor、导入 worker、GUI、渲染插件或 Python。
可信主机在启动/显式刷新时调用 `RuntimeAssetLoader.Prepare`；没有逐帧目录扫描、源 FBX 解码或图片重导入。
Windows 只读文件边界独立于作者写入服务：校验项目/UUID/代数的规范路径，拒绝重解析点、文件硬链接和大小写冲突，持有父目录身份及不可写/不可删除的 NCA 文件句柄。该实现目前仅支持 Windows，不宣称跨平台文件租约。

- 读取严格 `.ncmeta`、`.ncmaterial`、`.ncmatset`；身份冲突、未恢复 journal、未知字段/版本与类型错误拒绝。
- 从项目所属 `out/assets/<project>/<root>/<number>-<SHA256>.nca` 加载：完整文件 SHA256、NCA block SHA256、模型 manifest/typed mesh/slots/rig 内部关系均校验。
- StaticMesh 模型根是 manifest，不自动选择其中第一块 mesh；场景必须引用 typed mesh 子资产 UUID。
- 材质集合的 UUID 依赖闭包解析到作者材质/TextureData 完整 mip；Color/Normal/Data 用途不匹配拒绝。缺资源模式只允许缺失，不豁免损坏、错误类型或哈希错误。
- MAT1 只有导入槽名，保留 `imported_material_slots_only` 诊断和空材质 UUID，不虚构源 PBR 参数。实际 GPU 默认材质回退仍待协调器实现。
- 作者材质/集合的 generation 是规范内容 SHA256 派生的非零 64 位内容 token，不是磁盘单调代数；完整 hash 同时保留。派生 NCA 则使用已提交 generation number。
- 预算：扫描 16384 entries、4096 输入/闭包 typed refs、128 个 NCA pin、每 NCA 64 MiB、准备计费总量 512 MiB。加载整份已验证模型包，非只加载被引用子块；这些是有界内存/IO保护，不是性能验收证据。

`RuntimeAssetSnapshot` 和 `RuntimeAssetLease` owner-thread 使用引用计数共享不可变 CPU 资源；最终租约关闭才释放文件句柄。
没有公开可变数组/现场 World，也没有 GPU handle。准备失败关闭候选句柄，不发布半成品；旧租约保持有效。
作者文件在准备时读取为独立不可变值，旧值不要求锁住可编辑源文件；NCA 代数文件则被实际锁定。

## Scene / Editor / Player 接线

`SceneAssetPreparation` 从完整启动/刷新候选提取 UUID refs，将已验证资源转换为 `PreparedSceneAssets`；不在 simulation/render tick 使用此入口。
Scene.Rendering 无 Editor/Renderer 插件依赖，现依赖上述独立只读资产服务。CPU view 继续只读取已提交组件和 prepared metadata，不进行场景 JSON round-trip。

Editor 项目启动准备资源；可信 `PrepareRenderAssets` / `RefreshRenderAssets` 保持先准备后替换。
作者候选校验只使用准备的 metadata 与当前 catalog，已知错误类型/模型根引用在安装前拒绝；缺资源保留 UUID。
Play 取得独立租约及冻结 metadata 的组合校验策略，不借用随 Editor 刷新变化的策略。刷新后 Edit 可使用新代数，运行中的 Play 保留旧代数；Stop 释放 Play pin。
刷新 API 已实现，但 UI 刷新按钮、提交后自动增量准备和真实 scene GPU 消费尚未接线，不宣称导入完成即自动出图。

图形 Player 在加载 gameplay/插件之前严格资源预检；缺资源失败且不创建 GPU。
真实带网格资源的场景目前返回 `feature_unimplemented`（内部 `scene_3d_pipeline_unimplemented`），不使用 reference cube 冒充正式场景。
空图形场景保留明确的 reference 预览；Headless/Null 不解析/固定 3D 资产，保留渲染组件 UUID 后执行纯托管固定步。
没有删除现役 C# Player，也没有恢复旧类型/桥接/格式或引入 C++ World。

## 验证

`NcmaSceneRenderingTests` 已扩展为 67 项：原 40 项基础，加 27 项资源及 Player 预检测试。
新增覆盖无源 FBX 的派生加载、独立 payload 拷贝、导入槽诊断、类型/root/缺资源/缺代数/项目/path/哈希/block/作者 JSON/重复 UUID/tombstone/journal/预算/取消/线程负例，材质纹理闭包与语义、owner 关闭后 Play pin、新旧代数共存、刷新失败原子性、Editor/Play 冻结版本、图形 Player 无 GPU 启动拒绝。
`NcmaPlayerTests` 新增缺 mesh 的 Headless 成功路径，验证不装载 3D 或 native。

定向 Debug 67/67 已通过。首次完整 Debug 中场景测试通过，既有 `NcmaAssetTests` 在 60 秒达到超时；单独复测 38/38 通过。保留全部边界断言，增加逐用例计时，将该组包含 64 MiB descriptor 和 16384 个文件创建的 IO 测试 watchdog 调整为 180 秒，重新跑完整回归；这不是忽略失败或减少测试。
完整验证使用顺序、无 Skip 的：

1. `Build.bat -Configuration Debug`
2. `Build.bat -Configuration Release`

首次失败日志保留在 `out/verification/m3-4/Debug-assets-closure.log`，最终日志为 `Debug-assets-final.log` 和 `Release-assets-final.log`。
最终两次完整 Build.bat 均退出 0，严格新格式/移除格式拒绝、托管/原生 smoke、独立包、只读审计、三轮保留 runtime 测量与恢复式部署均通过：

| 项目 | Debug | Release |
| --- | --- | --- |
| CTest（native 10 + managed/graphics 19） | 29/29 | 29/29 |
| Scene/render/asset 组合测试（CTest 内） | 67/67 | 67/67 |
| 既有 Asset authoring（CTest 内） | 38/38，9.641s | 38/38，9.498s |
| Player（CTest 内） | 23 项 | 23 项 |
| Python inspect / unittest | 成功 / 42/42 | 成功 / 42/42 |
| 新代码编译 | 0 warnings / 0 errors | 0 warnings / 0 errors |
| 256 对象，4096 同版本缓存命中 | 0 bytes / 0.7768ms | 0 bytes / 0.6812ms |
| 保留 resource-pbr-v3 validation errors / warnings | 0 / 0 | 0 / 0 |

缓存耗时仅为本机该次 CPU cache-hit 测量；resource-v3 不含真实场景阴影，不能替代 G4。
完整 CTest 内容独立保存于 `out/verification/m3-4/{Debug,Release}-assets-managed-ctest.log`，缓存 JSON 为 `{Debug,Release}-scene-foundation.json`。
Release 已部署 `out/bin/NcmaEngine.exe`，部署备份为 `out/deployment/63894d47904644e18137437557e7cb61/backup`，audit_passed=true、h8_accepted=false，未关闭既有人工门禁。
已核对部署 DLL 与已测试 Release 输出 SHA256 相同：

- Ncma.Assets.Runtime.dll：`9D6A364CCFE696F13BF9C90D7016B834210EC4F95585CF058D36D7D73C3812F4`
- Ncma.Scene.Rendering.dll：`1FBD808B166754BBF32660BF3D93D59ABF142E6332B97B4EFCD3882F227F9338`
- NcmaEngine.dll（RID Release app）：`3231FECB65C58644A829079FEF257EB0C534A4C34107512C2E15A6EF5F544F8F`

## 下一步 / 未完成

GPU 资源/材质上传协调器与 frame lease、实际多对象 shadow → HDR geometry → tonemap、公共 typed 多阶段 graph、light-space caster 裁剪、Editor/Player scene 展示仍未实现。
G4、实际场景参考图/validation/resize/close/分层性能，以及既有人工 UI/MCP、自包含环境和长稳验收保持 pending；不开始 M3.5。
用户随后明确授权测试通过后提交推送：本批仅提交源码、测试和文档，排除生成部署/证据/备份及无关 `.vs` / `.user` 设置。提交不表示整个 M3.4 或 G4 已完成。
