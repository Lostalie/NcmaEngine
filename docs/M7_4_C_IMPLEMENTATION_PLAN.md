# M7.4-C 正式后处理接入方案

2026-10-14。上一片B已实现/测试/提交推送，开工前本地和远端main均为`fe7d7be62237c1c9f789bf0e163b1717b317aae1`，4项既有IDE变更保留。C按C1→C2→C3交付，每片定向修复后冻结源码、完整顺序no-Skip Debug/Release、核对受检部署/备份/journal/实际apphost，再提交推送并核对远端。C全部完成前不宣称正式后处理接入完毕；D完整替换及产品联合独立保留。

## C1：受检程序库与产品闭包（自动候选完成）

149项实际检查及最终顺序完整Debug/Release通过，见[C1交付](M7_4_C1_DELIVERY_REPORT.md)。默认行为未切换；C2交付状态见下节。初次超时、主动中止和旧场景文件替换间歇失败的记录均保留。

独立严格v1索引，分开列出具名模块UUID的NPH1数值Feature程序包和Tone用NCS1完整scene变体。精确UUID/hash/profile/shadow/skin，最多8个Feature包、32个Tone变体、总32MiB、索引16KiB；每个Tone模块/profile/skin要求双shadow变体。只复制有界值，不接受远程源码/编译器选项或绝对路径。旧shader selection/NCS1/NPH1格式不改。

RuntimePostProcessLibrary使用原RuntimeReadPin，校验canonical路径、reparse/hardlink、外部索引hash及部署manifest，文件/父目录保持只读pin。加载只做CPU验证，不初始化GPU/World/Editor。完整库准入先核对Tone的非Tone各stage与当前原scene程序逐字节一致，以及所有共享skin字节一致，再通过原query12/15/16真实反射；全部完成后才返回不可变准备结果。没有半组默认选择发布。取缓存结果也检查owner、健康/离帧、租约和审批；失败关闭所有候选pins。未知模块不能静默映射到官方模块。

构建期Cook为Editor/DX11 Player生成3种Tone×2profile×2shadow×2skin=24包，以及1个Bloom NPH和索引。使用A/B相同公开声明/编译/包合同，不改旧9/8个默认scene shader及索引，正式场景仍默认原渲染行为。UI和Null不部署后处理目录；UI保持18文件。不向产品分发Cook工具或作者源码。项目显式选择、场景配置与宿主实际消费在C2接入，本片不加入会被忽略的项目字段或启用按钮。

验证严格索引/所有文件/manifest/租约/取消/失败重试、CPU-only子进程、真实全部变体准入、用户Tone与官方同路径、真实像素、错误非Tone程序/共享skin/末项拒绝无部分结果、owner/reentry/revoke/关闭、实际Editor/Player部署与UI/Null排除。完整旧测试保留，新增文件计数按真实闭包更新而非削弱manifest检查。

## C2：高层配置与正式共享执行（自动候选完成）

67项模块/52项共享、双配置各71次正式宿主检查及最终顺序完整Debug第二轮/Release首轮通过。见[C2合同](M7_4_C2_RUNTIME_CONTRACT.md)、[C2交付](M7_4_C2_DELIVERY_REPORT.md)。首轮注册数量断言遗漏已修正；间歇File.Replace失败保留，独立及最终完整复测通过，不宣称原因已修复。下一片C3，D/人工/性能门禁保留。

增加单一scene-wide后处理配置，以精确注册模块UUID/版本和闭合有界参数表达，而非Shader文本/路径。默认Off；Off不准备子图资源。官方Bloom与可信启动时注册的用户Feature使用同一目录/图构建接口，明确Tone在末尾及线性域依赖，不能用无意义的任意重排序伪装定制。

项目选择明确绑定程序库外部hash；运行包和scene配置闭包在gameplay前验证。Editor/Player共用SceneRenderSession的off-frame准备和就绪状态，world/revision/Play/资源代次绑定；更换Tone/Bloom/尺寸/shadow/environment必须准备完整候选，不在Submit编译/读文件/等待IPC。失败保留上一安装，但宿主清除过期视口并显示诊断，不把旧图作为新配置。保持同一skin kernel、资源cache/lease、Play隔离与Reload/Undo失效规则。Headless验证持久值但不创建渲染资源；纯UI完全轻量。

## C3：编辑器与AI同一事务

Inspector提供预设、EV、Bloom阈值/soft knee/强度/层数/散射，以及已注册模块的有界参数/合法顺序。直接操作和AI提案均使用现有原命令、精确对象权限、会话/版本、唯一Undo/Redo；独立只读模块目录权限不得隐含执行/文件/编译权限。预览/对照结果有world/revision/stamp，不授权Agent直接访问World/GPU。未接入推理服务保持真实标识。

覆盖真实stdio默认拒绝、明确审批、过期/撤销/重配对/重入、撤销重做、Play冻结、真实视口效果和失败恢复。AI不是只生成代码，但也不新增任意项目脚本执行能力。

## 未关闭项目

D仍负责完整pipeline替换实例和真实FBX/IBL/shadow/Bloom/显示UI组合、搬移产品和目标成本。C1单模块像素/编译结果不替代它。人工UI/DPI/第三方MCP、用户HDR-FBX材质、HDR解码、目标机/.NET自包含、完整性能/1h保持开放；历史source恢复高分配仍待调查。DX11only，Vulkan/OpenGL下一版本；M7→M8→M9。
