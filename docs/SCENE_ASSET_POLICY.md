# 场景资产闭包：显式宿主策略

2026-10-24，采用增强版方案 2：在共享资产服务中定义策略，Player Headless（方案 1）和 Editor cpuOnly（方案 3）只负责选择同一策略，不各自添加 null 豁免。

## 合同

| 宿主启动模式 | 固定策略 | 必须解析的资源 |
| --- | --- | --- |
| Player DX11 / 普通 Editor | `PresentationAndSimulation` | 呈现与模拟的全部已声明依赖 |
| Player `--headless` / Editor `cpuOnly` | `Simulation` | 真实模拟消费者的依赖；无活动依赖时才允许无租约 |

默认策略为完整模式。策略来自可信宿主的显式启动选择；不能从 renderer 为 null、初始化失败、资源缺失推导，也不能运行中自动降级。EditorSessionOwner 的策略构造后不可更改。未支持的枚举值直接拒绝。

`References`、`Prepare`、`SceneRenderValidation.Inspect` 与 `ResolveDependencies` 使用相同策略。准备租约携带策略，借用保留策略，解析时策略不匹配拒绝。闭包 v2 的确定性哈希包含策略；回执区分呈现域是否启用、`resolved` 与 `not-required`。`not-required` 表示当前场景没有活动资产根，不表示验证了全部视觉资产。

当前模拟消费者：

- Animator 的图、骨架及绑定模型/网格；图所需的传递依赖仍由原加载器解析。
- RootMotion 的模型/网格、骨架和 ClipPlayback；ActionDefinition 的动作 Clip 仍必需。
- 单独的 ClipPlayback 当前仅驱动呈现，不构成 Headless 模拟依赖。

纯静态网格、材质/覆盖、环境和无模拟消费者的蒙皮/Clip 在 Simulation 下不要求视觉资产租约。数据的 schema、组合、数值、对象引用等原有检查不放宽；例如缺 Transform、非法 RootMotion 组合仍拒绝。脚本目录、物理启用、UI 包及 UI 行为继续使用各自的原合同，本次没有将它们整体豁免。

## 启动与失败边界

1. 宿主确定策略并冻结作者场景输入。
2. 按策略收集资产根并显式准备；非空根缺租约、错误类型/代次/哈希、未解析项均拒绝。
3. 解析资产与脚本闭包，确认当前来源和租约。
4. 才创建并启动实际运行实例；失败不写回作者模型、不默默转为 Headless。

图形模式的资产准备仍在 GPU/游戏逻辑启动前完成。CPU Editor 不准备 Play HUD/GPU 后处理文件，但保留后处理值检查及原模拟组合。现有事务、Undo、AI/MCP 权限没有扩大；策略不是远程权限或新的自动执行入口。

## 资源包与路径边界

- Simulation 没有活动资产根时，不解析无关的视觉 `.ncpak` 或视觉资产目录内容；有活动根时，所选资源包仍完整验证，失败不回退作者文件。
- `ProjectContext` 对显式配置路径的存在性、边界及 reparse 检查继续生效。低层准备函数不读取无关包，不等于宿主接受任意缺失/非法配置路径。
- 本次不修改 NCA/NCPAK 格式。模型包的同代成员可能共同驻留，不能据此宣称模拟模式完全不持有视觉字节或已实现按资源块裁剪。
- 暂无运行中追加未声明动态资源的豁免；扩展消费者必须进入显式闭包，不能依赖脚本字符串猜测。

## 验证范围

新增策略/闭包五组、真实 CPU Editor 一组，并复用原六组资产与脚本闭包验证。Player 保留真实 ASCII/binary FBX、32 次资源生命周期、图形/Headless、打包/未打包和实际 RootMotion 路径；损坏包分为“纯视觉 Headless 不消费”和“真实模拟依赖必须拒绝”两类，图形与 manifest 完整性断言仍保留。

构建与实际执行结果见 [E6 验证记录](E6_UNIFIED_VALIDATION_PLAN.md)。本策略的定向回归不替代整个 E6 的 100 入口双配置验收；E5-D 目标硬件性能测试仍延期。
