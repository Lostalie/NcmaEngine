# M7.4-D1 公开定制示例交付记录

2026-10-14。开工前本地/远端 `d263f44c56b942b8fd6d24a8a32e2312fe5e726c` 核对一致。完整D范围见[分片方案](M7_4_D_IMPLEMENTATION_PLAN.md)，接入与边界见[示例说明](M7_4_D1_CUSTOMIZATION_EXAMPLES.md)。本片不缩减D2/D3范围，不标记整个M7.4完成。

## 实现与定向证据

新增可编译纯C# `ExampleHdrGain`、`ExampleAmbientStage`、`ExampleScenePipeline` 与统一模块目录/工厂示例。HDR增益使用已有受检NPH数值程序构建真正的线性Feature，位于Bloom前；阶段替换只控制几何策略；完整管线自己定义资源/依赖/512阴影/一次Tone，不转发默认管线。无新native ABI、Shader、资产格式、默认开关、AI权限或Python gameplay。

首轮编译0警告0错误；定向76项/API0/0/maxError0通过，`out/m7-4-d1-first.log`、`out/verification/m7-4-d1/first/a59472fd54e942f895c7f4bed4b42fa6`。复核后补充gain零/边界/暗部/HDR独立double-Half整图、坏图无候选发布和帧内回调拒绝。第二轮编译0警告0错误，98项/API0/0/maxError0通过：`out/m7-4-d1-second.log`、`out/verification/m7-4-d1/second/8ee66dc5f7c748b19d7e7a7c47e4701f`。

共享SceneRenderSession真实IBL/静态/PBR/用户gain+官方Bloom、stage/full路径无shadow整图一致、512/1024实际shadow差异、错误/撤权/World写入/关闭/重入/owner/frame边界、奇数resize恢复/资源退回。两路径暖64 Submit分配0、无重新构图/不可变上传；这是有界示例测量，不是完整性能验收。D2实际FBX/skin/UI独立联合与D3搬移产品尚未完成。

## 完整门禁

冻结4份代码/构建文件后，完整无Skip Debug首轮通过：`out/m7-4-d1-full-debug-first.log`。12项native/28项managed/Python43，原综合渲染229.12秒，Editor154/Animation115/Player及原A538/B172/B3 353/环境产品594、M6/M7/MCP/格式拒绝/smokes/inspect/三轮profile/audits/部署恢复保留。D1为98项/整图误差0/API0/0，证据 `out/verification/m2/customization-Debug/1f3205939b624963a7ee7eb7e5d6aee5`；原共享post53/原宿主67/作者宿主89次检查通过，检查次数含动态采样帧而不是固定独立用例数。

Debug产品代次 `b43793b58c6141fea1a29ee2a70b6372`，Editor139/PlayerDX11102清单逐文件验证，各26后处理文件/CPU-only预检通过，Null无post。正式清单139/实际142/备份142逐项匹配，Complete journal `f8f8a62b6b3d4976bc3ca4f319908559`，实际apphost验证返回0，4份冻结SHA未变。同配置原生Renderer及10份旧Shader与C3一致；后处理索引SHA仍 `97330B1CA92BB54CE35B2C5F9E4C4BC7A97B30AF9F79C6436D292FE94A4A17DE`。

随后顺序完整无Skip Release首轮通过：`out/m7-4-d1-full-release-first.log`，12项native/28项managed/Python43、原综合渲染161.91秒、Editor154/Animation115/Player与上述全部旧门禁保留。D1仍为98项/maxError0/API0/0，证据 `out/verification/m2/customization-Release/4afee3dc61954b8983a79e1852664442`；共享post53/原宿主75/作者宿主84次检查均通过。两配置均首轮成功；历史间歇File.Replace失败保留，根因未确认，本轮未复现，不通过放宽断言或非原子写入隐藏问题。

Release产品代次 `66bcfe7acb664afd8191d1340b12c1b0`：Editor139/PlayerDX11102清单逐文件大小/SHA检查、各26后处理文件和CPU-only库预检通过，Null无post。正式清单139/实际142/备份142逐项核对journal.oldFiles，Complete journal `b071486db69c4d15b41ba20608a945a0`；实际apphost `--validate-package` 返回0，冻结4份SHA未变。同配置原生Renderer、10份旧Shader、默认NCE和post库索引与C3一致。两份142文件恢复备份、历史失败与IDE数据保留。

最终Release文件SHA：

| 文件 | SHA256 |
| --- | --- |
| out/bin/NcmaEngine.exe | 9DF27B82C50D819FE1038CE0DAE790BC5283D10BD9178A654A111BF065B759CC |
| out/bin/NcmaEngine.dll | 067A8419426F6C81214B1004B1E2E74E2B9BE3D56DBAB40FCCF1FFB5AF2F7E39 |
| out/bin/plugins/NcmaRenderer.dll | D5A8F1B4DF7160A114503645E06DCEE73B72FA873174CD880E5F230A31A83C92 |
| out/bin/assets/environments/neutral-v1.nce | 96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665 |

D1自动候选完成；下一片D2，不关闭整个D或M7.4。

## 冻结文件 SHA256

| 路径 | SHA256 |
| --- | --- |
| CMakeLists.txt | 406871706C0D33326A95E09A6F609DDC747A0557BA18BC0B5392A9BC12DA5A99 |
| managed/Ncma.Rendering.Tests/Program.cs | 175282EA02D73B4EFE21EE3BEC5B8226458404991ADF26A324F251E916738FBD |
| managed/Ncma.Rendering.Tests/CustomizationExampleTests.cs | BC4537FB3C93AAA2EDCBC434A9B9F225560D0D16CF315829E6EDDA60235B7042 |
| managed/Ncma.Rendering.Scene/Examples/SceneCustomization.cs | 12C1D14111319FEF3DEDDBBB683CD1F57932696639A846A922A8F18903B03729 |

## 保留门禁

D2/D3、人工UI/DPI/第三方MCP、用户HDR-FBX素材/HDR解码、目标机/.NET自包含、完整性能/1h、source-recovery高分配调查继续开放。GPU时间缺origin-frame不作逐帧证据。所有失败/备份/SDK/userdata/4项无关IDE变更保留。DX11only；Vulkan/OpenGL下一版本；M7→M8→M9。
