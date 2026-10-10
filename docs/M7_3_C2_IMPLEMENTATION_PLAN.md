# M7.3-C2 正式 IBL 接入详细分片

基线：C1 `2efb4db76432bbe0b4aaf52df00da6c49e5e3423`，2026-10-10 开工前核对 local/remote main 一致。目标仍是正式 Editor/Player 共用 IBL，不以独立测试管线替代。C2 按相互依赖的合同、共享执行、宿主验收三片实施；每片完整自动回归和提交推送后再进入下一片。

当前C2-A/B自动候选完成：C2-A71项、C2-B共享场景执行87项及各自最终顺序无Skip Debug/Release/checked deployment通过，见[C2-A交付](M7_3_C2A_DELIVERY_REPORT.md)、[C2-B合同](M7_3_C2B_RUNTIME_CONTRACT.md)、[C2-B交付](M7_3_C2B_DELIVERY_REPORT.md)。下一片C2-C正式宿主；pending guards保持，整个C2未完成。

## C2-A：显式 Shader 文件选择与完整准入（自动候选完成）

- 旧 ShaderPackageSelection v1 的 1–5 包/Flat2D/Scene3D 闭包冻结。新显式 v2 支持最多9包，追加独立 SceneEnvironment 的静态/skin × 两 shadow variants；每个 profile 各自完整配对，Renderer内所有profile的共享 skin 字节也必须一致（原生仅有一个共享计算核）。不把未知 profile 当 Scene3D，不把缺失环境变体回退到旧 shader。
- RuntimeShaderFileSet 继续启动时精确 path/SHA/profile/flags/read pins/manifest 校验；整组选项全部实际 query12/query15 准入成功后才发布到默认服务，任何最后一组失败不能留下半组选项。
- 默认服务独立 PrepareEnvironment 返回 query15 已准入的 EnvironmentShaderPreparation；源布局工具仅显式帧外 Cook，正式 file selection 缺失必须拒绝。缓存最多9条，实际反射、审批/owner/healthy/off-frame/非重入仍检查，CPU package.GpuValidated=false。
- 独立 SceneEnvironmentRuntimeShaders 持有确切双 shadow variants/shared skin 和同一 renderer 的已准入 preparations。它不创建环境 GPU/scene，也不删除 C1 正式宿主 guards。
- 实际 source-free/default/user/旧v1拒绝新profile/v2闭包/错hash/profile/skin配对/最后环境组反射失败/原子重试、审批撤销/owner/帧内拒绝/关闭 pin 与纯2D无环境资源测试；保留原格式与所有原断言。

## C2-B：共享 SceneRenderSession 环境准备和执行（自动候选完成）

构造/显式刷新接收确切 PreparedSceneAssetLease 与值配置；启用时使用 profile3 双变体/shared skin，不允许 profile1 冒充。环境 GPU 创建和配置更换在可信帧外/非 simulation 边界进行；提交只消费已准备数据，不读文件/Cook/IPC/推理。候选 shader、环境及 scene binding 完整准备后安装，失败保留旧有效组；旧 pipeline 先释放 scene pins，再释放环境 GPU 和资产租约。Off 不创建环境资源，纯2D/空场景不初始化3D。resize/shadow variant 切换沿用正确绑定，不重复 Cook/上传；同代 strength/rotation 刷新不重建环境。新增配置变动、错误代次、过期 world、失败保留、实际图像、动画共享 skin-shadow、租约/关闭与暖态成本测试。

## C2-C：正式宿主前置检查与联合验收

正式 Player 在 gameplay 初始化前核对源无关 shader/环境资产完整闭包并完成实际准入；初始化或运行中改变配置不能绕过，动态资源未准备必须明确失败而非静默忽略。Editor 启动/批准命令/Undo/Redo/Edit-Play-Stop/Reload 使用同一个共享服务和唯一历史；准备失败保留最后有效资源并报告，不造新 Agent GPU/编译权限。只有真正实现后才替换 C1 pending guards 和针对这些已移除 guards 的阶段测试；保留等价且更强的缺包/损坏/prestart 断言。实际正式 Editor/Player、源无关自定义 v2 包、静态和 FBX/NCA、API0/0、resize/关闭、纯UI/Headless闭包验证。C3 的 build-only 合成默认环境/部署与可视面板仍独立待实现；用户HDR解码/人工/目标/自包含/完整性能/1h不由自动测试关闭。

## 每片门禁

定向测试 → 修复 → 冻结源码 → 顺序完整无Skip Debug/Release Build.bat → native/managed/Python/M6/M7/MCP/新旧格式/smokes/inspect/三轮profiles/audit → checked deployment manifest/SHA/Complete journal/备份核验 → 合同/交付/路线图/AGENTS → 提交推送/远端SHA。保留所有失败、备份、IDE、SDK与用户数据。仅DX11，Vulkan/OpenGL下一版本；M7→M8→M9。
