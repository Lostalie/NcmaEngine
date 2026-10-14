# M7.5 软阴影档位：实施与验收方案

2026-10-14开始，起点本地/远端main为 `8ffe91d6e7dbcc0f6948eb71d9324653e5b0f3e4`。M7.5现已完成自动候选验收：47份冻结源码完整顺序无Skip Debug第三轮、Release第一轮及受检部署核验通过，详见文末最终交付。四项IDE改动保留；人工、用户素材、目标机/完整性能/1h门禁仍开放。下文首轮与增量失败记录保留为历史，不覆盖最终状态。

## 已核实现状

正式scene使用 `ScenePipelineKernel` 单张Depth32阴影图（非参考预览的4级联），C# `ShadowVolume`构建光空间矩阵，静态/动画共享已提交的mesh/skin。正式默认1024，PCF3x3，bias .0005、slope .002、distance40。PCF最多9次最近邻深度读取；近似PCSS先3x3 blocker search，再3x3过滤，最多18次，无遮挡早退9次；半径是texel单位并由归一化深度差乘经验系数64、夹在1–8，不是物理面积光源模型。现有ReferenceSettings的PCF5/级联/light angular radius不属于此路径，不能直接复用其名称或值。

默认/用户Scene3DPipeline已经共享图合同，Off无SceneShadow资源/pass；分辨率变更需新scene lease，不能用Configure偷偷更换分配。正式IBL/Bloom与可信factory有离帧批准/只读World/非重入边界，必须保持。现有GPU计时是last-valid且无origin frame，只记录这一限制；不在本阶段伪造逐帧采样归因。

## A：公开C#档位与可核验预算

| 档位 | 分辨率 | 官方过滤 | 深度读取上界/着色像素 | Depth32逻辑字节 |
|---|---:|---|---:|---:|
| Off | 0 | 不执行 | 0 | 0 |
| Low | 512 | PCF3x3 | 9 | 1,048,576 |
| Medium | 1024 | PCF3x3 | 9 | 4,194,304 |
| High | 2048 | 近似PCSS（9搜索+7x7双线性比较） | 205 | 16,777,216 |

Medium保持正式默认数值；Low/High初始bias/slope/distance保持一致以便独立比较分辨率，High blocker搜索半径8texel。数字是官方固定Shader的逻辑预算，不是自定义Shader保证，也不是driver显存或硬件texture transaction计数。LightRadius注明texel，不改名角半径。C#提供不可变选择、实际图构建及纯CPU测试；未知枚举严格拒绝。Off/无可投影主光均不得分配阴影资源或传caster。高级自定义参数仍经原有限范围校验，不能冒称某个预设的固定预算。

初始High使用旧9+9内核，实际图像的大半径3x3过滤出现明显块状/分段半影。现候选High改为独立C#声明的Geometry pixel程序：9次blocker搜索，7x7分布采样、每次用4个深度比较结果双线性插值，最多205次读取（不对深度值本身插值）。近似归一化深度差/经验半径保持，仍非物理PCSS；没有改写原生参考源码/默认字节。该候选的图像与成本需继续完整B/C验收，不能用单项通过称最终画质冻结。

`SceneShadowShaders.ComposeHigh`仅通过公开catalog/descriptor组合替换Geometry pixel，保留原VS/Shadow/Tone，并分别支持Scene3D/SceneEnvironment完整反射合同。High预设图携带精确pixel content-hash依赖；在包装管线后仍保留，创建/Configure/换完整程序时核对，缺少或仍是旧程序在native分配前拒绝。普通Tone更换不伪造Geometry身份。公开用户图可使用同一个`RequireSceneGeometryPixel`合同；声明不是编译、实际反射或批准凭证。

## B：场景配置与正式共享宿主

新增独立严格版本化场景值配置（单场景最多一份），不增加GameObject树、Actor或native业务状态；缺省保持Medium，显式Off关闭。C#读取/验证当前文档和World一致；Editor/Player共用选择、离帧准备与提交参数，Null只验证配置，无native资源。

沿用原EditSession命令/Undo/Redo，Inspector可选Off/Low/Medium/High并显示实际预算与近似算法说明。Agent只提出同一受限数据命令；原权限、预期revision、事务、撤销和冻结规则不变，不新增代码执行或GPU权限。未知配置/多个配置/运行时任意写入拒绝或要求显式重准备，不保留被标成新配置的旧阴影图。

