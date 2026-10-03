# C# World 与编辑器宿主边界

更新：2026-10-04。C++ SceneWorld 已删除；场景存储唯一权威为 managed/Ncma.Runtime/World.cs。
Ncma.Managed.SceneWorld 是兼容 C# 游戏门面，不持有 C++ 世界。游戏逻辑访问组件不跨 P/Invoke。

## 已实现

- 扁平空 GameObject、可选 TransformData、UUID 索引、线程亲和和失效引用校验。
- 编辑器/Play 各持有一个独立 C# World；Play 恢复编辑快照，Stop 不覆盖编辑场景。
- C# Behaviour 生命周期、数值/布尔 Exports、禁用、手动程序集重载已绑定该 World。
- Transform 批量读/写（最大 4096），重复写目标、外部/过期引用、非有限值整批拒绝。
- C# PlaySession 是唯一 WorldRunner 所有者：按固定步派发 OnFixedUpdate，内部 Commit/Abort；OnUpdate 只读，C++ 不再包裹阶段。读已提交状态、写暂存；中止不回滚私有状态/IO。
- 类型信号邮箱（最大 4096），阶段内发送在 Commit 后可见，显式轮询；不是同步脚本调用或网络 RPC。
- C# `.ncmascene` SceneDocument JSON v1 保存全部注册组件/绑定，原子替换文件；旧 .ncscene 兼容入口已删除。不将逻辑对象变为空间对象。
- 不保留旧 .ncscene v1-v6 导入、扁平化或备份迁移入口；无格式探测回退。新格式加载失败不更改当前 World 或输入文件。
- 恢复/Undo/Redo 使所有旧运行时引用失效；持久 UUID 可重新解析，数字对象 ID 不复用。

## C++ 仅为客户端

ManagedSceneClient 只保存 uint64 场景令牌，查询返回 DTO/数值副本，显式写入调用托管宿主。
没有 ObjectRecord、组件池、UUID 索引、可写 Transform 引用或另一份权威 World。
它目前用于过渡 ImGui 编辑器，不是高频 GPU/物理提取协议，也不是供 Agent 开放的权限网关。

Scene host bridge v6：cdecl、显式输入字节长度、4 MiB 请求/输出上限、调用者拥有缓冲；
UTF-8 字符串用 uint32 字节数，数值为当前 Windows x64 小端定长值，UUID high/low 保持规范文本顺序。
没有 STL/CLR 对象/C++ 指针跨界；异常转为错误缓冲，错误不返回部分输出。
会话表最大 64，令牌不复用，所有请求校验 owner thread。先 EndScene/Stop，再 Release。
CLR/hostfxr loader 为进程生命周期；不在仍持有托管 delegate 时卸载。

原生 NcmaNative 基础 ABI 升为 2；所有 ncma_world_*、旧 GameObject/World Access 版本导出均删除。
Gameplay host bridge v5：BeginScene 配置/激活全部绑定；AdvanceFrame、ControlPlay 与 GetPlayStatus 使用复制的 120 字节 POD，校验 scene token 和 PlaySession UUID。旧 Tick/CreateBehaviour/SetProperty/ActivateScene/GetTickCount ABI 已删除；旧消费者必须重新编译。Scene 操作 16/17/18 不再可调用。
动画和角色各自的专用 ABI 仍为 v1，与场景身份无关。

## 尚未实现

- C# Editor/Player 主入口；ImGui 场景业务命令已迁入 Editor.Core，唯一完整历史由 C# 持有。
- 动画图/UI/资产/源码扩展 Agent 文档命令；场景的 10 项 v2 能力已有本地 MCP，见 EDITOR_MCP.md。
- 完整动作角色 SDK。运行结构命令、输入快照、渲染插值、信号消费回滚与重载预检已实现。
- 资产引用/完整流水线、Prefab、跨组件查询、类型池优化与生产级性能优化。
- 独立 Renderer/Physics 插件、资源批量提取和 C# 装载；当前预览与算法保留。

验证入口：Build.bat；包含 managed/native 动画检查、托管批量/信号检查、场景迁移/恢复/Undo 和编辑器 Play smoke。

## M1.1 implemented document boundary

Complete Ncma.Scene document snapshots v1 cover all registered components and Behaviour/Export metadata. The retained C++/ImGui shell uses opaque snapshots for Undo and Play; C# .ncmascene JSON v1 files persist complete documents with atomic saves. Old .ncscene compatibility is removed. M1.2 commands/history, M1.3 fixed-step/input/interpolation/runtime commands/transactional signals/reload and M1.4 local scoped MCP are implemented; asset references/pipeline remain pending. See [M1.1 implementation](M1_1_SCENE_DOCUMENT.md).

## M1.3 运行边界

默认固定步 1/60 秒、每帧最多 8 步；严格策略超预算先拒绝，交互策略最多接受 0.25 秒并明确报告丢弃时间。Pause/Resume 清空积累时间，Paused Step 恰好一步且不调用 OnUpdate；故障禁止自动继续，Stop 后重新 Play。

启动回调在隔离 Play World 暂存写入并提交，但不增加模拟 Tick；失效会话/异线程/重入拒绝。只读回调拒绝组件、结构、配置、信号消费/发送和 World 释放。

信号消费/发送在固定步联合准备，成功安装后可见，失败不半消费；重载/Restore 更换邮箱 epoch。
IRuntimeCommands 每步最多 1024/1 MiB，预留 UUID 不提供可读句柄；联合安装保留存活对象身份，回执滚动 1024。
清理 callback 只读，删除 tombstone 提供 CleanupContext；新绑定在下一步激活，安装本身不执行生命周期。
重载先暂停、隔离加载与可信 catalog 预检；预检失败保留旧实例暂停，激活失败不可逆 Faulted。
成功更换会话 epoch、重置输入/信号/回执/私有脚本字段、保持组件/Tick，结束后 Paused；私有状态迁移未实现。
InputFrameV1 为 240 字节复制数据；RenderHeaderV1 48 字节、RenderObjectV1 56 字节。视图插值不改权威 World/revision。
