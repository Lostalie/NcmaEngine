# M6.10 交付记录

基线 `097840073a9874ae4d9842727da38ab0c2c6a3cb`；范围见[方案](M6_10_IMPLEMENTATION_PLAN.md)、[契约](M6_10_RUNTIME_CONTRACT.md)。
当前：最终完整顺序无Skip Debug(second)/Release(first)通过，M6.10自动候选闭环。
提交推送／远端核对后进入M7；不宣称M6正式全面／人工／目标机器验收完成。

## 实现

- 精确不可变图publication的一项编译缓存，所有命中仍先校验lease/owner/图对象；不持有额外pin或实例。
  全包/catalog preflight继续验证全部图但不保留未使用图的编译缓存；仅实际消费者惰性准备并复用。
- NCP1规范图v5 +实际NCA组合链：BlendSpace/CachePose/骨骼mask Layer/Blend/Montage/Notify/root/唯一Jolt/skin-shadow。
- 包只规范化节点X/Y=0，保留全部语义；runtime hash/generation与作者布局独立，不重写作者文件。
- 12种损坏/重算合法hash但语义非法包，各在Inspector及正式Player Headless/DX11初始化前拒绝；
  保存全部原件/变体，恢复合法包，不改用户/安装资产、不降级作者catalog。
- 1024次同publication程序ReferenceEquals复用allocation0；跨线程/foreign/disposed拒绝，嵌套数据复制不共享。
- 1角色30/60/144/fixed的全部120个成功量子TRS/recipe/root/Slot/Notify trace hash一致；
  0/1/8/32合成图有界8warm/16成本样本和全角色geometry/shadow绘制。
- 同一service/kernel/renderer/cache的128循环，每次8tick/实际绘制、32Reload、32post-solver fail-stop；
  保持启动文档、转身份、失效快照、关闭到原基线及最终包pin释放。
- 既有Editor0/1/8/32和16搬移Player保留；新增真实独立生产apphost Headless/DX11及hashmanifest验证。
- 成本只测实际simulation及draw区间，断言拼接在simulation计时外；GPU drain单列测试专用。
  GPU last-valid没有sample-frame ID，不宣称逐帧归属、独立样本、目标性能或吞吐通过。

## 保留失败／修复

- `out/m6-10-render-build-first.log` 新测试一处nullable错误，修复显式已验证非空引用；后续build0警告。
- `out/m6-10-render-first.log` 首轮通过（尚未包含最终补充轨迹/成本/apphost）；不作为最终全门禁。
- `out/m6-10-render-second.log` 零角色测试仍带地面collider导致正确创建numerical domain；
  修改空负载夹具不放physics binding，没有放宽生产资源断言。其128cycle/每ticktrace已通过但不是全通过。
- `out/m6-10-render-third.log` 真实Headless apphost通过、DX11依赖失败；夹具copy过滤漏掉
  PlayerPresentation.Start必要的NcmaEngine.ico，补齐独立包。保留失败包/报告，不削弱SetIcon/验证。

下面保留早期定向结果，最终完整门禁见末节；提交/远端核对以实际Git回执为准。
`out/m6-10-render-fourth.log` 完整真实render定向通过，含真实独立apphost Headless/DX11、
128cycles/11negative/22reject/完整trace/0-1-8-32cost/原有全部GPU与FBX oracle；不是完整Build.bat替代。
人工/用户FBX/第三方可见授权/目标环境/自包含/性能预算/1h仍false，不代表M6正式全面验收。

`out/m6-10-full-debug-first.log`首次完整Debug通过（12native/22managed、smokes/formats/inspect/
Python43/3profiles/audits/checkeddeploy），备份`f54b438e86084a8eba4cbc13023804c7`保留。
随后审查发现尚未消除运行包中的作者布局；补齐坐标规范化、布局独立字节及合法hash非零坐标拒绝。
因此首次Debug不是最终源码证明；必须重新完整Debug和Release，不提前提交。

## 最终完整双配置（源码冻结后顺序、无Skip）

- `out/m6-10-full-debug-second.log` 退出0：12native/22managed CTest，2.92s/316.84s。
- `out/m6-10-full-release-first.log` 退出0：12native/22managed CTest，1.32s/282.97s。
- 两配置pose/clock115/115、Editor123/123、Player56、Scene36/36、Editor.Core36/36、Gameplay53/53、
  fakeMovement38/38、Assets38/38、Import25/25、部署恢复8/8、Python43；smokes/strict新格式与
  旧格式拒绝/inspect/3轮retained profiles/pre-post audits/checkeddeploy全部通过，新C#代码0编译警告。
- 最终Debug组合报告：`out/verification/m2/render-Debug/m6-10/bc16b5575b2e41049fa85632a553c1b4/acceptance.json`；
  Release：`out/verification/m2/render-Release/m6-10/994330565e0a4608b3d80e9d11d88097/acceptance.json`。
  各128cycle/1024draw/32Reload/32postsolver fail-stop/12negative/24prestart Player reject，
  1角色四种调度全部120成功量子的TRS/root/Slot/Notify trace相同；不跨随机图UUID比较两配置hash。
  0/1/8/32各8warm/16成本样本；真实搬移apphost Headless/DX11，API0/0、关闭和hashmanifest通过。
  图运行字节及generation与作者布局独立，带非零坐标的重算合法hash包仍严格拒绝。
- 两配置`out/verification/m2/render-{Debug,Release}/m6-10-joint-results.json`：组合BlendSpace/cache/
  Layer/Montage为true；Editor0/1/8/32两次8tick及16搬移Player通过。此组合图没有状态机中断，
  不冒称有；独立中断/override/additive/真实FBX CPU-GPU oracle专项原样保留并通过。
- 6项源码SHA256在最终Debug/Release间未变化。仅后续交付文档更新，不改受测源码。
- post审计Debug `out/verification/m2-8/Debug/81cd6e898820480fbf32c581cf5a100e/audit.json`，
  Release `out/verification/m2-8/Release/229090bf5f0740dc9eb642ff2a839e3c/audit.json`：
  audit_passed=true、h8_accepted=false；所有既有pending gate保留。
- 最终Release `out/bin/deployment-manifest.json`101文件路径/普通文件/size/SHA256逐项一致，
  `out/deployment/editor-journal.json` Complete，generation `696f0abad197454992d4e9cdca74e6ad`。
  最终Debug backup `out/deployment/46cc61248a5e43b8a0f61689cf2e00f8/backup`；
  Release backup `out/deployment/696f0abad197454992d4e9cdca74e6ad/backup`均保留。
  Release apphost `out/bin/NcmaEngine.exe` SHA256 `125E8A29BD4630907DEDFCA8BDA1F4CE722ECF6F1DC0A968F069804F95F5F0FE`。

M6.1–M6.10自动候选链结束；仍非M6正式全面验收。manual/userMaterial/target/budget/longRun及
deployment manualAcceptance/selfContainedVerified均false。没有推理服务／liveAgent／Python gameplay／新nativeABI。
保留所有失败/备份/SDK/userdata及4个无关IDE更改；仅提交源码和文档，远端SHA成功核对后进入DX11-only M7。