重配置先完整准备新lease/程序变体、再离帧交换，失败保留旧有效状态或明确fail-stop；有租约不能强卸载。Play独立场景，Stop返回Edit；原动画姿态、IBL、Bloom、HUD、custom stage/full graph都使用同一选定阴影配置。可信自定义图需精确匹配选定分辨率和shadow presence；不假定任意用户Shader符合官方采样预算，标为custom/unknown。

High程序必须加入真实source-free闭包：静态/蒙皮、标准/IBL、各Tone/Bloom组合均要离线Cook、精确文件/包hash与整组GPU准入，正式运行时不得为补High重新编译HLSL或打开作者源码。原默认Shader文件与后处理库可保留独立合同，但不能把旧Geometry包绑定到High；扩展包的版本、预算、索引、Editor/Player部署/搬移验证须与B一起完成。纯UI产品不部署这些3D包。

## C：图像、生命周期与成本证据

- 原阴影/动画/IBL/后处理/UI测试原样保留；新增Off/3档、近平面/远处、接触/分离、slope bias、off-camera caster、alpha-mask、自阴影与漏光夹具。固定ROI和事先数值容限；独立CPU投影/深度比较/滤波参考，不仅比较同Shader输出不同。
- ASCII/Binary FBX→NCA的真实动画使用同一GPU蒙皮结果进入geometry/shadow；至少64姿态、移动主光/相机、resize和启停；不能把静态四边形测试当动作游戏阴影验收。
- 标准/IBL/Bloom/HUD、默认/stage/full管线，受检搬移Editor/Player/Null及实际apphost，原命令/Undo/Redo/3Play-Reload-Stop，错误配置启动前拒绝，API0/0。
- 每档记录逻辑depth预算与实际native resident计数增量，暖态CPU/线程分配/ABI复制/上传/创建/geometry与caster draw；画面捕获和准备成本与暖态分开。没有origin帧的GPU时间标注last-valid，不累计成独立样本。不将逻辑texture字节称完整显存。
- Off和纯2D不能初始化阴影pass/map/sampler/program；关闭后全部lease回收。先测量，目标机预算未确认就保持性能验收待定，不以提高阈值获得通过。

## 阶段交付门禁

A/B/C全部完成→定向测试/修复→冻结源码→完整顺序无Skip Debug/Release Build.bat→原native/managed/Python/严格格式/MCP/smokes/inspect/3轮profile/audit/recovery→产品manifest逐项hash/备份/Complete journal/实际EXE→交付记录/路线图/AGENTS→提交推送及远端SHA核对→M7.6。

本阶段仅DX11。无C++World、新ABI业务策略、Python gameplay、推理服务或传输实现。保留人工UI/IME/DPI/第三方MCP、用户素材/HDR解码、目标机/自包含/完整性能/1h及历史File.Replace/empty-cache/source-recovery调查；不自动关闭这些门禁。

## 首轮执行证据（未完成阶段）

本节描述旧18读取基线；后续高档候选与当前源码状态见下一节。早期“下一增量先解决过滤”的文字不覆盖后续38项独立整图验证。

- 新增 `SceneShadowPresets`不可变纯C#声明，单独CPU58检查（不加载Platform/Renderer）；独立CTest `NcmaRenderingTests.ShadowPresets`已注册，标准Build筛选包含此组。
- 规范Debug/Release `Build.bat -SkipTests`通过，日志 `out/m7-5-debug-preparation-first.log`、`out/m7-5-release-preparation-first.log`；候选分别 `4e8a90007f77492880f59e2583745bcc`、`6754e1e0efd64073b1c1504d2bfb9989`。这不是全回归，不部署正式exe。
- 两配置定向79检查均通过：日志 `out/m7-5-shadow-debug-first.log`、`out/m7-5-shadow-release-first.log`；证据 `out/verification/m2/shadow-presets-debug-first/125beba8e85a4713b5ddbaa7bc0fac8f`、`out/verification/m2/shadow-presets-release-first/087225d3d95f43cc90c459c73de2b803`。另独立运行CPU58以及原Shader合同双配置回归通过。
- 每档256x256夹具的native计数字节分别786840/1835416/4981144/17564056，减去相同scene HDR/depth/constants后精确对应0/1/4/16MiB阴影图。每档8帧预热后32帧测量，线程分配0、新pipeline创建0、资源重复上传0；Off caster draws0，其余每帧1；全部释放回到baseline；API0/0。这不是完整Editor分配或driver显存证明。
- Low/High图像已实际查看：High大半径仍有明显阶梯分段；本79项仅证明基线预算/可见投影/生命周期，不证明独立像素oracle、动画档位、正式宿主选项或High画质验收。下一增量先解决过滤质量并补独立数值参考，不能用“有阴影”验收软阴影高档。
- 尚未完成B/C、全阶段冻结无Skip双配置、M7.5提交推送。正式部署保持M7.4-D3-C，实际EXE SHA `820A0874617DD57090868CB5FA3F9A129B2B8D64D7895CF2B3A3F3811C51C3F5`未变；构建/测试进程均已exit0。

