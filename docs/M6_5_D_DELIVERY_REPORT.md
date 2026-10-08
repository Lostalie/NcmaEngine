# M6.5-D 联合自动候选交付

2026-10-08，C完整双配置通过并提交推送后，远端main核对为
`b293393af5dcdab9244c5c068161c21bc593c283`，开始D。
完整顺序D Debug/Release回归通过。M6.5 A–D自动候选完成，不把自动通过当成人工/生产/性能验收。

## 联合覆盖

- 实际同代NCA、严格v2持久事件/策略：Editor 0/1/8/32 actors，各2个Play生命周期、8个量子。
  自动闭合条件反复中断，各实例缓存generation和事件/graph/Character movement使用相同已提交tick。
  真正DX11姿态/skin-shadow路径，独立instance UUID、唯一Jolt；Edit bytes/identity/tick保持不变。
  空场景不创建3D pipeline/pose；32actor故障关闭保留资源所有权，重试后全部回到基线。
- 0/1/8/32 × root off/on × Headless/DX11，共16组正式Player；DX11用独立子进程，
  每组目标8ticks（交互帧允许越过目标，精确Headless为8），validation0/0、无shutdown错误。
  NCP1搬移包仅带assets/game.ncpak，无FBX/.ncmaanim来源或import cache，
  实际重新准备并核对图v2完整bytes/hash/事件/策略；无physics Headless加载0原生模块，
  有physics Headless只加载physics，不加载GPU/platform/pose。
  正式Player自动条件是合法图数据，没有测试后门的live Animator控制入口。
- 真实stdio sequence请求先停留在bounded endpoint queue；撤销后Pump在owner线程复核，
  拒绝运行，不改World/tick。独立root opt-out拒绝root断言、failed断言显式返回失败，
  取消/退休256IDs上限实测。可信本机typed case与MCP JSON共享48KiB检查。
  GUI结果按状态/事件/完整断言分别每页8行，不只显示前8个断言。
- 保留A/B/C事件边界/Abort/trigger/32实例/零分配、128实际姿态连续性oracle、
  unique Jolt root stripping、reload/fail-stop和精确scope/TTL/re-pair/Undo/Redo自动测试。

## 定向结果

out/m6-5-d-render-tests-third.log通过；实际证据
out/verification/m2/render-Debug/m6-5-d-joint-results.json（Editor8cycles/16formalPlayer cases）。
Editor新增排队/根选项/退休IDs/48KiB本机用例通过，91/91。

## 完整回归（通过）

- 无Skip的Build.bat顺序Debug out/m6-5-d-full-debug-first.log退出0，
  12 native/22 managed CTests全部通过，分别3.46s/216.41s。
- Release out/m6-5-d-full-release-first.log退出0，12 native/22 managed CTests全部通过，1.47s/193.45s。
- 两配置graph/pose/clock57、Editor91、Player56、Gameplay53、fakeMovement38、Python43；
  实际FBX/NCA/skin-shadow、128中断独立cache/GPU/root-strip oracle、unique Jolt、8个joint Editor周期、
  16组正式搬移Player均重跑通过；所有API validation0/0。新代码编译0警告。
  图/姿态暖1024次数值0分配等是有界观测，不替代完整性能验收。
- managed/native smokes、strict格式与已移除v1拒绝、python inspect、三轮runtime profiles、pre/post audit通过。
  Debug post audit out/verification/m2-8/Debug/217cad17f2634215b9d5276b7c4aefc7/audit.json；
  Release out/verification/m2-8/Release/5cd7e19b95424b49b2d7d11e4f0e7f48/audit.json。
  audit_passed=true而h8_accepted=false，原开放门禁不因本阶段自动关闭。
- Release out/bin/NcmaEngine.exe受控更新，101文件SHA256再次核对，editor-journal.json Complete。
  Debug旧安装备份out/deployment/846e6d7b72774303b992004669f91502/backup；
  Release旧安装备份out/deployment/288956ac6eb94a4abdc4bc1f0a7e8744/backup。
- 通过后按用户逐阶段授权提交推送并核对远端main；不纳入生成安装/备份/IDE文件。

保留首次D编译错误（私有package Decode/缺少Services using）及测试失败日志；
修正空场景应不提交3D和失败清理顺序，正式Player前释放测试Editor独立physics所有者，
没有放宽native单owner约束、World authority、旧格式或approval checks。

## 开放门禁

人工可见窗口/第三方MCP、真实用户材质与FBX、DPI/IME、self-contained目标机器、完整性能和1h长稳仍开放。
独立AI sequence是有界数值分析，不是推理服务、live Play控制、碰撞模拟、Notify回调或伤害执行。
Vulkan绘制、BlendSpace/Montage/IK/重定向等后续阶段不由D测试宣布完成。
保留所有失败日志/checked部署备份/用户数据与IDE设置；双配置通过后提交推送本切片并核对远端。
