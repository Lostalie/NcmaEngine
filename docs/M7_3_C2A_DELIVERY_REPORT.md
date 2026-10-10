# M7.3-C2-A 交付记录

2026-10-10；开工前C1 local/remote main `2efb4db76432bbe0b4aaf52df00da6c49e5e3423`一致。本片关闭C2-A自动候选，不关闭整个C2/C/M7.3；C2-B共享场景执行、C2-C正式宿主和C3默认部署仍待完成。见[C2详细方案](M7_3_C2_IMPLEMENTATION_PLAN.md)、[合同](M7_3_C2A_RUNTIME_CONTRACT.md)。

## 实现与边界

- 原ShaderPackageSelection v1的1–5包/两profile闭包保持；新显式v2最多9包，独立SceneEnvironment × shadow/skin四种变体，不把未知profile转成Scene3D。每个场景profile双shadow配对，所有profile的实际SkinCompute字节须相同，因为一个Renderer共用一个计算核。
- RuntimeShaderFileSet复用精确path/SHA/profile/flags/owner/read pins/manifest-index核验，query12/query15完整实际准入后才发布整个selection。最后环境组实际反射失败不发布半组，不污染后续正确selection重试。
- 独立DefaultRuntimeShaderService.PrepareEnvironment和SceneEnvironmentRuntimeShaders准备profile3双变体/shared skin；文件模式缺包不Cook、不回退，源布局工具保留显式帧外Cook，缓存最多9条。owner/healthy/off-frame/可信off-simulation审批/非重入/撤销/foreign owner核对仍在；没有新增Agent编译、源码、GPU执行权限。
- 包始终GpuValidated=false。原native ABI/NCS1/shader源/算法/默认五包与index字节不变；内部参考反射仍依赖编译器，不是compiler-free宿主或代码安全沙箱。本片不创建正式环境GPU/scene，不修改C1正式Editor/Player pending guards；不能称正式IBL已接入。

## 测试与修复

新增71项实际测试进入原NcmaRenderingTests完整回归：v1拒绝新profile、v2闭合9包/副本/重复字段/路径/hash/profile/skin配对、跨profile不同计算核拒绝、取消/owner/read pins/manifest、整组实际准入/最后组错误SV_TARGET1反射失败/原子重试、同owner双变体/撤销/非重入/帧内/foreign拒绝、源无关用户环境shader实际整图等于官方半强度并且确实照亮画面、纯UI无环境/3D资源；API0/0。所有原断言保留，未重置schema黄金值或修改旧格式接受范围。

- target-first用同一个窗口创建第二Renderer触发已有平台限制；target-second同一个Platform module创建第二窗口也被拒绝。跨owner夹具改为关闭第一个Renderer后，在同一窗口建立新Renderer再验证旧pair/跨owner构造拒绝。两份失败日志保留。
- target-fourth67项与第一轮完整Debug均通过。复核发现同Renderer各geometry profile还必须共用同一个skin计算核；新增全selection字节检查和独立自定义pair有效/混合冲突拒绝，定向71项通过。重新冻结7份源码后执行最终完整Debug第二轮/Release第一轮，不拿修正前结果替代。
- 原target SkipTests构建仅staging，未部署。所有失败、第一轮完整结果/备份、IDE/.user/SDK/userdata保留。备份核验首条合并命令超过Windows命令长度而未执行；改为分开的只读核验均通过。

| 最终无Skip Build.bat | Native CTest | Managed CTest | Python |
| --- | --- | --- | --- |
| Debug第二轮 | 12/12，3.77s | 22/22，413.23s | 43/43，1.110s |
| Release第一轮 | 12/12，1.92s | 22/22，308.22s | 43/43，0.888s |

