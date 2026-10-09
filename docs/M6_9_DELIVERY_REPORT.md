# M6.9 交付与测试记录

状态：最终顺序无Skip Debug/Release自动候选通过，Release checked部署核验完成。
提交推送/远端状态以Git回执为准；不是整个M6或任何人工/正式门禁验收。
基线远端 `fcc4e8abbf505b9ead914b2631e9e20b72af0397` 已核对。

实现与边界见 M6_9_IMPLEMENTATION_PLAN.md、M6_9_RUNTIME_CONTRACT.md。

首轮构建 `out/m6-9-build-first.log`：World.ReadOnly是internal，不扩大其权限；
改由Editor.Core trusted ObserveReadOnly进行owner/World只读/nestedInvoke保护。
second零警告编译；third测试原审批方法签名错误，按现有5参数修正；fourth零警告。
首轮Editor `out/m6-9-editor-first.log`：场景workflow通过，实际图事务缺少独立文件审批
返回graph_scope_denied，被误记失败。加入明确权限等待码（不放宽任何原审批）；
原失败和审计全部保留，实际双审批/资源与后续断言必须重测。

自动测试不等于人工/素材/目标/性能/1h验收。M6.10未开始，M7/M8/M9未开始。

fifth构建测试误用SceneDocument IDisposable失败；改成其实际managed生命周期（无Dispose API），
sixth零警告构建，`out/m6-9-editor-second.log` 121/121通过，新增8个M6.9用例通过。
实际前台ImGui截图 `out/verification/m2/editor-services/1128ecc2783540bb879a1b2efe0711bf/m6-9-workflow.bmp`
已读取检查：未接入推理标签、完整范围审阅、计划状态和同MCP回执可见。
另补实际NCA断言失败/固定断言修复拒绝、2活动计划/foreign owner/tampered scope、
12步多页计划完整审阅，须定向及完整门禁验证，尚未声明通过。

seventh零警告；`out/m6-9-editor-third.log` 123/123通过，含全部10项新M6.9测试。
检查实际12步前台截图后将回执改成4行分页的字段化摘要（保留完整MCP哈希/请求/提交版本），
避免整块JSON审阅墙；新增12回执前后翻页断言。下一次定向/完整结果待记录。

eighth零警告；`out/m6-9-editor-fourth.log` 123/123通过，actual字段化4行回执前台截图
`out/verification/m2/editor-services/3f0d6a2f571b486da9be37a7fb7bda31/m6-9-workflow.bmp` 已读取。
`out/m6-9-full-debug-first.log` 完整无Skip通过并受检部署，101哈希核验、journal Complete
generation `7a06a68d26ef4c0d8c7b5adae538559f`。此轮不是最终代码证据：收尾复审发现
Before拒绝已知ticket的伪造workflow capability时会残留caller上下文。把上下文建立限定
在非ticket的正常workflow调用，所有ticket路径caller为空；新增真实stdio伪造后
直接调用必须workflow_endpoint_context_required断言。必须重跑完整Debug和Release。

## 最终完整验证

源码冻结，第二轮Debug结束/核验后才运行Release，没有Skip，没有删除或弱化原用例：

- `out/m6-9-full-debug-second.log` exit0，12native(3.17s)/22managed(288.13s)。
- `out/m6-9-full-release-first.log` exit0，12native(1.24s)/22managed(250.76s)。
- 两配置动画pose/clock115/115、Editor123/123、Player56、Scene36/36、
  Editor.Core36/36、Gameplay53/53、fakeMovement38/38、Python43、deployment8/8。
- managed/native scene及animation ABI smoke、严格新格式及removed-format拒绝、
  python inspect、三轮retained profiles、pre/post审计通过。
- 10项新M6.9用例覆盖完整独立审批/NCA图六步链、独立断言失败停止后续、
  两活动计划/注册范围tamper/owner/stale review、已排队获批mutation取消、
  deadline/TTL/revoke-re-pair/endpoint/revision、伪造ticket无caller残留、
  1次修复后必须完整重新批准/预算耗尽拒绝、16终身计划、copy、12步完整UI页/12回执4行分页。
- 注入post-commit观察器异常：World写入和nested Invoke被拒绝；原成功commit/UndoCount保留，
  后续请求fail-stop，绝不伪造rollback。
- 所有原实际NCA/同Animator/root/Notify/uniqueMovement-Jolt/CPU-GPU oracle、
  Editor0-1-8-32/16 source-free Player、UI/物理/动画/格式/IPC用例保留并再次通过。

Debug最终画面 `out/verification/m2/editor-services/7b0b7384baa74a1e9a97655ace25838b/m6-9-workflow.bmp`；
Release最终 `out/verification/m2/editor-services/c25510ebb7054a0cb4f18f86ff9ff7ed/m6-9-workflow.bmp`。
Release已读取：前台标题、未接推理边界、completed12/12、字段化request/version/hash/断言和分页可见；
纯Ui=1/零3D资源组/API0 errors/0 warnings。不是人工输入法/DPI/可见第三方MCP验收。

## 最终部署与恢复

两次最终101文件路径边界/无reparse祖先/普通文件/大小/SHA256逐项通过，journal Complete。
Debug generation/备份 `out/deployment/af7257a69ed24400bdb77880cf92c342/backup`；
Release `out/deployment/45e5cc98697e4945b560698cae13bb5b/backup`。
Debug post-deploy audit `out/verification/m2-8/Debug/c537c8ceb8834d11bc8992129c910990/audit.json`；
Release `out/verification/m2-8/Release/d04ee27845784d5cb094c5835fcc75c9/audit.json`。
均audit_passed=true、h8_accepted=false，manualAcceptance/selfContainedVerified=false。
`out/bin/NcmaEngine.exe` 是本次framework-dependent Release入口（357376字节），
SHA256 `7059FB9B7BE815125EF58620CCD14DCEA94B7D052416463EDACC329F9A85FBCB`。
核验整个清单而非只查apphost，启动仍需其同目录受检DLL/资源和.NET8。

所有失败、首次Debug、早期截图、审计、journal/backup、SDK/userdata/IDE设置保留。
不提交out/备份或无关.vs/.user；不恢复旧格式/桥接/Player/Python gameplay。
没有推理/任意代码执行/新native ABI/liveAgent权限。自动候选M6.9闭环，人工/用户素材/
目标自包含/性能/1h仍开放。远端核对后下一阶段M6.10；整个M6结束后M7(DX11-only)→M8→M9。
