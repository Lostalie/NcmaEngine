# M4.2 薄 native 角色碰撞插件

日期：2026-10-06。基线：M4.1 `53f496eb1c5ead186343b7b723ca51130549c318`。
状态：实际Jolt数值专项、最终顺序完整Debug/Release Build.bat全部通过，K2退出门通过；独立提交推送。

## 实现边界

`NcmaPhysics` 内新增真正的 Jolt `CharacterVirtual` capsule，而非用 Box 的速度移动冒充角色。
Physics module 1.2 追加 `query_characters`，独立 Character API 1.0；旧 1.0/1.1 表大小、能力位、
操作顺序与无角色世界语义冻结。C# `PhysicsModuleHost(characterSupport:true)` 显式协商1.2；
默认宿主仍协商1.1，physicsEnabled仍false，Editor/Player没有创建角色或接入本次数值 quantum。
Runtime/Gameplay 不依赖 Physics。`Ncma.Physics` 仍只依赖 Interop，没有 GameObject/World/Editor。

C++负责数值形状、查询、Jolt执行、安全校验和复制结果；C#决定重力/跳跃/地面运动策略、
调度与最终Transform。没有新的通用native World、玩法回调、网络或AI服务。

## 契约与限制

- 现有同DLL唯一Jolt factory/job pool继续共享。最多16数值world、4096普通body/world、
  32角色/world；内核另预留32个kinematic sensor query proxy，不减少既有4096预算。
  这些是拒绝阈值，不是性能保证。角色仅capsule、普通碰撞体仅box；没有mesh/terrain接口。
- Y-up、米/秒；capsule位置为脚底原点，总高 `2*(radius+halfHeight)`，旋转xyzw且角色仅yaw。
  创建给单位旋转；数值输入可指定yaw。盒体支持单位四元数旋转。角色velocity由调用方提供，
  Jolt ExtendedUpdate的gravity用于body力，不替调用方累计角色重力。阶高、floor距离、坡角、
  padding、质量/推力只是数值参数，脚本的jump/gravity/state-machine留给M4.3。
- 32位category必须单bit，mask可以0；角色对body按mask过滤，对另一角色双向过滤。
  query proxy为sensor，角色数值步骤跳过它，避免额外未过滤碰撞；ray/sweep仍能命中角色。
  普通body之间仍用原Jolt layer对，不把category当成全功能rigid-body collision-matrix。
- 一个quantum先推进普通bodies一次，再按opaque character handle升序推进完整角色集合；
  缺项、重复、逆序、foreign/stale、错误sequence、非finite、非法quat、容量不足先拒绝。
  顺序CharacterVirtual允许互相推挤/滑动；不是同时crowd solver，也不承诺跨CPU位级确定性。
- copied状态/contacts都带sequence；ground支持body与virtual character身份映射。
  contacts含predictive/discarded/sensor数值标记，按角色、other-kind/handle/subshape排序，
  不是Begin/End gameplay事件，不把native pointer或Jolt的潜在陈旧character指针暴露给C#。
- 每角色64 contacts，调用方预留 `count*64` 输出。跨完整移动/stair/floor查询还保守限制64个
  distinct accepted candidates；可能在实际最终contacts较少时也拒绝密集几何。
  不单靠Jolt最后一次collector的overflow标记，因为后续查询会覆盖该标记。
  candidate/hit预算溢出发生在不可逆执行域，fail-stop且不截断/发布部分结果。
- ray/sweep是单次closest-hit（body或character），精确sequence、mask和可选ignore句柄；
  非零direction，sweep为capsule。不是all-hit攻击范围工具、batch-query或游戏命中系统。
- 创建body拓扑在有角色时冻结，角色创建在首次step后关闭；只允许整体销毁角色集合，
  不允许部分删除造成contact/support悬空。关闭角色→body/world→module；失败保留资源/lease、
  禁止卸载且可显式重试。全部owner-thread，不做同步AI/IPC调用。
