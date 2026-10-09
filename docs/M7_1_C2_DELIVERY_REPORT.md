# M7.1-C2 交付记录

状态：C2自动候选完成。真实Geometry／Shadow／Skin、40项定向/native故障/完整联合及最终顺序无Skip Debug/Release通过，Release checked deployment/hash/journal核验完成。前置C1 4f853a1远端main一致。见[方案](M7_1_C2_IMPLEMENTATION_PLAN.md)、[运行契约](M7_1_C2_RUNTIME_CONTRACT.md)。

## 定向与保留失败

- managed first构建0warnings/errors；native first/second通过。target first/second拒绝Skin：D3DReflect将StructuredBuffer元素作为RESOURCE_BIND_INFO常量区反射，平面成员验证误拒绝合法struct。修复为闭合有界递归type/member布局验证，保留stride/threadgroup checks；不是放宽为任意struct。
- native third失败：新增stage group变量隐藏既有变量，warning-as-error阻止构建。改为独立stageGroup，native fourth0新警告通过；所有first–fourth日志保留。
- target fourth40项通过：同一目录真实默认／用户Geometry BGR、Shadow alpha discard、Skin compute平移，image0/0、skin0、API0/0。错链接／末Tone输出／stage／resource／vertex offset／末成员／线程组／stride／owner／revoke／nonreentry／active／生命周期拒绝保留旧像素或数值；多mesh共享kernel验证，无新mesh/palette上传。
- 原生定向NcmaRendererNativeTests通过，日志out/m7-1-c2-native-tests-first.log：query10 table/source短输出原子、真实末成员/末stage/同stride不同字段offset/线程组、create/replace诊断异常、drain超时/保留旧whole group和Skin、candidate释放／旧资源baseline、最后kernel关闭。
- 完整Rendering定向：out/m7-1-c2-render-target-first.log，2/2（含reference setup），97.56s。ASCIIBinary FBX真实注册compute位置max1.1175870895385742e-8/9.5367431640625e-7，法线角度max.03956468264845247/.03426402009603798度；NCA start/middle/end完整注册Geometry/Shadow/Skin图像max0，原GPU/CPU容限和所有M6断言保留。

## 最终门禁

冻结16份实现／测试后顺序无Skip Build.bat，实际最终结果：

| 轮次 | 原生 CTest | 托管 CTest | Python |
| --- | --- | --- | --- |
| Debug first | 12/12，3.85s | 22/22，318.00s | 43/43，1.005s |
| Release first | 12/12，2.20s | 22/22，283.88s | 43/43，.856s |

日志：`out/m7-1-c2-full-debug-first.log`、`out/m7-1-c2-full-release-first.log`。新代码0编译warnings/errors。
两轮包含A70/B41/C1 24/C2 40、真实注册compute FBX数值、完整注册NCA geometry/shared-shadow inclusive endpoints、M6原始Animator/Montage/Jolt/root/Notify/Editor0-1-8-32/独立Player/MCP及全部原断言；Editor123/123。原A/B/C1测试输出是各自切片边界记录，不代表当前组合功能未执行。
新格式与移除格式拒绝、managed/native smokes、Python inspect、三轮保留profile、前后audit均通过。复测成本只是现有样本证据，不是新性能验收。

最终post-audit：

- Debug：`out/verification/m2-8/Debug/a0068934975c4668a78e42909e3ab95e/audit.json`。
- Release：`out/verification/m2-8/Release/6ec38a26a9fe464b8ef29a7f95c9399d/audit.json`。

均audit_passed=true/h8_accepted=false，不能关闭独立人工门禁。
两轮checked deployment均核验manifest101项/实际104文件/Complete journal。
Debug generation `fecf7d17550c413caa2f1b8f84d36a75`，backup保留C1 Release；
Release generation `7d73af778f384ec19df5eaa42b7ca734`，backup保留本轮最终Debug。
两份backup均104文件且通过非reparse路径／文件检查；所有失败日志及以前备份保留，无SDK/用户数据/IDE删除。

