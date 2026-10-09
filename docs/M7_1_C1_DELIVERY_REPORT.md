# M7.1-C1 交付记录

状态：C1自动候选完成。同一实际不可变目录默认／用户Tone、24项真实定向、native ABI／退役故障及修正后完整顺序无Skip Debug/Release通过；checked deployment/hash/journal核验完成。
前置B `6e6887c266ae25ecc7228de0cdd937172098cffd` 已推送并在C开始前核对远端。
见[契约](M7_1_C1_RUNTIME_CONTRACT.md)、[C切片计划](M7_1_C_IMPLEMENTATION_PLAN.md)。只有C1，C2–C4/整个C未完成。

## 定向与审查

- native first构建通过；second新增测试变量pair与既有skin夹具重名失败，单独作用域修复后third–sixth通过。
- managed first–fifth构建0warnings/errors；定向first–third 22项，补充同一实际目录和跨候选重入后fourth 24项通过。
- `out/m7-1-c1-target-fourth.log` 和 `out/verification/m7-1-c1/debug-target-fourth/registered-tone-results.json`：
  实际默认／用户GPU程序，独立整幅图误差0（预定义容限1字节），API0/0，16次同一scene替换、原图恢复、错link/target/资源/末成员、host revoke/owner/active/生命周期拒绝。
- 最新原生NcmaRendererNativeTests通过：40字节表、短source输出原子、错版本／真实错stage／真实末cbuffer member在GPU前拒绝，
  test-only drain超时保留原scene并释放候选，正常替换资源baseline不变，完整关闭。
- 审查收紧实际系统语义／stream、C1未执行依赖拒绝、候选RAII和设备失效fail-stop；末成员HLSL夹具统一重命名全部引用，
  确保测试确实是“合法shader的错误绑定”而不是语法失败。保留所有中间日志和旧M6断言。
- 实际PBR geometry默认／用户Tone整幅图联合测试通过：两者与原图／通道排列的maxError均0（容限1字节），既有独立GGX/ACES/sRGB oracle maxError1（既有容限2字节）；API errors/warnings均0。Geometry shader本身仍未注册。

## 最终门禁

冻结全部实现／测试源码，再顺序无Skip Debug/Release、checked deployment/hash/journal。
首轮完整Debug测试通过且部署到候选；但审查发现诊断分配位于发布后，存在异常时返回失败而已发布的风险。
首轮日志 `out/m7-1-c1-full-debug-first.log`、audit、部署备份保留，不能算修正后的最终门禁。
将所有可能分配的Validation移至scene key/Tone交换之前，noexcept发布，加入test-only诊断异常故障注入，
分别验证创建输出/计数未发布、替换旧stage保持、候选释放且未fail-stop。生产没有新增fault开关或诊断执行权限。
修正后冻结15份实现／测试源码，最终顺序无Skip门禁结果：

| 最终轮次 | 原生 CTest | 托管 CTest | Python |
| --- | --- | --- | --- |
| Debug second | 12/12，3.27s | 22/22，314.65s | 43/43 |
| Release first | 12/12，1.31s | 22/22，281.87s | 43/43 |

日志：`out/m7-1-c1-full-debug-second.log`、`out/m7-1-c1-full-release-first.log`。
两轮均包含A70、B41、C1 24、实际PBR默认／用户像素联合测试和原生诊断异常／drain超时原子故障注入；
保留原M6动画、Editor/Player、真实NCA/skin-shadow/Jolt、0/1/8/32角色、独立Player、MCP、smokes、新格式与移除格式拒绝、inspect、3轮profile与audit。
新代码编译无warnings。现有profile复测不是新性能／长跑验收。

最终audit：

- Debug：`out/verification/m2-8/Debug/a65b2aa82cda4855bf0b143b0723f61b/audit.json`。
- Release：`out/verification/m2-8/Release/7584b77387f24ec786adacb55019d35b/audit.json`。

均audit_passed=true，h8_accepted=false，不关闭独立人工门禁。
Release checked deployment manifest核验101项，实际目录104文件，journal phase=Complete，
generation=`626914fb3796426aa938c4efd94f9051`；未使用Skip部署。
恢复备份均核验存在、104文件、非reparse：
`e3daf3eb308e4116b15cc7eab2cc9183`（首轮Debug前）、
`469bda121b6448b19d7d0c99e9b67e35`（修正后Debug前）、
`626914fb3796426aa938c4efd94f9051`（Release前，保留最终Debug）。
首轮Debug及定向失败日志／备份全部保留，未删除SDK、用户数据或IDE设置。

