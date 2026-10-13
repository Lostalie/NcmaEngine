# M7.4-C1 受检后处理程序库合同

2026-10-14。范围是可搬移CPU程序库、整组真实准入与产品构建闭包，见[C计划](M7_4_C_IMPLEMENTATION_PLAN.md)、[交付](M7_4_C1_DELIVERY_REPORT.md)。本片不自动启用Bloom/Tone，不新增未被消费的项目或scene字段，不宣称C正式宿主/编辑器/AI完成。原native query1–16/NCS1/NPH1及旧默认shader不改。

## 索引、文件和所有权

独立`PostProcessLibrarySelection`严格JSON v1：schemaVersion、programs、tones三个必填字段。每个program为ModuleId/Path/Sha256；每个tone另含Profile/Shadows/Skinning。拒绝未知/重复/缺失字段、空UUID、重复路径（不区分大小写碰撞）、同UUID混用数值Feature与Tone、未知profile/版本/超预算。Tone精确module/profile/skin必须具备两种shadow变体。不根据未知UUID猜测官方实现。

固定索引`assets/postprocess/defaults.json`；文件必须是assets下canonical相对`.nph`或`.ncshader`，不是任意绝对路径或可执行文本。程序UUID是注册模块标识，库的外部SHA精确标识整个选择；不是组件实例、运行时GPU句柄、权限或来源签名。官方UUID常量包含Bloom、ACES fitted、Reinhard、Linear Clamp；用户UUID使用相同文件与准入入口。

索引≤16KiB，最多8个NPH、32个Tone包，不能全空；每文件≤8MiB，成功库的总payload≤32MiB（不把这个数称为CLR进程内存上限）。NPH和NCS各自严格验证；NCS必须与指定profile/shadow/skin完全一致。全部skin字节在CPU加载时就要求一致。owned不可变包可独立复制，`GpuValidated=false`；公开selection数组返回副本。

`RuntimePostProcessLibrary.Load(root,externalIndexHash)`为显式可信源目录读取；`LoadDeployment(root)`额外pin manifest，逐个所选文件和索引核对精确路径/size/hash，不只检查索引。manifest大小≤4MiB、条目≤8192、重复key/大小写路径碰撞拒绝。共享RuntimeReadPin保持文件和父目录租约，禁止写/替换/rename，拒绝hardlink/reparse/junction。失败/取消按逆序释放已打开的全部pins；source/deployment都不自动回退另一份库。

所有库读取/准备/关闭为创建线程；Dispose在准入期间拒绝。CPU加载没有native/compiler/World/Editor调用。释放文件pin后已复制的包仍可读，但原库及基于该租约的准备结果不能再次授权安装。关闭已安装scene/native资源仍由其原owner负责，不将文件租约当GPU资源owner。

## 整库真实准入

`Prepare(renderer,sceneFiles,allowed)`要求精确renderer健康/离帧、已加载的原scene文件集及可信非simulation批准；这不是安全沙箱或Agent端点。先逐字节比对每个Tone包所有非Tone阶段与原scene文件集合的对应变体，禁止借Tone库悄悄替换geometry/shadow/shared skin。参数化用户Feature依旧走B的NPH/query16合同。

然后所有NPH通过`PreparedPostProcessPrograms`、Scene3D Tone通过`RuntimeShaderPreparation`、Environment Tone通过独立`EnvironmentShaderPreparation`的真实native完整反射。原生不新增特殊官方旁路。字节/偏移/资源/输出反射全部成功、最后再获批准，才返回一个完整不可变`PreparedPostProcessLibrary`；不会逐项发布默认选择或创建scene/GPU效果资源。

准备标志禁止同库重入/关闭；首次和最终批准使用Renderer操作保护，不能让批准回调关闭Renderer。每个内部native准入仍使用其原操作保护。每次缓存查询重新核对精确Renderer、owner/健康/离帧、原scene和post库租约以及批准；撤销/关闭/换renderer不复用旧权限。失败不是自动恢复调用者外部IO；本片只保证不发布部分库或GPU安装。

## 构建与产品

原`Ncma.Shader.Cook`在可信构建流程生成3种Tone×2scene profile×2shadow×2skin共24个NCS，1个Bloom NPH和1个索引，共26文件；官方预设完全使用A/B公开声明/编译/包合同。Editor与DX11 Player独立cook的26份字节应完全一致；原9/8个scene shader、默认选择索引及neutral NCE不变。通过加载生成库并真实整组准入后才宣布Cook成功，无scene/post/skin GPU资源。

Package-M2_7只将确切26文件加入Editor和DX11 Player受检manifest；Cook工具/源码不随产品分发。UI仅原Flat2D包+索引，独立apphost18文件不变；Null无后处理目录。C1发布的是将供正式宿主使用的库，不是正式宿主已经消费或默认切换的证据。C2负责项目选择、持久配置、就绪状态/原子刷新与Editor/Player真实共享消费；C3负责人类/AI统一可撤销事务。

## 验证与门禁

CPU-only独立子进程验证无Renderer/Platform/Gui/compiler/Editor；严格索引/manifest/hash/缺失末项/预算/profile/canonical大小写/硬链接/junction、所有pins及owner/取消/reopen；完整真实准入/撤销/首尾回调/错误非Tone程序及末项错误输出拒绝无半组资源；所有24变体+Bloom常量场和用户BGR的独立逐像素oracle/API验证。skin标志在库中真实准入，但本片图像没有宣称绘制真实FBX；Environment图像为IBL Off。

完整双配置/受检部署及旧M6/M7/MCP/格式测试保留。C2/C3、D产品联合、人工UI/DPI/可见MCP、用户HDR-FBX材质、HDR解码、目标/自包含/完整性能/1h未关闭。DX11only，Vulkan/OpenGL下一版；M7→M8→M9。
