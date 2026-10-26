# M8.3-A：模块编辑来源合同 v2

最终验收：本机Debug/Release完整CTest各131/131，真实C#/Python通信8/8组、搬移客户端和无Python默认产品均通过；详见[M8.3交付](M8_3_DELIVERY_REPORT.md)。下方进行中/未运行描述是较早历史，不代表最终状态。未部署正式EXE，未接实际推理。

最新状态：全阶段实现现已接通批准观察、可选模块桥、人工确认和原领域事务，见[接口边界](M8_3_DOMAIN_PROPOSALS.md)。本机完整Debug回归131/131通过，真实C#/Python协议8组及客户端搬移已实际执行通过；Release完整回归进行中。下方“仅编译/未实现”是前序A协议增量的历史证据，不代表当前功能状态；仍不代表推理服务接入。

2026-10-25 实现 / 仅编译与语法检查，未进行功能验收。此合同替代模块 v1 的编辑上下文，不兼容旧构造器、字段或服务路由。它只描述来源，**不是读取、外发、审批或执行权限**。

## 精确编辑来源

C# `ModuleContext` 与 Python `authoring_context` 使用以下闭合结构。线上字段为 lowerCamelCase，所有字段都必须出现，不允许未知、重复或缺失字段。

| 字段 | 类型与边界 | 含义 |
| --- | --- | --- |
| projectId | 非空 UUID | 项目身份 |
| hostSessionId | 非空 UUID | 本次宿主身份 |
| editSessionId | 非空 UUID | 原编辑会话 |
| documentSessionId | 非空 UUID | 编辑文档实例 |
| documentGeneration | uint64，至少1 | 编辑文档代次 |
| revision | uint64，允许0 | 编辑修订 |
| behaviourCatalogGeneration | UUID，允许空 UUID | 脚本目录代次；空表示无目录 |
| reviewEpoch | uint64，至少1 | 提案来源撤销代次 |
| selection | 非空 UUID 或显式 null | 当前选择；空 UUID 不等于无选择 |
| endpointId | UUID，允许空 UUID | 端点实例身份；空表示无端点 |
| audienceRevision | uint64，允许0 | 配对受众代次；无端点时必须0 |

UUID 在线上采用小写标准 D 格式。四个无符号计数上限均为18446744073709551615，不经过浮点数或有符号转换；拒绝布尔、浮点、负数及溢出。模块邮箱自身的 `epoch` 仍为正 int64，与编辑来源计数不是同一字段。

移除 `documentId`、`generation`、`worldId`、`observationTick`。运行时观察属于独立授权合同，不能向编辑提案填入伪造 World/tick。C# 邮箱和可选宿主共用 `ModuleContext.Validate()`，无第二套宿主校验规则。

## 不兼容协议切换

- `ModuleRequest.ContractVersion = 2`；指纹绑定完整来源、操作/配置/schema 和精确观察字节。
- 本机 gRPC 服务为 `ncma.modules.v2.Worker`，不注册 v1 别名。
- bootstrap、ready、Handshake、Invoke、reply 都严格要求版本2；v1或未来版本不回退。
- C# 可选客户端与 Python worker 必须作为同一代候选包准备、核验；旧包不自动转换，也不自动重启/部署。当前正式安装未改变。
- 原 TLS/叶证书固定、私有 stdin、固定操作、请求/取消/剩余预算、背压、单次领取、4096请求库存及131072B信封/65536B载荷限制不变。

独立格式不跟随本次升级：领域草稿 `version:1`、安装库存指纹 `version:1`、包清单 schemaVersion1、`text_stats` 操作版本1.0和既有 schema UUID 保持原意。确定性文本统计不是推理服务。

## 接入边界与下一步

来源字段与 `DomainProposalSource` 对齐，但本轮**未实现**批准观察导出、宿主到模块的映射/调用桥、单次回包到领域预检、人工确认或事务执行。默认 Editor/Player 不增加 Modules/gRPC/Python 依赖，不启动 worker，不读取 live World，不新增 MCP 能力。

下一步先以真实宿主来源和既有读取授权制作有界观察，再将同一来源绑定到模块会话和单次回包领取；撤销、配对代次、选择、文档或 Play 变化必须失效。不要把 `Validate()` 或预检成功当作读取/执行授权。

## 本轮证据

`out/verification/m8-3/a-protocol-v2-first` 保留16份修改前源码、完整编译日志/binlog及所选来源哈希回执（不是全仓库冻结）。

- 完整 `Ncma.Modules.Tests` 依赖图 Debug 编译6.51秒，0警告/0错误。
- 完整 `Ncma.Modules.Grpc.Tests` 依赖图 Debug 编译5.95秒，0警告/0错误。
- 两份修改过的 Python 源文件仅执行内建 `compile` 语法检查；未导入或执行 worker，未写 pycache。
- 新增1个核心测试组，扩展全部来源字段失效、v1/旧字段拒绝、缺失字段、计数边界、独立原始协议和真实编码器用例；均登记未运行。原4096生命周期负载与超时不变。
- 未运行测试/IPC/Play/性能/搬移验收，未部署/提交/推送。正式 EXE SHA256 仍为 `595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`。

整个 M8.3 完成后统一测试；本次编译不是跨语言运行证明，历史 M8.2 结果不作为 v2 验收。
