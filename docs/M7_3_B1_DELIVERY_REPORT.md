# M7.3-B1 环境 GPU 资源交付记录

2026-10-10；A 的本地/远端 main `15380f19a5c9d1238d0b125656b29e412c943840` 在开工及提交前核对。B1自动候选完成；仅资源与真实GPU数值回读，不是场景 IBL。B2/B3/C及整个M7.3未完成。见[B分片方案](M7_3_B_IMPLEMENTATION_PLAN.md)、[B1合同](M7_3_B1_RUNTIME_CONTRACT.md)。

## 实现与确切证据

- Renderer独立query14/API1/56B/caps3，input40B/token16B/stats64B，query1–13和旧Shader/Scene/NCS1合同不变。RHI追加两种FP32格式及immutable cube；真实DX11三纹理和linear/clamp sampler，无C++环境业务/World/AI服务。
- C#拥有NCE1 UUID/generation/hash、包引用和renderer-scoped资源；全值复制/复验、RAII完整候选、诊断/有界drain先于noexcept交换，managed记账分配先于native发布。8资源/32MiB payload预算。owner/lease/healthy/off-frame/pureUI/显式可信off-simulation/nonreentry；回调不能卸载renderer或释放正在准备的环境。
- 54托管定向实际GPU检查：独立可区分的全face/mip/LUT texels、70000物理E无half clipping/gamma、最小/最大cube64-full7/irr16/LUT64、替换/旧内容/resize、审批/owner/重入/frame/释放/8资源上限/新renderer/纯UI。全部staging回读最大误差0，实际API错误/警告0/0。1024只读统计调用无上传/发布/回读，不宣称全性能或allocation0验收。
- 原生白盒实际view维度/FP32格式、全部descriptor闭合/最后值与alpha范围、三种部分GPU创建异常、诊断前publication异常、capture迟到异常/short/oversize/null、drain timeout、旧资源/输出/统计保持、释放retry/fail-stop清理/foreign generation。测试fault开关仅测试TU，无production控制/export。C与C++ layout assertions、RHI cube/rowPitch/data检查通过；没有模拟真实设备reset。
- 上传CPU数组不保留；诊断从真实GPU CopyResource/staging/有界event/DO_NOT_WAIT Map获取，完整局部成功后才一次写caller output。包的GpuValidated永远false，host-approved rehash不是可信Cook来源签名。

## 测试与完整回归

首轮定向54项通过后补齐descriptor/RHI负例，最终定向、原生测试均通过；保存compile first/final、targeted first/final等全部日志。冻结16份实现/测试，再顺序完整无Skip Build.bat；最终复核16份SHA没有变化。未削弱旧断言，未知query仅14→15因14新增。

| 配置 | 原生CTest | 托管CTest | Python | Rendering / Editor / Player秒 |
| --- | --- | --- | --- | --- |
| Debug first | 12/12，3.60s | 22/22，370.95s | 43/43，1.127s | 116.23 / 75.92 / 68.76 |
| Release first | 12/12，1.75s | 22/22，302.49s | 43/43，.870s | 105.32 / 71.14 / 66.86 |

日志：`out/m7-3-b1-full-debug-first.log`、`out/m7-3-b1-full-release-first.log`。双配置B1均54项、A123/M7.2 68及原M7.1 Shader/包/18文件UI/M6/实际FBX/NCA/Jolt/skin-shadow/Editor/Player/MCP/严格新旧格式拒绝/smokes/Python inspect/三轮profiles/audits/deployment recovery全部保留并通过。零新增编译警告/错误；旧切片日志pending描述不是后续完成状态。

## Checked deployment

Debug generation `c03053a38a1d493ba6a663b1b12c45e9`、Release `9066360ac9f645a2a82cf94a9e58f473`，Complete journal；各107 manifest/110实际文件完整hash核验。两份110文件backup逐项与journal旧文件尺寸/hash匹配，Release后再次核验早期Debug backup manifest。正式out/bin为本轮完整验证的Release闭包，不将生成部署/备份加入Git。

部署后audit Debug `out/verification/m2-8/Debug/a2ac684869b64e5fa56b47b919e3b37b/audit.json`、Release `out/verification/m2-8/Release/11342444c7634d6da5a5c16c60773d15/audit.json`：audit_passed=true / h8_accepted=false。记录三轮profiles不是完整性能验收。

