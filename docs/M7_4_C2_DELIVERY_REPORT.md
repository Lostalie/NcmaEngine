# M7.4-C2 高层配置与正式共享后处理交付记录

2026-10-14。开工前本地/远端 main 同为 `135a8088f71346b01c1237bb4c7b03b10869a745`。C2自动候选完成，最终25份冻结源码下顺序无Skip Debug第二轮/Release首轮全部通过；C3 专用编辑器/AI 与 D 联合/替换仍保留。见[合同](M7_4_C2_RUNTIME_CONTRACT.md)、[C 分片](M7_4_C_IMPLEMENTATION_PLAN.md)。

## 实现与边界

新增纯 C# 单一 scene-wide 值组件、精确注册模块/版本/闭合参数、公开 Feature 串接、显式项目库外部 SHA 选择和共享离帧准备。正式 Editor/Player 在 gameplay 前预检并共用执行，默认 Off；用户注册模块和官方模块走同一路径。Editor 原事务/Undo/Redo、Edit/Play/Reload 和 NCP 保留；失败视口清除，Player fail-stop。Headless 仅 CPU 值及显式文件闭包；纯 UI 不引入后处理。native、Shader、NCS1/NPH1、AI 执行权限、Python 定位不变。

## 定向验证与修复

早期构建中 World.ReadOnly 可见性、文件库入口名称、组件重复注册等夹具问题已修正，原日志保留。复查发现裁剪视图可能给旧 GPU 状态盖上新配置戳，已修为不发布、再次可见时必须准备；补入回归。审批回调保护 runtime 关闭/重入，最终关闭保护即便释放异常也归位。Player 准备失败明确清屏再报故障，保留清屏本身失败的异常。

- 最终模块/组成 67 项，独立 scalar/Half/ACES/sRGB 整图最大误差 0、API 0/0：`out/m7-4-c2-policy-final.log`，证据 `out/verification/m7-4-c2/policy-final/265d9f127c6745dea503d80469e419e3`。
- 最终共享场景 52 项、API 0/0，暖 64 次 Submit 分配 0/无重建；环境代次/Off/resize/shadow/审批/关闭/空视图/候选恢复：`out/m7-4-c2-shared-final.log`，证据 `out/verification/m7-4-c2/shared-final/fce2acc544dc440e9ea21e0377611b75`。
- 正式宿主第二轮 70 次检查、API 0/0：`out/m7-4-c2-hosts-second.log`，证据 `out/verification/m7-4-c2/hosts-second/811994a96ffb421fadde2ed0935ba780`。包含 Player Off/Bloom、未知注册/库 hash pre-gameplay、Headless、NCP、Editor 默认拒绝/原 scoped transaction/Undo/Redo/Play/Reload/故障清屏恢复，以及真实用户模块正式 Editor/Player。宿主计数含按实际帧数执行的 NCP 图像断言，不是固定用例数。
- 三组拆成独立 CTest，原 Rendering 240 秒与 PostLibrary 120 秒不变，未削减旧断言。

## 完整回归与部署

首轮完整 Debug `out/m7-4-c2-full-debug-first.log` 未通过且未部署：native12/3.72秒通过；managed24/26通过，总505.30秒，原 Rendering 223.49秒通过。C1程序库149、C2模块67/共享52通过。两项失败如下，保留全部日志/夹具。

1. PostHosts 在未知 Tone 模块负例后恢复场景时，原 SceneDocumentFiles.Save/File.Replace 报“无法删除要被替换的文件”（测试第45行）。保留 `out/verification/m2/post-host-Debug/2ad344ebe4ba498fac83b7550b408c8b/scene-fixture/e6afe42ece414cee955f7ac12588047f/start.ncmascene`，事后属性Archive/2590字节，只读独占打开成功；无法据此确定当时的占用来源。保存实现和该断言不改，同源码新夹具复测70次检查通过：`out/m7-4-c2-hosts-recheck.log`、`out/verification/m7-4-c2/hosts-recheck/6dd0c242f4fe4978a1917780b20fd054`。这是复测通过，不是原文件替换间歇问题已修复。
2. 原 SceneRendering 注册表检查仍预期11个组件。本片追加一个独立值组件，改为精确12个，并保留排除Environment/Post后旧10个的精确数量、旧往返/拒绝断言，额外检查Post Off往返。独立复测108/108通过，日志 `out/m7-4-c2-scene-registry-recheck.log`。未删旧测试或放宽为大于等于。

