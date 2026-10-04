# M2.6：独立物理插件交付记录

日期：2026-10-04。状态：薄 C++ 插件 + C# 高层物理服务已实现；追加方案 Debug/Release 完整回归通过，H6 自动门禁通过。

## 实现范围

- `NcmaPhysics.dll` 私有链接 Box2D/Jolt/Eigen，无 Core、Renderer、GLFW、GUI、hostfxr、World/Editor 依赖。
- 数值内核从 NcmaCore 编译目标迁出；原有 C++ 比较调用暂时借助私有 C++ 方法导出复用同一个 DLL，SDK 类型已从这些头文件隐藏。这些过渡导出不是公共插件契约，H8 再随旧调用方审计清理。本阶段没有删除旧入口。
- 公共 Physics Module ABI 1.1，152 字节函数表及72-byte原始计数；保留1.0的144-byte函数表，旧函数偏移不变；C / C++ / C# 固定布局检查。C# 仅调用版本化 C ABI，复制 POD；不传 STL、Eigen、Jolt、Box2D、异常或游戏对象。
- `Ncma.Physics` 只依赖 Ncma.Interop，缓存函数委托，以 caller-owned Span/数组批量调用；无逐对象 P/Invoke、反射、native memory view 或 finalizer。
- 2D/3D 显式 DTO 与方法，box 创建、销毁、线速度写入、同步 Step、带模拟序号状态读取、统计和关闭。无 raycast/contact/constraints/capsule/Character 占位接口。
- `.ncmaproject` 可选 `physicsEnabled` 默认 false，旧项目缺省保持禁用。候选程序仅显式启用模块，不创建场景刚体、不派发 Play 的物理 Step。纯托管 Runtime/headless 无物理依赖。
- 启用但缺失 DLL 或加载失败明确报错；不静默降级。初始化日志复用候选程序的 correlation；通用 Console/Agent physics inspection 面板尚未添加。

## 契约与失败语义

最多 16 个 world、每 world/批次 4096 个 body。owner-thread-only；2D substeps 1..16，3D 当前只支持 1；dt (0,0.25]。位置/速度/重力有界且 finite，half extent [0.001,1000]，density [0.001,10000]。

米、秒；2D density 为 kg/m²，3D 为 kg/m³。3D 不再将 density 偷换为质量；rotation 为 xyzw quaternion，2D angle 为 radians。floor fixture 容差 0.08m、静止速度 <0.1m/s；不是跨 CPU 逐位确定性。

模块生命周期内不复用资源 token；world map 校验 owner、维度和失效句柄。所有句柄仅用于求解器资源，不能保存到场景。DLL 卸载之后任何 token 均禁止继续使用。

Create 完整校验/输出容量预检；内部创建失败撤销本批新 body，输出不部分写回。Set/Destroy 完整校验全部目标，拒绝重复，之后才修改。Step 必须预检 sequence 输出；Read 要求当前精确模拟序号，容量不足/旧序号不会重复 Step。序号表示模拟步数，不是不可变快照；同一步的 SetVelocities 会改变随后的查询。

求解异常进入 WorldFaulted，不能继续 Create/Set/DestroyBodies/Step/Read；仅 stats/destroy_world 可用。**这不是 solver rollback**，没有接入 SceneDocument 事务。原生白盒测试注入执行异常，不宣称制造过真实 Jolt 硬件/内部故障。

Jolt factory/注册与两线程 worker pool 集中在 DLL 单一共享 owner，首个 3D world 获取、最后一个释放；world allocator 独立。Update 同步等待 job barrier，返回后 outstanding jobs 为 0；最后 owner join workers，再销毁 factory。没有异步调度、热卸载或可中断的 shutdown timeout：若 solver 自身不返回，不能强卸载 DLL。模块在有 world 时拒绝 shutdown，C# lease 保留模块至成功 destroy。

统计：live worlds/bodies/jobs、sequence、批次、跨界复制 bytes、errors；C# service 聚合最近256次 solver Step median/p95、生命周期 max；1.0 legacy Stats仅在查询时做原生quantile兼容，不再在Step排序。复制 bytes 不是内存占用；idle workers 不是 outstanding jobs。

## C# 高层服务补齐（本次）

