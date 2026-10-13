# M7.4-B HDR 子图与 Bloom 交付记录

2026-10-14。开工前本地/远端main `acffae59b14f629f3ba3578e26394f304f76db6b` 一致。见[方案](M7_4_IMPLEMENTATION_PLAN.md)、[运行合同](M7_4_B_RUNTIME_CONTRACT.md)。本片自动候选完成，最终冻结源码的顺序no-Skip Debug第二轮/Release第一轮、受检部署和实际apphost校验全部通过；人工/完整性能等门禁保持开放。

## 范围

公开C#有界线性HDR图、官方Bloom声明/参数、用户同契约程序、独立无源码NPH1，薄原生query16/API1完整反射/候选准备/原子安装/执行/退役。旧query1–15/NCS1/C400/C416和正式默认shader/宿主选择未改。PureUI产品增加零后处理资源诊断，不部署NPH。C正式配置/宿主/编辑与AI、D产品联合尚未完成。

## 定向测试

四次托管定向分别118、156、161、172项检查，全部通过；最终独立CPU全图最大误差1个8bit颜色码值，DX11 API错误/警告0/0。新增检查不是172个独立CTest。原日志/PNG/JSON在`out/m7-4-b-target-*`与`out/verification/m7-4-b/target-*`保留，没有本片已知定向失败被删除。

涵盖5种尺寸64²/63×47/1²/2×3/129×65、参数/levels、亮斑扩散/阈值/黑场无残留/常量能量、半精度量化、用户自定义无源码两pass和逆序依赖、strictNPH/DXBC/外部hash/旧NCS拒绝、无Renderer/Platform/Gui/compiler的子进程预检、错误图/预算/颜色域/alias、撤销/帧内/owner/回调重入/Off保留旧图、Environment+shadow变体（IBL Off）与实际显示域UI。

原生5次定向构建保留，第4轮12/12 CTest、3.87s；补强诊断拒绝后第5轮12/12、4.08s。新C ABI静态布局、short-query、实际arity/slot/output/最后常量偏移、异常/timeout、scalar无drain、不同布局完整候选失败保留已安装资源/计数、预算/Off/close/pureUI。真实InfoQueue人工注入warning使全量/参数发布均拒绝；测试仅还原这两次人工注入，生产无诊断清除接口，不把人工负例混作正常API0/0。SkipManaged/Python仅定向验证，不部署。

代码复查期间补齐：NPH复制后再校验hash；scalar发布前设备健康检查；Scene准备标志前owner检查；真实非空旧graph替换异常/timeout测试。最终冻结24份代码/测试/构建脚本后开始完整顺序no-Skip Debug/Release。

## 最终完整回归

Debug首轮失败，日志`out/m7-4-b-full-debug-first.log`：新增172项及此前A538/B3 353等已通过，既有EnvironmentProductTests在SceneDocumentFiles.Save/File.Replace报“无法删除要被替换的文件”，21/22 managed通过，总383.92s，未部署。失败目录`out/verification/m2/render-Debug/environment-products/3db12d71e0654a53bd671c1394677e28`保留；之后只读独占打开成功，不能据此断言当时外部锁定来源。原代码/断言不变，独立新夹具复测490项通过，日志`out/m7-4-b-environment-product-recheck.log`，证据`out/verification/m7-4-b/environment-product-recheck/4ff3179a84fe41378b9b1510b02d475b`。

首轮运行期间代码复查发现native仅记录diagnostics、没有显式拒绝。等待该轮退出后补齐发布前及drain后错误/警告拒绝，添加真实InfoQueue负例；此前首轮不属于最终证据。24份源码重新冻结，第二轮完整Debug全部通过，日志`out/m7-4-b-full-debug-second.log`：native12/12（3.69s）、managed22/22（477.23s）、Python43/43（1.097s），Rendering218.85s、Editor78.78s、Player66.60s。新增172项最大误差1/API0/0、原A538/B3 353/C3-C490和所有M6/M7测试保留；新旧格式、MCP、smokes/inspect、三轮profile、审计/部署恢复通过。

Debug部署generation `428abf0a5d4a404cb1030482d0e55be8`：113manifest /116实际 /116备份、Complete journal，逐文件路径/尺寸/SHA一致；24份冻结源码未变，实际apphost校验exit0，9个默认Shader包及索引与上一片同配置SHA一致。post-deploy审计 `d6125587a9774ca4ae0a68bdd28e23a7` 为audit_passed=true、h8_accepted=false。

