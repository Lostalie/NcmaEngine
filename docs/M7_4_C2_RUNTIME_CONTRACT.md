# M7.4-C2 正式配置与共享后处理合同

2026-10-14。C# 高层配置/模块组合/宿主接入；C1 程序库、B 数值图和薄 native 执行保持原合同。默认 Off。本片不实现 C3 专用 Inspector/AI 模块目录，也不关闭 D 产品联合与完整替换验收。

## 持久值与模块

`ncma.render.post_process` 是扁平场景中最多一个的值组件，包括 Off；不建立对象树。外层 schema v1 闭合，包含 Version、Enabled、精确 ToneModule UUID/ToneVersion、ExposureEv 和 StackJson。World 只存不可变标量/字符串；结构化 DTO 经 EncodeStack/DecodeStack 复制并规范化，不把可变数组或 JsonDocument 放入 World。StackJson 最多 8192 UTF-8 字节、8 个唯一 Feature、每个 16 个唯一 ASCII 参数名；严格必填/未知字段/重复字段/有限数值检查。参数名最多 48 字符，数值绝对值不超过 65504；参数按名称排序，Feature 顺序保留。旧场景无需该组件，缺省 Off；不增加旧格式兼容。

PostProcessModules 由可信 C# 启动代码注册，最多 32 Tone/32 Feature，拥有注册数组副本。注册包含精确 UUID/版本、参数区间/整数要求、合法顺序和先决模块；拒绝未知、重复、倒置依赖和环。运行配置必须精确匹配参数集合、版本、依赖和顺序。官方三种 Tone 与 Bloom 走相同注册入口；用户模块也返回公开 PostProcessPlan。委托不能从 JSON、Agent 文本或远程源码加载，这不是代码沙箱。

Bloom 提供 levels 1–6、threshold 0–16、softKnee/intensity/scatter 各自原范围；EV 为 -6 到 +4。EV 在 Tone 前乘原 RenderConfiguration.Exposure，最终必须满足既有 [.01,16] 合同，超出拒绝而非静默钳制。Tone 仍在 Feature 链末尾，之后才是显示域 UI。Off 不调用 Feature 构建器，不创建后处理 RT；启用但空 Feature 链仍可选择 Tone/EV。

## 程序库与组合

项目可显式使用 `postProcessLibrary: {version: 1, indexHash: "64位SHA256"}`，相对索引固定 `assets/postprocess/defaults.json`。显式选择从项目根加载，缺失/错误不回退；缺省使用受检部署 manifest。C1 的文件/父目录只读 pin、canonical/reparse/hardlink 检查、外部 hash 和整组准入保留。注册模块和值验证不等于 GPU 凭证。

PostProcessComposition 合并已拥有的无源码 NPH1 程序。相同数值程序 UUID 只有元数据及 VS/PS 字节完全一致时才去重；否则拒绝。最多 8 个程序包/8 个唯一程序/8MiB；串接图把后一 Feature 的 SceneHdr 映射为前一输出。仍使用原公开图编译器的 32 target/pass、128MiB 预算和依赖验证，不改 NPH1/NCS1/native ABI 或 Shader 算法。合并顺序确定，GpuValidated 仍为 false。

PostProcessRuntime 借用 renderer、scene 文件和程序库租约，宿主先关闭 scene，再关 runtime/files，最后关 renderer。构造时完整 query12/15/16 真实准入，非 Tone stage 与原 scene 字节一致、所有共享 skin 字节一致。最多缓存 4 个不可变组合；缓存命中仍检查 owner/健康/离帧/租约/审批。回调受非重入保护；不能关闭本服务/renderer，也不能写受保护 World。委托仅在可信离帧边界运行，非模拟调度、无 Python/IPC/inference 等待。

## 共享执行与故障

SceneRenderSession 使用原 PrepareEnvironmentView 离帧入口一并准备环境与后处理。准备精确 World 身份/提交边界下的配置、视口尺寸、shadow/skin、环境 UUID/代次/hash 和曝光；完整 scene+environment+post 候选创建成功才交换。Submit 只消费就绪资源；配置变化、环境变化、尺寸或输入标量变化却未准备时拒绝，不在 Submit 编译/读文件。空/裁剪视图不能把旧 GPU 绑定标成新配置，几何重新出现必须准备。

失败保留上一安装状态供安全释放或恢复，但不当成新配置展示。Editor 清除过期视口并诊断，可明确恢复后重绘；Player 准备失败先清屏，再由 runner 报故障和关闭。清屏也失败则保留两项异常，不转成成功。关闭 runtime 后不能新准备；已复制 GPU 数据的寿命遵循原 scene 所有权，不声称撤销文件租约能销毁现存 GPU 资源。

配置修改当前使用原精确对象/组件授权的可撤销 scene transaction，默认拒绝，Undo/Redo 恢复全快照并重解 UUID。注册模块语义检查在正式宿主/共享准备执行；专用命令/Inspector 的提案前检查与模块目录属于 C3，不能把现有通用组件写入误称为 C3 完成。游戏运行中禁止附加该组件（runtimeAttachable=false）；不扩张 Agent、网络或 Python 的活 World 写权限。

## 正式宿主与边界

Editor 在加载 gameplay 前、创建 Play 前预检；Player 在 gameplay 加载前验证注册值、显式/部署文件闭包及完整真实准入，再以 16×16 绑定检查程序与数值图。真实视口仍须检查实际尺寸/资源预算，16×16 不是任意分辨率的承诺。Edit/Play/Reload 使用同一共享服务、独立 World；NCP 场景资产与项目程序库的身份/租约不混用。

Headless 验证注册值；有显式库选择时还检查 CPU 文件闭包，无图形插件或 GPU 资源。Headless 未显式选库时不要求部署默认图形库，不宣称实际 GPU 验证。启用后处理的纯 UI/无 3D 场景被拒绝；UI/Null 产品不部署后处理文件。未使用后处理的旧正式路径保持原行为。

新增独立有界 CTest PostModules/PostScene/PostHosts，原 Rendering 240 秒和 PostLibrary 120 秒保留。暖 64 次纯 Submit 零分配不等于宿主全帧零分配：配置读取/离帧准备仍有分配，历史 source-recovery 高分配调查和完整 CPU/GPU 性能门禁继续开放。真实 FBX/IBL/shadow/Bloom/UI 搬移产品联合、完整管线替换属于 D；人工/DPI/MCP/用户素材/HDR 解码/目标机/自包含/1h 均未关闭。
