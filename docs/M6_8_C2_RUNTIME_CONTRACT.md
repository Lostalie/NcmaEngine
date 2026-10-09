# M6.8-C2 隔离 Montage 用例与联合验收契约

当前完整顺序无Skip Debug/Release自动候选通过，非人工/正式M6验收声明。
基线 main a80431e4ff8c5db78baf726a8d07426b0ec3718e 已核对，证据见交付记录。

复用 AnimationGraphSequence 和现有 exact-case human approval，不添加 live Play 权限。
严格五字段用例 fixedDelta/steps/writes/assertions/montageRequests 替代四字段格式，
无兼容/自动迁移。64条 typed Play/Cancel/Jump 仅有量子/Slot/Section/优先级，顺序保留；
全部复制与验证后才创建隔离实例。宿主生成 request/instance/session/world/tick，
首量子和 PlayOnStart 共用64队列预算。仍每Slot32边界、528区间、256量子，wire/本机提案48KiB。

SlotActive/Time/Weight/Section、MontageOutcome/ReceiptCount 为闭合数据断言，
Outcome 数字 0 Started/1 Cancelled/2 Jumped/3 PriorityRejected/4 NonInterruptibleRejected/5 NotPlaying。
Section 断言 subject=Section UUID,value=0，仅检查当前段，不隐含 Active。
Outcome 检查该量子同Slot存在该回执；ReceiptCount包含host startup。
复制输出最多4096 Slot行、80回执，requestIndex=-1为host startup，其余为作者数组索引。
不返回可操作的运行中句柄/请求token或逐量子完整528区间数组。

实际 NCA off-frame准备、同骨架/代次/长度、root/Notify 数值路径不改；
MCP propose 不执行、不IO、不改文件/history；run 需要单独精确用例审批，
hash/发布/资源/受众epoch/读取权限/TTL60/撤销/排队执行前检查覆盖缓存结果。
typed UI domain60编辑测试数据，domain55结果/完整分页审阅；domain59作者轨道不控制时钟。
没有推理接入、World/Movement/Jolt/Health/GPU/回调执行或新的原生ABI。

保留既有真实NCA/CPU-GPU/唯一Movement/Editor0-1-8-32/16sourcefreePlayer测试。
完整顺序无Skip Debug/Release、smokes/formats/inspect/profiles/audits/checkeddeploy通过再提交推送。
程序化素材与自动UI不替代用户FBX、人工可见MCP、目标环境、性能或1h验收。