Release交付SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| `out/bin/NcmaEngine.exe` | `5872AD28DAF2B6AB72716F24E6DD1C5680A8DD1769C6FA1D7E89A6EEA79A913A` |
| `out/bin/plugins/NcmaRenderer.dll` | `A1031D196308046CCD3B0C7666D3F2C5B1ADD322CAC80CDB51C2B8DF621A8588` |
| `out/bin/Ncma.Rendering.dll` | `A6AA85C46E416E44A5CA4E31805107379B5D1B23B0111CD6DE47177CA41D5828` |

## 实现／测试冻结

最终两轮完整回归及交付核验均匹配以下15份源码；回归后仅补充文档状态，不改实现／测试。

| 文件 | SHA-256 |
| --- | --- |
+| `engine/source/plugins/contracts/NcmaShaderPipeline.h` | `3A39C6A7B4EF036CDFDFCCA8E3DDAB4789F512E7654BD057514C3B0CE5771C7D` |
| `engine/source/plugins/renderer/ShaderPipelineServices.inl` | `AEE16037DEC067198672DF1E581E7C257B2F65E669008CAE7297E5107C8FE2E2` |
| `engine/source/plugins/renderer/RendererPlugin.cpp` | `B9EF6A42C9AF48BFB97B6197CF62AD78CCBA964495964B703B18F245EDFFF5EC` |
| `engine/source/plugins/renderer/ScenePipelineKernel.cpp` | `ED3EBA277D15C19B8D746035032C46F67C8771A75421BD3BECA35D9D50E1E5F0` |
| `engine/source/plugins/renderer/ScenePipelineKernel.h` | `6F8DCF16979F066D07C6E63032EEEF96EB10191FAE70FC0789BCF7992B688418` |
| `engine/source/plugins/renderer/ScenePipelineServices.inl` | `944AFDEB21344EA8A8573AFCA97CBBB08BA7E6362280B2EA69237B6D049167C4` |
| `engine/source/runtime/renderer/rhi/RhiResources.h` | `6B8557024682E8BBA87CEC45D99CE810926C7A5C6388F302C746DFE0DEE09A9D` |
| `engine/source/runtime/renderer/rhi/RhiResources.cpp` | `5E50C9137EDA3B6AF2B217A960EF4CBFD9CE8CBBFCA3CE577F9C2186C557B44E` |
| `engine/source/runtime/renderer/rhi/d3d11/D3D11RenderBackend.cpp` | `C6417D5F4CB17783AA4F50C6A587A8795D274C1FC7145D684D1B960CBA81E702` |
| `managed/Ncma.Rendering/RegisteredSceneTone.cs` | `77A1DA701CB5939A717A89759194FC160FC273BE9418AB6B9126EBE9CA02237E` |
| `managed/Ncma.Rendering/ScenePipeline.cs` | `0A3190FEC6C762D92EE52B5C13AC815032DE0E58B4BEE58F7AAFEE8897C1B481` |
| `managed/Ncma.Rendering.Tests/RegisteredToneTests.cs` | `4041D0DB37FC80EDFB5633E1C578E79543E2987F7535F7DB248FC1F05036F022` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `DB98F8764C294477764A8A95D5392030AF3E7BA14B36A962D0658C5CF2794F06` |
| `managed/Ncma.Rendering.Tests/ScenePipelineTests.cs` | `772CDD1A94C0CD6CA60BDF84F2615E9DD18CC3AFDA3ADD777AA4C69B3E2AB2E4` |
| `tests/plugins/RendererNativeTests.cpp` | `B0FC16A8821EEAC304FC770827E92784A22A9B8930BFE791F402423396F42779` |

## 提交边界

提交仅含C1源码／测试／计划／交付和持续上下文，排除生成部署、证据日志、备份及四份无关IDE设置。
提交／推送及remote main一致性以本交付记录所在Git提交和实际远端核验为准；远端核验后才进入C2。
无正式宿主默认切换、shader运行包／UI／MCP编译授权／inference或C2–C4实现；人工/用户材料/目标/自包含/性能/1h仍开放。
