# M7.3-A 交付记录

日期：2026-10-10。开工前核对 M7.2 main/remote `a9162d067646dd4f80208ec6f0b6f68db21fc2b5`。范围见[分片方案](M7_3_IMPLEMENTATION_PLAN.md)和[数值/包合同](M7_3_A_RUNTIME_CONTRACT.md)。

## 实现范围

C#复制线性HDR源/UUID/hash/显式Cook参数，新增不可变source-free NCE1 v1包和独立可信expectedHash预检。Renderer query13/API1/32B只提供有界离线CPU数值工作，原query1–12与GPU shader binding不变；输入48B、metadata32B、caller-owned float输出，完整候选完成后才复制。无GPU环境资源、World、推理、Python游戏脚本或新Agent执行权限。

Eigen数值kernel计算物理irradiance E、GGX specular mip cube、IBL BRDF A/B LUT；线性HDR最高65504，E允许65504*pi。算法方向/粗糙度网格和split-sum近似在合同锁定。Service持module lease、owner/非重入/显式off-simulation审批/native off-frame健康检查，pureUI renderer拒绝；取消检查在有界native调用前后，不能抢占调用。单条immutable缓存命中也复验权限/边界；失败、撤权、取消不替换旧结果。

配置仅独立值，尚未注册scene命令/组件/项目字段；HDR/EXR文件解码、真实GPU IBL和正式宿主闭包属于后续范围。包hash不是Cook来源、签名或GPU凭证，GpuValidated始终false，重hash合法格式案例明确验证此限制。

## 测试与失败保留

首次SkipTests编译因原生测试缺少numbers头文件失败，`out/m7-3-a-debug-compile-first.log`保留；同时修正未存在的UI factory调用。第二次编译通过，不部署。首次定向109项通过。补充边界测试时第三次编译因测试局部变量重名失败，`out/m7-3-a-debug-compile-third.log`保留；修正后第四次编译通过，零警告/错误。扩展后121项、最终123项和原生测试均通过，日志 `out/m7-3-a-cook-first/second/final.log`、`out/m7-3-a-native-first.log`保留。没有降低数值容限或删除原断言。

最终123项包含常量全部cube/mip、linear HDR最大值/黑环境/默认预设、方向解析卷积、独立均匀半球BRDF积分、LUT解析能量与有限网格扫描；严格包/版本/预算/alpha/负零/重hash非法值/副本、独立两个无Renderer/Platform/Gui插件子进程、插件关闭后的并发读取；owner、lease阻止卸载、纯UI、真实活动帧、重入、撤权/取消后保留旧cache、重试不命中取消结果。原生短表/短输出/描述/非法值/owner/context/active/fail-stop/busy/计算后注入异常原子拒绝也通过。真实活动帧夹具API0/0不代表IBL绘制。

| 指标 | Debug / Release |
| --- | --- |
| 常量 E=pi*L 最大误差 | 3.4969112050475815e-7 |
| 方向解析 E 最大误差 | .0009407997131347656（容限.02） |
| BRDF独立均匀半球积分最大误差 | .0031036734580993652（容限.02） |
| 检查网格最大A+B | .9984945058822632（容限1.03；不覆盖全部grazing边界） |
| 环境GPU安装 / 人工HDR素材验收 | false / false |

冻结11份实现/构建/测试后，顺序完整无Skip Build.bat：

| 配置 | 原生CTest | 托管CTest | Python | 渲染 / Editor / Player秒 |
| --- | --- | --- | --- | --- |
| Debug first | 12/12，3.65s | 22/22，358.49s | 43/43，1.087s | 108.25 / 74.97 / 66.87 |
| Release first | 12/12，1.74s | 22/22，299.26s | 43/43，.856s | 102.62 / 71.55 / 66.20 |

日志 `out/m7-3-a-full-debug-first.log`、`out/m7-3-a-full-release-first.log`。零新增编译警告/错误；原 M7.1 A70/B41/C1 24/C2 40/C3 32/C4-A77/B42/C62、M7.2材质68项（独立oracle1、static/compute skin1、API0/0）、18文件独立UI、M6/实际FBX/NCA/Jolt/动画/skin-shadow/Editor/Player/MCP/新旧格式拒绝/smokes/Python inspect、三轮保留profiles和部署恢复全部通过。原日志中的历史切片pending提示不改变已有后续自动候选完成记录。

