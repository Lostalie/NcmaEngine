# M7.4-C3 后处理面板与 AI 编辑合同

2026-10-14。扩展 C2 高层配置/正式共享执行，不改 native ABI、NCS1/NPH1、Shader 或默认 Off。见[实施方案](M7_4_C3_IMPLEMENTATION_PLAN.md)。C3 自动证据不替代 D 搬移产品/完整替换或人工可见 UI/MCP 验收。

## 注册语义与唯一事务

PostProcessModules.Describe 返回最多64个owned描述，含UUID/版本、Tone/Feature、名称、合法顺序、参数范围/整数/默认值和先决模块。参数/依赖数组为副本，不能改注册表；不暴露 Build 委托、包路径或 GPU 对象。名称最多64字符且无控制符，默认值必须有限、在范围内且符合整数要求。官方/用户统一使用注册元数据，不以官方UUID绕过参数/默认值逻辑。

支持后处理组件的 EditorSessionOwner 使用同一个可信模块注册表验证 scene 候选，覆盖原事务、JSON编辑、交互草稿、恢复/打开、Undo/Redo与Play副本。拒绝未知模块/版本、超界或不精确参数、非法依赖/顺序、多个配置；启用后处理必须有3D几何。EV与原RenderConfiguration.Exposure合成后仍需 [.01,16]，不以浮点转换隐式钳制非法输入。没有新增scene存储、history或AI专用修改通道。

所有改变仍是原 `ncma.scene.transaction` 的精确对象/`ncma.render.post_process`组件操作。RenderingEditorAdapter只构造输入，不授予权限。注册元数据可用于提案，实际接受仍由C#原会话/预期revision/权限/候选验证/安全边界控制。模块委托不在目录读取或编辑语义检查中执行；构图及GPU准备仍由正式宿主离帧执行。参数合法不意味着任意视口尺寸、程序库、GPU资源预算都必定成功。

## 面板与真实状态

仅在显式注册组件的编辑会话显示后处理目录。可添加一个Off配置；Tone选择、EV、Bloom与用户Feature参数、合法添加/删除/上下移动均进入原事务。浮点字段逐事件校验，整数不先四舍五入后接受；字段激活/修改只是草稿，提交只产生一次历史。取消、过期/伪造事件不改变场景。模块按钮保留对象选择，避免提交后丢失Inspector对象。

对照是明确的启用/关闭事务，Off保留高层参数但C2释放post GPU资源，Undo恢复。不存在绕过World的隐藏GPU开关；草稿标识为未提交，不冒充实时渲染。目录最多每页8模块，Feature最多8个/每个16参数，禁用不合法的依赖与移动。Play冻结全部修改。

复制准备状态绑定World UUID、World revision、Play session和最近提交frame。尺寸/准备失败沿C2清屏/恢复；未准备或裁剪视图不能报就绪。IsPostProcessReady只有当前revision和已安装配置/环境一致时为真；未改变资源的离帧准备仍更新观察戳而不重建。状态不是GPU句柄或Agent执行凭证。

## MCP 独立目录授权

新增唯一只读能力 `ncma.postprocess.modules.list`。输入严格 `{offset?, limit?}`，offset 0–64、limit 1–8；输出为闭合有界schema的模块描述和分页标记，不读取磁盘、不解析Shader、不编译、不建图。默认返回零可见项，不因配对自动授权。总可信inspection容量从17精确增至18，新增占一个槽，原注册上限/原子注册检查保留。

本地UI先选择具体模块UUID，展示全部当前已配对客户端并要求明确勾选审阅；核对fingerprint后批准60秒。授权绑定EditSession/generation/revision、Endpoint实例和AudienceRevision；过期、撤销、重配对、连接变化、替换endpoint、编辑版本变化或Play冻结使之失效。审批记录复制拥有，不相信客户端传入的权限字符串。目录范围不含对象/组件写权；AI提交原事务仍默认拒绝，须通过原人类可见、精确请求/对象/组件审批，Undo/Redo也复查授权。

EditorAuthorizationController配对/撤销及Owner替换endpoint显式撤销目录授权，读取时仍独立复查epoch。只读授权不是执行授权，不引入任意项目代码加载、编译、GPU写入、推理服务或Python gameplay；未接入推理服务标识不变。可信C#启动委托不是代码沙箱。

## 证据与限制

新增作者侧测试覆盖真实GuiEvent、单次草稿历史、Off对照、Tone/EV/Bloom及用户参数、依赖与合法排序、持久化、Play、原始JSON事务拒绝、owner/nonreentry、精确scope、真实MCP stdio读/改/撤销和schema。CPU-only使用全新子进程检查Renderer/Platform/Gui/Compiler均未加载，不拿已跑过图形测试的进程残留作证据。

新增独立有界 PostAuthorHosts CTest，真实正式Editor面板→同一事务→DX11完整视口像素变化/Undo/Redo/用户模块/失败清屏及状态。沿用C2的Player/Headless/NCP/共享资源/Play/Reload测试。原渲染240秒与程序库120秒不变。

自动GUI事件不是人工DPI/输入法验收；stdio测试不是用户第三方可见客户端验收。C3没有新增完整GPU性能结论或每帧零分配承诺；C2纯Submit暖64零分配与宿主离帧准备成本分开。D的真实FBX/IBL/shadow/Bloom/UI搬移联合、full pipeline replacement、用户素材/HDR解码、目标机/自包含、完整性能/1h和既有source-recovery分配调查继续开放。只实现DX11，Vulkan/OpenGL下一版本。
