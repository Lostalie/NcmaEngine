# M7.1-A：纯托管 Shader 描述与绑定契约

入口：已核对并推送的 M7 方案 `09e0608`，其 M6.10 基线 `74be5b4` 不变。
本切片只实现 C# 数据契约，真实编译/反射属于 B，默认及用户 GPU 管线接入属于 C。

## 数据与身份

`Ncma.Rendering/ShaderContracts.cs` 提供 `ShaderDefinition` v1、严格 UTF-8 JSON codec、
`ShaderDescriptor` 不可变准备结果、`ShaderCatalog` 不可变目录及声明级 `ShaderBindingValidation`。
没有文件读写、编译调用、GPU handle、World、Python 或 MCP 工具注册。

持久 UUID 不为空；内容 hash 是规范完整描述的 SHA256；source hash 单独绑定规范 LF 源。
规范顺序为 vertex semantic/index、constant slot/member offset、资源命名空间/slot、依赖 UUID。
源码行尾规范化不改写作者文件；其他源内容不裁剪。枚举精确大小写字符串，不接受整数、数字字符串、
缺失、重复或未知字段；所有嵌套对象同样闭合。错误只有固定 code/field path，不回显源码或输入文本。

源最多 256KiB UTF-8；单描述最多 1MiB JSON；64 个 constant/resource 声明，
256 总行（input + buffer + member + resource），16 依赖；128 shaders/目录、源合计 8MiB。
这些是受控语义预算，不是 DX11 的实际寄存器能力；B 必须继续按设备/编译器能力拒绝超限。
现有 Scene 400-byte（4矩阵+9向量）、7 texture/2 sampler、Skin 80/128-byte structured buffers
和16-byte settings 的声明形状适配，未改变预算规避故障。

## 布局与资源

Vertex 输入支持 Float32/Int32/UInt32、1–4分量、独立语义/index，4-byte 对齐有界 stream offset。
SV_VERTEXID/SV_INSTANCEID 仅 UInt32 scalar/index0/offset-1；没有 stream 分配。
Pixel/Compute 不接受 vertex 输入声明。本版没有 geometry/tessellation stage 或静默替代。

常量支持32-bit scalar/vector、显式浮点矩阵 row/column-major、数组 count/stride，
16-byte buffer/矩阵/数组对齐，scalar/vector 不跨16-byte行；范围与重叠完整验证。
成员声明不自动生成 HLSL layout，也不支持嵌套 struct/任意类型。类型大小按声明计算，B 需与实际反射核对。
Texture2D/Cube、Structured/ByteAddress、Sampler、RWStructured/RWByteAddress 有闭合用途与槽范围。
只读 SRV、UAV、sampler 使用不同中立命名空间；范围内槽不能重复。
UAV 仅 Compute/ReadWrite；structured stride 4–2048、4-byte 对齐；非 structured stride=0。

## 不可变目录与失败行为

prepare 先完整验证和规范编码，再私有保存字节；trusted `CopyDefinition` 重新复制所有嵌套数据。
目录仅在可信 off-frame 创建时接受显式描述，不预注册不存在的 GPU 实现。
Flat2D/Scene3D/Skinning profile 分离，空 Flat2D 目录保持空，无3D/IBL/动画初始化或部署改变。
验证唯一 UUID/name-stage、同 profile、依赖完整闭包和精确 hash、循环保护。
整个候选通过才返回；末行错误不会修改原目录。没有 live installation、后台编译或自动 reload。
`Require` 同时检查 UUID/hash；只读页每页8行，返回复制的元数据且 `Compiled=false`。
目录不可变，可供托管线程读取，不持有 native lease，无 plugin unload 生命周期扩展。

绑定验证核对完整 stage/hash/input/constants/resources 的规范声明，包括偏移、矩阵序、种类与槽。
这是 caller 对 immutable 声明的匹配检查，**不是 bytecode reflection、实际 GPU 绑定或画质证据**。
它在准备阶段编码/分配，不可当作 per-frame 零分配绑定器。

## AI/编辑器同期边界

同源有界 metadata page 和固定结构化诊断是以后 UI/Agent inspection 的数据源。
`CopyDefinition` 含源码，只属于可信宿主 API，不得直接接入 Agent；没有 source 自动外发。
此切片不新增 MCP 能力或审批：Agent 仍 unsupported，不能凭 registry/workflow ticket 编译、
修改源码/发布文件、装载 DLL、安装 GPU pipeline、自行审批或配置推理。
B/C 接入只读能力必须复用 exact session/revision/resource hash/endpoint/audience/TTL 宿主审批；
数据编辑复用 Editor.Core 单一 undo history；代码修改/编译/发布须另外明确授权。
声明编译缓存、runtime shader 包、编辑 UI 及 registered GPU stage 仍未实现。

## 测试边界

`Ncma.Rendering.Tests --shader-contract-tests` 不创建 native module/device，明确输出 compiled=false。
全套渲染测试亦无条件先运行同一契约测试，再保留原参考图/API/动画/Player/MCP 测试。
当前70项覆盖严格格式/Unicode/hash/未知枚举、布局/槽/完整末行拒绝、预算临界、
深复制、依赖闭包、128目录/8行分页、2D空目录，以及1024次不可变 lookup 的0托管分配。
该 lookup 成本不等于 Shader 编译、整个帧或 GPU 性能验收；声明形状 fixture 也不是实际反射。
最终双配置与部署记录见 [交付记录](M7_1_A_DELIVERY_REPORT.md)。
