# M6.6-D 联合验证记录

2026-10-08，C已推送并核对main `29ef3acd3b2b648b1d7c2f431b6fa9138b126a6f`。
本阶段复用同一正式ScenePlayRuntime与既有联合测试，不新增运行入口、原生ABI或宽松验证。

候选验证范围：实际不等duration空间在30/60/144frame与Headless固定步的120ticks相位/唯一根运动一致，
safe-boundary参数、Reload identity/时钟重置；caught tick control与post-solver失败不发布部分World，显式Stop关闭。
真实空间混合中断暖1024次分配0；0/1/8/32 Editor各两次8tick，事件/cache/Jolt/skin-shadow、Edit不变、关闭失败保留；
root off/on ×0/1/8/32 ×Headless/DX11的16个搬移源文件无依赖正式Player，严格v3包内容相同和API验证0/0。
保留B的CPU/TRS/GPU独立oracle及C的获批真实stdio作者/权重扫描测试。

定向 `out/m6-6-d-render-tests-first.log` / `out/m6-6-d-render-tests-final.log` 退出0，保留首轮编译缺using/nullable修正记录。
补强联合断言为所有角色实际geometry/shadow draw计数，使用可覆盖全部角色的网格位置和40高正交相机；
图证据以schema2的当前graphVersion记录，避免严格v3的保留测试仍标旧v2。
首轮完整Debug `out/m6-6-d-full-debug-first.log` 12native/22managed通过（3.56s/236.69s），
随后核对发现Vector3默认JSON只写空对象，修正证据为明确x/y/z（原数值断言已执行通过）。
## 最终完整自动门禁通过

最终源码顺序完整Build.bat（无Skip），两配置均退出0：

- `out/m6-6-d-full-debug-final.log`：12native/22managed CTests，3.29s/237.27s。
- `out/m6-6-d-full-release-final.log`：12native/22managed CTests，1.49s/214.72s。
- graph/pose73、Editor98、Player56、Gameplay53、fakeMovement38、Python43，
  smokes/严格格式拒绝/inspect/三轮profiles/pre-post审计/checkeddeploy通过；新代码0编译警告。
- 每配置 `out/verification/m2/render-<configuration>/m6-6-d-joint-results.json`：
  graphVersion=3、blendSpace=true；Editor0/1/8/32各2cycles×8ticks，所有角色实际geometry/shadow计数验证，
  32角色关闭失败后完整保留并可显式重试关闭；16个搬移正式Headless/DX11 Player通过，API校验0/0。
  Headless恰好8ticks，DX11按frame累计到至少8ticks并实际绘制，不伪称所有模式恰好同tick。
- `m6-6-d-schedules.json`：模拟30/60/144帧调度和Headless各120fixed ticks，phase约2.0，
  x=1.999999、z=0；不等duration相位与主贡献root一致，safe controls/Reload/pre-post solver fail-stop通过。
  这是host frame调度一致性，不是测得GPU FPS；没有solver rollback。
- `blendspace-scene-results.json` schema2：GPU顶点独立oracle最大误差5.98e-8、真实NCA空间混合
  暖中断1024次分配0、128次混合缓存/Abort与完整输出拒绝通过；不是整个World/工具零分配保证。
- post audit Debug `out/verification/m2-8/Debug/65b62217ff0b49df9b8bdb8dc7151549/audit.json`；
  Release `out/verification/m2-8/Release/9994f63dd12e49bca8e51f4c5b56974e/audit.json`。
  audit_passed=true，h8_accepted=false，人工门禁未改。
- Release部署101文件SHA256/size逐个核对，journal phase=Complete，入口 `out/bin/NcmaEngine.exe`。
  Debug恢复备份 `out/deployment/423c0ebc2e0d4e5d87debf1549466e0b/backup`；
  Release恢复备份 `out/deployment/d3753f0e03e04104a150dee38eeae1fa/backup`。

M6.6 A–D自动候选完成，按授权提交推送D并核对远端；保留所有first/final日志与备份。
C源码已推送29ef3ac，D不新增作者/Agent权限、格式兼容或原生ABI；唯一场景/history/solver边界不变。
下一阶段M6.7分层/蒙版/姿态缓存未实现，本交付不提前开始。
仅自动候选；人工UI/可见第三方MCP、用户FBX、目标环境、完整性能/Vulkan/1h门禁保持开放。
