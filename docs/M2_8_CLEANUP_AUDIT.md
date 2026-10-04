# M2.8 旧入口清理预检

2026-10-05 更新：用户“调整”授权已允许自动回归后的正式切换与精确旧入口/桥清理。
该授权不是来自自动审计报告。27 个源码/专属测试文件已移除，构建/solution/NMake/部署消费者同步解除；
19 个生成项隔离备份，未删除 SDK、共享内核、用户资产或 .vs/.user。详细当前清单见第 6 节。
ActionAnimationWorkspace、AgentCapabilityRegistry、ScriptRuntimeRegistry 及原生 UI/render-graph 策略原型仍有独立测试，
尚未整体清理；H8 全体验收和 consolidated_cleanup_completed 继续 false。下方第 1–5 节为历史预检。

日期：2026-10-04。状态：**预检已实施，清单尚未核定；没有执行删除。**
本文件不是删除授权。H1–H8 全部自动/人工通过后，按用户要求统一清理并复测。

## 1. 审计方法与边界

运行 Build.bat 完整双配置时执行 python -m ncma_tools.m2_audit，输出独立 UUID 目录。
报告包含源码内容哈希、HEAD、dirty 状态、ProjectReference 闭包、候选包依赖/大小/哈希、
旧标识符消费者以及原始 CTest 日志和当前局部测量。
源码词法搜索覆盖 managed、engine/source、tests、python、scripts 和指定根构建入口，
排除 SDK/生成目录；它不是语义 dead-code 证明，未覆盖文档、任意动态反射字符串或外部消费者。
非排除范围内链接/reparse 拒绝，不跟随；JSON 拒绝重复键/非有限值/超过 4 MiB。
测试及源文件/日志读取有资源预算。不能因词法命中数决定删除，不能批量清空目录。

Editor 的项目/发布依赖不得含 Ncma.Managed.Host；Player 还不得含 Editor/Gui。
包内 manifest 每个文件须匹配配置、大小、哈希、无重复路径；未知额外文件拒绝。
仅 Editor 的两个已知 out/user/logs/editor-candidate.jsonl[.1]、各至多 1 MiB 可作为
独立记录的运行日志，不豁免其他 out/ 文件或 DLL。
manifest metadata 校验不等于实际插件启动/真实图形或人工验收；实际运行由已有组合测试验证。

双配置本次词法报告分别命中 comparison 3、editor 9、host 35、policy 11 个路径记录；
组间可重复，包含测试/项目/部署消费者，**不是58个删除目标**。
实际记录与哈希见 M2_8_DELIVERY_REPORT.md 中 UUID 审计目录；异常编码列表为空。

## 2. 精确源码组与消费者处置

下表是已经找到的具体旧源码，不是已批准删除项。共用类型或内核须拆分，不删除整目录。

| 旧项（项目相对路径） | 当前消费者/阻止条件 | 替代与处置 |
| --- | --- | --- |
| engine/source/editor/EditorMain.cpp, EditorApplication.h, EditorApplication.cpp, EditorAnimation.cpp, EditorFbxCharacter.cpp | 默认 out/bin、CMake/NMake、图形参考 fixture；H5/H7 未过 | C# Editor.App/Services 已有候选，全部保留到统一清理 |
| engine/source/runtime/script/runtime/ManagedHost.h, ManagedHost.cpp | hostfxr CLR 引导、旧入口、ManagedHostTests/ManagedSceneBridgeTests、对照目标 | C# apphost/Scripting 候选；直接 catalog/Play 测试已补，机制测试的语义覆盖尚需逐条核对 |
| engine/source/runtime/scene/ManagedSceneClient.h, ManagedSceneClient.cpp | 旧入口及桥测试/Scene reference | 直接托管 SceneDocument/EditorSessionOwner；新增冻结 12 快照，保留独立进程对照 |
| engine/source/runtime/script/runtime/DotNetGameplayRuntime.h, DotNetGameplayRuntime.cpp; engine/source/runtime/interop/NcmaGameplayBridge.h | 旧入口 Play、脚本/桥测试；旧宿主共享协议 | PlaySession/RuntimeSessionOwner/Scripting；不能删除 Behaviour SDK/Gameplay/Runtime |
| managed/Ncma.Managed.Host/NativeEntry.cs, SceneEntry.cs 及该项目其他包装 | 旧二进制桥、旧宿主测试、Build 部署 managed/ | 新 apphost 不依赖该项目；先核对所有包装，再核定项目/solution/Build 的精确移除清单 |
| engine/source/runtime/animation/ActionAnimationWorkspace.h, ActionAnimationWorkspace.cpp | 旧 Editor 动作策略、tests/AnimationTests.cpp、--action-reference | C#/Python 隔离工具策略+数值 ABI 2；16 快照覆盖一条序列，不替代全部数值/策略负例 |
| engine/source/runtime/ai/AgentCapabilityRegistry.h, AgentCapabilityRegistry.cpp; engine/source/runtime/script/runtime/ScriptRuntimeRegistry.h, ScriptRuntimeRegistry.cpp | 旧入口能力/脚本注册及架构测试 | managed capabilities/catalog 已有；仍需逐条核对引用，不为移除建立转发桥 |
| tests/plugins/LegacySceneReference.cpp | 独立进程场景 parity | 冻结场景断言已落地；统一清理前审查覆盖，再移除对照 fixture 与 CMake 依赖 |
| tests/plugins/LegacyReferenceCapture.cpp | DX11 RGBA parity，依赖整个旧 Editor/Host | 已新增内核独立 capture 和旧/内核/托管三方对照；共享数值Shader，不是独立算法oracle；旧比较仍保留至统一评审 |
| tests/ManagedHostTests.cpp, tests/ManagedSceneBridgeTests.cpp | hostfxr/ABI5/6 机制与业务语义 | 固定布局/错误边界/重载/失败原子性/实际 MCP 等断言逐项迁移后才核定，不删整个测试集 |

