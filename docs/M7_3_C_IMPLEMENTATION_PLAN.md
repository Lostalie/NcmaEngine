# M7.3-C 正式宿主、环境资产与配置接入

基线：B3 `abe290ef87fb068e0416c59fda915f163889312a`，2026-10-10 开工前 local/remote main 一致。B 的真实 DX11 数值/图像证据保留；不把独立测试管线称为正式宿主接入。DX11 only，Vulkan/OpenGL 下一版本。

当前：C1 自动候选完成，29 项资产/文件/包/历史测试与 1 项正式 Editor 命令拒绝测试、完整顺序 Debug/Release/checked deployment 通过，见[合同](M7_3_C1_RUNTIME_CONTRACT.md)、[交付](M7_3_C1_DELIVERY_REPORT.md)。C2-A环境Shader文件/整组准入71项及完整双配置/部署通过，见[C2详细方案](M7_3_C2_IMPLEMENTATION_PLAN.md)、[C2-A交付](M7_3_C2A_DELIVERY_REPORT.md)。C2-B共享场景执行87项及完整双配置/checked部署通过，见[C2-B交付](M7_3_C2B_DELIVERY_REPORT.md)；C2-C正式宿主和C3 未实现；正式宿主仍显式拒绝 enabled 环境，不能称已支持正式 IBL。

## C1：纯 C# 资产、场景配置与运行包合同（自动候选完成）

- 追加 `AssetKind.Environment`，原枚举数值、NCA1 tag 1–10、native ABI 不变。环境不伪装 Texture，也不扩展旧模型 NCA 合同。`.ncmeta` v1 及其 metadata-only MCP kind 闭包保留原11值/原黄金哈希；独立环境描述不能塞入旧 ncmeta 的 root/subasset/dependency，环境目录检查入口在 C3 明确接入。
- 严格 `.ncenv` JSON v1 只保存 project/asset UUID、非零 generation、独立预期 NCE1 SHA 和精确规范 `out/assets/<project>/<asset>/<generation>-<SHA>.nce`。最多 8 KiB，闭合字段/重复字段拒绝。它是已 Cook 资产的描述，不是 HDR/EXR 解码器或冷源 Cook。
- 原 RuntimeAssetLoader 扫描描述、只解析请求闭包，复用 RuntimeReadPin 锁定不可变 NCE1 文件和全部父目录。核对项目/路径/hash/header UUID/generation；失败释放新 pins，旧出版物不变。描述元数据是复制的目录快照，不长期锁住可更新的描述文件；旧、新 immutable generation 可共存，Edit/Play 共享 lease，末租约释放文件锁。
- `RuntimeEnvironmentAsset` 持有不可变 NCE1；NCP1 v1 新显式 `environment` encoding，拒绝错误 kind/模型角色/header UUID/generation/hash。运行包可搬移，不需要源、描述、Cook 或 GPU。旧 encoding 合同不变。
- 独立 `ncma.render.environment` v1 值组件挂在普通扁平 GameObject，不需要 Transform。最多一个（含 Off），enabled 必须引用确切 asset UUID/generation/hash；Off 必须是规范空值；strength 0–16、rotation [-pi,pi]。注册严格 schema；SceneAssetPreparation 与 composition 校验共享闭包，present-but-wrong 在 Editor missing 模式也拒绝。
- 配置使用原 `ncma.scene.transaction`/权限/session/revision/Undo/Redo/Play freeze，不新增 Agent 编译、GPU 或文件写权限。C1 不新增面板、不切换正式 shader 默认值；C2 前正式宿主显式拒绝 enabled 环境，避免默默忽略配置。
- 验证闭合 JSON/输入副本、类型身份/重 hash 损坏、missing vs wrong、规范路径/项目/链接/大小写/锁/取消/线程、搬移包、两代并存/Play 隔离、失败保留旧出版物、单一历史/撤销重做/冻结/权限、空场景无资产 IO。全部原测试保留。

## C2：Editor/Player 共用 SceneRenderSession（A自动候选完成，B/C待实施）

新 profile3 环境 shader 闭包显式接入 SceneRuntimeShaders、默认注册服务与 source-free selection；两 shadow variants/shared skin 先实际准入，禁止旧 profile1 自动冒充。PreparedSceneAssetLease 的确切环境进入共享渲染服务，配置刷新在受控 owner/off-simulation 边界进行；GPU 候选准备完整后发布，失败保留旧有效组、正确释放 scene pins，关闭/resize/Play/Reload 顺序受测。提交只消费已准备资源，不读文件/Cook/HLSL/推理。Player 在 gameplay 初始化前核对完整环境和 shader 闭包。原 ambient 明确保留独立含义，不改成假 IBL。

## C3：默认预设、部署与联合验收

build-only Cook 明确标记合成 neutral 预设，正式 3D 默认可直接使用；按产品闭包部署 NCE/NCS/index，纯 UI/Null 不带环境或 3D 文件。Editor 的属性配置入口使用原批准的可撤销命令，Agent 只复用同一有界 schema/capability；不新增推理服务。完整检查 relocated Editor/Player、源不存在运行、包损坏/pre-start、配置和资产代次增量缓存、实际静态/FBX-NCA动画/阴影/材质/环境联合图像与关闭、成本和 API0/0，旧断言不得降级。HDR/EXR 文件解码与真实用户素材另设实证，不以合成预设关闭。

## 每片门禁

定向测试后冻结源码/测试；顺序完整无 Skip Debug/Release Build.bat，12 native/22 managed/Python、smokes、严格新旧格式、M6/MCP、inspect、三轮 profiles、audits、checked deployment 的 SHA/manifest/journal/备份核验。保留失败日志和用户 IDE/SDK/资产。更新合同/交付/路线图/AGENTS，提交推送、远端 SHA 一致后再进入下一片。C1 不等于整个 C/M7.3；manual UI/DPI/MCP、真实用户素材、目标机/自包含、完整性能/1h 门禁保持开放。
