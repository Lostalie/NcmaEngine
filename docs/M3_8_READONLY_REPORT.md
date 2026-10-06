# M3.8 资产 MCP 只读候选

日期：2026-10-06。基线：`c6a7e3aa0f53242494cbabdaf0412f0954207aff`。
状态：只读切片已实现，最终顺序 Debug/Release 完整回归通过。不是完整 M3.8；G7/G8 保持开放。

后续更新：正式资产只读UI授权与已知直接运行长路径失败的修复见 [后续交付](M3_8_UI_AUTHORIZATION_REPORT.md)。本文件记录前一checkpoint范围，下文“尚未接UI/失败未定位”为当时状态，不覆盖后续证据。

## 本轮范围

配置 Assets 的正式 C# Editor 注册 `ncma.assets.list`、`ncma.assets.inspect`、`ncma.assets.validate`，通过已有 capability v2、owner IPC pump 和真实 MCP stdio server 动态发现/调用。不开新协议、不改原 scene revision 的语义、不引入 Python 执行/AI 推理。

- list 按 kind/offset/limit 返回 root 和 subasset 的脱敏 UUID/kind/name/state；只枚举批准范围，最多64身份/64项分页，不遍历整个 catalog。
- inspect 的 section 为 summary/subassets/dependencies，返回独立 assetRevision、assetId、generation 和有界 items；不输出 source path、SourceKey/私有 DCC 名称、importer options、原始 metadata、磁盘凭据或 native handle。name 为 kind + UUID 前缀，不冒充用户文件名。
- validate 仅诊断**已提交 metadata 和 typed dependency**，不是 NCA payload/hash/API/GPU 校验，也不触发 reimport。不可见依赖只返回当前获批资产上的 dependency_not_visible，不泄露依赖 UUID/路径。未知诊断被折为固定代码，不传原始 Path/Message。
- 输入/输出各有独立 closed JSON Schema、UUID/枚举/整数/分页范围。成功与错误 envelope 分支分开；保留 v2 structured result、changed=false/replayed=false。原 v2 超长重复字段的错误信息可能超过4KiB，错误 message schema 沿用64KiB输入预算，并有32000字符重复键反例；不修改原协议或扩大输入预算。宿主缓存适配器异常则只返回固定 asset_inspection_failed，不传异常里的私有路径。重复只读 requestId 每次重新检查可见范围，撤权后不能复用旧答案。
- Query 不读文件、不哈希、不解析模型或同步等待。扫描仍由已有 Assets.Authoring 服务进行；显式 owner Refresh 在 endpoint pump 外将 immutable catalog 投影成缓存，只有 SnapshotRevision==Clock.Revision 才发布。陈旧缓存返回 asset_snapshot_stale，同 revision 改写缓存拒绝。

## 授权限制

`AssetInspectionService.ApproveForPairedClients` 是可信宿主 API，**不是 Agent 工具，尚未接可见 UI**。默认可见 UUID 为空，list 返回空、inspect/validate 拒绝；pairing 和原 objectScope 不自动授权资产或文件。

宿主需批准精确1..64个 root/subasset UUID（批准 root 不自动批准子资产）。授权对该 endpoint 的所有已配对客户端共享，**不是逐客户端 ACL**；基于 monotonic clock 固定60秒有效期，绑定 document generation 和资产版本。撤权、资产缓存更新/文档切换或项目关闭失效。此授权只能读取脱敏 metadata，不能批准文件/导入/场景写入。真实 UI 审批、独立客户端范围和更广可见策略留给后续，不扩大现有 mutation 权限。

公开 assetRevision/generation 限定0..2^53-1的 JSON 安全整数；超范围缓存/代数拒绝，极端高代数适配尚未实现。

Core 新增可信 startup inspection batch 的全量预验证，未知/重复/非只读 descriptor 或超过16扩展预算先拒绝，避免半注册。没有一般可执行插件/MCP 动态加载入口；所有回调仍受 live World read-only guard。

## 自动测试

新增7项 Editor Services 用例：三份 descriptor SHA256 golden、封闭 schema 与真实成功/错误返回、默认空范围与 root/subasset 精确授权、私有内容脱敏、60s过期/撤权/文档与项目身份、陈旧缓存/版本单调、异常分页/UUID/duplicate JSON、内部引用隐藏、owner thread、注册失败零部分能力，以及真正 named-pipe IPC 和子进程 MCP stdio tools/list/tools/call/撤权后同 requestId 拒绝（无 Python）。

最终冻结源码顺序运行完整 `Build.bat -Configuration Debug/Release`，均退出0，无 Skip；每配置执行31次 CTest（native11 + managed/application20）、Python43/43、managed/native smoke、inspect、新格式/旧格式拒绝、三轮保留 profile 和部署前后审计。Editor Services57/57（新增7项）、Scene36/36、Scene Rendering70/70；新增代码无编译警告。

最终证据：`out/verification/m3-8/Debug-final2.log`、`Release-final2.log`、`Debug-ctest-final2.log`、`Release-ctest-final2.log`。Release checked 全包已部署至 `out/bin/NcmaEngine.exe`；可恢复备份为 `out/deployment/75f7c51fc60e41c1b3af4d88553556db/backup`。部署前后 audit_passed=true、h8_accepted=false，人工及长期门禁不改。

中间 `editor-initial.log` 是测试夹具误用小写内部 SHA256 的格式失败；`schema-object-update.log`、`schema-error-bound-update.log` 是新增 schema 修改期间的旧 golden 不匹配，最终 golden 对应已冻结的三份 descriptor。`Debug-before-final-hardening.log`、`Debug-final.log`、`Release-final.log` 是最终异常脱敏/错误 schema 修正前的通过结果，不作为最终源码证据。

`editor-second.log`、`editor-stdio.log` 的新 M3.8 用例通过，但直接 `dotnet run` 的整套用例再次复现既有 M3.6 PrepareCommit 文件操作失败。正式最终 Build.bat 两配置中的该真实导入/重启测试均通过；直接运行失败原因仍未定位，不宣称已修复，也不以正式回归通过关闭此问题或 G6/G8。校正了 M3.7 记录中的步骤名称。所有失败/中间日志保留，不降低断言或移除用例。

## 未实现

导入 plan/begin/status/cancel/commit 的逐任务权限与 receipt、destructive 资产删除、Prefab inspect/实例提交/override/sync、文件/场景联合历史授权，以及真实第三方可见客户端的 UI 审批→修改→Undo/Redo→撤权闭环未实现。原有本地 UI 导入 participant 与 `ncma.assets.import.commit` 内部能力存在，不把它冒称为本方案的安全 Agent 导入流程。

M3.7 保存/实例发布/覆盖/恢复仍需完成，不能先暴露绕过它们的 Prefab 修改工具。G8 尚未关闭；M2 人工/自包含/长稳/性能不因此关闭。
