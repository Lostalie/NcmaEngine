# M4.7 联合回归与待验门禁

## 自动实现

`ActionJointChecks` 纳入 Player CTest，不是可选人工检查替代。使用同一 Renderer/cache/pose
kernel，0/1/8/32角色各32步预热+32步采样，固定1/60、320×240、vsync=false，普通场景/NCP1
程序化四 clip，实际 Jolt 战斗 query 和 DX11 compute skin/geometry/shadow 路径。
正交镜头显式覆盖全部角色，断言 geometry/shadow/pose 数量，不能通过剔除其余31角色冒充32角色。
0角色只清屏，不建未用的数值域/pose/cache/3D pipeline；空场景和 GPU BUSY 均使用公共轻量清屏，
不等待 GPU、补做 simulation 或伪造成功蒙皮提交。report 保留 submitted/backpressure。

C# Character profiling 为可信宿主显式 `profile:true`，默认关闭，无 IO/等待/历史或 MCP能力。
记录同一成功提交 tick 的 preparation、action policy、root extract、combat/query、commit observer
及 raw native counters/solver time；嵌套作用域不能相加当总耗时。fault/错线程/候选步/未启用/
Reload 后未提交均拒绝读取；重建清空旧 profile tick。未添加插件 ABI 或跨对象语言通路。
总 simulation wall time 在实际固定步入口外测。采样 allocations 覆盖 simulation+scene submission+
present，不含离帧准备和后续诊断/report 编码；GC collection delta 另报。pose/palette/extract/
skin ABI、constant/palette/vertex上传、GPU最后有效观测、资源 counters 保存到忽略目录。

GPU 统计 ABI 当前没有 sample-frame ID，last-valid 异步观测可能重复，**不是32个独立GPU样本或
精确逐帧归因**。仅读取已有 nonblocking 查询，未强制flush/wait。硬件报告含 OS/.NET/CPU/线程数、
Windows registry 中的适配器/驱动列表、插件SHA256、scene/package fixture hash；DX11默认适配器
的实际设备归因仍待补。测量是本机320×240的小程序化 workload，不是用户素材/目标硬件预算或FPS承诺。

32轮真正复用同一 PhysicsService/cache/kernel：成功Attack产生实际damage→蒙皮/主场景/阴影→
注入 native execution 后 managed commit 前失败→旧tick/文档保留且presentation失效→GPU/pose先关闭→
Stop/释放旧runtime→frozen startup新Play重建→新session/world/numerical identity→active Reload→Step→Stop。
Faulted Reload仍拒绝，绝不“自动恢复”或声称solver回滚；恢复health/root/actions/events从startup开始。
每轮核对numerical worlds/pose rigs/clips/cache/GPU meshes/pipelines/materials/textures/targets归零，
成功步 raw LiveJobs=0，API validation 0/0。仅是受控注入与有限循环，不是硬件故障或1小时长稳。

## 复现

正式入口：顺序 `Build.bat -Configuration Debug`、`Build.bat -Configuration Release`。
诊断专项：构建后的 `dotnet managed/Ncma.Player.Tests/bin/<configuration>/net8.0/Ncma.Player.Tests.dll
F:\NcmaEngine <configuration> <对应m2/plugins绝对路径> --action-joint-only`。
每次证据为 `out/verification/m2-7/<configuration>/tests-<uuid>/m4-joint/profile.json`，
CreateNew不覆盖先前结果，完整 Build 日志在 `out/verification/m4-7/Debug.log`、`Release.log`。
完整顺序Debug→Release均exit0：各12 native +20 managed CTests、Player56、Editor64、
Gameplay53/fakeMovement38、Python43；managed/native smokes、严格新格式/旧格式拒绝、inspect、
三轮保留profiles、只读audit、checked部署/恢复通过，新代码零编译警告。
最终Release可恢复部署备份 `out/deployment/984fb26241c04e7da253f444a1d06b2c/backup` 不入Git。

本轮完整构建证据：Debug `tests-c8d3950fa2b84c3b90da5cfeb427664e/m4-joint/profile.json`，
Release `tests-a6cbaa3b22c74b6db2465f715587dcb8/m4-joint/profile.json`，位于各配置的m2-7目录。
本机Windows10/.NET8、Ryzen5600X；registry列出RTX5060Ti驱动32.0.15.9649及Oray虚拟适配器，
未据此推断实际DX11 adapter。最终Release固定步观测如下（毫秒；非已接受预算）：

| 角色 | Simulation median / p95 | 数值Step median | 采样作用域allocated bytes median | 最大palette上传bytes/步 |
|---|---|---|---|---|
| 0 | 0.1780 / 0.3111 | 未初始化 | 55,440 | 0 |
| 1 | 0.8443 / 1.1410 | 0.0257 | 273,664 | 256 |
| 8 | 2.7593 / 4.0790 | 0.0513 | 1,401,784 | 2,048 |
| 32 | 9.1089 / 12.4101 | 0.2396 | 5,445,984 | 8,192 |

各有角色workload的32个采样均实际提交geometry/shadow、无BUSY，source vertex上传为0。
32角色采样发生GC generations0/1/2=14/5/5；托管分配明显偏高，**性能验收未通过/尚无用户预算**。
需针对snapshot/boxing/校验及临时数据开展分配归因/优化，不能将数值Step快或功能正确当成整体高性能。
本报告不声明极致2D效率、用户模型画质或生产动作游戏性能达标。

## K7 开放清单（不能关闭M4）

| 门禁 | 状态 |
|---|---|
| 完整Debug/Release、保留M1–M3/ABI/格式/Python/部署恢复 | 自动通过（见上述日志） |
| 32共享服务 coupled 创建/战斗/数值后故障/恢复/reload/Stop 与资源基线 | 专项和完整回归自动通过 |
| 0/1/8/32角色CPU/GC/upload/数值计数测量 | 本机程序化初测，不是已接受预算 |
| GPU独立样本/实际适配器归因/目标分辨率及性能预算 | 待补和用户确认 |
| 用户真实 idle/run/attack/dodge FBX、root配置、碰撞尺寸、素材许可/期望画面 | 未提供，不用synthetic/sausage替代 |
| 真机可见移动/跳跃/攻击/闪避/障碍、窗口/输入/DPI/Play-Stop/reload | 未验 |
| 真实第三方可见MCP客户端与审批/范围检查 | 未验（内部stdio协议自动检查不是人工验收） |
| 目标环境Player/self-contained与1小时工作流 | 未验 |

仅在证据齐全后关闭K7/M4；不得顺带关闭M2人工/环境/长稳、M3 G6–G9、Vulkan或通用2D门禁。
M5为UI/HUD，M6为Animator节点图；不因这份自动报告把它们标为已实现。
