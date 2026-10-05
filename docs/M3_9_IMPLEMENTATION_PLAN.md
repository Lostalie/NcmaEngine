# M3.9 Player 资产包与联合验收方案

日期：2026-10-05。状态：未实现。依赖 G8。目标是 M3 资产闭环能离开 Editor 运行并留下可追溯证据。
这里只提供最小资产 cook/包；完整发布器、所有目标机/性能优化与双 API 验收仍属于后续阶段。

## 1 资产包与依赖闭包

从 startup scene、显式资源引用、materialSet/textures、mesh/skeleton/clips、必要 Prefab 和
项目显式 dynamicAssetRoots 计算依赖闭包；不猜测 C# 私有字段/string 字面量中的动态资产需求。
动态资源需作者登记根，不自动打包所有文件；丢失依赖/循环不支持项/身份冲突阻止 cook。
native/runtime/editor 句柄不参与身份，UUID→kind/hash/块位置索引可校验、加载并给出版本错误。

包包含 typed 数据块、材质、纹理、manifest、C# gameplay 与实际所需原生数值/renderer插件。
不携带 raw FBX、ufbx ImportWorker/ImportKernel、authoring数据库/源码绝对路径、Editor/ImGui/MCP或Python。
如启用数值动画则携带独立 AnimationKernel；Null/禁用动画不因此强制加载图形或骨骼模块。
纯2D未来配置不带3D shadow/skin/PBR默认资源；此处保持包边界，不声称2D管线已实现。

## 2 实施切片

### A 可重建 cook

忽略缓存为空时由许可来源+settings+版本重新构建，不能依赖开发机旧 out/ 或解析后的进程句柄。
派生 cache key 包含 source/dependencies/settings/importer/data-format/target-profile hash。
同输入产生相同排序/UUID引用/块hash，排除临时job UUID/时间戳；外部工具不确定项明确记录而非假称bitwise deterministic。
cache 清理不得回收活跃租约/history/未恢复 journal generation，不递归清空目录。
素材许可和可分发 fixture 清单随包保存；用户私有角色不默认提交仓库或外发。

### B Editor 与 Player 共享资产运行

RuntimeAssetProvider 为只读接口，Application.Runtime 组合可信 bootstrap、Gameplay 与资源依赖。
Editor 使用 authoring+runtime adapters，Player 不通过 activateEditor=false 偷带 editor引用。
启动先校验包/组件/资产/骨架/脚本/图形依赖，再运行游戏 lifecycle；失败逆序释放。
源码程序集预检仍不是安全沙箱，不能声称构造函数/外部 IO 可回滚。
图形 Player 使用 G4/G5 场景管线，真正显示角色/材质/阴影；Null仅运行显式支持的逻辑服务。
M3不承诺runtime Prefab spawn已完成，未实现时仅cook已烘焙场景；需要动态模板必须明确列入额外交付并验收。

### C 部署与故障恢复

沿用 M2 完整manifest/hash、stage/backup/journal/explicit recovery和不强制解锁原则。
M3包manifest增加实际资产格式/版本/依赖和插件服务版本，仅在实现交付时更新 schema/能力状态。
未知用户文件/reparse/锁拒绝，启动不静默回退reference或另一个后端；Vulkan仍明确未实现。
重复/中断部署、缺/损坏数据块、wrong plugin/asset version、GPU/device故障和退出顺序分别记录。
framework-dependent开发包与self-contained目标环境验收分开；未实现/未实测的模式继续待验。

### D 联合验证与交付

冻结至少一个可分发的多mesh/多clip角色和一组材质/纹理/非对称场景，保留当前ASCII/二进制fixture。
另需用户真实游戏角色、动作片段/贴图许可、预期坐标/材质画面；没有输入就明确真实素材验收待完成。
出报告记录全部G1–G8证据、偏差、实际版本/环境、已实现和未实现，不从文档数量推断通过。
不顺带清理剩余M2策略原型或恢复旧桥；若核定废弃临时M3实现，精确列单且复测。

## 3 自动与人工矩阵

| 范围 | 必须验证 |
| --- | --- |
| 资产与来源 | 导入→保存→重启→冷缓存重建；UUID/子UUID匹配、依赖、冲突、许可、损坏格式 |
| 场景与模板 | Static/SkinnedMesh/Camera/Light round-trip、扁平Prefab/override、Undo/Redo、缺资源和句柄失效 |
| 角色数值 | CPU/ufbx与CPU/GPU误差、绑定/终点/loop、多mesh、法线/主pass与shadow一致 |
| GPU | 固定M3图像、D3D11验证实际0 errors/0 warnings、resize/close/device状态、资源计数 |
| 任务与权限 | 所有取消/故障点、journal恢复、过期/撤权、重试/迟到任务、来源改写、精确目标 |
| Player包 | 无FBX/Editor/Gui/MCP/Python依赖运行，缺块/错误hash/版本拒绝；已烘焙真实角色场景 |
| 性能 | 0/1/8/32角色及0/256/4096 relevant对象；simulation/提取/采样/ABI/upload/GPU/GC/native live |
| 人工 | 真机窗口/DPI/输入/导入/视口/Play、第三方MCP闭环、实际目标机Player与一小时工作流 |

计数是建议 workload，不是性能合格证明。锁定硬件/driver/分辨率/数据hash/validation/vsync、
预热和32样本，给median/p95/max/字节/分配，独立记录历史WorldRunner瓶颈。
同一组fixture比较优化前后；先采集预算再经用户审查锁定，不用未经测量FPS目标放宽正确性。
连续32轮创建/Play/Stop/候选失败检查Live资源恢复基线；一小时人工工作流仍单独记录。

## 4 退出门禁 G9

新增测试/包审计进入canonical Build.bat，Debug→Release顺序全量，无Skip；警告视缺陷。
保留M2冻结参考/API/ABI/Physics/Runtime/Core/Gameplay/Python负例，不为新图像刷新旧期望。
建议产出 docs/M3_DELIVERY_REPORT.md、G1–G9逐项记录及 out/verification/m3/<configuration>/<run>/ 证据。
这些是未来交付，当前不创建伪造的“通过”报告。

只有资产/场景/角色/Player闭环、故障/权限/恢复、实际图形和约定人工项都有证据才能关闭M3。
自动通过而真实素材/目标环境/长稳待验时，状态写“自动切片完成，人工待验”，不偷偷关闭M2旧门禁。
交给M4的接口包括真实rig/clip采样、committed tick时间、资源租约和只读根运动结果；
实际碰撞/根运动权限/战斗Notify与状态图由M4/M5设计，不在M3制造多写入者。