PhysicsService/PhysicsSimulation：只依赖 Interop；应用由低层 ModuleHost 改为拥有高层 service。
C# 管理16-world预算、初始Paused/运行/单步、固定量子、有界速度暂存与取消、服务/world身份、dense body索引、分页复制观察、诊断/profile和集中关闭。
单批重复targets拒绝、多批最后值胜出；删除维护pending与索引，不把旧速度写入重建body。
成功Step/Read/counters后才发布sequence；不确定错误停止发布并Faulted，raw不可用时明确NativeAvailable=false。
32条诊断、512-byte UTF-8 message与丢弃数；native关闭失败保留对象和lease、closing拒绝新操作、可显式重试。
服务不实现 wall-clock accumulator、不保证多世界原子推进、不自动接入Play或Scene、不注册物理MCP。
原生保留求解、安全校验、primitive batch回收与fail-stop，这是插件自保，不是应用政策。

本次新增service专项：暂存原子性、取消/删除/重建、快照分页与不共享内存、profile、owner thread、16-world预算、诊断有界、白盒资源失效/关闭失败保留及32服务循环。
每维度0/64/1024/4096 bodies另测 stage+flush+Step+复制快照，持续设置0.1m/s速度；hot路径当前托管线程分配0，不含native/其他线程。
输出：out/verification/m2/physics-{Debug,Release}/physics-service-results.json。
本次日志：out/verification/m2-6-service-{debug,release}.log。本次最终结果：Build.bat Debug/Release 均 exit0，每配置8 native +20 managed/editor CTest（28项）全通过，managed/native smoke、Python inspect及24 unittest通过。Physics专项包含1.0兼容、1.1原始计数、服务政策/故障/关闭及性能测试；Editor services16/16通过。新增C++按/W4 /WX、托管warnings-as-errors，零新代码警告。本次增量Release日志无编译警告；首次重编旧测试的4条D9025保留历史说明，不声称已修复旧目标。
下面原先测试记录为1.0交付时的历史证据，性能数字与本次service范围不同，不混作同一基准。

## 首次独立插件验证（历史）

原生：版本/短表/null 输出/线程/维度/跨 world/重复/失效句柄、容量预检、真实 Jolt 容量耗尽中途创建失败回收、执行 fail-stop、busy shutdown、资源排空和 context generation。

托管：布局/无 World/Editor 依赖、模块关闭 lease、禁用/缺失/错误模块/缺依赖、参数/批量负例、2D/3D 并存重力+floor reference（h=1/60，180 步）、32 次 world reference 生命周期、32 次 module 初始化/关闭。

性能观察：每维度 0/64/1024/4096 个分离 dynamic boxes，4 次 warmup +32 次 Step/Read，记录创建耗时、往返 median/p95/max 和 solver 统计。复用缓冲路径 managed 当前线程分配为 0；不包含原生堆/其他线程分配，不等于整帧零分配或性能承诺。

- Debug：`out/verification/m2-6-debug.log`
- Release：`out/verification/m2-6-release.log`
- 各配置：`out/verification/m2/physics-{Debug,Release}/physics-results.json`
- 项目启用/禁用及缺失后的候选资源关闭由 Editor services 集成测试覆盖。

最终结果：Build.bat Debug/Release 均 exit 0；每种配置 8 项 native +20 项 managed/editor CTest（共 28），托管/原生 smoke、Python inspect 与24 项 Python unittest 全通过。Editor services 新增项目 Physics 配置集成用例，16/16 通过。新 Physics C++ 按 /W4 /WX 编译，托管 warnings-as-errors，零新代码警告/错误。Release 对旧 Architecture/ManagedHost 测试目标仍产生4 条已有 D9025（/DNDEBUG 被 /UNDEBUG 覆盖，保留 Release assert），不是全工程零警告。

Release 本机分离 boxes、零重力 fixture 的 4096-body Step+Read 往返 median：2D 0.6460ms，3D 0.9467ms；p95 0.9256/1.1739ms。无活跃碰撞压力保证，body 可进入 sleep；不代表动作游戏场景性能。

本次完整回归同时保持 DX11 reference 32 次资源循环及像素对比 max/mean error=0，业务 GUI validation=0；不替代图形/UI 人工验收。

## 后续边界

M2.5 仍为部分业务迁移，H3/H4/H5 人工验收未通过；本次独立 H6 不替代这些门禁，也不授权切换/删除旧入口。M2.7/M2.8 未完成。

Scene PhysicsSystem、运动权威、固定步跨域提交/回滚策略、碰撞事件、raycast、CharacterMotor、Root Motion 碰撞均留在 M4，未实现。
