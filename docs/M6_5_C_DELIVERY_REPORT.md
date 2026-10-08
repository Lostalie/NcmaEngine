# M6.5-C 自动候选交付记录

2026-10-08，执行M6.5-C。完整顺序Debug/Release回归通过，不以定向测试推断通过。
D联合验收尚未完成，所有人工/素材/目标/性能/1小时门禁仍开放。

实现严格 `.ncmaanim` v2（v1拒绝）、持久事件/中断策略、shared semantic edits、
事件轨道草稿/属性、复制预览debug和同一UUID标识、可信实际NCA独立序列服务，
以及默认拒绝的 sequence.propose/run 和精确用例/资源/受众 GUI 审批。
边界/预算/未实现项见 M6_5_C_RUNTIME_CONTRACT.md。

定向证据：graph/pose/clock57通过；actual NCA v2 policy 自动驱动128重复中断，
target-only持久事件、GPU/root-strip/Jolt/0-8-32 Headless、独立preview/fault/reload通过。
Editor新增用例覆盖事件草稿拖动/取消/保存、序列UI完整分页审阅、真实stdio、TTL/re-pair/失效/预算，
Editor88/88通过；实际GUI事件轨道/GPU角色合成已生成并检查，人工可见验收未通过。

## 完整回归（通过）

- 无Skip的Build.bat Debug重跑out/m6-5-c-full-debug-retry.log退出0：12 native/22 managed CTests，3.66s/206.07s。
- 顺序Release out/m6-5-c-full-release-first.log退出0：12 native/22 managed CTests，1.62s/182.93s。
- 两配置graph/pose57、Editor88、Player56、Gameplay53、fakeMovement38、Python43，
  managed/native smokes、严格新/旧格式拒绝、inspect、三轮profiles和pre/post audit通过；新代码0编译警告。
- 实际NCA128中断/持久目标事件/独立cache与GPU/root-strip oracle最大误差4.053393709568809E-08；
  真实provider暖1024次0分配、预览World tick0，不是完整性能验收。
- post audits：Debug out/verification/m2-8/Debug/6d751d0bd4e5429b88a4bc38bf5d903b/audit.json；
  Release out/verification/m2-8/Release/9f37f7281c1f4d72b20723b2a0078ccd/audit.json。
  audit_passed=true，h8_accepted=false，人工门禁不关闭。
- Release out/bin/NcmaEngine.exe更新，101安装文件SHA256再次核对；editor-journal.json Complete。
  Debug旧安装备份out/deployment/c6cd0f06979543c3968b543b3121ca03/backup；
  Release旧安装备份out/deployment/b7eec61ad6384c619c99537cb6a37579/backup。

保留全部首次失败：valid_graph_v1断言、Assets命名空间、Guid.Value/GuiItem.Flags编译错误，
以及UI测试未选新元素/prepare发生于切区前导致stale/选择器匹配标签等日志。
首次完整Debug的格式测试误用canonical排序后的Nodes[0]当作Clip，导致随机UUID顺序触发metadata拒绝；
修为按Clip类型查找，不放宽真实同代资源校验。保留out/m6-5-c-full-debug-first.log，完整双配置须重跑。
修复正确类型、选中新元素及用例准备时机，不放宽生产权限或旧格式拒绝。
不提交out、部署备份和四项IDE设置；双配置通过后按授权提交推送并验证远端SHA，再开始D。
