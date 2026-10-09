# M6.8-B2a 交付记录

基线 main `b2f148ddfee615cd916d2d0bb95f28e305e84b4c`。
实现同一 graph instance/tick/attempt/token 所有者下的合作 Montage 事务、真实 NCA 资源准备和
可信 stopped-host 绑定，边界见 [运行契约](M6_8_B2A_RUNTIME_CONTRACT.md)。

定向第一轮核心97/97通过，新代码编译0警告；真实 NCA/共享 Animator/1-8-32对象、完整跨段区间、
搬移包 NCA、Reload、caught tick control/binding、4类 fault 与 Stop 恢复已通过定向测试中的新增组。
增加预检失败后同 attempt sequence 的保护，已纳入最终完整双配置回归。

## 最终完整自动门禁通过

顺序无Skip `Build.bat -Configuration Debug/Release` 退出0：

- Debug second `out/m6-8-b2a-full-debug-second.log`：12native/22managed CTests，3.02s/247.23s。
- Release first `out/m6-8-b2a-full-release-first.log`：12native/22managed CTests，1.41s/210.10s。
- graph/pose97、Editor104、Player56、Gameplay53、fakeMovement38、Python43；真实NCA联合组、
  protectedUserDACL/Owner/真实stdio、smokes/strictformats/inspect/3轮profiles/pre-post audit/checkeddeploy。
  新代码编译0警告；不将fakeMovement、程序化NCA或共享shader当作用户素材/性能/人工验收。
- post audit Debug `out/verification/m2-8/Debug/209e3117fdbc4b0ca17281092b93977e/audit.json`，
  Release `out/verification/m2-8/Release/4ca876cafb9949f69bb2d7b160c72d87/audit.json`。
  `audit_passed=true`，`h8_accepted=false`；所有既有pending gates保持。
- Release `out/bin`101文件SHA256/size逐项核对，journal `Complete`。
  Debug备份 `out/deployment/e69e61f6a0ae477e9f28446ed47aab9d/backup`，
  Release备份 `out/deployment/f3bd88770d0d47fe87233b3b3e62a194/backup`；都可恢复且保留。

按授权提交推送并核对main后才能开始下一切片；远端SHA以提交后核对结果为准。

## 保留的首次失败与修复

Debug first失败保留：EditorService/EditorTransport在受控高完整性令牌下被.NET CurrentUserOnly
owner检查拒绝；Montage97及实际NCA组通过。官方.NET8 client比较WindowsIdentity.Owner而非User：
[.NET源代码](https://raw.githubusercontent.com/dotnet/runtime/v8.0.0/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeClientStream.Windows.cs)。
原引擎SDDL固定owner=User，elevated token的Owner=Administrators时不匹配。修正owner为当前token
Owner，**protected DACL仍仅显式允许实际User SID**，local-only/first-instance/配对/TTL/撤销/权限不变。
新增真实client连接后独立ACL/owner断言，不移除CurrentUserOnly或扩大DACL，也不声称管理员运行是安全sandbox。
未验证跨不同完整性令牌连接；两端不匹配继续拒绝。上述最终完整双配置已覆盖修复。
所有 out 日志、失败与恢复备份、SDK/用户数据和4个无关 IDE 改动保留且不提交。

B2a不完成 Montage 姿态/root/Notify/持久化/作者/MCP/正式 Player加载；后续 B2b/C 继续待实现。
M6.9/M6.10与人工/真实素材/目标/性能/1h仍开放；M7(DX11-only)→M8→M9须等待前置阶段。
