# M3.2 持久 FBX 导入交付记录

日期：2026-10-05。实现已补齐，最终 Debug/Release 全量验证通过，G2 关闭。

交付顺序：完成 M3.2 → Debug/Release 测试通过/G2 关闭 → 提交推送 M3.1 + M3.2 → 验证远端身份 → 开始 M3.3。
本记录不把旧 Worker 局部测试当作当前持久发布的完成证据。

## 实现边界

- C++ NcmaImportKernel 仅做 ufbx/Eigen 数值导入，不链接 World/GUI/Physics/Renderer，不拥有持久 UUID 或历史。Import ABI 1.1 additive 增加静态模式；1.0 的 96-byte 表仍可查询，1.1 为 104 bytes。严格角色与静态模式互斥，静态来源有蒙皮则明确拒绝。
- C# Worker 独立进程保持 native owner affinity，闭合 64KiB IPC，仅产出 NIM1 原始候选。source、job、project generation、asset revision、SHA-256、设置与 Ready 状态均须一致；来源保持防写/防删除/nofollow/hardlink 拒绝租约至发布确认。
- ImportCoordinator 后台解析/hash/IPC/解码，一个活动 Worker、五个任务、四个 Ready，缓存 64 个 staging/1GiB。120s/1536MiB watchdog、500ms 合作退出窗口，仅结束自己创建且验证身份的进程。它不是恶意代码沙箱或 OS 内存硬限。
- ModelImportPlanner 在 C# 分配根/子资产 UUID，以类型、限定名称、完整数值结构/内容 hash 与 rig 证据匹配，绝不只按名称/数组索引。原生工具拒绝重名来源，重复匹配拒绝；改名、拓扑、rig/采样证据变化需要确认新身份，旧 UUID 保留 tombstone，不自动重定向引用。
- 型别化 NCA1 root manifest / MSH1 / SKL1 / CLP1 / MAT1 各 v1，显式 LE 编码与 UUID 引用，不序列化 C ABI struct、GPU/native handle。冷读只需描述和派生文件，不需要 FBX。静态变换/逆转置法线只烘焙一次，RH/+Y/metres、column-major、UV top-left；切线为确定 triangle-UV v1，镜像符号与退化回退有诊断，不称 MikkTSpace/DCC 等价。
- 不可变 generation 在后台验证、写入、flush、no-overwrite 发布，再由 ncma.assets.import.commit 经唯一 EditSession 安装有界 metadata memento。元数据 journal 是可见性边界；故障补偿或重启恢复旧描述，durable committed 决策之后保持新版本。提前发布的孤立 generation 可重建且只在 ignored out/assets 下。
- 提交、Undo/Redo、重放均重新检查权限/项目/版本/旧文件内容，不能覆盖外部改写；Play 冻结编辑。历史/幂等缓存版本保守锁定整个 session，启动后台预备既有 generation；正式 Editor StartPlay 取得独立版本租约，不同步读取大模型或等待准备。
- generation 缓存跨重启受 128 文件/1GiB 保留预算约束（未知/孤立文件也计入）；不自动删除。GC 仅接收已批准、精确项目相对路径，完整候选集合先校验/hash/持有文件租约，再逐项删除；当前 catalog/history/Play 或其他 owner 的 pin 拒绝回收。GC 不是磁盘故障时的多文件数据库事务。
- 正式 Editor 组装 Authoring/ImportCoordinator，默认只读、来源默认未授权；不从 Agent 请求接受任意工具代码路径。Worker 和独立内核部署在 tools/import-worker，全部文件随 manifest hash 校验；启动期间依赖保持租约。两个 Player 包不含 Worker/导入内核。项目关闭非阻塞撤销，Completion 在所有背景任务及项目锁释放后完成。

## 验证

最终 canonical 双配置回归均 exit 0，日志为：

- out/verification/m3-2/Debug-closure.log
- out/verification/m3-2/Release-recheck.log

