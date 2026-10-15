# M8.1：模块契约与异步 inbox

适用范围：纯 C# `Ncma.Modules` 的可信 adapter API。不是 RPC 线协议，不是 Python host、推理服务、MCP 工具或编辑器审批网关。M8.2 才处理严格 wire 编解码、协商、认证和实际独立 worker；现有 inspect 的完整 transport-neutral contract 标志仍为 false。

## 所有权

`ModuleCatalog` 在可信启动代码中注册；操作绑定模块 UUID/版本、配置 UUID、输入/输出 schema UUID、字节预算和时限。注册不能授予已有 Editor/Agent 权限。

`ModuleSession` 在创建线程调用 Submit/Pump/TryTake/Invalidate/Cancel/Stop/TryClose；跨线程和 Pump 重入拒绝。adapter 只在后台收到 `ModuleRequest`，不持有 session、World、Editor、GPU 或命令委托。可信 adapter 自己创建的任务/IO 不属于安全沙箱；禁止把用户输入当作 delegate/程序集路径。

`ModulePayload` 拥有复制字节，严格 UTF-8、根对象、深度16、重复属性与65,536字节上限。读取返回新副本；request/context/operation 均不可变。结构检查并不验证领域 schema，payload 中的任意内容均不构成指令或许可。

## 身份与结果

v1 request 指纹覆盖 request UUID、session UUID、epoch、完整 context、operation 和原始 payload SHA256。Context 包括 project/host-session/document UUID、generation、revision、可选 world UUID、observation tick。世代必须正数，无 World 时 tick 为0。

reply 必须匹配 version/request/session/epoch/fingerprint/output-schema、已知状态与输出预算。成功只得到 `NeedsDomainValidation`，不是“已批准”或“已应用”。外部调用者不得拿这个结果直接写 World；后续 M8.3 通过已有领域 schema、审批和事务重新验证并提交。

当前提供精确 authoring context，不提供 runtime 时效窗口。Pump/TryTake 传入新的 context 会增加 epoch，全部未领取结果作废；显式 Invalidate 用于即使 revision 相同的权限撤销/替换。新 epoch 不能复用旧回复。提案被领取后也只是复制数据，提交时必须再次核验当前领域状态。

## 调度与预算

- 目录最多64操作；会话最多16 outstanding（排队、执行中、未领取结果、尚未结束的已取消工作都计入）。
- 1–4活跃 adapter 调用；4096终身 request ID，不驱逐去重记录。满后需明确结束会话，不自动重置。
- 时限1–60000ms，单调时钟，从 Submit 算起，包括排队和完成后尚未领取的时间。
- Submit 不执行 adapter；Pump 有界观察完成的 Task，再按空余槽位派发。GetResult 只用于 IsCompleted 的任务，不等待工作完成。
- Cancel 在后台执行 CancellationToken 回调，主线程不执行可能阻塞/抛异常的回调。每个工作最多一个取消任务。
- 超时/取消不能强停可信 adapter。旧工作/取消回调未完成时继续保留其活跃槽；不会自动重试、重启或并发补位，避免无界堆积。进程硬隔离与资源限制是后续 worker 的独立责任。
- 错误只返回固定状态；异常文本、traceback、内部对象、路径或密钥不进入结果。

## 停止与关闭

Stop 拒绝新请求并撤销未领取结果。TryClose 明确丢弃停止会话的待领取结果，非阻塞收割已完成任务；false 表示仍须保留 session 和 adapter/transport 资源，后续继续 Pump/TryClose。只有 true 后宿主才可释放外部 adapter/transport。没有用 Dispose 暗中等待或强制卸载。

Cancel、Stop、Invalidate 不撤销已产生的外部 IO，也不撤销已经批准的编辑命令。此库没有任何文件写入、网络、命令执行、历史、World 修改或模型连接职责。

## 测试与尚缺证据

独立 `NcmaModuleTests` 纳入 Build.bat 的托管组和 CMake；未加入 Editor/Player 包依赖。测试替身用于故障/阻塞/乱序/身份/期限与预算证明，不替代真实 Python、网络或推理验收。

已复现“完成但未领取的 proposal 不过期”缺陷并修复领取前期限检查，红色日志保留在 `out/m8-1-unclaimed-deadline-red.log`。最终完整双配置结果由实施计划记录；本文件不宣称服务接入、用户授权、真实模型、跨进程取消或长期性能通过。
