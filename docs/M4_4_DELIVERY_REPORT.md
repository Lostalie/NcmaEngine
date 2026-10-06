# M4.4 根运动接入

日期：2026-10-06。基线：M4.3 `85a2bf3d512f99f2bf8720e7f490d1ff008f59c9`。
状态：最终源码的顺序完整 Debug/Release Build.bat 全部通过，K4 自动退出门通过。
M4.5尚未开始；提交推送并确认远端SHA后推进。人工/目标环境/性能/长稳仍开放。

## 实现边界

- 新持久值组件 `ncma.animation.root_motion` v1，闭合 schema，显式 rig root index，
  startup-only；要求同对象 capsule + SkinnedMesh + ClipPlayback。无场景父子树或原生 World。
- 可信启动从已提交 NCA/NCP1 预备并 pin 精确 model/mesh/skeleton/clip generation。
  支持 canonical metres、单 top-level root、unit root scale、XZ 位移/+Y yaw；
  pitch/roll、多个独立根、缩放根或无匹配 generation 明确拒绝。
  模型原点到 capsule 脚底使用已有 `CharacterData.FootOffsetY`，单位米；不隐式猜测额外坐标转换。
- `RootMotionTrack` 是 immutable C# 根轨迹采样；不是通用姿态/蒙皮内核替代。
  通过有界 unwrapped 时间区间和 SE(2) 周期组合处理循环边界/多圈/终点，
  每步至多32次跨圈、位移至多1000米、yaw绝对值至多π，超预算在 solver 前失败。
- 每对象仅一个 committed `ClipClock`：`PrepareNext` 不消费时钟；成功提交后观察一次。
  同代角色共享 immutable根轨迹缓存；全场景至多128轨迹/262144个根key，超过预算离帧拒绝。
  root 驱动和 Play 视觉共同使用它，未保留第二个呈现时钟。暂停/零速不推进，非循环终点 clamp。
  clip/startTime 变化在下一成功量子重置并消费一个区间；speed/loop/playing变化保留时间。
- C# root 意图替换水平 locomotion 和输入转向，重力/ground support/jump仍由角色策略负责。
  真实 Jolt capsule 返回碰撞约束结果，仍由唯一 MovementCoordinator 发布 Transform。
  bound root/skin/playback元数据冻结；捕获非法写仍 poison，不能绕过运动权威。
  `SetRootPlayback` 为可信 owner-thread safe-boundary控制，不是 Agent/任意输入入口；
  只选已 pin 片段，运行时控制不是 authoring 文档编辑，Edit仍走既有命令/Undo。
- 模型空间先移除抽取的期望 XZ/Yaw，再按每 mesh inverse-bind 构成 palette；保留 Y/子骨骼运动。
  不减去碰撞后实际位移，因此阻挡时视觉不会积累欲移动根量或重复穿墙。
  geometry/shadow继续共享 GPU compute 输出；无逐帧 CPU skin 顶点上传或 render 补物理步。
- Headless root 场景也做离帧资产准备，但不创建 pose/GPU/platform资源。
  Editor隔离Play与Player共享上述业务。失败前不消费时间；solver执行后、托管提交前失败
  保留旧tick并失效整域呈现，绝不宣称solver回滚。Stop/Reload重建冻结startup及全新identity/clock。
  派生GPU/pose先释放，再solver；关闭失败保留ownership和pins，不向下卸载。

## 专项自动证据

项目需显式 `physicsEnabled:true`，同一角色具备已有合法 `SkinnedMeshData`、
`CharacterData`、`ClipPlaybackData` 后添加 `new RootMotionData(0)`；只有满足上述根约束的
片段能启用。初始运行时播放源由持久ClipPlayback提供，可信宿主可在safe boundary通过
`CharacterPlayRuntime.SetRootPlayback`更改已pin片段/速度/播放/循环设置；这不改Edit文档。

首轮完整Debug发现两个旧测试假设：注册表仍硬编码7项（新增root后8项），
Editor root夹具仍要求D输入转向（root模式必须替换输入转向）。修正断言并保留失败日志，
没有放宽产品权限/数值检查。专项初轮还修正了测试GPU capture输出48-byte stride，
以及释放独立PhysicsService后再启动正式Player的单模块owner边界；未改成双owner。

`managed/Ncma.Player.Tests/RootMotionChecks.cs` 覆盖：

- 位移 oracle：跨边界、多圈、exact end、非交换 yaw/translation周期、非法index/scale/预算。
- 真实 Jolt 墙面限制X、沿Z滑动，root替换D输入；30/60/144Hz和Headless的120量子结果一致。
- 同一 committed time、暂停/单步、speed/换clip/非loop终点；期望与实际位移分开诊断。
- caught playback写失败在native之前；实际数值执行后的managed factory失败不消费tick，失效呈现。
- 重导入新 generation，旧 prepared lease 保持pin；8轮冻结startup Reload/reset/close。
- native GPU vertex capture验证root去重，墙阻挡和loop期间模型局部顶点不漂移，图形validation0/0；
  render alpha变化不修改tick。
- 正式DX11/Headless、NCP1/未打包Player都走真实root路径；Headless无图形模块初始化。
- genuine binary FBX rig/clip root采样；另外原ASCII/binary FBX Player回归开启root组件，
  包含coupled/packed/Headless与源文件隔离。此证据不是用户run/attack/dodge素材验收。

正式EditorWorkspace测试使用持久root clip + capsule，验证隔离Edit、实际GPU提交、
wall约束、FollowView、Play/Stop重复、关闭失败的pins/solver保留与显式重试。

## 保持开放

最终 `out/verification/m4-4/Debug-final.log`、`Release-final.log` 均 exit0：
各12 native +20 managed CTests，Player46、Editor63、Gameplay53/fakeMovement35、Python43，
managed/native smokes、新格式与旧格式拒绝、inspect、三轮profiles、audit与checked部署恢复均通过。
首轮失败的 `Debug.log` 保留；新增C#代码零编译警告。当前安装为Release包，
`out/bin/NcmaEngine.exe`保留正式C#入口；备份/journal不加入Git。


M4.5动作/Notify/战斗、M4.6新只读MCP/完整样例、M4.7人工真实可玩/目标环境/性能/长稳未完成。
M2人工门和M3 G6–G9不因本阶段关闭；物理默认仍为disabled，Vulkan/Animator图/全3D根运动未实现。
当前 C# root小轨迹是正确性实现，尚无“性能优于native”结论，需M4.7锁定预算测量。
