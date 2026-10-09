# M7.1 Shader契约、真实反射和绑定验证

状态：A70/B41/C1 24/C2 40及各自最终完整门禁通过；C3独立Flat2D默认/用户VS/PS32项真实测试、native故障、16文件独立UI apphost及最终顺序无Skip Debug/Release/checked deployment通过。C4-A自动完成，C4-B/C待执行，整个C尚未完成。见[C1交付](M7_1_C1_DELIVERY_REPORT.md)、[C2交付](M7_1_C2_DELIVERY_REPORT.md)、[C3交付](M7_1_C3_DELIVERY_REPORT.md)。
C4最新状态：A无源码NCS1包/纯C#结构预检77项、六种实际编译产物/独立进程和最终完整Debug/Release通过；B正式GPU准入/宿主、C项目部署联合仍待执行。见[C4方案](M7_1_C4_IMPLEMENTATION_PLAN.md)、[A交付](M7_1_C4A_DELIVERY_REPORT.md)。整个C尚未完成。
见[B契约](M7_1_B_RUNTIME_CONTRACT.md)、[B交付](M7_1_B_DELIVERY_REPORT.md)。
见[A契约](M7_1_A_RUNTIME_CONTRACT.md)、[A交付](M7_1_A_DELIVERY_REPORT.md)。M6.10远端`74be5b4`核对后才建立方案，方案`09e0608`已推送。

## A：纯C#资产与受控语义（先执行）

建立版本化闭合Shader描述和注册目录：持久UUID、内容hash、stage（VS/PS/CS）、入口、预期vertex语义、
常量成员标量/向量/矩阵布局、资源语义及读写用途、依赖与数值预算。公开语义不暴露D3D指针/对象。
2D/3D/skin使用独立profile；仅有对应profile时注册，不为纯2D预加载3Dshader/IBL。
候选预算：源256KiB、诊断16KiB、反射256行、绑定64项；实现前按官方shader基线核对适配，
不以扩大预算规避故障。固定版本、重复字段/UUID/语义、未知字段/stage、溢出/错格式/不完整输入严格拒绝。
验证输出结构化、复制、有界，不输出可执行文本或任意项目文件内容。

最先只建立数据验证与诊断，不注册fake GPU支持。A提供纯数据描述/目录/声明匹配；可发布shader资产与完整真实反射仍未实现。
新作者格式不加旧类型alias或兼容converter；持久UUID与运行handle分离。

## B：薄native编译／反射（A验证后）

在renderer插件内部用现有DX11编译资源设施，实际读取VS/PS/CS bytecode反射，包括：
vertex inputs、cbuffer size/member offsets/types、SRV/UAV/sampler种类/范围及shader stage。
通过新版本查询/有界caller-owned POD返回；保留所有原query/table，不泄漏STL/异常/COM对象。
明确bytecode/资源的owner线程、分配/释放、关闭与plugin unload顺序。
源在可信off-frame入口提交，禁include逃逸/任意外部路径/默认文件网络访问；不允许tick内编译。
cache key包含规范source hash/入口/stage/宏/编译器与契约版本；错版本/过期缓存严格拒绝。
Shader警告视为错误，诊断截断明确报告；失败不替换active shader/pipeline。

测试必须是真实D3D编译/反射，不以A的人工metadata描述冒充硬件证据。
错误输入语义、常量偏移、纹理资源种类、stage或容量、缺失入口/编译警告，以及完整批次末行错均应原子拒绝。

## C：同一目录接入默认与用户管线（B验证后）

执行切片见[C详细计划](M7_1_C_IMPLEMENTATION_PLAN.md)：C1真实Tone，C2 Geometry/Shadow/Skin，C3纯2D，C4正式宿主/运行包与联合门禁。

把已有官方2D/3D/skin shader描述和编译结果纳入同一注册目录；用户trusted模块使用同样服务与validator。
C#依据反射检查mesh/material/pass输入、依赖与输出，普通用户选择preset/参数/Feature，不处理API寄存器。
规范内部binding映射可由扩展作者查看，仍不把图形API调用泄漏到World/Behaviour。
优先证明实际registered stage替换样例；无实现的stage不能只靠枚举成为“支持”。
必要的新Shader/绑定ABI单独列size/version/thread/batch/cleanup，重建全部消费者，再通过真实静态/动画/2D图像。
运行包是精确shader/bytecode/资源闭包，不加载Editor代码或native handle；源与bytecode发布界限单独定义。

## AI与编辑器边界（各切片同期，非新增执行授权）

同源复制catalog、语义诊断、受控参数/preset/proposal/diff和Editor.Core唯一history。
源代码修改／shader编译／文件发布不同于数据事务：需要明确、独立trusted授权，不默认经workflow ticket赋权。
只读检查须exact session/revision/resource hash/endpoint/audience/TTL审批；没有真实实现则unsupported。
Agent不能写live GPU／World、self-approve、加载任意DLL、开启推理或在tick等待外部服务。

## 验证与交付

A纯托管headless tests明确“未执行shader”；B真实native反射与C实际reference scene/API0/0分别记录。
新增shader图像独立oracle/事先定义容限，保留全部M6动画/skin-shadow/包拒绝/MCP及2D无3D资源测试。
每个可交付切片先定向，再顺序无Skip完整Debug/Release，核验部署/journal/hash、提交推送与远端后再下一片。
本文件不是M7.1实现或M7画质/目标性能/人工/1h验收证明。
