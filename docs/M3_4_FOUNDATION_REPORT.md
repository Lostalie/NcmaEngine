# M3.4 启动切片：场景组件与 CPU 提取基础

日期：2026-10-05。基线提交：`7919a87fc8572178d25afa80bce0e1791fe88d39`。
状态：M3.4 开发中，**不是 G4 完整交付**，不开始 M3.5。

本文件保留首次 40 项基础切片的验证记录。后续生产只读资产解析、CPU 代数租约与启动预检进度见 [资产解析切片记录](M3_4_ASSET_RESOLVER_REPORT.md)；下文“尚未接生产 resolver”描述的是首次交付时点。

## 实现边界

新增独立 `managed/Ncma.Scene.Rendering`，仅引用 Scene/Runtime/Assets，不引用 Editor、Rendering、native 或 Python。
五类组件拥有稳定 `ncma.render.*` typeId/version 1：StaticMesh、SkinnedMesh、Camera、DirectionalLight、MaterialOverride；持久字段仅 UUID/标量，没有 GPU handle、材质/骨架数组、父子关系。
SkinnedMesh 是 G5 预留 schema，本切片遇到它返回 `skinning_unimplemented`，不把绑定姿态/CPU 提取称为 GPU 蒙皮。

SceneDocument 增加可信构造期作者校验，在完整 normalized restore/事务/Undo/Redo 候选安装前执行。
校验收到独立 DTO 拷贝，无法改写安装候选；原 World 在准备期间禁止重入写入。
保存前进行同一作者预检，失败不覆盖现有文件。捕获/检查不强制作者组合校验，便于诊断非法已提交数据；普通 GameObject 的可选 Transform 与既有缩放语义不变。
`CreateIsolatedCopy` 保留注册/校验策略、复制文档并生成独立 World；Editor StartPlay 使用这一方法。
作者校验不注入 gameplay fixed-step commit：非法已提交渲染数据由 extractor 诊断，不回滚已经成功的玩法 tick。

Editor/Player bootstrap 共用新增组件注册与结构组合策略，仍使用唯一 `.ncmascene` JSON v1。
没有新增旧格式、迁移器、兼容 Player、hostfxr 或 C++ SceneWorld。

## CPU 提取与元数据

`PreparedSceneAssets` 是可信资产服务帧外提供的不可变准备结果：类型、UUID、generation/hash、bounds、槽和 rig/character 身份。
给定这些数据时能拒绝错误资产类型、不足材质槽和不匹配 rig；Editor 缺资源保留 UUID 并给确定诊断，严格模式可拒绝缺资源。
它不读取项目目录、不验证磁盘文件 hash、不持有 GPU/generation pin，不是已经完成的生产 resolver。
当前 Editor/Player bootstrap 尚未接生产 prepared metadata；所接入的是结构组合校验。

`RenderSceneExtractor` 仅在 World owner-thread/committed safe boundary 读取值组件，不使用 CaptureBytes、全量文档快照或 JSON round-trip。
它复制 RH/-Z 相机、非均匀模型矩阵、资源元数据、UUID 到 view-local index 映射、CPU bounds 裁剪结果和独立保守 caster 集合。
没有显式相机/浏览相机就不绘制，绝不自动选列表第一个相机；多主光非法时不偷偷选其中一个。
caster 集合独立于相机裁剪/相机 layer，但尚未实现 light-space frustum 或任何新场景阴影 GPU 绘制。

缓存依据 World identity/revision、prepared metadata identity、相机/尺寸失效。
恢复/外部修改/替换资产代数后重新解析 UUID；旧 view 仍是不可变数值副本，不会暗中代表新 World。
变化时有界扫描/分配，未变化命中无分配；尚不是 dirty-component 增量提取或每 tick 零分配承诺。

## 验证

新增 `NcmaSceneRenderingTests` 已接入 CMake/Build.bat 的托管构建及 CTest 回归；native 初段排除此托管测试，避免冷构建提前执行未构建 DLL。
40 项定向用例覆盖：严格注册/闭合 schema/版本、文档保存重启、可选 Transform、事务拒绝的原子性、完整 Undo/Redo、作者回调写入/改写拒绝、clone 校验回调不能写原文档、缺资源/错误类型/slots/rig、Edit/Play 隔离、显式相机与 orthographic/near/far、相机外 caster/不同 layer、非均匀缩放、恢复/重导入代数/跨线程/固定步中提取拒绝、非法玩法渲染数据不 rollback，以及 1/256/4096 多实例共享准备结果。

初次测试捕获到测试自己在 restore 后使用旧 GameObject，以及缓存路径 LINQ 闭包每次 32 字节分配；均已修复，没有删减生产断言或恢复兼容类型。
缓存分配测量只针对 256 对象、4096 次同版本缓存命中；日志与 JSON 保存在 `out/verification/m3-4/`。
最终按顺序执行完整、无 Skip 的 `Build.bat -Configuration Debug` 和 `Build.bat -Configuration Release`，两次均退出 0：

| 项目 | Debug | Release |
| --- | --- | --- |
| CTest（native 10 + managed/graphics 19） | 29/29 | 29/29 |
| 新增场景基础用例（包含在 CTest 内） | 40/40 | 40/40 |
| Python inspect 与 unittest | inspect 成功，42/42 | inspect 成功，42/42 |
| managed/native smoke、独立包及恢复式部署 | 通过 | 通过 |
| 256 对象 / 4096 同版本缓存命中分配 | 0 bytes | 0 bytes |
| 同一缓存测量的耗时 | 0.8237 ms | 0.6578 ms |

这些耗时是本机这次有界 CPU cache-hit 测量，不是变化帧、整个 World、GPU 或 FPS 保证。
保留的 DX11 resource-pbr-v3 回归 validation errors/warnings 均为 0，不能代替尚未实现的 M3.4 场景绘制验收。
新代码编译 0 warnings/0 errors；严格 `.ncmascene` 与已移除格式拒绝测试继续保留在回归中。

证据：`out/verification/m3-4/Debug-foundation-closure.log`、`Release-foundation-closure.log`、
`Debug-scene-foundation.json`、`Release-scene-foundation.json` 与各配置 CTest LastTest 记录。
Release 经检查式包部署至 `out/bin/NcmaEngine.exe`，保留恢复备份/journal；本次源码尚未提交或推送。

## 下一步与未完成门禁

1. 生产 UUID → typed catalog/已校验派生 generation resolver；帧外解码/slot/rig 预检、独立 Edit/Play generation/GPU lease，Player 必需资源启动预检。
2. C：真正的多对象 shadow → HDR geometry → tonemap，以及同一公共多阶段 typed graph/Feature/stage 契约与 fail-stop ABI；不更改冻结 v3 的语义。
3. Editor/Player 消费 scene view，独立场景相机/浏览相机切换；Null/无 3D 场景不创建 3D 资源。
4. 实际多对象/相机外 caster 图像、D3D11 validation、resize/close/Play/Stop/generation pin 与分层成本证据后再关闭 G4。

当前默认图形展示仍是标注过的 DX11 reference 预览；本记录不把 M3.3 GPU 图片重新标记为 M3.4 场景证据。
M2 人工 UI/MCP、自包含目标环境、完整性能/长稳等待验收项仍保持 pending。
