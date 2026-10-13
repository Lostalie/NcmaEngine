# M7.4-D2 联合图像交付记录

2026-10-14。开工本地/远端 main 均为 `273ebd662ee2f4db607abdb9e3a208976d96d030`。测试证据边界见[图像合同](M7_4_D2_IMAGE_CONTRACT.md)；完整 D 范围见[分片方案](M7_4_D_IMPLEMENTATION_PLAN.md)。生产代码、原生 ABI、Shader、默认资源及 AI 权限均未改动。

## 定向验证

最终定向日志 `out/m7-4-d2-target-final.log`，证据目录 `out/verification/m7-4-d2/final/cd06879d39c34e12a3c40e89a45adfff`：582 次实际检查，120 张独立后处理/UI整图对照，最大误差 1 个 8 位色阶，API error/warning 0/0。原36个真实ASCII/Binary FBX→NCA动画姿态、CPU四权重所有顶点标量、静态导入三角形IBL独立采样、原阴影/环境消融保持通过。新联合测试覆盖静态模型和9个实际clip中点、两种尺寸、默认/全图替换、三档曝光。

40行暖态测量共640次提交尝试，619次接受、21次正常palette Busy；managed分配合计0、无不可变资源上传/后处理重新发布，各16次尝试中最长6.9712ms。诊断图像先明确drain再读回；计时区域无drain/读回/重试，仅使用已准备palette，不含姿态求解、UI提交或资源构建。这不是完整性能或无背压承诺，也不是640个独立GPU时间样本。

## 修复与保留失败

- 初始测试误用不存在的scene.Generation属性与可空对照图，修正为实际资源身份和非空检查；0警告编译通过。
- 0.001原始float clear转换得到相邻Half，校准改用可精确表示的Half输入。clear合同仅0–1；HDR测量改用全屏自发光材质，遵守原材质0–16范围，不扩大生产接口。
- 首次整图误差26定位到UI图元角部的导数抗锯齿；将scissor置于更大图元内部，精确隔离颜色域/顺序/透明度/裁剪参考，保留原AA测试，不放宽整图阈值3。
- 暖态快速蒙皮提交出现正常三槽Busy；改用原TryUpdateSkins跳过该次呈现并明确统计。诊断提交在drain后仍Busy则失败，绝不使用旧图。没有向runtime/tick添加同步等待。
- 第八次目标测试通过后复核修正compound-assignment覆盖回调计数问题（早期166不是最终总数），补强两种FBX与静态覆盖和旧图拒绝。最终582项通过，全部早期日志/失败PNG保留。

## 完整门禁

首轮完整Debug的原综合渲染在 `SkinSceneTests.cs:193` 暖态零分配断言失败：`4256; count=0, differentMesh=True, warmFrames=32`，日志 `out/m7-4-d2-full-debug-first.log` 保留。该路径及生产代码未改，本轮D2独立CTest仍通过（17.05秒）。初步静态检查确认空场景走缓存提取/无动画/GPU提交路径；尚无分配调用栈，根因未确认，不把再次通过写成已修复，不更改零分配断言或旧测试超时。

首轮其余28项通过、整轮退出1，正式Complete journal仍为D1的 `b071486db69c4d15b41ba20608a945a0`，没有部署失败候选。四份冻结SHA未变，第二轮原综合渲染224.97秒通过（含原零分配断言）。

最终完整无Skip Debug第二轮退出0：`out/m7-4-d2-full-debug-second.log`。12项native、29项managed/application、Python43及managed/native smoke、严格格式/旧格式拒绝、M6/M7/MCP/inspect、三轮profile、审计、部署恢复均通过。原A538/B172/B3 353/环境产品594/D1 98、Editor154、动画与Player门禁保留；post共享53/宿主70/作者宿主85次检查通过（宿主次数包含动态采样帧）。D2证据 `out/verification/m2/joint-post-Debug/a5a568b0bf474cd687999c0c0c41856c`：582次/120图/max1/API0/0，独立CTest16.23秒。

