# M3.4 场景组件与 DX11 场景渲染方案

日期：2026-10-05。状态：开发中；A 的组件/文档/组合校验、生产只读资产解析/CPU 租约与 B 的 CPU 提取基础已实现，GPU 协调器、C/D 全部门禁与 G4 尚未完成。依赖 G3。目标是从固定 reference 预览进入真实多对象场景。
仍为扁平空 GameObject + 可选组件，骨架/导入辅助节点不是场景父子对象。

## 1 组件与依赖

已新增独立无 Renderer 插件/Editor 依赖的 Ncma.Scene.Rendering；依赖 Assets.Runtime 进行帧外只读准备，Editor/Player 可信 bootstrap 共用注册：

| 组件 | 建议数据 | 约束 |
| --- | --- | --- |
| StaticMeshData | mesh UUID、materialSet UUID、visible/castShadow/layer | 必需 Transform；不存 buffers/material 数组 |
| SkinnedMeshData | character/mesh/skeleton UUID、materialSet UUID | 已定义单个 typed SkinnedMesh 引用；G5 补角色多 mesh 调度和 GPU 运行 |
| CameraData | projection/FOV/near/far/viewport/layer | 必需 Transform；无“第一个对象自动当相机” |
| DirectionalLightData | 线性颜色/intensity/shadow flags | 必需 Transform；首切片最多一个投影阴影主方向光 |
| MaterialOverrideData | 可选材质 UUID/小标量覆盖 | 不塞动态引用/数组，继承方式明确 |

所有字段为已支持的 Guid/标量/嵌套值类型；引用解析和 CPU/GPU lease 由独立服务持有。
注册在 Editor/Player 同一 bootstrap，Runtime/Scene 不反向引用 native/Editor。
文档仍为 .ncmascene JSON v1，组件各有稳定 typeId/version；未知注册/版本严格拒绝。

## 2 实施切片

### A 完整文档和作者校验

Scene/Core 的完整快照自然保存组件，但新增受控组合校验检查必需 Transform、asset kind、
相机 near/far、材质槽和骨架兼容性。事务候选校验全部通过后才提交，remove Transform 同样检查。
不全局改写 TransformData 对非渲染对象的既有语义；M3 renderable 暂拒绝负/奇异缩放。
Editor 缺资源保留 UUID 并显示占位/诊断，不能抹去未知引用；Player 对启动必需资源严格预检。

### B owner-thread 渲染提取

从已提交 World 读取 relevant 组件，构造有界 RenderSceneView：camera、lights、draw items、
object UUID→frame-local index、矩阵/bounds 与资源版本，绝不携带 live World/组件引用跨 ABI。
不在每帧 CaptureBytes/JSON round-trip；初版可扫描有界 relevant 对象，随后用提交变化通知缓存。
通知只使缓存失效，不能直接写 World；Undo/restore generation 或外部修改必须清缓存/重解 UUID。
无 Transform 对象不生成 draw。玩法造成非法渲染数据时返回确定诊断、保留已提交 tick，
不将渲染失败冒称玩法 rollback；GPU batch 先完整验证，执行后故障仍为 fail-stop。

### C 静态场景管线

提供默认 Scene3DPipeline：主方向光 shadow→opaque/alpha-mask geometry→HDR/tonemap。
场景相机与 Editor 独立浏览相机明确切换；0/多相机由用户显式选定，不依赖列表顺序。
实例按 bounds 做 CPU frustum culling，材质/资源排序只在不改变结果时合批。
阴影与主渲染使用同一 geometry/object transform 版本，但分别按灯光空间与相机空间裁剪；相机外 caster 仍可投影。现有 PCF/PCSS/接触阴影参数受控复用。
M3 限定一主方向光；点/聚光多阴影、IBL、透明、clustered lighting 不作为该阶段实现承诺。
默认与用户 Feature/stage/完整 pipeline 替换使用公共 typed graph 和资源契约，编译器检查依赖/能力。
静态/蒙皮路径共享 scene 输入，G5 加 palette，不为角色另建一个通用 native scene。

### D 生命周期与模式

