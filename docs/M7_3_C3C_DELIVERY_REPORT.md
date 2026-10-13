# M7.3-C3-C 正式产品联合验收记录

2026-10-14。开工及提交前 local/remote main 核对为 `7de102e3f8e6ddeb692f013eade47e1f23cf72e1`。边界见[运行合同](M7_3_C3C_RUNTIME_CONTRACT.md)、[实施方案](M7_3_C3C_IMPLEMENTATION_PLAN.md)。最终冻结源码的完整顺序无Skip Debug/Release、受检部署和实际apphost校验全部通过；M7.3-C3-C及M7.3自动候选链完成，人工/性能等门禁保留。

## 本片实现

新增受检搬移产品编排、严格程序集隔离子进程与正式宿主探针；原产品 CLI、世界/场景、Shader、原生 ABI 和环境算法不变。独立 UI 示例补充环境 live/resident/publications/upload 零计数及受检18文件包校验。

实际 apphost 与同部署程序集探针分开：前者证明正式入口启动/退出，后者证明实际正式共享服务图像/生命周期，不能把探针叫做 apphost 入口。初始复制精确清单和运行后 manifest、原生模块路径、实际加载生产程序集路径/hash均核对。源目录锁定，静态及真实 ASCII/Binary FBX 导入NCA后的运行项目仅保留NCP/scene/gameplay。默认产品 Shader 与 NCE 保持受检。

静态中性/方向环境独立软件 oracle、强度/旋转/Off、Undo/Redo、视口缩放、3轮Play/Reload/Stop、实际动画64步、GPU skin/shadow计数、关闭卸载、Null精确8步/无3D均纳入。坏NCP与UUID/generation/hash在Player session创建前拒绝；默认NCE/Shader损坏由实际apphost manifest拒绝。全套保留B3 353等旧算法/安全/文件测试。

代次切换单独使用源项目，可信夹具提前准备新NCE，显式Off/离帧刷新/重新绑定；不声称NCP原地修改、GPU缓存原地替换或自动重导入。无效代次命令不落地；额外测试故障注入才用于失败清屏/恢复，不扩大Agent权限。

## 定向失败与修复（记录保留）

- 初次编译：GuiItem Kind为uint，补正确枚举转换。
- 首轮产品探针：Windows Process.Modules也列出托管PE，改为检查PE元数据区分，托管程序集仍由严格ALC独立核对；未增加开发输出回退。
- 手动定向调用插件根少写`m2`，导致导入内核路径不存在；修正调用参数，原测试不降级跳过。
- 实际FBX默认首片段为静态BindPose；增加片段变化记录、选择真正有关键帧变化片段与包围盒构图、暂停64单步和图像变化断言。未把非空截图当动画成功。
- 直接将旧已准备元数据改绑新代次被正式验证正确拒绝；测试改为显式Off/refresh/on，另保留无效代次拒绝与可信扩展故障注入两条边界。

`out/m7-3-c3c-target-first.log`至`eighth.log`、compile-first至eighth日志保留。第八轮定向485项检查通过，之后增加5项实际Player成功报告核对并冻结7份源码进入完整回归。检查数包含精确复制逐路径断言，不等于新增独立用例数。所有失败、staging、备份和测试资源均保留。

## 完整回归与部署

7份源码SHA冻结后，完整顺序无Skip回归：

| Build.bat | Native CTest | Managed CTest | Python |
| --- | --- | --- | --- |
| Debug第一轮 | 12/12，3.66s | 22/22，477.91s | 43/43，1.128s |
| Release第一轮 | 12/12，1.92s | 22/22，349.13s | 43/43，0.860s |

Debug Rendering216.92s / Editor77.60s / Player67.84s。新产品编排490项检查、8个正式产品探针；静态/方向软件oracle最大像素误差0，API0/0。保留B3 353、C3-B2正式宿主116次检查及全部旧M6/M7/MCP/格式、FBX/NCA/Jolt、smokes/inspect、三轮历史profile和部署恢复。完整日志为 `out/m7-3-c3c-full-debug-first.log`、`out/m7-3-c3c-full-release-first.log`。

Debug generation `f509a83df3ec4181a271f8ce926b2529`：113 manifest / 116实际 / 116备份，Complete journal，逐路径/尺寸/SHA一致，实际 apphost `--validate-package`（Start-Process -Wait）exit0。post-deploy audit `c57ee146f76f4415b03eaedf0730fff6` 为audit_passed=true、h8_accepted=false。Renderer和9 Shader包+索引与上一片同配置完全一致，7份源码SHA未变。

Debug产品联合证据：`out/verification/m2/render-Debug/environment-products/2c35043eff2b477fbb46ce7b32fd4cf5`。其中Editor探针实际加载29个受检生产程序集、Player19个、Null18个；全部来自各自搬移产品清单。每个目录保留request/probe/实际程序集与原生路径/实际入口日志及Player报告。