Release在Debug部署验证后顺序执行，第一轮完整通过；日志`out/m7-4-b-full-release-first.log`：native12/12（2.12s）、managed22/22（357.21s）、Python43/43（0.947s），Rendering152.94s、Editor73.52s、Player67.39s。新增172项、A538/B3 353/C3-C490、原M6/M7、smokes/新旧格式/MCP/inspect/三轮profile/审计/恢复全部通过。独立纯UI真实apphost仍18文件，后处理6项计数全0。环境产品证据`out/verification/m2/render-Release/environment-products/ad9b8c207d5f4705816599e3ec838e16`。

Release部署generation `0b111d890fe346e0abebefe9a7a86559`：113manifest /116实际 /116备份、Complete journal，逐文件路径/尺寸/SHA一致；真实`NcmaEngine.exe --validate-package`以Start-Process -Wait读取exit0。24份冻结源码及Debug的116文件备份再核对通过；两配置9默认Shader包+索引与A同配置完全一致。post-deploy审计 `671ce9a4e8c9458c9715fb699856ca5e` 为audit_passed=true、h8_accepted=false，不能将审计通过称为人工门禁关闭。

- Release `NcmaEngine.exe`：`59C1BA9012A89E47F6D2E0E6A3372E2652A466FA3E95BA9C6BBBA1F7FB30CCBB`
- `NcmaEngine.dll`：`DE80F8A06431DB5BA9DE5D489EE2B187AB652102B6A2CFF58AE4258974730467`
- `Ncma.Rendering.dll`：`01F3795FBFA2733A5C97A1838D30C937EFC1F6C39C00294CF11AE4CED692748E`
- 原生Renderer：`D5A8F1B4DF7160A114503645E06DCEE73B72FA873174CD880E5F230A31A83C92`（本片新增执行内核，DLL有意改变）

## 费用证据

Debug先暖机32帧，再连续64帧；5组均0托管分配、0子图发布/资源重建/不可变数据重传。构图/cook/安装/图像回读/oracle不在该采样中。

| 尺寸 | 每帧pass | 逻辑resident字节 | 64帧draws | 64帧常量上传字节 | Debug总ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| 64×64 | 12 | 75280 | 768 | 12288 | 16.842 |
| 63×47 | 12 | 55576 | 768 | 12288 | 17.0442 |
| 1×1 | 4 | 48 | 256 | 4096 | 11.4045 |
| 2×3 | 8 | 152 | 512 | 8192 | 12.6928 |
| 129×65 | 12 | 157360 | 768 | 12288 | 15.755 |

Release相同5组64帧CPU总时长依次11.9665/9.8357/7.8120/8.2926/9.3486ms，均0托管分配且draws/常量字节/逻辑resident与Debug一致。证据分别为`out/verification/m2/render-Debug/post-process/post-process-results.json`与Release同路径。

时长为包含Submit/Present的CPU墙钟总耗时，不是GPU均值、独立逐帧GPU采样或目标机/full performance验收。Resident是目标+常量payload，不等于驱动VRAM。实际大小/levels受128MiB live+candidate约束，不承诺任意4K。

## 冻结源码（最终24份）

