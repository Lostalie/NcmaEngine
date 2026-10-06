# M2.8：联合验收、旧入口清理预检与 M3 移交

更新日期：2026-10-04。状态：预检/冻结参考/部分自动验证已实施；H8 未通过。依赖 H1–H7；不是提前删除源码的授权清单。
继续补齐 kernel-only GPU reference、三方图像对照、直接 Export/12活动reload、历史运行时三轮测量；测试替代见 [矩阵](M2_8_TEST_REPLACEMENT_MATRIX.md)。
本轮实施与缺口见 [交付记录](M2_8_DELIVERY_REPORT.md) 和 [消费者预检](M2_8_CLEANUP_AUDIT.md)。
遵循“先有替代和测试，再核对消费者，再删除”，不能用清理代替迁移。
用户确认旧入口在 M2 结束后统一清除：本阶段验收新路径、核定精确清单，不提前删除；全部 M2 门禁通过后执行一次性清理并复测。

## 1. 终点

默认 out/bin/NcmaEngine.exe 确为托管 apphost，C#拥有主循环与业务；Player/headless不带编辑器依赖。
C++保留经确认的窗口/GUI适配与数值/GPU/物理内核，没有第二份 World、脚本宿主、文档命令或编辑历史。
所有实际版本/能力与 docs/manifest一致；M1已有语义和原格式拒绝仍通过；人工证据齐全才关闭M2。

## 2. 清理目标与保留原则

以下是 M2 结束后的候选清单，不是当前删除指令。执行前逐项确认新旧消费者和替代测试、精确路径与文件类型；未迁移消费者会阻止对应项删除，不靠兼容转发掩盖未完成。

| 候选旧项 | 删除前条件 | 保留或替代 |
| --- | --- | --- |
| EditorMain.cpp/wWinMain、EditorApplication.h/cpp | H5/H7全功能+窗口关闭通过 | C# App/Services + native薄适配 |
| ManagedHost/hostfxr引导 | 新入口/Player不引用；桥接消费者核对完 | .NET apphost正常CLR启动 |
| ManagedSceneClient/DotNetGameplayRuntime | 图形/Gameplay/Editor/MCP smoke全部迁移 | 直接托管服务/复制native资源视图 |
| Ncma.Managed.Host旧NativeEntry/SceneEntry二进制包装 | Scripting服务、场景owner、catalog测试已有替代 | 不删除SDK/Runtime/Scene/Core/Gameplay |
| NcmaGameplayBridge v5 / Scene bridge v6 | 所有known consumers重建或退出 | 新native Module API不含scene token |
| 旧Native Editor CMake/NMake路径 | 新Build/Rider默认部署可用 | 保留canonical构建与新plugins/tests |
| C++ ActionAnimationWorkspace/Fbx preview history | C#预览/新kernel消费/Python工具回归通过 | 纯AnimationRuntime/import/数值reference |
| 排除编译的旧OpenGL/D3D12/Vulkan骨架等 | 独立搜索确认无消费者、不影响保留SDK | 删除清单单独审查；不凭文件名批量删除 |

最后一项只是候选审计，M2不默认大扫除SDK或用户历史资源。若不删除，须明确位于未使用/未实现区，
但不能让未使用 API假实现进入生产manifest。不能删除已验证D3D11算法、原生动画/FBX数值、Box2D/Jolt或合法SDK。

NcmaNative aggregate 是否退出按实际consumer决定：
如仅剩kernel接口，重组为真实kernel模块；原aggregate卸除须同步 C#/Python/build/test。
若某legacy API仍保有高层preview命令/history而未迁移，记录未完成，不声称C++只剩内核。
保持native World exports已删除，不增加旧桥forwarder、旧场景alias或临时Python gameplay host。

## 3. 测试替换而非删测试

旧tests/ManagedHostTests.cpp等验证hostfxr的机制测试，需先迁移语义断言：
组件/绑定、Export、Play/Stop、重载、失败原子性、SDK身份、catalog12次释放、活动MCP链。
新测试改为直接托管应用/IPC/真实nativeplugin组合；C/C#固定布局测试单独保留。
删桥后CTest数量可能改变，按实际列出，不为了保留“14/14”创建空test。
所有 Runtime/Scene/Core/Gameplay的已有用例、Python严格格式与真实stdio回归保持或加强，不能删负例把门禁刷绿。

## 4. 联合矩阵

| 领域 | 必须证明 |
| --- | --- |
| 默认入口 | 普通shell/Explorer/Rider启动，main在C#，仅一个owner线程/窗口/主循环 |
| 项目/文件 | .ncmaproject校验、.ncmascene v1、中文/空格/cwd变化、未知/旧格式拒绝、失败不改旧现场 |
| 编辑/脚本 | 全配置历史/选择/草稿/Dirty/file fingerprint、Play隔离、input/interpolation/结构/信号/reload |
| GUI | 键鼠捕获/真实失焦/输入法/DPI/resize/minimize/图标/字体/布局/关闭；保留展示适配不保留业务 |
| Agent | helper→新EditSession真实读写，批准/删除/共享历史/撤权/冲突/断线/超时/ownership/unknown outcome |
| Renderer | DX11参考图、GUI合成、API验证、资源重建/关闭；Vulkan显式未实现 |
| Physics | 独立2D/3D批量/负例/任务关闭，不虚称scene同步/Character |
| Player/headless | 无Editor依赖/无Python前置、固定tick、结构化报告/退出码、复制发布包无源码依赖 |
| 资源/ABI | 句柄generation/输出容量/线程、缺DLL/版本错误/初始化失败、lease/job/GPU安全退出 |
| 架构清理预检 | 新生产路径不使用hostfxr/旧NativeEntry场景桥；对照目标隔离，无原生World/Undo/Actor/Python gameplay，清理清单与替代测试齐全 |
| 文档/发布 | 实际版本/支持OS/依赖/许可证清单与产物一致，不把计划/预览标完整实现 |