## 3. 已增加的替代证据

- 冻结旧 Release 输出：场景 12 个完整文档/状态快照；动作初始+15命令共16快照。
  来源/哈希见 tests/assets/m2/README.md，不依赖运行旧 Scene bridge 或旧 Workspace。
- 直接 C# catalog：SDK 类型身份、3 Export、lease 拒绝 Clear、12 次弱引用 unload。
- 64 次隔离 Play start/advance/pause/step/stop：tick 正确，Edit 内容不变并解除冻结。
- Python 冻结动作对照；旧子进程对照仍保留。
- 新测试只覆盖上述断言；原 Runtime/Scene/Core/Gameplay/IPC/物理/插件测试保留。

本轮继续增加直接 Export 具体值/负例、失败 catalog load、12 次活动重载 unload 与输入代次失效。
GPU 对照使用新独立 fixture；机制退休与业务替代的逐组核对见 [测试替代矩阵](M2_8_TEST_REPLACEMENT_MATRIX.md)。
这补齐已知部分缺口，不表示全部旧测试可删除。
运行时复用 M1 9 个步数/命令 + 4 个多对象 fixture，3轮×8预热/32样本；
历史基线已冻结为tracked JSON，不让clean checkout依赖ignored out/m1历史。
报告对持续>10%的耗时/分配变化标记review，不据历史环境字段虚称同机并行新旧入口测试。

## 4. 待核定的构建/部署项

CMakeLists.txt、NcmaEngine.sln、NcmaEngine.vcxproj、scripts/Build.ps1、LaunchEditor.cmd
存在旧入口/桥/对照目标依赖；需要与源码同时变更，不能先删除或留坏 target。
out/bin/NcmaEngine.exe 本轮仍由 canonical Build 部署旧 C++；报告与同配置旧产物核对哈希。
out/bin/managed 的旧包装、历史生成对照程序只在最终清理时枚举精确文件，
不得以 out/、managed/、engine/ 为递归删除目标。NcmaNative 的 ABI 2 animation/character
仍被 Editor/Python 使用；保留合法资源 DLL，不按名字认定为废弃。
OpenGL/D3D12/Vulkan 骨架需要独立全消费者审查，本轮未核定也未删除。
.vs、.user、SDK、用户资产和无关修改不在清理范围。

## 5. 关闭条件

收到真实 H3/H4/H5 UI/MCP、H7 自包含目标机和正式切换/恢复证据；
补齐同 fixture 新旧性能矩阵与一小时稳定性；评审逐项替代断言和删除路径。
然后核定清单、一次性清理、Debug/Release 全量复测，才能关闭 H8/移交 M3。

## 6. 已执行的精确旧入口/桥清理（2026-10-05）

用户“调整”授权覆盖上述必须等待全部人工门禁的旧限制，仅针对本次切换与清理。
以下 27 个文件在删除前核定为旧入口/专属包装/机制测试，没有用户未提交内容覆盖；
使用补丁移除源码，同时解除 CMake/solution/NMake/Build 消费者，不递归删除源码目录。
项目根为 F:\NcmaEngine，所有清单路径均相对该根：

