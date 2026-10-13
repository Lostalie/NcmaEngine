# M7.4-C1 后处理程序库交付记录

2026-10-14，开工前本地/远端main `fe7d7be62237c1c9f789bf0e163b1717b317aae1`一致。见[C详细方案](M7_4_C_IMPLEMENTATION_PLAN.md)、[合同](M7_4_C1_RUNTIME_CONTRACT.md)。C1自动候选已完成，最终冻结源码下顺序无Skip Debug第三轮/Release第二轮全部通过；C2/C3与D尚未完成，人工门禁不关闭。

## 实现

纯Assets严格v1索引/模块UUID，C#有界pin/manifest/源目录与部署读取，完整库真实准入、Tone非Tone阶段精确一致和共享skin字节验证；公开官方/用户同路径。构建期24种Tone变体+Bloom+索引26文件进入Editor/DX11 Player，UI/Null不引入后处理。native ABI/旧shader/NCS1/NPH1不变，正式宿主/项目配置/scene组件/AI入口未改，C2/C3待做。

## 测试与修复记录

- 首次库/Cook及测试构建0警告0错误；实际Cook26文件、完整准入、scene/post资源0。
- 第一轮137项全部通过、图像最大误差0/API0/0。
- 第二轮新增审批生命周期负例发现首个整库审批回调能关闭Renderer，后续读取报PluginLease已释放；原日志`out/m7-4-c1-target-second.log`和夹具保留，不放宽断言。
- 为首尾审批增加与内部准入相同的Renderer操作保护，并增加最终审批关闭负例；第三轮148项通过，最大图像误差0/API0/0，证据`out/verification/m7-4-c1/target-third/78071aac368d4866954f275bfbe927bc`。
- 最后复查补上manifest精确大小写、只对总manifest要求非负文件大小（不误拒无关空文件）和用户图像误差纳入总统计；第四轮149项通过、最大图像误差0/API0/0，日志`out/m7-4-c1-target-fourth.log`，证据`out/verification/m7-4-c1/target-fourth/82a3730b107642f1905ad566e4158503`。
- 第一次完整Debug：native12通过；managed组21通过，原渲染组虽输出全部检查通过（含C1的149项）仍在240.03秒被CTest判定超时，因此完整门禁失败，不能当成通过。保留`out/m7-4-c1-full-debug-first.log`。新增三次实际Cook/程序库检查移为独立`NcmaRenderingTests.PostLibrary`（120秒），原渲染组240秒不变，原断言不变；名称前缀确保Build.bat原managed选择/排除表达式纳入它。总managed组增加为23项。

## 完整回归与部署

第二轮完整Debug在构建/打包阶段主动中止：复查发现独立测试参数应传实际插件根而非仓库根，未作为通过证据；日志`out/m7-4-c1-full-debug-second.log`保留。修正后重新冻结8份代码/测试/构建文件，第三轮完整Debug `out/m7-4-c1-full-debug-third.log`成功退出0：native12/3.70秒、managed23/502.23秒、Python43，原渲染221.61秒、独立程序库19.31秒。原A538/B172/环境产品594/M6-M7、MCP/新旧格式/smokes/inspect/三轮profile/audit/部署恢复通过。C1输出149项/max0/API0/0，证据`out/verification/m2/post-library-Debug/0d6106f56d2c49ebbece1ffee54664c3`。

Debug受检部署139条manifest/142实际文件/116备份逐项一致；Complete journal `ac272b029a684d42badcf4dd2ebcd0ba`，实际`out/bin/NcmaEngine.exe --validate-package`退出0。8份源码SHA保持冻结，native Renderer及旧10份shader文件与B的Debug部署字节一致。随后顺序启动完整Release，首次失败与最终通过记录如下。

Release首轮原渲染组在63.38秒失败，新增独立C1组11.43秒通过。失败在既有`EnvironmentProductTests`第98行：三个错误identity子进程均已退出，恢复原场景时`SceneDocumentFiles.Save/File.Replace`报“无法删除要被替换的文件”。夹具`out/verification/m2/render-Release/environment-products/97b6c269c941444f8273c269486c1af5`与全部日志保留。事后文件Attributes=Archive、长度2993且只读独占打开成功；不能据此断言当时的外部占用来源，也不认定Windows/杀毒软件为已证实原因。原生产Save和旧断言不改；此轮不是通过证据。

