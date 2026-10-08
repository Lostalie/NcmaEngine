# M6.6-A 纯数值基础候选

2026-10-08，详细方案见M6_6_IMPLEMENTATION_PLAN.md。A完整顺序Debug/Release自动候选通过。
当前图格式仍严格v2；不改图执行/资源ABI、NCP1/NCA、Player/GUI入口或已有授权。

准备期复制BlendSpaceDefinition，有限typed轴/范围/单位标签、2–32(1D)/3–32(2D)采样点、
位置标准化度量、sample/axis/space UUID及clip引用、明确cycleSeconds/syncGroup数据。
1D排序线段；2D确定性UUID插入Bowyer-Watson/有向三角形/凸包边，近退化拒绝；
共圆包含规则选择稳定对角线。采样只读预编译拓扑，不在tick三角化/读文件或分配。
域外先clamp轴，再最近凸包边投影；相等距离≤1e-10按稳定UUID边排序，
主贡献weight相差≤1e-10为UUID平局规则。输出最多3个正归一权重与明确投影位置/标记。

定向out/m6-6-a-tests-first.log：64/64（7个新增分组），涵盖441方形网格点、
24组最大32采样点随机拓扑/4800查询独立仿射重构、内部/共线边界样本、复制隔离、
非法UUID/有限值/重复位置/退化/上限、16384暖查询0托管分配。
成本证据out/verification/m6-6/weights-Debug.json，非完整性能验收。

## 完整回归（通过）

- 无Skip的Build.bat Debug out/m6-6-a-full-debug-first.log退出0：12 native/22 managed CTests，3.38s/215.26s。
- 顺序Release out/m6-6-a-full-release-first.log退出0：12 native/22 managed CTests，1.41s/193.20s。
- 两配置graph/pose64、Editor91、Player56、Gameplay53、fakeMovement38、Python43；
  actual FBX/NCA/DX11皮肤阴影/unique Jolt、M6.5反复中断/16正式Player、smokes/formats/inspect/
  三轮profiles/pre-post audits/checked deploy通过。新代码0编译警告。
- 16384暖查询0分配；本轮32点查询Debug49.0397ms、Release41.4896ms（总时间，不是单次时间/性能验收）。
- post audits：Debug out/verification/m2-8/Debug/d6718b5d918747ae9d7215ee1fc30f34/audit.json；
  Release out/verification/m2-8/Release/e23e452c1934421d8ab7dc37a068aba6/audit.json。
  audit_passed=true，h8_accepted=false，所有原人工门禁保持开放。
- Release out/bin安装101文件SHA256再次核对、journal Complete。
  Debug旧安装备份out/deployment/c798a26962484048a30a88862bda8a5e/backup，
  Release旧安装备份out/deployment/e06b3cc3e53642ee913235b4be23373a/backup。
按授权提交推送A并核对远端，再进入B；生成输出/备份/IDE设置不纳入提交。

A不准备实际clip/skeleton/generation、不采pose/root、不发事件、不拥有时钟/World/native资源，
没有图BlendSpace节点/编辑器/Agent扫描控制或推理服务；这些属于B/C/D。
不可用typed DTO/cycle/group字段或几何权重宣称实际资源绑定/同步已经实现。
后续B严格图v3不兼容旧v2输入，但拒绝时保留文件，不自动迁移/删除用户数据。
人工/真实FBX素材/目标/完整性能/1小时长稳门禁仍开放；测试通过后提交推送再进入B。
