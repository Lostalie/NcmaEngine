# M7.2 交付记录

日期：2026-10-10。开工前核对 M7.1 远端 main 为 `3345ab9cfecec7d91cb991f8b37f2f4103c72047`；提交前再次确认该基线。范围见 [实施方案](M7_2_IMPLEMENTATION_PLAN.md) 和 [材质合同](M7_2_RUNTIME_CONTRACT.md)。

## 实现结果

- C# 单一六槽语义/通道合同；显式 ORM/MRA、四种封闭预设、复制的绑定行和有界缺图诊断。线性色值不重复解码，Color RGB 使用 sRGB、alpha/Normal/Data 保持线性。
- MaterialCodec、运行资产、NCP1、GPU 缓存和直接创建材质共用 role 验证。同 UUID 不相容槽拒绝；错误实际纹理语义不能通过缺 UV/切线或关闭 normal map 绕过。缓存完整校验后才创建候选。
- PresetInput 生成原 ncma.assets.material.edit 文档输入；原确切权限、asset revision、磁盘事务和唯一 Undo/Redo 保留。提供受控 API，材质面板人工操作仍待验收。
- 现有正 affine 数值路径复用，native ABI/shader 无变更。镜像 UV 符号、normalY、normalScale、正非均匀对象/单骨变换及静态/实际 compute skin 同图已验证。负缩放/奇异矩阵仍拒绝，原拒绝测试保留。
- DX11 only；无新增 Agent 源码/编译/GPU 写/自审批权限，无 Python 游戏逻辑或推理服务。

## 测试与失败记录

首次 SkipTests 构建仅检查/暂存，未部署。资产定向 40/40、场景资产/运行包定向 79/79 通过。首次 GPU 夹具误将 skin 更新和绘制使用不同帧号，被原 native 同帧校验拒绝；日志 `out/m7-2-gpu-first.log` 保留。修正夹具后定向 65 项通过；随后补三项直接材质语义检查，最终 68 项进入完整回归。曾尝试的负行列式 shader 修正已撤回：原合同禁止负缩放，保留原限制，不扩大变换支持范围。

最终源文件冻结后依次运行完整无 Skip 的 Build.bat：

| 配置 | 原生 CTest | 托管 CTest | Python | 渲染 / Editor / Player 秒 |
| --- | --- | --- | --- | --- |
| Debug first | 12/12，4.18s | 22/22，358.69s | 43/43，1.132s | 99.39 / 77.33 / 68.53 |
| Release first | 12/12，1.77s | 22/22，301.44s | 43/43，.891s | 102.32 / 72.31 / 66.75 |

日志 `out/m7-2-full-debug-first.log`、`out/m7-2-full-release-first.log`。两轮均无新增编译警告/错误；M7.2 68 项真实 GPU 检查：独立 CPU GGX/ACES/sRGB 探针最大误差 1（容限3），静态/compute skin 全图最大误差1（容限1），DX11 API 0错误/0警告。覆盖 base/emissive sRGB、ORM/MRA 线性通道、alpha .5/.51、24种 TBN/单骨组合、错误语义原子拒绝、稳定帧创建/上传计数。Assets 40/40、Scene rendering 79/79（含新两项严格运行包/缺图测试）均通过。

原 M7.1 A70/B41/C1 24/C2 40/C3 32/C4-A77/B42/C62、独立18文件UI apphost、原 M6/实际 FBX/NCA/Jolt/动画/skin-shadow/Editor/搬移Player/MCP/格式拒绝/smoke/inspect、三轮保留profile和部署恢复测试全部保留通过。

## 部署核验

Debug generation `e46a939e0d6b4cb58dc984f3ed488ad2`，Release generation `95c48c0deb5a4fd5b1953a7149f6a350`。两轮均107 manifest/110实际文件、Complete journal；两份110文件旧备份保留，逐项尺寸/hash检查通过。Release旧备份与journal.oldFiles逐项匹配，更早Debug部署前备份的manifest也再次验证。

