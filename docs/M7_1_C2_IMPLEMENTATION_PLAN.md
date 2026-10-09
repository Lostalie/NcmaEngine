# M7.1-C2 Geometry／Shadow／Skin 注册执行计划

前置C1 `4f853a1a9d218d43b21327a59a070b46d3e4b601` 已推送、远端main一致。仅DX11；C3/C4和既有人工/材料/目标/性能/1h门禁仍未实现或未通过。

## 执行边界

C#从同源复制的默认ShaderDefinition建立Scene3D（Geometry/可选Shadow/Tone）和独立Skinning目录，默认/用户走同一B编译、真实反射、完整声明验证服务。没有文件扫描、Agent编译权限、World或正式宿主默认切换。

新增独立Renderer query10/API1，query1–9和现有Renderer表保持不变。Scene三对有界bytecode整体创建/替换；无shadow分支必须没有shadow代码或资源。只在可信owner/off-simulation/off-render准备入口执行。实际native重新验证封闭同源资源/输入输出/全部常量成员，不能以用户人工反射代替真实bytecode；Skin还验证真实结构stride和64x1x1线程组。

Scene候选程序全建成功、诊断成功、有界GPU drain后，noexcept一次发布并释放旧组；失败不改旧组/key/counters，不重建mesh/纹理/HDR/depth/shadow。Skin注册创建共享数值kernel，已有kernel必须同一bytecode；显式替换保留三palette槽/mesh/output/时钟，失败释放候选并保留旧shader。最后一份Skin mesh释放后kernel与shader一起关闭。所有compute代码为可信扩展，不是安全沙箱，反射不证明任意HLSL内存写入安全。

## 验证门禁

真实默认和用户Geometry／Shadow像素，与既有独立PBR/遮挡/alpha/PCSS参考核对；用户Skin实际compute位置/normal/tangent/UV/sign与独立CPU数值比较。错stage、末cbuffer成员、vertex offset/签名、resource type/slot/stride、线程组、owner/lifecycle/active/revoke/nonreentry、末组错和诊断/退役故障均原子拒绝；纯2D不创建scene/skin。

保留原M6 NCA/动画/Jolt/skin-shadow/Editor0-1-8-32/Player/MCP所有断言。完成后冻结源码，顺序无Skip Build.bat Debug/Release、新/移除格式、Python inspect、profiles、audit、checked deployment/hash/journal；失败保留日志修复复测。仅通过后提交推送核对remote SHA，再进入C3。C2不含shader运行包、UI/MCP编译/推理权限或正式入口切换。