| Release产物 | SHA-256 |
| --- | --- |
| out/bin/NcmaEngine.exe | D7D7021BC248400EE6C6B00CF61CA2ED51332D824FEA325F1D38631B28983F60 |
| out/bin/Ncma.Assets.dll | E417FBACA54BC6C8635D00807526C96397834E4F594AFF99B215D710B5FC6FCB |
| out/bin/Ncma.Rendering.dll | D878D6EAE01E7A21E43CF0B7860A7839B384492D740216AECD255D01CC1CCE50 |
| out/bin/plugins/NcmaRenderer.dll | F59C2316E2EEA89A4F4054BBD318C337D0FE9B648CB4F109A0007BAF780332E5 |

## 冻结源码

| 文件 | SHA-256 |
| --- | --- |
| `engine/source/plugins/contracts/NcmaEnvironmentGpu.h` | `6977B8D2121869E10DD474CFE0965B4C2DF8B068C155AE819CE174A434D07602` |
| `engine/source/plugins/renderer/EnvironmentGpuKernel.h` | `F6521AABC38BC0F1340811FDFE4F9C72F0D687BE264E2325A387E08C12BB95E0` |
| `engine/source/plugins/renderer/EnvironmentGpuServices.inl` | `BCAE4DF2205D5719C3AC5D67DABEE05A05C91BAA7754136F6628662FE0AF88B5` |
| `engine/source/plugins/renderer/RendererPlugin.cpp` | `5829821272B9D50A82156A465E5BE76214D8BB9CA9977F2CD5F38F48268B38FA` |
| `engine/source/runtime/renderer/rhi/RhiResources.h` | `ACA3DD31ABE5B96C2349642D5A2AA2A46797923E6525D41A1F992CB32E627206` |
| `engine/source/runtime/renderer/rhi/RhiResources.cpp` | `CBB78B08E4F64F15C6887C015FC565E07BF329AB9176174FECC170289312BCE1` |
| `engine/source/runtime/renderer/rhi/d3d11/D3D11RenderBackend.cpp` | `F3E5A97D4153D4FC7A26F062875497BF32F9F3A3801A5DE690FEEBAF7BDBF82B` |
| `managed/Ncma.Assets/EnvironmentData.cs` | `29EC1554469FD1D411A5A2B931B7E717DD956940F88176D3D5456693FAB31387` |
| `managed/Ncma.Rendering/EnvironmentGpuResources.cs` | `B76F061D45D14601DD5B84615AD6D6D411CC1A1A6100E47FE66E5CDACB8E7D4A` |
| `managed/Ncma.Rendering/RendererSession.cs` | `FF95B749CDC83BB7D19E3C97D1EED0569F735A32EB84177536DD68777131B268` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `E105E990AA353A76F6873EFDC6CECF86090F707342FB1A8AE52EC1485BB24713` |
| `managed/Ncma.Rendering.Tests/EnvironmentGpuTests.cs` | `4A8961402D940704AB62A10D682E1AC008BC3DBDE26325ED837EDBD432A85401` |
| `tests/plugins/PluginContractC.c` | `5AFC03940DCC85CFD0DC1641A37B043D812E348889EAD59683497717E45ADF31` |
| `tests/plugins/RendererNativeTests.cpp` | `565CA0E4616ED2E376AC357E79B7D08F0BD69A0B5C94CD914A631E60A3B451F9` |
| `tests/plugins/EnvironmentGpuNativeTests.inl` | `8EF80AB5BB9C0B40A690CE1FA817B6DE21C591694B902831BF0B11ED2307AC6E` |
| `tests/ArchitectureTests.cpp` | `CE48DD3E8F419AAF6367261708D453F339544AE0397B1E767CF4FDCB1A2491C7` |

整个B未关闭：B2场景Shader/绑定/source-free新合同/旋转与强度、B3独立IBL像素积分及联合图像、C正式宿主/资产闭包/可撤销配置仍待实施。B1无默认环境初始化/正式宿主配置，无推理/Python gameplay/Agent编译或GPU写权限/兼容恢复。人工UI/DPI/第三方MCP、真实用户FBX/HDR/材质、目标/自包含/完整性能/1h仍待验收。保存失败/备份/SDK/用户数据/无关IDE设置；提交推送与远端SHA核对后下一片B2。DX11 only，Vulkan/OpenGL下一版本；M7→M8→M9。
