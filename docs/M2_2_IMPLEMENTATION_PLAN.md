# M2.2：版本化原生插件 ABI 与加载器

更新日期：2026-10-04。状态：公共 ABI、显式加载器和自动 H2 测试已实施通过。依赖 H1；见 [M2 总览](M2_IMPLEMENTATION_PLAN.md)。

实交付：模块 ABI 1.0、Ncma.Interop、6 类假 DLL、原生 C/ABI 测试及 Interop.Tests 11 项；真实 Platform/Gui/Renderer 接入见 [联合报告](M2_3_4_TEST_REPORT.md)。

## 1. 目标与范围

建立能独立验证 Platform/GUI/Renderer/Physics 的公共模块边界，先用假 DLL 验证生命周期，再迁移真实内核。
不把当前 NcmaNative aggregate ABI 2 改名就当插件化完成。
此阶段不改渲染算法、物理行为、场景格式、MCP Schema 或游戏脚本语义。

建议新增 managed/Ncma.Interop、Ncma.Interop.Tests，以及 engine/source/plugins/contracts/ 的纯 C 头。
公共头应由 C 编译器可包含；内部 C++ 接口、Eigen、Windows、GLFW、D3D/Vulkan、Jolt/Box2D 头不进入 contracts。

## 2. 分步实施

### A. 冻结公共形状

每类模块只有一个稳定 get_api 导出，用 major/minor/structSize 协商函数表。
新模块各独立 v1；major 不兼容拒绝，minor 只允许声明过的追加字段，不能盲目接受未知布局。
函数表包含版本/能力、Create、GetStatus、Close/Destroy、诊断读取；模块自己的函数另按版本扩展。
字段固定 uint8/uint32/uint64/float/double，bool 用明确整数；UTF-8 指针+长度，禁止跨界 C++ long/STL/异常。
UUID 如需跨 ABI 则固定两个 uint64 的高/低位及字节序，复用已测身份转换；禁止假设 Guid 内存字节与标准 UUID 文本顺序一致。运行时资源 handle 不冒充 UUID。
给每个结构写 sizeof/alignof/offsetof 与 C# 对应测试，不在头未冻结前宣称某结构已是固定若干字节。

### B. 资源、批次和错误

opaque uint64 句柄有所属模块实例及 generation 校验；跨模块/已释放/重用旧句柄明确失败。
“Destroy”重复调用的结果明确，托管 Dispose 对用户幂等；不能让错误句柄释放另一个资源。
调用默认同步：输入指针只在调用期间有效；native 需要后续使用就复制到自己管理的资源。
返回复制数据到 caller-owned buffers，capacity/requiredBytes 明确，不返回借用 std::string。
两次查询长度/内容之间用 version/sequence 保证一致；不能重执行提交型操作来扩容输出。
修改前预检错误缓冲/输出容量；后台 job 接收复制输入并通过 ticket/poll 取结果，不缓存短期固定地址。

稳定错误建议：abi_mismatch、missing_export、invalid_argument、wrong_thread、invalid_handle、
buffer_too_small、unsupported_feature、busy、device_lost、shutdown_timeout、internal_error。
C++ catch 在导出边界转换；访问冲突/任意 OOM/进程崩溃不是可恢复的业务异常。

### C. 加载范围与依赖

PluginLoader 只读取已确认项目/发布清单：稳定 ID、模块 kind、允许文件名、相对路径、major/minor、可选标记、依赖 ID。
规范化并检查路径边界/reparse，禁止 cwd/PATH 优先找库；错误位数/依赖缺失有可执行诊断。
文件 hash 用于部署完整性，非信任来源证明；不提供“签名了所以可任意执行”的沙箱承诺。
原生 DLL 和受托管插件都是可信可执行代码，加载不是普通数据编辑，MCP 不新增 load_plugin/eval。
依赖图拒绝循环、重复 ID/冲突版本；启动拓扑排序、关闭逆序。
Renderer/Gui 的 native-private 后端服务需要自己的版本化接口，不能由 C# 偷拿 ID3D11Device。

### D. 租约、线程与卸载

PluginLease 绑定函数表与装载句柄；资源/进行中调用持有 lease，不能释放 DLL 后保留函数指针。
初期不支持热卸载/运行中替换插件；只有整体应用关闭且资源/job/回调全清空才可正常卸载。
窗口/GUI/Renderer 显式 owner-thread Dispose；finalizer 不能直接操作 GLFW/D3D。
泄漏兜底只入释放队列并持有模块租约；若 owner 已结束则记录并让进程退出回收，不能在卸载后跨界调用。
Physics 是否允许 off-thread job 由自己的契约明确；不能把所有插件当作可任意线程使用。

官方互操作建议用于固定布局和资源包装，具体租约/线程队列是引擎自己的设计：
[.NET native interop best practices](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices)。

### E. 假模块故障矩阵

准备受控假 DLL：正确版、major 错误、短函数表、缺必需导出、初始化失败、故障 job、资源未清空。
测试只加载 out/verification/m2/fixtures/ 精确文件，不能去扫描用户下载目录。
模块创建失败逆序清理已经创建的依赖；不可对尚未初始化对象调用 shutdown。
诊断记录模块 ID、phase、code、版本、耗时，不记录用户审批凭证或任意完整内存。

### F. 构建部署接入

CMake 按 native 模块独立 SHARED targets；共享源码不意味着所有模块共享 process-global solver/GLFW 初始化。
SDK 静态库依赖私有链接；避免 NcmaCore PUBLIC 依赖让每个 DLL 都携带全部 renderer/hostfxr/physics。
继续先构建 Core/Native/Architecture，对现有内核做回归；新 targets 加在优先构建后。
脚本按配置隔离 native 产物；候选部署包含精确插件清单和依赖，不覆盖正在运行的 DLL。
新项目进入 solution/Build/CTest，插件缺失测试不能依赖操作者手动删 DLL。

## 3. H2 验收

- 纯 C ABI 编译；C/C# 固定布局一致；x64 调用约定、UTF-8、容量、NaN/计数/溢出检查。
- 缺失/错误 DLL、错误 major/表长度、越界路径、依赖循环、重复/跨实例句柄拒绝。
- 输入生命周期与输出两次调用不重复修改；输出容量失败保持修改前状态。
- 初始化失败/重复 Dispose/关闭 job 超时无 use-after-free，记录 lease 尚存原因。
- 没有 STL/图形/物理 SDK 类型出现在公共头；Runtime/Scene/Gameplay 不依赖 Interop。
- Debug/Release 假模块负例与完整 Debug 基线通过，零新警告。

## 4. 完成与非目标

H2 只说明装载和边界可用；没有真实 Renderer/Physics 时不置 manifest 功能为 true。
不添加原生 World 导出，不做 Windows 以外平台的已支持宣称，不引入热插件或通用动态引擎扩展市场。
