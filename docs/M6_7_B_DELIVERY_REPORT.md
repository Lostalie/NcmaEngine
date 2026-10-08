# M6.7 B 分层图与缓存交付记录

A已推送并核对main 6cfa2d47b54a500664a0593dab178980d924a23e。
B候选：严格v4、真实NCA遮罩/参考准备、Layer配方、CachePose同候选别名及生命期scratch、
基础层事件/root、共享CPU/native/GPU路径和正式Host启动协商；不提前称C的typed作者/AI清单完成。
具体边界见M6_7_B_RUNTIME_CONTRACT.md。

定向core81/81通过；真实渲染的第三/第五轮日志通过512quanta override/additive×root off/on，
CPU全局pose与独立GPU顶点oracle、cache源只采样一次、基础层事件/唯一Jolt、Reload和NCP1严格v4。
第五轮另覆盖0/1/8/32 Editor两cycles和16个搬移正式Headless/DX11 Player、32关闭失败保留。
保留first/second计数失败：发现纯graph已有多余bind-pose prepass，修复实际路径而非放宽断言；
sixth编译用错World删除API改为现役GameObject.Destroy，未新增旧API兼容。
第六轮 `out/m6-7-b-layer-tests-sixth.log` 退出0，新增实际mask hash mismatch与failed candidate通过，
GPU顶点独立oracle最大误差约1.40e-8，API校验0/0。
编辑器second `out/m6-7-b-editor-tests-second.log` 98/98；保留first schema失败，响应投影补齐SourceC而非放宽schema。
## 完整自动门禁通过

顺序完整无Skip Build.bat，两配置退出0：

- `out/m6-7-b-full-debug-first.log`：12native/22managed CTests，3.49s/266.74s。
- `out/m6-7-b-full-release-first.log`：12native/22managed CTests，1.50s/225.71s。
- graph/pose81、Editor98、Player56、Gameplay53、fakeMovement38、Python43；
  smokes/strict formats/inspect/三轮profiles/pre-post审计/checkeddeploy全部通过，新代码0编译警告。
- 每配置 `out/verification/m2/render-<configuration>/m6-7-b-layer-results.json`：
  4种override/additive与root off/on各128quanta，真实NCA/CPU-GPU对照，顶点最大误差1.40e-8，
  maskhash mismatch在数值启动前拒绝，failed candidate/explicit Stop/Reload/NCP1通过。
  `m6-7-b-joint-results.json`：graphVersion4/layers/cache=true；Editor0/1/8/32各2×8tick，
  所有geometry/shadow计数验证，关闭失败保留；16个搬移正式Headless/DX11 Player API0/0。
- Debug post audit `out/verification/m2-8/Debug/dcaffb39ab1f4df7957d580f95ecba0f/audit.json`；
  Release `out/verification/m2-8/Release/776dd389515d4188866bcc34068af07b/audit.json`，
  audit_passed=true，h8_accepted=false。
- Release101文件SHA256/size逐个核对，journal phase=Complete。
  Debug备份 `out/deployment/efbe39d2b2e24f509f11538f3d45a344/backup`；
  Release备份 `out/deployment/0ffefbab51714152b90ab76e69a3209d/backup`。

B自动候选通过，按授权提交推送并核对远端后进入C。typed layer创建/骨清单工具尚未开放，
完整node JSON已走原精确审阅事务；不提前标整个M6.7或正式性能验收完成。

人工/用户素材/目标/性能/1h门禁保持开放；所有失败日志/部署备份/SDK/无关IDE数据保留。
