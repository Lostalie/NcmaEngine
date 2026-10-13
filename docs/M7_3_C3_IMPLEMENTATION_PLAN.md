# M7.3-C3 默认环境、配置面板与联合验收

2026-10-14。C2-C 已完成完整 Debug/Release、checked deployment 和提交推送，开工时local/remote main `9ac83b213f0cad8131d3907925ece85ee0faad09` 已核对。C3-A的83项及完整顺序Debug/Release/checked部署已通过，见[交付记录](M7_3_C3A_DELIVERY_REPORT.md)。C3-B1现有绑定属性/状态8组与正式宿主51项、完整双配置通过，见[C3-B1交付](M7_3_C3B1_DELIVERY_REPORT.md)。下一片C3-B2。C3 按 A → B → C 推进；每片测试失败先修复，最终双配置通过后提交推送，再进入下一片。

## C3-A：构建期默认环境与产品部署闭包（自动候选完成）

扩展现有 Ncma.Shader.Cook 为可信构建步骤：Editor 输出 Flat2D、Scene3D、SceneEnvironment，共9包；DX11 Player 输出两个3D profile，共8包；二者使用显式 v2 selection 和一致 shared skin。UI 保留 v1 单 Flat2D，Null 无 shader/环境。旧5个 shader 包字节必须保持；defaults.json 的 v2 更改显式记录，不伪称旧 index 不变。

新增可复现、具名、明确标识 synthetic 的 neutral 环境预设：固定源 UUID、环境 UUID、代次、线性常量辐照度和 Cook 参数；只在构建期生成 NCE。部署索引必须含独立 SHA、尺寸、预设身份与参数，受 manifest 约束。运行时通过严格有界只读 pin 加载；实际 GPU 准入仍由既有服务执行，不信任自声明凭据。环境默认库是可选产品资源，不给已有空/2D场景自动挂组件，不修改用户文档。项目采用预设时进入项目自身 UUID/资产闭包，不能序列化 native handle 或机器绝对路径。

修改 Package-M2_7 的确切文件白名单；checked Editor/DX11 加入环境闭包，UI/Null 文件计数/依赖保持轻量。必须验证 relocated product、缺文件/大小/hash/index损坏、重复项、未知版本、reparse/hardlink、旧 profile 一致性。默认包可真实进入 C2-C 宿主；没有运行时 source Cook 回退。沿用备份/journal/恢复。

## C3-B：人类与 AI 共用的配置业务

执行拆分（2026-10-14）：核对发现运行时环境 read pin 禁止文件写入/rename/delete，已有磁盘 participant 仅处理作者文件且不同时改变场景。为验证这一协调边界，按 B1 → B2 推进，各片完成后独立完整 Debug/Release、提交推送；没有减少原 C3-B 验收要求。

- B1：现有绑定属性 Inspector、显式添加 Off、强度/角度/关闭、World/revision/Play 绑定准备状态。定向8组与正式宿主51项、完整顺序无Skip Debug/Release/checked部署已通过；合同见 [C3-B1](M7_3_C3B1_RUNTIME_CONTRACT.md)。不增磁盘/Agent权限。
- B2：预设选择、项目资产可逆发布、read pin/Undo/失败恢复协调，以及独立有界环境目录与精确授权；尚未实现。不得通过宽化公共磁盘路径白名单、直接复制或释放活跃 Play 租约绕过问题。

在现有 Inspector 中新增环境启用、预设选择、强度、Y旋转和当前资源代次/准备错误显示。组件存在性、单配置、有限数值和精确 UUID/generation/hash 校验沿用 EnvironmentLightingData。创建对象/挂组件/设置参数都使用现有可撤销事务与版本检查；选择预设涉及文件发布时使用已有可逆磁盘事务，不以直接 File.Copy 绕过 Undo。不自动修改用户旧场景。

环境目录使用独立、分页、有上限的只读检查合同；旧 ncmeta 与已冻结 MCP schema 不扩容。只返回批准 UUID 的有界元数据，不返回源文本、路径、GPU句柄或可执行内容。UI 和 Agent 构造相同原命令；Agent 使用既有精确授权/会话/版本/到期/撤销，不新增 GPU 或编译权限。未接入推理服务保持真实显示。

验证命令/拒绝/过期/取消/Undo/Redo/重启保存/Play冻结，以及批准后的真实视口变化；错误准备保留最后有效资源并显示失败，不呈现过期画面。人工输入/DPI/真实第三方MCP继续单独登记。

## C3-C：正式产品联合验收

在已受检可搬移的 Editor/DX11 Player 中运行源目录不可用的 NCP 与预设 shader/NCE，核对启动先验证后 gameplay 的顺序；坏闭包必须在初始化前拒绝。纯 UI 与 Headless 不加载环境 GPU 或部署3D文件。静态与真实 ASCII/Binary FBX → NCA 动画、阴影、PBR材质、环境旋转/强度/Off、动态资源代次、resize、Play/Reload/关闭运行在正式共享服务。

继承 B3 独立软件/积分 oracle，明确常量/方向/BRDF误差范围；不能用同一 shader 生成的图冒充独立 oracle。记录 CPU/GPU/托管分配/上传/资源计数和关闭循环；有界测量不等于全场景性能保证。通过完整顺序无Skip Debug/Release、所有旧M6/M7/MCP/格式/smokes/inspect、三轮profile、部署恢复后写最终合同和交付。

C3全部自动门禁通过才将M7.3自动链标记完成并进入M7.4。HDR/EXR解码、真实用户HDR/FBX/材质、人工UI/DPI/MCP、目标机/自包含、完整性能和1h稳定性不得由合成预设自动关闭。DX11only；Vulkan/OpenGL下一版本；M7 → M8 → M9。
