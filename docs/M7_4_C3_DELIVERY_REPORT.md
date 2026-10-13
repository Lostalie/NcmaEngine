# M7.4-C3 后处理面板与 AI 编辑交付记录

2026-10-14。开工前本地/远端main同为 `1531253314ac3f8f1a22254a2c6b777e5b6d2cba`。本片C3，D联合与替换另行推进。见[实施方案](M7_4_C3_IMPLEMENTATION_PLAN.md)、[运行合同](M7_4_C3_RUNTIME_CONTRACT.md)。

## 实现

owned有界注册目录/默认参数，Editor原事务/草稿/恢复/历史/Play的共同语义校验，GUI Tone/EV/Bloom及用户Feature/合法顺序/Off对照，独立模块只读MCP目录与本地人类精确范围/客户端/60秒审批。状态有World/revision/Play/最近frame，不把裁剪或未安装配置报为ready。无native/Shader/格式/推理/Python gameplay改变，不新增AI任意执行权。

## 定向检查与修复

首次构建0警告0错误。第一轮作者测试发现模块按钮调用Transaction没有传selection，提交后丢失当前Inspector对象；保留 `out/m7-4-c3-author-first.log`，修为和原组件按钮一样明确传目标UUID。不是删除GUI断言或隐式恢复任意对象。

第二轮8组作者测试通过；后续补齐CPU-only子进程、组合曝光、原始JSON候选、Owner/重入/精确scope以及目录授权控件。第四轮 `out/m7-4-c3-author-fourth.log` 9/9组全部通过，包括真实stdio默认零可见、精确审批/TTL/撤销/重配对/endpoint更换、事务默认拒绝/人类审批/历史和过期/撤销。独立子进程确认native Renderer/Platform/Gui/Compiler=0。

正式宿主首轮90次实际检查/API0/0，`out/m7-4-c3-hosts-first.log`，证据 `out/verification/m7-4-c3/hosts-first/004c0fdc4628474a88dd024e02942e7a`。第二轮补入用户模块实际GUI参数改变及状态核验，90次检查/API0/0：`out/m7-4-c3-hosts-second.log`、`out/verification/m7-4-c3/hosts-second/e7367651f4ad441da42fda83bea5e350`。次数包含实际NCP采样帧断言，会随调度变化，不能当作固定独立用例数。

共享场景追加ready/culling戳检查后53项、暖64 Submit分配0、API0/0通过：`out/m7-4-c3-shared-final.log`、`out/verification/m7-4-c3/shared-final/47c100eeee9a4d93a1eb5af8f474f15c`。保留原C2所有断言。准备状态对当前revision保守失效；不重建的准备也更新已验证观察戳。

## 完整回归/部署

最终19份冻结源码的无Skip Debug首轮全部通过：`out/m7-4-c3-full-debug-first.log`，12项native/27项managed、Python43；Editor服务154/154（含本片9组）、原Animation115、M6/M7/真实stdio/新格式与旧格式拒绝/smokes/inspect/三轮profile/audit/部署恢复全部保留。原渲染综合226.86秒，新增正式作者宿主92次检查/API0/0（`out/verification/m2/post-author-Debug/7101752a759e4c89890802effd0559da`）。

Debug部署清单139/实际142/备份142逐路径、大小、SHA核对，Complete journal `57a2785228b9486c9ee1333af40690b1`；实际apphost `--validate-package` 返回0。与C2同配置原生Renderer及10份旧shader文件SHA一致。产品代次 `bc1c655a047d4f84bbb4fa3a969bc5e1` 中Editor139/PlayerDX11102文件、各26后处理文件，CPU-only库预检通过，Null无后处理目录；库索引SHA仍为 `97330B1CA92BB54CE35B2C5F9E4C4BC7A97B30AF9F79C6436D292FE94A4A17DE`。一次组合补充核验命令异常结束（exit -1、无输出），不作为通过证据；拆分后产品检查、冻结19、实际apphost分别通过，完整构建本身退出0。

顺序Release首轮（`out/m7-4-c3-full-release-first.log`）native12通过，managed26/27通过，唯一失败为新增PostAuthorHosts在原`SceneDocumentFiles.Save`/`File.Replace`恢复未知Tone负例后报“无法删除要被替换的文件”（PostHostTests:45），不是C3控件断言失败。构建退出1、未继续Python/profile或部署，Complete journal仍为上面的Debug代次。

失败文件保留：`out/verification/m2/post-author-Release/14f5351f2b3945b8b182dc265f77f036/scene-fixture/ef8ad54d2f4d4385b8a44f85a7792fa3/start.ncmascene`，2590字节、Archive、SHA `D2A885193414A9A952E129D7B83DE825280D7A3C0E6410C382F5EFBEC06836BE`。事后只读独占打开通过；代码检查该Player拒绝路径在native/files pin前退出，场景Read作用域已关闭，但不足以归因外部进程或证明根因修复。没有删除失败文件、添加重试/等待、改变原子替换实现或削弱断言。

独立Release新目录复测94次检查/API0/0通过：`out/m7-4-c3-hosts-release-recheck.log`、`out/verification/m7-4-c3/hosts-release-recheck/e1e6422061734962a2f60eb379739aff`。19份冻结代码未变；随后完整Release第二轮全部通过：`out/m7-4-c3-full-release-second.log`，12项native/27项managed/Python43，原渲染综合159.61秒、Editor154/154、Animation115及全部M6/M7/MCP/格式/smokes/inspect/三轮profile/audit/部署恢复保留。原A538/B172/B3 353/环境产品594检查通过，后处理模块67/共享53（暖64 Submit分配0）/原宿主71/作者宿主93检查均API0/0。作者宿主证据 `out/verification/m2/post-author-Release/a2755876126e4ee8bb0080e462ac4341`。最终代码证据为Debug首轮→Release第二轮，首轮Release失败未被抹去。

