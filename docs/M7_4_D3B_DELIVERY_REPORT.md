# M7.4-D3-B 交付记录

基线：本地/远端 main `253ee9946087525e8ca7a56a126bf170688dcb89`；[方案](M7_4_D3B_IMPLEMENTATION_PLAN.md)、[合同](M7_4_D3B_RUNTIME_CONTRACT.md)。

## 实现

- 新增显式项目 UiPackage 选择和源文件无关 NCU1 v1：严格版本/项目/文档 UUID/代次/外部 SHA，恰好匹配的字体与图片依赖、有界内存、只读文件 pin。
- 正式 Player 启动前准备被动 HUD（文字、图片、矩形等），随后在场景显示域合成；静态帧复用列表，resize 显式准备，失败关闭不卸载仍持有资源的插件。
- Text 惰性加载；图形 Player 增加可选 Text DLL 和既有 Flat2D Shader，原 8 个 3D 程序保留；Null 无原生插件且只验证 CPU UI 闭包。纯 UI 产品仍不携带 3D 资源。
- 不新增 World/UI 场景父子结构、native ABI、Shader 算法、Python gameplay、Agent 代码执行/推理权限，也不增加旧格式兼容。

## 定向证据及失败记录

1. 首次及第二次 source 定向失败：测试夹具在同一帧提交背景 UI 和 HUD 两份 UI 列表，触发现有 `UI frame already submitted`。随后的关闭错误仍保留日志；修正夹具为纯 UI 单次提交，不放宽 native 契约。日志 `out/m7-4-d3b-target-first.log`、`target-second.log`。
2. source 127 项通过后，首轮完整 Debug 的原生 12、原有 30 项托管组通过，但新增搬移 SceneUiHosts 失败。真实原因是旧图形 Player Cook 有 8 个 3D 变体却没有 Flat2D，源模式内部默认库掩盖了缺少发布资源。Player 启动在 tick0/session 空时拒绝且关闭完整，该轮没有正式部署。日志 `out/m7-4-d3b-full-debug-first.log`、现场 `out/verification/m2/scene-ui-host-Debug/61bdfa95de684ac2afb9f15407846b66` 保留。观察到失败后修复 source，因此首轮也不属于最终冻结证据。
3. 补齐 Player Flat2D、显式选择缺失 UI 时启动前拒绝；原 8 个场景变体完整保留，旧的精确总数检查更新为 9 并要求 Flat2D。独立 UI 仍只有 1 程序和 index。
4. 完整定向 336 项通过：`out/m7-4-d3b-target-full-first.log`，证据 `out/verification/m2/scene-ui-host-targeted/3c600020b9c54762988fdb23af4ee597`。包括格式字段/typed closure/hash/owner/只读 pin/硬链接/字形和 RGBA 实际像素、resize/关闭/API0/0、正式 source 与严格搬移 Player/Null、实际复制出的 NcmaPlayer.exe、无作者 UI/字体/图片/model 源、坏包/坏字体/缺 Shader 启动前拒绝、custom pipeline 显示域 HUD。测试 probe 只从搬移产品解析 Ncma 程序集并核对 manifest SHA，无源码程序集回退。

静态 32 帧无字体重新整形、UI 上传或列表重建；局部测量包含 Stopwatch 分配，不声称完整宿主零分配或完整性能验收。CPU-only Null 不执行字体解析/图形验证。

第二轮完整 Debug 发现历史产品闭包测试中另外三处仍将 Player 固定为8程序/9文件（EnvironmentPreset 的已部署产品检查、RuntimePackageChecks 文件总数、M6FinalAcceptance 的搬移程序/文件总数）。更新为原8个场景程序+既有Flat2D共9程序/10文件，同时明确要求Flat2D存在；没有删除场景变体、放宽hash/图像/性能断言。该轮失败记录保留，不作为最终通过证据。

第三轮完整 Debug 的12原生/31托管/Python43/三轮profile通过，但发布前 M2.8 审计旧白名单拒绝 Player 的 Text 模块；未部署。修正为仅图形 Player 可选的 exact ABI1/路径/文件hash/惰性 Text，继续拒绝 Null 的 Text（包括无metadata DLL）、错误ABI/路径/lazy、重复声明及Editor/Gui/Import资源。新增审计反向测试，专项13项及实际当前包只读审计通过；`h8_accepted`/原人工门禁保持false。最终复核同时将新 HUD 图形 Player 的 tick 检查对齐现有 AdvanceFrame 语义（至少目标tick，Null仍精确tick），不改变运行时或旧测试断言。

