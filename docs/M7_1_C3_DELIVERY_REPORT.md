# M7.1-C3 交付记录

状态：C3 自动候选完成。32项真实定向、原生故障、独立UI apphost与最终顺序无Skip Debug/Release全部通过；Release checked deployment/hash/journal已核验。前置C2 `0cf8e0533da36eeaba42c4b3301b1358f0b38f6b` 在开始前已核对远端main。见[方案](M7_1_C3_IMPLEMENTATION_PLAN.md)、[契约](M7_1_C3_RUNTIME_CONTRACT.md)。

## 功能与定向验证

- 独立 query11/API1 和 Flat2D 同目录默认/用户 VS/PS，真实严格编译/反射、固定布局/资源/输出；现有query1–10不变，没有3D强制阶段。
- 官方 VS/PS 与用户像素通道交换、顶点8像素位移均实际绘制；独立 scalar straight-alpha/纹理/顺序/clip/圆角内外采样最大误差1色阶，全图用户通道比较通过。截图保存在 `out/verification/m7-1-c3-target-second/ui-default.png` 和 `ui-user.png`，已目视检查默认图。
- 32项最终定向通过，日志 `out/m7-1-c3-target-second.log`。首轮30项通过，增加真实用户VS两项后再次通过；本片无测试失败或新代码编译warnings/errors。native first/second、managed first/second/third和原始日志均保留。
- 实际签名/输出/纹理slot/未使用末成员/vertex offset/stage、revoke/nonreentry/active/owner/foreign 生命周期拒绝；失败保留旧metadata/generation及像素。原生候选创建/替换诊断异常、drain timeout、短输出/错ABI/错stage原子拒绝，无隐式replacement。
- presentation lease阻止替换；替换保留旧缓存target像素，显式递增content revision后刷新。8次更换不重建图片/list/target、不新增上传。1024次已热身静态检查零托管线程分配、零新增draw/upload；这是有界样本，不是全产品性能验收。
- `out/m7-1-c3-closure-first.log` 两项原生/示例定向通过。示例在启动可信准备后关闭批准，原source-layout smoke继续执行，另跑16文件source-free apphost：4个原生DLL和共享托管闭包，API0/0，未部署3D专用插件或Editor。共享Rendering/Runtime仍有通用代码，不宣称DLL已完全裁剪3D。字体只读本机系统文件，无分发。

## 最终完整门禁

冻结12份实现/测试后，顺序执行完整无Skip Build.bat；未再修改实现/测试。

| 轮次 | 原生 CTest | 托管 CTest | Python |
| --- | --- | --- | --- |
| Debug first | 12/12，4.30s | 22/22，328.59s | 43/43，1.142s |
| Release first | 12/12，2.40s | 22/22，294.57s | 43/43，.895s |

日志：`out/m7-1-c3-full-debug-first.log`、`out/m7-1-c3-full-release-first.log`。两轮A70/B41/C1 24/C2 40/C3 32均通过，API0/0；Animation115/115、Editor123/123、Player56及所有原M6 FBX/NCA/Jolt/Notify/skin-shadow/Editor0-1-8-32/独立Player/MCP断言保留通过。早期A/B/C1/C2输出中的pending字段是各自切片边界，不代表本次组合未执行。

新格式与移除格式拒绝、managed/native smoke、Python inspect、三轮保留profile、前后audit均通过。最终post-audit：

- Debug：`out/verification/m2-8/Debug/90d00d2bf2984763a4d679c5d1890a5c/audit.json`。
- Release：`out/verification/m2-8/Release/c4a9386364884a8ea71250b17110a3dc/audit.json`。

均audit_passed=true/h8_accepted=false；人工门禁没有被自动关闭。

独立UI部署闭包结果（16文件逐一copy hash和运行后不变核验）：

- Debug：`out/verification/m7-1-c3/Debug/28f72ab6195441948bde2931e57cf825/result.json`。
- Release：`out/verification/m7-1-c3/Release/756d3f8302c741e8b839f213b3411b30/result.json`。

