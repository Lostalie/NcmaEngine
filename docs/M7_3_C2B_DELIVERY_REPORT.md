# M7.3-C2-B 交付记录

2026-10-10；开工前 C2-A local/remote main `d8fd39d3a7a7542f0455b908ce5453cb9d25f991` 一致。本片关闭共享 SceneRenderSession 的自动候选，正式宿主 C2-C、默认部署/面板 C3 和整个 M7.3 仍待完成。见[合同](M7_3_C2B_RUNTIME_CONTRACT.md)、[分片方案](M7_3_C2_IMPLEMENTATION_PLAN.md)。

## 实现

C# 共享服务以确切 World/已提交配置/PreparedSceneAssetLease 准备 profile3 双 shadow variants 与同一 skin kernel。显式可信 RefreshEnvironment/PrepareEnvironmentView 在 World.ReadOnly、owner/healthy/off-frame/非 simulation/非重入边界内完成；拒绝 callback 写 World、Submit/Dispose 重入、restore/component 验证窗口、foreign owner 和过期 World。只新增窄内部 World.IsCommittedBoundary 状态读，无 native ABI 或 shader 算法改变。

资源缓存按 UUID/generation/hash 复用环境 GPU；缓存命中仍验证审批，缓存资源禁止原地 Replace，独立 B1 资源 Replace 保持原合同。先完整准备候选、绑定后再交换；标量更新不重传，同代不同 CPU publication 可共享 GPU，显式新代/Off/resize/shadow 切换和旧 profile 提升均已接入。Submit 不读取文件/Cook/安装环境或纹理重传；未显式准备配置/view 时明确拒绝，失败保留旧安装值/绑定，恢复旧 World 值后原图仍可用。

初始 geometry publication 的 CPU lease 可能继续 pin 原环境直到整组场景关闭；零引用 GPU 由显式 Trim 退休。revision 改变时仍有有界对象扫描，不是动态高性能 ECS/零分配承诺。普通绘制常量上传仍存在。本片不接入正式 Editor/Player；C1 enabled pending guards 不变，生产包仍是原五 shader+index，无环境默认部署、HDR/EXR 文件解码、推理/Python gameplay 或新 Agent GPU/编译权限。CPU GpuValidated=false，原内部参考编译器仍存在，不是 shader 代码安全沙箱。

## 测试与修复

新增87项实际检查进入原完整 NcmaRenderingTests：独立软件 IBL 像素误差0；撤销/缓存命中/代次错误/重入/World写/owner/frame/simulation/组件验证/restore/过期identity拒绝；参数刷新/失败保留原图/Off/新代次/resize/shadow变体/关闭；源无关NCP资产与GPU共享；旧profile提升；synthetic与实际ASCII/Binary FBX导入NCA的动画geometry/shadow/IBL共同提交；v2源无关自定义环境文件经过实际准入并在同一个共享服务产出等于官方半强度的整图；API0/0。

- 初次测试编译使用不存在的 Begin，改用现有 SubmitResources；运行资产夹具误用 .ncp/绝对路径，改为正式 .ncpak/相对路径。
- 动画测试发现审批窗口在 skin 资源创建前结束；补齐同一 read-only/审批作用域后通过。
- 自定义文件夹具受现有平台单窗口/单Renderer限制；改为先释放第一组资源/Renderer再在同一窗口顺序验证新Renderer。两个失败日志保留，未改变原平台限制。
- 首轮完整Debug81项通过，但复核发现缓存原地Replace破坏不可变代次；加缓存保护，并将已提交检查覆盖组件验证/restore准备窗口。首轮源码在测试期间改变，因此只保留为记录/备份，不作为最终验收。最终87项定向通过后冻结8份源码，再顺序运行无Skip Debug第二轮/Release第一轮。最终回归期间无源码修改。

| 最终 Build.bat 无Skip | Native CTest | Managed CTest | Python |
| --- | --- | --- | --- |
| Debug第二轮 | 12/12，3.67s | 22/22，415.84s | 43/43，1.107s |
| Release第一轮 | 12/12，1.98s | 22/22，309.23s | 43/43，0.890s |