| 路径 | SHA256 |
| --- | --- |
| CMakeLists.txt | 6272827AEE76D65A012316E7BA1CB144E1FE517ADB0EC0748DCBE83C55D85E31 |
| engine/source/plugins/contracts/NcmaPostProcess.h | 476E65C06D996A6D49AB5D887B3D91CD78C59310C5CEDC419AF93FE7EF958FC8 |
| engine/source/plugins/renderer/PostProcessKernel.cpp | 5855DBBC8ABEE0454F51FA796D17573BB0FAABE07476CD3131AB9A2BB0092D20 |
| engine/source/plugins/renderer/PostProcessKernel.h | 8248006105A5CBA9A9EDD84B7BB3D1309D72588679A72053CE1542BDFD850CE2 |
| engine/source/plugins/renderer/PostProcessServices.inl | 25D09C6A875A7E70E4001BAA77CC2D9B80901A2140F36A9FFD3C1A6F7CD29708 |
| engine/source/plugins/renderer/RendererPlugin.cpp | C397D370078333B242048D28D204E1C0C53C2138D3199D149C8CC6F9CE3393C1 |
| engine/source/plugins/renderer/ScenePipelineKernel.cpp | 199FDB526893A8CB0B0258049844F7E1BC837953DD516C35EFEA3069D37A99B1 |
| engine/source/plugins/renderer/ScenePipelineKernel.h | 9E4DC068562E9A13E79587F9A1970992B080DCB36FB22BFA0F66BAF310152E58 |
| engine/source/plugins/renderer/ScenePipelineServices.inl | 097FC470660B2F1A0407180C031879AF57E374623B60D04895D2E798BC9AF18F |
| managed/Ncma.Rendering.Tests/BloomOracle.cs | AE7EE30287652BDFD76865D65BE1DB20B25B77E45259D15A77C309BB446E1374 |
| managed/Ncma.Rendering.Tests/PostProcessTests.cs | A7C5303B293E75D970DC8DE547183571BCBB8ED0C7FBCBA4C3E4782AF54F5F9B |
| managed/Ncma.Rendering.Tests/Program.cs | 3A365F58088A7D01684497FAFF691D0B4251CA20F9AE4F415E1CB7AAF47943B8 |
| managed/Ncma.Rendering/BloomFeature.cs | 7BB5D28BF175DE48C31A09E1106D812BBE101B4E617C8E5A99C9F3C5FFEDDC91 |
| managed/Ncma.Rendering/PostProcessGraph.cs | F1676F516D413A8E2D1DB74F6F3BF2C87D3724940DB6FC52E6E5AA55619AC80D |
| managed/Ncma.Rendering/PostProcessPackage.cs | 47F01EB095F0A2651C91A5BA4770D75EC95D568B699E3B2FED378203993FC7F5 |
| managed/Ncma.Rendering/PostProcessRendering.cs | 28E991B56ED8AB268CBCC550194401533D371683FAFADDADBEF4BA03607F8CC8 |
| managed/Ncma.Rendering/RuntimeShaderPackage.cs | 7DA95755942119C46A2E869ED55434A7A7BDA13200BD4C04F9FBE55E5557737A |
| managed/Ncma.Rendering/ScenePipeline.cs | 761165F951E2E71AAA600D796CBC4E67CC17A10798B27A64214877A7B4819690 |
| managed/Ncma.Rendering/ShaderContracts.cs | 672095FDD838E20B6CE4A9E9546D97F4162625471AC7C31BCC27949EDAFED0FB |
| managed/Ncma.Ui.Sample/Program.cs | 55B91774DF2F80D8FD040110E243E9BFF7437214C4CD7482EAE4C5A71F3030AE |
| scripts/Test-UiSamplePackage.ps1 | 4CE9CDB13AA1520F62F31EA8C1687787B30B52488C585D45D405A3DD634AFA6D |
| tests/plugins/PluginContractC.c | 05FCB7B3DBD7EC7FD8B0560E92FA184153F539CE00133B0792F67FFE97D6C143 |
| tests/plugins/PostProcessNativeTests.inl | E1B27137BF6585984D8AEB5CB080FC76B5325F333C51894B953B6234D1E4B36A |
| tests/plugins/RendererNativeTests.cpp | F877F4C3073B0D73A57A56704F8AF8BC53583685C74649BC7A542171536CAF9C |

## 未关闭门禁

文档同步后项目inspect再次exit0，Python43/43（0.904s）；提交前远端main仍是开工基线。提交仅包括本片24份代码/测试/构建脚本及7份文档/AGENTS，排除生成部署、证据/备份和4项既有IDE设置。完成提交推送及远端SHA核对后才能进入M7.4-C。

M7.4-C正式配置/共享宿主/Undo/AI、D完整替换/FBX-IBL-Bloom联合未完成；不宣称M7.4整体完成。纯静态kernel费用不是完整Editor性能，历史source恢复约1.31MB/帧仍待调查。人工UI/DPI/可见MCP、用户HDR/FBX/材质、HDR解码、目标机/.NET自包含、完整性能/1h仍开放。DX11only，Vulkan/OpenGL下一版；M7→M8→M9。保存全部失败/备份/SDK/userdata及4项既有IDE/.user修改，未纳入提交。