首轮最终native12通过、managed22/23通过（282.22秒），未部署。原环境产品用例不改源码/断言，在新夹具独立复测594项全部通过，日志`out/m7-4-c1-environment-product-recheck.log`，证据`out/verification/m7-4-c1/environment-product-recheck/d5e9ead9110947718dcaf80b82274b1a`。只能证明该次复测通过，不能宣布原文件替换问题已修复。8份源码SHA再次核对一致后，第二轮完整Release `out/m7-4-c1-full-release-second.log`退出0。

最终Release：native12/1.99秒、managed23/376.32秒、Python43/0.939秒；原渲染158.58秒、独立C1组11.11秒。新增149项/max0/API0/0证据`out/verification/m2/post-library-Release/341adf4b78d64b82881842ed8986b493`；原A538/B172/环境产品594（`render-Release/environment-products/2917d9b348004ca1b048522afe0b06e1`）及M6-M7/MCP/新旧格式/smokes/inspect/三轮profile/audit/部署恢复全部保留通过。最终两轮无新增编译器警告。

最终Release受检部署139条manifest/142实际文件/142备份逐文件核对，Complete journal `0def613ebdd0443e8e7b971e21485178`；再次核对Debug的116文件备份。实际apphost校验退出0，8份冻结SHA不变，native Renderer及旧10份shader文件分别与B同配置一致，neutral NCE仍为`96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665`。

Release EXE SHA256 `76DC9408B65F239655DEF6BD4508B349B92F7D4A833E795960C4FB36E683B4E2`；主DLL `24F602EA8F607D11EC66296863D744473738F66570A90B79DF68D525384EBBA0`；Renderer `D5A8F1B4DF7160A114503645E06DCEE73B72FA873174CD880E5F230A31A83C92`。

Debug实际暂存generation `4ef2a731366046bc9b725bbe05607510`单独只读复核：Editor139/Player-DX11102条manifest，各26个post文件逐路径/长度/SHA通过；从manifest读取外部索引SHA后，两个真实产品路径均通过独立CPU-only子进程预检（native/compiler/Editor=0），Null无post目录。索引SHA均为`97330B1CA92BB54CE35B2C5F9E4C4BC7A97B30AF9F79C6436D292FE94A4A17DE`。这是独立验证器读取实际产品，不是正式宿主已经使用后处理的声明。

最终Debug实际暂存generation `e49f4d6e660d45dfa0c48df8355cb423`重新进行了同样的两个实际产品逐文件核验、CPU-only预检及Null排除；数量、索引SHA均与前一代一致。

最终Release实际暂存generation `377ea220e69549889bccf2e4ad1f69c4`亦通过全部文件、两个实际产品CPU-only预检和Null排除，Editor139/Player102，各26个post文件；索引SHA与Debug一致。旧环境产品检查从490增至594来自4份完整产品副本各新增26文件，未减少或放宽旧检查。

## 冻结源码

| 路径 | SHA256 |
| --- | --- |
| managed/Ncma.Assets/PostProcessLibrarySelection.cs | 1EA9737837A63CF684F325AAFD717A8D84715D1913798CEB45ED5C8865BFFDCA |
| managed/Ncma.Rendering.Tests/PostLibraryTests.cs | 4433C28F1F560ADA9E78C943A38DC87F360F799B28B0DBCA90448158548BD9F8 |
| managed/Ncma.Rendering.Tests/Program.cs | C2664EDAC12BEECA0B368F24F758EB4E825AC1D4DD61E2A579C7D89E9C765DB7 |
| managed/Ncma.Rendering/RuntimePostProcessLibrary.cs | A2BBE1D2F8B39C53AE60B4F5809ED54E189E299E54A020A61000D0D8585E91F1 |
| managed/Ncma.Shader.Cook/PostLibraryCook.cs | FFC81EE351DE34F9007A11E44E82CC22D6A118598C76254BEB49581F17E340B9 |
| managed/Ncma.Shader.Cook/Program.cs | 732DF646C13E2D962170D7318C645DA9B785ED0B2DC55A14441DD5DFEC8D74DC |
| scripts/Package-M2_7.ps1 | 07C9F50A483E67567138821DA80A25665EACFF10A606996C1F4C88B6209A7755 |
| CMakeLists.txt | 26DC35889BAA23F27D4097C2FAAF092ECAA9B9C8986DD25F6F700E83B4E8F864 |

## 保留门禁

C1程序库不等于C2正式配置/共享执行或C3可撤销UI/AI；D真实FBX/IBL/Bloom联合仍保留。人工UI/DPI/MCP、用户素材/HDR解码、目标/自包含/完整性能/1h以及历史源恢复高分配调查未完成。失败夹具/备份/SDK/userdata/4项IDE设置全部保留；不得提交生成物。DX11only，后续M7→M8→M9。
