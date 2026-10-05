# M3.2 模型派生内容 v1

这些是 NCA1 容器内的 payload，不是 Import C ABI 的内存结构。容器、UUID RFC 字节序、
entry 排序与 SHA-256 遵循 `managed/Ncma.Assets/Schemas/nca-v1.layout.json`。
所有整数/IEEE754 数值 little-endian；不持久化 native/GPU/World handle。

## 模型根

root block 的 UUID 必须等于 descriptor.assetId。严格 camelCase JSON，≤64KiB/深度8，
拒绝重复、未知或缺失字段。字段为 version=1、root、staticOnly、sourceHash、settings、
skeleton（可空 UUID）、meshes（mesh/materials UUID 对）、clips（UUID 数组）。
每项 UUID 唯一、类型正确、对应活动 subasset；没有孤立或多余 block。
静态根没有 skeleton/clip；Character 根引用 Skeleton。

静态根和静态 mesh child 均使用 StaticMesh tag；必须按 root UUID 和 manifest 引用区分，
不能靠第一个 StaticMesh block 猜测根。材质当前仅为槽标签，不包含 GPU 材质或纹理。

## 类型化二进制块

每块先写四字节 ASCII magic 和 u32 version=1。count 均 i32，拒绝负数。
字符串为 i32 UTF-8 byte length + 严格 UTF-8 字节，无终止符；≤64KiB，拒绝 NUL。
TRS 为 position float3、quaternion xyzw float4、scale float3（共40 bytes）。

| Magic | Header 之后的字段/内容 |
| --- | --- |
| MSH1 | i32 skinned(0/1)、boneCount、vertexCount、indexCount、bindingCount、materialSlotCount、tangentCount；然后依序 vertices、tangents、indices、triangleMaterials、bindings |
| SKL1 | i32 boneCount；每 bone：name、i32 parent、TRS |
| CLP1 | i32 boneCount、name、f64 duration、i32 trackCount；每 track：u32 bone、i32 keyCount；每 key：f64 time、TRS |
| MAT1 | i32 materialSlotCount；依序 slot name 字符串 |

vertex = position float3 + normal float3 + UV float2 + mesh-palette joints u16x4 + weights float4，56 bytes。
tangent = float4，xyz 单位向量、w 为 ±1，16 bytes。可无 tangent，否则与 vertices 等长。
index/triangleMaterial 为 u32；binding = u32 bone + column-major geometryToBone float16，68 bytes。
joint 指向该 mesh 的 binding palette，不直接指向 skeleton bone 数组；binding 再映射 bone。

每块≤64MiB，容器≤256MiB/64 blocks；vertices≤200万、indices≤600万、keys≤200万、
bones/tracks≤1024、材质槽≤4096。索引、三角数/槽、有限数值、权重归一化、affine binding、
parent 先于 child、正 TRS、唯一 track bone、时间严格递增和0/duration终点都必须校验。
静态 mesh 的 boneCount/bindings/joints/weights 为0；静态节点与几何变换已烘焙，不再二次应用。

## 身份和版本发布

sourceKey = model1:type:SHA256(name + LF + scope + LF + SHA256(typed payload))。
scope 是限定 rig/mesh 数值证据。键不是 FBX 数组索引或单独名字；重排可保留身份，
改名/拓扑/骨架/采样证据变化保守申请新 UUID，保留旧 tombstone，禁止自动重定向。

generation 文件为 `out/assets/<project N>/<root N>/<positive number>-<uppercase SHA256>.nca`。
后台验证并发布不可变文件，metadata journal 提交 descriptor 的成功版本引用。
生成文件先发布但 descriptor 未提交时仅为 ignored 可重建 orphan；不能据此认为资产已提交。
history/session 和独立 Play 租约防止 GC；精确范围 GC 不自动删除未知/不匹配内容。

MAT1/切线 triangle-UV v1 不代表 PBR 或 MikkTSpace。后续新增材质/纹理格式必须另有版本，
不得无版本地重新解释这些已持久化内容。NIM1 仍只是工具内部候选，不作为 Player 资产格式。
