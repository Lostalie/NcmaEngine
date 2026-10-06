# M2.8 联合验证与清理预检交付记录

2026-10-05 最新状态：用户已确认调整门槛，以自动回归作为本次默认入口切换和旧入口/桥清理条件。
C# apphost 现部署到 out/bin；27 个旧入口/桥/专属测试文件已移除，19 个旧生成项移入可恢复隔离目录。
部署包含完整包校验、精确目标/锁检查、分代备份、journal、显式恢复和正式路径 smoke。
人工 UI/MCP、自包含目标环境、完整性能/一小时长稳及剩余旧策略审查仍待完成，H8/M2 未关闭。
下方第 1–7 节保留历史证据，关于默认 C++、不允许清理的旧描述已被本次授权覆盖；当前结果见第 8 节。

日期：2026-10-04。状态：**已实施可自动执行的预检与部分替代验证；H8 未通过，M2 未结束。**
默认生产入口未切换，旧入口/桥/对照未删除。本记录不是最终 M2_DELIVERY_REPORT.md。

## 1. 本轮交付

1. 冻结旧 Release 参考输出并增加直接 C# 场景/动作与隔离 Python 动作断言；
   保留已有独立进程对照及全部负例，来源见 tests/assets/m2/README.md。
2. 直接托管 SDK/catalog 身份与 12 次卸载、64 次隔离 Play 生命周期验证。
3. 有限局部测量：0/256/4096 Transform 对象的完整文档复制与缓存对象列表，
   8 次预热+32 次样本、median/p95/max、线程分配和 GC 次数。
4. Build.bat 完整验证末尾自动产生只读预检：源码/包哈希与版本、依赖边界、
   默认旧入口身份、实际词法消费者和原始测试证据。输出在 out/verification/m2-8。
5. 审计严格拒绝路径穿越、链接/reparse、JSON 重复键/非有限数/超预算、包改写、
   未列文件、配置错误与依赖越界；仅已知小日志明确例外。没有开放 MCP 删除工具。
6. manifest schema 升为 11，仅声明预检/冻结测试；H8/清理/正式切换均 false。

## 2. 首批自动回归证据（历史记录）

以下是新增 kernel fixture、直接托管替代案例与三轮 runtime 测量之前的首批结果；
不是当前测试计数。最新双配置结果见第 6 节，原日志与审计保留以便追溯。

Build.bat -Configuration Debug 和 Release 均 exit 0，无 Skip。
每个配置：CTest 8/8 native + 21/21 managed/组合（共29注册项）；Editor Services 38/38、
Player 22/22、Python 39/39；managed/native ABI smoke 和 Python inspect 通过。
DX11 reference/GUI/customization 32循环的图像 max/mean=0/0、validation errors/warnings=0/0，
cached graph 64帧提交的测试托管分配为0；这是既有固定 fixture，不是通用游戏性能保证。
构建日志没有新代码编译 warning/error（dotnet 各项目0警告/0错误）。

| 配置 | canonical 完整日志 | 本次只读审计 |
| --- | --- | --- |
| Debug | out/verification/m2-8-debug.log | out/verification/m2-8/Debug/5d3e7a22680c4bd5b8d501b83d386557/audit.json |
| Release | out/verification/m2-8-release.log | out/verification/m2-8/Release/13fcf91e4e9e421b92d57a3b074b0fac/audit.json |

完整日志 SHA256：
Debug 760F3A6ECE6326692DC695683A3140E3B75EF6165EDA424F71D6ED8DF5D2EFF5；
Release B7789608A0CA091D653E86A7729B9D3F7456AF987AC6779191762AF73B55D04C。

环境：Windows 10 build 19045（Python platform），.NET SDK 9.0.315（TFM net8.0），
Python 3.12.8，Build.bat 的 VS2022 x64 工具链。CIM 硬件/驱动查询被拒绝访问，
不填写猜测的 CPU/GPU/驱动版本；完整硬件验收信息仍待补。
HEAD a1ac9d444063721ed27d5791b059530c49286489，dirty=true；
报告不是 clean-commit 发布证据。本次双配置实际源文件清单哈希均为
1c2e6bbf2d491197dbaf19006cb71bee169fa492ecd102201bbf1cf7c185dced；
其范围不含本交付 md/SDK/生成目录，不是全仓库快照。
包各配置：Editor 63文件、Player Null 34、Player DX11 46，声明的哈希/依赖边界验证通过，
未列日志独立记录；仍 framework-dependent、manualAcceptance/selfContainedVerified/production=false。
默认入口与各次同配置旧 C++ build 的 SHA256 相同，matches_legacy_build=true；
最终 out/bin 为 Release 旧 C++。不存在本轮托管正式入口切换。
CTest 的最后一轮 LastTest.log 仅含后执行的 21 项，不能独称其包含完整 29 项；
完整双轮证据以 canonical build 日志为准。预检不替代 CTest 或真实运行。

