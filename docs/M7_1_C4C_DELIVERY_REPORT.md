# M7.1-C4-C 交付记录

日期：2026-10-10。前置 C4-B `c115a370afc3856aebb3ef199434d7f07ae84577` 开工前已独立核对远端 main。范围见 [C4方案](M7_1_C4_IMPLEMENTATION_PLAN.md) 和 [文件/发布契约](M7_1_C4C_RUNTIME_CONTRACT.md)。

## 实现范围

- 独立版本化项目 ShaderPackageSelection，显式路径/独立hash/profile/shadow/skin；严格 JSON 字段、复制、预算、精确成对闭包。单个包仍为原 NCS1 v1；没有格式兼容层。
- RuntimeReadPin 共享到 Assets，启动前完整父目录/文件/manifest/index pin；拒绝重解析、硬链接、错误大小写、写入替换。已锁定的默认索引再与独立 manifest 尺寸/hash 比较。最多5包/40MiB，失败逆序释放，owner-thread-only。
- 所有包预检、整个选择的实际 native 反射全部通过才绑定默认服务。随后缺少组合直接拒绝，不编译补齐；Player 在 gameplay/PlaySession 之前准入。正式 Editor 与 Player 共用原 C1–C4-B GPU 路径，无新 native ABI/Agent 执行权限。
- 构建时 Ncma.Shader.Cook 不随产品发布；Editor 5包+索引，DX11 Player 4场景包+索引，Null无Shader包，独立纯UI 1包+索引/共18文件。所有正式包均进入原 checked manifest/deployment。
- 正式产品不读取作者HLSL文件、tick不Cook；但原生首次内部参考反射仍依赖D3DCompiler，源码布局测试和可信cook工具仍有off-frame默认编译，共享程序集没有物理裁剪。不是无编译器或指令安全沙箱。

## 测试和保留的失败

开发构建日志 `out/m7-1-c4c-build-first.log` 为 SkipTests，仅构建/暂存、不部署生产。首轮定向54项，增强后58项；复查增加 pinned manifest/index 哈希绑定、最后一个选择失败原子拒绝后，最终定向62项通过：`out/m7-1-c4c-target-third.log`。用户UI通道像素独立校验、真实GPU反射及Player pre-start（tick0/session empty）拒绝、文件/目录锁、硬链接、复制/owner/取消/关闭均覆盖，API0/0。

首次完整 Debug 的12原生通过，托管20/22通过：M6搬移Player与FBX独立Player夹具只复制程序/插件，遗漏新增Shader包，被新门禁正确拒绝。两个夹具改为复制已校验DX11发布包的四组场景程序及索引，保留原动画/FBX/蒙皮阴影/Headless/真正apphost断言。没有删测试或降低门禁。日志 `out/m7-1-c4c-full-debug-first.log`、生成夹具及所有旧备份保留；该轮没有进入部署。

## 最终完整回归

Debug second 完整无Skip通过：12/12原生（3.81s）、22/22托管（357.48s）、43/43 Python（1.096s）；渲染100.62s、Editor76.61s、Player67.87s。原A70/B41/C1 24/C2 40/C3 32/C4-A77/C4-B42及新C4-C62、全部原静态/动画/Jolt/FBX/NCA/skin-shadow/MCP/格式拒绝/smoke/inspect、3轮保留profile及部署恢复均保留通过。日志 `out/m7-1-c4c-full-debug-second.log`。

Debug部署generation `ec052bb31f634966b08ca83c33205c0d`，107 manifest/110实际文件、Complete journal、104文件旧备份逐项尺寸/hash独立核验。部署后审计 `out/verification/m2-8/Debug/72f016b879f3468f8d725adb63ebe220/audit.json` 为audit_passed=true/h8_accepted=false。

Release first 在 Debug second 后完整无Skip通过：12/12原生（1.87s）、22/22托管（301.27s）、43/43 Python（0.912s）；渲染102.04s、Editor72.69s、Player66.75s。62项新增与全部原门禁、独立18文件UI apphost、实际搬移FBX/动画Player、三轮profile、部署恢复/审计都通过，无新增编译警告。日志 `out/m7-1-c4c-full-release-first.log`。

Release部署generation `c835f3ff00b646c5b2f625013da78a0c`，107 manifest/110实际/110文件Debug备份、Complete journal逐项尺寸/hash核验。更早104文件备份保留。部署后审计 `out/verification/m2-8/Release/777afb69d6da4f12a0eaccb2f9ffb2b2/audit.json` 为audit_passed=true/h8_accepted=false。已核对Debug/Release五份Cook包hash完全一致，Editor与Player四种场景包hash一致；Editor107 manifest、DX11 Player70 manifest、Null52 manifest（Shader文件分别6/5/0）。当前 `out/bin/NcmaEngine.exe` 是本轮检查通过的Release。

