# M3.8 资产只读 UI 授权与长路径修复

日期：2026-10-06。基线：`8dc0ff2204bd2cdb0b3a402afba88e507ed31585`。
状态：候选实现；直接自动测试和最终顺序完整 Debug/Release 回归均通过。不是完整 M3.8，G7/G8 保持开放。

## 本轮实现

正式 C# Editor 的 Assets 面板增加根资产/子资产的独立精确 UUID 勾选（最多64），完整审核页、人工确认、60秒 metadata-only 授权、倒计时及撤权。根资产不隐含子资产；翻页保留同一文档/资产版本的选择，文档或资产版本变化清空选择及待审批页。不新增 Agent 自批能力。

`EditorAssetAuthorizationController` 只供可信本地 UI：审核 fingerprint 包含 project UUID/generation、scene stamp、asset revision、endpoint UUID、准确资产 UUID/kind/state 和当前已配对且连接的客户端集合。审批时重新生成完整审核数据，拒绝篡改、未确认、陈旧文档/资产/客户端、未开启端点或没有已配对客户端的请求。审批页显示所有范围，不用隐藏字段代替人类审核。

授权仍为该端点中**审核时所列客户端共享**的资产读范围，不是逐客户端 ACL。端点替换、配对/撤权操作立即清除授权；查询/UI状态还核对当前客户端集合，断连/新增客户端等已观察到的变化永久撤权，不能由随后恢复相同集合自动复活。严格60秒过期、文档 generation、单调资产缓存及脱敏规则沿用前一切片。直接 trusted API 不提供 audience 回调时仍是宿主明确承担边界的低层选项，不作为 Agent 可调用审批端点。

授予的只有 `assets.list/inspect/validate` 已提交 metadata 可见性，不包括磁盘读取、原始文件路径、导入/候选发布、scene写入或历史授权；场景/文件权限体系未扩大。授权状态返回 owned UUID副本。查询只读宿主缓存，不扫描/哈希/解析，不在 MCP pump 中等待IO。GUI中变更审批选择不会修改 World 或加入 Undo；导入/Prefab写入必须另有未来的联合事务授权。

## 已知直接运行失败的修复

本轮先运行未改代码的 `dotnet run`，再次复现既有 M3.6 PrepareCommit 的 `Asset file operation failed`，见 `out/verification/m3-8/direct-before-ui.log`。原最终NCA路径243字符，添加 `.<32位UUID>.prepared` 后285字符，超过260；普通 `dotnet run` apphost 与 CTest 的 `dotnet DLL` 对原始 CreateFileW 长路径行为不同。

私有 `WindowsAssetFile` 的 CreateFileW/read lease/handle rename 统一使用规范绝对路径的扩展长度表示（本地 `\\?\`、UNC `\\?\UNC\`）；不缩短UUID/hash身份、不关闭父目录/reparse/hardlink/文件类型/授权验证，不增加公开设备路径能力。`.prepared` 创建→写→rename→读取/校验→重新打开仍通过已有数值资源租约/文件事务。

新增独立数值导入回归明确要求临时路径超过260字符，检验真正写入与发布、bundle校验、冷重开及无剩余prepared文件。直接 apphost 的编辑器全套（包括此前失败的M3.6真实导入/历史/取消/重启）和导入全套均通过；旧失败日志保留。此修复不关闭M3.6的缩略图/材质/人工等其他缺口。

## 测试证据

- `out/verification/m3-8/ui-direct-final.log`：Editor Services **62/62**，新增5项：审核fingerprint/精准范围及篡改拒绝；GUI禁用/伪造/未审阅/陈旧事件；客户端/资产/文档变化撤权；真实MCP stdio由实际人工UI事件审批/撤权且导入权限仍拒绝；原生GUI/DX11绘制审核页并锁住metadata证明无逐帧磁盘读取。
- `out/verification/m3-8/long-file-direct-final.log`：Import **25/25**，新增1项长prepared路径真正发布/重开。直接运行使用Debug apphost，不以只执行相同dotnet DLL取代失败模式。
- 最终冻结源码顺序执行完整 `Build.bat -Configuration Debug` / `Release`，均退出0、无Skip；每配置31次CTest（native11 + managed/application20）、Python43/43、managed/native smoke、inspect、严格新格式/已移除格式拒绝、3轮保留profile及部署前后审计。两配置均Editor Services62/62、Import25/25；新增代码无编译警告。
- 最终日志：`out/verification/m3-8/Debug-ui-final.log`、`Release-ui-final.log`、`Debug-ui-ctest-final.log`、`Release-ui-ctest-final.log`。Release checked整包已部署 `out/bin/NcmaEngine.exe`，可恢复备份为 `out/deployment/ea78b858ad5742429e07520891f8ff3d/backup`。部署前后 `audit_passed=true`、`h8_accepted=false`；人工/目标环境/完整性能/长稳门禁不改。
- 自动UI事件/stdio/隐藏窗口绘制不是人工操作或真实第三方可见客户端验收。数值回归覆盖本地Windows长路径，不宣称UNC目标环境已验收。

中间 `ui-direct-first.log` 发现新增widget域19与既有Browser相机冲突；已换独立域21并由实际GUI/Presenter测试覆盖。`ui-direct-second.log` 为加入最后原生GUI用例前61/61通过。`long-file-direct.log` 是首次调用少传configuration参数的入口拒绝，不作为最终测试证据。日志全部保留。

## 剩余范围

导入plan/begin/status/cancel/commit的逐任务范围、异步生命周期及去重receipt未实现；资产删除/Prefab inspect/实例化/覆盖/同步MCP未实现。Prefab发布与文件/场景联合历史依赖M3.7未完成部分，不暴露绕过它们的写工具。逐客户端资产ACL未实现。

真实第三方可见客户端的检查→人工UI审批→修改→UI Undo/MCP Redo→撤权闭环未验收；本轮仅补**读取**的正式UI授权，不冒称修改闭环或关闭G8。M2人工/自包含/性能/长稳门禁不改。
