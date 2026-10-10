# M7.3-B2 环境 Shader 与场景绑定交付记录

2026-10-10；B1本地/远端main `0d91eda6bb9672c0bcda17a1a4fa172dfd203da4` 在开工及提交前核对。B2自动候选完成；B3完整独立IBL图像、C正式宿主/配置/资产闭包及整个M7.3尚未完成。见[实施方案](M7_3_B2_IMPLEMENTATION_PLAN.md)、[合同](M7_3_B2_RUNTIME_CONTRACT.md)。

## 实现与确切证据

- 独立query15/API1/56B/caps3、40B环境绑定，Scene104B/version2、RuntimeShaders176B/version2/profile3。旧Renderer1.2/query1–14/Scene frame v4冻结。新SceneEnvironment/geometry角色9/10、C416/t7–9/s7与独立binding hash；旧Tone/Shadow C400、原可选Skin保留。默认/用户/源无关包共享同一公开闭合合同、真实编译/反射和整组准入，不做隐式升级或降级。
- 新包NCS1 v1/profile3严格角色闭包、逐项外部hash/完整数值布局，Preflight纯C#且GpuValidated=false；所有programs包括最后可选Skin真实准入之后才能产生wrappers，安装再实际反射。新增独立环境reference cache4，旧cache10不扩展。内部参考仍有D3DCompiler依赖，不称compiler-free、代码沙箱或Agent编译权限。
- 线性E/pi diffuse + GGX mip/specular和NoV/roughness BRDF LUT split-sum，环境AO、强度与明确Y lookup旋转，随后原Tone。Off复用既有Scene sampler、无环境纹理/上传；新增RHI显式sampler-only opt-in默认false，原配对拒绝保留，opt-in也不能漏texture sampler。
- C#验证exact UUID/generation/hash、session/renderer/owner和可信off-simulation审批前后边界，复制native资源绑定。native pin保护环境替换/释放；解绑/成功Scene关闭退pin，timeout/异常保留旧program/配置/pins/资源/计数。候选RAII/完整实际反射/诊断/有界drain在发布前，发布无分配/回调。416B真实constant上传计数，旧Scene400B不变。
- 46托管实际DX11检查：Off与旧unlit像素一致/零环境分配、强度、常量旋转不变与方向旋转改变真实像素、默认/用户算法/用户source-free及旧合同拒绝、完整包准入、审批/重入/线程/数值/identity、pin/替换/解绑/关闭、两个shadow变体与GPU compute skin + shadow联合draw。API错误/警告0/0。相对像素与控制检查不是B3独立IBL积分/材质网格/实际FBX-NCA联合图像验收。
- 原生白盒：API/source短缓冲原子、错误旧C400/真实末成员offset/资源slot/最后可选Skin、创建/整组替换/绑定迟到异常及timeout、旧program identity/资源数量/输出保持、active/foreign generation/reserved/Off闭合、pinned释放和替换拒绝、Scene关闭timeout保pin及成功退pin；全部清理/API0/0。故障开关只在测试TU，没有production export或真实device reset模拟。

## 测试与保留的失败

先构建/定向，再复审补齐RHI opt-in、实际错误反射、用户和方向fixture，最终46项及原生测试通过后冻结24份源码/测试。首轮测试编译使用不存在的Dispatches计数，改为现有Batches；一次定向传入workspace而非plugins目录；Off真实DX11报s7 null sampler warning，修复明确sampler绑定；白盒访问private program失败，补只读内部program副本；方向fixture使用cos表示X而非Z，按已有atan2(z,x)合同改sin。所有失败日志保存，没有减少断言、屏蔽API警告或放宽像素要求。最终顺序无Skip双配置原CTest全部通过，无新增编译警告/错误，24份冻结SHA复核无变化。

| 配置 | 原生CTest | 托管CTest | Python | Rendering / Editor / Player秒 |
| --- | --- | --- | --- | --- |
| Debug first | 12/12，4.18s | 22/22，370.89s | 43/43，1.127s | 115.95 / 75.91 / 69.17 |
| Release first | 12/12，1.93s | 22/22，302.66s | 43/43，.873s | 105.62 / 71.46 / 66.26 |

完整日志：`out/m7-3-b2-full-debug-first.log`、`out/m7-3-b2-full-release-first.log`。最终定向：`out/m7-3-b2-targeted-frozen.log`、`out/m7-3-b2-native-final.log`。完整Rendering产物`out/verification/m2/render-Debug/environment-scene/report.json`及Release对应路径均46/API0/0，并明确fullIblImageAcceptance=false/formalHosts=false。原B1 54/A123/M7.2 68/M7.1/M6/FBX/NCA/Jolt/Editor/Player/MCP/新旧格式拒绝/smokes/Python inspect/三轮profiles/audits/部署恢复保留。未知query15→16仅因15新增。

## Checked deployment

Debug generation `e644fc6a6a8e42fba9605d3fd571809f`、Release `1e57bb7e33c54a189f7993954b9e6c06`，Complete journal；双配置均107manifest/110实际文件完整路径/尺寸/hash核验，两个110文件backup逐项对照各自journal旧文件匹配；Release后再次核验早期Debug backup。5个旧profile0/1 Shader包及defaults.json共6份与B1正式部署SHA逐项一致，没有偷偷扩展旧闭包。正式exe是本轮完整通过的Release闭包；环境仍显式新合同，正式默认宿主尚未切换到IBL。

部署后audit Debug `out/verification/m2-8/Debug/2f4d0cd5b66345e9bfe7a06bcc9cbc89/audit.json`、Release `out/verification/m2-8/Release/e539e7e6c8804436969aa1a7d24a4245/audit.json`：audit_passed=true / h8_accepted=false。不将生成部署/备份/无关IDE设置加入Git。

