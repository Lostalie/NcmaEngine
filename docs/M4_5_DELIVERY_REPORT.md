# M4.5 动作状态、Notify 与战斗

日期：2026-10-06。M4.4 已提交推送 `447e69f` 后推进本阶段。
状态：K5 自动门禁通过，完整 Debug/Release 回归均通过；人工/真实用户素材/
目标环境/性能/长稳门禁保持开放。不是 Animator 图、Montage 或旧动作实验室兼容层。

## 实现边界

- `ncma.action.definition` v1 为闭合持久值 schema：定义 UUID、四个不同且同 model/rig/generation
  的 Idle/Run/Attack/Dodge clip UUID、normalized hit/cancel/combo/invulnerability windows、damage/reach/mask/buffer。
  `ncma.combat.health` v1 为明确 current/maximum 值。均 startup-only；动作要求 capsule/skin/root/health。
  authoring 复用 Editor.Core 事务、资产预检和 Undo/Redo，恢复后重新按 UUID 解析；无新 MCP 写工具。
- C# 管理 Idle/Run/Attack/Dodge、J/K edge 输入、可信 owner-thread safe-boundary `RequestAction`、
  按 committed tick 到期的单槽 attack/dodge buffer、cancel 与单次 queued combo。
  这不是可从任意 Behaviour 固定步调用的动作命令 SDK；运行中外部控制不得绕过 safe boundary。
  Idle/Run 使用输入 locomotion；Attack/Dodge 用根运动替换水平位移/转向；重力/地面支撑继续由角色服务管理。
  死亡拒绝移动/跳跃/攻击。选 clip/同 clip 重入都使用唯一根运动 ClipClock，成功提交才消费。
- 命中是 K2 **closest ray**，从实际 capsule 中心沿已接受朝向前方查询；忽略自己、使用明确 mask，
  查询 sequence 绑定本量子 solver 结果。不是完整刀刃 sweep/多 hitbox/连续碰撞战斗系统。
  相同攻击 instance 对同一 persistent target 至多一次命中尝试（无敌阻挡也去重）。
  Health 和 Transform 在同一托管候选步由独立 host-only authority 发布；无 native callback 写 World。
- 同量子无敌窗口参与命中判定；多攻击采用稳定 actor UUID 顺序处理，生命值下限为0；
  动作准备依据量子开始的已提交生命值，同步致死命中不撤销本量子已准备的反击，下一量子禁止死者输入。
  当前窗口重叠以整个固定量子为粒度，不声称连续亚帧攻击/无敌时间排序。
- `CombatEvent` 是每次成功提交的有界只读事件快照，不是通用 GameplaySignal mailbox 或网络广播。
  含 session/world/tick/actor/target/稳定 notify UUID/动作 instance/kind/damage；返回复制数组。
  只保留最后提交量子；需要逐步消费的宿主通过 committed observer 读取，不能按 render 帧轮询保证不漏事件。
  start/end 通知按 `[previous,current)`，非 loop 精确终点额外闭合一次；中断主动关闭未关闭窗口。
  每步最多256 events、32 action actors、每 attack instance 至多64 targets；root 原预算保持。
- 数值前捕获非法 Health/definition/control 写会 poison 候选步。只读准备回调中的结构写
  也标记失败，吞异常不能偷偷提交候选。数值后、managed commit 前失败失效整域呈现并 fail-stop，
  旧 tick/文档保留，**不回滚 solver**。commit 后观察器失败保留已提交 damage/tick，仍禁止继续 Play。
  Reload/恢复关闭资源，从 frozen startup 重建新 session/world/solver/clock/动作状态和 health。
  派生资源关闭失败保留 pins、health/transform authority 和 solver ownership；不卸载依赖。
- NCP1/Headless 离帧准备四个 typed clip 引用，无原始 FBX/Editor/MCP/Python 依赖；Headless 不初始化 pose/GPU。
  未更改 native ABI，未恢复旧入口/类型；物理仍默认 disabled。

## 自动验证

`ActionCombatChecks` 用四份不同 root/hand 数值 clip（不是 sausage 的四个 UUID 别名）验证：

- strict schema/不同 UUID/缺失 clip、NCP1 closure；Idle/Run/Attack、输入瞬态与 held 不重复触发。
- 真实 Jolt ray、伤害原子发布、instance/target 去重、复制且带 identity/tick 的事件。
- combo 同 clip 重入归零、cancel 关闭攻击窗口、Dodge exact terminal 通知一次、buffer 过期。
- 两角色同量子 Dodge/Attack 无敌阻挡、下一攻击命中、死亡输入禁用。
- caught Health/definition/control、实际数值后 factory 失败、commit 后 observer 故障的准确边界。
- Pause/Step、冻结 startup Reload；30/60/144Hz/Headless 每 commit 的事件 oracle、health/root 一致。
- 正式 packed/unpacked Headless 攻击路径和 DX11 四 clip 默认动作/pose 路径，graphics validation 0/0。
  DX11 隐藏窗口不伪造真实键盘输入验收；可见 J/K 人工交互仍待验。

实际 EditorWorkspace 测试覆盖动作定义/调参/Undo/Redo/外来引用失败保存、隔离 Play 中 tuned damage、
pose/main/shadow GPU 提交、派生关闭失败和显式重试、Stop 不污染 Edit health/定义。
Movement fake 增加3项 publication/normalizer/只读写测试；fake 不是 Jolt 验收替代。
测试入口处理失败返回非0，避免未经捕获异常残留测试进程。

## 开放项

完整 `Build.bat -Configuration Debug`、随后 `Release` 均 exit0：
各12 native +20 managed CTests，Player53、Editor63、Gameplay53/fakeMovement38、Python43，
managed/native smokes、新格式/旧格式拒绝、inspect、三轮profiles、audit、checked部署及恢复均通过。
日志：`out/verification/m4-5/Debug-verified.log`、`Release-verified.log`；早期失败日志保留。
最终 Release 部署备份为 `out/deployment/0eafc497ca694ba6beb9dde28793ca37/backup`，不加入 Git。
初次集成修正了 frozen-membership+required-publication 的 Runtime 组合约束、Editor action clip
必须解析的预检和测试事务拒绝类型；没有放宽安全门禁。新代码零编译警告。
提交推送并核对远端 SHA 后才进入 M4.6。
真实用户 run/attack/dodge FBX 素材、可见窗口/第三方 MCP、目标环境、CPU/GPU/GC 完整预算和1小时长稳未通过。
完整 Animator/BlendSpace/Montage/IK/重定向留 M6；连续武器 hitbox/通用 GameplaySignal 转发尚未实现。
本阶段不关闭既有 M2/M3 人工门禁，不声明 Vulkan 绘制/通用2D渲染或正式AI传输已实现。
