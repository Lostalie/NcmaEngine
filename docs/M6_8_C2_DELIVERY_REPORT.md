# M6.8-C2 交付记录

状态：C2完整顺序无Skip Debug/Release自动候选通过，最终Release部署核验完成。
M6.8 A/B/C自动候选闭环；不等于整个M6正式验收，M6.9/M6.10仍待执行。
提交推送/远端状态以Git回执为准。保留人工/用户素材/目标/性能/1h门禁。

首轮 Editor构建 `out/m6-8-c2-build-first.log` 失败：UI引用 internal Scalar。
改为 UI 本地有限数值验证，不扩大库API或放宽范围；失败记录保留。

core首轮 `out/m6-8-c2-core-build-first.log` 零警告构建，
`out/m6-8-c2-core-first.log` 115/115通过，含4项新增闭合控制/复制/预算测试。
Editor second构建失败（测试GuiFrame参数），third构建零警告通过。
`out/m6-8-c2-editor-first.log` 首个新增终止用例失败：binary64中
(.4-.1)+(.8-.4)>.7，改用.8确保跨过实际终点，保留inactive/两段Notify/后续无Notify所有断言；
不向正式时钟加epsilon或舍入。`editor-second.log` 前3项C2通过，实际GUI测试
RenderGpu失败：测试没有先提交目标。补既有纯2D透明quad清屏提交，保留原GUI/API断言并加PureUi/零3D资源检查。
全部首轮记录保留，完整门禁不因定向结果关闭。

`editor-third.log` 113/113通过，但截图发现独立序列面板被workspace遮挡；
采用既有 foreground panel Value=2（不改native ABI），增加两帧实际GUI和前景契约断言。
`editor-fourth.log` 新断言测试错误（Panel身份为high1/low412而非high412），修正后复测。
这是测试身份映射修复，没有移除前景/原typed/分页/回执/API断言。

最终定向 `out/m6-8-c2-editor-build-seventh.log` 零警告构建、
`out/m6-8-c2-editor-fifth.log` 113/113通过；4项新C2真实资源/MCP/UI用例全通过。
实际前景typed截图 `out/verification/m2/editor-services/01755b950c444ca9a680ee50373653f3/m6-8-c2-isolated-sequence.bmp`
已读取检查：标题、隔离边界、JSON与typed量子/类型/Slot/Section字段可见，后续内容滚动访问。
不是人机可见MCP/输入法/DPI或正式目标环境验收。保留所有早期失败与未前景截图。

契约见 M6_8_C2_RUNTIME_CONTRACT.md；最终完整证据见下。

## 最终完整回归

冻结代码后顺序执行，未使用Skip：

- `out/m6-8-c2-full-debug-first.log` exit0：12native(3.09s)、22managed(273.51s)。
- `out/m6-8-c2-full-release-first.log` exit0：12native(1.38s)、22managed(236.74s)。
- 两配置 core115/Editor113/Player56/Scene36/Gameplay53/fakeMovement38全部通过，
  Python43、managed/native smokes、新格式和移除格式拒绝、inspect、三轮retained profiles及审计通过。
- 保留原B2b-2实际NCA/1-8-32/root/terminal Notify/GPU oracle/Reload、
  unique Jolt/skin-shadow Editor0-1-8-32及16 source-free Headless/DX11 Player用例，完整回归再次通过。
- 新4core+4Editor用例覆盖typed请求/closed断言/完整拒绝/16Slot256量子4096行80回执，
  actual NCA root/Notify/terminal、实际stdio闭合分页/defaultdeny/精确用例hash/TTL/revoke/queued/re-pair，
  全页审阅不能跳过、stamped typed UI追加/替换/删除/分页/复制/陈旧事件、纯2D实际ImGui及无live/history/file变化。

Debug完整截图 `out/verification/m2/editor-services/c92e991b74f24ed288da9950ee057526/m6-8-c2-isolated-sequence.bmp`。
Release完整截图 `out/verification/m2/editor-services/d73f3d7e681142ada6c6c00dc4b6eb78/m6-8-c2-isolated-sequence.bmp`
已读取，前景标题/隔离边界/typed字段可见。纯Ui=1、无3D资源组、API0/0；不是性能或人工门禁。

## 部署、审计与恢复

Debug post-deploy audit `out/verification/m2-8/Debug/1876cc8b545a4e38bd19290e72ce4831/audit.json`；
Release post-deploy audit `out/verification/m2-8/Release/5ed390a8d9fd438893950c0f96e8e017/audit.json`。
两者 audit_passed=true、h8_accepted=false，所有列出的待验收门禁仍开放。
两次101文件的路径边界/无reparse祖先/普通文件/大小/SHA256逐项核验，journal Complete。
Debug备份 `out/deployment/33e5b5138d554b52a6423dba04539143/backup`；
最终Release备份 `out/deployment/b44fa7f3115d4aefb9d4393ab0bb92e7/backup`、journal generation同目录ID。
`out/bin/NcmaEngine.exe` 为本次framework-dependent Release入口，不是self-contained目标验收。

失败日志、早期未前景截图、生成部署/备份及SDK/user assets全部保留，不提交out或无关IDE设置。
提交推送并核对远端SHA后下一切片M6.9；然后M6.10，M6结束后M7(DX11-only)→M8→M9。
无推理服务、Python gameplay、新的transport/native ABI/live Agent权限或旧格式兼容。
