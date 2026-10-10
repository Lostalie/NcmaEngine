# M7.3-C1 环境资产与场景配置合同

## 所有权与范围

纯 C# 持久化/资产/场景配置合同。`AssetKind.Environment=11` 追加在既有 AnimationGraph=10 之后，旧数值不变；NCA1 tag 1–10、native ABI/Cook/shader/渲染实现不变。环境是独立数值资产，不是 Texture/model 子资产。`AssetRecordCodec.SupportsKind` 明确冻结 `.ncmeta` v1 原11 kind，拒绝 Environment root/subasset/dependency；既有 metadata-only MCP 的三份 schema 与原黄金哈希不变，查询 kind=environment 仍拒绝，不靠扩大全局 enum 偷开旧格式/检查能力。专门的环境目录检查接入留给 C3。C1 不提供 HDR/EXR 文件解码、环境资产写入工具/面板或正式 IBL 渲染接入，也不授权新 Agent 文件/GPU/编译能力。

## 描述与代次文件

`.ncenv` 是严格 JSON v1，全部六字段必填：version、projectId、assetId、generation、contentHash、path。对象/未知/重复/缺失字段、非规范 uppercase SHA、空 UUID/零 generation、路径逃逸均拒绝。最多 8192 bytes、JSON depth4。路径必须精确等于 `out/assets/<project:N>/<asset:N>/<generation>-<SHA>.nce`，不接受大小写/前导零/扩展名/流路径变体。

描述是复制的目录快照，不是签名或可信算法来源证明。描述可更新为新代次；旧 lease 保留自己的不可变包。加载只打开请求闭包的 NCE1 文件，以描述中的独立 expected SHA 校验，再核对 NCE header asset UUID/generation；NCE1 自身源/layout/payload checksum/数值预算校验保留。`.ncenv` 不含原始源文件或像素，不会在运行时 Cook。

复用 RuntimeReadPin 的规范 root、所有父目录身份锁和不可变文件 read lease，拒绝 reparse/hardlink/非规范 casing/写/rename。取消在读循环及准备边界检查；只限 Windows 文件 pin。描述文件只在扫描期间 pin，不长期锁定可更新目录元数据。共享 snapshot/lease owner-thread-only；最终 lease 释放 generation pins。只读扫描最多16384 entries/128 environment descriptors，最多4096请求（去重前同样有界）/128 generation pins/512 MiB累计准备读取预算；每个环境包最多4 MiB。空请求/规范 Off 在不存在的 root 也不扫描或初始化3D。

Authoring/derived/environment UUID 冲突（含不匹配类型）拒绝。未请求的环境只解析描述，不读取其 NCE 文件。原 Editor missing 模式仍允许真正缺失的资产/代次返回诊断；错误 hash/header/type/项目/路径或 present-but-wrong generation 不允许 fallback。显式 NCP 路径缺失/损坏始终拒绝，绝不回退到 authoring 文件。

## NCP1 与场景闭包

原 NCP1 v1 增加闭合 `environment` encoding，只允许 Kind.Environment + ModelId/SkeletonId 为空。数据为完整 NCE1 原字节；entry asset UUID/generation/hash 必须与 NCE header/whole-file SHA 完全一致，不重写代次，不带 native handle/source/catalog/path。保留64 MiB/2 MiB index/4096 assets、顺序/偏移/完整长度/重复字段/整体依赖门禁。旧 encoding 的验证不变。仅包含实际 scene/dynamic 根请求闭包；可与原模型/材质/动画共包，可搬移并通过原包 pin 使用。

`ncma.render.environment` v1 是普通扁平 GameObject 的独立值组件，不需要 Transform，不引入 scene parent。字段 version/assetId/generation/contentHash/strength/rotationRadians/enabled 全部必填、closed schema。最多一个配置（包括 Off），重复时 whole-document candidate 拒绝。version1；场景 generation 限1..Int64.MaxValue（沿用当前 registry integer 支持范围），enabled 引用精确 UUID/generation/uppercase SHA；strength有限0..16、rotation有限[-pi,pi]。Off 精确为 UUID空/generation0/hash空/strength0/rotation0/enabledfalse，拒绝残留身份。strength0 的 enabled 仍有精确资产闭包；不把它当 Off 规避校验。

SceneAssetPreparation.References 仅为 enabled 配置加入 typed reference；共享 SceneRenderValidation 在有 prepared metadata 时核对精确 generation/hash/kind。配置本身与源数据/GPU发布分别验证；场景命令不会触发文件 IO/Cook/GPU 创建。

## 编辑权限与正式宿主边界

`RenderingEditorAdapter.EnvironmentRequest` 只构造原 `ncma.scene.transaction` 输入，不执行也不授权。沿用同一 history、host permission/object/component scope、session、expected revision、幂等 retry、Undo/Redo 原始授权和 Play freeze。Undo/Redo 会恢复完整 snapshot 并失效旧 runtime references，用户/Agent 都必须按 UUID 再解析。没有新增 MCP endpoint、推理/IPC wait 或 live GPU/world 写通道。

C1 的正式宿主尚未应用此配置：Editor 在创建 plugin/log/window 前明确拒绝 enabled，其正式 composition policy 也拒绝运行中的开启命令，原文档/历史不变；Edit/Play presentation 发现运行时 enabled 时失败，不忽略。图形 Player 先验证资产闭包，之后在 module/gameplay/PlaySession 前返回 feature_unimplemented；BindScene/Present 同样拒绝 gameplay 后开启，不能绕过 startup guard。损坏环境先返回 dependency_failed，不被 pending 标签掩盖。Headless 无图形环境安装；原按需 package/animation 准备路径不变。C2 必须用真正 profile3 默认/user 两 shadow variants + shared skin 的实际准入替换这些明确 pending guards，不能通过删除 guard 就宣称接入完成。

## 证据边界

C1 测试的 NCE 是独立写 header/checksum 的 CPU 存储夹具，不是 Cook 算法/物理图像 oracle。原 A123/B1 54/B2 46/B3 353 与实际 FBX/NCA/GPU/API0/0 测试继续运行。C1 不改变正式 shader/default selection/环境部署，不关闭整个 C/M7.3、人工 UI/DPI/MCP、用户 HDR/FBX、目标机/自包含、完整性能/1h 门禁。