Edit 和 Play 分别拥有 document/cache/view，asset generation 以租约共享不可变资源。
Play/Stop 不改 Edit 组件；GPU frame-local index 不能在恢复后继续代表旧对象。
Null/无三维场景时不创建 shadow/rig/3D buffers；保持 pure 2D 未来可独立应用。
对缓存中 resources 与 render graph 的 revision 分开判断，不能每个 World tick 重建 GPU pipeline。

## 3 测试与退出门禁 G4

- 注册/序列化/保存重启、完整 Undo/Redo、可选 Transform、不支持组件/错误资产类型/非法相机拒绝。
- 0/1/256/4096 relevant 对象提取、非空间对象、缺资源、bounds/culling、材质分段和多实例共享。
- 非对称轴向/绕序/非均匀对象缩放、camera clipping 与实际多对象 PBR/阴影参考图。
- 阴影 caster 在相机外仍正确投影；主视口 culling 不能直接当 light frustum culling。
- Play/Stop/恢复/外部修改/资源 reimport pin、帧 generation、跨线程和批次越界负例。
- D3D11 validation errors/warnings=0、close/resize 资源释放与分层性能日志。

完整双配置回归通过后交付 G4 记录，明确它仍是静态场景切片；不把 SkinnedMesh schema 定义称为 GPU 蒙皮完成。
规范命令为 Build.bat -Configuration Debug 与 Build.bat -Configuration Release，顺序执行且无 Skip。

## 4 本次启动切片与剩余工作

- A 基础：五类值组件 v1、闭合必需字段 schema；SceneDocument 可信构造期组合校验在 normalized 完整候选安装前执行，保存同样预检。Undo/Redo、删除 Transform、组合冲突、正可逆渲染矩阵、相机参数、单主光、材质槽和 rig 身份已有自动测试。普通对象的 Transform 语义不变；不将作者校验加入 gameplay commit。
- `PreparedSceneAssets` 是不可变类型/版本/hash/bounds/slot 元数据，不是 GPU lease。现新增 `Ncma.Assets.Runtime` 及 `SceneAssetPreparation`：可信启动/显式刷新解析 typed catalog/作者材质/严格 NCA generation，校验 hash/模型/纹理语义并保留 CPU/file lease。Editor 缺失模式、已知错误类型安装前拒绝、Player 严格资源启动预检已接入；UI 缺资源占位、自动增量刷新和 GPU 协调器未完成，不称 A 全部完成。
- B 基础：直接读取 committed World 值，复制多对象矩阵/UUID/版本及明确相机；RH/-Z/0..1 深度裁剪。阴影只形成独立保守 caster 集合，尚无 light-frustum 裁剪或阴影绘制。无选择不自动用首个相机；非法已提交渲染数据仅诊断，不 rollback gameplay。
- 初版按 World identity/revision 与 prepared metadata identity/显式相机/尺寸失效；未变化提取命中无分配。变化时有界扫描并分配新只读 view，尚未做分组件增量 dirty 通知优化。`FrameIdentity` 是不可变 view 的识别 token，不是 GPU 帧计数；旧 view 可读，但新 World/metadata 不复用旧映射。
- D 的 Edit/Play 文档隔离、拷贝保留校验策略、独立 CPU generation/file pin 已测试。Play 校验冻结在自己的 metadata；Editor 刷新不改变 Play 代数。GPU pin、Play/Stop/resize 图形资源生命周期仍待协调器验证。
- C 的真实 geometry shadow → HDR → tonemap、公共多阶段 typed graph、Editor/Player 场景消费未实现；默认展示仍明确为 DX11 reference 预览。不得把保守 caster 列表或已有 M3.3 资源图片当作真实场景阴影证据。

基础验证见 [M3.4 启动记录](M3_4_FOUNDATION_REPORT.md)，生产资源进度见 [资产解析记录](M3_4_ASSET_RESOLVER_REPORT.md)。图形 Player 带 mesh 资源的场景当前明确返回未实现，不偷偷使用 reference cube。G4 保持未关闭，不进入 M3.5。

本批 A/B/CPU 租约切片已通过完整顺序 Debug/Release Build.bat（各 29/29 CTest，67/67 场景定向项，42/42 Python）；用户授权测试后提交推送。真实 C/D GPU 管线及其图像/validation/生命周期门禁尚未实现，不以本批回归结果关闭 G4。
