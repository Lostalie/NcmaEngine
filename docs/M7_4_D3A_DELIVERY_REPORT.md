# M7.4-D3-A 交付记录

2026-10-14。开工本地/远端以及提交前复核基线均为 `d2884bef9c477ca9ce57d5cdfb2b0b64f1752fb4`。

## 本片范围

正式 C# Player/Editor 可信启动管线工厂、两种 shadow 变体启动前实际准入、工厂与延迟 Build 的 committed World 只读/线程/非重入/renderer 离帧边界；plain 定制场景也离帧准备。失败不冒用旧画面。默认 null、Null/空场景无 3D 扩展调用、原事务/权限/Undo/Play 隔离保持。

见[运行合同](M7_4_D3A_RUNTIME_CONTRACT.md)。没有新增 native ABI/Shader 算法、远程代码加载、Agent 权限、Python gameplay 或后端。**本片不是整个 D3 或 M7.4 完成**。

## 定向测试及保留失败

- 初次新增测试编译发现 nullable 实参，修正测试空值说明后零警告；初次定向命令误用了不存在的 `out/build/debug` 插件目录，按现有 `windows-ninja-debug` 路径修正调用，没有增加回退。
- 源布局最终 156 项检查后进入首轮全量 Debug。复核及独立子进程复现发现测试共用方法触发 JIT 提前解析另一宿主程序集：Editor 产品本来就不应有 `NcmaPlayer.dll`，Player 本来就不应有 `NcmaEngine.dll`。保留严格 ALC/产品清单，不添加错误产品依赖；拆分 non-inline 宿主方法。
- 首轮 `out/m7-4-d3a-full-debug-first.log`：12 native 通过，应用 29/30 通过，仅新增 PipelineHosts 失败；原综合渲染 229.55 秒通过。整轮退出 1，无正式部署，Complete journal 仍为 D2 `876523c9beb14eb0bb59b2d8514d55e4`。本轮运行期间补充了测试隔离和预算断言，不能用它作最终冻结证据。
- 修复后 `out/m7-4-d3a-products-second.log`：599 项检查，证据 `out/verification/m7-4-d3a/products-second/b268a822c42d4788aef73437774a9443`。包括 22 项边界检查、8 组源/搬移宿主 probe 及清单逐项检查，不声称 599 个独立图像 oracle。
- 搬移 probe 在严格独立 ALC 中核对每个实际 Ncma 程序集路径/清单 SHA；保留测试程序集这个明确例外。它是产品 API 测试，不冒充原始 apphost 的定制 CLI。原有 environment-products 真 apphost 测试保留。

## 最终顺序完整回归

冻结下表 14 份源码/构建文件，无 Skip 顺序 Debug 再 Release。

Debug：`out/m7-4-d3a-full-debug-second.log` 退出 0；12 native、30 managed、Python 43，原 M6/M7、格式拒绝、MCP、smokes、inspect、三轮 profile、audit、部署恢复保留。原综合渲染 234.83 秒通过；PipelineHosts 30.73 秒、599 检查通过，证据 `out/verification/m2/pipeline-host-Debug/60f99e54427f440ea9f0129d076818dc`。D1 示例与 D2 联合图像保留通过。

Debug 产品代次 `586d4ef5c3f84db088c70cc60f851e25`：Editor139/PlayerDX11102 清单逐项大小/SHA、各26后处理文件及 CPU-only 预检通过；Null 无 post。正式 139manifest/142actual/142backup，Complete journal `76bcd8e785ad4469aa66d24543321d54`，实际 apphost `--validate-package` 退出0。14份冻结SHA未变；同配置 nativeRenderer 和10份旧 Shader 与D2一致。

Release首轮 `out/m7-4-d3a-full-release-first.log` 退出1：应用29/30通过（新增599项/29.03秒通过），原综合渲染63.37秒处在既有 `EnvironmentProductTests.cs:95` 的首次场景替换失败，`SceneDocumentFiles.Save:60` 抛出 `IOException`（无法删除要被替换的文件）。本轮曾根据后续测试进度误报原综合渲染通过，逐项复核后已立即纠正；只能以上述实际失败日志为准。

现场 `out/verification/m2/render-Release/environment-products/ddafbda9d2304aab95bee7b8757919ca/runtime-static/start.ncmascene` 保留，2993字节、Archive属性、JSON v1/6对象，SHA256 `4291123232F4A3C4CFAA2762FFEEE39E5D818B17DA3FC4CA30B2419401491FB1`；没有生成任何 bad-identity 报告。事后独占只读打开成功，无持续文件占用证据；这不是发生时的句柄记录，不能据此确认根因。CIM进程查询被权限拒绝，没有提升权限强制处理进程。原子保存源码与断言未改、无fallback/retry waiver；正式目录仍是已通过Debug，Complete journal未变。

Release第二轮：`out/m7-4-d3a-full-release-second.log` 退出0，同14份冻结SHA顺序完整无Skip回归通过：12 native、30 managed、Python43及原M6/M7/格式/MCP/smokes/inspect/三轮profile/audit/部署恢复。原综合渲染159.87秒通过，新增PipelineHosts29.72秒/599项通过，证据 `out/verification/m2/pipeline-host-Release/7e518f2316704a7ba8feddf90c1db7b0`。这不是File.Replace根因已修复，调查仍开放。

