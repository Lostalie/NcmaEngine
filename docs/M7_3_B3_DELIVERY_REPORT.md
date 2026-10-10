# M7.3-B3 IBL 图像验收交付记录

2026-10-10；B2 本地/远端 main `4e1b4a597e7a3cee64b24ff4429339a207707b7c` 开工及提交前核对。见[方案](M7_3_B3_IMPLEMENTATION_PLAN.md)、[验收合同](M7_3_B3_RUNTIME_CONTRACT.md)。353项、完整顺序Debug/Release及checked部署通过，B自动候选链完成；C正式宿主和整个M7.3仍待完成。

## 实现与证据

仅增加测试和其原Rendering入口，不修改生产ABI、Cook、Shader、host/项目/Agent合同。独立软件cube/LUT/mip/linear HDR/tone消费oracle、解析常量/方向积分及均匀半球BRDF三层证据；预定门限3/4色阶与BRDF .02未放宽，实际pixel1/integral1/BRDF .0005584955。48组金属粗糙度、黑/常量/HDR/方向、28组旋转强度、零强度/Off、常量旋转、±X/±Y倾斜法线/±Z旋转、ORM AO0/64/255、sRGB base/normal/emissive、用户乘.5/source-free一致、resize320×192及generation2更新/8次scene关闭重建。NCE1与NCS1的复制字节均重新严格decode/preflight并真实GPU准入，GpuValidated不提升。

实际静态不对称FBX/NCA有解析投影内部像素；ASCII5clip×4/Binary4×4，共36姿态经NCA写入/重新读取解码、native Pose TRS采样、registered GPU skin/shadow/material/IBL；独立CPU four-weight完整position/normal/tangent/UV/sign（12标量）与整图oracle。ASCII vertex最大1.78814e-7、Binary1.43051e-6，全图最大1阶；去IBL和去casters都有显著实际图像变化（36行证据）。这是同进程NCA重读，不是独立Player重启或第二个动画/DCC算法oracle；原ufbx及M6固定步/图/Notify/Movement断言保留。

warm32+64static帧：线程allocation0、environment/immutable mesh/material upload/create/cook/readback0，ABI copied33792B、真实constants53248B。Tone旧C400声明未变，Scene共享C416 allocation，两次上传均416B。CPU elapsed是本机异步submit/present样本，不称完整性能/GPU时长或1h门禁；详见生成costs.json。API0/0，最终环境Live/Resident0。

## 测试与失败保留

原Rendering CTest只增加调用，原断言/CTest timeout240不删改。定向最终日志 `out/m7-3-b3-targeted-final.log`，证据 `out/verification/m7-3-b3/Debug/targeted-final/`；353项通过。此前编译将RuntimeShaderPackage名称写反；第一次定向把Tone声明C400误当共享buffer上传400（实际416，按native UpdateBuffer/计数核实为53248）；第二次将已preflight的Skin wrapper再次Cook，改用原Compiler-created输入；第三次复用一次性Importer上下文被拒绝，改每文件独立上下文，并保持每个GPU skin提交更新相同frame。失败日志保留，无production行为/警告屏蔽/容错fallback或像素门限扩大。

## 完整门禁与部署

最终完整顺序无Skip Debug first/Release first Build.bat均成功；无新增编译警告/错误，14份冻结源码/测试SHA逐项复核未变。原M7.3-A123/B1 54/B2 46、M7.2 68/M7.1、M6 Animation115/Editor123/Player56及所有native/managed smokes、新格式/旧格式拒绝、MCP、Python inspect、三轮profiles/audits/deployment故障恢复保留。

| 配置 | 原生CTest | 托管CTest | Python | Rendering / Editor / Player秒 |
| --- | --- | --- | --- | --- |
| Debug first | 12/12，3.96s | 22/22，412.15s | 43/43，1.084s | 158.66 / 75.71 / 68.15 |
| Release first | 12/12，1.91s | 22/22，305.29s | 43/43，.898s | 107.52 / 71.30 / 67.06 |

日志 `out/m7-3-b3-full-debug-first.log`、`out/m7-3-b3-full-release-first.log`；原Rendering CTest生成 `out/verification/m2/render-Debug/environment-image/` 和Release对应目录，两者353/API0/0、pixel1/integral1/BRDF .0005584955。64帧CPU样本Debug10.4226ms/Release8.9062ms，两者allocation0、环境upload0；不称GPU时间/完整性能。

