# M7.3-C3-A 交付记录

2026-10-14。C2-C 已提交推送 `9ac83b213f0cad8131d3907925ece85ee0faad09`，开工前 local/remote main 一致。本片完成默认环境构建/只读加载/产品部署自动候选。C3-B 面板与可撤销预设发布、C3-C 正式产品联合验收及整个M7.3仍待完成。见[合同](M7_3_C3A_RUNTIME_CONTRACT.md)、[C3分片方案](M7_3_C3_IMPLEMENTATION_PLAN.md)。

## 实现

构建工具输出 Editor 9个shader+index、DX11 Player 8个shader+index，显式v2、两个3D profile双阴影/skin闭包；原五个shader包字节保持，默认selection index明确改变。UI仍v1单包+index，Null不带3D文件。两个3D产品各加入47,232字节的合成neutral NCE和独立严格索引；没有修改用户场景或提供新的默认挂组件行为。

EnvironmentPresetIndex闭合8字段、固定身份/代次/路径/合成标记；EnvironmentPresetLibrary启动期只读、owner-thread、共享Windows read pin、外部索引hash或manifest独立size/hash验证、包内recipe与数值校验；异常/取消释放全部文件租约。没有Renderer/World依赖、原生ABI或shader算法修改。元数据/哈希不是Cook来源证明；GpuValidated=false。Cooker不进入运行时产品，无HDR/EXR解码或新Agent执行权限。

## 自动验证

83项：实际Editor/Player/UI构建工具，跨产品与Debug/Release完全相同的NCE/索引，旧五shader冻结哈希，UI/Null无环境闭包，字段/版本/大小/哈希/重复/未知字段/缺文件/实际腐坏/伪recipe拒绝，manifest独立大小/哈希/重复文件，写锁/父目录rename/owner/取消/12次重开/失败释放，真实hardlink/junction，产品manifest和实际GPU绑定，默认环境共享场景独立软件IBL像素最大误差0/API0/0。

第二轮定向fixture调用mklink时传入含正斜杠的路径，Windows将其识别为无效参数；改用规范完整路径，保留失败日志和夹具。最终定向83项通过后冻结7份源码，再顺序运行完整无Skip Debug/Release。原M6 Player搬移测试升级精确shader数量断言为8+index，原场景/数值/动画/权限/无Editor依赖/关停断言全部保留。

| 最终 Build.bat | Native CTest | Managed CTest | Python |
| --- | --- | --- | --- |
| Debug第一轮 | 12/12，3.74s | 22/22，438.97s | 43/43，1.031s |
| Release第一轮 | 12/12，1.93s | 22/22，318.63s | 43/43，0.854s |

Rendering182.39s/120.73s、Editor76.81s/72.15s、Player67.32s/66.18s。原B3 353项、C2-B87项、C2-C43项、M6/M7/FBX-NCA/Jolt/MCP/格式/managed-native smokes、inspect、三轮profile、部署恢复全部通过；新编译零warning。独立UI原18文件验收保留。完整运行期间7份冻结SHA一致。

日志：`out/m7-3-c3a-target-first.log` 到 fourth（second为失败）、`out/m7-3-c3a-stage-debug.log`（SkipTests仅生成候选，未部署）、`out/m7-3-c3a-full-debug-first.log`、`out/m7-3-c3a-full-release-first.log`。图像/测量位于 `out/verification/m2/render-{Debug,Release}/environment-presets/<generation>/`。

## 部署

最终Debug `c9163cb50ba343e68915d958b14d730d`、Release `706538414a7a4193a715d9a2fc5906e9`：113 manifest / 116实际文件，Complete journal；Debug前110文件备份与Release前116文件备份逐路径/size/SHA核验，Release后再次核对Debug备份。实际 out/bin/NcmaEngine.exe --validate-package 返回0。原生Renderer和旧五shader包SHA不变；新v2 index在manifest中受检。

- NCE（Debug/Release一致）：`96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665`
- NcmaEngine.exe：`A381D69541378C56B9204F3D83E938D9694F40A1D6EFB70E0390CD921D3E268B`
- NcmaEngine.dll：`3143F52856C757A9AB20C26718A999E5C86080D7E702B41275275CFBA0552435`
- NcmaRenderer.dll：`C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D`

最终post-deploy audit：Debug `827c05221a694e699e93c24fea52e99a`，Release `5ac0321914294ff391d83f836f03a6d6`；audit_passed=true，h8_accepted=false。人工UI/DPI/MCP、真实用户HDR-FBX-material、目标机/自包含、完整性能/1h继续开放。

| 冻结文件 | SHA256 |
| --- | --- |
| managed/Ncma.Player.Tests/RuntimePackageChecks.cs | 48522A0A481379FEC8A259497F30777784B6B75196B907F395C73B142775FD26 |
| managed/Ncma.Rendering.Tests/M6FinalAcceptanceTests.cs | 7610A2AB2706B5A83A2D6185D3571DB21EB4DD897BE39BC9B912C33B686BF2C3 |
| managed/Ncma.Rendering.Tests/Program.cs | 5104B167EF91F315565A1A046F01B331C81C178F9B557D282432ADFC587ABA08 |
| managed/Ncma.Shader.Cook/Program.cs | D415B417CCDB6B57D353665DB8B22A65F7133F0007978320D1FBBC306E31217C |
| scripts/Package-M2_7.ps1 | F0FF7EC75893E78EA6A928BC7EC1274142652B759D4B94D6F2B7AAF7E998429E |
| managed/Ncma.Assets/EnvironmentPresetLibrary.cs | F1DBA70D571E4EE9EAF9C999834376223DAF45CE86FFE68AF1F6CE76DCA36733 |
| managed/Ncma.Rendering.Tests/EnvironmentPresetTests.cs | C3A9B244B7A3E91589356F30AC1048DD827F47B8D8A86D6AAFCB3409B9FD0CDE |

提交排除生成包/日志/备份及4项用户IDE/.user设置。推送核对后进入C3-B；默认预设的选择、可逆文件发布和UI/AI共享事务尚未实现，不将部署资源描述为完整制作流程。仅DX11，Vulkan/OpenGL下一版本；M7 → M8 → M9。