部署后审计 Debug `out/verification/m2-8/Debug/d510446a3cc543c392363af0aa0bb952/audit.json`、Release `out/verification/m2-8/Release/1ee3d9639f964914b7f29facbc168acf/audit.json` 均 audit_passed=true / h8_accepted=false。当前 `out/bin/NcmaEngine.exe` 及其插件/托管程序集为本轮检查通过的Release包。

| Release产物 | SHA-256 |
| --- | --- |
| out/bin/NcmaEngine.exe | B67B0C7485FDA30CD632ABEAC8631FF5D42C063B78241DB43D05F93A0CE13626 |
| out/bin/Ncma.Rendering.dll | 45F564CEF2AD68551E59771D2D293D5AD837B77AFF25A149A65AA1EBA4B9405F |
| out/bin/Ncma.Assets.dll | 67DD724F3DFB5A0CF6E834DA6377C7C983600E6A84A8F2846C13928866950B79 |
| out/bin/plugins/NcmaRenderer.dll | 08D67E31A469BF1270983B2B58C84396858550171D57F4DD8078BA9945135DF2 |

## 冻结源码

以下11份实现/测试在完整双配置前冻结，最终部署后再次核对未改变。随后仅同步文档/路线图/持续化上下文。

| 文件 | SHA-256 |
| --- | --- |
| `managed/Ncma.Assets.Authoring/MaterialCommands.cs` | `616B761638074480B6196680D91D5A9BF600A7CBC5384D45F3ACB003071859C0` |
| `managed/Ncma.Assets.Runtime/RuntimeAssetLoader.cs` | `C662168A5290F28BC8F68CCB049F22D87E527D1D5390AC1C763E7BC4A0951B37` |
| `managed/Ncma.Assets.Runtime/RuntimeAssetPackage.cs` | `43E462932E5BE252C8B8C6F68B7759E88866B786D1F3567F6CAA73F2A27555BA` |
| `managed/Ncma.Assets.Tests/Program.cs` | `2ED47B6361FC0906D0D73E9B7787C940465F47EE1870C8D96120D78A8FE418F9` |
| `managed/Ncma.Assets/MaterialData.cs` | `067C20324B2AED8BD427BA6152E7E4447CFB5DAFA4E9EEE94B700484B16E5C59` |
| `managed/Ncma.Assets/MaterialSurfaceContract.cs` | `BF5C11B673EC3ADC28E9337A4250A6BF7A2A74BC2CFC601944AABD272B54F0FA` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `3B367F06197EAFB92C7E6D4D7037761A4B093CB57D21299A5DA084AD25852375` |
| `managed/Ncma.Rendering.Tests/MaterialSurfaceTests.cs` | `47D10164E446D80A807D4884196430AB9BF91D038F86A0D9B8C6717BBFE85F71` |
| `managed/Ncma.Rendering/RenderResourceCache.cs` | `6A4974C7F4B7BDE801038AFE8E12B6F03383C03ACDF89DD69E69E8F53364AD1A` |
| `managed/Ncma.Rendering/ResourceRendering.cs` | `A7B71C3CCD0D85E6DADAA7EB769AE9F78B9179B39901B61A0346044EA85AE4E6` |
| `managed/Ncma.Scene.Rendering.Tests/RuntimePackageTests.cs` | `D0642D13488FA3FE76FB557445C16C5144CDF3AB25CB9D3FF43E492B4CF14C53` |

M7.2 自动候选闭合，M7.3–M7.7尚未实现。材质面板人工/UI/DPI/第三方MCP、真实用户素材/FBX、目标机、自包含、完整性能和1h长跑仍待验收。缓存计数不是完整性能验收，ambient不是IBL。保存所有失败/备份/SDK/用户数据和无关IDE设置；提交不包含生成部署与备份。提交推送并核对远端后，下一阶段为M7.3环境光与IBL。