Debug generation `dacc8ac0781d445fb1bbb0eedfa4c0af`、Release `60f556f4598a48b297b72961a658001e`，均107manifest/110实际、Complete journal；两个110文件backup逐项对各自journal旧文件核验，Release完成后再次核验早期Debug backup。原5个profile0/1 Shader包及defaults.json共6份与B2正式部署SHA逐项相等。正式exe来自本轮完整通过的Release闭包，仍未切换正式宿主IBL。

部署后audit Debug `out/verification/m2-8/Debug/820155f4751f4c308b4c59fd0fa0019b/audit.json`、Release `out/verification/m2-8/Release/49072314697b4949814b35676bff86d0/audit.json`，audit_passed=true/h8_accepted=false。所有失败/日志/备份、SDK/用户数据及无关.vs/.user保留，不加入Git。

| Release产物 | SHA-256 |
| --- | --- |
| `out/bin/NcmaEngine.exe` | `412616960BF0AA2A1D80C11B7815A477622CCD5CCA961C1D1A1BE31043856E61` |
| `out/bin/Ncma.Rendering.dll` | `AF59DA3CDACDFA2EF25BFE122BF4EFAC62262211102D40BBA39BFFCEDFF7C683` |
| `out/bin/plugins/NcmaRenderer.dll` | `C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D` |

B1/B2/B3自动候选链完成；提交推送/远端SHA核对后下一片为C。整个M7.3及人工门禁未完成。

## 冻结源码

| 文件 | SHA-256 |
| --- | --- |
| `managed/Ncma.Rendering.Tests/EnvironmentImageOracle.cs` | `7502FC1691362D4EEFFE22D46DA870ECFBE5ED5696FB2BD5C8AABEA678FE076F` |
| `managed/Ncma.Rendering.Tests/EnvironmentImageTests.cs` | `BBC279D40BF455351ACC904C4D744A720D307A7D969C64E18D797E49D328049A` |
| `managed/Ncma.Rendering.Tests/EnvironmentJointImageTests.cs` | `D9F4F95A72B2827FAFC8CAD5E37A5EF968C104A878ECA5FCF5331023982F57C6` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `466772FC5F55482A7AA18D044028EE4703EE712FC315319191AB1AE4E984910B` |
| `managed/Ncma.Rendering.Tests/EnvironmentCookTests.cs` | `B2FED7883E24A4400BAFBF0FDC9FC25410EEE6ECE8DE8EA680A0D0CCFEDD6FC8` |
| `engine/source/plugins/renderer/ScenePipelineKernel.cpp` | `B9AD34F9E53A3898C41D17309F5B602024590530C8BBF505460C4A7DD143C5E1` |
| `engine/source/plugins/renderer/EnvironmentSceneServices.inl` | `62D873241003DD0DFFED03DD784D81E2FABF7F2CD75BE2954DB7D46815B79B73` |
| `engine/source/plugins/renderer/EnvironmentCookKernel.cpp` | `BA8F1B24E7C2C6E5C394A7C0A584FD6E4915D18295D06BDF478EC396FD217B48` |
| `engine/source/plugins/renderer/EnvironmentGpuServices.inl` | `A9A0A51E9650B6A0FB0451B01E7D5A67798B3E75C26CD40B7A76FEBDA0247D3C` |
| `managed/Ncma.Rendering/EnvironmentSceneShaders.cs` | `F38CC37B7CFFBD8F54CF8CE81819748B83C33C5E86F7596C80ED2E0161A4236A` |
| `managed/Ncma.Rendering/EnvironmentGpuResources.cs` | `B76F061D45D14601DD5B84615AD6D6D411CC1A1A6100E47FE66E5CDACB8E7D4A` |
| `managed/Ncma.Rendering/ScenePipeline.cs` | `700257BF65F20DC5AF5276E34CDC9AF602E18FD8626DEF7C937BE8E155783618` |
| `managed/Ncma.Rendering/RuntimeShaderPackage.cs` | `34D78062F12B64B8E9A43D722BD7F51068158FDB7D131A5D113D50889F6E20DC` |
| `CMakeLists.txt` | `C528C7AD488BE5C88A93A5B76F1F6DBFB6E51BFF15CEEBDA33C6A928201A13B5` |

## 未关闭门禁

正式Editor/Player环境配置/项目-NCP闭包/permissions/Undo为C下一片；整个M7.3尚未完成。HDR/EXR解码、人工UI/DPI/MCP、用户FBX/HDR/材质、目标机/自包含/完整性能/1h验收保持待办。无推理、Python gameplay、新Agent/liveGPU/code execution权限、兼容旧格式；DX11only，Vulkan/OpenGL下一版本，M7 -> M8 -> M9。
