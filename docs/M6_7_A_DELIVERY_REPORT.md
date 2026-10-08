# M6.7 A 遮罩和分层数值交付

候选实现AnimationBoneMaskProgram与独立layer1.0，C#负责精确身份/复制权重，C++仅数值。
现有图v3及正式runtime行为保持，B/C尚未实现。详细数值/owner/限额在M6_7_IMPLEMENTATION_PLAN.md。

定向Build.bat Debug -SkipManaged -SkipPython：12native通过，新kernel成功；Skip不部署。
定向dotnet build零警告，`out/m6-7-a-pose-tests-first.log` 77/77通过（新增4项）。
原生覆盖独立layer版本/尺寸、reserved/ref/mask/alias/stale/thread、输出/成功计数原子拒绝；
托管覆盖骨架hash/fullpath/root、override/additive独立oracle/antipodal、owner/foreign、32×1024及暖分配0。
这些结果不称graph层/cache/GPU/作者或Agent工具已实现。

## 完整自动门禁通过

顺序无Skip Build.bat Debug/Release退出0：

- `out/m6-7-a-full-debug-first.log` 12native/22managed，3.54s/255.75s。
- `out/m6-7-a-full-release-first.log` 12native/22managed，1.50s/213.94s。
- 两配置graph/pose77、Editor98、Player56、Gameplay53、fakeMovement38、Python43，
  smokes/formats/inspect/三轮profiles/pre-post审计/checkeddeploy通过，新代码0编译警告。
- post audit Debug `out/verification/m2-8/Debug/e074bf9f98fb4aacbf919917d547dc5f/audit.json`；
  Release `out/verification/m2-8/Release/636fc164421e4805a7bc28c582b3be44/audit.json`。
  audit_passed=true，h8_accepted=false。
- Release安装101文件SHA256/size逐个核对，journal phase=Complete。
  Debug备份 `out/deployment/5ed69daacf1b4b8c9b1802e2c21910ba/backup`；
  Release备份 `out/deployment/dd756e07e59940b4949552f64e548631/backup`。

A自动候选通过，按授权提交推送并核对远端后进入B；A不提前标整个M6.7完成。

保留输出/备份/SDK/用户数据及四个无关IDE设置，人工/素材/目标/性能/1h验收保持开放。
