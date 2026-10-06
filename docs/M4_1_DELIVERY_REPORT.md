# M4.1 运动权威与跨域固定步边界

日期：2026-10-06。基线 `f31d5915c07bcfe0d8e96f1522eb8d9d5d19a49b`。
状态：补强后顺序完整 Debug/Release Build.bat 及专项回归通过，K1 退出门通过；准备独立提交推送。

## 授权与范围

用户在阅读 M4 方案后要求逐个小阶段推进、失败修复复测、通过后提交推送；按该后续指令落实
[M4方案](M4_IMPLEMENTATION_PLAN.md) 中 C# 唯一运动发布和跨域 fail-stop，不恢复兼容旧类型。
本切片只证明协调契约，**没有调用真实 Physics Step、没有 Jolt capsule/CharacterVirtual、
没有接入默认 Editor/Player、没有根运动/动作战斗或新的 MCP 工具**。默认 physicsEnabled=false 不变。

## 实现

- `Ncma.Runtime`：host-only 泛型组件权威，精确 UUID/type/world identity；无 ambient 特权，
  脚本 SDK 不暴露 proof。整个 Transform 保留；已绑定对象删除/加删组件和 World restore 被拒绝。
  捕获非法写也 poison 候选步，覆盖 SDK/Behaviour、System、批量写、直接结构操作和运行命令。
  每个正式非初始化固定步必须由 owner 发布所有保留组件；其他 WorldRunner 不能绕过发布推进 tick。
- `Ncma.Gameplay`：只增加内部受控 participant，无 Physics/Animation/native/Python 依赖。
  Gameplay 暂存 → 输入/World/document/signals 预检 → adapter 预检 → 单次数值执行 → 全结果校验
  → owner 暂存 → 最终准备/提交 → copied committed results → readonly observers。
  预检不安装提交、不重复构造 Behaviour；最终 factory/预算仍可失败，不声称可完全预排除异常。
- `Ncma.Movement`：可信启动注册，显式 startup 副本和精确有序 UUID 映射；最多4096目标。
  一目标/一步最多一个 intent，精确 session/world/tick/revision/sequence，重复/陈旧/外来意图拒绝。
  输入/输出缓存有界，位置绝对值≤1,000,000，单步位移长度≤1000，intent yaw≤π；finite、
  正 scale/unit quaternion、固定 scale、exact result count/order/receipt 都校验。所有结果先验证再暂存。
  这些是安全拒绝预算，不是角色运动参数或性能承诺。contract version1 是托管数值契约，不是 C ABI。
- 资源生命周期：adapter 工厂和 Initialize/Preflight/Execute/Dispose 在 World 只读 guard 下；
  可信 C# adapter 必须遵守“不在 Preflight 推进”的契约，这不是对任意 C#/reflection/native code 的沙箱。
  adapter 关闭失败保留 adapter 与 authority，Faulted/invalid；Stop 可重试，Dispose 不吞掉资源所有权。
  正确顺序：Play.Stop 成功 → coordinator.Dispose → Play.Dispose；不能用强制卸载代替关闭。

## 故障和恢复

| 边界 | 实际语义 |
| --- | --- |
| 数值执行前失败 | World/tick/input消费/signal消费及发送不安装；fake Steps=0；上次耦合快照仍有效，会话 Faulted |
| Execute 开始后、World 提交前失败 | 标记 executionStarted/invalid；World/tick 保持前次成功提交，数值域可能已经改变；不再允许 Resume/推进/读取数值快照 |
| World 提交与耦合发布成功后 observer 失败 | T+1/sequence/有效 committed 数值快照保留，会话 Faulted；不得撤回成功步 |
| Stop 关闭失败 | 不释放 authority/adapter，不 restore/unload，不创建替代 adapter；显式重试 |
| 耦合 Reload | Stop→从冻结 startup 文档 restore→fresh world identity/session/adapter→Paused，数值 sequence 重置；World tick 时间线保留，不伪造回滚 |