Release产品代次 `f7cc89fcd66d484c86d5f6bdea7b8cf1`，Editor139/PlayerDX11102清单逐文件检查及CPU库预检通过，各26后处理文件，索引SHA未变，Null无后处理目录。正式`out/bin`清单139/实际142，备份142逐项匹配journal.oldFiles，Complete journal `87db93a508aa460982914e7eb86511df`。实际apphost验证返回0；冻结19份SHA逐项未变。两次成功部署的142文件恢复备份与失败现场均保留。

最终Release文件SHA：

| 文件 | SHA256 |
| --- | --- |
| out/bin/NcmaEngine.exe | 15A5FBDBE3A3383865F5CCB40721F8D8828B453B5C7690D062389A5684C26734 |
| out/bin/NcmaEngine.dll | 97938FC854B3EA20F777917975BFCED7D388065FC557D7029EDC9AF5B04EAF97 |
| out/bin/plugins/NcmaRenderer.dll | D5A8F1B4DF7160A114503645E06DCEE73B72FA873174CD880E5F230A31A83C92 |
| out/bin/assets/environments/neutral-v1.nce | 96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665 |

与C2同配置的原生Renderer、10份旧Shader、默认NCE、后处理库索引均未改变。此次自动候选完成仅指C3及C链，下一片D；没有关闭人工或完整性能门禁。

## 冻结代码/构建文件

首轮完整Debug前冻结19份，后续每次源码变化均须重新冻结并重跑完整门禁。

| 路径 | SHA256 |
| --- | --- |
| CMakeLists.txt | B446478D5C1C33C3ABC3F5B4A979E50A0F974DA85925325C2C6D9E9A25DBD27F |
| managed/Ncma.Editor.App/EditorPresenter.cs | A1D684FA0203016610F2DE416DBDDA28EAA921F9B1A5B7EEA7BBACBD93012780 |
| managed/Ncma.Editor.App/EditorPresenter.PostProcess.cs | 76BE93C6450738F6D956BAA251997CF90C66770DDCEA0A9A2D677AC4D73CE2CA |
| managed/Ncma.Editor.App/Program.cs | 7FA1FD16DBDA8D6E6F7C2FC61FD0A234558D7236468C890D315B45EEC83629E6 |
| managed/Ncma.Editor.Core/InspectionExtensions.cs | 207335FD0176173494AFEFFA3A683FB8E13E9F49990886CA1410363751D9F135 |
| managed/Ncma.Editor.Services.Tests/EnvironmentAuthoringTests.cs | 7AD010254B95DC6A4DFC161C152BBA35348FF7718A6DBC90555080E003E7E107 |
| managed/Ncma.Editor.Services.Tests/PostProcessAuthoringTests.cs | 24891DA381625187362487CB29A56861E407C9A978E39E81361472BFB65E094C |
| managed/Ncma.Editor.Services.Tests/Program.cs | 620A66590231FA720EB7955CEDFC8E73E63DE9CD380B4299AB2F55BEC8713693 |
| managed/Ncma.Editor.Services/EditorAuthorizationController.cs | A661CB90F81BA7A754C5275BB0D77A4CD8D4C3603633D199DDA3682B6A543910 |
| managed/Ncma.Editor.Services/EditorPostProcessWorkspace.cs | 54F628A6D150EE5F834C51C029D552EB76D0D1288C4F3DCAD9E0FA7CED838D13 |
| managed/Ncma.Editor.Services/EditorSessionOwner.cs | EABD41BFE67775FDC3269A8EBA44971C94BD5C16178120E3A5D8738AF670E86A |
| managed/Ncma.Editor.Services/PostProcessEditorAdapter.cs | 92BCD87B270FBA549A6CC3EAE66EEFA6EFF6EFC69FA28FA2862A249E8E0687A5 |
| managed/Ncma.Editor.Services/RenderingEditorAdapter.cs | 1FE2DB027E7E294ED62E1B2871B5B13EB7529611E40CD6393DA1D933D0C1123D |
| managed/Ncma.Rendering.Scene/PostProcessModules.cs | 1555584C035A7DBC77C50697B88849BDA0E19E8643BED809C8773D3CCC057079 |
| managed/Ncma.Rendering.Scene/ScenePostProcessPreflight.cs | E0BF3E4B9E34639EFEF589379AB02BAB3A9C675036D040EFB0DCD3D57009BC04 |
| managed/Ncma.Rendering.Scene/SceneRenderPostProcess.cs | FAE44467D07E8BDAA1E740528A4B9715B52A31FCD832B2ED68A22A8E90DA6AA5 |
| managed/Ncma.Rendering.Tests/PostHostTests.cs | AD6BF88DB63FD9CAFAF59D9125003BCF5728BF1DA10845DBC892E4028EF7B9EC |
| managed/Ncma.Rendering.Tests/PostSceneTests.cs | 1F12D2257934E6DD2BCB1F06ABFA9512982C665502DD226E1346D1B099C8A39C |
| managed/Ncma.Rendering.Tests/Program.cs | 409CB4367F64E050E829ECAC3C3E83D67FF0E5919BC05C3BA1F22C6A76BCD5BE |

## 保留

D完整管线替换与实际FBX/IBL/shadow/Bloom/UI搬移产品联合未完成；人工UI/DPI/MCP、用户素材/HDR解码、目标机/自包含、完整性能/1h继续开放。首轮间歇File.Replace原因不因后续复测通过而宣称已修复；所有失败/备份/SDK/userdata/4项IDE设置保留。只实现DX11；M7→M8→M9。