## 3. 首批局部性能范围与限制（历史记录）

这批测量仅 current_managed_document_and_cached_list_only；
oldBaselineComparison=false、frameRateAcceptance=false。不含 GUI 绘制、Simulation、
完整 MCP 请求、GPU submit/Present、Physics 批量或同 fixture 旧入口对照。
M1 4096 对象约138–156ms/91MB使用不同 workload，不能直接给出提升百分比。
有限 unload/Play 循环不等于一小时稳定性，不证明全生命周期无泄漏。
完整快照高分配仍需后续优化门禁，不能宣称已满足动作游戏每帧预算。

| workload / 对象数 | Debug median / p95 / max（ms） | Release median / p95 / max（ms） | median线程分配（bytes） |
| --- | --- | --- | --- |
| snapshot_copy / 0 | 0.01075 / 0.0161 / 0.0334 | 0.0074 / 0.0121 / 0.0268 | 3,808 |
| snapshot_copy / 256 | 11.7074 / 15.1366 / 17.6063 | 10.2003 / 14.7013 / 14.7792 | 2,081,376 |
| snapshot_copy / 4096 | 47.4643 / 58.6315 / 58.722 | 42.1506 / 51.7508 / 51.892 | 33,993,936 |
| cached_list_32 / 0 | 0.0012 / 0.0037 / 0.0042 | 0.0006 / 0.0024 / 0.0028 | 552 |
| cached_list_32 / 256 | 0.0089 / 0.0101 / 0.0163 | 0.0071 / 0.008 / 0.0174 | 2,400 |
| cached_list_32 / 4096 | 0.1947 / 0.2047 / 0.2056 | 0.10075 / 0.1343 / 0.1615 | 2,400 |

4096快照32样本 GC次数（Gen0/1/2）：Debug 76/72/32，Release 70/60/27；
原始每项数据在审计目录 performance.json。cached_list_32 指最多32行列表，
每个样本一次 Capture，不是32个GUI帧，更不是完整UI测量。

## 4. 未完成门禁与下一顺序

| 门禁 | 未完成内容 |
| --- | --- |
| H3/H4/H5 | 真机输入法/焦点/跨屏 DPI/最小化/图标，第三方可见 MCP 审批→UI Undo→MCP Redo/撤权 |
| H7 | 自包含目标环境真实运行；Rider/Explorer/正式部署切换与恢复，默认入口仍旧 C++ |
| H8 自动矩阵余项 | 完整同 fixture 新旧性能/ABI/GC/native live 矩阵、旧机制断言逐项替代；不依赖旧宿主的 kernel GPU fixture 已补齐（非独立算法 oracle），旧比较仍保留；全部旧策略/机制断言尚未核定 |
| H8 人工 | 一小时稳定性及受审查的精确清理清单 |
| M2 结束后 | 一次性旧入口/构建/部署清理，随后全量双配置复测 |

[清理预检](M2_8_CLEANUP_AUDIT.md)列出当前消费者和覆盖缺口；词法命中不是删除证明。
自动审计 audit_passed=true 只表示预检成功，始终 h8_accepted=false，
production_promoted=false、cleanup_authorized_by_this_report=false。
不扩展 M3/M4/M5/M6，不虚称 Vulkan/场景物理/Animator图/UI制作与HUD/AIworker 已实现。

## 5. 继续实施：测试替代与性能证据

2026-10-04 新增 kernel-only reference target（无Editor/Host/World/CLR/GUI），
托管渲染测试主参考改为该图片，同时保留旧入口→kernel→managed三方比较。
图像容差max<=4、mean<=0.1不变；要求实际DebugLayer、警告/错误0、内核资源maps0。
该fixture共享数值Shader，不作为独立算法oracle；旧对照没有删除。

