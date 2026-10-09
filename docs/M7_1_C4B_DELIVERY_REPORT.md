# M7.1-C4-B 交付记录

日期：2026-10-10。前置C4-A提交 a7f377794126aaa11905f875c3e947faa0a5e8bb 开工前独立核对远端main一致。范围见[C4方案](M7_1_C4_IMPLEMENTATION_PLAN.md)、[B契约](M7_1_C4B_RUNTIME_CONTRACT.md)。本记录不关闭C4-C或整个C/M7。

## 已实现

- query12/API1完整真实运行包反射，原表1–11冻结；176B闭合UI/Scene/shadow/skin组、24B表、每代码1MiB，不持有调用者指针或传COM/STL。
- A原始NCS1预检及GpuValidated=false不变。独立BindingsValidated准备持有无源码包和exact renderer，拒绝错误末尾输出/compute线程组、活动帧、外来或释放owner、错误线程、撤权及重入。没有新增ShaderDefinition假源、Agent权限或推理。
- 同一C1–C3实际原生验证/候选创建/原子交换路径消费source-free bytes；原先的异常、drain超时、旧资源保留测试全部保留。参考反射在renderer内以最多10条固定内部契约缓存，不重复编译已准备的参考组合，依然逐次校验实际用户字节。
- 正式共享SceneRenderSession准备shadow off/on两组及可选同一Skin；SceneGpuResources使用注册compute，submit/resize消费准备结果。空场景不准备3D组；Headless不加载GPU。UiCanvas在image/cache前接入，已有用户程序不覆盖，独立UI apphost也走相同包准入。
- 官方首次Cook仍在off-frame startup；尚无项目shader字段、运行包文件pin/发布或无编译器Player承诺，这些是C4-C。GPU反射不证明任意指令安全，不是代码沙箱。

## 测试和失败记录

- 首次Build.bat Debug -SkipTests仅开发构建，0新增warnings/errors，不部署生产入口。
- 首轮定向39项通过，实际默认/用户UI及四种Scene闭包像素maxError=0/API0/0。后续增加末尾compute线程组拒绝及identity Skin数值oracle，最终42项在完整回归中通过。
- 第一轮完整Debug停在原生旧测试：原未知query12现为合法新接口；将未知版本断言移动为query13，同时保留新增query12短表/准确布局/闭包/末尾stage/active/旧renderer检查。没有删除或放宽错误版本测试。日志 out/m7-1-c4b-full-debug-first.log 保留，未进入部署。
- 修正后完整Debug second通过：12/12原生（3.80s）、22/22托管（345.54s）、43/43 Python（1.087s），原渲染联合95.36s、Editor75.88s、Player67.03s。定向输出 out/verification/m2/render-Debug/runtime-shaders/runtime-shaders.json 为42项/maxError0。
- Debug后审计 out/verification/m2-8/Debug/ff4b12d5efe6434ea9df4212e2c75862/audit.json：audit_passed=true，h8_accepted=false。deployment generation 9850ebbde9284302bfc861fa40b462c8，101 manifest/104实际/104备份及Complete journal独立核验。

## 最终自动门禁

完整顺序无Skip Debug second之后，Release first通过：12/12原生（1.70s）、22/22托管（295.81s）、43/43 Python（0.916s）。渲染联合100.80s、Editor71.98s、Player66.17s；Release新增42项/maxError0/API0/0与Debug一致。两轮包含全部原M6实际FBX/NCA/根运动/Jolt/蒙皮阴影、Editor/Player/Headless/独立UI apphost、MCP、managed/native smoke、严格新格式/旧格式拒绝、Python inspect、3轮保留profile及部署恢复测试，无新增编译警告。日志 out/m7-1-c4b-full-debug-second.log、out/m7-1-c4b-full-release-first.log。

Release后审计 out/verification/m2-8/Release/989250d97224490b9645f7eb183ffa3c/audit.json：audit_passed=true/h8_accepted=false。generation 9359db44e3da47dc8cdc65135f403b3b，101 manifest/104实际/104备份及Complete journal独立核验。当前out/bin为检查通过的Release，前一轮Debug和更早A Release备份均保留。正式人工/用户FBX/材料/目标环境/自包含/完整性能/1h门禁没有因自动通过而关闭。