## 高档过滤与精确程序依赖增量（阶段仍未完成）

- 新增独立 `SceneShadowShaders`，只替换注册组的Geometry pixel，保留原VS/Shadow/Tone。Scene3D与SceneEnvironment分别严格匹配400/416字节常量合同；先真实编译/反射，再Cook无源码NCS并整组重新准入。测试包不是正式部署文件。
- 高档由3x3点比较改为7x7位置分布、4比较双线性插值，上界205读取；blocker部分仍9次，保留旧经验半径的近似性质。实际查看初轮新High图像，较旧3x3明显减轻分段；不能据此声称所有素材/动画/性能门禁通过。
- 公开 `RequireSceneGeometryPixel`将精确内容hash放进编译图；图缓存会因依赖变化失效，而已编译图保持不可变。High预设按指定Scene3D或SceneEnvironment profile自动加入依赖，外层pipeline包装不丢失；缺失/旧程序在native分配前拒绝，Configure和换程序拒绝后原图/程序保留。原生ABI无新增字段，普通公开图默认行为不变。
- 新增独立解析矩形texel深度参考，不读取GPU深度，不解析生产Shader。测试6种场景×稀疏/密集2程序，共12整图：共面自阴影、接触、近/远分离、子像素偏移、边界；深度比较、双线性权重和PBR分别CPU计算，全通道误差上限事先为2，最终最大1。另Environment-Off整图与标准profile精确一致；这不是完整动画或IBL-On/用户素材oracle。
- 首两次图像失败保留：`out/m7-5-high-image-debug-{first,second}.log`，contact-sparse最大误差164在(175,175)，GPU与double CPU落在点采样整数边界的两侧。采用固定四分之一texel light projection偏移、解析深度逆映射同步后验证整图；不排除像素、不放宽阈值、不修改产品路径。另 `out/m7-5-high-contract-build-second.log`为ClearPipeline测试构造形参误用，修正后0警告/0错误。
- 最终规范Skip构建通过：`out/m7-5-high-debug-preparation.log`候选 `8c899598bd14481aa0e970e1e702dc33`、`out/m7-5-high-release-preparation.log`候选 `8ca56d56a3414d919d27196d40d2d800`，没有正式部署。Debug CTest两个新增组通过（4.57秒），Release两个新增组加原定制示例通过（5.87秒）；日志 `out/m7-5-high-ctest-{debug,release}.log`。
- 两配置均为91项档位检查（含70纯CPU），38项过滤检查/12整图/最大误差1/API0/0。最终Debug证据 `out/verification/m2/shadow-presets-Debug/d6a0c8fdae6445d898d4554e85ca71f9`、`shadow-filter-images-Debug/fdc26078e7b04560a5fd0f80a4e29b92`；Release同目录前缀分别 `shadow-presets-Release/59c2b409664e40769e5c664eac484c3c`、`shadow-filter-images-Release/5779d0bf93974ab09b7c2f6a9041066f`。
- 原Shader合同70、已注册Geometry/Shadow/Skin40、环境绑定46均双配置通过；原公开定制示例98 Release通过。结果不代替尚未进行的完整冻结无Skip门禁。正式原生默认Shader和post库仍旧独立包，本轮High包只在测试目录。
- 下一步B：场景选择值、共享正式宿主/原Undo/Inspector、真正的High static/skin、standard/IBL、Tone/Bloom无源码运行包与部署/搬移；之后C完整动画/生命周期/成本。所有本增量测试进程exit0，未提交推送M7.5。

## B 的无源码阴影库增量（历史记录，正式宿主接入见下节）