修正后25份代码/构建文件重新冻结，完整 Debug第二轮 `out/m7-4-c2-full-debug-second.log` 退出0。native12/3.71秒，managed26/508.72秒，Python43/1.084秒；原 Rendering222.90秒、C1库19.38秒、C2模块0.55/共享1.13/宿主5.39秒。新增67/52/71次检查均通过，宿主次数受实际采样帧数影响；maxError0/API0/0。原A538/B172/B3 353/产品594/M6-M7/MCP/格式/smokes/inspect/三轮profile/audit/部署恢复全部通过。

Debug证据：`post-module-Debug/5ca13b4f51b94d5b8687e168fbc834d3`、`post-scene-Debug/9e68d4208ffc40c880c846a8913e848d`、`post-host-Debug/4b39222552ee4793ab372518492b4b51`，均在`out/verification/m2/`。受检部署139条manifest/142实际/142备份逐项匹配，Complete journal `8d017342952b4a01a1ca1f76c43d7164`，实际apphost校验退出0；25份冻结SHA不变，原Renderer/10份shader与C1同配置逐字节一致。

Debug实际暂存 `292524154aef4e7d997436b007aff0bd` 的Editor139/Player-DX11102条manifest全部核对，各26个post文件，独立CPU-only预检通过，Null无post目录。索引SHA `97330B1CA92BB54CE35B2C5F9E4C4BC7A97B30AF9F79C6436D292FE94A4A17DE`。随后才启动完整Release，最终结果如下；首轮/独立复测不替代最终完整门禁。

Release首轮 `out/m7-4-c2-full-release-first.log` 退出0：native12/2.07秒、managed26/383.17秒、Python43/0.934秒；Rendering158.72秒、PostLibrary11.07秒、C2模块0.56/共享1.17/宿主5.20秒。新增67/52/71次检查，maxError0/API0/0，原A538/B172/B3 353/产品594/M6-M7及全部smokes/格式/MCP/inspect/profile/audit/恢复保留通过。最终两套构建无新增编译器警告。

Release证据位于`out/verification/m2/`：`post-module-Release/a8b4e01ada6b4084b669c49335a662c8`、`post-scene-Release/864dc49504eb4b1aa5fe239b4c89eb1b`、`post-host-Release/e2b08fc888ff482691ab172c14768a63`；原环境产品594项在`render-Release/environment-products/867c9074669a45e19dc1fcd5fe389e0e`。

最终Release受检部署139条manifest/142实际/142备份逐路径/大小/SHA核对，Complete journal `fab2f17d22084dcc81c679500bc701d8`，实际`out/bin/NcmaEngine.exe --validate-package`退出0；25份源码SHA再次一致。EXE SHA `C6E419F905DE5FA0BE3807166BE73F2796A5C5F616F9F3BD32BFA36262012785`，主DLL `8756345F22DCC19A4D94145B0C605D61CE07441C667D309776D61B3B108A5467`。原Renderer/10份shader与C1同配置一致，默认NCE仍为`96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665`。

Release暂存`8c45f18b8c5d4b85bbb71df2177c0508`的Editor139/Player102条manifest全部逐文件核验，各26个post文件、与Debug相同索引SHA、两个独立CPU-only预检均通过，Null无post目录。这是正式服务接入/受检产品闭包证据，不替代D整套搬移后处理产品联合验收。

Release之后再次逐文件复核Debug的142文件备份；文档/路线图更新后inspect退出0、Python43项再次通过（`out/m7-4-c2-final-python.log`，0.889秒），`git diff --check`通过。提交前本地/远端仍为开工基线；仅纳入本片源码/测试/文档，四项IDE变更及全部生成物排除。

## 冻结源码

首轮24份，修正注册断言后最终25份代码/构建文件在第二轮完整 Debug 前冻结；不得以源码变化前的通过结果当作最终证据。