规范命令仍为：

~~~bat
Build.bat -Configuration Debug
Build.bat -Configuration Release
~~~

先 Core/Native/Architecture，再新插件和托管项目/CTest/managed-native smoke/Python inspect及全部回归。
如果旧NcmaNative已实际退出，保留名为NcmaNative的规范kernel目标或在同一变更明确更新AGENTS/Build依赖，不能无解释跳过优先内核门禁。
依赖升级有单独矩阵，最终无SkipTests/SkipManaged/SkipPython；新代码零编译警告/错误。

## 5. 故障与关闭注入

在可信test fixtures注入，不注册为MCP能力：

- config/catalog/platform/renderer/gui/physics每一步初始化失败。
- UI旧frame/文档代次/批准提案过期，Agent排队后人工草稿/Play/重载竞争。
- resize零尺寸/字体重建失败/资源创建失败/device_lost模拟。
- step失败、脚本constructor/lifecycle/prepare失败、reload预检/激活失败。
- physics任务延迟/模块lease泄漏/重复Dispose/关闭超时。
- helper响应丢失/提交后断线，不回报“零修改”，不自动换requestId重放。
- 输出buffer不足、ABI mismatch/缺导出/DLL锁住，不冒险强卸载/删除。

核对World/doc/history/tick/input/signals/receipts/授权与native live counts；
native数值故障/私有脚本字段/IO边界诚实记录，不把跨资源域都描述为完全回滚。

## 6. 性能与有限资源记录

同一机器/构建/fixture，先记录M1基线，再新入口各8预热/至少32样本median/p95/max。
覆盖：

- 0/1/8步、0/64/1024运行命令；
- 128×9组件×4绑定、32×9×64绑定、1024/4096对象；
- UI 0/256/4096对象虚拟列表、Inspector/Console/MCP完整提案；
- 无连接/只读/批准写入/四连接64队列及65th queue_full；
- DX11固定分辨率/VSync/参考资源、物理0/64/1024/4096body批量；
- ABI calls/bytes、simulation/view/prepare/submit/Present、托管分配/GC暂停、native live stats；
- 64次Play Start/Stop、12次catalog/活动reload、32次窗口/Renderer/Physics/IPC关闭与弱引用/句柄。

M1 4096对象约138–156ms/91MB是已知限制，不定为M2的达标帧率。
建议回归报警：同fixture稳态median耗时/分配较基线恶化>10%时复测至少3轮并分析，
这是提议门限，不把一次桌面抖动当失败，也不保证不同CPU/GPU相同绝对ms。
应用适配/UI成本与Simulation分开；若仍远超动作游戏预算，保留M3/M4前性能优化门禁。
有限释放循环不等于长期无泄漏证明，另记录1小时真实编辑/Play/客户端稳定性时长及未能执行项。

## 7. 人工与最终报告

真实MCP客户端：名称/版本/脱敏配置，读→默认拒写→批准事务→UI Undo→MCP Redo→validate→删除确认→撤权→关闭。
GUI记录真实键鼠/焦点/中文输入/DPI/窗口/图标；renderer记录GPU/驱动/debug layer图像/错误，不能仅引用自动smoke。
Player包在声明目标环境实际启动；不能因本机已装SDK而宣称self-contained无需任何环境。

全部完成后生成docs/M2_DELIVERY_REPORT.md：
实际提交/环境/TFM、模块与consumer版本、能力/依赖、自动/人工证据、失败修复、
相对M1性能/资源、发布目录/许可证、已知限制、未实现范围及M3门禁。
同步AGENTS/ROADMAP/ARCHITECTURE/FRAMEWORK_REFACTOR/BUILDING/AI_DEVELOPMENT/WORLD_ACCESS/manifest。
只有真实已实现的模块改变能力状态；本次方案生成不改manifest或M1人工状态。

## 8. M3移交

交付稳定托管App/Scripting/Editor services、唯一World/command、窗口输入/GUI/Renderer/Physics契约、Player/headless与部署流程。
M3可在它们上加资产UUID/导入缓存/FBX场景/GPU蒙皮，不重建主循环、文档历史或权限。
尚未实现：完整资产cook/Prefab、场景物理/角色、Animator图、UI制作/运行时HUD、Vulkan绘制、Python推理通信、网络与动态扩展。
在H8证据齐全前，不把M2标为完成，不进入完整M3交付。

## 9. M2 结束后的统一清理与复测

H1–H8 自动/人工门禁全部通过后，按本文件核定清单统一移除已替代的旧入口、hostfxr 场景/玩法桥、旧对照构建和部署路径，以及仅服务这些旧路径的测试包装与产物。
保留替代后的语义测试、共享内核/SDK、合法项目和用户资产；不删除 .vs、.user 或其他用户本机修改，不对 out/ 或工作区进行无清单递归删除。
对仍运行、文件锁定、路径不明或尚有消费者的目标停止处理并报告，不强制删除或扩大范围。

同步 solution/CMake/Build/Rider/启动器、文档和 manifest，运行 Build.bat Debug/Release 完整矩阵、managed/native smoke、Python inspect 与回归；验证 out/bin/NcmaEngine.exe 仅为新入口且不依赖旧桥。
记录实际移除文件、可恢复方式与复测证据。清理复测通过后再移交 M3；清理失败不撤销已经取得的 M2 功能证据，但必须明确标记清理未完成。