新增两组直接托管案例：Export具体值/非法值/种类/成员/候选归属/metadata copies，
以及Fast/Slow/disabled/unattached实际30tick、非空间逻辑对象、12次active reload、
失败预检保留catalog/paused World、旧load-context弱引用释放、旧input epoch拒绝。
业务/机制处置详见 [替代矩阵](M2_8_TEST_REPLACEMENT_MATRIX.md)。

Build末尾复用原M1 Gameplay benchmark与PressureProfiles，无改fixture实现：
0/1/8步×0/64/1024命令共9项，多对象128×9×4、32×9×64、1024×1×1、4096×1×1共4项；
每配置3轮，每轮8预热+32样本。历史JSON从M1原始日志冻结并记录来源hash，
不会从当前实现再生成期望值；clean checkout不依赖out/m1历史日志。
报告输出每轮median/p95/max/线程分配/分层耗时，对3轮持续>10%变化标记review。
小于20微秒的历史median不作耗时回归判定。历史完整硬件/源hash未记录，
所以这是retained fixture历史比较，不是同机同时间旧/新入口整体performance验收。
结构审计通过不等于性能review已处理；H8仍false。

新审计会保存kernel RGBA、fixture.json、render-results.json、3轮原始profile日志与冻结基线，
不只保存可被后续测试覆盖的路径。正式双配置验证结果在下方追加。

## 6. 最新双配置完整回归（2026-10-04）

按 Debug→Release 顺序运行完整 Build.bat，无任何 Skip，两次均 exit 0。
每配置 CTest native 8/8 + managed/组合 22/22，共 30 个不同注册项；
Editor Services 40/40、Player 22/22、Python 41/41，managed/native smoke、Python inspect 通过。
新增审计负例包含重复键、NaN/Infinity 以及 1e999/-1e999 浮点溢出拒绝。
新代码无编译 warning/error；未处理或覆盖用户既有 .vs/.user 等工作区改动。

| 配置 | 完整日志 | 只读审计 |
| --- | --- | --- |
| Debug | out/verification/m2-8-final-debug.log | out/verification/m2-8/Debug/f705c19c2dca4eb8acb2ed1936280301/audit.json |
| Release | out/verification/m2-8-final-release.log | out/verification/m2-8/Release/0e1eebf8e7034c4abed98357682a9519/audit.json |

完整日志 SHA256：
Debug CD2DB85C35B79A46B9B2427B47E40E3236DA1DF0DBA8CE0694398BFE84A31816；
Release 20B9DB7A2F4E606CD9FA929BFD8B0EDD4AF0F2102B398C96121AF37C59FCD522。
两配置源码清单 SHA256 相同：
85a231cf1652f67f41f65b03e4c84d6b7c50c4b8e610cf4f8c010dc96311ae0d。
HEAD 与 dirty 状态、哈希范围限制同第 2 节；该哈希不是全仓库或 clean-commit 发布证明。

两配置 legacy→kernel 和 kernel→managed max/mean 均 0/0；
DX11 32 次 reference/GUI/customization 循环 validation errors/warnings=0/0，
64 次 cached submit 的 owner-thread/process 测试分配均 0。
kernel fixture 的 GPU resource maps=0，不等于独立验证全部 D3D live objects。
render-results 中实际适配器为 NVIDIA GeForce RTX 5060 Ti；driver 字段是原始编码值，
不推断其可读版本，也不据此声称历史完整硬件身份已验证。

每配置 13 项 retained runtime fixture 均完成三轮、每轮 8 预热+32 样本。
报告 environmentFieldsMatch=true、reviewRequired=false：
未出现三轮均超过历史 median 10% 的耗时/线程分配 review 标记，
不等于正式性能门禁通过，p95/max 和完整入口矩阵仍需评审。

| 4096 对象固定步压力 fixture | 历史 median（ms） | 本次三轮 median（ms） | 本次三轮 median 分配（bytes） |
| --- | --- | --- | --- |
| Debug | 156.0802 | 155.6718 / 161.8377 / 153.8488 | 90,806,496 / 90,806,496 / 90,806,512 |
| Release | 138.0619 | 131.0700 / 136.2752 / 131.9582 | 90,806,496 / 90,806,504 / 90,806,504 |

这是完整 AdvanceFrame correctness fixture 的时间/线程分配，不是 GPU 帧时间或 FPS；
高分配/耗时仍不能满足动作游戏每帧预算，不将较低单项值包装为整体性能提升。
历史 fixture 源码/完整硬件身份缺失、非同时旧/新入口等限制同第 5 节。
每次审计已归档 9 份证据并记录 hash，含原始 LastTest.log、局部 performance.json、
kernel fixture.json/RGBA、render-results.json、冻结基线及三轮完整日志。
LastTest.log 仅含最后的 22 个组合注册项，完整 30 项以两轮 canonical 日志为准。