使用 Build.bat -Configuration Debug 后顺序执行 Release，不使用 Skip flags。

| 项目 | Debug | Release |
| --- | --- | --- |
| native CTest / 其余 CTest | 10/10 + 18/18 | 10/10 + 18/18 |
| 资产专项 / 导入专项 | 36/36 + 24/24 | 36/36 + 24/24 |
| Editor services / 部署专项 | 40/40 + 8/8 | 40/40 + 8/8 |
| managed/native smoke | 通过 | 通过 |
| Python inspect / unittest | 通过 / 42/42 | 通过 / 42/42 |
| C# 编译警告/错误 | 0/0 | 0/0 |

首次 Release-closure.log 曾在旧 Editor.Core 场景保存测试出现 Windows File.Replace 失败；
未修改原子保存机制或强删文件。三个独立新测试目录复核均 36/36，完整 Release 重跑通过。
原因尚不能确定，保留初次失败日志，不隐去此异常。日志中 Git 全局 ignore 读取权限提示不是编译警告。
最终 out/bin/NcmaEngine.exe 为通过回归后恢复式部署的 Release C# apphost；生成包记录提交前源码 revision/dirty 状态。
内容布局另见 [模型派生格式 v1](M3_2_ASSET_FORMAT.md)。

测试覆盖 frozen ASCII/binary 的完整 raw streams/NIM1 与 typed NCA round trip、采样终点；cm/Y-up/非对称非均匀变换、m/Z-up/多实例、实际 Base/Spin/Wiggle 多 take、片段/网格重排；五权重截断与无权重回退；重名/改名/拓扑/rig/采样冲突；实际项目重启再导入保持 UUID、FBX 缺失后的冷 Play；正式 Editor 版本 pin、Undo/Redo/权限撤销/取消/外部改写；队列/候选/retained 压力、GC pin/未知内容/完整集合预检；八个生成/发布故障点及六个不做进程内补偿的 journal 重启恢复点；解析/转换/采样 phase 取消、IPC/crash/watchdog、关闭迟到；实际部署工具启动/依赖篡改/Player 隔离；挂起 Worker 时 owner pump 和独立动画预览可继续。

五份 frozen fixture 的来源/许可/SHA-256 见 tests/assets/fbx/README.md：两份 upstream MIT 角色，三份 Ncma CC0 数值模型；没有提交用户素材。采样重排/冲突负例另用 ignored 测试副本，冻结参考不重写。

## 明确限制与后续阶段

- G2 是受控模型数值/身份/持久发布门禁，不是任意 FBX 兼容、所有 DCC 材质或用户真实角色验收。反射/负缩放、dual quaternion、多 skin deformer、实例化蒙皮等仍明确拒绝。
- 当前只导入材质槽/名称，不读取外部或内嵌纹理，不做 FBX→PBR 等价转换。独立受许可纹理解码/材质覆盖为 M3.3；完整资源绘制/GPU lease 由 G3/G5 验收，当前没有新 mesh GPU 渲染完成声明。
- 导入面板和完整资产 MCP 工具为 M3.6。已有稳定 import.commit capability/精确 host-owned grants，不表示第三方 MCP/UI 人工验收已通过。任意映射手工重定向没有实现；当前支持的冲突决策是显式批准新 UUID + tombstone。
- Scene/Prefab 消费者尚未实现（G4/G7），不能报告不存在的引用迁移。变更计划列出子资产 UUID/type/reason；后续引用诊断必须使用这些 UUID，不按同名替换。
- session 保守 pin/缓存满即拒绝，需要关闭 session 或显式审查 GC；尚未按历史游标精细退休。owner 安装 metadata 最多 4MiB/有界 journal，不承诺硬 2ms。真实项目吞吐、最大素材和长稳尚未验收。
- M2 audit 即使 audit_passed=true，仍 h8_accepted=false；人工 UI/MCP、自包含目标环境、性能/长稳等旧门禁保持待验。
