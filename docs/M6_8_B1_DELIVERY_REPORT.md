# M6.8 B1 动作编排状态基础交付记录

A基线main ffce3cd77ee1c873dfc041a95be125dc52cb81ff。
B1实现同一外部成功fixed-step context下的合作状态，见M6_8_B1_RUNTIME_CONTRACT.md，
不是新scheduler/World/native资源，B2正式Animator/NCA/GPU仍待接通。

定向 `out/m6-8-b1-core-tests-second.log` 92/92，新代码0编译警告。
保留first微小循环测试失败，用补偿累计修复真实区间计算，不放宽32边界；
无区间的Cancel输出允许空buffer，容量拒绝测试改为确有区间的Play候选。
近期ID cache有界但不造成256次play lifetime上限；1024长期请求/warm候选验证通过。

本切片同步用户2026-10-09路线图方向：完成M6→M7（本版本只DX11，Vulkan/OpenGL留扩展、下一版本实现）
→M8→M9；不提前开始M7/M8/M9代码，不关闭既有人机/素材/目标/性能/1h门禁。
## 完整自动门禁通过

顺序无Skip Build.bat Debug/Release退出0：

- `out/m6-8-b1-full-debug-first.log` 12native/22managed CTests，3.43s/273.85s。
- `out/m6-8-b1-full-release-first.log` 12native/22managed CTests，1.45s/234.11s。
- graph/pose92、Editor104、Player56、Gameplay53、fakeMovement38、Python43；
  smokes/strictformats/inspect/三轮profiles/pre-post audit/checkeddeploy通过，新代码0编译警告。
- post audit Debug `out/verification/m2-8/Debug/5d0fddd0da8c45c4871016ba848aceb0/audit.json`；
  Release `out/verification/m2-8/Release/5b45820c9671493db78531031ee4c966/audit.json`。
  audit_passed=true，h8_accepted=false。
- Release101文件SHA256/size核对，journal phase=Complete。
  Debug备份 `out/deployment/53b5b141fd7f4b7ca93108818d117649/backup`；
  Release备份 `out/deployment/4cfb179e033e4109b23df480068037b0/backup`。

B1自动候选通过，按授权提交推送并核对远端后进入B2。不要把B1合作状态基础称为完整B、
正式Slot/图/真实NCA/GPU/MCP/Player Montage接线完成，所有未完成阶段和验收门禁继续保留。