## 部署与备份

Debug generation `bf0af0bf5ed143c1920e920fc2e4c0aa`、Release `18bcc29472f54f17b96b440a9bc48c72`；各107 manifest/110实际文件，Complete journal。两份110文件备份保留；Debug和Release备份均逐项尺寸/hash核对，最终再次验证早期Debug备份的manifest。正式 `out/bin/NcmaEngine.exe`及程序集/插件为本轮完整检查通过的Release包。

部署后审计 Debug `out/verification/m2-8/Debug/e571b463ccc242afbf992ee5a4a17544/audit.json`、Release `out/verification/m2-8/Release/9034b5da36854ad9bc20081018dc4b17/audit.json`，audit_passed=true / h8_accepted=false；三轮profile只是保留证据，不是完整性能验收。

| Release产物 | SHA-256 |
| --- | --- |
| out/bin/NcmaEngine.exe | EF5374C3E9B5A64A1BE2F746A2CBE8D871B122FEC8565090A4B617ABB17C3A60 |
| out/bin/Ncma.Assets.dll | 7443F29261176FC535CF4D81226C3ECFEFB574B321C2DC20A7709C1890FB99CB |
| out/bin/Ncma.Rendering.dll | 9E90414EE652684D47E4E78A08B6898E0A8141A6FC79826BE33E58B500C453B9 |
| out/bin/plugins/NcmaRenderer.dll | D2EC4B84C1AD9DC5A6BDFF3CF038C41693390511C6D450590796651DA2C418A7 |

## 冻结源码

完整双配置和部署后核对以下11份文件未改变，随后仅同步文档/路线图/持续化上下文。

| 文件 | SHA-256 |
| --- | --- |
| `CMakeLists.txt` | `C528C7AD488BE5C88A93A5B76F1F6DBFB6E51BFF15CEEBDA33C6A928201A13B5` |
| `engine/source/plugins/contracts/NcmaEnvironmentCook.h` | `D62E55C6F618B0C578238A494B3097DA5E81BF6F53FFFE56F7DA3C772501C4A2` |
| `engine/source/plugins/renderer/EnvironmentCookKernel.h` | `128FBF1AAD0C769C41D497273F2BD377CDB8987F34D977BA393655FE44A09D73` |
| `engine/source/plugins/renderer/EnvironmentCookKernel.cpp` | `BA8F1B24E7C2C6E5C394A7C0A584FD6E4915D18295D06BDF478EC396FD217B48` |
| `engine/source/plugins/renderer/EnvironmentCookServices.inl` | `91710E3023AF955C97945195845F44476E57CCADC65834F7C3F561EBE9C18C25` |
| `engine/source/plugins/renderer/RendererPlugin.cpp` | `4203C38B56D9E5E8D19CA9B4045677606D8C467D1C7DF4F582C04357B42FCBB1` |
| `managed/Ncma.Assets/EnvironmentData.cs` | `BE3E9305976BCC343556EFF20E3A39CF465011F8313B1D688551F39FE1317B32` |
| `managed/Ncma.Rendering/EnvironmentCookService.cs` | `99307B804D119F164A7ADE862D39001157583CD8E767BE73009F743E935437D2` |
| `managed/Ncma.Rendering.Tests/Program.cs` | `3B6BEE0250A74F33D0A4DA66EEB6BD93A3A7F7D8CFACE546225119B3864DE3C5` |
| `managed/Ncma.Rendering.Tests/EnvironmentCookTests.cs` | `B2FED7883E24A4400BAFBF0FDC9FC25410EEE6ECE8DE8EA680A0D0CCFEDD6FC8` |
| `tests/plugins/RendererNativeTests.cpp` | `4C00D96BD6309505815677E8FF3C73172CFC52DECBD938C412078DFAB2B33BB3` |

M7.3-A自动候选闭合；B真实DX11 IBL绑定/像素、C正式宿主/资产闭包/配置命令仍未实现，整个M7.3未完成。人工UI/DPI/第三方MCP、真实用户FBX/HDR/材质、目标机、自包含、完整性能、1h长跑仍待验收。DX11 only，Vulkan/OpenGL下一版本；M7→M8→M9。保存所有失败/备份/SDK/用户数据/无关IDE设置，提交不含生成部署。提交推送/远端核对后下一片M7.3-B。