耦合 Reload 不迁移 live gameplay 值/私有脚本状态，也没有原子“新旧求解器切换”。Stop 后的关闭/restore/新启动
任一失败都显式终止，旧 solver 不恢复。普通未耦合 Reload 的预检失败保留旧 Paused 行为不变。
故障恢复从原始 startup 重建；直接对同一个未重建 identity 再 Start 被拒绝。
私有脚本/System 字段和外部 IO 不回滚。当前没有动画/GPU由此 coordinator 拥有；M4.3接线时
还必须协调应用的 presentation/clip/physics teardown，不应以本切片声称已完成跨所有插件生命周期。

## 验证

专项：既有 Gameplay **53/53**，新增 Movement **35/35**（多组边界子案例），32次 create/step/close。
覆盖 caught 非法写/结构、batch prefix、独立 runner、旧身份/重复/finite/容量、初始化故障、preflight
失败、partial execute、最后一项损坏的多对象输出、最终托管 factory 失败、输入/信号提交/中止、
之前成功 tick 保留、observer 故障、Pause/Step/render-only、Reload 及失败关闭重试、恢复、线程和重入。
新增微小 quaternion 误差归一化后的数值/World 精确一致，以及自定义组件 normalizer 改变结果时 fail-stop。
deterministic fake 的测试 **不是** Jolt 碰撞、跨CPU位级确定性、性能或人工验收。

首轮全量 Debug 已通过，但代码复核发现独立 WorldRunner 绕过协调的缺口；补强 authority write
completeness 后第二轮 Debug 通过。第一轮 Release 部署重部署 journal 的 File.Replace 出现 IOException，
其余19项托管回归通过。原测试目录/journal保留（未强制删除/替换），journal已记录 RolledBack；
新的隔离测试目录部署专项8/8通过，尚未定位最初 IOException 的占用来源，不把偶发 I/O 冒充已修复。
随后补齐 quaternion/注册校验后的一致性，运行代码变化后重跑双配置完整回归；旧日志均不是最终版证据。

- 首轮（已被补强版替代）：`out/verification/m4-1/Debug-attempt1.log`。
- 第二轮 Debug（归一化前）：`out/verification/m4-1/Debug-pre-normalization.log`。
- 失败 Release：`out/verification/m4-1/Release-attempt1-failed.log`。
- 部署隔离复测：`out/verification/m4-1/Release-deployment-recheck.log`（8/8）。
- 最终 Debug：`out/verification/m4-1/Debug-final.log`（exit0，11项原生+20项托管CTest）。
- 最终 Release：`out/verification/m4-1/Release-final.log`（exit0，11项原生+20项托管CTest）。
- 最终详细 CTest：`Debug-final-ctest.log` / `Release-final-ctest.log`（同目录，含 Gameplay53、Movement35）。
- 同版 DLL 专项复核：`Debug-final-gameplay.log` / `Release-final-gameplay.log`（同目录，53/53+35/35）。

两套完整构建均无新代码编译警告/错误；包含严格新格式/移除格式拒绝、托管/native smoke、
Python43项+inspect、3轮保留runtime测量、source/consumer/package审计和受检部署（不使用Skip）。
系统 Git ignore 配置读取权限警告与行尾提示不是代码编译警告；未改变用户全局配置。
Debug最终受检部署备份 `out/deployment/094a52a4c8a442e09c137bd79e1d4360/backup`；
Release最终备份 `out/deployment/076bd40e707541539e7c24c56de2df9e/backup`，默认入口为此Release整包。
最终部署后审计：Debug `out/verification/m2-8/Debug/275722ab754a41008b89b24e1a32e694/audit.json`；
Release `out/verification/m2-8/Release/4577fa796a3f4b1a8b1b09c7a07cb651/audit.json`。

K1：通过（仅此协调底座）。下一步M4.2实际Jolt角色数值插件，未提前宣布退出K2。
人工 UI/MCP、自包含目标环境、完整性能/长稳、M2 与 M3 尚未闭合的门仍开放；不标记整个 M4 完成。