```text
engine/source/editor/EditorMain.cpp
engine/source/editor/EditorApplication.cpp
engine/source/editor/EditorApplication.h
engine/source/editor/EditorAnimation.cpp
engine/source/editor/EditorFbxCharacter.cpp
engine/source/editor/EditorResources.h
engine/source/editor/NcmaEditor.rc
engine/source/runtime/script/runtime/ManagedHost.h
engine/source/runtime/script/runtime/ManagedHost.cpp
engine/source/runtime/script/runtime/DotNetGameplayRuntime.h
engine/source/runtime/script/runtime/DotNetGameplayRuntime.cpp
engine/source/runtime/interop/NcmaGameplayBridge.h
engine/source/runtime/scene/ManagedSceneClient.h
engine/source/runtime/scene/ManagedSceneClient.cpp
managed/Ncma.Managed.Host/EditorCatalogEntry.cs
managed/Ncma.Managed.Host/EditorEntry.cs
managed/Ncma.Managed.Host/NativeEntry.cs
managed/Ncma.Managed.Host/Ncma.Managed.Host.csproj
managed/Ncma.Managed.Host/PlayViewsEntry.cs
managed/Ncma.Managed.Host/SceneEntry.cs
managed/Ncma.Managed.Host.Tests/Ncma.Managed.Host.Tests.csproj
managed/Ncma.Managed.Host.Tests/Program.cs
tests/ManagedHostTests.cpp
tests/ManagedSceneBridgeTests.cpp
tests/SceneFlatTests.cpp
tests/plugins/LegacySceneReference.cpp
tests/plugins/LegacyReferenceCapture.cpp
```

旧 ImGui 重复库、NcmaEditor target、宿主/场景/GPU 旧比较 targets 和 Host 项目引用均退出。
仅删除旧机制断言/重复实时对照；直接 C# Runtime/Scene/Core/Gameplay/catalog/Play/IPC/stdio、
原生数值资源/模块 ABI/FBX/物理负例继续保留。场景冻结 12 快照和清理前旧 Release 冻结图像成为稳定参考。
退役方案和业务断言对应见 [替代矩阵](M2_8_TEST_REPLACEMENT_MATRIX.md)。

### 可恢复生成项备份

以下 19 个精确生成文件逐项核定类型、路径、无链接、锁和 hash 后移至
F:\NcmaEngine\out\deployment\retired-artifacts-20261005，保留相对 out/ 的原路径；未删除：

```text
managed/Ncma.Managed.Host.deps.json
managed/Ncma.Managed.Host.dll
managed/Ncma.Managed.Host.pdb
managed/Ncma.Managed.Host.runtimeconfig.json
build/windows-ninja-debug/NcmaLegacySceneReference.exe
build/windows-ninja-debug/NcmaLegacySceneReference.ilk
build/windows-ninja-debug/NcmaLegacySceneReference.pdb
build/windows-ninja-debug/NcmaManagedHostTests.exe
build/windows-ninja-debug/NcmaManagedHostTests.ilk
build/windows-ninja-debug/NcmaManagedHostTests.pdb
build/windows-ninja-debug/bin/NcmaEngine.exe
build/windows-ninja-debug/bin/NcmaLegacyReferenceCapture.exe
build/windows-ninja-debug/bin/NcmaLegacyReferenceCapture.ilk
build/windows-ninja-debug/bin/NcmaLegacyReferenceCapture.pdb
build/windows-ninja-release/NcmaLegacySceneReference.exe
build/windows-ninja-release/NcmaManagedHostTests.exe
build/windows-ninja-release/bin/NcmaEngine.exe
build/windows-ninja-release/bin/NcmaLegacyReferenceCapture.exe
symbols/NcmaEngine.pdb
```

两个已退役托管项目仅余 bin/obj，也完整移至该隔离目录的 retired-managed-projects/：
Ncma.Managed.Host（106 文件）、Ncma.Managed.Host.Tests（111 文件）。所有文件移动后逐项核对大小与 SHA256。
没有递归删除或移动 managed/ 根目录，没有覆盖共享源码、SDK 或用户资产。
前一版本源码可从 Git 历史恢复；旧生成缓存直接从隔离目录恢复，不能自动进入当前生产加载路径。

### 正式部署与范围限制

初次旧 C++ 三文件安装备份仍在 out/deployment/2da52184ae0c4f27ada710bec7e4a41e/backup。
当前 Release 完整包 journal 为 ae4c96c4166645c78ae1572528994e69 / Complete，
该代 backup 是上一代 C# Debug 安装；scripts/Recover-Editor.bat 验证并恢复当前 journal 的上一代，
不声称一次执行可跨代恢复初始 C++。后续重复部署保留分代 journal。
未知用户文件、reparse 或文件锁会停止部署/恢复；不强制解锁、不自动降级、不清空 out/。
全量 Debug/Release 与 7 个部署用例证据见 [交付第 8 节](M2_8_DELIVERY_REPORT.md)。

仅 old_entry_bridge_removed=true；consolidated_cleanup_completed/h8_accepted=false。
保留 ActionAnimationWorkspace/AgentCapabilityRegistry/ScriptRuntimeRegistry、原生 UI/render-graph 策略原型和其独立测试。
未删除 OpenGL/D3D12/Vulkan 骨架或共享 native kernels；后续另行核定消费者与语义覆盖。
人工 UI/MCP、自包含目标环境、完整性能/长稳仍待完成，.vs/.user 与无关改动保留。本次没有新提交/推送。