Release 首轮再次遇到历史 `EnvironmentProductTests:95 → SceneDocumentFiles.Save:60 → File.Replace` IOException（无法删除要被替换的文件），原有渲染组失败，HUD组通过；未部署。保留 `out/m7-4-d3b-full-release-first.log` 和 `out/verification/m2/render-Release/environment-products/5662eda58bf844d8aa6e296ddf8dca86`。失败的 `runtime-static/start.ncmascene` 是2995字节/Archive/v1/6对象，SHA `590BA0D3555A44192B87FA513D3765A375600481328C42C0C5FA14FBE0A6B532`，前两份 bad-identity 报告已生成，第三次保存失败。事后独占只读打开成功、Editor/Player进程数0，但这不能证明失败瞬间没有文件占用；根因未确认。没有修改原子保存实现、添加删除/非原子回退、放宽原断言或覆盖该现场。使用同一冻结源码和新的测试目录重跑完整Release；即使复测通过也不宣称修复了此间歇问题。

## 最终回归与部署

Release 第二轮日志 `out/m7-4-d3b-full-release-second.log` 在用户中断时停止于 NcmaAssetImportTests；已完成的原有渲染组、HUD、Editor、Player 组通过，但未完成整轮，不能作为最终通过证据。后续确认统一执行句柄不存在、系统无对应 Build/CTest/dotnet 进程，部署 journal 仍是 Debug 第四轮，才以同一21份冻结源码启动完整 Release 第三轮。没有仅因观察超时重复启动构建。

最终使用相同21份冻结源码，顺序完整无Skip `Build.bat -Configuration Debug` 第四轮与 `Build.bat -Configuration Release` 第三轮通过。日志 `out/m7-4-d3b-full-debug-fourth.log`、`out/m7-4-d3b-full-release-third.log`。两配置各12原生/31托管测试组/Python44、原M6/M7、严格新格式与移除格式拒绝、MCP、managed/native smokes、inspect、3轮保留profile、只读审计及部署恢复全部通过；编译0警告/0错误。审计 `audit_passed=true` 不等于 `h8_accepted`，后者和人工门禁仍为false。

| 最终证据 | Debug 第四轮 | Release 第三轮 |
| --- | --- | --- |
| 综合渲染组 | 225.71秒 | 163.87秒 |
| D3-B HUD检查 | 336项 / 9.41秒 | 336项 / 9.66秒 |
| D3-A 原宿主检查 | 609项（新增清单条目计入） | 609项 / 29.82秒 |
| D2 原联合图像 | 582次 / 120图 / 最大误差1 / API0/0 | 582次 / 120图 / 最大误差1 / API0/0 |
| HUD证据目录 | `out/verification/m2/scene-ui-host-Debug/7601a2284ca14c9cb5cb1851c2bce5c9` | `out/verification/m2/scene-ui-host-Release/cdf82f2c24174711bbed4401f76bd2b1` |
| 产品代次 | `064ad59aa08540819de386d80d754b12` | `6afedf9083e44008a0726ff976295120` |
| Complete部署journal | `e6761e1036a148f6b6ee0aa3e7b6eff0` | `a290a77b582c49cd812e7e3aeb90dc70` |
| 备份核对 | 142文件 | 143文件（含允许的工作区/日志增量） |

两配置 Editor139 / Player-DX11108 / Null56 清单条目逐项大小/SHA匹配；Editor与DX11 Player各26后处理文件及9程序/10文件Shader闭包，Null无原生插件/post/Shader。默认纯UI的18文件独立产品原回归保留。Release正式目录139manifest/143actual，额外为manifest和允许的workspace/日志文件；Complete journal的143份旧文件备份逐项大小/SHA匹配。源码21份SHA重查一致；原生Renderer、10份旧Editor Shader与26份post资源分别与D3-A同配置实际包一致，post索引仍为 `97330B1CA92BB54CE35B2C5F9E4C4BC7A97B30AF9F79C6436D292FE94A4A17DE`。

实际 `out/bin/NcmaEngine.exe --validate-package` 返回0。最终EXE SHA256 `2BD5CD7D15C201B3889A589ADDD715468702DE19DA9EC2B14B51106EFACEFB2F`，NcmaEngine.dll SHA256 `4358C922C92706EF45EE2E5DD38CB0576DA18466AC7C50CD23A154CB6228F688`。Release审计前/后分别为 `out/verification/m2-8/Release/7950a88ac82343198a795147df379cd4/audit.json`、`out/verification/m2-8/Release/63c10380f8154a50a504d0dc5c6f809a/audit.json`，保留全部pending gates。

HUD暖32帧Debug/Release局部CPU总时长3.9742/4.2539ms，均40字节测量器分配、39344字节UI resident，无重新整形/上传/列表重建。包含Stopwatch且不是完整宿主性能或跨机器保证。Release原宿主证据 `out/verification/m2/pipeline-host-Release/9d7eaf9a24c8403fa22a2fc0994cd0f1`、联合图像 `out/verification/m2/joint-post-Release/77f86b75218e42d8ace11a82e05895a9`；原宿主高分配调查未关闭。

