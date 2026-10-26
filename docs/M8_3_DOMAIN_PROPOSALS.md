# M8.3 领域提案接口与授权边界

本页描述实现合同；验收状态和未完成事项以 [M8.3 实施记录](M8_3_IMPLEMENTATION_PLAN.md#当前状态) 为准。

## 默认产品

Editor 打开项目后持有被动 `EditorDomainProposals`。默认没有注册模块，不加载 Python、gRPC 或推理服务；`Ncma.Editor.Modules` 是可选 C# 宿主桥程序集，不是默认 Editor/Player 依赖。模型服务接入属于 M8.4。

正式菜单：AI → 领域提案。用户可输入小型本地 JSON 草稿做预检，或审阅可信宿主模块生成的提案。面板输入为1023字节；服务/API使用完整64KiB预算，不截断超限提案。

## 数据流与两次确认

1. 可信宿主注册精确 `DomainModuleTarget` 和控制器；模块身份、版本、配置、操作、输入输出schema不能由提案决定。
2. `ReviewObservation` 调用原只读能力，最多8个请求、合计64KiB。场景对象必须明确UUID；资产/动画仍需原独立读审批。禁止运行World、私有脚本反射及任意文件读取。
3. 本地人审阅完整观察字节、来源和接收模块，通过 `ApproveObservation` 创建不可自行构造的单次请求。
4. `DomainModuleBridge.Begin` 领取一次观察，映射十一字段v2编辑上下文，提交到原 `ModuleSession`。领取、回包前重验原读授权与观察内容。模块返回仅为不可信提案，没有写权限。
5. 领域服务经原校验器生成完整前后差异。人逐页审阅并独立确认写入；删除需输入准确UUID，C#可执行绑定需额外确认且原目录信任不能绕过。
6. 原事务执行并返回回执，原 Undo/Redo 管理历史。提交票据在执行前消费，失败不自动重试，不自动Undo其他用户工作。

观察有效期60秒；写入审阅有效期60秒。项目/宿主/EditSession/文档/代次/修订/脚本目录代次/选择/撤销代次/端点及配对代次均绑定。Play、重载、关闭和授权撤销使相关结果失效；相同UUID重新配对不能恢复旧授权。字段身份本身不是授权。

## 允许的提案

信封严格使用三个成员：

```json
{"version":1,"capability":"ncma.scene.transaction","input":{"operations":[{"op":"rename","objectId":"明确对象UUID","name":"名称"}]}}
```

上例UUID占位符需替换为合法UUID。模块运输协议为v2，**提案信封独立为v1**；不是允许旧模块协议。

| 能力 | 原审批及提交约束 |
| --- | --- |
| `ncma.scene.transaction` | 原128操作/64KiB；精确影响；完整前后值；规范化改变值则拒绝旧提案；绑定另行确认 |
| `ncma.scene.delete_object` | 单独确认准确删除UUID；原删除能力、目标权限与历史检查保留 |
| `ncma.assets.metadata.edit` | 原文件/资产UUID授权、来源SHA、资产修订、元数据校验、持久化日志 |
| `ncma.ui.document.commit` | 原已准备proposalId和资产修订；精确文件/文档、注册C#语义Action、资源版本及已用资源SHA |
| `ncma.animgraph.transaction` | 原已准备proposalId和资产修订；原图审批、骨架/片段身份和资源授权 |

动画候选仍从原 propose 流程进入并由原资源审阅获批；UI完整候选由可信本地 `EditorUiWorkspace.PrepareDomainProposal` 检查、复制，再生成原提交句柄。没有开放模型直接提交任意UI文件或选择资产路径的通用入口。导入、删除文件、未知工具、恢复整个文档等明确拒绝。

UI/动画的原proposal句柄仍由原工作区/可信调用者持有；丢弃领域审阅不擅自删除原草稿，调用者需按原接口明确取消不再使用的句柄。原有限proposal库存不扩大，不能借领域审阅绕过。

## 文件原事务与失败

`IEditCommandReview` 只对已审核的原参与者启用，Prepare不发布文件；本地审阅包含完整文件前后JSON/SHA。确认重新Prepare后比较memento和完整审阅哈希，并在原发布边界重新校验。资源或文件变化不能替换用户已确认的内容。

文件发布与场景历史不是全局原子分布式事务。仍使用原参与者 Validate/Publish/Compensate/Complete；补偿或提交后收尾失败时会冻结/使历史失效，回执 `RecoveryRequired` 明确要求人工恢复，不得按“无写入”处理。

## 可选模块生命周期

可信插件以真实 `ModuleOperation` 和 `IModuleAdapter` 构造 `DomainModuleBridge`，在项目Owner关闭前受同一所有者线程管理。UI审阅批准观察后才能Begin；宿主每100ms最多自动泵一次已注册控制器，或者用户手动刷新。

取消只请求停止并丢弃邮箱，不谎称后台工作已结束。`TryClose` 等实际工作结束才归还资源；Owner关闭遇到尚在回收的模块会明确拒绝完成关闭，可在工作结束后重试原关闭，不重新开放审批。没有自动杀进程、自动重试请求或隐式费用授权。

模块注册不是不可信代码安全沙箱。插件安装和模型凭据/费用/网络策略不由本阶段解决；这些能力必须由可信宿主明确提供。
