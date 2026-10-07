# 动作游戏动画系统：首个运行时版本

2026-10-07 当前进度：M3 已有正式 NCA 场景片段与 DX11 GPU 蒙皮，M4 已有 C# 动作/根运动/碰撞候选，
M6 开始正式 C# 图资产和验证。详见 [M6 小阶段方案](M6_IMPLEMENTATION_PLAN.md) 和
[M6.1 图契约](M6_1_GRAPH_CONTRACT.md)。下文程序化实验室是独立保留功能，旧的“尚未接场景/GPU”
描述仅指该实验室接口，不代表 M3/M4 的现有场景链路。M6 编译求值/真实角色图接线/可视编辑/MCP仍未实现，
不把严格图数据、旧实验室或 M3/M4 自动候选视为完整 Animator 验收。

引擎方向：以角色模型、骨骼和动画驱动的动作游戏。参考 UE5 的状态机、
姿势混合、Root Motion、Animation Notifies 的职责划分。
目标职责已调整：动画状态机、动作规则、通知分发和角色运动权威属于 C#；
仅有性能依据的骨骼采样/混合/蒙皮等数值内核保留为 C++ 插件。
M2.5 已迁移独立预览：C# 编辑器 / 隔离 Python 工具保存时钟、动作策略、通知消费和历史；
C++ animation ABI 2 仅提供不可变内置库、采样/混合/根运动/通知区间数值。
正式场景 Animator 与通用图运行时仍未实现，详见 [H5 交付](M2_5_H5_DELIVERY_REPORT.md)。
这不是 UE 资产兼容层，也不是完整的 Animation Blueprint/Montage 实现。

## 已落地

| 层 | 当前实现 |
|---|---|
| 资产数据 | Skeleton、Bone、AnimationClip、TransformKey、Notify；骨架/片段 UUID；加载时校验 |
| 姿势求值 | 平移/缩放线性插值、四元数最短路径插值、缺失轨道使用绑定姿势 |
| 蒙皮准备 | 父节点先于子节点的模型矩阵计算、逆绑定矩阵和 CPU 蒙皮矩阵输出；尚未接 GPU 蒙皮 |
| 混合 | 普通姿势混合、逐骨骼权重遮罩；可中断的缓存源姿势过渡 |
| 根运动 | 提取平移/旋转增量、循环边界累计、从输出骨骼姿势剥离根位移 |
| 动作示例 | Idle/Run、Attack、Dodge；动作结束返回移动状态，保留跨过动作终点的剩余时间 |
| 通知 | 按更新区间派发脚步、命中起止、连击窗口、无敌窗口事件 |
| 编辑器 | Window > Action Animation Lab；骨骼预览、播放、暂停、单步、状态调试、参数与独立撤销历史 |
| C# | `Ncma.ActionAnimationSession` 管理策略/时钟/128 项历史；通过数值动画 C ABI 2 显式采样 |
| AI/MCP | 本地 stdio 服务，8 个真实工具，默认只读、显式开启修改、版本冲突保护、撤销/重做 |

动作实验室使用内置的 12 骨骼程序化角色和 4 个片段。另有独立 FBX 角色导入窗口，
通过 ufbx 读取真实骨架/网格/动画，转为同一 `AnimationLibrary` 并做 CPU 蒙皮线框预览。
详情见 [FBX_IMPORT.md](FBX_IMPORT.md)。当前 C#/MCP 动画会话 API 仍只暴露内置动作实验室；
FBX 未接入游戏场景组件、GPU 蒙皮或 MCP 修改工具。

## 运行时语义

- 坐标约定：示例 Y 向上，+Z 向前，位移单位为米，时间单位为秒。
- 绑定层级只有一个根，按父先子后排列，最多 1024 个骨骼。旋转必须是单位四元数；
  缩放必须为正，根运动不支持根缩放动画。片段至少 0.001 秒。
- `AnimationPlayer::Advance` 接受有限的 `[0,1]` 秒；单次提取最多跨 64 个片段周期，
  避免异常输入造成无限循环。循环时钟保持在片段长度以内。
- 通知时间范围 `(0, Duration]`，派发区间为 `(previous, current]`；0 秒更新不会重新触发。
  单纯 Sample 不发送通知。跨过多个循环会按发生顺序返回全部通知，而非只看终点姿势。
- `RootDelta` 是角色局部刚体增量，包含旋转；应用时用当前角色旋转转换位移。
  实验室直接累积它做预览。将来必须经过角色运动/碰撞控制器，不能穿透场景直接写 Transform。
- 当前过渡是“缓存可见源姿势 → 持续采样目标”，不是双播放器混合，也不是惯性化算法。
  中途再切换会重新缓存当前可见姿势；根运动和通知仅来自目标片段，不按姿势过渡权重缩放。