- 新增严格 `ShadowLibrarySelection` v1：固定4份Scene3D/SceneEnvironment×static/skin高档包，8192字节索引、每包8MiB/合计32MiB上限，未知/重复字段、路径别名、版本、profile/skin不符全部拒绝。`RuntimeShadowLibrary`使用原受检文件/目录pin；显式外部索引SHA或正式manifest固定索引SHA，没有搜索、作者源码或降级回退。
- `Ncma.Shader.Cook`离线生成4份High NCS及索引，完整实际反射准入后才结束；`Package-M2_7.ps1`将精确5文件加入Editor/DX11 Player候选manifest。Null和纯UI不部署。原9个程序包、原24 Tone/Bloom/索引以及原生插件不改写；Release对正式旧安装的44个原文件逐项SHA相同。
- 新增纯CPU `RuntimeShaderPackage.ComposeGeometryPixel`，从已固定的High包替换Geometry pixel，其余非Tone字节必须与basis一致，Tone保留basis；重新生成完整NCS并重新Preflight，仍不是GPU/权限凭证。`PreparedShadowLibrary`离帧整组实际反射、初末批准、owner/renderer/pin生命周期验证；批准期间不能关闭库或重入。缓存最多16个内容身份（4默认+12官方Tone组合），满则明确拒绝、不静默驱逐活跃借用；旧scene文件pin也必须持续有效。官方库不隐式接受任意不同VS/Shadow/Skin，用户完整程序仍需自己的可信离线声明及准入。
- 新增独立CTest `NcmaRenderingTests.ShadowLibrary`，最终两配置180检查：独立Editor/Player cook字节确定性、纯CPU子进程无native/compiler/Editor、文件pin/修改/缺失/硬链接/manifest负例、撤销/最后一次批准/跨线程/回调关闭与重入、纯UI拒绝、缓存预算等。错误SV_TARGET1程序先被作者身份拒绝；随后夹具故意伪造声明hash仍由实际整组反射拒绝，未分配scene资源。声明hash不是安全沙箱或算法证明。
- 12份High与3种Tone×2种profile×static/skin整组组合实际创建、配合Bloom渲染整张常量HDR图并与独立Tone/Half标量参考比较，最大通道误差0、API0/0、释放后scene/post/skin资源归零。这验证程序闭包/Tone/Bloom不被替换污染，不声称这些常量图验证了动画投影或IBL-On的软阴影画质。
- 真实候选Editor/Player manifest整体校验通过；搬移5份阴影文件及manifest，独占锁定原包阴影文件后，搬移库和独立CPU子进程仍只读取副本成功。Null的3D Shader/阴影目录均不存在。**这是运行库搬移验证，不是搬移产品程序集/正式宿主或真实动画联合验收。**
- 规范Skip构建通过：`out/m7-5-library-debug-preparation.log`候选 `e4c4ecb248aa4142bfbe1f7591834e08`，`out/m7-5-library-release-preparation.log`候选 `5328ac1f230a49979aa72b7d78279fa6`。Debug候选后只补测试中的manifest/搬移库检查，测试目标重新编译；生产源码未变。所有新编译0警告。保留第二次测试构建CS0136局部row命名冲突日志，修复后通过，未修改产品守卫。
- 两配置CTest原PostLibrary149 + ShadowPresets91（CPU70）+ ShadowFilterImages38/12图/max1 + ShadowLibrary180/12图/max0全通过，分别71.18s/37.70s；日志 `out/m7-5-library-ctest-{debug,release}.log`。新库证据 `out/verification/m2/shadow-library-Debug/5613d8569734429ea4582f4af446431f`、`shadow-library-Release/d85852340de549f98cb7baca772fa0ad`。所有本轮进程exit0。
- 正式 `out/bin` EXE仍SHA `820A0874617DD57090868CB5FA3F9A129B2B8D64D7895CF2B3A3F3811C51C3F5`，DLL仍 `9AC812B9FBA130178AAB1C55F7EBB92E9F05D449C446A22031611ED8AAD31D45`。未执行本阶段最终冻结无Skip双配置、未部署、未提交推送。
- 下一步完成B：严格单场景质量值、共享Editor/Player离帧选择与交换、Tone高档解析、原Inspector/Undo/Agent数据命令；之后C真实64姿态FBX投影、默认/stage/full/IBL/Bloom/HUD和正式搬移产品/生命周期/成本。不能把本运行库增量视为整个B或M7.5交付。全部既有人工/用户素材/目标机/性能/1h及历史失败调查保持待验收。

## B 的场景、正式宿主与 Inspector 增量（当前，M7.5仍未交付）

