# M7.3-C3-B2 交付记录

2026-10-14。开工 local/remote main 核对为 `342753a89e00e1e84b75bb1fd6fe90b163464799`。本片实现预设选择、精确项目资产发布、场景/文件同一历史以及独立环境目录授权。详细边界见[合同](M7_3_C3B2_RUNTIME_CONTRACT.md)。C3-C 联合验收、整个 M7.3 和人工门禁仍开放。

## 实现与验证范围

人类选择合成中性预设或现有环境，审批精确目标/UUID/代次/SHA/路径。不可变 NCE 显式准备，描述文件与场景通过 Core scene participant 一次提交/Undo/Redo。旧 read pin、磁盘路径白名单、原 ncmeta 与三项 MCP schema 不变。取消不创建缓存，40 次取消不会耗尽代次预算。撤销保留仍可被租用的 NCE，不声称删除所有缓存。

新 apply 工具仅接受人类准备的 UUID 令牌，沿用 Endpoint 精确变更授权。独立只读目录默认空、分页/UUID限额/60秒授权，绑定文档、资产、客户端实例与 audience epoch，不返回路径/源内容/GPU。已批准的本地文件范围仍不隐含 AI 变更权限。

可信配对注册预检和 inspection 上限17（原16+环境目录），第18项拒绝；容量错误不残留命令。原生 ABI、shader/Cook 算法、默认 NCE 字节、网络/Python玩法/推理均无变更。

## 定向测试与修复

新增13组：Core 权限先于 durable Prepare、只读回调、迟到撤权补偿、no-op请求作用域、空操作拒绝；17项容量与原子注册；owned NCE clone/延迟加载/取消；pin下单历史Undo/Redo/保存重开；默认拒绝/注入/过期/原权限历史；审阅篡改/资产版本/场景变化/Play/线程；四个发布故障点及重试；commit_decided故障冻结与精确恢复；准备取消/故障/外项目/重复UUID；复制UI确认/取消/冻结；真实stdio目录schema/无文件IO/TTL/重配对；真实stdio变更提案审阅/应用/Undo。原132组保留，Editor **145/145**。

正式宿主新增 Editor 审阅事件→实际GPU图像、独立IBL像素、三轮Undo/Redo、NCE pin、Play/Reload/Stop、保存后源项目Player与NCP Player。定向运行70项检查，pixelError=0、API0/0。完整构建内的检查计数包含逐帧像素检查，随实际呈现帧数变化，不是新增测试用例数量。隐藏原生窗口持续取消失焦交互，因此自动用复制GUI帧完成审阅，再经正式宿主绘制提交结果；没有把自动输入称为人工验收。

保留的失败记录：新增测试初次使用错误ProposalView/PlayerOptions属性名，编译拒绝后修正；手动推进资产时钟的fixture未刷新即Play而被拒绝，补齐真实刷新；正式源宿主触发原16项容量，修正为明确17项并补测拒绝边界；隐藏窗口取消审阅，调整自动输入fixture而不放松失焦取消；独立schema测试器对null整数分支先检查类型。全部旧断言保留，未删测试以通过。

最终19份源码冻结后完整顺序无Skip Debug/Release均通过，二次核对源码SHA一致，无新增编译警告；C3-B2自动候选完成。

| Build.bat | Native CTest | Managed CTest | Python |
| --- | --- | --- | --- |
| Debug第一轮 | 12/12，3.65s | 22/22，442.87s | 43/43，1.081s |
| Release第一轮 | 12/12，1.92s | 22/22，323.62s | 43/43，0.884s |

Debug Rendering184.66s / Editor76.72s / Player67.41s；新增正式宿主102次检查、像素误差0/API0/0。原 B3 353、C2-C51、C3-A83 与全部旧 M6/M7、MCP/格式、FBX/NCA/Jolt、smokes/inspect、三轮历史 profile 和部署恢复均通过。

Release Rendering123.81s / Editor73.51s / Player66.78s，Editor145/145，新增正式宿主111次检查、像素误差0/API0/0。原断言和自动门禁全部保留。

定向日志 `out/m7-3-c3b2-authoring-{first,second,third,fourth,fifth}.log`、`gpu-{first,second,third,fourth}.log`；编译日志同前缀。完整日志 `out/m7-3-c3b2-full-debug-first.log`、`out/m7-3-c3b2-full-release-first.log`。失败、staging、备份、测试数据不删除。

## 部署

Debug generation `d09db91946c1411f9287d6b1abf0e854`：113 manifest / 116实际 / 116备份，Complete journal，逐路径/尺寸/SHA检查；实际 apphost `--validate-package` exit0（Start-Process -Wait）。post-deploy audit `fc1202272b3e437ca8449c3fb9de2aca`，audit_passed=true、h8_accepted=false。

Release generation `9f3d75eb372d411da17bb4daac0370f3`：113 manifest / 116实际 / 116备份，Complete journal，逐路径/尺寸/SHA检查及真实apphost验证exit0。post-deploy audit `1d5c824b74674a6a843a043aa693ed0a`，audit_passed=true、h8_accepted=false。两次部署均保留可恢复备份。

