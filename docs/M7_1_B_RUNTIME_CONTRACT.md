# M7.1-B 真实 DX11 Shader 编译／反射契约

本片是可信、停用模拟期间的准备服务，不是新 GPU 管线、源文件发布或 Agent 编译能力。
C# 持有不可变描述、完整反射匹配和有界缓存；native 只执行 SM5 编译及数值布局查询。
默认／用户 Shader 接入同一 GPU 注册管线留给 M7.1-C。仅 DX11，没有 Vulkan/OpenGL 回退。

## ABI、线程和资源

Renderer 主 ABI 1.2／152字节及 query1–7 不变；新增 query8，独立 API1／32字节，caps=1。
两个操作 compile 和 validate_preparation 均要求准确 module/renderer、owner线程、健康且无 active frame。
请求56字节、宏128字节、反射行192字节、输出32字节，x64固定布局 POD；不传 STL/COM/异常。
输入源256KiB、宏16个，反射256行，绑定64项，bytecode1MiB，disassembly4MiB。
源和入口为显式长度，宏为固定 ASCII 字段；名称最多63字节（A数据可容纳64，不代表B支持）。
调用者持有输出内存。native 在私有候选中完成编译、全部反射、预算和容量检查，再一次复制输出；
失败只修改 NcmaError，输出／原 GPU 管线不变。COM编译、反射、反汇编 blob 在返回前释放；无 shader GPU handle。
CompiledShader 保存私有复制 bytecode 和不可变行；查询返回副本，反射分页最多8行。CPU副本不是可安装的GPU资源。
ShaderCompilerService 持有额外 plugin lease；缓存关闭后释放，不能越过 renderer销毁／owner／fail-stop／active-frame检查。
缓存命中同样执行 native preparation 边界。关闭服务可清理CPU缓存，不要求已故障renderer恢复健康。

## 编译及完整声明匹配

目标 vs_5_0/ps_5_0/cs_5_0，D3DCompiler47，strict + warnings-as-errors + O3，flags=0x48800。
固定 source label，无 include handler、文件扫描或网络访问；`#include` 必须失败。
这是 D3DCompile 的 [pInclude 官方规则](https://learn.microsoft.com/en-us/windows/win32/api/d3dcompiler/nf-d3dcompiler-d3dcompile)。
读取实际 shader stage、VS signature 类型／分量／语义索引、cbuffer slot/size、成员 type/offset/span/array stride/matrix major，
SRV/UAV/sampler类型、slot和连续数组范围。反射保留真实末成员跨度（例如float3x3为44字节，预留48）。
input stream offset 无法由 bytecode 得到，用 UINT_MAX；mesh偏移与跨stage／pass绑定检查留在C，不冒称已验证。
结构化资源 stride 从实际 D3DDisassemble SM5 declaration 严格读取，而非作者数据或 NumSamples；
[官方 BindDesc](https://learn.microsoft.com/en-us/windows/win32/api/d3d11shader/ns-d3d11shader-d3d11_shader_input_bind_desc) 的 NumSamples 是采样数。
无法唯一解析的 bytecode declaration 直接拒绝。纹理数组的实际 `[i]` 项只在类型／访问／stride相同且slot/index连续时合并。

当前闭合子集：float/int/uint标量、向量、矩阵及数组；float Texture2D/Cube、普通sampler、Structured/ByteAddress及CS RW版本。
嵌套cbuffer struct、bool/半精度、comparison sampler、整数纹理、append/counter资源、不支持的维度或PS UAV均拒绝，
不能仅凭枚举声称通用HLSL支持。结构化资源只核对实际stride，不声称验证其全部结构字段布局。
C#要求实际全部行与预期声明完整匹配；未知／缺失／末行错不缓存，不修改旧artifact。

## 权限、缓存和诊断

构造服务必须显式提供可信 host 的 preparationAllowed 回调，Prepare先检查owner、非重入、host许可，
再查renderer健康／idle。回调应由未来应用集成检查停用模拟边界；B没有接入World调度或公开MCP入口。
`true` 回调是可信测试／宿主约定，不是安全沙箱或防止任意可信代码滥用的保证。
缓存key包括完整规范descriptor hash（source/entry/stage/版本）、规范排序宏、query/API版本、compiler47和flags。
仅内存16项／8MiB，单项1MiB；无磁盘cache／foreign旧版本读取／自动淘汰。预算失败保留已有结果。
所有编译器原始诊断文本均脱敏丢弃，避免 `#error/#line` 泄漏源／项目路径；仅返回固定错误码和实际长度。
DiagnosticTruncated 表示原始诊断超过16KiB（reserved=1），不是返回了16KiB文本；实际短诊断和合成阈值策略分别测试。
源码复制／编译服务只供可信准备代码，不注册 Agent tool、不授予代码执行／DLL加载／推理／live World或GPU写权限。

## 验证边界

真实VS/PS/CS、实际80/128字节structured stride、矩阵／数组／纹理数组、warning/include/entry错误、
声明末行错、原子ABI容量／末成员拒绝、owner/idle/fail-stop、cache/复制/预算测试。
失败后现有参考管线仍可render并保持API0/0；这不是新CompiledShader已经执行GPU的证明。
完整自动Debug/Release及checked部署另见[交付记录](M7_1_B_DELIVERY_REPORT.md)。
人工UI/MCP、用户FBX/材质、目标环境、自包含、性能预算和1h门禁不由本片关闭。
