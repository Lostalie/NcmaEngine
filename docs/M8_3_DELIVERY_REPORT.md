# M8.3 交付记录

2026-10-25：A/B/C/D 已按闭合领域范围实现，整体实现后统一测试；本机 Debug/Release 自动验收通过。没有自动进入 M8.4，没有提交、推送或替换正式 EXE。

## 实现范围

| 子阶段 | 交付 |
| --- | --- |
| A 来源/观察 | 十一字段编辑来源v2，原只读授权下的有界复制观察，精确模块接收者、独立外发确认、单次领取，实际ModuleSession回包桥与生命周期失效 |
| B 场景闭环 | 原transaction/delete完整前后差异、规范化拒绝、人工确认、独立删除UUID/绑定确认、单次票据、原Commit/Undo/Redo及回执 |
| C 领域接入 | 原资产元数据、已准备动画/UI事务；原文件/资源审批，来源SHA与UI资源版本/哈希；原参与者memento+完整审阅指纹复核、原日志与补偿 |
| D 正式宿主 | AI菜单领域面板、完整分页审阅、两次确认、过期隐藏、取消/实际回收和恢复提示；默认不注册模块、不启动网络或解释器 |

详细入口、预算和支持的五种能力见 [领域接口与边界](M8_3_DOMAIN_PROPOSALS.md)。不支持任意工具/文件写入；UI与动画原草稿审批仍保留，没有另建远程文件网关。文件发布不是全局原子事务，失败不自动Undo或重试。实际模型推理和模型安装/连接UI属于M8.4；模块协议v2不代表接入模型。

## 统一验收

| 门禁 | Debug | Release |
| --- | --- | --- |
| 全64托管项目（当前csproj清单） | 0警告/0错误，167.10秒 | 0警告/0错误，173.32秒 |
| 原生全目标构建 | exit0，当前无增量工作 | exit0，当前无增量工作 |
| `ctest -N`及JSON清单 | 131项 | 131项 |
| 完整串行CTest，无过滤 | 131/131，1259.56秒 | 131/131，1131.13秒 |
| 完整Editor.Services内部用例 | 264/264 | 264/264 |
| M8.3专门入口（亦纳入完整服务） | 21/21 | 21/21 |
| 模块核心 | 18组/8408项检查 | 18组/8408项检查 |
| 真实C#/Python TLS通信 | 8/8组 | 8/8组 |
| 搬移可选客户端 | 1174项检查 | 1174项检查 |
| 无Python默认产品 | 614项检查 | 614项检查 |
| CodeGen单元、实际构建、发布合同 | 26/26；17条命令及2种发布通过 | 26/26；17条命令及2种发布通过 |
| Python工具集与inspect（匹配原生输入） | 106/106及inspect exit0 | 106/106及inspect exit0 |

JUnit名称逐项匹配131项库存，0失败、0跳过、0禁用；32个Gameplay入口保持原30秒预算，原像素/严格0B/4096负载没有减量。没有调用整条Build.ps1或Build.bat充当无部署验证。

Python工具首轮106项也通过，但当时固定`out/managed`库为Release而参考程序选择Debug，**不作为匹配Debug验收**。首轮日志保留；随后分别归档切换为精确Debug/Release原生输入并复跑，最终以`Python-*-matched.log`及回执为准。证据解析器单元测试不代表恢复E5-D目标性能测量。

## 证据与产物

主目录：`out/verification/m8-3/whole-first`。

- 18份修改前源码留档；`stage-end-first.json`与对应目录归档4,841份所选实现/测试/构建/SDK源码，完整测试后逐SHA复核未变。不是全仓库冻结，文档/IDE状态不在该库存。
- `Build-*-first.log/binlog`、`Native-*`、`Inventory-*.log/json`、`Test-*.log/xml`、`LastTest-*`、`Domain-*-first.log`及`execution-result.json`保存完整执行结果。
- `staging-before-*`保存被替换的测试输入，包索引归档；`Python-*-matched.*`保存匹配配置补充测试。没有删除历史失败或用后续通过覆盖原日志。
- 当前产品候选：Debug `4c5c4cd5240e4461ad67c771e3f8f322`；Release `d746c2b00ab8497cbc8e8522c58d01b7`。均为`out/package/m2-7/<配置>/<代次>`下独立framework-dependent测试候选，不是正式安装。
- 最终源码/正式EXE复核通过，补充Python测试后只读进程审计未发现匹配验证辅助进程。审计脚本首轮把通过用例名中的`failed_player`误判为失败；原脚本/说明保留，改为精确匹配unittest汇总行后通过。未修改产品源码、测试断言或测试结果。
- 可选客户端候选：Debug `ee790ae4e75b4ca99b90a52b0d59f05c`；Release `a0dcde160d094ee399df08b8c3f49238`。默认Editor/Player双配置deps均没有Modules/Editor.Modules/gRPC/Python依赖，亦通过实际无Python产品测试。
- 正式`out/bin/NcmaEngine.exe`始终SHA256 `595CFEB416B1E3ACAB3394DA07FA0A165732C119CFBB7FEB8E90BA3C48BAB19B`。Git HEAD仍`04026db52aebf3af082e1adf2f8feee126e71c24`；4个既有IDE文件改动未处理。

## 保留边界

本次是当前Windows本机自动验收，不等于人工界面体验、跨平台或目标机性能验收。E5-D代表性动作内容/目标硬件、SDK8及VS全框架宿主矩阵、历史间歇性原子保存故障根因仍开放；本轮未复现不表示根因已修复。正式EXE部署和Git交付需后续明确请求。下一阶段为M8.4真实推理连接，需要用户明确选择服务及外发/凭据/费用边界。
