# EditorSessionOwner 夹具资产闭包审计

2026-10-24。按用户要求逐项复核所有 `new EditorSessionOwner`，并扩展检查目标类型简写 `Owner = new(...)`、共享 Fixture、`workspace.PlayControl("start")` 和真实宿主间接持有者。

范围：修改前 47 个引用该类型的托管测试文件；显式构造 103 处（102 行，一行含两个构造），目标类型简写 11 处，共 114 处构造。另新增一个不构造 Owner 的测试资产辅助文件。原始源码、逐点行号/指纹及检索结果保留在 `out/verification/ecs-e6/owner-fixture-audit-first`。这是一份静态构造/依赖路径审计，不冒充全部夹具都完成运行验证。

## 修复规则

- 不给产品构造器增加测试专用注入接口；使用已有 `PrepareRenderAssets`，不修改 `StartPlay` 或闭包校验。
- 有环境/几何根时，实际编码、保存、读取 NCE/NCA 与描述文件；不伪造 PreparedSceneAssets，不用空租约替代缺失资源。
- 空场景的显式准备得到真正空租约，并断言 Play 复用同一租约身份；原本测试合法 null 的正例和缺资源的负例继续保留。
- 不把普通夹具改成 Simulation。原隔离夹具恢复完整策略、配真实资产；专门的 Simulation 合同测试保持原用途。
- 原字节不变、Play/Stop 隔离、Undo/Redo、权限、资源锁和历史断言保留；新增准备过程字节/历史不变断言。

## 逐文件结论

构造列是当前源码的 `行号 E=显式 / T=目标类型简写`；空列表示类型引用/复用 Fixture/真实宿主持有者，而非本文件构造。