| 路径 | SHA256 |
| --- | --- |
| managed/Ncma.Scene.Rendering.Tests/Program.cs | 84AE7CDF63F3D402FF6F7F9BE3598BDEB76C3663EEC88717988FE4A09ACC071C |
| CMakeLists.txt | 880180CC73CE3ED5F62B7BFA8A64885B4743860D0843D0AF5106BE8B67BDA112 |
| managed/Ncma.Application/ProjectContext.cs | 06F52FC12331BB83BD4738AA70EEC683C2042879EB316D85A89E812084DD9FD3 |
| managed/Ncma.Assets/PostProcessLibraryReference.cs | 2E4107D6B889B82B5F44C6C6A3E7A68C2B60D7495B04B1FFAB31DFD0C182391B |
| managed/Ncma.Editor.App/EditorSceneEnvironment.cs | 61D5E67DCE0C7D21967B51EA073FD7459E57E0E34826AB7A9A5DB420C0B8FA2C |
| managed/Ncma.Editor.App/EditorScenePostProcess.cs | 5D98EB438684B8076724DCB407061FBC9B65D00B09DEB66D9A7DDF2672C87BBE |
| managed/Ncma.Editor.App/Program.cs | BAB7383BD4575DB7F2EBA22C777B2F4404E164B70BCA88622CD98AA46B5676AC |
| managed/Ncma.Player.App/PlayerPresentation.cs | EC261110645480F47262ED954899A60C2CD975C6DDBE4E5598820C4B79F34D9E |
| managed/Ncma.Player.App/PlayerRunner.cs | A9B32BC81B76A145670CE69C73A3720B2B5A9B7936065C21D4DBD865557413F3 |
| managed/Ncma.Rendering.Scene/PostProcessModules.cs | 80EDE76FE9E9BE35EED277C3F8A101121B7DE3B5BDECCA7B4B74355CE3BFC5A8 |
| managed/Ncma.Rendering.Scene/PostProcessRuntime.cs | BD7F86A5093301C240499AEDDC718CC0A55244A9A195783DF02783779B47F12F |
| managed/Ncma.Rendering.Scene/ScenePostProcessPreflight.cs | AEC8AF55081DFFFF44AB9521BE4900DD626077FA9B0683324C44806B8BC9238A |
| managed/Ncma.Rendering.Scene/SceneRenderEnvironment.cs | 88ECA69B9DAA460907B62077649A1EDE12EF9B0026C1E2B79F84BB533E8726C0 |
| managed/Ncma.Rendering.Scene/SceneRenderPostProcess.cs | 55A44E42F5C47E55E9D7E038BDA1539628BAD08A6C17A6DD92EAA7D41C7DFCD7 |
| managed/Ncma.Rendering.Scene/SceneRenderSession.cs | 511EC672E5FE016FF990F29FC9A6B67FF5560F2E3339E7D91055E65797B2DCED |
| managed/Ncma.Rendering.Tests/PostHostTests.cs | 0278A2683007C77D3519046C0C1C0D6FE16F3CF3390F049E947560A3A9CFC8BA |
| managed/Ncma.Rendering.Tests/PostModuleTests.cs | 4294DE5F583B85A8B649B24B481CE311D9CFAA4F9CF0475DFE2585B4785AE680 |
| managed/Ncma.Rendering.Tests/PostSceneTests.cs | E5FF7B39B18C02ED2021133494A07D29EB0BB56E11F08B834DE1E56A77D05014 |
| managed/Ncma.Rendering.Tests/Program.cs | 82C46DF8C43747BF6F176362ACD9867F9234121F1CE0753A96E87BCF51D36EE2 |
| managed/Ncma.Rendering/PostProcessComposition.cs | 69091D3B977E46901DE74A751215D0A41DA88DAB4AB8505E7F7F24CFE24313FA |
| managed/Ncma.Rendering/RuntimePostProcessLibrary.cs | 85EA2B34BCCEEA377988CA1D2B07774BDE16AD3D352300B14C2E90F2BBA69882 |
| managed/Ncma.Scene.Rendering/PostProcessData.cs | DB344F9A6D6E7906C33B37EAB7754C60946608243FB02C07086D72453D7845CE |
| managed/Ncma.Scene.Rendering/RenderComponents.cs | D475F9708D762FD8C013D9B815687079CDB298E79FAA63C15EE719044463BB1E |
| managed/Ncma.Scene.Rendering/ScenePostProcessState.cs | 219920F3A03801B5F3A80DDED6412B18820BEDE2D4ACB9FD137CD97CA0BAD068 |
| managed/Ncma.Scene.Rendering/SceneRenderValidation.cs | 707F567009D775E647DECC6A3853F87CA1D0CEEFDDFEEA8EAC2AB0B5D479676A |

## 保留门禁

C3 专用 Inspector/AI 模块目录/提案前语义检查、D 真实 FBX/IBL/shadow/Bloom/UI 搬移联合与完整管线替换未完成。暖 Submit 零分配不代表正式宿主全帧零分配；离帧准备分配与历史 source-recovery 高分配调查仍保留。人工 UI/DPI/第三方 MCP、用户 HDR-FBX-材质、HDR 解码、目标机/自包含、完整性能/1h 不关闭。原文件替换间歇失败的原因仍未证实，不借本片宣称修复。保留失败、备份、SDK/userdata 和四项 IDE 变更，不提交生成物。DX11only；M7→M8→M9。
