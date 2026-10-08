# M6.3-C/D 交付记录

日期：2026-10-08。用户授权补充 C/D，完成后测试，失败修复，测试通过提交推送。
前置 M6.4 main：5714a52c3a0e295f802909cc5b0e63d196f267c0。

## 实施范围

- 同一图候选 token 的根配方与唯一 Character/Jolt 运动量子接线；允许正确组合的 Animator +
  RootMotion，冻结成员/绑定，保留垂直策略、失败停机和精确资源 pins。
- 真实图/GPU 根剥离独立 oracle、8周期及32角色、Editor关闭重试、source-free NCP正式共享
  PlayerRunner Headless/独立进程DX11。
- ncma.animgraph.runtime 独立默认拒绝、精确完整资源/Play/受众审阅60s批准；本机共享观察界面，
  当前pose stamp、故障空观察、过期/撤销/重配对/Reload拒绝，无live控制或推理服务。
- 保留开始 M6.5-A 时实现的纯事件/debug/隔离sequence基础和10项测试；本轮使用其复制debug，
  不关闭 M6.5-B/C/D，不注册事件/序列Agent工具或改变 .ncmaanim v1。

详细边界：[C/D运行契约](M6_3_CD_RUNTIME_CONTRACT.md)、[C方案](M6_3_C_IMPLEMENTATION_PLAN.md)。

## 测试与修复现场

C 定向最终 out/m6-3-c-render12.log 通过；D 定向 out/m6-3-d-editor-third.log 82/82通过。
最终完整顺序 Debug/Release 均以0退出（无Skip），自动候选门禁通过：

- out/m6-3-cd-full-debug-retry.log：12 native/22 managed CTests，CTest203.85s。
- out/m6-3-cd-full-release.log：12 native/22 managed CTests，CTest180.75s。
- 两配置 graph/pose/clock45（含10项保留M6.5-A基础）、Editor82、Player56、Gameplay53、fakeMovement38、
  Python43；托管/原生smokes、严格新格式/旧格式拒绝、inspect、三轮profiles全部通过。新代码零编译警告。
- 两配置实际 pinned NCA/Jolt/GPU graph root联合测试通过，独立加权顶点最大误差
  3.332000986233652E-08；0/1/8/32角色、33拒绝、8直接GPU周期、8正式Editor周期、关闭失败重试及
  source-free NCP Headless/独立进程DX11 Player均通过。参考素材不等于用户素材验收。
- D 真实stdio/精确审阅/UI/受众epoch/撤销/期限/Reload/Faulted及当前成功GPU pose/旧pose拒绝均通过；
  各配置 out/verification/m2/editor-services/<run>/m6-3-d-runtime-mcp-results.json记录证据，人工门禁false。
- Debug post-audit out/verification/m2-8/Debug/ac91e8c4bcf04935aa0be8e3f827113e/audit.json；
  Release post-audit out/verification/m2-8/Release/4bffeb88d908423bac53466d03c2e4cb/audit.json。
  audit_passed=true，h8_accepted=false，所有原人工/性能/长稳门禁仍开放。
- 最终Release已部署 out/bin/NcmaEngine.exe，101安装文件哈希通过，editor-journal.json为Complete；
  Debug旧安装备份 out/deployment/462f772fbcef485090545e13e2272564/backup，
  Release旧安装备份 out/deployment/36411cd976b84f269f6b679e5b849a35/backup。无强制解锁/删除。

按用户授权提交推送C/D测试后源码检查点并核对远端main；实际SHA以Git回执为准。
提交不包含安装/测试生成物/备份及 unrelated .vs/.user。交付文档状态更新不改变已测试执行源码。
首轮完整 out/m6-3-cd-full-debug-first.log 因新增负例使用了不存在的CastShadows字段失败，修正为
正式CastShadow后完整重跑；保留首轮日志，不降低冻结写入测试的要求。
保留首轮编译/测试及 out/m6-3-c-render2..11.log、out/m6-3-d-editor-first.log 等失败日志。

已修复两处测试 oracle/输入设置：浮点累积接近循环终点时使用真实 committed 图区间而非有理
tick时间跨边界；首次 focus 帧按现有输入契约不接收 transient press，先建立 focus 后提交跳跃。
严格位移/姿态阈值未放宽。旧结构 composition 对 RootMotion 的重复约束已同步图组合。
DX11 Player 夹具改为独立进程，遵守原生每进程单上下文，不改生产插件限制。
闭合schema测试器修复其负整数只按unsigned读取的误判，生产schema未放宽。

## 尚未验收

用户真实FBX/素材、可见第三方MCP、DPI/IME、目标环境/性能预算和1小时长稳均待验收。
Animator + Action仍拒绝；量子内公开typed参数提案队列未开放，可信参数控制只在安全边界。
中断/持久事件轨道/Agent序列/BlendSpace/层混合/Montage/推理服务/Python通信未由本轮交付。
不删除旧验证基线、SDK、用户数据、失败日志或部署备份，不提交 unrelated .vs/.user。
