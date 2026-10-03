# M1.1：完整托管场景文档

本阶段保留 C++/Dear ImGui 外壳，C# 管理场景数据。实现位于 `managed/Ncma.Scene`，仅依赖 Ncma.Runtime，无 native/Python 依赖。

## 数据与所有权

- Ncma.Runtime.World 是唯一对象/组件存储；SceneDocument 不维护第二份对象数据库。
- SceneDocument 保存 UUID 关联的 Behaviour 配置（绑定 UUID、TypeName、Enabled、四种标量 Export），不保存运行中的脚本实例或反射对象。
- Host 的 SceneSession 是令牌/生命周期适配；Ncma.Managed.SceneWorld 借用文档的同一个 World。
- SceneDocumentSnapshot JSON v1 包含所有注册组件 TypeId/version/data、扁平对象和脚本配置；作为 `.ncmascene` 文件格式；WorldSnapshot v1 是独立的运行时快照。
- 旧 C++ SceneSnapshot/SceneObjectSnapshot DTO 与恢复入口已删除；新文档仅使用 SceneDocumentCodec 编码，不保留 SceneSnapshotCodec 别名或旧格式转换。SceneDocumentSnapshot 是完整新文档快照，WorldSnapshot 是运行时内部快照，均继续保留。
- 快照和返回的绑定数组隔离；JSON 字段必须显式提供，重复/未知字段被拒绝。未知组件/版本无法恢复，未知脚本名称只保留配置，不在恢复时执行代码。
- 组件字典/装箱仍是正确性基础，不是已优化的 ECS 或资产数据库。

## 原子性、版本和线程

恢复先复制/校验完整候选，再通过 World 内部 PrepareRestore 创建存储、索引和运行时身份，最后一次安装。准备期间禁止重入写入；提交阶段不调用扩展验证器或脚本回调。失败保留原数据、引用和版本；不承诺进程崩溃/内存耗尽恢复。

DocumentRevision 检测 World 外部版本变化，也覆盖元数据修改；外部 World 修改在下次文档访问时合并观察，并清理已销毁 UUID 的绑定。它不是共享命令服务：M1.2 仍需统一事务与编辑历史、拒绝绕过网关的编辑。完整恢复递增一次文档版本，所有运行时引用失效，必须重新按 UUID 查询。SimulationTick 不进入快照，也不被恢复为旧值。

只允许所有者线程访问；更新阶段不可捕获/恢复完整快照或修改绑定；绑定中的 Play 场景不能由编辑桥接恢复。普通 World 读写仍遵守现有 gameplay Begin/Commit/Abort 规则，固定步编辑器接入未实现。

## C++ 接入

M1.1 当时 Scene host bridge 升级为 **v3**；当前 M1.2 已升为 v4，Gameplay bridge 仍为 v3，native plugin ABI 为 v2。旧消费者必须重编译。

- `CaptureDocument / RestoreDocument`：C# 编码的完整有界载荷；C++ 不重新解释/构造文档。
- `CaptureView`：只读界面/脚本绑定投影，不作为持久化、Undo 或 Play 克隆来源。
- `LoadDocument / SaveDocument / ResetDocument`：调用 C# 文件与文档服务；C++ 不提供场景编解码或投影恢复。
- 原有 C++ Undo 栈保存完整载荷和选择 UUID，Play 用完整载荷建立隔离世界；命令和历史业务尚未迁移到 C#。
- SceneCall 21 捕获、22 恢复、23 投影、24 文档版本、25 加载、26 保存、27 重置；旧操作 4/14 已删除；请求/响应上限仍为 4 MiB，caller-owned 缓冲、显式长度、cdecl，异常转换为错误。
- 完整快照不是每帧绘制数据。快照编码采用有界输出流；binding 更新在总大小检查通过后才提交；组件验证器归一化后的单项和完整文档大小也在提交前重新检查。

## 新文件格式与清理

唯一文件格式为 `.ncmascene`：SceneDocument JSON v1。C# SceneDocumentFiles 使用有界读取、完整候选校验和原子恢复；保存先编码全部注册组件与脚本配置，再写入同目录唯一临时文件、Flush(true)，通过 File.Replace/File.Move 安装。失败不会截断原文件，操作自己的临时文件被清理。目标已存在但不是受支持的文档时拒绝覆盖。旧 `.ncscene` v1-v6 编解码、DTO 导入/导出、迁移、旧版本备份及语言槽全部移除，不提供兼容格式；资产引用、Prefab 与完整资产流水线仍未实现。

清除了 12 个 Git 跟踪的旧 Scene/Entity/ComponentPool、原生场景组件和仅依赖旧 Entity 的物理组件/事件包装文件；它们不在现用构建/模块引用链中。保留 SceneUuid（FBX/动画使用）、过渡 Undo 栈、渲染与 Box2D/Jolt 求解内核、SDK 和用户资产。Git 中可恢复删除文件。

## 验证与后续

`Build.bat` 构建 Ncma.Scene.Tests 并将程序集部署到 out/managed；CTest 包含 NcmaSceneDocumentTests。测试覆盖自定义组件、无 Transform、多脚本与全部 Export、深复制、总大小预算、原子失败、文件读写、旧格式拒绝、保存失败保全、异线程、验证重入、引用失效、版本冲突、完整克隆；原生桥接测试覆盖完整 Undo/Redo 和 Play 隔离。保留旧 Runtime、FBX、动画、编辑器及 Python 回归。

后续 M1.2 已迁入独立 Editor.Core：完整文档命令/事务/历史、交互草稿和保存状态由 C# 持有，原生命令栈已删除；见 M1_2_IMPLEMENTATION_PLAN.md 的交付记录。OnFixedUpdate、live MCP、完整资产系统、Python 通信仍未实现。

## 本次验证记录

- `Build.bat -Configuration Debug`：9 项 CTest（含 25 项 SceneDocument、26 项 Runtime 无界面用例）、managed/native smoke、23 项 Python 回归与 CLI inspect 通过。
- 编辑器隐藏启动、FBX 导入、C# Play/重载和完整原生快照 Undo/Redo 回归通过。
- 本记录不代表 Vulkan、固定步编辑器调度或 live MCP 已实现。