## 冻结源码 SHA256

| 文件 | SHA256 |
| --- | --- |
| CMakeLists.txt | E3D5CD71D554AEDCEF605FEEDFFD11B3DF846AB2B27B644F2E20D7E3BD070C84 |
| managed/Ncma.Application/ProjectContext.cs | 81F8C5450B7E8FA40221EE89F1C007393D9FD054279ED4007AB292F813F3C6C3 |
| managed/Ncma.Assets/Ncma.Assets.csproj | 059E6667F4CCF4C5A0285A17FDA58BFB672D92FDB167E9F130908C3CAAFB8418 |
| managed/Ncma.Assets/UiPackageReference.cs | 51C75182D743FB76EBB8B4CE29CD040F2C0229A250B39317B5358F2481514CC5 |
| managed/Ncma.Editor.App/Program.cs | ABFE66FAB5B5B1E8B222CFE344147E0AD4224EC882E64F5DFDF0AF489886A53D |
| managed/Ncma.Player.App/Ncma.Player.App.csproj | 64D39252A78E40FCFFD807326F41A8DC808687885A6596AEF212C6F0051EE289 |
| managed/Ncma.Player.App/PlayerPresentation.cs | 85625E3EF4A00A902BCE30AFDC93170669EBE2A6088EA063C387BA13E73F7087 |
| managed/Ncma.Player.App/PlayerRunner.cs | D61FA3D06B223BE698D28B1B0D6987BE13DF9E71648A6534277B5C97290A7491 |
| managed/Ncma.Player.Tests/RuntimePackageChecks.cs | AC88707C3D18F0F2279E85254A2117365EFFA0602A4FD8CAB6BCAF3DD766F366 |
| managed/Ncma.Rendering.Tests/EnvironmentPresetTests.cs | C60BD4928EBB6667449F63AD250557BB398643B7824019169C070E64C4153C83 |
| managed/Ncma.Rendering.Tests/EnvironmentProductBootstrap.cs | C7686B9F2770D05DEEF4CEF511C978424E94E3DBBBD3EA721899039585170D40 |
| managed/Ncma.Rendering.Tests/M6FinalAcceptanceTests.cs | F2689109E44B16A12BD984654E44423DD5EAED3ADFEBE3528C30852195CFA29B |
| managed/Ncma.Rendering.Tests/Program.cs | 6F11424DC88E16FE812DF5D615F75059F8B48AE49B21C04769919683EF1B90B2 |
| managed/Ncma.Rendering.Tests/SceneUiHostProbe.cs | E347AC1EE219F1D65E24E6E47FDC7182ACB6CF91CADB126EFD753CE7777D8ADE |
| managed/Ncma.Rendering.Tests/SceneUiHostTests.cs | FC21F2510EE5FE73F1438C53E03804014991D88DD9B22A6B7DF597551DE49421 |
| managed/Ncma.Shader.Cook/Program.cs | B8F637832F360DB935E9D0FECEFFDE89F1CD64E6F836C4573983CFE77DAA8984 |
| managed/Ncma.Ui.Rendering/RuntimeUiPackage.cs | B197062542DB860A4F58B5003F6012687DFEB78B31CDBEF183179DA40DB0B6D8 |
| managed/Ncma.Ui.Rendering/SceneUiPresentation.cs | C3F3F0C1FF1470107EA880B651F35D3E6E8CCCCA48E454FDBA4DA84FFA5BA914 |
| python/src/ncma_tools/m2_audit.py | C5C3A34DF18416A51327663048C0F911C8B1ED1BBD862DD87215A35D09604CFC |
| python/tests/test_m2_audit.py | 75305272EF07BBD1A959D1416E023EB660A20EAFD000711FABC151C061682E63 |
| scripts/Package-M2_7.ps1 | C9E7DFB7A9EA3EC9A2D9C88C5E19ED98646468E3EC77E50FE21AFB3742EC5BD8 |

## 未关闭

最终冻结清单见上节；Git提交时仅纳入本片代码、测试及文档，不包含生成包、备份与四项无关IDE文件。

本片仅被动 HUD；交互控件/非空 action 明确拒绝。Editor 显式 scene UiPackage 在插件加载前明确拒绝，正式 Editor 场景 HUD、原编辑/Undo/Play/Reload 联动与真实 FBX/IBL/shadow/Bloom/UI 搬移联合属于 D3-C。整个 D3/D/M7.4、人工 UI/DPI/MCP、用户素材/HDR 解码、目标机/自包含、完整性能与 1h 未完成；旧 source-recovery 分配/偶发 empty-cache/File.Replace 调查保留。所有失败现场、备份、SDK、用户数据和四项无关 IDE 修改保留。
