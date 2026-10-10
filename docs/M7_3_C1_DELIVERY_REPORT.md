# M7.3-C1 交付记录

2026-10-10；开工前 local/remote main 为 B3 `abe290ef87fb068e0416c59fda915f163889312a`。C 先按[详细方案](M7_3_C_IMPLEMENTATION_PLAN.md)拆为 C1资产/配置、C2共用正式渲染、C3默认/部署/联合验收，本次只关闭 C1 自动候选；C2/C3、整个 C/M7.3 未完成。

## 实现

- 新独立 `.ncenv` v1描述与 RuntimeEnvironmentAsset：project/asset/generation/expected SHA/精确规范路径，复制 NCE1、4MiB验证和原 generation/父目录 read pins。UUID冲突、错误类型/项目/hash/header、取消/预算/owner拒绝；两代与Edit/Play租约共存，末租约释放。
- NCP1 显式 environment encoding，精确UUID/generation/hash、模型角色必须空；实际文件搬移后无源/描述/derived/Cook/GPU加载，混合模型闭包和32次包租约关闭通过。NCA与旧encoding不变。
- 独立普通扁平对象值组件 `ncma.render.environment` v1：无需Transform、最多一个含Off、严格有限标量/身份/代次/hash，SceneAssetPreparation与composition共用核对。EnvironmentRequest只构造原事务，不授权/执行/写文件/GPU；原权限/scope/session/revision/幂等/单一history/Undo/Redo/Play freeze通过。
- `.ncmeta` v1与旧metadata-only MCP类型闭包冻结原11值/三份原schema黄金哈希；新Environment只用于独立ncenv/NCE/NCP，旧root/subasset/dependency及kind=environment检查入口明确拒绝。环境目录检查在C3接入，不隐式扩充Agent或旧格式。
- 正式Editor/图形Player尚未支持IBL配置：startup、Editor live command与Edit/Play/Player presentation fail-closed pending guards防静默忽略，损坏资产先dependency_failed；C2需真正注册profile3双shadow/sharedSkin并准备资源后替换，不能仅删除guard。没有native ABI/算法/shader、Python gameplay/inference或新Agent执行权限变更。

合同与具体上限见[M7.3-C1合同](M7_3_C1_RUNTIME_CONTRACT.md)。新NCE夹具是独立CPU存储验证，不是Cook/物理图像oracle。

## 定向与完整回归

新增29项具名环境CPU/文件/闭包/历史检查，Scene rendering共108/108；新增1项正式Editor live开启拒绝，Editor服务124/124（原123保留）。资产40/40、Player原56、Animation原115保留。Scene测试检查自身进程没有NcmaRenderer/NcmaPlatform模块；另实际启动正式Editor apphost验证pre-plugin拒绝，不把子进程拒绝视为人工UI验证。

| 最终完整无Skip Build.bat | Native CTest | Managed CTest | Python | 主要实际回归 |
| --- | --- | --- | --- | --- |
| Debug第二轮 | 12/12，3.72s | 22/22，410.72s | 43/43，1.066s | Rendering157.49s / Editor75.27s / Player67.85s / Scene1.46s |
| Release第一轮 | 12/12，2.03s | 22/22，305.16s | 43/43，.842s | Rendering107.35s / Editor71.80s / Player66.57s / Scene1.39s |

日志：`out/m7-3-c1-full-debug-second.log`、`out/m7-3-c1-full-release-first.log`。所有managed/native smokes、严格新/旧格式拒绝、M6/真实FBX-NCA/Jolt、实际Editor/Player/搬移UI/MCP、Python inspect、三轮profiles、audits与部署恢复测试保留。新代码构建0warning。原A123/B1 54/B2 46/B3 353仍通过，B3独立图像pixel/integral最大1阶、BRDF .0005584955215454102/API0/0；未替换/降低旧断言。

文档同步后再次检查22份冻结源码SHA一致、Python inspect通过、Python43/43（0.824s）；日志为`out/m7-3-c1-postdocs-inspect.log`和`out/m7-3-c1-postdocs-python.log`。

失败记录保留：

- target-first的junction fixture使用混合斜杠，被Windows mklink解析为开关；仅对自有fixture路径调用GetFullPath修复，保留junction/目标，不做递归删除。
- target-third再次使用同一不可覆盖Player report目标；改用新报告路径，保留原no-overwrite断言/报告。
- 第一轮完整Debug：旧AssetInspection黄金hash与ncmeta schema/enumeration一致性两项失败。不是改黄金值跳过，而是修复全局enum隐式扩充旧合同，冻结SupportsKind，保留三份原hash；旧格式assert增强为全部11个具体值并拒绝环境root/subasset/dependency。
- 单独Editor定向调用漏传CTest的repository/plugin参数导致IndexOutOfRange；按实际CTest参数重新执行，124项通过。
- SkipTests定向构建仅staging，未部署；第一轮失败完整构建未部署。最后22份冻结代码/测试全部重验后完整Debug/Release成功，源冻结后未再修改实现。

## 部署与审计

Debug generation `8fa2272db273400aad3c06d22393ceca`，Release generation `7be2365cf5cf4cfcaf053d4119433bfd`；各107 manifest /110 actual，journal Complete，两份110-file备份逐路径/size/SHA核对，Release后再次复核Debug备份。原5 Shader包+defaults.json六份SHA保持B3相同，无新增环境文件部署。当前完整Release `out/bin/NcmaEngine.exe --validate-package`实际返回0。