- Speed 大于 0.1 时使用 Run；Run 播放倍率为 `0.5 + 0.5 * Speed`，这不是 BlendSpace。
  Attack/Dodge 期间不允许随意中断，Attack 只能在 Combo.Open/Close 区间再次触发。
- Hit/Combo/Invulnerability 只是预览标志和事件，不直接处理伤害、碰撞或无敌判定；这些仍由 C# 游戏逻辑负责。
- 每个预览会话保留最多 128 个命令快照，包含姿势、时钟、通知、根位移和参数。
  编辑器自动播放不逐帧添加历史。MCP 工具的时间通过显式 Step 推进；候选 C# 编辑器另有不入历史的自动 Tick（即使 paused 为 true）。
  失败命令不改变状态；新命令清除 redo。Reset 也可撤销。
- 实验室使用独立历史；场景 Ctrl+Z 仍作用于场景，动画请用实验室 Undo/Redo。
- 旧 C++ 对照入口仍采用实验室打开时暂停主 PBR 的绘制策略；候选使用 GUI 复制画布与 GPU reference 合成，互不暂停。
  预览画布不是场景 Animator；没有新自由 docking 或可持久化动画资产编辑器。

## 有针对性的优化

1. 多实例共享不可变、预校验动画库；逆绑定矩阵和骨骼轨道索引只构建一次。
2. 关键帧使用二分查找；骨架矩阵按拓扑顺序线性计算，不递归查找父骨骼。
3. 原生播放器内部复用局部姿势、混合姿势、模型矩阵、蒙皮矩阵缓冲区；新复制 ABI/托管调试路径仍有分配。
4. 只在明确命令边界保存撤销快照；逐帧求值与资产编辑职责分离。
5. 求值不访问场景、渲染器或 Python；将来可按实例分配任务，游戏线程消费结果。

尚未做跨引擎性能对比。事件字符串、调试 JSON 和命令快照仍可能分配内存；
不能称为“全程零分配”或“比 UE5 更快”。暂未实现多线程调度、压缩或动画 LOD。

## C# 使用

```csharp
using Ncma;
using System.Text.Json;

using var preview = new ActionAnimationSession();
preview.TriggerAction("Attack");
preview.Step(0.2);
using var state = JsonDocument.Parse(preview.InspectJson());
bool hitWindow = state.RootElement.GetProperty("hit_window").GetBoolean();
preview.Undo();
```

这是独立预览会话，尚未自动挂载到 GameObject 或 SkinnedMeshComponent；不要将它误当作已完成的游戏角色组件。

## 接下来按动作游戏需求排序

1. FBX 骨架/动画/蒙皮读取与 CPU 线框预览已落地；接下来验证实际游戏角色，
   完成动画资产保存、SkinnedMeshComponent 和场景接入。角色格式优先 FBX，glTF 暂不优先。
2. DX11 GPU 蒙皮与骨骼调试叠加；保留 API 无关数据，随后做 Vulkan 对等实现。
3. C# 角色动画/动作组件 + 固定步更新 + Jolt 插件适配；按性能需要批量调用原生姿势内核。
   Python AI 只返回决策建议，不挂载角色 Behaviour 或持有动作/运动权威；
   根运动经碰撞解算，通知驱动 C# 游戏逻辑。旧 Python 游戏脚本与预览已移除，正式角色动画组件仍未实现。
4. 可持久化 AnimGraph/状态机编辑器、参数/条件、BlendSpace、分层遮罩、缓存姿势。
5. 动作 Montage/Slot/Section、输入缓冲、打断优先级、连招分支和通知状态轨道。
6. Motion Warping、足部/手部 IK、重定向；之后评估 Motion Matching，而不是现在宣称已支持。
7. 动画压缩、任务调度、LOD/预算、性能与确定性回归。
8. MCP 连接活动编辑器：显式会话授权、主线程命令队列、资产事务、dry-run、审计；
   所有修改复用相同命令层，绝不增加任意脚本/系统命令执行入口。

MCP 启动与工具表见 [ANIMATION_MCP.md](ANIMATION_MCP.md)。

## 设计参考

- [UE5 State Machines](https://dev.epicgames.com/documentation/en-us/unreal-engine/state-machines-in-unreal-engine)：状态、条件和过渡职责。
- [UE5 Root Motion](https://dev.epicgames.com/documentation/en-us/unreal-engine/root-motion-in-unreal-engine)：动画根位移与角色运动的边界。
- [UE5 Animation Notifies](https://dev.epicgames.com/documentation/en-us/unreal-engine/animation-notifies-in-unreal-engine)：动画时间轴同步事件。
- [UE5 Animation Optimization](https://dev.epicgames.com/documentation/en-us/unreal-engine/animation-optimization-in-unreal-engine)：作为后续求值调度设计参考，不代表已实现 UE 的线程系统。