| 文件（managed/ 下） | 构造位置 | 审计结论 |
| --- | --- | --- |
| Ncma.Asset.Import.Tests/Program.cs | 197E, 201E | 两个Owner均为空场景；ConfigureAssets管理导入代次钉住，原拒绝与GC边界保留，不伪造渲染依赖。 |
| Ncma.Application.Tests/Program.cs | 77E, 94E, 104E | 纯逻辑/Transform/脚本隔离与启动失败，无场景资产根。 |
| Ncma.Editor.Services.Tests/AnimationRuntimeInspectionTests.cs | 38E | 实际Animator场景；已有PrepareRenderAssets和composePreparedPlay，保留。 |
| Ncma.Editor.Services.Tests/EnvironmentAuthoringTests.cs | 122E, 54T | 修复：两条Play前PrepareRenderAssets；未发布场景复用真实空租约，已发布场景真实NCE。重新打开仅验证作者存档。 |
| Ncma.Editor.Services.Tests/CharacterEditorChecks.cs | 49E | 真实角色/动作PlayControl；已有PrepareRenderAssets和显式刷新，保留。 |
| Ncma.Editor.Services.Tests/DeclarationConsumerAuditTests.cs | 30E, 60E, 70E, 95E | 元数据/Export目录编辑审计，无Play。 |
| Ncma.Editor.Services.Tests/EditorSceneProjectionTests.cs | 20E, 31E, 44E, 55E, 55E, 64E | 显式作者投影/失效检查，无活动渲染根及Owner Play。 |
| Ncma.Editor.Services.Tests/M36WorkflowTests.cs | 52E, 134E | 修复：原导入/放置场景在PlayControl前准备真实资产；增加准备与PlayStop字节/历史不变断言。重开Owner已准备。 |
| Ncma.Editor.Services.Tests/ComponentMetadataPresenterTests.cs | 27E, 39E | 声明元数据/默认值编辑，无Play。 |
| Ncma.Editor.Services.Tests/EditorPrefabTests.cs | 41E, 59E, 72E, 86E, 91E, 102E, 120E | Prefab的game.asset_value仅作者引用元数据，不是内建渲染消费者；PlayControl场景只有Transform及自定义值，无资产根。 |
| Ncma.Editor.Services.Tests/BakeDependencyTests.cs | 66E, 71E | 空场景合法无租约、缺失真实资产必须拒绝；负例不能补成成功。其余Owner为RuntimeSessionOwner。 |
| Ncma.Editor.Services.Tests/CharacterInspectionTests.cs | 45E | 真实角色检查/重启；已有PrepareRenderAssets，保留。 |
| Ncma.Editor.Services.Tests/EcsReadConsumerTests.cs | 69E | 引用索引及分页读取；有伪UUID的独立作者文档不启动Play。 |
| Ncma.Editor.Services.Tests/AssetPolicyHostTests.cs | — | 实际CandidatePresentation cpuOnly持有空Simulation租约；原策略正例保留。 |
| Ncma.Editor.Services.Tests/GraphInspectionTests.cs | 37T | 图元数据/授权/独立作者图，不启动Owner场景Play。 |
| Ncma.Editor.Services.Tests/GraphAuthoringTests.cs | 25T | 独立图文档；Owner场景为空，图草稿不构成Animator运行依赖；Play冻结断言保留。 |
| Ncma.Editor.Services.Tests/AssetInspectionTests.cs | 104E, 18T | 资产检查/批次描述，不启动场景Play。 |
| Ncma.Editor.Services.Tests/EnvironmentInspectorTests.cs | 74E, 103E, 16T | 修复：Owner使用真实NCE/descriptor/哈希并显式准备；Empty/Reopen只做作者UI/存档。 |
| Ncma.Editor.Services.Tests/AssetAuthorizationTests.cs | 24T | 资源授权/目录UI，不启动场景Play。 |
| Ncma.Editor.Services.Tests/PresenterPanelTests.cs | 33E, 52E | 开关面板/生命周期只读，无资产消费者Play。 |
| Ncma.Editor.Services.Tests/PostProcessAuthoringTests.cs | 33T | 修复：有几何时真实NCA网格/材质槽，geometry=false时真实空租约；不改完整策略/原事务断言。 |
| Ncma.Editor.Services.Tests/Program.cs | 90E, 190E, 233E, 249E, 300E, 309E, 330E, 342E, 384E, 407E, 420E, 455E, 477E, 515E, 551E, 569E, 577E, 585E, 597E, 607E, 613E, 624E, 632E, 637E, 650E, 660E, 673E, 687E, 695E, 740E, 764E, 773E, 784E, 788E, 801E, 817E | 逐构造点及PlayControl审计：主逻辑/脚本/FBX独立预览/草稿冻结无场景渲染根；环境待准备、相机、阴影、JSON等只编辑。Export分页失败独立记录，不改断言。 |
| Ncma.Editor.Services.Tests/OriginalBatchHistoryTests.cs | 10E | 原批次/Undo历史，无资产消费者Play。 |
| Ncma.Editor.Services.Tests/PresenterTextTests.cs | 38E, 57E, 71E, 87E | UTF8/页预算/重用，用于呈现包测试，无资产消费者Play。 |
| Ncma.Editor.Services.Tests/RuntimeChangeApplyTests.cs | 39E, 58E | 原差异事务，Transform/自定义Budget值，无资产根；复用Review Fixture。 |
| Ncma.Editor.Services.Tests/RuntimeChangePresenterTests.cs | — | 复用Review Fixture，纯逻辑差异确认/Stop/历史，无资产根。 |
| Ncma.Editor.Services.Tests/RuntimeChangeReviewTests.cs | 33T | 简写Owner，Transform/Payload/脚本差异，不含渲染组件。 |
| Ncma.Editor.Services.Tests/WorkspaceTests.cs | 17E | 对象列表与工作区UI，Play场景空对象无组件资产。 |
| Ncma.Editor.Services.Tests/WorkflowTests.cs | 206E, 213E | 工作流及Observe/Runtime-only Owner，无场景资产消费者。 |
| Ncma.Editor.Services.Tests/UiWorkspaceTests.cs | 21E | 独立UI文档与空Owner场景；PlayControl不引入3D资产。 |
| Ncma.Editor.Services.Tests/UiSelectionTests.cs | 22T | 场景UI引用的编辑/原子事务，不启动Owner Play；UI包不属于本渲染资产闭包。 |
| Ncma.Editor.Services.Tests/UiPackagePreparationTests.cs | 19T | UI出版独立合同，Owner不启动资产场景Play。 |
| Ncma.Editor.Services.Tests/UiGeometryTests.cs | 15E | UI布局独立文档，不启动资产场景Play。 |
| Ncma.Editor.Services.Tests/ToolbarTests.cs | 17E | 工具栏及独立GUI设备，不启动资产场景Play。 |
| Ncma.Editor.Services.Tests/RuntimeInspectionTests.cs | 50T | 简写Owner，Transform/Payload/脚本检查及观察系统，无资产根。 |
| Ncma.Rendering.Tests/AnimatorJointAcceptanceTests.cs | 34E | 真实动画联合入口；已有PrepareRenderAssets及composePreparedPlay。 |
| Ncma.Rendering.Tests/AnimatorRootSceneTests.cs | 190E | 真实Animator/RootMotion；已有PrepareRenderAssets及租约关闭失败断言。 |
| Ncma.Rendering.Tests/AnimatorSceneTests.cs | 95E | 真实Animator场景；已有PrepareRenderAssets及composePreparedPlay。 |
| Ncma.Scene.Rendering.Tests/AssetPolicyTests.cs | 58E, 61E, 67E | 有意覆盖Simulation无需求、完整策略缺资源拒绝、非法策略；不能为负例补资源。 |
| Ncma.Scene.Rendering.Tests/RuntimeAssetTests.cs | 93E, 94E | 两个Owner已有真实AssetFixture及PrepareRenderAssets；失败保留旧资源和代次隔离。 |
| Ncma.Scene.Rendering.Tests/Program.cs | 90E | 修复：隔离用例从Simulation恢复完整策略，以原AssetFixture真实网格/材质准备，原运行写与作者字节断言保留。 |
| Ncma.Scene.Rendering.Tests/EnvironmentAssetTests.cs | 223E, 229E | 两个Owner已有真实EnvironmentFixture及PrepareRenderAssets；代次/字节/Undo/冻结保留。 |
| Ncma.Editor.Services.Tests/PresenterBuildSectionTests.cs | 27E, 57E | 面板构建/隐藏预览，只读空场景，无Play资产需求。 |
| Ncma.Rendering.Tests/PresentationCostTests.cs | — | 读取真实CandidatePresentation拥有的Owner，使用宿主原资产准备，不在此new Owner。 |
| Ncma.Rendering.Tests/Program.cs | 44E | Rendering commands Owner用于原渲染配置命令，不启动场景Play。 |
| Ncma.Rendering.Tests/SceneResourcesTests.cs | 50E | GPU资源真实场景；已有PrepareRenderAssets，版本/锁/回收断言保留。 |
| Ncma.Rendering.Tests/SkinSceneTests.cs | 121E | 蒙皮存档/重启场景；已有PrepareRenderAssets，资源钉住断言保留。 |

## 实测与未通过项

两配置全63托管编译均零警告/错误，原生全目标无增量工作；`ctest -N`各100。修复相关33项原用例Debug/Release均通过；完整Application、SceneRendering、AssetImport均通过。完整Services两配置仍在 `Program.cs:669` 的未知脚本Export分页断言失败，失败及被阻断的后续用例不算通过。产品代码、正式EXE未变，无提交推送。

本轮执行结果与失败边界记录在 [E6 统一验证记录](E6_UNIFIED_VALIDATION_PLAN.md)。本审计不删除或跳过原完整 Services 注册；新增 `--asset-owner-fixture-tests` 仅作为修复后的定向复测，包含原环境 Inspector、环境发布、后处理与 M3.6 导入工作流的完整用例体。任何完整入口失败仍单独记失败，不能以定向通过替代 E6 验收。
