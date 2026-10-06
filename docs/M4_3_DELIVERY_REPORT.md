# M4.3 固定步角色移动与跟随相机

日期：2026-10-06。基线：M4.2 `fbf8a6a7552e90c2d40ee3a403a88b4cb78eea40`。
状态：最终源码的顺序完整Debug/Release Build.bat全部通过，K3自动退出门通过；M4.4未开始。

## 业务边界

新增纯C#应用模块 `Ncma.Characters -> Ncma.Movement / Ncma.Physics / Ncma.Scene.Rendering`。
Runtime/Managed SDK/Gameplay不反向依赖Physics；Player不引入Editor/Gui/MCP/Python。
C++仍只通过已验Physics1.2/Character1.0执行数值，没有原生World或Gameplay回调。

- 注册持久值组件 `ncma.character.capsule`、`ncma.physics.box`、`ncma.camera.follow` v1，
  显式闭合schema、finite/range/单bit类别检查；引用只用持久UUID，不保存native句柄。
  组合预检要求显式Transform、unit scale、角色yaw-only、角色/盒体互斥、相机目标确切存在，
  最多32角色且最多一个本地controlled角色，仍受Runtime共4096对象限制。
- C#通过Play的同一固定步输入执行world-space WASD、对角线归一化、重力、ground支持速度、
  按Space transient跳跃和有界yaw转向。重力/输入/控制模式不是native玩法状态；相机方向不影响WASD。
  不消费渲染帧时钟，不在render/OnUpdate中补步；暂停不推进solver，单步仅一个quantum，失焦清水平输入。
- trusted startup将capsule/static/dynamic box映射到同一PhysicsService的数值world，
  先建盒体再建完整角色集合。无角色但有盒体也走同一协调器；dynamic结果由C#发布，static不接受移动意图。
  Shape/Transform/结构在Play中冻结；配置通过同一通用host-only authority只读冻结，非法写即使被捕获也poison。
  冻结完整组件类型成员集合（含空集合），运行时向未绑定普通对象新增capsule/box/follow同样在solver前拒绝，
  不留下没有数值映射的静默组件；普通未绑定对象的合法spawn/Transform结构命令保留。
  三类组件还通过可信注册的通用`runtimeAttachable:false`策略拒绝运行时AddComponent，
  包括启动时完全无物理绑定、因此不组合数值服务的Play；不为这个负例初始化无用的solver。
  该策略不是可序列化/Agent输入的开关，现有其他组件默认仍可按原规则运行时挂载；托管消费者需整体重建。
  无绑定或默认`physicsEnabled=false`不创建角色/物理world；禁用项目含绑定在Player模块初始化前拒绝。
- `RuntimeSessionOwner`拥有通用composition lease（无native依赖）；Editor从Edit复制Play，
  Player与Headless共用同一业务组合。Stop清理，耦合Reload从冻结startup重建fresh world/session/resource epoch，
  sequence归零但tick不倒退。关闭失败保留owner、catalog/asset pins和Edit freeze，不能继续卸载依赖。
- FollowCamera独立值配置从committed插值目标构建只读camera view，不写Camera或角色World。
  SceneRenderSession在耦合Play使用复制的插值Transform；geometry与shadow共用同一模型值。
  Faulted/外来World不作为有效耦合渲染快照；Editor清空该视图，Player停止运行。
- GPU/派生动画先关闭，随后耦合solver，再释放插件。Editor停止/重载有明确pre-close回调；
  顶层dispose遇关闭失败停止向下释放并保留失败owner，以便显式重试。

## 使用边界

项目显式 `physicsEnabled:true`；场景使用严格`.ncmascene` v1。可信C#注册示意：

```csharp
var components = CharacterComponents.Register(RenderComponentRegistry.CreateRegistry());
var scene = new SceneDocument("Action", components, CharacterComponents.RequireComposition);
var hero = scene.World.CreateObject("Hero");
hero.Set(TransformData.Identity);
hero.Set(CharacterData.Default); // 默认半径.3、半圆柱高度.6，总高度1.8米；脚底原点
var floor = scene.World.CreateObject("Floor");
floor.Set(TransformData.Identity with { Position = new(0, -.5f, 0) });
floor.Set(new BoxColliderData(20, .5f, 20, 1000, 1, false));
```

显示模型仍需已有合法StaticMesh/SkinnedMesh、NCA/NCP1与显式sceneCamera；本组件不自动创建参考图形。
FootOffsetY将模型对象原点转换为数值脚底，单位米；正向为局部-Z，yaw绕+Y。
首版仅capsule/box，不提供mesh terrain、自由teleport、运行中形状编辑、多控制器或camerarelative input。
编辑配置通过原有generic component JSON/Editor.Core事务，启动前注册是可信宿主代码，不是任意Agent执行入口。