- 所有ABI POD布局由C/C++/C#测试核对；调用方拥有有界有效输入/输出且不得重叠。
  无STL、异常、托管对象或借用数组跨ABI。C#缓存delegates并复用调用方缓冲。

## 验证与修复记录

新增 `NcmaCharacterNativeTests`（独立CTest）与 `Ncma.Physics.Tests` 的真实native角色专项：

- 地面/grounding/contacts、墙体滑动、顶棚、可走/过陡旋转坡面、台阶开启/关闭、浅初始穿透。
- 1000m/s输入对薄墙不穿透、moving dynamic obstacle及普通bodies每quantum只推进一次。
- 角色对角色的分离/contact/固定顺序同机复测、双向mask、站在另一角色上的support身份。
- closest body/character ray与capsule sweep、mask0/ignore/foreign、布局/短表/版本/线程。
- 完整输入/容量/sequence预检、body当character拒绝、拓扑冻结、32角色上限。
- 80个真实重叠box触发保守candidate/真实hit预算fail-stop，旧sequence及调用方输出保持，
  后续read/query/step拒绝；这不是fake注入。
- 测试专用内核资源耗尽使第二角色创建失败，第一proxy回收且输出不部分写；
  真正执行后注入映射失败证明solver已推进却未发布sequence，没有声称rollback。
- 测试专用关闭故障验证保留所有权/禁止unload，恢复映射后显式关闭；32次native生命周期。
- C#真实Jolt墙/地面/ray/sweep、旧API显式拒绝角色、关闭guard、overflow、线程及32次module/world
  周期；warmup后复用step/read/query缓冲的32采样段当前线程GC分配为0，**不是native零分配或性能验收**。

首轮编译发现测试局部变量遮蔽警告，按/WX修复；测试Fixture大数组造成Windows默认栈溢出，
改为有界heap vector。角色对角色原断言错误地要求静止目标，已改为数值实际的分离/contact及
重复性，不伪造“不可推挤”能力。站立角色测试补真实floor，避免下方角色本身可下移。
首轮完整Debug原生12项通过，托管Physics项因测试未处理关闭guard的AggregateException而失败；
保留失败日志，修正异常断言后专项通过，新增密集预算等案例后重新完整回归。

日志均在 `out/verification/m4-2`（ignored）：编译/专项失败尝试保留；`Debug-attempt1.log`为失败
旧版本，不能当最终证据。`Debug-final.log` / `Release-final.log` 是最终无Skip完整构建记录。
最终两套完整Build.bat均exit0：每配置12项原生+20项托管/应用CTest，Python43项+inspect，
严格新格式/旧格式拒绝、managed/native smoke、3轮保留runtime测量、受检整包部署及前后审计。
新代码无编译警告/错误；Git全局ignore读取权限/行尾提示不是代码警告，未修改全局配置。
详细托管CTest保存在 `Debug-final-ctest.log` / `Release-final-ctest.log`，包括真实K2、
既有Gameplay53+Movement35；同版native专项另保存在 `Debug-final-native.log` / `Release-final-native.log`。
Debug部署备份 `out/deployment/7f333e5952a44906a1d9f2ca1d9ccb03/backup`；Release备份
`out/deployment/2efe19ba1a1a4a03b60db83ebd084df3/backup`，正式out/bin为最后Release整包。
部署后审计：Debug `out/verification/m2-8/Debug/50d7264f655e418d82ad0f34859e0729/audit.json`；
Release `out/verification/m2-8/Release/00ab66b88e5949afbe0087f40deefdac/audit.json`，audit_passed=true、
h8_accepted=false。没有强制删除旧证据、备份或用户文件，没有包含无关.vs/.user修改。

## 仍未实现

M4.3 场景配置/Play与角色碰撞接线、gravity/jump/input/camera；M4.4碰撞根运动；M4.5动作战斗；
M4.6正式调试/MCP/sample；M4.7真实角色素材/目标机/性能/长稳。此切片不是可玩CharacterMotor。
人工UI/MCP、自包含环境及M2/M3未闭合门仍开放，不能用本次自动数值测试关闭整个M4。