Release产品代次 `a65fbd740d8f45018fc31ba016f4cf3e`：Editor139/PlayerDX11102清单逐项大小/SHA、各26后处理文件、CPU-only库预检与Null无post通过。正式139manifest/142actual/142backup，Complete journal `c3f1ae88a9104e00b51e917cc4bf92cb`，实际apphost `--validate-package` 退出0；两份本轮142文件备份均逐项核对。14份冻结SHA未变；同配置nativeRenderer与10份旧Shader和D2一致，默认post索引保持 `97330B1CA92BB54CE35B2C5F9E4C4BC7A97B30AF9F79C6436D292FE94A4A17DE`。最终EXE SHA256 `1CFF4AAD7B83278E532655FD057E7110258F92E46943BE821A91F8624A8E0023`，NcmaEngine.dll SHA256 `DA57BC5AE81377C76C22167B0CAAB02D6C7FAEBDF5853D3A811ABA1C467F3A50`。

## 成本口径

每个 Editor mode 暖机8帧、测量16帧；断言无定制回调、管线重建、后处理发布、不可变模型/纹理/环境上传或环境 readback，实际 post resident bytes 等于当前图计划，环境不超过32MiB预算。记录的是完整宿主局部样本而非零分配内核宣称。

定向搬移样本：stage/full 分别分配1,091,712/1,086,208字节（16帧），CPU23.559/20.8729ms，post4,713,080字节、environment47,072字节。GPU0.060064/0.060192ms是 last-valid，无origin-frame，不算独立逐帧测量。完整配置结果见各probe.json，不用此样本承诺跨机器性能。

## 冻结源码 SHA256

| 文件 | SHA256 |
| --- | --- |
| CMakeLists.txt | E7AE4A2E84F6E06E551B9CF38ABDDE84156062C737467F3E6B6B2BA1B526C611 |
| managed/Ncma.Editor.App/EditorSceneEnvironment.cs | 9A19A91F665890763DB32ACBC18A06DE14E149F5081A773097EC38DAD1509046 |
| managed/Ncma.Editor.App/EditorScenePostProcess.cs | 511918AF8189409898390F08599F07B4DACC00963C11EE616ECDEEFFD456D1D5 |
| managed/Ncma.Editor.App/Program.cs | 86069E56995F1B78994B64C0927F9AD4B90939857E43BD3DFA29BEF0CE654E81 |
| managed/Ncma.Player.App/PlayerPresentation.cs | 3DE476343CC1CA8F599E43456C47B698F42067498E994CBEB9DCD5D50DB3B252 |
| managed/Ncma.Player.App/PlayerRunner.cs | CC4BA81F636048781CC620CB0EE0C4F522CD0B5B4F8F12ACE23894048B238AE5 |
| managed/Ncma.Rendering.Scene/ScenePipelineStartup.cs | 0813C9D345474DAB6FE5B665B8A1984BBBEB96B055D550CCEC77CBCCE1898672 |
| managed/Ncma.Rendering.Scene/SceneRenderEnvironment.cs | 5BF4D746C9E9E1A3BAD9992AA52D7DF95C562C95901B2EE2C14F1C4FF5AC2477 |
| managed/Ncma.Rendering.Scene/SceneRenderSession.cs | 5D1EE664A5F4B19C41D02439C4FAFF98927A4F89BE02C66CEFDF0F413D77C2B5 |
| managed/Ncma.Rendering.Tests/EnvironmentProductBootstrap.cs | 20887187E18695B554D1F7237D4EA9DEB3ACA1954627B9C61002C566A3C81E40 |
| managed/Ncma.Rendering.Tests/PipelineHostProbe.cs | EB95CD1016281FACB85674B384B3C7F4FA3A9F58D2E25BCE8EEBEEDB4DACE7FA |
| managed/Ncma.Rendering.Tests/PipelineHostTests.cs | A1E9E393A4243CBE22D8D6B599A567A1334FC74C46A02C72930BD319D1DD0A61 |
| managed/Ncma.Rendering.Tests/PipelineStartupBoundaryTests.cs | 1E092ECD893DC1B9F2E09648578F393B27DE1DA781C0B71F41DD592C72CFC702 |
| managed/Ncma.Rendering.Tests/Program.cs | EB073812FB991A444D5128C9E4CEC947414C2CBB1753D1C2F4E1A7FDCF02AE7D |

## 仍开放

D3 下一片正式场景 UI 产品加载/合成及完整 FBX/IBL/shadow/Bloom/UI 搬移联合与错误闭包拒绝；整个D/M7.4未关闭。人工UI/DPI/第三方可见MCP、用户HDR-FBX/材质、HDR解码、目标机/自包含、完整性能/1h、source-recovery分配及历史间歇空skin-cache/File.Replace调查均保留。所有失败现场、备份和4项无关IDE修改保留。DX11only；Vulkan/OpenGL下一版本；M7→M8→M9。