## 自动证据与验收

专项已覆盖：

- 实际Jolt 30/60/144Hz与Headless各120quantum一致、floor/wall/坡面、转向、gravity/jump/landing，
  held jump不重复、Pause/Step/focus、fresh Reload与32次start/step/stop资源释放。
- Dynamic box真实重力结果；非法Transform/capsule/follow配置被捕获仍在native前中止；
  包括无角色的纯盒体耦合路径、初始不存在的组件类型新增拒绝和普通结构命令正例。
  真实dense overflow与数值成功后的managed factory失败均使耦合快照invalid，旧managed tick保留。
  committed observer失败保留已成功tick；从未宣称solver回滚。
- Runtime composition startup写拒绝、Stop/失败启动的关闭失败保留catalog ownership并可显式重试。
  无绑定且禁用物理的Play拒绝三类startup-only组件新增，tick/文档不变且数值world始终0。
- 实际EditorWorkspace Play按钮业务路径隔离Edit，真实scene geometry GPU batch、只读插值、
  暂停/单步/停止/重启及derived-close失败保留owner/solver，重试后释放；Edit generation在Edit关闭前仍保持pin。
- ASCII/binary FBX实际SkinnedMesh/ClipPlayback + capsule + follow的图形/Headless Player，
  scene文件不被改写；NCP1保留新增typed值，并验证不含FBX/NCA/authoring catalog的独立资产目录。
  图形验证必须0/0，无reference替代；现有旧格式拒绝、默认无物理/Null路径与M1–M3测试保留。

首次Editor专项失败为测试提前要求Edit准备租约解锁：正常Edit owner仍持有generation pin。
已改为分别验证“Stop后Edit仍持有pin”和“Edit owner关闭后解锁”，不放宽产品租约或删除证据。
完整Debug首次回归的新增NCP1 fixture用根目录`game.ncpak`，被既有`assets/`路径策略正确拒绝；
已修正测试目录而不放宽产品预检。首次19/20托管CTest通过，失败日志保留。
中间Debug-attempt2与Release-attempt1通过后，源码复核补齐完整类型拓扑冻结与新增负例；
这两个中间构建不代表最终源码验收，须重新按Debug→Release完整回归。
Debug-final通过后，再补无绑定Play的startup-only注册策略与负例，最终验收只采用之后的Debug-final2→Release-final。
最终完整构建证据记录于 `out/verification/m4-3/`：

| 最终门禁 | Debug-final2 | Release-final |
| --- | --- | --- |
| Build.bat完整构建/验证（无Skip） | exit 0 | exit 0 |
| 原生CTest | 12/12 | 12/12 |
| 托管/应用CTest | 20/20 | 20/20 |
| Player专项 | 39/39 | 39/39 |
| Editor Services专项 | 63/63 | 63/63 |
| 原有Gameplay + fake Movement | 53 + 35 | 53 + 35 |
| Python工具/格式/边界测试 | 43/43 | 43/43 |
| managed/native smoke、严格新格式/旧格式拒绝、inspect | 通过 | 通过 |
| 保留fixture采样、包哈希审计和带backup/journal部署 | 通过 | 通过 |

`Debug-final2.log`与`Release-final.log`保留完整输出；新托管编译均0警告/0错误。
真实图形专项DX11验证0错误/0警告；fake Movement仍只作为跨域边界证据，不代替真实Jolt案例。
Debug最终安装备份：`out/deployment/3698463b5964423bb370ad40c32a10e2/backup`。
Release最终安装备份：`out/deployment/c6dfd6b11bb64a0daf469bbf713c0b28/backup`。
Release最终审计：`out/verification/m2-8/Release/1abb061655c64c6abdafbeffd9122b71/audit.json`。
最后部署为Release `out/bin/NcmaEngine.exe`；原有人工/目标环境/性能/长稳门禁仍false。
本阶段新增项目已列入NcmaEngine.sln；只提交源码/测试/文档，不提交证据、部署、备份和无关.vs/.user。

## 保持开放

本阶段不是M4完成：root motion只报告，M4.4根通道去重/碰撞接入、M4.5动作战斗、
M4.6调试/只读MCP/正式样例和M4.7性能/真机可玩验收未实现。
保留M2人工/目标环境/性能/长稳与M3 G6–G9门禁；隐藏窗口/注入输入不替代人工与用户真实动作素材。
当前RenderView dictionary/boxed组件提取是有界正确性实现，有帧分配，尚无高性能ECS或极致效率承诺。
默认Physics仍禁用，没有新增AI传输、Python玩法或网络耦合。