| Release产物 | SHA-256 |
| --- | --- |
| out/bin/NcmaEngine.exe | C490BAACE6A661F1DD4127DE8C4DABEABC7AA1A04CE197ADC51FF325E3C31629 |
| out/bin/Ncma.Rendering.dll | EFE4FDA60AA621E0CD8A026DD9AD67A4C268AF37485DEF3EAB7F00953CF14F1F |
| out/bin/Ncma.Assets.dll | FE25D86982FDA0B52027D7A7949D8270055F68D77D13974C4581D53B79E10041 |
| out/bin/plugins/NcmaRenderer.dll | 06CEB7370E57AF6F08AB0D41E4F801243438F18DE3BB290285A8EB8C93FC3350 |

## 冻结源码

以下19份实现/构建/测试文件在最终 Debug second / Release first 前冻结，最终部署后再次确认全部hash一致。只有文档/路线图/AGENTS在测试后同步。RuntimeReadPin是共享迁移，不是删除IO保护；旧路径移除，新路径保留相同保护并扩展精确manifest pin。

| 文件 | SHA-256 |
| --- | --- |
| `managed/Ncma.Application/Ncma.Application.csproj` | `4DDE9CA3858413BE47916AC0550A3CC1F4AE64167EBBA6FC51199618D56AC1C8` |
| `managed/Ncma.Application/ProjectContext.cs` | `EBC280041E71411A6DA79612294F52F02C1AA012F1B7B4AE7EF52F6F712AC263` |
| `managed/Ncma.Assets/Ncma.Assets.csproj` | `BFD7F399A9FBF4942E8F3AB681757DD933DD5EC1168C59FB631D0C40D6B9229C` |
| `managed/Ncma.Assets/RuntimeReadPin.cs` | `93D5B98FE83709809A8DA639AC5765479D8C912502F8386A07D185DA41B87A5C` |
| `managed/Ncma.Assets/ShaderPackageSelection.cs` | `27E9BC6461AAF75DDF01B896F73EE167D5C2DC7567079446F318D7354E416AB7` |
| `managed/Ncma.Rendering/RuntimeShaderFileSet.cs` | `A023C16F53BD54B9AB5012131D1F05F266774FA0CDB1C38DDACC59699DB9B0EA` |
| `managed/Ncma.Rendering/RuntimeShaderPreparation.cs` | `9FC6580225752319F520F224C1503D61998AEDAB78D9765AFB25F500778258AC` |
| `managed/Ncma.Editor.App/Program.cs` | `C89E05F91B33F5D07926385C2CDF5200C18C73983E80639C5F243705BA0840D8` |
| `managed/Ncma.Player.App/PlayerRunner.cs` | `2797DCA6801DFD38A946B0686130B0D124128719BC606F563A7AB65A96EFD4E0` |
| `managed/Ncma.Player.App/PlayerPresentation.cs` | `8D387B8D9DD868D8B544DC49CEA867BE6D22D8975EE3E9546AB9D61CFF6B9E25` |
| `managed/Ncma.Ui.Sample/Program.cs` | `E85969F48463104EEAC2FA563AC28C7EF529464B82C178E571CFE064E3DDBC22` |
| `managed/Ncma.Shader.Cook/Ncma.Shader.Cook.csproj` | `863C557F49383E14748A0EAC6402039B331112A2C5FAA04700E246C239BF4525` |
| `managed/Ncma.Shader.Cook/Program.cs` | `433DA2EE211CC7B3827A0BAA90E0DE618232CC5D33BD00247E2748E411383DE5` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `F2D1B115A237C40971EF85203B3339868D074737318E74FD1BF74DF2C82AC0D8` |
| `managed/Ncma.Rendering.Tests/ShaderFileTests.cs` | `47ED54BDE923F105D0323FDA9093E9CEEEBEE2D204D6B458B987B345A3003ED7` |
| `scripts/Package-M2_7.ps1` | `04B421BF36165FE55B0F87A523FC9B9B9D2B774DCEFBDDDA20A5131882288A3C` |
| `scripts/Test-UiSamplePackage.ps1` | `BD803197A5E67881379D5710AAB169549F0F65591FFD87064701D99CC9A5E1A2` |
| `managed/Ncma.Rendering.Tests/M6FinalAcceptanceTests.cs` | `25E37B59CD9AC45AFC9E62F6552C16C642AE0E5F8BAC7E80FAED1A823A3D8370` |
| `managed/Ncma.Player.Tests/RuntimePackageChecks.cs` | `12A3AE30458F75714C9B4471158BF5C34C08EEC8C0D7321F23A5F81034FFD7F0` |

## 验收与后续

C4-C及C4/整个M7.1的自动候选链路完成，本记录所在Git提交为交付基线。所有失败日志/备份/SDK/用户数据和4份既有IDE改动保留，不提交生成物或IDE设置。人工UI/MCP、用户FBX/材料、目标环境、自包含、完整性能/1h仍未完成；自动门禁不替代它们。完成提交推送并独立核对远端后，下一阶段为M7.2；仅DX11，Vulkan/OpenGL留下一版本，后续M7→M8→M9。
