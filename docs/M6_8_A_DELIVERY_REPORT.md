# M6.8 A 动作编排数据交付记录

2026-10-09，M6.7远端0eebe79a2f6b619247a11b5dba5bbd402373b4a9已核对。
A候选为纯C#严格Montage v1数据和复制准备元数据：UUID/slot/section/clip区间、后继、priority/
interruptible/root/blend窗口；16slots/64sections/256KiB，完整可达、同slot后继、循环允许但运行有界。
AnimationMontageProgram核对same-skeleton/generation、完整Clip metadata和actual duration；不是NCA pin或执行。
当前graph仍v4，原生表/正式Player行为不变，没有空Slot节点、工具、文件写服务或包支持声明。

定向 `out/m6-8-a-core-tests-first.log` 86/86，新增3组codec/structure/preparation测试；
构建零编译警告。

## 完整自动门禁通过

顺序无Skip Build.bat Debug/Release退出0：

- `out/m6-8-a-full-debug-first.log` 12native/22managed CTests，3.46s/275.35s。
- `out/m6-8-a-full-release-first.log` 12native/22managed CTests，1.52s/234.88s。
- graph/pose86、Editor104、Player56、Gameplay53、fakeMovement38、Python43；
  smokes/strictformats/inspect/三轮profiles/pre-post audit/checkeddeploy通过，新代码0编译警告。
- post audit Debug `out/verification/m2-8/Debug/2f14af40204045809d6c457a5f7c3741/audit.json`；
  Release `out/verification/m2-8/Release/0cd8b8920ded4baf9f442053eac9bb69/audit.json`。
  audit_passed=true，h8_accepted=false。
- Release101文件SHA256/size核对，journal phase=Complete。
  Debug备份 `out/deployment/a1e9926a171944ac8c7a4f51de71c5f1/backup`；
  Release备份 `out/deployment/ed09dd1bc21e486989dcd5ff4fa963a6/backup`。

A自动候选通过，按授权提交推送并核对远端后进入B。没有将纯数据准备标为运行、图/作者工具或包接线完成。
人工/用户素材/目标/性能/1h仍开放，不能用metadata校验宣称Montage运行已完成。