- `SceneShadowQuality`移至无图形依赖的`Ncma.Assets`，显式固定Off/Low/Medium/High数值0/1/2/3；没有旧namespace别名。新增`ncma.render.shadow` v1纯标量组件`version/quality`，两个字段均必填，未知字段/版本/枚举拒绝，`runtimeAttachable:false`，最多一份（含Off）。缺省不合成组件：官方路径为Medium，未显式选档的用户管线保持自身政策、成本未知，不冒称Medium。场景registry14项，原10项保留，未增加Rendering依赖。
- 场景组合验证、文件Save/Load、只读World身份/committed/owner边界与原`ncma.scene.transaction`、Undo/Redo接通。新增5组CPU测试，连同原Scene.Rendering合计113组；非法请求、重复配置、撤销/重做、旧request重试都检查文档/历史不被错误改写。Agent仍默认无写入权限，只接受原session/revision/object/component scope批准，不新增GPU、脚本执行或文件权限。
- `SceneShadowPipelinePolicy`为正式默认、阶段定制和完整用户图提供同一显式档位约束：Off无阴影图，Low512/Medium1024/High2048，High声明精确profile的Geometry pixel；自定义图与显式分辨率/presence不符在分配前拒绝。显式档位不能被Submit临时参数覆盖。未选择档位的自定义图不被强改为1024，其采样成本仍需独立测量。
- `SceneRenderSession`比较启动snapshot与真实World选择；只有离帧准备成功并交换完整候选后才提交新档位，失败保留旧lease但拒绝按新配置渲染。普通/环境/后处理共享相同有效阴影开关与预设参数；High从受检运行库组合已有Tone，最终绑定前仍验证。空/裁剪视图不把旧图标成新配置，恢复可见后必须准备；尺寸变化与配置变化一致处理。显式Off不编码caster或分配map；静态High暖态64帧Submit线程分配0、无重建，仅局部成本证据。
- 项目可选`shadowLibrary:{version:1,indexHash}`固定索引/外部SHA；未选时正式宿主使用部署manifest，不搜索项目或工作区回退。Editor在离帧可信边界首次需要High时加载并借用不可变库，Edit/Play关闭后再释放pin；Player在游戏启动前做CPU文件验证、整组反射与16x16实际管线预检，错误hash/自定义图拒绝后tick0/session空。Null只验证场景/项目值，不读取GPU阴影文件，不部署或加载渲染资源。
- Inspector在同一场景对象上新增四档按钮和添加Medium入口；显示预设Depth32逻辑预算、205读取上界及近似PCSS/自定义成本说明，不声称完整显存或物理算法。所有按钮使用原transaction，删除使用原组件删除；Play冻结，不增加独立历史。实际源Editor验证Off/High像素不同、Undo/Redo恢复逐字节相同图像，原scoped Agent命令、非法值拒绝及3轮Play/Reload/Stop保护Edit。
- 新CTest `ShadowScene` 48检查（四档实际map/resident、失败恢复、空视图、显式参数拒绝、用户图256保留/显式Medium拒绝、High+Tone/Bloom、环境开启后精确profile切换、Off保留IBL/Bloom/释放阴影、API0/0）；`ShadowHosts` 39检查（实际源Player/Editor/Null、Inspector合成控件事件、原历史及3轮生命周期）。这里没有把平面静态夹具当64姿态动画、没有把源程序集API探针当搬移产品/原生OS输入验收。
- 保留首轮CPU测试失败：非法值夹具误用会提前验证的registry.Encode，改为原始JSON请求后事务层确实拒绝且113通过。另Release单独构建Scene.Tests后，子进程旧Editor不识别新`shadowLibrary`字段；日志`out/m7-5-scene-shadow-release.log`保留，未改断言，规范Release全依赖重建后原子进程测试通过。
- 规范Skip候选Release `9b1ba09df0b948809455e73e1317e65b`、Debug `93f6d7ac7eb84ede84613ff6fc9fe939`，日志`out/m7-5-shadow-host-{release,debug}-preparation.log`；没有正式部署。两配置同十组CTest全通过（Release113.26s/Debug126.74s）：原PostScene53、PostAuthorHosts、Application17、Editor154、Scene.Rendering113和阴影91/38/180/48/39，日志`out/m7-5-shadow-host-ctest-{release,debug}.log`。独立Debug原EnvironmentRenderSession87/零暖态分配、PipelineHosts源158、PostScene53/零分配、PostAuthorHosts89也通过。新共享宿主证据为`shadow-scene-Debug/3a957d366a3541f495c12680a5eba7d6`及`shadow-scene-Release/232da5366f1c4b798a82c50b1cb11173`，正式源宿主为`shadow-hosts-Debug/ebedc590f00d452da6c82a323a485a51`及`shadow-hosts-Release/8f9fc536c9984d74b691100cb336be0a`（均在`out/verification/m2`）。本轮所有测试/构建进程已exit0。
- 正式out/bin仍M7.4-D3-C，EXE/DLL SHA与上一节相同；44份原native/Shader/post文件逐项未变。本增量不是全阶段冻结无Skip Debug/Release，尚未提交推送。下一步仍需C的真实ASCII/Binary FBX64姿态/独立软阴影图像及alpha/slope/边界、默认/stage/full/IBL/Bloom/HUD搬移产品与成本联合，补齐最后负例和生命周期，再跑全量门禁。所有历史人工/目标机/用户素材/完整性能/1h与失败调查保留。

