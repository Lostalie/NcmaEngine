# M6.6-B 交付记录

2026-10-08，基线 A 已提交推送并核对 main `8e252c997b5d11f9f3727f0c873da2b7118cdc41`。
当前 B 完整顺序 Debug/Release 自动门禁通过；C/D仍未完成。

## 候选内容

- 严格 graph v3/必填 nullable space 字段、删除 v2 接受路径，closed schema 与128 clips/16spaces/929rows预算同步。
- typed Float axes、显式 cycle/同 context 同步组、实际同代NCA时长和共享相位；最多3样本既有数值配方。
- 主贡献 Clip 事件/根运动，完整混合姿态 root strip，现有缓存中断与唯一 Jolt/skin-shadow 接线。
- 原生 ABI、十个 NCA 标签、NCP1 route 不变；正式搬移 source-free Headless Player 与既有权限回归。
- 没有新审批/live Agent控制、AI推理、Python gameplay；C/D 尚未完成。见运行契约。

## 定向证据与修复

Animation 71/71，Editor91基线回归；真实渲染定向第五/第六轮通过。
保留 `out/m6-6-b-render-tests-first.log` 旧770上限测试失败、second 读取未提交Reload快照失败、
third/fourth 旧 World 呈现观察器未退休的重载失败。修正测试生命周期，不放松 runtime 身份验证。
第五轮验证2D/不同duration/128quanta × root off/on、主事件、GPU独立TRS oracle和共享shadow、
0/1/8/32 Headless/Reload资源基线；第六轮加入128实际NCA空间中断/Abort/全姿态冻结与拒绝不复制输出。
最后新增正式搬移NCP1/Headless8ticks将由完整构建确认；保留全部日志和部署备份。

## 完整门禁（通过）

完整 Build.bat Debug → Release（无 Skip）退出0：

- `out/m6-6-b-full-debug-first.log`，12native/22managed CTests，3.72s/221.68s。
- `out/m6-6-b-full-release-first.log`，12native/22managed CTests，1.52s/201.24s。
- 两配置 graph/pose71、Editor91、Player56、Gameplay53、fakeMovement38、Python43；新代码0编译警告。
  smokes/格式拒绝/inspect/三轮profiles/pre-post审计/checkeddeploy全部通过。
- 搬移NCP1/正式Headless8ticks/0native成功；真实空间128次中断与Abort缓存通过；
  `out/verification/m2/render-Release/blendspace-scene-results.json` GPU最大顶点误差约5.98e-8，API校验0/0。
- post audit：Debug `out/verification/m2-8/Debug/1ce84fc3925e44e5aff827ef05b01cf6/audit.json`；
  Release `out/verification/m2-8/Release/063bc2723e0f45e69925f6fecfd5fd27/audit.json`。
  audit_passed=true，h8_accepted=false。
- Release安装101个文件SHA256/size再次全部核对，`out/deployment/editor-journal.json` phase=Complete。
  旧安装Debug备份 `out/deployment/ae1ef2bc772d4066a5a65c948465a718/backup`；
  Release备份 `out/deployment/40b26c6bc6a84382bab905ae875bcd48/backup`。

按授权提交推送B和核对远端 SHA，再进入 C；忽略生成输出/部署/备份，保留四个无关IDE改动。
定向零分配/程序化 NCA/独立 affine oracle不是用户 FBX、性能预算、Vulkan、人工 UI/可见 MCP 或1h验收。
