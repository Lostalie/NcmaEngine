# M3 资产系统与 FBX 场景角色实施方案

日期：2026-10-05。基线：165c50b5a77e9ab7b81ce6ff19147f9a1eb27e3b。
性质：实施方案。M3.1–M3.3 最终完整 Debug/Release 回归通过，G1/G2/G3 资源门禁关闭；M3.4 已补真实静态 DX11 场景、CPU/GPU 租约与 Editor/Player 消费，最终回归/G4 结论见 [GPU 记录](M3_4_GPU_DELIVERY_REPORT.md)。M3.5 已补正式 NCA/场景角色/最小片段播放与 DX11 GPU 蒙皮，最终双配置通过、G5 关闭，见 [交付记录](M3_5_GPU_DELIVERY_REPORT.md)；M3.6 候选资产工作流/离屏视口已接线，缩略图/独立材质浏览/人工项待补、G6开放，见 [记录](M3_6_DELIVERY_REPORT.md)；M3.7 已按用户要求推进严格格式、只读提取/放置预检候选，实例发布/覆盖/恢复未实现、G7开放，见 [候选记录](M3_7_FOUNDATION_REPORT.md)；M3.8 已启动只读资产 MCP 候选，G8开放，见 [记录](M3_8_READONLY_REPORT.md)；M3.9 已按用户要求启动已提交 generation 的 source-free 运行包/Player 候选，cold cook/正式导出与联合人工验收未实现、G9开放，见 [运行包记录](M3_9_RUNTIME_PACKAGE_REPORT.md) 和 [联合状态](M3_DELIVERY_REPORT.md)。历史范围见 [M3.1记录](M3_1_DELIVERY_REPORT.md)、[M3.2记录](M3_2_DELIVERY_REPORT.md)、[M3.3-B/C/D记录](M3_3_BCD_DELIVERY_REPORT.md)、[M3.4启动记录](M3_4_FOUNDATION_REPORT.md) 和 [资产解析记录](M3_4_ASSET_RESOLVER_REPORT.md)，不将当前切片视为完整后端完成。

M3 的终点是：真实 FBX 角色经过受控导入成为持久资产，重启后可实例化到扁平场景，
在 Editor 和 DX11 Player 中播放动画、显示 PBR 材质和阴影；重新导入、编辑和获批 Agent 修改可恢复。
它不是战斗系统、完整动画图、Figma UI、Vulkan 绘制或最终通用发布器。

## 1 当前基线与进入条件

M3.8 后续已补只读资产的正式UI精确范围/客户端集合审核、60秒授权/撤权及直接apphost长prepared路径修复；完整测试与剩余范围见 [后续记录](M3_8_UI_AUTHORIZATION_REPORT.md)。异步导入与Prefab修改尚未实现，G7/G8仍开放。

| 模块 | 当前实际能力 | M3 缺口 |
| --- | --- | --- |
| 应用与编辑 | C# apphost 已成为 out/bin 默认入口，原生 ImGui 为展示插件；旧宿主/场景桥已删除 | 增加资产业务，不恢复原生 World/hostfxr |
| 场景与玩法 | C# World、SceneDocument JSON v1、唯一编辑历史、隔离 Play、固定步和本地 scoped MCP；M3.4 typed 资产/网格/相机/灯光，M3.5 ClipPlayback | 完整 Inspector、Prefab、动画动作/物理调度 |
| FBX | G2 持久导入/异步 Worker/取消/原始流；M3.5 骨架/片段 NCA → 场景 GPU，Character ABI 2 仅诊断 | 完整源材质/纹理和用户 DCC 覆盖 |
| Renderer | M3.3 资源/v1/v2/v3；M3.4 additive v4 Scene/Editor/Player、单方向光 shadow/HDR/tone、CPU/GPU lease、typed Graph；M3.5 query 5 GPU 蒙皮共享主画面/阴影；M3.6 GUI1.3 opaque离屏合成候选 | 缩略图、CSM/contact、IBL 与通用多阶段资源图未实现 |
| 组件注册 | 值类型组件、Guid/标量/嵌套值字段；现有 schema 检查支持闭合对象，不支持通用数组 | 不能把材质数组、骨骼数组、资源对象塞入组件 |
| 验证 | M3.5 最终双配置各 31 CTest、43 Python；真实 GPU/API 数值/图像与 Player、部署恢复均验证 | 不代替用户真实 FBX、人工/长稳/生产性能覆盖 |