## C 动画、边界与搬移联合增量（最终全量门禁进行前）

- 新增独立CTest `ShadowAnimation`：真实ASCII/Binary FBX→NCA各四档×64姿态，共512组整图CPU/GPU蒙皮对照；独立CPU加权位置/逆转置法线/切线，最大通道误差1（既定上限2），不是独立光照/滤波算法证明。所有启用档位每姿态都验证整个另一个角色在相机右平面外，单独移除该caster会改变接收面；不是把画面内自阴影当off-camera证明。不同姿态、移动光源/相机、128/160尺寸重建、精确旧帧拒绝和完整释放均覆盖。暖态64帧仅皮肤调度/提交/Present，线程分配0，无pipeline重建/静态上传；计数、上传及last-valid GPU时间单列，不称完整性能通过。Debug第二轮3016检查通过，日志`out/m7-5-shadow-animation-debug-second.log`，证据`shadow-animation-debug-second/6894e10574b9466aaac3f4abea23672b`。
- 新增独立CTest `ShadowBoundaryImages`：四档×10场景=40张全通道图，与独立解析texel覆盖/alpha裁剪/深度比较/3x3 PCF或双线性比较7x7/标量PBR参考对照；涵盖完全透明、半透明遮罩、共面、接触、法线slope bias、边界/体积外、近裁剪面外caster、子像素。全部最大误差1（上限2），无像素排除。Debug第二轮81检查/API0/0，日志`out/m7-5-shadow-boundary-debug-second.log`，证据`shadow-boundary-debug-second/6a05d6cbeb3e4bd1bca0cc875410e69a`。
- 新增独立CTest `ShadowJointProducts`，复用而不移除原D3-C无显式质量联合测试。High配置、真实两种FBX/NCA、静态接收面、IBL、Bloom、HUD，受检搬移Editor/Player/Null程序集与native路径/manifest SHA，原始作者/运行资源独占锁定；实际apphost成功。默认/自定义阶段/完整自定义图均测试，High/Off原事务、精确Undo/Redo、64固定步动画、3Play/Reload/Stop、HUD不二次Tone、resize、暖态资源复用。Debug第二轮866外层检查通过，日志`out/m7-5-shadow-joint-debug-second.log`，证据`shadow-joint-debug-second/5b7a627b6309456a9724a504121b38b8`。后续补入无效质量/缺失显式库启动拒绝及Null不打开无关GPU库，需由最新候选重新验收。
- 保留失败：动画测试初编译类型/Span/Png形参错误（前三份build日志）修复后零警告；边界第一轮把局部normal当世界normal，Off即误差65，按既定非均匀模型逆转置修正夹具，未改产品或阈值；搬移first/diagnostic中原D3-C只有角色无接收面，Off计数2→2正确但图像相同，添加真实打包静态接收面后原像素差异断言通过。没有屏蔽失败断言或降低门禁。
- 另补共享公开policy未知ShaderProfile在custom factory回调前拒绝，原共享组新增两检查。上述首轮证据尚不覆盖这项最后生产变更，须规范最新候选与完整门禁。正式EXE/DLL仍旧hash；未部署/提交/推送。所有既有人工/用户素材/目标机/完整性能/1h门禁不因自动候选改变。

## 最后定向门禁与运行合同