- NcmaEngine.exe：`6D111B043326665C24458598846CA650703AFA89476552ECD0D4FCD9B36C0514`
- NcmaEngine.dll：`C468199A6B9F96B1AACBDBD6ECF08F84D114D4D52D536B985E753D3F4E064129`
- Ncma.Assets.dll：`30E11B38D4595CE93F230907F3D904C5F693D718FE2C77142DE43901798A61B9`
- NcmaRenderer.dll：`C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D`（B2/B3同一原生实现）

最后postaudit：

- Debug：`out/verification/m2-8/Debug/c557b370b75447c7aad08ca3ae4dbf25/audit.json`
- Release：`out/verification/m2-8/Release/4ca9730dfd21480c82e39fc681148c47/audit.json`

audit_passed=true，h8_accepted=false。手动UI/DPI/MCP、用户HDR/EXR/FBX/材质、目标机/自包含、完整性能/1h门禁保持开放；Source/Python transport仍不宣称服务已接入。IDE/.user/SDK/用户资产/旧失败/备份保留，生成部署不提交。

## 冻结实现/测试 SHA256

以下22份在最终Debug前冻结、Release前及部署核验后重查一致；文档之后更新不改变这些字节。

| 文件 | SHA256 |
| --- | --- |
| managed/Ncma.Assets/AssetContracts.cs | 74B00E228BCE342A4736E8CC2A8A1F966559F1771C7C340222E8AA864DE25BFC |
| managed/Ncma.Assets/EnvironmentAssetDescriptor.cs | 74C78E82DE7402CC25D496E63B52E2D50CBC6935485C3589C7D2B5D662FCFE7B |
| managed/Ncma.Assets.Runtime/RuntimeAssets.cs | 14839C964031987E1E3F5D499716B6596B6DEB929922B58C0F74D571A166383A |
| managed/Ncma.Assets.Runtime/RuntimeAssetLoader.cs | 2474C09CDA597903127CC024014A1D86FD805B9CB9FAD4BFC62E58344086A2DB |
| managed/Ncma.Assets.Runtime/RuntimeAssetPackage.cs | E524932B19402F98E124FDA1E056BEE27654722BCE4F0B7DA2FF5732E6DE481F |
| managed/Ncma.Scene.Rendering/EnvironmentLightingData.cs | 03FCEA9F1A35D846F5FCF32E82C5EFB6E31DEED80848912F4A294AF6BDF772BC |
| managed/Ncma.Scene.Rendering/RenderComponents.cs | A5497EF667E9550CE52781E1F5F770328885356A8CBD03FDF482B0C80A5B31F4 |
| managed/Ncma.Scene.Rendering/SceneAssetPreparation.cs | AD783AE5F2E1332395544D108A0D1568FD1AA8E30D1E99E4B677DCD28BB35FE0 |
| managed/Ncma.Scene.Rendering/SceneRenderValidation.cs | DCAFFEA18EF35935845C335F2D797EF02F824786A6986EFF5E9E1C867EA90162 |
| managed/Ncma.Editor.Services/RenderingEditorAdapter.cs | 38FC85633A44E36BD367A4715B40C8520935BA89BD3D86DB4CAAFC88D3D9DA84 |
| managed/Ncma.Player.App/PlayerRunner.cs | 5639825C66217DB5998CF49B9CEDB537476B9A6BB3D5D9854CEC6A3B6140FD05 |
| managed/Ncma.Editor.App/Program.cs | 394ED2255FFECA5261C2141BDD7C00876DF0FD0368CF89E8458D7E1F2DDA11C1 |
| managed/Ncma.Scene.Rendering.Tests/Program.cs | 09DA0D6B2E5B88CB69451C6783E73EA4E68B1C69018D7E86B5DD3116584B0231 |
| managed/Ncma.Scene.Rendering.Tests/EnvironmentAssetTests.cs | C31163799E18F490C04291993EE9E034B573CF4516078C0349580E327185537C |
| managed/Ncma.Assets/AssetRecordCodec.cs | F1A2006A38FB6ECE57370DB75C127F2D39437CE9C6D4A25C6DD52962C12FC569 |
| managed/Ncma.Assets/EnvironmentData.cs | 761463549CB0912C6D6D2C95E19B6D43C60C84C9E3FBDF4CE5426663FF0DAB9C |
| managed/Ncma.Editor.Services/AssetInspectionSchemas.cs | 232817AB335E034D36D27AB8BF8FAE2918ADCD5F48E1427728C2EAF3390A137F |
| managed/Ncma.Editor.Services/AssetInspectionService.cs | ADF0DE72BD5CFF22618883E98A835F9ECF0187BDE20B7AA6E76F4866B6706301 |
| managed/Ncma.Editor.Services.Tests/AssetInspectionTests.cs | 8DB191A6DACBB2B090AD2FDEF0EDB142B9B662A7D25E6C0EA866A16310DDF972 |
| managed/Ncma.Assets.Tests/Program.cs | 93A87817219B08D4E1842BFEF843C2C7AB6E58EFDB7E94DFFE67B53FF43EF5BA |
| managed/Ncma.Player.App/PlayerPresentation.cs | 0B9D966F6F8BA2C5784992B68ECC6B4631737E82D21FC800ADD60E788B86AF98 |
| managed/Ncma.Editor.Services.Tests/Program.cs | 58ECEE525BF28BCDBFEA1FBADEC9E5C1565CEAC93778727F76AD5DB55BB594A7 |

下一片：提交推送/远端SHA核对后M7.3-C2。默认3D可用环境预设、专用检查与环境部署/产品闭包在C3，不以C1包合同冒称正式渲染完成。DX11 only，Vulkan/OpenGL下一版本；M7→M8→M9。