M2 人工 UI/MCP、自包含目标环境、完整性能/长稳和剩余策略审查仍待完成。
用户已授权实施；继续记录哪些 M2 风险带入后续验证，不暗中关闭 M2/H8。
保留 net8.0 和现有 SDK，本阶段不绑定框架升级或全面 ECS 重写。

## 2 小阶段与依赖

| 小阶段 | 方案 | 核心交付 | 退出门禁 |
| --- | --- | --- | --- |
| M3.1 | [资产身份与数据库](M3_1_IMPLEMENTATION_PLAN.md) | 持久 UUID、描述文件、索引、引用与命令参与者 | G1 重启身份稳定、严格格式/路径、元数据 Undo |
| M3.2 | [异步 FBX 导入与派生数据](M3_2_IMPLEMENTATION_PLAN.md) | 隔离导入任务、可取消、原始数据、重导入匹配与 journal | G2 失败/取消不破坏上一代资产 |
| M3.3 | [网格纹理材质与渲染资源](M3_3_IMPLEMENTATION_PLAN.md) | 通用 mesh/texture/material 资源及 scene-render 服务 | G3 真实静态资源绘制与 API 验证 |
| M3.4 | [场景组件与 DX11 场景渲染](M3_4_IMPLEMENTATION_PLAN.md) | 相机/灯光/网格组件、提取/裁剪、共享 3D 管线 | G4 多对象场景，不靠 reference cube 冒充 |
| M3.5 | [GPU 蒙皮与片段播放](M3_5_IMPLEMENTATION_PLAN.md) | 真实骨架/片段数值资源、C# 播放控制、GPU skinning | G5 CPU/GPU 与外部参考比较 |
| M3.6 | [资产浏览与离屏编辑视口](M3_6_IMPLEMENTATION_PLAN.md) | 导入面板/Inspector、拖入场景、离屏图像、编辑意图 | G6 保存重启、编辑/Play 隔离和窗口验证 |
| M3.7 | [扁平 Prefab 与实例覆盖](M3_7_IMPLEMENTATION_PLAN.md) | 模板、映射、显式覆盖/同步和冲突计划 | G7 无父子继承、单对象删除、实例 Undo |
| M3.8 | [资产与 Prefab 的 MCP 能力](M3_8_IMPLEMENTATION_PLAN.md) | 数据检查、导入计划/任务、授权提交、共享 Undo | G8 UI/Agent 同一路径、权限/陈旧任务负例 |
| M3.9 | [Player 资产包与联合验收](M3_9_IMPLEMENTATION_PLAN.md) | 最小依赖闭包 cook、无 FBX 解析器 Player、完整交付证据 | G9 冷缓存重建与真实角色端到端 |

推荐按 M3.1→M3.9 串行交付，每阶段先完成自身测试，再运行完整 Debug/Release Build.bat。
G3/G5/G6 另需实际 GPU/窗口证据；G8 另需第三方可见客户端；没有人工证据只标自动门禁通过。
M3.1 定义命令参与者底座，M3.2 起就要求本地资产修改可撤销；不能等到 M3.8 才补权限/恢复。

## 3 模块与所有权

以下均为拟新增或扩展模块，不是当前工程清单。

| 模块 | 所有权与依赖 | 不承担 |
| --- | --- | --- |
| Ncma.Assets | BCL-only 的 UUID/资产引用/描述格式/依赖与只读索引，独立于 World | GUI、native 句柄、导入线程 |
| Ncma.Assets.Authoring | 导入配置、来源许可、身份匹配、journal、编辑命令参与者；组合 Assets/Editor.Core | 第二套 scene Undo、任意文件/代码执行 |
| Ncma.Assets.Runtime | 只读版本解析、依赖装载、租约/预算/缓存，不依赖 Editor | FBX 解析、MCP、authoring 写入 |
| Ncma.Asset.ImportWorker | C# 工具进程，调用原生解析/解码，复制有界结果，后台不接触 live World | 场景提交、GPU、脚本运行 |
| NcmaImportKernel | 工具侧 ufbx/必要图像解码数值插件，版本化 C ABI | 数据库、子资产身份决策、文件提交或 Undo |
| Ncma.Scene.Rendering | 值组件注册、只读渲染提取、相机/灯光/可见性服务 | D3D/Vulkan 类型、第二份 World |
| Ncma.Animation | C# 片段实例/时间/固定步/只读姿态服务；M3 仅最小播放器 | 完整状态机/节点图、物理移动权 |
| NcmaAnimationKernel | 不可变骨架/片段的采样与矩阵数值计算，无 ufbx/窗口/GPU | clip 时钟、Agent、高层动作状态 |
| Ncma.Rendering / NcmaRenderer | C# 图/资源租约/批次，native GPU 执行 | 场景存储、导入政策、World 回调 |
| Ncma.Editor.Services/App | 组合上述服务、语义面板与审批/选择；复用唯一 EditSession | 原生指针型 ImGui TextureID |