- Debug规范Skip候选`44ea9f45bbe84ceb93de6abfdf196ec3`已包含最后production profile守卫，日志`out/m7-5-final-debug-preparation.log`。九组CTest（原SceneUiJointProducts + 八组Shadow）全通过，108.26秒，日志`out/m7-5-final-targeted-debug.log`；共享组50、源宿主39、动画3016、边界81、完整搬移联合902（负例包括未知quality、显式缺失库，Null只验证值不读取库），原联合仍原样运行。最终定向搬移证据`shadow-joint-products-Debug/c7d0830762ab43529e7a22107f385124`，每个Editor探针137检查。仍非最终全量Debug/Release。
- 冻结47份实际源码/测试/构建文件于`out/verification/m7-5/source-freeze.json`，完整门禁前后逐SHA复核；IDE/.user及文档状态不进入此源码冻结。当前完整Debug日志`out/m7-5-full-debug-first.log`进行中，结束之前不得宣称通过或部署。
- 场景保存的唯一设置为`ncma.render.shadow`版本1组件，数据严格`{"version":1,"quality":0|1|2|3}`，场景最多一个，包括Off。运行handle不序列化。编辑器使用原命令、撤销重做及权限；自定义启动factory仍是可信C#代码，不是场景或Agent可注入的delegate。
- 显式预设约束管线presence/分辨率；未选配置保留用户自定义图。High shader只替换Geometry pixel，要求Scene3D/Environment精确身份；库四变体（各自static/skin）仍整组反射，声明hash不是算法安全证明。配合Tone由`ComposeGeometryPixel`保留已有Tone、比较其余非pixel字节，最大16个准备结果，不自动驱逐。
- 正式3D Editor/Player新增`assets/shadows/defaults.json`与四份NCS，manifest或项目`shadowLibrary:{"version":1,"indexHash":"<SHA256>"}`固定索引；未找到或不匹配即拒绝，不读作者HLSL、不在线编译、不回退到原High近似内核。纯UI/Null不部署该目录。初始及最终批准、owner-thread、文件pin与借用scene lease不跨卸载。
- 画质范围：单主方向光/单图，近似PCSS，不是级联/物理面积光、完整接触阴影或任意用户素材保证。205是官方高档最多深度读取，不是硬件texture transactions；0/1/4/16MiB为Depth32逻辑字节，不是完整显存。GPU时间无origin frame，只标last-valid；目标机完整性能仍待验收。

## 完整Debug首次超时与默认准备优化

- `out/m7-5-full-debug-first.log`失败并未部署：12原生通过，47联合中46通过，原`NcmaRenderingTests`在240.05秒超时，联合总749.84秒。超时前没有数值/图像断言失败，已推进到M6.7旧动画联合，不能据此把未运行完的断言视为通过。
- 同一47份首冻结源码、原240秒阈值独立复测`out/m7-5-original-render-debug-recheck.log`通过，原组238.10秒。此前D3-C Debug原组236.86秒，说明旧总组的截止余量很小；本次单次超时根因未证实，不声称已修复机器/缓存/调度问题，不扩大timeout、不删除/拆分或跳过原断言。
- 代码核对发现本增量额外使未显式选档、无环境/后处理/自定义管线的默认路径每次进入离帧提取。恢复原lazy默认路径，显式配置及显式→缺省的交换仍离帧准备；新增64次默认Prepare零分配断言。该优化避免不必要工作，不把它直接归因为超时唯一根因。需重新冻结最后源码并重跑完整双配置；首冻结与所有失败证据保留。

## 第二轮全回归与耗时诊断（尚未通过最终门禁）

- 第二份47文件冻结 `out/verification/m7-5/source-freeze-second.json` 仅上述默认准备和对应测试两文件改变。Debug第二轮候选 `0af47e2c503e425cb789bab8bdee749d` 原生12组通过，联合45/47通过；原渲染总组240.03秒、原Player总组120.25秒超时，总812.18秒。日志 `out/m7-5-full-debug-second.log`；没有执行后续Python/profile/audit/部署，不把完成的子检查计为整个总组通过。
- 原完整渲染命令加外部stdout时间戳，诊断运行全部断言通过但耗时261.37秒，仍超过原240秒门禁；日志 `out/m7-5-render-diagnostic-timing.log`。旧环境光照/搬移产品在160.11秒结束，旧动画联合至248.44秒，最终32次renderer生命周期在261.34秒结束。没有单项断言卡死的证据，也没有证实唯一超时根因。
- 同样方式原完整Player56项全部通过，73.89秒，日志 `out/m7-5-player-diagnostic-timing.log`；不能替代整轮CTest结果。诊断时可用物理内存约19GiB、没有MSBuild nodemode残留，不支持当时内存不足假设；没有关闭任何用户进程或修改机器设置。
- 两份诊断都已退出0，但均仅定位证据。全部原断言、图像容限、循环次数、原CTest240/120秒均保留，没有拆组或Skip。第三轮同一冻结源码完整Debug `out/m7-5-full-debug-third.log` 正在运行，最终结果及后续Release须另行核实。正式out/bin仍上阶段D3-C，M7.5未提交推送。