两审计 audit_passed=true，h8_accepted/production_promoted/cleanup_authorized_by_this_report=false。
最终 out/bin/NcmaEngine.exe 仍为 Release 旧 C++ 入口，matches_legacy_build=true；
本轮未删除旧入口、桥、构建目标或测试，也未提交/推送。
下一步先继续核定 Animation/Agent/Script/UI 旧策略测试替代与消费者，
再完成第 4 节真实 UI/MCP、H7 部署恢复、完整性能与一小时人工稳定性门禁。

## 7. 后续源码提交与 EXE 重建（2026-10-05）

用户随后授权 H8/M2.8 提交推送和重新构建 EXE。
候选实现提交 18ae5ca；Release 冷重编译发现测试宏冲突，经 35bc6ac 修复，
使用测试专用前置头保持全部 Release assert，不屏蔽警告、不降低断言。
修复提交后的完整 Debug、Release -CleanNative 均退出 0，
每配置 30 CTest、40 Editor Services、22 Player、41 Python 及 smoke/inspect 全部通过。
DX11 parity max/mean 和 API validation errors/warnings 均为 0/0。
本轮只读审计通过但 H8 acceptance/production/cleanup 仍 false。

最终 Release out/bin/NcmaEngine.exe 已重新生成并与 Release 链接产物 hash 相同。
提交身份、日志/审计路径、SHA256、首次性能 review 和最终结果见
[M2 测试状态第 6–7 节](M2_TEST_STATUS.md)。
本次提交授权只改变源码交付范围，不完成第 4 节人工/生产/长稳门禁，不删除旧入口。

## 8. 正式入口切换与旧桥清理（2026-10-05）

用户明确调整本次门槛：自动回归通过后允许切换和精确清理，保留人工/自包含/长稳待验。
这不是自动审计工具授权，也不关闭整个 H8/M2。当前源码尚未提交/推送；HEAD 为
37a92a2da93cee532b9d6eeb729c001b6887211c，dirty=true，包括本次改动与保留的用户 .vs/.user 设置。

### 入口、部署与退出机制

- 正式 out/bin/NcmaEngine.exe 是 Ncma.Editor.App 的 .NET 8 C# apphost；无参数打开编辑器。
  C# Application/Services/Core/Scene/Gameplay/Runtime 拥有业务与唯一 World；ImGui/GLFW/渲染/物理/资源使用原生插件。
- 全量 Build.bat 先构建 NcmaCore/NcmaNative/NcmaArchitectureTests，完整自动回归和预检通过后才部署；
  SkipTests/SkipManaged/SkipPython 不部署，不会生成一个缺 DLL 的新默认入口。
- 部署整个 manifest/hash 校验包，拒绝未知文件、锁定安装、链接、越界目标与未完成 journal。
  暂存验证→备份→移动完整包→正式路径实际图形 smoke→Complete；失败显式恢复，不静默运行旧入口。
  既有已知、有限日志与偏好保留；不清空 out/ 或用户项目。
- scripts/Recover-Editor.bat 按当前 journal 验证备份原始哈希，恢复上一代安装。
  重复部署保留上一代 journal-Complete/RolledBack.json；首次安装也有单独回归。
  当前 journal generation 为 ae4c96c4166645c78ae1572528994e69，phase=Complete，configuration=Release。
- 27 个旧入口/自定义 CLR、Scene、Gameplay 桥/专属测试源码退出，CMake/solution/NMake/Build 同步解除，
  没有新增兼容转发层。精确清单在 [清理记录第 6 节](M2_8_CLEANUP_AUDIT.md)。
  19 个旧生成文件及两个专属宿主 bin/obj 目录（106+111 文件）移动到
  out/deployment/retired-artifacts-20261005，可恢复；源码可从前一 Git 版本恢复。

### 最终自动回归

先完成 Release -CleanNative 冷构建，再对最终部署逻辑执行 Debug→Release 完整 Build.bat，均 exit 0，无 Skip。
未发现编译 warning/error；未降低图像容差、物理零分配、安全负例或 Release assert。
旧机制退出后 CTest 注册项由 30 变为 24，Editor Services 40→39 仅退出重复实时旧桥比较；
12 个冻结旧场景快照、直接 catalog/Export/12 次 reload/64 次 Play、IPC/stdio 及数值测试继续保留。

