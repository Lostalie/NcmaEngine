# M2.8 旧桥测试替代矩阵

2026-10-05 已执行处置（用户调整清理门槛后）：ManagedHost/ManagedSceneBridge/SceneFlat 与 Host.Tests
专属机制测试退出；直接 C# Runtime/Scene/Core/Gameplay/catalog/Play/IPC/stdio 业务测试继续保留。
旧实时场景比较退出，12 个旧版本冻结快照保留；旧 GPU capture 退出，旧 Release 冻结图像成为第三方输入，
与 kernel/managed 比较容差不变。新增部署失败恢复/锁/未知文件/正式路径/首次安装/重复部署证据。
此处仅退休已不存在的 ABI5/6/hostfxr 机制，不新增兼容 stubs，也不删除原生资源/模块 ABI 负例。
原生 ActionAnimationWorkspace/Agent/Script/UI/render-graph 策略测试尚未全量迁移，因此不标记统一旧代码清理完成。
下文为处置前的逐项覆盖依据；其中“保留旧比较/尚未删除”是历史状态。

更新：2026-10-04。用途：将业务断言与即将退出的桥机制分开，供最终清单评审。
状态：自动替代覆盖继续补齐，**不是测试删除或 H8 关闭授权**。

## 1. GPU reference

新 tests/plugins/KernelReferenceCapture.cpp 只链接 GLFW/Eigen/D3D11/RhiResources/ReferenceKernel，
不链接 NcmaCore、EditorApplication、ManagedHost、ImGui、NcmaPlatform 或 CLR。
它通过数值内核组织固定 identity cube/ground、shadow→geometry→tonemap，
写256×256 RGBA8，要求 Debug Layer 存在、warning/error=0、内核资源maps归零。
这是测试中的固定流程，不是第二份 C++ 产品默认管线。

NcmaRenderingTests 主参考改为 kernel image，并在 M2 内保留旧 image 三方校验：
旧 Editor image→kernel image→C# pipeline，max<=4、mean<=0.1（8bit通道，含alpha）。
运行 args3 可只消费kernel image；canonical args4继续消费旧比较image。
两条 CTest fixture 都保留，不能本轮删旧目标。
该参考共享数值Shader/算法，只证明流程迁移/资源/接口等价，不作为独立 PBR 正确性 oracle。
future kernel+managed 两者共同变化可能逃过它，因此独立旧比较在最终评审前不能拿掉。

## 2. ManagedHostTests.cpp 业务与机制

所有替代项目已在 canonical CTest 中，不依赖旧 Host（Host.Tests 机制自身除外）。

| 旧断言组 | 直接托管替代（文件相对 managed/） | 当前结论 |
| --- | --- | --- |
| one discovered type/3 float-bool-int Export/SDK类型身份；metadata不创建live gameplay | Ncma.Editor.Services.Tests/Program.cs: Direct catalog exported values...；Direct catalog SDK identity... | 已补具体值、缺成员/种类错/范围错、candidate归属、失败加载保持metadata |
| Fast90×2逆时针、Slow30顺时针；disabled/unattached不动 | 同文件: Direct sample attachment/Exports and 12 active reload unloads... | 已补30固定步的Quaternion具体值；非空间disabled不附加Transform |
| stop不销毁借用scene | Ncma.Player.Tests/Program.cs: Shared service owns direct runtime document, borrowed catalog and close lease；Editor services isolation/Stop | 已有覆盖；新的Edit clone语义不等于旧桥in-place状态 |
| 失败绑定后无半活实例、可恢复；缺类型 | Ncma.Application.Tests/Program.cs: Reload preserves edit generation on failed preflight；Ncma.Gameplay.Tests/Program.cs startup/factory failure；Editor catalog missing type | 已有覆盖，实际异常信息是否等价需最终review；不保留旧桥措辞作为API |
| fixed6steps、Paused不累积、Step1、Resume无debt、8步上限/dropped | Ncma.Gameplay.Tests/Program.cs frame/pause/budget；Ncma.Player.Tests/Program.cs exact fixed-step | 已有更细断言 |
| 12 active reload保留committed World/tick、旋转session epoch | Editor services: Direct sample attachment...；Gameplay.Tests reload preflight/activation | 已补12次旧load-context弱引用释放，旧input epoch拒绝；失败reload保留paused catalog与World |
| input sequence/epoch，copied render views、foreign thread、不可直接写activePlay | Gameplay.Tests input foreign/sequence、owner thread、callback read-only、render interpolation；Runtime.Tests owner/committed reads | 保留这些业务安全边界；不能把guard删成无约束普通调用 |
| ABI5 120-byte status/token、Resolve Tick被移除、unscoped EndScene/Unload | 旧Host与ABI机制测试 | 旧协议退出后机制不再适用；在删除export/全部consumer证据下评审退休，**不新增兼容桥/拒绝stubs** |

## 3. ManagedSceneBridgeTests.cpp

| 旧断言组 | 直接替代 | 当前结论 |
| --- | --- | --- |
| copied Transform、不回写live值；Restore使old handles失效，UUID重解析；empty无Transform | Ncma.Runtime.Tests: Transform validation is atomic / Managed snapshot round-trip and stale handles；Ncma.Scene.Tests: Restore invalidates old references and increments once / All components... | 业务断言已有覆盖 |
| malformed/truncated restore完全保持文档/revision；完整组件与四种Export | Ncma.Scene.Tests: AtomicReject、roundtrip、invalid Export、removed formats | 已有覆盖，不删除严格负例 |
| editor UUID命令、草稿10updates单commit/Cancel、冻结与外部写排斥 | Ncma.Editor.Core.Tests: Interaction preview, one commit, cancel and write exclusion / Draft conflicts... / Export previews...；Services draft/frozen cases | 已有覆盖 |
| Undo/Redo保持绑定/完整文档，单对象delete恢复selection，不递归delete，optionalcomponent | Core.Tests: Complete components... / Bindings and optional component... / Delete clears only its UUID...；Services frozen scene12快照 | 已有覆盖 |
| Save/New/Open与Undo恢复路径/fingerprint；失败open/save不改变history/frozen拒绝 | Core.Tests: New/open Undo restores selection, file association and saved fingerprint / Failed open/save and frozen...；Services file cases | 已有覆盖 |
| SceneCall ABI6 buffer长度/空指针/错op/无效token/foreignthread/old ops4,14,16,17,18拒绝 | 旧Scene bridge机制 | 退出旧API后评审退休；替代服务继续保持owner-thread、边界、安全阶段，IPC/插件ABI各自负例保留 |
| NcmaNative不得导出native World / GetGameObjectApi | NcmaNativeTests + managed/native smoke；Runtime独立依赖断言 | 符号删除负例继续保留，不能随旧宿主一起丢掉 |

## 4. 尚未封闭范围

- ActionAnimationWorkspace 16快照仅覆盖指定命令序列；AnimationTests.cpp 所有策略/数值边界还需逐项拆分。
- C++ AgentCapabilityRegistry/ScriptRuntimeRegistry/旧UI model的架构测试与全部新consumer仍需核定。
- 原生ABI固定布局、安全资源、数值/FBX/Physics、Runtime/Scene/Core/Gameplay/真实stdio的独立测试不随旧入口删除。
- Player/Editor apphost真实部署、权限/失败/生命周期证据不等于H5人工/H7自包含。
- 完整同机新旧入口性能、GPU/Physics/UI/MCP矩阵与一小时人工稳定性尚未完成。
- 对仍依赖旧桥/旧截图的测试只在统一清理阶段审查移除精确包装；本轮没有删除任何测试。
