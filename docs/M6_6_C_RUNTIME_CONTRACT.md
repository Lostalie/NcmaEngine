# M6.6-C BlendSpace 作者和 AI 检查契约

2026-10-08。基线 B 远端 main `f52746f8f63818849bb11cb190b9fbd8be1eb808` 已核对。
本切片不增加图/原生 ABI 版本，也不增加推理服务或 live Agent 控制。

## 作者界面与共享语义

节点画布新增 BlendSpace 创建和 speed Float 引脚。创建必须由用户明确填 Clip UUID；
初始1D/0–1 normalized轴、1秒cycle、独立group、两个相同Clip的端点只是明确草稿初值，不推断资源或速度。
保存/独立预览仍要求完整真实NCA依赖审阅；缺资源/退化/错误轴/同组clock设置拒绝。
轴名称/单位/范围/FloatUUID、维度、周期、group、loop/speed及采样clip/坐标提供类型化属性。
切换1D明确清除Y，可取消草稿或经已保存历史撤销；2D须明确补齐不同Y轴和非退化点。
每页4个采样属性，最多32；预编译三角形/点、选中/主源颜色及投影查询位置在图诊断区域显示。
查询仅计算几何权重，无Clip/GPU/World执行或live preview参数写入。
只有已批准文件草稿可拖动点；坐标/事件含view/content stamp，取消不落盘；完整审阅后单次保存走唯一history。

共享 closed operations 增加 `blendspace.sample.upsert {nodeId,sample}` 与
`blendspace.sample.delete {nodeId,sampleId}`；整个轴/同步定义仍通过完整 typed `node.upsert`。
点UUID不可冒充节点/clip/参数；exact bound node必须已经是BlendSpace。
允许不完整草稿用于诊断，但完整proposal/保存/预览拒绝缺点/非法轴/剖分退化或未知资源。
现有事务实际NCA代次/骨架/clip长度/唯一history、冲突、Undo/Redo、Play freeze约束不变。
Agent proposal仍只复制内存，不读取/准备资源、不写文件；端点请求授权与图文件/NCA审阅是两道独立人工批准。

## 受审阅坐标序列的权重扫描

复用 `ncma.animgraph.sequence.propose`/`sequence.run`，新增闭合分页 section=`weights`，没有新审批或live工具。
case的typed Float writes即精确逐步坐标输入；最多256steps/512writes/64assert/48KiB，4cases/256 lifetimeIDs。
主机人工在tick外准备实际NCA closure/program；Agent不能用路径/伪描述符触发准备。
run逐步使用独立实例的committed参数和同一预编译空间权重（不再次剖分）；每空间每量子一条观测，
最多16×256=4096条。每页1–8，结果含step/node/space UUID、最多3正权重、主采样/Clip、投影坐标/标记。
扫描包含inactive空间，因此它是参数空间检查，不是该节点正在live pose评估的证据。
现有timeline/事件/root/checks仍是独立数值序列；没有World/GPU/solver、seek或同步AI推理。

run每次（含repeat/cache/排队）重新核对exact case/hash、graph/event hash、resource publication/identity、
Edit/graph stamp、endpoint、配对受众epoch、读取permission和60秒审批。撤销、TTL、重新配对或源变化不能复活缓存。
UI与MCP用同一个服务和全页case/resource/audience审阅；UI结果可分页显示同一空间权重。
`resourcesPrepared=true`只证明服务已保留实际NCA，并非GPU/Jolt/用户素材/性能/人工MCP验收。

## 证据边界

定向及完整 Animation73/73；Editor98/98覆盖 shared ops、缺资源实际审阅、Undo/Redo、点拖动/取消、
realstdio点提案/双审批事务、几何权重页默认拒绝/撤销/TTL、queued revoke/re-pair以及实际ImGui/GPU预览。
16×256最大扫描通过；运行时暖采样零分配由A/B及保留测试验证，不代表工具扫描零分配。
纯空间图也可创建状态与Clip依赖事件，不依赖不存在的Clip节点。
完整顺序Debug/Release通过，101部署hash/journal已核对，见交付报告；D联合门禁未完成。
人工可见UI/MCP、用户FBX、目标环境、完整性能及1h门禁继续开放。