Scene/Runtime 仍可无资产、无窗口运行。新增组件在可信 Editor/Player bootstrap 中共同注册；
不让 Runtime 引用 Editor 或根据 Agent JSON 动态加载组件程序集。
项目可合并薄库，但上述依赖方向与测试不得合并成循环。

## 4 数据身份与持久化

- 资产/子资产使用持久 UUID；版本另用内容 hash 和 generation，运行时资源句柄另有 device/session generation。
  禁止把三者互用。Joint/palette 数字是资产内索引，不是 World 或 GPU 句柄。
- 建议采用 assets/ 中受版本控制的来源、配置和描述文件，以及 out/assets/<projectUuid>/ 中可重建派生数据。
  采用 .ncmeta JSON v1、类型化 .ncmaterial/.ncprefab JSON v1 和派生 .nca 数据块；名称/版本在 G1 冻结。
  不改变 .ncmascene JSON v1 的现有结构；仅新增显式注册组件版本，不恢复 .ncscene 或兼容桥。
- 索引可从持久描述重建；派生缓存不能成为 UUID 的唯一来源。文件移动由显式命令连同元数据完成。
  外部手工移动/覆盖仅产生待协调诊断，不自动重新分配 UUID 或强行修改场景。
- 子资产匹配优先已有映射与可信来源标识，再用限定结构特征；名字/数组下标不是稳定身份。
  重名、拓扑变化、骨架变化或不唯一匹配必须返回冲突计划，缺失 UUID 保留 tombstone，不错误复用。
- descriptor/model/material 可以拥有有界数组；World 组件仅持有 UUID 和小值数据。
  多材质以 MaterialSet 资产引用，骨骼以独立 rig 资源保存，不破坏现有值组件/schema 约束。

## 5 坐标与数值契约

G1 必须先锁定：资产与新场景渲染统一右手系、+Y 向上、米；约定角色前向 -Z。
沿用现有 FBX 转换结果，不对它再施加隐式反射。DX11 不强制采用左手数学，
新 scene-render 服务使用明确的右手 view/projection，深度范围与 viewport 约定独立记录。
旧 reference ABI 的 LH 标注和冻结测试保留原语义，不能把其 112-byte 帧 DTO 重新解释为新场景包。

Eigen/HLSL 使用列向量/column-major；System.Numerics 的行向量通过唯一编码适配转换，
必须测试非对称 TRS、平移、轴向、绕序、UV 与法线，而不是只验证 identity cube。
FBX 每网格 palette = boneModel × geometryToBone；不能用一份 skeleton inverseBind 替代所有 mesh cluster。
对象 world 变换在蒙皮结果后只应用一次。静态网格支持正非均匀缩放及正确法线变换；
M3 蒙皮对非均匀骨骼缩放先严格拒绝或经专门数值验收后开放，负/奇异/剪切保持明确限制。

## 6 并发提交与历史

导入默认使用工具进程：C# 调度与身份策略 + 原生解析，任务仅产出隔离的候选 generation。
不得直接 Task.Run 现有 FbxPreviewSession/ImportedCharacterResource：它们有 owner-thread 限制，
且现有 Character ABI 在全局资源锁内解析，可能阻塞预览采样。

worker 只读经确认来源，写自己的 ignored staging。应用 owner thread 在安全边界验证项目/会话、
asset revision、source hash、设置 hash、授权和取消状态，再发布一次可撤销提交。
大文件 hash/候选验证与持久 IO 准备在后台完成；准备器持有防改写/防删除的来源租约到发布确认。
owner 只重检已验证来源身份/租约、revision和批准范围并执行短发布点，不在 MCP pump 同步扫描256MiB文件。
若无法取得可靠来源租约，重新异步预检或拒绝提交，不能仅用mtime当内容身份；2ms仍是现有软预算而非硬实时保证。
Play pin 住已取得资产版本；重导入不会改活动 rig 或突然替换 GPU buffer，默认 Stop 后采用新版本。
被历史/Play/GPU 租约引用的 generation 不得缓存回收。

