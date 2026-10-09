# M6.8-C1 交付记录

状态：C1完整顺序无Skip Debug/Release自动候选通过，最终Release已核验部署。
提交/远端状态以Git回执为准；C2和整个M6.8-C尚未完成。

契约见 M6_8_C1_RUNTIME_CONTRACT.md。本切片覆盖 typed Slot/Section 属性、
源Clip时间/Notify轨道、共享闭合语义操作及获批MCP提案/完整差异；C2待实现。

## 定向证据

首轮 core111/111、Editor109/109通过（轨道可读性调整后需复测）。
`out/m6-8-c1-core-build-first.log`、`out/m6-8-c1-core-first.log`；
`out/m6-8-c1-editor-build-first.log`与third构建通过，second测试代码编译失败保留；
`out/m6-8-c1-editor-first.log`实际 stdio两重批准/replay/Undo、TTL/revoke/queued/re-pair、
实际NCA区间越界/代次变更拒绝、typedUI拖动/取消/修复、独立GPU预览/drain通过。
实际 ImGui截图保留在该次 editor-services `c8c9dd469c924f90aab33a8693da4c82`。
首轮新增编译错误仅缺少测试using及nullable注解；已修复，不弱化产品或断言。
分页复测 `out/m6-8-c1-editor-final.log` 失败：新测试直接修改服务草稿后遗漏正式宿主
SynchronizeGraph 调用，UI仍读旧复制数据；补齐同步后 `out/m6-8-c1-editor-repair.log`
109/109通过，未删除/放宽分页断言。随后截图检查将轨道下移避免标题重叠、
实际截图选中Section显示typed字段，并补Agent Slot/Section两项更新差异断言；完整回归将验证最终源码。

## 首轮完整回归与修复

Debug首轮无Skip完整通过：12native(3.06s)/22managed(267.38s)、Python43及后续核验。
post-deploy audit `Debug/c51eb42fed81426cb05dd30e0d5f88aa/audit.json`通过，
101大小/SHA256核对、journal Complete，备份 `out/deployment/93cb7fa6a828449f8c2825c97d7dae0f/backup`。
Release首轮12native通过，22managed中21通过、Rendering失败，完整日志
`out/m6-8-c1-full-release-first.log`保留；未执行部署，未提交。
失败为既有joint测试count8/cycle1/step4提交false，只有材质默认诊断，首轮未记录背压计数，
不能声称已证明该帧一定是背压。检查显示正式皮肤提交明确允许非阻塞Busy，Present不保证GPU完成；
同步逐量子绘制验收不能依赖主机/驱动偶然足够慢。
joint测试改为Submit前调用既有有界2s GPU诊断drain，新增背压计数与相同World tick断言，
原0/1/8/32、每步成功绘制/全部geometry/shadow、16搬移Player及关闭资源断言全部保留。
不改生产渲染/模拟、不添加生产等待或重试、不声称性能/长稳通过；双配置完整重跑待完成。

## 修复后最终完整证据

Release渲染定向 `out/m6-8-c1-render-repair-build.log`/`out/m6-8-c1-render-repair-first.log`
exit0；实际NCA、所有M6 joint Editor/Player、CPU-GPU/reference/GUI/生命周期/API0/0通过。
之后冻结源码，按顺序无Skip `Build.bat -Configuration Debug`、Release重跑：

- Debug：`out/m6-8-c1-full-debug-second.log` exit0，12native(3.10s)/22managed(267.44s)。
- Release：`out/m6-8-c1-full-release-second.log` exit0，12native(1.31s)/22managed(229.64s)。
- 两配置core111/Editor109/Player56/Scene36/Gameplay53/fakeMovement38/Python43，
  smokes、严格新格式/旧格式拒绝、inspect、三轮保留运行测量、源/消费者/包审计与checked deployment通过。
- 最终actual ImGui/Section属性/Notify轨道/独立Slot角色预览截图分别保留在
  editor-services `e04624ff200640ae913b9b09aa6f1d80`(Debug)、`6a1eae68d3f848b799f2b049535f81f4`(Release)。

审计位置（均audit_passed=true、h8_accepted=false）：

- Debug post-deploy：`out/verification/m2-8/Debug/0079460619a147fa9926ada0f26d5380/audit.json`。
- Release post-deploy：`out/verification/m2-8/Release/28a23a1795314176a2ff5efdebdba302/audit.json`。

Debug备份 `out/deployment/1e2fc754392e4ee6af0a21429d31d196/backup`；
Release备份 `out/deployment/b6fe18f3558f415492fe41226fbbbbfd/backup`。
两个部署分别核对101文件大小/SHA256，最终Release额外核验路径/无reparse祖先，
`out/deployment/editor-journal.json` Complete，generation `b6fe18f3558f415492fe41226fbbbbfd`。
正式 `out/bin/NcmaEngine.exe`已更新；不提交生成部署/备份/失败输出/无关IDE修改。
本段为最新结论，上文所有“待完成”仅记录修复之前的历史，不表示当前完整自动回归仍未完成。

## 开放门禁

完整自动回归和checked deployment已通过；提交后仍须核对远端SHA再推进C2。
C2 typed隔离Montage控制/用例及最终联合验收尚未完成，不标整个C或M6.8完成。
保留所有失败/输出/备份、SDK/素材/IDE修改；所有人工/用户FBX/目标/性能/1h门禁开放。
后续顺序 M6完成 → M7本版DX11（Vulkan/OpenGL下一版实际实现）→ M8 → M9。