Release之后再次核对Debug的116文件备份不变。文档更新后inspect exit0、Python43/43（0.850s），19份源码SHA仍一致。

最终 Release 哈希：

- NcmaEngine.exe：`088811E36ED9512C966E41367C8FFC7C3566CF74E9612DB28854723C45D29E70`
- NcmaEngine.dll：`D3E995A23717028EB59A1780DDD222ACC706AA5142AA28F51CED406BFF45206D`
- NcmaRenderer.dll：`C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D`
- 默认 NCE：`96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665`
- Debug/Release 各自原生Renderer、9 Shader包+索引均与C3-B1同配置哈希不变；默认NCE不变。

提交前再次核对远端仍为开工基线。提交排除生成/部署/备份以及4项既有IDE/.user变更。人工UI/DPI/第三方可见MCP、真实用户HDR-FBX-material、目标机/自包含、完整性能/1h仍待验收；可信磁盘恢复不自动恢复未保存的内存文档。C3-C 才执行下一轮正式产品联合收口；DX11only，Vulkan/OpenGL下一版，M7→M8→M9。

## 冻结源码

| 源码 | SHA256 |
| --- | --- |
| managed/Ncma.Assets.Authoring/EnvironmentAuthoring.cs | 7FAAC7B928954AC3FF416E264744889052AE3CC8CB5F92A5C97384AE130E4770 |
| managed/Ncma.Assets.Authoring/Storage/AssetDiskTransaction.cs | 4CBCB812B5C249D90054B469FE6359F6DB44489D121D84AB49EFEBC91A89747F |
| managed/Ncma.Assets/EnvironmentData.cs | B0E0B33E49633DBEB5871F697FB2BAC767BA39E98354EF3B82F416E8035493D0 |
| managed/Ncma.Editor.App/EditorPresenter.cs | 5F908E86523D5A07C7B982DA1880CDC5D1AD4262BE54A4A7182B6A5CEAE613DA |
| managed/Ncma.Editor.App/EditorPresenter.Environment.cs | 3D1B62CDD14C91078648782A29BADAB826C92B24E19256B54FE9228FC5F86481 |
| managed/Ncma.Editor.App/EditorSceneEnvironment.cs | 967EFEBAB89F4D41A3D24CAF351F8C348DDD54731716FDE4A18CD5373D1ABEFF |
| managed/Ncma.Editor.App/Program.cs | 3CCC01B3439B5E6CB07A572FC461B0D5CD620D47CF33912954613862C181D87C |
| managed/Ncma.Editor.Core/CommandImpacts.cs | 9FAF3A704D65ACE150E29E7C6DF42FE44C9FB510A9CB69F64AD65962DC0C5E1F |
| managed/Ncma.Editor.Core/CommandParticipants.cs | 0587D282A13943D22D6264F118AB3C21FBACDCEDBBA84335670542D8F5DA0F7B |
| managed/Ncma.Editor.Core/EditSession.cs | 55417CB858A8DE397618C677DFC05D90FF39D68A0100CFE4322144CA71174790 |
| managed/Ncma.Editor.Core/InspectionExtensions.cs | 902E1EC08C5C86BB4D9F6872B156F25AEA4B01D89282EE8903C9DEB9012125C4 |
| managed/Ncma.Editor.Services.Tests/EnvironmentAuthoringTests.cs | EEA864C5CE34AA00CDACF8A0D78E17699A1D2A07386540327F203284536FFA52 |
| managed/Ncma.Editor.Services.Tests/Program.cs | C2869DD1A7A66C3422FBE9AC6CD0FF69ACD826A3AC13035D3901191B8D159163 |
| managed/Ncma.Editor.Services/EditorAuthorizationController.cs | A79B24DCE0371CB36B657897FD6D0D081340D8C0CBA98350E8DC7FEEB46FB4E5 |
| managed/Ncma.Editor.Services/EditorEnvironmentWorkspace.cs | D1ECE7312D78CFD3BAB6D69B02CD468F34D0EC6CE98D4D5EC3D3032DD068908B |
| managed/Ncma.Editor.Services/EditorSessionOwner.cs | 3B3B47111FC8BEF3080CC74640CECE91FC6175D082A2B40BDE12BAB1AC716708 |
| managed/Ncma.Editor.Services/EditorWorkspace.cs | D38A2B447F358BAB1FA64E8905DD831FA2CE8C3F8F696EAD97C4105B50925DBC |
| managed/Ncma.Rendering.Tests/EnvironmentAuthoringHostTests.cs | 66BBFB35B4FBC68533793336303DAC67AE570C7237B429609406B374184A5037 |
| managed/Ncma.Rendering.Tests/Program.cs | 76E816E87F6ACAEA1FD734F8A8A35DC3D131A33B987130F9E8E6338757229780 |