| Release产物 | SHA-256 |
| --- | --- |
| out/bin/NcmaEngine.exe | 21CFB6D7F359725DDB63263440E6D65EA6F10B2AB87A159EB1D24AA0C0B89CBA |
| out/bin/Ncma.Rendering.dll | F4D134D8EE36AE7FFD6C57F4D4E78D9868A7068ACF80887015E72D2048DEB21C |
| out/bin/plugins/NcmaRenderer.dll | C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D |

## 冻结源码（本轮工作文件SHA-256）

| 文件 | SHA-256 |
| --- | --- |
| `engine/source/plugins/contracts/NcmaEnvironmentScene.h` | `A8A4CC84976791190F2A2F2DA8A43E57B340ED6DF5CAE32070737F1D0F5F8605` |
| `engine/source/plugins/renderer/EnvironmentGpuKernel.h` | `61A26202F9A63AC31A405934E9AB8B19636977252D5CD201F4AD610450C56E3B` |
| `engine/source/plugins/renderer/EnvironmentGpuServices.inl` | `A9A0A51E9650B6A0FB0451B01E7D5A67798B3E75C26CD40B7A76FEBDA0247D3C` |
| `engine/source/plugins/renderer/EnvironmentSceneServices.inl` | `62D873241003DD0DFFED03DD784D81E2FABF7F2CD75BE2954DB7D46815B79B73` |
| `engine/source/plugins/renderer/RendererPlugin.cpp` | `0CEDE2F147F6BA6AC9994409FB616341FD6DEBBEE710FE269F022EF7C5A217C6` |
| `engine/source/plugins/renderer/ScenePipelineKernel.cpp` | `B9AD34F9E53A3898C41D17309F5B602024590530C8BBF505460C4A7DD143C5E1` |
| `engine/source/plugins/renderer/ScenePipelineKernel.h` | `1D427339A377D0BBA7231B96B85EA9D0D890E7B93D4022038DAA089E582AE3D1` |
| `engine/source/plugins/renderer/ScenePipelineServices.inl` | `B3F332195D03E1218B5DECC3B36A2C6034EC364CF41EF965A4BD8FBA9AE1EE87` |
| `engine/source/plugins/renderer/ShaderPipelineServices.inl` | `83DFE075268A7FAC4C5BEACED038180557B732A5BF29144C2782F6BE0DDD060E` |
| `engine/source/plugins/renderer/ShaderStagesServices.inl` | `419297C10A06C8F5B294E834656A55D78CD3FF6323528EDFAB481FC5DB115020` |
| `engine/source/runtime/renderer/rhi/d3d11/D3D11RenderBackend.cpp` | `07AF0CB78A3D190743649AC2F0BAAC7ED4C80DC97A0D198F7B59B0FBDBEC2134` |
| `engine/source/runtime/renderer/rhi/RhiResources.cpp` | `13F90C9023FE90CFBE3FEFD9D5E584D014D5D54905D7482661441D6DAC6F7E40` |
| `engine/source/runtime/renderer/rhi/RhiResources.h` | `F9DD8CE68973BEF54D924668A1A254AC3AD483140D29FB5B942A56573EEDF661` |
| `managed/Ncma.Rendering.Tests/EnvironmentSceneTests.cs` | `21AC5EB33FCA9375F0C26C65BA1968CEE1DF834F6D45D09F11A49C9380733096` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `6E965886D1694FE077713AF4B5B3DA0EB064082E045CF5E15D8053BF44814A99` |
| `managed/Ncma.Rendering/EnvironmentSceneShaders.cs` | `F38CC37B7CFFBD8F54CF8CE81819748B83C33C5E86F7596C80ED2E0161A4236A` |
| `managed/Ncma.Rendering/RuntimeShaderPackage.cs` | `34D78062F12B64B8E9A43D722BD7F51068158FDB7D131A5D113D50889F6E20DC` |
| `managed/Ncma.Rendering/RuntimeShaderPreparation.cs` | `52C82EC294C4EBA104A92B2B37B91BDDA89B73B92AED277EFEDDC02E2BFDFD49` |
| `managed/Ncma.Rendering/ScenePipeline.cs` | `700257BF65F20DC5AF5276E34CDC9AF602E18FD8626DEF7C937BE8E155783618` |
| `managed/Ncma.Rendering/ShaderContracts.cs` | `4D000CBEE19166016668CB6DA387D8F8C6B8AC4D0D2DCE9C4AC50912476BF17E` |
| `tests/ArchitectureTests.cpp` | `F46C1F65A32E04B8D17A2F1F502583BF1988E4822CA0434C4731E34793A775CB` |
| `tests/plugins/EnvironmentSceneNativeTests.inl` | `523E67D6BB08EC355C58BBD4DD5896455586EC9903C2D0C6D048398278621539` |
| `tests/plugins/PluginContractC.c` | `D566ADAD0F53FA545188BADB2FB0F263047EDC9765194555B1DAEE8942CC9EB4` |
| `tests/plugins/RendererNativeTests.cpp` | `C190446FC93490E276A01F73B25BF6806B52236FD6302258931A679D9E1C3F14` |

B2不关闭B3/C或人工UI/DPI/第三方MCP、真实用户FBX/HDR/材质、目标/自包含/完整性能/1h门禁；没有HDR/EXR解码器、推理/Python gameplay/Agent新执行权、C++ World或旧格式兼容。保留失败/备份/SDK/用户数据/IDE；提交推送并核对远端后下一片M7.3-B3。DX11only，Vulkan/OpenGL下一版本；M7→M8→M9。