| Release产物 | SHA-256 |
| --- | --- |
| out/bin/NcmaEngine.exe | 44643805B9466F5C7C08DC9C29AC341A22C7AE8E82FDCA49EDD385E52A42E65A |
| out/bin/Ncma.Rendering.dll | 3AC9DC08BE630248151EB6AD80670DB1E2AA45208D9BB216F8C98465E668685E |
| out/bin/plugins/NcmaRenderer.dll | 06CEB7370E57AF6F08AB0D41E4F801243438F18DE3BB290285A8EB8C93FC3350 |

## 源码冻结与下一步

最终部署后再次核对17份实现/测试文件hash一致；之后仅同步文档、路线图、AGENTS。失败日志、备份、SDK、用户数据和4份原IDE改动均保留且不提交生成物/IDE。B自动候选完成，下一片C4-C必须在本提交推送并独立核验remote之后开始；本记录所在Git提交为交付基线。

| 文件 | SHA-256 |
| --- | --- |
| `engine/source/plugins/renderer/RendererPlugin.cpp` | `4D86418C141C832D873E12D6C247EFBA9180067BB122F5E65EB205BF81A80B8B` |
| `engine/source/plugins/renderer/ShaderStagesServices.inl` | `7633FCF767C20BDD3AD0CA5A2E31C1E8A5352C5B11283128B35C10F1A87779C9` |
| `managed/Ncma.Rendering.Scene/SceneGpuResources.cs` | `61BFA6A7FDFFBF37196D7FD02A0E89D6F5C5B839F57C7B624A7F3CB17D0C76D5` |
| `managed/Ncma.Rendering.Scene/SceneRenderSession.cs` | `1DBF534D669E2A9C2B8D0ACE046D2CF66DDC6DC7E2CF7D3BA851D81AA386F26F` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `F5560E942A663658B22525F43C1DE76D100CC8783C5D4A783C2F168C7902D11C` |
| `managed/Ncma.Rendering/RegisteredShaderStages.cs` | `5AFF809D0F159C08CA2E7E77E56717EA37A6C7EC0EAA2290F9685A46766CA913` |
| `managed/Ncma.Rendering/RegisteredUiShaders.cs` | `A9E9D31D843CC3F1DAE992792FDFFD7AB51B2A8F76B2DD77107BEC6F81E36384` |
| `managed/Ncma.Rendering/UiRendering.cs` | `117B9900D2671A41E6CA9D24800951FDC6970F882A6A5CE115E94FEAFF090401` |
| `managed/Ncma.Rendering/UiTargets.cs` | `7C0EACE1CD2AC5746713CAC51C0CEF226D9A9F531CDA31B2C2A9B02C2631AD8C` |
| `managed/Ncma.Ui.Rendering/UiCanvas.cs` | `8D650AE1A6590CF8A80DF2E83F967C8B1C099FF062E51888414617B23EEA05AA` |
| `managed/Ncma.Ui.Sample/Program.cs` | `1095350BD6ED6982F24146C739DCA124475EE45A6EDC362344538517F29BBDBA` |
| `tests/plugins/RendererNativeTests.cpp` | `EC6989A6B73506BAC1FDEE80D11026832C8DC908CAC894B43BF3AD3C87DA91DF` |
| `engine/source/plugins/contracts/NcmaRuntimeShaders.h` | `3D3B80A1CB2ED272012EC87C35896323BA7D28811A6E928E13BD4B71E084D959` |
| `engine/source/plugins/renderer/RuntimeShadersServices.inl` | `06643118C0FF13310276E525FD494D4915FAA676786E0682D3BCC1F3BEC7C57E` |
| `managed/Ncma.Rendering.Scene/SceneRuntimeShaders.cs` | `B2DD75C126F6A027AA2856A6040AC7814A75005BAC58525F1164EAD8504C1C25` |
| `managed/Ncma.Rendering.Tests/RuntimeShaderTests.cs` | `437E8C9E858135B1D39F2F2FA9D89B3B35B18752C42D9EC1AF01B76FA0D99208` |
| `managed/Ncma.Rendering/RuntimeShaderPreparation.cs` | `13E2DE04CF508E329DE4C9045F16604720C283A5A091F5687A3C3506C2649ADB` |
