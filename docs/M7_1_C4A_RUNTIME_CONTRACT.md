# M7.1-C4-A NCS1 运行包结构契约

这是无源码CPU包及结构预检，不是GPU admission、正式Editor/Player切换、发行文件服务或签名机制。下一步和总范围见[C4方案](M7_1_C4_IMPLEMENTATION_PLAN.md)。

## 布局与约束

所有整数little-endian。60字节头：magic NCS1、version1、backend1(DX11)、profile(Flat2D=0/Scene3D=1)、features(bit0 shadow/bit1 skin)、entryCount、totalBytes，随后32字节payload SHA256。整个包另用独立可信选择提供的SHA256核对；不能把从不可信包自己计算出的hash视为授权或真实性证明。

每条128字节头：role uint32、16字节UUID（.NET Guid.ToByteArray字段顺序）、authorContentHash32、bindingContractHash32、compilerVersion47、compilerFlags0x48800、bytecodeBytes、bytecodeHash32，然后原始DXBC。包最大8MiB、程序最多9条预算、每份32字节至1MiB。当前实际闭包最多7条。

角色序号：0 UI VS、1 UI PS、2 Geometry VS、3 Geometry PS、4 Tone VS、5 Tone PS、6 Shadow VS、7 Shadow PS、8 Skin CS。按此顺序无重复、UUID非空且独立。Flat2D只含0/1且flags=0；Scene必须2–5，shadow时再6/7、skin时再8。不能带未引用程序或任意源依赖。Headless消费/项目profile选择尚未接入，不能由此推出Headless会加载shader。

BindingContractHash来自source-free同源固定布局表（version1/profile/stage/规范输入、常量成员、资源顺序的UTF8 System.Text.Json记录SHA256）。UI和Skin原有布局提取为共享属性，无布局/ABI/渲染语义变化。原author hash用于精确作者身份追踪，但运行包不含源，无法重新从源验证该hash；独立可信package选择负责完整性/批准。

## 两道门禁

Cook是可信内存API，显式输入B产生的不可变CompiledShader；核对profile/stage/完整声明及B实际反射、无外部依赖、编译器版本/flags，再封装字节。枚举最多读取10条即拒绝；不会展开无限输入。调用者负责off-frame调度和独立批准的文件事务，本片不写文件、不注册Agent工具。

Preflight是纯C#，先取有界自有副本，再检查外部hash、版本/flags/total、payload、闭包、UUID、layout/compiler和每条hash。DXBC检查header、封闭五chunk(RDEF/ISGN/OSGN/SHEX/STAT)、连续无间隙/重叠/重复/尾随、SM5实际stage和指令流声明长度；debug/private/source类型chunk拒绝。失败抛出小型结构化ShaderContractException，不输出源或任意输入值。

这里**不做完整RDEF/签名语义/指令安全验证，也不检查DXBC内部checksum**，因此始终GpuValidated=false，不产生安装凭据。测试刻意保留“SV_TARGET1结构可读但未获GPU准入”的负向边界证据。C4-B必须使用实际native完整闭合反射，在任何shader安装前拒绝它。自报布局hash和容器hash都不是安全沙箱。

## 存储和生命周期

对象为无native资源的不可变CPU快照；CopyBytes/CopyBytecode/CopyPage均复制，metadata每页8条，无源码或句柄。不同线程可独立读取不可变对象；Cook输入CompiledShader也是已复制产物，不访问Renderer。纯预检无需PluginLoader、D3DCompiler或Editor；不能把未调用native误称已在目标GPU验证。

目前没有shader文件自动发现、项目字段、生产宿主消费、GPU程序反向重建或兼容格式；这些按C4-B/C推进。Vulkan/OpenGL依旧unsupported/下一版本，原人工/材料/目标/性能/长跑门禁不变。
