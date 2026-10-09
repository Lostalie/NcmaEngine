# M6.8-B2b-1 交付记录

基线 main `83d7da9328cf1839133d0e6d96aa8edd24fe94f7`。
实现跨段 authored blend envelope 均值与 partial-step fraction、内部 bounded root numerical
消费者和独立/实际 NCA 验证。范围见 [运行契约](M6_8_B2B1_RUNTIME_CONTRACT.md)。

## 定向验证

- 首次 target build 发现 Ncma.Vector3 与 System.Numerics.Vector3 名称歧义；显式 alias 修复。
  `out/m6-8-b2b1-target-build-first.log` 保留；second build0警告/0错误。
- 首轮动画核心103/103通过，`out/m6-8-b2b1-core-first.log`。
- 首轮真实渲染/独立 interval-root 数值/既有 NCA、Jolt、CPU-GPU、形式 Player 回归通过，
  `out/m6-8-b2b1-render-first.log`。这些既有图测试不是 Montage Movement/pose 通过证据。
- 后续追加16Slot528区间/末行失败保护、实际 packed NCA/同 Animator1-8-32区间 root oracle，
  `out/m6-8-b2b1-target-build-third.log`0警告/0错误及`out/m6-8-b2b1-render-second.log`通过。
  最终完整门禁包含动画核心104/104（7组新增 envelope 测试）。

## 完整自动门禁

完整 sequential no-Skip `Build.bat -Configuration Debug/Release`最终均退出0：

- Debug second `out/m6-8-b2b1-full-debug-second.log`：12native/22managed CTests，3.11s/248.62s。
- Release first `out/m6-8-b2b1-full-release-first.log`：12native/22managed CTests，1.34s/212.42s。
- 两配置 graph/pose104、Editor104、Player56、Scene36、Gameplay53、fakeMovement38、Python43；
  smokes/strict new-and-removed formats/inspect/3轮profiles/pre-post audit/checkeddeploy全部通过。
  新代码0编译警告；既有GPU/Jolt/形式Player通过不代表本切片接通Montage姿态/Movement/Player。
- 独立数值证据`out/verification/m2/render-{Debug,Release}/montage-root-recipe-results.json`：
  线性root与矩阵heading oracle、终止partial coverage、16Slot528区间、最后错误行的完整拒绝、
  owner-thread与1024 warmed evaluations allocation0。不是整个World/工具或性能预算验收。
- post audit Debug `out/verification/m2-8/Debug/14e24f8c16d449efb0703f42d67c0188/audit.json`，
  Release `out/verification/m2-8/Release/d483303078404369a34d6648edde204f/audit.json`；
  `audit_passed=true`，`h8_accepted=false`，所有既有pending gates保持。
- 最终Release `out/bin`101文件逐项SHA256/size及路径边界核对；journal `Complete`。
  Debug备份`out/deployment/770e61da95a84035aa47ae5606163334/backup`，
  Release备份`out/deployment/0a85bb9127ed459b98e34faf6f011b70/backup`，均保留可恢复。

按授权提交推送并核对远端main后才能开始B2b-2，远端SHA以提交后的实际核对为准。
所有首次失败、输出及恢复备份保留；4个无关 .vs/.user修改不纳入提交。

## 保留的全量首次失败

Debug first `out/m6-8-b2b1-full-debug-first.log`：新增动画104及真实NCA/数值组、Editor104通过，
但既有SceneDocument完整保存用例File.Replace报告IOException“无法删除要被替换的文件”。
该失败没有部署或提交。生产SceneDocumentFiles没有改动，没有降级成非原子替换、添加重试、
取消锁定拒绝或跳过测试。独立场景首次复测36/36及5次重复36/36通过，日志分别为
`out/m6-8-b2b1-scene-retest-first.log`与`out/m6-8-b2b1-scene-repeat-{1..5}.log`。
重新完整Debug及Release全通过；首次具体干扰原因未确认，不将复测通过冒称已修复其根因。
原失败out/tests UUID目录/文件和日志保留，不覆盖或删除首次证据。

本切片不是完整 B2b：graph仍strictv4，Montage pose/root Movement/Notify/persisted package、
作者/MCP与正式 Player自动 Montage加载仍待后续。人工/真实用户素材/目标/性能/1h不关闭。