Release交付SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| `out/bin/NcmaEngine.exe` | `80A4F46A03366CC99B64B18761D975FC3EDAE177CF12E020DAA0D1E475C50E6B` |
| `out/bin/plugins/NcmaRenderer.dll` | `57646D372DD4FC87EF01C87B5B50CF0750DE7DFA67DF5912789769E49DC54789` |
| `out/bin/Ncma.Rendering.dll` | `BF5CDFAD087019A90388E3F32FED91F4D3619F9CFDC449FBE66E58F804BAD439` |

## 源码冻结与提交边界

最终两轮、部署后核验匹配以下16份源码；回归后仅更新文档／路线图状态，没有再改实现或测试。

| 文件 | SHA-256 |
| --- | --- |
+| `engine/source/plugins/contracts/NcmaShaderStages.h` | `45241625791F6ACDC488ECE54822C62538D6B4005959170C6CE3C0923D8A0801` |
| `engine/source/plugins/renderer/ShaderStagesServices.inl` | `DA827802A19033C03BFFC30784308E647077093C6E2132E82DE2DE1EC5A63571` |
| `engine/source/plugins/renderer/RendererPlugin.cpp` | `8414C0BEF60E30B6DDAD6AF37CEF28C3494DB3C5C0BEE9E4DC9F67630DF72EAC` |
| `engine/source/plugins/renderer/ScenePipelineKernel.cpp` | `078B52E6F83F4595DF56E2EDAD14B31B68B9ECBAF06E7971875A9E6BF70E4477` |
| `engine/source/plugins/renderer/ScenePipelineKernel.h` | `5CF854314170E589700F0E0334A99C779A7DABD252AB67A3A11C8D20F09B74CB` |
| `engine/source/plugins/renderer/ScenePipelineServices.inl` | `04DF2D9C9AE4DB6D86B9588263B1EEBCE9E172F9E019D3C41F75671AF0FAA58F` |
| `engine/source/plugins/renderer/SkinKernel.cpp` | `18EFA431402DCFFD71464141B397C08F01F047DD1A5F0D72FCE76FDF727B406D` |
| `engine/source/plugins/renderer/SkinKernel.h` | `82EA7980643A6284AF68E5E03905135729691F0399B4A89E56B0D06BFE6C9C74` |
| `engine/source/plugins/renderer/SkinServices.inl` | `66FE73915A0F8C94C60F6430199FCF8181F8405FC325252C257090DEE4235F00` |
| `managed/Ncma.Rendering/RegisteredShaderStages.cs` | `6BD4A09B45DA9080DDFE004AC5B67839BB862D4AE0B03E0E7A716F6A0FA81E85` |
| `managed/Ncma.Rendering/ScenePipeline.cs` | `5F6D2DFC6507298F6968E0276C724ABCCAA9484ECCA9C9779996D5EC75C5B9B3` |
| `managed/Ncma.Rendering.Tests/RegisteredStagesTests.cs` | `2712801D7FF1044111A5036239414F65A128302D5484F645D3CB1E55DAED7BE8` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `7775727CCDD5922D627A55C76FDBBCE6C182A95A2356C986F119CAF14F24E837` |
| `managed/Ncma.Rendering.Tests/SkinSceneTests.cs` | `8FECEFDDC5992D5220903C406A2F41C0B456C56B71399D7183404EE70657CCC4` |
| `managed/Ncma.Rendering.Tests/AnimatedShadowTests.cs` | `E4AE919B8DC508446FF856CED73BFDA71248EAEC01FAA9560DEAF3E5ED023992` |
| `tests/plugins/RendererNativeTests.cpp` | `AB8F6B746C2F4576060638E38B0C243B5DC2E9C54CFDC0DD84E525CA21C5BE81` |

提交仅含C2源码／测试／计划／交付和持续上下文，排除生成部署、日志、备份及四份无关IDE改动；提交／推送以此记录所在Git提交和独立remote main核验为准。remote SHA一致后再进入C3。
C3/C4、正式宿主默认切换、shader运行包、UI/MCP编译授权和推理未实现。人工／用户材料／目标／自包含／性能／1h均保持开放，日志／备份／SDK／用户数据／IDE不得清理。
