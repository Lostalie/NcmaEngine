# M6.6 BlendSpace 实施方案

2026-10-08，按用户“执行M6.6”顺序实施；每切片完整Build.bat Debug/Release、修复至通过、提交推送并核对远端后前进。
基线远端为M6.5-D的0dcbbed5f879064268c5ab6b3fa392b963e4df9f；保留IDE/用户素材/SDK/所有失败与部署备份。
本方案不是完成声明；人工/真实素材/目标环境/性能/1h门禁不变，没有推理服务或Agent live控制。

## A：准备期拓扑与纯数值权重（完整双配置自动候选通过）

C#拥有复制BlendSpace定义（UUID、1D/2D、轴参数/名称/单位/显式范围、采样UUID/ClipUUID/位置、
显式cycleSeconds/syncGroup）。2–32个1D点、3–32个2D点；有限范围、同位置/重复UUID/退化拒绝。
轴范围标准化到[0,1]作为明确距离度量；运行请求有限，先clamp轴范围。
1D预排序与相邻线段插值；2D准备期确定性Delaunay（固定UUID插入顺序/共圆确定性规则），
仅保留有向非退化三角形/凸包边。内部重心，域外投影最近凸包边；边/相等距离稳定UUID优先。
返回最多3个正权重，和为1；主贡献源为最大权重/UUID平局。不截断、猜clip或实时剖分。
公开准备后的不可变定义/三角形副本和无分配权重值，不拥有clock/World/native/IO。
A仅数值基础，不变更当前图v2，不宣称资源已准备、图节点/pose/root/事件/UI/MCP已接通。
测试1D点/中间/夹取，2D顶点/边/内部/凸包外、共圆/点重排/内部点覆盖、随机网格、数值预算、
整批输入拒绝、复制与暖采样分配/测量；完整双配置后提交。

## B：严格图、唯一时钟和真实资源运行时（完整双配置自动候选通过）

图新增明确BlendSpace pose节点与中性空字段；严格新图v3替换v2，无旧类型/迁移/兼容fallback，
拒绝但保留旧输入。定义嵌入节点，persistent sample/space UUID，closed schema与NCP1同一graph路由。
验证typed Float轴、最多16space nodes/128实际Clip依赖、骨架/同代实际NCA/单位/clip duration；
全图共享65536 TRS scratch和32actors/32768bones等现有预算保持。原生ABI不变。
显式cycleSeconds同步归一相位；同一评估state/context内同syncGroup共享提交相位，
同组cycle/loop/speed必须相同，无跨对象共享可变时钟。inactive state不推进，重入清相位，Abort不消费。
最多3片段生成已有Clip/Blend配方，权重有序归一；最多32周期/量子，递归plan有独立上限。
目标主贡献样本负责事件；weight变化不导致重复补发旧事件，非主源不发步音/命中。
root采用该BlendSpace主贡献源（不是叠加多个源），进入既有唯一Movement/Jolt；混合pose仍正确去除desired根。
中断继续上次committed alpha1全pose缓存；不引入第二个World/solver/native clock。
测试不同duration同相位、group、循环/边界/reentry/Abort/weight-source变更、32实例零分配、
真实NCA/native pose/GPU/共享skin-shadow/root-strip与已有事件/中断安全边界；双配置提交。

## C：作者工作区与同期AI工具（完整双配置自动候选通过）

UI显示轴/点/预编译三角形/当前权重/主源，定位同一UUID；采样点拖动、轴/范围/单位/cycle/group/clip
修改走shared closed semantic edits、draft、精确file/NCA审阅与唯一Undo/Redo。
构造不完整草稿可诊断，保存/预览不猜缺失资源。schema/inspect依赖/summary同步更新，不暴露新路径权限。
AI与UI使用同一prepare/采样扫描：有界闭合坐标请求和主源/权重结果，必须审阅精确图/资源/用例/受众，
复用M6.5独立sequence审批或同等独立精确审批；默认拒绝，无审批工具/live参数写入/推理等待。
主机真实NCA准备在tick外，扫描不执行World/GPU/Jolt；结构几何扫描不能声称资源已准备。
测试shared edits、GUI stamped gestures、完整review分页、真实stdio/闭合schema/撤销/TTL/重新配对/
排队失效/冲突/Undo/资源变更/只读Play，完整双配置提交。

## D：联合回归与交付（完整双配置自动候选通过）

0/1/8/32Editor/Player/Headless，搬移NCP1图与实际NCA闭包，不同render frame rate/fixed step；
独立CPU权重/TRS/GPU/root oracle、循环及source事件唯一、反复中断、fault/reload/close-retention/
Stop/new-Play、resource baselines、成本证据与完整Debug/Release/smokes/formats/inspect/audits/checkeddeploy。
核对Release安装hash/journal/可恢复备份，提交推送并核对SHA。
仅A–D相应证据完成后标M6.6自动候选完成；不宣称真实用户素材/可见第三方MCP/目标机器/性能/1h验收，
不提前声称M6.7分层缓存或M6.8Montage实现。

2026-10-08：A/B/C/D分别按授权完整双配置通过；C远端29ef3ac已核对，D见M6_6_D_DELIVERY_REPORT.md。
仅自动候选完成，人工/用户素材/目标环境/性能/1h门禁保持开放。
