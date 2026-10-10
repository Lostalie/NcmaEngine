# M7.2 材质表面合同

## 所有权与范围

C# `MaterialSurfaceContract` 是纯值策略：六槽语义、显式 packed 通道、封闭预设、最多六行诊断。资产文件仍为严格 `.ncmaterial` JSON v1，UUID 不变，不序列化句柄。不引入新 native ABI、shader、World、Python 游戏脚本或 Agent 执行能力。现有 DX11 Scene/Resource/compute skin 数值路径复用；Vulkan/OpenGL 留到下一版本。

| 槽 | 纹理语义 / 解码 | 缺省采样 / 行为 |
| --- | --- | --- |
| BaseColor | Color，RGB sRGB → linear，alpha 不解码 | 白色乘线性 BaseColor |
| Normal | Normal，linear XYZ | 无图时关闭 normal map，使用顶点法线 |
| Metallic | Data，linear，显式 R/G/B/A 通道 | 白色乘 metallic，默认因子 0 |
| Roughness | Data，linear，显式 R/G/B/A 通道 | 白色乘 roughness，默认因子 .5，最终下限 .045 |
| Occlusion | Data，linear，显式 R/G/B/A 通道 | 白色，不遮蔽环境项 |
| Emissive | Color，RGB sRGB → linear | 白色乘线性 Emissive，默认因子 0 |

BaseColor/Emissive 因子本身是线性值，不再次 gamma 解码；Emissive 在线性 HDR 中相加，再统一 ACES/显示编码。AO 仅影响当前环境项，当前 ambient **不是 IBL**。alpha mask 使用 BaseColor.a × 采样 alpha；无半透明混合或自动材质类型推断。

`WithPackedSurface` 显式选择 ORM（R=AO/G=roughness/B=metallic）或 MRA（R=metallic/G=roughness/B=AO），设置同一 Data UUID 的三个槽，metallic/roughness 因子均为 1。不猜测文件名、不自动转换导入素材。不允许同一 UUID 混用 Color/Normal/Data；Color 可共享 base/emissive，Data 可共享三个标量槽。`Bindings` 返回独立六行副本。

## 验证、诊断和失败

- MaterialCodec 的所有编码/解码入口验证跨槽 role；运行资产加载、NCP1 运行包、GPU 缓存和直接 CreateMaterial 共用语义映射。错误实际纹理语义必须拒绝，缺 UV/切线或关闭 normal map 都不能绕过。
- 缓存先解析并检查全部六槽的 UUID/generation/hash/semantic，再创建纹理和材质。身份或语义失败不会创建/上传 GPU 资源。原缓存代次、预算、依赖 lease 和释放顺序不变。
- 明确区分“缺失”与“错误”：Editor 的原显式 missing/unsafe fallback 仍可用；`Inspect(strictMissing:false)` 返回 `missing_texture` 和槽/UUID。strictMissing=true 拒绝缺图；运行包的完整闭包始终拒绝缺失。错误类型/语义始终拒绝。
- `Inspect` 是有界纯校验，不含文件路径、源码、GPU 句柄，不访问 live World；lookup 是可信宿主解析器，不是 Agent 回调。实际文件/pin/manifest/运行包所有权仍由原服务负责。

## 切线与静态/动画一致性

维持现有正行列式可逆 affine 变换合同：负缩放、奇异变换仍拒绝（原测试保留），不是新增支持。镜像 UV 使用 tangent.w=±1；normalYDown 独立改变采样 y；normalScale 只作用于采样 xy。法线 inverse-transpose，切线线性变换后正交化。

新增真实 GPU 测试覆盖正非均匀对象缩放/旋转、正非均匀单骨 palette 变形、倾斜顶点法线、两种 UV 符号、两种 Y 方向和三个 normalScale。静态组合矩阵与实际 compute skin 使用同一材质/Scene shaders。skin 更新与绘制必须同帧，保留原校验。并非任意负骨缩放、多骨退化/反折形变的普遍正确性声明。

## 预设与命令权限

Default：Opaque/metallic0/roughness.5；Matte：Opaque/0/1；PolishedMetal：Opaque/1/.2；Cutout：AlphaMask/0/.5、cutoff.5。均重置 normalScale=1；Default 同样重置 cutoff=.5，Matte/PolishedMetal 保留原 cutoff。均保留 UUID、线性色值、贴图和通道。不隐式清除用户纹理；例如 roughness 图仍会调制预设因子。

`MaterialCommands.PresetInput` 只构造已有 `ncma.assets.material.edit` 输入，不执行/授权。宿主仍必须提供原确切 session/revision/path/UUID/dependency grant；磁盘事务、撤权、冲突、Undo/Redo 沿用唯一历史。没有新增免审批 Agent endpoint，也不宣称材质面板按钮/人工操作已经完成。

## 证据边界

独立 CPU GGX/ACES/sRGB 固定探针容限 3 个 8-bit 色阶；静态/compute skin 全图容限 1；DX11 validation 必须 0/0。原 UI 独立运行包、实际 FBX/NCA 动画和 shader 包门禁继续运行。缓存计数证明测试场景稳定帧没有纹理/网格重建或上传，不是完整性能验收。真实用户素材、手动 UI/MCP、目标机、自包含、完整性能和 1h 长跑仍待验收。