扩展 Editor.Core 的唯一历史以接纳受控资产命令参与者；Core 不直接依赖文件 IO 或 Assets 实现。
资产 before/after 使用版本引用与有界 memento，不把数百 MiB 网格写入 16 MiB scene history。
单场景历史、资产文件 journal 与 GPU 生命周期不是一个数据库事务：commit 前准备/补偿，
用 journal+发布点保证恢复；提交后 GPU 故障是 fail-stop/显式恢复，不虚称 GPU 执行可撤销。
Undo/Redo 再次校验原始能力、范围、外部文件 hash 与 lease，冲突不得覆盖用户新内容。

## 7 预算与性能原则

以下为实施初值建议，不是已测性能或对用户模型的支持保证。超过硬限明确失败，禁止静默截断。

| 项目 | 初值与限制 |
| --- | --- |
| FBX | 沿用 256 MiB 文件、200 万角点/关键帧、1024 骨骼/辅助节点、600s 片段、1–120Hz |
| 导入任务 | 1 个活动 worker、最多 4 个候选请求；进度节流 10Hz，退出等待有上限 |
| 资产元数据 | 单描述 ≤4MiB、分页索引/报告；二进制块有版本/长度/hash/溢出检查 |
| 纹理解码 | PNG/JPEG 起步；最长边 ≤4096、单张解码 RGBA ≤64MiB；其他格式明确拒绝 |
| 场景提交 | ≤4096 draw items/批次；透明先不做，超限返回结构化预算错误 |
| 蒙皮 | ≤1024 bones 与每 mesh palette；先测 0/1/8/32 个角色，不据样例宣称万人动画 |
| GPU | 总资源预算建议 512MiB、每帧上传建议 16MiB，取设备能力与项目限额的较小值 |
| Scene/MCP/history | 保留既有 4MiB document、64KiB 请求、128 操作、64 历史/16MiB 等限制 |

每帧输入为复制的紧凑视图/缓存的资源/变化的姿态，不序列化整个 SceneDocument，不每帧 CPU 蒙皮上传全部顶点。
图、材质和资源稳定时复用；CPU→GPU 数据的增长应随骨骼/可见批次而非 mesh 顶点数增长。
测量 simulation、提取、采样、palette、ABI、上传、submit/GPU、GC/native live；不把历史 WorldRunner 高分配隐藏为 M3 零分配。

2D 与 3D 共用基础资源契约，但纯 2D/Null 不创建 shadow/rig/PBR/3D reference 资源。
默认 3D 管线继续提供参数、Feature、stage/整管线替换；用户不手写 GPU API 同步才能替换模块。
M3 不实现完整 2D 产品管线，也不将其接入必需 3D 初始化。

## 8 验收与交付记录

所有新增工程/测试接入 NcmaEngine.sln、CMake/Build.ps1/CTest；Windows 只推荐 Build.bat，
先 Core/Native/Architecture，再完整 managed/native/Python/inspect。每阶段通过自身测试后，
顺序执行 Debug 和 Release，不能并行改共享 out/bin/out/managed。
GPU 项必须有固定场景图像、D3D11 Debug Layer 的实际错误/警告 0、资源释放证据。
旧 M2 冻结参考不重新生成来掩盖回归；新的 M3 参考需记录外部来源/捕获版本/容差。

每小阶段交付记录必须包含实现边界、实际 ABI/格式、提交身份、命令/日志、正负例、性能环境和人工未验项。
用户真实角色/纹理尚未提供：规划使用现有可分发 fixture 先回归，另需用户批准的真实 FBX、许可及期望图像。
没有这些输入不能称“所有 FBX 兼容”或 G9 真实素材验收通过；不将用户私有素材自动提交 GitHub。

## 9 技术依据

当前事实以本仓库 FbxCharacterImporter、NcmaCharacterApi、ImportedCharacterResource、
RenderFrameView、NcmaRenderer.h、NcmaGui.h、ComponentRegistry 和 EditSession 为准。
上游说明仅支撑算法/API 语义，不证明 NcmaEngine 已实现以下规划：

- 每网格 geometry_to_bone 的绑定语义与有限权重处理见 [ufbx deformers](https://ufbx.github.io/elements/deformers/)。
- 解析进度/取消及外部文件选项见 [ufbx reference](https://ufbx.github.io/reference)，实施仍核对 vendored 0.23.0。
- 静态、动态与读回资源的用途见 [Microsoft D3D11_USAGE](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_usage)；本方案建议常驻网格、变化姿态与测试专用读回分离。
