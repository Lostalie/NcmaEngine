# M3 联合交付状态

更新：2026-10-06；M3.9 工作基线 `1bbb9ea033787d584f018ff41142428e1dac6894`。
这是证据与缺口汇总，不是完整 M3 验收通过证书。**M3 尚未结束，G6–G9 开放。**
用户已明确要求推进各候选阶段；这不等于豁免未实现功能或人工门禁。

## G1–G9

| 门禁 | 实际结论 | 证据与未覆盖范围 |
| --- | --- | --- |
| G1 资产身份/元数据 | 已关闭该底座门禁 | [M3.1](M3_1_DELIVERY_REPORT.md)：严格UUID/格式、共享历史、精确文件事务/恢复。不是完整资源导入 |
| G2 异步 FBX/generation | 已关闭该数值/持久发布门禁 | [M3.2](M3_2_DELIVERY_REPORT.md)：Worker、原始数据、取消/故障、代数与身份、来源租约/journal。不是任意FBX/DCC材质兼容 |
| G3 GPU资源 | 已关闭静态资源切片 | [M3.3-B/C/D](M3_3_BCD_DELIVERY_REPORT.md)：mesh/texture/material、固定图像/ABI、DX11验证。不是完整后端或材质节点 |
| G4 场景渲染 | 已关闭静态DX11切片 | [M3.4 GPU](M3_4_GPU_DELIVERY_REPORT.md)：相机/灯光、多对象、shadow/HDR、Editor/Player。Vulkan/IBL/多光阴影仍未实现 |
| G5 动画角色 | 已关闭最小片段/DX11蒙皮切片 | [M3.5 GPU](M3_5_GPU_DELIVERY_REPORT.md)：真实NCA、tick时钟、pose数值/GPU误差、主画面/阴影、资源生命周期。不是Animator、根运动/物理/战斗图 |
| G6 编辑工作流 | 开放 | [M3.6](M3_6_DELIVERY_REPORT.md)：本地导入/放置/唯一Undo、离屏视口/浏览相机已接线；缩略图、独立材质浏览未实现，真实输入/DPI/窗口人工项待验 |
| G7 Prefab/覆盖 | 开放 | [M3.7](M3_7_FOUNDATION_REPORT.md)：严格格式、只读提取/展开/放置预检；生产保存、membership/实例发布、覆盖/三方同步/恢复未实现 |
| G8 Agent资产闭环 | 开放 | [M3.8读取](M3_8_READONLY_REPORT.md)、[UI授权](M3_8_UI_AUTHORIZATION_REPORT.md)：精确metadata范围/60秒授权与撤权；异步导入/Prefab写任务/联合历史权限未实现，真实第三方可见修改闭环待验 |
| G9 Player包/联合验收 | 开放，新增运行包候选 | [M3.9](M3_9_RUNTIME_PACKAGE_REPORT.md)：已提交generation→NCP1→source-free Player；cold cook、正式导出/恢复、许可inventory、上游闭环与目标环境/完整性能/一小时人工项未完成 |

前五项“关闭”仅使用各自冻结范围的历史结论，不把后续 foundation stdout 或文件数量当新验收。
新的全量构建必须继续重跑其保留自动负例/参考/ABI，不更新旧图像期望来掩盖差异。

## 当前能做到的链路

现有受许可 ASCII/binary FBX fixture → 受控数值导入/持久 UUID/generation → 扁平场景
Transform/StaticMesh 或 SkinnedMesh/ClipPlayback/Camera/Light → Editor/独立 DX11 Player。
片段时间由 C# committed tick 控制，GPU 蒙皮与阴影共用变形结果；根位移仅报告，不写 World/Physics。
独立 PBR材质/纹理运行解析存在，未声称原 FBX 材质自动等价转换或完整材质GUI。

M3.9 新 adapter 把已验证的运行数据打成一个只读 typed 包；显式配置后，Player 在游戏回调及
GPU/pose初始化前验证所有块，不需要原始FBX、authoring metadata、ImportWorker、Editor/ImGui/MCP/Python。
未配置包的现役开发加载器并非废弃入口，仍用于 Editor/已存在项目；不是新旧格式兼容桥。
Prefab 尚无生产实例提交；普通已烘焙场景可打包，不提前支持动态模板。

## 联合验收缺口

1. 完成 G6 的缩略图/独立材质工作流与人工窗口清单。
2. 完成 G7 的严格文件发布、membership/OverrideSet、唯一项目事务、冲突/Undo/恢复。
3. 完成 G8 的逐任务权限/异步结果/撤权/receipt，以及 UI/第三方客户端共享修改历史。
4. 完成 G9 cold-source重建/target profile cache key、可信 cook recipe与导出入口、许可清单、
   专用包 stage/backup/journal/recovery 与所有约定故障点；current generation重复打包不代替这些。
5. 用户需提供真实游戏角色/片段/贴图许可及预期画面；锁定目标设备/driver/分辨率/预算，
   完成真实输入/DPI/MCP、自包含目标机、约定32样本性能和一小时工作流。

M2 人工/自包含/完整性能/长稳门禁不因自动 regression 或 audit_passed 关闭。
M4/M5/M6、完整2D管线、Vulkan绘制、网络/Python AI transport 不由本次候选自动变为实现。
数值插件/SDK/用户数据及未审查旧策略原型均保留，未新增旧类型别名或兼容格式。

## 本轮联合自动回归

最终冻结M3.9源码顺序完整Debug/Release `Build.bat`，每配置31次CTest、Python43项、
strict/removed格式、managed/native smoke、inspect、三轮保留profile及部署前后审计全通过。
最终日志、包位置、备份及32轮真实Player生命周期与lease证据见 [M3.9记录](M3_9_RUNTIME_PACKAGE_REPORT.md)。
这是保留范围的联合自动回归，不是上表G6–G9功能、人工/目标环境/生产性能验收；不宣布整个M3结束。
