# M7.3-C2-C 交付记录

2026-10-13。开工基线与提交前远端 main `09476c8e809b29119319cb7c8c0b615c725208f2` 一致。本片完成正式 Editor / Player 环境光接入的自动候选，C2 自动链闭合；C3 默认环境部署/面板与整个 M7.3 仍待完成。见[运行合同](M7_3_C2C_RUNTIME_CONTRACT.md)。

## 实现与边界

C# 共享 SceneEnvironmentPreflight 在 gameplay 前核对确切资产/配置与 profile3 双变体/skin，并进行实际 GPU 环境绑定；租约保留到正式 SceneRenderSession 接手。Player 在固定更新后显式准备环境和视图；Editor 启动/Play/Reload 使用同一准备边界，显式 NCP 选择在资产刷新时保留。命令沿用唯一历史；失败保留资源并清除视口与 picking，新的已提交状态允许恢复。无 native ABI/shader 算法变更。

本片替换 C1 enabled pending guards，但保留更强的缺 shader/错误实际输出/错误资源代次的 pre-gameplay 拒绝。没有默认环境生产部署/HDR解码/新Agent GPU权限/推理/Python gameplay。GpuValidated=false；内部参考编译保留，不是代码安全沙箱。Editor 的候选重建/版本扫描仍有成本，未宣称动态零分配。

## 测试与失败记录

新增43项正式宿主检查：独立软件 IBL oracle 最大像素误差0、DX11 API0/0；自定义v2包、NCP、启动拒绝、动态Off/on、未准备代次失败、源文档不变、Agent默认拒绝、Undo/Redo、Edit/Play/Stop/Reload、失败均匀清屏与恢复、实际ASCII/Binary FBX-NCA动画、独立Editor与搬移Player进程、空场景/Headless。原B3 353项、C2-B87项及M6/M7/MCP/格式/关停断言均保留。

前期修复了可空Physics提示、测试命名空间、Present后捕获时机、测试误把深色背景当纯黑、Player不支持的测试启动参数和缺少glfw依赖。第一次完整Debug因旧资产夹具未选择相机而先触发配置拒绝；补齐有效相机，保持缺插件拒绝断言。独立复测又发现Editor缺根目录的正确错误码为2，改为精确匹配原诊断；未放宽生产校验。场景108/108复测通过后重新冻结13份源码。所有失败日志保留，首次完整Debug未部署。

| 最终无Skip Build.bat | Native CTest | Managed CTest | Python |
| --- | --- | --- | --- |
| Debug第二轮 | 12/12，3.70s | 22/22，430.61s | 43/43，1.091s |
| Release第一轮 | 12/12，1.96s | 22/22，314.50s | 43/43，0.921s |

Debug/Release正式宿主均43项、最大误差0。Rendering171.77s/116.28s，Editor77.78s/72.13s，Player68.26s/66.23s。原smokes、新格式/移除格式拒绝、inspect、三轮profile、audits、checked deployment恢复均通过。所有新编译零warning；最终双配置期间13份源码SHA不变。

日志：`out/m7-3-c2c-full-debug-first.log`（失败）、`out/m7-3-c2c-full-debug-second.log`（最终）、`out/m7-3-c2c-full-release-first.log`（最终）；定向日志 `out/m7-3-c2c-target-first.log` 至 sixth，`out/m7-3-c2c-scene-target*.log` 保留。正式图像位于 `out/verification/m2/render-{Debug,Release}/environment-hosts/<generation>/`。

## 部署审计

Debug generation `ba6aa3bf87e341e287b8c03d7ddb39d6`，Release generation `6c5690e5d9b34d96b79719e7938ef805`。各107 manifest / 110 actual，Complete journal；两份110文件备份逐路径/size/SHA核验，Release后复核Debug备份。原五个shader包+defaults.json六份SHA完全不变，Release原生Renderer不变。实际 out/bin/NcmaEngine.exe --validate-package 返回0。

- NcmaEngine.exe：`3DB30EE7B7E0760224ABC1AB0BD9AC7B1A2DE58EE66D2BD6882DBD8F9A61AC70`
- NcmaEngine.dll：`4CABC88698468ED75BC6CC0F12EB55F85360F34942221C6E15D83314DBE4DBA3`
- NcmaRenderer.dll：`C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D`

最终post-deploy audit：Debug `6dfd217607864cba80f12b98422da895`；Release `9348078b318f40c5bd167a93a7d544f6`。audit_passed=true，h8_accepted=false；人工UI/DPI/MCP、用户HDR-FBX-material、目标机/自包含、完整性能和1h门禁仍开放。

| 冻结源码 | SHA256 |
| --- | --- |
| managed/Ncma.Editor.App/Ncma.Editor.App.csproj | 77DCBF62CEAB4D4C1ED9764255C638C4DFC9E4DB458232FBC2D0C1E5B19F8E48 |
| managed/Ncma.Editor.App/Program.cs | B81B75A2D2AB602E38685C9325AC8443D2159B82A6F5E3B7A1541094F4053DC0 |
| managed/Ncma.Editor.Services.Tests/Program.cs | 078333B0C340EFC40182946BC9B39D7CE0D255508B8450DE04001637B3E77EBD |
| managed/Ncma.Editor.Services/EditorSessionOwner.cs | 9925B07C3C6B2A9FFC872938D3BCB673A63FA4F2478E8F445CBB07F80151B27C |
| managed/Ncma.Player.App/Ncma.Player.App.csproj | 5717468BEC3AA410C1AEFCB98FAC18B0F446F9F70FF7BC26569BD11818D744B5 |
| managed/Ncma.Player.App/PlayerPresentation.cs | C31F5463ADEEF4F8BC01FABF5A6FDE39EF49D0E569CDDFCC9D4F9802BE4CE953 |
| managed/Ncma.Player.App/PlayerRunner.cs | 2050F571F02C53CB750DCBBD4174330AF292408CC7AB27366EF7444C192A2173 |
| managed/Ncma.Rendering.Tests/Ncma.Rendering.Tests.csproj | 78C3B233F45693D36268B04BB53D3743650FB25F8A41D49F3778DF492773634E |
| managed/Ncma.Rendering.Tests/Program.cs | 604A6D7B2D1EE1C1D9380D664EE296D912E2355498A83EECA0BBBEC45F59C9FD |
| managed/Ncma.Scene.Rendering.Tests/EnvironmentAssetTests.cs | F70BDFB0BB62D6B45E107BAED0520185679B56CF4C2B0222AB28FC79945837D6 |
| managed/Ncma.Editor.App/EditorSceneEnvironment.cs | 53728FCC1C7B8BDE7E8FE8E94CE12E0F5DF7C4D986FF31D62D0546B671DFE023 |
| managed/Ncma.Rendering.Scene/SceneEnvironmentPreflight.cs | F285848957AE78659836F59496FB2EA8B0A4B734A696C3BB7E50362C99741CA2 |
| managed/Ncma.Rendering.Tests/EnvironmentHostTests.cs | 5AA836056002A73E8F383184D9532316C51CC25190AC03AC14AF827AD3CA14DF |

提交只包含本片源码/测试/文档，排除生成部署、备份与4项IDE/.user本地改动。提交推送并核对远端后进入C3-A默认环境/Shader部署；DX11only，Vulkan/OpenGL下一版本，M7 → M8 → M9。