Debug Rendering162.29s/Editor76.59s/Player67.14s；Release Rendering110.10s/Editor71.98s/Player66.79s。新增共享87项双配置像素误差0/API0/0。64帧静态暖态托管分配0，不Cook/环境纹理重传/资源重建/诊断readback，一帧单个scene submit；Debug10.8902ms/Release9.4969ms，仅有界夹具成本，不是完整性能/FPS承诺。原B3 353项仍像素/积分最大1阶、BRDF最大.0005584955215454102/API0/0；所有原M6/M7/FBX-NCA/Jolt/Editor/Player/MCP/格式/smokes/inspect/三轮profiles/audits/部署恢复保留并通过，编译零warning。人工/用户HDR-FBX-material/目标/自包含/完整性能/1h门禁不关闭。

日志保留：`out/m7-3-c2b-managed-build-first.log` 到 eighth、`out/m7-3-c2b-target-first.log` 到 seventh、`out/m7-3-c2b-full-debug-first.log`、`out/m7-3-c2b-full-debug-second.log`、`out/m7-3-c2b-full-release-first.log`。最终测量：`out/verification/m2/render-{Debug,Release}/environment-render-session/shared-environment-results.json`；实际图像与测试原始产物均保留。

## 部署与审计

首轮Debug `37c1078e50f74ce9aad1c81edf0000bf`；最终Debug `9c91ef31e6e74bf28abc7121cfda3258`；最终Release `ca30a57ec0df425f92acf8e3c4263ae2`。全部107manifest/110实际文件/Complete journal；三份110文件备份逐路径/size/SHA核验，Release后再次复核前两份。原五Shader包+index六份SHA不变；当前实际 `out/bin/NcmaEngine.exe --validate-package` 返回0。

- NcmaEngine.exe：`12474B00695DD0A03CA780A708E34BC04E7FBD31115FA1853ED96B2813FB3213`
- NcmaEngine.dll：`284570CD0B91DEC433415FAED2C1D4458A1366EDD1593D00F586B046CD0FE0AC`
- Ncma.Rendering.dll：`16ED8C401EB9CBA8968AE68C9A55249956E9030401BAFDC4CA9645EF91C31904`
- NcmaRenderer.dll：`C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D`（原生不变）

最终postaudit：Debug `out/verification/m2-8/Debug/f34a809d64d840e1857fefe495e5010c/audit.json`；Release确切 `out/verification/m2-8/Release/98ccdc0bc0144c5f94193bd55aa254ff/audit.json`；audit_passed不是h8_accepted，后者仍false。

冻结8份最终源码：

| Path | SHA256 |
| --- | --- |
| managed/Ncma.Scene.Rendering/SceneEnvironmentState.cs | E24D1FC3078C0BF1664EBB3F774DCABE49652B8A8BDA31C390C23B68AFF3C5CF |
| managed/Ncma.Rendering/EnvironmentGpuResources.cs | 257AA51DF95EE5F88ABB5877EDAC71ACEA827865FE2C96E6C2A5B32BAB25851B |
| managed/Ncma.Rendering/RenderResourceCache.cs | 94B094B6162D05AA82259AD652008574976B15F9BBCEAAF2E9803058BD3D69B5 |
| managed/Ncma.Rendering.Scene/SceneRenderSession.cs | A15B582DF9B5E20A81B44C494FE63E8424F1BE72F28B841D1EB75A9D3BFEA053 |
| managed/Ncma.Rendering.Scene/SceneRenderEnvironment.cs | 1A81B6C53BEA5FD418A556BFB85DAFED1E57ABAB746F567A47F3A16C0E93AF6D |
| managed/Ncma.Rendering.Tests/EnvironmentRenderSessionTests.cs | 7696EF8FBC074E3DDE5142769F68EF5D52BA6455C684FA22977EBFB357211F27 |
| managed/Ncma.Rendering.Tests/Program.cs | 7B96998F54B0694163887B7F12F50345146AD0465F9610E0E006353A3AFD4212 |
| managed/Ncma.Runtime/World.cs | 089C3264C057291E3928E2E105C2EFC4B01A021C801F7E4C8125169DED8C72F7 |

## 后续

文档同步后再次 Python inspect 通过，Python43/43（0.919s），8份冻结源码仍一致；见 `out/m7-3-c2b-postdocs-inspect.log` 和 `out/m7-3-c2b-postdocs-python.log`。

下一片 C2-C 正式宿主 pregameplay 全闭包/动态命令与Undo-Play-Reload/联合验收；只有实现后才移除 C1 pending guards，C3 默认合成环境部署/面板另行推进。仅DX11，Vulkan/OpenGL下一版本，M7→M8→M9。提交排除生成部署/备份和4项IDE/.user本地变化；保留SDK/userdata/失败记录。