| 验证项 | Debug | Release |
| --- | --- | --- |
| CTest（8 native + 16 managed/组合） | 24/24 | 24/24 |
| Editor Services / Player | 39/39 / 22/22 | 39/39 / 22/22 |
| Python unittest / managed-native smoke / inspect | 42/42 / 通过 / 通过 | 42/42 / 通过 / 通过 |
| 部署用例（CTest 内部） | 7/7 | 7/7 |
| 冻结旧图像→kernel / kernel→managed max/mean | 0/0 / 0/0 | 0/0 / 0/0 |
| DX11 32 循环 API errors/warnings | 0/0 | 0/0 |
| 13 项 retained runtime ×3 轮 | 完成，reviewRequired=false | 完成，reviewRequired=false |

| 配置 | 完整日志 | 部署后审计 |
| --- | --- | --- |
| Debug | out/verification/m2-entry-final-debug.log | out/verification/m2-8/Debug/2c3c867017b944eab0ef6816662068fa/audit.json |
| Release | out/verification/m2-entry-final-release.log | out/verification/m2-8/Release/968c7d9434344e43867d1f74d7e53bed/audit.json |

完整日志 SHA256：Debug 4A3ADCEF095CC2395B79D493716F73B4CC9DF241FABB1839E72C637B76189A52；
Release BF2320EB35DCC871F67AF2333C7156B75F3CF8814157444B6647CA7F99C1297A。
两配置源码清单 SHA256：35c6d9aea3df50a76b0860d5469aa6c605503065100fafc888b7205ff3578567，
范围仍不含 docs/SDK/生成目录，不是 clean-commit 全仓库证据。
归档 LastTest.log 仅含最后 16 项，完整 24 项以 canonical 双轮日志为准。

当前 EXE 为 357,376 bytes，SHA256 20BBE1BB216EF3E6003DC8AE045BBC5E89C262004FCD33457ECD7456CF753256，
与本次 Release editor package 的 apphost 一致；.NET apphost 的 Debug/Release EXE 可同哈希，
配置身份由配套 manifest、托管 DLL 与 native plugin 文件清单共同确认，不能只看 EXE hash。
已安装 manifest product=NcmaEngine-editor、production=true、framework-dependent，
manualAcceptance/selfContainedVerified=false；候选包仍独立，未虚称自包含发布。
两审计 audit_passed/production_promoted=true，h8_accepted/cleanup_authorized_by_this_report=false。

### 仍保留的边界

冻结旧图像来自清理前 Release，不由新 Renderer 生成；共享 kernel/Shader 不构成独立算法 oracle。
ActionAnimationWorkspace/AgentCapabilityRegistry/ScriptRuntimeRegistry 和原生 UI/render-graph 策略原型仍有独立测试，
不在本次旧入口/桥清理范围；consolidated_cleanup_completed=false。
H3/H4/H5 真实 UI/MCP、H7 自包含/Explorer/Rider、完整性能与一小时长稳未完成。
本次不会因正式目录切换而宣称 Vulkan、通用场景渲染、场景物理、完整 Animator/UI制作与HUD 或 Python AI worker 已实现。

两个 retired managed 缓存目录移出源码目录后，追加 Python 42/42 与 Release 只读审计通过；
日志 out/verification/m2-entry-post-archive-python.log，审计
out/verification/m2-8/Release/2e0dc328ab214236bc0b6aa8d31f8641/audit.json。
这次只移动 ignored 生成项，未修改最终双配置受测源码或正式安装。

后续用户明确要求“提交推送”，已授权交付本次受测源码、文档和冻结参考。
第 8 节的未提交描述记录该授权之前的状态；提交不包含 out/ 生成包、备份或 .vs/.user 设置，
也不关闭仍待完成的人工、自包含、性能/长稳与剩余旧策略清理门禁。

提交前再次执行 Release 只读审计通过：
out/verification/m2-8/Release/43f83e3363ad4fc284983fa8ddeb579c/audit.json。
当前清单 hash=d183e7bcfc002ca2cb936210efa0a6d59d9b3042cabbb6633780f81492a59c4c；
与最终完整回归清单逐项比较，唯一变化为 AGENTS.md 补充用户提交/推送授权，
可执行源码、构建脚本与冻结参考均未变化。其他本次后续记录仅在 docs/ 中。
