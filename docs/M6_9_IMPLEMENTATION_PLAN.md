# M6.9 AI 工作流可靠性实施方案

基线：M6.8-C2 `fcc4e8abbf505b9ead914b2631e9e20b72af0397` 已远端核对。
只编排现有已实现工具；不接推理、凭据、外部传输、任意代码或 live Play。

## A：账本、原工具请求与安全边界

C# EditorWorkflows 保存闭合 v1 计划、精确请求、结果摘要。每计划 1–12 步，
48 KiB，单步输入16 KiB；编辑会话终身最多16计划、最多2获批活动计划，
deadline 1–120秒，修复预算0–2。没有自动循环、动态工具名、表达式或嵌套执行。
新增唯一只读 `ncma.workflow` 数据工具；注册容量仍16，不扩大旧工具/权限。

先独立审阅实际已注册能力元数据和配对受众，再提案完整计划并逐页审阅。
next 发放宿主创建 UUID 的原 CapabilityRequest。客户端随后调用原工具；
它仍需原 endpoint 事务审批、图/资产/UI文件及资源审批、独立用例审批。
工作流审批不是上述权限，也不自动执行。

端点 owner-thread 只读观察器在执行前限制 ticket，执行后接收实际结果。
禁止观察器 live World 写入、嵌套 Invoke 或注册；观察器异常 fail-stop，
保留已提交操作的原回执，绝不伪造回滚。Runtime/Player不初始化工作流。

## B：取消、过期、失败和有限修复

session/document generation/catalog、registry schema hashes、endpoint、
配对受众epoch/UUID、revision、exact input/request UUID 均重检。
仅自身成功步骤的实际结果允许推进 expected revision，不自动 rebase 人工修改。
工作流/能力元数据审批TTL60；取消、撤销、过期和版本变化阻止排队中的未执行ticket。
所有发出的UUID保留为终身有界 tombstone，不淘汰后重新变成普通调用。

明确原权限拒绝为等待，不消耗修复预算；语义错误/固定 valid 或 passed 断言失败
停止后续。修复仅替换当前失败步骤输入，工具/步骤身份/成功回执不变，
完整计划重新审阅批准，原工具审批仍独立。没有自动Undo或跨文件/GPU/IO原子回滚承诺。

## C：同一 UI/MCP 路径与测试交付

ImGui AI菜单加入前台工作流审阅面板；全部输入页访问后才可勾选批准。
显示配对受众、能力风险及schema hash、精确输入、deadline、预算和清洗后的回执。
仍显示“未接入推理服务”。不添加推理执行按钮。

验证真实stdio场景事务及实际NCA图提案→独立资源审批→图事务→验证→
独立序列提案→精确用例审批→断言→回执；取消已排队获批mutation、TTL/deadline、
撤销/re-pair/端点变化、场景变化、错输入、预算、复制隔离、完整UI页和实际ImGui/API验证。
冻结源码后顺序无Skip Build.bat Debug/Release；checked部署核对hash/journal，再提交推送核对远端。
人工可见第三方MCP、用户素材、目标自包含、性能和1h门禁仍独立；后续M6.10。