最终Editor两次checked部署均manifest101项/实际104文件/Complete journal；Debug generation `68e90eeda7e24f8d84d070a775ad7aa6` 备份C2 Release，Release generation `792b0b6404144a42b3f62de8da99f77b` 备份本轮Debug。两份backup104文件均经过非reparse路径/文件枚举，未删除任何旧日志或备份。

| Release文件 | SHA-256 |
| --- | --- |
| `out/bin/NcmaEngine.exe` | `DB54C125BB188FB738AC67312186AE65F0163A86C631BC17EE6D102D8D30806E` |
| `out/bin/plugins/NcmaRenderer.dll` | `62BF119D2E7127A3BEFC6A2E4024C8EACAF29C6594DC8DA184BBBD6F10D07F3B` |
| `out/bin/Ncma.Rendering.dll` | `E430CB8FFE19EAB439B19B603687C562DED2C40E14146A60C9B381656514BFDC` |

## 冻结源码与提交范围

最终部署后再次核对以下12份源文件一致。之后仅更新文档/持续上下文。

| 文件 | SHA-256 |
| --- | --- |
| `CMakeLists.txt` | `6F02FC8E0E3E431B7BAFAEF60EAAC14A8146046DA7F3734A19D8DD7442AA0F10` |
| `engine/source/plugins/contracts/NcmaUiShaders.h` | `24DAEC5DE8C89EA14108D7BE589E8D14BAF33726CA48FB247B8642FD5460704E` |
| `engine/source/plugins/renderer/UiShadersServices.inl` | `E2729657A9F5CD7011E705294824F08A7472E652293EBEB9F249D2E651647C66` |
| `engine/source/plugins/renderer/RendererPlugin.cpp` | `C12FF23F0546D8D083CD9262C33E1F90307835E9F73B3B66F4B46CF048B3EFEC` |
| `engine/source/plugins/renderer/UiKernel.cpp` | `AFF7A4E5B47C25FAEAF5CFCBA3C4971458485C6A298ADA424C623C9AA24B51F9` |
| `engine/source/plugins/renderer/UiKernel.h` | `F31A320B9107A14B5CDE4A1656DB53B858C94C91A830DB57BD0B6BD8FBF1E4FD` |
| `managed/Ncma.Rendering/RegisteredUiShaders.cs` | `6320BDD780F73959E7036C23D053704479C12009217563DB5B68BE3FC471F30B` |
| `managed/Ncma.Rendering.Tests/RegisteredUiTests.cs` | `5E8069F25E5941C863A6CD8E25761F339B98A56D42956BDE4A2BE4B988EAA013` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `B806A29D7B69EC7292A3982BD426CBC642257CE981EAB0E8F4B7047F8FE7AB8B` |
| `managed/Ncma.Ui.Sample/Program.cs` | `ECE0519C80A0529D89B8783E60593AF530AF911A4F557F881FF2A9F95CE591AF` |
| `tests/plugins/RendererNativeTests.cpp` | `CF1E390D8C8CCEBFDED2556E99587AD9F89DA6139F9004DCA8AD4391E79AC47E` |
| `scripts/Test-UiSamplePackage.ps1` | `7C363B7C8D414B5059B620EF952E24DF30E991522426C805CB5076B5AF55DC2A` |

提交仅包含C3源码、测试、方案/契约/交付和路线图/AGENTS，排除生成部署、所有日志、备份及4份无关IDE改动。提交推送以本记录所在Git提交和独立remote main核验为准，确认一致后才进入C4。

C4正式Editor/Player默认切换、持久化shader运行包及整个C仍未完成。未增加Agent编译/代码执行/live GPU权限或推理服务。Vulkan/OpenGL实际后端下一版本；人工/用户FBX/材料/目标/自包含/完整性能/1小时门禁保持开放。顺序仍为M7→M8→M9。
