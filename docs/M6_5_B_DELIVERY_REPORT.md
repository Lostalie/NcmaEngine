# M6.5-B 自动候选交付记录

2026-10-08，按用户“执行M6.5”推进B。A基础已随M6.3-C/D双配置通过。
完整Debug/Release自动候选通过，不能视为M6.5完成、C/D完成或人工验收。

## 实现

单实例双缓冲Frozen local TRS、Prepare/Commit/Abort、严格generation、target重入/有序中断，
实际NCA lease数值provider及同一个native pose/skin-shadow/唯一Jolt接线。
默认关闭、无v1格式扩展/ABI变化、新Agent控制或推理传输。
冻结源根区间为零；语义和预算见M6_5_B_RUNTIME_CONTRACT.md。

## 定向证据

- out/m6-5-b-animation-fourth.log：53/53，8个新增B项，固定预算、缓存完整验证/Abort、
  陈旧/跨实例/owner、端点、优先级/trigger/目标事件与暖1024量子0分配。
- out/m6-5-b-render-first.log：实际NCA/native DX11 skin-shadow/Jolt、128重复中断，
  独立完整缓存/加权顶点/root strip oracle、0/8/32 Headless、失败候选和Reload关闭通过。
- out/m6-5-b-render-second.log增补真实provider零分配/整批拒绝/独立预览通过，随最终完整回归重跑。

## 完整回归（通过）

顺序Build.bat Debug、Release，无Skip，均以0退出：

- out/m6-5-b-full-debug-first.log：12 native/22 managed CTests，分别3.51s/203.06s。
- out/m6-5-b-full-release-first.log：12 native/22 managed CTests，分别1.63s/180.70s。
- 两配置graph/pose/clock53、Editor82、Player56、Gameplay53、fakeMovement38、Python43；
  managed/native smokes、strict格式/旧格式拒绝、inspect、三轮profiles与pre/post audit通过。
  新代码编译0警告；Git全局ignore读取权限告警不属于编译警告。
- 两配置actual NCA cache/native GPU skin/root-strip最大独立误差4.053393709568809E-08。
  真实数值provider暖1024次中断0分配，独立preview World tick=0；这是有界测试观测，非完整性能验收。
- Debug post-audit：out/verification/m2-8/Debug/7b4baef912894f8fa2dff7f2065e77fe/audit.json。
  Release post-audit：out/verification/m2-8/Release/094c938b28b14e72a3564d21b0cedd97/audit.json。
  audit_passed=true，h8_accepted=false，不关闭原人工/目标/性能/长稳门禁。
- Release out/bin/NcmaEngine.exe部署完成，101个安装文件SHA256再次核对，editor-journal.json为Complete。
  Debug旧安装备份out/deployment/bf2b441574064f1c87d78a1e015deea5/backup；
  Release旧安装备份out/deployment/3152001db1c2432d91d75883224c705c/backup。

按用户授权提交推送本切片并核对远端main；实际SHA以Git回执为准。不包含out/IDE更改。
状态文档更新不改变已测执行源码。

## 保留证据与未验收

保留首次编译Vector3/uint索引错误和测试fixture条件/priority错误日志，修复未放宽生产校验。
保留out/m6-5-b-build-first.log、render-build2、animation-first/third等，不删除部署备份/用户数据。
持久事件/策略、事件轨道UI、获批sequence MCP是C，联合验收D尚未完成。
用户素材、可见第三方MCP、DPI/IME、目标环境性能与1小时长稳门禁开放。