Debug产品代次 `3020977c32ed4b6eb1a90fbfad1eab23`：Editor139/PlayerDX11102清单逐文件大小/SHA核对，各26后处理文件/CPU-only库预检通过，Null无post。正式清单139/实际142/备份142逐项核对Complete journal `fd33a5b84f9a44caad141e2926a0393a`，实际apphost `--validate-package` 退出0。4份冻结SHA未变，同配置nativeRenderer和10份旧shader与D1一致，post索引SHA不变。

该完整Debug的D2暖态640次尝试全部接受、Busy0、managed分配0；与定向轮619/21的差异明确保留，不能把有界背压统计写成固定吞吐保证。

随后顺序完整无Skip Release首轮退出0：`out/m7-4-d2-full-release-first.log`。12项native、29项managed/application、Python43、Editor154/Animation115/Player56及全部上述旧门禁、三轮profile/audits/部署恢复通过；综合渲染160.97秒。D2独立CTest5.52秒，证据 `out/verification/m2/joint-post-Release/7188a2472734475b80614e3fe682b2ce`：582次/120图/max1/API0/0；40行暖态640尝试/625接受/15 Busy/分配0，单行16次尝试最长4.0601ms。原post共享53/宿主68/作者宿主93通过。

Release产品代次 `d22568deacf349cfbb9ebc998ca56bbf`：Editor139/PlayerDX11102清单逐文件大小/SHA和各26后处理文件、CPU-only预检通过，Null无post。正式清单139/实际142/备份142逐项核对Complete journal `876523c9beb14eb0bb59b2d8514d55e4`；实际apphost校验退出0，四份冻结SHA未变。两份142文件恢复备份保留。同配置nativeRenderer、10份旧Shader、默认NCE和post索引与D1一致。生产源码未改不代表托管程序集字节应不变，构建版本/仓库修订等元数据仍按本轮实际清单核对。

| 最终Release文件 | SHA256 |
| --- | --- |
| out/bin/NcmaEngine.exe | 10EFB74F03E0D15D598F1C37577E2477A44EEACDCE60E13B8914575A6C6DB965 |
| out/bin/NcmaEngine.dll | 2289DB28A10555CDED79C6E63BAD9E4004B7E9D28D8BADD0A08F19869F6A20C2 |
| out/bin/plugins/NcmaRenderer.dll | D5A8F1B4DF7160A114503645E06DCEE73B72FA873174CD880E5F230A31A83C92 |
| out/bin/assets/environments/neutral-v1.nce | 96441592ADD2FA59D9FF8612F1D46ED24783D5FAE234F17306E3D768D13F1665 |
| out/bin/assets/postprocess/defaults.json | 97330B1CA92BB54CE35B2C5F9E4C4BC7A97B30AF9F79C6436D292FE94A4A17DE |

D2自动候选完成；提交推送并核对远端后进入D3，不关闭整个D/M7.4。首轮旧分配失败及历史File.Replace间歇失败继续保留，不宣称其根因已经解决。

## 冻结源码 SHA256

| 文件 | SHA256 |
| --- | --- |
| CMakeLists.txt | 26EA0A38CB3DD108057D94BE9EF8ED7F795B64E0496BF4E921A46962A97EE20A |
| managed/Ncma.Rendering.Tests/Program.cs | 67AFA9D61565A9F3380F9D462472542E4856284424FE15BA5107B704C2A3A0FB |
| managed/Ncma.Rendering.Tests/EnvironmentJointImageTests.cs | 03A1C509F2E78FE30B3448D0E035C858F9CEF1E0CDF3D45F7E83E14A2E430306 |
| managed/Ncma.Rendering.Tests/JointPostImageTests.cs | 8F7E50AF8D55846AE90C76639B27CB1FBFE95B5AB445141BDCC311980D490353 |

## 保留门禁

D3搬移正式产品/宿主生命周期与成本仍待完成，整个D/M7.4不关闭。测量HDR只作为独立Bloom/Tone/UI参考的输入，不声称完整独立CPU PBR/阴影光栅oracle。NCA重读是同进程，非产品重启。人工UI/DPI/第三方MCP、用户HDR-FBX素材/HDR解码、目标机/自包含、完整性能/1h、source-recovery高分配及历史间歇File.Replace根因调查保留。DX11only；Vulkan/OpenGL下一版本；M7→M8→M9。
