# C# 原生 World 访问：已实现的第一阶段

当前 C# 游戏逻辑通过 C ABI 访问 C++ SceneWorld。独立 Ncma.Runtime headless 原型已实现，但尚未接管此原生编辑器门面。
Python 游戏 SDK/宿主已删除；Python 独立模块不得持有 live World 或使用此门面执行游戏脚本。
Transform 批量、安全引用与 POD 信号接口保留供 C# 使用，通用组件批处理未实现。
目标边界见 [ARCHITECTURE.md](ARCHITECTURE.md)、[PYTHON_MODULES.md](PYTHON_MODULES.md)。

## 更新与提交

编辑器每帧：BeginGameplayPhase → C# Tick → CommitGameplayPhase。
标量与批量 ABI 均读已提交 Transform，写入暂存，Commit 后统一可见。
Begin 不复制整个场景。脚本不能立即读回本帧暂存结果，需保留自己的计算值。
同一批次重复目标失败；不同批次/脚本写相同目标时最后一次提交生效，按 C# 宿主提交顺序。
建议应用为组件指定唯一写入者，其余对象发信号。没有脚本并行或逐组件优先级。

宿主更新错误时暂停并 Abort 未提交 Transform/新信号；脚本私有状态、IO、已消费的旧信号
不回滚或重放。OnCreate/OnDestroy 在阶段外，Transform 修改立即生效。
原生系统应在阶段外修改组件；直接 C++ 引用不受 ABI 暂存约束，不得在脚本阶段写组件。
阶段内创建/删除/恢复快照明确报错，Spawn/Despawn 命令、动态脚本自动绑定未实现。
独立宿主需自行用 C++/C ABI 驱动阶段，仅调用 Tick 不会自动形成边界。
阶段控制不暴露在公共脚本门面；编辑与 Agent 修改仍走原有 Undo 命令路径。

## 引用与批量接口

World Access ABI v1 是新增接口，原始 ABI v1 / GameObject 语义 API 升至 v4（仅 C#）。
ObjectReference 包含临时 World UUID（High/Low）与 Object ID，不序列化；不使用各 DLL
独立的计数器作为进程级身份。对象持久 UUID 使用哈希索引查找。
删除、成功恢复快照或切换 World 后旧引用失效。恢复不复用旧数字 ID，须通过 UUID 重新解析。
Stop/Reload 必须先结束 C# 借用会话再销毁 World。

访问限定 World 创建线程，前端与桥均检查，不允许后台线程访问 World。
批次最大 4096 项、空批次合法。调用方复用数组，桥复用线程本地转换区，不暴露 STL、
Eigen 对象地址或可写裸指针。整个批次验证引用、容量、数值、重复目标后才修改输出/写入，
无效输入不部分修改组件或覆盖前半段输出；四元数长度平方必须有限且 >=1e-6，随后归一化。
布局：ObjectReference=24、Transform=40、TransformWrite=64、GameplaySignal=72 字节，原生/托管检查；
32 位平台尚未验证。

C#：GameObject.Reference/PersistentId、World.FindObject(uuid)、
ReadTransforms(ReadOnlySpan<ObjectReference>, Span<Transform>)、WriteTransforms(ReadOnlySpan<TransformWrite>)。
UUID 引用型 Export 未实现，应用代码/原生适配层负责提供对方 UUID。

```csharp
// self/peer 为 C# GameObject；数组只初始化一次。
ObjectReference[] refs = [self.Reference, peer.Reference];
Transform[] states = new Transform[2];
TransformWrite[] writes = new TransformWrite[1];
// 每帧复用：一次读、语言内计算、一次写。
self.World.ReadTransforms(refs, states);
states[0].Position = states[1].Position;
writes[0] = new TransformWrite(self, states[0]);
self.World.WriteTransforms(writes);
```

示例 self 是 GameObject，不是 Behaviour；脚本通过 GameObject 获取。
数组初始化移入 OnCreate，后续每帧只读、计算和写。

## 行为协作：信号邮箱

GameplaySignal 包含 Source、Target、非零 uint32 Code、有限 double Value、提交序号 Sequence。
不含语言对象、委托、方法名或 JSON。Code 由应用定义，C# 接收方处理。
这是行为请求基础，不代表 Damage/Animator/CharacterMotor 系统已实现。
C#：SendSignal(source, target, code, value)、ReceiveSignals(target, Span<GameplaySignal>)；
Receive 返回实际数量。

信号只能在活动阶段发送，Commit 后可见，编辑器通常下一帧显式轮询；按目标 FIFO。
缓冲区不足时剩余消息保留。待提交与未消费总量上限 4096，满队列明确报错，无静默丢弃/自动过期。
删除任一端点清理关联消息，恢复快照清空邮箱，Abort 丢弃本阶段新信号。
Receive 为消耗性读取，应用须指定唯一消费者；可信脚本间无访问权限隔离。
邮箱是有界线性扫描，不适合对大量对象逐项轮询巨量事件；高频连续数据使用批量接口。
与网络/MCP 无关，不新增对象网络角色、RPC 或 Agent 任意脚本执行权限。

## 验证与诊断

Build.bat 覆盖 C# 批处理与阶段提交、布局、失效/跨 World/跨线程引用、原子拒绝、
Abort、FIFO、溢出，以及旧 Python 对象/绑定的拒绝加载。旧 Python benchmark 已删除。

未实现：通用组件/状态槽批处理、引用 Export、完整 World/SceneAsset 分离、固定步 System 调度、
结构命令、完整动作/动画命令分发、并行脚本、零拷贝内存、自动信号回调与实时场景 MCP。