Release Rendering146.63s / Editor73.82s / Player66.80s；产品编排同为490项，8个探针共176次检查，静态/方向独立像素误差0/API0/0。保留B3 353、C3-B2正式宿主98次检查及所有原测试。证据目录 `out/verification/m2/render-Release/environment-products/50bba21898644c049af05e7980b9223f`；实际生产程序集计数/路径与Debug相同规则。

Release generation `54cc7d8ef82f480bb623e763fc364c77`：113 manifest / 116实际 / 116备份，Complete journal，逐路径/尺寸/SHA和实际apphost验证exit0。post-deploy audit `9339aa07fd9348b48c41df04388a96aa` 为audit_passed=true、h8_accepted=false；三轮profile均完成。7份冻结源码未变，原生Renderer及9 Shader包+索引与上片同配置一致，默认NCE字节不变。

Release后再次核对Debug的116文件备份，全部路径/尺寸/SHA仍一致。文档更新后inspect exit0、Python43/43（0.879s），7份冻结源码SHA仍一致。提交只包含本片7份代码/测试和方案/合同/交付/路线图/AGENTS，不包含生成部署、备份或4项既有IDE/.user变更。

- Release `NcmaEngine.exe`：`442A65B3816F1536F8C7ADAE8EF6987673989194EEBF01C38FAFBB156D33BB86`
- `NcmaEngine.dll`：`5C9665DF21F18E6F360568B47A12569D3E9C69F4708D8C7F9C99F8F0F302F69E`
- `NcmaRenderer.dll`：`C638F87B64F5E8ABC6BB1EE40E7B406F38F1306FAD1B09E6079434AB2C877A5D`
- 默认NCE：`96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665`

## 成本与开放项

Debug暖机16帧后32帧：静态Editor CPU总37.4957ms / 864864B托管分配 / 最后有效renderer GPU样本0.043072ms；ASCII动画Editor40.9784ms / 928152B / 0.02896ms；Binary45.1729ms / 940952B / 0.02944ms。代次切换及故障恢复后的源项目Editor324.7266ms / 41899664B / 0.044352ms。所有GPU样本valid=1，但不是32帧GPU均值或目标机预算。

源项目故障恢复路径约1.31MB/帧的完整宿主分配明确保留为后续性能调查项，不宣称性能验收。完整Editor包括UI/诊断/服务分配，不能称为零分配；环境上传/发布/readback与不可变网格/纹理上传和管线重建单独断言无新增。未关闭完整性能/1h门禁。

Release相同32帧：静态14.9241ms / 864864B / 最后有效GPU0.040192ms；ASCII14.9689ms / 928152B / 0.027616ms；Binary16.3309ms / 940952B / 0.028ms；源项目代次/故障恢复321.3085ms / 41899664B / 0.043488ms，GPU样本valid=1。该高分配源项目路径并未被Release优化消除，不以平均FPS掩盖。原共享渲染缓存静态零分配测试仍独立保留。

人工UI/DPI/第三方可见MCP、真实用户HDR/FBX/材质、HDR/EXR解码、目标机/.NET自包含、完整性能和1h仍待验收。无推理服务/Python游戏脚本/新Agent执行权限。M7.3只能完成自动候选链；整个引擎及M7仍未完成，下一小阶段M7.4；DX11only，Vulkan/OpenGL下一版，M7→M8→M9。

## 冻结源码

| 源码 | SHA256 |
| --- | --- |
| managed/Ncma.Rendering.Tests/EnvironmentImageOracle.cs | C1FF94913C2FEBCE1AA1A33313CF8DF88484E92D4F85AEEC1AE25D6C1858EB81 |
| managed/Ncma.Rendering.Tests/EnvironmentProductBootstrap.cs | 8C791949A1982D2B7F6521B387F4B4820B0127D37A175C53D7E3C8950BB4E14D |
| managed/Ncma.Rendering.Tests/EnvironmentProductProbe.cs | DF978D6AAF2B85464939AB6EB3F2145F4CCD5A4C22DF8A29E05F7F0EF6C39349 |
| managed/Ncma.Rendering.Tests/EnvironmentProductTests.cs | C7A3795FBE6A98AA29944042815E96719C0EF2BE06E1064CF969BD055F0F8110 |
| managed/Ncma.Rendering.Tests/Program.cs | A2AD780350614B707DEABA155D4315969F868B2F2E15D27095FDF578E391D0FA |
| managed/Ncma.Ui.Sample/Program.cs | 708A85C14606807C13AAC135B1CCFBE802B718F045B9AB056285EE8F3C76FBC6 |
| scripts/Test-UiSamplePackage.ps1 | 49478B5C06D53C731011EB161AD8D604405A9782C9F21707872B39C5793B9EA3 |