Debug Rendering160.45s/Editor76.02s/Player67.08s；Release Rendering109.68s/Editor71.84s/Player66.47s。原A123/B1 54/B2 46/B3 353及C1 Scene108/Editor124/Assets40/Animation115/Player56、M7/M6/实际FBX-NCA/Jolt/MCP、managed/native smokes、严格新旧格式、inspect、三轮profiles、audits和部署恢复全通过。B3仍pixel/integral最大1阶/BRDF .0005584955215454102/API0/0。构建零warning；没有将自动结果称人工/目标/完整性能/1h验收。

日志：`out/m7-3-c2a-target-tests-first.log`、`out/m7-3-c2a-target-tests-second.log`、`out/m7-3-c2a-target-tests-fifth.log`、`out/m7-3-c2a-full-debug-first.log`、`out/m7-3-c2a-full-debug-second.log`、`out/m7-3-c2a-full-release-first.log`。文档同步后再次Python inspect通过、Python43/43（0.838s），见`out/m7-3-c2a-postdocs-inspect.log`和`out/m7-3-c2a-postdocs-python.log`；7份冻结源码SHA仍一致。

## 部署与审计

修正前Debug generation `fefc09e99072491bac61eb17a2f0a640`；最终Debug `10cfebdebe0d4fb78015a89caf41deca`；最终Release `18e5ca14e3a54ab3a4a778ae839a1ce9`。全部107 manifest/110实际/Complete journal；三份110-file备份逐路径/size/SHA核对，Release后复核前两份Debug备份。原五Shader包+index六份SHA不变，无环境文件部署。实际当前Release `out/bin/NcmaEngine.exe --validate-package`返回0。

- NcmaEngine.exe：`B6959C8B429174A3542BA5CFB51F93DFBF3C69B5433DC206E25DE0D4063F4885`
- NcmaEngine.dll：`A83541422D73D61FB520F3B6DACD00C75981D5442BBC2946CBA2BC091A8A0524`
- Ncma.Rendering.dll：`F139985C80F2D1FD8D9C8BEC82338EB187C021BE2A4309A90B1BEB6134543C84`
- NcmaRenderer.dll：`C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D`（原生未改变）

最终postaudit：Debug `out/verification/m2-8/Debug/e3a9634cd11741a39bd8da2e823f3455/audit.json`；Release `out/verification/m2-8/Release/b469c40ada2843f5a0ecf0f027b8722a/audit.json`。audit_passed=true/h8_accepted=false，原人工UI/DPI/MCP、真实用户HDR/FBX/材质、目标/自包含、完整性能/1h门禁仍开放。无Python gameplay/inference或传输服务接入，生成部署/备份/IDE不提交。

## 最终冻结SHA256

在最终Debug前冻结、Release前及所有部署核验后7份字节一致。

| 文件 | SHA256 |
| --- | --- |
| managed/Ncma.Assets/ShaderPackageSelection.cs | 48FBDB4A886A25E9C25C64EE441EBBC5F7CAD74A62C0C2A1758F3BA1B620B922 |
| managed/Ncma.Rendering/RuntimeShaderFileSet.cs | E83D68555FEF811B47EFEAC044ABE92E4192EF8E43E168550308374631998C7D |
| managed/Ncma.Rendering/EnvironmentSceneShaders.cs | CC9C6609A6EE2584BF6E44DC86B25C15EAF34733C0E47DE916A8156516BAAAD2 |
| managed/Ncma.Rendering/RuntimeShaderPreparation.cs | DB1A10A01B15E445C244A20DF38BD1CF73EA7627A210236A4231A0AB45696C64 |
| managed/Ncma.Rendering.Scene/SceneEnvironmentRuntimeShaders.cs | 237EFAD7C8862098830FBEFC5D972CB852A7A825EC2CBA6594F94A4A80B3BCE3 |
| managed/Ncma.Rendering.Tests/EnvironmentShaderFileTests.cs | 6E91116CCAC446A8C626173E02B420D171E8A99ADC9100B6DEA7977003F32DD2 |
| managed/Ncma.Rendering.Tests/Program.cs | C63756038CBEB3470873324CC1C1AC0A79D1AF148B464A8F1997C76A4EA3C8F9 |