## 最终 Debug 门禁（Release 尚待完成）

- 同一第二份47文件冻结，第三轮完整无Skip Debug通过，日志 `out/m7-5-full-debug-third.log`：12原生/47联合组/Python44、严格格式/MCP/原Editor与Player/smokes/inspect、3轮保留profile、审计及部署恢复全部通过。47组总728.51秒，原渲染233.86秒、Player68.07秒，均保留原240/120秒门限。前两轮失败和261.37秒诊断保留，未宣称超时根因已修复。
- Debug候选 `64403f18704a405c8317eecbc6c85a59`：Editor144/Player-DX11113/Null56项逐项大小/SHA核对通过，前两者各新增5份阴影文件，Null为0；原46份native/Shader/post/environment清单文件与D3-C同配置SHA相同。第二份47源码冻结仍一致。
- 受检部署Complete `f60575fa0fcc4ff1ac0a17552a29b9b9`，144manifest/148actual，143份恢复备份逐项核验。实际 `out/bin/NcmaEngine.exe --validate-package` exit0；EXE SHA `5487DF87B13019C24AF2E19C6EB1DB767E04E950AA47ED32E6C7A76619068C76`，入口DLL SHA `0F10824D91290E835A9494BD24ECEED9B83BD3ED7BFA3E65B9E0835AAF04692B`。记录 `out/m7-5-debug-deployment-check.json`。
- 两次Debug审计audit_passed=true、h8_accepted=false，原人工/目标环境/性能/长稳门禁仍开放。同源码完整Release `out/m7-5-full-release-first.log`已启动，尚不能将M7.5标记交付或提交推送。

## 最终 Release 与 M7.5 自动交付

- 同一第二份47文件冻结的完整无Skip Release第一轮通过，日志 `out/m7-5-full-release-first.log`：12原生/47联合组/Python44、所有原测试/严格格式/MCP/smokes/inspect、3轮profile、审计及恢复通过。47组总564.45秒；原渲染165.47秒、Player67.05秒。两配置均未放宽原240/120秒门限、未拆组、未减少断言或循环次数。Debug两次超时及独立诊断全部保留，根因未证实；本次通过不是稳定性问题已根治的声明。
- 最终Release候选 `7005fcd6c50c494bab39640caf7721fa`：Editor144/Player-DX11113/Null56项逐项大小/SHA通过；新增阴影闭包分别5/5/0文件。原46份native/Shader/post/environment文件与D3-C同配置SHA完全一致。47份冻结源码在最终包核验后仍一致，没有原生ABI或默认Shader变更。
- Complete部署 `4b507c258a5a4648ad626d63a5465eca`，144manifest/148actual；148份Debug恢复备份逐项核对通过。实际 `out/bin/NcmaEngine.exe --validate-package` exit0，EXE SHA `5487DF87B13019C24AF2E19C6EB1DB767E04E950AA47ED32E6C7A76619068C76`，Release入口DLL SHA `B4AD9CDC660ABF4E4AF416AC6241EF427ABDC8783D3F4FC0E1112ADB2C814D6A`。核验记录 `out/m7-5-release-deployment-check.json`。
- 最终Release定向证据（均在全回归中重跑）：档位91/CPU70，独立过滤38/12图/max1，库180/12 Tone-Bloom图/max0，共享场景51/暖态Submit与默认Prepare各64帧分配0，源宿主39，真实动画3016/512图对/max1，边界81/40图/max1，以及High搬移联合902；原SceneUiJointProducts仍保留。动画证据 `shadow-animation-Release/0926fd8147b34949a707d89174eae1d3`，边界 `shadow-boundary-Release/fa8c881f227f468aaadac76e116d2c9b`，根为 `out/verification/m2`。这些成本不是整个Editor或目标机性能验收。
- Release前后审计audit_passed=true、h8_accepted=false。仅提交受检源码与文档，排除out产品/日志/备份及四项既有IDE设置；提交推送并核对远端后进入M7.6资源更新与故障恢复。本版本仍仅DX11；没有新增推理服务、Python gameplay或Agent写入特权。原人工UI/IME/DPI/MCP、用户HDR/FBX/HDR解码、目标机/自包含/完整性能/1h以及历史File.Replace/empty-cache/source-recovery调查均未被本阶段关闭。
